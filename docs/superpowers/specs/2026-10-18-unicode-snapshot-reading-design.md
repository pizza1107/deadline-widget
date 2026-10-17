# Unicode Snapshot Reading Design

## Problem

The manager writes `display.json` under Rainmeter's settings directory, but the skin opens that file with Lua `io.open`. On Windows accounts whose profile path contains non-ASCII characters, the manager can start while Lua fails to open the snapshot and shows `数据不可用`.

## Design

Keep Rainmeter 4.5 compatibility by extending the existing `DeadlineWidgetBridge` plugin. During each plugin update, it reads `%APPDATA%\Rainmeter\DeadlineWidgetV2\display.json` with wide-character Win32 file APIs, validates UTF-8, converts the contents to UTF-16, and exposes the cached value through Rainmeter's optional `GetString` callback. `GetString` performs no file I/O.

The Lua renderer obtains production snapshots from `MeasureBridge:GetStringValue()`. Its existing direct-file path remains available only in test mode so renderer tests can use isolated fixtures. Missing, oversized, or invalid UTF-8 snapshots return an empty bridge string and continue to render `数据不可用`.

## Verification

- A skin contract test proves production mode renders bridge-provided JSON even when `SnapshotPath` contains a Chinese Windows user name and cannot be opened.
- Native bridge tests verify the exact six callback exports and read a UTF-8 snapshot through a Unicode directory.
- Existing .NET, Lua, native bridge, and packaging tests remain green.

