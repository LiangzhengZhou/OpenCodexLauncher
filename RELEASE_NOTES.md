# 2.6.1 release notes / 发布说明

2026-09-08. Windows 10/11 x64, .NET Framework 4.8. OpenCodexLauncher.exe version 2.6.1.0; keep its .config file beside it. MIT license.

## Model list compatibility / 模型列表兼容修复

- Saved provider selections and configured models populate Models even without duplicate customModels entries or a generated Codex catalog.
- Optional catalog errors and native CLI refresh failures no longer hide locally saved third-party models. Retain the last valid catalog without overwriting damaged files.
- Entering Models reloads selections. Save selection verifies read-back and displayed routes, and shows/logs the count before optional sync.
- Bilingual counts, guidance and Copy model diagnostics help diagnose different setup states. Diagnostics contain only version, counts and fixed state codes: no credentials, provider/model names, endpoints or paths.

修复部分配置格式下已选模型没有显示，以及生成目录/CLI 刷新错误影响本地列表的情况。增加保存后回读校验、进入模型管理自动刷新、模型数量提示和“复制模型诊断”。独立空白环境中的真实勾选、保存、页面切换、损坏目录、CLI 错误和重启保留已加入验证。

## Upgrade and verify / 升级及验证

For 2.6.0+, open Settings → Check launcher updates → Update and restart. Older versions need one manual full Windows ZIP upgrade. Keep application data, providers, credentials and runtimes.

2.6.0 用户在“设置 → 检查启动器更新 → 一键更新并重启”升级；更早版本需完整解压 Windows ZIP。保留本地配置、密钥和运行时，无需重新安装 OpenCodex。

After upgrading, select and save models on Providers, then open Models. No proxy/Desktop restart or inference request is needed to verify this local list. If models remain absent, use Copy model diagnostics and report the result. These fixes cover reproduced compatibility failures; the exact cause on an inaccessible test machine still requires its diagnostic report. Codex Desktop's model picker is a separate integration.

升级后在供应商页勾选并“保存选择”，再进入“模型管理”查看数量和下拉列表。此验证不需要重启代理或 Desktop，也不发送推理请求。若仍未显示，请反馈“复制模型诊断”的结果；测试用户电脑的确切原因仍需该诊断确认。此次修复针对启动器自己的列表。

Packages are allowlisted and contain no user configuration or credentials. Checksums are in SHA256SUMS.txt. The updater retains its existing backup/rollback behavior.
