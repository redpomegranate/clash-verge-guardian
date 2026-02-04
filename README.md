# Clash Guardian Pro - Clash Verge 智能守护进程

一个智能化的 Windows 系统托盘应用，用于自动监控和维护 Clash Verge 代理客户端的稳定运行。

## ✨ 功能特性

### 🔍 智能监控
- **进程监控** - 实时检测 mihomo 核心进程状态
- **内存监控** - 监控内存占用，防止内存泄漏
- **延迟测量** - 不只测通断，还测量实际延迟
- **多目标测试** - 同时测试 Google、Cloudflare，避免误判
- **TCP 连接统计** - 监控 TIME_WAIT、ESTABLISHED、CLOSE_WAIT 连接数

### ⚡ 自适应检测
- **正常状态**：10 秒检测间隔（省资源）
- **异常状态**：3 秒快速检测（快速响应）
- **连续稳定 3 次后**：自动恢复正常间隔

### 🧠 智能决策
- **节点黑名单** - 失败节点 5 分钟内不再使用
- **延迟优化** - 延迟 > 3000ms 自动切换节点
- **综合判断** - 内存高但网络正常时不重启
- **定期测速** - 每 6-7 分钟触发全节点延迟测试

### ⚡ 自动恢复
- **进程崩溃自动重启** - 检测到进程不存在时自动启动 Clash Verge
- **内存过高自动重启** - 内存超过 150MB 无条件重启
- **智能节点切换** - 代理无响应或延迟过高时自动切换
- **静默运行** - 所有操作静音执行，无弹窗打扰

### 📊 数据统计
- **稳定性统计** - 显示连续稳定时长、运行时长、成功率
- **监控日志** - 记录所有事件到 `guardian.log`
- **详细数据** - 记录每次检测数据到按日期命名的 CSV 文件
- **自动清理** - 自动清理 7 天前的日志文件

## 🖥️ 系统要求

- Windows 10/11
- .NET Framework 4.5+
- Clash Verge 已安装（默认路径：`%LocalAppData%\Programs\clash-verge\`）

## ⚙️ 配置

在 `ClashGuardian.cs` 中可以修改以下配置：

```csharp
private string clashApi = "http://127.0.0.1:9097";    // Clash API 地址
private string clashSecret = "set-your-secret";       // API 密钥
private int normalInterval = 10000;   // 正常检测间隔：10秒
private int fastInterval = 3000;      // 异常时快速检测：3秒
private int blacklistMinutes = 5;     // 节点黑名单时长（分钟）
private int highDelayThreshold = 3000; // 高延迟阈值（毫秒）
```

**重要**：请将 `clashSecret` 修改为你的 Clash Verge 中设置的 API 密钥。

### 触发条件

| 条件 | 阈值 | 动作 |
|------|------|------|
| 进程不存在 | - | 立即重启 |
| 内存极高 | > 150 MB | 无条件重启 |
| 内存较高 + 代理无响应 | > 70 MB | 立即重启 |
| 连接泄漏 + 代理无响应 | CLOSE_WAIT > 20 | 立即重启 |
| 代理无响应 | 连续 2 次 | 切换节点（加入黑名单） |
| 代理无响应 | 连续 4 次 | 重启程序 |
| 延迟过高 | > 3000ms 连续 2 次 | 切换节点 |
| 内存较高但代理正常 | > 70 MB | 仅记录，不重启 |

## 🚀 使用方法

### 编译运行

```bash
# 使用 csc 编译
csc /target:winexe /out:ClashGuardian.exe ClashGuardian.cs

# 或直接运行已编译的可执行文件
ClashGuardian.exe
```

### 界面操作

程序启动后显示主窗口，包含：
- **状态显示** - 当前运行状态
- **内存信息** - 内存占用、句柄数、TIME_WAIT 连接数
- **代理状态** - 代理连通性、延迟和当前节点名称
- **稳定性统计** - 稳定时长、运行时长、成功率
- **统计信息** - 检测次数、重启次数、节点切换次数、黑名单数量

**按钮功能：**
- `立即重启` - 手动重启 Clash Verge
- `查看日志` - 打开当日 CSV 监控日志
- `退出` - 完全退出程序
- `测速` - 手动触发全节点延迟测试
- `切换节点` - 手动切换到最佳节点
- `清除黑名单` - 清空节点黑名单

### 系统托盘

最小化后程序进入系统托盘，右键菜单：
- 显示窗口
- 立即重启
- 切换节点
- 触发测速
- 查看日志
- 退出

## 📁 文件说明

```
├── ClashGuardian.cs       # 源代码
├── ClashGuardian.exe      # 可执行文件
├── guardian.log           # 事件日志
├── monitor_YYYYMMDD.csv   # 每日监控数据
└── README.md              # 本文档
```

### CSV 数据格式

```csv
Time,ProxyOK,Delay,MemMB,Handles,TimeWait,Established,CloseWait,Node,Event
```

| 字段 | 说明 |
|------|------|
| Time | 检测时间 |
| ProxyOK | 代理状态 (OK/FAIL) |
| Delay | 延迟 (ms) |
| MemMB | 内存占用 (MB) |
| Handles | 句柄数 |
| TimeWait | TIME_WAIT 连接数 |
| Established | ESTABLISHED 连接数 |
| CloseWait | CLOSE_WAIT 连接数 |
| Node | 当前节点名称 |
| Event | 事件类型 |

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

智能节点切换会自动排除以下节点：
- 港澳台节点（HK、TW、香港、台湾）
- 系统内置节点（DIRECT、REJECT、GLOBAL）
- 特殊分组（自动选择、故障转移、负载均衡）
- **黑名单节点**（最近 5 分钟内失败的节点）

支持的代理协议：`ss`、`vmess`、`trojan`、`vless`、`hysteria`、`hysteria2`

## 📝 开机自启（可选）

1. 按 `Win + R` 输入 `shell:startup` 打开启动文件夹
2. 创建 `ClashGuardian.exe` 的快捷方式放入该文件夹

## ⚠️ 注意事项

1. 确保 Clash Verge 的外部控制 API 已启用
2. 确保 API 密钥配置正确
3. 程序需要管理员权限来终止和启动进程
4. 代理测试使用 7897 端口，请确保与 Clash Verge 设置一致

## 📄 License

MIT License
