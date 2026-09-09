# OpenCodex Launcher 3.0.2

## 中文

新增可操作的关键版本回退：**设置 → 版本管理 → 选择 2.6.5 → 一键回退到所选版本 → 确认**。当前仅收录已经确认的启动器 2.6.5，不将所有历史 Release 自动列为关键版本。

回退会校验配置兼容性、固定的下载包 SHA-256、文件清单和 EXE 版本，备份程序文件后自动关闭并重开启动器。供应商、API Key、模型选择、Desktop 关联和运行时路径保留；不重启 Desktop/OpenCodex。成功下载并验证的缓存可离线使用，损坏缓存、配置不兼容或启用 Reserve Force 时停止操作。写入或启动命令失败时尝试恢复，发现外部修改则保留恢复记录并停止覆盖。

修复文件占用导致恢复中断的问题。隔离测试使用真实 2.6.5 发布包验证往返文件切换、旧版配置读取及 DPAPI 凭据解密，覆盖取消、下载缓存损坏、文件占用、启动失败和外部修改。沿用 3.0 UI、图标及所有现有业务功能。

2.6.5 使用旧界面且没有关键版本选择器，可通过其内置“检查启动器更新”再次升级。OpenCodex 运行时关键版本尚未确认，本列表只切换启动器。请先保存编辑内容、关闭其他启动器窗口，再操作更新或回退。诊断不发送推理，进程启动成功不等于全部功能验证通过。

## English

Add an actionable milestone selector: **Settings → Version management → select 2.6.5 → Roll back to selected version → confirm**. Only launcher 2.6.5 is currently confirmed; arbitrary past releases are not added automatically.

Rollback checks settings compatibility, a pinned package SHA-256, the file allowlist and executable version. It backs up program files, closes the launcher and starts the selected version while preserving providers, credentials, model choices, Desktop association and runtime paths. Desktop/OpenCodex are not restarted. Verified cached packages work offline; damaged caches, incompatible settings and Reserve Force block rollback. Write or launch-command failures attempt recovery; external changes stop overwriting and retain recovery records.

Fix recovery aborting on an unchanged locked file. Isolated checks use the actual 2.6.5 package for round-trip file switching, historical settings reading and DPAPI decryption, plus cancellation, damaged caches, locks, launch failures and external edits. Existing 3.0 UI and business features are retained.

Version 2.6.5 has its older interface without this selector; its built-in updater can return to the latest stable release. OpenCodex runtime milestones remain unconfirmed. Save edits and close other launcher windows first. Diagnostics send no inference requests; process launch success is not a full application functionality test.

Windows x64 · .NET Framework 4.8 · MIT
