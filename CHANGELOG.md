# Changelog / 更新记录

## 2.6.3 — 2026-09-08

- Add explicit, bounded, cancellable one-click Desktop diagnostics with bilingual copy/save reports on Overview, Models and Codex routing.
- Compare home candidates, root references and full provider/model catalog coverage; report process evidence and loopback-only health checks without claiming Desktop has loaded models.
- Keep reports free of raw configuration, paths, provider/model names, endpoints, credentials and request logs. No inference, restart or automatic uploads.
- Share port candidate parsing with runtime health checks; test mismatched homes, missing root keys, corruption, cancellation, privacy, redirect refusal and report UI behavior in isolated fixtures.
- 新增一键诊断 Desktop，生成可复制/保存的双语脱敏报告；检查目录候选、模型引用、完整路由、进程及本机健康状态，不改配置、不重启、不发推理请求。

## 2.6.2 — 2026-09-08

- Add explicit association with the existing config.toml opened from Codex Desktop settings. Keep the manual OpenCodex provider store, credentials and model selections independent of the selected Codex home.
- Show the sync target on Codex routing; retain it across restarts and language changes. Reject missing selected files instead of creating replacement configurations.
- Distinguish file sync, explicit configuration association and unverified Desktop loading. Prepare routing before the explicitly confirmed Desktop sync/restart command.
- Test association, child-process home, provider preservation, read-only preview, onboarding isolation, missing-file handling and bilingual UI persistence in isolated fixtures.
- Use ASCII build errors so the native build script also parses in Windows PowerShell 5 without a UTF-8 BOM.
- 增加 Desktop 配置关联和同步目标显示；保留已有供应商与模型。修正同步提示，明确文件写入并不代表 Desktop 已加载，不自动重启 Desktop 或导入账号。

## 2.6.1 — 2026-09-08

- Build the local model list from provider selections and configured model seeds as well as customModels; support homes without generated catalogs.
- Keep saved third-party models available when a generated catalog is unreadable or the optional native CLI refresh fails. Preserve the last valid catalog without rewriting damaged files.
- Reload local selections when entering Models. Verify saved selections by reading them back, retain the success message and log the verified count before optional sync.
- Add bilingual list counts, empty-state guidance and a copyable model diagnostic report containing only version, counts and fixed status codes.
- Test the real provider checkbox/save/navigation flow, incomplete catalogs, CLI failure, restart persistence and diagnostics in isolated new-user environments. Wrap long log lines.
- 修复部分配置格式下模型已选却不显示，以及可选目录/CLI 刷新失败影响本地模型列表的问题；增加不含密钥、名称、端点和路径的模型诊断。

## 2.6.0 — 2026-09-08

- Add explicit launcher self-update checks and update/restart controls in Settings, onboarding and recovery, with live Chinese/English text.
- Validate stable release metadata, pinned repository assets, SHA-256, allowlisted ZIP contents and executable version before replacement.
- Add a separate updater helper with exact-parent waiting, backups, guarded file replacement, rollback and restart; retain settings, credentials, runtimes and running services.
- Repair missing config.toml only in the launcher-managed manual Codex home during explicit setup/CLI actions, without overwriting existing files or importing accounts.
- Add updater regression coverage including a real helper process and isolated parent/new-version fixtures.
- 新增启动器自身在线更新；修复手动配置下缺少 config.toml 的同步错误，保留既有配置。

## 2.5.2 — 2026-09-08

- Fix missing launcher-owned CODEX_HOME / OPENCODEX_HOME directories after manual and one-click setup; repair existing affected installations at explicit command launch without importing account data.
- Preserve existing configuration files and reject missing external homes or file/path collisions with localized guidance.
- Check the real Node/Bun CLI help path before proxy startup and show bounded, redacted runtime/import errors.
- Add 10 core and 2 UI regressions; reproduce the failure with real OpenCodex 2.47.0 and verify an isolated proxy reaches readiness.
## 2.5.1 — 2026-09-08

- Fix installation finalization when Windows directory handles prevent moving the runtime: use a stable unique directory and atomically save a completion marker after validation.
- Keep failed/cancelled generations unselected and preserve existing configuration and runtimes.
- Include the failed phase and actual diagnostic log path in installation errors; report when a log could not be saved. Add Chinese/English guidance for access errors.
- Add a real Windows directory-handle regression and coverage for completion write failure, late cancellation and diagnostic reporting.
- 修复安装目录被占用时，收尾重命名失败导致的一键安装失败；保留旧配置，增加失败阶段及日志位置提示。

## 2.5.0 — 2026-09-07

- Add one-click OpenCodex installation with private Node.js/Bun runtimes and empty first-use configuration.
- Add on-demand stable update checks, cancellation and previous-version selection, preserving account data and running services.
- Verify hashes, isolate npm configuration, guard concurrent installs and validate the staged CLI before activation.
- Add long Windows dependency path support and bilingual installer UI.


## 2.4.0 — 2026-09-07

- Added immediate persistent Chinese / English switching with stable page IDs and retained drafts.
- Added empty onboarding, explicit import preview, isolated manual homes, versioned settings migration and damaged-file recovery.
- Required complete provider/model routes and attributable session evidence; capped fallback at one retry.
- Unified health/dashboard/runtime endpoints; malformed runtime metadata no longer hides configured ports.
- Added Reserve Force compatibility checks, owned-write journaling and conservative rollback.
- Removed private provider examples and real-credential tests. Added release allowlists, string scanning, dual builds and CI.
- 新增中英文即时切换、空白引导、旧设置迁移与损坏配置恢复。
- 修复跨供应商同名模型误匹配、无关联日志触发会话处理、重复回退和动态端口解析。
- 强化 Reserve Force 失败恢复，隔离测试与公开发布内容。

Earlier development snapshots are not included in this public release.
