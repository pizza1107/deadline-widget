using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class EditorViewModelTests
{
    [TestMethod]
    public void TryBuild_RejectsBlankTitle()
    {
        var editor = ValidEditor();
        editor.Title = "  ";

        Assert.IsFalse(editor.TryBuild(out _, out _));
        StringAssert.Contains(editor.ValidationMessage, "标题");
    }

    [TestMethod]
    public void TryBuild_RejectsMissingDate()
    {
        var editor = ValidEditor();
        editor.DueDate = null;

        Assert.IsFalse(editor.TryBuild(out _, out _));
        StringAssert.Contains(editor.ValidationMessage, "日期");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("24")]
    [DataRow("-1")]
    [DataRow("午")]
    public void TryBuild_RejectsMissingOrInvalidHour(string hour)
    {
        var editor = ValidEditor();
        editor.Hour = hour;

        Assert.IsFalse(editor.TryBuild(out _, out _));
        StringAssert.Contains(editor.ValidationMessage, "小时");
    }

    [DataTestMethod]
    [DataRow("60")]
    [DataRow("-1")]
    [DataRow("半")]
    public void TryBuild_RejectsInvalidMinute(string minute)
    {
        var editor = ValidEditor();
        editor.Minute = minute;

        Assert.IsFalse(editor.TryBuild(out _, out _));
        StringAssert.Contains(editor.ValidationMessage, "分钟");
    }

    [TestMethod]
    public void TryBuild_EmptyMinuteNormalizesToZeroAndProducesLocalTime()
    {
        var editor = ValidEditor();
        editor.Title = "  数据结构  ";
        editor.Minute = "";

        Assert.IsTrue(editor.TryBuild(out var title, out var dueAt));
        Assert.AreEqual("数据结构", title);
        Assert.AreEqual(new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Local), dueAt);
        Assert.IsNull(editor.ValidationMessage);
    }

    private static EditorViewModel ValidEditor() => new()
    {
        Title = "数据结构",
        DueDate = new DateTime(2026, 9, 20),
        Hour = "18",
        Minute = "30"
    };
}
