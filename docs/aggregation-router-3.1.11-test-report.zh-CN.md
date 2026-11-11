# 3.1.11 聚合路由 502 修复（请求头）

日期：2026-11-11
分支：`fix/aggregation-headers-3111`
基线：v3.1.10 `8737bee`
候选版本：`3.1.11.0`

## 现场证据

3.1.9 的聚合路由日志：

```text
stage=route type=ArgumentException hresult=0x80070057
```

说明 502 不是 OpenCodex 上游返回，而是路由在处理请求头时抛出了 `ArgumentException`。

## 修复

- 请求头改为允许列表：
  - `Authorization`
  - `Accept`
  - `Accept-Language`
  - `OpenAI-*`
  - `X-OpenAI-*`
- Packy 请求不转发官方 Authorization，再替换为 Packy DPAPI Key；
- 响应 Content-Type 使用 `HttpListenerResponse.ContentType`，并对受限响应头逐项忽略；
- 失败日志增加目标方法与参数名，仍不记录 prompt、payload、Key 或完整 URL。

## 实测

3.1.11 候选路由向当前 OpenCodex 转发一个带 Authorization/OpenAI-Beta 的官方请求：

```text
STATUS=429
BODY={"error":{"type":"upstream_error","code":"upstream_reset_replay_refused"...}}
```

返回的是 OpenCodex 上游错误，路由自身不再 502；`aggregation.log` 无新增记录。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 120 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

## 未完成

- 真实 Codex 官方/Packy 首条消息；
- 上游 429/401 的业务重试验证；
- 长流式响应桌面验收。
