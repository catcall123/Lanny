using Lanny.Data;
using Lanny.Discovery;
using Lanny.Hubs;
using Lanny.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Lanny;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly DeviceRepository _repo;
    private readonly IEnumerable<IDiscoveryService> _scanners;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScanSettings _settings;

    public Worker(
        ILogger<Worker> logger,
        DeviceRepository repo,
        IEnumerable<IDiscoveryService> scanners,
        IServiceScopeFactory scopeFactory,
        IOptions<ScanSettings> settings)
    {
        _logger = logger;
        _repo = repo;
        _scanners = scanners;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lanny starting — loading cached devices");
        await _repo.LoadFromDatabaseAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Starting scan cycle at {Time}", DateTimeOffset.Now);

            var allDiscovered = new List<Device>();

            foreach (var scanner in _scanners)
            {
                try
                {
                    var found = await scanner.ScanAsync(stoppingToken);
                    allDiscovered.AddRange(found);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Scanner {Name} failed", scanner.Name);
                }
            }

            // Correlate devices: merge ping results (no MAC) with ARP results (have MAC)
            var withMac = allDiscovered.Where(d => !string.IsNullOrEmpty(d.MacAddress)).ToList();
            var withoutMac = allDiscovered.Where(d => string.IsNullOrEmpty(d.MacAddress)).ToList();

            foreach (var device in withMac)
            {
                DeviceMetadataEnricher.MergeRelatedObservations(device, withoutMac);
                await _repo.UpsertAsync(device);
            }

            // Mark devices not seen in this cycle as offline
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_settings.OfflineThresholdMinutes);
            foreach (var device in _repo.GetAll())
            {
                if (device.IsOnline && device.LastSeen < cutoff)
                    _repo.MarkOffline(device.MacAddress);
            }

            // Persist and notify dashboard
            await _repo.PersistAllAsync();

            using (var scope = _scopeFactory.CreateScope())
            {
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<DeviceHub>>();
                await hub.Clients.All.SendAsync("DevicesUpdated", _repo.GetAll(), stoppingToken);
            }

            _logger.LogInformation("Scan cycle complete — {Count} devices tracked", _repo.GetAll().Count);

            await Task.Delay(TimeSpan.FromSeconds(_settings.ScanIntervalSeconds), stoppingToken);
        }
    }
}
