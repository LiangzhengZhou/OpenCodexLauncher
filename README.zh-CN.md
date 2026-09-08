# OpenCodex Launcher 2.5.1

[English](README.md)

用于管理本机 OpenCodex 供应商、模型选择及 Codex 路由的 Windows 桌面启动器。中文 / EN 可立即切换，并保留表单草稿和模型勾选。本项目为独立社区项目。

## 环境与首次使用

需要 Windows 10/11 x64 和 .NET Framework 4.8。首次引导提供“一键安装并开始配置”，自动下载 OpenCodex 和独立的 Node.js/Bun 环境，无需预装 Node/npm 或管理员权限。Codex 和可选的 Claude Code 仍需另行安装；启动器不提供 API 订阅。

将 Windows ZIP 解压至可写的软件目录，运行 OpenCodexLauncher.exe。下载包不含供应商、模型、API 密钥或自定义路径配置。

首次打开时，可选择“检测并导入本机配置”：先预览检测到的路径及供应商数量，确认后建立关联；也可选择“手动配置”，使用独立的空白配置目录。完成此选择前，程序不会加载本机供应商和凭据、运行 CLI 或探测代理。手动模式的配置目录位于启动器本地应用数据目录内；请在设置中填写程序路径，再自行添加供应商。示例统一采用 demo-provider 和 https://example.invalid/v1。

常驻“中文 / EN”按钮可随时切换语言。首次根据系统界面语言选择，中文系统用中文，其余用英文。模型 ID、协议值、用户输入和外部程序原始日志不翻译。

## 一键安装与更新

首次引导点击“一键安装并开始配置”，安装 OpenCodex 后进入空白账号配置。设置页提供“检查更新”“安装 / 更新到最新稳定版”“取消安装 / 检查”和“切回上一版本”。每次点击才查询 npm 稳定版标签，随后锁定该具体版本安装；已知版本较新时不降级。不会后台自动检查更新。

安装器从 nodejs.org 下载 Node.js LTS 并验证官方 SHA-256，从 registry.npmjs.org 下载锁定版本的 @bitkyc08/opencodex 并验证 SHA-512。HTTPS 元数据和校验用于检测传输损坏，不能抵御上游发布账号本身被入侵。npm 验证依赖完整性并检查 Node 版本兼容性；禁用依赖生命周期脚本，仅运行必需的 Bun 安装脚本。npm 使用空白配置和独立缓存，不继承 npm token、供应商凭据或 Node 预加载参数。CLI 验证使用临时空白账号目录。

需要能访问 nodejs.org 和 registry.npmjs.org 的网络，可能耗时数分钟；下载包不含离线运行环境。每次安装在 %LOCALAPPDATA%/OpenCodexLauncher/runtimes 下新建独立目录，验证成功后才保存所选路径。失败或取消保留原路径与配置，取消或关闭窗口会终止安装器启动的进程组。失败暂存和历史版本保留用于诊断、回退，每份可能占用数百 MB；仅在确认无进程、服务或保存路径使用时手动清理，保留当前和上一版本。此目录属于私有数据，不能上传。

启动器只切换自己使用的路径，外部命令、Windows 服务和托盘仍指向原安装，现有代理不会重启。需要你通过原有管理方式停止旧代理，再从启动器启动，才会使用新版本。启用 Reserve Force 时禁止切换；先关闭，再在新版本上重新检查补丁兼容性。本功能更新 OpenCodex，不更新启动器自身或 Codex CLI。

请保留 EXE 同目录的 OpenCodexLauncher.exe.config，其中启用了 .NET 4.8 和长依赖路径支持。安装器不修改系统 PATH，也不安装全局 npm 命令。

## 数据与旧版升级

若 2.5.0 在 runtimes/.staging-... 报“访问被拒绝”或目录被占用，请关闭启动器，把 2.5.1 Windows ZIP 完整解压到软件目录（保留随包提供的 EXE 配置文件），重新打开并重试安装。保留本地应用数据与已有运行时。2.5.1 直接使用独立且固定的目录，验证通过后才写入 installation.json 完成标记，避免最后重命名目录；未完成的目录仅保留用于诊断，不会自动选用。

若仍失败，提示会给出失败阶段，以及确实保存成功的诊断日志位置。访问错误请检查目录权限、占用情况和安全软件拦截记录。分享日志前检查并隐藏个人路径，不要上传整个 runtimes 或配置目录。本修复不能绕过实际存在的写入限制。

启动器设置及当前 Windows 用户 DPAPI 加密凭据存放在 %LOCALAPPDATA%/OpenCodexLauncher。请勿分享此目录。已关联的 OpenCodex/Codex 配置仍放在外部原位置；上游配置可能包含其自身保存的明文凭据，DPAPI 仅保护启动器凭据库中的内容。

升级前备份旧安装和本地应用数据，再替换 EXE。2.5 会识别旧设置，保留路径、策略及高级功能状态，下次保存设置时更新格式。常规启动不再自动读取 EXE 旁的 settings.json；若非常旧的版本只有该文件，可在明确升级时将其复制到本地应用数据目录，但不能覆盖已存在的设置。设置损坏时打开恢复页，不会写入空设置覆盖原文件。

## 路由与高级功能

路由模型使用完整 provider/model；不会按模型名后缀选中另一供应商。若网关只暴露生成别名、没有完整路由，Claude 模型解析会拒绝继续。

会话验证需要可靠的 session/conversation ID、有效请求时间戳和成功响应证据。目前普通启动的进程尚未向启动器提供可靠会话 ID，因此显示“待验证”。全局最近日志不会触发当前会话终止。严格校验开关控制可关联路由不匹配后的进程终止；启动失败与路由验证共享一次回退预算，使用原始模型和工作目录。

Reserve Force 为高级功能，新用户默认关闭。手动确认后才修改上游源码与配置并重启代理；源码结构必须兼容。程序备份相关文件、逐次记录自己写入的内容，失败时尝试恢复。检测到外部修改则停止覆盖，恢复日志及原始文件保存在本地应用数据目录的 recovery 下，手动恢复前请逐项比较。失败后需自行检查或重启代理。关闭 Reserve Force 会清除其路由配置，但保留上游源码中不再生效的兼容补丁；上游升级后可能需要重新检查兼容性。

## 构建、测试与发布

传统 Windows 编译：

    powershell -NoProfile -File ./build-v2.ps1 -OutputDirectory ./dist

SDK 编译（需要 .NET SDK 及 .NET Framework 4.8 引用程序集）：

    dotnet build OpenCodexLauncher.csproj -c Release -o ./dist-sdk

也可使用 build-sdk.ps1。两个入口统一生成 OpenCodexLauncher.exe，文件版本 2.5.1.0。

    ./tests/run-tests.ps1 -OutputDirectory ./test-output
    ./tests/run-ui-tests.ps1 -OutputDirectory ./test-output
    ./create-release.ps1 -OutputDirectory ./release-output

测试使用隔离目录、虚构凭据和本机模拟 HTTP 服务，不发送推理请求。界面测试覆盖中英文、最小窗口和草稿保留，并生成 100%、150%、200% 比例截图；这不能代替真实显示器 DPI 切换测试。

仅上传生成的 github-source 目录或 ZIP，不要上传整个工作目录。release-files.txt 为严格发布白名单；发布检查扫描文本、资源和二进制字符串，排除用户数据、缓存及历史资料。维护者还应通过 release-check.ps1 的 ForbiddenValues 在内存中提供自己的私有值进行专项扫描，不要将私有值写入仓库或打印。扫描无法保证发现所有秘密及编码变体。仓库内提供双构建、回归及发布检查的 GitHub Actions 工作流，上传仓库后才会执行。

详情参见 [发布说明](RELEASE_NOTES.md)、[安全说明](SECURITY.md) 和 [MIT 许可](LICENSE)。
