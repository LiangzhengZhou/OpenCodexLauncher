# 3.1.15 请求体诊断与 Magic 解压报告

日期：2026-11-11
分支：`fix/aggregation-body-3115`
基线：v3.1.14 `d97533b`
候选版本：`3.1.15.0`

## 现场状态

3.1.14 仍然记录：

```text
stage=route type=ArgumentException site=Deserialize param=input
```

说明请求体在 JSON 解析前仍不是 JSON 文本。3.1.14 只在 `Content-Encoding` 为 gzip/deflate 时解压；如果 Codex 压缩了 body 但省略该头，仍会失败。

## 修复

- `DecodeBody` 检查 magic bytes：
  - gzip `1F 8B`
  - deflate/zlib `78 ...`
- 即使没有 Content-Encoding 也自动解压；
- JSON 解析失败时记录：
  - Content-Encoding
  - raw 字节数
  - decoded 字符数
  - 首 4 字节
- 不记录 prompt 内容。

## 实测

3.1.15 候选路由接收一个 **没有 Content-Encoding 头** 的 gzip body：

```text
STATUS=400
BODY={"error":{"message":"One of \"input\" ... must be provided."...}}
```

说明 magic-byte 解压生效，返回 OpenCodex 的校验错误而不是 502；`aggregation.log` 无新增记录。

## 测试

| 套件 | 结果 |
|---|---|
| `build-v2.ps1` | 通过 |
| `tests/run-native-tests.ps1` | 122 项通过 |
| `tests/run-tests.ps1` | 280 项通过 |
| `tests/run-ui-tests.ps1` | 147 项通过，100 个渲染用例 |
| source/binary release scan | 通过 |

## 下一步

安装 3.1.15 后再次发送消息；如果仍 502，`aggregation.log` 会记录类似：

```text
stage=body-json ... encoding=... raw=... decoded=... head=...
```

`head=28B52FFD` 表示 zstd；`head=...` 其他值可继续定位。
