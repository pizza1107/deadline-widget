using System.IO;

namespace DeadlineWidget.Manager;

public sealed class WakeupFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Func<Task> _activate;
    private int _activationPending;

    public WakeupFileWatcher(string wakeupFilePath, Func<Task> activate)
    {
        if (string.IsNullOrWhiteSpace(wakeupFilePath) || !Path.IsPathFullyQualified(wakeupFilePath))
        {
            throw new ArgumentException("Wakeup file path must be absolute.", nameof(wakeupFilePath));
        }

        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        var directory = Path.GetDirectoryName(wakeupFilePath)!;
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(wakeupFilePath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnWakeup;
        _watcher.Changed += OnWakeup;
        _watcher.Renamed += OnWakeup;
    }

    public void Dispose() => _watcher.Dispose();

    private void OnWakeup(object sender, FileSystemEventArgs eventArgs)
    {
        if (Interlocked.Exchange(ref _activationPending, 1) != 0)
        {
            return;
        }

        _ = ActivateAsync();
    }

    private async Task ActivateAsync()
    {
        try
        {
            await _activate();
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _activationPending, 0);
        }
    }
}
