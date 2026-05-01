using System.ComponentModel;
using System.Diagnostics;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public sealed class PassiveArpListener : BackgroundService, IDiscoveryService
{
    private readonly PassiveObservationCache _observations = new();
    private readonly ILogger<PassiveArpListener> _logger;
    private readonly ScanSettings _settings;

    public PassiveArpListener(ILogger<PassiveArpListener> logger, IOptions<ScanSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public string Name => "PassiveARP";

    public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        var devices = _observations.GetSnapshot(GetObservationRetention(), DateTimeOffset.UtcNow);
        return Task.FromResult<IReadOnlyList<Device>>(devices);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnablePassiveArpListener)
        {
            _logger.LogInformation("Passive ARP listener disabled by configuration");
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            _logger.LogInformation("Passive ARP listener requires Linux and is disabled on this host");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTcpdumpAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning("Passive ARP listener could not start tcpdump: {Message}", ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Passive ARP listener stopped unexpectedly: {Message}", ex.Message);
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task RunTcpdumpAsync(CancellationToken stoppingToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "tcpdump",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.StartInfo.ArgumentList.Add("-l");
        process.StartInfo.ArgumentList.Add("-n");
        process.StartInfo.ArgumentList.Add("-e");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(GetCaptureInterface());
        process.StartInfo.ArgumentList.Add("arp");

        process.Start();
        _ = DrainStandardErrorAsync(process, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(stoppingToken);
            if (line is null)
                break;

            if (PassiveArpObservationParser.TryParseTcpdumpLine(line, DateTimeOffset.UtcNow, out var device) && device is not null)
                _observations.Upsert(device.MacAddress, device);
        }

        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        await process.WaitForExitAsync(stoppingToken);
    }

    private string GetCaptureInterface()
    {
        return string.IsNullOrWhiteSpace(_settings.PassiveCaptureInterface)
            ? "any"
            : _settings.PassiveCaptureInterface.Trim();
    }

    private TimeSpan GetObservationRetention()
    {
        var retentionMinutes = Math.Max(1, _settings.PassiveObservationRetentionMinutes);
        return TimeSpan.FromMinutes(retentionMinutes);
    }

    private static async Task DrainStandardErrorAsync(Process process, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested && await process.StandardError.ReadLineAsync(stoppingToken) is not null)
            {
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
