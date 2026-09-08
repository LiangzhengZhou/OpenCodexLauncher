# 2.5.2 release notes / 发布说明

Release date: 2026-09-08. Windows 10/11 x64; .NET Framework 4.8. OpenCodexLauncher.exe (2.5.2.0); keep its .config file beside it. MIT license.

Fix the startup failure reporting CODEX_HOME / ENOENT after one-click installation or manual setup. The launcher previously selected its private Codex directory without creating it; OpenCodex 2.47.0 requires that directory during CLI import. Setup now creates empty launcher-owned homes. Existing affected installations repair these homes on the next explicit CLI command. Existing files are preserved, and missing external/custom homes are reported rather than silently replaced.

修复一键安装或手动配置后出现“代理启动失败，退出代码 1”，详细错误为 CODEX_HOME / ENOENT 的问题：启动器此前只指定目录但没有创建，OpenCodex 2.47.0 加载 CLI 时即退出。新版创建启动器管理的空目录；受影响旧配置在下次明确运行命令时补建，已有文件保留，不导入账号数据。

A CLI help preflight now runs before proxy launch and reports bounded, redacted runtime/import errors. Unlike --version, it exercises the Bun CLI imports. This does not diagnose every possible error after the proxy starts.

Upgrade / 升级：Close the launcher, extract the entire Windows ZIP into its application directory, then reopen and retry. Keep application data and installed OpenCodex runtimes. No provider-key re-entry or runtime reinstall is needed for this defect. 关闭启动器后完整解压新版 ZIP，再打开重试；无需删除配置、重新填写密钥或重装 OpenCodex。设置页的“更新 OpenCodex”按钮不会更新启动器自身。

Validation: native and SDK builds; 123 isolated core checks, 35 WPF checks and 42 page/scale renders per UI run. A separate real OpenCodex 2.47.0 test reproduces the missing-home error before repair, passes CLI help after repair, and starts a proxy to HTTP 200 /readyz with a fictitious provider, isolated homes and no inference requests. Only the test process tree is stopped. No existing user proxy is restarted.

Public packages contain only allowlisted source or application files, public documentation, license and checksums. No personal API keys, provider configuration, custom paths, account data or private test logs are shipped. Fresh users remain empty until explicit setup. OpenCodex and its runtime download on demand; Codex and Claude Code are separate installations.

Known limits: downloads need npm/nodejs.org connectivity; missing external directories or genuine access restrictions need correction. Other proxy startup failures may require upstream logs. Existing services/global commands retain their original runtime until manually switched. Reserve Force needs to be disabled before runtime changes. EXE is not Authenticode signed. See README and repository Actions for additional limitations and CI results.
