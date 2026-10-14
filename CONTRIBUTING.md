# 参与开发

## 开发环境

需要 .NET 8 SDK、PowerShell 5.1 或 PowerShell 7，以及可以生成 Windows x64 DLL 的 MinGW-w64 C++ 编译器。

请从短期分支提交改动，不要把构建输出、真实 deadline 数据或本机路径加入仓库。功能与缺陷修复需要先加入能复现目标行为的测试。

## 本地验证

在仓库根目录运行：

```powershell
dotnet restore .\DeadlineWidgetV2.sln --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet test .\DeadlineWidgetV2.sln -c Release --no-restore

$compiler = (Get-Command g++.exe).Source
.\tests\Bridge.Tests.ps1 -CompilerPath $compiler
.\tests\Packaging.Tests.ps1 -DotNetPath (Get-Command dotnet).Source -CompilerPath $compiler
```

Pull request 应说明用户可见变化、测试结果和仍需人工验证的内容。依赖更新应单独提交，并同时更新 `packages.lock.json`。
