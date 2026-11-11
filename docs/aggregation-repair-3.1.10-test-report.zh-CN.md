# 3.1.10 聚合 Catalog 修复报告

日期：2026-11-11
分支：`fix/aggregation-catalog-3110`
基线：v3.1.9 `d70363d`
候选版本：`3.1.10.0`

## 问题

聚合模式开启后，Codex 模型列表只剩官方模型，没有 `PA gpt-6.1-sol` 和 `PA gpt-6-astra`。

现场状态：

```text
aggregation-state.json: Enabled=true, Port=10201
aggregation-routes.json: 2 个 Packy alias
config.toml: model_provider = 'launcher_aggregate'
opencodex-catalog.json: 只有官方模型，没有 launcher-agg-*
```

这是外部 Codex/OpenCodex 同步重写了 catalog，而 Launcher 只在首次启用聚合时写入 alias；后续恢复聚合路由时没有重新应用 catalog。

## 修复

- `AggregationMode.Resume` 在启动路由前调用 `Repair(paths)`；
- `AggregationMode.Repair` 重新应用 aggregate provider 和 catalog aliases；
- 修复时保留当前选中的官方模型，不重置为固定默认值；
- `Sync()` 成功后在聚合模式启用时再次 Repair；
- `PrepareProxyRouteAsync` 同时保留 `launcher_aggregate`，避免同步删除聚合 provider。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 120 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

新增测试模拟外部 catalog 同步删除 `launcher-agg-*`，调用 Repair 后确认 alias 恢复。

## 未完成

- 真实 Codex Desktop 重启后模型列表验收；
- 官方 Authorization 与真实 Packy 首条消息验收。
