# Sticky Notes CokeCloud Fix

Windows 小型 WinForms 工具：诊断 Microsoft Sticky Notes 在 CokeCloud 等本机代理运行时的同步问题，并提供可回滚的修复操作。

完整中文发布说明见 [GitHub发布说明.md](GitHub发布说明.md)。

## 快速使用

- 双击 `StickyNotes-CokeCloud-Fix.exe` 打开图形界面。
- 双击 `启动便笺同步修复工具.cmd` 启动 EXE；如果 EXE 不存在，会回退到 PowerShell 版本。
- 双击 `检查便笺同步状态.cmd` 做只读检查。
- 双击 `build-exe.cmd` 从 C# 源码重新构建 x64 EXE。

## 发布边界

工具只处理当前用户的 Windows WinINet 代理状态和 Sticky Notes 诊断日志，不修改 CokeCloud 安装文件或便笺数据库。回滚文件在 `%LOCALAPPDATA%\StickyNotes-CokeCloud-Fix\rollback-state.json`，不要提交到 GitHub。

当前版本：`0.1.0`。
