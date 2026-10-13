using System.Text.Json;

namespace DeadlineWidget.Core;

public sealed class LegacyDeadlineImporter
{
    private const string LegacyDirectoryName = "CourseDeadlineWidget";
    private const string V2DirectoryName = "DeadlineWidgetV2";
    private const string PrimaryFileName = "deadlines.json";
    private const string MarkerFileName = "migration-v2.complete";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _legacyPath;
    private readonly string _v2Directory;
    private readonly string _primaryPath;
    private readonly string _markerPath;

    public LegacyDeadlineImporter(string? localApplicationDataPath = null)
    {
        var root = localApplicationDataPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Local application data path is required.", nameof(localApplicationDataPath));
        }

        _legacyPath = Path.Combine(root, LegacyDirectoryName, PrimaryFileName);
        _v2Directory = Path.Combine(root, V2DirectoryName);
        _primaryPath = Path.Combine(_v2Directory, PrimaryFileName);
        _markerPath = Path.Combine(_v2Directory, MarkerFileName);
    }

    public async Task<bool> ImportOnceAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_primaryPath) || File.Exists(_markerPath) || !File.Exists(_legacyPath))
        {
            return false;
        }

        var imported = await ReadLegacyAsync(cancellationToken);
        var repository = new DeadlineRepository(_v2Directory);
        await repository.SaveAsync(imported, cancellationToken);

        var reloaded = await repository.LoadAsync(cancellationToken);
        if (!MigrationMatches(imported, reloaded))
        {
            throw new InvalidDataException("V2 migration verification failed.");
        }

        await using var marker = new FileStream(
            _markerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(marker);
        await writer.WriteAsync("schema 2 migration complete".AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        marker.Flush(flushToDisk: true);
        return true;
    }

    private async Task<IReadOnlyList<Deadline>> ReadLegacyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                _legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var store = await JsonSerializer.DeserializeAsync<LegacyStore>(stream, JsonOptions, cancellationToken);
            if (store?.Version != 1 || store.Items is null)
            {
                throw new InvalidDataException("Legacy deadline data has an unsupported or invalid schema.");
            }

            var result = store.Items.Select(item => new Deadline(
                item.Id,
                item.Title,
                item.DueAt,
                item.IsCompleted,
                item.CreatedAt,
                item.UpdatedAt,
                item.CompletedAt)).ToArray();
            ValidateLegacy(result);
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Legacy deadline data is not valid JSON.", exception);
        }
    }

    private static bool MigrationMatches(
        IReadOnlyList<Deadline> imported,
        IReadOnlyList<Deadline> reloaded)
    {
        if (imported.Count != reloaded.Count)
        {
            return false;
        }

        var expected = imported.OrderBy(item => item.Id).ToArray();
        var actual = reloaded.OrderBy(item => item.Id).ToArray();
        return expected.Zip(actual).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Title == pair.Second.Title
            && pair.First.DueAt == pair.Second.DueAt
            && pair.First.IsCompleted == pair.Second.IsCompleted);
    }

    private static void ValidateLegacy(IReadOnlyCollection<Deadline> deadlines)
    {
        var ids = new HashSet<Guid>();
        if (deadlines.Any(deadline =>
                deadline.Id == Guid.Empty
                || !ids.Add(deadline.Id)
                || string.IsNullOrWhiteSpace(deadline.Title)
                || deadline.DueAt == default
                || deadline.CreatedAt == default
                || deadline.ModifiedAt == default
                || deadline.IsCompleted != deadline.CompletedAt.HasValue))
        {
            throw new InvalidDataException("Legacy deadline data contains invalid items.");
        }
    }

    private sealed record LegacyStore(int Version, IReadOnlyList<LegacyDeadline> Items);

    private sealed record LegacyDeadline(
        Guid Id,
        string Title,
        DateTime DueAt,
        bool IsCompleted,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? CompletedAt);
}
