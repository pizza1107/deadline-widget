# Unicode Snapshot Reading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Rainmeter widget read its snapshot on Windows accounts whose profile path contains Unicode characters.

**Architecture:** Cache the UTF-8 snapshot in the existing native bridge using Win32 wide-character file APIs and expose it as a Rainmeter string measure. Make Lua consume that measure in production while retaining direct fixture files in test mode.

**Tech Stack:** C++17, Win32, Rainmeter plugin ABI, Lua, MSTest, PowerShell

## Global Constraints

- Continue to support Rainmeter 4.5.
- Preserve the snapshot schema and manager storage paths.
- Perform file I/O in `Update`; `GetString` only returns cached text.
- Treat missing, oversized, and invalid UTF-8 snapshots as unavailable data.

---

### Task 1: Route production snapshots through the native bridge

**Files:**
- Modify: `tests/DeadlineWidget.Skin.Tests/SkinContractTests.cs`
- Modify: `native/DeadlineWidgetBridge/BridgeCore.h`
- Modify: `native/DeadlineWidgetBridge/BridgeCore.cpp`
- Modify: `native/DeadlineWidgetBridge/DeadlineWidgetBridge.cpp`
- Modify: `native/DeadlineWidgetBridge/BridgeHarness.cpp`
- Modify: `scripts/Build-Bridge.ps1`
- Modify: `tests/Bridge.Tests.ps1`
- Modify: `rainmeter/Skins/DeadlineWidgetV2/@Resources/DeadlineWidgetV2.lua`

**Interfaces:**
- Consumes: `%APPDATA%\Rainmeter\DeadlineWidgetV2\display.json`
- Produces: `Controller::refreshSnapshot()`, `Controller::snapshot()`, and Rainmeter `GetString(void*)`

- [ ] **Step 1: Write a failing skin regression test**

Add a production-mode test whose `SnapshotPath` is an inaccessible Chinese profile path while `MeasureBridge:GetStringValue()` supplies valid JSON. Assert that the row and footer render from the bridge value.

- [ ] **Step 2: Run the focused skin test and verify RED**

Run `dotnet test tests/DeadlineWidget.Skin.Tests/DeadlineWidget.Skin.Tests.csproj -c Release --filter ProductionMode_UsesBridgeSnapshotInsteadOfOpeningUnicodePath`.

Expected: FAIL with `数据不可用`, because production Lua still calls `io.open`.

- [ ] **Step 3: Implement native snapshot caching and Lua consumption**

Read at most 4 MiB through `CreateFileW` and `ReadFile`, strip one UTF-8 BOM, reject invalid UTF-8, and cache the converted string during `Update`. Export `GetString` and change production `readSnapshot()` to use the bridge measure.

- [ ] **Step 4: Extend native and packaging contracts**

Require exactly `ExecuteBang,Finalize,GetString,Initialize,Reload,Update`; exercise the real DLL against a snapshot stored below a Unicode `%APPDATA%` test directory; keep PE32+ x64 checks.

- [ ] **Step 5: Verify GREEN and the full affected suite**

Run the focused skin test, full solution tests, `tests/Bridge.Tests.ps1`, and `tests/Packaging.Tests.ps1` with the configured x64 MinGW compiler.

- [ ] **Step 6: Commit**

Commit the source, tests, spec, and plan with `fix: read snapshots from Unicode Windows paths`.

