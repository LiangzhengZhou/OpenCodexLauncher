# OpenCodex Launcher UI / 交互重构方案

## 1. 现状判断

当前版本把“首次配置”“日常启动”“模型同步”“Native 接入”“故障诊断”混在同一个侧边栏里，导致：

- 8 个一级页面，日常用户只需要其中 2～3 个。
- 同一个动作出现多次：同步、关联并同步、同步并重启、Native Prepare、更新 Helper。
- “同步到 Codex”和“Associate and sync Desktop”看起来像两个目标，实际是包含关系。
- Native 不是一个独立产品模式，而是藏在“Native 直连”页面里，用户不知道当前到底走代理还是 Native。
- 诊断报告把范围说明（transport-get-only、desktop-loading-unverified）和真正问题（desktop-not-detected）放在一起。
- Helper 更新是手动按钮，但它是可安全自动完成的内容寻址更新。

## 2. 设计目标

1. **一个状态只给一个主操作。**
2. **首次配置和日常使用分开。**
3. **代理模式和 Native 模式变成显式选择，而不是两套页面。**
4. **安全且可逆的操作自动完成；有副作用的操作才确认。**
5. **技术诊断放到二级，不再污染主流程。**
6. **保留现有 provider、模型、凭据、Native 和回滚能力。**

## 3. 目标信息架构

一级导航从 8 个收敛为 5 个：

```text
首页
模型
Desktop
活动
设置
```

合并关系：

| 当前页面 | 新位置 |
|---|---|
| 概览 | 首页 |
| 供应商 + 模型管理 | 模型 |
| Codex 路由 | Desktop → 代理连接 |
| Native 直连 | Desktop → Native |
| 日志 | 活动 |
| 强制启动 | 设置 → 高级 |
| 设置 | 设置 |

## 4. 首页

首页只回答三个问题：

1. 现在是什么模式？
2. 能不能启动 Codex？
3. 下一步应该做什么？

```text
┌──────────────────────────────────────────────────────────┐
│ OpenCodex Launcher        [代理] [Native]      健康 设置 │
├──────────────┬───────────────────────────────────────────┤
│ 首页          │ 当前模式：代理                            │
│ 模型          │ 状态：已就绪                              │
│ Desktop      │                                           │
│ 活动          │ [启动 Codex]       [应用更改并重启]       │
│ 设置          │                                           │
│              │ ✓ Desktop 配置已关联                      │
│              │ ✓ OpenCodex 10100 正常                    │
│              │ ✓ 6 个模型已写入目标 catalog              │
│              │ ! Desktop 需要重启后才会加载新模型        │
│              │                                           │
│              │ 目标：...\.codex\config.toml  [更改]     │
│              │ 最近活动：...                             │
└──────────────┴───────────────────────────────────────────┘
```

状态驱动的唯一主按钮：

| 状态 | 主按钮 | 次按钮 |
|---|---|---|
| 未完成首次配置 | 连接 Desktop | 手动设置 |
| Desktop 未关联 | 连接 Desktop | 高级 |
| 代理未运行 | 启动 OpenCodex | 诊断 |
| 模型选择已变化 | 应用模型到 Codex | 放弃更改 |
| 已同步、Desktop 未重启 | 启动 Codex | 重启 Desktop |
| Native 未启用 | 启用 Native | 使用代理模式 |
| Native 已启用且健康 | 启动 Codex | 关闭 Native |
| Native 需要修复 | 修复 Native 接入 | 查看诊断 |

页面不再出现多个“同步”“准备”“更新”并列的情况。

## 5. 模型页

把供应商和模型合并成一个工作区：

```text
┌──────────────────────────────────────────────────────────┐
│ 目标：...\.codex\config.toml  [更改]                      │
├──────────────┬───────────────────────────────────────────┤
│ 供应商列表    │ Provider A                                │
│ + 添加供应商  │ API / 协议 / 密钥                         │
│              │ [获取模型]  [保存供应商]                  │
│              │                                           │
│              │ 已选 6 / 可用 20    [全选] [清空]        │
│              │ ☑ model-a                                 │
│              │ ☑ model-b                                 │
│              │ ☐ model-c                                 │
├──────────────┴───────────────────────────────────────────┤
│ 已选 6 个模型     [应用模型到 Codex]  [更多 ▾]           │
└──────────────────────────────────────────────────────────┘
```

- “获取模型”“保存供应商”只在当前供应商卡片内。
- “应用模型到 Codex”是唯一同步入口。
- 目标未关联时，主按钮变为“连接 Desktop”；关联完成后才允许应用。
- “更多”里放导入、批量选择、重新生成目录等低频动作。
- “应用”确认框显示将要写入的目标、模型数量、是否重启 Desktop。
- 不再同时存在“同步到 Codex”“同步模型”“Associate and sync Desktop”。

## 6. Desktop 页

Desktop 页分两个标签：

```text
[代理连接] [Native]
```

### 代理连接

```text
Desktop 目标：...\.codex\config.toml   [更改]
OpenCodex 服务：运行中 · 10100         [停止]
健康检查：healthz 成功
模型目录：6 个第三方模型 / 6 个已选
传输检查：仅 GET，不作推理证明          [查看详情]
```

### Native

```text
Helper：3.1.4 · 当前构建
Runtime：与 Desktop 包匹配
桥接：已启用
Desktop 进程：ChatGPT.exe 已检测

[关闭 Native]   [重新检查]
```

异常状态才出现：

```text
Helper 已自动更新，重启 Desktop 生效
Runtime 与当前 Desktop 包不匹配     [修复 Native 接入]
未检测到 Desktop 进程                [诊断]
```

- 自动 Helper 更新后只显示一条重启提示，不再要求用户点“更新 Helper”。
- “准备 Native 配置”“选择并启动 Desktop（诊断）”移入“诊断/高级”。
- “恢复原生 Codex”“重新接入 OpenCodex”从日常路由页移到这里的高级操作。

## 7. 活动页

把“日志”改成事件流：

```text
[全部] [错误] [同步] [Native]        [导出脱敏日志]
────────────────────────────────────────────────
14:32  同步成功 · 6 个模型 · 目标 config.toml
14:31  Desktop 进程检测：ChatGPT.exe
14:30  Helper 自动更新完成，等待 Desktop 重启
```

- 默认显示人类可读事件。
- 点击事件展开详情。
- 原始日志和 JSON 诊断放在“高级/导出”，不直接铺满页面。
- `transport-get-only`、`desktop-loading-unverified` 归入“检查范围”，不再当作错误条目。

## 8. 设置页

```text
通用
  语言
  启动行为
  自动更新 Native Helper（默认开）
路径
  Codex home / 配置 / catalog
更新
  Launcher 版本、Helper 版本
高级
  强制启动
  Reserve Force
  诊断启动
  原始诊断报告
```

危险操作只在高级区域，且都需要确认。

## 9. 交互规则

1. 每个页面最多一个 Primary 按钮。
2. 先选择目标和影响，再执行写入。
3. 所有长操作显示进度、禁用重复点击、允许取消。
4. 只有重启 Desktop、关闭 Native、恢复原生、删除 provider 需要确认。
5. Helper 更新、状态刷新、目标路径发现属于安全操作，自动完成或后台完成。
6. 写操作失败时保留原文件，显示“原因 + 下一步 + 查看诊断”，不显示裸异常。
7. 不把“未验证”显示成“失败”；用“已检测/未验证”区分事实和范围。
8. 目标路径、端口、模式、版本始终可见，避免用户猜当前生效对象。
9. 页面切换或窗口失焦时隐藏密钥。
10. 中英文使用同一状态模型，文案不出现“Codex / Desktop / 代理 / Native”混用。

## 10. 状态模型

建议引入一个统一的 `LauncherState`，页面只读状态并渲染：

```text
SetupState:
  incomplete | ready
Mode:
  proxy | native
DesktopTarget:
  unassociated | associated-valid | associated-invalid
ProxyState:
  stopped | starting | healthy | unhealthy
ModelState:
  clean | dirty | syncing | synced | missing
NativeState:
  disabled | helper-outdated | runtime-outdated | enabled-healthy | repair-needed
DesktopProcess:
  detected | not-detected | unknown
```

主按钮由状态计算，不再由页面各自决定。

## 11. 自动 Helper 更新

触发条件：

```text
Native 已启用
Helper 状态 = outdated
无 CODEX_CLI_PATH 冲突
无未完成的注册恢复记录
```

行为：

```text
后台部署当前 Helper
旧 Helper 保留
当前 Desktop 会话不打断
显示“Helper 已更新，重启 Desktop 生效”
失败则显示“重试更新”，不循环重试
```

Native 未启用时不自动更新，也不自动启用。

## 12. 诊断报告调整

- 进程检测同时识别 `ChatGPT.exe` 和 `Codex.exe`，排除 Electron `--type=` 子进程。
- `desktopHomeEvidence` 根据实际关联状态生成，不再固定 `candidates-only`。
- `scope` 与 `findings` 分离。
- 报告首页显示：
  - Desktop 进程：已检测 / 未检测 / 未知
  - Desktop 配置目标：已关联 / 候选
  - 代理健康：正常 / 异常
  - 模型加载：未验证
- “模型加载未验证”是事实说明，不是错误。

## 13. 实施顺序

### Phase 1：低风险修复（建议先做）

- 修 Desktop 进程检测和报表语义。
- 自动 Helper 更新。
- Home / Native / Routes 的按钮去重。
- 同步入口合并为一个。
- 补 UI 状态可见性测试。

### Phase 2：信息架构

- 合并供应商与模型为“模型”页。
- 新增 Desktop 页，吸收路由和 Native。
- 日志改为活动。
- 强制启动移入设置高级。

### Phase 3：视觉与易用性

- 统一状态卡、主按钮、提示条、确认框。
- 首次启动向导。
- 键盘操作、焦点顺序、屏幕阅读器标签。
- 中英文一致性和错误文案。

## 14. 不应改变的部分

- provider、模型选择、凭据存储；
- Native bridge 协议和内容寻址 Helper；
- 代理路由、OpenCodex 服务控制；
- 同步前备份、失败恢复和脱敏日志。

本方案只调整信息架构、状态展示和操作入口，不改变底层写入语义。
