# OpenCodex Launcher 2.6.5

## 中文

本版增加消息连接诊断，不宣称已修复所有 stream disconnected 问题。

通过启动器内的一键更新升级后，在报错时点击 **一键诊断 Desktop → 复制报告**。新报告包含目标端口 TCP 连接、健康接口和消息地址的 GET 状态，以及启动器代理设置的有无，帮助区分连接不可达和 HTTP 已返回等情况。无需手动运行命令或上传配置。

检查不发送推理、不读凭据、不改配置、不重启服务。GET 404/405 不代表 POST 失败；本地鉴权、Desktop 进程连接及供应商推理仍需进一步证据。空请求日志也不能排除记录前的拒绝。报告只导出固定状态与有无标志，不导出代理值、响应体或原始日志。

启动 OpenCodex 的健康检查现在直连本机并拒绝重定向，与诊断保持一致。模型关联与供应商配置保留。

## English

This release adds message transport diagnostics; it does not claim to fix every stream-disconnected failure.

Use the built-in launcher updater, then **Diagnose Desktop → Copy report** while the fault is present. Report format 2 adds loopback TCP, health GET and responses-address GET results plus launcher proxy-presence flags. No manual commands or configuration uploads are needed.

Checks send no inference, read no credentials, edit no configuration and restart no services. GET 404/405 does not establish POST failure. POST authentication, Desktop-process connectivity and upstream inference remain untested. Empty request logs do not rule out rejection before logging. Reports contain only allowlisted statuses/flags; no proxy values, response bodies or raw logs.

Runtime health now bypasses system proxies and refuses redirects, matching diagnostic behavior. Existing associations and provider settings are retained.

Windows x64 · .NET Framework 4.8 · MIT