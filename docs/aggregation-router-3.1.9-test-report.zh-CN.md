# 3.1.9 聚合路由 502 修复报告

日期：2026-11-10
分支：`fix/aggregation-502-319`
基线：v3.1.8 `e3f8ac2`
候选版本：`3.1.9.0`

## 问题

3.1.8 聚合路由在真实请求中返回：

```text
unexpected status 502 Bad Gateway: Unknown error
url: http://127.0.0.1:10202/v1/responses
```

`10202` 是聚合路由端口，OpenCodex 在 `10101` 正常健康。
502 是路由自身的统一异常处理，不是上游 OpenCodex 的 502。

## 修复

- 上游 `Content-Type` 改用 `HttpListenerResponse.ContentType`。
- 对 `HttpListenerResponse.Headers` 不接受的头逐项忽略，而不是让整个请求失败。
- 异常写入 `%LOCALAPPDATA%\OpenCodexLauncher\logs\aggregation.log`，只记录阶段、异常类型和 HRESULT，不记录 prompt、URL、Key 或响应正文。

## 实测

用 3.1.9 候选程序启动一个本地聚合路由，向运行中的 OpenCodex `10101` 转发一个缺少 input 的官方请求：

```text
STATUS=400
BODY={"error":{"message":"One of \"input\" or ... must be provided."...}}
aggregation.log: no failure
```

说明路由已经把 OpenCodex 的响应原样返回，不再自行变成 502。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 119 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

## 未完成

- 真实 Codex 官方/Packy 首条消息验收；
- Codex 是否向 aggregate provider 附加官方 Authorization；
- 真实 401/权限错误、流式长响应的桌面验收。
