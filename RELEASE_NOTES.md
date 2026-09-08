# 2.6.3 release notes / 发布说明

2026-09-08. Windows 10/11 x64, .NET Framework 4.8. OpenCodexLauncher.exe version 2.6.3.0; keep its .config file beside it. MIT license.

## One-click Desktop diagnostics / 一键诊断 Desktop

Click **Diagnose Desktop** on Overview, Models or Codex routing, then **Copy report** or **Save diagnostic log…**. Send that text to the maintainer instead of searching for and sharing configuration files. Checks compare configuration-home candidates, root catalog and proxy references, selected full routes, process candidates and loopback health responses.

在“概览”“模型管理”或“Codex 路由”点击 **一键诊断 Desktop**，再点击 **复制报告** 或 **保存诊断日志…**，把文本发回即可收集排查证据。自动比较配置目录候选、根级目录/代理引用、已选完整模型路由、进程候选和本机健康状态，无需手动查找和发送配置文件。

Diagnostics preserve configuration and running services, send no inference requests, and never upload results automatically. Reports exclude account data, keys, provider/model names, paths, URLs, raw configuration, request logs and process command lines. Only completed setup plus an explicit click enables collection. Double-clicks do not overlap; closing cancels collection; the wait is bounded to 12 seconds. Network checks refuse redirects and do not use system proxies or credentials.

诊断不改配置、不启停服务、不发送推理请求、不自动上传。报告不含账号数据、密钥、供应商/模型名称、路径、URL、原始配置、请求日志或进程命令行。只有完成首次配置并主动点击才采集；重复点击不会并发执行，关闭窗口会取消，等待上限 12 秒。健康检查不跟随重定向、不使用系统代理或凭据。

Possible home mismatches and active app-servers are evidence for investigation, not proof that Desktop has read a particular home or loaded the model catalog. Unsupported syntax, inaccessible/oversized files and process queries remain unknown. This release adds evidence collection; it does not silently associate a Desktop home or claim to fix every missing-model cause.

目录可能不一致和 app-server 运行仅用于辅助判断，不能证明 Desktop 读取了某个目录或加载了模型。无法识别的语法、不可访问/过大的文件和进程查询保留为未知。本版提供证据采集，不会自动关联目录，也不宣称修复所有模型缺失原因。

## Upgrade / 升级

From 2.6.0+, use Settings → Check launcher updates → Update and restart. Earlier versions need the complete Windows ZIP once. Local settings, credentials and OpenCodex runtime selection are preserved. This changes the launcher; OpenCodex runtime updates remain separate.

2.6.0 及以后可在“设置 → 检查启动器更新 → 一键更新并重启”升级；更早版本需完整解压一次 Windows ZIP。保留设置、凭据和 OpenCodex 运行时选择。此处更新启动器，OpenCodex 运行时更新仍为独立功能。
