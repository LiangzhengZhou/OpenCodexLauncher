# 3.1.8 设计方案：Codex 聚合路由

## 1. 目标

同时满足：

- 官方模型可用；
- Packy 仍走 Native 直连，不经过 OpenCodex 代理；
- Codex 模型列表同时显示官方和 Packy 模型；
- 在模型列表里切换模型，不需要切换 Codex 的全局 provider；
- 保留官方登录、官方额度、Packy API Key 和现有用户配置。

## 2. 为什么不能继续改 model_provider

3.1.7 把根 provider 设为 `launcher_native_PackyAPI` 后：

```text
官方模型被选中
当前 provider = Native
=> Codex 拒绝
```

反过来把 provider 设回 `openai`：

```text
Packy Native 线程
当前 provider = openai
=> Codex 拒绝
```

Codex 当前配置只有一个全局 `model_provider`，Native 的 thread-level provider 不能稳定承载官方和第三方共存。继续在 provider 字段上打补丁，只会在两个错误之间来回切换。

## 3. 核心架构

采用 CC Switch Aggregation 的思路：

```text
Codex
  -> 固定本地 provider: launcher_aggregate
  -> http://127.0.0.1:<aggregate-port>/v1/responses

Launcher 本地聚合路由
  ├─ 官方模型
  │    -> 现有 OpenCodex 官方代理
  │       http://127.0.0.1:10100/v1/responses
  └─ Packy 模型
       -> Packy Responses 直连
          https://www.packyapi.ai/v1
```

Codex 始终只看到一个 provider：

```toml
model_provider = "launcher_aggregate"
model = "gpt-5.6-luna"
```

模型选择由 catalog 中的模型 ID 决定，不再修改根 provider。

## 4. Codex 配置

```toml
model_provider = "launcher_aggregate"
model = "gpt-5.6-luna"

[model_providers.launcher_aggregate]
name = 'OpenCodex Aggregate'
base_url = 'http://127.0.0.1:10201/v1'
wire_api = 'responses'
requires_openai_auth = true
supports_websockets = false
```

要求：

- 不再保留 `launcher_native_*` provider 表；
- 不再在模型切换时改 `model_provider`；
- 官方登录继续保留；
- 聚合路由停止或启动失败时，Codex 必须显示明确错误，而不是回退到 Native/代理混写。

## 5. 模型目录

`opencodex-catalog.json` 同时包含：

```text
官方模型
  gpt-5.6-luna
  gpt-5.6-sol
  ...

Packy 聚合模型
  launcher-agg-packy-<hash-1>  ->  gpt-6.1-sol
  launcher-agg-packy-<hash-2>  ->  gpt-6-astra
  ...
```

规则：

- 官方模型保持原 ID；
- Packy 模型使用 `launcher-agg-` 前缀，避免与官方和 Native alias 冲突；
- 每个 alias 在 `aggregation-routes.json` 中有唯一映射；
- 模型展示名带 `PA` 前缀；
- 模型能力字段沿用官方 catalog 模板，不伪造能力；
- 聚合列表变化后要求彻底重启 Codex。

## 6. 请求路由

### 官方模型

```text
Codex -> 聚合路由 -> OpenCodex 官方代理
```

- 保留 Codex 发来的官方认证；
- 转发 `/v1/responses` 请求；
- 透传 SSE、工具调用、错误和 usage；
- 不记录 prompt、响应正文和 Authorization。

### Packy 模型

```text
Codex -> 聚合路由 -> https://www.packyapi.ai/v1/responses
```

- 根据 alias 找到 Packy 原始模型 ID；
- 使用 DPAPI 中的 Packy Key 设置 Bearer；
- 移除 Codex 发来的官方 Authorization；
- 透传 Responses 请求和 SSE；
- 不使用 OpenCodex 代理，满足 Packy 的限制。

### 通用规则

- 只监听 `127.0.0.1`；
- 只允许 `POST /v1/responses` 和健康检查；
- 不提供任意 URL 转发，防止 SSRF；
- 请求体大小、上游超时、取消、并发有限制；
- 流式响应边读边写，不缓存完整正文；
- 错误返回给 Codex，但日志只记录状态码、路由类别和错误分类。

## 7. 聚合路由进程

选择 C# 内嵌 `HttpListener`，由 Launcher 进程托管：

- 启动 Launcher 后监听随机空闲端口；
- 端口写入 `model_providers.launcher_aggregate.base_url`；
- Launcher 最小化到托盘时继续运行；
- 退出 Launcher 前恢复 Codex 的直连/官方配置，并提示用户 Codex 需要重启；
- 如果 Launcher 崩溃，Codex 请求失败，不静默回退到 Native。

可选替代：独立后台 helper，由 Launcher 拉起并在退出时关闭；3.1.8 先使用进程内路由，减少部署件。

## 8. 官方认证的边界

这是本设计最大的技术风险。

优先方案：

```text
本地 aggregate provider requires_openai_auth = true
Codex 把官方 Authorization 发给本地路由
路由转发给 OpenCodex 官方代理
```

必须验证：

- Codex Desktop 对自定义 provider 是否仍附加官方 Authorization；
- OpenCodex 官方代理是否接受该请求；
- 官方额度用尽时 Codex 的 Luna Reserve 行为是否仍然生效。

如果 Codex 不向自定义 provider 附加认证，备选：

1. 由聚合路由读取官方登录状态并使用 OpenCodex 的官方账号池；
2. 官方模型仍通过 Codex 原生 provider，Packy 通过另一个 provider，但这就回到 provider 切换问题，不可接受；
3. 需要官方提供受支持的 per-thread provider 机制。

在没有验证第 1 项前，不应承诺 3.1.8 已完成官方共存。

## 9. UI 和交互

新增一个明确的模式：

```text
[聚合模式]  [Native 直连]  [官方直连]
```

聚合模式页面：

```text
默认 provider：OpenAI Official
已加入：Packy Native（5 个模型）

模型列表：
  GPT-5.6 Luna        Official
  GPT-5.6 Sol         Official
  PA GPT-6.1 Sol      Packy
  PA GPT-6 Astra      Packy

[应用聚合配置]  [重启 Codex]
```

要求：

- 只有一个主操作；
- 添加/移除 provider 后显示“需要彻底重启 Codex”；
- 聚合路由进程状态可见；
- Packy Key 缺失时明确提示；
- 官方登录缺失时明确提示；
- 不把 Native probe 或 provider 切换按钮混入主流程。

## 10. 配置切换和迁移

从 3.1.7 升级：

1. 备份 `config.toml`、catalog、native 配置；
2. 检测 `model_provider = launcher_native_*` 或 Native provider 表；
3. 询问是否迁移到聚合模式；
4. 写入单一 aggregate provider；
5. 移除 Native provider 表和 `launcher-native-*` catalog alias；
6. 生成官方 + Packy 聚合 catalog；
7. 提示完全重启 Codex。

保留 Native 直连模式供纯 Packy 用户使用，但：

- 不与聚合模式同时写同一份 `config.toml`；
- 切换模式需要明确操作并重启 Codex；
- 不自动在两种模式之间猜。

## 11. 测试

离线测试不调用真实模型：

- 官方 alias -> mock 官方上游；
- Packy alias -> mock Packy 上游；
- 确认 Packy 请求带 Packy Key、不带官方 Authorization；
- 确认官方请求保留官方认证；
- SSE 流式透传和取消；
- 上游 4xx/5xx 原样分类；
- alias 不存在时失败关闭；
- catalog/config 写入和备份；
- 模式切换不残留 Native provider；
- Launcher 退出后 Codex 得到明确错误；
- 日志不含 prompt、响应正文、Key、完整 URL 查询参数。

真实验收：

- 官方模型一条消息；
- Packy 模型一条消息；
- 同一会话切换官方/Packy 模型（验证是否允许）；
- 重启 Codex 后模型列表仍同时存在；
- 官方额度用尽场景另行评估。

## 12. 分期

### 3.1.8a：路由 PoC

- 本地 Responses 转发；
- 官方认证方式验证；
- Packy 直连转发；
- 模型 alias；
- 离线 mock 测试。

### 3.1.8b：Launcher 集成

- 聚合模式 UI；
- config/catalog 生成与迁移；
- 进程生命周期、端口、托盘；
- 原生 Desktop 验收。

### 3.1.8c：稳定化

- 错误分类、重启提示；
- 删除 Native 混写路径；
- 更新文档和回滚方案。

## 13. 明确不做

- 不把 Packy 强行塞进 OpenCodex 代理；
- 不在同一个会话里切换全局 provider；
- 不把 `model_provider` 当模型路由开关；
- 不在没有官方认证验证的情况下发布“官方+Packy 共存”；
- 不把聚合路由日志变成 prompt/密钥记录器。
