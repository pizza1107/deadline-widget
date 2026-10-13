using System.IO;

namespace DeadlineWidget.Manager;

internal sealed record ManagerPaths(string RepositoryDirectory, string SnapshotDirectory, string WakeupFilePath)
{
    internal static ManagerPaths FromAppDataRoots(
        string? localApplicationDataBase,
        string? roamingApplicationDataBase)
    {
        var snapshotDirectory = SnapshotDirectoryFrom(roamingApplicationDataBase);
        return new(
            RepositoryDirectoryFrom(localApplicationDataBase),
            snapshotDirectory,
            Path.Combine(snapshotDirectory, "manager-request.ini"));
    }

    internal static string SnapshotDirectoryFrom(string? roamingApplicationDataBase) => Path.Combine(
        RequireAbsoluteBase(roamingApplicationDataBase, nameof(roamingApplicationDataBase)),
        "Rainmeter",
        "DeadlineWidgetV2");

    private static string RepositoryDirectoryFrom(string? localApplicationDataBase) => Path.Combine(
        RequireAbsoluteBase(localApplicationDataBase, nameof(localApplicationDataBase)),
        "DeadlineWidgetV2");

    private static string RequireAbsoluteBase(string? path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Application data base must be an absolute path.", parameterName);
        }

        return path;
    }
}
