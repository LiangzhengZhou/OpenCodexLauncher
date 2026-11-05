# 3.1.8 聚合模式离线报告

日期：2026-11-05
分支：`fix/aggregation-318`
基线：v3.1.7 `0895f00`
候选版本：`3.1.8.0`

## 实现

- Codex 使用单一 `launcher_aggregate` provider；
- 本地 `HttpListener` 路由 `/v1/responses`；
- 官方模型转发到现有 OpenCodex 路由；
- Packy alias 直连 Packy Responses，并替换为 Packy DPAPI key；
- 聚合 catalog 保留官方模型并添加 `launcher-agg-*` Packy 模型；
- 启用前备份直连配置，关闭时恢复；
- 启动时恢复聚合路由。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 119 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

新增测试覆盖：

- Packy alias 改写为原始模型；
- Packy 请求只携带 Packy Bearer；
- 官方请求保留官方 Authorization；
- SSE 响应转发；
- 聚合 provider/catalog 生成；
- Native provider 区块移除；
- 关闭聚合后恢复直连配置。

## 尚未完成

- 真实 Codex Desktop 使用官方登录经过聚合路由的验收；
- 真实 Packy 首条消息和同会话官方/Packy 切换验收；
- 官方额度用尽与 Luna Reserve 行为；
- 生产配置未修改。
