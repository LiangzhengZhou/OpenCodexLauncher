# OpenCodex Launcher 2.5.0

[简体中文](README.zh-CN.md)

A Windows desktop launcher for managing local OpenCodex providers, model selection and Codex routing. English / Chinese switching preserves form drafts and model selections. This is an independent community project.

## Requirements and first use

Windows 10/11 x64 with .NET Framework 4.8. The first-use **Install and start setup** button downloads OpenCodex and its private Node.js/Bun runtimes. No prior Node/npm installation or administrator rights are needed. Codex and optional Claude Code are separate tools. The launcher does not provide an API subscription.

Extract the Windows ZIP to a writable application directory and run OpenCodexLauncher.exe. The download includes no provider, model, API key or custom path configuration.

On first launch, choose **Detect and import local configuration** to preview detected paths and a provider count, then explicitly confirm the association; or choose **Manual setup** to start with independent empty configuration homes. Before that choice, the launcher does not load ambient providers or credentials, invoke CLI tools or probe the proxy. Manual mode stores its homes under the launcher's local application data directory; configure executable paths in Settings and add providers yourself. Examples use demo-provider and https://example.invalid/v1 only.

Use the persistent **中文 / EN** button to change language. The initial language follows the system UI language: Chinese for zh, English otherwise. IDs, protocol values, user input and external program logs remain unchanged.

## Install and update OpenCodex

Choose **Install and start setup** on the first-use page to install OpenCodex with empty account configuration. In **Settings**, use **Check for updates**, **Install / update to latest stable**, **Cancel install / check**, or **Switch to previous version**. Each click queries npm's stable tag and pins the returned version. Known newer stable installations are not downgraded. There are no automatic update checks.

The installer downloads Node LTS from nodejs.org and checks its published SHA-256. It downloads the pinned @bitkyc08/opencodex package from registry.npmjs.org and verifies SHA-512 integrity. HTTPS metadata and hashes detect transfer corruption; they cannot protect against a compromised upstream publisher. npm verifies dependency integrity and engine compatibility. Dependency lifecycle scripts are disabled; only Bun's required runtime installer runs. npm uses empty user/global configuration and a private cache without inherited npm tokens, provider credentials or Node preload options. CLI verification uses temporary empty account homes.

Downloads require access to nodejs.org and registry.npmjs.org and may take several minutes. There is no offline runtime bundle. Each installation uses a unique directory under %LOCALAPPDATA%/OpenCodexLauncher/runtimes. The new executable is selected only after package and CLI verification. Failure/cancellation leaves previous settings and configuration intact. Cancel/close terminates the install process group. Failed staging and previous generations are retained for diagnosis/rollback and may occupy hundreds of MB each. Remove unused generations only after confirming no proxy, service or saved path uses them. Retain the selected and previous generation. Never publish this private directory.

Only the launcher's executable selection changes. External commands, Windows services and tray entries retain their original installation; existing proxies are not restarted. Stop the old proxy yourself through its existing management controls, then start it from this launcher to use the new version. Disable Reserve Force before switching; re-enabling requires new source compatibility checks. This updates OpenCodex, not the launcher itself or Codex CLI.

Keep OpenCodexLauncher.exe.config beside the EXE for .NET 4.8 and long dependency path support. The installer does not edit system PATH or install a global npm command.

## Data and upgrades

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

Or use build-sdk.ps1. Both builds produce OpenCodexLauncher.exe, version 2.5.0.0.

    ./tests/run-tests.ps1 -OutputDirectory ./test-output
    ./tests/run-ui-tests.ps1 -OutputDirectory ./test-output

Tests use isolated homes and fictional credentials. Network tests use a loopback mock server, not inference requests. UI tests cover both languages at minimum window size, retain drafts, and render pages at 100%, 150% and 200%. These renders do not replace real monitor DPI switching tests.

## Prepare a public release

    ./create-release.ps1 -OutputDirectory ./release-output

Publish only the generated github-source directory or generated ZIP files. The exact release-files.txt allowlist excludes user data, historical files, binaries from source, caches and private work. The release check validates the allowlist and searches text/resource/binary strings for credential-like values and personal paths. Maintainers should additionally pass their private strings in memory to release-check.ps1 using ForbiddenValues; never commit or print those values. No scanner guarantees detection of every secret or encoding. The supplied GitHub Actions workflow checks both builds, regressions and packaging after the repository is published.

See [release notes](RELEASE_NOTES.md), [security notes](SECURITY.md) and [MIT license](LICENSE).
