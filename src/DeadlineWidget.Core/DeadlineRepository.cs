using System.Text.Json;
using System.Text;

namespace DeadlineWidget.Core;

public sealed class DeadlineRepository
{
    public const int SchemaVersion = 2;

    private const string PrimaryFileName = "deadlines.json";
    private const string BackupFileName = "deadlines.backup.json";
    private const string TempFileName = "deadlines.tmp.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _directoryPath;
    private readonly IDeadlineFileReplacer _fileReplacer;

    public DeadlineRepository(
        string? directoryPath = null,
        IDeadlineFileReplacer? fileReplacer = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeadlineWidgetV2");

        if (string.IsNullOrWhiteSpace(_directoryPath))
        {
            throw new ArgumentException("Directory path is required.", nameof(directoryPath));
        }

        _fileReplacer = fileReplacer ?? new PlatformFileReplacer();
    }

    public async Task<IReadOnlyList<Deadline>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var primaryPath = Path.Combine(_directoryPath, PrimaryFileName);
        var backupPath = Path.Combine(_directoryPath, BackupFileName);

        if (!File.Exists(primaryPath) && !File.Exists(backupPath))
        {
            return [];
        }

        if (File.Exists(primaryPath))
        {
            try
            {
                return await ReadValidAsync(primaryPath, cancellationToken);
            }
            catch (InvalidDataException)
            {
                PreserveInvalidOriginal(primaryPath);
            }
        }

        if (File.Exists(backupPath))
        {
            try
            {
                return await ReadValidAsync(backupPath, cancellationToken);
            }
            catch (InvalidDataException)
            {
                PreserveInvalidOriginal(backupPath);
            }
        }

        throw new InvalidDataException("No valid deadline data file is available.");
    }

    public async Task SaveAsync(
        IEnumerable<Deadline> deadlines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadlines);
        var items = deadlines.ToArray();
        Validate(items);

        Directory.CreateDirectory(_directoryPath);
        var primaryPath = Path.Combine(_directoryPath, PrimaryFileName);
        var backupPath = Path.Combine(_directoryPath, BackupFileName);
        var tempPath = Path.Combine(_directoryPath, TempFileName);
        var document = new DeadlineDocument(SchemaVersion, items);

        try
        {
            await WriteAndFlushAsync(tempPath, document, cancellationToken);

            var primaryIsValid = await IsValidAsync(primaryPath, cancellationToken);
            if (File.Exists(primaryPath) && !primaryIsValid)
            {
                PreserveInvalidOriginal(primaryPath);
            }

            var backupIsValid = await IsValidAsync(backupPath, cancellationToken);
            if (File.Exists(backupPath) && !backupIsValid)
            {
                PreserveInvalidOriginal(backupPath);
            }

            if (primaryIsValid)
            {
                File.Copy(primaryPath, backupPath, overwrite: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(primaryPath))
            {
                _fileReplacer.Replace(tempPath, primaryPath);
            }
            else
            {
                File.Move(tempPath, primaryPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task WriteAndFlushAsync(
        string path,
        DeadlineDocument document,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task<IReadOnlyList<Deadline>> ReadValidAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var document = await JsonSerializer.DeserializeAsync<DeadlineDocument>(
                stream, JsonOptions, cancellationToken);
            if (document is null || document.SchemaVersion != SchemaVersion || document.Deadlines is null)
            {
                throw new InvalidDataException("Deadline data has an unsupported or invalid schema.");
            }

            Validate(document.Deadlines);
            return document.Deadlines.ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Deadline data is not valid JSON.", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Deadline data is not valid UTF-8.", exception);
        }
    }

    private static async Task<bool> IsValidAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            await ReadValidAsync(path, cancellationToken);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void Validate(IReadOnlyCollection<Deadline> deadlines)
    {
        var ids = new HashSet<Guid>();
        if (deadlines.Any(deadline =>
                deadline is null
                || deadline.Id == Guid.Empty
                || !ids.Add(deadline.Id)
                || string.IsNullOrWhiteSpace(deadline.Title)
                || deadline.DueAt == default
                || deadline.CreatedAt == default
                || deadline.ModifiedAt == default
                || deadline.IsCompleted != deadline.CompletedAt.HasValue))
        {
            throw new InvalidDataException("Deadline data contains invalid items.");
        }
    }

    private void PreserveInvalidOriginal(string path)
    {
        var recoveryDirectory = Path.Combine(_directoryPath, "recovery");
        Directory.CreateDirectory(recoveryDirectory);
        var recoveryPath = Path.Combine(
            recoveryDirectory,
            $"{Path.GetFileName(path)}.recovery-{Guid.NewGuid():N}");
        File.Copy(path, recoveryPath, overwrite: false);
    }

    private sealed record DeadlineDocument(int SchemaVersion, IReadOnlyList<Deadline> Deadlines);

    private sealed class PlatformFileReplacer : IDeadlineFileReplacer
    {
        public void Replace(string sourcePath, string destinationPath) =>
            File.Replace(sourcePath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
    }
}
