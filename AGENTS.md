# AGENTS.md - AI 开发指�?

> 本文档供 AI 快速了解项目并执行开发流�?

## 📋 项目概述

- **项目名称**：Clash Guardian Pro
- **版本**：v1.0.8
- **功能**：多 Clash 客户端的智能守护进程
- **语言**：C# (.NET Framework 4.5+)
- **平台**：Windows 10/11
- **架构**�? �?partial class 文件，按职责拆分

## 📁 项目结构

```
ClashGuardian\
├── ClashGuardian.cs
├── ClashGuardian.UI.cs
├── ClashGuardian.Network.cs
├── ClashGuardian.Monitor.cs
├── ClashGuardian.Update.cs
├── ClashGuardian.Connectivity.cs
├── ClashGuardian.ConfigBackfill.cs
├── ClashGuardian.TcpCoreStats.cs
├── ClashGuardian.AssemblyInfo.cs # 程序元数据（版本/产品信息�?
├── assets\
�?  ├── icon-source.png        # icon 源图
�?  └── ClashGuardian.ico      # 编译�?win32 icon
├── build.ps1                  # 一键编译脚本（输出�?dist\�?
├── dist\                      # 编译产物输出目录（本地生成，不提交）
├── README.md                  # 项目说明文档
└── AGENTS.md                  # 本文�?
```

## 📂 运行数据目录（重要）

运行时文件默认存放在 `%LOCALAPPDATA%\\ClashGuardian\\`，不会与源码/可执行混放：

- `config\\config.json` - 配置文件
- `logs\\guardian.log` - 异常日志（仅异常�?
- `monitor\\monitor_YYYYMMDD.csv` - 监控数据
- `diagnostics\\diagnostics_YYYYMMDD_HHmmss\\` - 诊断包导出目�?

## 🔧 编译命令

```powershell
# 推荐：一键编译（�?icon�?
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 或手动编译（需指定 win32 icon�?
mkdir dist -Force | Out-Null
$sources = Get-ChildItem -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:assets\ClashGuardian.ico /out:dist\ClashGuardian.exe $sources
```

编译成功标志：无 error 输出（warning 可忽略）

## ⚠️ 重要注意事项

1. **UI 线程安全** - 后台线程操作 UI 必须使用 `this.BeginInvoke((Action)(() => { ... }))`
2. **跨线程字�?* - `currentNode`/`nodeGroup`/`detectedCoreName`/`detectedClientPath` 声明�?`volatile`；计数器使用 `Interlocked.Increment`；`nodeBlacklist` 使用 `blacklistLock`
3. **日志精简** - 正常情况不记录日志，只记录异常（TestProxy > 5s，其�?> 2s�?
4. **静默运行** - 所有自动操作不要有弹窗/通知（自动更新除外）；默�?`allowAutoStartClient=false`，不自动启动/重启客户�?UI
5. **节点名称** - 使用 `ExtractJsonString` 解析 Unicode 转义，用 `SafeNodeName` 过滤不可显示字符�?emoji surrogate pair
6. **代理组切�?* - 不要硬编�?GLOBAL，使�?`FindSelectorGroup` 自动发现实际节点所属的 Selector 组；优先选择“有可用候选节点”的主组，避免误选仅�?`Proxy/DIRECT` 的辅助组
7. **节点列表获取** - �?Selector 组的 `all` 数组正向提取节点名（`GetGroupAllNodes`），不要反向扫描 type 字段
8. **JSON 解析** - 使用 `FindObjectBounds` + `FindFieldValue` 统一入口，避免重复的括号匹配代码
9. **决策逻辑** - `EvaluateStatus` 是纯函数，返�?`StatusDecision` 结构体，不直接修改实例状�?
10. **重启逻辑** - 杀内核→等5秒→检查自动恢�?代理可用验证；仅�?`allowAutoStartClient=true` 才允许自动重启客户端；客户端不在时不干涉（显示“等�?Clash...”）；`restartLock` + `_isRestarting` 防并�?
11. **按钮/菜单** - 耗时操作（重启、切换、更新检查）必须通过 `ThreadPool.QueueUserWorkItem` 在后台执行，禁止阻塞 UI 线程
12. **客户端路�?* - 检测到后持久化�?config.json �?`clientPath` 字段；搜索优先级：运行进程→config→默认路径→注册�?
13. **暂停检�?* - 暂停期间停止检测循环（Timer 停止），不自动重�?切换；恢复时重置 `failCount/highDelayCount/closeWaitFailCount/consecutiveOK/cooldownCount` 并恢�?interval
14. **诊断导出** - `ExportDiagnostics` 仅用户触发，脱敏 `clashSecret`，导出到 `%LOCALAPPDATA%\\ClashGuardian\\diagnostics_*`
15. **禁用名单（disabledNodes�?* - 托盘勾选后写入 config；一旦存�?`disabledNodes` 将忽�?`excludeRegions`
16. **偏好节点（preferredNodes�?* - 托盘勾选后写入 config；自动切换优先偏好节点（不可用则回退，偏好集合过小可能降低抗风险�?
17. **订阅级自动切换（Clash Verge Rev�?* - 默认关闭；通过修改 `%APPDATA%\\io.github.clash-verge-rev.clash-verge-rev\\profiles.yaml` �?`current:` 并强制重启客户端生效；严禁日志输出订�?URL/token
18. **延迟指标区分** - `TestProxy` RTT（`lastDelay`）与节点 `histDelay/liveDelay` 必须分离；UI 只展示前�?
19. **配置补全策略** - 仅补全安�?key（如 `fastInterval/speedFactor/proxyTestTimeoutMs/connectivity*` �?guardrail）；不要自动�?`disabledNodes`
20. **订阅切换前置条件** - 所有自动订阅切换路径必须要�?`allowAutoStartClient=true`
21. **文档同步强制** - 每次修改代码/参数/UI/行为后，必须同步更新 `README.md` �?`AGENTS.md` 的对应说明，再进行编译与交付
22. **窗口行为** - 最小化保留在任务栏；仅�?`OnFormClosing(UserClosing)` 时隐藏到托盘后台
23. **稳态去重原�?* - 可以提取私有 helper 做去重，但不得改变阈值、事件名、配�?key、CSV 列结构和自动动作触发顺序

## 🏗�?代码模块（按文件�?

### ClashGuardian.cs（主文件�?
| 区域 | 内容 |
|------|------|
| 常量 | `DEFAULT_*`、`APP_VERSION`、超时常量、阈值常�?|
| 结构�?| `StatusDecision` �?决策结果（纯数据�?|
| 静态数�?| `DEFAULT_CORE_NAMES`、`DEFAULT_CLIENT_NAMES`、`DEFAULT_API_PORTS`、`DEFAULT_EXCLUDE_REGIONS`、`DEFAULT_CONNECTIVITY_TEST_URLS` |
| 字段 | 运行时配置、UI 组件、运行时状态、线程安全设�?|
| 方法 | 构造函数、`DoFirstCheck`、`LoadConfigFast`、`LoadIntConfigWithClamp`、`SaveDefaultConfig`、`UpdateConfigJson`、`DetectRunningCore/Client`、`FindClientFromRegistry`、`SaveClientPath`、`AutoDiscoverApi`、`Main` |

### ClashGuardian.UI.cs
| 方法 | 说明 |
|------|------|
| `InitializeUI` | 窗口布局和控件创�?|
| `CreateButton`/`CreateInfoLabel`/`CreateSeparator` | UI 工厂方法 |
| `ShowMainWindowFromTray` | 托盘恢复窗口统一入口 |
| `QueueManualSwitchAction` | 手动切换节点统一入口（按�?菜单复用�?|
| `InitializeTrayIcon` | 系统托盘菜单（含禁用名单/偏好节点/暂停检�?诊断导出/黑名单管�?检查更新） |
| `OpenFileInNotepad` | 安全打开配置/数据/日志（try/catch，不崩溃�?|
| `ToggleDetectionPause`/`PauseDetectionUi`/`ResumeDetectionUi` | 暂停/恢复检测（停止 Timer�?|
| `ToggleFollowClashWatcher` | 跟随 Clash：开机启�?Watcher，检测到 Clash 启动后拉�?Guardian |
| `RefreshNodeDisplay` | 刷新节点和统计显�?|
| `FormatTimeSpan` | 时间格式�?|

### ClashGuardian.Network.cs
| 方法 | 说明 |
|------|------|
| `ApiRequest`/`ApiPut` | HTTP API 通信 |
| `FindObjectBounds`/`FindFieldValue` | JSON 对象边界查找和字段提取（统一入口，忽略字符串内花括号�?|
| `FindProxyNow`/`FindProxyType` | 基于上述方法的便捷包�?|
| `ExtractJsonString`/`ExtractJsonStringAt` | Unicode 转义解析 |
| `SafeNodeName` | 节点名安全过�?|
| `GetCurrentNode`/`ResolveActualNode` | 节点解析（递归�?|
| `GetGroupAllNodes`/`GetNodeDelay`/`FindSelectorGroup` | 节点组管�?|
| `SwitchToBestNode`/`CleanBlacklist` | 节点切换和黑名单 |
| `ClearBlacklist`/`RemoveCurrentNodeFromBlacklist` | 黑名单管理（托盘操作�?|
| `TryGetRecentSubscriptionProbe`/`RunSubscriptionHealthProbeWorker` | **订阅健康探测**：抽�?delay probe 判断订阅整体可用性（异常态触发，后台并行�?|
| `TriggerDelayTest`/`TestProxy` | 延迟测试和代理测�?|

### ClashGuardian.Monitor.cs
| 方法 | 说明 |
|------|------|
| `Log`/`LogPerf`/`LogData`/`CleanOldLogs` | 日志管理 |
| `ExportDiagnostics` | 诊断包导出：summary+脱敏配置+日志+监控数据 |
| `IsClientRunningSafe`/`ApplyWaitingClashUiState`/`ResetIssueCounters` | 稳态去�?helper（统一客户端在场判断、等�?Clash UI、计数重置） |
| `GetTcpStats`/`GetMihomoStats` | 系统状态采�?|
| `RestartClash` | 重启流程：杀内核→等5秒→检查恢�?代理验证→必要时重启客户端（默认禁止，需 `allowAutoStartClient=true`）；客户端不在时不干涉；`_isRestarting` 防并�?|
| `StartClientProcess` | 启动客户端进程（最小化窗口�?|
| `CheckStatus` | Timer 入口，检�?`_isRestarting` �?`_isChecking` 防重�?|
| `DoCooldownCheck` | 冷却期检测：内核恢复+代理正常→立即结束冷�?|
| `DoCheckInBackground` | 正常检测循�?|
| `MaybeStartSubscriptionHealthProbe`/`TryHandleSubscriptionProbeDown` | **订阅健康探测**：异常首次出现时启动探测；确认订阅整体不可用时快速降级为“订阅切�?提示更换提供商�?|
| `UpdateUI` | UI 渲染（调�?EvaluateStatus 获取决策，应用状态，更新界面�?|
| `EvaluateStatus` | **纯决策函�?*：输入当前状态，输出 `StatusDecision`，不修改实例 |

### ClashGuardian.Update.cs
| 方法 | 说明 |
|------|------|
| `CheckForUpdate` | 检�?GitHub 最新版本（代理优先，直连回退�?|
| `CompareVersions` | 语义化版本比�?|
| `ExtractAssetUrl` | �?Release JSON 提取 .exe 下载链接 |
| `DownloadAndUpdate` | 下载 + 热替�?+ 回滚保护 |

### ClashGuardian.Connectivity.cs
| 方法 | 说明 |
|------|------|
| `MaybeStartConnectivityProbe` | 异常态触发连接性探测（节流 + 防重入） |
| `RunConnectivityProbeWorker` | 对配�?URL 列表做代理连通性探�?|
| `TryGetRecentConnectivity` | 获取有效期内的探测快照（Unknown/Ok/Slow/Down�?|

### ClashGuardian.ConfigBackfill.cs
| 方法 | 说明 |
|------|------|
| `BackfillConfigIfMissing` | 启动时安全补全缺�?key（不引入语义变化字段�?|

### ClashGuardian.TcpCoreStats.cs
| 方法 | 说明 |
|------|------|
| `GetTcpStatsSnapshot` | 采集全局 TCP 统计并补�?`CoreCloseWait`（决策使用） |
| `GetCoreProcessPidSnapshot`/`TryParseNetstatTcpLine` | core PID 快照�?netstat 解析辅助 |

## 📊 决策逻辑（EvaluateStatus�?

| 条件 | 动作 | Event |
|------|------|-------|
| 进程不存�?| 重启 | `ProcessDown` |
| 内存 > 150MB | 无条件重�?| `CriticalMemory` |
| 内存 > 70MB + 代理异常 | 重启 | `HighMemoryNoProxy` |
| 内存 > 70MB + 代理正常 + 延迟 > 400ms | 重启（快速恢复管线） | `HighMemoryHighDelay` |
| core CloseWait > 25 + 代理异常，连�?3 �?| 重启 | `CloseWaitLeak` |
| 代理连续 2 次无响应 | 切换节点 | `NodeSwitch` |
| 代理连续 4 次无响应 | 重启 | `ProxyTimeout` |
| 高延�?+ Conn=Slow/Down�?400ms）连�?2 �?| 切换节点 | `HighDelaySwitch` |
| 高延�?+ Conn=Unknown�?400ms）连�?3 �?| 切换节点 | `HighDelaySwitch` |
| 高延�?+ Conn=Ok�?520ms）连�?4 �?| 切换节点 | `HighDelaySwitch` |

## 🔒 线程安全模型

| 字段 | 保护方式 | 说明 |
|------|---------|------|
| `currentNode`/`nodeGroup` | `volatile` | 后台写，UI �?|
| `detectedCoreName`/`detectedClientPath` | `volatile` | 后台写，UI �?|
| `lastDelay` | `Interlocked.Exchange` | 后台写，UI �?|
| `lastNodeDelay`/`lastNodeDelayKind` | `Interlocked + volatile` | 节点 history/live delay（仅诊断/日志�?|
| `totalIssues`/`totalChecks`/`totalRestarts`/`totalSwitches` | `Interlocked.Increment` | 后台写，UI �?|
| `failCount`/`highDelayCount`/`closeWaitFailCount`/`consecutiveOK`/`cooldownCount` | UI 线程专用 | 仅通过 `BeginInvoke` 修改 |
| `autoSwitchEpisodeAttempts`/`pendingSwitchVerification` | UI 线程专用 | 订阅切换 episode 计数 |
| `nodeBlacklist` | `blacklistLock` | 多线程读�?|
| `restartLock` | `lock` | 重启门闩原子化（避免并发重启竞态） |
| `_isChecking` | `Interlocked.CompareExchange` | 防重�?|
| `_isRestarting` | `volatile bool` | 防止重启期间并发检�?|
| `_isDetectionPaused` | `volatile bool` | 暂停检测开关（跨线程读写） |
| `connectivity*` 快照字段 | `Interlocked` | 连接性探测结果跨线程读写 |

## 🔄 关键修复记录

### v1.0.7 改进
1. **稳定性：高延迟判据分�?* - `HighDelay` �?`ConnVerdict(Ok/Unknown/Slow/Down)` 分层，`Conn=Ok` 需更高延迟和更多连续命中后才切换，降低误切换�?
2. **稳定性：CloseWait 进程级判�?* - 新增 `CoreCloseWait` 聚合采样；仅�?`proxyFail + coreCloseWait 连续超阈值` 时触�?`CloseWaitLeak` 重启�?
3. **防风暴：自动动作统一 Gate** - 自动切换/重启共享 10 分钟窗口限流、最小间隔、抑制窗口和抑制日志节流；手动操作不受自�?Gate 限制�?
4. **架构精简：核心流程拆�?* - `UpdateUI` 拆为 `ApplyDecisionState/RenderUi/ScheduleAutoActions`；`RestartClash` 拆为阶段方法，提升可维护性�?
5. **配置路径去重** - `LoadConfigFast` 引入 `LoadIntConfigWithClamp`；`SaveClientPath/SaveDisabledNodes/SavePreferredNodes` 统一�?`UpdateConfigJson`�?
6. **新增 guardrail 配置** - 增加高延迟、CloseWait、自动动作频率相�?key，并�?`ConfigBackfill` 做安全补全（仅补缺失，向后兼容）�?
7. **交互修复：最小化回任务栏** - 最小化不再隐藏；关闭（X）才入托盘，托盘恢复和退出行为保持原有语义�?

### v1.0.6 改进
1. **修复：首次检测句柄竞�?* - 首次检测改为句柄创建后触发，避�?“在创建窗口句柄之前调用 BeginInvoke�?
2. **修复：延迟指标混�?* - 节点切换日志改为 `histDelay/liveDelay`，不再覆�?`lastDelay`
3. **新增：连接性探�?* - 高延迟场景增加真实网站连通性探测，用于订阅切换前综合判�?
4. **优化：订阅切换策�?* - 触发条件改为 `proxyFail` �?`highDelay + conn(Slow/Down)`，并采用 episode 计数
5. **新增：配置安全补�?* - 自动补齐 `fastInterval/speedFactor/proxyTestTimeoutMs/connectivity*`，不自动�?`disabledNodes`

### v1.0.4 改进
1. **自动切换失败风暴保护** - 当出现“延迟过�?5000ms / �?delay 历史 / API无响应”等导致的切换失败时：自动节流日志、限制切换频率，并在连续失败达到阈值后升级为“订阅切�?重启客户端”，避免无限循环刷屏
2. **恢复链路提�?* - 客户端重启后的“内�?API 就绪等待”合并为单循环并提前触发 `AutoDiscoverApi`；代理恢复检测前 3 秒改�?500ms 轮询；常�?core 重启后的代理验证窗口缩短�?~4.5s，失败尽快升级为重启客户�?
3. **订阅切换紧急绕�?* - 在“客户端重启 + 节点切换仍无效”的恢复阶段，订阅切换允许在严重故障场景下绕�?cooldown（仍有最小间隔保护）

### v1.0.3 改进
1. **修复：mihomo/meta 延迟测试接口不兼�?* - `TriggerDelayTest` 使用 `/proxies/{name}/delay`，避�?`/group/{name}/delay` 404 导致“请先测速”死循环（影响自动切节点与恢复链路）
2. **增强：无 delay 历史时的实时探测** - 自动切节点在 delay history 不可用时，对候选节点做实时 delay probe 后再切换（有限并发、轮转覆盖）
3. **优化：恢复链路升�?* - “内核恢复但代理未恢复”时：强制重启客户端（尽量模拟手动退出重进，包含后台进程）→刷新/切换低延迟节点（最�?2 次）→订阅切�?强制重启→再次刷�?切换�? 次）→再失败则停止继续自动循环（需要人工介入）

### v1.0.0 改进
1. **禁用名单可配�?* - 托盘“禁用名单”勾选节点，写入 `disabledNodes`，并覆盖 `excludeRegions`
2. **偏好节点** - 托盘“偏好节点”勾选节点，自动切换优先偏好节点（不可用则回退�?
3. **订阅级自动切换（Clash Verge Rev�?* - 连续自动切换节点仍不可用时，按白名单轮换订阅并强制重启客户端（默认关闭）
4. **统计口径调整** - UI 统计由检测次数改为“问题段落次数”（正常→异�?+1�?
5. **图标内置** - `build.ps1` 使用 `/win32icon`，窗�?托盘图标�?EXE 一�?

### v0.0.9 改进
1. **运行数据目录分离** - `config/log/monitor/diagnostics` 统一存放�?`%LOCALAPPDATA%\\ClashGuardian\\`，避免与源码/可执行混放（启动时自动尝试迁移旧文件�?
2. **编译产物分离** - 提供 `build.ps1`，默认输出到 `dist\\ClashGuardian.exe`

### v0.0.8 改进
1. **并发重启门闩** - `restartLock` + `_isRestarting` 原子化，避免重启流程并发
2. **配置兜底** - 配置数�?`TryParse + Clamp`，异常配置不再导致崩溃（不回�?config�?
3. **JSON 边界加固** - `FindObjectBounds` 忽略字符串内花括号，降低误判
4. **本地 API 直连** - loopback API 禁用系统代理，避�?PAC/全局代理干扰
5. **控制与诊断增�?* - 托盘支持暂停检测、导出诊断包、打开配置/数据/日志、黑名单管理

### v0.0.7 改进
1. **客户端路径持久化** - `detectedClientPath` 保存�?config.json，客户端关闭后仍可重�?
2. **注册表搜�?* - `FindClientFromRegistry` 遍历 HKLM/HKCU Uninstall 键发现安装路�?
3. **默认路径扩充** - 15+ 条路径覆�?Clash Verge Rev、Scoop、Program Files (x86) �?

### v0.0.6 改进
1. **重启死循环修�?* - 添加 `_isRestarting` 防并发，杀内核后分步检测恢�?
2. **冷却期修�?* - 使用 `COOLDOWN_COUNT` 常量�?�?�?25秒）替代硬编�?2 �?
3. **分步恢复** - 杀内核→等5秒→检查恢复→未恢复则重启客户端（智能降级�?

### v0.0.5 改进
1. **重启静默�?* - 只杀内核进程，客户端自动恢复，不再弹�?Clash GUI 窗口
2. **UI 线程安全** - 重启/切换/更新检查全部移至后台线程，UI 不再卡死
3. **快速恢�?* - 冷却期检测到内核+代理正常后立即结束，恢复时间 ~8s（旧�?~32s�?

### v0.0.4 改进
1. **自动更新** - 启动时静默检�?GitHub Release，代理优�?直连回退下载，NTFS 热替换，回滚保护
2. **partial class 拆分** - 单文件拆�?5 个模块文件，按职责分�?
3. **线程安全强化** - `volatile`/`Interlocked` 保护所有跨线程字段
4. **决策逻辑纯化** - `EvaluateStatus` 返回 `StatusDecision` 结构�?
5. **JSON 解析去重** - `FindObjectBounds`/`FindFieldValue` 统一入口
6. **节点排除可配�?* - `excludeRegions` �?config.json 加载
7. **�?catch 全部修复** - 15 处加日志�?8 处加注释
8. **魔法数字消除** - 30+ 个常量替代硬编码�?

### v0.0.3 修复
1. **节点切换 "proxy not exist"** - �?Selector 组的 `all` 数组正向获取节点列表
2. **硬编�?GLOBAL �?* - `FindSelectorGroup` 自动发现�?Selector �?
3. **节点名框框乱�?* - `SafeNodeName` 跳过 surrogate pair
4. **测速阻�?* - `TriggerDelayTest` 改为 `BeginGetResponse` 异步

### v0.0.2 修复
1. **重启�?UI 卡住** - `RestartClash` UI 操作需 `BeginInvoke`
2. **冷却期无响应** - 冷却期主动探�?
3. **节点名乱�?* - Unicode 转义解析

## 🛠�?常用开发命�?

```powershell
# 编译（推荐：�?icon�?
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 查看 Clash 相关进程
Get-Process | Where-Object {$_.ProcessName -like "*clash*" -or $_.ProcessName -like "*mihomo*"}

# 结束 ClashGuardian
Get-Process | Where-Object {$_.ProcessName -like "*ClashGuardian*"} | Stop-Process -Force
```

## v1.0.8 增量约束（新增）

1. 新增 `--watch-uu-route` �?UI watcher 模式，禁止创建额外架构文件，保持既有 partial class 架构（当�?8 个）�?
2. 新增主界面按钮与托盘菜单项：`UU 联动（Steam/PUBG）`，状态必须双向一致�?
3. 新增计划任务名：`ClashGuardianUURouteWatcher`，并兼容清理旧任�?`ClashGuardian.UUWatcher`�?
4. UU watcher 运行数据目录固定�?`%LOCALAPPDATA%\\ClashGuardian\\uu-watcher\\`（`state.json`/`watcher.log`/`heartbeat.json`）�?
5. 关闭 UU 联动时必须执行“先放行硬隔离、再回滚路由�?ProxyOverride”的可恢复策略�?
6. 主界面按钮区保持 `2�?x 3列`，并在下方区域视觉居中（允许小幅上移调优）�?7. 任何代码改动完成后，必须同步更新说明文档（至�?`README.md` �?`AGENTS.md`）�?8. UU 联动状态文案必须体现“配置状�?+ 运行健康”而非仅开关：`�?/ 开-运行�?/ 开-未运�?自愈�? / 开-需管理员`�?9. UU watcher 必须提供运行态自愈：当已启用�?`heartbeat` 过期/缺失时，�?15 秒周期尝试拉�?`--watch-uu-route`，日志需节流�?10. UU 严格管理员门槛：`uu.exe` 运行�?watcher 非管理员时，不允许进�?`UU_ACTIVE`；需记录 `ADMIN_REQUIRED_FOR_UU` 并走回退收敛�?11. `127.0.0.1:7897` 命中一律视为故障信号：记录 `LOCAL_7897_FAULT_SIGNAL`；Mihomo `chains` �?`Proxy` 记录 `PROXY_CHAIN_LEAK_DETECTED`�?12. 本策略版本不引入 UU_ACTIVE 下主守护自动动作抑制；`ClashGuardian.Monitor.cs` 自动重启/切换/应急触发顺序保持原逻辑�?13. UU 联动启用策略为“严格管理员任务模式”：只允�?`ClashGuardianUURouteWatcher`（`RL=HIGHEST`），禁止回退写入 `HKCU\\...\\Run` �?`ClashGuardianUURouteWatcher`�?14. 非管理员点击“开启UU联动”时，必须支持一�?`runas` 提权安装流程；拒�?UAC 时保持未启用并给出明确提示，不允许静默回退�?15. 允许新增内部维护参数：`--install-uu-route-task`、`--repair-uu-route-startup`；仅用于安装/修复管理员任务与清理旧残留�?16. UU 联动启用状态判定仅�?`ClashGuardianUURouteWatcher` 任务是否存在为准；RunKey 仅作历史残留清理，不参与“已启用”判断�?17. 关闭 UU 联动必须走“事务式关闭”：先删除新/旧任务并校验任务确实不存在，再发�?watcher 停止事件并记录“已关闭”�?18. 非管理员点击“关闭UU联动”时，也必须支持一�?`runas` 提权删除任务；拒�?UAC 时保持已启用并明确提示“未关闭”�?19. UU 自愈拉起触发条件为“任务存�?+ 心跳过期/缺失”；任务不存在视为正常关闭，不触发自愈日志�?20. Steam+PUBG 强接管策略：`UU ON` 进入阶段允许执行一次性清流（最�?30�? 一次补偿清流（最�?10），目标是尽快清�?`steam/steamwebhelper/tslgame -> 127.0.0.1:7897`�?21. 强接管失败仅告警不自动重�?Steam：若一次补偿后仍残�?`steam* -> 7897`，必须记�?`STEAM_UU_TAKEOVER_NOT_COMPLETE`，禁止引入自动重�?Steam 客户端�?22. 硬隔离失败必须可诊断：记�?`HARD_ISOLATION_APPLY_FAIL`，日志要包含失败命令、退出码�?stderr/stdout 摘要，成功应用后需收敛 `hardIsolationUnavailable=false`�?
## v1.0.8 Post-Match Guard Addendum

1. Add `postMatchGuard` transition policy for PUBG `���� -> ����`; default guard window is `90s`.
2. During guard window, suppress only automatic actions (`restart/switch/emergency/subscription-escalation`) when `matchFreezeAutoActions=true`.
3. During guard window, pin node for auto-switch path when `matchPinNodeEnabled=true`; manual actions remain available.
4. On guard enter, run exactly one compensation drain (`max=10`) when `steamTakeoverCompensateOnPostMatch=true`; do not auto-restart Steam.
5. If Steam residual to `127.0.0.1:7897` remains after compensation, emit `STEAM_7897_RESIDUAL_DURING_POST_MATCH`.
6. Keep CSV schema unchanged; new observability uses log events only (`POST_MATCH_GUARD_*`).
7. Mandatory doc sync rule (strict): every code/config/UI/behavior change must update both `README.md` and `AGENTS.md` in the same change set before build/delivery.

## v1.0.8 Env Restore Addendum

1. UU watcher must snapshot user-level proxy env (HTTP_PROXY/HTTPS_PROXY/NO_PROXY) on enter and persist to state.json under snapshot.env.
2. On every UU_ACTIVE -> NORMAL rollback path, restore env from snapshot.env; treat `snapshot.env` as empty when it is missing OR captured-but-all-empty (`http/https/no_proxy` all absent/blank), then fallback to current system proxy (http= -> https= -> single value) and write NO_PROXY=localhost,127.0.0.1.
3. If system proxy is disabled or unparseable during fallback, clear HTTP_PROXY/HTTPS_PROXY/NO_PROXY and emit warning logs; use ENV_RESTORE_FAILED only for write failures.
4. On watcher stop event, execute one forced converge pass (without switch gate) and emit STOP_FORCE_EXIT_BEGIN and STOP_FORCE_EXIT_DONE for diagnosable shutdown rollback; after env write, broadcast `WM_SETTINGCHANGE(Environment)` so newly started processes can observe updated user env.
5. Even when stop path resolves to `action=noop` (no rollback payload), still run one env convergence pass using the same restore/fallback pipeline so user env follows current system proxy state.

## Build Workflow Rule (2026-02-26)

1. After every code change, check whether ClashGuardian.exe is running before build.
2. If running, terminate all ClashGuardian* processes first, then compile.

## 文档编码编写规则（防乱码）

1. 所有 .md/.cs/.ps1/.json 文件统一使用 UTF-8；Windows 环境建议使用 UTF-8 with BOM，避免被旧工具误判为 ANSI/GBK。
2. 禁止用 ANSI/GBK 打开后直接保存 UTF-8 文件；出现 澶/锛/æ/� 这类乱码特征时，必须先从 Git 恢复再编辑。
3. 终端查阅中文内容时先切换 UTF-8 输出（chcp 65001 或 PowerShell 7+），优先区分“显示乱码”和“文件乱码”。
4. 文档提交前执行编码自检：Get-Content README.md -Encoding UTF8 -TotalCount 5 与 Get-Content AGENTS.md -Encoding UTF8 -TotalCount 5。
