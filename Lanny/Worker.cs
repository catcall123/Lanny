using Lanny.Data;
using Lanny.Discovery;
using Lanny.Hubs;
using Lanny.Models;
using Lanny.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Lanny;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly DeviceRepository _repo;
    private readonly IEnumerable<IDiscoveryService> _scanners;
    private readonly ScanLoopMonitor _scanLoopMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScanSettings _settings;

    public Worker(
        ILogger<Worker> logger,
        DeviceRepository repo,
        IEnumerable<IDiscoveryService> scanners,
        ScanLoopMonitor scanLoopMonitor,
        IServiceScopeFactory scopeFactory,
        IOptions<ScanSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _scanners = scanners ?? throw new ArgumentNullException(nameof(scanners));
        _scanLoopMonitor = scanLoopMonitor ?? throw new ArgumentNullException(nameof(scanLoopMonitor));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Outer self-heal loop: anything escaping the inner loop (e.g. a DB
        // load failure, or a bug in a hot path) is logged and retried, so the
        // BackgroundService can't die silently on a single transient fault.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunMainLoopAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var backoff = TimeSpan.FromSeconds(30);
                _logger.LogCritical(ex, "Scan worker died unexpectedly; restarting in {Backoff}", backoff);
                try
                {
                    await Task.Delay(backoff, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }
    }

    private async Task RunMainLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lanny starting — loading cached devices");
        await _repo.LoadFromDatabaseAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleNumber = _scanLoopMonitor.BeginCycle(DateTimeOffset.UtcNow);
            using var logScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CycleNumber"] = cycleNumber,
            });

            LogStalledCycleWarningIfNeeded();

            try
            {
                await ExecuteScanCycleAsync(cycleNumber, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Stopping scan loop during cycle {CycleNumber}", cycleNumber);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scan cycle {CycleNumber} failed unexpectedly", cycleNumber);
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.ScanIntervalSeconds), stoppingToken);
        }
    }

    private async Task ExecuteScanCycleAsync(long cycleNumber, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Starting scan cycle {CycleNumber}", cycleNumber);

        var allDiscovered = new List<Device>();
        var targetedScanners = new List<ITargetedDiscoveryService>();

        foreach (var scanner in _scanners)
        {
            if (scanner is ITargetedDiscoveryService targetedScanner)
            {
                targetedScanners.Add(targetedScanner);
                continue;
            }

            try
            {
                var found = await scanner.ScanAsync(stoppingToken);
                allDiscovered.AddRange(found);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scanner {ScannerName} failed during cycle {CycleNumber}", scanner.Name, cycleNumber);
            }
        }

        var withMac = allDiscovered.Where(device => !string.IsNullOrEmpty(device.MacAddress)).ToList();
        var withoutMac = allDiscovered.Where(device => string.IsNullOrEmpty(device.MacAddress)).ToList();

        foreach (var targetedScanner in targetedScanners)
        {
            try
            {
                var found = await targetedScanner.ScanAsync(withMac, stoppingToken);
                withoutMac.AddRange(found.Where(device => string.IsNullOrEmpty(device.MacAddress)));
                withMac.AddRange(found.Where(device => !string.IsNullOrEmpty(device.MacAddress)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var scannerName = targetedScanner is IDiscoveryService discoveryService
                    ? discoveryService.Name
                    : targetedScanner.GetType().Name;
                _logger.LogWarning(ex, "Scanner {ScannerName} failed during cycle {CycleNumber}", scannerName, cycleNumber);
            }
        }

        foreach (var device in withMac)
        {
            stoppingToken.ThrowIfCancellationRequested();
            DeviceMetadataEnricher.MergeRelatedObservations(device, withoutMac);
            await _repo.UpsertAsync(device, stoppingToken);
        }

        var offlineCutoff = DateTimeOffset.UtcNow.AddMinutes(-_settings.OfflineThresholdMinutes);
        foreach (var device in _repo.GetAll())
        {
            if (device.IsOnline && device.LastSeen < offlineCutoff)
                _repo.MarkOffline(device.MacAddress);
        }

        var pruneCutoff = DateTimeOffset.UtcNow.AddHours(-_settings.OfflineDeviceRetentionHours);
        var prunedDeviceCount = await _repo.PruneOfflineDevicesAsync(pruneCutoff, stoppingToken);

        await _repo.PersistAllAsync(stoppingToken);

        using (var scope = _scopeFactory.CreateScope())
        {
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<DeviceHub>>();
            await hub.Clients.All.SendAsync("DevicesUpdated", _repo.GetAll(), stoppingToken);
        }

        _scanLoopMonitor.CompleteCycle(cycleNumber, DateTimeOffset.UtcNow);
        _logger.LogDebug(
            "Scan cycle {CycleNumber} complete with {TrackedDeviceCount} tracked devices and {PrunedDeviceCount} pruned devices",
            cycleNumber,
            _repo.GetAll().Count,
            prunedDeviceCount);
    }

    private void LogStalledCycleWarningIfNeeded()
    {
        var threshold = TimeSpan.FromMinutes(_settings.StalledScanWarningMinutes);
        if (_scanLoopMonitor.TryMarkStalledWarning(DateTimeOffset.UtcNow, threshold, out var snapshot))
        {
            var referenceTime = snapshot.LastCycleCompletedAtUtc ?? snapshot.StartedAtUtc;
            _logger.LogWarning(
                "Scan loop has not completed a cycle since {ReferenceTimeUtc}",
                referenceTime);
        }
    }
}
