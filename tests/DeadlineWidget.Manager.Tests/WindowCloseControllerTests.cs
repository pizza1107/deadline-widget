using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using DeadlineWidget.Core;
using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class WindowCloseControllerTests
{
    [TestMethod]
    public void Closing_HidesWindowAndKeepsApplicationAlive()
    {
        var events = new List<string>();
        var controller = new WindowCloseController(
            () => events.Add("hide"));
        var closing = new CancelEventArgs();

        controller.HandleClosing(closing);

        Assert.IsTrue(closing.Cancel);
        CollectionAssert.AreEqual(new[] { "hide" }, events);
    }

    [TestMethod]
    public void AuthorizedApplicationExit_AllowsWindowToClose()
    {
        var events = new List<string>();
        var controller = new WindowCloseController(() => events.Add("hide"));
        controller.AuthorizeApplicationExit();
        var closing = new CancelEventArgs();

        controller.HandleClosing(closing);

        Assert.IsFalse(closing.Cancel);
        Assert.AreEqual(0, events.Count);
    }

    [TestMethod]
    public void MainWindow_CloseHidesWithoutClosingOrExiting() => RunSta(() =>
    {
        var events = new List<string>();
        var viewModel = new ManagerViewModel(
            new EmptyStore(),
            new EmptyPublisher(),
            new EmptyRefresher());
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        var closeCoordinator = new ManagerCloseCoordinator(viewModel, () =>
        {
            events.Add("dispose");
            return Task.CompletedTask;
        });
        var windowClosed = false;
        var exitRequests = 0;
        var window = new MainWindow(viewModel, closeCoordinator, () =>
        {
            exitRequests++;
            Assert.IsTrue(windowClosed, "The window must close before requesting process exit.");
        });
        window.Closed += (_, _) => windowClosed = true;

        window.Show();
        window.Close();

        Assert.IsFalse(window.IsVisible);
        Assert.IsFalse(windowClosed);
        Assert.AreEqual(0, exitRequests);
        Assert.AreEqual(0, events.Count);
        window.AuthorizeApplicationExit();
        window.Close();
    });

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), "Timed out waiting for the asynchronous close controller.");
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private sealed class EmptyStore : IDeadlineStore
    {
        public Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Deadline>>([]);

        public Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ItemStore(Deadline item) : IDeadlineStore
    {
        public Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Deadline>>([item]);

        public Task SaveAsync(IReadOnlyCollection<Deadline> deadlines, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyPublisher : ISnapshotPublisher
    {
        public Task PublishAsync(IReadOnlyCollection<Deadline> deadlines, DateTime generatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ToggleFailingPublisher : ISnapshotPublisher
    {
        public bool ShouldFail { get; set; }

        public Task PublishAsync(IReadOnlyCollection<Deadline> deadlines, DateTime generatedAt, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new IOException("replace failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class EmptyRefresher : IRainmeterRefresher
    {
        public Task<RainmeterRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RainmeterRefreshResult(true, null));
    }
}
