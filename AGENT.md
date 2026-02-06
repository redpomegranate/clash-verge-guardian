# AGENT.md - AI 行动清单

> 本文档供后续 AI 快速了解项目并执行开发流程

## 📋 项目概述

- **项目名称**：Clash Guardian Pro
- **功能**：多 Clash 客户端的智能守护进程（支持 Clash Verge、Mihomo Party、Clash Nyanpasu 等）
- **语言**：C# (.NET Framework 4.5+)
- **平台**：Windows 10/11
- **GitHub**：https://github.com/redpomegranate/clash-verge-guardian

## 📁 项目结构

```
F:\Clash verge守护进程\
├── ClashGuardian.cs       # 主源代码（唯一源文件）
├── ClashGuardian.exe      # 编译后的可执行文件
├── config.json            # 配置文件（首次运行自动生成）
├── guardian.log           # 运行日志
├── monitor_YYYYMMDD.csv   # 每日监控数据
├── README.md              # 项目说明文档
└── AGENT.md               # 本文件
```

## ⚠️ 重要注意事项

1. **先测试，后推送** - 任何代码修改必须先本地编译测试通过，再推送 GitHub
2. **静默运行** - 所有自动操作（重启、切换节点）不要有弹窗/通知
3. **路径编码问题** - 项目路径包含中文，PowerShell 直接 cd 会失败，需特殊处理

## 🏗️ 代码架构

### 代码组织结构（ClashGuardian.cs）

```
1. 配置常量区（const）
   - 默认检测间隔、阈值等
   - UI 颜色常量
   
2. 运行时配置（可从 config.json 加载）
   - API 地址、密钥
   - 各种阈值设置

3. UI 组件声明

4. 运行时状态变量

5. 构造函数
   - 初始化配置
   - 初始化 UI
   - 启动定时器

6. 功能模块（按区域组织）
   - 配置管理：LoadConfig, SaveDefaultConfig, GetJsonValue
   - UI 初始化：InitializeUI, CreateButton, CreateInfoLabel, CreateSeparator
   - 托盘图标：InitializeTrayIcon
   - 开机自启：ToggleAutoStart, IsAutoStartEnabled
   - 日志管理：CleanOldLogs, Log, LogData
   - API 通信：ApiGet, ApiPut
   - 工具函数：CleanString, FormatTimeSpan
   - 节点管理：GetCurrentNode, TriggerDelayTest, CleanBlacklist, SwitchToBestNode
   - 代理测试：TestProxyWithDelay
   - 系统监控：GetTcpStats, GetMihomoStats
   - 重启管理：RestartClash, AdjustInterval
   - 主检测循环：CheckStatus
```

### 配置常量

```csharp
// 代码顶部的配置常量
private const int DEFAULT_NORMAL_INTERVAL = 10000;    // 正常检测间隔：10秒
private const int DEFAULT_FAST_INTERVAL = 3000;       // 异常时快速检测：3秒
private const int DEFAULT_MEMORY_THRESHOLD = 150;     // 内存阈值 (MB)
private const int DEFAULT_MEMORY_WARNING = 70;        // 内存警告阈值 (MB)
private const int DEFAULT_HIGH_DELAY = 3000;          // 高延迟阈值 (ms)
private const int DEFAULT_BLACKLIST_MINUTES = 20;     // 黑名单时长（分钟）
private const int DEFAULT_PROXY_PORT = 7897;          // 代理端口
private const int DEFAULT_API_PORT = 9097;            // API 端口
private const int TCP_CHECK_INTERVAL = 5;             // TCP 统计检测间隔
```

### config.json 配置文件

```json
{
  "clashApi": "http://127.0.0.1:9097",
  "clashSecret": "set-your-secret",
  "proxyPort": 7897,
  "normalInterval": 10000,
  "memoryThreshold": 150,
  "highDelayThreshold": 3000,
  "blacklistMinutes": 20,
  "coreProcessNames": ["verge-mihomo", "mihomo", "clash-meta", "clash-rs", "clash", "clash-win64"],
  "clientProcessNames": ["Clash Verge", "clash-verge", "Clash Nyanpasu", "mihomo-party", "Clash for Windows"]
}
```

### 多内核支持

程序自动检测运行中的内核进程，支持的内核列表可在 config.json 中自定义：
- `coreProcessNames` - 内核进程名（按优先级排序）
- `clientProcessNames` - 客户端进程名

**自动检测机制**：启动时按顺序扫描内核列表，找到第一个运行中的进程作为监控目标。

## 🔧 开发流程

### 1. 修改代码

直接编辑 `ClashGuardian.cs` 文件。

### 2. 编译

**步骤 1**：关闭正在运行的程序
```powershell
Get-Process | Where-Object {$_.ProcessName -like "*ClashGuardian*"} | Stop-Process -Force -ErrorAction SilentlyContinue
```

**步骤 2**：编译（处理中文路径）
```powershell
$dir = (Get-ChildItem F:\ | Where-Object {$_.Name -like "Clash*"}).FullName; cd $dir; C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:ClashGuardian.exe ClashGuardian.cs
```

**编译成功标志**：无 error 输出（warning 可忽略）

### 3. 本地测试

运行 `ClashGuardian.exe`，检查：
- [ ] 界面正常显示
- [ ] 内存、延迟、节点信息正确
- [ ] 代理状态检测正常
- [ ] 按钮功能正常（测速、切换节点、开机自启）
- [ ] 托盘图标和菜单正常
- [ ] 最小化到托盘正常
- [ ] config.json 正确生成

### 4. 推送到 GitHub

**步骤 1**：添加并提交
```powershell
cd "F:\Clash*"; git add .; git commit -m "你的提交信息（英文）"
```

**步骤 2**：推送
```powershell
cd "F:\Clash*"; git push origin main
```

**注意**：commit 信息使用英文，避免乱码

## 🛠️ 常用命令速查

### 路径处理
```powershell
# 方法1：通配符
cd "F:\Clash*"

# 方法2：变量（更可靠）
$dir = (Get-ChildItem F:\ | Where-Object {$_.Name -like "Clash*"}).FullName
cd $dir
```

### Git 操作
```powershell
# 查看状态
cd "F:\Clash*"; git status

# 查看日志
cd "F:\Clash*"; git log --oneline -5

# 强制推送（谨慎使用）
cd "F:\Clash*"; git push --force origin main
```

### 进程管理
```powershell
# 查看 Clash 相关进程
Get-Process | Where-Object {$_.ProcessName -like "*clash*" -or $_.ProcessName -like "*mihomo*"}

# 结束 ClashGuardian
Get-Process | Where-Object {$_.ProcessName -like "*ClashGuardian*"} | Stop-Process -Force
```

## 📊 核心功能逻辑

### 检测间隔（自适应）
- 正常：10 秒
- 异常：3 秒
- 连续 3 次正常后恢复 10 秒

### 重启触发条件
| 条件 | 动作 |
|------|------|
| 进程不存在 | 重启 |
| 内存 > 150MB | 无条件重启 |
| 内存 > 70MB + 代理异常 | 重启 |
| CLOSE_WAIT > 20 + 代理异常 | 重启 |
| 代理连续 4 次无响应 | 重启 |

### 切换节点触发条件
| 条件 | 动作 |
|------|------|
| 代理连续 2 次无响应 | 切换（当前节点加入黑名单） |
| 延迟 > 3000ms 连续 2 次 | 切换 |

### 节点黑名单
- 切换时自动将原节点加入黑名单
- 黑名单有效期：20 分钟
- 黑名单内节点不会被选中

## 🚀 性能优化点

| 优化项 | 说明 |
|--------|------|
| 配置常量 | 所有配置集中在代码顶部 |
| 按钮工厂 | `CreateButton()` 消除重复代码 |
| 进程查找 | `GetProcessesByName` 替代遍历所有进程 |
| TCP 缓存 | 每 5 次检测才执行 netstat |
| 日志优化 | 空事件不写入 CSV |
| 配置文件 | 支持 config.json 外部配置 |

## 🐛 常见问题

### Q: PowerShell 中文路径报错
**A**: 使用通配符 `cd "F:\Clash*"` 或变量方式处理

### Q: 编译报错 "无法写入文件"
**A**: 先关闭正在运行的 ClashGuardian.exe 进程

### Q: Git commit 出现乱码
**A**: commit 信息使用纯英文

### Q: 代理测试一直失败
**A**: 检查 config.json 中的 proxyPort 和 clashApi 配置

### Q: 开机自启不生效
**A**: 确保程序以管理员权限运行（需要写注册表）

## 📝 修改历史

| 日期 | 修改内容 |
|------|---------|
| 2026-02-04 | 初始版本 |
| 2026-02-04 | 检测间隔 2s → 8s |
| 2026-02-04 | 优化重启逻辑：内存高但网络正常不重启 |
| 2026-02-04 | 升级 Pro 版：自适应间隔、延迟测量、节点黑名单、多目标测试 |
| 2026-02-04 | 代码重构：配置常量、按钮工厂、性能优化、配置文件支持、开机自启 |
| 2026-02-04 | **多内核支持**：支持 Clash Verge、Mihomo Party、Clash Nyanpasu、CFW 等多客户端 |

## 🎯 后续优化方向

- [ ] 支持多代理组切换
- [ ] 添加速度测试功能
- [ ] 支持自定义测试 URL
- [ ] 添加流量统计
- [ ] 支持订阅自动更新
- [x] ~~多内核支持~~ ✅
