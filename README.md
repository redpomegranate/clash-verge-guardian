# Clash Guardian Pro v1.0.4 - 多内核智能守护进程

一个智能化的 Windows 系统托盘应用，用于自动监控和维护 Clash 系列代理客户端的稳定运行。

Icon based on Clash Verge, modified background by [Tao Zheng].

**支持多种客户端和内核：**
- Clash Verge / Clash Verge Rev (verge-mihomo)
- Mihomo Party (mihomo)
- Clash Nyanpasu (clash-rs / mihomo)
- Clash for Windows (clash / clash-win64)
- 原版 Clash Meta (clash-meta)

## ✨ 功能特性

### 🔍 智能监控
- **多内核支持** - 自动检测并适配不同 Clash 客户端和内核
- **进程监控** - 实时检测内核进程状态（自动识别进程名）
- **内存监控** - 监控内存占用，防止内存泄漏
- **延迟测量** - 不只测通断，还测量实际延迟
- **多目标测试** - 同时测试 Google、Cloudflare，避免误判
- **TCP 连接统计** - 监控 TIME_WAIT、ESTABLISHED、CLOSE_WAIT 连接数
- **API 自动发现** - 自动尝试常用端口（9097, 9090, 7890, 9898）
- **节点名称显示** - 正确解析 Unicode，过滤 emoji 乱码

### ⚡ 自适应检测
- **基础间隔**：`normalInterval`（默认 5 秒）/ `fastInterval`（默认 1 秒）
- **提速系数**：`speedFactor`（默认 3，范围 1..5），实际检测间隔 = 基础间隔 / `speedFactor`
- **默认效果**：健康态约 1.6 秒一次检测（>=3x 提速），异常态约 0.33 秒快速检测
- **连续稳定 3 次后**：自动恢复正常间隔

### 🧠 智能决策
- **纯函数决策引擎** - `EvaluateStatus` 返回结构化决策结果，逻辑与 UI 分离
- **智能代理组识别** - 自动发现实际节点所属的 Selector 组，不硬编码 GLOBAL
- **节点黑名单** - 失败节点 20 分钟内不再使用
- **延迟优化** - 延迟过高自动切换节点
- **综合判断** - 内存高但网络正常时不重启
- **定期测速** - 约每 6 分钟触发全节点延迟测试
- **禁用名单（可勾选）** - 托盘“禁用名单”勾选节点写入 `disabledNodes`；未配置时仍按 `excludeRegions` 关键字排除（默认港澳台）
- **偏好节点（可勾选）** - 托盘“偏好节点”会在自动切换时优先选择；偏好集合过小/不稳定时抗风险会下降（不可用则回退）

### ⚡ 自动恢复
- **订阅级自动切换（Clash Verge Rev）** - 连续自动切换节点仍不可用时，按白名单轮换订阅并强制重启客户端（默认关闭）

- **进程崩溃自动重启** - 检测到进程不存在时自动重启内核
- **分步恢复策略** - 杀内核→等待自动恢复→未恢复则重启客户端（智能降级）
- **防并发重启** - `_isRestarting` + `restartLock` 门闩，避免并发重启竞态
- **内存过高自动重启** - 内存超过 150MB 无条件重启
- **智能节点切换** - 代理无响应或延迟过高时自动切换到最优节点
- **快速恢复** - 重启后主动检测内核状态，一旦恢复立即切换到正常模式
- **静默运行** - 所有操作静音执行，无弹窗打扰

### 🧰 控制与诊断
- **暂停检测** - 暂停整个检测循环（不检测、不自动切换/重启，UI 进入“暂停检测”状态）
- **一键导出诊断包** - 自动脱敏 `clashSecret`，便于排障反馈
- **托盘工具箱** - 快速打开配置/监控数据/异常日志，管理黑名单

### 🔄 自动更新
- **静默检查** - 启动时后台检查 GitHub 最新版本
- **代理降级** - 优先通过代理下载，失败自动切换直连
- **热替换** - NTFS 文件热替换，无需手动关闭程序
- **回滚保护** - 更新失败自动恢复旧版本

### 🔎 客户端路径智能发现
- **进程探测** - 从运行中进程获取可执行文件路径（最准确）
- **路径持久化** - 检测到的路径保存到 `config.json`，下次启动直接读取
- **默认路径列表** - 覆盖 15+ 种常见安装方式
- **注册表搜索** - 从 HKLM/HKCU Uninstall 键自动发现安装路径（兜底）

### 📊 数据统计
- **问题段落次数** - UI 只统计“正常→异常”的次数，异常持续不重复累加
- **监控日志** - 记录关键事件到 `guardian.log`（仅记录异常）
- **详细数据** - 记录每次检测数据到按日期命名的 CSV 文件
- **自动清理** - 自动清理 7 天前的日志文件

## 🖥️ 系统要求

- Windows 10/11
- .NET Framework 4.5+
- 任一支持的 Clash 客户端已安装

## ⚙️ 配置

### 配置文件（推荐）

首次运行时会自动生成配置文件（默认路径）：

- `%LOCALAPPDATA%\\ClashGuardian\\config\\config.json`

也可以通过托盘菜单的 `打开配置` 快速定位。

```json
{
  "clashApi": "http://127.0.0.1:9097",
  "clashSecret": "set-your-secret",
  "proxyPort": 7897,
  "normalInterval": 5000,
  "fastInterval": 1000,
  "speedFactor": 3,
  "allowAutoStartClient": false,
  "memoryThreshold": 150,
  "highDelayThreshold": 400,
  "blacklistMinutes": 20,
  "coreProcessNames": ["verge-mihomo", "mihomo", "clash-meta", "clash-rs", "clash", "clash-win64"],
  "clientProcessNames": ["Clash Verge", "clash-verge", "Clash Nyanpasu", "mihomo-party", "Clash for Windows"],

  "excludeRegions": ["HK", "香港", "TW", "台湾", "MO", "澳门"],
  "disabledNodes": [],
  "preferredNodes": [],

  "autoSwitchSubscription": false,
  "subscriptionSwitchThreshold": 3,
  "subscriptionSwitchCooldownMinutes": 15,
  "subscriptionWhitelist": [],

  "clientPath": "C:\\Users\\...\\Clash Verge.exe"
}
```

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `clashApi` | Clash API 地址 | `http://127.0.0.1:9097` |
| `clashSecret` | API 密钥 | `set-your-secret` |
| `proxyPort` | 代理端口 | `7897` |
| `normalInterval` | 正常检测间隔(ms) | `5000` |
| `fastInterval` | 异常时快速检测间隔(ms) | `1000` |
| `speedFactor` | 检测提速系数（实际间隔=interval/speedFactor，范围 1..5） | `3` |
| `allowAutoStartClient` | 是否允许自动启动/重启 Clash 客户端（可能弹出 UI；默认关闭） | `false` |
| `memoryThreshold` | 内存阈值(MB) | `150` |
| `highDelayThreshold` | 高延迟阈值(ms) | `400` |
| `blacklistMinutes` | 黑名单时长(分钟) | `20` |
| `coreProcessNames` | 内核进程名列表（按优先级） | 见上方示例 |
| `clientProcessNames` | 客户端进程名列表 | 见上方示例 |
| `excludeRegions` | 节点排除关键词 | `HK,香港,TW,台湾,MO,澳门` |
| `clientPath` | 客户端可执行文件路径（自动检测并持久化） | 自动 |
| `disabledNodes` | 节点禁用显式名单（存在则优先，覆盖 `excludeRegions`） | 空数组 |
| `preferredNodes` | 偏好节点名单（自动切换优先；不可用则回退到其他节点） | 空数组 |
| `autoSwitchSubscription` | 订阅级自动切换（仅 Clash Verge Rev；默认关闭） | `false` |
| `subscriptionSwitchThreshold` | 连续自动切换节点仍不可用时触发阈值 | `3` |
| `subscriptionSwitchCooldownMinutes` | 订阅切换冷却期（分钟） | `15` |
| `subscriptionWhitelist` | 订阅白名单（profile name 或 uid；至少 2 条才会切换） | `[]` |

**重要**：请将 `clashSecret` 修改为你的 Clash 客户端中设置的 API 密钥。

### 触发条件

| 条件 | 阈值 | 动作 |
|------|------|------|
| 进程不存在 | - | 立即重启 |
| 内存极高 | > 150 MB | 无条件重启 |
| 内存较高 + 代理无响应 | > 70 MB | 立即重启 |
| 内存较高 + 延迟过高 | > 70 MB 且延迟 > 400ms | 快速恢复：重置内核(最多2次)→刷新测速→切节点；无效则升级重启客户端；仍无效可切订阅（Clash Verge Rev，可选） |
| 连接泄漏 + 代理无响应 | CLOSE_WAIT > 20 | 立即重启 |
| 代理无响应 | 连续 2 次 | 切换节点（加入黑名单） |
| 代理无响应 | 连续 4 次 | 重启程序 |
| 延迟过高 | > 400ms 连续 2 次 | 切换节点 |
| 内存较高但代理正常 | > 70 MB 且延迟正常 | 仅记录，不重启 |

## 🚀 使用方法

### 下载运行

从 [GitHub Releases](https://github.com/redpomegranate/clash-verge-guardian/releases) 下载最新的 `ClashGuardian.exe`，直接运行即可。

### 从源码编译

```powershell
# 推荐：一键编译（含 icon）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 或手动编译（需指定 win32 icon）
mkdir dist -Force | Out-Null
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:assets\\ClashGuardian.ico /out:dist\\ClashGuardian.exe ClashGuardian.cs ClashGuardian.UI.cs ClashGuardian.Network.cs ClashGuardian.Monitor.cs ClashGuardian.Update.cs
```

编译产物输出到 `dist\\ClashGuardian.exe`。

### 界面操作

程序正常启动后显示主窗口；由 Watcher 拉起（`--follow-clash`）时默认不弹出主窗口，仅显示托盘（可从托盘菜单“显示窗口”打开）。

主窗口包含：
- **状态显示** - 当前运行状态（运行中/重启中/等待内核）
- **内核信息** - 检测到的内核名称、内存占用、句柄数
- **代理状态** - 代理连通性、延迟和当前节点名称
- **稳定性统计** - 稳定时长、运行时长、问题段落次数
- **统计信息** - 问题次数、重启次数、节点切换次数、黑名单数量

**按钮功能：**
- `立即重启` - 重启 Clash 内核（默认不自动启动/重启客户端；如需允许请设置 `allowAutoStartClient=true`）
- `暂停检测/恢复检测` - 暂停/恢复整个检测循环
- `退出` - 完全退出程序
- `测速` - 手动测试代理延迟并更新状态栏（同时触发 Clash 全节点测速）
- `切换节点` - 手动切换到最佳节点（立即刷新统计）
- `跟随 Clash` - 启用/关闭“跟随 Clash 启动”（登录后后台 Watcher 检测到 Clash 启动会拉起 Guardian）

### 跟随 Clash 启动/退出（推荐）

启用后会在登录时启动一个轻量 Watcher：当检测到 Clash 客户端进程启动后，会自动拉起 ClashGuardian；当 Clash 全部退出后，ClashGuardian 也会自动退出（Watcher 继续等待下一次启动）。

**注意**：Watcher 只负责拉起 ClashGuardian，本身不会启动/重启 Clash 客户端。

命令行参数：
- `--watch-clash`：Watcher 模式（无 UI/托盘）
- `--follow-clash`：跟随模式（有托盘，默认不弹主窗口；Clash 退出后自动退出）

### 系统托盘

最小化后程序进入系统托盘，右键菜单：
- 显示窗口
- 暂停检测 / 恢复检测
- 立即重启
- 切换节点
- 触发测速
- 禁用名单（固定高度 + 可滚动勾选）
- 偏好节点（固定高度 + 可滚动勾选）
- 导出诊断包
- 打开配置 / 查看监控数据 / 查看异常日志
- 检查更新
- 清空黑名单 / 移除当前节点黑名单
- 退出

## 📁 项目结构

```
ClashGuardian\
├── ClashGuardian.cs
├── ClashGuardian.UI.cs
├── ClashGuardian.Network.cs
├── ClashGuardian.Monitor.cs
├── ClashGuardian.Update.cs
├── assets\
│   ├── icon-source.png
│   └── ClashGuardian.ico
├── build.ps1
├── dist\                      # 编译产物输出目录（本地生成，不提交）
├── README.md
└── AGENTS.md
```

**运行时文件不会写入 exe 所在目录**，默认存放在：`%LOCALAPPDATA%\\ClashGuardian\\`

- `config\\config.json` - 配置文件
- `logs\\guardian.log` - 异常日志（仅异常）
- `monitor\\monitor_YYYYMMDD.csv` - 监控数据
- `diagnostics\\diagnostics_YYYYMMDD_HHmmss\\` - 诊断包导出目录

### CSV 数据格式

```csv
Time,ProxyOK,Delay,MemMB,Handles,TimeWait,Established,CloseWait,Node,Event
```

### 事件类型

| 事件 | 说明 |
|------|------|
| `ProcessDown` | 进程不存在 |
| `CriticalMemory` | 内存极高 (>150MB) |
| `HighMemoryNoProxy` | 内存高+代理无响应 |
| `HighMemoryHighDelay` | 内存较高 + 延迟过高（快速恢复管线） |
| `CloseWaitLeak` | 连接泄漏 |
| `ProxyFail` | 代理无响应 |
| `NodeSwitch` | 触发节点切换 |
| `ProxyTimeout` | 代理超时，触发重启 |
| `HighDelay` | 延迟过高 |
| `HighDelaySwitch` | 延迟过高，触发切换 |
| `HighMemoryOK` | 内存高但代理正常 |

## 🔧 节点切换策略

自动识别代理组结构：
1. 从 GLOBAL 的子组中找到实际的 Selector 组
2. 从该组的 `all` 列表获取所有可用节点
3. 按延迟排序，选择最优节点切换

自动排除以下节点：
- **可配置的地区节点**（默认排除港澳台，通过 `excludeRegions` 自定义）
- 系统内置节点（DIRECT、REJECT、GLOBAL）
- 特殊分组（自动选择、故障转移、负载均衡）
- **黑名单节点**（最近 20 分钟内失败的节点）

## 📝 跟随 Clash 启动/退出

启用后会在登录时启动一个轻量 Watcher（无 UI/托盘）：
- 检测到 Clash 客户端启动：自动拉起 ClashGuardian（`--follow-clash`）
- 检测到 Clash 全部退出：ClashGuardian 自动退出（Watcher 继续等待下一次启动）

### 方式 1：程序内置（推荐）
点击主界面 `跟随 Clash` 按钮即可启用/关闭（优先使用计划任务，失败回退注册表 Run）。

### 方式 2：手动设置
1. 按 `Win + R` 输入 `shell:startup` 打开启动文件夹
2. 创建 `ClashGuardian.exe --watch-clash` 的快捷方式放入该文件夹

## ⚠️ 注意事项

1. 确保 Clash 客户端的外部控制 API 已启用
2. 确保 API 密钥配置正确（托盘菜单“打开配置”修改 `%LOCALAPPDATA%\\ClashGuardian\\config\\config.json`）
3. 程序需要管理员权限来终止和启动进程
4. 代理测试端口需与 Clash 客户端设置一致

## 🧭 自动恢复流程（流程图）

```mermaid
flowchart TD
  A[Timer CheckStatus] --> B[DoCheckInBackground]
  B --> C[EvaluateStatus -> StatusDecision]

  C -->|NeedSwitch| SW[切换节点]
  C -->|NeedRestart| RS[重启管线 RestartClash]
  C -->|No action| UI[仅更新UI/统计]

  SW --> SWR{切换结果?}
  SWR -->|成功| OK
  SWR -->|失败: 无可用低延迟节点/延迟超时| NOGOOD[连续失败x3(节流)<br/>尝试订阅切换/重启客户端]
  NOGOOD --> CR
  NOGOOD --> SUB2
 
  RS --> HM{HighMemoryHighDelay?}
  HM -->|是| HMPIPE[快速内核重置 x2<br/>每次: 刷新测速 -> 切最佳节点 -> 验证代理+延迟]
  HMPIPE -->|恢复| OK[恢复正常]
  HMPIPE -->|仍失败| UP[升级: 重启客户端]

  HM -->|否| NORMAL[常规: 杀内核 -> 等待自动恢复(<=8s) -> 验证代理]
  NORMAL -->|恢复| OK
  NORMAL -->|代理未恢复| UP

  UP --> AUTO{allowAutoStartClient=true?}
  AUTO -->|否| STOP[停止自动操作<br/>提示手动介入]

  AUTO -->|是| CR[强制重启客户端(含后台进程)]
  CR --> READY[等待内核+API就绪]
  READY --> CHECK{代理/延迟恢复?}
  CHECK -->|恢复| OK
  CHECK -->|仍失败| TRY2[刷新测速并切节点(最多2次)]
  TRY2 -->|恢复| OK
  TRY2 -->|仍失败| SUB{可切换订阅? <br/>(autoSwitchSubscription & whitelist>=2 & cooldown ok/紧急绕过)}
  SUB -->|否| STOP
  SUB -->|是| SUB2[切换订阅 -> 强制重启客户端]
  SUB2 --> TRY2B[订阅切换后再刷新/切节点(2次)]
  TRY2B -->|恢复| OK
  TRY2B -->|仍失败| STOP
```

补充说明：
- 当进入 `STOP`（需要手动介入）后，Guardian 会**停止继续自动重启/自动切换/自动订阅切换**，避免无限循环干扰；当代理恢复正常后会自动退出该状态。
- 节点切换在 delay history 不可用时，会回退到 `/proxies/{name}/delay` 的实时探测后再切换，避免“请先测速”的死循环。

## 🔄 更新日志

### v1.0.4 (2026-02-09)
- **优化：自动切换失败风暴保护** - 当出现“延迟过高 5000ms / 无 delay 历史 / API无响应”等导致的切换失败时：自动节流日志、限制切换频率，并在连续失败达到阈值后升级为“订阅切换/重启客户端”，避免无限循环刷屏
- **提速：恢复链路缩短** - 客户端重启后的“内核+API 就绪等待”合并为单循环并提前触发 `AutoDiscoverApi`；代理恢复检测前 3 秒改为 500ms 轮询；常规 core 重启后的代理验证窗口缩短为 ~4.5s，失败尽快升级为重启客户端
- **增强：订阅切换紧急绕过** - 在“客户端重启 + 节点切换仍无效”的恢复阶段，订阅切换允许在严重故障场景下绕过 cooldown（仍有最小间隔保护），并在客户端重启后立即刷新测速+切最佳节点，提高命中可用节点概率

### v1.0.3 (2026-02-09)
- **修复：mihomo/meta 延迟测试接口不兼容** - `TriggerDelayTest` 改为 `/proxies/{name}/delay`，避免 `/group/{name}/delay` 404 导致“请先测速”死循环（影响自动切节点与恢复链路）
- **增强：无 delay 历史时的实时探测** - 自动切节点在 delay history 不可用时，会对候选节点做实时 delay probe（有限并发、轮转覆盖）后再切换，避免重启后“无可用节点”
- **优化：客户端重启后恢复链路** - 客户端重启后若仍不恢复：立即刷新低延迟节点并尝试切换（最多 2 次）；仍无效且启用 `autoSwitchSubscription` 时切换订阅并强制重启客户端；订阅切换后再次刷新并切换 2 次；再失败则停止继续自动循环（需要人工介入）

### v1.0.2 (2026-02-08)
- **修复：`--watch-clash` 稳定性** - 进程名变体匹配、启用后立即生效、禁用后可停止 Watcher
- **变更：默认不干涉用户手动退出 Clash** - 客户端不在时仅显示“等待 Clash...”，不触发自动切换/重启策略
- **新增：静音开关** - `allowAutoStartClient`（默认 `false`），禁止自动启动/重启客户端（避免弹出 UI 干扰）
- **优化：`--follow-clash` 静默启动** - 默认只显示托盘，不弹出主窗口

### v1.0.1 (2026-02-08)
- **新增：跟随 Clash 启动/退出** - 登录后 Watcher 监测到 Clash 客户端启动会自动拉起 Guardian；Clash 全部退出后 Guardian 自动退出
- **变更：暂停检测** - 原“暂停自动操作”改为暂停整个检测循环（Timer 停止），恢复后重置计数避免误触发
- **新增：检测提速** - `speedFactor`（默认 3），健康态检测频率提升 >=3x；周期任务改为按时间节流
- **修复：节点显示与刷新** - 切换后测速显示回弹、手动切换不更新、节点名前 emoji 乱码（方块）等
- **增强：应急机制** - 修复“进程恢复但网络未恢复”的误判，并在冷却期代理持续异常时触发激进自查与恢复策略（节流）

### v1.0.0 (2026-02-07)
- **新增：禁用名单（节点级）可配置** - 托盘勾选写入 `disabledNodes`；未配置则按 `excludeRegions` 关键字排除（默认港澳台）
- **新增：订阅级自动切换（Clash Verge Rev）** - 连续自动切换节点仍不可用时，按白名单轮换订阅并强制重启客户端（默认关闭）
- **优化：图标统一** - `build.ps1` 编译产物内置 icon，窗口/托盘图标与 EXE 一致
- **调整：统计口径** - UI 统计改为“问题段落次数”（正常→异常 +1）

### v0.0.9 (2026-02-07)
- **优化：运行数据目录分离** - `config/log/monitor/diagnostics` 统一迁移到 `%LOCALAPPDATA%\\ClashGuardian\\`，避免与源码/可执行混放（启动时自动尝试迁移旧文件）
- **优化：编译产物分离** - 提供 `build.ps1`，默认输出到 `dist\\ClashGuardian.exe`

### v0.0.8 (2026-02-07)
- **修复：并发重启竞态** - `restartLock` + `_isRestarting` 原子化门闩，避免重启流程并发
- **加固：配置兜底** - 配置数值 `TryParse + Clamp`，避免异常配置导致崩溃（不回写 config）
- **加固：JSON 边界扫描** - `FindObjectBounds` 忽略字符串内花括号，降低误判
- **加固：本地 API 直连** - loopback API 请求禁用系统代理，避免 PAC/全局代理干扰
- **优化：更新资源释放** - `WebClient` 用 `using` 自动释放
- **新增：暂停自动操作** - 暂停自动重启/自动切换（仍检测仍更新 UI）
- **新增：诊断包导出** - 一键导出 summary+脱敏配置+日志+监控数据
- **新增：托盘工具** - 打开配置/监控数据/异常日志，黑名单清理与移除

### v0.0.7 (2026-02-06)
- **修复：客户端路径丢失** - 持久化 `clientPath` 到 config.json，关闭客户端后仍可正确重启
- **新增：注册表搜索** - 从 HKLM/HKCU Uninstall 键自动发现客户端安装路径
- **新增：默认路径扩充** - 覆盖 Clash Verge Rev、Scoop、Program Files (x86) 等 15+ 种安装方式
- **优化：路径搜索优先级** - 运行进程 → config 持久化 → 默认路径 → 注册表（逐级兜底）

### v0.0.6 (2026-02-06)
- **修复：重启死循环** - 手动重启后内核无法恢复，导致无限"进程不存在"循环
- **修复：重启竞态条件** - 添加 `_isRestarting` 标志，阻止重启期间 CheckStatus 并发执行
- **修复：冷却期过短** - 使用 `COOLDOWN_COUNT` 常量（5次 ≈ 25秒）替代硬编码的 2 次
- **优化：分步恢复** - 杀内核 → 等 5 秒 → 检查自动恢复 → 未恢复则重启客户端

### v0.0.5 (2026-02-06)
- **修复：重启弹窗** - 只终止内核进程，客户端自动重启内核，不再弹出 Clash GUI 窗口
- **修复：UI 线程阻塞** - 重启/切换/更新检查移至后台线程，UI 不再卡死
- **优化：快速恢复** - 重启后一旦检测到内核恢复+代理正常，立即切回正常模式
- **优化：代码架构** - 拆分为 5 个 partial class 文件（Main/UI/Network/Monitor/Update）
- **优化：线程安全** - `volatile`/`Interlocked` 保护所有跨线程字段
- **优化：决策纯化** - `EvaluateStatus` 返回 `StatusDecision` 结构体
- **新增：自动更新** - 启动时静默检查 GitHub Release，NTFS 热替换，回滚保护
- **新增：节点排除可配置** - `excludeRegions` 从 config.json 加载
- **修复：空 catch / 魔法数字** - 44 个 catch 块修复，30+ 常量替代

### v0.0.3 (2026-02-06)
- 修复节点切换失败（自动发现 Selector 组）
- 修复节点名 emoji 乱码
- 优化检测频率和测速响应

### v0.0.2 (2026-02-04)
- 多内核支持
- 自适应检测间隔
- 节点黑名单机制
- 配置文件支持

## 📄 License

MIT License
