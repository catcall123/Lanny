using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public class SnmpScanner : IDiscoveryService, ITargetedDiscoveryService
{
    private readonly ILogger<SnmpScanner> _logger;
    private readonly ISnmpMetadataProvider _snmpMetadataProvider;
    private readonly ScanSettings _settings;

    public SnmpScanner(
        ISnmpMetadataProvider snmpMetadataProvider,
        ILogger<SnmpScanner> logger,
        IOptions<ScanSettings> settings)
    {
        _snmpMetadataProvider = snmpMetadataProvider ?? throw new ArgumentNullException(nameof(snmpMetadataProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public string Name => "SNMP";

    public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<Device>>([]);
    }

    public async Task<IReadOnlyList<Device>> ScanAsync(IReadOnlyList<Device> devices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (!_settings.EnableSnmpInspection)
            return [];

        var tasks = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.IpAddress))
            .Select(device => _snmpMetadataProvider.TryGetObservationAsync(device, cancellationToken));

        var observations = await Task.WhenAll(tasks);
        var discovered = observations
            .Where(observation => observation is not null)
            .Cast<Device>()
            .ToList();

        _logger.LogDebug("SNMP inspection enriched {Count} devices", discovered.Count);
        return discovered;
    }
}