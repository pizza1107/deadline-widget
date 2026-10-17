# Deadline Widget 工程质量完善设计

## 目标与范围

在不重写现有架构、不修改或删除用户 deadline 数据的前提下，将 Deadline Widget 提升到公开发布的工程质量。工作按三个可独立验证的阶段实施，优先修复 Unicode 快照缺陷并建立 Windows CI，再完善可复现构建与发布流程，最后处理低风险维护问题和仓库展示。


仓库采用 MIT License，版权声明为：

```text
Copyright (c) 2026 Zhang Ziang
```

## 当前基线

- 当前分支为 `master`，基线提交为 `00e1ff9`。
- 工作区在设计开始时没有未提交改动。
- 仓库没有 `AGENTS.md`、GitHub Actions、`global.json` 或 NuGet lock file。
- 当前解决方案包含 C# Core、WPF Manager、C++ Rainmeter Bridge、Lua Skin 及对应测试。
- 当前 `SnapshotPublisher` 使用 `GetACP()` 和异常回退把 JSON 转为 Windows 当前 ANSI 代码页。
- 三个测试项目均设置了 `NuGetAudit=false`。
- 当前依赖扫描未报告直接或间接依赖漏洞；测试依赖存在更新的大版本，但不在本轮盲目升级。

## 阶段一：Unicode 快照与 Windows CI

### Unicode 快照契约

Rainmeter 显示快照 `display.json` 固定使用带 BOM 的 UTF-8。快照 schema 继续使用版本 `1`，字段名、日期格式、时区语义、排序逻辑和原子替换行为均保持不变。主数据 `deadlines.json` 不迁移、不重写，已有用户数据不受影响。

C# 发布器移除当前 ANSI 代码页依赖，使用严格 UTF-8 编码并显式写入 `EF BB BF` BOM。临时文件、写穿、落盘刷新、替换/移动和失败清理逻辑保持现状。

Lua 读取外部快照后识别并移除 UTF-8 BOM，再交给 JSON 解码器。JSON 解码继续支持直接 UTF-8 内容与 `\uXXXX` 转义，包括 UTF-16 surrogate pair 表示的 emoji。Rainmeter 安装包中的主 Lua 脚本转换为 UTF-16 LE with BOM，以满足 Rainmeter Unicode 脚本要求；纯 ASCII 的 `json.lua` 保持单字节形式，以保留其逐字节 UTF-8 解码行为。Rainmeter INI 继续使用 UTF-16 LE with BOM。

参考：[Rainmeter Unicode 文档](https://docs.rainmeter.net/tips/unicode-in-rainmeter/)。

### 测试先行

先在 `SnapshotPublisherTests` 添加包含 `课程 😀 𠮷 café` 的失败测试。旧实现必须稳定因 `EncoderFallbackException` 失败，证明测试捕获的是 ANSI 编码缺陷。修复后验证：

- 文件以 UTF-8 BOM 开头；
- 严格 UTF-8 解码能完整还原标题；
- ASCII、中文、emoji、生僻字和拉丁扩展字符均保留；
- 引号、反斜杠、换行和 JSON 转义正确；
- 原子写入失败仍保留旧快照并清理临时文件；
- 日期、时区、排序和 urgency 状态不变。

Skin 测试补充带 BOM 快照、直接 UTF-8 字符、转义 Unicode 和 surrogate pair 场景。Packaging 测试验证安装包内 INI、主 Lua、JSON helper 的编码契约。

### Windows CI

新增 `.github/workflows/ci.yml`，固定运行于 `windows-2022`。第一阶段先使用 `actions/setup-dotnet` 安装稳定的 `8.0.x` SDK，通过 MSYS2 准备 `mingw-w64-x86_64-gcc`，并使用明确的 x64 `g++.exe` 路径调用 PowerShell 测试。第二阶段加入 `global.json` 和 lock file 后，同一工作流改为读取固定 SDK 并使用 locked-mode restore。

CI 自动执行：

1. NuGet restore（第二阶段升级为 locked mode）；
2. `dotnet test .\DeadlineWidgetV2.sln -c Release --no-restore`；
3. `tests\Bridge.Tests.ps1`；
4. `tests\Packaging.Tests.ps1`；
5. 自包含 win-x64 Manager、PE32+ x64 Bridge 和 `.rmskin` 产物验证。

`Build-Package.ps1` 增加 `-SkipSolutionTests`。默认本地构建仍执行测试；只有已经完成解决方案测试的 CI 和 Packaging 编排显式跳过，避免重复执行相同的昂贵步骤。

## 阶段二：可复现构建、依赖安全与标签发布

### SDK 和 NuGet

新增 `global.json`：

```json
{
  "sdk": {
    "version": "8.0.425",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

新增 `Directory.Build.props`，统一启用 `RestorePackagesWithLockFile` 和 NuGet 全部直接/间接依赖审计。删除三个测试项目的 `NuGetAudit=false`。生成并提交所有项目的 `packages.lock.json`；CI 和 Release 使用 locked mode，依赖图与锁文件不一致时失败。CI 将 `NU1901`、`NU1902`、`NU1903`、`NU1904` 视为错误。

参考：[NuGet PackageReference lock file](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)。

本轮保留当前已验证的 MSTest、Microsoft.NET.Test.Sdk、coverlet 和 MoonSharp 版本。原因是当前漏洞扫描无报告，而可用更新跨越多个大版本。新增每月 Dependabot 检查，让依赖升级通过独立变更和完整回归验证完成。

### 发布产物

新增 `.github/workflows/release.yml`，仅响应符合 `v<major>.<minor>.<patch>` 的标签。工作流具有最小的 `contents: write` 权限，并完成测试、构建、打包、校验和 Release 上传。本次工作不创建标签或实际运行发布。

发布产物为：

- `DeadlineWidgetV2.rmskin`；
- `DeadlineWidgetV2-v<version>-win-x64.zip`；
- `SHA256SUMS.txt`。

完整 ZIP 包含自包含 `Manager/`、`.rmskin`、`Install-V2.ps1` 和面向安装用户的 `README.txt`。工作流使用 runner 自带 GitHub CLI 创建 Release，避免增加不必要的第三方发布 action。

`Build-Package.ps1` 增加 `-Version`，把版本写入 `Rainstaller.cfg` 并返回结构化产物路径和 SHA-256。Packaging 测试验证版本、文件白名单、哈希、重复构建清洁性和仓库外路径独立性。

## 阶段三：运行维护、卸载与仓库展示

### Rainmeter 路径发现

新增职责单一的 Rainmeter locator，按以下顺序查找：运行中的 `Rainmeter.exe`、Windows 卸载注册表、`ProgramFiles`、`ProgramFiles(x86)`。返回第一个存在的文件。`RainmeterRefresher` 仅负责执行刷新命令。路径来源和优先级通过 fake probe 测试，不依赖测试机真实安装状态。

### Manager 诊断

新增小型 `IDiagnosticSink`：

```csharp
public interface IDiagnosticSink
{
    void Write(string component, Exception exception);
}
```

默认实现写入 `%LOCALAPPDATA%\DeadlineWidgetV2\logs\manager.log`，仅记录 UTC 时间、组件名、异常类型和异常消息，不记录 deadline 标题、JSON 内容或任务数据。`SingleInstanceCoordinator` 和 `WakeupFileWatcher` 捕获异常后写入诊断，但继续保持当前容错行为。测试使用内存实现验证异常不再静默丢失。

### 日志轮转

Manager 和 C++ Bridge 日志最大约 1 MiB，保留一个 `.1` 备份。写入前发现当前日志达到上限时，以替换方式轮转，然后创建新日志。不引入日志框架或多级归档。Bridge 测试预置超大日志并验证 UTF-8、轮转结果和文件数量。

### 安全卸载

新增 `scripts/Uninstall-V2.ps1`。默认仅删除 Manager 程序目录、V2 Rainmeter 皮肤、Bridge DLL、Bridge root 配置和启动快捷方式。默认保留：

- `deadlines.json`；
- `deadlines.backup.json`；
- `migration-backup/`；
- `logs/`。

只有显式传入 `-RemoveUserData` 才删除 V2 数据目录。删除前验证所有目标均位于预期 AppData/Rainmeter 根目录。测试在隔离临时目录中验证默认保留数据、显式数据删除、不影响旧版目录及其他皮肤/插件。

## README 与社区文件

README 更新为面向招聘者和普通用户的结构：项目亮点、CI/平台/许可证徽章、截图章节、架构、Release 安装、升级、卸载、源码构建、测试范围、数据与隐私、损坏恢复和限制。

仓库当前没有适合公开使用的无隐私截图，因此不伪造图片。新增 `docs/images/README.md`，说明未来截图应使用虚构任务且不得包含真实课程信息；主 README 显示清晰的截图待补说明，并预留 `docs/images/deadline-widget.png` 路径。

新增：

- `LICENSE`：MIT；
- `CHANGELOG.md`：简化 Keep a Changelog，先记录 `Unreleased`；
- `CONTRIBUTING.md`：开发环境、测试和提交要求；
- `SECURITY.md`：安全报告不得包含真实 deadline 数据，优先使用 GitHub 私密安全报告；
- `.github/dependabot.yml`：每月检查 NuGet 和 GitHub Actions。

## 验证与安全边界

每个阶段都先运行定向测试，再检查 diff。最终运行：

```powershell
dotnet test .\DeadlineWidgetV2.sln -c Release
.\tests\Bridge.Tests.ps1
.\tests\Packaging.Tests.ps1
```

最终审计确认：

- `bin/`、`obj/`、`artifacts/`、临时文件未被跟踪；
- 没有真实 deadline JSON、恢复数据或日志进入仓库；
- 没有用户绝对路径、密钥、Token 或凭据；
- workflow 权限最小化；
- 所有功能修复均有先失败后通过的测试证据；
- 不修改或删除 `%LOCALAPPDATA%`、`%APPDATA%` 或仓库 `recovery/` 中的真实用户数据。

## 推荐提交边界

1. `fix: publish Rainmeter snapshots as UTF-8`
2. `ci: add Windows validation workflow`
3. `build: lock SDK and NuGet dependencies`
4. `ci: add tagged release workflow`
5. `fix: improve Rainmeter discovery and diagnostics`
6. `feat: add safe uninstall workflow`
7. `docs: prepare repository for public release`

