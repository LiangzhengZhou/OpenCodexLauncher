# 3.1.17 恢复原生 Codex 修复报告

日期：2026-11-18
分支：`fix/native-restore-3.1.17`
基线：v3.1.16 `236b28b`
候选版本：`3.1.17.0`

## 现场问题

执行“恢复原生 Codex”后，`~/.codex/config.toml` 为：

```toml
model_provider = 'openai'
model = "gpt-6.1-sol"
openai_base_url = "http://127.0.0.1:10102/v1"
```

OpenCodex 进程和 10102 端口都不存在。

因此：

- Codex 仍把官方请求发往 10102；
- 请求报 `connect 10061`（目标计算机积极拒绝）；
- 官方模型列表/新模型无法通过官方后端刷新。

根因是 `ocx restore` 恢复了包含代理 base URL 的备份，而 Launcher 没有做最终的原生归一化。

## 修复

新增 `ConfigStore.RestoreNativeCodex(path)`：

- 删除根 `openai_base_url`；
- 删除根 `model_catalog_json`；
- 恢复 `model_provider = 'openai'`；
- 如果当前 `model` 是 `launcher-*` / `ccs-*`，删除该行，让 Codex 使用官方默认模型。

`LauncherV2.Ocx()` 在 `restore` / `restore back` 成功后调用该归一化。

## 当前机器已手工修复

已将当前配置中的 `openai_base_url` 删除；现在只有：

```toml
model_provider = 'openai'
model = "gpt-6.1-sol"
```

备份在：

```text
%LOCALAPPDATA%\OpenCodexLauncher\recovery\config-before-native-restore-20261118-111411.toml
```

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 124 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| source/binary release scan | 通过 |

新增测试覆盖：删除 `openai_base_url`、`model_catalog_json`、`launcher-*` 模型并恢复 `openai` provider。
