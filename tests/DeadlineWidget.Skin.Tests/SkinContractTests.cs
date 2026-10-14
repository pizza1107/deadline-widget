using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace DeadlineWidget.Skin.Tests;

[TestClass]
public sealed class SkinContractTests
{
    static SkinContractTests()
    {
        UserData.RegisterType<FakeSkin>();
        UserData.RegisterType<FakeMeasure>();
    }

    private const int TestLocalOffsetSeconds = 8 * 3600;
    private static readonly DateTimeOffset TestNow = new(
        2026,
        9,
        16,
        12,
        0,
        0,
        TimeSpan.FromSeconds(TestLocalOffsetSeconds));
    private static readonly long TestNowEpoch = TestNow.ToUnixTimeSeconds();
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SkinRoot = Path.Combine(RepositoryRoot, "rainmeter", "Skins", "DeadlineWidgetV2");
    private static readonly string IniPath = Path.Combine(SkinRoot, "DeadlineWidgetV2.ini");
    private static readonly string RendererPath = Path.Combine(SkinRoot, "@Resources", "DeadlineWidgetV2.lua");
    private static readonly string DecoderPath = Path.Combine(SkinRoot, "@Resources", "json.lua");

    [TestMethod]
    public void SkinFiles_ArePresentAtTheV2PackageBoundary()
    {
        Assert.IsTrue(File.Exists(IniPath), IniPath);
        Assert.IsTrue(File.Exists(RendererPath), RendererPath);
        Assert.IsTrue(File.Exists(DecoderPath), DecoderPath);
    }

    [TestMethod]
    public void Ini_DeclaresExactCanvasFiveRowsMinuteRefreshAndWheelPaging()
    {
        var ini = ReadIni();

        StringAssert.Contains(ini, "Update=60000");
        StringAssert.Contains(ini, "Shape=Rectangle 0,0,240,280,12");
        StringAssert.Contains(ini, "MouseScrollUpAction=[!CommandMeasure MeasureScript \"Page(-1)\"]");
        StringAssert.Contains(ini, "MouseScrollDownAction=[!CommandMeasure MeasureScript \"Page(1)\"]");
        Assert.AreEqual(5, Regex.Matches(ini, @"(?m)^\[MeterRow\dTitle\]$").Count);
        Assert.AreEqual(5, Regex.Matches(ini, @"(?m)^\[MeterRow\dDue\]$").Count);
        Assert.AreEqual(5, Regex.Matches(ini, @"(?m)^\[MeterRow\dCountdown\]$").Count);
        Assert.AreEqual(5, Regex.Matches(ini, @"(?m)^Group=Row\d$").Select(match => match.Value).Distinct().Count());
        for (var index = 1; index <= 5; index++)
        {
            StringAssert.Contains(IniSection(ini, $"MeterRow{index}Title"), "ClipString=1");
        }
    }

    [TestMethod]
    public void Ini_UsesCrispTypographyAndKeepsFiveRowsInsideTheFixedCanvas()
    {
        var ini = ReadIni();

        StringAssert.Contains(ini, "BackgroundColor=255,255,255,248");
        StringAssert.Contains(ini, "NeutralColor=36,42,48,255");
        StringAssert.Contains(ini, "MutedColor=88,96,104,255");

        var header = IniSection(ini, "MeterTitle");
        StringAssert.Contains(header, "FontFace=Segoe UI Semibold");
        StringAssert.Contains(header, "FontSize=15.5");
        StringAssert.Contains(header, "AntiAlias=1");

        var expectedTitleY = new[] { 49, 89, 129, 169, 209 };
        var expectedDueY = new[] { 69, 109, 149, 189, 229 };
        for (var index = 1; index <= 5; index++)
        {
            var title = IniSection(ini, $"MeterRow{index}Title");
            StringAssert.Contains(title, $"Y={expectedTitleY[index - 1]}");
            StringAssert.Contains(title, "H=20");
            StringAssert.Contains(title, "FontFace=Microsoft YaHei UI");
            StringAssert.Contains(title, "FontSize=10.5");
            StringAssert.Contains(title, "FontWeight=600");
            StringAssert.Contains(title, "AntiAlias=1");
            StringAssert.Contains(title, "ClipString=1");

            foreach (var suffix in new[] { "Due", "Countdown" })
            {
                var detail = IniSection(ini, $"MeterRow{index}{suffix}");
                StringAssert.Contains(detail, $"Y={expectedDueY[index - 1]}");
                StringAssert.Contains(detail, "FontFace=Microsoft YaHei UI");
                StringAssert.Contains(detail, "FontSize=8.5");
                StringAssert.Contains(detail, "AntiAlias=1");
            }
        }

        var status = IniSection(ini, "MeterStatus");
        StringAssert.Contains(status, "Y=258");
        StringAssert.Contains(status, "FontFace=Microsoft YaHei UI");
        StringAssert.Contains(status, "FontSize=8.5");
        StringAssert.Contains(status, "ClipString=1");
    }

    [TestMethod]
    public void Ini_AllowsOnlyThePlusToClickAndWritesTheManagerWakeupSignal()
    {
        var ini = ReadIni();

        StringAssert.Contains(ini, "[MeasureBridge]");
        StringAssert.Contains(ini, "Plugin=DeadlineWidgetBridge");
        StringAssert.Contains(ini, "[MeterAdd]");
        StringAssert.Contains(ini, "Text=＋");
        var clickActions = Regex.Matches(ini, @"(?m)^LeftMouse(?:Up|Down)Action=(.*)$");
        Assert.AreEqual(1, clickActions.Count);
        StringAssert.Contains(ini, "[MeasureWakeSignal]");
        Assert.AreEqual("[!UpdateMeasure MeasureWakeSignal][!WriteKeyValue Variables OpenRequest \"[MeasureWakeSignal]\" \"#SETTINGSPATH#DeadlineWidgetV2\\manager-request.ini\"]", clickActions[0].Groups[1].Value);
        for (var index = 1; index <= 5; index++)
        {
            foreach (var suffix in new[] { "Title", "Due", "Countdown" })
            {
                Assert.IsFalse(IniSection(ini, $"MeterRow{index}{suffix}").Contains("LeftMouse", StringComparison.Ordinal));
            }
        }
    }

    [TestMethod]
    public void Ini_AddHitTargetCoversTheRightAlignedPlusAndOwnsTheOnlyClickAction()
    {
        var ini = ReadIni();
        var add = IniSection(ini, "MeterAdd");
        var hitTarget = IniSection(ini, "MeterAddHitTarget");

        StringAssert.Contains(add, "X=224");
        StringAssert.Contains(add, "Y=10");
        StringAssert.Contains(add, "StringAlign=Right");
        StringAssert.Contains(add, "Text=＋");
        Assert.IsFalse(add.Contains("LeftMouse", StringComparison.Ordinal));

        StringAssert.Contains(hitTarget, "Meter=Shape");
        StringAssert.Contains(hitTarget, "X=196");
        StringAssert.Contains(hitTarget, "Y=6");
        StringAssert.Contains(hitTarget, "Shape=Rectangle 0,0,40,36 | Fill Color 0,0,0,1 | StrokeWidth 0");
        StringAssert.Contains(hitTarget, "LeftMouseUpAction=[!UpdateMeasure MeasureWakeSignal][!WriteKeyValue Variables OpenRequest \"[MeasureWakeSignal]\" \"#SETTINGSPATH#DeadlineWidgetV2\\manager-request.ini\"]");
        Assert.IsTrue(
            ini.IndexOf("[MeterAdd]\n", StringComparison.Ordinal) < ini.IndexOf("[MeterAddHitTarget]\n", StringComparison.Ordinal),
            "The transparent hit target must be drawn after the plus so it receives the click.");
    }

    [TestMethod]
    public void Sources_ExcludeForbiddenLaunchesLocationsAndLegacyIdentifiers()
    {
        var allSource = string.Join("\n", ReadIni(), ReadLua(RendererPath), ReadLua(DecoderPath));

        foreach (var forbidden in new[]
                 {
                     "RunCommand", ".exe", "http://", "https://", "C:\\Users\\",
                     "CourseDeadlineWidget", "DeadlineWidget.App", "DeadlineWidgetManager"
                 })
        {
            Assert.IsFalse(allSource.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    [TestMethod]
    public void LuaSources_AreAsciiOnlyAndKeepDecoderSeparateFromRenderer()
    {
        foreach (var path in new[] { RendererPath, DecoderPath })
        {
            var bytes = File.ReadAllBytes(path);
            Assert.IsTrue(bytes.All(value => value <= 0x7f), path);
        }

        var decoder = ReadLua(DecoderPath);
        Assert.IsFalse(decoder.Contains("SKIN", StringComparison.Ordinal));
        Assert.IsFalse(decoder.Contains("io.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Ini_OwnsAllChineseLabelsColorsAndDeterministicTestSettings()
    {
        var ini = ReadIni();

        foreach (var declaration in new[]
                 {
                     "TitleText=Deadlines", "LabelDays=天", "LabelHours=小时", "LabelMinutes=分钟",
                     "LabelOverdue=已过期", "LabelEmpty=暂无截止事项", "LabelError=数据不可用",
                     "LabelBridgeCommandError=命令无效", "LabelBridgeMissing=管理器不可用",
                     "LabelBridgeLaunchError=启动失败", "LabelTotalPrefix=共", "LabelItemUnit=项",
                     "LabelPagePrefix=第", "LabelPageUnit=页", "LabelSeparator=·",
                     "NeutralColor=36,42,48,255",
                     "WarningColor=214,124,0,255", "CriticalColor=196,43,28,255",
                     "OverdueColor=120,120,120,255", "BackgroundColor=255,255,255,248",
                     "TestMode=0", "TestNowEpoch=0", "TestPage=1", "TestResultPath="
                 })
        {
            StringAssert.Contains(ini, declaration);
        }
    }

    [TestMethod]
    public void Ini_ReadsTheSnapshotFromTheStableRainmeterSettingsPath()
    {
        var ini = ReadIni();

        StringAssert.Contains(ini, "SnapshotPath=#SETTINGSPATH#DeadlineWidgetV2\\display.json");
        Assert.IsFalse(ini.Contains("SnapshotPath=#LOCALAPPDATA#", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow(0, 1, 0, 1, 1)]
    [DataRow(3, 1, 3, 1, 1)]
    [DataRow(5, 1, 5, 1, 1)]
    [DataRow(6, 2, 1, 2, 2)]
    [DataRow(11, 1, 5, 1, 3)]
    [DataRow(11, 3, 1, 3, 3)]
    public void Renderer_PaginatesFixtureCountsByFive(int itemCount, int requestedPage, int visibleCount, int page, int pages)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(index => Item($"Item {index:00}", TestNow.AddDays(index)))
            .ToArray();

        var result = Render(Snapshot(items), requestedPage);

        Assert.AreEqual(itemCount == 0 ? "empty" : "ok", Text(result, "status"));
        Assert.AreEqual(itemCount, Number(result, "total"));
        Assert.AreEqual(page, Number(result, "page"));
        Assert.AreEqual(pages, Number(result, "pages"));
        Assert.AreEqual(visibleCount, Rows(result).Count);
        if (itemCount > 0)
        {
            Assert.AreEqual($"共 {itemCount} 项 · 第 {page}/{pages} 页", Text(result, "footer"));
        }
        else
        {
            Assert.AreEqual("暂无截止事项", Text(result, "footer"));
        }
    }

    [TestMethod]
    public void Renderer_FiltersCompletedThenSortsFutureBeforeAscendingOverdue()
    {
        var json = Snapshot(
            Item("Overdue recent", TestNow.AddHours(-1)),
            Item("Future later", TestNow.AddDays(2)),
            Item("Completed", TestNow.AddMinutes(5), completed: true),
            Item("Overdue old", TestNow.AddDays(-4)),
            Item("Future soon", TestNow.AddHours(1)));

        var titles = Rows(Render(json)).Select(row => Text(row, "title")).ToArray();

        CollectionAssert.AreEqual(
            new[] { "Future soon", "Future later", "Overdue old", "Overdue recent" },
            titles);
    }

    [TestMethod]
    public void Renderer_AppliesExactUrgencyBoundariesAndCountdownFormats()
    {
        var json = Snapshot(
            Item("Neutral", TestNow.AddDays(10).AddMinutes(1)),
            Item("Warning 10", TestNow.AddDays(10)),
            Item("Warning 3 plus", TestNow.AddDays(3).AddMinutes(1)),
            Item("Critical 3", TestNow.AddDays(3)),
            Item("Hours", TestNow.AddHours(5).AddMinutes(30)),
            Item("Overdue", TestNow.AddSeconds(-1)));

        var firstPage = Rows(Render(json, 1));
        var secondPage = Rows(Render(json, 2));

        AssertRow(firstPage[0], "Hours", "CriticalColor", "5 小时 30 分钟");
        AssertRow(firstPage[1], "Critical 3", "CriticalColor", "3 天");
        AssertRow(firstPage[2], "Warning 3 plus", "WarningColor", "4 天");
        AssertRow(firstPage[3], "Warning 10", "WarningColor", "10 天");
        AssertRow(firstPage[4], "Neutral", "NeutralColor", "11 天");
        AssertRow(secondPage[0], "Overdue", "OverdueColor", "已过期");
    }

    [TestMethod]
    public void Renderer_FormatsDueTimeInLocalTimeAndPreservesLongTitlesForIniClipping()
    {
        const string longTitle = "A very long title that must be clipped by the Rainmeter meter";
        var due = TestNow.AddDays(1).AddHours(2).AddMinutes(3);

        var row = Rows(Render(Snapshot(Item(longTitle, due))))[0];

        Assert.AreEqual(longTitle, Text(row, "title"));
        Assert.AreEqual(due.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), Text(row, "due"));
    }

    [DataTestMethod]
    [DataRow(2, "命令无效")]
    [DataRow(3, "管理器不可用")]
    [DataRow(4, "启动失败")]
    public void Renderer_SurfacesBridgeErrorsWithoutReplacingThemWithTheFooter(int bridgeStatus, string expected)
    {
        var result = Render(Snapshot(Item("Future", TestNow.AddDays(1))), bridgeStatus: bridgeStatus);

        Assert.AreEqual("bridge-error", Text(result, "status"));
        Assert.AreEqual(expected, Text(result, "footer"));
        Assert.AreEqual(1, Number(result, "total"));
        Assert.AreEqual(1, Rows(result).Count);
    }

    [TestMethod]
    public void TestMode_SurfacesBridgeErrorEvenWhenSnapshotIsMissing()
    {
        using var directory = new TestDirectory();
        var resultPath = Path.Combine(directory.Path, "result.txt");
        var script = LoadLua();
        var skin = new FakeSkin(Path.Combine(directory.Path, "missing.json"), resultPath, TestNowEpoch, 1, 4);
        script.Globals["SKIN"] = skin;

        script.Call(script.Globals.Get("Initialize"));
        script.Call(script.Globals.Get("Update"));

        StringAssert.Contains(File.ReadAllText(resultPath), "status=bridge-error");
        Assert.AreEqual("启动失败", skin.Variables["StatusText"]);
    }

    [TestMethod]
    public void ProductionMode_ReadsTheExpandedSettingsPathWithoutConsultingTheEnvironment()
    {
        using var directory = new TestDirectory();
        var snapshotPath = Path.Combine(directory.Path, "Rainmeter", "DeadlineWidgetV2", "display.json");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        File.WriteAllText(snapshotPath, Snapshot(Item("Future", TestNow.AddDays(1))), Encoding.UTF8);

        var script = LoadLua();
        var environmentReadCount = 0;
        ConfigureEnvironmentReader(script, _ =>
        {
            environmentReadCount++;
            return DynValue.Nil;
        });
        var skin = new FakeSkin(snapshotPath, string.Empty, TestNowEpoch, 1);
        skin.Variables["TestMode"] = "0";
        script.Globals["SKIN"] = skin;

        script.Call(script.Globals.Get("Initialize"));
        script.Call(script.Globals.Get("Update"));

        Assert.AreEqual("Future", skin.Variables["Row1Title"]);
        Assert.AreEqual("共 1 项 · 第 1/1 页", skin.Variables["StatusText"]);
        Assert.AreEqual(0, environmentReadCount);
    }

    [TestMethod]
    public void TestMode_UsesAnExplicitMissingSnapshotPathAndReportsDataUnavailable()
    {
        using var directory = new TestDirectory();
        var snapshotPath = Path.Combine(directory.Path, "Rainmeter", "DeadlineWidgetV2", "display.json");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        File.WriteAllText(snapshotPath, Snapshot(Item("Must not render", TestNow.AddDays(1))), Encoding.UTF8);

        var script = LoadLua();
        ConfigureEnvironmentReader(script, _ => DynValue.NewString(directory.Path));
        var skin = new FakeSkin(Path.Combine(directory.Path, "missing.json"), Path.Combine(directory.Path, "result.txt"), TestNowEpoch, 1);
        script.Globals["SKIN"] = skin;

        script.Call(script.Globals.Get("Initialize"));
        script.Call(script.Globals.Get("Update"));

        StringAssert.Contains(File.ReadAllText(skin.Variables["TestResultPath"]), "status=error");
        Assert.AreEqual("数据不可用", skin.Variables["StatusText"]);
        Assert.IsFalse(skin.Variables.ContainsKey("Row1Title"));
    }

    [DataTestMethod]
    [DataRow(2, "\u547d\u4ee4\u65e0\u6548")]
    [DataRow(3, "\u7ba1\u7406\u5668\u4e0d\u53ef\u7528")]
    [DataRow(4, "\u542f\u52a8\u5931\u8d25")]
    public void TestMode_PreservesBridgeStatusWhenAnOtherwiseValidSnapshotRenders(int bridgeStatus, string expected)
    {
        using var directory = new TestDirectory();
        var snapshotPath = Path.Combine(directory.Path, "fixture.json");
        var resultPath = Path.Combine(directory.Path, "result.txt");
        File.WriteAllText(snapshotPath, Snapshot(Item("Future", TestNow.AddDays(1))), Encoding.UTF8);
        var script = LoadLua();
        var skin = new FakeSkin(snapshotPath, resultPath, TestNowEpoch, 1, bridgeStatus);
        script.Globals["SKIN"] = skin;

        script.Call(script.Globals.Get("Initialize"));
        script.Call(script.Globals.Get("Update"));

        StringAssert.Contains(File.ReadAllText(resultPath), "status=bridge-error");
        Assert.AreEqual(expected, skin.Variables["StatusText"]);
    }

    [DataTestMethod]
    [DataRow(0, 1, 2)]
    [DataRow(99, -1, 2)]
    public void TestMode_NormalizesBoundaryPageWhenBridgeErrorOverlaysValidContent(int initialPage, int delta, int expectedPage)
    {
        using var directory = new TestDirectory();
        var snapshotPath = Path.Combine(directory.Path, "fixture.json");
        var resultPath = Path.Combine(directory.Path, "result.txt");
        File.WriteAllText(
            snapshotPath,
            Snapshot(Enumerable.Range(1, 11).Select(index => Item($"Item {index:00}", TestNow.AddDays(index))).ToArray()),
            Encoding.UTF8);
        var script = LoadLua();
        var skin = new FakeSkin(snapshotPath, resultPath, TestNowEpoch, initialPage, bridgeStatus: 3);
        script.Globals["SKIN"] = skin;

        script.Call(script.Globals.Get("Initialize"));
        script.Call(script.Globals.Get("Update"));
        StringAssert.Contains(File.ReadAllText(resultPath), "status=bridge-error");

        skin.SetBridgeStatus(0);
        script.Call(script.Globals.Get("Page"), (double)delta);

        StringAssert.Contains(File.ReadAllText(resultPath), $"page={expectedPage}");
        Assert.AreEqual($"共 11 项 · 第 {expectedPage}/3 页", skin.Variables["StatusText"]);
    }

    [DataTestMethod]
    [DataRow("{\"version\":2,\"generatedAt\":\"2026-09-16T12:00:00\",\"items\":[]}")]
    [DataRow("{\"version\":1,\"generatedAt\":\"2026-09-16T12:00:00\",\"items\":{}}")]
    [DataRow("{\"version\":1,\"generatedAt\":\"2026-09-16T12:00:00\",\"items\":[null]}")]
    [DataRow("{\"version\":1,\"generatedAt\":\"2026-09-16T12:00:00\",\"items\":[{\"id\":3,\"title\":\"Bad\",\"dueAt\":\"x\",\"isCompleted\":false}]}")]
    public void Renderer_RejectsWrongSchemaVersionsAndTypes(string json)
    {
        var result = Render(json);

        Assert.AreEqual("error", Text(result, "status"));
        Assert.AreEqual("数据不可用", Text(result, "footer"));
        Assert.AreEqual(0, Rows(result).Count);
    }

    [DataTestMethod]
    [DataRow("2026-02-29T12:00:00Z")]
    [DataRow("2026-02-30T12:00:00Z")]
    [DataRow("2026-04-31T12:00:00+08:00")]
    public void Renderer_RejectsImpossibleCalendarDates(string dueAt)
    {
        var result = Render(Snapshot(ItemWithDueText("Impossible", dueAt)));

        Assert.AreEqual("error", Text(result, "status"));
        Assert.AreEqual("数据不可用", Text(result, "footer"));
        Assert.AreEqual(0, Rows(result).Count);
    }

    [TestMethod]
    public void Decoder_HandlesEscapesNumbersBooleansNullAndNestedValues()
    {
        var script = LoadLua();
        var decoder = script.Globals.Get("DeadlineWidgetJson").Table;

        var decoded = script.Call(decoder.Get("decode"), "{\"text\":\"line\\n\\u0041\",\"number\":-12.5e2,\"flag\":true,\"none\":null,\"nested\":[1,{\"x\":\"y\"}]}").Table;

        Assert.AreEqual("line\nA", Text(decoded, "text"));
        Assert.AreEqual(-1250d, decoded.Get("number").Number);
        Assert.IsTrue(decoded.Get("flag").Boolean);
        Assert.AreEqual(DataType.Nil, decoded.Get("none").Type);
        Assert.AreEqual("y", decoded.Get("nested").Table.Get(2).Table.Get("x").String);
    }

    [TestMethod]
    public void Decoder_DecodesUnicodeEscapesAndSurrogatePairsInAsciiOnlySource()
    {
        var script = LoadLua();
        var decoder = script.Globals.Get("DeadlineWidgetJson").Table;

        var decoded = script.Call(
            decoder.Get("decode"),
            "{\"title\":\"\\u4e2d\\u6587\\ud83d\\ude00\"}").Table;

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("\u4e2d\u6587\ud83d\ude00"),
            Encoding.Latin1.GetBytes(Text(decoded, "title")));
    }

    [TestMethod]
    public void Renderer_ReadsBomMarkedUtf8SnapshotWithoutLosingUnicode()
    {
        const string title = "课程 😀 𠮷 café";
        var utf8Bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(Snapshot(Item(title, TestNow.AddDays(1)))))
            .ToArray();
        var luaByteString = Encoding.Latin1.GetString(utf8Bytes);

        var row = Rows(Render(luaByteString))[0];

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes(title),
            Encoding.Latin1.GetBytes(Text(row, "title")));
    }

    [TestMethod]
    public void Renderer_DecodesUnicodeEscapesFromUtf8Snapshot()
    {
        const string json = "{\"version\":1,\"generatedAt\":\"2026-09-16T12:00:00+08:00\",\"items\":[" +
            "{\"id\":\"00000000-0000-0000-0000-000000000002\",\"title\":\"\\u8BFE\\u7A0B \\uD83D\\uDE00\"," +
            "\"dueAt\":\"2026-09-17T12:00:00+08:00\",\"isCompleted\":false}]}";

        var row = Rows(Render(json))[0];

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("课程 😀"),
            Encoding.Latin1.GetBytes(Text(row, "title")));
    }

    [TestMethod]
    public void Renderer_ConvertsExplicitDueTimesInAControlledDaylightSavingZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var expected = TimeZoneInfo.ConvertTime(
                DateTimeOffset.Parse("2026-07-15T15:30:00Z", CultureInfo.InvariantCulture),
                zone)
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.AreEqual(TimeSpan.FromHours(-4), zone.GetUtcOffset(new DateTime(2026, 7, 15)));

        var result = RenderInControlledTimeZone(
            Snapshot(
                ItemWithDueText("Zulu", "2026-07-15T15:30:00Z"),
                ItemWithDueText("Foreign", "2026-07-15T17:30:00+02:00")),
            zone);

        CollectionAssert.AreEqual(new[] { expected, expected }, Rows(result).Select(row => Text(row, "due")).ToArray());
    }

    [TestMethod]
    public void TestMode_UsesExplicitSnapshotClockPageAndResultPathWithoutPageDrift()
    {
        using var directory = new TestDirectory();
        var snapshotPath = Path.Combine(directory.Path, "fixture.json");
        var resultPath = Path.Combine(directory.Path, "result.txt");
        File.WriteAllText(
            snapshotPath,
            Snapshot(Enumerable.Range(1, 11).Select(index => Item($"Item {index:00}", TestNow.AddDays(index))).ToArray()),
            Encoding.UTF8);
        var script = LoadLua();
        var skin = new FakeSkin(snapshotPath, resultPath, TestNowEpoch, 3);
        script.Globals["SKIN"] = skin;

        script.Call(script.Globals.Get("Initialize"));
        script.Call(script.Globals.Get("Update"));
        StringAssert.Contains(File.ReadAllText(resultPath), "page=3");

        script.Call(script.Globals.Get("Page"), 1d);
        StringAssert.Contains(File.ReadAllText(resultPath), "page=3");

        script.Call(script.Globals.Get("Page"), -1d);
        StringAssert.Contains(File.ReadAllText(resultPath), "page=2");
        Assert.AreEqual("共 11 项 · 第 2/3 页", skin.Variables["StatusText"]);
    }

    private static void AssertRow(Table row, string title, string color, string countdown)
    {
        Assert.AreEqual(title, Text(row, "title"));
        Assert.AreEqual(color, Text(row, "color"));
        Assert.AreEqual(countdown, Text(row, "countdown"));
    }

    private static Table Render(string json, int page = 1, int bridgeStatus = 0, int localOffsetSeconds = TestLocalOffsetSeconds)
    {
        var script = LoadLua();
        var renderer = script.Globals.Get("DeadlineWidgetV2").Table;
        var result = script.Call(
            renderer.Get("TestRender"),
            json,
            (double)TestNowEpoch,
            (double)page,
            Labels(script),
            (double)bridgeStatus,
            (double)localOffsetSeconds);
        return result.Table;
    }

    private static Table RenderInControlledTimeZone(string json, TimeZoneInfo zone)
    {
        var script = LoadLua();
        ConfigureControlledTimeZone(script, zone);
        var renderer = script.Globals.Get("DeadlineWidgetV2").Table;
        var result = script.Call(
            renderer.Get("TestRender"),
            json,
            (double)TestNowEpoch,
            1d,
            Labels(script),
            0d,
            DynValue.Nil);
        return result.Table;
    }

    private static void ConfigureControlledTimeZone(Script script, TimeZoneInfo zone)
    {
        var os = script.Globals.Get("os").Table;
        os.Set("time", DynValue.NewCallback((_, arguments) => ControlledTime(zone, arguments)));
        os.Set("date", DynValue.NewCallback((_, arguments) => ControlledDate(script, zone, arguments)));
        os.Set("difftime", DynValue.NewCallback((_, arguments) =>
            DynValue.NewNumber(arguments[0].Number - arguments[1].Number)));
    }

    private static void ConfigureEnvironmentReader(Script script, Func<string, DynValue> readEnvironment)
    {
        var os = script.Globals.Get("os").Table;
        os.Set("getenv", DynValue.NewCallback((_, arguments) =>
            readEnvironment(arguments[0].String)));
    }

    private static DynValue ControlledTime(TimeZoneInfo zone, CallbackArguments arguments)
    {
        var value = arguments[0].Table;
        var local = new DateTime(
            checked((int)value.Get("year").Number),
            checked((int)value.Get("month").Number),
            checked((int)value.Get("day").Number),
            checked((int)value.Get("hour").Number),
            checked((int)value.Get("min").Number),
            checked((int)value.Get("sec").Number),
            DateTimeKind.Unspecified);
        var isDaylightSaving = value.Get("isdst");
        DateTimeOffset instant;
        if (isDaylightSaving.Type == DataType.Boolean && !isDaylightSaving.Boolean)
        {
            instant = new DateTimeOffset(local, zone.BaseUtcOffset);
        }
        else
        {
            instant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        }
        return DynValue.NewNumber(instant.ToUnixTimeSeconds());
    }

    private static DynValue ControlledDate(Script script, TimeZoneInfo zone, CallbackArguments arguments)
    {
        var format = arguments[0].String;
        var instant = DateTimeOffset.FromUnixTimeSeconds(checked((long)arguments[1].Number));
        var local = format == "!*t" ? instant.ToUniversalTime() : TimeZoneInfo.ConvertTime(instant, zone);
        var table = new Table(script)
        {
            ["year"] = local.Year,
            ["month"] = local.Month,
            ["day"] = local.Day,
            ["hour"] = local.Hour,
            ["min"] = local.Minute,
            ["sec"] = local.Second,
            ["isdst"] = format != "!*t" && zone.IsDaylightSavingTime(local)
        };
        return DynValue.NewTable(table);
    }

    private static Script LoadLua()
    {
        var script = new Script(CoreModules.Preset_Complete);
        script.DoString(ReadLua(DecoderPath), codeFriendlyName: "json.lua");
        script.DoString(ReadLua(RendererPath), codeFriendlyName: "DeadlineWidgetV2.lua");
        return script;
    }

    private static Table Labels(Script script)
    {
        var labels = new Table(script)
        {
            ["days"] = "天",
            ["hours"] = "小时",
            ["minutes"] = "分钟",
            ["overdue"] = "已过期",
            ["empty"] = "暂无截止事项",
            ["error"] = "数据不可用",
            ["bridgeCommandError"] = "命令无效",
            ["bridgeMissing"] = "管理器不可用",
            ["bridgeLaunchError"] = "启动失败",
            ["totalPrefix"] = "共",
            ["itemUnit"] = "项",
            ["pagePrefix"] = "第",
            ["pageUnit"] = "页",
            ["separator"] = "·"
        };
        return labels;
    }

    private static IReadOnlyList<Table> Rows(Table result)
    {
        var rows = result.Get("rows").Table;
        return rows.Values.Select(value => value.Table).ToArray();
    }

    private static string Text(Table table, string key) => table.Get(key).String;
    private static int Number(Table table, string key) => checked((int)table.Get(key).Number);

    private static string Snapshot(params object[] items) => JsonSerializer.Serialize(new
    {
        version = 1,
        generatedAt = TestNow.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        items
    });

    private static object Item(string title, DateTimeOffset dueAt, bool completed = false) => new
    {
        id = Guid.NewGuid().ToString(),
        title,
        dueAt = dueAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        countdown = "publisher hint ignored",
        urgency = "Neutral",
        isOverdue = dueAt < TestNow,
        isCompleted = completed
    };

    private static object ItemWithDueText(string title, string dueAt) => new
    {
        id = Guid.NewGuid().ToString(),
        title,
        dueAt,
        countdown = "publisher hint ignored",
        urgency = "Neutral",
        isOverdue = false,
        isCompleted = false
    };

    private static string ReadIni() => File.ReadAllText(IniPath, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal);
    private static string ReadLua(string path) => File.ReadAllText(path, Encoding.ASCII).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string IniSection(string ini, string sectionName)
    {
        var match = Regex.Match(ini, $@"(?ms)^\[{Regex.Escape(sectionName)}\]\n(?<body>.*?)(?=^\[|\z)");
        Assert.IsTrue(match.Success, sectionName);
        return match.Groups["body"].Value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DeadlineWidgetV2.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    public sealed class FakeSkin
    {
        private readonly FakeMeasure _measure;

        public FakeSkin(string snapshotPath, string resultPath, long nowEpoch, int page, int bridgeStatus = 0)
        {
            _measure = new FakeMeasure(bridgeStatus);
            Variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["@"] = Path.GetDirectoryName(RendererPath)! + Path.DirectorySeparatorChar,
                ["SnapshotPath"] = snapshotPath,
                ["TestMode"] = "1",
                ["TestNowEpoch"] = nowEpoch.ToString(CultureInfo.InvariantCulture),
                ["TestPage"] = page.ToString(CultureInfo.InvariantCulture),
                ["TestResultPath"] = resultPath,
                ["LabelDays"] = "天",
                ["LabelHours"] = "小时",
                ["LabelMinutes"] = "分钟",
                ["LabelOverdue"] = "已过期",
                ["LabelEmpty"] = "暂无截止事项",
                ["LabelError"] = "数据不可用",
                ["LabelBridgeCommandError"] = "命令无效",
                ["LabelBridgeMissing"] = "管理器不可用",
                ["LabelBridgeLaunchError"] = "启动失败",
                ["LabelTotalPrefix"] = "共",
                ["LabelItemUnit"] = "项",
                ["LabelPagePrefix"] = "第",
                ["LabelPageUnit"] = "页",
                ["LabelSeparator"] = "·"
            };
        }

        public Dictionary<string, string> Variables { get; }

        public string GetVariable(string name) => Variables.TryGetValue(name, out var value) ? value : string.Empty;
        public FakeMeasure GetMeasure(string name) => _measure;
        public void SetBridgeStatus(double value) => _measure.SetValue(value);

        public void Bang(string command, string argument)
        {
        }

        public void Bang(string command, string first, string second)
        {
            if (command == "!SetVariable")
            {
                Variables[first] = second;
            }
        }
    }

    public sealed class FakeMeasure
    {
        private double _value;

        public FakeMeasure(double value)
        {
            _value = value;
        }

        public double GetValue() => _value;
        public void SetValue(double value) => _value = value;
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeadlineWidget.Skin.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
