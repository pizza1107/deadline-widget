using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class RainmeterLocatorTests
{
    [TestMethod]
    public void FindExecutable_PrefersRunningProcessThenRegistryThenStandardFolders()
    {
        var probe = new FakeRainmeterProbe
        {
            RunningExecutable = @"D:\Portable\Rainmeter.exe",
            ProgramFiles = @"C:\Program Files",
            ProgramFilesX86 = @"C:\Program Files (x86)"
        };
        probe.RegistryLocations.Add(@"E:\Apps\Rainmeter");
        probe.ExistingFiles.UnionWith([
            @"D:\Portable\Rainmeter.exe",
            @"E:\Apps\Rainmeter\Rainmeter.exe",
            @"C:\Program Files\Rainmeter\Rainmeter.exe"
        ]);

        Assert.AreEqual(@"D:\Portable\Rainmeter.exe", new RainmeterLocator(probe).FindExecutable());
    }

    [TestMethod]
    public void FindExecutable_UsesRegistryInstallLocationWhenNoRunningProcessExists()
    {
        var probe = new FakeRainmeterProbe();
        probe.RegistryLocations.Add(@"E:\Apps\Rainmeter");
        probe.ExistingFiles.Add(@"E:\Apps\Rainmeter\Rainmeter.exe");

        Assert.AreEqual(@"E:\Apps\Rainmeter\Rainmeter.exe", new RainmeterLocator(probe).FindExecutable());
    }

    [TestMethod]
    public void FindExecutable_UsesProgramFilesBeforeProgramFilesX86()
    {
        var probe = new FakeRainmeterProbe
        {
            ProgramFiles = @"C:\Program Files",
            ProgramFilesX86 = @"C:\Program Files (x86)"
        };
        probe.ExistingFiles.UnionWith([
            @"C:\Program Files\Rainmeter\Rainmeter.exe",
            @"C:\Program Files (x86)\Rainmeter\Rainmeter.exe"
        ]);

        Assert.AreEqual(@"C:\Program Files\Rainmeter\Rainmeter.exe", new RainmeterLocator(probe).FindExecutable());
    }

    [TestMethod]
    public void FindExecutable_FallsBackToProgramFilesX86()
    {
        var probe = new FakeRainmeterProbe { ProgramFilesX86 = @"C:\Program Files (x86)" };
        probe.ExistingFiles.Add(@"C:\Program Files (x86)\Rainmeter\Rainmeter.exe");

        Assert.AreEqual(@"C:\Program Files (x86)\Rainmeter\Rainmeter.exe", new RainmeterLocator(probe).FindExecutable());
    }

    [TestMethod]
    public void FindExecutable_SkipsInvalidCandidatesAndReturnsNullWhenNothingExists()
    {
        var probe = new FakeRainmeterProbe
        {
            RunningExecutable = "\0invalid",
            ProgramFiles = "",
            ProgramFilesX86 = " "
        };
        probe.RegistryLocations.Add("::invalid::");

        Assert.IsNull(new RainmeterLocator(probe).FindExecutable());
    }

    private sealed class FakeRainmeterProbe : IRainmeterProbe
    {
        public string? RunningExecutable { get; set; }
        public List<string> RegistryLocations { get; } = [];
        public string ProgramFiles { get; set; } = "";
        public string ProgramFilesX86 { get; set; } = "";
        public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? FindRunningExecutable() => RunningExecutable;
        public IEnumerable<string> ReadUninstallLocations() => RegistryLocations;
        public string GetProgramFiles() => ProgramFiles;
        public string GetProgramFilesX86() => ProgramFilesX86;
        public bool FileExists(string path) => ExistingFiles.Contains(path);
    }
}
