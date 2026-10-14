[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [string]$DotNetPath = 'dotnet',
    [string]$CompilerPath,
    [string]$SkinSource = (Join-Path (Split-Path $PSScriptRoot -Parent) 'rainmeter/Skins/DeadlineWidgetV2'),
    [switch]$SkipSolutionTests,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$v2Root = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$repositoryRoot = $v2Root
$solutionPath = Join-Path $v2Root 'DeadlineWidgetV2.sln'
$managerProject = Join-Path $v2Root 'src/DeadlineWidget.Manager/DeadlineWidget.Manager.csproj'
$bridgeBuild = Join-Path $PSScriptRoot 'Build-Bridge.ps1'
$installScript = Join-Path $PSScriptRoot 'Install-V2.ps1'
$uninstallScript = Join-Path $PSScriptRoot 'Uninstall-V2.ps1'
$releaseReadme = Join-Path $PSScriptRoot 'Release-Readme.txt'
if (-not (Test-Path -LiteralPath $SkinSource -PathType Container)) { throw "V2 skin source not found: $SkinSource" }
if (-not (Test-Path -LiteralPath $installScript -PathType Leaf)) { throw "Installer not found: $installScript" }
if (-not (Test-Path -LiteralPath $uninstallScript -PathType Leaf)) { throw "Uninstaller not found: $uninstallScript" }
if (-not (Test-Path -LiteralPath $releaseReadme -PathType Leaf)) { throw "Release readme not found: $releaseReadme" }

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$skinSourcePath = (Resolve-Path -LiteralPath $SkinSource).Path
function Test-SameOrAncestorPath {
    param([Parameter(Mandatory)][string]$Candidate, [Parameter(Mandatory)][string]$Path)
    $candidatePath = [IO.Path]::GetFullPath($Candidate).TrimEnd('\')
    $childPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    return $childPath.Equals($candidatePath, [StringComparison]::OrdinalIgnoreCase) -or
        $childPath.StartsWith($candidatePath + '\', [StringComparison]::OrdinalIgnoreCase)
}

$filesystemRoot = [IO.Path]::GetPathRoot($outputPath)
$unsafeReason = $null
if ($outputPath.TrimEnd('\').Equals($filesystemRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    $unsafeReason = 'filesystem root'
} elseif (Test-SameOrAncestorPath -Candidate $outputPath -Path $repositoryRoot) {
    $unsafeReason = 'repository root or its ancestor'
} elseif ((Test-SameOrAncestorPath -Candidate $outputPath -Path $skinSourcePath) -or
    (Test-SameOrAncestorPath -Candidate $skinSourcePath -Path $outputPath)) {
    $unsafeReason = 'V2 skin source overlap'
} elseif (Test-Path -LiteralPath $outputPath) {
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) {
        $unsafeReason = 'existing output is not a directory'
    } else {
        $topLevel = @(Get-ChildItem -LiteralPath $outputPath -Force)
        $manager = Join-Path $outputPath 'Manager'
        $package = Join-Path $outputPath 'DeadlineWidgetV2.rmskin'
        $installer = Join-Path $outputPath 'Install-V2.ps1'
        $uninstaller = Join-Path $outputPath 'Uninstall-V2.ps1'
        $readme = Join-Path $outputPath 'README.txt'
        if ($topLevel.Count -ne 5 -or
            -not (Test-Path -LiteralPath $manager -PathType Container) -or
            -not (Test-Path -LiteralPath (Join-Path $manager 'DeadlineWidget.Manager.exe') -PathType Leaf) -or
            -not (Test-Path -LiteralPath $package -PathType Leaf) -or
            -not (Test-Path -LiteralPath $installer -PathType Leaf) -or
            -not (Test-Path -LiteralPath $uninstaller -PathType Leaf) -or
            -not (Test-Path -LiteralPath $readme -PathType Leaf)) {
            $unsafeReason = 'existing directory is not an exact V2 release bundle'
        }
    }
}
if ($unsafeReason) { throw "Refusing unsafe V2 bundle output: $outputPath ($unsafeReason)." }

$outputParent = Split-Path $outputPath -Parent
[void](New-Item -ItemType Directory -Path $outputParent -Force)
$buildId = [Guid]::NewGuid().ToString('N')
$buildRoot = Join-Path $outputParent ".DeadlineWidgetV2.package-$buildId"
$releaseStage = Join-Path $buildRoot 'release'
$managerStage = Join-Path $releaseStage 'Manager'
$packageStage = Join-Path $buildRoot 'rmskin'
$skinStage = Join-Path $packageStage 'Skins/DeadlineWidgetV2'
$pluginStage = Join-Path $packageStage 'Plugins/64bit'
$zipPath = Join-Path $releaseStage 'DeadlineWidgetV2.rmskin'
$previousOutput = "$outputPath.previous-$buildId"
$outputBackedUp = $false
$savedNuGetPackages = $env:NUGET_PACKAGES
$workspacePackageCache = Join-Path $repositoryRoot '.nuget/packages'

try {
    [void](New-Item -ItemType Directory -Path $managerStage, $skinStage, $pluginStage -Force)

    if (-not $SkipSolutionTests) {
        & $DotNetPath test $solutionPath -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "V2 solution tests failed: $LASTEXITCODE" }
    }

    try {
        if (-not $env:NUGET_PACKAGES -and (Test-Path -LiteralPath $workspacePackageCache -PathType Container)) {
            $env:NUGET_PACKAGES = (Resolve-Path -LiteralPath $workspacePackageCache).Path
        }
        & $DotNetPath publish $managerProject -c Release -r win-x64 --self-contained true --nologo -o $managerStage
        if ($LASTEXITCODE -ne 0) { throw "Self-contained manager publication failed: $LASTEXITCODE" }
    }
    finally {
        $env:NUGET_PACKAGES = $savedNuGetPackages
    }
    foreach ($requiredRuntimeFile in @('DeadlineWidget.Manager.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $managerStage $requiredRuntimeFile) -PathType Leaf)) {
            throw "Self-contained manager publication is missing $requiredRuntimeFile."
        }
    }

    $bridgeArguments = @{ OutputDirectory = $pluginStage }
    if ($CompilerPath) { $bridgeArguments.CompilerPath = $CompilerPath }
    $bridge = & $bridgeBuild @bridgeArguments
    if (-not $bridge -or $bridge.Architecture -cne 'PE32+ x86-64' -or
        (($bridge.Exports | Sort-Object -CaseSensitive) -join ',') -cne 'ExecuteBang,Finalize,Initialize,Reload,Update') {
        throw 'Bridge build did not return the verified x64 export contract.'
    }

    Get-ChildItem -LiteralPath $SkinSource -Force | Copy-Item -Destination $skinStage -Recurse -Force
    $unicode = [Text.UnicodeEncoding]::new($false, $true, $true)
    foreach ($ini in Get-ChildItem -LiteralPath $skinStage -Recurse -File -Filter '*.ini') {
        [IO.File]::WriteAllText($ini.FullName, [IO.File]::ReadAllText($ini.FullName), $unicode)
    }
    $mainRenderer = Join-Path $skinStage '@Resources/DeadlineWidgetV2.lua'
    [IO.File]::WriteAllText($mainRenderer, [IO.File]::ReadAllText($mainRenderer), $unicode)
    foreach ($lua in Get-ChildItem -LiteralPath $skinStage -Recurse -File -Filter '*.lua' |
        Where-Object { $_.FullName -cne $mainRenderer }) {
        $luaBytes = [IO.File]::ReadAllBytes($lua.FullName)
        if (@($luaBytes | Where-Object { $_ -gt 0x7f }).Count -ne 0) {
            throw "$($lua.Name) must remain ASCII-compatible for byte-oriented UTF-8 decoding."
        }
    }

    $rainstaller = @"
[Rainstaller]
Name=Deadline Widget V2
Author=CourseDeadlineWidget
Version=$Version
Skins=1
Themes=-1
Addons=-1
Plugins=1
Fonts=-1
AdminRights=1
MinRainmeterVer=4.5.0
UpdateINIPath=
UpdateINI=
LaunchType=Load
LaunchCommand=DeadlineWidgetV2\DeadlineWidgetV2.ini
"@
    [IO.File]::WriteAllText((Join-Path $packageStage 'Rainstaller.cfg'), $rainstaller, [Text.Encoding]::ASCII)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $packageStage,
        $zipPath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
    Copy-Item -LiteralPath $installScript -Destination (Join-Path $releaseStage 'Install-V2.ps1')
    Copy-Item -LiteralPath $uninstallScript -Destination (Join-Path $releaseStage 'Uninstall-V2.ps1')
    Copy-Item -LiteralPath $releaseReadme -Destination (Join-Path $releaseStage 'README.txt')

    if (Test-Path -LiteralPath $outputPath) {
        Move-Item -LiteralPath $outputPath -Destination $previousOutput
        $outputBackedUp = $true
    }
    Move-Item -LiteralPath $releaseStage -Destination $outputPath
    if ($outputBackedUp -and (Test-Path -LiteralPath $previousOutput)) {
        Remove-Item -LiteralPath $previousOutput -Recurse -Force
        $outputBackedUp = $false
    }

    $packageHash = Get-FileHash -LiteralPath (Join-Path $outputPath 'DeadlineWidgetV2.rmskin') -Algorithm SHA256
    Write-Host "Release bundle: $outputPath"
    Write-Host "Package SHA-256: $($packageHash.Hash)"
    [pscustomobject]@{
        ReleaseDirectory = $outputPath
        PackagePath = Join-Path $outputPath 'DeadlineWidgetV2.rmskin'
        PackageSha256 = $packageHash.Hash
    }
}
catch {
    if ($outputBackedUp -and -not (Test-Path -LiteralPath $outputPath) -and (Test-Path -LiteralPath $previousOutput)) {
        Move-Item -LiteralPath $previousOutput -Destination $outputPath
        $outputBackedUp = $false
    }
    throw
}
finally {
    $env:NUGET_PACKAGES = $savedNuGetPackages
    if (Test-Path -LiteralPath $buildRoot) { Remove-Item -LiteralPath $buildRoot -Recurse -Force }
}
