[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/Plugins/64bit'),
    [string]$CompilerPath
)
$ErrorActionPreference = 'Stop'

function Get-BridgeExportNames {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text, [ValidateSet('ObjDump', 'DumpBin')][string]$Format)
    $names = [Collections.Generic.List[string]]::new()
    $ordinals = [Collections.Generic.HashSet[int]]::new()
    if ($Format -eq 'ObjDump') {
        $counts = [regex]::Matches($Text, '(?m)^Number in:[ \t]*\r?\n[ \t]+Export Address Table[ \t]+([0-9a-fA-F]+)[ \t]*\r?\n[ \t]+\[Name Pointer/Ordinal\] Table[ \t]+([0-9a-fA-F]+)[ \t]*\r?$')
        if ($counts.Count -ne 1) { throw 'Expected one complete objdump export-count header.' }
        $declaredAddresses = [Convert]::ToInt32($counts[0].Groups[1].Value, 16)
        $declaredNames = [Convert]::ToInt32($counts[0].Groups[2].Value, 16)
        $addresses = [Collections.Generic.Dictionary[int,int]]::new()
        $namedSlots = [Collections.Generic.HashSet[int]]::new()
        $section = ''
        $addressTables = 0
        $nameTables = 0
        $ordinalBase = 0
        foreach ($rawLine in ($Text -split '\r?\n')) {
            $line = $rawLine.Trim()
            if ($line -match '^Export Address Table -- Ordinal Base (\d+)$') {
                $addressTables++
                $ordinalBase = [int]$Matches[1]
                $section = 'addresses'
                continue
            }
            if ($line -match '^\[Ordinal/Name Pointer\] Table(?: -- Ordinal Base (\d+))?$') {
                $nameTables++
                if ($Matches[1] -and [int]$Matches[1] -ne $ordinalBase) { throw 'Mismatched objdump ordinal bases.' }
                $section = 'names'
                continue
            }
            if (-not $section -or -not $line) { continue }
            if ($section -eq 'addresses') {
                if ($line -eq 'Ordinal  Address  Type' -or $line -match '^Ordinal\s+Address\s+Type$') { continue }
                if ($line -notmatch '^\[\s*(\d+)\]\s+\+base\[\s*(\d+)\]\s+([0-9a-fA-F]+)\s+Export RVA$') { throw "Unparsed or forwarded objdump address record: $line" }
                $slot = [int]$Matches[1]
                $ordinal = [int]$Matches[2]
                if ([Convert]::ToUInt64($Matches[3], 16) -eq 0 -or $ordinal -ne ($ordinalBase + $slot) -or $addresses.ContainsKey($slot) -or -not $ordinals.Add($ordinal)) { throw "Invalid or duplicate export ordinal: $line" }
                $addresses.Add($slot, $ordinal)
            } else {
                if ($line -match '^Ordinal\s+Hint\s+Name$') { continue }
                if ($line -match '^The (Function Table|PE File Base Relocations|Debug Directory)\b' -or $line -match '^PE File Base Relocations\b') { $section = ''; continue }
                if ($line -notmatch '^\[\s*(\d+)\]\s+(?:\+base\[\s*(\d+)\]\s+[0-9a-fA-F]+\s+)?(\S+)$') { throw "Unparsed objdump named export record: $line" }
                $slot = [int]$Matches[1]
                $ordinalText = $Matches[2]
                $name = $Matches[3]
                if (-not $addresses.ContainsKey($slot) -or -not $namedSlots.Add($slot) -or ($ordinalText -and [int]$ordinalText -ne $addresses[$slot])) { throw "Invalid named export ordinal: $line" }
                $names.Add($name)
            }
        }
        if ($addressTables -ne 1 -or $nameTables -ne 1 -or $declaredAddresses -ne $addresses.Count -or $declaredNames -ne $names.Count -or $addresses.Count -ne $names.Count) { throw 'Export counts disagree or an ordinal-only export is present.' }
    } else {
        $inside = $false
        $tables = 0
        foreach ($rawLine in ($Text -split '\r?\n')) {
            $line = $rawLine.Trim()
            if ($line -match '^ordinal\s+hint\s+RVA\s+name$') { $inside = $true; $tables++; continue }
            if (-not $inside -or -not $line) { continue }
            if ($line -eq 'Summary') { $inside = $false; continue }
            if ($line -notmatch '^(\d+)\s+(?:[0-9a-fA-F]+\s+)?[0-9a-fA-F]{8}\s+(\S+)$') { throw "Unparsed or forwarded dumpbin export record: $line" }
            if (-not $ordinals.Add([int]$Matches[1])) { throw 'Duplicate dumpbin export ordinal.' }
            $names.Add($Matches[2])
        }
        if ($tables -ne 1 -or $ordinals.Count -ne $names.Count) { throw 'Incomplete dumpbin export table.' }
    }
    $sortedNames = @($names | Sort-Object -CaseSensitive)
    if ($names.Count -ne 5 -or ($sortedNames -join ',') -cne 'ExecuteBang,Finalize,Initialize,Reload,Update') { throw "Expected exactly five bridge exports; found $($names.Count): $($sortedNames -join ', ')" }
    return $sortedNames
}

# Dot-sourcing exposes the same inspector used by the build for reusable validation.
if ($MyInvocation.InvocationName -eq '.') { return }

if (-not $CompilerPath) {
    $onPath = Get-Command 'g++.exe' -ErrorAction SilentlyContinue
    if ($onPath) { $CompilerPath = $onPath.Source }
}
if (-not $CompilerPath) {
    $installed = @(Get-ChildItem -LiteralPath 'C:\' -Directory -Filter 'x86_64*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'mingw64/bin/g++.exe' } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($installed.Count -eq 1) { $CompilerPath = $installed[0] }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Existing x64 MinGW-w64 g++.exe not found. Specify -CompilerPath.' }
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
$machine = (& $CompilerPath '-dumpmachine' | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $machine -cne 'x86_64-w64-mingw32') { throw "Expected an x64 MinGW-w64 compiler; found: $machine" }
$sourcePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'native/DeadlineWidgetBridge'
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$dll = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path 'DeadlineWidgetBridge.dll'
$flags = @('-std=c++17', '-O2', '-Wall', '-Wextra', '-Werror', '-DUNICODE', '-D_UNICODE', '-shared', '-static-libgcc', '-static-libstdc++', '-Wl,--exclude-all-symbols')
& $CompilerPath @flags (Join-Path $sourcePath 'BridgeCore.cpp') (Join-Path $sourcePath 'DeadlineWidgetBridge.cpp') '-lshell32' '-lole32' '-luuid' '-lkernel32' '-o' $dll
if ($LASTEXITCODE -ne 0) { throw "Bridge compilation failed: $LASTEXITCODE" }
$dumpbin = Get-Command 'dumpbin.exe' -ErrorAction SilentlyContinue
if ($dumpbin) {
    $table = (& $dumpbin.Source '/exports' $dll | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'dumpbin export inspection failed.' }
    $exports = @(Get-BridgeExportNames -Text $table -Format DumpBin)
} else {
    $objdump = Join-Path (Split-Path $CompilerPath -Parent) 'objdump.exe'
    if (-not (Test-Path -LiteralPath $objdump -PathType Leaf)) { throw 'Export inspector unavailable: neither dumpbin nor compiler-adjacent objdump exists.' }
    $table = (& $objdump '-p' $dll | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'objdump export inspection failed.' }
    $exports = @(Get-BridgeExportNames -Text $table -Format ObjDump)
}
$bytes = [IO.File]::ReadAllBytes($dll)
if ($bytes.Length -lt 64 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5a4d) { throw 'Invalid DOS/PE header.' }
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if ($peOffset -lt 64 -or $peOffset -gt ($bytes.Length - 26) -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550 -or
    [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664 -or [BitConverter]::ToUInt16($bytes, $peOffset + 24) -ne 0x20b) { throw 'Expected PE32+ x86-64 DLL.' }
Write-Host "Built $dll"
Write-Host "Verified PE32+ x86-64; exports ($($exports.Count)): $($exports -join ', ')"
[pscustomobject]@{ Dll = $dll; Compiler = $CompilerPath; Flags = $flags; Exports = $exports; Architecture = 'PE32+ x86-64' }
