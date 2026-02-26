using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.Security.Principal;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Runtime.InteropServices;

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
    private const int DEFAULT_HIGH_DELAY_CONN_OK_EXTRA_MS = 120;     // 连接性为 OK 时的额外高延迟阈值 (ms)
    private const int DEFAULT_HIGH_DELAY_SWITCH_CONSEC_CONN_OK = 4;  // 连接性 OK 时高延迟连续命中次数
    private const int DEFAULT_HIGH_DELAY_SWITCH_CONSEC_CONN_UNKNOWN = 3; // 连接性 Unknown 时高延迟连续命中次数
    private const int DEFAULT_BLACKLIST_MINUTES = 20;      // 黑名单时长（分钟）
    private const int DEFAULT_CLOSEWAIT_THRESHOLD_CORE = 25;          // Core 级 CloseWait 阈值
    private const int DEFAULT_CLOSEWAIT_CONSECUTIVE = 3;              // Core CloseWait 连续命中次数
    private const int DEFAULT_AUTO_SWITCH_MAX_PER_10MIN = 6;          // 自动切换 10 分钟上限
    private const int DEFAULT_AUTO_RESTART_MAX_PER_10MIN = 3;         // 自动重启 10 分钟上限
    private const int DEFAULT_AUTO_RESTART_MIN_INTERVAL_SECONDS = 20; // 自动重启最小间隔
    private const int DEFAULT_SWITCH_STORM_SUPPRESS_SECONDS = 60;     // 切换风暴抑制时长
    private const int DEFAULT_RESTART_STORM_SUPPRESS_SECONDS = 120;   // 重启风暴抑制时长
    private const bool DEFAULT_POST_MATCH_GUARD_ENABLED = true;        // 结算->大厅过渡窗口保护开关
    private const int DEFAULT_POST_MATCH_GUARD_SECONDS = 90;           // 结算->大厅过渡窗口保护时长
    private const bool DEFAULT_MATCH_FREEZE_AUTO_ACTIONS = true;       // 过渡窗口冻结自动重启/切换/应急
    private const bool DEFAULT_MATCH_PIN_NODE_ENABLED = true;          // 过渡窗口固定当前节点
    private const bool DEFAULT_STEAM_TAKEOVER_COMPENSATE_ON_POST_MATCH = true; // 过渡窗口执行单轮Steam补偿清流
    private const int DEFAULT_PROXY_PORT = 7897;           // 代理端口
    private const int DEFAULT_API_PORT = 9097;             // API 端口
    private const int LOG_RETENTION_DAYS = 7;              // 日志保留天数
    private const int COOLDOWN_COUNT = 5;                  // 重启后冷却次数
    private const int MAX_NODE_NAME_LENGTH = 50;           // 节点名最大长度
    private const int MAX_NODE_DISPLAY_LENGTH = 15;        // 节点名显示截断长度
    private const int MAX_ACCEPTABLE_DELAY = 2000;         // 最大可接受延迟 (ms)
    private const int CONSECUTIVE_OK_THRESHOLD = 3;        // 恢复正常间隔所需连续成功次数
    private const int MAX_RECURSE_DEPTH = 5;               // 代理组递归解析最大深度
    private const int MIN_UPDATE_FILE_SIZE = 10240;        // 更新文件最小有效大小 (bytes)
    private const int UPDATE_CHECK_TIMEOUT = 15000;        // 更新检查/进程退出等待超时 (ms)
    private const int PROCESS_KILL_TIMEOUT = 3000;         // 终止进程等待超时 (ms)
    private const int MAX_LOG_SIZE = 1048576;              // 日志文件最大大小 (1MB)
    private const int UU_ROUTE_INSTALL_EXIT_OK = 0;
    private const int UU_ROUTE_INSTALL_EXIT_NOT_ADMIN = 10;
    private const int UU_ROUTE_INSTALL_EXIT_TASK_CREATE_FAILED = 11;
    private const int UU_ROUTE_INSTALL_EXIT_TASK_VERIFY_FAILED = 12;

    // 恢复管线（高内存+高延迟）相关参数
    private const int HMHD_CORE_RESET_ATTEMPTS = 2;        // 内核快速重置次数（每次后会刷新测速并切节点）
    private const int HMHD_COOLDOWN_SECONDS = 60;          // 触发节流：避免短时间内反复重入恢复管线
    private const int CORE_RECOVERY_MAX_WAIT_MS = 8000;    // Kill core 后等待 core 自动恢复的最大时间
    private const int PROXY_RECOVERY_MAX_WAIT_MS = 8000;   // 等待代理恢复的最大时间（含重启/切换后验证）
    private const int DELAY_REFRESH_MAX_WAIT_MS = 6000;    // 切节点前等待延迟历史可用的最大时间（触发 delay test 后轮询）

    // 自动切换风暴保护 + 无可用低延迟节点升级策略
    private const int AUTO_SWITCH_MIN_INTERVAL_MS = 2500;              // 自动切节点最小间隔（避免 1s/2s 级刷屏）
    private const int AUTO_SWITCH_NO_GOOD_NODE_STREAK_THRESHOLD = 3;   // 连续失败达到阈值则升级处理
    private const int AUTO_SWITCH_NO_GOOD_NODE_WINDOW_SECONDS = 20;    // 连续失败计数窗口（超过则重置）
    private const int AUTO_SWITCH_NO_GOOD_NODE_LOG_THROTTLE_SECONDS = 10; // 切换失败日志节流
    private const int AUTO_SWITCH_NO_GOOD_NODE_ESCALATE_THROTTLE_SECONDS = 30; // 升级动作节流
    private const int SEVERE_DELAY_MS = 4500;                          // 接近 delay timeout 的延迟视为“严重”
    private const int SUB_SWITCH_EMERGENCY_MIN_INTERVAL_SECONDS = 60;  // 紧急订阅切换最小间隔（绕过 cooldown 时）
    private const int PROXY_RECOVERY_FAST_WAIT_MS = 4500;              // 非 HMHD 恢复阶段的快速验证窗口（失败则尽快升级）

    // 订阅健康探测（并行后台任务）：用于快速识别“当前订阅整体不可用”并升级为切换订阅
    private const int SUB_PROBE_MIN_INTERVAL_MS = 60000;               // 探测最小间隔（避免频繁全局探测）
    private const int SUB_PROBE_API_WAIT_MS = 16000;                   // 等待 API 就绪的最大时间（覆盖 core/client 重启窗口）
    private const int SUB_PROBE_SAMPLE_A = 8;                          // 第一阶段抽样节点数（快速判断）
    private const int SUB_PROBE_SAMPLE_B = 12;                         // 第二阶段抽样节点数（确认判断）
    private const int SUB_PROBE_TIMEOUT_A = 3500;                      // 第一阶段单节点 delay timeout
    private const int SUB_PROBE_TIMEOUT_B = 5000;                      // 第二阶段单节点 delay timeout
    private const int SUB_PROBE_CONCURRENCY = 6;                       // 探测并发度（避免压垮 API）
    private const int SUB_PROBE_RESULT_MAX_AGE_SECONDS = 60;           // 探测结果有效期（过期则视为未知）

    // 自动更新配置
    private const string APP_VERSION = "1.0.8";
    private const string GITHUB_REPO = "redpomegranate/clash-verge-guardian";
    private const string UPDATE_API = "https://api.github.com/repos/{0}/releases/latest";

    // 网络超时常量
    private const int API_TIMEOUT_FAST = 1000;             // 快速 API 超时 (ms)
    private const int API_TIMEOUT_NORMAL = 3000;           // 正常 API 超时 (ms)
    private const int PROXY_TEST_TIMEOUT = 900;            // 代理测试超时 (ms) - 提速探测
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
        public int NewFailCount;           // 决策后的 proxy failCount 值
        public int NewHighDelayCount;      // 决策后的高延迟连续计数
        public int NewCloseWaitFailCount;  // 决策后的 core CloseWait 连续计数
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

    private static readonly string[] DEFAULT_CONNECTIVITY_TEST_URLS = new string[] {
        "http://www.gstatic.com/generate_204",
        "http://cp.cloudflare.com/generate_204",
        "http://www.msftconnecttest.com/connecttest.txt"
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
    private int speedFactor;                     // 检测提速倍率（>=1）
    private int effectiveNormalInterval;         // 实际使用的 normalInterval（考虑 speedFactor）
    private int effectiveFastInterval;           // 实际使用的 fastInterval（考虑 speedFactor）
    private int memoryThreshold;
    private int memoryWarning;
    private int highDelayThreshold;
    private int highDelayConnOkExtraMs;
    private int highDelaySwitchConsecutiveConnOk;
    private int highDelaySwitchConsecutiveConnUnknown;
    private int closeWaitThresholdCore;
    private int closeWaitConsecutive;
    private int autoSwitchMaxPer10Min;
    private int autoRestartMaxPer10Min;
    private int autoRestartMinIntervalSeconds;
    private int switchStormSuppressSeconds;
    private int restartStormSuppressSeconds;
    private int blacklistMinutes;
    private int proxyTestTimeoutMs;              // TestProxy 超时（ms）

    // 连接性探测（用于区分“高延迟但可用” vs “高延迟且不可用/极慢”）
    private string[] connectivityTestUrls;
    private int connectivityProbeTimeoutMs;
    private int connectivityProbeMinSuccessCount;
    private int connectivitySlowThresholdMs;
    private int connectivityProbeMinIntervalSeconds;
    private int connectivityResultMaxAgeSeconds;
    private bool postMatchGuardEnabled;
    private int postMatchGuardSeconds;
    private bool matchFreezeAutoActions;
    private bool matchPinNodeEnabled;
    private bool steamTakeoverCompensateOnPostMatch;

    private string[] coreProcessNames;
    private string[] clientProcessNames;
    private string[] clientProcessNamesExpanded; // 归一化+扩展变体后的进程名列表（用于更可靠的跟随/检测）
    private string[] clientPaths;
    private string[] excludeRegions;  // 可配置的节点排除规则

    // 静音策略：默认不允许自动启动/重启 Clash 客户端（避免弹出 UI 干扰用户）
    private bool allowAutoStartClient = false;

    // 节点禁用（显式名单优先；若 config 中不存在 disabledNodes 则退回到 excludeRegions 关键字模式）
    private HashSet<string> disabledNodes = new HashSet<string>();
    private bool disabledNodesExplicitMode = false;

    // 节点偏好（自动切换时优先考虑；当偏好节点不可用时仍会回退到其他节点）
    private HashSet<string> preferredNodes = new HashSet<string>();

    // 订阅级自动切换（Clash Verge Rev，默认关闭）
    private bool autoSwitchSubscription = false;
    private int subscriptionSwitchThreshold = 3;
    private int subscriptionSwitchCooldownMinutes = 15;
    private string[] subscriptionWhitelist = new string[0];

    // 当前检测到的进程信息（volatile 保证跨线程可见性）
    private volatile string detectedCoreName = "";
    private volatile string detectedClientPath = "";

    // ==================== UI 组件 ====================
    private NotifyIcon trayIcon;
    private Label statusLabel, memLabel, proxyLabel, logLabel, checkLabel, stableLabel;
    private Button restartBtn, exitBtn, pauseBtn, followBtn, uuRouteBtn;
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
    private int highDelayCount = 0;
    private int closeWaitFailCount = 0;
    private int totalFails = 0;
    private int totalIssues = 0; // 只统计“问题段落次数”（正常->异常记1次）
    private int consecutiveOK = 0;
    private int cooldownCount = 0;
    private int autoSwitchEpisodeAttempts = 0;              // 订阅切换 episode：切换后仍未恢复的次数（UI 线程）
    private bool pendingSwitchVerification = false;         // 切换后延迟验证挂起（UI 线程）
    private DateTime pendingVerifyAt = DateTime.MinValue;   // 计划验证时间（UI 线程）
    private bool lastHadIssue = false;
    private DateTime lastStableTime;
    private DateTime startTime;

    // 跨线程字符串字段（volatile 保证可见性）
    private volatile string currentNode = "";
    private volatile string nodeGroup = "";

    // 跨线程数值字段（使用 Interlocked 读写）
    private int lastDelay = 0;
    private int lastNodeDelay = 0; // 节点延迟（history/live），仅用于辅助展示/诊断
    private volatile string lastNodeDelayKind = ""; // "histDelay"/"liveDelay"

    private Dictionary<string, DateTime> nodeBlacklist = new Dictionary<string, DateTime>();
    private int[] lastTcpStats = new int[] { 0, 0, 0 };

    // ==================== 线程安全设施 ====================
    private readonly object blacklistLock = new object();  // nodeBlacklist 专用锁
    private readonly object restartLock = new object();    // RestartClash 并发门闩
    private readonly object disabledNodesLock = new object(); // disabledNodes 跨线程读写
    private readonly object preferredNodesLock = new object(); // preferredNodes 跨线程读写
    private readonly object configLock = new object();        // 串行化写 config.json，避免字段互相覆盖
    private readonly object subscriptionLock = new object();  // 订阅切换门闩
    private volatile bool _isSwitchingSubscription = false;
    private long lastSubscriptionSwitchTicks = 0;            // DateTime.Ticks (Interlocked 读写)

    // 自动切节点节流 + 升级计数器（跨线程：用 Interlocked / volatile）
    private int _isSwitchingNode = 0;                        // 0=空闲 1=切换中
    private long lastAutoSwitchTicks = 0;                    // DateTime.Ticks (Interlocked 读写)
    private int autoSwitchNoGoodNodeStreak = 0;              // 连续“无可用低延迟节点/延迟超时”失败次数
    private long lastAutoSwitchNoGoodNodeTicks = 0;          // 上次失败时间（用于窗口重置）
    private long lastAutoSwitchNoGoodNodeLogTicks = 0;       // 日志节流
    private long lastAutoSwitchNoGoodNodeEscalateTicks = 0;  // 升级节流

    // 订阅健康探测（跨线程：用 Interlocked / volatile）
    private int _isSubscriptionProbeRunning = 0;             // 0=空闲 1=探测中
    private long lastSubscriptionProbeStartTicks = 0;        // DateTime.Ticks (Interlocked 读写)
    private long subscriptionProbeId = 0;                    // 探测任务递增 ID
    private int subscriptionProbeVerdict = 0;                // SubscriptionProbeVerdict (int) (Interlocked 读写)
    private long subscriptionProbeUpdatedTicks = 0;          // 最近更新时间（用于 age 判断）
    private long subscriptionProbeSubSwitchTicks = 0;        // 启动探测时的 lastSubscriptionSwitchTicks 快照
    private int subscriptionProbeProbed = 0;                 // 已探测节点数
    private int subscriptionProbeReachable = 0;              // 可达节点数（delay>0 且 <SEVERE_DELAY_MS）
    private int subscriptionProbeBestDelay = 0;              // 最佳 delay（Interlocked 更新）
    private volatile string subscriptionProbeGroup = "";     // 探测时的 selector group
    private volatile string subscriptionProbeBestNode = "";  // 最佳节点名
    private int subscriptionProbeCursor = 0;                 // round-robin cursor（候选节点轮转）
    private int _isChecking = 0;                           // 0=空闲, 1=检测中; Interlocked 操作
    private int _isRestartQueued = 0;                     // 0=空闲, 1=已排队; 防止重复排队重启任务
    private volatile bool _isRestarting = false;           // 重启进行中标志（阻止 CheckStatus 并发）
    private int _didFirstCheck = 0;                        // 防止 HandleCreated 多次触发导致重复首次检测

    private bool monitorHasConnectivityColumns = false;    // 仅对新生成的 monitor_YYYYMMDD.csv 为 true

    // ==================== 控制：暂停检测 ====================
    // 暂停整个检测循环（Timer 停止），不会再自动切换/重启；手动操作仍可执行
    private volatile bool _isDetectionPaused = false;

    // Follow Clash exit monitor (enabled only in --follow-clash mode)
    private static bool s_followClashMode = false;
    private System.Windows.Forms.Timer followExitTimer;
    private DateTime followMissingSince = DateTime.MinValue;
    private bool _followInitialHideDone = false;

    // ==================== 构造函数 ====================
    public ClashGuardian()
    {
        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        startTime = DateTime.Now;
        lastStableTime = DateTime.Now;

        InitRuntimePaths();

        LoadConfigFast();

        ThreadPool.QueueUserWorkItem(_ => CleanOldLogs());

        // 新版本的监控 CSV 会追加连接性列；旧文件不强制升级头部，保持兼容
        if (!File.Exists(dataFile)) {
            File.WriteAllText(dataFile, "Time,ProxyOK,Delay,MemMB,Handles,TimeWait,Established,CloseWait,Node,Event,ConnVerdict,ConnBestRttMs,ConnAgeSec,ConnSuccess,ConnAttempts\n");
            monitorHasConnectivityColumns = true;
        } else {
            monitorHasConnectivityColumns = false;
            try {
                using (StreamReader sr = new StreamReader(dataFile, Encoding.UTF8, true)) {
                    string header = sr.ReadLine() ?? "";
                    if (header.IndexOf("ConnVerdict", StringComparison.OrdinalIgnoreCase) >= 0) monitorHasConnectivityColumns = true;
                }
            } catch { /* ignore */ }
        }

        InitializeUI();
        InitializeTrayIcon();

        timer = new System.Windows.Forms.Timer();
        timer.Interval = effectiveNormalInterval;
        timer.Tick += CheckStatus;
        timer.Start();

        Log("守护启动 Pro");

        // 配置安全补全（不阻塞启动；不会引入 disabledNodes 等语义改变字段）
        ThreadPool.QueueUserWorkItem(_ => BackfillConfigIfMissing());

        // 首次检测：等句柄创建后再执行，避免 BeginInvoke 句柄未创建异常
        this.HandleCreated += (s, e) => {
            if (Interlocked.CompareExchange(ref _didFirstCheck, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ => DoFirstCheck());
        };
        // 极少数情况下句柄可能已创建（例如被某些组件提前触发）；此时直接触发一次
        if (this.IsHandleCreated) {
            if (Interlocked.CompareExchange(ref _didFirstCheck, 1, 0) == 0) {
                ThreadPool.QueueUserWorkItem(_ => DoFirstCheck());
            }
        }
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

            if (this.IsHandleCreated) {
                this.BeginInvoke((Action)(() => {
                    string delayStr = delay > 0 ? delay + "ms" : "--";
                    string coreShort = string.IsNullOrEmpty(detectedCoreName) ? "未检测" : detectedCoreName;
                    memLabel.Text = "内  核:  " + coreShort + "  |  " + mem.ToString("F1") + "MB  |  句柄: " + handles;

                    string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "--" : SafeNodeName(currentNode);
                    string nodeShort = TruncateNodeName(nodeDisplay);
                    proxyLabel.Text = "代  理:  " + (proxyOK ? "OK" : "X") + " " + delayStr + " | " + nodeShort;
                    proxyLabel.ForeColor = proxyOK ? COLOR_OK : COLOR_ERROR;

                    statusLabel.Text = "● 状态: 运行中";
                    statusLabel.ForeColor = COLOR_OK;

                    checkLabel.Text = "统  计:  问题 0  |  重启 0  |  切换 0  |  黑名单 0";
                    stableLabel.Text = "稳定性:  连续 0s  |  运行 0s  |  问题 0";

                    if (!string.IsNullOrEmpty(detectedCoreName)) {
                        Log("检测到内核: " + detectedCoreName);
                    }
                }));
            }

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

    int LoadIntConfigWithClamp(string json, string key, int currentValue, int min, int max) {
        bool parsed;
        string raw = GetJsonValue(json, key, currentValue.ToString());
        int parsedValue = TryParseInt(raw, currentValue, out parsed);
        int fixedValue = ClampInt(parsedValue, min, max);
        if (!parsed || fixedValue != parsedValue) Log("配置修正: " + key + " " + raw + " -> " + fixedValue);
        return fixedValue;
    }

    void LoadConfigFast() {
        clashApi = "http://127.0.0.1:" + DEFAULT_API_PORT;
        clashSecret = "set-your-secret";
        proxyPort = DEFAULT_PROXY_PORT;
        normalInterval = DEFAULT_NORMAL_INTERVAL;
        fastInterval = DEFAULT_FAST_INTERVAL;
        speedFactor = 3;
        allowAutoStartClient = false;
        memoryThreshold = DEFAULT_MEMORY_THRESHOLD;
        memoryWarning = DEFAULT_MEMORY_WARNING;
        highDelayThreshold = DEFAULT_HIGH_DELAY;
        highDelayConnOkExtraMs = DEFAULT_HIGH_DELAY_CONN_OK_EXTRA_MS;
        highDelaySwitchConsecutiveConnOk = DEFAULT_HIGH_DELAY_SWITCH_CONSEC_CONN_OK;
        highDelaySwitchConsecutiveConnUnknown = DEFAULT_HIGH_DELAY_SWITCH_CONSEC_CONN_UNKNOWN;
        closeWaitThresholdCore = DEFAULT_CLOSEWAIT_THRESHOLD_CORE;
        closeWaitConsecutive = DEFAULT_CLOSEWAIT_CONSECUTIVE;
        autoSwitchMaxPer10Min = DEFAULT_AUTO_SWITCH_MAX_PER_10MIN;
        autoRestartMaxPer10Min = DEFAULT_AUTO_RESTART_MAX_PER_10MIN;
        autoRestartMinIntervalSeconds = DEFAULT_AUTO_RESTART_MIN_INTERVAL_SECONDS;
        switchStormSuppressSeconds = DEFAULT_SWITCH_STORM_SUPPRESS_SECONDS;
        restartStormSuppressSeconds = DEFAULT_RESTART_STORM_SUPPRESS_SECONDS;
        blacklistMinutes = DEFAULT_BLACKLIST_MINUTES;
        proxyTestTimeoutMs = PROXY_TEST_TIMEOUT;

        connectivityTestUrls = (string[])DEFAULT_CONNECTIVITY_TEST_URLS.Clone();
        connectivityProbeTimeoutMs = 3000;
        connectivityProbeMinSuccessCount = 1;
        connectivitySlowThresholdMs = 800;
        connectivityProbeMinIntervalSeconds = 15;
        connectivityResultMaxAgeSeconds = 30;
        postMatchGuardEnabled = DEFAULT_POST_MATCH_GUARD_ENABLED;
        postMatchGuardSeconds = DEFAULT_POST_MATCH_GUARD_SECONDS;
        matchFreezeAutoActions = DEFAULT_MATCH_FREEZE_AUTO_ACTIONS;
        matchPinNodeEnabled = DEFAULT_MATCH_PIN_NODE_ENABLED;
        steamTakeoverCompensateOnPostMatch = DEFAULT_STEAM_TAKEOVER_COMPENSATE_ON_POST_MATCH;

        coreProcessNames = DEFAULT_CORE_NAMES;
        clientProcessNames = DEFAULT_CLIENT_NAMES;
        clientProcessNamesExpanded = NormalizeAndExpandProcessNamesStatic(clientProcessNames);
        clientPaths = GetDefaultClientPaths();
        excludeRegions = DEFAULT_EXCLUDE_REGIONS;

        if (File.Exists(configFile)) {
            try {
                string json = File.ReadAllText(configFile, Encoding.UTF8);

                clashApi = GetJsonValue(json, "clashApi", clashApi);
                if (clashApi == null) clashApi = "";
                clashApi = clashApi.Trim().TrimEnd('/');

                clashSecret = GetJsonValue(json, "clashSecret", clashSecret);

                proxyPort = LoadIntConfigWithClamp(json, "proxyPort", proxyPort, 1, 65535);
                normalInterval = LoadIntConfigWithClamp(json, "normalInterval", normalInterval, 500, 600000);

                bool parsed;
                string rawFastInterval = GetJsonValue(json, "fastInterval", fastInterval.ToString());
                int fastIntervalParsed = TryParseInt(rawFastInterval, fastInterval, out parsed);
                int fastIntervalFixed = ClampInt(fastIntervalParsed, 200, normalInterval);
                if (!parsed || fastIntervalFixed != fastIntervalParsed) Log("閰嶇疆淇: fastInterval " + rawFastInterval + " -> " + fastIntervalFixed);
                fastInterval = fastIntervalFixed;

                speedFactor = LoadIntConfigWithClamp(json, "speedFactor", speedFactor, 1, 5);

                string rawAllowAutoStartClient = GetJsonValue(json, "allowAutoStartClient", allowAutoStartClient ? "true" : "false");
                bool allowClient;
                if (bool.TryParse(rawAllowAutoStartClient, out allowClient)) allowAutoStartClient = allowClient;

                memoryThreshold = LoadIntConfigWithClamp(json, "memoryThreshold", memoryThreshold, 10, 4096);
                memoryWarning = LoadIntConfigWithClamp(json, "memoryWarning", memoryWarning, 10, 4096);
                highDelayThreshold = LoadIntConfigWithClamp(json, "highDelayThreshold", highDelayThreshold, 50, 10000);
                highDelayConnOkExtraMs = LoadIntConfigWithClamp(json, "highDelayConnOkExtraMs", highDelayConnOkExtraMs, 0, 5000);
                highDelaySwitchConsecutiveConnOk = LoadIntConfigWithClamp(json, "highDelaySwitchConsecutiveConnOk", highDelaySwitchConsecutiveConnOk, 1, 10);
                highDelaySwitchConsecutiveConnUnknown = LoadIntConfigWithClamp(json, "highDelaySwitchConsecutiveConnUnknown", highDelaySwitchConsecutiveConnUnknown, 1, 10);
                blacklistMinutes = LoadIntConfigWithClamp(json, "blacklistMinutes", blacklistMinutes, 1, 1440);
                closeWaitThresholdCore = LoadIntConfigWithClamp(json, "closeWaitThresholdCore", closeWaitThresholdCore, 1, 100000);
                closeWaitConsecutive = LoadIntConfigWithClamp(json, "closeWaitConsecutive", closeWaitConsecutive, 1, 10);
                autoSwitchMaxPer10Min = LoadIntConfigWithClamp(json, "autoSwitchMaxPer10Min", autoSwitchMaxPer10Min, 1, 120);
                autoRestartMaxPer10Min = LoadIntConfigWithClamp(json, "autoRestartMaxPer10Min", autoRestartMaxPer10Min, 1, 60);
                autoRestartMinIntervalSeconds = LoadIntConfigWithClamp(json, "autoRestartMinIntervalSeconds", autoRestartMinIntervalSeconds, 0, 600);
                switchStormSuppressSeconds = LoadIntConfigWithClamp(json, "switchStormSuppressSeconds", switchStormSuppressSeconds, 5, 3600);
                restartStormSuppressSeconds = LoadIntConfigWithClamp(json, "restartStormSuppressSeconds", restartStormSuppressSeconds, 5, 3600);

                // 只影响极端误配：保持阈值一致性，避免配置导致异常行为
                memoryWarning = ClampInt(memoryWarning, 10, 4096);
                if (memoryThreshold < memoryWarning) {
                    Log("配置修正: memoryThreshold " + memoryThreshold + " -> " + memoryWarning + " (>=memoryWarning)");
                    memoryThreshold = memoryWarning;
                }
                fastInterval = ClampInt(fastInterval, 200, normalInterval);

                proxyTestTimeoutMs = LoadIntConfigWithClamp(json, "proxyTestTimeoutMs", proxyTestTimeoutMs, 200, 10000);

                List<string> customCores = GetJsonStringArray(json, "coreProcessNames");
                if (customCores.Count > 0) coreProcessNames = customCores.ToArray();

                List<string> customClients = GetJsonStringArray(json, "clientProcessNames");
                if (customClients.Count > 0) clientProcessNames = customClients.ToArray();
                clientProcessNamesExpanded = NormalizeAndExpandProcessNamesStatic(clientProcessNames);

                List<string> excludes = GetJsonStringArray(json, "excludeRegions");
                if (excludes.Count > 0) excludeRegions = excludes.ToArray();

                // 节点禁用名单（显式模式：disabledNodes 存在则以其为准；否则走 excludeRegions 关键字）
                disabledNodesExplicitMode = ConfigHasKey(json, "disabledNodes");
                if (disabledNodesExplicitMode) {
                    List<string> dn = GetJsonStringArray(json, "disabledNodes");
                    lock (disabledNodesLock) {
                        disabledNodes.Clear();
                        foreach (string n in dn) {
                            if (!string.IsNullOrEmpty(n)) disabledNodes.Add(n);
                        }
                    }
                }

                // 偏好节点（仅影响自动切换的优先级；若同时出现在 disabledNodes，则以禁用为准）
                List<string> pn = GetJsonStringArray(json, "preferredNodes");
                if (pn.Count > 0) {
                    lock (preferredNodesLock) {
                        preferredNodes.Clear();
                        foreach (string n in pn) {
                            if (!string.IsNullOrEmpty(n)) preferredNodes.Add(n);
                        }
                    }
                    if (disabledNodesExplicitMode) {
                        lock (disabledNodesLock) {
                            lock (preferredNodesLock) {
                                foreach (string n in disabledNodes) {
                                    if (!string.IsNullOrEmpty(n)) preferredNodes.Remove(n);
                                }
                            }
                        }
                    }
                }

                // 订阅级自动切换（Clash Verge Rev）
                string rawAutoSub = GetJsonValue(json, "autoSwitchSubscription", autoSwitchSubscription ? "true" : "false");
                bool autoSub;
                if (bool.TryParse(rawAutoSub, out autoSub)) autoSwitchSubscription = autoSub;

                subscriptionSwitchThreshold = LoadIntConfigWithClamp(json, "subscriptionSwitchThreshold", subscriptionSwitchThreshold, 1, 10);
                subscriptionSwitchCooldownMinutes = LoadIntConfigWithClamp(json, "subscriptionSwitchCooldownMinutes", subscriptionSwitchCooldownMinutes, 1, 1440);

                List<string> wl = GetJsonStringArray(json, "subscriptionWhitelist");
                subscriptionWhitelist = wl.Count > 0 ? wl.ToArray() : new string[0];

                // 连接性探测（用于订阅切换前的综合判断）
                List<string> urls = GetJsonStringArray(json, "connectivityTestUrls");
                if (urls.Count > 0) connectivityTestUrls = urls.ToArray();

                connectivityProbeTimeoutMs = LoadIntConfigWithClamp(json, "connectivityProbeTimeoutMs", connectivityProbeTimeoutMs, 300, 30000);
                connectivityProbeMinSuccessCount = LoadIntConfigWithClamp(json, "connectivityProbeMinSuccessCount", connectivityProbeMinSuccessCount, 1, 10);
                connectivitySlowThresholdMs = LoadIntConfigWithClamp(json, "connectivitySlowThresholdMs", connectivitySlowThresholdMs, 50, 20000);
                connectivityProbeMinIntervalSeconds = LoadIntConfigWithClamp(json, "connectivityProbeMinIntervalSeconds", connectivityProbeMinIntervalSeconds, 1, 600);
                connectivityResultMaxAgeSeconds = LoadIntConfigWithClamp(json, "connectivityResultMaxAgeSeconds", connectivityResultMaxAgeSeconds, 1, 600);
                postMatchGuardSeconds = LoadIntConfigWithClamp(json, "postMatchGuardSeconds", postMatchGuardSeconds, 15, 300);

                string rawPostMatchGuardEnabled = GetJsonValue(json, "postMatchGuardEnabled", postMatchGuardEnabled ? "true" : "false");
                bool parsedPostMatchGuardEnabled;
                if (bool.TryParse(rawPostMatchGuardEnabled, out parsedPostMatchGuardEnabled)) postMatchGuardEnabled = parsedPostMatchGuardEnabled;

                string rawMatchFreezeAutoActions = GetJsonValue(json, "matchFreezeAutoActions", matchFreezeAutoActions ? "true" : "false");
                bool parsedMatchFreezeAutoActions;
                if (bool.TryParse(rawMatchFreezeAutoActions, out parsedMatchFreezeAutoActions)) matchFreezeAutoActions = parsedMatchFreezeAutoActions;

                string rawMatchPinNodeEnabled = GetJsonValue(json, "matchPinNodeEnabled", matchPinNodeEnabled ? "true" : "false");
                bool parsedMatchPinNodeEnabled;
                if (bool.TryParse(rawMatchPinNodeEnabled, out parsedMatchPinNodeEnabled)) matchPinNodeEnabled = parsedMatchPinNodeEnabled;

                string rawSteamTakeoverCompensateOnPostMatch = GetJsonValue(json, "steamTakeoverCompensateOnPostMatch", steamTakeoverCompensateOnPostMatch ? "true" : "false");
                bool parsedSteamTakeoverCompensateOnPostMatch;
                if (bool.TryParse(rawSteamTakeoverCompensateOnPostMatch, out parsedSteamTakeoverCompensateOnPostMatch)) steamTakeoverCompensateOnPostMatch = parsedSteamTakeoverCompensateOnPostMatch;

                // 从配置文件恢复上次检测到的客户端路径
                string savedClientPath = GetJsonValue(json, "clientPath", "");
                if (!string.IsNullOrEmpty(savedClientPath) && File.Exists(savedClientPath)) {
                    detectedClientPath = savedClientPath;
                }
            } catch (Exception ex) { Log("配置加载异常: " + ex.Message); }
        } else {
            ThreadPool.QueueUserWorkItem(_ => SaveDefaultConfig());
        }

        ComputeEffectiveIntervals();
    }

    void ComputeEffectiveIntervals() {
        int sf = speedFactor;
        if (sf < 1) sf = 1;

        // Keep base interval values in config, but use effective intervals for runtime.
        effectiveNormalInterval = ClampInt(normalInterval / sf, 300, 600000);
        effectiveFastInterval = ClampInt(fastInterval / sf, 200, effectiveNormalInterval);
    }

    bool RunSteamTakeoverCompensationOnPostMatch(string phaseTag) {
        if (!steamTakeoverCompensateOnPostMatch) return true;
        try {
            UuWatcherContext ctx = new UuWatcherContext();
            string apiBase = clashApi;
            if (string.IsNullOrEmpty(apiBase)) apiBase = UU_WATCHER_DEFAULT_API;
            apiBase = apiBase.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(apiBase)) apiBase = UU_WATCHER_DEFAULT_API;
            ctx.ApiBase = apiBase;
            ctx.ApiSecret = string.IsNullOrEmpty(clashSecret) ? UU_WATCHER_DEFAULT_SECRET : clashSecret;
            ctx.IsAdmin = IsCurrentProcessAdminStatic();
            ctx.LogFile = logFile;

            UuWatcherState state = LoadUuWatcherState(ctx);
            string switchId = state.SwitchId ?? "";

            int total;
            int closed = DrainMihomoTargetConnectionsStatic(
                ctx,
                UU_WATCHER_TARGET_PROCESS_NAMES,
                UU_WATCHER_TAKEOVER_DRAIN_MAX_COMPENSATION,
                false,
                out total);
            Log("UU_TAKEOVER_ONE_SHOT_DRAIN phase=" + (string.IsNullOrEmpty(phaseTag) ? "postMatch" : phaseTag)
                + " closed=" + closed
                + " total=" + total
                + " limit=" + UU_WATCHER_TAKEOVER_DRAIN_MAX_COMPENSATION
                + " proxyOnly=false");

            try { Thread.Sleep(UU_WATCHER_TAKEOVER_RECHECK_DELAY_MS); } catch { /* ignore */ }

            List<UuWatcherLocalProxySignal> hits = GetLocalProxyFaultSignalsStatic(ctx, UU_WATCHER_TARGET_PROCESS_NAMES);
            int steam7897 = CountLocalProxyFaultForProcessesStatic(hits, "steam.exe", "steamwebhelper.exe");
            int tsl7897 = CountLocalProxyFaultForProcessesStatic(hits, "tslgame.exe");
            if (steam7897 > 0) {
                string routeNow = UuGetRouteNow(ctx);
                Log("[ALERT] STEAM_7897_RESIDUAL_DURING_POST_MATCH switchId=" + switchId
                    + " steam7897Count=" + steam7897
                    + " tsl7897Count=" + tsl7897
                    + " hardIsolationUnavailable=" + state.HardIsolationUnavailable
                    + " routeNow=" + routeNow);
                return false;
            }
            return true;
        } catch (Exception ex) {
            Log("post-match Steam compensation failed: " + ex.Message);
            return false;
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
        string[] names = (clientProcessNamesExpanded != null && clientProcessNamesExpanded.Length > 0)
            ? clientProcessNamesExpanded
            : clientProcessNames;
        foreach (string clientName in names) {
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

    bool UpdateConfigJson(Func<string, string> transform, string opName) {
        if (transform == null) return false;
        try {
            if (!File.Exists(configFile)) {
                SaveDefaultConfig();
            }

            lock (configLock) {
                if (!File.Exists(configFile)) return false;
                string json = File.ReadAllText(configFile, Encoding.UTF8);
                string updated = transform(json);
                if (string.IsNullOrEmpty(updated)) return false;

                if (!string.Equals(updated, json, StringComparison.Ordinal)) {
                    File.WriteAllText(configFile, updated, Encoding.UTF8);
                }
            }
            return true;
        } catch (Exception ex) {
            string tag = string.IsNullOrEmpty(opName) ? "" : "(" + opName + ")";
            Log("保存配置失败" + tag + ": " + ex.Message);
            return false;
        }
    }

    static string UpsertJsonFieldLiteral(string json, string key, string rawValueLiteral) {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(rawValueLiteral)) return json;
        string keyTag = "\"" + key + "\"";
        int keyIdx = json.IndexOf(keyTag, StringComparison.Ordinal);
        if (keyIdx >= 0) {
            int colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return json;

            int valueStart = colon + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;

            int valueEnd = valueStart;
            bool inStr = false;
            bool escape = false;
            while (valueEnd < json.Length) {
                char c = json[valueEnd];
                if (escape) { escape = false; valueEnd++; continue; }
                if (inStr && c == '\\') { escape = true; valueEnd++; continue; }
                if (c == '"') { inStr = !inStr; valueEnd++; continue; }
                if (!inStr && (c == ',' || c == '}')) break;
                valueEnd++;
            }
            return json.Substring(0, valueStart) + rawValueLiteral + json.Substring(valueEnd);
        }

        int lastBrace = json.LastIndexOf('}');
        if (lastBrace <= 0) return json;
        int p = lastBrace - 1;
        while (p >= 0 && char.IsWhiteSpace(json[p])) p--;
        bool needComma = p >= 0 && json[p] != '{' && json[p] != ',';
        string insert = (needComma ? "," : "") + "\n  " + keyTag + ": " + rawValueLiteral + "\n";
        return json.Substring(0, lastBrace) + insert + json.Substring(lastBrace);
    }

    static string BuildJsonArrayLiteral(IList<string> values) {
        StringBuilder sb = new StringBuilder();
        sb.Append("[");
        if (values != null) {
            for (int i = 0; i < values.Count; i++) {
                if (i > 0) sb.Append(", ");
                sb.Append("\"").Append(EscapeJsonString(values[i] ?? "")).Append("\"");
            }
        }
        sb.Append("]");
        return sb.ToString();
    }

    // 将检测到的客户端路径持久化到 config.json
    void SaveClientPath() {
        if (string.IsNullOrEmpty(detectedClientPath)) return;
        string escaped = EscapeJsonString(detectedClientPath);
        UpdateConfigJson(
            json => UpsertJsonFieldLiteral(json, "clientPath", "\"" + escaped + "\""),
            "clientPath");
    }

    void SaveDisabledNodes() {
        try {
            List<string> snapshot = new List<string>();
            lock (disabledNodesLock) {
                foreach (string n in disabledNodes) {
                    if (!string.IsNullOrEmpty(n)) snapshot.Add(n);
                }
            }
            snapshot.Sort(StringComparer.OrdinalIgnoreCase);
            string arr = BuildJsonArrayLiteral(snapshot);
            UpdateConfigJson(
                json => UpsertJsonFieldLiteral(json, "disabledNodes", arr),
                "disabledNodes");
        } catch (Exception ex) {
            Log("保存禁用名单失败: " + ex.Message);
        }
    }

    void SavePreferredNodes() {
        try {
            List<string> snapshot = new List<string>();
            lock (preferredNodesLock) {
                foreach (string n in preferredNodes) {
                    if (!string.IsNullOrEmpty(n)) snapshot.Add(n);
                }
            }
            snapshot.Sort(StringComparer.OrdinalIgnoreCase);
            string arr = BuildJsonArrayLiteral(snapshot);
            UpdateConfigJson(
                json => UpsertJsonFieldLiteral(json, "preferredNodes", arr),
                "preferredNodes");
        } catch (Exception ex) {
            Log("保存偏好节点失败: " + ex.Message);
        }
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

    bool ConfigHasKey(string json, string key) {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
        return json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
    }

    static string EscapeJsonString(string s) {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder(s.Length + 8);
        foreach (char c in s) {
            switch (c) {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append(' ');
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // 轻量 JSON 字符串数组解析：["a","b"] -> List<string>
    List<string> GetJsonStringArray(string json, string key) {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return result;

        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return result;

        idx = json.IndexOf('[', idx);
        if (idx < 0) return result;

        int i = idx + 1;
        bool inString = false;
        bool escape = false;
        StringBuilder sb = new StringBuilder();

        while (i < json.Length) {
            char c = json[i];

            if (!inString) {
                if (c == ']') break;
                if (c == '"') { inString = true; sb.Length = 0; }
                i++;
                continue;
            }

            if (escape) {
                escape = false;
                switch (c) {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < json.Length) {
                            try {
                                string hex = json.Substring(i + 1, 4);
                                int code = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                                sb.Append((char)code);
                                i += 4;
                            } catch { /* ignore invalid */ }
                        }
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
                i++;
                continue;
            }

            if (c == '\\') { escape = true; i++; continue; }
            if (c == '"') {
                inString = false;
                result.Add(sb.ToString());
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return result;
    }

    static int FindMatchingArrayBracket(string json, int arrStart) {
        if (string.IsNullOrEmpty(json) || arrStart < 0 || arrStart >= json.Length) return -1;
        bool inString = false;
        bool escape = false;
        int depth = 0;
        for (int i = arrStart; i < json.Length; i++) {
            char c = json[i];
            if (escape) { escape = false; continue; }
            if (inString && c == '\\') { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '[') depth++;
            else if (c == ']') {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    void SaveDefaultConfig() {
        string coreNames = string.Join("\", \"", DEFAULT_CORE_NAMES);
        string clientNames = string.Join("\", \"", DEFAULT_CLIENT_NAMES);
        string excludeNames = string.Join("\", \"", DEFAULT_EXCLUDE_REGIONS);

        string connUrls = string.Join("\", \"", DEFAULT_CONNECTIVITY_TEST_URLS);

        string config = "{\n" +
            "  \"clashApi\": \"" + clashApi + "\",\n" +
            "  \"clashSecret\": \"" + clashSecret + "\",\n" +
            "  \"proxyPort\": " + proxyPort + ",\n" +
            "  \"normalInterval\": " + normalInterval + ",\n" +
            "  \"fastInterval\": " + fastInterval + ",\n" +
            "  \"speedFactor\": " + speedFactor + ",\n" +
            "  \"allowAutoStartClient\": " + (allowAutoStartClient ? "true" : "false") + ",\n" +
            "  \"memoryThreshold\": " + memoryThreshold + ",\n" +
            "  \"memoryWarning\": " + memoryWarning + ",\n" +
            "  \"highDelayThreshold\": " + highDelayThreshold + ",\n" +
            "  \"highDelayConnOkExtraMs\": " + highDelayConnOkExtraMs + ",\n" +
            "  \"highDelaySwitchConsecutiveConnOk\": " + highDelaySwitchConsecutiveConnOk + ",\n" +
            "  \"highDelaySwitchConsecutiveConnUnknown\": " + highDelaySwitchConsecutiveConnUnknown + ",\n" +
            "  \"closeWaitThresholdCore\": " + closeWaitThresholdCore + ",\n" +
            "  \"closeWaitConsecutive\": " + closeWaitConsecutive + ",\n" +
            "  \"autoSwitchMaxPer10Min\": " + autoSwitchMaxPer10Min + ",\n" +
            "  \"autoRestartMaxPer10Min\": " + autoRestartMaxPer10Min + ",\n" +
            "  \"autoRestartMinIntervalSeconds\": " + autoRestartMinIntervalSeconds + ",\n" +
            "  \"switchStormSuppressSeconds\": " + switchStormSuppressSeconds + ",\n" +
            "  \"restartStormSuppressSeconds\": " + restartStormSuppressSeconds + ",\n" +
            "  \"blacklistMinutes\": " + blacklistMinutes + ",\n" +
            "  \"proxyTestTimeoutMs\": " + proxyTestTimeoutMs + ",\n" +
            "  \"connectivityTestUrls\": [\"" + connUrls + "\"],\n" +
            "  \"connectivityProbeTimeoutMs\": " + connectivityProbeTimeoutMs + ",\n" +
            "  \"connectivityProbeMinSuccessCount\": " + connectivityProbeMinSuccessCount + ",\n" +
            "  \"connectivitySlowThresholdMs\": " + connectivitySlowThresholdMs + ",\n" +
            "  \"connectivityProbeMinIntervalSeconds\": " + connectivityProbeMinIntervalSeconds + ",\n" +
            "  \"connectivityResultMaxAgeSeconds\": " + connectivityResultMaxAgeSeconds + ",\n" +
            "  \"postMatchGuardEnabled\": " + (postMatchGuardEnabled ? "true" : "false") + ",\n" +
            "  \"postMatchGuardSeconds\": " + postMatchGuardSeconds + ",\n" +
            "  \"matchFreezeAutoActions\": " + (matchFreezeAutoActions ? "true" : "false") + ",\n" +
            "  \"matchPinNodeEnabled\": " + (matchPinNodeEnabled ? "true" : "false") + ",\n" +
            "  \"steamTakeoverCompensateOnPostMatch\": " + (steamTakeoverCompensateOnPostMatch ? "true" : "false") + ",\n" +
            "  \"autoSwitchSubscription\": false,\n" +
            "  \"subscriptionSwitchThreshold\": 3,\n" +
            "  \"subscriptionSwitchCooldownMinutes\": 15,\n" +
            "  \"subscriptionWhitelist\": [],\n" +
            "  \"coreProcessNames\": [\"" + coreNames + "\"],\n" +
            "  \"clientProcessNames\": [\"" + clientNames + "\"],\n" +
            "  \"excludeRegions\": [\"" + excludeNames + "\"],\n" +
            "  \"disabledNodes\": [],\n" +
            "  \"preferredNodes\": []\n" +
            "}";
        try {
            lock (configLock) { File.WriteAllText(configFile, config, Encoding.UTF8); }
        }
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

    protected override void SetVisibleCore(bool value) {
        // In follow mode, keep fully silent on startup: no main window pop.
        if (s_followClashMode && !_followInitialHideDone && value) {
            _followInitialHideDone = true;
            base.SetVisibleCore(false);
            return;
        }
        base.SetVisibleCore(value);
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

        bool watch = false;
        bool watchUuRoute = false;
        bool installUuRouteTask = false;
        bool repairUuRouteStartup = false;
        bool follow = false;
        if (args != null) {
            for (int i = 0; i < args.Length; i++) {
                string a = args[i];
                if (a == "--watch-clash") watch = true;
                else if (a == "--watch-uu-route") watchUuRoute = true;
                else if (a == "--install-uu-route-task") installUuRouteTask = true;
                else if (a == "--repair-uu-route-startup") repairUuRouteStartup = true;
                else if (a == "--follow-clash") follow = true;
            }
        }

        if (installUuRouteTask || repairUuRouteStartup) {
            int code = InstallOrRepairUuRouteTaskStatic(true);
            try { Environment.Exit(code); } catch { /* ignore */ }
            return;
        }

        if (watch) {
            RunClashWatcherLoop();
            return;
        }
        if (watchUuRoute) {
            RunUuRouteWatcherLoop();
            return;
        }

        s_followClashMode = follow;

        bool createdNew;
        using (Mutex mutex = new Mutex(true, "ClashGuardianSingleInstance", out createdNew)) {
            if (!createdNew) return;
            Application.EnableVisualStyles();
            Application.Run(new ClashGuardian());
        }
    }

    static bool IsCurrentProcessAdminStatic() {
        try {
            WindowsPrincipal wp = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return wp.IsInRole(WindowsBuiltInRole.Administrator);
        } catch { return false; }
    }

    static string GetExecutablePathStatic() {
        try {
            string p = Application.ExecutablePath;
            if (!string.IsNullOrEmpty(p)) return p;
        } catch { /* ignore */ }
        try {
            string p = Process.GetCurrentProcess().MainModule.FileName;
            if (!string.IsNullOrEmpty(p)) return p;
        } catch { /* ignore */ }
        return "";
    }

    static bool IsScheduledTaskPresentStatic(string taskName) {
        int code;
        bool launched = RunProcessHiddenStatic("schtasks.exe", "/Query /TN \"" + taskName + "\"", 8000, out code);
        return launched && code == 0;
    }

    static bool TryCreateUuRouteTaskHighestStatic(string exePath) {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
        int code;
        string tr = "\"\\\"" + exePath + "\\\" --watch-uu-route\"";
        string args = "/Create /F /SC ONLOGON /RL HIGHEST /TN \"" + UU_ROUTE_TASK_NAME + "\" /TR " + tr;
        bool launched = RunProcessHiddenStatic("schtasks.exe", args, 10000, out code);
        return launched && code == 0;
    }

    static void DeleteTaskIfExistsStatic(string taskName) {
        try {
            int code;
            RunProcessHiddenStatic("schtasks.exe", "/Delete /F /TN \"" + taskName + "\"", 10000, out code);
        } catch { /* ignore */ }
    }

    static void RemoveRunValueStatic(string valueName) {
        if (string.IsNullOrEmpty(valueName)) return;
        try {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) {
                if (rk == null) return;
                rk.DeleteValue(valueName, false);
            }
        } catch { /* ignore */ }
    }

    static void CleanupUuRouteRunArtifactsStatic() {
        RemoveRunValueStatic(UU_ROUTE_RUN_VALUE);
        RemoveRunValueStatic(UU_ROUTE_LEGACY_RUN_VALUE);
        RemoveRunValueStatic("ClashGuardian.UUWatcher");
        RemoveRunValueStatic("ClashGuardianUUWatcher");
    }

    static int InstallOrRepairUuRouteTaskStatic(bool runNow) {
        if (!IsCurrentProcessAdminStatic()) return UU_ROUTE_INSTALL_EXIT_NOT_ADMIN;

        string exePath = GetExecutablePathStatic();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return UU_ROUTE_INSTALL_EXIT_TASK_CREATE_FAILED;

        DeleteTaskIfExistsStatic(UU_ROUTE_LEGACY_TASK_NAME);
        DeleteTaskIfExistsStatic(UU_ROUTE_TASK_NAME);
        CleanupUuRouteRunArtifactsStatic();

        if (!TryCreateUuRouteTaskHighestStatic(exePath)) return UU_ROUTE_INSTALL_EXIT_TASK_CREATE_FAILED;
        if (!IsScheduledTaskPresentStatic(UU_ROUTE_TASK_NAME)) return UU_ROUTE_INSTALL_EXIT_TASK_VERIFY_FAILED;

        if (runNow) {
            try {
                int runCode;
                RunProcessHiddenStatic("schtasks.exe", "/Run /TN \"" + UU_ROUTE_TASK_NAME + "\"", 10000, out runCode);
            } catch { /* ignore */ }
        }
        return UU_ROUTE_INSTALL_EXIT_OK;
    }

    // ==================== Watcher: follow Clash launch (no UI) ====================

    static string GetWatcherConfigFilePath() {
        try {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) {
                return Path.Combine(local, "ClashGuardian", "config", "config.json");
            }
        } catch { /* ignore */ }
        try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); }
        catch { return "config.json"; }
    }

    static List<string> ParseJsonStringArrayStatic(string json, string key) {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return result;

        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return result;

        idx = json.IndexOf('[', idx);
        if (idx < 0) return result;

        int i = idx + 1;
        bool inString = false;
        bool escape = false;
        StringBuilder sb = new StringBuilder();

        while (i < json.Length) {
            char c = json[i];

            if (!inString) {
                if (c == ']') break;
                if (c == '"') { inString = true; sb.Length = 0; }
                i++;
                continue;
            }

            if (escape) {
                escape = false;
                switch (c) {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < json.Length) {
                            try {
                                string hex = json.Substring(i + 1, 4);
                                int code = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                                sb.Append((char)code);
                                i += 4;
                            } catch { /* ignore invalid */ }
                        }
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
                i++;
                continue;
            }

            if (c == '\\') { escape = true; i++; continue; }
            if (c == '"') {
                inString = false;
                result.Add(sb.ToString());
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return result;
    }

    static string[] ExpandProcessNameVariantsStatic(string name) {
        if (string.IsNullOrEmpty(name)) return new string[0];
        string n = name.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
        if (string.IsNullOrEmpty(n)) return new string[0];

        // Keep list small: only a few high-signal variants to tolerate config/process-name mismatch.
        HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.Add(n);
        set.Add(n.ToLowerInvariant());

        string dash = n.Replace(' ', '-');
        if (!string.Equals(dash, n, StringComparison.Ordinal)) {
            set.Add(dash);
        }
        set.Add(dash.ToLowerInvariant());

        string[] arr = new string[set.Count];
        set.CopyTo(arr);
        return arr;
    }

    static string[] NormalizeAndExpandProcessNamesStatic(string[] processNames) {
        if (processNames == null || processNames.Length == 0) return new string[0];
        HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in processNames) {
            string[] variants = ExpandProcessNameVariantsStatic(name);
            foreach (string v in variants) {
                if (!string.IsNullOrEmpty(v)) set.Add(v);
            }
        }
        string[] arr = new string[set.Count];
        set.CopyTo(arr);
        return arr;
    }

    static string[] TryLoadClientProcessNamesFromConfigForWatcher() {
        try {
            string cfg = GetWatcherConfigFilePath();
            if (!string.IsNullOrEmpty(cfg) && File.Exists(cfg)) {
                string json = File.ReadAllText(cfg, Encoding.UTF8);
                List<string> arr = ParseJsonStringArrayStatic(json, "clientProcessNames");
                if (arr.Count > 0) return arr.ToArray();
            }
        } catch { /* ignore */ }
        return DEFAULT_CLIENT_NAMES;
    }

    static bool IsAnyNamedProcessRunningStatic(string[] processNames) {
        if (processNames == null || processNames.Length == 0) return false;
        foreach (string name in processNames) {
            if (string.IsNullOrEmpty(name)) continue;
            try {
                Process[] procs = Process.GetProcessesByName(name);
                if (procs != null && procs.Length > 0) {
                    foreach (var p in procs) p.Dispose();
                    return true;
                }
                if (procs != null) foreach (var p in procs) p.Dispose();
            } catch { /* ignore */ }
        }
        return false;
    }

    static bool IsGuardianRunningByMutexStatic() {
        try {
            using (Mutex m = Mutex.OpenExisting("ClashGuardianSingleInstance")) { }
            return true;
        } catch { return false; }
    }

    static void RunClashWatcherLoop() {
        bool createdNew;
        using (Mutex mutex = new Mutex(true, "ClashGuardianWatcherSingleInstance", out createdNew)) {
            if (!createdNew) return;

            EventWaitHandle stopEvent = null;
            try {
                stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, "ClashGuardianWatcherStopEvent");
                stopEvent.Reset();
            } catch { stopEvent = null; }

            string exePath = "";
            try { exePath = Application.ExecutablePath; }
            catch {
                try { exePath = Process.GetCurrentProcess().MainModule.FileName; }
                catch { exePath = ""; }
            }
            if (string.IsNullOrEmpty(exePath)) return;

            DateTime lastConfigReload = DateTime.MinValue;
            string[] clientNames = NormalizeAndExpandProcessNamesStatic(DEFAULT_CLIENT_NAMES);
            DateTime lastLaunchAttempt = DateTime.MinValue;

            while (true) {
                try {
                    DateTime now = DateTime.Now;
                    if ((now - lastConfigReload).TotalSeconds >= 30) {
                        clientNames = NormalizeAndExpandProcessNamesStatic(TryLoadClientProcessNamesFromConfigForWatcher());
                        lastConfigReload = now;
                    }

                    if (IsAnyNamedProcessRunningStatic(clientNames)) {
                        if (!IsGuardianRunningByMutexStatic() && (now - lastLaunchAttempt).TotalSeconds >= 5) {
                            try {
                                ProcessStartInfo psi = new ProcessStartInfo(exePath, "--follow-clash");
                                psi.UseShellExecute = false;
                                psi.CreateNoWindow = true;
                                try { psi.WorkingDirectory = Path.GetDirectoryName(exePath); } catch { /* ignore */ }
                                Process.Start(psi);
                            } catch { /* ignore */ }
                            lastLaunchAttempt = now;
                        }
                    }
                } catch { /* ignore */ }

                try {
                    if (stopEvent != null && stopEvent.WaitOne(500)) break;
                } catch { Thread.Sleep(500); }
            }
        }
    }

    // ==================== Watcher: UU route guard (no UI) ====================

    const string UU_WATCHER_MUTEX_NAME = "Global\\ClashGuardian_UU_Watcher";
    const string UU_WATCHER_STOP_EVENT_NAME = "ClashGuardianUuWatcherStopEvent";
    const string UU_WATCHER_ROOT_RELATIVE = "uu-watcher";
    const string UU_WATCHER_STATE_FILE_NAME = "state.json";
    const string UU_WATCHER_LOG_FILE_NAME = "watcher.log";
    const string UU_WATCHER_HEARTBEAT_FILE_NAME = "heartbeat.json";
    const string UU_WATCHER_POLICY_TAG = "2026-02-22-builtin-v1";
    const string UU_WATCHER_FIREWALL_RULE_GROUP = "ClashGuardian.UUWatcher";
    const string UU_WATCHER_FIREWALL_RULE_PREFIX = "ClashGuardian.UUWatcher.Block7897";
    const string UU_WATCHER_ROUTE_GROUP = "GAME_STEAM_ROUTE";
    const string UU_WATCHER_DEFAULT_API = "http://127.0.0.1:9097";
    const string UU_WATCHER_DEFAULT_SECRET = "set-your-secret";
    const string UU_WATCHER_LOCAL_PROXY_ADDRESS = "127.0.0.1";
    const int UU_WATCHER_LOCAL_PROXY_PORT = 7897;
    const int UU_WATCHER_POLL_SECONDS = 2;
    const int UU_WATCHER_DEBOUNCE_SECONDS = 3;
    const int UU_WATCHER_POLICY_INTERVAL_SECONDS = 10;
    const int UU_WATCHER_LEAK_DRAIN_MIN_INTERVAL_SECONDS = 10;
    const int UU_WATCHER_LEAK_DRAIN_MAX_PER_ROUND = 5;
    const int UU_WATCHER_TAKEOVER_DRAIN_MAX_ON_ENTER = 30;
    const int UU_WATCHER_TAKEOVER_DRAIN_MAX_COMPENSATION = 10;
    const int UU_WATCHER_TAKEOVER_RECHECK_DELAY_MS = 2000;
    const int UU_WATCHER_MIN_SWITCH_INTERVAL_SECONDS = 15;
    const int UU_WATCHER_FLAP_WINDOW_SECONDS = 60;
    const int UU_WATCHER_FLAP_SWITCH_THRESHOLD = 4;
    const int UU_WATCHER_QUARANTINE_SECONDS = 30;
    const int UU_WATCHER_WAKE_GAP_SECONDS = 20;
    const int UU_WATCHER_API_FAIL_ALERT_THRESHOLD = 10;
    const int UU_WATCHER_PENDING_ALERT_SECONDS = 300;
    const int UU_WATCHER_HEARTBEAT_INTERVAL_SECONDS = 5;
    const int UU_WATCHER_HEARTBEAT_STALE_SECONDS = 20;
    const string UU_WATCHER_USER_ENV_KEY = @"Environment";
    const string UU_WATCHER_ENV_HTTP_PROXY = "HTTP_PROXY";
    const string UU_WATCHER_ENV_HTTPS_PROXY = "HTTPS_PROXY";
    const string UU_WATCHER_ENV_NO_PROXY = "NO_PROXY";
    const string UU_WATCHER_ENV_NO_PROXY_DEFAULT = "localhost,127.0.0.1";
    const int UU_WATCHER_ENV_BROADCAST_TIMEOUT_MS = 1000;
    const uint UU_WATCHER_WM_SETTINGCHANGE = 0x001A;
    const uint UU_WATCHER_SMTO_ABORTIFHUNG = 0x0002;
    static readonly IntPtr UU_WATCHER_HWND_BROADCAST = new IntPtr(0xffff);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    static readonly int[] UU_WATCHER_RETRY_DELAYS_SECONDS = new int[] { 2, 5, 10, 20, 30 };

    static readonly string[] UU_WATCHER_TARGET_PROCESS_NAMES = new string[] {
        "steam.exe", "steamwebhelper.exe", "tslgame.exe"
    };
    static readonly string[] UU_WATCHER_LEAK_DRAIN_PROCESS_NAMES = new string[] {
        "tslgame.exe"
    };
    static readonly string[] UU_WATCHER_BYPASS_ENTRIES = new string[] {
        "*.steampowered.com", "steampowered.com",
        "*.steamcommunity.com", "steamcommunity.com",
        "*.steamgames.com", "steamgames.com",
        "*.steamusercontent.com", "steamusercontent.com",
        "*.steamcontent.com", "steamcontent.com",
        "*.steamstatic.com", "steamstatic.com",
        "*.steamserver.net", "steamserver.net",
        "*.valve.net", "valve.net",
        "*.valvesoftware.com", "valvesoftware.com",
        "*.steamcdn-a.akamaihd.net", "steamcdn-a.akamaihd.net",
        "*.api.steampowered.com", "api.steampowered.com",
        "*.cm.steampowered.com", "cm.steampowered.com",
        "*.playbattlegrounds.com", "playbattlegrounds.com",
        "*.pubg.com", "pubg.com",
        "*.krafton.com", "krafton.com",
        "*.kraftonde.com", "kraftonde.com",
        "*.battleye.com", "battleye.com",
        "*.easyanticheat.net", "easyanticheat.net",
        "*.globalsign.com", "globalsign.com"
    };

    class UuWatcherFirewallRuleDef {
        public string DisplayName = "";
        public string Direction = "Outbound";
        public string Action = "Block";
        public string Enabled = "True";
        public string Profile = "Any";
        public string Program = "";
        public string Protocol = "";
        public string LocalPort = "";
        public string RemotePort = "";
        public string LocalAddress = "";
        public string RemoteAddress = "";
    }

    class UuWatcherEnvSnapshot {
        public bool Captured = false;
        public bool HttpProxyExists = false;
        public string HttpProxyValue = "";
        public bool HttpsProxyExists = false;
        public string HttpsProxyValue = "";
        public bool NoProxyExists = false;
        public string NoProxyValue = "";
    }

    class UuWatcherSnapshot {
        public string Route = "";
        public string ProxyOverride = "";
        public int ProxyEnable = 0;
        public UuWatcherEnvSnapshot Env = new UuWatcherEnvSnapshot();
        public List<UuWatcherFirewallRuleDef> FwRules = new List<UuWatcherFirewallRuleDef>();
    }

    class UuWatcherState {
        public string Mode = "NORMAL";
        public string DesiredMode = "NORMAL";
        public UuWatcherSnapshot Snapshot = new UuWatcherSnapshot();
        public bool RollbackPending = false;
        public string RollbackPendingSince = "";
        public int RetryCount = 0;
        public string NextRetryAt = "";
        public string SwitchId = "";
        public bool HardIsolationUnavailable = false;
        public int FlapCounter = 0;
        public List<string> SwitchWindow = new List<string>();
        public string LastSwitchAt = "";
        public string QuarantineUntil = "";
    }

    class UuWatcherLeakConn {
        public string Id = "";
        public string Process = "";
        public string Host = "";
        public string Rule = "";
        public string RulePayload = "";
        public string Chains = "";
    }

    class UuWatcherLocalProxySignal {
        public string Process = "";
        public int Established = 0;
        public int SynSent = 0;
    }

    class UuWatcherContext {
        public string RootDir = "";
        public string StateFile = "";
        public string LogFile = "";
        public string HeartbeatFile = "";
        public string ApiBase = UU_WATCHER_DEFAULT_API;
        public string ApiSecret = UU_WATCHER_DEFAULT_SECRET;
        public bool IsAdmin = false;
        public string InstanceId = "";
        public int ApiFailConsec = 0;
        public int LastApiAlertCount = 0;
        public DateTime LastPolicyEnforceAt = DateTime.MinValue;
        public DateTime LastLeakDrainAt = DateTime.MinValue;
        public DateTime LastPendingAlertAt = DateTime.MinValue;
        public DateTime LastMinSwitchWarnAt = DateTime.MinValue;
        public DateTime LastQuarantineSkipWarnAt = DateTime.MinValue;
        public DateTime LastHeartbeatAt = DateTime.MinValue;
        public DateTime LastAdminRequiredAlertAt = DateTime.MinValue;
        public DateTime LastLocal7897SignalAt = DateTime.MinValue;
        public DateTime LastProxyLeakSignalAt = DateTime.MinValue;
        public string LastFirewallSignature = "";
        public string LastTakeoverDrainSwitchId = "";
        public string LastTakeoverCompSwitchId = "";
        public bool QuarantineActive = false;
        public EventWaitHandle StopEvent = null;
    }

    static UuWatcherState NewDefaultUuWatcherState() {
        return new UuWatcherState();
    }

    static string UuWatcherRootDir() {
        string local = "";
        try { local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } catch { local = ""; }
        if (string.IsNullOrEmpty(local)) {
            try { local = AppDomain.CurrentDomain.BaseDirectory; } catch { local = "."; }
        }
        return Path.Combine(local, "ClashGuardian", UU_WATCHER_ROOT_RELATIVE);
    }

    static string GetJsonStringValueStatic(string json, string key, string defaultValue) {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return defaultValue;
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return defaultValue;
        idx += search.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        if (idx >= json.Length) return defaultValue;
        if (json[idx] == '"') {
            int next;
            return ReadJsonStringAtStatic(json, idx, out next);
        }
        int end = idx;
        while (end < json.Length) {
            char c = json[end];
            if (c == ',' || c == '}' || c == ']' || c == '\r' || c == '\n') break;
            end++;
        }
        string raw = json.Substring(idx, end - idx).Trim();
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        return raw;
    }

    static bool GetJsonBoolValueStatic(string json, string key, bool defaultValue) {
        string v = GetJsonStringValueStatic(json, key, defaultValue ? "true" : "false");
        bool parsed;
        if (bool.TryParse(v, out parsed)) return parsed;
        return defaultValue;
    }

    static int GetJsonIntValueStatic(string json, string key, int defaultValue) {
        string v = GetJsonStringValueStatic(json, key, defaultValue.ToString());
        int parsed;
        if (int.TryParse(v, out parsed)) return parsed;
        return defaultValue;
    }

    static bool JsonHasKeyStatic(string json, string key) {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
        return json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
    }

    static string ReadJsonStringAtStatic(string json, int quoteIndex, out int nextIndex) {
        nextIndex = quoteIndex;
        if (string.IsNullOrEmpty(json) || quoteIndex < 0 || quoteIndex >= json.Length || json[quoteIndex] != '"') return "";
        StringBuilder sb = new StringBuilder();
        int i = quoteIndex + 1;
        while (i < json.Length) {
            char c = json[i];
            if (c == '"') {
                nextIndex = i + 1;
                return sb.ToString();
            }
            if (c == '\\' && i + 1 < json.Length) {
                char n = json[i + 1];
                if (n == '"' || n == '\\' || n == '/') { sb.Append(n); i += 2; continue; }
                if (n == 'n') { sb.Append('\n'); i += 2; continue; }
                if (n == 'r') { sb.Append('\r'); i += 2; continue; }
                if (n == 't') { sb.Append('\t'); i += 2; continue; }
                if (n == 'b') { sb.Append('\b'); i += 2; continue; }
                if (n == 'f') { sb.Append('\f'); i += 2; continue; }
                if (n == 'u' && i + 5 < json.Length) {
                    try {
                        string hex = json.Substring(i + 2, 4);
                        int code = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                        sb.Append((char)code);
                        i += 6;
                        continue;
                    } catch { }
                }
            }
            sb.Append(c);
            i++;
        }
        nextIndex = i;
        return sb.ToString();
    }

    static bool FindJsonObjectBoundsStatic(string json, string key, out int objStart, out int objEnd) {
        objStart = 0; objEnd = 0;
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return false;
        idx += search.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        if (idx >= json.Length || json[idx] != '{') return false;
        objStart = idx;
        int depth = 1;
        bool inString = false;
        bool escape = false;
        int i = idx + 1;
        while (i < json.Length) {
            char c = json[i];
            if (inString) {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
            } else {
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}') depth--;
                if (depth == 0) {
                    objEnd = i + 1;
                    return true;
                }
            }
            i++;
        }
        return false;
    }

    static DateTime ParseUuWatcherTime(string value) {
        if (string.IsNullOrEmpty(value)) return DateTime.MinValue;
        DateTime dt;
        if (DateTime.TryParse(value, out dt)) return dt;
        return DateTime.MinValue;
    }

    static void WriteUuWatcherLog(UuWatcherContext ctx, string msg) {
        try {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg;
            File.AppendAllText(ctx.LogFile, line + Environment.NewLine, Encoding.UTF8);
        } catch { /* ignore */ }
    }

    static string NewUuWatcherSwitchId() {
        return DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    static List<string> ExtractTopLevelJsonObjectsFromArrayByKeyStatic(string json, string key) {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return result;
        int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (keyIdx < 0) return result;
        int arrStart = json.IndexOf('[', keyIdx);
        if (arrStart < 0) return result;
        bool inString = false;
        bool escape = false;
        int depthObj = 0;
        int objStart = -1;
        for (int i = arrStart + 1; i < json.Length; i++) {
            char c = json[i];
            if (inString) {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '{') {
                if (depthObj == 0) objStart = i;
                depthObj++;
                continue;
            }
            if (c == '}') {
                if (depthObj > 0) {
                    depthObj--;
                    if (depthObj == 0 && objStart >= 0) {
                        result.Add(json.Substring(objStart, i - objStart + 1));
                        objStart = -1;
                    }
                }
                continue;
            }
            if (c == ']' && depthObj == 0) break;
        }
        return result;
    }

    static UuWatcherState NormalizeUuWatcherState(string raw) {
        UuWatcherState s = NewDefaultUuWatcherState();
        if (string.IsNullOrEmpty(raw)) return s;

        // Backward compatibility with old schema.
        if (JsonHasKeyStatic(raw, "isUuMode")) {
            bool oldUu = GetJsonBoolValueStatic(raw, "isUuMode", false);
            if (oldUu) {
                s.Mode = "UU_ACTIVE";
                s.DesiredMode = "UU_ACTIVE";
            }
            s.SwitchId = GetJsonStringValueStatic(raw, "lastSwitch", "");
            s.Snapshot.Route = GetJsonStringValueStatic(raw, "routeBeforeUu", "");
            s.Snapshot.ProxyOverride = GetJsonStringValueStatic(raw, "proxyOverrideBeforeUu", "");
            return s;
        }

        string mode = GetJsonStringValueStatic(raw, "mode", "NORMAL");
        if (mode == "NORMAL" || mode == "ENTERING_UU" || mode == "UU_ACTIVE" || mode == "EXITING_UU" || mode == "DEGRADED_EXIT_PENDING") s.Mode = mode;
        string desired = GetJsonStringValueStatic(raw, "desiredMode", "NORMAL");
        if (desired == "NORMAL" || desired == "UU_ACTIVE") s.DesiredMode = desired;
        s.RollbackPending = GetJsonBoolValueStatic(raw, "rollbackPending", false);
        s.RollbackPendingSince = GetJsonStringValueStatic(raw, "rollbackPendingSince", "");
        s.RetryCount = GetJsonIntValueStatic(raw, "retryCount", 0);
        s.NextRetryAt = GetJsonStringValueStatic(raw, "nextRetryAt", "");
        s.SwitchId = GetJsonStringValueStatic(raw, "switchId", "");
        s.HardIsolationUnavailable = GetJsonBoolValueStatic(raw, "hardIsolationUnavailable", false);
        s.FlapCounter = GetJsonIntValueStatic(raw, "flapCounter", 0);
        s.LastSwitchAt = GetJsonStringValueStatic(raw, "lastSwitchAt", "");
        s.QuarantineUntil = GetJsonStringValueStatic(raw, "quarantineUntil", "");

        List<string> switchWindow = ParseJsonStringArrayStatic(raw, "switchWindow");
        if (switchWindow != null && switchWindow.Count > 0) s.SwitchWindow = switchWindow;

        int snapshotStart, snapshotEnd;
        if (FindJsonObjectBoundsStatic(raw, "snapshot", out snapshotStart, out snapshotEnd)) {
            string snap = raw.Substring(snapshotStart, snapshotEnd - snapshotStart);
            s.Snapshot.Route = GetJsonStringValueStatic(snap, "route", "");
            s.Snapshot.ProxyOverride = GetJsonStringValueStatic(snap, "proxyOverride", "");
            s.Snapshot.ProxyEnable = GetJsonIntValueStatic(snap, "proxyEnable", 0);
            if (s.Snapshot.Env == null) s.Snapshot.Env = new UuWatcherEnvSnapshot();

            int envStart, envEnd;
            if (FindJsonObjectBoundsStatic(snap, "env", out envStart, out envEnd)) {
                string env = snap.Substring(envStart, envEnd - envStart);
                s.Snapshot.Env.Captured = GetJsonBoolValueStatic(env, "captured", false);
                s.Snapshot.Env.HttpProxyExists = GetJsonBoolValueStatic(env, "httpProxyExists", false);
                s.Snapshot.Env.HttpProxyValue = GetJsonStringValueStatic(env, "httpProxyValue", "");
                s.Snapshot.Env.HttpsProxyExists = GetJsonBoolValueStatic(env, "httpsProxyExists", false);
                s.Snapshot.Env.HttpsProxyValue = GetJsonStringValueStatic(env, "httpsProxyValue", "");
                s.Snapshot.Env.NoProxyExists = GetJsonBoolValueStatic(env, "noProxyExists", false);
                s.Snapshot.Env.NoProxyValue = GetJsonStringValueStatic(env, "noProxyValue", "");
            }

            List<string> fwObjs = ExtractTopLevelJsonObjectsFromArrayByKeyStatic(snap, "fwRules");
            for (int i = 0; i < fwObjs.Count; i++) {
                string obj = fwObjs[i];
                UuWatcherFirewallRuleDef def = new UuWatcherFirewallRuleDef();
                def.DisplayName = GetJsonStringValueStatic(obj, "displayName", "");
                def.Direction = GetJsonStringValueStatic(obj, "direction", "Outbound");
                def.Action = GetJsonStringValueStatic(obj, "action", "Block");
                def.Enabled = GetJsonStringValueStatic(obj, "enabled", "True");
                def.Profile = GetJsonStringValueStatic(obj, "profile", "Any");
                def.Program = GetJsonStringValueStatic(obj, "program", "");
                def.Protocol = GetJsonStringValueStatic(obj, "protocol", "");
                def.LocalPort = GetJsonStringValueStatic(obj, "localPort", "");
                def.RemotePort = GetJsonStringValueStatic(obj, "remotePort", "");
                def.LocalAddress = GetJsonStringValueStatic(obj, "localAddress", "");
                def.RemoteAddress = GetJsonStringValueStatic(obj, "remoteAddress", "");
                if (!string.IsNullOrEmpty(def.DisplayName)) s.Snapshot.FwRules.Add(def);
            }
        }

        return s;
    }

    static string BuildUuWatcherStateJson(UuWatcherState s) {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"mode\": \"").Append(EscapeJsonString(s.Mode)).Append("\",\n");
        sb.Append("  \"desiredMode\": \"").Append(EscapeJsonString(s.DesiredMode)).Append("\",\n");
        sb.Append("  \"rollbackPending\": ").Append(s.RollbackPending ? "true" : "false").Append(",\n");
        sb.Append("  \"rollbackPendingSince\": \"").Append(EscapeJsonString(s.RollbackPendingSince)).Append("\",\n");
        sb.Append("  \"retryCount\": ").Append(s.RetryCount).Append(",\n");
        sb.Append("  \"nextRetryAt\": \"").Append(EscapeJsonString(s.NextRetryAt)).Append("\",\n");
        sb.Append("  \"switchId\": \"").Append(EscapeJsonString(s.SwitchId)).Append("\",\n");
        sb.Append("  \"hardIsolationUnavailable\": ").Append(s.HardIsolationUnavailable ? "true" : "false").Append(",\n");
        sb.Append("  \"flapCounter\": ").Append(s.FlapCounter).Append(",\n");
        sb.Append("  \"switchWindow\": [");
        for (int i = 0; i < s.SwitchWindow.Count; i++) {
            if (i > 0) sb.Append(", ");
            sb.Append("\"").Append(EscapeJsonString(s.SwitchWindow[i])).Append("\"");
        }
        sb.Append("],\n");
        sb.Append("  \"lastSwitchAt\": \"").Append(EscapeJsonString(s.LastSwitchAt)).Append("\",\n");
        sb.Append("  \"quarantineUntil\": \"").Append(EscapeJsonString(s.QuarantineUntil)).Append("\",\n");
        sb.Append("  \"snapshot\": {\n");
        sb.Append("    \"route\": \"").Append(EscapeJsonString(s.Snapshot.Route)).Append("\",\n");
        sb.Append("    \"proxyOverride\": \"").Append(EscapeJsonString(s.Snapshot.ProxyOverride)).Append("\",\n");
        sb.Append("    \"proxyEnable\": ").Append(s.Snapshot.ProxyEnable).Append(",\n");
        UuWatcherEnvSnapshot env = s.Snapshot.Env;
        if (env == null) env = new UuWatcherEnvSnapshot();
        sb.Append("    \"env\": {\n");
        sb.Append("      \"captured\": ").Append(env.Captured ? "true" : "false").Append(",\n");
        sb.Append("      \"httpProxyExists\": ").Append(env.HttpProxyExists ? "true" : "false").Append(",\n");
        sb.Append("      \"httpProxyValue\": \"").Append(EscapeJsonString(env.HttpProxyValue)).Append("\",\n");
        sb.Append("      \"httpsProxyExists\": ").Append(env.HttpsProxyExists ? "true" : "false").Append(",\n");
        sb.Append("      \"httpsProxyValue\": \"").Append(EscapeJsonString(env.HttpsProxyValue)).Append("\",\n");
        sb.Append("      \"noProxyExists\": ").Append(env.NoProxyExists ? "true" : "false").Append(",\n");
        sb.Append("      \"noProxyValue\": \"").Append(EscapeJsonString(env.NoProxyValue)).Append("\"\n");
        sb.Append("    },\n");
        sb.Append("    \"fwRules\": [");
        for (int i = 0; i < s.Snapshot.FwRules.Count; i++) {
            UuWatcherFirewallRuleDef d = s.Snapshot.FwRules[i];
            if (i > 0) sb.Append(", ");
            sb.Append("{");
            sb.Append("\"displayName\":\"").Append(EscapeJsonString(d.DisplayName)).Append("\",");
            sb.Append("\"direction\":\"").Append(EscapeJsonString(d.Direction)).Append("\",");
            sb.Append("\"action\":\"").Append(EscapeJsonString(d.Action)).Append("\",");
            sb.Append("\"enabled\":\"").Append(EscapeJsonString(d.Enabled)).Append("\",");
            sb.Append("\"profile\":\"").Append(EscapeJsonString(d.Profile)).Append("\",");
            sb.Append("\"program\":\"").Append(EscapeJsonString(d.Program)).Append("\",");
            sb.Append("\"protocol\":\"").Append(EscapeJsonString(d.Protocol)).Append("\",");
            sb.Append("\"localPort\":\"").Append(EscapeJsonString(d.LocalPort)).Append("\",");
            sb.Append("\"remotePort\":\"").Append(EscapeJsonString(d.RemotePort)).Append("\",");
            sb.Append("\"localAddress\":\"").Append(EscapeJsonString(d.LocalAddress)).Append("\",");
            sb.Append("\"remoteAddress\":\"").Append(EscapeJsonString(d.RemoteAddress)).Append("\"");
            sb.Append("}");
        }
        sb.Append("]\n");
        sb.Append("  }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    static UuWatcherState LoadUuWatcherState(UuWatcherContext ctx) {
        try {
            if (!File.Exists(ctx.StateFile)) return NewDefaultUuWatcherState();
            string raw = File.ReadAllText(ctx.StateFile, Encoding.UTF8);
            if (string.IsNullOrEmpty(raw)) return NewDefaultUuWatcherState();
            return NormalizeUuWatcherState(raw);
        } catch (Exception ex) {
            WriteUuWatcherLog(ctx, "[WARN] state read failed: " + ex.Message);
            return NewDefaultUuWatcherState();
        }
    }

    static void SaveUuWatcherState(UuWatcherContext ctx, UuWatcherState s) {
        try {
            File.WriteAllText(ctx.StateFile, BuildUuWatcherStateJson(s), Encoding.UTF8);
        } catch (Exception ex) {
            WriteUuWatcherLog(ctx, "[WARN] state write failed: " + ex.Message);
        }
    }

    static string[] LoadWatcherApiConfig() {
        string api = UU_WATCHER_DEFAULT_API;
        string secret = UU_WATCHER_DEFAULT_SECRET;
        try {
            string cfg = GetWatcherConfigFilePath();
            if (!string.IsNullOrEmpty(cfg) && File.Exists(cfg)) {
                string json = File.ReadAllText(cfg, Encoding.UTF8);
                string apiCfg = GetJsonStringValueStatic(json, "clashApi", api);
                if (!string.IsNullOrEmpty(apiCfg)) api = apiCfg.Trim().TrimEnd('/');
                string secCfg = GetJsonStringValueStatic(json, "clashSecret", secret);
                if (!string.IsNullOrEmpty(secCfg)) secret = secCfg.Trim();
            }
        } catch { /* ignore */ }
        return new string[] { api, secret };
    }

    static void EnsureUuWatcherHeartbeat(UuWatcherContext ctx) {
        DateTime now = DateTime.Now;
        if ((now - ctx.LastHeartbeatAt).TotalSeconds < UU_WATCHER_HEARTBEAT_INTERVAL_SECONDS) return;
        try {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"instanceId\": \"").Append(EscapeJsonString(ctx.InstanceId)).Append("\",\n");
            sb.Append("  \"lastSeen\": \"").Append(now.ToString("o")).Append("\",\n");
            sb.Append("  \"pid\": ").Append(Process.GetCurrentProcess().Id).Append("\n");
            sb.Append("}\n");
            File.WriteAllText(ctx.HeartbeatFile, sb.ToString(), Encoding.UTF8);
            ctx.LastHeartbeatAt = now;
        } catch (Exception ex) {
            WriteUuWatcherLog(ctx, "[WARN] heartbeat write failed: " + ex.Message);
        }
    }

    static void InitUuWatcherHeartbeat(UuWatcherContext ctx) {
        try {
            if (File.Exists(ctx.HeartbeatFile)) {
                string prevRaw = File.ReadAllText(ctx.HeartbeatFile, Encoding.UTF8);
                if (!string.IsNullOrEmpty(prevRaw)) {
                    string prevId = GetJsonStringValueStatic(prevRaw, "instanceId", "");
                    DateTime last = ParseUuWatcherTime(GetJsonStringValueStatic(prevRaw, "lastSeen", ""));
                    if (last != DateTime.MinValue) {
                        TimeSpan age = DateTime.Now - last;
                        if (age.TotalSeconds > UU_WATCHER_HEARTBEAT_STALE_SECONDS) {
                            WriteUuWatcherLog(ctx, "[WARN] stale heartbeat detected, taking over previousInstance=" + prevId + ", ageSec=" + ((int)age.TotalSeconds).ToString());
                        }
                    }
                }
            }
        } catch (Exception ex) {
            WriteUuWatcherLog(ctx, "[WARN] heartbeat init failed: " + ex.Message);
        }
        EnsureUuWatcherHeartbeat(ctx);
    }

    static bool RunProcessHiddenStatic(string fileName, string args, int timeoutMs, out int exitCode) {
        exitCode = -1;
        try {
            ProcessStartInfo psi = new ProcessStartInfo(fileName, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi)) {
                try { p.WaitForExit(timeoutMs); } catch { }
                try { if (!p.HasExited) p.Kill(); } catch { }
                if (!p.HasExited) return false;
                exitCode = p.ExitCode;
                return true;
            }
        } catch { return false; }
    }

    static bool RunProcessCaptureStatic(string fileName, string args, int timeoutMs, out int exitCode, out string stdout, out string stderr) {
        exitCode = -1;
        stdout = "";
        stderr = "";
        try {
            ProcessStartInfo psi = new ProcessStartInfo(fileName, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi)) {
                if (p == null) return false;
                try { p.WaitForExit(timeoutMs); } catch { /* ignore */ }
                try {
                    if (!p.HasExited) {
                        try { p.Kill(); } catch { /* ignore */ }
                    }
                } catch { /* ignore */ }
                try { stdout = p.StandardOutput.ReadToEnd(); } catch { stdout = ""; }
                try { stderr = p.StandardError.ReadToEnd(); } catch { stderr = ""; }
                if (!p.HasExited) return false;
                exitCode = p.ExitCode;
                return true;
            }
        } catch {
            return false;
        }
    }

    static bool InvokeMihomoRequest(UuWatcherContext ctx, string method, string path, string body, out string responseText, out string errorText) {
        responseText = "";
        errorText = "";
        try {
            HttpWebRequest req = WebRequest.Create(ctx.ApiBase + path) as HttpWebRequest;
            req.Method = method;
            req.Timeout = 5000;
            req.ReadWriteTimeout = 5000;
            req.Headers.Add("Authorization", "Bearer " + ctx.ApiSecret);
            try { if (req.RequestUri != null && req.RequestUri.IsLoopback) req.Proxy = null; } catch { }
            if (!string.IsNullOrEmpty(body)) {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/json; charset=utf-8";
                req.ContentLength = bytes.Length;
                using (Stream stream = req.GetRequestStream()) {
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse) {
                using (Stream rs = resp.GetResponseStream()) {
                    if (rs != null) {
                        using (StreamReader sr = new StreamReader(rs, Encoding.UTF8)) {
                            responseText = sr.ReadToEnd();
                        }
                    }
                }
            }
            ctx.ApiFailConsec = 0;
            return true;
        } catch (Exception ex) {
            ctx.ApiFailConsec++;
            errorText = ex.Message;
            WriteUuWatcherLog(ctx, "[WARN] api failed method=" + method + " path=" + path + " consec=" + ctx.ApiFailConsec + " error=" + errorText);
            if (ctx.ApiFailConsec > UU_WATCHER_API_FAIL_ALERT_THRESHOLD && ctx.ApiFailConsec != ctx.LastApiAlertCount) {
                WriteUuWatcherLog(ctx, "[ALERT] API_FAIL_CONSEC=" + ctx.ApiFailConsec);
                ctx.LastApiAlertCount = ctx.ApiFailConsec;
            }
            return false;
        }
    }

    static string UuGetRouteNow(UuWatcherContext ctx) {
        string resp, err;
        string groupEscaped = Uri.EscapeDataString(UU_WATCHER_ROUTE_GROUP);
        if (!InvokeMihomoRequest(ctx, "GET", "/proxies/" + groupEscaped, null, out resp, out err)) return "";
        return GetJsonStringValueStatic(resp, "now", "");
    }

    static bool UuSetRouteNow(UuWatcherContext ctx, string name) {
        if (string.IsNullOrEmpty(name)) return false;
        string resp, err;
        string groupEscaped = Uri.EscapeDataString(UU_WATCHER_ROUTE_GROUP);
        string body = "{\"name\":\"" + EscapeJsonString(name) + "\"}";
        bool ok = InvokeMihomoRequest(ctx, "PUT", "/proxies/" + groupEscaped, body, out resp, out err);
        if (ok) WriteUuWatcherLog(ctx, "[INFO] route set " + UU_WATCHER_ROUTE_GROUP + " -> " + name);
        return ok;
    }

    static bool TestUuRunningStatic() {
        try {
            Process[] procs = Process.GetProcessesByName("uu");
            bool running = procs != null && procs.Length > 0;
            if (procs != null) foreach (var p in procs) p.Dispose();
            return running;
        } catch { return false; }
    }

    static void GetProxySettingsStatic(out int proxyEnable, out string proxyServer, out string proxyOverride) {
        proxyEnable = 0; proxyServer = ""; proxyOverride = "";
        try {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false)) {
                if (rk == null) return;
                object en = rk.GetValue("ProxyEnable");
                object sv = rk.GetValue("ProxyServer");
                object ov = rk.GetValue("ProxyOverride");
                if (en != null) {
                    try { proxyEnable = Convert.ToInt32(en); } catch { proxyEnable = 0; }
                }
                if (sv != null) proxyServer = sv.ToString();
                if (ov != null) proxyOverride = ov.ToString();
            }
        } catch { /* ignore */ }
    }

    static bool SetProxyOverrideStatic(string value) {
        try {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true)) {
                if (rk == null) return false;
                rk.SetValue("ProxyOverride", value ?? "");
                return true;
            }
        } catch { return false; }
    }

    static bool SetProxyEnableStatic(int value) {
        try {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true)) {
                if (rk == null) return false;
                rk.SetValue("ProxyEnable", value);
                return true;
            }
        } catch { return false; }
    }

    static void GetUserEnvironmentValueStatic(string name, out bool exists, out string value) {
        exists = false;
        value = "";
        if (string.IsNullOrEmpty(name)) return;
        try {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(UU_WATCHER_USER_ENV_KEY, false)) {
                if (rk == null) return;
                object v = rk.GetValue(name, null);
                if (v == null) return;
                exists = true;
                value = v.ToString();
            }
        } catch { /* ignore */ }
    }

    static void CaptureUserEnvSnapshotStatic(UuWatcherEnvSnapshot envSnapshot) {
        if (envSnapshot == null) return;
        envSnapshot.Captured = true;
        GetUserEnvironmentValueStatic(UU_WATCHER_ENV_HTTP_PROXY, out envSnapshot.HttpProxyExists, out envSnapshot.HttpProxyValue);
        GetUserEnvironmentValueStatic(UU_WATCHER_ENV_HTTPS_PROXY, out envSnapshot.HttpsProxyExists, out envSnapshot.HttpsProxyValue);
        GetUserEnvironmentValueStatic(UU_WATCHER_ENV_NO_PROXY, out envSnapshot.NoProxyExists, out envSnapshot.NoProxyValue);
    }

    static void SetSingleUserEnvironmentValueStatic(RegistryKey rk, string name, bool exists, string value) {
        if (rk == null || string.IsNullOrEmpty(name)) return;
        if (exists) rk.SetValue(name, value ?? "", RegistryValueKind.String);
        else rk.DeleteValue(name, false);
    }

    static bool ApplyUserProxyEnvironmentStatic(
        bool httpExists, string httpValue,
        bool httpsExists, string httpsValue,
        bool noProxyExists, string noProxyValue,
        out string errorDetail) {
        errorDetail = "";
        try {
            using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(UU_WATCHER_USER_ENV_KEY)) {
                if (rk == null) {
                    errorDetail = "open_hkcu_environment_failed";
                    return false;
                }
                SetSingleUserEnvironmentValueStatic(rk, UU_WATCHER_ENV_HTTP_PROXY, httpExists, httpValue);
                SetSingleUserEnvironmentValueStatic(rk, UU_WATCHER_ENV_HTTPS_PROXY, httpsExists, httpsValue);
                SetSingleUserEnvironmentValueStatic(rk, UU_WATCHER_ENV_NO_PROXY, noProxyExists, noProxyValue);
            }
            return true;
        } catch (Exception ex) {
            errorDetail = ex.Message;
            return false;
        }
    }

    static void NotifyUserEnvironmentChangedStatic(UuWatcherContext ctx) {
        try {
            UIntPtr result;
            SendMessageTimeout(
                UU_WATCHER_HWND_BROADCAST,
                UU_WATCHER_WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                UU_WATCHER_SMTO_ABORTIFHUNG,
                UU_WATCHER_ENV_BROADCAST_TIMEOUT_MS,
                out result);
        } catch (Exception ex) {
            WriteUuWatcherLog(ctx, "[WARN] ENV_BROADCAST_FAILED detail=" + ex.Message);
        }
    }

    static bool IsEnvSnapshotMeaningfulStatic(UuWatcherEnvSnapshot env) {
        if (env == null || !env.Captured) return false;
        if (env.HttpProxyExists || env.HttpsProxyExists || env.NoProxyExists) return true;
        if (!string.IsNullOrEmpty((env.HttpProxyValue ?? "").Trim())) return true;
        if (!string.IsNullOrEmpty((env.HttpsProxyValue ?? "").Trim())) return true;
        if (!string.IsNullOrEmpty((env.NoProxyValue ?? "").Trim())) return true;
        return false;
    }

    static bool TryResolveProxyEndpointFromProxyServerStatic(string proxyServer, out string endpoint) {
        endpoint = "";
        if (string.IsNullOrEmpty(proxyServer)) return false;

        string http = "";
        string https = "";
        string single = "";
        string[] entries = proxyServer.Split(';');
        for (int i = 0; i < entries.Length; i++) {
            string item = entries[i] == null ? "" : entries[i].Trim();
            if (string.IsNullOrEmpty(item)) continue;

            int eq = item.IndexOf('=');
            if (eq > 0) {
                string key = item.Substring(0, eq).Trim();
                string val = item.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(val)) continue;
                if (string.Equals(key, "http", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(http)) {
                    http = val;
                } else if (string.Equals(key, "https", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(https)) {
                    https = val;
                }
            } else if (string.IsNullOrEmpty(single)) {
                single = item;
            }
        }

        if (!string.IsNullOrEmpty(http)) endpoint = http;
        else if (!string.IsNullOrEmpty(https)) endpoint = https;
        else endpoint = single;
        return !string.IsNullOrEmpty(endpoint);
    }

    static bool TryBuildHttpProxyUrlFromProxyServerStatic(string proxyServer, out string proxyUrl) {
        proxyUrl = "";
        string endpoint;
        if (!TryResolveProxyEndpointFromProxyServerStatic(proxyServer, out endpoint)) return false;
        endpoint = (endpoint ?? "").Trim();
        if (string.IsNullOrEmpty(endpoint)) return false;

        int schemeSep = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (schemeSep > 0) endpoint = endpoint.Substring(schemeSep + 3);
        endpoint = endpoint.Trim();
        if (string.IsNullOrEmpty(endpoint)) return false;

        proxyUrl = "http://" + endpoint;
        return true;
    }

    static bool ApplyEnvRestoreAfterExitStatic(UuWatcherContext ctx, UuWatcherState state, out string source, out string detail) {
        source = "snapshot";
        detail = "";

        UuWatcherEnvSnapshot env = null;
        if (state != null && state.Snapshot != null) env = state.Snapshot.Env;
        if (IsEnvSnapshotMeaningfulStatic(env)) {
            bool fromSnapshot = ApplyUserProxyEnvironmentStatic(
                env.HttpProxyExists, env.HttpProxyValue,
                env.HttpsProxyExists, env.HttpsProxyValue,
                env.NoProxyExists, env.NoProxyValue,
                out detail);
            if (fromSnapshot) {
                WriteUuWatcherLog(ctx, "[INFO] ENV_RESTORE_FROM_SNAPSHOT hasHttp=" + (env.HttpProxyExists ? 1 : 0) + " hasHttps=" + (env.HttpsProxyExists ? 1 : 0) + " hasNoProxy=" + (env.NoProxyExists ? 1 : 0));
                NotifyUserEnvironmentChangedStatic(ctx);
                return true;
            }
            WriteUuWatcherLog(ctx, "[WARN] ENV_RESTORE_FAILED source=snapshot detail=" + detail);
            return false;
        }
        if (env != null && env.Captured) {
            WriteUuWatcherLog(ctx, "[WARN] ENV_RESTORE_FROM_SNAPSHOT mode=empty_fallback_system_proxy");
        }

        source = "systemProxyFallback";
        int proxyEnable; string proxyServer; string proxyOverride;
        GetProxySettingsStatic(out proxyEnable, out proxyServer, out proxyOverride);

        if (proxyEnable != 0) {
            string proxyUrl;
            if (TryBuildHttpProxyUrlFromProxyServerStatic(proxyServer, out proxyUrl)) {
                bool fallbackSet = ApplyUserProxyEnvironmentStatic(
                    true, proxyUrl,
                    true, proxyUrl,
                    true, UU_WATCHER_ENV_NO_PROXY_DEFAULT,
                    out detail);
                if (fallbackSet) {
                    WriteUuWatcherLog(ctx, "[INFO] ENV_RESTORE_FROM_SYSTEM_PROXY mode=set");
                    NotifyUserEnvironmentChangedStatic(ctx);
                    return true;
                }
                WriteUuWatcherLog(ctx, "[WARN] ENV_RESTORE_FAILED source=systemProxyFallback detail=" + detail);
                return false;
            }
        }

        bool fallbackClear = ApplyUserProxyEnvironmentStatic(
            false, "",
            false, "",
            false, "",
            out detail);
        if (fallbackClear) {
            if (proxyEnable == 0) WriteUuWatcherLog(ctx, "[WARN] ENV_RESTORE_FROM_SYSTEM_PROXY mode=clear_proxy_disabled");
            else WriteUuWatcherLog(ctx, "[WARN] ENV_RESTORE_FROM_SYSTEM_PROXY mode=clear_proxy_parse_fail");
            NotifyUserEnvironmentChangedStatic(ctx);
            return true;
        }

        WriteUuWatcherLog(ctx, "[WARN] ENV_RESTORE_FAILED source=systemProxyFallback detail=" + detail);
        return false;
    }

    static bool IsInvalidProxyOverrideTokenStatic(string item) {
        if (string.IsNullOrEmpty(item)) return true;
        string k = item.Trim().ToLowerInvariant();
        if (k == "[string]" || k == "[string[]]" || k == "[object]" || k == "[object[]]" || k == "system.string[]" || k == "system.object[]") return true;
        if (k.StartsWith("[") && k.EndsWith("]")) return true;
        return false;
    }

    static string MergeProxyOverrideStatic(string baseValue, string[] appendEntries) {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> result = new List<string>();
        if (!string.IsNullOrEmpty(baseValue)) {
            string[] arr = baseValue.Split(';');
            for (int i = 0; i < arr.Length; i++) {
                string item = arr[i] == null ? "" : arr[i].Trim();
                if (string.IsNullOrEmpty(item)) continue;
                if (IsInvalidProxyOverrideTokenStatic(item)) continue;
                if (seen.Add(item)) result.Add(item);
            }
        }
        if (appendEntries != null) {
            for (int i = 0; i < appendEntries.Length; i++) {
                string item = appendEntries[i] == null ? "" : appendEntries[i].Trim();
                if (string.IsNullOrEmpty(item)) continue;
                if (IsInvalidProxyOverrideTokenStatic(item)) continue;
                if (seen.Add(item)) result.Add(item);
            }
        }
        return string.Join(";", result.ToArray());
    }

    static string GetShortHashStatic(string text) {
        if (text == null) text = "";
        using (MD5 md5 = MD5.Create()) {
            byte[] input = Encoding.UTF8.GetBytes(text);
            byte[] hash = md5.ComputeHash(input);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            string full = sb.ToString();
            if (full.Length > 8) return full.Substring(0, 8);
            return full;
        }
    }

    static List<string> GetRunningTargetProgramPathsStatic() {
        Dictionary<string, string> set = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < UU_WATCHER_TARGET_PROCESS_NAMES.Length; i++) {
            string procName = UU_WATCHER_TARGET_PROCESS_NAMES[i];
            string baseName = procName;
            if (baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(0, baseName.Length - 4);
            try {
                Process[] procs = Process.GetProcessesByName(baseName);
                foreach (Process p in procs) {
                    try {
                        string path = "";
                        try { path = p.MainModule.FileName; } catch { path = ""; }
                        if (!string.IsNullOrEmpty(path) && !set.ContainsKey(path)) set[path] = path;
                    } finally { p.Dispose(); }
                }
            } catch { /* ignore */ }
        }
        List<string> result = new List<string>();
        foreach (var kv in set) result.Add(kv.Value);
        return result;
    }

    static List<UuWatcherFirewallRuleDef> BuildDynamicIsolationRuleDefinitionsStatic() {
        List<UuWatcherFirewallRuleDef> defs = new List<UuWatcherFirewallRuleDef>();
        List<string> paths = GetRunningTargetProgramPathsStatic();
        for (int p = 0; p < paths.Count; p++) {
            string path = paths[p];
            string file = "";
            try { file = Path.GetFileName(path).ToLowerInvariant(); } catch { file = "target.exe"; }
            string hash = GetShortHashStatic(path);
            for (int i = 0; i < 2; i++) {
                string protocol = i == 0 ? "TCP" : "UDP";
                UuWatcherFirewallRuleDef d = new UuWatcherFirewallRuleDef();
                d.DisplayName = UU_WATCHER_FIREWALL_RULE_PREFIX + "." + file + "." + protocol + "." + hash;
                d.Direction = "Outbound";
                d.Action = "Block";
                d.Enabled = "True";
                d.Profile = "Any";
                d.Program = path;
                d.Protocol = protocol;
                d.RemotePort = UU_WATCHER_LOCAL_PROXY_PORT.ToString();
                d.RemoteAddress = UU_WATCHER_LOCAL_PROXY_ADDRESS;
                defs.Add(d);
            }
        }
        return defs;
    }

    static string BuildFirewallSignatureStatic(List<UuWatcherFirewallRuleDef> defs) {
        if (defs == null || defs.Count == 0) return "";
        List<string> parts = new List<string>();
        for (int i = 0; i < defs.Count; i++) {
            UuWatcherFirewallRuleDef d = defs[i];
            parts.Add((d.Program ?? "") + "|" + (d.Protocol ?? "") + "|" + (d.RemoteAddress ?? "") + "|" + (d.RemotePort ?? ""));
        }
        parts.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(";", parts.ToArray());
    }

    static string CompactCommandOutputForLogStatic(string text, int maxLen) {
        if (string.IsNullOrEmpty(text)) return "";
        string oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (oneLine.IndexOf("  ", StringComparison.Ordinal) >= 0) oneLine = oneLine.Replace("  ", " ");
        if (oneLine.Length <= maxLen) return oneLine;
        return oneLine.Substring(0, maxLen) + "...";
    }

    static string BuildCommandDiagForLogStatic(string exe, string args, int exitCode, string stdout, string stderr, bool launched) {
        return "cmd=" + exe + " " + args
            + " launched=" + launched
            + " exitCode=" + exitCode
            + " stdout=\"" + CompactCommandOutputForLogStatic(stdout, 240) + "\""
            + " stderr=\"" + CompactCommandOutputForLogStatic(stderr, 240) + "\"";
    }

    static bool DeleteWatcherFirewallRuleGroupStatic() {
        int code;
        bool launched = RunProcessHiddenStatic("netsh.exe", "advfirewall firewall delete rule name=all group=\"" + UU_WATCHER_FIREWALL_RULE_GROUP + "\"", 10000, out code);
        if (!launched) return false;
        return code == 0 || code == 1;
    }

    static bool AddFirewallRuleStatic(UuWatcherFirewallRuleDef def) {
        if (def == null || string.IsNullOrEmpty(def.DisplayName) || string.IsNullOrEmpty(def.Program) || string.IsNullOrEmpty(def.Protocol)) return false;
        string args = "advfirewall firewall add rule " +
                      "name=\"" + def.DisplayName + "\" " +
                      "dir=out action=block " +
                      "enable=yes profile=any " +
                      "program=\"" + def.Program + "\" " +
                      "protocol=" + def.Protocol + " " +
                      "remoteip=" + UU_WATCHER_LOCAL_PROXY_ADDRESS + " " +
                      "remoteport=" + UU_WATCHER_LOCAL_PROXY_PORT + " " +
                      "group=\"" + UU_WATCHER_FIREWALL_RULE_GROUP + "\"";
        int code;
        bool launched = RunProcessHiddenStatic("netsh.exe", args, 10000, out code);
        if (!launched) return false;
        return code == 0;
    }

    static bool ResetWatcherFirewallRulesStatic(List<UuWatcherFirewallRuleDef> defs) {
        if (!DeleteWatcherFirewallRuleGroupStatic()) return false;
        if (defs == null || defs.Count == 0) return true;
        for (int i = 0; i < defs.Count; i++) {
            if (!AddFirewallRuleStatic(defs[i])) return false;
        }
        return true;
    }

    static bool TryResetWatcherFirewallRulesWithDiagStatic(List<UuWatcherFirewallRuleDef> defs, out string failDiag) {
        failDiag = "";

        string deleteArgs = "advfirewall firewall delete rule name=all group=\"" + UU_WATCHER_FIREWALL_RULE_GROUP + "\"";
        int deleteCode;
        string deleteOut;
        string deleteErr;
        bool deleteLaunched = RunProcessCaptureStatic("netsh.exe", deleteArgs, 10000, out deleteCode, out deleteOut, out deleteErr);
        if (!deleteLaunched || (deleteCode != 0 && deleteCode != 1)) {
            failDiag = BuildCommandDiagForLogStatic("netsh.exe", deleteArgs, deleteCode, deleteOut, deleteErr, deleteLaunched);
            return false;
        }

        if (defs == null || defs.Count == 0) return true;
        for (int i = 0; i < defs.Count; i++) {
            UuWatcherFirewallRuleDef def = defs[i];
            if (def == null) continue;
            string addArgs = "advfirewall firewall add rule " +
                             "name=\"" + def.DisplayName + "\" " +
                             "dir=out action=block " +
                             "enable=yes profile=any " +
                             "program=\"" + def.Program + "\" " +
                             "protocol=" + def.Protocol + " " +
                             "remoteip=" + UU_WATCHER_LOCAL_PROXY_ADDRESS + " " +
                             "remoteport=" + UU_WATCHER_LOCAL_PROXY_PORT + " " +
                             "group=\"" + UU_WATCHER_FIREWALL_RULE_GROUP + "\"";

            int addCode;
            string addOut;
            string addErr;
            bool addLaunched = RunProcessCaptureStatic("netsh.exe", addArgs, 10000, out addCode, out addOut, out addErr);
            if (!addLaunched || addCode != 0) {
                failDiag = "rule=" + (def.DisplayName ?? "") + " " + BuildCommandDiagForLogStatic("netsh.exe", addArgs, addCode, addOut, addErr, addLaunched);
                return false;
            }
        }
        return true;
    }

    static bool ApplyHardIsolationRulesStatic(UuWatcherContext ctx, UuWatcherState state) {
        if (!ctx.IsAdmin) {
            if (!state.HardIsolationUnavailable) {
                state.HardIsolationUnavailable = true;
                SaveUuWatcherState(ctx, state);
            }
            WriteUuWatcherLog(ctx, "[WARN] hard isolation unavailable: not running as administrator");
            return false;
        }
        List<UuWatcherFirewallRuleDef> defs = BuildDynamicIsolationRuleDefinitionsStatic();
        string failDiag;
        bool ok = TryResetWatcherFirewallRulesWithDiagStatic(defs, out failDiag);
        if (ok) {
            ctx.LastFirewallSignature = BuildFirewallSignatureStatic(defs);
            if (state.HardIsolationUnavailable) {
                state.HardIsolationUnavailable = false;
                SaveUuWatcherState(ctx, state);
            }
            WriteUuWatcherLog(ctx, "[INFO] hard isolation applied rules=" + defs.Count);
        } else {
            if (!state.HardIsolationUnavailable) {
                state.HardIsolationUnavailable = true;
                SaveUuWatcherState(ctx, state);
            }
            WriteUuWatcherLog(ctx, "[ALERT] HARD_ISOLATION_APPLY_FAIL switchId=" + state.SwitchId + " detail=" + failDiag);
        }
        return ok;
    }

    static void EnsureHardIsolationRulesForRunningTargetsStatic(UuWatcherContext ctx, UuWatcherState state) {
        if (!ctx.IsAdmin) {
            if (!state.HardIsolationUnavailable) {
                state.HardIsolationUnavailable = true;
                SaveUuWatcherState(ctx, state);
            }
            return;
        }
        List<UuWatcherFirewallRuleDef> defs = BuildDynamicIsolationRuleDefinitionsStatic();
        if (defs.Count == 0) return;
        string signature = BuildFirewallSignatureStatic(defs);
        if (signature == ctx.LastFirewallSignature) return;
        bool ok = ResetWatcherFirewallRulesStatic(defs);
        if (ok) {
            ctx.LastFirewallSignature = signature;
            WriteUuWatcherLog(ctx, "[INFO] hard isolation incrementally refreshed rules=" + defs.Count);
        }
    }

    static Dictionary<int, string> GetTargetProcessPidMapStatic(string[] processNames) {
        Dictionary<int, string> map = new Dictionary<int, string>();
        if (processNames == null) return map;
        for (int i = 0; i < processNames.Length; i++) {
            string proc = processNames[i];
            if (string.IsNullOrEmpty(proc)) continue;
            string baseName = proc;
            if (baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                baseName = baseName.Substring(0, baseName.Length - 4);
            }
            if (string.IsNullOrEmpty(baseName)) continue;
            try {
                Process[] procs = Process.GetProcessesByName(baseName);
                if (procs == null) continue;
                for (int p = 0; p < procs.Length; p++) {
                    Process one = procs[p];
                    try {
                        int pid = one.Id;
                        if (!map.ContainsKey(pid)) map[pid] = proc.ToLowerInvariant();
                    } catch { /* ignore */ }
                    finally { one.Dispose(); }
                }
            } catch { /* ignore */ }
        }
        return map;
    }

    static bool IsLoopbackProxyEndpointStatic(string endpoint) {
        if (string.IsNullOrEmpty(endpoint)) return false;
        string ep = endpoint.Trim().ToLowerInvariant();
        if (!ep.EndsWith(":7897")) return false;
        return ep.StartsWith("127.0.0.1:");
    }

    static List<UuWatcherLocalProxySignal> GetLocalProxyFaultSignalsStatic(UuWatcherContext ctx, string[] processNames) {
        List<UuWatcherLocalProxySignal> result = new List<UuWatcherLocalProxySignal>();
        Dictionary<int, string> pidMap = GetTargetProcessPidMapStatic(processNames);
        if (pidMap.Count == 0) return result;

        int code;
        string stdout, stderr;
        if (!RunProcessCaptureStatic("netstat.exe", "-ano -p tcp", 5000, out code, out stdout, out stderr)) return result;
        if (code != 0 || string.IsNullOrEmpty(stdout)) return result;

        Dictionary<string, UuWatcherLocalProxySignal> agg = new Dictionary<string, UuWatcherLocalProxySignal>(StringComparer.OrdinalIgnoreCase);
        string[] lines = stdout.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++) {
            string line = lines[i];
            if (string.IsNullOrEmpty(line)) continue;
            line = line.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;

            string remote = parts[2];
            if (!IsLoopbackProxyEndpointStatic(remote)) continue;

            int pid;
            if (!int.TryParse(parts[4], out pid)) continue;
            string proc;
            if (!pidMap.TryGetValue(pid, out proc)) continue;

            UuWatcherLocalProxySignal item;
            if (!agg.TryGetValue(proc, out item)) {
                item = new UuWatcherLocalProxySignal();
                item.Process = proc;
                agg[proc] = item;
            }

            string state = parts[3];
            if (string.Equals(state, "ESTABLISHED", StringComparison.OrdinalIgnoreCase)) item.Established++;
            else if (string.Equals(state, "SYN_SENT", StringComparison.OrdinalIgnoreCase)) item.SynSent++;
        }

        foreach (KeyValuePair<string, UuWatcherLocalProxySignal> kv in agg) {
            UuWatcherLocalProxySignal v = kv.Value;
            if (v.Established > 0 || v.SynSent > 0) result.Add(v);
        }
        return result;
    }

    static void EmitLocalProxyFaultSignalsStatic(UuWatcherContext ctx) {
        List<UuWatcherLocalProxySignal> hits = GetLocalProxyFaultSignalsStatic(ctx, UU_WATCHER_TARGET_PROCESS_NAMES);
        if (hits.Count == 0) return;

        DateTime now = DateTime.Now;
        if (ctx.LastLocal7897SignalAt != DateTime.MinValue && (now - ctx.LastLocal7897SignalAt).TotalSeconds < UU_WATCHER_POLICY_INTERVAL_SECONDS) return;
        ctx.LastLocal7897SignalAt = now;

        List<string> parts = new List<string>();
        for (int i = 0; i < hits.Count; i++) {
            UuWatcherLocalProxySignal h = hits[i];
            parts.Add((h.Process ?? "") + "(ESTABLISHED=" + h.Established + ",SYN_SENT=" + h.SynSent + ")");
        }
        WriteUuWatcherLog(ctx, "[ALERT] LOCAL_7897_FAULT_SIGNAL targets=" + string.Join("; ", parts.ToArray()));
    }

    static List<UuWatcherLeakConn> GetMihomoTargetConnectionsStatic(UuWatcherContext ctx, string[] processNames, bool onlyProxyChains) {
        List<UuWatcherLeakConn> hits = new List<UuWatcherLeakConn>();
        Dictionary<string, bool> targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (processNames != null) {
            for (int i = 0; i < processNames.Length; i++) {
                string p = processNames[i];
                if (!string.IsNullOrEmpty(p) && !targets.ContainsKey(p)) targets[p] = true;
            }
        }

        string resp, err;
        if (!InvokeMihomoRequest(ctx, "GET", "/connections", null, out resp, out err)) return hits;
        List<string> objs = ExtractTopLevelJsonObjectsFromArrayByKeyStatic(resp, "connections");
        for (int i = 0; i < objs.Count; i++) {
            string obj = objs[i];
            string proc = GetJsonStringValueStatic(obj, "process", "");
            if (string.IsNullOrEmpty(proc)) continue;
            if (!targets.ContainsKey(proc)) continue;
            List<string> chains = ParseJsonStringArrayStatic(obj, "chains");
            bool hasProxy = false;
            if (chains != null) {
                for (int c = 0; c < chains.Count; c++) {
                    string part = chains[c];
                    if (!string.IsNullOrEmpty(part) && part.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0) {
                        hasProxy = true;
                        break;
                    }
                }
            }
            if (onlyProxyChains && !hasProxy) continue;
            UuWatcherLeakConn hit = new UuWatcherLeakConn();
            hit.Id = GetJsonStringValueStatic(obj, "id", "");
            hit.Process = proc;
            hit.Host = GetJsonStringValueStatic(obj, "host", "");
            hit.Rule = GetJsonStringValueStatic(obj, "rule", "");
            hit.RulePayload = GetJsonStringValueStatic(obj, "rulePayload", "");
            hit.Chains = chains == null ? "" : string.Join(" > ", chains.ToArray());
            hits.Add(hit);
        }
        return hits;
    }

    static List<UuWatcherLeakConn> GetMihomoProxyLeakConnectionsStatic(UuWatcherContext ctx, string[] processNames) {
        return GetMihomoTargetConnectionsStatic(ctx, processNames, true);
    }

    static void EmitProxyChainLeakSignalsStatic(UuWatcherContext ctx) {
        List<UuWatcherLeakConn> leaks = GetMihomoProxyLeakConnectionsStatic(ctx, UU_WATCHER_TARGET_PROCESS_NAMES);
        if (leaks.Count == 0) return;

        DateTime now = DateTime.Now;
        if (ctx.LastProxyLeakSignalAt != DateTime.MinValue && (now - ctx.LastProxyLeakSignalAt).TotalSeconds < UU_WATCHER_POLICY_INTERVAL_SECONDS) return;
        ctx.LastProxyLeakSignalAt = now;

        Dictionary<string, int> byProcess = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < leaks.Count; i++) {
            string proc = leaks[i].Process ?? "";
            int v;
            if (!byProcess.TryGetValue(proc, out v)) v = 0;
            byProcess[proc] = v + 1;
        }

        List<string> procParts = new List<string>();
        foreach (KeyValuePair<string, int> kv in byProcess) procParts.Add(kv.Key + "=" + kv.Value);

        List<string> sample = new List<string>();
        for (int i = 0; i < leaks.Count && i < 3; i++) {
            UuWatcherLeakConn one = leaks[i];
            sample.Add((one.Process ?? "") + "|" + (one.Host ?? "") + "|" + (one.Chains ?? ""));
        }

        WriteUuWatcherLog(ctx, "[ALERT] PROXY_CHAIN_LEAK_DETECTED total=" + leaks.Count + " byProcess=" + string.Join(",", procParts.ToArray()) + " sample=" + string.Join(" ; ", sample.ToArray()));
    }

    static int DrainMihomoTargetConnectionsStatic(UuWatcherContext ctx, string[] processNames, int maxClose, bool onlyProxyChains, out int total) {
        total = 0;
        if (maxClose <= 0) return 0;
        List<UuWatcherLeakConn> hits = GetMihomoTargetConnectionsStatic(ctx, processNames, onlyProxyChains);
        total = hits.Count;
        if (hits.Count == 0) return 0;
        int closeCount = 0;
        for (int i = 0; i < hits.Count && i < maxClose; i++) {
            string id = hits[i].Id;
            if (string.IsNullOrEmpty(id)) continue;
            string resp;
            string err;
            bool ok = InvokeMihomoRequest(ctx, "DELETE", "/connections/" + id, null, out resp, out err);
            if (ok) closeCount++;
        }
        return closeCount;
    }

    static void DrainMihomoProxyLeakConnectionsStatic(UuWatcherContext ctx, string[] processNames, bool force) {
        DateTime now = DateTime.Now;
        if (!force && (now - ctx.LastLeakDrainAt).TotalSeconds < UU_WATCHER_LEAK_DRAIN_MIN_INTERVAL_SECONDS) return;
        int total;
        int closeCount = DrainMihomoTargetConnectionsStatic(ctx, processNames, UU_WATCHER_LEAK_DRAIN_MAX_PER_ROUND, true, out total);
        if (total == 0) {
            ctx.LastLeakDrainAt = now;
            return;
        }
        ctx.LastLeakDrainAt = now;
        if (closeCount > 0) {
            WriteUuWatcherLog(ctx, "[WARN] drained proxy leak connections closed=" + closeCount + " total=" + total + " limit=" + UU_WATCHER_LEAK_DRAIN_MAX_PER_ROUND + " scope=tslgame.exe");
        }
    }

    static int CountLocalProxyFaultForProcessesStatic(List<UuWatcherLocalProxySignal> hits, params string[] processes) {
        if (hits == null || hits.Count == 0 || processes == null || processes.Length == 0) return 0;
        int total = 0;
        for (int i = 0; i < hits.Count; i++) {
            UuWatcherLocalProxySignal h = hits[i];
            string proc = h == null ? "" : h.Process;
            if (string.IsNullOrEmpty(proc)) continue;
            for (int p = 0; p < processes.Length; p++) {
                string expected = processes[p];
                if (string.IsNullOrEmpty(expected)) continue;
                if (string.Equals(proc, expected, StringComparison.OrdinalIgnoreCase)) {
                    total += h.Established + h.SynSent;
                    break;
                }
            }
        }
        return total;
    }

    static void RunUuTakeoverOneShotDrainStatic(UuWatcherContext ctx, UuWatcherState state) {
        string switchId = state.SwitchId ?? "";
        if (string.IsNullOrEmpty(switchId)) return;

        if (!string.Equals(ctx.LastTakeoverDrainSwitchId, switchId, StringComparison.Ordinal)) {
            int oneShotTotal;
            int oneShotClosed = DrainMihomoTargetConnectionsStatic(ctx, UU_WATCHER_TARGET_PROCESS_NAMES, UU_WATCHER_TAKEOVER_DRAIN_MAX_ON_ENTER, false, out oneShotTotal);
            ctx.LastTakeoverDrainSwitchId = switchId;
            WriteUuWatcherLog(ctx, "[INFO] UU_TAKEOVER_ONE_SHOT_DRAIN switchId=" + switchId + " phase=initial closed=" + oneShotClosed + " total=" + oneShotTotal + " limit=" + UU_WATCHER_TAKEOVER_DRAIN_MAX_ON_ENTER + " proxyOnly=false");
            Thread.Sleep(UU_WATCHER_TAKEOVER_RECHECK_DELAY_MS);
        }

        List<UuWatcherLocalProxySignal> hits = GetLocalProxyFaultSignalsStatic(ctx, UU_WATCHER_TARGET_PROCESS_NAMES);
        int steam7897 = CountLocalProxyFaultForProcessesStatic(hits, "steam.exe", "steamwebhelper.exe");
        int tsl7897 = CountLocalProxyFaultForProcessesStatic(hits, "tslgame.exe");

        if (steam7897 > 0 && !string.Equals(ctx.LastTakeoverCompSwitchId, switchId, StringComparison.Ordinal)) {
            int compTotal;
            int compClosed = DrainMihomoTargetConnectionsStatic(ctx, UU_WATCHER_TARGET_PROCESS_NAMES, UU_WATCHER_TAKEOVER_DRAIN_MAX_COMPENSATION, false, out compTotal);
            ctx.LastTakeoverCompSwitchId = switchId;
            WriteUuWatcherLog(ctx, "[INFO] UU_TAKEOVER_ONE_SHOT_DRAIN switchId=" + switchId + " phase=compensation closed=" + compClosed + " total=" + compTotal + " limit=" + UU_WATCHER_TAKEOVER_DRAIN_MAX_COMPENSATION + " steam7897Before=" + steam7897 + " tsl7897Before=" + tsl7897 + " proxyOnly=false");
            Thread.Sleep(UU_WATCHER_TAKEOVER_RECHECK_DELAY_MS);

            hits = GetLocalProxyFaultSignalsStatic(ctx, UU_WATCHER_TARGET_PROCESS_NAMES);
            steam7897 = CountLocalProxyFaultForProcessesStatic(hits, "steam.exe", "steamwebhelper.exe");
            tsl7897 = CountLocalProxyFaultForProcessesStatic(hits, "tslgame.exe");
        }

        if (steam7897 > 0) {
            string routeNow = UuGetRouteNow(ctx);
            WriteUuWatcherLog(ctx, "[ALERT] STEAM_UU_TAKEOVER_NOT_COMPLETE switchId=" + switchId
                + " steam7897Count=" + steam7897
                + " tsl7897Count=" + tsl7897
                + " hardIsolationUnavailable=" + state.HardIsolationUnavailable
                + " routeNow=" + routeNow);
        }
    }

    static int GetRetryDelaySecondsStatic(int retryCount) {
        if (retryCount < 0) retryCount = 0;
        if (retryCount >= UU_WATCHER_RETRY_DELAYS_SECONDS.Length) return UU_WATCHER_RETRY_DELAYS_SECONDS[UU_WATCHER_RETRY_DELAYS_SECONDS.Length - 1];
        return UU_WATCHER_RETRY_DELAYS_SECONDS[retryCount];
    }

    static bool UpdateQuarantineStateStatic(UuWatcherContext ctx, UuWatcherState state) {
        DateTime now = DateTime.Now;
        DateTime until = ParseUuWatcherTime(state.QuarantineUntil);
        if (until > now) {
            ctx.QuarantineActive = true;
            return true;
        }
        if (ctx.QuarantineActive) WriteUuWatcherLog(ctx, "[INFO] QUARANTINE_EXIT");
        ctx.QuarantineActive = false;
        if (!string.IsNullOrEmpty(state.QuarantineUntil)) {
            state.QuarantineUntil = "";
            SaveUuWatcherState(ctx, state);
        }
        return false;
    }

    static void RegisterSwitchEventStatic(UuWatcherContext ctx, UuWatcherState state, DateTime now, string switchId) {
        List<string> window = new List<string>();
        if (state.SwitchWindow != null) {
            for (int i = 0; i < state.SwitchWindow.Count; i++) {
                DateTime t = ParseUuWatcherTime(state.SwitchWindow[i]);
                if (t == DateTime.MinValue) continue;
                if ((now - t).TotalSeconds <= UU_WATCHER_FLAP_WINDOW_SECONDS) window.Add(t.ToString("o"));
            }
        }
        window.Add(now.ToString("o"));
        state.SwitchWindow = window;
        state.FlapCounter = window.Count;
        state.LastSwitchAt = now.ToString("o");
        if (window.Count > UU_WATCHER_FLAP_SWITCH_THRESHOLD) {
            DateTime until = now.AddSeconds(UU_WATCHER_QUARANTINE_SECONDS);
            state.QuarantineUntil = until.ToString("o");
            ctx.QuarantineActive = true;
            WriteUuWatcherLog(ctx, "[WARN] QUARANTINE_ENTER switchId=" + switchId + " switchesIn60s=" + window.Count + " until=" + until.ToString("o"));
            WriteUuWatcherLog(ctx, "[ALERT] switchesIn60s=" + window.Count + " threshold=" + UU_WATCHER_FLAP_SWITCH_THRESHOLD);
        }
    }

    static bool CanPerformSwitchStatic(UuWatcherContext ctx, UuWatcherState state, bool ignoreInterval) {
        DateTime now = DateTime.Now;
        if (UpdateQuarantineStateStatic(ctx, state)) {
            if ((now - ctx.LastQuarantineSkipWarnAt).TotalSeconds >= 5) {
                WriteUuWatcherLog(ctx, "[WARN] switch skipped due to quarantine");
                ctx.LastQuarantineSkipWarnAt = now;
            }
            return false;
        }
        if (!ignoreInterval) {
            DateTime last = ParseUuWatcherTime(state.LastSwitchAt);
            if (last != DateTime.MinValue && (now - last).TotalSeconds < UU_WATCHER_MIN_SWITCH_INTERVAL_SECONDS) {
                if ((now - ctx.LastMinSwitchWarnAt).TotalSeconds >= 5) {
                    WriteUuWatcherLog(ctx, "[WARN] switch skipped due to minSwitchInterval=" + UU_WATCHER_MIN_SWITCH_INTERVAL_SECONDS + "s");
                    ctx.LastMinSwitchWarnAt = now;
                }
                return false;
            }
        }
        return true;
    }

    static void EnsureEnterSnapshotStatic(UuWatcherContext ctx, UuWatcherState state) {
        if (state.Snapshot == null) state.Snapshot = new UuWatcherSnapshot();
        if (state.Snapshot.FwRules == null) state.Snapshot.FwRules = new List<UuWatcherFirewallRuleDef>();
        if (state.Snapshot.Env == null) state.Snapshot.Env = new UuWatcherEnvSnapshot();

        bool hasCoreSnapshot = false;
        if (!string.IsNullOrEmpty(state.Snapshot.Route) || !string.IsNullOrEmpty(state.Snapshot.ProxyOverride)) hasCoreSnapshot = true;
        if (state.Snapshot.FwRules.Count > 0) hasCoreSnapshot = true;
        bool hasEnvSnapshot = state.Snapshot.Env.Captured;
        bool changed = false;

        if (!hasCoreSnapshot) {
            string routeBefore = UuGetRouteNow(ctx);
            int proxyEnable; string proxyServer; string proxyOverride;
            GetProxySettingsStatic(out proxyEnable, out proxyServer, out proxyOverride);
            state.Snapshot.Route = routeBefore;
            state.Snapshot.ProxyOverride = MergeProxyOverrideStatic(proxyOverride, new string[0]);
            state.Snapshot.ProxyEnable = proxyEnable;
            state.Snapshot.FwRules = new List<UuWatcherFirewallRuleDef>();
            changed = true;
            WriteUuWatcherLog(ctx, "[INFO] snapshot captured route=" + routeBefore + " proxyEnable=" + proxyEnable + " fwRules=0");
        }

        if (!hasEnvSnapshot) {
            CaptureUserEnvSnapshotStatic(state.Snapshot.Env);
            changed = true;
            WriteUuWatcherLog(ctx, "[INFO] ENV_SNAPSHOT_CAPTURED hasHttp=" + (state.Snapshot.Env.HttpProxyExists ? 1 : 0) + " hasHttps=" + (state.Snapshot.Env.HttpsProxyExists ? 1 : 0) + " hasNoProxy=" + (state.Snapshot.Env.NoProxyExists ? 1 : 0));
        }

        if (changed) SaveUuWatcherState(ctx, state);
    }

    static void EnterUuModeStatic(UuWatcherContext ctx, UuWatcherState state, bool ignoreSwitchInterval) {
        if (!CanPerformSwitchStatic(ctx, state, ignoreSwitchInterval)) return;
        DateTime now = DateTime.Now;
        string switchId = NewUuWatcherSwitchId();
        RegisterSwitchEventStatic(ctx, state, now, switchId);
        state.Mode = "ENTERING_UU";
        state.DesiredMode = "UU_ACTIVE";
        state.SwitchId = switchId;
        SaveUuWatcherState(ctx, state);
        WriteUuWatcherLog(ctx, "[INFO] ENTER_BEGIN switchId=" + switchId);

        EnsureEnterSnapshotStatic(ctx, state);
        bool routeOk = UuSetRouteNow(ctx, "DIRECT");
        int proxyEnable; string proxyServer; string proxyOverride;
        GetProxySettingsStatic(out proxyEnable, out proxyServer, out proxyOverride);
        string merged = MergeProxyOverrideStatic(proxyOverride, UU_WATCHER_BYPASS_ENTRIES);
        bool overrideOk = SetProxyOverrideStatic(merged);
        bool hardOk = ApplyHardIsolationRulesStatic(ctx, state);
        RunUuTakeoverOneShotDrainStatic(ctx, state);

        state.Mode = "UU_ACTIVE";
        state.DesiredMode = "UU_ACTIVE";
        state.RollbackPending = false;
        state.RollbackPendingSince = "";
        state.RetryCount = 0;
        state.NextRetryAt = "";
        SaveUuWatcherState(ctx, state);
        WriteUuWatcherLog(ctx, "[INFO] ENTER_DONE switchId=" + switchId + " routeOk=" + routeOk + " overrideOk=" + overrideOk + " hardIsolationOk=" + hardOk);
    }

    static bool TryCompleteExitRollbackStatic(UuWatcherContext ctx, UuWatcherState state, string source) {
        if (state.Snapshot == null) state.Snapshot = new UuWatcherSnapshot();
        if (state.Snapshot.FwRules == null) state.Snapshot.FwRules = new List<UuWatcherFirewallRuleDef>();
        if (state.Snapshot.Env == null) state.Snapshot.Env = new UuWatcherEnvSnapshot();

        string routeTarget = state.Snapshot.Route;
        if (string.IsNullOrEmpty(routeTarget)) {
            routeTarget = "Proxy";
            WriteUuWatcherLog(ctx, "[WARN] snapshot.route missing, fallback route target=Proxy");
        }
        string overrideTarget = state.Snapshot.ProxyOverride ?? "";
        int proxyEnableTarget = state.Snapshot.ProxyEnable;
        List<UuWatcherFirewallRuleDef> fwRules = state.Snapshot.FwRules ?? new List<UuWatcherFirewallRuleDef>();

        bool routeOk = UuSetRouteNow(ctx, routeTarget);
        bool overrideOk = SetProxyOverrideStatic(overrideTarget);
        bool proxyEnableOk = SetProxyEnableStatic(proxyEnableTarget);
        bool fwRestoreOk = true;
        if (ctx.IsAdmin) {
            fwRestoreOk = ResetWatcherFirewallRulesStatic(fwRules);
        } else {
            state.HardIsolationUnavailable = true;
            SaveUuWatcherState(ctx, state);
        }

        string envSource;
        string envDetail;
        bool envRestoreOk = false;
        if (routeOk && overrideOk && proxyEnableOk && fwRestoreOk) {
            envRestoreOk = ApplyEnvRestoreAfterExitStatic(ctx, state, out envSource, out envDetail);
        } else {
            envSource = "skipped";
            envDetail = "";
        }

        if (routeOk && overrideOk && proxyEnableOk && fwRestoreOk && envRestoreOk) {
            if (source == "retry") WriteUuWatcherLog(ctx, "[INFO] EXIT_RETRY_SUCCESS switchId=" + state.SwitchId);
            state.Mode = "NORMAL";
            state.DesiredMode = "NORMAL";
            state.RollbackPending = false;
            state.RollbackPendingSince = "";
            state.RetryCount = 0;
            state.NextRetryAt = "";
            state.Snapshot = new UuWatcherSnapshot();
            SaveUuWatcherState(ctx, state);
            WriteUuWatcherLog(ctx, "[INFO] EXIT_DONE switchId=" + state.SwitchId + " route=" + routeTarget);
            return true;
        }

        DateTime now = DateTime.Now;
        int delay = GetRetryDelaySecondsStatic(state.RetryCount);
        DateTime next = now.AddSeconds(delay);
        state.Mode = "DEGRADED_EXIT_PENDING";
        state.DesiredMode = "NORMAL";
        state.RollbackPending = true;
        if (string.IsNullOrEmpty(state.RollbackPendingSince)) state.RollbackPendingSince = now.ToString("o");
        state.RetryCount = state.RetryCount + 1;
        state.NextRetryAt = next.ToString("o");
        SaveUuWatcherState(ctx, state);
        WriteUuWatcherLog(ctx, "[WARN] EXIT_RETRY_PENDING switchId=" + state.SwitchId + " source=" + source + " retryCount=" + state.RetryCount + " nextRetryAt=" + state.NextRetryAt + " routeOk=" + routeOk + " overrideOk=" + overrideOk + " proxyEnableOk=" + proxyEnableOk + " fwRestoreOk=" + fwRestoreOk + " envRestoreOk=" + envRestoreOk + " envSource=" + envSource + " envDetail=" + envDetail);
        return false;
    }

    static void ExitUuModeStatic(UuWatcherContext ctx, UuWatcherState state, bool ignoreSwitchInterval) {
        if (!CanPerformSwitchStatic(ctx, state, ignoreSwitchInterval)) return;
        DateTime now = DateTime.Now;
        string switchId = NewUuWatcherSwitchId();
        RegisterSwitchEventStatic(ctx, state, now, switchId);
        state.Mode = "EXITING_UU";
        state.DesiredMode = "NORMAL";
        state.SwitchId = switchId;
        SaveUuWatcherState(ctx, state);
        WriteUuWatcherLog(ctx, "[INFO] EXIT_BEGIN switchId=" + switchId);

        bool phase1Ok = true;
        if (ctx.IsAdmin) {
            phase1Ok = ResetWatcherFirewallRulesStatic(new List<UuWatcherFirewallRuleDef>());
        } else {
            state.HardIsolationUnavailable = true;
            SaveUuWatcherState(ctx, state);
            phase1Ok = false;
        }
        WriteUuWatcherLog(ctx, "[INFO] EXIT_PHASE1_DONE switchId=" + switchId + " hardIsolationRemoved=" + phase1Ok);
        TryCompleteExitRollbackStatic(ctx, state, "exit");
    }

    static void ForceExitOnStopStatic(UuWatcherContext ctx, UuWatcherState state) {
        if (ctx == null || state == null) return;
        if (state.Snapshot == null) state.Snapshot = new UuWatcherSnapshot();
        if (state.Snapshot.FwRules == null) state.Snapshot.FwRules = new List<UuWatcherFirewallRuleDef>();
        if (state.Snapshot.Env == null) state.Snapshot.Env = new UuWatcherEnvSnapshot();

        WriteUuWatcherLog(ctx, "[INFO] STOP_FORCE_EXIT_BEGIN mode=" + state.Mode + " desired=" + state.DesiredMode + " rollbackPending=" + state.RollbackPending);

        bool hasSnapshotData = !string.IsNullOrEmpty(state.Snapshot.Route)
            || !string.IsNullOrEmpty(state.Snapshot.ProxyOverride)
            || state.Snapshot.ProxyEnable != 0
            || state.Snapshot.FwRules.Count > 0
            || state.Snapshot.Env.Captured;
        bool needRollback = state.Mode == "UU_ACTIVE"
            || state.Mode == "ENTERING_UU"
            || state.Mode == "EXITING_UU"
            || (state.Mode == "DEGRADED_EXIT_PENDING" && state.RollbackPending)
            || hasSnapshotData;

        if (!needRollback) {
            bool cleanupOk = true;
            if (ctx.IsAdmin) cleanupOk = ResetWatcherFirewallRulesStatic(new List<UuWatcherFirewallRuleDef>());
            string envSource;
            string envDetail;
            bool envRestoreOk = ApplyEnvRestoreAfterExitStatic(ctx, state, out envSource, out envDetail);
            WriteUuWatcherLog(ctx, "[INFO] STOP_FORCE_EXIT_DONE action=noop hardIsolationRemoved=" + cleanupOk + " envRestoreOk=" + envRestoreOk + " envSource=" + envSource + " envDetail=" + envDetail);
            return;
        }

        state.Mode = "EXITING_UU";
        state.DesiredMode = "NORMAL";
        SaveUuWatcherState(ctx, state);

        bool phase1Ok = true;
        if (ctx.IsAdmin) {
            phase1Ok = ResetWatcherFirewallRulesStatic(new List<UuWatcherFirewallRuleDef>());
        } else {
            state.HardIsolationUnavailable = true;
            SaveUuWatcherState(ctx, state);
            phase1Ok = false;
        }

        bool rollbackOk = TryCompleteExitRollbackStatic(ctx, state, "stop");
        WriteUuWatcherLog(ctx, "[INFO] STOP_FORCE_EXIT_DONE hardIsolationRemoved=" + phase1Ok + " rollbackOk=" + rollbackOk + " mode=" + state.Mode + " desired=" + state.DesiredMode);
    }

    static void HandlePendingRollbackStatic(UuWatcherContext ctx, UuWatcherState state, bool force) {
        if (!state.RollbackPending) return;
        if (state.DesiredMode == "UU_ACTIVE") return;
        DateTime next = ParseUuWatcherTime(state.NextRetryAt);
        if (!force && next != DateTime.MinValue && next > DateTime.Now) return;
        TryCompleteExitRollbackStatic(ctx, state, "retry");
    }

    static void EnforceUuPolicyStatic(UuWatcherContext ctx, UuWatcherState state, bool force) {
        DateTime now = DateTime.Now;
        if (!force && (now - ctx.LastPolicyEnforceAt).TotalSeconds < UU_WATCHER_POLICY_INTERVAL_SECONDS) return;
        ctx.LastPolicyEnforceAt = now;

        string routeNow = UuGetRouteNow(ctx);
        if (!string.IsNullOrEmpty(routeNow) && !string.Equals(routeNow, "DIRECT", StringComparison.OrdinalIgnoreCase)) {
            WriteUuWatcherLog(ctx, "[WARN] policy correction route " + routeNow + " -> DIRECT");
            UuSetRouteNow(ctx, "DIRECT");
        }

        int proxyEnable; string proxyServer; string proxyOverride;
        GetProxySettingsStatic(out proxyEnable, out proxyServer, out proxyOverride);
        string merged = MergeProxyOverrideStatic(proxyOverride, UU_WATCHER_BYPASS_ENTRIES);
        if (!string.Equals(merged, proxyOverride, StringComparison.Ordinal)) {
            WriteUuWatcherLog(ctx, "[INFO] policy correction proxy override");
            SetProxyOverrideStatic(merged);
        }

        EnsureHardIsolationRulesForRunningTargetsStatic(ctx, state);
        EmitLocalProxyFaultSignalsStatic(ctx);
        EmitProxyChainLeakSignalsStatic(ctx);
        DrainMihomoProxyLeakConnectionsStatic(ctx, UU_WATCHER_LEAK_DRAIN_PROCESS_NAMES, false);
    }

    static void EmitUuHealthAlertsStatic(UuWatcherContext ctx, UuWatcherState state) {
        if (state.Mode == "DEGRADED_EXIT_PENDING" && state.RollbackPending) {
            DateTime since = ParseUuWatcherTime(state.RollbackPendingSince);
            if (since != DateTime.MinValue) {
                TimeSpan elapsed = DateTime.Now - since;
                if (elapsed.TotalSeconds > UU_WATCHER_PENDING_ALERT_SECONDS) {
                    if (ctx.LastPendingAlertAt == DateTime.MinValue || (DateTime.Now - ctx.LastPendingAlertAt).TotalSeconds >= 60) {
                        WriteUuWatcherLog(ctx, "[ALERT] DEGRADED_EXIT_PENDING durationSec=" + ((int)elapsed.TotalSeconds).ToString());
                        ctx.LastPendingAlertAt = DateTime.Now;
                    }
                }
            }
        }
    }

    static bool IsUuActiveLikeModeStatic(string mode) {
        if (string.IsNullOrEmpty(mode)) return false;
        return string.Equals(mode, "UU_ACTIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "ENTERING_UU", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "DEGRADED_EXIT_PENDING", StringComparison.OrdinalIgnoreCase);
    }

    static bool HandleAdminRequirementForUuStatic(UuWatcherContext ctx, UuWatcherState state, bool uuRunning, string reason, bool ignoreSwitchInterval) {
        if (!uuRunning || ctx.IsAdmin) return false;

        DateTime now = DateTime.Now;
        if (ctx.LastAdminRequiredAlertAt == DateTime.MinValue || (now - ctx.LastAdminRequiredAlertAt).TotalSeconds >= 60) {
            WriteUuWatcherLog(ctx, "[ALERT] ADMIN_REQUIRED_FOR_UU reason=" + reason + " mode=" + state.Mode + " desired=" + state.DesiredMode);
            ctx.LastAdminRequiredAlertAt = now;
        }

        bool changed = false;
        if (!state.HardIsolationUnavailable) {
            state.HardIsolationUnavailable = true;
            changed = true;
        }
        if (!string.Equals(state.DesiredMode, "NORMAL", StringComparison.Ordinal)) {
            state.DesiredMode = "NORMAL";
            changed = true;
        }
        if (changed) SaveUuWatcherState(ctx, state);

        if (state.Mode == "UU_ACTIVE" || state.Mode == "ENTERING_UU") {
            ExitUuModeStatic(ctx, state, ignoreSwitchInterval);
        } else if ((state.Mode == "DEGRADED_EXIT_PENDING" && state.RollbackPending) || state.Mode == "EXITING_UU") {
            HandlePendingRollbackStatic(ctx, state, true);
        } else if (IsUuActiveLikeModeStatic(state.Mode)) {
            state.Mode = "NORMAL";
            SaveUuWatcherState(ctx, state);
        }

        return true;
    }

    static void InvokeUuReconcileStatic(UuWatcherContext ctx, UuWatcherState state, bool uuRunning, string reason, bool ignoreSwitchInterval) {
        WriteUuWatcherLog(ctx, "[INFO] RECONCILE_BEGIN reason=" + reason + " mode=" + state.Mode + " desired=" + state.DesiredMode + " uuRunning=" + uuRunning);
        bool adminRequired = HandleAdminRequirementForUuStatic(ctx, state, uuRunning, reason, ignoreSwitchInterval);
        if (adminRequired) {
            WriteUuWatcherLog(ctx, "[INFO] RECONCILE_DONE reason=" + reason + " mode=" + state.Mode + " desired=" + state.DesiredMode);
            return;
        }

        string desired = uuRunning ? "UU_ACTIVE" : "NORMAL";
        if (state.DesiredMode != desired) {
            state.DesiredMode = desired;
            SaveUuWatcherState(ctx, state);
        }

        if (uuRunning) {
            if (state.Mode == "DEGRADED_EXIT_PENDING") {
                state.Mode = "UU_ACTIVE";
                state.RollbackPending = false;
                state.RollbackPendingSince = "";
                state.RetryCount = 0;
                state.NextRetryAt = "";
                SaveUuWatcherState(ctx, state);
                WriteUuWatcherLog(ctx, "[INFO] reconcile cancelled pending rollback because UU is ON");
            }
            if (state.Mode != "UU_ACTIVE") {
                EnterUuModeStatic(ctx, state, ignoreSwitchInterval);
            } else {
                EnforceUuPolicyStatic(ctx, state, true);
            }
        } else {
            if (state.Mode == "UU_ACTIVE" || state.Mode == "ENTERING_UU") {
                ExitUuModeStatic(ctx, state, ignoreSwitchInterval);
            } else if (state.Mode == "DEGRADED_EXIT_PENDING" && state.RollbackPending) {
                HandlePendingRollbackStatic(ctx, state, true);
            } else {
                if (ctx.IsAdmin) {
                    ResetWatcherFirewallRulesStatic(new List<UuWatcherFirewallRuleDef>());
                }
            }
        }
        WriteUuWatcherLog(ctx, "[INFO] RECONCILE_DONE reason=" + reason + " mode=" + state.Mode + " desired=" + state.DesiredMode);
    }

    static void RunUuRouteWatcherLoop() {
        bool createdNew;
        using (Mutex mutex = new Mutex(true, UU_WATCHER_MUTEX_NAME, out createdNew)) {
            if (!createdNew) return;

            UuWatcherContext ctx = new UuWatcherContext();
            ctx.RootDir = UuWatcherRootDir();
            try { Directory.CreateDirectory(ctx.RootDir); } catch { }
            ctx.StateFile = Path.Combine(ctx.RootDir, UU_WATCHER_STATE_FILE_NAME);
            ctx.LogFile = Path.Combine(ctx.RootDir, UU_WATCHER_LOG_FILE_NAME);
            ctx.HeartbeatFile = Path.Combine(ctx.RootDir, UU_WATCHER_HEARTBEAT_FILE_NAME);
            string[] apiCfg = LoadWatcherApiConfig();
            ctx.ApiBase = apiCfg[0];
            ctx.ApiSecret = apiCfg[1];
            ctx.IsAdmin = false;
            try {
                WindowsIdentity wi = WindowsIdentity.GetCurrent();
                WindowsPrincipal wp = new WindowsPrincipal(wi);
                ctx.IsAdmin = wp.IsInRole(WindowsBuiltInRole.Administrator);
            } catch { ctx.IsAdmin = false; }
            ctx.InstanceId = Guid.NewGuid().ToString("N");
            try {
                ctx.StopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, UU_WATCHER_STOP_EVENT_NAME);
                ctx.StopEvent.Reset();
            } catch { ctx.StopEvent = null; }

            WriteUuWatcherLog(ctx, "[INFO] watcher starting instanceId=" + ctx.InstanceId + " admin=" + ctx.IsAdmin);
            WriteUuWatcherLog(ctx, "[INFO] policy version=" + UU_WATCHER_POLICY_TAG);

            UuWatcherState state = LoadUuWatcherState(ctx);
            if (!ctx.IsAdmin && !state.HardIsolationUnavailable) {
                state.HardIsolationUnavailable = true;
                SaveUuWatcherState(ctx, state);
            }

            InitUuWatcherHeartbeat(ctx);
            bool uuRaw = TestUuRunningStatic();
            DateTime uuRawChangedAt = DateTime.Now;
            bool uuStable = uuRaw;
            InvokeUuReconcileStatic(ctx, state, uuStable, "startup", true);

            DateTime lastLoopAt = DateTime.Now;
            bool stopRequested = false;
            while (true) {
                try {
                    EnsureUuWatcherHeartbeat(ctx);

                    DateTime now = DateTime.Now;
                    if ((now - lastLoopAt).TotalSeconds >= UU_WATCHER_WAKE_GAP_SECONDS) {
                        WriteUuWatcherLog(ctx, "[INFO] wake detected gapSec=" + ((int)(now - lastLoopAt).TotalSeconds).ToString());
                        InvokeUuReconcileStatic(ctx, state, uuStable, "wake", true);
                    }
                    lastLoopAt = now;

                    bool currentRaw = TestUuRunningStatic();
                    if (currentRaw != uuRaw) {
                        uuRaw = currentRaw;
                        uuRawChangedAt = DateTime.Now;
                        WriteUuWatcherLog(ctx, "[INFO] uu raw status changed running=" + uuRaw);
                    }
                    if (uuStable != uuRaw) {
                        if ((DateTime.Now - uuRawChangedAt).TotalSeconds >= UU_WATCHER_DEBOUNCE_SECONDS) {
                            uuStable = uuRaw;
                            WriteUuWatcherLog(ctx, "[INFO] uu stable status changed running=" + uuStable);
                        }
                    }

                    string desired = (uuStable && ctx.IsAdmin) ? "UU_ACTIVE" : "NORMAL";
                    if (state.DesiredMode != desired) {
                        state.DesiredMode = desired;
                        SaveUuWatcherState(ctx, state);
                    }

                    bool adminRequired = HandleAdminRequirementForUuStatic(ctx, state, uuStable, "loop", true);
                    if (adminRequired) {
                        EmitUuHealthAlertsStatic(ctx, state);
                    } else {
                        bool inQuarantine = UpdateQuarantineStateStatic(ctx, state);
                        if (inQuarantine) {
                            EmitUuHealthAlertsStatic(ctx, state);
                        } else {
                            if (state.DesiredMode == "UU_ACTIVE") {
                                if (state.Mode == "NORMAL" || state.Mode == "EXITING_UU") {
                                    EnterUuModeStatic(ctx, state, false);
                                } else if (state.Mode == "DEGRADED_EXIT_PENDING") {
                                    state.Mode = "UU_ACTIVE";
                                    state.RollbackPending = false;
                                    state.RollbackPendingSince = "";
                                    state.RetryCount = 0;
                                    state.NextRetryAt = "";
                                    SaveUuWatcherState(ctx, state);
                                    WriteUuWatcherLog(ctx, "[INFO] switched from DEGRADED_EXIT_PENDING back to UU_ACTIVE because UU is ON");
                                    EnforceUuPolicyStatic(ctx, state, true);
                                } else {
                                    EnforceUuPolicyStatic(ctx, state, false);
                                }
                            } else {
                                if (state.Mode == "UU_ACTIVE" || state.Mode == "ENTERING_UU") {
                                    ExitUuModeStatic(ctx, state, false);
                                } else if (state.Mode == "DEGRADED_EXIT_PENDING" && state.RollbackPending) {
                                    HandlePendingRollbackStatic(ctx, state, false);
                                } else if (state.Mode == "EXITING_UU") {
                                    HandlePendingRollbackStatic(ctx, state, true);
                                }
                            }
                            EmitUuHealthAlertsStatic(ctx, state);
                        }
                    }
                } catch (Exception ex) {
                    WriteUuWatcherLog(ctx, "[ERROR] loop exception: " + ex.Message);
                }

                try {
                    if (ctx.StopEvent != null) {
                        if (ctx.StopEvent.WaitOne(UU_WATCHER_POLL_SECONDS * 1000)) {
                            stopRequested = true;
                            break;
                        }
                    } else {
                        Thread.Sleep(UU_WATCHER_POLL_SECONDS * 1000);
                    }
                } catch {
                    Thread.Sleep(UU_WATCHER_POLL_SECONDS * 1000);
                }
            }

            if (stopRequested) {
                try { ForceExitOnStopStatic(ctx, state); }
                catch (Exception ex) { WriteUuWatcherLog(ctx, "[ERROR] STOP_FORCE_EXIT_FAILED detail=" + ex.Message); }
            }
        }
    }
}
