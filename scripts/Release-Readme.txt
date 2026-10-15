Deadline Widget 安装说明
========================

适用系统：64 位 Windows 10 或 Windows 11。
安装前请先安装 Rainmeter 4.5 或更新版本：https://www.rainmeter.net/
不需要单独安装 .NET。

安装或升级
----------
1. 保持本目录里的 Manager 文件夹、DeadlineWidgetV2.rmskin 和两个 .ps1 文件在一起。
2. 在文件资源管理器中打开本目录，在地址栏输入 powershell 并按回车。
3. 运行：.\Install-V2.ps1
4. Rainmeter 弹出皮肤安装确认时，点击“安装”。

桌面出现 Deadlines 卡片后，点击右上角的“＋”打开管理窗口。
升级时用新版完整压缩包重复以上步骤；已有任务会保留。

卸载
----
在本目录运行：.\Uninstall-V2.ps1
默认卸载会保留任务数据和备份。
只有确定不再需要数据时，才运行：.\Uninstall-V2.ps1 -RemoveUserData

更多说明与常见问题：https://github.com/pizza1107/deadline-widget
