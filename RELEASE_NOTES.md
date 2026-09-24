# OpenCodexLauncher 3.1.2

- Launcher 启动后自动核对已注册 Native Helper 的 EXE 与 .config 内容。GUI 更新后不会再把旧 Helper 当成当前构建。
- 首页及 Native 页面提供确认后更新入口；新 Helper 部署到独立内容寻址目录，不覆盖仍在运行的旧文件。
- 更新仅切换启动注册，保留供应商、DPAPI 密钥、TOML、模型目录和手动 Runtime 配置；复用失败恢复记录和并发锁。
- 完成后请完整退出并重新打开 Codex Desktop。不会强制停止当前会话。若启动来源缓存旧环境，请重开该来源或注销 Windows。
- 3.1.1 的 Runtime 自动恢复和 Native thread/provider 隔离规则保持不变。3.0.4、2.6.5 回退规则不变。

Detect stale Native helpers after GUI updates and offer a confirmed helper-only update. Deployment is content addressed and registration is recoverable. Existing sessions and credential command paths remain valid because older helpers are retained. Fully restart Desktop after updating.

Validation covers isolated Native, UI, regression, rollback, build and package checks. A full restart acceptance test against a real Codex Desktop session has not been performed for this release; existing user sessions were not interrupted.
