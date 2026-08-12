# Sticky Notes CokeCloud Fix

一个面向 Windows 的小型 WinForms 工具，用于排查 Microsoft 便笺在 CokeCloud 等本机代理运行时出现的同步失败，尤其是 `0x80072EFD`。

## 核心功能

- 读取 Windows 当前用户 WinINet 系统代理状态。
- 读取 CokeCloud 的模式、连接状态和进程数；不读取或展示账号、订阅和节点信息，也不依赖固定安装盘符。
- 读取 Microsoft Sticky Notes 诊断日志，识别 `RealTimeConnectionOpened`、`NoteContentUpdated` 和同步失败事件。
- 一键保存回滚点、关闭 Windows 系统代理并重启便笺。
- 单独重启便笺、保存/恢复代理状态、打开日志目录。
- 提供 `--check-only` 只读诊断模式。

## 安全边界

- 不修改 `D:\CokeCloud` 内的文件，不修改 CokeCloud 配置，不修改便笺数据库。
- 一键修复只修改当前用户的 Windows Internet Settings 中 `ProxyEnable`，并重启便笺。
- 回滚文件位于 `%LOCALAPPDATA%\StickyNotes-CokeCloud-Fix\rollback-state.json`，可能包含本机代理例外列表，不应上传到 GitHub。
- 这是诊断和修复辅助工具，不保证远端多设备内容同步；最终仍应使用第二台设备或网页版确认内容。

## 运行

双击 `StickyNotes-CokeCloud-Fix.exe`，或双击 `启动便笺同步修复工具.cmd`。

只读检查：

```powershell
StickyNotes-CokeCloud-Fix.exe --check-only

也可以双击 `检查便笺同步状态.cmd`。

自动化或命令行验证可使用 `StickyNotes-CokeCloud-Fix.exe --check-only-text`，不会弹出窗口。
```

## 从源码构建

本项目使用 Windows 自带 .NET Framework C# 编译器，不要求安装 Visual Studio。双击 `build-exe.cmd` 即可构建 x64 WinForms EXE。构建脚本会把编译临时文件放在 `%TEMP%`，适配 OneDrive/云盘占位目录。

## 当前版本

`0.1.0`：首次 EXE 原型，保留 PowerShell 回退工具。
