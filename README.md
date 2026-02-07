# Clash Guardian Pro v1.0.0 - 多内核智能守护进程

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
- **正常状态**：5 秒检测间隔
- **异常状态**：1 秒快速检测（极速响应）
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
- **暂停自动操作** - 暂停自动重启/自动切换（仍持续检测与更新 UI）
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
| 连接泄漏 + 代理无响应 | CLOSE_WAIT > 20 | 立即重启 |
| 代理无响应 | 连续 2 次 | 切换节点（加入黑名单） |
| 代理无响应 | 连续 4 次 | 重启程序 |
| 延迟过高 | > 400ms 连续 2 次 | 切换节点 |
| 内存较高但代理正常 | > 70 MB | 仅记录，不重启 |

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

程序启动后显示主窗口，包含：
- **状态显示** - 当前运行状态（运行中/重启中/等待内核）
- **内核信息** - 检测到的内核名称、内存占用、句柄数
- **代理状态** - 代理连通性、延迟和当前节点名称
- **稳定性统计** - 稳定时长、运行时长、问题段落次数
- **统计信息** - 问题次数、重启次数、节点切换次数、黑名单数量

**按钮功能：**
- `立即重启` - 重启 Clash 内核（先杀内核，若不恢复则重启客户端）
- `查看数据` - 打开当日 CSV 监控数据
- `退出` - 完全退出程序
- `测速` - 手动测试代理延迟并更新状态栏（同时触发 Clash 全节点测速）
- `切换节点` - 手动切换到最佳节点（立即刷新统计）
- `开机自启` - 切换开机自启状态

### 系统托盘

最小化后程序进入系统托盘，右键菜单：
- 显示窗口
- 暂停自动操作 10/30/60分钟
- 恢复自动操作
- 立即重启
- 切换节点
- 触发测速
- 禁用名单（勾选即禁用）
- 偏好节点（勾选即偏好）
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

## 📝 开机自启

两种方式：

### 方式 1：程序内置（推荐）
点击界面上的 `开机自启` 按钮即可切换开机自启状态。

### 方式 2：手动设置
1. 按 `Win + R` 输入 `shell:startup` 打开启动文件夹
2. 创建 `ClashGuardian.exe` 的快捷方式放入该文件夹

## ⚠️ 注意事项

1. 确保 Clash 客户端的外部控制 API 已启用
2. 确保 API 密钥配置正确（托盘菜单“打开配置”修改 `%LOCALAPPDATA%\\ClashGuardian\\config\\config.json`）
3. 程序需要管理员权限来终止和启动进程
4. 代理测试端口需与 Clash 客户端设置一致

## 🔄 更新日志

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




