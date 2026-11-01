# 3.1.6 代理端口冲突修复报告

日期：2026-11-02
分支：`fix/proxy-port-316`
基线：v3.1.5 `78f98f7`
候选版本：`3.1.6.0`

## 问题

Launcher 默认让 OpenCodex 使用端口 `10100`。当 Clash Verge 的 `verge-mihomo.exe` 已经绑定该端口时，`ocx start` 退出码为 1，而 Launcher 只显示“代理启动失败，退出代码 1”。

## 修复

- 启动代理前用本地短期监听测试当前配置端口是否可用。
- 端口被占用时选择后续 100 个端口中的第一个空闲端口。
- 用户确认后：
  - 写入 `.opencodex/config.json` 的 `port`；
  - 更新 `~/.codex/config.toml` 的 `openai_base_url`；
  - 如果存在 `opencodex.config.toml`，同步更新；
  - 记录端口变更，然后启动代理。
- 取消时不会修改配置。

## 执行结果

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 109 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 145 项通过，94 个渲染用例 |
| source/binary release scan | 通过 |

全程未执行真实模型请求。

## 未执行

- 当前机器的 Clash/verge-mihomo 端口未改动；
- 未对生产 OpenCodex 配置执行端口变更；
- 真实 Desktop 重启验收仍留待安装后执行。
