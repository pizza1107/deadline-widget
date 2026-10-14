# Unicode Snapshot and Windows CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Rainmeter snapshot Unicode-safe end to end and establish Windows CI for .NET, native bridge, and packaging validation.

**Architecture:** `SnapshotPublisher` writes a version-1 JSON document as strict UTF-8 with BOM through the existing atomic file boundary. The Lua renderer strips the BOM before decoding; packaged Rainmeter files use their required encodings. CI runs the existing test layers once each and passes an explicit skip switch to nested package builds to avoid repeating solution tests.

**Tech Stack:** .NET 8, C# / MSTest, Rainmeter Lua, PowerShell 5.1, C++17 / MinGW-w64, GitHub Actions Windows runner.

## Global Constraints

- Do not modify or delete real files under `%LOCALAPPDATA%`, `%APPDATA%`, or repository `recovery/`.
- Preserve snapshot schema version `1`, JSON field names, date/time semantics, sorting, and atomic replacement behavior.
- Production changes follow RED-GREEN-REFACTOR; capture the expected failing output before implementation.
- Do not push, create a PR, create a tag, or publish a Release.
- Do not commit `bin/`, `obj/`, `artifacts/`, logs, temporary files, or real deadline data.

---

### Task 1: Reproduce the ANSI snapshot failure and publish strict UTF-8

**Files:**
- Modify: `tests/DeadlineWidget.Manager.Tests/SnapshotPublisherTests.cs`
- Modify: `src/DeadlineWidget.Manager/SnapshotPublisher.cs`

**Interfaces:**
- Consumes: `SnapshotPublisher.PublishAsync(IReadOnlyCollection<Deadline>, DateTime, CancellationToken)`.
- Produces: `display.json` bytes beginning with `EF BB BF`, followed by strict UTF-8 JSON.

- [ ] **Step 1: Add a failing Unicode test**

Replace the ANSI-oriented test with a UTF-8 contract test whose title is exactly `课程 😀 𠮷 café`, assert the BOM, decode the payload with `new UTF8Encoding(false, true)`, and verify the title through `JsonDocument`:

```csharp
[TestMethod]
public async Task Publish_WritesBomMarkedUtf8SnapshotWithoutLosingUnicode()
{
    var fileSystem = new FakeSnapshotFileSystem();
    var publisher = new SnapshotPublisher("X:\\DeadlineWidgetV2", fileSystem, new DeadlineClock());
    var title = "课程 😀 𠮷 café";
    var deadline = new Deadline(Guid.NewGuid(), title, Now.AddDays(5), false, Now, Now, null);

    await publisher.PublishAsync([deadline], Now);

    CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, fileSystem.DestinationBytes![..3]);
    var json = new UTF8Encoding(false, true).GetString(fileSystem.DestinationBytes[3..]);
    using var document = JsonDocument.Parse(json);
    Assert.AreEqual(title, document.RootElement.GetProperty("items")[0].GetProperty("title").GetString());
}
```

- [ ] **Step 2: Run the targeted test and verify RED**

Run:

```powershell
dotnet test .\tests\DeadlineWidget.Manager.Tests\DeadlineWidget.Manager.Tests.csproj `
  -c Release --filter Publish_WritesBomMarkedUtf8SnapshotWithoutLosingUnicode
```

Expected: FAIL because the current `GetACP()` encoder throws `EncoderFallbackException` for the emoji or supplementary-plane character.

- [ ] **Step 3: Implement the minimal UTF-8 encoder**

Remove `System.Runtime.InteropServices`, `Encoding.RegisterProvider`, `GetACP`, and the ANSI encoder. Add a single encoding field and prepend its preamble:

```csharp
private static readonly UTF8Encoding SnapshotEncoding = new(
    encoderShouldEmitUTF8Identifier: true,
    throwOnInvalidBytes: true);

var jsonBytes = SnapshotEncoding.GetBytes(JsonSerializer.Serialize(document, JsonOptions));
var preamble = SnapshotEncoding.GetPreamble();
var bytes = new byte[preamble.Length + jsonBytes.Length];
preamble.CopyTo(bytes, 0);
jsonBytes.CopyTo(bytes, preamble.Length);
```

- [ ] **Step 4: Verify GREEN and existing atomic behavior**

Run:

```powershell
dotnet test .\tests\DeadlineWidget.Manager.Tests\DeadlineWidget.Manager.Tests.csproj -c Release
```

Expected: all Manager tests pass, including atomic replacement failure coverage.

- [ ] **Step 5: Commit the isolated publisher change**

```powershell
git add src/DeadlineWidget.Manager/SnapshotPublisher.cs tests/DeadlineWidget.Manager.Tests/SnapshotPublisherTests.cs
git commit -m "fix: publish Rainmeter snapshots as UTF-8"
```

### Task 2: Make Lua consume BOM-marked UTF-8 snapshots

**Files:**
- Modify: `tests/DeadlineWidget.Skin.Tests/SkinContractTests.cs`
- Modify: `rainmeter/Skins/DeadlineWidgetV2/@Resources/DeadlineWidgetV2.lua`

**Interfaces:**
- Consumes: raw bytes returned by `readSnapshot()`.
- Produces: BOM-free UTF-8 JSON text passed to `DeadlineWidgetJson.decode`.

- [ ] **Step 1: Add failing Lua integration cases**

Add tests that call the real MoonSharp renderer with:

```csharp
var directUtf8 = "\uFEFF{\"version\":1,\"generatedAt\":\"2026-09-16T12:00:00\",\"items\":[{" +
    "\"id\":\"00000000-0000-0000-0000-000000000001\"," +
    "\"title\":\"课程 😀 𠮷 café\",\"dueAt\":\"2026-09-17T12:00:00\",\"isCompleted\":false}]}";
var escapedUnicode = "{\"version\":1,\"generatedAt\":\"2026-09-16T12:00:00\",\"items\":[{" +
    "\"id\":\"00000000-0000-0000-0000-000000000002\"," +
    "\"title\":\"\\u8BFE\\u7A0B \\uD83D\\uDE00\",\"dueAt\":\"2026-09-17T12:00:00\",\"isCompleted\":false}]}";
```

Assert the first row titles are respectively `课程 😀 𠮷 café` and `课程 😀`.

- [ ] **Step 2: Run the targeted Skin tests and verify RED**

Run:

```powershell
dotnet test .\tests\DeadlineWidget.Skin.Tests\DeadlineWidget.Skin.Tests.csproj `
  -c Release --filter "FullyQualifiedName~Utf8|FullyQualifiedName~UnicodeEscape"
```

Expected: the BOM case returns the skin error state because `json.lua` sees BOM bytes before `{`.

- [ ] **Step 3: Strip only a leading UTF-8 BOM in Lua**

Add immediately before JSON decoding:

```lua
local function stripUtf8Bom(text)
    if string.sub(text, 1, 3) == "\239\187\191" then
        return string.sub(text, 4)
    end
    return text
end
```

Change the decoder call to:

```lua
return ensureDecoder().decode(stripUtf8Bom(snapshotText))
```

- [ ] **Step 4: Run all Skin tests**

```powershell
dotnet test .\tests\DeadlineWidget.Skin.Tests\DeadlineWidget.Skin.Tests.csproj -c Release
```

Expected: all Skin tests pass with existing timezone and countdown assertions unchanged.

- [ ] **Step 5: Commit the renderer change**

```powershell
git add rainmeter/Skins/DeadlineWidgetV2/@Resources/DeadlineWidgetV2.lua tests/DeadlineWidget.Skin.Tests/SkinContractTests.cs
git commit -m "fix: decode UTF-8 Rainmeter snapshots"
```

### Task 3: Enforce Rainmeter package encodings

**Files:**
- Modify: `tests/Packaging.Tests.ps1`
- Modify: `scripts/Build-Package.ps1`

**Interfaces:**
- Consumes: UTF-8 repository source files.
- Produces: UTF-16 LE BOM `.ini`, UTF-16 LE BOM `DeadlineWidgetV2.lua`, and ASCII-compatible `json.lua` inside `.rmskin`.

- [ ] **Step 1: Replace the old ANSI package assertions with explicit file contracts**

Assert:

```powershell
$mainLuaBytes = [IO.File]::ReadAllBytes((Join-Path $extractRoot 'Skins/DeadlineWidgetV2/@Resources/DeadlineWidgetV2.lua'))
Assert-BytesEqual ([byte[]](0xff, 0xfe)) ([byte[]]$mainLuaBytes[0..1]) 'Main Rainmeter Lua must be UTF-16 LE with BOM.'
$jsonLuaBytes = [IO.File]::ReadAllBytes((Join-Path $extractRoot 'Skins/DeadlineWidgetV2/@Resources/json.lua'))
Assert-True (-not ($jsonLuaBytes[0] -eq 0xff -and $jsonLuaBytes[1] -eq 0xfe)) 'JSON byte decoder must remain single-byte source.'
Assert-True (($jsonLuaBytes | Where-Object { $_ -gt 0x7f }).Count -eq 0) 'JSON helper must remain ASCII-compatible.'
```

- [ ] **Step 2: Run Packaging tests and verify RED**

```powershell
.\tests\Packaging.Tests.ps1
```

Expected: FAIL because the current build converts every Lua file to the active ANSI code page.

- [ ] **Step 3: Implement filename-specific staging**

Remove `AnsiCodePage`, `GetACP`, and the blanket Lua conversion. Write the main renderer using the existing UTF-16 encoder and require every other Lua helper to be ASCII before copying its original bytes:

```powershell
$mainRenderer = Join-Path $skinStage '@Resources/DeadlineWidgetV2.lua'
[IO.File]::WriteAllText($mainRenderer, [IO.File]::ReadAllText($mainRenderer), $unicode)
$jsonHelper = Join-Path $skinStage '@Resources/json.lua'
$jsonBytes = [IO.File]::ReadAllBytes($jsonHelper)
if (@($jsonBytes | Where-Object { $_ -gt 0x7f }).Count -ne 0) {
    throw 'json.lua must remain ASCII-compatible for byte-oriented UTF-8 decoding.'
}
```

Update Packaging test invocations that previously supplied `-AnsiCodePage 936`; the parameter no longer exists, and encoding expectations are now filename-specific rather than machine-code-page-specific.

- [ ] **Step 4: Re-run Packaging tests**

```powershell
.\tests\Packaging.Tests.ps1
```

Expected: all packaging and isolated-install assertions pass.

- [ ] **Step 5: Commit the packaging encoding contract**

```powershell
git add scripts/Build-Package.ps1 tests/Packaging.Tests.ps1
git commit -m "build: enforce Rainmeter Unicode encodings"
```

### Task 4: Avoid nested solution-test duplication

**Files:**
- Modify: `tests/Packaging.Tests.ps1`
- Modify: `scripts/Build-Package.ps1`

**Interfaces:**
- Produces: `Build-Package.ps1 -SkipSolutionTests`, defaulting to false.

- [ ] **Step 1: Add a failing packaging contract assertion**

Require the build script to declare a switch and require the test harness to pass it:

```powershell
Assert-True ($buildScriptText -match '\[switch\]\$SkipSolutionTests') 'Build-Package must expose -SkipSolutionTests.'
Assert-True ($packagingTestText -match 'SkipSolutionTests\s*=\s*\$true') 'Packaging tests must avoid nested solution tests.'
```

- [ ] **Step 2: Run the assertion and verify RED**

```powershell
.\tests\Packaging.Tests.ps1
```

Expected: FAIL because the switch does not exist.

- [ ] **Step 3: Add the switch and guard the existing test command**

```powershell
param(
    [switch]$SkipSolutionTests
)

if (-not $SkipSolutionTests) {
    & $DotNetPath test $solutionPath -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "V2 solution tests failed: $LASTEXITCODE" }
}
```

Add `SkipSolutionTests = $true` to the Packaging test build arguments.

- [ ] **Step 4: Verify default and orchestrated behavior**

```powershell
dotnet test .\DeadlineWidgetV2.sln -c Release
.\tests\Packaging.Tests.ps1
```

Expected: solution tests run once explicitly; Packaging tests pass without a nested solution run.

- [ ] **Step 5: Commit the orchestration change**

```powershell
git add scripts/Build-Package.ps1 tests/Packaging.Tests.ps1
git commit -m "build: avoid duplicate solution tests"
```

### Task 5: Add Windows continuous integration

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: PowerShell test scripts and MinGW compiler path.
- Produces: required Windows validation on pushes and pull requests.

- [ ] **Step 1: Create the workflow**

Use this job structure:

```yaml
name: CI

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

permissions:
  contents: read

jobs:
  windows:
    runs-on: windows-2022
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - uses: msys2/setup-msys2@v2
        with:
          msystem: MINGW64
          update: false
          install: mingw-w64-x86_64-gcc
      - name: Restore
        shell: pwsh
        run: dotnet restore .\DeadlineWidgetV2.sln
      - name: Test .NET and Lua
        shell: pwsh
        run: dotnet test .\DeadlineWidgetV2.sln -c Release --no-restore
      - name: Test native bridge
        shell: pwsh
        run: .\tests\Bridge.Tests.ps1 -CompilerPath C:\msys64\mingw64\bin\g++.exe
      - name: Test packaging and isolated install
        shell: pwsh
        run: .\tests\Packaging.Tests.ps1 -DotNetPath (Get-Command dotnet).Source -CompilerPath C:\msys64\mingw64\bin\g++.exe
```

- [ ] **Step 2: Review action pinning and permissions**

Confirm every job has only `contents: read`, no secrets, no write permissions, no real Rainmeter installation, and no absolute path tied to a developer account.

- [ ] **Step 3: Run the exact CI commands locally**

```powershell
dotnet restore .\DeadlineWidgetV2.sln
dotnet test .\DeadlineWidgetV2.sln -c Release --no-restore
.\tests\Bridge.Tests.ps1 -CompilerPath C:\x86_64-15.2.0-release-win32-seh-ucrt-rt_v13-rev1\mingw64\bin\x86_64-w64-mingw32-g++.exe
.\tests\Packaging.Tests.ps1 -DotNetPath (Get-Command dotnet).Source -CompilerPath C:\x86_64-15.2.0-release-win32-seh-ucrt-rt_v13-rev1\mingw64\bin\x86_64-w64-mingw32-g++.exe
```

Expected: every command exits 0. The local compiler path is verification input only and must not be written into tracked files.

- [ ] **Step 4: Inspect the phase diff and repository boundary**

```powershell
git diff --check
git status --short
git diff -- . ':!docs/superpowers'
git ls-files | rg '(^|/)(bin|obj|artifacts)/|deadlines(\.backup)?\.json|bridge\.log'
```

Expected: no generated output, user data, or logs are tracked.

- [ ] **Step 5: Commit CI**

```powershell
git add .github/workflows/ci.yml
git commit -m "ci: add Windows validation workflow"
```

