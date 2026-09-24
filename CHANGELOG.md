# Changelog / 更新记录

## 3.1.2 — 2026-09-24

- GUI 启动及 Native 页面比较已注册 Helper 与当前 EXE/.config 的实际内容，发现旧构建时显示提示及确认后更新入口。
- Helper 更新复用内容寻址目录和注册恢复记录，保留旧文件及当前会话，不重新生成 TOML、模型目录或 Runtime 清单。完成后需自行完整重启 Desktop。
- 新增跨构建、配置变化、部署并发、注册失败恢复、回退和内容损坏回归；保留 3.1.1 Runtime 自动恢复算法。
- Detect stale Native helpers after GUI updates and provide a confirmed, recoverable helper-only update. Existing Desktop sessions continue until the user restarts Desktop.

## 3.1.1 — 2026-09-19

- 修复 Desktop 更新后 Native helper 使用残缺旧 runtime 的问题，自动模式无需 Launcher GUI 即可解析完整 bundle。
- 共享 runtime 校验，保留显式路径优先级和失败保护；新增独立 runtime 健康状态。
- 新增 rollover 回归测试，覆盖旧 EXE 留存但 host 消失、并发解析及手动路径。升级后需更新日常 Native 接入并重开 Desktop 一次以部署新版 helper。
- Recover stale Native runtime paths after Desktop updates; preserve credentials, provider isolation and rollback compatibility.

## 3.1.0 — 2026-09-18

- 新增一次启用的日常 Native 接入：固定目录安装桥接程序、注册当前用户启动环境并通知 Windows，之后可从原快捷方式启动。提供关闭恢复、环境冲突保护及中断恢复记录；首次启用或更新后需要完全重开 Desktop，日常快捷方式接入已通过用户验收。
- 修复 Desktop 在 turn/start.collaborationMode.settings.model 再次携带 Native 别名时未转换的问题；仅在真实 app-server 已确认会话供应商后转换，未知或跨供应商请求在本地拒绝。
- 供应商统一在原供应商页面管理，勾选 Native 直连即可复用地址、DPAPI 密钥和模型选择；Native 页面仅保留准备及启动入口。Native 推理由真实 Codex 发送，不经过 OpenCodex HTTP relay。
- 新增带确认的删除供应商操作，清理配置、模型和有效凭据；默认路由或 Reserve Force 冲突时阻止删除，失败时恢复本次修改并保留外部编辑。
- 保留现有代理配置，准备 Native 配置时采用恢复快照和受管 TOML 区域校验。Reserve Force 启用时拒绝准备。
- 新会话绑定供应商；不实现 thread ID virtualization。已识别的跨供应商模型变更返回明确错误。
- 本地真实 app-server 模拟验证通过；用户已确认真实 Desktop Packy 请求通过，日常启用已通过用户验收。新增 3.0.4 关键版本回退并保留 2.6.5；回退前须关闭日常 Native 接入。
- Add persistent per-user Native activation, a stable helper and reversible environment registration. Packy requests passed user testing; everyday activation passed user acceptance. Add confirmed 3.0.4 rollback alongside 2.6.5.

## 3.0.4 — 2026-09-16

- 新增系统托盘常驻，关闭或最小化隐藏主窗口，点击图标恢复；右键提供随语言切换的“显示主窗口”和“退出启动器”。
- 隐藏时保留后台操作和窗口草稿，真正退出时取消操作并释放托盘图标；更新、回退和 Windows 会话结束使用退出流程。
- 复用已确认的粉色多尺寸图标，保留配置格式和 2.6.5 关键版本回退。
- Add a Windows tray icon, close/minimize to tray, restore and explicit exit. Preserve background work while hidden and release resources on exit; update and rollback still close the launcher before replacement.

## 3.0.3 — 2026-09-11

- 更换为已确认的粉色图标，裁剪透明留白并保留安全边距，使窗口、任务栏和 EXE 图标主体更清晰。
- 生成 16–256 像素多尺寸图标，保持素材比例；保留现有配置和 2.6.5 关键版本回退功能。
- Replace the icon with approved pink artwork, trim transparent margins and preserve aspect ratio across Windows icon sizes. No configuration or business logic changes.

## 3.0.2 — 2026-09-09

- 新增已确认关键版本选择列表及“一键回退到所选版本”，首个目标为 2.6.5；与普通向上更新入口分开。
- 固定发布包 SHA-256，验证配置、文件清单与 EXE 版本，支持已校验缓存离线回退；保留供应商、凭据、模型及 Desktop 关联。
- Reserve Force、损坏缓存或不兼容配置阻止回退；程序文件切换失败尝试恢复，外部修改停止覆盖。修复未改动文件被占用时阻断恢复的问题。
- 使用真实 2.6.5 发布包验证往返文件切换、旧版配置读取和 DPAPI 解密；添加中英文列表与按钮检查，修正 CI 发布包版本硬编码。
- Add confirmed launcher milestone selection, guarded rollback, verified offline caching and recovery tests using the actual 2.6.5 package. OpenCodex runtime milestones remain unconfirmed.

## 3.0.1 — 2026-09-09

- 修正窗口标题、中英文副标题、版本管理和发布文档中的预览版标识；界面版本统一读取程序集版本。
- 打包脚本直接生成正式文件名，3.0.0 用户可通过内置更新器升级。
- Correct release labels and derive UI version labels from assembly metadata. Generate stable asset names for the existing updater.
- 保留历史版本回退尚未完成验证的说明；本次不变更配置格式和业务行为。

## 3.0.0 Preview — 2026-09-09

- 深度重设计供应商与模型管理界面：当前供应商概览、连接/API、模型状态卡片、按供应商隔离模型、搜索与全部/已选筛选。
- API Key 默认隐藏，支持临时显示、键盘编辑、未修改保留和明确清除；移除新保存逻辑中的默认模型 ID。
- 保留 2.6.5 的供应商、模型、Desktop 同步、诊断、路由、更新、安装和双语能力；新图标已嵌入预览 EXE。
- 2.6.5 已确认作为首个关键回退版本；历史版本选择和双向回退验证尚未完成。

## 2.6.5 — 2026-09-08

- Extend one-click Desktop diagnostics with bounded, direct-loopback TCP/GET evidence for message connection failures. Export only status codes, fixed paths and proxy-presence flags.
- Keep POST authentication, Desktop connectivity and inference explicitly untested. Do not infer POST failure from GET 404/405 or infer no arrivals from empty request logs.
- Align runtime health with diagnostics: bypass system proxies, refuse redirects and read headers without buffering bodies.
- Add isolated tests for connection failures, timeouts, redirects, HTTP errors, IPv6, unsafe targets, cancellation, privacy and onboarding isolation.
- 扩展一键诊断以排查“模型可见但发送失败”；只检查连接，不发送推理、不读取凭据、不重启 Desktop。统一启动时健康检查，避免系统代理和重定向干扰。
## 2.6.4 — 2026-09-08

- Add confirmed candidate selection and one-step Desktop association/sync with private recovery snapshots; preserve providers and avoid automatic Desktop restarts.
- Verify actual root references, referenced catalog, selected full routes and proxy port after normal sync. Do not treat zero exit as proof of successful catalog generation.
- Automatically open a sanitized report on incomplete sync; include fixed upstream skip categories and the last attempt time. Report native-cache availability without exporting content.
- Test missing-source zero exits, cross-provider collisions, disk corruption, changed selections, cancelled candidates, operation gating and bilingual confirmation/report UI.
- 新增“关联并同步 Desktop”，确认目标后备份并同步；回读真实文件判断完成，失败自动生成脱敏报告。不以主进程未检测到作为同步失败判据，不自动重启。

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
