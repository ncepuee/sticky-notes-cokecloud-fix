# 便笺同步修复工具

启动文件：StickyNotes-CokeCloud-Fix.exe 或 启动便笺同步修复工具.cmd

用途：处理 Windows 便笺在 CokeCloud 或其他本机代理运行时出现 0x80072EFD 的问题。

按钮：
- 刷新状态：只读检查代理、CokeCloud 和便笺日志。
- 一键修复：保存回滚点，关闭 Windows 系统代理，重启便笺；不关闭 CokeCloud 核心。
- 只重启便笺：不改变代理。
- 保存回滚点 / 恢复代理状态：保存或恢复当前用户代理设置。
- 打开日志目录：打开 Sticky Notes 诊断日志目录。
- 打开排障文档：打开既有 Playbook。

边界：
- 不修改 D:\CokeCloud、app.asar、core.exe 或便笺数据库。
- 不常驻后台。
- 当前机器的已验证稳定状态是：CokeCloud 保持运行，Windows 系统代理保持关闭。
- rollback-state.json 可能包含代理例外列表，不要分享。

只读检查：双击 `检查便笺同步状态.cmd`，或运行 EXE 的 `--check-only` 参数。
