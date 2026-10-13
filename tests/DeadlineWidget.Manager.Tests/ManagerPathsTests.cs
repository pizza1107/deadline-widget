using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class ManagerPathsTests
{
    [TestMethod]
    public void FromAppDataRoots_SeparatesAuthoritativeDataFromTheRainmeterSnapshot()
    {
        var localBase = Path.Combine(Path.GetTempPath(), "manager-paths-local", Guid.NewGuid().ToString("N"));
        var roamingBase = Path.Combine(Path.GetTempPath(), "manager-paths-roaming", Guid.NewGuid().ToString("N"));

        var paths = ManagerPaths.FromAppDataRoots(localBase, roamingBase);

        Assert.AreEqual(Path.Combine(localBase, "DeadlineWidgetV2"), paths.RepositoryDirectory);
        Assert.AreEqual(
            Path.Combine(roamingBase, "Rainmeter", "DeadlineWidgetV2"),
            paths.SnapshotDirectory);
        Assert.AreEqual(
            Path.Combine(roamingBase, "Rainmeter", "DeadlineWidgetV2", "manager-request.ini"),
            paths.WakeupFilePath);
        Assert.IsFalse(paths.SnapshotDirectory.StartsWith(localBase, StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("relative-local")]
    public void FromAppDataRoots_RejectsUnsafeLocalApplicationDataBase(string? localBase)
    {
        var roamingBase = Path.Combine(Path.GetTempPath(), "manager-paths-roaming", Guid.NewGuid().ToString("N"));

        var exception = Assert.ThrowsException<ArgumentException>(
            () => ManagerPaths.FromAppDataRoots(localBase, roamingBase));

        Assert.AreEqual("localApplicationDataBase", exception.ParamName);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("relative-roaming")]
    public void FromAppDataRoots_RejectsUnsafeRoamingApplicationDataBase(string? roamingBase)
    {
        var localBase = Path.Combine(Path.GetTempPath(), "manager-paths-local", Guid.NewGuid().ToString("N"));

        var exception = Assert.ThrowsException<ArgumentException>(
            () => ManagerPaths.FromAppDataRoots(localBase, roamingBase));

        Assert.AreEqual("roamingApplicationDataBase", exception.ParamName);
    }
}
