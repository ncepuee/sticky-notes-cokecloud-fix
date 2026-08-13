# Windows Direct Route Fix

一个通用的 Windows 桌面应用直连诊断工具，用于排查应用在本机代理环境下的连接失败。

## 核心功能

- 读取 Windows 当前用户 WinINet 系统代理状态。
- 配置目标应用 App launch ID、日志目录和直连域名。
- 读取目标日志中的连接建立、内容更新和失败事件。
- 追加 WinINet 域名直连例外，或使用明确标注的全局代理关闭兜底。
- 保存/恢复当前用户代理状态并重启目标应用。
- 提供 `--check-only-text` 只读诊断模式。

## 安全边界

- 不修改任何代理软件安装文件、配置文件或节点信息，不修改目标应用数据库。
- 域名例外修改的是当前用户 WinINet `ProxyOverride`，可能影响其他 WinINet 应用。
- 全局兜底修改的是当前用户 WinINet `ProxyEnable`，影响所有依赖该设置的应用。
- 严格进程级分流需要 WFP 或代理后端/驱动，本工具不冒充实现该能力。
- 回滚文件位于 `%LOCALAPPDATA%\WindowsDirectRouteFix\rollback-state.json`，不应上传 GitHub。

## 运行

双击 `Windows-Direct-Route-Fix.exe`，或双击 `启动直连策略工具.cmd`。

只读检查：

```powershell
Windows-Direct-Route-Fix.exe --check-only-text

也可以双击 `检查直连策略.cmd`。
```

## 从源码构建

本项目使用 Windows 自带 .NET Framework C# 编译器，不要求安装 Visual Studio。双击 `build-exe.cmd` 即可构建 x64 WinForms EXE。构建脚本会把编译临时文件放在 `%TEMP%`，适配 OneDrive/云盘占位目录。

## 当前版本

`0.3.0`：增加中文/English 切换，突出一键设置目标应用同步，降低高级全局代理操作的误触风险。
