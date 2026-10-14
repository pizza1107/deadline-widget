# Changelog

本项目使用语义化版本号。尚未发布的改动记录在 `Unreleased` 下。

## Unreleased

### Added

- Windows CI、NuGet 锁文件和按版本标签运行的 Release 工作流。
- 自动构建的完整 Windows x64 发布包及 SHA-256 校验文件。
- 安全卸载脚本、Rainmeter 自定义安装位置探测和有大小限制的诊断日志。

### Changed

- Rainmeter 显示快照和读取链路统一使用 UTF-8。
- Rainmeter 主 Lua 文件在打包时转换为 UTF-16 LE，JSON 解码器保持 ASCII 字节语义。
- 发布包接受显式语义化版本号。

### Fixed

- 修复 Windows 当前代码页无法表示 emoji 或生僻字时桌面仍显示旧数据的问题。
- 修复诊断日志无限增长的问题。

### Security

- 启用 NuGet 全依赖漏洞审计，并在 CI 中把 NU1901 到 NU1904 视为错误。
- 锁定 .NET SDK 策略和 NuGet 依赖图。
