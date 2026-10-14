using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class WakeupFileWatcherTests
{
    [TestMethod]
    public async Task RewritingWakeupFile_RequestsWindowActivation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeadlineWidgetV2.WakeupTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "manager-request.ini");
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var watcher = new WakeupFileWatcher(path, () =>
            {
                activated.TrySetResult();
                return Task.CompletedTask;
            });

            await File.WriteAllTextAsync(path, "[Variables]\r\nOpenRequest=1\r\n");

            await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task ActivationFailure_IsRecordedAndDoesNotEscapeTheWatcher()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeadlineWidgetV2.WakeupTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "manager-request.ini");
        var diagnostics = new RecordingDiagnosticSink();
        try
        {
            using var watcher = new WakeupFileWatcher(
                path,
                () => Task.FromException(new IOException("activation failed")),
                diagnostics);

            await File.WriteAllTextAsync(path, "[Variables]\r\nOpenRequest=1\r\n");
            await diagnostics.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("wakeup-watcher", diagnostics.Component);
            Assert.AreEqual("activation failed", diagnostics.Exception?.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Component { get; private set; }
        public Exception? Exception { get; private set; }

        public void Write(string component, Exception exception)
        {
            Component = component;
            Exception = exception;
            Written.TrySetResult();
        }
    }
}
