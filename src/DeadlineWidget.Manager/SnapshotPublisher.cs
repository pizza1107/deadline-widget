using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeadlineWidget.Core;

namespace DeadlineWidget.Manager;

public interface ISnapshotPublisher
{
    Task PublishAsync(
        IReadOnlyCollection<Deadline> deadlines,
        DateTime generatedAt,
        CancellationToken cancellationToken = default);
}

public interface ISnapshotFileSystem
{
    void CreateDirectory(string path);
    bool FileExists(string path);
    Task WriteAllBytesAndFlushAsync(string path, byte[] contents, CancellationToken cancellationToken);
    void Replace(string sourcePath, string destinationPath);
    void Move(string sourcePath, string destinationPath);
    void Delete(string path);
}

public sealed class SnapshotPublisher : ISnapshotPublisher
{
    private const string SnapshotFileName = "display.json";
    private const string TempFileName = "display.tmp.json";

    private static readonly UTF8Encoding SnapshotEncoding = new(
        encoderShouldEmitUTF8Identifier: true,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directoryPath;
    private readonly ISnapshotFileSystem _fileSystem;
    private readonly DeadlineClock _clock;

    public SnapshotPublisher(
        string? directoryPath = null,
        ISnapshotFileSystem? fileSystem = null,
        DeadlineClock? clock = null)
    {
        _directoryPath = directoryPath ?? ManagerPaths.SnapshotDirectoryFrom(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        if (string.IsNullOrWhiteSpace(_directoryPath))
        {
            throw new ArgumentException("Snapshot directory path is required.", nameof(directoryPath));
        }

        _fileSystem = fileSystem ?? new SnapshotFileSystem();
        _clock = clock ?? new DeadlineClock();
    }

    public async Task PublishAsync(
        IReadOnlyCollection<Deadline> deadlines,
        DateTime generatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadlines);
        var destinationPath = Path.Combine(_directoryPath, SnapshotFileName);
        var tempPath = Path.Combine(_directoryPath, TempFileName);
        var items = deadlines
            .Where(deadline => !deadline.IsCompleted)
            .OrderBy(deadline => deadline.DueAt < generatedAt)
            .ThenBy(deadline => deadline.DueAt)
            .ThenBy(deadline => deadline.CreatedAt)
            .Select(deadline =>
            {
                var state = _clock.Describe(deadline, generatedAt);
                return new SnapshotItem(
                    deadline.Id,
                    deadline.Title,
                    deadline.DueAt,
                    state.Countdown,
                    state.Urgency,
                    state.IsOverdue);
            })
            .ToArray();
        var document = new SnapshotDocument(1, generatedAt, items);

        var jsonBytes = SnapshotEncoding.GetBytes(JsonSerializer.Serialize(document, JsonOptions));
        var preamble = SnapshotEncoding.GetPreamble();
        var bytes = new byte[preamble.Length + jsonBytes.Length];
        preamble.CopyTo(bytes, 0);
        jsonBytes.CopyTo(bytes, preamble.Length);

        _fileSystem.CreateDirectory(_directoryPath);
        try
        {
            await _fileSystem.WriteAllBytesAndFlushAsync(tempPath, bytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_fileSystem.FileExists(destinationPath))
            {
                _fileSystem.Replace(tempPath, destinationPath);
            }
            else
            {
                _fileSystem.Move(tempPath, destinationPath);
            }
        }
        finally
        {
            if (_fileSystem.FileExists(tempPath))
            {
                _fileSystem.Delete(tempPath);
            }
        }
    }

    private sealed record SnapshotDocument(int Version, DateTime GeneratedAt, IReadOnlyList<SnapshotItem> Items);

    private sealed record SnapshotItem(
        Guid Id,
        string Title,
        DateTime DueAt,
        string Countdown,
        DeadlineUrgency Urgency,
        bool IsOverdue);
}

internal sealed class SnapshotFileSystem : ISnapshotFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool FileExists(string path) => File.Exists(path);

    public async Task WriteAllBytesAndFlushAsync(
        string path,
        byte[] contents,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(contents, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public void Replace(string sourcePath, string destinationPath) =>
        File.Replace(sourcePath, destinationPath, null, ignoreMetadataErrors: true);

    public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);
    public void Delete(string path) => File.Delete(path);
}
