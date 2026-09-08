# 2.6.4 release notes / 发布说明

2026-09-08. Windows 10/11 x64, .NET Framework 4.8. OpenCodexLauncher.exe version 2.6.4.0; keep its .config file beside it. MIT license.

## Desktop association and verified sync / Desktop 关联与同步校验

Use **Associate and sync Desktop**, review the detected configuration candidates and confirm the target used by Desktop. Existing provider settings and selected models stay in place. Confirmation saves the association, keeps a local recovery snapshot and runs upstream sync once. No automatic Desktop restart or inference request; model metadata services may be contacted.

点击 **关联并同步 Desktop**，核对候选并确认 Desktop 使用的配置。保留供应商和已选模型，保存关联、创建本机快照后执行一次同步。不自动重启 Desktop，不发送推理请求；可能访问模型元数据服务。

Sync reads back the root catalog reference, actual catalog, selected full provider/model routes and proxy port. A zero CLI exit with skipped/missing catalog generation is now incomplete. Failure automatically opens a copyable diagnostic report; the association workflow opens one after success too. Reports contain fixed result/skip codes and counts, never raw output, keys, provider/model names or paths. No reports are uploaded automatically.

同步会回读根级引用、真实目录文件、所选完整模型和代理端口。即使 CLI 返回 0，目录缺失或跳过也不会显示成功。未完成时自动弹出可复制报告；关联同步成功后也展示报告。报告仅含固定结果/跳过类别、数量等，不含原始输出、密钥、供应商/模型名称或路径，不自动上传。

Candidate homes are not proven Desktop homes. Process-detection warnings are not sync blockers. Missing upstream catalog sources may still require an OpenCodex update or investigation. Disk verification does not prove Desktop loaded the catalog or that inference works. Snapshots do not provide atomic rollback of upstream writes; retain local recovery data and avoid overwriting subsequent edits. Read-only diagnostics remain available separately.

候选目录不能证明 Desktop 正在使用它；进程检测警告不阻止同步。上游缺少基础目录来源仍可能需要更新 OpenCodex 或继续排查。文件验证不能证明 Desktop 已加载或推理可用。快照不代表上游多文件原子回滚，请保留本机恢复数据并避免覆盖后续修改。只读诊断仍独立可用。

## Upgrade / 升级

From 2.6.0+, use Settings → Check launcher updates → Update and restart. Earlier versions need the complete Windows ZIP once. Settings, credentials and OpenCodex runtime selection are preserved. OpenCodex runtime updates remain separate.

2.6.0 及以后在“设置 → 检查启动器更新 → 一键更新并重启”升级；更早版本完整解压一次 Windows ZIP。保留设置、凭据与运行时选择。OpenCodex 运行时更新仍是独立功能。