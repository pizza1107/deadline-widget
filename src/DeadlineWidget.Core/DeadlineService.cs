namespace DeadlineWidget.Core;

public sealed class DeadlineService
{
    public Deadline Create(string title, DateTime dueAt, DateTime now) =>
        new(
            Guid.NewGuid(),
            NormalizeTitle(title),
            NormalizeDueAt(dueAt),
            false,
            now,
            now,
            null);

    public Deadline Update(Deadline deadline, string title, DateTime dueAt, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        return deadline with
        {
            Title = NormalizeTitle(title),
            DueAt = NormalizeDueAt(dueAt),
            ModifiedAt = now
        };
    }

    public Deadline Complete(Deadline deadline, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        return deadline with { IsCompleted = true, CompletedAt = now, ModifiedAt = now };
    }

    public Deadline Restore(Deadline deadline, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        return deadline with { IsCompleted = false, CompletedAt = null, ModifiedAt = now };
    }

    public IReadOnlyList<Deadline> Delete(IEnumerable<Deadline> deadlines, Guid id)
    {
        ArgumentNullException.ThrowIfNull(deadlines);
        return deadlines.Where(deadline => deadline.Id != id).ToArray();
    }

    public IReadOnlyList<Deadline> GetActiveOrdered(IEnumerable<Deadline> deadlines, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(deadlines);
        return deadlines
            .Where(deadline => !deadline.IsCompleted)
            .OrderBy(deadline => deadline.DueAt < now)
            .ThenBy(deadline => deadline.DueAt)
            .ThenBy(deadline => deadline.CreatedAt)
            .ToArray();
    }

    public IReadOnlyList<Deadline> GetCompletedOrdered(IEnumerable<Deadline> deadlines)
    {
        ArgumentNullException.ThrowIfNull(deadlines);
        return deadlines
            .Where(deadline => deadline.IsCompleted)
            .OrderByDescending(deadline => deadline.CompletedAt)
            .ThenByDescending(deadline => deadline.ModifiedAt)
            .ToArray();
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        return title.Trim();
    }

    private static DateTime NormalizeDueAt(DateTime dueAt) =>
        new(dueAt.Year, dueAt.Month, dueAt.Day, dueAt.Hour, 0, 0, dueAt.Kind);
}
