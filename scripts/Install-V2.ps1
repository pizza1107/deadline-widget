[CmdletBinding()]
param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [string]$LocalAppDataRoot = $env:LOCALAPPDATA,
    [string]$SkinInstallerPath,
    [string]$SkinRoot,
    [string]$PluginRoot,
    [string]$StartupRoot
)

$ErrorActionPreference = 'Stop'

function New-UnicodeShortcut {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$TargetPath,
        [string]$Arguments,
        [string]$WorkingDirectory,
        [string]$Description
    )

    if (-not ('DeadlineWidget.Installation.ShortcutWriter' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DeadlineWidget.Installation
{
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkObject
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maximumPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription(IntPtr name, int maximumName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr directory, int maximumPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maximumPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(IntPtr iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    public static class ShortcutWriter
    {
        public static void Create(string shortcutPath, string targetPath, string arguments, string workingDirectory, string description)
        {
            object instance = new ShellLinkObject();
            try
            {
                IShellLinkW shortcut = (IShellLinkW)instance;
                shortcut.SetPath(targetPath);
                shortcut.SetArguments(arguments ?? string.Empty);
                shortcut.SetWorkingDirectory(workingDirectory ?? string.Empty);
                shortcut.SetDescription(description ?? string.Empty);
                ((IPersistFile)instance).Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }

        public static string ReadTarget(string shortcutPath)
        {
            return Read(shortcutPath, false);
        }

        public static string ReadArguments(string shortcutPath)
        {
            return Read(shortcutPath, true);
        }

        private static string Read(string shortcutPath, bool arguments)
        {
            object instance = new ShellLinkObject();
            try
            {
                ((IPersistFile)instance).Load(shortcutPath, 0);
                IShellLinkW shortcut = (IShellLinkW)instance;
                StringBuilder value = new StringBuilder(32768);
                if (arguments)
                    shortcut.GetArguments(value, value.Capacity);
                else
                    shortcut.GetPath(value, value.Capacity, IntPtr.Zero, 0);
                return value.ToString();
            }
            finally
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
    }
}
'@
    }

    [DeadlineWidget.Installation.ShortcutWriter]::Create($Path, $TargetPath, $Arguments, $WorkingDirectory, $Description)
}

function Get-TreeHashes {
    param([Parameter(Mandatory)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $result = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse) {
        $relative = $file.FullName.Substring($rootPath.Length)
        $result.Add($relative, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
    }
    return $result
}

function Assert-TreesEqual {
    param(
        [Parameter(Mandatory)][string]$ExpectedRoot,
        [Parameter(Mandatory)][string]$ActualRoot
    )
    $expected = Get-TreeHashes $ExpectedRoot
    $actual = Get-TreeHashes $ActualRoot
    if ($expected.Count -ne $actual.Count) { throw 'Manager staging verification failed: file counts differ.' }
    foreach ($item in $expected.GetEnumerator()) {
        if (-not $actual.ContainsKey($item.Key) -or $actual[$item.Key] -cne $item.Value) {
            throw "Manager staging verification failed: $($item.Key) differs."
        }
    }
}

function Get-ProtectedFileStates {
    param([Parameter(Mandatory)][string[]]$Paths)
    $states = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        $exists = Test-Path -LiteralPath $path -PathType Leaf
        $states.Add($path, [pscustomobject]@{
            Exists = $exists
            Hash = if ($exists) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { $null }
        })
    }
    return $states
}

function Assert-ProtectedFileStates {
    param([Parameter(Mandatory)]$ExpectedStates)
    foreach ($item in $ExpectedStates.GetEnumerator()) {
        $exists = Test-Path -LiteralPath $item.Key -PathType Leaf
        if ($exists -ne $item.Value.Exists) {
            throw "Protected deadline data existence changed during installation: $($item.Key)"
        }
        if ($exists -and (Get-FileHash -LiteralPath $item.Key -Algorithm SHA256).Hash -cne $item.Value.Hash) {
            throw "Protected deadline data changed during installation: $($item.Key)"
        }
    }
}

function Get-PackageEntryHash {
    param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$EntryName)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $normalizedEntryName = $EntryName.Replace('\', '/')
        $entry = $archive.Entries |
            Where-Object { $_.FullName.Replace('\', '/') -ceq $normalizedEntryName } |
            Select-Object -First 1
        if (-not $entry) { throw "V2 package entry not found: $EntryName" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
    }
    finally { $archive.Dispose() }
}

if ([string]::IsNullOrWhiteSpace($LocalAppDataRoot)) { throw 'Local application data root is required.' }
$releasePath = [IO.Path]::GetFullPath($ReleaseDirectory)
$managerSource = Join-Path $releasePath 'Manager'
$packagePath = Join-Path $releasePath 'DeadlineWidgetV2.rmskin'
if (-not (Test-Path -LiteralPath $managerSource -PathType Container)) { throw "Manager release directory not found: $managerSource" }
if (-not (Test-Path -LiteralPath (Join-Path $managerSource 'DeadlineWidget.Manager.exe') -PathType Leaf)) { throw 'Manager release is incomplete.' }
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "V2 rmskin package not found: $packagePath" }

if (-not $SkinInstallerPath) {
    $candidates = [Collections.Generic.List[string]]::new()
    $runningRainmeter = Get-Process -Name Rainmeter -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($runningRainmeter -and $runningRainmeter.Path) {
        $candidates.Add((Join-Path (Split-Path $runningRainmeter.Path -Parent) 'SkinInstaller.exe'))
    }
    if ($env:ProgramFiles) { $candidates.Add((Join-Path $env:ProgramFiles 'Rainmeter/SkinInstaller.exe')) }
    if (${env:ProgramFiles(x86)}) { $candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Rainmeter/SkinInstaller.exe')) }
    $SkinInstallerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $SkinInstallerPath -or -not (Test-Path -LiteralPath $SkinInstallerPath -PathType Leaf)) {
    throw 'Rainmeter SkinInstaller.exe could not be located.'
}
$SkinInstallerPath = (Resolve-Path -LiteralPath $SkinInstallerPath).Path
if (-not $SkinRoot) {
    $rainmeterIni = if ($env:APPDATA) { Join-Path $env:APPDATA 'Rainmeter/Rainmeter.ini' } else { $null }
    if ($rainmeterIni -and (Test-Path -LiteralPath $rainmeterIni -PathType Leaf)) {
        $skinLine = Get-Content -LiteralPath $rainmeterIni | Where-Object { $_ -match '^SkinPath=' } | Select-Object -First 1
        if ($skinLine) { $SkinRoot = [Environment]::ExpandEnvironmentVariables($skinLine.Substring(9).Trim()) }
    }
    if (-not $SkinRoot) { $SkinRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter/Skins' }
}
if (-not $PluginRoot -and $env:APPDATA) { $PluginRoot = Join-Path $env:APPDATA 'Rainmeter/Plugins' }
if (-not $StartupRoot -and $env:APPDATA) { $StartupRoot = Join-Path $env:APPDATA 'Microsoft/Windows/Start Menu/Programs/Startup' }
if ([string]::IsNullOrWhiteSpace($SkinRoot) -or [string]::IsNullOrWhiteSpace($PluginRoot) -or [string]::IsNullOrWhiteSpace($StartupRoot)) {
    throw 'Rainmeter skin, plugin, and Startup installation roots are required.'
}
$SkinRoot = [IO.Path]::GetFullPath($SkinRoot)
$PluginRoot = [IO.Path]::GetFullPath($PluginRoot)
$StartupRoot = [IO.Path]::GetFullPath($StartupRoot)
$installedSkinPath = Join-Path $SkinRoot 'DeadlineWidgetV2/DeadlineWidgetV2.ini'
$installedPluginPath = Join-Path $PluginRoot 'DeadlineWidgetBridge.dll'
$installedPluginRootConfig = Join-Path $PluginRoot 'DeadlineWidgetBridge.root'
$expectedSkinHash = Get-PackageEntryHash -Package $packagePath -EntryName 'Skins/DeadlineWidgetV2/DeadlineWidgetV2.ini'
$expectedPluginHash = Get-PackageEntryHash -Package $packagePath -EntryName 'Plugins/64bit/DeadlineWidgetBridge.dll'

$localRoot = [IO.Path]::GetFullPath($LocalAppDataRoot)
$legacyRoot = Join-Path $localRoot 'CourseDeadlineWidget'
$v2Root = Join-Path $localRoot 'DeadlineWidgetV2'
$managerTarget = Join-Path $v2Root 'Manager'
$protectedPaths = @(
    (Join-Path $legacyRoot 'deadlines.json'),
    (Join-Path $legacyRoot 'deadlines.backup.json'),
    (Join-Path $v2Root 'deadlines.json'),
    (Join-Path $v2Root 'deadlines.backup.json')
)
$protectedBefore = Get-ProtectedFileStates -Paths $protectedPaths

[void](New-Item -ItemType Directory -Path $v2Root -Force)
$installId = [Guid]::NewGuid().ToString('N')
$migrationRoot = Join-Path $v2Root 'migration-backup'
$migrationStage = Join-Path $migrationRoot ".installing-$installId"
$migrationTarget = $null
$legacyFiles = @(
    @{ Source = Join-Path $legacyRoot 'deadlines.json'; Name = 'legacy-deadlines.json' },
    @{ Source = Join-Path $legacyRoot 'deadlines.backup.json'; Name = 'legacy-deadlines.backup.json' }
)
$presentLegacyFiles = @($legacyFiles | Where-Object { $protectedBefore[$_.Source].Exists })
if ($presentLegacyFiles.Count -gt 0) {
    [void](New-Item -ItemType Directory -Path $migrationStage -Force)
    $manifestLines = [Collections.Generic.List[string]]::new()
    try {
        foreach ($file in $presentLegacyFiles) {
            $destination = Join-Path $migrationStage $file.Name
            Copy-Item -LiteralPath $file.Source -Destination $destination
            $sourceHash = $protectedBefore[$file.Source].Hash
            $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            if ($sourceHash -cne $destinationHash) { throw "Migration backup verification failed for $($file.Name)." }
            $manifestLines.Add("$sourceHash  $($file.Name)")
        }
        [IO.File]::WriteAllLines((Join-Path $migrationStage 'SHA256SUMS.txt'), $manifestLines, [Text.UTF8Encoding]::new($false))
        $migrationTarget = Join-Path $migrationRoot ((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + "-$installId")
        Move-Item -LiteralPath $migrationStage -Destination $migrationTarget
    }
    catch {
        if (Test-Path -LiteralPath $migrationStage) { Remove-Item -LiteralPath $migrationStage -Recurse -Force }
        throw
    }
}

$managerStage = Join-Path $v2Root ".Manager.installing-$installId"
$managerPrevious = Join-Path $v2Root ".Manager.previous-$installId"
$managerBackedUp = $false
$managerInstalled = $false
try {
    [void](New-Item -ItemType Directory -Path $managerStage -Force)
    Get-ChildItem -LiteralPath $managerSource -Force | Copy-Item -Destination $managerStage -Recurse -Force
    Assert-TreesEqual -ExpectedRoot $managerSource -ActualRoot $managerStage
    if (Test-Path -LiteralPath $managerTarget) {
        Move-Item -LiteralPath $managerTarget -Destination $managerPrevious
        $managerBackedUp = $true
    }
    Move-Item -LiteralPath $managerStage -Destination $managerTarget
    $managerInstalled = $true

    $installerProcess = Start-Process -FilePath $SkinInstallerPath -ArgumentList ('"{0}"' -f $packagePath) -Wait -PassThru
    if ($installerProcess.ExitCode -ne 0) { throw "SkinInstaller failed: $($installerProcess.ExitCode)" }
    if (-not (Test-Path -LiteralPath $installedSkinPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $installedSkinPath -Algorithm SHA256).Hash -cne $expectedSkinHash) {
        throw "V2 skin installation postcondition failed: $installedSkinPath"
    }
    if (-not (Test-Path -LiteralPath $installedPluginPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $installedPluginPath -Algorithm SHA256).Hash -cne $expectedPluginHash) {
        throw "V2 plugin installation postcondition failed: $installedPluginPath"
    }
    [IO.File]::WriteAllText($installedPluginRootConfig, $localRoot, [Text.UTF8Encoding]::new($false))
    if ([IO.File]::ReadAllText($installedPluginRootConfig, [Text.UTF8Encoding]::new($false)).Trim() -cne $localRoot) {
        throw "V2 plugin root configuration failed: $installedPluginRootConfig"
    }
    $managerExecutable = [IO.Path]::GetFullPath((Join-Path $managerTarget 'DeadlineWidget.Manager.exe'))
    [void](New-Item -ItemType Directory -Path $StartupRoot -Force)
    $startupShortcutPath = Join-Path $StartupRoot 'DeadlineWidgetV2 Manager.lnk'
    New-UnicodeShortcut `
        -Path $startupShortcutPath `
        -TargetPath $managerExecutable `
        -Arguments '--background' `
        -WorkingDirectory $managerTarget `
        -Description 'Deadline Widget background manager'
    if (-not (Test-Path -LiteralPath $startupShortcutPath -PathType Leaf)) { throw 'V2 background manager Startup shortcut was not created.' }
    Assert-ProtectedFileStates -ExpectedStates $protectedBefore
}
catch {
    $installError = $_
    if ($managerInstalled -and (Test-Path -LiteralPath $managerTarget)) {
        Remove-Item -LiteralPath $managerTarget -Recurse -Force
    }
    if ($managerBackedUp -and (Test-Path -LiteralPath $managerPrevious) -and -not (Test-Path -LiteralPath $managerTarget)) {
        Move-Item -LiteralPath $managerPrevious -Destination $managerTarget
    }
    if (Test-Path -LiteralPath $managerStage) { Remove-Item -LiteralPath $managerStage -Recurse -Force }
    throw $installError
}
if ($managerBackedUp -and (Test-Path -LiteralPath $managerPrevious)) {
    Remove-Item -LiteralPath $managerPrevious -Recurse -Force
}

Write-Output "Manager: $managerTarget"
Write-Output "Package: $packagePath"
Write-Output "Skin: $installedSkinPath"
Write-Output "Plugin: $installedPluginPath"
Write-Output "Plugin root: $installedPluginRootConfig"
if ($migrationTarget) { Write-Output "Verified migration backup: $migrationTarget" }
foreach ($item in $protectedBefore.GetEnumerator() | Sort-Object Key) {
    if ($item.Value.Exists) { Write-Output "Preserved SHA-256 $($item.Value.Hash)  $($item.Key)" }
    else { Write-Output "Preserved absent path  $($item.Key)" }
}
Write-Output 'Legacy skin DeadlineWidget and legacy data directory CourseDeadlineWidget were not modified.'
