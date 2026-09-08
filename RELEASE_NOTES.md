# 2.6.2 release notes / 发布说明

2026-09-08. Windows 10/11 x64, .NET Framework 4.8. OpenCodexLauncher.exe version 2.6.2.0; keep its .config file beside it. MIT license.

## Codex Desktop configuration association / Desktop 配置关联

Manual setup uses an independent Codex home. Sync can update that home's catalog while a separately launched Codex Desktop reads another home. Version 2.6.2 adds **Codex routing → Associate Desktop config…**, with the selected target displayed in both languages. Choose the existing config.toml opened from Desktop settings and confirm it. Existing OpenCodex providers, keys and model selections stay in place.

手动初始化的独立 Codex 目录可能与 Desktop 读取的目录不同。2.6.2 新增“Codex 路由 → 关联 Desktop 配置…”，明确显示同步目标。请选择从 Desktop 设置打开的现有 config.toml 并确认；保留已有供应商、密钥和模型选择。

Association alone does not edit the selected config, copy accounts or run/restart services. Subsequent syncs back up and modify the selected config and catalog. Missing associated files cause an error without silent replacement. Restore the selected file and reload if necessary.

关联操作只保存同步目标；后续同步才备份并修改所选配置和目录。不会复制账号或自动启停服务。所选文件丢失时报告错误，不创建替代配置；请恢复原文件后重新加载。

## Upgrade and verify / 升级及验证

1. In 2.6.0+, use **Settings → Check launcher updates → Update and restart**. Earlier versions need one manual full Windows ZIP upgrade. Preserve application data and runtimes.
2. Associate the config.toml opened from Desktop settings, then start OpenCodex and sync.
3. If necessary, finish active tasks before **Sync and restart Desktop**. Read the actual restart result in the log and check Desktop's bottom-right model picker. Active sessions may be interrupted.

2.6.0 及以上版本在“设置 → 检查启动器更新 → 一键更新并重启”升级。关联 Desktop 配置后启动 OpenCodex 并同步；必要时等任务结束，再“同步并重启 Desktop”，核对日志中的重启结果及 Desktop 右下角模型列表。

Sync messages now distinguish files updated from Desktop loaded. Diagnostics expose only an association boolean and an unverified loading state, never the selected path or config contents. A catalog count or successful command exit does not prove the running Desktop loaded that home. The exact home used on a remote tester's computer still requires confirmation; this release does not claim that machine has been verified.

修正“同步成功”提示：目录写入、代理运行、Desktop 加载是分别需要确认的状态。诊断只增加关联状态和“尚未验证加载”，不包含路径或文件内容。测试用户电脑实际读取的目录仍需在其 Desktop 中核对。

Both builds and isolated regression/UI tests cover the new association. No real account configuration, running Desktop/proxy or inference service is used in acceptance tests. Packages are allowlisted and scanned; checksums are in SHA256SUMS.txt. The updater retains its backup/rollback behavior.
