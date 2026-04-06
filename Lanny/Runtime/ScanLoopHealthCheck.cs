using Lanny.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Lanny.Runtime;

public sealed class ScanLoopHealthCheck : IHealthCheck
{
    private readonly ScanLoopMonitor _scanLoopMonitor;
    private readonly ScanSettings _settings;

    public ScanLoopHealthCheck(ScanLoopMonitor scanLoopMonitor, IOptions<ScanSettings> settings)
    {
        _scanLoopMonitor = scanLoopMonitor ?? throw new ArgumentNullException(nameof(scanLoopMonitor));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = _scanLoopMonitor.GetSnapshot();
        var now = DateTimeOffset.UtcNow;
        var threshold = TimeSpan.FromMinutes(_settings.StalledScanWarningMinutes);
        var referenceTime = snapshot.LastCycleCompletedAtUtc ?? snapshot.StartedAtUtc;

        if (now - referenceTime > threshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Scan loop has not completed a cycle since {referenceTime:O}.",
                data: CreateData(snapshot)));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Scan loop is healthy.",
            CreateData(snapshot)));
    }

    private static IReadOnlyDictionary<string, object> CreateData(ScanLoopSnapshot snapshot)
    {
        var data = new Dictionary<string, object>
        {
            ["startedAtUtc"] = snapshot.StartedAtUtc,
            ["currentCycleNumber"] = snapshot.CurrentCycleNumber,
            ["lastCompletedCycleNumber"] = snapshot.LastCompletedCycleNumber,
        };

        if (snapshot.LastCycleStartedAtUtc is not null)
            data["lastCycleStartedAtUtc"] = snapshot.LastCycleStartedAtUtc.Value;

        if (snapshot.LastCycleCompletedAtUtc is not null)
            data["lastCycleCompletedAtUtc"] = snapshot.LastCycleCompletedAtUtc.Value;

        return data;
    }
}