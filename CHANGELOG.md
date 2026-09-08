# Changelog / 更新记录

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
