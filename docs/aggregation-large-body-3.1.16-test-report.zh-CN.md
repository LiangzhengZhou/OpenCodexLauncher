# 3.1.16 聚合大请求体 502 修复报告

日期：2026-11-11
分支：`fix/aggregation-large-body-3.1.16`
基线：v3.1.15 `d23815d`
候选版本：`3.1.16.0`

## 现场日志

3.1.15 的 `aggregation.log`：

```text
stage=body-json type=ArgumentException hresult=0x80070057
site=Deserialize param=input
encoding= raw=10527235 decoded=10457530 head=7B226D6F
```

解读：

- `encoding=` 为空，不是压缩请求；
- `head=7B226D6F` = `{"mo`，是合法 JSON；
- `raw=10,527,235` 字节，约 10 MB；
- `decoded=10,457,530` 字符；
- 抛出 `ArgumentException param=input`。

根因是 `JavaScriptSerializer` 的 `MaxJsonLength` 默认被设为 8 MB，10.5 MB 请求体超过限制。

## 修复

- `JsonData.Serializer(int maxJsonLength)` 增加上限参数；
- `JsonData.Parse(text, maxJsonLength)` 增加上限参数；
- 聚合路由用 64 MB 上限解析请求体；
- Packy 模型重写时用 64 MB 上限序列化；
- 增加 >8 MB JSON body 回归检查。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 123 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

## 未完成

- 真实 Codex Desktop 首条消息验收；
- 超过 64 MB 的请求仍会被 413 拒绝。
