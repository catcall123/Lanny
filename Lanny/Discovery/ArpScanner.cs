using System.Diagnostics;
using System.Text.RegularExpressions;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

/// <summary>Runs arp-scan (Linux) or reads the ARP table to discover L2 neighbors.</summary>
public partial class ArpScanner : IDiscoveryService
{
    private readonly ILogger<ArpScanner> _logger;
    private readonly ScanSettings _settings;

    public string Name => "ARP";

    public ArpScanner(ILogger<ArpScanner> logger, IOptions<ScanSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        var devices = new List<Device>();

        if (OperatingSystem.IsLinux())
        {
            devices.AddRange(await RunArpScanAsync(ct));
        }
        else
        {
            devices.AddRange(await ReadArpTableAsync(ct));
        }

        return devices;
    }

    private async Task<List<Device>> RunArpScanAsync(CancellationToken ct)
    {
        var devices = new List<Device>();
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "arp-scan",
                Arguments = $"--localnet --interface=eth0 --retry=1 --timeout=500",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            // arp-scan output lines: "192.168.1.1\t00:11:22:33:44:55\tVendor Name"
            foreach (var line in output.Split('\n'))
            {
                var match = ArpScanLineRegex().Match(line);
                if (!match.Success) continue;

                var ipAddress = match.Groups["ip"].Value;
                var mac = match.Groups["mac"].Value.ToUpperInvariant();
                if (!ArpEntryFilter.IsRelevantNeighbor(ipAddress, mac))
                    continue;

                devices.Add(new Device
                {
                    MacAddress = mac,
                    IpAddress = ipAddress,
                    Vendor = OuiLookup.Resolve(mac) ?? match.Groups["vendor"].Value,
                    DiscoveryMethod = Name,
                    LastSeen = DateTimeOffset.UtcNow,
                });
            }
            _logger.LogInformation("ARP scan found {Count} devices", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "arp-scan failed — is it installed?");
        }
        return devices;
    }

    private async Task<List<Device>> ReadArpTableAsync(CancellationToken ct)
    {
        var devices = new List<Device>();
        try
        {
            using var process = new Process();
            process.StartInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo { FileName = "arp", Arguments = "-a", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo { FileName = "cat", Arguments = "/proc/net/arp", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            foreach (var line in output.Split('\n'))
            {
                var match = ArpTableRegex().Match(line);
                if (!match.Success) continue;

                var ipAddress = match.Groups["ip"].Value;
                var mac = match.Groups["mac"].Value.ToUpperInvariant();
                if (!ArpEntryFilter.IsRelevantNeighbor(ipAddress, mac))
                    continue;

                devices.Add(new Device
                {
                    MacAddress = mac,
                    IpAddress = ipAddress,
                    Vendor = OuiLookup.Resolve(mac),
                    DiscoveryMethod = Name,
                    LastSeen = DateTimeOffset.UtcNow,
                });
            }
            _logger.LogInformation("ARP table read found {Count} devices", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read ARP table");
        }
        return devices;
    }

    [GeneratedRegex(@"(?<ip>\d+\.\d+\.\d+\.\d+)\s+(?<mac>[\da-fA-F:]{17})\s+(?<vendor>.*)")]
    private static partial Regex ArpScanLineRegex();

    [GeneratedRegex(@"(?<ip>\d+\.\d+\.\d+\.\d+)\s+.*?(?<mac>[\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2}[:-][\da-fA-F]{2})")]
    private static partial Regex ArpTableRegex();
}
