using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;

/// <summary>
/// Clash Guardian Pro — 多 Clash 客户端智能守护进程
/// 主文件：常量、字段、构造函数、配置管理、入口点
/// </summary>
public partial class ClashGuardian : Form
{
    // ==================== 配置常量 ====================
    private const int DEFAULT_NORMAL_INTERVAL = 5000;      // 正常检测间隔：5秒
    private const int DEFAULT_FAST_INTERVAL = 1000;        // 异常时快速检测：1秒
    private const int DEFAULT_MEMORY_THRESHOLD = 150;      // 内存阈值 (MB)
    private const int DEFAULT_MEMORY_WARNING = 70;         // 内存警告阈值 (MB)
    private const int DEFAULT_HIGH_DELAY = 400;            // 高延迟阈值 (ms)
    private const int DEFAULT_BLACKLIST_MINUTES = 20;      // 黑名单时长（分钟）
    private const int DEFAULT_PROXY_PORT = 7897;           // 代理端口
    private const int DEFAULT_API_PORT = 9097;             // API 端口
    private const int TCP_CHECK_INTERVAL = 10;             // TCP 统计检测间隔（~50s）
    private const int NODE_UPDATE_INTERVAL = 30;           // 节点信息更新间隔（~150s）
    private const int DELAY_TEST_INTERVAL = 72;            // 延迟测试间隔（~6min）
    private const int LOG_RETENTION_DAYS = 7;              // 日志保留天数
    private const int COOLDOWN_COUNT = 5;                  // 重启后冷却次数
    private const int MAX_NODE_NAME_LENGTH = 50;           // 节点名最大长度
    private const int MAX_NODE_DISPLAY_LENGTH = 15;        // 节点名显示截断长度
    private const int MAX_ACCEPTABLE_DELAY = 2000;         // 最大可接受延迟 (ms)
    private const int CLOSE_WAIT_THRESHOLD = 20;           // CloseWait 连接泄漏阈值
    private const int CONSECUTIVE_OK_THRESHOLD = 3;        // 恢复正常间隔所需连续成功次数
    private const int MAX_RECURSE_DEPTH = 5;               // 代理组递归解析最大深度
    private const int MIN_UPDATE_FILE_SIZE = 10240;        // 更新文件最小有效大小 (bytes)
    private const int UPDATE_CHECK_TIMEOUT = 15000;        // 更新检查/进程退出等待超时 (ms)
    private const int PROCESS_KILL_TIMEOUT = 3000;         // 终止进程等待超时 (ms)
    private const int MAX_LOG_SIZE = 1048576;              // 日志文件最大大小 (1MB)

    // 自动更新配置
    private const string APP_VERSION = "0.0.9";
    private const string GITHUB_REPO = "redpomegranate/clash-verge-guardian";
    private const string UPDATE_API = "https://api.github.com/repos/{0}/releases/latest";

    // 网络超时常量
    private const int API_TIMEOUT_FAST = 1000;             // 快速 API 超时 (ms)
    private const int API_TIMEOUT_NORMAL = 3000;           // 正常 API 超时 (ms)
    private const int PROXY_TEST_TIMEOUT = 2500;           // 代理测试超时 (ms)
    private const int API_DISCOVER_TIMEOUT = 500;          // API 发现超时 (ms)

    // 性能日志阈值
    private const int PERF_LOG_PROXY_THRESHOLD = 5000;     // TestProxy 超时阈值 (ms)
    private const int PERF_LOG_DEFAULT_THRESHOLD = 2000;   // 其他操作超时阈值 (ms)

    // ==================== 决策结果（纯数据，无副作用） ====================
    struct StatusDecision {
        public bool NeedRestart;
        public bool NeedSwitch;
        public string Reason;
        public string Event;
        public bool HasIssue;
        public int NewFailCount;           // 决策后的 failCount 值
        public bool IncrementTotalFails;   // 是否增加总失败数
        public bool ResetConsecutiveOK;    // 是否重置连续成功计数
        public bool IncrementConsecutiveOK;// 是否增加连续成功计数
        public bool ResetStableTime;       // 是否重置稳定时间
    }

    // ==================== 多内核/多客户端支持 ====================
    private static readonly string[] DEFAULT_CORE_NAMES = new string[] {
        "verge-mihomo", "mihomo", "clash-meta", "clash-rs", "clash", "clash-win64"
    };

    private static readonly string[] DEFAULT_CLIENT_NAMES = new string[] {
        "Clash Verge", "clash-verge", "Clash Nyanpasu", "mihomo-party", "Clash for Windows"
    };

    private static readonly int[] DEFAULT_API_PORTS = new int[] { 9097, 9090, 7890, 9898 };

    private static readonly string[] DEFAULT_EXCLUDE_REGIONS = new string[] {
        "HK", "香港", "TW", "台湾", "MO", "澳门"
    };

    // ==================== UI 颜色常量 ====================
    private static readonly Color COLOR_OK = Color.FromArgb(34, 139, 34);
    private static readonly Color COLOR_WARNING = Color.FromArgb(255, 140, 0);
    private static readonly Color COLOR_ERROR = Color.FromArgb(220, 53, 69);
    private static readonly Color COLOR_TEXT = Color.FromArgb(60, 60, 60);
    private static readonly Color COLOR_GRAY = Color.FromArgb(100, 100, 100);
    private static readonly Color COLOR_CYAN = Color.FromArgb(0, 120, 140);
    private static readonly Color COLOR_BTN_BG = Color.FromArgb(230, 230, 230);
    private static readonly Color COLOR_BTN_FG = Color.FromArgb(33, 33, 33);
    private static readonly Color COLOR_FORM_BG = Color.FromArgb(250, 250, 252);

    // ==================== 运行时配置（可从配置文件加载） ====================
    private string clashApi;
    private string clashSecret;
    private int proxyPort;
    private int normalInterval;
    private int fastInterval;
    private int memoryThreshold;
    private int memoryWarning;
    private int highDelayThreshold;
    private int blacklistMinutes;

    private string[] coreProcessNames;
    private string[] clientProcessNames;
    private string[] clientPaths;
    private string[] excludeRegions;  // 可配置的节点排除规则

    // 当前检测到的进程信息（volatile 保证跨线程可见性）
    private volatile string detectedCoreName = "";
    private volatile string detectedClientPath = "";

    // ==================== UI 组件 ====================
    private NotifyIcon trayIcon;
    private Label statusLabel, memLabel, proxyLabel, logLabel, checkLabel, stableLabel;
    private Button restartBtn, exitBtn, logBtn;
    private System.Windows.Forms.Timer timer;

    // ==================== 运行时状态 ====================
    private string logFile, dataFile, configFile, baseDir;
    private string appDataDir, configDir, logsDir, monitorDir, diagnosticsDir;

    // 计数器：跨线程写入使用 Interlocked，UI 读取无需加锁（int 读取原子）
    private int totalChecks = 0;
    private int totalRestarts = 0;
    private int totalSwitches = 0;

    // 以下字段仅在 UI 线程修改（通过 BeginInvoke 确保），无需额外同步
    private int failCount = 0;
    private int totalFails = 0;
    private int consecutiveOK = 0;
    private int cooldownCount = 0;
    private DateTime lastStableTime;
    private DateTime startTime;

    // 跨线程字符串字段（volatile 保证可见性）
    private volatile string currentNode = "";
    private volatile string nodeGroup = "";

    // 跨线程数值字段（使用 Interlocked 读写）
    private int lastDelay = 0;

    private Dictionary<string, DateTime> nodeBlacklist = new Dictionary<string, DateTime>();
    private int[] lastTcpStats = new int[] { 0, 0, 0 };

    // ==================== 线程安全设施 ====================
    private readonly object blacklistLock = new object();  // nodeBlacklist 专用锁
    private readonly object restartLock = new object();    // RestartClash 并发门闩
    private int _isChecking = 0;                           // 0=空闲, 1=检测中; Interlocked 操作
    private volatile bool _isRestarting = false;           // 重启进行中标志（阻止 CheckStatus 并发）

    // ==================== 控制与诊断：暂停自动操作 ====================
    // 仅暂停“自动切换/自动重启”，不影响检测与 UI 更新；手动操作仍可执行
    private DateTime pauseAutoActionsUntil = DateTime.MinValue;
    private DateTime lastSuppressedActionLog = DateTime.MinValue;

    bool IsAutoActionsPaused {
        get { return DateTime.Now < pauseAutoActionsUntil; }
    }

    TimeSpan GetAutoActionsPauseRemaining() {
        DateTime now = DateTime.Now;
        if (now >= pauseAutoActionsUntil) return TimeSpan.Zero;
        TimeSpan ts = pauseAutoActionsUntil - now;
        return ts < TimeSpan.Zero ? TimeSpan.Zero : ts;
    }

    // ==================== 构造函数 ====================
    public ClashGuardian()
    {
        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        startTime = DateTime.Now;
        lastStableTime = DateTime.Now;

        InitRuntimePaths();

        LoadConfigFast();

        ThreadPool.QueueUserWorkItem(_ => CleanOldLogs());

        if (!File.Exists(dataFile))
            File.WriteAllText(dataFile, "Time,ProxyOK,Delay,MemMB,Handles,TimeWait,Established,CloseWait,Node,Event\n");

        InitializeUI();
        InitializeTrayIcon();

        timer = new System.Windows.Forms.Timer();
        timer.Interval = normalInterval;
        timer.Tick += CheckStatus;
        timer.Start();

        Log("守护启动 Pro");

        ThreadPool.QueueUserWorkItem(_ => DoFirstCheck());
    }

    // ==================== 运行时目录规划（运行数据与源码/可执行分离） ====================
    void InitRuntimePaths() {
        // 默认回退：仍放在程序目录
        appDataDir = baseDir;
        configDir = baseDir;
        logsDir = baseDir;
        monitorDir = baseDir;
        diagnosticsDir = baseDir;

        configFile = Path.Combine(configDir, "config.json");
        logFile = Path.Combine(logsDir, "guardian.log");
        dataFile = Path.Combine(monitorDir, "monitor_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");

        try {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return;

            appDataDir = Path.Combine(local, "ClashGuardian");
            configDir = Path.Combine(appDataDir, "config");
            logsDir = Path.Combine(appDataDir, "logs");
            monitorDir = Path.Combine(appDataDir, "monitor");
            diagnosticsDir = Path.Combine(appDataDir, "diagnostics");

            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(logsDir);
            Directory.CreateDirectory(monitorDir);
            Directory.CreateDirectory(diagnosticsDir);

            configFile = Path.Combine(configDir, "config.json");
            logFile = Path.Combine(logsDir, "guardian.log");
            dataFile = Path.Combine(monitorDir, "monitor_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");

            // 配置迁移需要同步完成，否则本次启动无法读取到旧配置
            MigrateLegacyConfig();

            // 日志/监控数据迁移不影响启动，放后台做
            ThreadPool.QueueUserWorkItem(_ => MigrateLegacyLogsAndMonitor());
        } catch {
            // 保持回退路径，不打断启动
        }
    }

    void MigrateLegacyConfig() {
        try {
            if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(configFile)) return;
            string legacy = Path.Combine(baseDir, "config.json");
            if (!File.Exists(legacy)) return;
            if (File.Exists(configFile)) return;

            try {
                Directory.CreateDirectory(Path.GetDirectoryName(configFile));
                File.Move(legacy, configFile);
                Log("迁移配置: 已移动到 " + configFile);
            } catch {
                try {
                    File.Copy(legacy, configFile, true);
                    Log("迁移配置: 已复制到 " + configFile);
                } catch { /* ignore */ }
            }
        } catch { /* ignore */ }
    }

    void MigrateLegacyLogsAndMonitor() {
        try {
            if (string.IsNullOrEmpty(baseDir)) return;

            // 迁移 guardian.log
            try {
                string legacyLog = Path.Combine(baseDir, "guardian.log");
                if (File.Exists(legacyLog) && !string.Equals(legacyLog, logFile, StringComparison.OrdinalIgnoreCase)) {
                    try {
                        Directory.CreateDirectory(Path.GetDirectoryName(logFile));
                        if (!File.Exists(logFile)) {
                            File.Move(legacyLog, logFile);
                        } else {
                            string legacyName = "guardian_legacy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                            File.Copy(legacyLog, Path.Combine(logsDir, legacyName), true);
                        }
                    } catch { /* ignore */ }
                }
            } catch { /* ignore */ }

            // 迁移 monitor_*.csv
            try {
                string[] files = Directory.GetFiles(baseDir, "monitor_*.csv");
                foreach (string f in files) {
                    try {
                        string dest = Path.Combine(monitorDir, Path.GetFileName(f));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        if (!File.Exists(dest)) File.Move(f, dest);
                        else File.Copy(f, dest, true);
                    } catch { /* ignore */ }
                }
            } catch { /* ignore */ }
        } catch { /* ignore */ }
    }

    // 首次检测（后台执行，含进程探测）
    void DoFirstCheck() {
        try {
            DetectRunningCore();
            if (string.IsNullOrEmpty(detectedCoreName)) {
                AutoDiscoverApi();
            }

            double mem = 0;
            int handles = 0;
            bool running = GetMihomoStats(out mem, out handles);

            bool proxyOK = false;
            int delay = TestProxy(out proxyOK, true);

            GetCurrentNode();

            this.BeginInvoke((Action)(() => {
                string delayStr = delay > 0 ? delay + "ms" : "--";
                string coreShort = string.IsNullOrEmpty(detectedCoreName) ? "未检测" : detectedCoreName;
                memLabel.Text = "内  核:  " + coreShort + "  |  " + mem.ToString("F1") + "MB  |  句柄: " + handles;

                string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "--" : currentNode;
                string nodeShort = TruncateNodeName(nodeDisplay);
                proxyLabel.Text = "代  理:  " + (proxyOK ? "OK" : "X") + " " + delayStr + " | " + nodeShort;
                proxyLabel.ForeColor = proxyOK ? COLOR_OK : COLOR_ERROR;

                statusLabel.Text = "● 状态: 运行中";
                statusLabel.ForeColor = COLOR_OK;

                checkLabel.Text = "统  计:  检测 1  |  重启 0  |  切换 0  |  黑名单 0";
                stableLabel.Text = "稳定性:  连续 0s  |  运行 0s  |  成功率 100.0%";

                if (!string.IsNullOrEmpty(detectedCoreName)) {
                    Log("检测到内核: " + detectedCoreName);
                }
            }));

            CheckForUpdate(true);
            Interlocked.Exchange(ref totalChecks, 1);
        } catch (Exception ex) { Log("首次检测异常: " + ex.Message); }
    }

    // 节点名截断显示
    static string TruncateNodeName(string name) {
        return name.Length > MAX_NODE_DISPLAY_LENGTH
            ? name.Substring(0, MAX_NODE_DISPLAY_LENGTH) + ".."
            : name;
    }

    // ==================== 配置管理 ====================
    static int ClampInt(int v, int min, int max) {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    static int TryParseInt(string s, int fallback, out bool parsed) {
        int v;
        parsed = int.TryParse(s, out v);
        return parsed ? v : fallback;
    }

    static int TryParseInt(string s, int fallback) {
        bool _;
        return TryParseInt(s, fallback, out _);
    }

    void LoadConfigFast() {
        clashApi = "http://127.0.0.1:" + DEFAULT_API_PORT;
        clashSecret = "set-your-secret";
        proxyPort = DEFAULT_PROXY_PORT;
        normalInterval = DEFAULT_NORMAL_INTERVAL;
        fastInterval = DEFAULT_FAST_INTERVAL;
        memoryThreshold = DEFAULT_MEMORY_THRESHOLD;
        memoryWarning = DEFAULT_MEMORY_WARNING;
        highDelayThreshold = DEFAULT_HIGH_DELAY;
        blacklistMinutes = DEFAULT_BLACKLIST_MINUTES;

        coreProcessNames = DEFAULT_CORE_NAMES;
        clientProcessNames = DEFAULT_CLIENT_NAMES;
        clientPaths = GetDefaultClientPaths();
        excludeRegions = DEFAULT_EXCLUDE_REGIONS;

        if (File.Exists(configFile)) {
            try {
                string json = File.ReadAllText(configFile, Encoding.UTF8);

                clashApi = GetJsonValue(json, "clashApi", clashApi);
                if (clashApi == null) clashApi = "";
                clashApi = clashApi.Trim().TrimEnd('/');

                clashSecret = GetJsonValue(json, "clashSecret", clashSecret);

                bool parsed;
                string rawProxyPort = GetJsonValue(json, "proxyPort", proxyPort.ToString());
                int proxyPortParsed = TryParseInt(rawProxyPort, proxyPort, out parsed);
                int proxyPortFixed = ClampInt(proxyPortParsed, 1, 65535);
                if (!parsed || proxyPortFixed != proxyPortParsed) Log("配置修正: proxyPort " + rawProxyPort + " -> " + proxyPortFixed);
                proxyPort = proxyPortFixed;

                string rawNormalInterval = GetJsonValue(json, "normalInterval", normalInterval.ToString());
                int normalIntervalParsed = TryParseInt(rawNormalInterval, normalInterval, out parsed);
                int normalIntervalFixed = ClampInt(normalIntervalParsed, 500, 600000);
                if (!parsed || normalIntervalFixed != normalIntervalParsed) Log("配置修正: normalInterval " + rawNormalInterval + " -> " + normalIntervalFixed);
                normalInterval = normalIntervalFixed;

                string rawMemoryThreshold = GetJsonValue(json, "memoryThreshold", memoryThreshold.ToString());
                int memoryThresholdParsed = TryParseInt(rawMemoryThreshold, memoryThreshold, out parsed);
                int memoryThresholdFixed = ClampInt(memoryThresholdParsed, 10, 4096);
                if (!parsed || memoryThresholdFixed != memoryThresholdParsed) Log("配置修正: memoryThreshold " + rawMemoryThreshold + " -> " + memoryThresholdFixed);
                memoryThreshold = memoryThresholdFixed;

                string rawHighDelay = GetJsonValue(json, "highDelayThreshold", highDelayThreshold.ToString());
                int highDelayParsed = TryParseInt(rawHighDelay, highDelayThreshold, out parsed);
                int highDelayFixed = ClampInt(highDelayParsed, 50, 10000);
                if (!parsed || highDelayFixed != highDelayParsed) Log("配置修正: highDelayThreshold " + rawHighDelay + " -> " + highDelayFixed);
                highDelayThreshold = highDelayFixed;

                string rawBlacklist = GetJsonValue(json, "blacklistMinutes", blacklistMinutes.ToString());
                int blacklistParsed = TryParseInt(rawBlacklist, blacklistMinutes, out parsed);
                int blacklistFixed = ClampInt(blacklistParsed, 1, 1440);
                if (!parsed || blacklistFixed != blacklistParsed) Log("配置修正: blacklistMinutes " + rawBlacklist + " -> " + blacklistFixed);
                blacklistMinutes = blacklistFixed;

                // 只影响极端误配：保持阈值一致性，避免配置导致异常行为
                memoryWarning = ClampInt(memoryWarning, 10, 4096);
                if (memoryThreshold < memoryWarning) {
                    Log("配置修正: memoryThreshold " + memoryThreshold + " -> " + memoryWarning + " (>=memoryWarning)");
                    memoryThreshold = memoryWarning;
                }
                fastInterval = ClampInt(fastInterval, 200, normalInterval);

                string customCores = GetJsonArray(json, "coreProcessNames");
                if (!string.IsNullOrEmpty(customCores)) coreProcessNames = customCores.Split(',');

                string customClients = GetJsonArray(json, "clientProcessNames");
                if (!string.IsNullOrEmpty(customClients)) clientProcessNames = customClients.Split(',');

                string customExcludes = GetJsonArray(json, "excludeRegions");
                if (!string.IsNullOrEmpty(customExcludes)) excludeRegions = customExcludes.Split(',');

                // 从配置文件恢复上次检测到的客户端路径
                string savedClientPath = GetJsonValue(json, "clientPath", "");
                if (!string.IsNullOrEmpty(savedClientPath) && File.Exists(savedClientPath)) {
                    detectedClientPath = savedClientPath;
                }
            } catch (Exception ex) { Log("配置加载异常: " + ex.Message); }
        } else {
            ThreadPool.QueueUserWorkItem(_ => SaveDefaultConfig());
        }
    }

    string[] GetDefaultClientPaths() {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        List<string> paths = new List<string>();

        // Clash Verge / Clash Verge Rev（常见安装路径）
        paths.Add(Path.Combine(localAppData, @"Programs\clash-verge\Clash Verge.exe"));
        paths.Add(Path.Combine(localAppData, @"Programs\clash-verge\clash-verge.exe"));
        paths.Add(Path.Combine(localAppData, @"Clash Verge\Clash Verge.exe"));
        paths.Add(Path.Combine(programFiles, @"Clash Verge\Clash Verge.exe"));
        paths.Add(Path.Combine(programFilesX86, @"Clash Verge\Clash Verge.exe"));

        // Clash Nyanpasu
        paths.Add(Path.Combine(localAppData, @"Programs\Clash Nyanpasu\Clash Nyanpasu.exe"));
        paths.Add(Path.Combine(programFiles, @"Clash Nyanpasu\Clash Nyanpasu.exe"));

        // mihomo-party
        paths.Add(Path.Combine(localAppData, @"mihomo-party\mihomo-party.exe"));
        paths.Add(Path.Combine(programFiles, @"mihomo-party\mihomo-party.exe"));

        // Clash for Windows
        paths.Add(Path.Combine(localAppData, @"Programs\Clash for Windows\Clash for Windows.exe"));

        // Scoop 安装路径
        paths.Add(Path.Combine(userProfile, @"scoop\apps\clash-verge\current\Clash Verge.exe"));
        paths.Add(Path.Combine(userProfile, @"scoop\apps\clash-nyanpasu\current\Clash Nyanpasu.exe"));

        return paths.ToArray();
    }

    void DetectRunningCore() {
        foreach (string coreName in coreProcessNames) {
            try {
                Process[] procs = Process.GetProcessesByName(coreName);
                if (procs.Length > 0) {
                    detectedCoreName = coreName;
                    foreach (var p in procs) p.Dispose();
                    DetectRunningClient();
                    return;
                }
            } catch { /* 进程探测：未找到属正常情况 */ }
        }
    }

    void DetectRunningClient() {
        // 1. 从运行中的进程获取路径（最准确）
        foreach (string clientName in clientProcessNames) {
            try {
                Process[] procs = Process.GetProcessesByName(clientName);
                if (procs.Length > 0) {
                    try { detectedClientPath = procs[0].MainModule.FileName; }
                    catch { /* 32/64位进程访问限制可忽略 */ }
                    foreach (var p in procs) p.Dispose();
                    if (!string.IsNullOrEmpty(detectedClientPath)) { SaveClientPath(); return; }
                }
            } catch { /* 客户端探测：未找到属正常情况 */ }
        }

        // 2. 如果已有持久化路径，验证是否仍然有效
        if (!string.IsNullOrEmpty(detectedClientPath) && File.Exists(detectedClientPath)) return;

        // 3. 从默认路径列表查找
        foreach (string path in clientPaths) {
            if (File.Exists(path)) { detectedClientPath = path; SaveClientPath(); return; }
        }

        // 4. 从注册表查找（兜底）
        string regPath = FindClientFromRegistry();
        if (!string.IsNullOrEmpty(regPath)) { detectedClientPath = regPath; SaveClientPath(); return; }
    }

    // 从注册表搜索客户端安装路径
    string FindClientFromRegistry() {
        string[] regRoots = new string[] {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        string[] keywords = new string[] { "Clash Verge", "clash-verge", "Clash Nyanpasu", "mihomo-party", "Clash for Windows" };

        foreach (string regRoot in regRoots) {
            try {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regRoot)) {
                    if (key == null) continue;
                    foreach (string subKeyName in key.GetSubKeyNames()) {
                        try {
                            using (RegistryKey sub = key.OpenSubKey(subKeyName)) {
                                if (sub == null) continue;
                                string displayName = sub.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(displayName)) continue;

                                foreach (string kw in keywords) {
                                    if (displayName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) {
                                        string installDir = sub.GetValue("InstallLocation") as string;
                                        if (!string.IsNullOrEmpty(installDir)) {
                                            // 尝试常见的可执行文件名
                                            string[] exeNames = new string[] {
                                                displayName + ".exe",
                                                displayName.Replace(" ", "-").ToLower() + ".exe",
                                                Path.GetFileName(installDir) + ".exe"
                                            };
                                            foreach (string exe in exeNames) {
                                                string fullPath = Path.Combine(installDir, exe);
                                                if (File.Exists(fullPath)) return fullPath;
                                            }
                                            // 搜索目录下的 exe 文件
                                            try {
                                                foreach (string f in Directory.GetFiles(installDir, "*.exe")) {
                                                    string fn = Path.GetFileNameWithoutExtension(f).ToLower();
                                                    if (fn.Contains("clash") || fn.Contains("mihomo") || fn.Contains("nyanpasu"))
                                                        return f;
                                                }
                                            } catch { /* 目录遍历失败可忽略 */ }
                                        }
                                        break;
                                    }
                                }
                            }
                        } catch { /* 单个注册表子键读取失败可忽略 */ }
                    }
                }
            } catch { /* 注册表访问失败可忽略 */ }
        }

        // 也搜索 HKCU
        try {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")) {
                if (key != null) {
                    foreach (string subKeyName in key.GetSubKeyNames()) {
                        try {
                            using (RegistryKey sub = key.OpenSubKey(subKeyName)) {
                                if (sub == null) continue;
                                string displayName = sub.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(displayName)) continue;

                                foreach (string kw in keywords) {
                                    if (displayName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) {
                                        string installDir = sub.GetValue("InstallLocation") as string;
                                        if (!string.IsNullOrEmpty(installDir)) {
                                            try {
                                                foreach (string f in Directory.GetFiles(installDir, "*.exe")) {
                                                    string fn = Path.GetFileNameWithoutExtension(f).ToLower();
                                                    if (fn.Contains("clash") || fn.Contains("mihomo") || fn.Contains("nyanpasu"))
                                                        return f;
                                                }
                                            } catch { /* 目录遍历失败可忽略 */ }
                                        }
                                        break;
                                    }
                                }
                            }
                        } catch { /* 单个注册表子键读取失败可忽略 */ }
                    }
                }
            }
        } catch { /* 注册表访问失败可忽略 */ }

        return null;
    }

    // 将检测到的客户端路径持久化到 config.json
    void SaveClientPath() {
        if (string.IsNullOrEmpty(detectedClientPath)) return;
        try {
            if (File.Exists(configFile)) {
                string json = File.ReadAllText(configFile, Encoding.UTF8);
                if (json.Contains("\"clientPath\"")) {
                    // 替换已有的 clientPath
                    int start = json.IndexOf("\"clientPath\"");
                    int valueStart = json.IndexOf(':', start) + 1;
                    // 找到值的结束位置（逗号或 }）
                    int valueEnd = valueStart;
                    bool inStr = false;
                    while (valueEnd < json.Length) {
                        if (json[valueEnd] == '"') inStr = !inStr;
                        if (!inStr && (json[valueEnd] == ',' || json[valueEnd] == '}')) break;
                        valueEnd++;
                    }
                    string escaped = detectedClientPath.Replace("\\", "\\\\");
                    json = json.Substring(0, valueStart) + " \"" + escaped + "\"" + json.Substring(valueEnd);
                } else {
                    // 在最后一个 } 前插入
                    int lastBrace = json.LastIndexOf('}');
                    if (lastBrace > 0) {
                        string escaped = detectedClientPath.Replace("\\", "\\\\");
                        json = json.Substring(0, lastBrace) + ",\n  \"clientPath\": \"" + escaped + "\"\n}";
                    }
                }
                File.WriteAllText(configFile, json, Encoding.UTF8);
            }
        } catch { /* 保存客户端路径失败不影响运行 */ }
    }

    void AutoDiscoverApi() {
        Stopwatch sw = Stopwatch.StartNew();
        foreach (int port in DEFAULT_API_PORTS) {
            try {
                string testApi = "http://127.0.0.1:" + port;
                HttpWebRequest req = WebRequest.Create(testApi + "/version") as HttpWebRequest;
                req.Headers.Add("Authorization", "Bearer " + clashSecret);
                req.Timeout = API_DISCOVER_TIMEOUT;
                // 本地 API 不应走系统代理
                try { if (req.RequestUri != null && req.RequestUri.IsLoopback) req.Proxy = null; } catch { /* ignore */ }
                using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse) {
                    if (resp.StatusCode == HttpStatusCode.OK) {
                        clashApi = testApi;
                        LogPerf("AutoDiscoverApi(found:" + port + ")", sw.ElapsedMilliseconds);
                        return;
                    }
                }
            } catch { /* 端口探测：连接失败属正常情况 */ }
        }
        LogPerf("AutoDiscoverApi(notfound)", sw.ElapsedMilliseconds);
    }

    string GetJsonArray(string json, string key) {
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search);
        if (idx < 0) return "";
        idx = json.IndexOf('[', idx);
        if (idx < 0) return "";
        int end = json.IndexOf(']', idx);
        if (end < 0) return "";
        string arr = json.Substring(idx + 1, end - idx - 1);
        return arr.Replace("\"", "").Replace(" ", "").Replace("\n", "").Replace("\r", "");
    }

    void SaveDefaultConfig() {
        string coreNames = string.Join("\", \"", DEFAULT_CORE_NAMES);
        string clientNames = string.Join("\", \"", DEFAULT_CLIENT_NAMES);
        string excludeNames = string.Join("\", \"", DEFAULT_EXCLUDE_REGIONS);

        string config = "{\n" +
            "  \"clashApi\": \"" + clashApi + "\",\n" +
            "  \"clashSecret\": \"" + clashSecret + "\",\n" +
            "  \"proxyPort\": " + proxyPort + ",\n" +
            "  \"normalInterval\": " + normalInterval + ",\n" +
            "  \"memoryThreshold\": " + memoryThreshold + ",\n" +
            "  \"highDelayThreshold\": " + highDelayThreshold + ",\n" +
            "  \"blacklistMinutes\": " + blacklistMinutes + ",\n" +
            "  \"coreProcessNames\": [\"" + coreNames + "\"],\n" +
            "  \"clientProcessNames\": [\"" + clientNames + "\"],\n" +
            "  \"excludeRegions\": [\"" + excludeNames + "\"]\n" +
            "}";
        try { File.WriteAllText(configFile, config, Encoding.UTF8); }
        catch (Exception ex) { Log("保存配置失败: " + ex.Message); }
    }

    string GetJsonValue(string json, string key, string defaultValue) {
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search);
        if (idx < 0) return defaultValue;
        idx += search.Length;
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == '"')) idx++;
        int end = idx;
        bool inQuote = idx > 0 && json[idx - 1] == '"';
        if (inQuote) {
            end = json.IndexOf('"', idx);
        } else {
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n') end++;
        }
        return json.Substring(idx, end - idx).Trim();
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
    }

    [STAThread]
    static void Main(string[] args) {
        if (args.Length >= 2 && args[0] == "--wait-pid") {
            try {
                int oldPid = int.Parse(args[1]);
                try { Process.GetProcessById(oldPid).WaitForExit(UPDATE_CHECK_TIMEOUT); }
                catch { /* 旧进程可能已退出 */ }
            } catch { /* PID 解析失败可忽略 */ }
            Thread.Sleep(1000);
            try {
                string oldFile = Application.ExecutablePath + ".old";
                if (File.Exists(oldFile)) File.Delete(oldFile);
            } catch { /* 清理旧版本失败不影响运行 */ }
        }

        bool createdNew;
        using (Mutex mutex = new Mutex(true, "ClashGuardianSingleInstance", out createdNew)) {
            if (!createdNew) return;
            Application.EnableVisualStyles();
            Application.Run(new ClashGuardian());
        }
    }
}
