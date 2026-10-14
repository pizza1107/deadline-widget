using System.Text;
using System.Text.Json;
using DeadlineWidget.Core;
using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class SnapshotPublisherTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Local);

    [TestMethod]
    public async Task DefaultPublisher_WritesOnlyToTheRainmeterSettingsDirectory()
    {
        var fileSystem = new FakeSnapshotFileSystem();
        var publisher = new SnapshotPublisher(fileSystem: fileSystem, clock: new DeadlineClock());
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Rainmeter",
            "DeadlineWidgetV2");

        await publisher.PublishAsync([], Now);

        Assert.AreEqual(expectedDirectory, fileSystem.CreatedDirectory);
        Assert.AreEqual(Path.Combine(expectedDirectory, "display.tmp.json"), fileSystem.WrittenPath);
        Assert.AreEqual(Path.Combine(expectedDirectory, "display.json"), fileSystem.MoveDestinationPath);
    }

    [TestMethod]
    public async Task Publish_WritesBomMarkedUtf8SnapshotWithoutLosingUnicode()
    {
        var fileSystem = new FakeSnapshotFileSystem();
        var publisher = new SnapshotPublisher("X:\\DeadlineWidgetV2", fileSystem, new DeadlineClock());
        var title = "课程 😀 𠮷 café";
        var deadline = new Deadline(Guid.NewGuid(), title, Now.AddDays(5), false, Now, Now, null);

        await publisher.PublishAsync([deadline], Now);

        CollectionAssert.AreEqual(new[] { "create-directory", "write-temp", "move-temp" }, fileSystem.Events);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, fileSystem.DestinationBytes![..3]);
        var json = new UTF8Encoding(false, true).GetString(fileSystem.DestinationBytes[3..]);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.AreEqual(title, document.RootElement.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.AreEqual("Warning", document.RootElement.GetProperty("items")[0].GetProperty("urgency").GetString());
    }

    [TestMethod]
    public async Task Publish_WhenAtomicReplaceFails_PreservesDestinationAndDeletesTemp()
    {
        var original = Encoding.ASCII.GetBytes("original");
        var fileSystem = new FakeSnapshotFileSystem { DestinationBytes = original, ThrowOnReplace = true };
        var publisher = new SnapshotPublisher("X:\\DeadlineWidgetV2", fileSystem, new DeadlineClock());
        var deadline = new Deadline(Guid.NewGuid(), "课程", Now.AddDays(2), false, Now, Now, null);

        await Assert.ThrowsExceptionAsync<IOException>(() => publisher.PublishAsync([deadline], Now));

        CollectionAssert.AreEqual(original, fileSystem.DestinationBytes!);
        Assert.IsNull(fileSystem.TempBytes);
        CollectionAssert.Contains(fileSystem.Events, "delete-temp");
    }

    private sealed class FakeSnapshotFileSystem : ISnapshotFileSystem
    {
        public List<string> Events { get; } = [];
        public byte[]? TempBytes { get; private set; }
        public byte[]? DestinationBytes { get; set; }
        public bool ThrowOnReplace { get; set; }
        public string? CreatedDirectory { get; private set; }
        public string? WrittenPath { get; private set; }
        public string? MoveDestinationPath { get; private set; }

        public void CreateDirectory(string path)
        {
            Events.Add("create-directory");
            CreatedDirectory = path;
        }
        public bool FileExists(string path) => path.EndsWith("display.json", StringComparison.Ordinal) ? DestinationBytes is not null : TempBytes is not null;

        public Task WriteAllBytesAndFlushAsync(string path, byte[] contents, CancellationToken cancellationToken)
        {
            Events.Add("write-temp");
            WrittenPath = path;
            TempBytes = contents;
            return Task.CompletedTask;
        }

        public void Replace(string sourcePath, string destinationPath)
        {
            Events.Add("replace-temp");
            if (ThrowOnReplace)
            {
                throw new IOException("replace failed");
            }

            DestinationBytes = TempBytes;
            TempBytes = null;
        }

        public void Move(string sourcePath, string destinationPath)
        {
            Events.Add("move-temp");
            MoveDestinationPath = destinationPath;
            DestinationBytes = TempBytes;
            TempBytes = null;
        }

        public void Delete(string path)
        {
            Events.Add("delete-temp");
            TempBytes = null;
        }
    }
}
