# 3.1.0 Native Provider

OpenCodexLauncher 3.1.0 支持不同新会话分别使用 Native 和代理供应商。
不覆盖已有安装。请将整个 ZIP 解压到独立、短路径，例如 D:\OCL31。
EXE 与 .config 文件应保留在一起。日常接入启用时会将桥接程序复制到 Launcher 数据目录的 native-host 中；之后不依赖解压目录，不要删除该固定目录。

## 启用步骤

1. 双击 OpenCodexLauncher.exe。首次使用先完成原有配置目标关联及模型目录同步。
2. 打开“供应商”，在现有供应商列表中选中 Packy，勾选“Native 直连”。地址、已保存密钥和模型选择会复用，不需要在另一页重复填写。其他供应商不勾选，继续走原来的代理。
3. 点击“保存供应商”。新供应商也统一在此页新增，填写供应商提供的 HTTPS Responses 基础地址和自己的 API Key。
4. 在同一个供应商页获取模型，到“模型管理”选择并保存。获取模型只调用模型列表接口，不发推理。
5. 打开“Native 直连”，点击“启用 / 更新日常 Native 接入”。这会准备配置、目录和固定位置的桥接程序，并注册当前用户的启动环境；不再每次选 EXE。凭据保存在 DPAPI 中，TOML 仅引用固定位置的 credential helper。
6. 完成正在执行的任务后，自行完全退出 Desktop（包括后台进程），再从原来的快捷方式或开始菜单打开。无需从 Launcher 启动，也无需保持 Launcher 窗口开启。首次启用和更新桥接后各需重开一次；日常不同新会话选择不同供应商不需重开。
7. 在 Desktop 的新会话模型列表选择 PA 模型。旧代理会话保持原样。不要在旧会话里切到另一供应商。
8. 用供应商后台验证自己的测试请求；不需要消耗官方 OpenAI 额度来验收 Native。

Launcher 只传递 app-server JSON-RPC，不转发 Native HTTP/SSE。原 Desktop 安装目录和 codex.exe 不会被替换。
日常接入设置当前用户的 CODEX_CLI_PATH 和 OPENCODEX_LAUNCHER_BRIDGE，并向 Windows 广播环境变更。不设置全局 CODEX_HOME，不修改系统级环境，也不需要管理员权限。如果启动来源仍缓存旧环境，请先重开启动来源；必要时注销 Windows 后登录一次。不同 Windows 启动来源的实际继承行为仍需用户验收；不能仅凭“已启用”状态断言 Desktop 已接管。

关闭时点击“关闭日常接入并恢复启动环境”，然后完全退出并重开 Desktop。关闭保留供应商配置和固定 helper，以便已有进程与凭据命令继续使用。遇到其他工具已经设置或后来修改的启动环境，Launcher 会拒绝覆盖。旧的“选择并启动 Desktop 自测”仅作为诊断备用。

### 日常启动验收

启用后关闭 Launcher 和 Desktop，再用原快捷方式打开 Desktop。新建 PA 会话验证 Native，再新建原代理供应商会话验证旧路径。第二次完全退出并照常打开，重复验证；不需要每次点击启用。不要在原会话里跨供应商切换。若失败，保留错误信息，不反复更换 API 地址。

## 删除供应商

在“供应商”页选中目标后点击“删除供应商”，确认后移除其配置、模型选择和有效凭据。其他供应商保留。若它是当前代理默认供应商，需先调整默认路由；Reserve Force 相关限制也会阻止不安全操作。不会自动结束已有会话或重启代理，已有运行时可能仍缓存旧配置。恢复快照仅存本机，包含敏感配置，不应上传或分享。

## 当前证据与限制

- 新 GUI 可以独立打开、退出；有 Native 管理页面及真实 credential/helper 入口。
- 隔离测试已验证 DPAPI helper stdout 仅输出 token，TOML 无明文密钥。
- 真实 Codex app-server + 本地模拟 HTTP 端点验证：运行中新增 provider、并行不同 provider 会话、别名转换、默认端点零调用。无官方登录、无付费推理。
- 现有会话跨 provider 被拒绝，不伪装 thread ID。已绑定 Native 会话允许已配置的同 provider 模型别名。
- 模型能力元数据暂沿用现有官方 catalog 模板，需进一步验证供应商能力差异。
- 用户已确认真实 Desktop 的 Packy 请求通过；不将该反馈扩大为 usage/cache、全部恢复会话及所有 Windows 启动方式均通过。日常快捷方式接入已通过用户验收。
- 当前准备步骤需要已有 model_catalog_json。复杂多行 TOML 或被外部修改的 Launcher-owned 区域会拒绝覆盖。
- Reserve Force 启用时拒绝 Native 准备。关闭日常接入只恢复启动环境，不自动删除 TOML、模型或密钥。修改前快照在本机 Launcher recovery 目录。不要用旧快照覆盖后续手工修改。

设置中的版本管理提供 3.0.4 和 2.6.5 回退。旧版不支持 Native 管理；先关闭日常 Native 接入并完全重开 Desktop，再回退。回退保留配置和密钥。
