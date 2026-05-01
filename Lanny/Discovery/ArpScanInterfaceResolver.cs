using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Lanny.Models;

namespace Lanny.Discovery;

public sealed partial class ArpScanInterfaceResolver
{
    private const string AutoInterfaceValue = "auto";
    private const string FallbackInterface = "eth0";

    private readonly ScanSettings _settings;
    private readonly ILogger<ArpScanInterfaceResolver> _logger;

    public ArpScanInterfaceResolver(
        IOptions<ScanSettings> settings,
        ILogger<ArpScanInterfaceResolver> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ResolveAsync(CancellationToken cancellationToken)
    {
        var configuredInterface = GetConfiguredInterface();
        if (configuredInterface is not null)
            return configuredInterface;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "ip",
                Arguments = "route show default",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return Resolve(output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to resolve ARP scan interface; falling back to {Interface}", FallbackInterface);
            return FallbackInterface;
        }
    }

    public string Resolve(string defaultRouteOutput)
    {
        var configuredInterface = GetConfiguredInterface();
        if (configuredInterface is not null)
            return configuredInterface;

        ArgumentNullException.ThrowIfNull(defaultRouteOutput);

        foreach (var line in defaultRouteOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = DefaultRouteDeviceRegex().Match(line);
            if (match.Success)
                return match.Groups["interface"].Value;
        }

        return FallbackInterface;
    }

    private string? GetConfiguredInterface()
    {
        if (string.IsNullOrWhiteSpace(_settings.ArpScanInterface))
            return null;

        var configuredInterface = _settings.ArpScanInterface.Trim();
        return configuredInterface.Equals(AutoInterfaceValue, StringComparison.OrdinalIgnoreCase)
            ? null
            : configuredInterface;
    }

    [GeneratedRegex(@"^default\b.*\bdev\s+(?<interface>\S+)")]
    private static partial Regex DefaultRouteDeviceRegex();
}
