# OpenCodexLauncher 3.1.0

- Native 直连供应商与现有 OpenCodex 代理共存；统一供应商管理，新增删除供应商。
- 一次启用日常接入，首次完全重开 Desktop 后可使用原快捷方式；不同新会话可并行绑定不同供应商。已有会话不支持原地跨供应商切换。
- Native 请求由真实 Codex app-server 直连，不经过 OpenCodex HTTP relay；DPAPI 凭据通过 helper 提供，不写入 TOML。
- 修复 Desktop 后续 turn 携带模型别名的问题，拒绝未知或跨供应商路由，避免静默错发。
- 关键版本回退新增 3.0.4（3.0 最后公开版），保留 2.6.5。先关闭 Native 日常接入并重开 Desktop，再回退旧版；配置和密钥保留。
- 保持更新 ZIP 布局，可通过软件内置检查更新安装。启用 Native 的用户更新后需点击“启用 / 更新日常 Native 接入”并完全重开 Desktop 一次。

Native providers now coexist with existing proxy providers across separate conversations. Enable everyday activation once and reopen Desktop; use your normal shortcut thereafter. No inference relay, credential plaintext in TOML, or hidden thread identity switching. Confirmed rollback versions: 3.0.4 and 2.6.5. Disable Native activation before rollback. Existing proxy workflows and package layout remain compatible.
