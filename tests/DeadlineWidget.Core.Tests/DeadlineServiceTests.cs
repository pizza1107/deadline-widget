using DeadlineWidget.Core;

namespace DeadlineWidget.Core.Tests;

[TestClass]
public sealed class DeadlineServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 9, 17, 42, DateTimeKind.Local);
    private readonly DeadlineService _service = new();

    [TestMethod]
    public void Create_TrimsTitleAndNormalizesDueMinutesToZero()
    {
        var item = _service.Create("  数据结构  ", new DateTime(2026, 9, 20, 18, 37, 55, DateTimeKind.Local), Now);

        Assert.AreEqual("数据结构", item.Title);
        Assert.AreEqual(new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Local), item.DueAt);
        Assert.AreEqual(Now, item.CreatedAt);
        Assert.AreEqual(Now, item.ModifiedAt);
        Assert.IsFalse(item.IsCompleted);
        Assert.IsNull(item.CompletedAt);
        Assert.AreNotEqual(Guid.Empty, item.Id);
    }

    [TestMethod]
    public void Update_PreservesIdentityAndCreationTime()
    {
        var original = Item("旧标题", Now.AddDays(2), createdAt: Now.AddDays(-2));

        var updated = _service.Update(original, " 新标题 ", Now.AddDays(4).AddMinutes(44), Now.AddHours(1));

        Assert.AreEqual(original.Id, updated.Id);
        Assert.AreEqual(original.CreatedAt, updated.CreatedAt);
        Assert.AreEqual("新标题", updated.Title);
        Assert.AreEqual(new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Local), updated.DueAt);
        Assert.AreEqual(Now.AddHours(1), updated.ModifiedAt);
    }

    [TestMethod]
    public void CompleteAndRestore_MaintainCompletionFields()
    {
        var original = Item("课程", Now.AddDays(2));
        var completed = _service.Complete(original, Now.AddHours(1));
        var restored = _service.Restore(completed, Now.AddHours(2));

        Assert.IsTrue(completed.IsCompleted);
        Assert.AreEqual(Now.AddHours(1), completed.CompletedAt);
        Assert.AreEqual(Now.AddHours(1), completed.ModifiedAt);
        Assert.IsFalse(restored.IsCompleted);
        Assert.IsNull(restored.CompletedAt);
        Assert.AreEqual(Now.AddHours(2), restored.ModifiedAt);
    }

    [TestMethod]
    public void Delete_RemovesOnlyMatchingDeadline()
    {
        var kept = Item("保留", Now.AddDays(1));
        var removed = Item("删除", Now.AddDays(2));

        var result = _service.Delete([kept, removed], removed.Id);

        CollectionAssert.AreEqual(new[] { kept }, result.ToArray());
    }

    [TestMethod]
    public void GetActiveOrdered_FiltersCompletedAndPlacesOverdueAfterActive()
    {
        var activeLater = Item("活动较晚", Now.AddDays(8));
        var overdueOlder = Item("过期较早", Now.AddDays(-5));
        var activeSooner = Item("活动较早", Now.AddDays(2));
        var overdueRecent = Item("过期较近", Now.AddDays(-1));
        var completed = Item("已完成", Now.AddDays(1)) with { IsCompleted = true, CompletedAt = Now };

        var result = _service.GetActiveOrdered(
            [overdueRecent, completed, activeLater, overdueOlder, activeSooner], Now);

        CollectionAssert.AreEqual(
            new[] { activeSooner.Id, activeLater.Id, overdueOlder.Id, overdueRecent.Id },
            result.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void GetCompletedOrdered_FiltersActiveAndUsesNewestCompletionFirst()
    {
        var older = Item("先完成", Now.AddDays(1)) with { IsCompleted = true, CompletedAt = Now.AddHours(-2) };
        var newer = Item("后完成", Now.AddDays(2)) with { IsCompleted = true, CompletedAt = Now.AddHours(-1) };
        var active = Item("活动", Now.AddDays(3));

        var result = _service.GetCompletedOrdered([older, active, newer]);

        CollectionAssert.AreEqual(new[] { newer.Id, older.Id }, result.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void Create_RejectsBlankTitle()
    {
        Assert.ThrowsException<ArgumentException>(() => _service.Create("  ", Now.AddDays(1), Now));
    }

    private static Deadline Item(string title, DateTime dueAt, DateTime? createdAt = null)
    {
        var created = createdAt ?? Now.AddDays(-1);
        return new Deadline(Guid.NewGuid(), title, dueAt, false, created, created, null);
    }
}
