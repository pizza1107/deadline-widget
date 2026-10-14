# Runtime Maintenance and Repository Showcase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve Rainmeter discovery, preserve actionable diagnostics with bounded logs, add safe uninstall behavior, and present the repository clearly to users and recruiters.

**Architecture:** Small injectable probes isolate Windows process/registry discovery from refresh execution. A minimal diagnostic sink records previously swallowed exceptions without logging user content and rotates at 1 MiB. Uninstall logic operates only on validated known paths and preserves data unless the caller explicitly opts in to deletion.

**Tech Stack:** .NET 8 / WPF, Windows Registry and process APIs, C++17 Win32 APIs, PowerShell, Markdown.

## Global Constraints

- Do not log deadline titles, JSON bodies, snapshot contents, or user data paths beyond the application root needed for diagnostics.
- Preserve current non-fatal behavior for activation and Rainmeter refresh failures.
- Default uninstall must preserve all deadline data, migration backups, recovery material, and logs.
- Every production behavior change begins with a failing automated test.
- Do not push, create a PR, create a tag, or publish a Release.

---

### Task 1: Discover custom Rainmeter installations

**Files:**
- Create: `src/DeadlineWidget.Manager/RainmeterLocator.cs`
- Modify: `src/DeadlineWidget.Manager/RainmeterRefresher.cs`
- Create: `tests/DeadlineWidget.Manager.Tests/RainmeterLocatorTests.cs`
- Modify: `tests/DeadlineWidget.Manager.Tests/RainmeterRefresherTests.cs`

**Interfaces:**
- Produces: `IRainmeterLocator.FindExecutable()` returning an existing executable path or null.
- Consumes: `IRainmeterProbe` methods for running process, uninstall locations, special folders, and file existence.

- [ ] **Step 1: Write failing locator priority tests**

Use a fake probe and assert this order:

```csharp
[TestMethod]
public void FindExecutable_PrefersRunningProcessThenRegistryThenStandardFolders()
{
    var probe = new FakeRainmeterProbe
    {
        RunningExecutable = @"D:\\Portable\\Rainmeter.exe",
        ExistingFiles = { @"D:\\Portable\\Rainmeter.exe", @"E:\\Apps\\Rainmeter\\Rainmeter.exe" },
        RegistryLocations = { @"E:\\Apps\\Rainmeter" }
    };

    Assert.AreEqual(@"D:\\Portable\\Rainmeter.exe", new RainmeterLocator(probe).FindExecutable());
}
```

Add separate tests for registry fallback, Program Files fallback, x86 fallback, invalid candidates, and no installation.

- [ ] **Step 2: Run targeted tests and verify RED**

```powershell
dotnet test .\tests\DeadlineWidget.Manager.Tests\DeadlineWidget.Manager.Tests.csproj `
  -c Release --filter FullyQualifiedName~RainmeterLocatorTests
```

Expected: compile failure because the locator interfaces do not exist.

- [ ] **Step 3: Implement the smallest locator**

Define:

```csharp
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
```

Normalize candidates with `Path.GetFullPath`, append `Rainmeter.exe` only to directory candidates, and return the first existing file.

- [ ] **Step 4: Update refresher and tests**

Change `RainmeterRefresher` to request a path from `IRainmeterLocator`; retain an explicit-path constructor for deterministic tests. Missing Rainmeter remains a non-fatal warning.

- [ ] **Step 5: Run Manager tests and commit**

```powershell
dotnet test .\tests\DeadlineWidget.Manager.Tests\DeadlineWidget.Manager.Tests.csproj -c Release
git add src/DeadlineWidget.Manager tests/DeadlineWidget.Manager.Tests
git commit -m "fix: discover custom Rainmeter installations"
```

### Task 2: Record activation failures in a bounded Manager log

**Files:**
- Create: `src/DeadlineWidget.Manager/DiagnosticSink.cs`
- Modify: `src/DeadlineWidget.Manager/App.xaml.cs`
- Modify: `src/DeadlineWidget.Manager/SingleInstanceCoordinator.cs`
- Modify: `src/DeadlineWidget.Manager/WakeupFileWatcher.cs`
- Create: `tests/DeadlineWidget.Manager.Tests/DiagnosticSinkTests.cs`
- Modify: `tests/DeadlineWidget.Manager.Tests/SingleInstanceCoordinatorTests.cs`
- Modify: `tests/DeadlineWidget.Manager.Tests/WakeupFileWatcherTests.cs`

**Interfaces:**
- Produces: `IDiagnosticSink.Write(string component, Exception exception)`.
- Log contract: UTF-8, maximum active size 1 MiB, one `.1` backup, no deadline content.

- [ ] **Step 1: Write failing exception-observation tests**

Inject an in-memory sink, make activation throw `new IOException("activation failed")`, and assert one entry with component `single-instance` or `wakeup-watcher`. Assert the activation path remains non-throwing.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test .\tests\DeadlineWidget.Manager.Tests\DeadlineWidget.Manager.Tests.csproj `
  -c Release --filter "FullyQualifiedName~SingleInstanceCoordinatorTests|FullyQualifiedName~WakeupFileWatcherTests"
```

Expected: compile failure because `IDiagnosticSink` and constructor parameters do not exist.

- [ ] **Step 3: Implement diagnostic injection and remove empty catches**

Define:

```csharp
public interface IDiagnosticSink
{
    void Write(string component, Exception exception);
}
```

In both activation components, catch `Exception exception`, call the sink inside a secondary non-throwing guard, and preserve the original completion behavior.

- [ ] **Step 4: Test file logging and rotation**

In a temporary directory, pre-create a log larger than `1_048_576` bytes, write one entry, and assert:

- active log contains UTC timestamp, component, exception type, and message;
- `.1` contains the previous bytes;
- no `.2` exists;
- UTF-8 strict decoding succeeds.

- [ ] **Step 5: Wire the default sink and run Manager tests**

Create one sink in `App.OnStartup` rooted at `%LOCALAPPDATA%\DeadlineWidgetV2\logs`, pass it to both components, and run:

```powershell
dotnet test .\tests\DeadlineWidget.Manager.Tests\DeadlineWidget.Manager.Tests.csproj -c Release
```

- [ ] **Step 6: Commit**

```powershell
git add src/DeadlineWidget.Manager tests/DeadlineWidget.Manager.Tests
git commit -m "fix: preserve manager activation diagnostics"
```

### Task 3: Bound the native bridge log

**Files:**
- Modify: `native/DeadlineWidgetBridge/DeadlineWidgetBridge.cpp`
- Modify: `tests/Bridge.Tests.ps1`

**Interfaces:**
- Produces: `bridge.log` no larger than approximately 1 MiB before rotation and at most one `bridge.log.1` backup.

- [ ] **Step 1: Add a failing oversized-log fixture**

Before the second harness run, write more than `1_048_576` bytes to `bridge.log`. After the bridge writes, assert old bytes moved to `bridge.log.1`, active log contains the new diagnostic, and only those two log files exist.

- [ ] **Step 2: Run Bridge tests and verify RED**

```powershell
.\tests\Bridge.Tests.ps1
```

Expected: FAIL because the current native code always appends.

- [ ] **Step 3: Implement one-generation rotation**

Before opening with `FILE_APPEND_DATA`, inspect size with `GetFileAttributesExW`. At or above `1_048_576` bytes, close any handle, call:

```cpp
MoveFileExW(filePath.c_str(), backupPath.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
```

Then open and append the new UTF-8 diagnostic. Logging failure must never change bridge launch status.

- [ ] **Step 4: Re-run native tests and commit**

```powershell
.\tests\Bridge.Tests.ps1
git add native/DeadlineWidgetBridge/DeadlineWidgetBridge.cpp tests/Bridge.Tests.ps1
git commit -m "fix: rotate bridge diagnostics"
```

### Task 4: Add a data-preserving uninstaller

**Files:**
- Create: `scripts/Uninstall-V2.ps1`
- Modify: `tests/Packaging.Tests.ps1`
- Modify: `scripts/Build-Package.ps1`

**Interfaces:**
- Produces: `Uninstall-V2.ps1 [-RemoveUserData]`.
- Default preserves deadline files, migration backups, logs, and legacy V1 directories.

- [ ] **Step 1: Add failing isolated uninstall tests**

Create isolated Manager, skin, plugin, root config, shortcut, `deadlines.json`, `deadlines.backup.json`, `migration-backup`, and logs. Run the missing script and assert the default behavior removes only program components while preserving data. Run a second fixture with `-RemoveUserData` and assert only the validated V2 data root is removed. Assert legacy data and unrelated skins remain byte-identical.

- [ ] **Step 2: Verify RED**

```powershell
.\tests\Packaging.Tests.ps1
```

Expected: FAIL because `Uninstall-V2.ps1` is absent.

- [ ] **Step 3: Implement validated deletion boundaries**

Accept the same overridable roots used by installation tests. Resolve each absolute target, verify it is an exact child of its expected root, then remove only:

```text
%LOCALAPPDATA%\DeadlineWidgetV2\Manager
<SkinRoot>\DeadlineWidgetV2
<PluginRoot>\DeadlineWidgetBridge.dll
<PluginRoot>\DeadlineWidgetBridge.root
<StartupRoot>\DeadlineWidgetV2 Manager.lnk
```

With `-RemoveUserData`, remove `%LOCALAPPDATA%\DeadlineWidgetV2` only after program-component cleanup and exact-root validation.

- [ ] **Step 4: Include the uninstaller in the release bundle**

Copy the script beside `Install-V2.ps1` and update the release file whitelist.

- [ ] **Step 5: Run Packaging tests and commit**

```powershell
.\tests\Packaging.Tests.ps1
git add scripts/Uninstall-V2.ps1 scripts/Build-Package.ps1 tests/Packaging.Tests.ps1
git commit -m "feat: add data-preserving uninstaller"
```

### Task 5: Prepare documentation and policy files

**Files:**
- Modify: `README.md`
- Create: `LICENSE`
- Create: `CHANGELOG.md`
- Create: `CONTRIBUTING.md`
- Create: `SECURITY.md`
- Create: `docs/images/README.md`

**Interfaces:**
- Produces: public-facing project documentation without fabricated screenshots or personal data.

- [ ] **Step 1: Add MIT license**

Use the standard MIT text with:

```text
Copyright (c) 2026 Zhang Ziang
```

- [ ] **Step 2: Rewrite README around user and recruiter questions**

Required order:

1. title, one-sentence purpose, CI/platform/license badges;
2. feature highlights;
3. screenshot section stating that a privacy-safe screenshot is not yet included;
4. four-component architecture;
5. Release installation and SHA-256 verification;
6. upgrade and default data-preserving uninstall;
7. source prerequisites and exact commands;
8. test matrix with actual commands;
9. data locations, privacy, atomic backup/recovery;
10. limitations and roadmap.

Use this Release link:

```text
https://github.com/pizza1107/deadline-widget/releases/latest
```

Use this CI badge target:

```text
https://github.com/pizza1107/deadline-widget/actions/workflows/ci.yml
```

- [ ] **Step 3: Add concise supporting documents**

`CHANGELOG.md` uses `Unreleased` sections for Added, Changed, Fixed, and Security. `CONTRIBUTING.md` lists .NET 8, PowerShell, MinGW-w64, branching, and all three validation commands. `SECURITY.md` directs reporters to GitHub private vulnerability reporting and explicitly forbids including real deadline data. `docs/images/README.md` requires fictional tasks and removal of desktop/account identifiers.

- [ ] **Step 4: Verify documentation claims against scripts**

Check every command in README uses an existing path and parameter. Confirm no screenshot link points to a missing image; the placeholder is text only.

- [ ] **Step 5: Commit documentation**

```powershell
git add README.md LICENSE CHANGELOG.md CONTRIBUTING.md SECURITY.md docs/images/README.md
git commit -m "docs: prepare repository for public release"
```

### Task 6: Run final repository verification

**Files:**
- Verify only; modify only if a failing test identifies a scoped defect.

**Interfaces:**
- Produces: evidence-backed completion report and recommended commit list.

- [ ] **Step 1: Run all validation commands**

```powershell
dotnet restore .\DeadlineWidgetV2.sln --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet test .\DeadlineWidgetV2.sln -c Release --no-restore
.\tests\Bridge.Tests.ps1
.\tests\Packaging.Tests.ps1
```

- [ ] **Step 2: Run repository hygiene scans**

```powershell
git diff --check
git status --short
git ls-files | rg '(^|/)(bin|obj|artifacts|\.nuget)/|deadlines(\.backup)?\.json|bridge\.log|manager\.log'
git grep -n -I -E 'C:\\Users\\|C:/Users/|u3666248@connect\.hku\.hk|api[_-]?key|access[_-]?token|client[_-]?secret'
```

Expected: generated/user-data scan is empty; private-path/secret scan is empty except intentional generic documentation terms, which must be reviewed manually.

- [ ] **Step 3: Review history and diff without publishing**

```powershell
git log --oneline --decorate -10
git diff origin/master...HEAD --stat
git diff origin/master...HEAD
```

Confirm no tag exists for this work and no Release was created.

- [ ] **Step 4: Produce the final report**

Report modifications, mitigated risks, tests added/changed, every validation command and result, manual limitations, and the actual commit sequence. Do not push.

