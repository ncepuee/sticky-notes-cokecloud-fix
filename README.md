# Windows Direct Route Fix

<p align="center">
  <a href="https://github.com/ncepuee/windows-direct-route-fix/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/ncepuee/windows-direct-route-fix?display_name=tag&logo=github"></a>
  <a href="https://github.com/ncepuee/windows-direct-route-fix/blob/main/LICENSE"><img alt="MIT license" src="https://img.shields.io/github/license/ncepuee/windows-direct-route-fix?color=blue"></a>
  <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows%20x64-0078D4">
  <img alt="WinINet" src="https://img.shields.io/badge/routing-WinINet-5C2D91">
</p>

一个通用的 Windows 桌面应用直连诊断工具。不绑定任何特定代理软件，核心是：识别目标应用、配置直连域名、观察日志证据，并提供可回滚的 WinINet 策略修改。

## 快速使用

- 双击 `Windows-Direct-Route-Fix.exe` 打开图形界面。
- 命令行只读检查：`Windows-Direct-Route-Fix.exe --check-only-text`。
- 重新构建：运行 `build-exe.cmd`。

所有必要文件均位于本目录，不再使用嵌套源代码目录。

## 发布边界

工具只处理当前用户的 Windows WinINet 代理状态和目标应用诊断日志，不修改代理软件安装文件或目标应用数据库。域名例外是 WinINet 共享设置，不等于严格的进程级分流。回滚文件在 `%LOCALAPPDATA%\WindowsDirectRouteFix\rollback-state.json`，不要提交到 GitHub。

当前版本：[`v0.3.1`](https://github.com/ncepuee/windows-direct-route-fix/releases/tag/v0.3.1)。

界面支持中文/English 切换；主按钮为“一键设置便笺同步”，默认只添加目标域名直连例外并重启目标应用，不会直接关闭全局系统代理。

项目地址：[github.com/ncepuee/windows-direct-route-fix](https://github.com/ncepuee/windows-direct-route-fix)
