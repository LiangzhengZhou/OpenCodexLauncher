# 2.5.1 release notes / 发布说明

Release date: 2026-09-08. Windows 10/11 x64; .NET Framework 4.8. Entry point: OpenCodexLauncher.exe (2.5.1.0). Keep OpenCodexLauncher.exe.config beside it. MIT license.

Fix the one-click installation finalization failure when a Windows directory handle blocks the final runtime rename. A unique runtime path now stays stable throughout installation; the completion marker is committed atomically only after package and CLI validation. Failure and cancellation preserve the previous selection. Errors identify the failed phase and the actual log path, or explicitly report that no log could be saved.

修复一键安装在收尾阶段因目录占用导致重命名失败的问题。安装过程保持目录固定，验证通过后才保存完成标记；失败或取消不切换原路径。错误提示补充失败阶段、实际日志位置及中英文权限排查说明。截图中的具体占用来源需要受影响电脑的日志才能确认，真正的目录写入限制仍需检查。

Upgrade / 升级：Close the launcher, extract the entire Windows ZIP into the application directory, then reopen and retry. Keep existing local application data and runtimes. 关闭启动器后完整解压新版 Windows ZIP，再打开重试；无需删除配置，也不会自动重启已有代理。此版本修复启动器，设置页的“更新 OpenCodex”按钮不更新启动器自身。

The source ZIP contains allowlisted project files. The Windows ZIP contains the executable, .NET runtime configuration, documentation, MIT license and SHA-256 checksums. No account settings, API credentials, user providers or personal custom paths are included. OpenCodex and its runtime are downloaded on demand; Codex/Claude Code remain separate installations.

Validation: native and SDK builds; 113 isolated core checks (including the original 49); 33 WPF checks and 42 page/scale renders per UI run. The new Windows directory-handle test fails against the old installer and passes with the fix. Additional cases cover completion write failure, late cancellation and localized diagnostic reporting. A real installation is checked separately with empty account homes and a CLI version query. No paid inference requests or proxy restarts are used.

Known limits / 已知限制:

- Downloads require official npm registry and nodejs.org connectivity. Old generations and failed staging remain in private application data and may take hundreds of MB each; see README before cleanup.
- Existing services, tray entries and global commands stay on their old installation. Stop the old proxy manually, then start from this launcher to use the newly selected version. This feature does not update Codex CLI or the launcher itself.
- Reserve Force must be disabled before switching runtimes and checked for compatibility again afterwards. Disabling retains an inert source patch. External edits can require manual recovery using the private journal.
- Ordinary launched sessions stay pending until an authoritative session ID is available. Claude resolution requires full provider/model routes.
- TOML uses targeted edits rather than a complete parser. Not every malformed upstream TOML construct is diagnosed.
- Rendered scale tests do not replace switching actual monitor DPI. See the repository Actions page for hosted CI results.
- The EXE is not Authenticode signed. Download hashes verify integrity, not publisher identity or trustworthiness.
