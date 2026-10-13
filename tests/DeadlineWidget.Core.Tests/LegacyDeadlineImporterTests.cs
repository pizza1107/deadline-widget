using System.Text.Json;
using DeadlineWidget.Core;

namespace DeadlineWidget.Core.Tests;

[TestClass]
public sealed class LegacyDeadlineImporterTests
{
    [TestMethod]
    public async Task ImportOnceAsync_CopiesLegacyDataVerifiesItAndWritesMarker()
    {
        using var localAppData = new TestDirectory();
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await WriteLegacyAsync(localAppData, id, "编译原理");
        var importer = new LegacyDeadlineImporter(localAppData.Path);

        var imported = await importer.ImportOnceAsync();

        Assert.IsTrue(imported);
        var v2Directory = localAppData.File("DeadlineWidgetV2");
        var loaded = await new DeadlineRepository(v2Directory).LoadAsync();
        Assert.AreEqual(id, loaded.Single().Id);
        Assert.AreEqual("编译原理", loaded.Single().Title);
        Assert.AreEqual(new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Local), loaded.Single().DueAt);
        Assert.IsTrue(loaded.Single().IsCompleted);
        Assert.IsTrue(File.Exists(Path.Combine(v2Directory, "migration-v2.complete")));
    }

    [TestMethod]
    public async Task ImportOnceAsync_IsIdempotentAfterSuccessfulImport()
    {
        using var localAppData = new TestDirectory();
        await WriteLegacyAsync(localAppData, Guid.NewGuid(), "原始");
        var importer = new LegacyDeadlineImporter(localAppData.Path);
        Assert.IsTrue(await importer.ImportOnceAsync());
        var primary = localAppData.File("DeadlineWidgetV2", "deadlines.json");
        var firstBytes = await File.ReadAllBytesAsync(primary);
        await WriteLegacyAsync(localAppData, Guid.NewGuid(), "后来");

        var importedAgain = await importer.ImportOnceAsync();

        Assert.IsFalse(importedAgain);
        CollectionAssert.AreEqual(firstBytes, await File.ReadAllBytesAsync(primary));
    }

    [TestMethod]
    public async Task ImportOnceAsync_DoesNothingWhenV2PrimaryAlreadyExistsWithoutMarker()
    {
        using var localAppData = new TestDirectory();
        await WriteLegacyAsync(localAppData, Guid.NewGuid(), "旧数据");
        var v2Directory = localAppData.File("DeadlineWidgetV2");
        var existing = new Deadline(
            Guid.NewGuid(), "V2 数据", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Local), false,
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Local),
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Local), null);
        await new DeadlineRepository(v2Directory).SaveAsync([existing]);

        var imported = await new LegacyDeadlineImporter(localAppData.Path).ImportOnceAsync();

        Assert.IsFalse(imported);
        Assert.AreEqual(existing.Id, (await new DeadlineRepository(v2Directory).LoadAsync()).Single().Id);
        Assert.IsFalse(File.Exists(Path.Combine(v2Directory, "migration-v2.complete")));
    }

    [TestMethod]
    public async Task ImportOnceAsync_DoesNothingWhenMarkerAlreadyExistsWithoutPrimary()
    {
        using var localAppData = new TestDirectory();
        await WriteLegacyAsync(localAppData, Guid.NewGuid(), "旧数据");
        var v2Directory = localAppData.File("DeadlineWidgetV2");
        Directory.CreateDirectory(v2Directory);
        await File.WriteAllTextAsync(Path.Combine(v2Directory, "migration-v2.complete"), "complete");

        var imported = await new LegacyDeadlineImporter(localAppData.Path).ImportOnceAsync();

        Assert.IsFalse(imported);
        Assert.IsFalse(File.Exists(Path.Combine(v2Directory, "deadlines.json")));
    }

    [TestMethod]
    public async Task ImportOnceAsync_DoesNothingWhenLegacyFileIsAbsent()
    {
        using var localAppData = new TestDirectory();

        var imported = await new LegacyDeadlineImporter(localAppData.Path).ImportOnceAsync();

        Assert.IsFalse(imported);
        Assert.IsFalse(Directory.Exists(localAppData.File("DeadlineWidgetV2")));
    }

    private static async Task WriteLegacyAsync(TestDirectory root, Guid id, string title)
    {
        var legacyDirectory = root.File("CourseDeadlineWidget");
        Directory.CreateDirectory(legacyDirectory);
        var legacy = new
        {
            Version = 1,
            Items = new[]
            {
                new
                {
                    Id = id,
                    Title = title,
                    DueAt = new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Local),
                    IsCompleted = true,
                    CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Local),
                    UpdatedAt = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Local),
                    CompletedAt = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Local)
                }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(legacyDirectory, "deadlines.json"),
            JsonSerializer.Serialize(legacy));
    }
}
