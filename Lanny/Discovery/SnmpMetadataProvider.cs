using System.Net;
using System.Threading;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public class SnmpMetadataProvider : ISnmpMetadataProvider
{
    private readonly ISnmpClient _client;
    private readonly ILogger<SnmpMetadataProvider> _logger;
    private readonly SnmpSettings _settings;
    private int _missingCommunityWarningWritten;

    public SnmpMetadataProvider(
        ISnmpClient client,
        IOptions<SnmpSettings> settings,
        ILogger<SnmpMetadataProvider> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Device?> TryGetObservationAsync(Device device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_settings.Enabled || string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        if (string.IsNullOrWhiteSpace(_settings.Community))
        {
            if (Interlocked.Exchange(ref _missingCommunityWarningWritten, 1) == 0)
            {
                _logger.LogWarning("SNMP enrichment is enabled but no community string is configured");
            }

            return null;
        }

        if (!IPAddress.TryParse(device.IpAddress, out var ipAddress))
            return null;

        try
        {
            var systemData = await _client.GetSystemDataAsync(ipAddress, _settings, cancellationToken);
            if (systemData is null || IsEmpty(systemData))
                return null;

            return new Device
            {
                IpAddress = device.IpAddress,
                Hostname = systemData.SystemName,
                SystemName = systemData.SystemName,
                SystemDescription = systemData.SystemDescription,
                SystemObjectId = systemData.SystemObjectId,
                SystemUptime = systemData.SystemUptime,
                InterfaceCount = systemData.InterfaceCount,
                DiscoveryMethod = "SNMP",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsEmpty(SnmpSystemData systemData)
    {
        return string.IsNullOrWhiteSpace(systemData.SystemName)
            && string.IsNullOrWhiteSpace(systemData.SystemDescription)
            && string.IsNullOrWhiteSpace(systemData.SystemObjectId)
            && systemData.SystemUptime is null
            && systemData.InterfaceCount is null;
    }
}
