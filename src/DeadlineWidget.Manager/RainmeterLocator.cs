using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DeadlineWidget.Manager;

public interface IRainmeterLocator
{
    string? FindExecutable();
}

internal interface IRainmeterProbe
{
    string? FindRunningExecutable();
    IEnumerable<string> ReadUninstallLocations();
    string GetProgramFiles();
    string GetProgramFilesX86();
    bool FileExists(string path);
}

internal sealed class RainmeterLocator(IRainmeterProbe? probe = null) : IRainmeterLocator
{
    private readonly IRainmeterProbe _probe = probe ?? new RainmeterProbe();

    public string? FindExecutable()
    {
        var candidates = new List<(string? Path, bool IsDirectory)>
        {
            (_probe.FindRunningExecutable(), false)
        };
        candidates.AddRange(_probe.ReadUninstallLocations().Select(path => ((string?)path, true)));
        candidates.Add((_probe.GetProgramFiles(), true));
        candidates.Add((_probe.GetProgramFilesX86(), true));

        foreach (var (candidate, isDirectory) in candidates)
        {
            var executable = Normalize(candidate, isDirectory);
            if (executable is not null && _probe.FileExists(executable))
            {
                return executable;
            }
        }

        return null;
    }

    private static string? Normalize(string? candidate, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            var path = candidate.Trim().Trim('"');
            if (isDirectory)
            {
                path = Path.Combine(path, "Rainmeter.exe");
                if (string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "Rainmeter", StringComparison.OrdinalIgnoreCase) is false)
                {
                    path = Path.Combine(candidate.Trim().Trim('"'), "Rainmeter", "Rainmeter.exe");
                }
            }
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

internal sealed class RainmeterProbe : IRainmeterProbe
{
    public string? FindRunningExecutable()
    {
        foreach (var process in Process.GetProcessesByName("Rainmeter"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                }
            }
        }
        return null;
    }

    public IEnumerable<string> ReadUninstallLocations()
    {
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in views)
            {
                RegistryKey? uninstall = null;
                try
                {
                    uninstall = RegistryKey.OpenBaseKey(hive, view)
                        .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        using var product = uninstall.OpenSubKey(name);
                        var displayName = product?.GetValue("DisplayName") as string;
                        var location = product?.GetValue("InstallLocation") as string;
                        if (displayName?.Contains("Rainmeter", StringComparison.OrdinalIgnoreCase) is true &&
                            !string.IsNullOrWhiteSpace(location))
                        {
                            yield return location;
                        }
                    }
                }
                finally
                {
                    uninstall?.Dispose();
                }
            }
        }
    }

    public string GetProgramFiles() => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public string GetProgramFilesX86() => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    public bool FileExists(string path) => File.Exists(path);
}
