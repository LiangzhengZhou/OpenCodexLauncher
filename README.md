# OpenCodex Launcher 3.0.1
This is the 3.0.1 stable release, correcting preview labels left in 3.0.0. Providers use overview, connection/API and model-state cards. The model workspace keeps per-provider cached lists and selections. Stored keys are masked, can be revealed and edited, and are hidden when navigating or losing focus. The provider default-model editor is removed; legacy fields remain intact.

The existing installation is not replaced. Version 2.6.5 is the confirmed critical rollback target, but the historical-version workflow and a real round-trip upgrade remain unverified. The historical rollback feature is not advertised as verified. The 2.6.x sections below document retained functionality.


## Diagnose message connection failures

Version 2.6.5 extends **Diagnose Desktop** for models that are visible but fail when sending a message. Update using the built-in launcher updater, then collect and copy a report while the fault is present. Report format 2 adds transport results: target loopback TCP, health GET, responses-address GET, and the presence (never values) of proxy environment settings inherited by the launcher. System-proxy applicability describes the launcher only; Desktop may have different settings. A localhost target is probed as IPv4 and explicitly marked.

These are bounded, credential-free GET/TCP checks, not a message test. GET 404/405 does not mean POST is broken; local POST authentication, Desktop-process connectivity and upstream inference remain unverified. Empty recent-request logs do not prove requests never arrived: upstream may reject a request before recording it. No bodies, cookies, credentials, provider URLs or raw error text enter the report. Unsupported or non-loopback targets are reported without probing. Normal Start OpenCodex health checks now bypass system proxies and refuse redirects.

## Associate and verify Desktop synchronization

Version 2.6.4 added **Associate and sync Desktop** to Overview, Models, Codex routing and the diagnostic report. It lists readable configuration candidates; review the displayed paths and confirm the one used by Desktop. Candidate discovery alone does not change settings. Standard installations usually use the default user home; a custom installation may use another home. Existing explicit associations remain first. If the correct file is not listed, use **Associate Desktop config…** on Codex routing.

Confirmation saves the association, keeps the OpenCodex provider store and selected models, creates a private pre-sync snapshot and runs upstream sync once. It may contact model metadata services but sends no inference request and does not restart Desktop. The snapshot is for recovery, not an atomic rollback: upstream can partially modify files; do not restore over subsequent edits. Never share recovery directories.

Every normal sync now reads back the root catalog reference, the actual catalog, each selected full provider/model route and the configured proxy port. A zero CLI exit alone no longer means success. Missing sources/references/files or models produce an incomplete result and automatically open the sanitized diagnostic report. Confirmed association and sync opens a report on success too. **Copy report** is sufficient for feedback; no configuration upload is needed. The report includes the last attempt's fixed result/upstream codes and time, plus current native-cache availability and counts. Raw upstream output remains in local application logs only.

A missing native catalog source can still require an OpenCodex update or investigation; this release does not fabricate catalog metadata. A valid disk result is useful even when process detection or explicit association is unavailable. Neither result proves that Desktop loaded the catalog; inspect the picker after safely restarting Desktop yourself if needed. Read-only **Diagnose Desktop** remains separate from this confirmed write operation.


## One-click Desktop diagnostics

When Desktop's bottom-right model picker is missing third-party models, click **Diagnose Desktop** on **Overview**, **Models** or **Codex routing**, then **Copy report** or **Save diagnostic log…** and send the resulting text to the maintainer. No manual file search or configuration upload is needed to collect evidence.

The report compares the launcher target with the default and launcher-environment Codex homes; inspects root catalog/proxy references, selected model counts and full-route catalog coverage; queries Codex process candidates; and checks local health endpoints. Findings distinguish a possible home mismatch, absent references, missing selected models, unreadable files, unavailable proxy and unknown process state. Candidate homes and running processes do not prove which configuration Desktop loaded. No inference request is sent.

Checks begin only after setup and an explicit click. They do not edit configuration, import credentials, run CLI commands, stop services or restart Desktop. Network checks use GET /healthz on numeric loopback candidate ports plus one TCP attempt and GET of the configured standard responses path on the target loopback port only, without credentials, system proxy, response-body logging or redirects. File reads are bounded to 2 MiB each and local fixed drives; linked files, network paths, relative catalog references and unsupported TOML syntax remain unverified. The overall inspection deadline is 12 seconds; closing the launcher cancels active checks. If a system call remains blocked, the deadline returns a timeout report and ignores late results.

Reports contain fixed finding codes, counts, relationships, timestamp, launcher version and allowlisted transport statuses, local ports, fixed route paths and proxy-presence flags. They exclude raw configuration, application/request logs, command lines, keys, account data, provider/model names, URLs and absolute paths. Copy/save is explicit; nothing is uploaded automatically. The report helps select the next fix but does not claim to test remote model availability or verify Desktop's loaded model list.


[简体中文](README.zh-CN.md)

A Windows desktop launcher for managing local OpenCodex providers, model selection and Codex routing. English / Chinese switching preserves form drafts and model selections. This is an independent community project.

## Requirements and first use

Windows 10/11 x64 with .NET Framework 4.8. The first-use **Install and start setup** button downloads OpenCodex and its private Node.js/Bun runtimes. No prior Node/npm installation or administrator rights are needed. Codex and optional Claude Code are separate tools. The launcher does not provide an API subscription.

Extract the Windows ZIP to a writable application directory and run OpenCodexLauncher.exe. The download includes no provider, model, API key or custom path configuration.

On first launch, choose **Detect and import local configuration** to preview detected paths and a provider count, then explicitly confirm the association; or choose **Manual setup** to start with independent empty configuration homes. Before that choice, the launcher does not load ambient providers or credentials, invoke CLI tools or probe the proxy. Manual mode stores its homes under the launcher's local application data directory; configure executable paths in Settings and add providers yourself. Examples use demo-provider and https://example.invalid/v1 only.

Use the persistent **中文 / EN** button to change language. The initial language follows the system UI language: Chinese for zh, English otherwise. IDs, protocol values, user input and external program logs remain unchanged.

## Update the launcher

In 2.6.0+, use the built-in updater to install 2.6.5. For Codex Desktop's model picker, follow the Desktop association steps below.

In **Settings**, use **Check launcher updates**, then **Update and restart**. The panel is also available on onboarding and recovery pages. Save form edits before confirming. Only an explicit click contacts the public GitHub Releases API; no GitHub login is needed. Updates verify GitHub's SHA-256 digest, internal file checksums and executable version. Rate limits, missing digests and network restrictions stop the update. Access to api.github.com, github.com and GitHub's release CDN is required.

The helper waits for this launcher to close, backs up release files, replaces them and starts the new launcher. OpenCodex services, settings, credentials, runtime selection and unrelated files are preserved. Close other launcher windows first. Write failures attempt rollback; external changes stop restoration and retain backups. The installation must be writable; there is no automatic administrator elevation. Cancelled downloads leave installed files unchanged. There are no background update checks or downgrades.

Versions before 2.6.0 need one final manual Windows ZIP upgrade to acquire this feature. Close the launcher and extract the entire package into its application directory. Subsequent releases can be installed through this panel. OpenCodex runtime updates remain separate.

If power loss interrupts replacement, keep %LOCALAPPDATA%/OpenCodexLauncher/updates and recovery. updates/<id>/recovery.txt points to the backup journal; journal.json maps target files to numbered .original backups. Close launcher windows, compare current files, then restore the full original file set. Entries with no BeforeFile represent newly added files. Preserve application data and unrelated files; do not blindly overwrite external edits. Alternatively extract a verified Windows release ZIP. Interrupted transactions do not resume automatically. Old backups remain until manually removed; never share these private directories.

The EXE is not Authenticode signed. Hash verification trusts HTTPS and the repository publisher; it cannot protect against a compromised publisher. Unknown archive entries and links are rejected, so future package-layout changes may require a manual upgrade.

## Third-party models missing in Codex Desktop

The launcher and Codex Desktop can use different configuration homes, especially after manual or one-click setup. Successful sync updates the selected files; it does not prove Desktop loaded those files.

1. In Codex Desktop settings, open its config.toml and note its location. Do not share its contents or account files.
2. In the launcher, open **Codex routing → Associate Desktop config…** (also accessible from Settings). Select that existing config.toml, review the target and confirm.
3. Existing provider configuration, keys and model selections stay in place. Association alone saves the target in launcher settings; it does not edit the selected config, copy account files, run CLI commands or restart services.
4. Start OpenCodex from Overview and sync the model catalog. Later syncs back up and modify the selected Codex config and catalog. If the proxy is stopped, start it before expecting routed requests to work.
5. If Desktop still shows its old list, finish active tasks and use **Sync and restart Desktop**. Review the actual restart outcome in the log, then check Desktop's bottom-right model picker. This action can interrupt Desktop sessions.

Do not assume the independent manual home is Desktop's home, or change the provider configuration mode to work around this. The target is shown on Codex routing. Association requires completed onboarding and a readable existing config.toml; a missing associated file produces an error instead of a replacement. Restore the selected file and reload if startup enters recovery. The launcher does not automatically detect or verify the running Desktop process's home.

Use Diagnose Desktop above to collect current, sanitized evidence before changing configuration.

## Third-party models missing in the launcher's Models page

On **Providers**, fetch models, check the required entries, then click **Save selection**. The launcher verifies the saved selection and updates **Models** immediately; this local list does not require Codex, proxy startup or catalog sync. Saving provider details or a default model ID alone does not enable models. **Save selection and sync to Codex** performs the same local save before the separate sync step. A sync failure does not undo the saved selection.

Version 2.6.1 also reads provider-selected/configured models without requiring duplicate customModels entries. Entering **Models** reloads local selections. A missing or unreadable generated catalog and a failed native CLI refresh no longer hide saved third-party models. The page shows native/third-party counts and relevant warnings.

If a test machine still shows zero third-party models, use Diagnose Desktop and share its report. Do not send config.json, credentials or an application-data ZIP.

## Install and update OpenCodex

Choose **Install and start setup** on the first-use page to install OpenCodex with empty account configuration. In **Settings**, use **Check for updates**, **Install / update to latest stable**, **Cancel install / check**, or **Switch to previous version**. Each click queries npm's stable tag and pins the returned version. Known newer stable installations are not downgraded. There are no automatic update checks.

The installer downloads Node LTS from nodejs.org and checks its published SHA-256. It downloads the pinned @bitkyc08/opencodex package from registry.npmjs.org and verifies SHA-512 integrity. HTTPS metadata and hashes detect transfer corruption; they cannot protect against a compromised upstream publisher. npm verifies dependency integrity and engine compatibility. Dependency lifecycle scripts are disabled; only Bun's required runtime installer runs. npm uses empty user/global configuration and a private cache without inherited npm tokens, provider credentials or Node preload options. CLI verification uses temporary empty account homes.

Downloads require access to nodejs.org and registry.npmjs.org and may take several minutes. There is no offline runtime bundle. Each installation uses a unique directory under %LOCALAPPDATA%/OpenCodexLauncher/runtimes. The new executable is selected only after package and CLI verification. Failure/cancellation leaves previous settings and configuration intact. Cancel/close terminates the install process group. Failed staging and previous generations are retained for diagnosis/rollback and may occupy hundreds of MB each. Remove unused generations only after confirming no proxy, service or saved path uses them. Retain the selected and previous generation. Never publish this private directory.

Only the launcher's executable selection changes. External commands, Windows services and tray entries retain their original installation; existing proxies are not restarted. Stop the old proxy yourself through its existing management controls, then start it from this launcher to use the new version. Disable Reserve Force before switching; re-enabling requires new source compatibility checks. This updates OpenCodex, not the launcher itself or Codex CLI.

Keep OpenCodexLauncher.exe.config beside the EXE for .NET 4.8 and long dependency path support. The installer does not edit system PATH or install a global npm command.

## Data and upgrades

If 2.5.0 reports access denied or a directory in use under runtimes/.staging-..., close the launcher and extract the full 2.6.0 Windows ZIP into your application directory, including the EXE configuration file. Reopen it and retry installation. Keep your local application data and existing runtimes. Version 2.5.1 and later install directly into a unique stable directory and writes installation.json only after validation, avoiding the final directory move. An incomplete directory is retained for diagnosis and is not selected automatically.

If installation still fails, the error includes the failed phase and the diagnostic log path when one could be written. For an access error, check directory permissions, open handles and security software block history. Review logs for personal paths before sharing; do not upload your entire runtimes or configuration directory. This fix does not bypass a genuine write restriction.

Settings and current-user DPAPI-encrypted launcher credentials live under %LOCALAPPDATA%/OpenCodexLauncher. Do not share that directory. Associated OpenCodex/Codex configurations remain external to the application. Upstream files can contain their own plaintext credentials; DPAPI applies only to credentials saved in the launcher's credential store.

Back up the old installation and local application data before replacing the executable. Version 2.5 recognizes existing settings and preserves strategy, paths and advanced flags. It migrates the schema when settings are next saved. Normal startup never imports settings.json beside the EXE. If an older installation used only that file, copy it into local application data as an explicit upgrade step, only when no settings file already exists. Damaged settings open a recovery page and are not replaced with empty settings.

## Routing and advanced features

Full provider/model IDs are required for routed models. Models from another provider are never selected by suffix. Gateways exposing only generated aliases rather than full routes are rejected by the Claude resolver.

Session verification requires an authoritative session/conversation ID, a valid timestamp and successful response evidence. Ordinary launched processes currently do not expose such an ID to the launcher, so they remain **Pending verification**. Global recent logs do not justify terminating a session. Strict verification gates termination after an attributable mismatch; startup and verification fallback share one retry budget and retain the original model and project.

**Reserve Force** is advanced and off for new users. Confirmation explains source/configuration changes and proxy restart. Compatible upstream source shapes are required. The launcher snapshots affected files and journals owned writes; failure attempts to restore previous bytes. Detected external edits stop restoration instead of being overwritten. Private journals and original files remain under local application data/recovery; compare them with current files before manual restoration. Check or restart the proxy yourself after a failed operation. Disabling Reserve Force clears its route configuration but leaves the inert compatibility patch in upstream source. Upstream upgrades may require compatibility rechecking.

## Build and test

Native Windows build using the .NET Framework compiler:

    powershell -NoProfile -File ./build-v2.ps1 -OutputDirectory ./dist

SDK build, requiring .NET SDK and .NET Framework 4.8 reference assemblies:

    dotnet build OpenCodexLauncher.csproj -c Release -o ./dist-sdk

Or use build-sdk.ps1. Both builds produce OpenCodexLauncher.exe, version 3.0.1.0.

    ./tests/run-tests.ps1 -OutputDirectory ./test-output
    ./tests/run-ui-tests.ps1 -OutputDirectory ./test-output

Tests use isolated homes and fictional credentials. Network tests use a loopback mock server, not inference requests. UI tests cover both languages at minimum window size, retain drafts, and render pages at 100%, 150% and 200%. These renders do not replace real monitor DPI switching tests.

## Prepare a public release

    ./create-release.ps1 -OutputDirectory ./release-output

Publish only the generated github-source directory or generated ZIP files. The exact release-files.txt allowlist excludes user data, historical files, binaries from source, caches and private work. The release check validates the allowlist and searches text/resource/binary strings for credential-like values and personal paths. Maintainers should additionally pass their private strings in memory to release-check.ps1 using ForbiddenValues; never commit or print those values. No scanner guarantees detection of every secret or encoding. The supplied GitHub Actions workflow checks both builds, regressions and packaging after the repository is published.

See [release notes](RELEASE_NOTES.md), [security notes](SECURITY.md) and [MIT license](LICENSE).

### Startup fails with CODEX_HOME / ENOENT

Version 2.5.2 and later create the empty launcher-owned Codex and OpenCodex home directories when manual or one-click setup completes. Existing installations with missing launcher-owned homes are repaired on the next explicit CLI command. No account files are imported. Missing imported/custom directories are reported instead of silently replaced. A CLI help preflight captures runtime/import errors before starting the proxy; this also catches problems that a version-only check misses.

If you saw this failure in 2.5.1, close the launcher and extract the entire 2.6.0 Windows ZIP, then reopen and retry. Do not delete application data, reinstall OpenCodex, or re-enter provider keys for this directory-creation defect. Other startup failures can still require the upstream runtime logs.

### Sync reports missing config.toml

Version 2.6.0 also creates an empty config.toml if it is missing from the launcher-managed manual Codex home, during explicit setup or CLI actions. Existing file contents are preserved. Retry sync after upgrading. Codex remains a separate installation: without an available Codex model catalog, OpenCodex 2.47.0 may complete sync but report that the proxy is not ready. Install/configure Codex and explicitly associate its valid home/catalog through setup. The launcher does not import accounts or invent native models to suppress this warning.
