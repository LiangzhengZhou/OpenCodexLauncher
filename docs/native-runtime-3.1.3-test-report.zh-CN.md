# 3.1.3 本地离线回归报告

日期：2026-10-25
分支：`fix/native-runtime-compat-313`
基线：v3.1.2 `7320dec`
候选版本：`3.1.3.0`

## 执行结果

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 98 项通过 |
| `tests/run-tests.ps1` | 278 项通过 |
| `tests/run-ui-tests.ps1` | 145 项通过，94 个渲染用例 |
| `create-release.ps1` 的 source/binary privacy scan | 通过 |

全程未执行 `codex exec`、Desktop 真实消息、PackyAPI 或 OpenAI 推理请求；测试使用 fixture runtime、fixture Desktop 包内容和 `MockAppServer.exe`。

## 覆盖重点

- 旧 Runtime A 完整、Desktop 包已升级到 Runtime B：自动模式选择 B，不按时间戳或旧清单继续使用 A。
- Desktop 包 core 与 runtime `codex.exe` 内容哈希匹配；无匹配时 `no-compatible-runtime` 失败关闭。
- 手动固定路径 `AutomaticRuntime=false` 保持原语义。
- 3.1.0 遗留清单（`AutomaticRuntime=null`）：Desktop 管理目录外保持显式路径，目录内升级到包兼容 runtime。
- 旧 runtime 缺 host / 缺 exe：按包兼容 runtime 恢复。
- Launcher 旧 `PathSet` 场景：fresh Native Prepare 替换 bridge 中的旧 runtime，同时保持用户 provider 配置字节不变。
- Helper 构建一致性与 Runtime 兼容性分开报告：`Runtime=outdated` / `incompatible` 不会被 `Helper=current` 掩盖。
- `configRequirements/read` 旧 schema（无 `requirements`）与新 schema（有 `requirements`）均有诊断记录；错误响应不会因缺少 `result` 抛异常。
- 子进程退出码、控制帧不完整/EOF、provider 会话隔离、Native 别名转换、TOML/DPAPI 凭据脱敏。

## 未执行

- 真实 Codex Desktop 重启验收（需要生产环境安装新 Helper 后执行）。
- 通过真实旧 Core 捕获 `configRequirements/read` 原始响应；本地已用旧/新 schema fixture 覆盖协议分支。

## 产物

由 `create-release.ps1` 在隔离目录生成：

- `windows-x64/OpenCodexLauncher.exe`（文件版本 `3.1.3.0`）
- `windows-x64/OpenCodexLauncher.exe.config`
- `windows-x64/SHA256SUMS.txt`
- `OpenCodexLauncher-3.1.3-windows-x64.zip`
- `OpenCodexLauncher-3.1.3-source.zip`

SHA256 以产物目录中的 `SHA256SUMS.txt` 和最终交付说明为准。
