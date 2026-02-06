# Changelog

## [v0.0.2] - 2026-02-04 (测试版)

### 新增功能

#### 多内核/多客户端支持
- **自动检测内核进程**：支持 `verge-mihomo`、`mihomo`、`clash-meta`、`clash-rs`、`clash`、`clash-win64`
- **自动检测客户端**：支持 Clash Verge、Mihomo Party、Clash Nyanpasu、Clash for Windows
- **API 端口自动发现**：自动尝试 9097、9090、7890、9898 端口
- **配置文件扩展**：`config.json` 新增 `coreProcessNames` 和 `clientProcessNames` 配置项
- **多路径重启**：重启时自动查找可用的客户端安装路径
- **UI 显示内核名**：界面和托盘显示当前检测到的内核进程名

#### 性能诊断日志
- 新增 `LogPerf()` 函数，记录超过 100ms 的操作耗时
- 启动各阶段耗时记录：LoadConfig、InitializeUI、TotalInit
- CheckStatus 各步骤耗时记录：GetMihomoStats、TestProxy、GetTcpStats
- API 发现耗时记录

### 性能优化

#### 后台线程检测模式（彻底解决 UI 卡顿）
- **架构重构**：将检测逻辑从 UI 线程移到后台线程
- **新增 `isChecking` 标志**：防止检测任务堆积
- **新增 `DoCheckInBackground()`**：在后台线程执行所有耗时操作
- **新增 `UpdateUI()`**：通过 `BeginInvoke` 安全回到 UI 线程更新界面
- **重启/切换异步化**：避免阻塞 UI

#### 超时优化
- `AutoDiscoverApi` 超时：2000ms → 500ms
- `TestProxyWithDelay` 超时：5000ms → 3000ms
- 首次检测延迟：500ms 后开始，让界面先显示

### 改进

- 移除构造函数中的同步 `CheckStatus()` 调用
- `AutoDiscoverApi` 改为后台线程执行
- 界面始终流畅响应，鼠标点击、窗口拖动不受影响

### 配置文件变更

**config.json 新增字段：**
```json
{
  "coreProcessNames": ["verge-mihomo", "mihomo", "clash-meta", "clash-rs", "clash", "clash-win64"],
  "clientProcessNames": ["Clash Verge", "clash-verge", "Clash Nyanpasu", "mihomo-party", "Clash for Windows"]
}
```

### 向后兼容

- 默认配置与 v0.0.1 兼容，`verge-mihomo` 仍是第一优先级
- 无需修改配置文件即可正常使用
- 新配置项为可选，不存在时使用默认值

---

## [v0.0.1] - 2026-02-04 (初始版本)

### 功能
- 智能进程监控（mihomo 核心）
- 内存监控与智能重启逻辑
- 多目标代理测试（Google、Cloudflare）
- 自适应检测间隔（正常 10s，异常 3s）
- 自动节点切换与黑名单机制（20 分钟）
- TCP 连接统计监控
- 开机自启管理（注册表）
- 外部 config.json 配置支持
- 单实例检测（Mutex）
- 系统托盘与右键菜单
- 每日 CSV 日志与 7 天自动清理
