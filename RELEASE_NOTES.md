# OpenCodexLauncher 3.1.1

- 修复 Desktop 更新后 Native helper 仍启动残缺旧 runtime 的问题：旧目录即使保留 codex.exe，缺少 codex-code-mode-host.exe 时也不再使用。
- 自动模式下 helper 自行查找完整 runtime，无需 Launcher 窗口运行；手动固定路径保持优先，残缺时明确失败。
- 新增独立 runtime 健康状态，保持 Native 直连、供应商隔离、DPAPI 凭据及更新 ZIP 布局；保留 3.0.4、2.6.5 关键版本回退。

**升级提示：** 已启用 Native 的用户升级后，请点击一次“启用 / 更新日常 Native 接入”并完全重开 Desktop，以部署新版固定 helper。此后自动发现模式可在 Desktop runtime 更新后自行恢复，无需再次打开 Launcher。手动固定路径仍需用户维护。

Fix Native activation after Desktop runtime rollover. The standalone helper rejects incomplete bundles and discovers a complete runtime without the Launcher GUI. Explicit paths fail closed rather than silently switching. Credentials, provider isolation and inference transport remain unchanged. Refresh Native activation and fully reopen Desktop once after upgrading to install the updated helper.