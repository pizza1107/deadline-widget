# Deadline Widget

一个简洁的 Windows 课程截止日期桌面小组件。它使用 Rainmeter 在桌面显示任务，并通过独立的管理窗口新增和修改 deadline；所有数据都保存在本机，不需要账号或网络连接。

## 功能特点

- 以紧凑列表展示课程任务，适合同时查看 3–8 个 deadline。
- 根据精确剩余时间自动显示浅灰、淡黄或淡红色。
- 在管理窗口中新增、编辑、完成、恢复和删除任务。
- Rainmeter 负责桌面层显示，使用“显示桌面”时小组件仍然可见。
- 数据完全离线保存，不上传课程或任务信息。

## 工作方式

Rainmeter 负责绘制桌面上的截止日期卡片。点击右上角 `＋` 会唤起独立的 DeadlineWidget 管理窗口，在那里编辑任务。

关闭管理窗口只会隐藏窗口，后台管理器仍会保持运行，以便小组件继续读取数据并响应下一次点击。重新登录 Windows 后，管理器会通过启动项在后台启动。

## 使用要求

- 64 位 Windows 10 或 Windows 11
- Rainmeter 4.5 或更高版本

## 安装

项目目前还没有提供可供普通用户直接下载的 GitHub Release 安装包。正式发布后，只需安装 Rainmeter，再运行随 Release 提供的安装程序即可。

在正式安装包发布之前，不建议普通用户运行仓库中的开发脚本；这些脚本用于从源码构建和验证项目。

## 数据与隐私

Deadline 数据保存在当前 Windows 用户的 `%LOCALAPPDATA%\DeadlineWidgetV2` 目录中，包括主数据、最近一次备份和供 Rainmeter 读取的显示快照。

程序不会把数据上传到网络，本仓库也不包含作者个人的 deadline 数据。重新安装或更新程序时，安装脚本会保留已有任务数据。

## 时间与颜色

颜色根据本机当前时间和截止时间之间的精确时长计算：

| 剩余时间 | 显示状态 |
| --- | --- |
| 大于 10 天 | 浅灰色 |
| 大于 3 天且不超过 10 天 | 淡黄色 |
| 大于等于 0 且不超过 3 天 | 淡红色 |
| 已超过截止时间 | 降低透明度并显示“已过期” |

## 开发者运行

从源码开发需要 .NET 8 SDK、PowerShell、Rainmeter 和可生成 64 位 Windows DLL 的 C++ 编译器。构建出的管理器为自包含版本，普通用户不需要另外安装 .NET 运行时。

在仓库根目录运行测试：

```powershell
dotnet test .\DeadlineWidgetV2.sln -c Release
```

构建 Rainmeter 皮肤、桥接插件和自包含管理器：

```powershell
& .\scripts\Build-Package.ps1 `
  -DotNetPath (Get-Command dotnet).Source `
  -CompilerPath (Get-Command x86_64-w64-mingw32-g++).Source
```

构建成功后，发布文件位于 `artifacts`。在测试电脑上安装并验证：

```powershell
& .\scripts\Install-V2.ps1
```

`Build-Package.ps1` 会先运行自动化测试，再生成管理器、Rainmeter 皮肤和桥接插件；`Install-V2.ps1` 会安装这些文件，同时保留已有的 deadline 数据。

## 当前限制

- 仅支持 Windows。
- 暂不提供系统通知。
- 暂不提供账号、云同步或多设备同步。
- 暂不支持从课程表或日历导入任务。
