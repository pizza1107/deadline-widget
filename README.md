# Deadline Widget

[![CI](https://github.com/pizza1107/deadline-widget/actions/workflows/ci.yml/badge.svg)](https://github.com/pizza1107/deadline-widget/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)](https://github.com/pizza1107/deadline-widget)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

一个面向大学课程的 Windows 桌面 deadline 小组件。Rainmeter 将任务固定在桌面层，独立的 WPF 管理器负责新增、编辑、完成和删除任务。程序不要求登录，任务数据只保存在本机。

## 功能

- 用 240 × 280 的紧凑卡片显示最多五条任务，并支持滚轮翻页。
- 按精确剩余时间显示普通、黄色、红色和过期状态。
- 通过独立管理窗口维护任务，关闭窗口后管理器可以继续在后台响应小组件。
- 支持中文、emoji、生僻字、JSON Unicode 转义和带时区的截止时间。
- 使用临时文件、原子替换、最近备份和损坏文件恢复目录保护任务数据。
- 安装、升级和默认卸载都保留已有任务。

## 截图

仓库暂时没有加入可公开使用的截图。截图发布前需要换成虚构课程和日期，并移除桌面、账号名称等个人信息。要求见 [docs/images/README.md](docs/images/README.md)。

## 架构

| 组件 | 作用 |
| --- | --- |
| C# Core | deadline 模型、时间状态、排序、本地 JSON 保存与恢复 |
| WPF Manager | 任务管理界面、单实例运行、启动唤醒和 Rainmeter 刷新 |
| C++ Rainmeter Bridge | 接收小组件点击并启动或唤醒管理器 |
| Lua Skin | 读取 UTF-8 快照，分页并渲染 Rainmeter 小组件 |

管理器修改任务后先保存主数据，再发布 Rainmeter 快照。皮肤只读取快照，不直接修改主数据。

## 安装

运行环境：

- 64 位 Windows 10 或 Windows 11
- Rainmeter 4.5 或更高版本

从 [最新 Release](https://github.com/pizza1107/deadline-widget/releases/latest) 下载完整的 `win-x64.zip` 和 `SHA256SUMS.txt`。解压 ZIP 后，在该目录打开 PowerShell：

```powershell
.\Install-V2.ps1
```

安装脚本会复制自包含的 Manager，安装 Rainmeter 皮肤和 x64 桥接 DLL，并创建当前用户的后台启动快捷方式。普通用户不需要另外安装 .NET 运行时。

可以在下载目录验证 SHA-256：

```powershell
Get-FileHash .\DeadlineWidgetV2-*-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

将 `Get-FileHash` 输出与 `SHA256SUMS.txt` 中对应文件的值逐字比较。

## 升级和卸载

升级时下载新的完整 ZIP，再运行其中的 `Install-V2.ps1`。安装器会替换程序组件，同时保留 deadline 主数据、备份和迁移材料。

默认卸载同样保留数据：

```powershell
.\Uninstall-V2.ps1
```

如果确定不再需要任何 V2 数据，可以显式删除：

```powershell
.\Uninstall-V2.ps1 -RemoveUserData
```

`-RemoveUserData` 会删除 `%LOCALAPPDATA%\DeadlineWidgetV2`，其中包括任务、备份、恢复材料和日志。它不会删除旧版 `CourseDeadlineWidget` 目录或其他 Rainmeter 皮肤。

## 从源码构建

需要安装：

- .NET 8 SDK。仓库通过 `global.json` 使用 8.0.425，并允许同一 feature band 的更新补丁。
- PowerShell 5.1 或 PowerShell 7。
- 可生成 Windows x64 DLL 的 MinGW-w64 `g++.exe`。
- Rainmeter 仅用于手动运行皮肤，自动化测试不要求安装 Rainmeter。

在仓库根目录执行：

```powershell
dotnet restore .\DeadlineWidgetV2.sln --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet test .\DeadlineWidgetV2.sln -c Release --no-restore

$compiler = (Get-Command g++.exe).Source
.\tests\Bridge.Tests.ps1 -CompilerPath $compiler
.\tests\Packaging.Tests.ps1 -DotNetPath (Get-Command dotnet).Source -CompilerPath $compiler
```

生成本地发布目录：

```powershell
.\scripts\Build-Package.ps1 `
  -Version 0.1.0 `
  -DotNetPath (Get-Command dotnet).Source `
  -CompilerPath $compiler
```

输出位于 `artifacts`。构建脚本默认先运行解决方案测试；已经单独运行测试时，可以传入 `-SkipSolutionTests`。

## 测试范围

| 测试层 | 命令 | 主要覆盖范围 |
| --- | --- | --- |
| C# 与 Lua | `dotnet test .\DeadlineWidgetV2.sln -c Release` | 时间边界、数据恢复、管理器行为、Unicode 快照、Lua 渲染 |
| 原生桥接 | `.\tests\Bridge.Tests.ps1 -CompilerPath $compiler` | x64 PE、导出表、启动状态、路径处理、日志轮转 |
| 打包与安装 | `.\tests\Packaging.Tests.ps1 -CompilerPath $compiler` | `.rmskin` 内容、文件编码、自包含发布、数据保护、安装回滚和卸载 |

GitHub Actions 在 Windows runner 上执行相同层级的验证。版本标签 `v<major>.<minor>.<patch>` 会在所有测试通过后构建 Release 产物和 SHA-256 文件。

## 数据与隐私

程序不包含账号、遥测或云同步，也不会上传课程信息。

| 位置 | 内容 |
| --- | --- |
| `%LOCALAPPDATA%\DeadlineWidgetV2\deadlines.json` | 主任务数据 |
| `%LOCALAPPDATA%\DeadlineWidgetV2\deadlines.backup.json` | 最近一份有效备份 |
| `%LOCALAPPDATA%\DeadlineWidgetV2\recovery\` | 无法解析的原始文件副本 |
| `%LOCALAPPDATA%\DeadlineWidgetV2\migration-backup\` | 旧版迁移前的校验备份 |
| `%LOCALAPPDATA%\DeadlineWidgetV2\logs\` | 有大小限制的 Manager 和 Bridge 诊断日志 |
| `%APPDATA%\Rainmeter\DeadlineWidgetV2\display.json` | Rainmeter 使用的 UTF-8 显示快照 |

保存任务时，程序先写入并刷新临时文件，再原子替换主文件。现有主文件有效时会先复制为备份；损坏文件不会被覆盖，而是复制到 `recovery` 后再尝试备份。

提交 issue 或安全报告时，不要上传真实的 `deadlines.json`、显示快照或包含个人路径的日志。

## 当前限制

- 只支持 Windows x64。
- 没有系统通知、账号、云同步或多设备同步。
- 不能从课程表、日历或学习平台自动导入任务。
- 每页固定显示五条任务，外观定制仍需要手动修改 Rainmeter 配置。
- 首个公开 Release 尚未创建，Release 链接会在发布后生效。

## 许可证

项目使用 [MIT License](LICENSE)。
