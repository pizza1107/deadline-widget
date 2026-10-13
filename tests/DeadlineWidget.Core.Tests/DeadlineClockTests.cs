using DeadlineWidget.Core;

namespace DeadlineWidget.Core.Tests;

[TestClass]
public sealed class DeadlineClockTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 9, 0, 0, DateTimeKind.Local);

    [DataTestMethod]
    [DataRow(10, DeadlineUrgency.Warning)]
    [DataRow(3, DeadlineUrgency.Critical)]
    public void Describe_UsesInclusiveUrgencyBoundaries(int days, DeadlineUrgency expected)
    {
        var state = new DeadlineClock().Describe(Item(Now.AddDays(days)), Now);

        Assert.AreEqual(expected, state.Urgency);
        Assert.IsFalse(state.IsOverdue);
        Assert.AreEqual($"剩余 {days} 天", state.Countdown);
    }

    [TestMethod]
    public void Describe_UsesNeutralBeyondTenDays()
    {
        var state = new DeadlineClock().Describe(Item(Now.AddDays(10).AddMinutes(1)), Now);

        Assert.AreEqual(DeadlineUrgency.Neutral, state.Urgency);
    }

    [TestMethod]
    public void Describe_MarksPastDueAsOverdue()
    {
        var state = new DeadlineClock().Describe(Item(Now.AddTicks(-1)), Now);

        Assert.AreEqual(new DeadlineState("已过期", DeadlineUrgency.Overdue, true), state);
    }

    [TestMethod]
    public void Describe_ShowsHoursAndMinutesInsideOneDay()
    {
        var state = new DeadlineClock().Describe(Item(Now.AddHours(2).AddMinutes(15)), Now);

        Assert.AreEqual("剩余 2 小时 15 分钟", state.Countdown);
    }

    private static Deadline Item(DateTime dueAt) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "测试", dueAt, false, Now, Now, null);
}
