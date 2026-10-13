using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class SingleInstanceCoordinatorTests
{
    [TestMethod]
    public async Task StartAsSecondInstance_SendsExactlyActivateAndReturnsFalse()
    {
        var platform = new FakePlatform { Acquired = false };
        var coordinator = new SingleInstanceCoordinator(platform, () => Task.CompletedTask);

        var isPrimary = await coordinator.StartAsync();

        Assert.IsFalse(isPrimary);
        Assert.AreEqual("Local\\DeadlineWidgetV2.Manager", platform.MutexName);
        Assert.AreEqual("DeadlineWidgetV2.Activate", platform.PipeName);
        Assert.AreEqual("ACTIVATE", platform.SentMessage);
    }

    [TestMethod]
    public async Task PrimaryListener_ActivatesOnlyForExactMessage()
    {
        var activationCount = 0;
        var platform = new FakePlatform { Acquired = true };
        await using var coordinator = new SingleInstanceCoordinator(platform, () =>
        {
            activationCount++;
            return Task.CompletedTask;
        });
        Assert.IsTrue(await coordinator.StartAsync());

        await platform.DeliverAsync("ignored");
        await platform.DeliverAsync("ACTIVATE");

        Assert.AreEqual(1, activationCount);
    }

    [TestMethod]
    public async Task PrimaryListener_DoesNotWaitForQueuedWindowActivation()
    {
        var activationRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new FakePlatform { Acquired = true };
        var coordinator = new SingleInstanceCoordinator(platform, () => activationRelease.Task);
        Assert.IsTrue(await coordinator.StartAsync());

        var delivery = platform.DeliverAsync("ACTIVATE");
        await Task.Delay(50);
        var returnedBeforeWindowActivation = delivery.IsCompleted;
        activationRelease.TrySetResult();
        await delivery;
        await coordinator.DisposeAsync();

        Assert.IsTrue(returnedBeforeWindowActivation);
    }

    [TestMethod]
    public async Task Dispose_CancelsListenerAndReleasesOwnedMutex()
    {
        var platform = new FakePlatform { Acquired = true };
        var coordinator = new SingleInstanceCoordinator(platform, () => Task.CompletedTask);
        Assert.IsTrue(await coordinator.StartAsync());

        await coordinator.DisposeAsync();

        Assert.IsTrue(platform.ListenerCancelled);
        Assert.IsTrue(platform.Released);
    }

    [TestMethod]
    public async Task Dispose_WhenListenerAlreadyFaulted_StillReleasesOwnedMutex()
    {
        var platform = new FakePlatform { Acquired = true, ListenerException = new IOException("pipe failed") };
        var coordinator = new SingleInstanceCoordinator(platform, () => Task.CompletedTask);
        Assert.IsTrue(await coordinator.StartAsync());

        await coordinator.DisposeAsync();

        Assert.IsTrue(platform.Released);
    }

    [TestMethod]
    public async Task WindowsListener_CancellationDoesNotDependOnCapturedUiContext()
    {
        var platform = new WindowsSingleInstancePlatform();
        using var cancellation = new CancellationTokenSource();
        var previousContext = SynchronizationContext.Current;
        Task listener;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new NeverRunSynchronizationContext());
            listener = platform.ListenAsync(
                $"DeadlineWidgetV2.Tests.{Guid.NewGuid():N}",
                _ => Task.CompletedTask,
                cancellation.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        cancellation.Cancel();
        var completed = await Task.WhenAny(listener, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.AreSame(listener, completed);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => listener);
    }

    private sealed class FakePlatform : ISingleInstancePlatform
    {
        private Func<string, Task>? _onMessage;
        private TaskCompletionSource _listener = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Acquired { get; set; }
        public Exception? ListenerException { get; set; }
        public string? MutexName { get; private set; }
        public string? PipeName { get; private set; }
        public string? SentMessage { get; private set; }
        public bool ListenerCancelled { get; private set; }
        public bool Released { get; private set; }

        public bool TryAcquireMutex(string name)
        {
            MutexName = name;
            return Acquired;
        }

        public async Task ListenAsync(string pipeName, Func<string, Task> onMessage, CancellationToken cancellationToken)
        {
            PipeName = pipeName;
            _onMessage = onMessage;
            if (ListenerException is not null)
            {
                await Task.Yield();
                throw ListenerException;
            }

            using var registration = cancellationToken.Register(() =>
            {
                ListenerCancelled = true;
                _listener.TrySetCanceled(cancellationToken);
            });
            await _listener.Task;
        }

        public Task SendAsync(string pipeName, string message, CancellationToken cancellationToken)
        {
            PipeName = pipeName;
            SentMessage = message;
            return Task.CompletedTask;
        }

        public void ReleaseMutex() => Released = true;

        public Task DeliverAsync(string message) => _onMessage!(message);
    }

    private sealed class NeverRunSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }
    }
}
