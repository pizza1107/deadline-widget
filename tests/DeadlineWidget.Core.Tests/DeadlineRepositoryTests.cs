using System.Text.Json;
using DeadlineWidget.Core;

namespace DeadlineWidget.Core.Tests;

[TestClass]
public sealed class DeadlineRepositoryTests
{
    [TestMethod]
    public async Task SaveAsync_WritesSchemaTwoAndRemovesTemporaryFile()
    {
        using var sandbox = new TestDirectory();
        var item = Item("算法", 1);
        var repository = new DeadlineRepository(sandbox.Path);

        await repository.SaveAsync([item]);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(sandbox.File("deadlines.json")));
        Assert.AreEqual(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(item.Id, document.RootElement.GetProperty("deadlines")[0].GetProperty("id").GetGuid());
        Assert.IsFalse(File.Exists(sandbox.File("deadlines.tmp.json")));
    }

    [TestMethod]
    public async Task SaveAsync_CopiesLastValidPrimaryToBackupBeforeReplacingIt()
    {
        using var sandbox = new TestDirectory();
        var repository = new DeadlineRepository(sandbox.Path);
        var first = Item("第一版", 1);
        await repository.SaveAsync([first]);
        var originalBytes = await File.ReadAllBytesAsync(sandbox.File("deadlines.json"));

        await repository.SaveAsync([Item("第二版", 2)]);

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(sandbox.File("deadlines.backup.json")));
        Assert.AreEqual("第二版", (await repository.LoadAsync()).Single().Title);
    }

    [TestMethod]
    public async Task SaveAsync_PreservesCorruptBackupBeforeRotatingValidPrimary()
    {
        using var sandbox = new TestDirectory();
        var repository = new DeadlineRepository(sandbox.Path);
        await repository.SaveAsync([Item("第一版", 1)]);
        var corruptBackup = new byte[] { 0x7B, 0xFF, 0x00, 0x7D };
        await File.WriteAllBytesAsync(sandbox.File("deadlines.backup.json"), corruptBackup);
        Directory.CreateDirectory(sandbox.File("recovery"));

        await repository.SaveAsync([Item("第二版", 2)]);

        var preserved = Directory.GetFiles(
            sandbox.File("recovery"),
            "deadlines.backup.json.recovery-*");
        Assert.AreEqual(1, preserved.Length);
        CollectionAssert.AreEqual(corruptBackup, await File.ReadAllBytesAsync(preserved[0]));
        Assert.AreEqual("第一版", (await ReadTitlesAsync(sandbox.File("deadlines.backup.json"))).Single());
        Assert.AreEqual("第二版", (await repository.LoadAsync()).Single().Title);
    }

    [TestMethod]
    public async Task SaveAsync_WhenCorruptBackupCannotBePreserved_LeavesBothDataFilesUnchanged()
    {
        using var sandbox = new TestDirectory();
        var repository = new DeadlineRepository(sandbox.Path);
        await repository.SaveAsync([Item("第一版", 1)]);
        var primaryPath = sandbox.File("deadlines.json");
        var backupPath = sandbox.File("deadlines.backup.json");
        var originalPrimary = await File.ReadAllBytesAsync(primaryPath);
        var corruptBackup = new byte[] { 0x7B, 0xFF, 0x00, 0x7D };
        await File.WriteAllBytesAsync(backupPath, corruptBackup);
        await File.WriteAllTextAsync(sandbox.File("recovery"), "blocks recovery directory creation");

        await Assert.ThrowsExceptionAsync<IOException>(
            () => repository.SaveAsync([Item("第二版", 2)]));

        CollectionAssert.AreEqual(originalPrimary, await File.ReadAllBytesAsync(primaryPath));
        CollectionAssert.AreEqual(corruptBackup, await File.ReadAllBytesAsync(backupPath));
        Assert.IsFalse(File.Exists(sandbox.File("deadlines.tmp.json")));
    }

    [TestMethod]
    public async Task SaveAsync_WhenAtomicReplacementFails_LeavesPrimaryCleansTempAndCanRetry()
    {
        using var sandbox = new TestDirectory();
        var replacementFailure = new ThrowOnceFileReplacer();
        var repository = new DeadlineRepository(sandbox.Path, replacementFailure);
        await repository.SaveAsync([Item("第一版", 1)]);
        var primaryPath = sandbox.File("deadlines.json");
        var originalPrimary = await File.ReadAllBytesAsync(primaryPath);
        var replacement = Item("第二版", 2);

        replacementFailure.FailNextReplacement = true;

        await Assert.ThrowsExceptionAsync<IOException>(
            () => repository.SaveAsync([replacement]));

        CollectionAssert.AreEqual(originalPrimary, await File.ReadAllBytesAsync(primaryPath));
        Assert.IsFalse(File.Exists(sandbox.File("deadlines.tmp.json")));

        await repository.SaveAsync([replacement]);

        Assert.AreEqual(replacement.Id, (await repository.LoadAsync()).Single().Id);
        Assert.IsFalse(File.Exists(sandbox.File("deadlines.tmp.json")));
    }

    [TestMethod]
    public async Task SaveAsync_RetriesWindowsUnableToRemoveReplacedFileError()
    {
        using var sandbox = new TestDirectory();
        var replacer = new TransientWindowsFileReplacer { FailuresRemaining = 2 };
        var repository = new DeadlineRepository(sandbox.Path, replacer);
        await repository.SaveAsync([Item("第一版", 1)]);

        await repository.SaveAsync([Item("第二版", 2)]);

        Assert.AreEqual(3, replacer.ReplaceCount);
        Assert.AreEqual("第二版", (await repository.LoadAsync()).Single().Title);
    }

    [TestMethod]
    public async Task LoadAsync_RecoversFromBackupAndPreservesCorruptPrimaryBytes()
    {
        using var sandbox = new TestDirectory();
        var repository = new DeadlineRepository(sandbox.Path);
        var backupItem = Item("备份", 1);
        await repository.SaveAsync([backupItem]);
        await repository.SaveAsync([Item("当前", 2)]);
        var corrupt = new byte[] { 0x7B, 0xFF, 0x00, 0x7D };
        await File.WriteAllBytesAsync(sandbox.File("deadlines.json"), corrupt);

        var loaded = await repository.LoadAsync();

        Assert.AreEqual(backupItem.Id, loaded.Single().Id);
        CollectionAssert.AreEqual(corrupt, await File.ReadAllBytesAsync(sandbox.File("deadlines.json")));
        var preserved = Directory.GetFiles(sandbox.File("recovery"), "deadlines.json.recovery-*");
        Assert.AreEqual(1, preserved.Length);
        CollectionAssert.AreEqual(corrupt, await File.ReadAllBytesAsync(preserved[0]));
    }

    [TestMethod]
    public async Task LoadAsync_ReturnsEmptyWhenNoFilesExist()
    {
        using var sandbox = new TestDirectory();

        var loaded = await new DeadlineRepository(sandbox.Path).LoadAsync();

        Assert.AreEqual(0, loaded.Count);
        Assert.IsFalse(File.Exists(sandbox.File("deadlines.json")));
    }

    [TestMethod]
    public async Task LoadAsync_RejectsUnsupportedSchemaWithoutChangingIt()
    {
        using var sandbox = new TestDirectory();
        const string unsupported = "{\"schemaVersion\":99,\"deadlines\":[]}";
        await File.WriteAllTextAsync(sandbox.File("deadlines.json"), unsupported);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            () => new DeadlineRepository(sandbox.Path).LoadAsync());

        Assert.AreEqual(unsupported, await File.ReadAllTextAsync(sandbox.File("deadlines.json")));
    }

    private static Deadline Item(string title, int day) => new(
        Guid.NewGuid(), title, new DateTime(2026, 9, 13 + day, 18, 0, 0, DateTimeKind.Local), false,
        new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local),
        new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local), null);

    private static async Task<IReadOnlyList<string>> ReadTitlesAsync(string path)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return document.RootElement.GetProperty("deadlines")
            .EnumerateArray()
            .Select(item => item.GetProperty("title").GetString()!)
            .ToArray();
    }

    private sealed class ThrowOnceFileReplacer : IDeadlineFileReplacer
    {
        public bool FailNextReplacement { get; set; }

        public void Replace(string sourcePath, string destinationPath)
        {
            if (FailNextReplacement)
            {
                FailNextReplacement = false;
                throw new IOException("Test replacement failure.");
            }

            File.Replace(sourcePath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
    }

    private sealed class TransientWindowsFileReplacer : IDeadlineFileReplacer
    {
        public int FailuresRemaining { get; set; }
        public int ReplaceCount { get; private set; }

        public void Replace(string sourcePath, string destinationPath)
        {
            ReplaceCount++;
            if (FailuresRemaining-- > 0)
            {
                throw new IOException("Unable to remove file to be replaced.", unchecked((int)0x80070497));
            }

            File.Replace(sourcePath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
    }
}
