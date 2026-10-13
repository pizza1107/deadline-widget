namespace DeadlineWidget.Core;

public enum DeadlineUrgency
{
    Neutral,
    Warning,
    Critical,
    Overdue
}

public sealed record DeadlineState(
    string Countdown,
    DeadlineUrgency Urgency,
    bool IsOverdue);
