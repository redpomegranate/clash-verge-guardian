# AGENTS.md - AI 开发指南

> 本文档供 AI 快速了解项目并执行开发流程

## 📋 项目概述

- **项目名称**：Clash Guardian Pro
- **版本**：v1.0.5
- **功能**：多 Clash 客户端的智能守护进程
- **语言**：C# (.NET Framework 4.5+)
- **平台**：Windows 10/11
- **架构**：5 个 partial class 文件，按职责拆分

## 📁 项目结构

```
ClashGuardian\
├── ClashGuardian.cs
├── ClashGuardian.UI.cs
├── ClashGuardian.Network.cs
├── ClashGuardian.Monitor.cs
├── ClashGuardian.Update.cs
├── assets\
│   ├── icon-source.png        # icon 源图
│   └── ClashGuardian.ico      # 编译用 win32 icon
├── build.ps1                  # 一键编译脚本（输出到 dist\）
├── dist\                      # 编译产物输出目录（本地生成，不提交）
├── README.md                  # 项目说明文档
└── AGENTS.md                  # 本文件
```

## 📂 运行数据目录（重要）

运行时文件默认存放在 `%LOCALAPPDATA%\\ClashGuardian\\`，不会与源码/可执行混放：

- `config\\config.json` - 配置文件
- `logs\\guardian.log` - 异常日志（仅异常）
- `monitor\\monitor_YYYYMMDD.csv` - 监控数据
- `diagnostics\\diagnostics_YYYYMMDD_HHmmss\\` - 诊断包导出目录

## 🔧 编译命令

```powershell
# 推荐：一键编译（含 icon）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 或手动编译（需指定 win32 icon）
mkdir dist -Force | Out-Null
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:assets\ClashGuardian.ico /out:dist\ClashGuardian.exe ClashGuardian.cs ClashGuardian.UI.cs ClashGuardian.Network.cs ClashGuardian.Monitor.cs ClashGuardian.Update.cs
```

编译成功标志：无 error 输出（warning 可忽略）

## ⚠️ 重要注意事项

1. **UI 线程安全** - 后台线程操作 UI 必须使用 `this.BeginInvoke((Action)(() => { ... }))`
2. **跨线程字段** - `currentNode`/`nodeGroup`/`detectedCoreName`/`detectedClientPath` 声明为 `volatile`；计数器使用 `Interlocked.Increment`；`nodeBlacklist` 使用 `blacklistLock`
3. **日志精简** - 正常情况不记录日志，只记录异常（TestProxy > 5s，其他 > 2s）
4. **静默运行** - 所有自动操作不要有弹窗/通知（自动更新除外）；默认 `allowAutoStartClient=false`，不自动启动/重启客户端 UI
5. **节点名称** - 使用 `ExtractJsonString` 解析 Unicode 转义，用 `SafeNodeName` 过滤不可显示字符和 emoji surrogate pair
6. **代理组切换** - 不要硬编码 GLOBAL，使用 `FindSelectorGroup` 自动发现实际节点所属的 Selector 组
7. **节点列表获取** - 从 Selector 组的 `all` 数组正向提取节点名（`GetGroupAllNodes`），不要反向扫描 type 字段
8. **JSON 解析** - 使用 `FindObjectBounds` + `FindFieldValue` 统一入口，避免重复的括号匹配代码
9. **决策逻辑** - `EvaluateStatus` 是纯函数，返回 `StatusDecision` 结构体，不直接修改实例状态
10. **重启逻辑** - 杀内核→等5秒→检查自动恢复+代理可用验证；仅当 `allowAutoStartClient=true` 才允许自动重启客户端；客户端不在时不干涉（显示“等待 Clash...”）；`restartLock` + `_isRestarting` 防并发
11. **按钮/菜单** - 耗时操作（重启、切换、更新检查）必须通过 `ThreadPool.QueueUserWorkItem` 在后台执行，禁止阻塞 UI 线程
12. **客户端路径** - 检测到后持久化到 config.json 的 `clientPath` 字段；搜索优先级：运行进程→config→默认路径→注册表
13. **暂停检测** - 暂停期间停止检测循环（Timer 停止），不自动重启/切换；恢复时重置 failCount/consecutiveOK/cooldownCount 并恢复 interval
14. **诊断导出** - `ExportDiagnostics` 仅用户触发，脱敏 `clashSecret`，导出到 `%LOCALAPPDATA%\\ClashGuardian\\diagnostics_*`
15. **禁用名单（disabledNodes）** - 托盘勾选后写入 config；一旦存在 `disabledNodes` 将忽略 `excludeRegions`
16. **偏好节点（preferredNodes）** - 托盘勾选后写入 config；自动切换优先偏好节点（不可用则回退，偏好集合过小可能降低抗风险）
17. **订阅级自动切换（Clash Verge Rev）** - 默认关闭；通过修改 `%APPDATA%\\io.github.clash-verge-rev.clash-verge-rev\\profiles.yaml` 的 `current:` 并强制重启客户端生效；严禁日志输出订阅 URL/token

## 🏗️ 代码模块（按文件）

### ClashGuardian.cs（主文件）
| 区域 | 内容 |
|------|------|
| 常量 | `DEFAULT_*`、`APP_VERSION`、超时常量、阈值常量 |
| 结构体 | `StatusDecision` — 决策结果（纯数据） |
| 静态数组 | `DEFAULT_CORE_NAMES`、`DEFAULT_CLIENT_NAMES`、`DEFAULT_API_PORTS`、`DEFAULT_EXCLUDE_REGIONS` |
| 字段 | 运行时配置、UI 组件、运行时状态、线程安全设施 |
| 方法 | 构造函数、`DoFirstCheck`、`LoadConfigFast`、`SaveDefaultConfig`、`DetectRunningCore/Client`、`FindClientFromRegistry`、`SaveClientPath`、`AutoDiscoverApi`、`Main` |

### ClashGuardian.UI.cs
| 方法 | 说明 |
|------|------|
| `InitializeUI` | 窗口布局和控件创建 |
| `CreateButton`/`CreateInfoLabel`/`CreateSeparator` | UI 工厂方法 |
| `InitializeTrayIcon` | 系统托盘菜单（含禁用名单/偏好节点/暂停检测/诊断导出/黑名单管理/检查更新） |
| `OpenFileInNotepad` | 安全打开配置/数据/日志（try/catch，不崩溃） |
| `ToggleDetectionPause`/`PauseDetectionUi`/`ResumeDetectionUi` | 暂停/恢复检测（停止 Timer） |
| `ToggleFollowClashWatcher` | 跟随 Clash：开机启动 Watcher，检测到 Clash 启动后拉起 Guardian |
| `RefreshNodeDisplay` | 刷新节点和统计显示 |
| `FormatTimeSpan` | 时间格式化 |

### ClashGuardian.Network.cs
| 方法 | 说明 |
|------|------|
| `ApiRequest`/`ApiPut` | HTTP API 通信 |
| `FindObjectBounds`/`FindFieldValue` | JSON 对象边界查找和字段提取（统一入口，忽略字符串内花括号） |
| `FindProxyNow`/`FindProxyType` | 基于上述方法的便捷包装 |
| `ExtractJsonString`/`ExtractJsonStringAt` | Unicode 转义解析 |
| `SafeNodeName` | 节点名安全过滤 |
| `GetCurrentNode`/`ResolveActualNode` | 节点解析（递归） |
| `GetGroupAllNodes`/`GetNodeDelay`/`FindSelectorGroup` | 节点组管理 |
| `SwitchToBestNode`/`CleanBlacklist` | 节点切换和黑名单 |
| `ClearBlacklist`/`RemoveCurrentNodeFromBlacklist` | 黑名单管理（托盘操作） |
| `TryGetRecentSubscriptionProbe`/`RunSubscriptionHealthProbeWorker` | **订阅健康探测**：抽样 delay probe 判断订阅整体可用性（异常态触发，后台并行） |
| `TriggerDelayTest`/`TestProxy` | 延迟测试和代理测试 |

### ClashGuardian.Monitor.cs
| 方法 | 说明 |
|------|------|
| `Log`/`LogPerf`/`LogData`/`CleanOldLogs` | 日志管理 |
| `ExportDiagnostics` | 诊断包导出：summary+脱敏配置+日志+监控数据 |
| `GetTcpStats`/`GetMihomoStats` | 系统状态采集 |
| `RestartClash` | 重启流程：杀内核→等5秒→检查恢复+代理验证→必要时重启客户端（默认禁止，需 `allowAutoStartClient=true`）；客户端不在时不干涉；`_isRestarting` 防并发 |
| `StartClientProcess` | 启动客户端进程（最小化窗口） |
| `CheckStatus` | Timer 入口，检查 `_isRestarting` 和 `_isChecking` 防重入 |
| `DoCooldownCheck` | 冷却期检测：内核恢复+代理正常→立即结束冷却 |
| `DoCheckInBackground` | 正常检测循环 |
| `MaybeStartSubscriptionHealthProbe`/`TryHandleSubscriptionProbeDown` | **订阅健康探测**：异常首次出现时启动探测；确认订阅整体不可用时快速降级为“订阅切换/提示更换提供商” |
| `UpdateUI` | UI 渲染（调用 EvaluateStatus 获取决策，应用状态，更新界面） |
| `EvaluateStatus` | **纯决策函数**：输入当前状态，输出 `StatusDecision`，不修改实例 |

### ClashGuardian.Update.cs
| 方法 | 说明 |
|------|------|
| `CheckForUpdate` | 检查 GitHub 最新版本（代理优先，直连回退） |
| `CompareVersions` | 语义化版本比较 |
| `ExtractAssetUrl` | 从 Release JSON 提取 .exe 下载链接 |
| `DownloadAndUpdate` | 下载 + 热替换 + 回滚保护 |

## 📊 决策逻辑（EvaluateStatus）

| 条件 | 动作 | Event |
|------|------|-------|
| 进程不存在 | 重启 | `ProcessDown` |
| 内存 > 150MB | 无条件重启 | `CriticalMemory` |
| 内存 > 70MB + 代理异常 | 重启 | `HighMemoryNoProxy` |
| 内存 > 70MB + 代理正常 + 延迟 > 400ms | 重启（快速恢复管线） | `HighMemoryHighDelay` |
| CloseWait > 20 + 代理异常 | 重启 | `CloseWaitLeak` |
| 代理连续 2 次无响应 | 切换节点 | `NodeSwitch` |
| 代理连续 4 次无响应 | 重启 | `ProxyTimeout` |
| 延迟 > 400ms 连续 2 次 | 切换节点 | `HighDelaySwitch` |

## 🔒 线程安全模型

| 字段 | 保护方式 | 说明 |
|------|---------|------|
| `currentNode`/`nodeGroup` | `volatile` | 后台写，UI 读 |
| `detectedCoreName`/`detectedClientPath` | `volatile` | 后台写，UI 读 |
| `lastDelay` | `Interlocked.Exchange` | 后台写，UI 读 |
| `totalIssues`/`totalChecks`/`totalRestarts`/`totalSwitches` | `Interlocked.Increment` | 后台写，UI 读 |
| `failCount`/`consecutiveOK`/`cooldownCount` | UI 线程专用 | 仅通过 `BeginInvoke` 修改 |
| `nodeBlacklist` | `blacklistLock` | 多线程读写 |
| `restartLock` | `lock` | 重启门闩原子化（避免并发重启竞态） |
| `_isChecking` | `Interlocked.CompareExchange` | 防重入 |
| `_isRestarting` | `volatile bool` | 防止重启期间并发检测 |
| `_isDetectionPaused` | `volatile bool` | 暂停检测开关（跨线程读写） |

## 🔄 关键修复记录

### v1.0.4 改进
1. **自动切换失败风暴保护** - 当出现“延迟过高 5000ms / 无 delay 历史 / API无响应”等导致的切换失败时：自动节流日志、限制切换频率，并在连续失败达到阈值后升级为“订阅切换/重启客户端”，避免无限循环刷屏
2. **恢复链路提速** - 客户端重启后的“内核+API 就绪等待”合并为单循环并提前触发 `AutoDiscoverApi`；代理恢复检测前 3 秒改为 500ms 轮询；常规 core 重启后的代理验证窗口缩短为 ~4.5s，失败尽快升级为重启客户端
3. **订阅切换紧急绕过** - 在“客户端重启 + 节点切换仍无效”的恢复阶段，订阅切换允许在严重故障场景下绕过 cooldown（仍有最小间隔保护）

### v1.0.3 改进
1. **修复：mihomo/meta 延迟测试接口不兼容** - `TriggerDelayTest` 使用 `/proxies/{name}/delay`，避免 `/group/{name}/delay` 404 导致“请先测速”死循环（影响自动切节点与恢复链路）
2. **增强：无 delay 历史时的实时探测** - 自动切节点在 delay history 不可用时，对候选节点做实时 delay probe 后再切换（有限并发、轮转覆盖）
3. **优化：恢复链路升级** - “内核恢复但代理未恢复”时：强制重启客户端（尽量模拟手动退出重进，包含后台进程）→刷新/切换低延迟节点（最多 2 次）→订阅切换+强制重启→再次刷新/切换（2 次）→再失败则停止继续自动循环（需要人工介入）

### v1.0.0 改进
1. **禁用名单可配置** - 托盘“禁用名单”勾选节点，写入 `disabledNodes`，并覆盖 `excludeRegions`
2. **偏好节点** - 托盘“偏好节点”勾选节点，自动切换优先偏好节点（不可用则回退）
3. **订阅级自动切换（Clash Verge Rev）** - 连续自动切换节点仍不可用时，按白名单轮换订阅并强制重启客户端（默认关闭）
4. **统计口径调整** - UI 统计由检测次数改为“问题段落次数”（正常→异常 +1）
5. **图标内置** - `build.ps1` 使用 `/win32icon`，窗口/托盘图标与 EXE 一致

### v0.0.9 改进
1. **运行数据目录分离** - `config/log/monitor/diagnostics` 统一存放到 `%LOCALAPPDATA%\\ClashGuardian\\`，避免与源码/可执行混放（启动时自动尝试迁移旧文件）
2. **编译产物分离** - 提供 `build.ps1`，默认输出到 `dist\\ClashGuardian.exe`

### v0.0.8 改进
1. **并发重启门闩** - `restartLock` + `_isRestarting` 原子化，避免重启流程并发
2. **配置兜底** - 配置数值 `TryParse + Clamp`，异常配置不再导致崩溃（不回写 config）
3. **JSON 边界加固** - `FindObjectBounds` 忽略字符串内花括号，降低误判
4. **本地 API 直连** - loopback API 禁用系统代理，避免 PAC/全局代理干扰
5. **控制与诊断增强** - 托盘支持暂停检测、导出诊断包、打开配置/数据/日志、黑名单管理

### v0.0.7 改进
1. **客户端路径持久化** - `detectedClientPath` 保存到 config.json，客户端关闭后仍可重启
2. **注册表搜索** - `FindClientFromRegistry` 遍历 HKLM/HKCU Uninstall 键发现安装路径
3. **默认路径扩充** - 15+ 条路径覆盖 Clash Verge Rev、Scoop、Program Files (x86) 等

### v0.0.6 改进
1. **重启死循环修复** - 添加 `_isRestarting` 防并发，杀内核后分步检测恢复
2. **冷却期修正** - 使用 `COOLDOWN_COUNT` 常量（5次 ≈ 25秒）替代硬编码 2 次
3. **分步恢复** - 杀内核→等5秒→检查恢复→未恢复则重启客户端（智能降级）

### v0.0.5 改进
1. **重启静默化** - 只杀内核进程，客户端自动恢复，不再弹出 Clash GUI 窗口
2. **UI 线程安全** - 重启/切换/更新检查全部移至后台线程，UI 不再卡死
3. **快速恢复** - 冷却期检测到内核+代理正常后立即结束，恢复时间 ~8s（旧版 ~32s）

### v0.0.4 改进
1. **自动更新** - 启动时静默检查 GitHub Release，代理优先+直连回退下载，NTFS 热替换，回滚保护
2. **partial class 拆分** - 单文件拆为 5 个模块文件，按职责分离
3. **线程安全强化** - `volatile`/`Interlocked` 保护所有跨线程字段
4. **决策逻辑纯化** - `EvaluateStatus` 返回 `StatusDecision` 结构体
5. **JSON 解析去重** - `FindObjectBounds`/`FindFieldValue` 统一入口
6. **节点排除可配置** - `excludeRegions` 从 config.json 加载
7. **空 catch 全部修复** - 15 处加日志，18 处加注释
8. **魔法数字消除** - 30+ 个常量替代硬编码值

### v0.0.3 修复
1. **节点切换 "proxy not exist"** - 从 Selector 组的 `all` 数组正向获取节点列表
2. **硬编码 GLOBAL 组** - `FindSelectorGroup` 自动发现子 Selector 组
3. **节点名框框乱码** - `SafeNodeName` 跳过 surrogate pair
4. **测速阻塞** - `TriggerDelayTest` 改为 `BeginGetResponse` 异步

### v0.0.2 修复
1. **重启后 UI 卡住** - `RestartClash` UI 操作需 `BeginInvoke`
2. **冷却期无响应** - 冷却期主动探测
3. **节点名乱码** - Unicode 转义解析

## 🛠️ 常用开发命令

```powershell
# 编译（推荐：含 icon）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 查看 Clash 相关进程
Get-Process | Where-Object {$_.ProcessName -like "*clash*" -or $_.ProcessName -like "*mihomo*"}

# 结束 ClashGuardian
Get-Process | Where-Object {$_.ProcessName -like "*ClashGuardian*"} | Stop-Process -Force
```
