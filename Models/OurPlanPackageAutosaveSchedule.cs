namespace OurPlanCore;

internal static class OurPlanPackageAutosaveSchedule
{
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan MinimumCheckpointInterval = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan MaximumDirtyAge = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(2);

    internal static DateTime CalculateDueUtc(
        DateTime nowUtc,
        DateTime dirtySinceUtc,
        DateTime lastStartedUtc,
        bool waitForQuietPeriod,
        TimeSpan retryDelay)
    {
        DateTime maximumDirtyDueUtc = dirtySinceUtc + MaximumDirtyAge;
        DateTime dueUtc = waitForQuietPeriod
            ? Earlier(nowUtc + QuietPeriod, maximumDirtyDueUtc)
            : nowUtc;
        if (lastStartedUtc != DateTime.MinValue && retryDelay <= TimeSpan.Zero)
        {
            dueUtc = Later(dueUtc, lastStartedUtc + MinimumCheckpointInterval);
            if (waitForQuietPeriod)
                dueUtc = Earlier(dueUtc, maximumDirtyDueUtc);
        }
        if (retryDelay > TimeSpan.Zero)
            dueUtc = Later(dueUtc, nowUtc + retryDelay);
        return Later(nowUtc, dueUtc);
    }

    internal static TimeSpan FailureRetryDelay(int consecutiveFailureCount) =>
        consecutiveFailureCount switch
        {
            <= 1 => TimeSpan.FromSeconds(5),
            2 => TimeSpan.FromSeconds(15),
            3 => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromMinutes(1),
        };

    private static DateTime Earlier(DateTime left, DateTime right) =>
        left <= right ? left : right;

    private static DateTime Later(DateTime left, DateTime right) =>
        left >= right ? left : right;
}
