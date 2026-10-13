using DeadlineWidget.Core;
using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class ManagerCloseCoordinatorTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Local);

    [TestMethod]
    public async Task RequestClose_WaitsForSavePublishRefreshBeforeDisposingInstanceCoordinator()
    {
        var item = Item();
        var events = new List<string>();
        var store = new BlockingStore([item], events);
        var viewModel = new ManagerViewModel(
            store,
            new RecordingPublisher(events),
            new RecordingRefresher(events),
            new DeadlineService(),
            () => Now);
        await viewModel.InitializeAsync();
        events.Clear();
        var mutation = viewModel.CompleteCommand.ExecuteAsync(item);
        await store.SaveStarted.Task;
        var coordinator = new ManagerCloseCoordinator(viewModel, () =>
        {
            events.Add("dispose");
            return Task.CompletedTask;
        });

        var close = coordinator.RequestCloseAsync();
        await Task.Delay(50);
        Assert.IsFalse(close.IsCompleted);
        CollectionAssert.AreEqual(new[] { "save" }, events);

        store.ReleaseSave.TrySetResult();
        await mutation;
        Assert.IsTrue(await close);

        CollectionAssert.AreEqual(new[] { "save", "publish", "refresh", "dispose" }, events);
    }

    [TestMethod]
    public async Task RequestClose_WaitsForSnapshotFailureToBecomeVisibleBeforeDisposing()
    {
        var item = Item();
        var events = new List<string>();
        var publisher = new BlockingFailingPublisher(events);
        var viewModel = new ManagerViewModel(
            new RecordingStore([item], events),
            publisher,
            new RecordingRefresher(events),
            new DeadlineService(),
            () => Now);
        await viewModel.InitializeAsync();
        events.Clear();
        publisher.ShouldBlockAndFail = true;
        var mutation = viewModel.CompleteCommand.ExecuteAsync(item);
        await publisher.PublishStarted.Task;
        string? errorAtDispose = null;
        var coordinator = new ManagerCloseCoordinator(viewModel, () =>
        {
            errorAtDispose = viewModel.ErrorMessage;
            events.Add("dispose");
            return Task.CompletedTask;
        });

        var close = coordinator.RequestCloseAsync();
        await Task.Delay(50);
        Assert.IsFalse(close.IsCompleted);
        Assert.IsNull(errorAtDispose);

        publisher.ReleasePublish.TrySetResult();
        await mutation;
        var shouldClose = await close;

        Assert.IsFalse(shouldClose);
        StringAssert.Contains(viewModel.ErrorMessage, "快照");
        Assert.IsNull(errorAtDispose);
        CollectionAssert.AreEqual(new[] { "save", "publish" }, events);

        Assert.IsTrue(await coordinator.RequestCloseAsync());
        StringAssert.Contains(errorAtDispose, "快照");
        CollectionAssert.AreEqual(new[] { "save", "publish", "dispose" }, events);
    }

    private static Deadline Item() =>
        new(Guid.NewGuid(), "课程", Now.AddDays(2), false, Now.AddDays(-1), Now.AddDays(-1), null);

    private sealed class BlockingStore(IReadOnlyList<Deadline> initial, List<string> events) : IDeadlineStore
    {
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(initial);

        public async Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default)
        {
            events.Add("save");
            SaveStarted.TrySetResult();
            await ReleaseSave.Task;
        }
    }

    private sealed class RecordingStore(IReadOnlyList<Deadline> initial, List<string> events) : IDeadlineStore
    {
        public Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(initial);

        public Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default)
        {
            events.Add("save");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher(List<string> events) : ISnapshotPublisher
    {
        public Task PublishAsync(IReadOnlyCollection<Deadline> deadlines, DateTime generatedAt, CancellationToken cancellationToken = default)
        {
            events.Add("publish");
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingFailingPublisher(List<string> events) : ISnapshotPublisher
    {
        public TaskCompletionSource PublishStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePublish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ShouldBlockAndFail { get; set; }

        public async Task PublishAsync(IReadOnlyCollection<Deadline> deadlines, DateTime generatedAt, CancellationToken cancellationToken = default)
        {
            events.Add("publish");
            if (!ShouldBlockAndFail)
            {
                return;
            }

            PublishStarted.TrySetResult();
            await ReleasePublish.Task;
            throw new IOException("replace failed");
        }
    }

    private sealed class RecordingRefresher(List<string> events) : IRainmeterRefresher
    {
        public Task<RainmeterRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            events.Add("refresh");
            return Task.FromResult(new RainmeterRefreshResult(true, null));
        }
    }
}
