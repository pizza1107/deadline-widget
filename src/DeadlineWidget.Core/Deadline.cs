namespace DeadlineWidget.Core;

public sealed record Deadline(
    Guid Id,
    string Title,
    DateTime DueAt,
    bool IsCompleted,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    DateTime? CompletedAt);
