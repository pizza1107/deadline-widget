[CmdletBinding()]
param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$v2Directory = Split-Path $PSScriptRoot -Parent
$nativeDirectory = Join-Path $v2Directory 'native/DeadlineWidgetBridge'
$buildPath = Join-Path $v2Directory 'scripts/Build-Bridge.ps1'
$absent = @('BridgeCore.h', 'BridgeCore.cpp', 'DeadlineWidgetBridge.cpp' | Where-Object { -not (Test-Path -LiteralPath (Join-Path $nativeDirectory $_)) })
if (-not (Test-Path -LiteralPath $buildPath)) { $absent += 'Build-Bridge.ps1' }
if ($absent.Count -gt 0) {
    Write-Host "FAIL: Missing production bridge implementation: $($absent -join ', ')"
    exit 1
}

. $buildPath
$script:exportAssertions = 0
function Assert-ExportFixture {
    param([string]$Text, [string]$Format = 'ObjDump', [bool]$Accepted = $true, [string]$Label, [string]$ExpectedError)
    $errorMessage = $null
    $names = @()
    try { $names = @(Get-BridgeExportNames -Text $Text -Format $Format) } catch { $errorMessage = $_.Exception.Message }
    if ($Accepted) {
        if ($errorMessage) { throw "Fixture $Label was rejected: $errorMessage" }
        if (($names -join ',') -cne 'ExecuteBang,Finalize,Initialize,Reload,Update') { throw "Wrong exports in fixture $Label" }
    } elseif (-not $errorMessage) { throw "Fixture $Label should have been rejected" }
    if ($ExpectedError -and $errorMessage -notlike $ExpectedError) { throw "Fixture $Label failed for the wrong reason: $errorMessage" }
    $script:exportAssertions++
}

# Complete representative GNU objdump records: declared counts, EAT, and names.
$objdump = @'
Export Flags                    0
Ordinal Base                    1
Number in:
    Export Address Table        00000005
    [Name Pointer/Ordinal] Table 00000005
Table Addresses
    Export Address Table        0000000000008028
    Name Pointer Table          0000000000008040
    Ordinal Table               0000000000008060

Export Address Table -- Ordinal Base 1
              Ordinal  Address  Type
    [   0] +base[   1] 00001010 Export RVA
    [   1] +base[   2] 00001020 Export RVA
    [   2] +base[   3] 00001030 Export RVA
    [   3] +base[   4] 00001040 Export RVA
    [   4] +base[   5] 00001050 Export RVA

[Ordinal/Name Pointer] Table -- Ordinal Base 1
              Ordinal   Hint Name
    [   0] +base[   1] 0000 ExecuteBang
    [   1] +base[   2] 0001 Finalize
    [   2] +base[   3] 0002 Initialize
    [   3] +base[   4] 0003 Reload
    [   4] +base[   5] 0004 Update

The Function Table (interpreted .pdata section contents)
'@
Assert-ExportFixture $objdump -Label 'complete objdump export table'
Assert-ExportFixture ($objdump -replace '\r?\n', "`r`n") -Label 'Windows CRLF objdump output'
$oldObjdump = $objdump -replace '\[Ordinal/Name Pointer\] Table -- Ordinal Base 1', '[Ordinal/Name Pointer] Table' -replace '(?m)^\s+Ordinal\s+Hint Name\r?\n', '' -replace '(?m)(\[\s*\d+\]) \+base\[\s*\d+\] [0-9a-f]{4} (\S+)\s*$', '$1 $2'
Assert-ExportFixture $oldObjdump -Label 'older objdump named records'
$extraDecorated = $objdump.Replace('00000005', '00000006').Replace("`n[Ordinal/Name Pointer]", "`n    [   5] +base[   6] 00001060 Export RVA`n`n[Ordinal/Name Pointer]").Replace("`nThe Function Table", "`n    [   5] +base[   6] 0005 Extra@4`n`nThe Function Table")
Assert-ExportFixture $extraDecorated -Accepted $false -Label 'extra decorated export Extra@4' -ExpectedError '*found 6:*Extra@4*'
$extraOrdinal = $objdump.Replace('Export Address Table        00000005', 'Export Address Table        00000006').Replace("`n[Ordinal/Name Pointer]", "`n    [   5] +base[   6] 00001060 Export RVA`n`n[Ordinal/Name Pointer]")
Assert-ExportFixture $extraOrdinal -Accepted $false -Label 'extra ordinal-only export' -ExpectedError '*ordinal-only export*'
Assert-ExportFixture ($objdump.Replace('0004 Update', '0004 Update@4')) -Accepted $false -Label 'decorated required export'
Assert-ExportFixture ($objdump.Replace('0004 Update', '0004 Reload')) -Accepted $false -Label 'duplicate named export'
Assert-ExportFixture ($objdump.Replace('00001050 Export RVA', '00001050 Forwarder RVA -- other.Update')) -Accepted $false -Label 'forwarded export'
Assert-ExportFixture ($objdump.Replace('00000005', '00000006')) -Accepted $false -Label 'declared count disagrees with records'
Assert-ExportFixture ($objdump.Replace('    [   4] +base[   5] 0004 Update', '    [   4] malformed record')) -Accepted $false -Label 'unparsed named record'
Assert-ExportFixture ($objdump.Replace('    [   4] +base[   5] 00001050 Export RVA', '    [   4] malformed address record')) -Accepted $false -Label 'unparsed ordinal record'
Assert-ExportFixture ($objdump.Replace('    [   4] +base[   5] 0004 Update', '    [   8] +base[   9] 0004 Update')) -Accepted $false -Label 'name has no matching ordinal'
$dumpbin = @'
    ordinal hint RVA      name
          1    0 00001010 ExecuteBang
          2    1 00001020 Finalize
          3    2 00001030 Initialize
          4    3 00001040 Reload
          5    4 00001050 Update

  Summary
'@
Assert-ExportFixture $dumpbin -Format DumpBin -Label 'dumpbin named records'
Assert-ExportFixture ($dumpbin.Replace('  Summary', "          6    5 00001060 Extra@4`n`n  Summary")) -Format DumpBin -Accepted $false -Label 'dumpbin extra decorated export' -ExpectedError '*found 6:*Extra@4*'
Assert-ExportFixture ($dumpbin.Replace('  Summary', "          6      00001060 [NONAME]`n`n  Summary")) -Format DumpBin -Accepted $false -Label 'dumpbin ordinal-only export'
Assert-ExportFixture ($dumpbin.Replace('00001050 Update', '00001050 Update = other.Update')) -Format DumpBin -Accepted $false -Label 'dumpbin forwarder'
Write-Host "PASS: $script:exportAssertions export-table fixtures"

$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('DeadlineWidgetBridge.Tests-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $testDirectory)
$savedLocalAppData = $env:LOCALAPPDATA
try {
    $buildArguments = @{ OutputDirectory = Join-Path $testDirectory 'Production/Plugins/64bit' }
    if ($CompilerPath) { $buildArguments.CompilerPath = $CompilerPath }
    $build = & $buildPath @buildArguments
    if ($build.Flags -contains '-DDEADLINE_WIDGET_BRIDGE_TEST_KNOWN_FOLDER_FROM_ENVIRONMENT') {
        throw 'Production bridge build enabled the test-only Known Folder provider.'
    }

    $testPluginDirectory = Join-Path $testDirectory 'TestOnly/Plugins/64bit'
    [void](New-Item -ItemType Directory -Path $testPluginDirectory -Force)
    $testDll = Join-Path $testPluginDirectory 'DeadlineWidgetBridge.dll'
    $testFlags = @('-std=c++17', '-O2', '-Wall', '-Wextra', '-Werror', '-DUNICODE', '-D_UNICODE',
        '-DDEADLINE_WIDGET_BRIDGE_TEST_KNOWN_FOLDER_FROM_ENVIRONMENT', '-shared', '-static-libgcc', '-static-libstdc++',
        '-Wl,--exclude-all-symbols')
    & $build.Compiler @testFlags (Join-Path $nativeDirectory 'BridgeCore.cpp') (Join-Path $nativeDirectory 'DeadlineWidgetBridge.cpp') '-lshell32' '-lole32' '-luuid' '-lkernel32' '-o' $testDll
    if ($LASTEXITCODE -ne 0) { throw "Test-only bridge compilation failed: $LASTEXITCODE" }
    $objdumpPath = Join-Path (Split-Path $build.Compiler -Parent) 'objdump.exe'
    $testExportText = (& $objdumpPath '-p' $testDll | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Test-only bridge export inspection failed.' }
    $testExports = @(Get-BridgeExportNames -Text $testExportText -Format ObjDump)
    $testBytes = [IO.File]::ReadAllBytes($testDll)
    $testPeOffset = [BitConverter]::ToInt32($testBytes, 0x3c)
    if ([BitConverter]::ToUInt16($testBytes, $testPeOffset + 4) -ne 0x8664 -or
        [BitConverter]::ToUInt16($testBytes, $testPeOffset + 24) -ne 0x20b) {
        throw 'Expected test-only bridge to remain PE32+ x86-64.'
    }
    $harness = Join-Path $testDirectory 'BridgeHarness.exe'
    & $build.Compiler '-std=c++17' '-Wall' '-Wextra' '-Werror' '-DUNICODE' '-D_UNICODE' '-municode' '-static-libgcc' '-static-libstdc++' (Join-Path $nativeDirectory 'BridgeHarness.cpp') (Join-Path $nativeDirectory 'BridgeCore.cpp') (Join-Path $nativeDirectory 'DeadlineWidgetBridge.cpp') '-lshell32' '-lole32' '-luuid' '-lkernel32' '-o' $harness
    if ($LASTEXITCODE -ne 0) { throw "Harness compilation failed: $LASTEXITCODE" }
    $env:LOCALAPPDATA = Join-Path $testDirectory 'Local Data 测试'
    [void](New-Item -ItemType Directory -Path $env:LOCALAPPDATA)
    & $harness $testDll
    if ($LASTEXITCODE -ne 0) { throw "Native contract failed: $LASTEXITCODE" }
    $diagnosticPath = Join-Path $env:LOCALAPPDATA 'DeadlineWidgetV2/logs/bridge.log'
    $firstLines = @(Get-Content -LiteralPath $diagnosticPath -Encoding utf8)
    if ($firstLines.Count -ne 2) { throw 'Expected two diagnostic lines, with embedded newlines escaped.' }
    if ($firstLines[0] -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z command="Other\\r\\nInjected" status=2 error=87$') { throw 'Invalid timestamp/unknown-command diagnostic.' }
    if ($firstLines[1] -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z command="OpenManager" status=3 error=2$') { throw 'Invalid timestamp/missing-manager diagnostic.' }
    [void](New-Item -ItemType Directory -Path (Join-Path $env:LOCALAPPDATA 'DeadlineWidgetV2/Manager/DeadlineWidget.Manager.exe') -Force)
    & $harness $testDll
    if ($LASTEXITCODE -ne 0) { throw "Directory rejection/append repeat failed: $LASTEXITCODE" }
    $secondLines = @(Get-Content -LiteralPath $diagnosticPath -Encoding utf8)
    if ($secondLines.Count -ne 4 -or $secondLines[0] -cne $firstLines[0] -or $secondLines[1] -cne $firstLines[1]) { throw 'Diagnostics must append without overwriting.' }
    $outputs = @(Get-ChildItem -LiteralPath $env:LOCALAPPDATA -Recurse -File)
    if ($outputs.Count -ne 1 -or $outputs[0].FullName -ne $diagnosticPath) { throw 'Bridge wrote a file outside its sole log destination.' }
    Write-Host 'PASS: 5 diagnostic checks; absent and directory manager paths rejected'
    Write-Host "Test-only exports ($($testExports.Count)): $($testExports -join ', ')"
    Write-Host 'Test-only architecture: PE32+ x86-64'
    Write-Host "Exports ($($build.Exports.Count)): $($build.Exports -join ', ')"
    Write-Host "Architecture: $($build.Architecture)"
} finally {
    $env:LOCALAPPDATA = $savedLocalAppData
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTestDirectory.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolvedTestDirectory -Leaf) -notlike 'DeadlineWidgetBridge.Tests-*') { throw 'Refusing cleanup outside the test temporary directory.' }
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
}
