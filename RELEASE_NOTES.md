# OpenCodex Launcher 3.0.1

## 中文

修正 3.0.0 发布后仍显示“3.0 预览版”的遗漏。窗口标题、版本管理和中英文副标题统一读取程序集版本；本版显示 3.0.1 正式版。打包脚本直接生成正式 ZIP 文件名，保持旧版内置更新器兼容。

在设置中检查启动器更新即可从 2.6.5 或 3.0.0 升级。本次不变更供应商、密钥、模型选择、Desktop 关联或运行时路径的配置格式与业务逻辑。沿用 3.0 UI、粉色云朵图标和渐变配色。

2.6.5 保留为确认的关键回退目标，但历史版本选择与实际双向回退尚未完成验证，当前不宣称提供已验证的指定版本回退。诊断不发送推理，健康检查不等于模型推理成功。

## English

Fix the preview labels accidentally retained in the published 3.0.0 executable. The title, bilingual subtitle and version panel now use the assembly version. Packaging creates the exact stable asset filenames required by the existing updater.

Use Check launcher updates in Settings to upgrade from 2.6.5 or 3.0.0. Provider settings, credentials, model selections, Desktop association and runtime paths retain their existing format and behavior. The 3.0 UI, cloud icon and gradients are retained.

2.6.5 remains the confirmed critical rollback target. Historical version selection and real round-trip rollback remain unverified; this release does not claim that workflow is complete. Diagnostics do not send inference requests.

Windows x64 · .NET Framework 4.8 · MIT
