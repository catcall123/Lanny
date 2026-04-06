using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public class ServiceFingerprintScanner : IDiscoveryService, ITargetedDiscoveryService
{
    private readonly ILogger<ServiceFingerprintScanner> _logger;
    private readonly IReadOnlyList<IServiceFingerprintProbe> _probes;
    private readonly ScanSettings _settings;

    public ServiceFingerprintScanner(
        IEnumerable<IServiceFingerprintProbe> probes,
        ILogger<ServiceFingerprintScanner> logger,
        IOptions<ScanSettings> settings)
    {
        _probes = probes?.ToList() ?? throw new ArgumentNullException(nameof(probes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public string Name => "ServiceFingerprint";

    public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<Device>>([]);
    }

    public async Task<IReadOnlyList<Device>> ScanAsync(IReadOnlyList<Device> devices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (!_settings.EnableServiceFingerprinting || _probes.Count == 0)
            return [];

        var maxConcurrency = _settings.FingerprintMaxConcurrency > 0
            ? _settings.FingerprintMaxConcurrency
            : 4;
        using var gate = new SemaphoreSlim(maxConcurrency);

        var tasks = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.IpAddress))
            .Select(async device =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    return await ProbeDeviceAsync(device, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            });

        var observations = await Task.WhenAll(tasks);
        var results = observations.Where(device => device is not null).Cast<Device>().ToList();
        _logger.LogDebug("Service fingerprinting enriched {Count} devices", results.Count);
        return results;
    }

    private async Task<Device?> ProbeDeviceAsync(Device device, CancellationToken cancellationToken)
    {
        var aggregate = new Device
        {
            IpAddress = device.IpAddress,
        };

        foreach (var probe in _probes)
        {
            try
            {
                using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeTimeout.CancelAfter(TimeSpan.FromMilliseconds(_settings.FingerprintTimeoutMs > 0 ? _settings.FingerprintTimeoutMs : 1000));

                var observation = await probe.ProbeAsync(device, probeTimeout.Token);
                if (observation is not null)
                    DeviceMetadataEnricher.MergeObservation(aggregate, observation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Service fingerprint probe {ProbeName} failed for {IpAddress}", probe.GetType().Name, device.IpAddress);
            }
        }

        return HasFingerprintMetadata(aggregate)
            ? aggregate
            : null;
    }

    private static bool HasFingerprintMetadata(Device device)
    {
        return !string.IsNullOrWhiteSpace(device.HttpTitle)
            || (device.HttpHeaders?.Count ?? 0) > 0
            || !string.IsNullOrWhiteSpace(device.TlsCertificateSubject)
            || (device.TlsSubjectAlternativeNames?.Count ?? 0) > 0
            || !string.IsNullOrWhiteSpace(device.SshBanner)
            || !string.IsNullOrWhiteSpace(device.DiscoveryMethod);
    }
}