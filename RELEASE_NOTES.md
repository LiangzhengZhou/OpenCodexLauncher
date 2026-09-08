# 2.6.0 release notes / 发布说明

Release date: 2026-09-08. Windows 10/11 x64; .NET Framework 4.8. OpenCodexLauncher.exe version 2.6.0.0. Keep its .config file beside it. MIT license.

## Update the launcher in the app / 软件内更新启动器

In Settings, click **Check launcher updates**, then **Update and restart**. The same panel is available during onboarding and configuration recovery. Updates are requested explicitly from this repository's latest stable GitHub Release. No GitHub account or background checks are required. Chinese and English text switches immediately.

在设置、首次使用或配置恢复页面，点击 **检查启动器更新**，有新版本后点击 **一键更新并重启**。确认前保存表单修改。此功能更新 OpenCodexLauncher 本身；原有“更新 OpenCodex”功能继续用于更新代理运行时。

The download is checked against GitHub's SHA-256 digest, the package's internal checksums, allowed file names and executable version. The helper waits for this launcher to exit, retains a backup, replaces release files and starts the verified new executable. It preserves settings, credentials, installed runtimes and unrelated files; existing OpenCodex services are not restarted. Cancellation and failed validation leave the installation unchanged. Replacement failures attempt rollback; concurrent external edits retain recovery information instead of being overwritten.

下载完成后校验来源、SHA-256、包内文件及版本。更新助手等待当前启动器退出后备份、替换并打开新版。保留设置、凭据、运行时及其他文件；不重启已有代理服务。安装目录须可写，其他启动器窗口须先关闭。替换失败尝试恢复，发现外部修改时保留备份供人工处理。

**Versions before 2.6.0 need one final manual upgrade:** close the launcher and extract the entire Windows ZIP into its application directory. Subsequent versions can use the new panel. Keep local application data and runtime installations.

**旧版用户需最后手动升级一次到 2.6.0**，才能获得这个按钮：关闭启动器，完整解压 Windows ZIP 到软件目录，重新打开。之后可在软件内更新。无需删除配置、重新填写密钥或重装 OpenCodex。

## Missing manual Codex configuration / 手动配置文件缺失

Fix the reported “Codex config not found … config.toml / Codex sync did not complete” failure. Explicit manual setup and CLI actions now create an empty config.toml only when it is missing from the launcher-managed Codex home. Existing files are never overwritten. Missing external/custom homes are reported. This builds on the 2.5.2 missing-directory fix and imports no account data.

修复手动配置下缺少 config.toml 导致的同步失败。在明确配置或运行命令时，仅为启动器管理的 Codex 目录补建缺失的空 TOML 文件；保留已有内容，不导入本机账号。首次打开软件仍为空白。

Codex itself remains a separate installation. If its model catalog is unavailable, OpenCodex 2.47.0 can complete configuration sync but still report that the proxy is not ready. Install/configure Codex and explicitly associate a valid home/catalog through setup, then retry. The launcher does not manufacture native models or import another account to suppress that warning.

Codex 仍需单独安装。完全没有 Codex 模型目录时，上游可能在配置同步成功后仍报告未就绪；应安装并配置 Codex，再通过配置引导明确关联有效目录。此更新不伪造原生模型来隐藏该提示。

## Verification and limits / 验证与限制

Both native and SDK builds pass 163 isolated core checks and 38 WPF checks each, with 42 page/scale renders per UI run. Tests cover validation, cancellation, transactional replacement, rollback and an actual helper/parent/restarted-child fixture. Real OpenCodex 2.47.0 reproduces the missing-config failure and confirms successful explicit sync after repair. Readiness is tested with a separate test-only catalog cache. No paid inference or user service restart is used.

Public source and Windows packages use release allowlists and text/resource/binary scans. Personal provider settings, endpoints, credentials, custom paths, account data and test logs are excluded. Fresh users start with empty configuration; dependencies download on demand.

GitHub access (including its release CDN) is required; rate limits, unavailable SHA-256 metadata or network failures stop the update. The EXE is not Authenticode signed; hashes trust HTTPS and the repository publisher. There is no administrator elevation, downgrade, automatic interrupted-update recovery or backup cleanup. If power loss interrupts replacement, consult README recovery instructions or extract a verified full Windows ZIP. Retained backups and runtime logs are private. Future package-layout changes may need a manual upgrade.
