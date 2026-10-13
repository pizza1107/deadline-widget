using DeadlineWidget.Core;

namespace DeadlineWidget.Manager;

public interface IDeadlineStore
{
    Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default);
}

public sealed class DeadlineRepositoryStore(DeadlineRepository repository) : IDeadlineStore
{
    public Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default) =>
        repository.LoadAsync(cancellationToken);

    public Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default) =>
        repository.SaveAsync(deadlines, cancellationToken);
}
