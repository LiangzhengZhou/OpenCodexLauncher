# 3.1.4 本地离线回归报告

日期：2026-10-28
分支：`fix/native-package-core-fallback-314`
基线：v3.1.3 `4e18ce6`
候选版本：`3.1.4.0`

## 修复目标

Codex Desktop MSIX 更新后，包内 `app/resources/codex.exe` 已经变化，但 `%LOCALAPPDATA%\OpenAI\Codex\bin` 还没有生成新的 runtime 副本。3.1.3 会返回 `no-compatible-runtime`，Helper 不启动，Desktop 因此也无法运行自己的新 core 来生成副本，形成启动死锁。

3.1.4 在“本地 runtime 无 hash 匹配”时，若当前 Desktop 包内 `codex.exe` 与 `codex-code-mode-host.exe` 完整存在，则使用当前包内 core；只有包内也不完整时才失败关闭。

## 执行结果

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 102 项通过 |
| `tests/run-tests.ps1` | 278 项通过 |
| `tests/run-ui-tests.ps1` | 145 项通过，94 个渲染用例 |
| 真实环境只读 fallback 探针 | 返回当前 `26.1023.6621.0` 包内 `app/resources/codex.exe` |

全程未执行 `codex exec`、Desktop 真实消息、PackyAPI 或 OpenAI 推理请求。

## 覆盖重点

- 本地无匹配 runtime、包内 bundle 完整：自动模式返回包内 core。
- GUI 发现 `FindCurrentDesktopRuntime` 使用同一 fallback。
- Helper 健康状态从 `outdated` 变为 `healthy`（Prepare 写入包内 core 后）。
- 包内 core 不完整时仍返回 `no-compatible-runtime`，不会回退旧 runtime。
- 本地 hash 匹配 runtime 的优先级不变。
- 手动固定路径、遗留清单、Helper 更新、provider 隔离、凭据脱敏等原测试继续通过。

## 真实环境只读探针

用反射调用 3.1.4 候选程序集的 `CodexRuntime.FindCurrentDesktopRuntime()`，没有启动 Codex、没有修改配置：

```text
C:\Program Files\WindowsApps\OpenAI.Codex_26.1023.6621.0_x64__2p2nqsd0c76g0\app\resources\codex.exe
```

这证明在当前“新包 core + 旧 LocalAppData runtime”状态下，3.1.4 会选择包内 core，而不是再次返回 `no-compatible-runtime`。

## 未执行

- 真实 Codex Desktop 重启验收（需要安装 3.1.4 Helper 后执行）。
- 真实模型推理/消息。
