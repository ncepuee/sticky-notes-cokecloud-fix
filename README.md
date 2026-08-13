# Windows Direct Route Fix

一个通用的 Windows 桌面应用直连诊断工具。不绑定任何特定代理软件，核心是：识别目标应用、配置直连域名、观察日志证据，并提供可回滚的 WinINet 策略修改。

完整发布说明见 [GitHub发布说明.md](GitHub发布说明.md)。

## 快速使用

- 双击 `Windows-Direct-Route-Fix.exe` 打开图形界面。
- 双击 `启动直连策略工具.cmd` 启动 EXE；如果 EXE 不存在，会回退到 PowerShell 版本。
- 双击 `检查直连策略.cmd` 做只读检查。
- 双击 `build-exe.cmd` 从 C# 源码重新构建 x64 EXE。

## 发布边界

工具只处理当前用户的 Windows WinINet 代理状态和目标应用诊断日志，不修改代理软件安装文件或目标应用数据库。域名例外是 WinINet 共享设置，不等于严格的进程级分流。回滚文件在 `%LOCALAPPDATA%\WindowsDirectRouteFix\rollback-state.json`，不要提交到 GitHub。

当前版本：`0.3.0`。

界面支持中文/English 切换；主按钮为“一键设置便笺同步”，默认只添加目标域名直连例外并重启目标应用，不会直接关闭全局系统代理。

项目地址：[github.com/ncepuee/windows-direct-route-fix](https://github.com/ncepuee/windows-direct-route-fix)
