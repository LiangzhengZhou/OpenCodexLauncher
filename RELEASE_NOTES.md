# OpenCodexLauncher 3.1.5

- Automatic Native Helper update for enabled installations whose Helper is only build-outdated. The Home page now reports the restart requirement instead of requiring a separate update click every Launcher release.
- Desktop diagnostics detect the current `ChatGPT.exe` Desktop process as well as legacy `Codex.exe`, report observed process names, and separate observation scope (`transport-get-only`, `desktop-loading-unverified`) from actual findings.
- `desktopHomeEvidence` now reflects the explicit association instead of always reporting `candidates-only`.
- Duplicate sync entries were reduced: the routes page no longer duplicates the daily sync, the models page no longer duplicates Desktop association, and Native Prepare/diagnostic launch moved under Advanced.
- Added offline tests for automatic Helper update and the new diagnostic fields. No real model request is used.

Local validation on 2026-10-28: 104 Native checks, 280 regression checks and 145 UI checks passed. No real model request was made.

## 3.1.4

- Fixed a Desktop-update startup deadlock: when the installed MSIX package contains a new `app/resources/codex.exe` but the LocalAppData runtime copy has not been refreshed yet, automatic mode now uses the complete packaged core as a safe fallback.
- Resolution order is: local runtime whose `codex.exe` hash matches the current package core; otherwise the current package's complete bundled core; otherwise fail closed with `no-compatible-runtime`. A stale complete runtime is still never reused.
- This preserves the 3.1.3 package-hash compatibility guarantee while allowing Desktop to start after an MSIX update, instead of blocking the very process that would create the new runtime copy.
- Added offline fixture coverage for package-core fallback, GUI discovery and Helper health transitions. No real model request is used.

Local validation on 2026-10-28: 102 Native checks, 278 regression checks and 145 UI checks passed; a read-only probe against the installed 26.1023 package returned its bundled `app/resources/codex.exe` as the fallback. Source and binary release scans passed. No real model request was made.

## 3.1.3

- Native Helper now resolves the runtime from the installed Codex Desktop package: the runtime `codex.exe` must match the package's `app/resources/codex.exe` by content hash. A complete but outdated runtime is no longer silently reused.
- Automatic mode and explicit manual paths remain separate. `AutomaticRuntime=true` selects the package-compatible complete runtime; `false` keeps the user-selected path; legacy manifests without provenance are upgraded only when their path is inside Desktop's managed runtime root.
- Native Prepare and Enable re-resolve the current `PathSet` instead of reusing a Launcher-window cache. This prevents a long-running GUI from writing an old runtime back into `native-bridge.json`.
- Helper build consistency remains intact. Runtime compatibility is reported separately as `outdated` or `incompatible`, so `Helper=current` no longer hides `Runtime=outdated`.
- Native Bridge diagnostics are bounded and non-sensitive: runtime identifier, child PID/exit status, stderr byte count, JSON-RPC method names and `configRequirements` schema-presence booleans. Raw stderr, JSON payloads, prompts, credentials and user paths are never written.
- Offline regression tests cover complete-but-stale runtimes, package-hash matching, manual pinning, legacy manifests, incomplete/missing runtime recovery, Helper updates, old/new `configRequirements` schemas, child exit/EOF, provider isolation, Native aliases and credential redaction. No real model request is used.

Local validation on 2026-10-25: 98 Native checks, 278 regression checks and 145 UI checks passed; source and binary release scans passed. No real model request was made. A real Desktop restart acceptance test is intentionally left for the production rollout; this candidate is not installed into the current Windows Codex environment.
