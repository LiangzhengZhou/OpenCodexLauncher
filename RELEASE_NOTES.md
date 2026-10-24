# OpenCodexLauncher 3.1.3

- Native Helper now resolves the runtime from the installed Codex Desktop package: the runtime `codex.exe` must match the package's `app/resources/codex.exe` by content hash. A complete but outdated runtime is no longer silently reused.
- Automatic mode and explicit manual paths remain separate. `AutomaticRuntime=true` selects the package-compatible complete runtime; `false` keeps the user-selected path; legacy manifests without provenance are upgraded only when their path is inside Desktop's managed runtime root.
- Native Prepare and Enable re-resolve the current `PathSet` instead of reusing a Launcher-window cache. This prevents a long-running GUI from writing an old runtime back into `native-bridge.json`.
- Helper build consistency remains intact. Runtime compatibility is reported separately as `outdated` or `incompatible`, so `Helper=current` no longer hides `Runtime=outdated`.
- Native Bridge diagnostics are bounded and non-sensitive: runtime identifier, child PID/exit status, stderr byte count, JSON-RPC method names and `configRequirements` schema-presence booleans. Raw stderr, JSON payloads, prompts, credentials and user paths are never written.
- Offline regression tests cover complete-but-stale runtimes, package-hash matching, manual pinning, legacy manifests, incomplete/missing runtime recovery, Helper updates, old/new `configRequirements` schemas, child exit/EOF, provider isolation, Native aliases and credential redaction. No real model request is used.

Local validation on 2026-10-25: 98 Native checks, 278 regression checks and 145 UI checks passed; source and binary release scans passed. No real model request was made. A real Desktop restart acceptance test is intentionally left for the production rollout; this candidate is not installed into the current Windows Codex environment.
