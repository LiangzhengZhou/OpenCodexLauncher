# 3.1.5 本地离线回归报告

日期：2026-10-28
分支：`fix/ui-phase1-315`
基线：v3.1.4 `9530e68`
候选版本：`3.1.5.0`

## 本版修复

- Desktop 诊断不再只查询 `Codex.exe`，同时识别当前 `OpenAI.Codex` MSIX 主进程 `ChatGPT.exe`；Electron `--type=` 子进程不计为 Desktop 主进程。
- 诊断报告新增实际 Desktop 进程名，`desktopHomeEvidence` 按关联状态生成，`transport-get-only` / `desktop-loading-unverified` 从 findings 移入 scope。
- 已启用 Native 且 Helper 仅构建过期时，首页自动完成 Helper 更新；只提示重启 Desktop，不再要求手动点击。
- 去掉 Codex 路由页重复的同步入口和模型页重复的 Desktop 关联入口；Native Prepare 与诊断启动移入高级操作。
- 新增自动 Helper 更新测试和诊断字段测试。

## 执行结果

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 104 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 145 项通过，94 个渲染用例 |
| source/binary release scan | 通过 |

全程未执行 `codex exec`、Desktop 真实消息、PackyAPI 或 OpenAI 推理请求。

## 未执行

- 真实 Codex Desktop 重启验收；
- 真实模型推理。
