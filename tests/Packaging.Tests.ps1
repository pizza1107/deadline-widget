[CmdletBinding()]
param(
    [string]$DotNetPath = 'dotnet',
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
$v2Root = Split-Path $PSScriptRoot -Parent
$buildScript = Join-Path $v2Root 'scripts/Build-Package.ps1'
$installScript = Join-Path $v2Root 'scripts/Install-V2.ps1'
$uninstallScript = Join-Path $v2Root 'scripts/Uninstall-V2.ps1'
$missing = @($buildScript, $installScript, $uninstallScript | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missing.Count -gt 0) {
    Write-Host "FAIL: Missing production packaging scripts: $($missing -join ', ')"
    exit 1
}

$buildScriptText = [IO.File]::ReadAllText($buildScript)
$packagingTestText = [IO.File]::ReadAllText($PSCommandPath)
if ($buildScriptText -notmatch [regex]::Escape('$repositoryRoot = $v2Root') -or
    $buildScriptText -notmatch [regex]::Escape("`$workspacePackageCache = Join-Path `$repositoryRoot '.nuget/packages'")) {
    throw 'Build-Package.ps1 does not resolve repository paths from the relocated V2 repository root.'
}

$script:assertions = 0
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:assertions++
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -cne $Actual) { throw "$Message Expected: <$Expected>; actual: <$Actual>." }
    $script:assertions++
}

function Assert-BytesEqual {
    param([byte[]]$Expected, [byte[]]$Actual, [string]$Message)
    if ($Expected.Length -ne $Actual.Length) { throw "$Message Byte lengths differ." }
    for ($index = 0; $index -lt $Expected.Length; $index++) {
        if ($Expected[$index] -ne $Actual[$index]) { throw "$Message Bytes differ at offset $index." }
    }
    $script:assertions++
}

function Get-FileSha256 {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-RelativeFileNames {
    param([Parameter(Mandatory)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | ForEach-Object {
        $_.FullName.Substring($rootPath.Length).Replace('\', '/')
    } | Sort-Object -CaseSensitive)
}

function Read-IniSection {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Section)
    $values = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    $inside = $false
    foreach ($rawLine in [IO.File]::ReadAllLines($Path, [Text.Encoding]::ASCII)) {
        $line = $rawLine.Trim()
        if (-not $line -or $line.StartsWith(';')) { continue }
        if ($line -match '^\[(.+)\]$') { $inside = $Matches[1] -ceq $Section; continue }
        if (-not $inside -or $line -notmatch '^([^=]+)=(.*)$') { continue }
        $values.Add($Matches[1].Trim(), $Matches[2])
    }
    return $values
}

function Assert-InstallFails {
    param([hashtable]$Arguments, [string]$ExpectedMessage)
    $failure = $null
    try { & $installScript @Arguments | Out-Null } catch { $failure = $_.Exception.Message }
    Assert-True ($null -ne $failure) "Install was expected to fail: $ExpectedMessage"
    Assert-True ($failure -like $ExpectedMessage) "Install failed for the wrong reason. Expected: $ExpectedMessage; actual: $failure"
}

Assert-True ($buildScriptText -match '\[switch\]\$SkipSolutionTests') 'Build-Package must expose -SkipSolutionTests.'
Assert-True ($packagingTestText -match 'SkipSolutionTests\s*=\s*\$true') 'Packaging tests must avoid nested solution tests.'

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('DeadlineWidgetV2.Packaging.Tests-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $testRoot)
$savedLocalAppData = $env:LOCALAPPDATA
$savedAppData = $env:APPDATA
$savedFakeLog = $env:FAKE_SKININSTALLER_LOG
$savedFakeMode = $env:FAKE_SKININSTALLER_MODE
$savedFakeSkinSource = $env:FAKE_SKIN_SOURCE
$savedFakeSkinTarget = $env:FAKE_SKIN_TARGET
$savedFakePluginSource = $env:FAKE_PLUGIN_SOURCE
$savedFakePluginTarget = $env:FAKE_PLUGIN_TARGET
$savedFakeProtectedPath = $env:FAKE_PROTECTED_PATH
try {
    $releaseRoot = Join-Path $testRoot 'release'
    $skinSource = Join-Path $testRoot 'skin-source/DeadlineWidgetV2'
    [void](New-Item -ItemType Directory -Path (Split-Path $skinSource -Parent) -Force)
    Copy-Item -LiteralPath (Join-Path $v2Root 'rainmeter/Skins/DeadlineWidgetV2') -Destination $skinSource -Recurse
    $luaSourcePath = Join-Path $skinSource '@Resources/DeadlineWidgetV2.lua'
    [IO.File]::AppendAllText($luaSourcePath, "`r`n-- 中文打包边界`r`n", [Text.UTF8Encoding]::new($false))

    $buildArguments = @{
        OutputDirectory = $releaseRoot
        DotNetPath = $DotNetPath
        SkinSource = $skinSource
        SkipSolutionTests = $true
        Version = '1.2.3'
    }
    if ($CompilerPath) { $buildArguments.CompilerPath = $CompilerPath }
    $buildResult = & $buildScript @buildArguments

    $releaseFiles = @(Get-ChildItem -LiteralPath $releaseRoot -Force | Sort-Object Name)
    Assert-Equal 5 $releaseFiles.Count 'Release bundle must contain exactly the manager, rmskin, installer, uninstaller, and readme.'
    Assert-Equal 'DeadlineWidgetV2.rmskin' $releaseFiles[0].Name 'Release package name is wrong.'
    Assert-Equal 'Install-V2.ps1' $releaseFiles[1].Name 'Release installer is missing.'
    Assert-Equal 'Manager' $releaseFiles[2].Name 'Published manager directory is missing.'
    Assert-True $releaseFiles[2].PSIsContainer 'Manager bundle entry must be a directory.'
    Assert-Equal 'README.txt' $releaseFiles[3].Name 'Release readme is missing.'
    Assert-Equal 'Uninstall-V2.ps1' $releaseFiles[4].Name 'Release uninstaller is missing.'

    $managerRoot = Join-Path $releaseRoot 'Manager'
    foreach ($requiredRuntimeFile in @('DeadlineWidget.Manager.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $managerRoot $requiredRuntimeFile) -PathType Leaf) "Self-contained manager is missing $requiredRuntimeFile."
    }
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $managerRoot 'DeadlineWidgetBridge.dll'))) 'Bridge DLL must not be mixed into the manager publish.'

    $packagePath = Join-Path $releaseRoot 'DeadlineWidgetV2.rmskin'
    Assert-True (Test-Path -LiteralPath $packagePath -PathType Leaf) 'The rmskin package was not created.'
    Assert-Equal ([IO.Path]::GetFullPath($releaseRoot)) $buildResult.ReleaseDirectory 'Build result must report the release directory.'
    Assert-Equal ([IO.Path]::GetFullPath($packagePath)) $buildResult.PackagePath 'Build result must report the package path.'
    Assert-Equal (Get-FileSha256 $packagePath) $buildResult.PackageSha256 'Build result must report the package SHA-256.'
    $extractRoot = Join-Path $testRoot 'extracted-rmskin'
    [IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $extractRoot)
    $expectedPackageFiles = @(
        'Plugins/64bit/DeadlineWidgetBridge.dll',
        'Rainstaller.cfg',
        'Skins/DeadlineWidgetV2/@Resources/DeadlineWidgetV2.lua',
        'Skins/DeadlineWidgetV2/@Resources/json.lua',
        'Skins/DeadlineWidgetV2/DeadlineWidgetV2.ini'
    )
    Assert-Equal ($expectedPackageFiles -join '|') ((Get-RelativeFileNames $extractRoot) -join '|') 'The rmskin must contain only the V2 skin, x64 plugin, and Rainstaller metadata.'

    $rainstallerPath = Join-Path $extractRoot 'Rainstaller.cfg'
    $rainstaller = Read-IniSection -Path $rainstallerPath -Section 'Rainstaller'
    $expectedRainstaller = [ordered]@{
        Name = 'Deadline Widget V2'
        Author = 'CourseDeadlineWidget'
        Version = '1.2.3'
        Skins = '1'
        Themes = '-1'
        Addons = '-1'
        Plugins = '1'
        Fonts = '-1'
        AdminRights = '1'
        MinRainmeterVer = '4.5.0'
        UpdateINIPath = ''
        UpdateINI = ''
        LaunchType = 'Load'
        LaunchCommand = 'DeadlineWidgetV2\DeadlineWidgetV2.ini'
    }
    Assert-Equal (($expectedRainstaller.Keys | Sort-Object) -join '|') (($rainstaller.Keys | Sort-Object) -join '|') 'Rainstaller.cfg must use the complete old-format schema and no new-format keys.'
    foreach ($entry in $expectedRainstaller.GetEnumerator()) {
        Assert-Equal $entry.Value $rainstaller[$entry.Key] "Wrong old-format Rainstaller value for $($entry.Key)."
    }

    $stagedIni = Join-Path $extractRoot 'Skins/DeadlineWidgetV2/DeadlineWidgetV2.ini'
    $iniBytes = [IO.File]::ReadAllBytes($stagedIni)
    Assert-BytesEqual ([byte[]](0xff, 0xfe)) ([byte[]]$iniBytes[0..1]) 'The staged Rainmeter INI must be UTF-16 LE with BOM.'
    Assert-Equal ([IO.File]::ReadAllText((Join-Path $skinSource 'DeadlineWidgetV2.ini'))) ([IO.File]::ReadAllText($stagedIni, [Text.UnicodeEncoding]::new($false, $true, $true))) 'UTF-16 staging changed INI text.'

    $stagedLua = Join-Path $extractRoot 'Skins/DeadlineWidgetV2/@Resources/DeadlineWidgetV2.lua'
    $mainLuaBytes = [IO.File]::ReadAllBytes($stagedLua)
    Assert-BytesEqual ([byte[]](0xff, 0xfe)) ([byte[]]$mainLuaBytes[0..1]) 'Main Rainmeter Lua must be UTF-16 LE with BOM.'
    Assert-Equal ([IO.File]::ReadAllText($luaSourcePath)) ([IO.File]::ReadAllText($stagedLua, [Text.UnicodeEncoding]::new($false, $true, $true))) 'UTF-16 staging changed the main renderer text.'

    $jsonLuaPath = Join-Path $extractRoot 'Skins/DeadlineWidgetV2/@Resources/json.lua'
    $jsonLuaBytes = [IO.File]::ReadAllBytes($jsonLuaPath)
    Assert-True (-not ($jsonLuaBytes[0] -eq 0xff -and $jsonLuaBytes[1] -eq 0xfe)) 'JSON byte decoder must remain single-byte source.'
    Assert-True (($jsonLuaBytes | Where-Object { $_ -gt 0x7f }).Count -eq 0) 'JSON helper must remain ASCII-compatible.'

    $pluginPath = Join-Path $extractRoot 'Plugins/64bit/DeadlineWidgetBridge.dll'
    $bridgeBuild = Join-Path $v2Root 'scripts/Build-Bridge.ps1'
    . $bridgeBuild
    if (-not $CompilerPath) {
        $CompilerPath = (Get-Command 'g++.exe' -ErrorAction SilentlyContinue).Source
        if (-not $CompilerPath) {
            $CompilerPath = @(Get-ChildItem -LiteralPath 'C:\' -Directory -Filter 'x86_64*' -ErrorAction SilentlyContinue |
                ForEach-Object { Join-Path $_.FullName 'mingw64/bin/g++.exe' } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })[0]
        }
    }
    $objdumpPath = Join-Path (Split-Path $CompilerPath -Parent) 'objdump.exe'
    $exportText = (& $objdumpPath '-p' $pluginPath | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Independent packaged bridge export inspection failed.' }
    Assert-Equal 'ExecuteBang,Finalize,Initialize,Reload,Update' ((Get-BridgeExportNames -Text $exportText -Format ObjDump) -join ',') 'Packaged bridge exports are wrong.'
    $peBytes = [IO.File]::ReadAllBytes($pluginPath)
    $peOffset = [BitConverter]::ToInt32($peBytes, 0x3c)
    Assert-True ([BitConverter]::ToUInt16($peBytes, $peOffset + 4) -eq 0x8664) 'Packaged plugin is not x64.'
    Assert-True ([BitConverter]::ToUInt16($peBytes, $peOffset + 24) -eq 0x20b) 'Packaged plugin is not PE32+.'

    $repositoryRoot = Split-Path $v2Root -Parent
    $nonBundleOutput = Join-Path $testRoot 'not-a-v2-bundle'
    [void](New-Item -ItemType Directory -Path $nonBundleOutput)
    [IO.File]::WriteAllText((Join-Path $nonBundleOutput 'sentinel.txt'), 'DO-NOT-DELETE')
    foreach ($unsafeOutput in @([IO.Path]::GetPathRoot($repositoryRoot), $repositoryRoot, $skinSource, $nonBundleOutput)) {
        $unsafeFailure = $null
        try {
            & $buildScript -OutputDirectory $unsafeOutput -DotNetPath (Join-Path $testRoot 'must-not-run.exe') -SkinSource $skinSource | Out-Null
        } catch { $unsafeFailure = $_.Exception.Message }
        Assert-True ($unsafeFailure -like 'Refusing unsafe V2 bundle output:*') "Unsafe output was not rejected before build: $unsafeOutput; failure: $unsafeFailure"
    }
    Assert-Equal 'DO-NOT-DELETE' ([IO.File]::ReadAllText((Join-Path $nonBundleOutput 'sentinel.txt')) ) 'Unsafe output validation modified a non-bundle directory.'

    $localRoot = Join-Path $testRoot 'Local AppData 隔离'
    $appDataRoot = Join-Path $testRoot 'Roaming AppData 隔离'
    $legacyRoot = Join-Path $localRoot 'CourseDeadlineWidget'
    $v2DataRoot = Join-Path $localRoot 'DeadlineWidgetV2'
    $legacySkinRoot = Join-Path $appDataRoot 'Rainmeter/Skins/DeadlineWidget'
    $installedSkinRoot = Join-Path $testRoot 'Documents/Rainmeter/Skins'
    $installedPluginRoot = Join-Path $appDataRoot 'Rainmeter/Plugins'
    $startupRoot = Join-Path $appDataRoot 'Microsoft/Windows/Start Menu/Programs/Startup'
    [void](New-Item -ItemType Directory -Path $legacyRoot, (Join-Path $v2DataRoot 'Manager'), $legacySkinRoot, $installedSkinRoot, $installedPluginRoot -Force)
    $legacyPrimary = Join-Path $legacyRoot 'deadlines.json'
    $legacyBackup = Join-Path $legacyRoot 'deadlines.backup.json'
    $v2Primary = Join-Path $v2DataRoot 'deadlines.json'
    $v2Backup = Join-Path $v2DataRoot 'deadlines.backup.json'
    [IO.File]::WriteAllBytes($legacyPrimary, [byte[]](0, 1, 2, 3, 239, 187, 191, 255))
    [IO.File]::WriteAllBytes($legacyBackup, [Text.Encoding]::UTF8.GetBytes('LEGACY-BACKUP-中文'))
    [IO.File]::WriteAllBytes($v2Primary, [Text.Encoding]::UTF8.GetBytes('V2-PRIMARY-中文'))
    [IO.File]::WriteAllText((Join-Path $v2DataRoot 'Manager/DeadlineWidget.Manager.exe'), 'OLD-MANAGER')
    [IO.File]::WriteAllText((Join-Path $legacyRoot 'legacy-sentinel.txt'), 'KEEP-LEGACY-DIRECTORY')
    [IO.File]::WriteAllText((Join-Path $legacySkinRoot 'legacy-sentinel.txt'), 'KEEP-LEGACY-SKIN')
    $protectedFiles = @($legacyPrimary, $legacyBackup, $v2Primary)
    $beforeHashes = @{}
    foreach ($path in $protectedFiles) { $beforeHashes[$path] = Get-FileSha256 $path }

    $installRelease = Join-Path $testRoot 'install-release'
    [void](New-Item -ItemType Directory -Path (Join-Path $installRelease 'Manager') -Force)
    [IO.File]::WriteAllText((Join-Path $installRelease 'Manager/DeadlineWidget.Manager.exe'), 'NEW-MANAGER')
    [IO.File]::WriteAllText((Join-Path $installRelease 'Manager/manager-component.dll'), 'NEW-COMPONENT')
    Copy-Item -LiteralPath $packagePath -Destination (Join-Path $installRelease 'DeadlineWidgetV2.rmskin')
    $fakeInstaller = Join-Path $testRoot 'FakeSkinInstaller.cmd'
    $fakeLog = Join-Path $testRoot 'skin-installer-argument.txt'
    $fakeInstallerBody = @'
@echo off
>"%FAKE_SKININSTALLER_LOG%" echo %~f1
if /I "%FAKE_SKININSTALLER_MODE%"=="nonzero" exit /b 23
if /I "%FAKE_SKININSTALLER_MODE%"=="cancel" exit /b 0
if not exist "%FAKE_SKIN_TARGET%" mkdir "%FAKE_SKIN_TARGET%"
if not exist "%FAKE_PLUGIN_TARGET%" mkdir "%FAKE_PLUGIN_TARGET%"
copy /Y "%FAKE_SKIN_SOURCE%" "%FAKE_SKIN_TARGET%\DeadlineWidgetV2.ini" >nul
copy /Y "%FAKE_PLUGIN_SOURCE%" "%FAKE_PLUGIN_TARGET%\DeadlineWidgetBridge.dll" >nul
if /I "%FAKE_SKININSTALLER_MODE%"=="create-protected" >"%FAKE_PROTECTED_PATH%" echo CREATED-BY-FAKE
if /I "%FAKE_SKININSTALLER_MODE%"=="mutate-protected" >"%FAKE_PROTECTED_PATH%" echo MUTATED-BY-FAKE
exit /b 0
'@
    [IO.File]::WriteAllText($fakeInstaller, $fakeInstallerBody.Replace("`n", "`r`n"), [Text.Encoding]::ASCII)
    $delayedGuiSource = Join-Path $testRoot 'DelayedGuiSkinInstaller.cpp'
    $delayedGuiInstaller = Join-Path $testRoot 'DelayedGuiSkinInstaller.exe'
    $delayedGuiBody = @'
#include <windows.h>
#include <string>

static std::wstring ReadEnvironment(const wchar_t* name) {
    DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (size == 0) return L"";
    std::wstring value(size, L'\0');
    GetEnvironmentVariableW(name, value.data(), size);
    value.resize(size - 1);
    return value;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    Sleep(1000);
    const std::wstring skinSource = ReadEnvironment(L"FAKE_SKIN_SOURCE");
    const std::wstring skinTarget = ReadEnvironment(L"FAKE_SKIN_TARGET") + L"\\DeadlineWidgetV2.ini";
    const std::wstring pluginSource = ReadEnvironment(L"FAKE_PLUGIN_SOURCE");
    const std::wstring pluginTarget = ReadEnvironment(L"FAKE_PLUGIN_TARGET") + L"\\DeadlineWidgetBridge.dll";
    if (!CopyFileW(skinSource.c_str(), skinTarget.c_str(), FALSE)) return 31;
    if (!CopyFileW(pluginSource.c_str(), pluginTarget.c_str(), FALSE)) return 32;
    return 0;
}
'@
    [IO.File]::WriteAllText($delayedGuiSource, $delayedGuiBody, [Text.Encoding]::UTF8)
    & $CompilerPath '-std=c++17' '-municode' '-mwindows' '-Os' '-s' $delayedGuiSource '-o' $delayedGuiInstaller
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $delayedGuiInstaller -PathType Leaf)) {
        throw 'Could not compile the delayed GUI SkinInstaller test fixture.'
    }
    $env:LOCALAPPDATA = $localRoot
    $env:APPDATA = $appDataRoot
    $env:FAKE_SKININSTALLER_LOG = $fakeLog
    $env:FAKE_SKININSTALLER_MODE = 'success'
    $env:FAKE_SKIN_SOURCE = $stagedIni
    $env:FAKE_SKIN_TARGET = Join-Path $installedSkinRoot 'DeadlineWidgetV2'
    $env:FAKE_PLUGIN_SOURCE = $pluginPath
    $env:FAKE_PLUGIN_TARGET = $installedPluginRoot
    $env:FAKE_PROTECTED_PATH = $v2Backup
    $installArguments = @{
        ReleaseDirectory = $installRelease
        LocalAppDataRoot = $localRoot
        SkinInstallerPath = $fakeInstaller
        SkinRoot = $installedSkinRoot
        StartupRoot = $startupRoot
    }
    & $installScript @installArguments

    foreach ($path in $protectedFiles) {
        Assert-Equal $beforeHashes[$path] (Get-FileSha256 $path) "Installer changed protected deadline bytes at $path."
    }
    Assert-True (-not (Test-Path -LiteralPath $v2Backup)) 'Installer created a protected deadline file that was absent at start.'
    Assert-Equal 'KEEP-LEGACY-DIRECTORY' ([IO.File]::ReadAllText((Join-Path $legacyRoot 'legacy-sentinel.txt'))) 'Installer changed the legacy data directory.'
    Assert-Equal 'KEEP-LEGACY-SKIN' ([IO.File]::ReadAllText((Join-Path $legacySkinRoot 'legacy-sentinel.txt'))) 'Installer changed the legacy Rainmeter skin.'
    Assert-Equal 'NEW-MANAGER' ([IO.File]::ReadAllText((Join-Path $v2DataRoot 'Manager/DeadlineWidget.Manager.exe'))) 'Installed manager does not match the release.'
    Assert-Equal (Get-FileSha256 $stagedIni) (Get-FileSha256 (Join-Path $installedSkinRoot 'DeadlineWidgetV2/DeadlineWidgetV2.ini')) 'Installed V2 skin did not satisfy the package postcondition.'
    Assert-Equal (Get-FileSha256 $pluginPath) (Get-FileSha256 (Join-Path $installedPluginRoot 'DeadlineWidgetBridge.dll')) 'Without -PluginRoot, the installed plugin must be verified under the isolated APPDATA Rainmeter Plugins directory.'
    Assert-Equal ([IO.Path]::GetFullPath($localRoot)) ([IO.File]::ReadAllText((Join-Path $installedPluginRoot 'DeadlineWidgetBridge.root'), [Text.UTF8Encoding]::new($false)).Trim()) 'Installer must persist the absolute Local AppData root beside the plugin.'
    $startupShortcut = Join-Path $startupRoot 'DeadlineWidgetV2 Manager.lnk'
    Assert-True (Test-Path -LiteralPath $startupShortcut -PathType Leaf) 'Installer must create the current-user background manager startup shortcut.'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startupShortcut)
    Assert-Equal ([IO.Path]::GetFullPath((Join-Path $v2DataRoot 'Manager/DeadlineWidget.Manager.exe'))) $shortcut.TargetPath 'Startup shortcut targets the wrong manager executable.'
    Assert-Equal '--background' $shortcut.Arguments 'Startup shortcut must launch the manager hidden.'
    Assert-Equal ([IO.Path]::GetFullPath((Join-Path $installRelease 'DeadlineWidgetV2.rmskin'))) ([IO.File]::ReadAllText($fakeLog).Trim()) 'SkinInstaller did not receive the exact V2 package path.'

    $migrationSets = @(Get-ChildItem -LiteralPath (Join-Path $v2DataRoot 'migration-backup') -Directory)
    Assert-Equal 1 $migrationSets.Count 'Installer must create one isolated migration backup set.'
    $migrationSet = $migrationSets[0].FullName
    Assert-BytesEqual ([IO.File]::ReadAllBytes($legacyPrimary)) ([IO.File]::ReadAllBytes((Join-Path $migrationSet 'legacy-deadlines.json'))) 'Legacy primary migration backup is not byte-identical.'
    Assert-BytesEqual ([IO.File]::ReadAllBytes($legacyBackup)) ([IO.File]::ReadAllBytes((Join-Path $migrationSet 'legacy-deadlines.backup.json'))) 'Legacy secondary migration backup is not byte-identical.'
    $manifest = [IO.File]::ReadAllText((Join-Path $migrationSet 'SHA256SUMS.txt'))
    Assert-True ($manifest -match [regex]::Escape($beforeHashes[$legacyPrimary])) 'Migration manifest is missing the legacy primary hash.'
    Assert-True ($manifest -match [regex]::Escape($beforeHashes[$legacyBackup])) 'Migration manifest is missing the legacy backup hash.'

    function Set-RollbackManager {
        param([string]$Label)
        [IO.File]::WriteAllText((Join-Path $v2DataRoot 'Manager/DeadlineWidget.Manager.exe'), "OLD-$Label")
        [IO.File]::WriteAllText((Join-Path $v2DataRoot 'Manager/rollback-sentinel.txt'), "KEEP-$Label")
    }
    function Reset-InstalledComponents {
        if (Test-Path -LiteralPath (Join-Path $installedSkinRoot 'DeadlineWidgetV2')) { Remove-Item -LiteralPath (Join-Path $installedSkinRoot 'DeadlineWidgetV2') -Recurse -Force }
        if (Test-Path -LiteralPath (Join-Path $installedPluginRoot 'DeadlineWidgetBridge.dll')) { Remove-Item -LiteralPath (Join-Path $installedPluginRoot 'DeadlineWidgetBridge.dll') -Force }
        if (Test-Path -LiteralPath (Join-Path $installedPluginRoot 'DeadlineWidgetBridge.root')) { Remove-Item -LiteralPath (Join-Path $installedPluginRoot 'DeadlineWidgetBridge.root') -Force }
    }
    function Assert-ManagerRolledBack {
        param([string]$Label)
        Assert-Equal "OLD-$Label" ([IO.File]::ReadAllText((Join-Path $v2DataRoot 'Manager/DeadlineWidget.Manager.exe'))) "Manager was not restored after $Label."
        Assert-Equal "KEEP-$Label" ([IO.File]::ReadAllText((Join-Path $v2DataRoot 'Manager/rollback-sentinel.txt'))) "Manager sentinel was not restored after $Label."
    }

    Reset-InstalledComponents
    [void](New-Item -ItemType Directory -Path (Join-Path $installedSkinRoot 'DeadlineWidgetV2') -Force)
    Set-RollbackManager 'GUI-WAIT'
    $delayedGuiArguments = $installArguments.Clone()
    $delayedGuiArguments.SkinInstallerPath = $delayedGuiInstaller
    $waitTimer = [Diagnostics.Stopwatch]::StartNew()
    & $installScript @delayedGuiArguments
    $waitTimer.Stop()
    Assert-True ($waitTimer.ElapsedMilliseconds -ge 900) 'Install-V2 returned before the GUI SkinInstaller process completed.'
    Assert-Equal (Get-FileSha256 $stagedIni) (Get-FileSha256 (Join-Path $installedSkinRoot 'DeadlineWidgetV2/DeadlineWidgetV2.ini')) 'The delayed GUI installer skin result was not verified after process completion.'
    Assert-Equal (Get-FileSha256 $pluginPath) (Get-FileSha256 (Join-Path $installedPluginRoot 'DeadlineWidgetBridge.dll')) 'The delayed GUI installer plugin result was not verified after process completion.'
    Assert-Equal ([IO.Path]::GetFullPath($localRoot)) ([IO.File]::ReadAllText((Join-Path $installedPluginRoot 'DeadlineWidgetBridge.root'), [Text.UTF8Encoding]::new($false)).Trim()) 'The delayed installer must persist the bridge root configuration.'

    Reset-InstalledComponents
    Set-RollbackManager 'CANCEL'
    $env:FAKE_SKININSTALLER_MODE = 'cancel'
    Assert-InstallFails $installArguments '*V2 skin installation postcondition failed*'
    Assert-ManagerRolledBack 'CANCEL'

    Reset-InstalledComponents
    Set-RollbackManager 'CREATE-PROTECTED'
    $env:FAKE_SKININSTALLER_MODE = 'create-protected'
    Assert-InstallFails $installArguments "*Protected deadline data existence changed during installation: $v2Backup*"
    Assert-ManagerRolledBack 'CREATE-PROTECTED'
    Assert-True (Test-Path -LiteralPath $v2Backup -PathType Leaf) 'Failure injection did not create the initially absent protected path.'
    Remove-Item -LiteralPath $v2Backup -Force

    $uninstallLocalRoot = Join-Path $testRoot 'Uninstall Local'
    $uninstallDataRoot = Join-Path $uninstallLocalRoot 'DeadlineWidgetV2'
    $uninstallManager = Join-Path $uninstallDataRoot 'Manager'
    $uninstallSkinRoot = Join-Path $testRoot 'Uninstall Skins'
    $uninstallSkin = Join-Path $uninstallSkinRoot 'DeadlineWidgetV2'
    $uninstallPluginRoot = Join-Path $testRoot 'Uninstall Plugins'
    $uninstallStartupRoot = Join-Path $testRoot 'Uninstall Startup'
    $uninstallLegacy = Join-Path $uninstallLocalRoot 'CourseDeadlineWidget'
    [void](New-Item -ItemType Directory -Path $uninstallManager, $uninstallSkin, $uninstallPluginRoot, $uninstallStartupRoot, (Join-Path $uninstallDataRoot 'migration-backup/set'), (Join-Path $uninstallDataRoot 'logs'), $uninstallLegacy, (Join-Path $uninstallSkinRoot 'UnrelatedSkin') -Force)
    [IO.File]::WriteAllText((Join-Path $uninstallManager 'DeadlineWidget.Manager.exe'), 'PROGRAM')
    [IO.File]::WriteAllText((Join-Path $uninstallSkin 'DeadlineWidgetV2.ini'), 'SKIN')
    [IO.File]::WriteAllText((Join-Path $uninstallPluginRoot 'DeadlineWidgetBridge.dll'), 'PLUGIN')
    [IO.File]::WriteAllText((Join-Path $uninstallPluginRoot 'DeadlineWidgetBridge.root'), 'ROOT')
    [IO.File]::WriteAllText((Join-Path $uninstallStartupRoot 'DeadlineWidgetV2 Manager.lnk'), 'SHORTCUT')
    [IO.File]::WriteAllText((Join-Path $uninstallDataRoot 'deadlines.json'), 'DEADLINES')
    [IO.File]::WriteAllText((Join-Path $uninstallDataRoot 'deadlines.backup.json'), 'BACKUP')
    [IO.File]::WriteAllText((Join-Path $uninstallDataRoot 'migration-backup/set/manifest.txt'), 'MIGRATION')
    [IO.File]::WriteAllText((Join-Path $uninstallDataRoot 'logs/manager.log'), 'LOG')
    [IO.File]::WriteAllText((Join-Path $uninstallLegacy 'sentinel.txt'), 'LEGACY')
    [IO.File]::WriteAllText((Join-Path $uninstallSkinRoot 'UnrelatedSkin/sentinel.ini'), 'UNRELATED')
    $uninstallArguments = @{
        LocalAppDataRoot = $uninstallLocalRoot
        SkinRoot = $uninstallSkinRoot
        PluginRoot = $uninstallPluginRoot
        StartupRoot = $uninstallStartupRoot
    }

    & $uninstallScript @uninstallArguments

    Assert-True (-not (Test-Path -LiteralPath $uninstallManager)) 'Default uninstall left the Manager directory.'
    Assert-True (-not (Test-Path -LiteralPath $uninstallSkin)) 'Default uninstall left the V2 skin.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $uninstallPluginRoot 'DeadlineWidgetBridge.dll'))) 'Default uninstall left the bridge DLL.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $uninstallPluginRoot 'DeadlineWidgetBridge.root'))) 'Default uninstall left the bridge root file.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $uninstallStartupRoot 'DeadlineWidgetV2 Manager.lnk'))) 'Default uninstall left the startup shortcut.'
    foreach ($preserved in @('deadlines.json', 'deadlines.backup.json', 'migration-backup/set/manifest.txt', 'logs/manager.log')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $uninstallDataRoot $preserved) -PathType Leaf) "Default uninstall removed protected data: $preserved"
    }
    Assert-Equal 'LEGACY' ([IO.File]::ReadAllText((Join-Path $uninstallLegacy 'sentinel.txt'))) 'Uninstall changed legacy data.'
    Assert-Equal 'UNRELATED' ([IO.File]::ReadAllText((Join-Path $uninstallSkinRoot 'UnrelatedSkin/sentinel.ini'))) 'Uninstall changed an unrelated skin.'

    [void](New-Item -ItemType Directory -Path $uninstallManager, $uninstallSkin -Force)
    [IO.File]::WriteAllText((Join-Path $uninstallManager 'DeadlineWidget.Manager.exe'), 'PROGRAM-AGAIN')
    & $uninstallScript @uninstallArguments -RemoveUserData

    Assert-True (-not (Test-Path -LiteralPath $uninstallDataRoot)) 'Explicit data removal left the validated V2 data root.'
    Assert-Equal 'LEGACY' ([IO.File]::ReadAllText((Join-Path $uninstallLegacy 'sentinel.txt'))) 'Explicit V2 data removal changed legacy data.'
    Assert-Equal 'UNRELATED' ([IO.File]::ReadAllText((Join-Path $uninstallSkinRoot 'UnrelatedSkin/sentinel.ini'))) 'Explicit V2 data removal changed an unrelated skin.'

    Write-Host "PASS: $script:assertions packaging and isolated-install assertions"
    Write-Host "Package SHA-256: $(Get-FileSha256 $packagePath)"
} finally {
    $env:LOCALAPPDATA = $savedLocalAppData
    $env:APPDATA = $savedAppData
    $env:FAKE_SKININSTALLER_LOG = $savedFakeLog
    $env:FAKE_SKININSTALLER_MODE = $savedFakeMode
    $env:FAKE_SKIN_SOURCE = $savedFakeSkinSource
    $env:FAKE_SKIN_TARGET = $savedFakeSkinTarget
    $env:FAKE_PLUGIN_SOURCE = $savedFakePluginSource
    $env:FAKE_PLUGIN_TARGET = $savedFakePluginTarget
    $env:FAKE_PROTECTED_PATH = $savedFakeProtectedPath
    if ($delayedGuiInstaller) {
        foreach ($process in [Diagnostics.Process]::GetProcessesByName([IO.Path]::GetFileNameWithoutExtension($delayedGuiInstaller))) {
            try {
                if ($process.MainModule.FileName -ceq $delayedGuiInstaller) { [void]$process.WaitForExit(5000) }
            } finally { $process.Dispose() }
        }
    }
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolvedTestRoot -Leaf) -notlike 'DeadlineWidgetV2.Packaging.Tests-*') {
        throw 'Refusing cleanup outside the packaging test temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
