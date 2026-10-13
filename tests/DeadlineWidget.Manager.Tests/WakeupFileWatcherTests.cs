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
}
