# 3.1.13 聚合 Catalog 根引用修复报告

日期：2026-11-11
分支：`fix/aggregation-catalog-ref-3113`
基线：v3.1.12 `3b387c4`
候选版本：`3.1.13.0`

## 现场根因

当前 `~/.codex/config.toml` 根配置缺少：

```toml
model_catalog_json = ...
```

但 `opencodex-catalog.json` 仍然存在。

`AggregationMode.Repair` 和 `CheckCatalog` 都调用 `CatalogPath(config)`，该函数在找不到 `model_catalog_json` 时返回 `null`，随后直接 `return`，不抛出、不记录，也不补回根引用。

因此：

- aggregation routes 存在；
- Native provider 存在；
- router 正常；
- watcher 没有异常日志；
- 但 Repair 永远无法写 catalog，PA alias 永远不恢复。

## 修复

- 新增 `ResolveCatalog(PathSet)`：
  - 优先使用根 `model_catalog_json`；
  - 回退到 `paths.Catalog`；
  - 再回退到 Codex 配置旁的 `opencodex-catalog.json`。
- `ApplyConfig` 在根配置缺少 `model_catalog_json` 时自动写回。
- Repair/CheckCatalog 使用解析后的 catalog，不再静默丢失。

## 现场验证

用 3.1.13 候选程序对当前生产配置执行 Repair：

```text
model_catalog_json = "C:\\Users\\admin\\.codex\\opencodex-catalog.json"
```

catalog 随后包含：

```text
launcher-agg-ede6...
launcher-agg-17bf...
```

官方模型保持不变。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 122 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

新增测试删除根 `model_catalog_json` 后调用 Repair，确认根引用和 PA alias 都恢复。

## 未完成

- 真实 Codex Desktop 重启后模型选择器验收；
- 官方/Packy 首条消息和流式响应验收。
