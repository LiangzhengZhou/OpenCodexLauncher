# 3.1.14 聚合压缩请求体 502 修复报告

日期：2026-11-11
分支：`fix/aggregation-compressed-3.1.14`
基线：v3.1.13 `3058ab8`
候选版本：`3.1.14.0`

## 现场根因

3.1.13 的聚合日志反复出现：

```text
stage=route type=ArgumentException hresult=0x80070057
site=Deserialize param=input
```

这是 `JavaScriptSerializer.DeserializeObject` 收到非 JSON 输入（很可能是压缩后的请求体）时抛出的异常，不是路由头、catalog 或 Native 配置问题。

## 修复

- `ReadBody` 改为读取原始字节；
- 新增 `DecodeBody`：
  - `gzip` -> `GZipStream`；
  - `deflate` -> `DeflateStream`；
  - 空/identity -> UTF-8；
- 不支持的 encoding 记录 encoding 名称和字节数；
- JSON 解析前完成解压。

## 实测

3.1.14 候选路由接收 gzip 压缩的官方 Responses JSON：

```text
STATUS=400
BODY={"error":{"message":"One of \"input\" ... must be provided."...}}
```

返回的是 OpenCodex 的校验错误，而不是路由 502；`aggregation.log` 无新增记录。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 122 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

## 未完成

- 真实 Codex Desktop 首条消息验收；
- 如果真实请求使用 br/zstd，仍需根据日志中的 encoding 名称补支持。
