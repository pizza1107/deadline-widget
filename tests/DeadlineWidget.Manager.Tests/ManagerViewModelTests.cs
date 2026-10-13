using DeadlineWidget.Core;
using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class ManagerViewModelTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Local);

    [TestMethod]
    public async Task Initialize_ExposesOrderedActiveAndCompletedCollections()
    {
        var active = Item("活动", Now.AddDays(2));
        var completed = Item("完成", Now.AddDays(1)) with { IsCompleted = true, CompletedAt = Now.AddHours(-1) };
        var fixture = Fixture([completed, active]);

        await fixture.ViewModel.InitializeAsync();

        CollectionAssert.AreEqual(new[] { active }, fixture.ViewModel.ActiveDeadlines.ToArray());
        CollectionAssert.AreEqual(new[] { completed }, fixture.ViewModel.CompletedDeadlines.ToArray());
    }

    [TestMethod]
    public async Task Initialize_PublishesLoadedSnapshotAndRefreshesRainmeter()
    {
        var active = Item("活动", Now.AddDays(2));
        var fixture = Fixture([active]);

        await fixture.ViewModel.InitializeAsync();

        CollectionAssert.AreEqual(new[] { "publish", "refresh" }, fixture.Events);
        CollectionAssert.AreEqual(new[] { active }, fixture.Publisher.PublishedSnapshots.Single().ToArray());
    }

    [TestMethod]
    public async Task Initialize_SnapshotFailureKeepsLoadedDeadlinesVisibleAndReportsWarning()
    {
        var active = Item("活动", Now.AddDays(2));
        var fixture = Fixture([active]);
        fixture.Publisher.Exception = new IOException("replace failed");

        await fixture.ViewModel.InitializeAsync();

        CollectionAssert.AreEqual(new[] { active }, fixture.ViewModel.ActiveDeadlines.ToArray());
        CollectionAssert.AreEqual(new[] { "publish" }, fixture.Events);
        Assert.IsNull(fixture.ViewModel.ErrorMessage);
        StringAssert.Contains(fixture.ViewModel.WarningMessage, "快照");
    }

    [TestMethod]
    public async Task Initialize_RainmeterRefreshWarningKeepsLoadedDeadlinesVisible()
    {
        var active = Item("活动", Now.AddDays(2));
        var fixture = Fixture([active]);
        fixture.Refresher.Result = new RainmeterRefreshResult(false, "未找到 Rainmeter");

        await fixture.ViewModel.InitializeAsync();

        CollectionAssert.AreEqual(new[] { active }, fixture.ViewModel.ActiveDeadlines.ToArray());
        CollectionAssert.AreEqual(new[] { "publish", "refresh" }, fixture.Events);
        Assert.AreEqual("未找到 Rainmeter", fixture.ViewModel.WarningMessage);
        Assert.IsNull(fixture.ViewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task AddAndCancelCommands_ReuseEditorWithoutMutatingData()
    {
        var fixture = Fixture([]);
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.AddCommand.Execute(null);
        Assert.IsTrue(fixture.ViewModel.IsEditorOpen);
        fixture.ViewModel.Editor.Title = "课程";
        fixture.ViewModel.CancelCommand.Execute(null);

        Assert.IsFalse(fixture.ViewModel.IsEditorOpen);
        Assert.AreEqual(0, fixture.Store.Saves.Count);
        Assert.AreEqual(0, fixture.ViewModel.ActiveDeadlines.Count);
    }

    [TestMethod]
    public async Task SaveCommand_AddsDeadlineAndRunsSavePublishRefreshInOrder()
    {
        var fixture = Fixture([]);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.AddCommand.Execute(null);
        fixture.ViewModel.Editor.Title = " 数据结构 ";
        fixture.ViewModel.Editor.DueDate = new DateTime(2026, 9, 20);
        fixture.ViewModel.Editor.Hour = "18";
        fixture.ViewModel.Editor.Minute = "";

        await fixture.ViewModel.SaveCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "publish", "refresh", "save", "publish", "refresh" }, fixture.Events);
        Assert.AreEqual("数据结构", fixture.ViewModel.ActiveDeadlines.Single().Title);
        Assert.AreEqual(0, fixture.ViewModel.ActiveDeadlines.Single().DueAt.Minute);
        Assert.AreEqual("已保存", fixture.ViewModel.StatusMessage);
    }

    [TestMethod]
    public async Task EditCommand_UpdatesExistingDeadline()
    {
        var original = Item("旧标题", Now.AddDays(2));
        var fixture = Fixture([original]);
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.EditCommand.Execute(original);
        fixture.ViewModel.Editor.Title = "新标题";
        fixture.ViewModel.Editor.DueDate = new DateTime(2026, 9, 25);
        fixture.ViewModel.Editor.Hour = "9";
        fixture.ViewModel.Editor.Minute = "15";
        await fixture.ViewModel.SaveCommand.ExecuteAsync(null);

        var updated = fixture.ViewModel.ActiveDeadlines.Single();
        Assert.AreEqual(original.Id, updated.Id);
        Assert.AreEqual("新标题", updated.Title);
        Assert.AreEqual(new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Local), updated.DueAt);
    }

    [TestMethod]
    public async Task CompleteRestoreAndDeleteCommands_MutateExpectedCollections()
    {
        var item = Item("课程", Now.AddDays(2));
        var fixture = Fixture([item]);
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.CompleteCommand.ExecuteAsync(item);
        var completed = fixture.ViewModel.CompletedDeadlines.Single();
        Assert.IsTrue(completed.IsCompleted);

        await fixture.ViewModel.RestoreCommand.ExecuteAsync(completed);
        var restored = fixture.ViewModel.ActiveDeadlines.Single();
        Assert.IsFalse(restored.IsCompleted);

        await fixture.ViewModel.DeleteCommand.ExecuteAsync(restored);
        Assert.AreEqual(0, fixture.ViewModel.ActiveDeadlines.Count);
        Assert.AreEqual(3, fixture.Store.Saves.Count);
    }

    [TestMethod]
    public async Task SaveFailure_DoesNotPublishOrReportSuccess()
    {
        var fixture = Fixture([]);
        fixture.Store.SaveException = new IOException("disk full");
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.AddCommand.Execute(null);
        fixture.ViewModel.Editor.Title = "课程";
        fixture.ViewModel.Editor.DueDate = new DateTime(2026, 9, 20);
        fixture.ViewModel.Editor.Hour = "18";

        await fixture.ViewModel.SaveCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "publish", "refresh", "save" }, fixture.Events);
        Assert.AreEqual(0, fixture.ViewModel.ActiveDeadlines.Count);
        Assert.IsNull(fixture.ViewModel.StatusMessage);
        StringAssert.Contains(fixture.ViewModel.ErrorMessage, "保存失败");
    }

    [TestMethod]
    public async Task SnapshotFailure_HappensOnlyAfterSuccessfulSaveAndDoesNotReportSuccess()
    {
        var item = Item("课程", Now.AddDays(2));
        var fixture = Fixture([item]);
        fixture.Publisher.Exception = new IOException("replace failed");
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.CompleteCommand.ExecuteAsync(item);

        CollectionAssert.AreEqual(new[] { "publish", "save", "publish" }, fixture.Events);
        Assert.IsTrue(fixture.Store.Saves.Single().Single().IsCompleted);
        Assert.IsNull(fixture.ViewModel.StatusMessage);
        StringAssert.Contains(fixture.ViewModel.ErrorMessage, "快照");
    }

    [TestMethod]
    public async Task MissingRainmeter_IsNonFatalWarningAfterDataIsSaved()
    {
        var item = Item("课程", Now.AddDays(2));
        var fixture = Fixture([item]);
        fixture.Refresher.Result = new RainmeterRefreshResult(false, "未找到 Rainmeter");
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.CompleteCommand.ExecuteAsync(item);

        Assert.AreEqual("已保存", fixture.ViewModel.StatusMessage);
        Assert.AreEqual("未找到 Rainmeter", fixture.ViewModel.WarningMessage);
        Assert.IsNull(fixture.ViewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task ConcurrentCrudCommands_AreSerializedAndBuildFromLatestSavedState()
    {
        var first = Item("第一项", Now.AddDays(2));
        var second = Item("第二项", Now.AddDays(3));
        var store = new BlockingStore([first, second]);
        var events = new List<string>();
        var viewModel = new ManagerViewModel(
            store,
            new FakePublisher(events),
            new FakeRefresher(events),
            new DeadlineService(),
            () => Now);
        await viewModel.InitializeAsync();

        var completeTask = viewModel.CompleteCommand.ExecuteAsync(first);
        await store.FirstSaveStarted.Task;
        var deleteTask = viewModel.DeleteCommand.ExecuteAsync(second);
        await Task.Delay(50);

        Assert.AreEqual(1, store.SaveCallCount);
        store.ReleaseFirstSave.TrySetResult();
        await Task.WhenAll(completeTask, deleteTask);
        Assert.AreEqual(2, store.SaveCallCount);
        Assert.AreEqual(0, viewModel.ActiveDeadlines.Count);
        Assert.AreEqual(first.Id, viewModel.CompletedDeadlines.Single().Id);
        var finalSave = store.Saves.Last();
        Assert.AreEqual(1, finalSave.Count);
        Assert.IsTrue(finalSave.Single().IsCompleted);
    }

    [TestMethod]
    public async Task CrudCommandsWhileEditingAnotherDeadline_PreserveUnsavedDraft()
    {
        var edited = Item("编辑目标", Now.AddDays(5));
        var toComplete = Item("待完成", Now.AddDays(2));
        var toRestore = Item("待恢复", Now.AddDays(3)) with { IsCompleted = true, CompletedAt = Now.AddHours(-1) };
        var toDelete = Item("待删除", Now.AddDays(4));
        var fixture = Fixture([edited, toComplete, toRestore, toDelete]);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.EditCommand.Execute(edited);
        fixture.ViewModel.Editor.Title = "尚未保存的草稿";

        await fixture.ViewModel.CompleteCommand.ExecuteAsync(toComplete);
        AssertDraftIsStillOpen(fixture.ViewModel);
        await fixture.ViewModel.RestoreCommand.ExecuteAsync(toRestore);
        AssertDraftIsStillOpen(fixture.ViewModel);
        await fixture.ViewModel.DeleteCommand.ExecuteAsync(toDelete);
        AssertDraftIsStillOpen(fixture.ViewModel);
    }

    [TestMethod]
    public async Task OlderEditorSaveCompleting_DoesNotCloseNewerEditorSession()
    {
        var first = Item("第一项", Now.AddDays(2));
        var second = Item("第二项", Now.AddDays(3));
        var store = new BlockingStore([first, second]);
        var events = new List<string>();
        var viewModel = new ManagerViewModel(
            store,
            new FakePublisher(events),
            new FakeRefresher(events),
            new DeadlineService(),
            () => Now);
        await viewModel.InitializeAsync();
        viewModel.EditCommand.Execute(first);
        viewModel.Editor.Title = "第一项已保存";

        var firstSave = viewModel.SaveCommand.ExecuteAsync(null);
        await store.FirstSaveStarted.Task;
        viewModel.EditCommand.Execute(second);
        viewModel.Editor.Title = "第二项未保存草稿";
        store.ReleaseFirstSave.TrySetResult();
        await firstSave;

        Assert.IsTrue(viewModel.IsEditorOpen);
        Assert.AreEqual("第二项未保存草稿", viewModel.Editor.Title);
        Assert.AreEqual("第一项已保存", viewModel.ActiveDeadlines.Single(item => item.Id == first.Id).Title);
    }

    private static TestFixture Fixture(IReadOnlyList<Deadline> initial)
    {
        var events = new List<string>();
        var store = new FakeStore(initial, events);
        var publisher = new FakePublisher(events);
        var refresher = new FakeRefresher(events);
        return new TestFixture(
            new ManagerViewModel(store, publisher, refresher, new DeadlineService(), () => Now),
            store,
            publisher,
            refresher,
            events);
    }

    private static Deadline Item(string title, DateTime dueAt) =>
        new(Guid.NewGuid(), title, dueAt, false, Now.AddDays(-1), Now.AddDays(-1), null);

    private static void AssertDraftIsStillOpen(ManagerViewModel viewModel)
    {
        Assert.IsTrue(viewModel.IsEditorOpen);
        Assert.AreEqual("尚未保存的草稿", viewModel.Editor.Title);
    }

    private sealed record TestFixture(
        ManagerViewModel ViewModel,
        FakeStore Store,
        FakePublisher Publisher,
        FakeRefresher Refresher,
        List<string> Events);

    private sealed class FakeStore(IReadOnlyList<Deadline> initial, List<string> events) : IDeadlineStore
    {
        public Exception? SaveException { get; set; }
        public List<IReadOnlyCollection<Deadline>> Saves { get; } = [];

        public Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(initial);

        public Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default)
        {
            events.Add("save");
            if (SaveException is not null)
            {
                throw SaveException;
            }

            Saves.Add(deadlines.ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublisher(List<string> events) : ISnapshotPublisher
    {
        public Exception? Exception { get; set; }
        public List<IReadOnlyCollection<Deadline>> PublishedSnapshots { get; } = [];

        public Task PublishAsync(IReadOnlyCollection<Deadline> deadlines, DateTime generatedAt, CancellationToken cancellationToken = default)
        {
            events.Add("publish");
            PublishedSnapshots.Add(deadlines.ToArray());
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }

    private sealed class FakeRefresher(List<string> events) : IRainmeterRefresher
    {
        public RainmeterRefreshResult Result { get; set; } = new(true, null);

        public Task<RainmeterRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            events.Add("refresh");
            return Task.FromResult(Result);
        }
    }

    private sealed class BlockingStore(IReadOnlyList<Deadline> initial) : IDeadlineStore
    {
        private int _saveCallCount;

        public TaskCompletionSource FirstSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IReadOnlyCollection<Deadline>> Saves { get; } = [];
        public int SaveCallCount => Volatile.Read(ref _saveCallCount);

        public Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(initial);

        public async Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _saveCallCount);
            lock (Saves)
            {
                Saves.Add(deadlines.ToArray());
            }

            if (call == 1)
            {
                FirstSaveStarted.TrySetResult();
                await ReleaseFirstSave.Task;
            }
        }
    }
}
