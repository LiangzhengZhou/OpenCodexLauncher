# 3.1.7 Native Provider 绑定修复报告

日期：2026-11-05
分支：`fix/native-provider-binding-317`
基线：v3.1.6 `3e63dae`
候选版本：`3.1.7.0`

## 问题

Codex Desktop 创建了一个绑定 `launcher_native_PackyAPI` 的聊天，但配置中的当前 provider 仍是 `openai`，首条消息被拒绝：

```text
This chat uses launcher_native_PackyAPI,
but your current provider is openai.
```

根因是 Native alias 已写入 `model`，但根配置缺少对应的 `model_provider`；同时代理路由准备会删除根 `model_provider`，导致 Native 和代理状态混写。

## 修复

- Native Prepare 根据当前 `launcher-native-*` alias 写入根 `model_provider`。
- 代理路由准备保留 `launcher_native_*` 的根 provider，不再删除。
- Launcher 启动时检测 alias 和 provider 不一致，并自动执行一次 Native Prepare 修复。
- 保留已有 external-edit、Reserve Force、catalog 和 credential 安全检查。

## 执行结果

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 111 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 145 项通过，94 个渲染用例 |
| source/binary release scan | 通过 |

未执行真实模型请求。

## 未执行

- 未修改当前生产 `~/.codex/config.toml` 或 Packy 会话；
- 真实 Desktop 首条消息测试需在安装 3.1.7 后执行；
- 官方与 Native 的频繁切换仍需按明确模式操作。
