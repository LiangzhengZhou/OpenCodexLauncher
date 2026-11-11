# 3.1.12 聚合 Catalog Watcher 报告

日期：2026-11-11
分支：`fix/aggregation-watch-3112`
基线：v3.1.11 `bd6ac6c`
候选版本：`3.1.12.0`

## 现场状态

- Launcher 版本：3.1.11.0
- `aggregation-state.json`：Enabled=true, Port=10201
- `aggregation-routes.json`：2 个 Packy alias
- `native-providers.json`：PackyAPI，RouteMode=NativeCodex
- `opencodex-catalog.json`：只有官方模型
- `aggregation.log`：空

说明 Native 直连配置本身存在；问题是 OpenCodex/Codex 在 Launcher 启动后重写了 catalog，而 3.1.11 只会在 Resume/Sync 时修复，没有持续检查。

## 修复

- 聚合模式启用时启动轻量 2 秒 Timer；
- 检查 catalog 是否包含全部 `launcher-agg-*` alias；
- 缺失时调用 Repair 重新应用 aggregate provider 和 catalog；
- 已启用状态下再次点击“启用聚合模式”会执行 Repair；
- Disable 时停止 Timer。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 121 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

新增测试模拟外部删除全部 `launcher-agg-*` alias，然后在已启用状态下调用 Enable，确认 alias 被修复。

## 未完成

- 真实 Codex Desktop 重启后 PA 模型显示验收；
- 官方/Packy 首条消息和流式响应验收；
- 真实 OpenCodex 启动时的 catalog 重写时序仍需观察。
