using System.Globalization;
using System.IO;
using System.Text;

namespace DeadlineWidget.Manager;

public interface IDiagnosticSink
{
    void Write(string component, Exception exception);
}

public sealed class FileDiagnosticSink : IDiagnosticSink
{
    private const long MaximumActiveBytes = 1_048_576;
    private static readonly UTF8Encoding LogEncoding = new(false, true);
    private readonly string _directory;
    private readonly object _sync = new();

    public FileDiagnosticSink(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Diagnostic directory is required.", nameof(directory));
        }
        _directory = Path.GetFullPath(directory);
    }

    public void Write(string component, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            Directory.CreateDirectory(_directory);
            var logPath = Path.Combine(_directory, "manager.log");
            var backupPath = logPath + ".1";
            if (File.Exists(logPath) && new FileInfo(logPath).Length >= MaximumActiveBytes)
            {
                File.Move(logPath, backupPath, overwrite: true);
            }

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:O} {component} {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}");
            File.AppendAllText(logPath, line, LogEncoding);
        }
    }
}

internal sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();
    public void Write(string component, Exception exception)
    {
    }
}
