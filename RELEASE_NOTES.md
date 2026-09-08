# 2.5.0 release notes / 发布说明

Release date: 2026-09-07. Windows 10/11 x64; .NET Framework 4.8. Entry point: OpenCodexLauncher.exe (2.5.0.0). Keep OpenCodexLauncher.exe.config beside it. MIT license.

首次引导新增“一键安装并开始配置”；设置页新增检查更新、安装 / 更新到最新稳定版、取消和切回上一版本。安装器准备独立 Node.js/Bun 环境，首次账号配置保持空白；已有用户保留供应商、凭据、路径及策略。更新成功后只切换启动器选用的程序，当前代理、外部命令、系统服务及托盘不自动迁移或重启。

The source ZIP contains allowlisted project files. The Windows ZIP contains the executable, .NET runtime configuration, documentation, MIT license and SHA-256 checksums. No account settings, API credentials, user providers or personal custom paths are included. OpenCodex and its runtime are downloaded on demand; Codex/Claude Code remain separate installations.

Validation: native and SDK builds; 105 isolated core checks (including the original 49); 33 WPF checks, including one-click setup, live localization, cancellation and empty provider/model state; 42 page/scale renders per UI run. A real installation was verified in a separate empty test environment with a CLI version check, without starting a proxy or using account credentials. Cancellation of the installer process tree was tested on Windows. No paid inference requests were made.

Known limits / 已知限制:

- Downloads require official npm registry and nodejs.org connectivity. Old generations and failed staging remain in private application data and may take hundreds of MB each; see README before cleanup.
- Existing services, tray entries and global commands stay on their old installation. Stop the old proxy manually, then start from this launcher to use the newly selected version. This feature does not update Codex CLI or the launcher itself.
- Reserve Force must be disabled before switching runtimes and checked for compatibility again afterwards. Disabling retains an inert source patch. External edits can require manual recovery using the private journal.
- Ordinary launched sessions stay pending until an authoritative session ID is available. Claude resolution requires full provider/model routes.
- TOML uses targeted edits rather than a complete parser. Not every malformed upstream TOML construct is diagnosed.
- Rendered scale tests do not replace switching actual monitor DPI. Hosted GitHub Actions has not been executed locally.
- The EXE is not Authenticode signed. Download hashes verify integrity, not publisher identity or trustworthiness.
