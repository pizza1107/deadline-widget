[CmdletBinding()]
param(
    [string]$LocalAppDataRoot = $env:LOCALAPPDATA,
    [string]$SkinRoot,
    [string]$PluginRoot,
    [string]$StartupRoot,
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'

function Get-ExactChildPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Child
    )
    if ([string]::IsNullOrWhiteSpace($Root)) { throw 'Removal root is required.' }
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $targetPath = [IO.Path]::GetFullPath((Join-Path $rootPath $Child)).TrimEnd('\')
    $expected = $rootPath + '\' + $Child.TrimStart('\')
    if (-not $targetPath.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unsafe uninstall target: $targetPath"
    }
    return $targetPath
}

function Remove-KnownItem {
    param([Parameter(Mandatory)][string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

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
if ([string]::IsNullOrWhiteSpace($LocalAppDataRoot) -or [string]::IsNullOrWhiteSpace($SkinRoot) -or
    [string]::IsNullOrWhiteSpace($PluginRoot) -or [string]::IsNullOrWhiteSpace($StartupRoot)) {
    throw 'Local data, skin, plugin, and Startup roots are required.'
}

$v2Root = Get-ExactChildPath -Root $LocalAppDataRoot -Child 'DeadlineWidgetV2'
$manager = Get-ExactChildPath -Root $v2Root -Child 'Manager'
$skin = Get-ExactChildPath -Root $SkinRoot -Child 'DeadlineWidgetV2'
$plugin = Get-ExactChildPath -Root $PluginRoot -Child 'DeadlineWidgetBridge.dll'
$pluginRootConfig = Get-ExactChildPath -Root $PluginRoot -Child 'DeadlineWidgetBridge.root'
$shortcut = Get-ExactChildPath -Root $StartupRoot -Child 'DeadlineWidgetV2 Manager.lnk'

Remove-KnownItem $manager
Remove-KnownItem $skin
Remove-KnownItem $plugin
Remove-KnownItem $pluginRootConfig
Remove-KnownItem $shortcut

if ($RemoveUserData) {
    Remove-KnownItem $v2Root
    Write-Host 'Deadline Widget V2 program components and user data were removed.'
} else {
    Write-Host "Deadline Widget V2 program components were removed. User data was preserved at: $v2Root"
}
