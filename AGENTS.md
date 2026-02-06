# AGENTS.md - AI 开发指南

> 本文档供 AI 快速了解项目并执行开发流程

## 📋 项目概述

- **项目名称**：Clash Guardian Pro
- **版本**：v0.0.3
- **功能**：多 Clash 客户端的智能守护进程
- **语言**：C# (.NET Framework 4.5+)
- **平台**：Windows 10/11

## 📁 项目结构

```
F:\clash-verge-guardian-0.0.2\
├── ClashGuardian.cs       # 主源代码（唯一源文件）
├── ClashGuardian.exe      # 编译后的可执行文件
├── config.json            # 配置文件（首次运行自动生成）
├── guardian.log           # 运行日志（仅异常）
├── monitor_YYYYMMDD.csv   # 每日监控数据
├── README.md              # 项目说明文档
└── AGENTS.md              # 本文件
```

## 🔧 编译命令

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:ClashGuardian.exe ClashGuardian.cs
```

编译成功标志：无 error 输出（warning 可忽略）

## ⚠️ 重要注意事项

1. **UI 线程安全** - 后台线程操作 UI 必须使用 `this.BeginInvoke((Action)(() => { ... }))`
2. **日志精简** - 正常情况不记录日志，只记录异常（TestProxy > 5s，其他 > 2s）
3. **静默运行** - 所有自动操作不要有弹窗/通知
4. **节点名称** - 使用 `ExtractJsonString` 解析 Unicode 转义，用 `SafeNodeName` 过滤不可显示字符和 emoji surrogate pair
5. **代理组切换** - 不要硬编码 GLOBAL，使用 `FindSelectorGroup` 自动发现实际节点所属的 Selector 组
6. **节点列表获取** - 从 Selector 组的 `all` 数组正向提取节点名（`GetGroupAllNodes`），不要反向扫描 type 字段（会匹配到 `extra` 里的嵌套对象）

## 🏗️ 核心代码区域

| 行号范围 | 功能模块 |
|---------|---------|
| 1-107 | 配置常量、UI 颜色、运行时配置/状态 |
| 108-250 | 构造函数、配置加载、进程探测 |
| 356-520 | UI 初始化、按钮创建 |
| 543-590 | 日志管理（Log、LogPerf） |
| 591-636 | API 通信（ApiRequest、ApiPut） |
| 655-870 | 节点管理（GetCurrentNode、FindProxyNow/Type、ExtractJsonString、SafeNodeName、TriggerDelayTest） |
| 883-1060 | 节点切换（GetGroupAllNodes、GetNodeDelay、FindSelectorGroup、SwitchToBestNode） |
| 1061-1090 | 代理测试（TestProxy） |
| 1093-1165 | 系统监控（GetTcpStats、GetMihomoStats） |
| 1165-1240 | 重启管理（RestartClash） |
| 1246-1365 | 后台检测循环（CheckStatus、DoCooldownCheck、DoCheckInBackground） |
| 1366-1445 | UI 更新与决策逻辑（UpdateUI） |

## 🔄 关键修复记录

### v0.0.3 修复
1. **节点切换 "proxy not exist"** - 旧代码扫描 `"type":"Shadowsocks"` 反向查找节点名，会误匹配 `extra` 里的 URL 对象。改为从 Selector 组的 `all` 数组正向获取节点列表
2. **硬编码 GLOBAL 组** - 用 `FindSelectorGroup` 自动发现子 Selector 组（如 BoostNet），切换和测速都对正确的组操作
3. **节点名框框乱码** - emoji 国旗是 surrogate pair，WinForms 无法渲染，`SafeNodeName` 直接跳过
4. **测速按钮无反馈** - 测速后立即调用 `TestProxy` 并更新状态栏
5. **测速阻塞** - `TriggerDelayTest` 改为 `BeginGetResponse` 异步，不等待全部节点测完
6. **检测频率** - 正常 5s / 异常 1s（原 10s / 3s），子任务间隔按倍数调整保持不变

### v0.0.2 修复
1. **重启后 UI 卡住** - `RestartClash` 在后台线程执行，UI 操作需 `BeginInvoke`
2. **冷却期无响应** - 冷却期改为主动探测内核+代理，恢复后立即更新状态
3. **切换后统计不更新** - 添加 `RefreshNodeDisplay()` 统一刷新
4. **节点名乱码** - 添加 Unicode 转义解析和安全字符过滤
5. **日志过多** - LogPerf 阈值改为 TestProxy > 5s，其他 > 2s

## 📊 决策逻辑

| 条件 | 动作 |
|------|------|
| 进程不存在 | 重启 |
| 内存 > 150MB | 无条件重启 |
| 内存 > 70MB + 代理异常 | 重启 |
| 代理连续 2 次无响应 | 切换节点 |
| 代理连续 4 次无响应 | 重启 |
| 延迟 > 400ms 连续 2 次 | 切换节点 |

## 🛠️ 常用开发命令

```powershell
# 编译
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:ClashGuardian.exe ClashGuardian.cs

# 查看 Clash 相关进程
Get-Process | Where-Object {$_.ProcessName -like "*clash*" -or $_.ProcessName -like "*mihomo*"}

# 结束 ClashGuardian
Get-Process | Where-Object {$_.ProcessName -like "*ClashGuardian*"} | Stop-Process -Force
```
