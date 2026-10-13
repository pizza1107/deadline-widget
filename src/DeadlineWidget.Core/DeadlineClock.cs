namespace DeadlineWidget.Core;

public sealed class DeadlineClock
{
    private static readonly TimeSpan WarningBoundary = TimeSpan.FromDays(10);
    private static readonly TimeSpan CriticalBoundary = TimeSpan.FromDays(3);

    public DeadlineState Describe(Deadline deadline, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(deadline);

        var remaining = deadline.DueAt - now;
        if (remaining < TimeSpan.Zero)
        {
            return new DeadlineState("已过期", DeadlineUrgency.Overdue, true);
        }

        var urgency = remaining <= CriticalBoundary
            ? DeadlineUrgency.Critical
            : remaining <= WarningBoundary
                ? DeadlineUrgency.Warning
                : DeadlineUrgency.Neutral;

        return new DeadlineState(FormatCountdown(remaining), urgency, false);
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining == TimeSpan.Zero)
        {
            return "现在到期";
        }

        if (remaining >= TimeSpan.FromDays(1))
        {
            return $"剩余 {(int)Math.Ceiling(remaining.TotalDays)} 天";
        }

        var hours = (int)remaining.TotalHours;
        return hours > 0
            ? $"剩余 {hours} 小时 {remaining.Minutes} 分钟"
            : $"剩余 {remaining.Minutes} 分钟";
    }
}
