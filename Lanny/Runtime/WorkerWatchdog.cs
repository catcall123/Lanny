using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Runtime;

/// <summary>
/// Independently monitors <see cref="ScanLoopMonitor"/> and logs a critical
/// warning if no cycle has completed in a long time. Runs in its own
/// BackgroundService so a deadlocked scan worker can't silence the alarm.
/// </summary>
public sealed class WorkerWatchdog : BackgroundService
{
    private readonly ScanLoopMonitor _monitor;
    private readonly ILogger<WorkerWatchdog> _logger;
    private readonly ScanSettings _settings;
    private DateTimeOffset _lastWarningAtUtc = DateTimeOffset.MinValue;

    public WorkerWatchdog(
        ScanLoopMonitor monitor,
        ILogger<WorkerWatchdog> logger,
        IOptions<ScanSettings> settings)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkInterval = TimeSpan.FromSeconds(Math.Max(_settings.ScanIntervalSeconds, 30));
        var staleThreshold = TimeSpan.FromSeconds(_settings.ScanIntervalSeconds * 3);

        _logger.LogInformation(
            "Worker watchdog active: warning if no cycle completes within {Threshold}",
            staleThreshold);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            CheckHealth(staleThreshold);
        }
    }

    private void CheckHealth(TimeSpan staleThreshold)
    {
        var snapshot = _monitor.GetSnapshot();
        var reference = snapshot.LastCycleCompletedAtUtc ?? snapshot.StartedAtUtc;
        var staleness = DateTimeOffset.UtcNow - reference;

        if (staleness < staleThreshold)
            return;

        // Throttle: at most one critical log per staleness window so we don't
        // spam the journal while the worker stays dead.
        if (DateTimeOffset.UtcNow - _lastWarningAtUtc < staleThreshold)
            return;

        _lastWarningAtUtc = DateTimeOffset.UtcNow;
        _logger.LogCritical(
            "Scan worker has not completed a cycle in {Minutes:F1} minutes — device data is stale. Last completed cycle: {LastCycle}",
            staleness.TotalMinutes,
            snapshot.LastCycleCompletedAtUtc);
    }
}
