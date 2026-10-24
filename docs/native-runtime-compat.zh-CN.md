# Native Runtime 自动兼容（3.1.3）

## 问题

Codex Desktop 更新后，旧 runtime 目录可能仍然完整。3.1.1/3.1.2 的 `CodexRuntime.IsUsable` 只检查 `codex.exe` 与 `codex-code-mode-host.exe` 是否存在，因此 `ResolveNative` 会继续返回旧的 `RealCodex`。新版 Desktop 的 `configRequirements/read` 协议可能与旧 Core 不兼容，表现为：

```text
Desktop startup policy read failed ... phase=policy-read reason=protocol
```

## 3.1.3 规则

- 自动模式（`AutomaticRuntime=true`）不再把“文件完整”等同于“版本兼容”。
- 从当前用户已注册的 `OpenAI.Codex` AppModel 包读取安装根目录，计算包内 `app/resources/codex.exe` 的 SHA-256。
- 只在 Desktop 管理目录中查找 `codex.exe` + `codex-code-mode-host.exe` 完整、长度相同且内容哈希匹配的 runtime。
- 不比较 CLI `0.164` 与 Desktop `26.x` 两套版本号；以内容哈希为准。
- 找不到匹配项时返回 `NativeRuntimeException` 的明确错误码，不启动旧 Core：
  - `desktop-package-unavailable`
  - `desktop-core-missing`
  - `no-compatible-runtime`
  - `manual-runtime-incomplete`
- 手动模式（`AutomaticRuntime=false`）继续尊重用户固定路径。
- 3.1.0 遗留清单（`AutomaticRuntime=null`）：
  - 路径在 Desktop 管理目录之外且完整：保留为显式选择；
  - 路径在 Desktop 管理目录内或已缺失：按自动模式重新解析。

## 健康状态

`NativeActivation.RuntimeHealth` / `RuntimeHealthFor` 现在区分：

- `healthy`：记录的 runtime 与当前 Desktop 包匹配；
- `outdated`：记录项完整，但应切换到当前包匹配的 runtime；
- `incompatible`：找不到与当前 Desktop 包匹配的完整 runtime；
- `missing` / `stale` / `unverified`：原有清单、显式路径和不可验证状态。

Helper 构建一致性与 Runtime 兼容性分开报告，`Helper=current` 不会掩盖 `Runtime=outdated`。

## 诊断日志

`NativeBridgeDiagnostics` 仅写入有界、脱敏字段：

- Runtime 目录标识与文件长度；
- 子进程 PID、退出码、stderr 字节数；
- JSON-RPC 方法名（`initialize`、`configRequirements/read` 等）；
- `configRequirements` 响应中 `requirements` / `appServerVersion` 是否存在；
- 失败类别与 HRESULT。

不保存原始 stderr、JSON-RPC payload、prompt、文件内容、凭据或用户路径。日志上限 128 KiB，滚动保留一份。

## 测试

`tests/NativeChecks.cs` 使用 fixture runtime、fixture Desktop 包内容和 `MockAppServer.exe`，不调用任何真实模型接口。覆盖旧 runtime 完整但包已升级、包哈希匹配、手动固定、遗留清单、残缺/缺失恢复、Helper 更新、旧/新 `configRequirements` schema、子进程退出/不完整帧、provider 隔离、Native 别名和凭据脱敏。
