using System.Diagnostics;
using System.IO;

namespace DeadlineWidget.Manager;

public sealed record RainmeterRefreshResult(bool WasRequested, string? Warning);

public interface IRainmeterRefresher
{
    Task<RainmeterRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IRainmeterProcess
{
    bool FileExists(string path);
    Task RunAsync(string executable, string arguments, CancellationToken cancellationToken);
}

public sealed class RainmeterRefresher : IRainmeterRefresher
{
    public const string RefreshArguments = "!Refresh DeadlineWidgetV2";

    private readonly IRainmeterLocator _locator;
    private readonly IRainmeterProcess _process;

    public RainmeterRefresher(string? executablePath = null, IRainmeterProcess? process = null)
    {
        _process = process ?? new RainmeterProcess();
        _locator = executablePath is null
            ? new RainmeterLocator()
            : new ExplicitRainmeterLocator(executablePath, _process);
    }

    public RainmeterRefresher(IRainmeterLocator locator, IRainmeterProcess? process = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _process = process ?? new RainmeterProcess();
    }

    public async Task<RainmeterRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var executablePath = _locator.FindExecutable();
        if (executablePath is null)
        {
            return new RainmeterRefreshResult(false, "未找到 Rainmeter；数据已安全保存，可稍后手动刷新。 ");
        }

        try
        {
            await _process.RunAsync(executablePath, RefreshArguments, cancellationToken);
            return new RainmeterRefreshResult(true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new RainmeterRefreshResult(false, $"Rainmeter 刷新失败：{exception.Message}");
        }
    }

    private sealed class ExplicitRainmeterLocator(string path, IRainmeterProcess process) : IRainmeterLocator
    {
        public string? FindExecutable() => process.FileExists(path) ? path : null;
    }
}

internal sealed class RainmeterProcess : IRainmeterProcess
{
    public bool FileExists(string path) => File.Exists(path);

    public Task RunAsync(string executable, string arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(startInfo)?.Dispose();
        return Task.CompletedTask;
    }
}
