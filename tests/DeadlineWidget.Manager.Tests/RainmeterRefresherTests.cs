using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class RainmeterRefresherTests
{
    [TestMethod]
    public async Task Refresh_WhenRainmeterIsMissing_ReturnsNonFatalWarning()
    {
        var process = new FakeProcess { Exists = false };
        var refresher = new RainmeterRefresher("X:\\Rainmeter.exe", process);

        var result = await refresher.RefreshAsync();

        Assert.IsFalse(result.WasRequested);
        StringAssert.Contains(result.Warning, "Rainmeter");
        Assert.AreEqual(0, process.RunCount);
    }

    [TestMethod]
    public async Task Refresh_ExecutesExactRefreshBangForV2Skin()
    {
        var process = new FakeProcess { Exists = true };
        var refresher = new RainmeterRefresher("X:\\Rainmeter.exe", process);

        var result = await refresher.RefreshAsync();

        Assert.IsTrue(result.WasRequested);
        Assert.IsNull(result.Warning);
        Assert.AreEqual("X:\\Rainmeter.exe", process.Executable);
        Assert.AreEqual("!Refresh DeadlineWidgetV2", process.Arguments);
    }

    private sealed class FakeProcess : IRainmeterProcess
    {
        public bool Exists { get; set; }
        public int RunCount { get; private set; }
        public string? Executable { get; private set; }
        public string? Arguments { get; private set; }

        public bool FileExists(string path) => Exists;

        public Task RunAsync(string executable, string arguments, CancellationToken cancellationToken)
        {
            RunCount++;
            Executable = executable;
            Arguments = arguments;
            return Task.CompletedTask;
        }
    }
}
