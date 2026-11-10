# OpenCodexLauncher 3.1.9

- Fix the aggregation router 502 Bad Gateway caused by copying restricted response headers. The router now uses `ContentType` for the upstream content type and ignores headers that `HttpListener` does not allow.
- The aggregation router now writes a sanitized failure type/HRESULT to `logs/aggregation.log` instead of failing silently.
- Live local probe: the router forwarded a request to the running OpenCodex endpoint and returned its `400 missing input` response instead of 502.
- No change to the aggregation model/config design.

Local validation on 2026-11-10: 119 Native checks, 280 regression checks and 147 UI checks passed. No real model request was made.

## 3.1.8

- Add an Aggregation mode: Codex uses one local aggregate provider instead of switching its global `model_provider` between official and Native Packy.
- Official requests are relayed to the existing OpenCodex route; Packy model aliases are forwarded directly to Packy Responses with the Packy DPAPI credential.
- Aggregation writes a single provider table and a combined catalog, removes the old Native provider block, and backs up the direct configuration before switching.
- Added offline router tests for official auth passthrough, Packy auth replacement, model alias rewriting, SSE forwarding, catalog/config generation and disable restoration.

Local validation on 2026-11-05: 119 Native checks, 280 regression checks and 147 UI checks passed. No real model request was made.

## 3.1.7

- Fix Native provider binding after Codex Desktop updates. Native Prepare now writes the root `model_provider` for the selected `launcher-native-*` model instead of leaving Codex on `openai`.
- Proxy route preparation preserves an explicitly selected `launcher_native_*` provider, so a later sync no longer turns a Native chat back into an `openai` mismatch.
- Launcher startup detects an inconsistent Native alias/provider configuration and repairs it before the next Desktop session, with a log entry.
- Added offline tests for root provider binding, idempotent Native Prepare, and proxy-route preservation.

Local validation on 2026-11-05: 111 Native checks, 280 regression checks and 145 UI checks passed. No real model request was made.

## 3.1.6

- Detect when the configured OpenCodex port is already occupied before starting the proxy. This covers the common Clash/verge-mihomo conflict on 10100.
- Offer a nearby free port, write it to `.opencodex/config.json`, update the Codex `openai_base_url` route and the fallback `opencodex.config.toml`, then start normally.
- Show the actual port conflict/change in the Launcher log instead of only `Proxy start failed with exit code 1`.
- Added offline tests for occupied/free port detection, nearby free-port selection, config preservation and route rewriting.

Local validation on 2026-11-02: 109 Native checks, 280 regression checks and 145 UI checks passed. No real model request was made.

## 3.1.5

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
