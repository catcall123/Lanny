namespace Lanny.Runtime;

public sealed class ScanLoopMonitor
{
    private readonly Lock _sync = new();

    public ScanLoopMonitor()
    {
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset StartedAtUtc { get; }

    public long BeginCycle(DateTimeOffset startedAtUtc)
    {
        lock (_sync)
        {
            CurrentCycleNumber++;
            LastCycleStartedAtUtc = startedAtUtc;
            return CurrentCycleNumber;
        }
    }

    public void CompleteCycle(long cycleNumber, DateTimeOffset completedAtUtc)
    {
        lock (_sync)
        {
            CurrentCycleNumber = Math.Max(CurrentCycleNumber, cycleNumber);
            LastCompletedCycleNumber = cycleNumber;
            LastCycleCompletedAtUtc = completedAtUtc;
        }
    }

    public ScanLoopSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new ScanLoopSnapshot(
                StartedAtUtc,
                CurrentCycleNumber,
                LastCompletedCycleNumber,
                LastCycleStartedAtUtc,
                LastCycleCompletedAtUtc,
                LastWarningAtUtc);
        }
    }

    public bool TryMarkStalledWarning(DateTimeOffset now, TimeSpan threshold, out ScanLoopSnapshot snapshot)
    {
        lock (_sync)
        {
            var referenceTime = LastCycleCompletedAtUtc ?? StartedAtUtc;
            if (now - referenceTime < threshold)
            {
                snapshot = new ScanLoopSnapshot(
                    StartedAtUtc,
                    CurrentCycleNumber,
                    LastCompletedCycleNumber,
                    LastCycleStartedAtUtc,
                    LastCycleCompletedAtUtc,
                    LastWarningAtUtc);
                return false;
            }

            if (LastWarningAtUtc is not null && now - LastWarningAtUtc.Value < threshold)
            {
                snapshot = new ScanLoopSnapshot(
                    StartedAtUtc,
                    CurrentCycleNumber,
                    LastCompletedCycleNumber,
                    LastCycleStartedAtUtc,
                    LastCycleCompletedAtUtc,
                    LastWarningAtUtc);
                return false;
            }

            LastWarningAtUtc = now;
            snapshot = new ScanLoopSnapshot(
                StartedAtUtc,
                CurrentCycleNumber,
                LastCompletedCycleNumber,
                LastCycleStartedAtUtc,
                LastCycleCompletedAtUtc,
                LastWarningAtUtc);
            return true;
        }
    }

    private long CurrentCycleNumber { get; set; }

    private long LastCompletedCycleNumber { get; set; }

    private DateTimeOffset? LastCycleStartedAtUtc { get; set; }

    private DateTimeOffset? LastCycleCompletedAtUtc { get; set; }

    private DateTimeOffset? LastWarningAtUtc { get; set; }
}

public sealed record ScanLoopSnapshot(
    DateTimeOffset StartedAtUtc,
    long CurrentCycleNumber,
    long LastCompletedCycleNumber,
    DateTimeOffset? LastCycleStartedAtUtc,
    DateTimeOffset? LastCycleCompletedAtUtc,
    DateTimeOffset? LastWarningAtUtc);