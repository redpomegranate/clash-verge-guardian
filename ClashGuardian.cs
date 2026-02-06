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

public class ClashGuardian : Form
{ 
    // ==================== 配置常量 ====================
    private const int DEFAULT_NORMAL_INTERVAL = 5000;     // 正常检测间隔：5秒
    private const int DEFAULT_FAST_INTERVAL = 1000;       // 异常时快速检测：1秒
    private const int DEFAULT_MEMORY_THRESHOLD = 150;     // 内存阈值 (MB)
    private const int DEFAULT_MEMORY_WARNING = 70;        // 内存警告阈值 (MB)
    private const int DEFAULT_HIGH_DELAY = 400;           // 高延迟阈值 (ms) - 超过此值触发切换
    private const int DEFAULT_BLACKLIST_MINUTES = 20;     // 黑名单时长（分钟）
    private const int DEFAULT_PROXY_PORT = 7897;          // 代理端口
    private const int DEFAULT_API_PORT = 9097;            // API 端口
    private const int TCP_CHECK_INTERVAL = 10;            // TCP 统计检测间隔（~50s）
    private const int NODE_UPDATE_INTERVAL = 30;          // 节点信息更新间隔（~150s）
    private const int DELAY_TEST_INTERVAL = 72;           // 延迟测试间隔（~6min）
    private const int LOG_RETENTION_DAYS = 7;             // 日志保留天数
    private const int COOLDOWN_COUNT = 5;                 // 重启后冷却次数
    
    // 网络超时常量
    private const int API_TIMEOUT_FAST = 1000;            // 快速 API 超时 (ms)
    private const int API_TIMEOUT_NORMAL = 3000;          // 正常 API 超时 (ms)
    private const int PROXY_TEST_TIMEOUT = 2500;          // 代理测试超时 (ms)
    private const int API_DISCOVER_TIMEOUT = 500;         // API 发现超时 (ms)

    // ==================== 多内核/多客户端支持 ====================
    // 默认支持的内核进程名（按优先级排序）
    private static readonly string[] DEFAULT_CORE_NAMES = new string[] {
        "verge-mihomo",     // Clash Verge Rev
        "mihomo",           // Mihomo Party / 独立 mihomo
        "clash-meta",       // Clash Meta
        "clash-rs",         // Clash Nyanpasu (Rust)
        "clash",            // 原版 Clash
        "clash-win64"       // Clash for Windows
    };
    
    // 默认支持的客户端进程名
    private static readonly string[] DEFAULT_CLIENT_NAMES = new string[] {
        "Clash Verge",      // Clash Verge Rev (带空格)
        "clash-verge",      // Clash Verge Rev
        "Clash Nyanpasu",   // Clash Nyanpasu
        "mihomo-party",     // Mihomo Party
        "Clash for Windows" // CFW
    };
    
    // 默认 API 端口列表
    private static readonly int[] DEFAULT_API_PORTS = new int[] { 9097, 9090, 7890, 9898 };

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
    
    // 多内核支持配置
    private string[] coreProcessNames;
    private string[] clientProcessNames;
    private string[] clientPaths;
    
    // 当前检测到的进程信息
    private string detectedCoreName = "";
    private string detectedClientPath = "";

    // ==================== UI 组件 ====================
    private NotifyIcon trayIcon;
    private Label statusLabel, memLabel, proxyLabel, logLabel, checkLabel, stableLabel;
    private Button restartBtn, exitBtn, logBtn;
    private System.Windows.Forms.Timer timer;

    // ==================== 运行时状态 ====================
    private string logFile, dataFile, configFile, baseDir;
    private int failCount = 0, totalChecks = 0, totalFails = 0, totalRestarts = 0, totalSwitches = 0;
    private string currentNode = "";
    private string nodeGroup = "";  // 缓存实际节点所属的 Selector 组名
    private int cooldownCount = 0;
    private DateTime lastStableTime;
    private DateTime startTime;
    private int consecutiveOK = 0;
    private Dictionary<string, DateTime> nodeBlacklist = new Dictionary<string, DateTime>();
    private int lastDelay = 0;
    private int[] lastTcpStats = new int[] { 0, 0, 0 };  // TCP 统计缓存
    private volatile bool isChecking = false;  // 后台检测锁，防止重复执行

    public ClashGuardian()
    { 
        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        logFile = Path.Combine(baseDir, "guardian.log");
        dataFile = Path.Combine(baseDir, "monitor_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        configFile = Path.Combine(baseDir, "config.json");
        startTime = DateTime.Now;
        lastStableTime = DateTime.Now;

        // 只加载配置文件（不做进程探测，推迟到后台）
        LoadConfigFast();
        
        // 后台清理日志
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
        
        // 立即在后台执行首次检测（不阻塞 UI）
        ThreadPool.QueueUserWorkItem(_ => DoFirstCheck());
    }
    
    // 首次检测（后台执行，含进程探测）
    void DoFirstCheck() {
        try {
            // 先探测运行中的内核（之前在 LoadConfig 中同步执行，现在推迟到后台）
            DetectRunningCore();
            if (string.IsNullOrEmpty(detectedCoreName)) {
                AutoDiscoverApi();
            }
            
            // 快速获取基本信息
            double mem = 0;
            int handles = 0;
            bool running = GetMihomoStats(out mem, out handles);
            
            // 快速测试代理
            bool proxyOK = false;
            int delay = TestProxy(out proxyOK, true);
            
            // 获取节点（使用改进的方法）
            GetCurrentNode();
            
            // 更新 UI
            this.BeginInvoke((Action)(() => {
                string delayStr = delay > 0 ? delay + "ms" : "--";
                string coreShort = string.IsNullOrEmpty(detectedCoreName) ? "未检测" : detectedCoreName;
                memLabel.Text = "内  核:  " + coreShort + "  |  " + mem.ToString("F1") + "MB  |  句柄: " + handles;
                
                string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "--" : currentNode;
                string nodeShort = nodeDisplay.Length > 15 ? nodeDisplay.Substring(0, 15) + ".." : nodeDisplay;
                proxyLabel.Text = "代  理:  " + (proxyOK ? "OK" : "X") + " " + delayStr + " | " + nodeShort;
                proxyLabel.ForeColor = proxyOK ? COLOR_OK : COLOR_ERROR;
                
                statusLabel.Text = "● 状态: 运行中";
                statusLabel.ForeColor = COLOR_OK;
                
                checkLabel.Text = "统  计:  检测 1  |  重启 0  |  切换 0  |  黑名单 0";
                stableLabel.Text = "稳定性:  连续 0s  |  运行 0s  |  成功率 100.0%";
                
                // 记录检测到的内核
                if (!string.IsNullOrEmpty(detectedCoreName)) {
                    Log("检测到内核: " + detectedCoreName);
                }
            }));
            
            totalChecks = 1;
        } catch { }
    }

    // ==================== 配置管理 ====================
    // 快速加载配置（不做进程探测，用于构造函数）
    void LoadConfigFast() {
        // 设置默认值
        clashApi = "http://127.0.0.1:" + DEFAULT_API_PORT;
        clashSecret = "set-your-secret";
        proxyPort = DEFAULT_PROXY_PORT;
        normalInterval = DEFAULT_NORMAL_INTERVAL;
        fastInterval = DEFAULT_FAST_INTERVAL;
        memoryThreshold = DEFAULT_MEMORY_THRESHOLD;
        memoryWarning = DEFAULT_MEMORY_WARNING;
        highDelayThreshold = DEFAULT_HIGH_DELAY;
        blacklistMinutes = DEFAULT_BLACKLIST_MINUTES;
        
        // 多内核默认配置
        coreProcessNames = DEFAULT_CORE_NAMES;
        clientProcessNames = DEFAULT_CLIENT_NAMES;
        clientPaths = GetDefaultClientPaths();

        // 尝试读取配置文件
        if (File.Exists(configFile)) {
            try {
                string json = File.ReadAllText(configFile, Encoding.UTF8);
                clashApi = GetJsonValue(json, "clashApi", clashApi);
                clashSecret = GetJsonValue(json, "clashSecret", clashSecret);
                proxyPort = int.Parse(GetJsonValue(json, "proxyPort", proxyPort.ToString()));
                normalInterval = int.Parse(GetJsonValue(json, "normalInterval", normalInterval.ToString()));
                memoryThreshold = int.Parse(GetJsonValue(json, "memoryThreshold", memoryThreshold.ToString()));
                highDelayThreshold = int.Parse(GetJsonValue(json, "highDelayThreshold", highDelayThreshold.ToString()));
                blacklistMinutes = int.Parse(GetJsonValue(json, "blacklistMinutes", blacklistMinutes.ToString()));
                
                // 加载自定义进程名配置
                string customCores = GetJsonArray(json, "coreProcessNames");
                if (!string.IsNullOrEmpty(customCores)) coreProcessNames = customCores.Split(',');
                
                string customClients = GetJsonArray(json, "clientProcessNames");
                if (!string.IsNullOrEmpty(customClients)) clientProcessNames = customClients.Split(',');
            } catch { }
        } else {
            // 后台保存默认配置（不阻塞）
            ThreadPool.QueueUserWorkItem(_ => SaveDefaultConfig());
        }
        // 注意：进程探测推迟到 DoFirstCheck() 中执行
    }
    
    // 获取默认客户端路径列表
    string[] GetDefaultClientPaths() {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new string[] {
            Path.Combine(localAppData, @"Programs\clash-verge\Clash Verge.exe"),
            Path.Combine(localAppData, @"Programs\clash-verge\clash-verge.exe"),
            Path.Combine(localAppData, @"Programs\Clash Nyanpasu\Clash Nyanpasu.exe"),
            Path.Combine(localAppData, @"mihomo-party\mihomo-party.exe"),
            Path.Combine(localAppData, @"Programs\Clash for Windows\Clash for Windows.exe"),
            @"C:\Program Files\Clash Verge\Clash Verge.exe",
            @"C:\Program Files\mihomo-party\mihomo-party.exe"
        };
    }
    
    // 自动探测运行中的内核进程
    void DetectRunningCore() {
        foreach (string coreName in coreProcessNames) {
            try {
                Process[] procs = Process.GetProcessesByName(coreName);
                if (procs.Length > 0) {
                    detectedCoreName = coreName;
                    foreach (var p in procs) p.Dispose();
                    // 同时找到对应的客户端
                    DetectRunningClient();
                    return;
                }
            } catch { }
        }
    }
    
    // 探测运行中的客户端
    void DetectRunningClient() {
        foreach (string clientName in clientProcessNames) {
            try {
                Process[] procs = Process.GetProcessesByName(clientName);
                if (procs.Length > 0) {
                    try {
                        detectedClientPath = procs[0].MainModule.FileName;
                    } catch { }
                    foreach (var p in procs) p.Dispose();
                    return;
                }
            } catch { }
        }
        // 如果没找到运行中的客户端，从默认路径中查找存在的
        foreach (string path in clientPaths) {
            if (File.Exists(path)) {
                detectedClientPath = path;
                return;
            }
        }
    }
    
    // 自动发现 API 端口（后台线程执行）
    void AutoDiscoverApi() {
        Stopwatch sw = Stopwatch.StartNew();
        foreach (int port in DEFAULT_API_PORTS) {
            try {
                string testApi = "http://127.0.0.1:" + port;
                HttpWebRequest req = WebRequest.Create(testApi + "/version") as HttpWebRequest;
                req.Headers.Add("Authorization", "Bearer " + clashSecret);
                req.Timeout = API_DISCOVER_TIMEOUT;
                using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse) {
                    if (resp.StatusCode == HttpStatusCode.OK) {
                        clashApi = testApi;
                        LogPerf("AutoDiscoverApi(found:" + port + ")", sw.ElapsedMilliseconds);
                        return;
                    }
                }
            } catch { }
        }
        LogPerf("AutoDiscoverApi(notfound)", sw.ElapsedMilliseconds);
    }
    
    // 解析 JSON 数组（简易实现）
    string GetJsonArray(string json, string key) {
        string search = "\"" + key + "\":";
        int idx = json.IndexOf(search);
        if (idx < 0) return "";
        idx = json.IndexOf('[', idx);
        if (idx < 0) return "";
        int end = json.IndexOf(']', idx);
        if (end < 0) return "";
        string arr = json.Substring(idx + 1, end - idx - 1);
        // 移除引号和空格
        return arr.Replace("\"", "").Replace(" ", "").Replace("\n", "").Replace("\r", "");
    }

    void SaveDefaultConfig() {
        string coreNames = string.Join("\", \"", DEFAULT_CORE_NAMES);
        string clientNames = string.Join("\", \"", DEFAULT_CLIENT_NAMES);
        
        string config = "{\n" +
            "  \"clashApi\": \"" + clashApi + "\",\n" +
            "  \"clashSecret\": \"" + clashSecret + "\",\n" +
            "  \"proxyPort\": " + proxyPort + ",\n" +
            "  \"normalInterval\": " + normalInterval + ",\n" +
            "  \"memoryThreshold\": " + memoryThreshold + ",\n" +
            "  \"highDelayThreshold\": " + highDelayThreshold + ",\n" +
            "  \"blacklistMinutes\": " + blacklistMinutes + ",\n" +
            "  \"coreProcessNames\": [\"" + coreNames + "\"],\n" +
            "  \"clientProcessNames\": [\"" + clientNames + "\"]\n" +
            "}";
        try { File.WriteAllText(configFile, config, Encoding.UTF8); } catch { }
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

    // ==================== UI 初始化 ====================
    void InitializeUI() {
        this.Text = "Clash Guardian Pro";
        this.Size = new Size(400, 340);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = SystemIcons.Shield;
        this.Font = new Font("Microsoft YaHei UI", 9);
        this.BackColor = COLOR_FORM_BG;

        int padding = 16;
        int labelHeight = 22;
        int y = padding;

        // 状态标题
        statusLabel = new Label();
        statusLabel.Text = "● 状态: 加速启动中，请稍等...";
        statusLabel.Location = new Point(padding, y);
        statusLabel.Size = new Size(360, 28);
        statusLabel.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        statusLabel.ForeColor = COLOR_WARNING;
        y += 36;

        // 分隔线
        Label line1 = CreateSeparator(padding, y);
        y += 12;

        // 监控信息区
        memLabel = CreateInfoLabel("内  存:  --", padding, y, COLOR_TEXT);
        y += labelHeight + 4;

        proxyLabel = CreateInfoLabel("代  理:  --", padding, y, COLOR_TEXT);
        y += labelHeight + 4;

        checkLabel = CreateInfoLabel("统  计:  --", padding, y, COLOR_GRAY);
        y += labelHeight + 4;

        stableLabel = CreateInfoLabel("稳定性:  --", padding, y, COLOR_CYAN);
        y += labelHeight + 8;

        // 分隔线
        Label line2 = CreateSeparator(padding, y);
        y += 10;

        // 日志区
        logLabel = new Label();
        logLabel.Text = "最近事件:  无";
        logLabel.Location = new Point(padding, y);
        logLabel.Size = new Size(360, 36);
        logLabel.ForeColor = Color.FromArgb(80, 80, 80);
        y += 44;

        // 按钮区 - 第一行
        int btnWidth = 110;
        int btnHeight = 32;
        int btnSpacing = 10;

        restartBtn = CreateButton("立即重启", padding, y, btnWidth, btnHeight, () => RestartClash("手动"));
        logBtn = CreateButton("查看日志", padding + btnWidth + btnSpacing, y, btnWidth, btnHeight, () => Process.Start("notepad", dataFile));
        exitBtn = CreateButton("退出", padding + (btnWidth + btnSpacing) * 2, y, btnWidth, btnHeight, () => { trayIcon.Visible = false; Application.Exit(); });
        y += btnHeight + 8;

        // 按钮区 - 第二行
        Button testBtn = CreateButton("测速", padding, y, btnWidth, btnHeight, () => { 
            ThreadPool.QueueUserWorkItem(_ => {
                // 先触发 Clash 后台全量测速
                TriggerDelayTest();
                // 然后测当前代理延迟并更新 UI
                bool ok;
                int d = TestProxy(out ok, true);
                GetCurrentNode();
                this.BeginInvoke((Action)(() => {
                    string ds = d > 0 ? d + "ms" : "--";
                    string nd = string.IsNullOrEmpty(currentNode) ? "--" : SafeNodeName(currentNode);
                    string ns = nd.Length > 15 ? nd.Substring(0, 15) + ".." : nd;
                    proxyLabel.Text = "代  理:  " + (ok ? "OK" : "X") + " " + ds + " | " + ns;
                    proxyLabel.ForeColor = ok ? COLOR_OK : COLOR_ERROR;
                    Log("测速: " + ds);
                }));
            });
        });
        Button switchBtn = CreateButton("切换节点", padding + btnWidth + btnSpacing, y, btnWidth, btnHeight, () => { 
            ThreadPool.QueueUserWorkItem(_ => {
                if (SwitchToBestNode()) {
                    this.BeginInvoke((Action)(() => {
                        RefreshNodeDisplay();
                        Log("手动切换成功");
                    }));
                } else {
                    this.BeginInvoke((Action)(() => Log("切换失败")));
                }
            });
        });
        Button autoStartBtn = CreateButton("开机自启", padding + (btnWidth + btnSpacing) * 2, y, btnWidth, btnHeight, ToggleAutoStart);

        // 添加控件
        this.Controls.Add(statusLabel);
        this.Controls.Add(line1);
        this.Controls.Add(memLabel);
        this.Controls.Add(proxyLabel);
        this.Controls.Add(checkLabel);
        this.Controls.Add(stableLabel);
        this.Controls.Add(line2);
        this.Controls.Add(logLabel);
        this.Controls.Add(restartBtn);
        this.Controls.Add(logBtn);
        this.Controls.Add(exitBtn);
        this.Controls.Add(testBtn);
        this.Controls.Add(switchBtn);
        this.Controls.Add(autoStartBtn);

        this.Resize += delegate { if (this.WindowState == FormWindowState.Minimized) this.Hide(); };
    }

    // 按钮工厂方法
    Button CreateButton(string text, int x, int y, int width, int height, Action onClick) {
        Button btn = new Button();
        btn.Text = text;
        btn.Location = new Point(x, y);
        btn.Size = new Size(width, height);
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = COLOR_BTN_BG;
        btn.ForeColor = COLOR_BTN_FG;
        btn.FlatAppearance.BorderSize = 0;
        btn.Cursor = Cursors.Hand;
        btn.Click += delegate { onClick(); };
        return btn;
    }

    Label CreateInfoLabel(string text, int x, int y, Color color) {
        Label lbl = new Label();
        lbl.Text = text;
        lbl.Location = new Point(x, y);
        lbl.Size = new Size(360, 22);
        lbl.ForeColor = color;
        return lbl;
    }

    Label CreateSeparator(int x, int y) {
        Label line = new Label();
        line.BorderStyle = BorderStyle.Fixed3D;
        line.Location = new Point(x, y);
        line.Size = new Size(360, 2);
        return line;
    }

    void InitializeTrayIcon() {
        trayIcon = new NotifyIcon();
        trayIcon.Icon = SystemIcons.Shield;
        trayIcon.Text = "Clash 守护";
        trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); };

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, delegate { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); });
        menu.Items.Add("立即重启", null, delegate { RestartClash("手动"); });
        menu.Items.Add("切换节点", null, delegate { SwitchToBestNode(); });
        menu.Items.Add("触发测速", null, delegate { TriggerDelayTest(); });
        menu.Items.Add("查看日志", null, delegate { Process.Start("notepad", dataFile); });
        menu.Items.Add("-");
        menu.Items.Add("退出", null, delegate { trayIcon.Visible = false; Application.Exit(); });
        trayIcon.ContextMenuStrip = menu;
    }

    // ==================== 开机自启管理 ====================
    void ToggleAutoStart() {
        try {
            string appPath = Application.ExecutablePath;
            string keyName = "ClashGuardian";
            RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            
            if (rk.GetValue(keyName) != null) {
                // 已启用，移除
                rk.DeleteValue(keyName, false);
                Log("已关闭开机自启");
            } else {
                // 未启用，添加
                rk.SetValue(keyName, "\"" + appPath + "\"");
                Log("已启用开机自启");
            }
            rk.Close();
        } catch {
            Log("自启设置失败");
        }
    }

    // ==================== 日志管理 ====================
    void CleanOldLogs() {
        try {
            DateTime cutoff = DateTime.Now.AddDays(-LOG_RETENTION_DAYS);
            foreach (string file in Directory.GetFiles(baseDir, "monitor_*.csv")) {
                FileInfo fi = new FileInfo(file);
                if (fi.LastWriteTime < cutoff) fi.Delete();
            }
            FileInfo logFi = new FileInfo(logFile);
            if (logFi.Exists && logFi.Length > 1024 * 1024) logFi.Delete();
        } catch { }
    }

    void Log(string msg) {
        string line = "[" + DateTime.Now.ToString("MM-dd HH:mm:ss") + "] " + msg;
        try { File.AppendAllText(logFile, line + "\n"); } catch { }
        if (logLabel != null) logLabel.Text = "最近事件:  " + msg;
    }
    
    // 性能日志：只记录异常耗时的操作（显著超时或问题场景）
    void LogPerf(string operation, long elapsedMs) {
        // 只记录显著异常的情况：
        // - TestProxy 超过 5000ms（严重超时）
        // - 其他操作超过 2000ms
        // - 包含 Error/Warn/异常 关键字的总是记录
        bool shouldLog = false;
        if (operation.Contains("Error") || operation.Contains("Warn") || operation.Contains("异常")) {
            shouldLog = true;
        } else if (operation.StartsWith("TestProxy")) {
            shouldLog = elapsedMs > 5000;  // 只记录严重超时
        } else {
            shouldLog = elapsedMs > 2000;  // 其他操作超过 2 秒才记录
        }
        
        if (shouldLog) {
            string line = "[" + DateTime.Now.ToString("MM-dd HH:mm:ss") + "] [PERF] " + operation + ": " + elapsedMs + "ms";
            try { File.AppendAllText(logFile, line + "\n"); } catch { }
        }
    }

    void LogData(bool proxyOK, int delay, double mem, int handles, int tw, int est, int cw, string node, string evt) {
        // 优化：空事件不写入
        if (string.IsNullOrEmpty(evt)) return;
        string line = string.Format("{0},{1},{2},{3:F1},{4},{5},{6},{7},{8},{9}",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), proxyOK ? "OK" : "FAIL", delay, mem, handles, tw, est, cw, node, evt);
        try { File.AppendAllText(dataFile, line + "\n"); } catch { }
    }

    // ==================== API 通信（统一使用 HttpWebRequest） ====================
    string ApiRequest(string path, int timeout = API_TIMEOUT_NORMAL) {
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + path) as HttpWebRequest;
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse)
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                return reader.ReadToEnd();
            }
        } catch { return null; }
    }

    bool ApiPut(string path, string body) {
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + path) as HttpWebRequest;
            req.Method = "PUT";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = API_TIMEOUT_NORMAL;
            byte[] data = Encoding.UTF8.GetBytes(body);
            req.ContentLength = data.Length;
            using (Stream stream = req.GetRequestStream()) {
                stream.Write(data, 0, data.Length);
            }
            using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse) {
                return resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NoContent;
            }
        } catch (WebException wex) {
            if (wex.Response != null) {
                using (HttpWebResponse errResp = wex.Response as HttpWebResponse) {
                    using (StreamReader reader = new StreamReader(errResp.GetResponseStream())) {
                        string errBody = reader.ReadToEnd();
                        Log("API错误: " + (int)errResp.StatusCode + " " + errBody);
                    }
                }
            } else {
                Log("API异常: " + wex.Message);
            }
            return false;
        } catch (Exception ex) {
            Log("API异常: " + ex.Message);
            return false;
        }
    }

    // ==================== 工具函数 ====================
    string FormatTimeSpan(TimeSpan ts) {
        if (ts.TotalHours >= 1) return string.Format("{0:F1}h", ts.TotalHours);
        if (ts.TotalMinutes >= 1) return string.Format("{0:F0}m", ts.TotalMinutes);
        return string.Format("{0:F0}s", ts.TotalSeconds);
    }
    
    // 刷新节点和统计显示（UI 线程调用）
    void RefreshNodeDisplay() {
        string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "获取中..." : currentNode;
        string nodeShort = nodeDisplay.Length > 15 ? nodeDisplay.Substring(0, 15) + ".." : nodeDisplay;
        string delayStr = lastDelay > 0 ? lastDelay + "ms" : "--";
        proxyLabel.Text = "代  理:  OK " + delayStr + " | " + nodeShort;
        proxyLabel.ForeColor = COLOR_OK;
        checkLabel.Text = "统  计:  检测 " + totalChecks + "  |  重启 " + totalRestarts + "  |  切换 " + totalSwitches + "  |  黑名单 " + nodeBlacklist.Count;
    }

    // ==================== 节点管理 ====================
    // 尝试获取当前节点的多个 selector 名称（按优先级排序）
    private static readonly string[] SELECTOR_NAMES = new string[] {
        "GLOBAL", "节点选择", "Proxy", "代理模式", "手动切换", "Select", "🚀 节点选择"
    };
    
    // 跳过的代理组名称（这些是策略组，不是实际节点）
    private static readonly string[] SKIP_GROUPS = new string[] {
        "DIRECT", "REJECT", "GLOBAL", "Proxy", "节点选择", "代理模式", 
        "手动切换", "Select", "自动选择", "故障转移", "负载均衡",
        "🚀 节点选择", "♻️ 自动选择", "🎯 全球直连", "🛑 全球拦截"
    };
    
    void GetCurrentNode() {
        try {
            // 一次性获取所有代理信息
            string json = ApiRequest("/proxies", API_TIMEOUT_NORMAL);
            if (string.IsNullOrEmpty(json)) return;
            
            // 从 GLOBAL 开始递归查找实际节点
            string node = ResolveActualNode(json, "GLOBAL", 0);
            if (!string.IsNullOrEmpty(node)) {
                currentNode = SafeNodeName(node);
                return;
            }
            
            // 备用：尝试其他常用 selector
            foreach (string selector in SELECTOR_NAMES) {
                if (selector == "GLOBAL") continue; // 已经尝试过
                node = ResolveActualNode(json, selector, 0);
                if (!string.IsNullOrEmpty(node)) {
                    currentNode = SafeNodeName(node);
                    return;
                }
            }
        } catch { }
    }
    
    // 递归解析，找到实际的节点（而非代理组）
    string ResolveActualNode(string json, string proxyName, int depth) {
        // 防止无限递归
        if (depth > 5) return proxyName;
        
        // 获取该代理的信息
        string nowValue = FindProxyNow(json, proxyName);
        if (string.IsNullOrEmpty(nowValue)) return "";
        
        // 检查是否是需要跳过的代理组
        bool isGroup = false;
        foreach (string skip in SKIP_GROUPS) {
            if (nowValue == skip || nowValue.Contains(skip)) {
                isGroup = true;
                break;
            }
        }
        
        // 检查该 now 值对应的代理类型
        string proxyType = FindProxyType(json, nowValue);
        
        // 如果是 Selector/URLTest/Fallback/LoadBalance，继续递归
        if (proxyType == "Selector" || proxyType == "URLTest" || 
            proxyType == "Fallback" || proxyType == "LoadBalance") {
            return ResolveActualNode(json, nowValue, depth + 1);
        }
        
        // 如果不是代理组类型，可能是实际节点
        if (!isGroup && !string.IsNullOrEmpty(proxyType)) {
            return nowValue;
        }
        
        // 即使没有类型信息，也返回找到的值（可能是实际节点）
        if (!isGroup) {
            return nowValue;
        }
        
        // 继续递归尝试
        return ResolveActualNode(json, nowValue, depth + 1);
    }
    
    // 在 JSON 中查找指定代理的 now 字段
    string FindProxyNow(string json, string proxyName) {
        // 查找 "proxyName": { ... "now": "xxx" ... }
        string search = "\"" + proxyName + "\":{";
        int idx = json.IndexOf(search);
        if (idx < 0) {
            // 尝试带空格的格式
            search = "\"" + proxyName + "\": {";
            idx = json.IndexOf(search);
        }
        if (idx < 0) return "";
        
        // 找到这个对象内的 now 字段
        int objStart = idx + search.Length - 1;
        
        // 找到对象结束位置（匹配括号）
        int braceCount = 1;
        int objEnd = objStart + 1;
        while (objEnd < json.Length && braceCount > 0) {
            if (json[objEnd] == '{') braceCount++;
            else if (json[objEnd] == '}') braceCount--;
            objEnd++;
        }
        
        // 在对象范围内查找 now 字段
        int nowIdx = json.IndexOf("\"now\":\"", objStart);
        if (nowIdx > 0 && nowIdx < objEnd) {
            return ExtractJsonStringAt(json, nowIdx + 7);
        }
        
        // 尝试无空格格式
        nowIdx = json.IndexOf("\"now\": \"", objStart);
        if (nowIdx > 0 && nowIdx < objEnd) {
            return ExtractJsonStringAt(json, nowIdx + 8);
        }
        
        return "";
    }
    
    // 在 JSON 中查找指定代理的 type 字段
    string FindProxyType(string json, string proxyName) {
        string search = "\"" + proxyName + "\":{";
        int idx = json.IndexOf(search);
        if (idx < 0) {
            search = "\"" + proxyName + "\": {";
            idx = json.IndexOf(search);
        }
        if (idx < 0) return "";
        
        int objStart = idx + search.Length - 1;
        
        // 找对象范围
        int braceCount = 1;
        int objEnd = objStart + 1;
        while (objEnd < json.Length && braceCount > 0) {
            if (json[objEnd] == '{') braceCount++;
            else if (json[objEnd] == '}') braceCount--;
            objEnd++;
        }
        
        // 查找 type 字段
        int typeIdx = json.IndexOf("\"type\":\"", objStart);
        if (typeIdx > 0 && typeIdx < objEnd) {
            return ExtractJsonStringAt(json, typeIdx + 8);
        }
        
        typeIdx = json.IndexOf("\"type\": \"", objStart);
        if (typeIdx > 0 && typeIdx < objEnd) {
            return ExtractJsonStringAt(json, typeIdx + 9);
        }
        
        return "";
    }
    
    // 从 JSON 中提取字符串值（处理 Unicode 转义）
    string ExtractJsonString(string json, string key) {
        string search = "\"" + key + "\":\"";
        int start = json.IndexOf(search);
        if (start < 0) return "";
        start += search.Length;
        return ExtractJsonStringAt(json, start);
    }
    
    string ExtractJsonStringAt(string json, int start) {
        StringBuilder sb = new StringBuilder();
        int i = start;
        while (i < json.Length) {
            char c = json[i];
            if (c == '"') break;
            if (c == '\\' && i + 1 < json.Length) {
                char next = json[i + 1];
                if (next == 'u' && i + 5 < json.Length) {
                    // Unicode 转义: \uXXXX
                    string hex = json.Substring(i + 2, 4);
                    int code;
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code)) {
                        sb.Append((char)code);
                        i += 6;
                        continue;
                    }
                } else if (next == 'n') { sb.Append('\n'); i += 2; continue; }
                else if (next == 'r') { sb.Append('\r'); i += 2; continue; }
                else if (next == 't') { sb.Append('\t'); i += 2; continue; }
                else if (next == '"') { sb.Append('"'); i += 2; continue; }
                else if (next == '\\') { sb.Append('\\'); i += 2; continue; }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
    
    // 安全的节点名称（移除不可显示字符，跳过 emoji surrogate pair）
    string SafeNodeName(string name) {
        if (string.IsNullOrEmpty(name)) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++) {
            char c = name[i];
            // 跳过 surrogate pair（emoji 国旗等，WinForms 无法渲染）
            if (char.IsHighSurrogate(c)) {
                if (i + 1 < name.Length && char.IsLowSurrogate(name[i + 1])) i++;
                continue;
            }
            if (char.IsLowSurrogate(c)) continue;
            // ASCII 可打印字符 + 中文 + 日文假名 + 韩文 + 常用符号
            if ((c >= 0x20 && c <= 0x7E) ||      // ASCII
                (c >= 0x4E00 && c <= 0x9FFF) ||  // CJK 统一汉字
                (c >= 0x3040 && c <= 0x30FF) ||  // 日文假名
                (c >= 0xAC00 && c <= 0xD7AF) ||  // 韩文
                (c >= 0x2000 && c <= 0x206F) ||  // 通用标点
                (c >= 0xFF00 && c <= 0xFFEF)) {  // 全角字符
                sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    void TriggerDelayTest() {
        string group = string.IsNullOrEmpty(nodeGroup) ? "GLOBAL" : nodeGroup;
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + "/group/" + Uri.EscapeDataString(group) + "/delay?url=http://www.gstatic.com/generate_204&timeout=5000") as HttpWebRequest;
            req.Method = "GET";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.Timeout = 2000;
            // 异步发送，不等待全部节点测完（Clash 收到请求后会自行后台测速）
            req.BeginGetResponse(ar => { try { req.EndGetResponse(ar).Close(); } catch { } }, null);
        } catch { }
    }

    void CleanBlacklist() {
        List<string> toRemove = new List<string>();
        DateTime now = DateTime.Now;
        foreach (var kv in nodeBlacklist) {
            if ((now - kv.Value).TotalMinutes > blacklistMinutes) toRemove.Add(kv.Key);
        }
        foreach (string key in toRemove) nodeBlacklist.Remove(key);
    }

    // 从 Selector 组的 all 数组中提取节点名列表
    List<string> GetGroupAllNodes(string json, string groupName) {
        List<string> nodes = new List<string>();
        string search = "\"" + groupName + "\":{";
        int idx = json.IndexOf(search);
        if (idx < 0) { search = "\"" + groupName + "\": {"; idx = json.IndexOf(search); }
        if (idx < 0) return nodes;
        
        // 找 all 数组
        int objStart = idx + search.Length - 1;
        int allIdx = json.IndexOf("\"all\":[", objStart);
        if (allIdx < 0) return nodes;
        int arrStart = allIdx + 6; // 跳过 "all":[
        int arrEnd = json.IndexOf("]", arrStart);
        if (arrEnd < 0) return nodes;
        
        // 解析数组中的字符串
        string arrStr = json.Substring(arrStart, arrEnd - arrStart);
        int pos = 0;
        while (pos < arrStr.Length) {
            int qStart = arrStr.IndexOf('"', pos);
            if (qStart < 0) break;
            // 用 ExtractJsonStringAt 处理 Unicode 转义
            string name = ExtractJsonStringAt(arrStr, qStart + 1);
            if (!string.IsNullOrEmpty(name)) nodes.Add(name);
            // 跳过这个字符串，找到闭合引号
            int qEnd = qStart + 1;
            while (qEnd < arrStr.Length) {
                if (arrStr[qEnd] == '"' && arrStr[qEnd - 1] != '\\') break;
                qEnd++;
            }
            pos = qEnd + 1;
        }
        return nodes;
    }
    
    // 获取节点的最新延迟
    int GetNodeDelay(string json, string nodeName) {
        string search = "\"" + nodeName + "\":{";
        int idx = json.IndexOf(search);
        if (idx < 0) { search = "\"" + nodeName + "\": {"; idx = json.IndexOf(search); }
        if (idx < 0) return 0;
        
        int objStart = idx + search.Length - 1;
        int braceCount = 1;
        int objEnd = objStart + 1;
        while (objEnd < json.Length && braceCount > 0) {
            if (json[objEnd] == '{') braceCount++;
            else if (json[objEnd] == '}') braceCount--;
            objEnd++;
        }
        
        // 找顶层 history（跳过 extra 里嵌套的）— 用最后一个 "history":[ 
        string objStr = json.Substring(objStart, objEnd - objStart);
        int historyIdx = objStr.LastIndexOf("\"history\":[");
        if (historyIdx < 0) return 0;
        int historyEnd = objStr.IndexOf("]", historyIdx);
        if (historyEnd <= historyIdx) return 0;
        string historyStr = objStr.Substring(historyIdx, historyEnd - historyIdx);
        int lastDelayIdx = historyStr.LastIndexOf("\"delay\":");
        if (lastDelayIdx < 0) return 0;
        int delayStart = lastDelayIdx + 8;
        int delayEnd = historyStr.IndexOfAny(new char[] { ',', '}' }, delayStart);
        if (delayEnd <= delayStart) return 0;
        int delay;
        if (int.TryParse(historyStr.Substring(delayStart, delayEnd - delayStart).Trim(), out delay) && delay > 0)
            return delay;
        return 0;
    }
    
    // 查找包含实际节点的 Selector 组名
    string FindSelectorGroup(string json) {
        // 策略：从 GLOBAL 的 all 列表找到第一个 Selector 子组
        List<string> globalAll = GetGroupAllNodes(json, "GLOBAL");
        foreach (string entry in globalAll) {
            string t = FindProxyType(json, entry);
            if (t == "Selector" || t == "URLTest" || t == "Fallback") {
                return entry;  // 比如 BoostNet
            }
        }
        return "GLOBAL";
    }

    bool SwitchToBestNode() {
        CleanBlacklist();
        try {
            string json = ApiRequest("/proxies");
            if (string.IsNullOrEmpty(json)) {
                Log("切换失败: API无响应");
                return false;
            }

            // 找到包含实际节点的 Selector 组
            string group = FindSelectorGroup(json);
            nodeGroup = group;
            
            // 从该组的 all 数组获取节点列表
            List<string> allNodes = GetGroupAllNodes(json, group);
            
            // 收集可用节点及延迟
            List<KeyValuePair<string, int>> nodesWithDelay = new List<KeyValuePair<string, int>>();
            string[] skipTypes = new string[] { "Selector", "URLTest", "Fallback", "LoadBalance", "Direct", "Reject" };
            
            foreach (string nodeName in allNodes) {
                if (string.IsNullOrEmpty(nodeName) || nodeName.Length > 50) continue;
                
                // 跳过策略组
                bool skip = false;
                foreach (string sg in SKIP_GROUPS) { if (nodeName == sg) { skip = true; break; } }
                if (skip) continue;
                
                // 跳过策略组类型
                string nodeType = FindProxyType(json, nodeName);
                foreach (string st in skipTypes) { if (nodeType == st) { skip = true; break; } }
                if (skip) continue;
                
                // 排除条件
                if (nodeName.Contains("HK") || nodeName.Contains("香港") || 
                    nodeName.Contains("TW") || nodeName.Contains("台湾") ||
                    nodeName.Contains("MO") || nodeName.Contains("澳门")) continue;
                if (nodeBlacklist.ContainsKey(nodeName)) continue;
                
                int delay = GetNodeDelay(json, nodeName);
                if (delay > 0) {
                    nodesWithDelay.Add(new KeyValuePair<string, int>(nodeName, delay));
                }
            }
            
            if (nodesWithDelay.Count == 0) {
                Log("切换失败: 无可用节点(请先测速) group=" + group + " allCount=" + allNodes.Count);
                return false;
            }
            
            // 按延迟排序
            nodesWithDelay.Sort((a, b) => a.Value.CompareTo(b.Value));
            
            // 选择延迟最低且不是当前节点的
            string bestNode = null;
            int bestDelay = 9999;
            foreach (var kv in nodesWithDelay) {
                if (kv.Key != currentNode) {
                    bestNode = kv.Key;
                    bestDelay = kv.Value;
                    break;
                }
            }

            if (bestNode != null && bestDelay < 2000) {
                if (!string.IsNullOrEmpty(currentNode)) nodeBlacklist[currentNode] = DateTime.Now;
                
                string url = "/proxies/" + Uri.EscapeDataString(group);
                if (ApiPut(url, "{\"name\":\"" + bestNode + "\"}")) {
                    Log("切换: " + SafeNodeName(bestNode) + " (" + bestDelay + "ms) @" + group);
                    currentNode = bestNode;
                    lastDelay = bestDelay;
                    totalSwitches++;
                    return true;
                } else {
                    Log("切换失败: PUT " + group + " node=" + SafeNodeName(bestNode));
                }
            } else if (bestNode == null) {
                Log("切换失败: 无更优节点");
            }
        } catch (Exception ex) {
            Log("切换异常: " + ex.Message);
        }
        return false;
    }

    // ==================== 代理测试（统一方法） ====================
    // fast=true: 单URL快速测试; fast=false: 双URL完整测试
    int TestProxy(out bool success, bool fast = false) {
        string[] testUrls = fast 
            ? new string[] { "http://www.gstatic.com/generate_204" }
            : new string[] { "http://www.gstatic.com/generate_204", "http://cp.cloudflare.com/generate_204" };
        
        int successCount = 0;
        int minDelay = 9999;
        int timeout = fast ? PROXY_TEST_TIMEOUT : API_TIMEOUT_NORMAL;

        foreach (string url in testUrls) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                HttpWebRequest req = WebRequest.Create(url) as HttpWebRequest;
                req.Proxy = new WebProxy("127.0.0.1", proxyPort);
                req.Timeout = timeout;
                using (WebResponse resp = req.GetResponse()) {
                    sw.Stop();
                    int delay = (int)sw.ElapsedMilliseconds;
                    successCount++;
                    if (delay < minDelay) minDelay = delay;
                    if (fast) break; // 快速模式只测一个
                }
            } catch { }
        }

        success = successCount > 0;
        lastDelay = success ? minDelay : 0;
        return success ? minDelay : 0;
    }

    // ==================== 系统监控（使用 IPGlobalProperties 替代 netstat） ====================
    int[] GetTcpStats() {
        int tw = 0, est = 0, cw = 0;
        try {
            System.Net.NetworkInformation.IPGlobalProperties properties = 
                System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            System.Net.NetworkInformation.TcpConnectionInformation[] connections = 
                properties.GetActiveTcpConnections();
            
            foreach (var conn in connections) {
                // 检查是否与代理端口相关
                if (conn.LocalEndPoint.Port == proxyPort || conn.RemoteEndPoint.Port == proxyPort) {
                    switch (conn.State) {
                        case System.Net.NetworkInformation.TcpState.TimeWait:
                            tw++;
                            break;
                        case System.Net.NetworkInformation.TcpState.Established:
                            est++;
                            break;
                        case System.Net.NetworkInformation.TcpState.CloseWait:
                            cw++;
                            break;
                    }
                }
            }
        } catch { }
        return new int[] { tw, est, cw };
    }

    // 多内核支持：遍历内核进程名列表检测
    bool GetMihomoStats(out double mem, out int handles) {
        mem = 0;
        handles = 0;
        
        // 优先使用已检测到的内核名
        if (!string.IsNullOrEmpty(detectedCoreName)) {
            try {
                Process[] procs = Process.GetProcessesByName(detectedCoreName);
                if (procs.Length > 0) {
                    mem = procs[0].WorkingSet64 / 1024.0 / 1024.0;
                    handles = procs[0].HandleCount;
                    foreach (var p in procs) p.Dispose();
                    return true;
                }
            } catch { }
        }
        
        // 未找到，重新扫描所有支持的内核
        foreach (string coreName in coreProcessNames) {
            try {
                Process[] procs = Process.GetProcessesByName(coreName);
                if (procs.Length > 0) {
                    mem = procs[0].WorkingSet64 / 1024.0 / 1024.0;
                    handles = procs[0].HandleCount;
                    foreach (var p in procs) p.Dispose();
                    // 更新检测到的内核名
                    if (detectedCoreName != coreName) {
                        detectedCoreName = coreName;
                        Log("检测到内核: " + coreName);
                    }
                    return true;
                }
            } catch { }
        }
        
        // 都没找到，清空检测结果
        if (!string.IsNullOrEmpty(detectedCoreName)) {
            detectedCoreName = "";
        }
        return false;
    }

    // ==================== 重启管理 ====================
    void RestartClash(string reason) {
        // 注意：此方法可能在后台线程执行，UI 操作需要切换到 UI 线程
        Log("重启: " + reason);
        totalRestarts++;
        consecutiveOK = 0;

        // 终止所有已知的客户端和内核进程
        try {
            // 终止客户端
            foreach (string clientName in clientProcessNames) {
                foreach (Process p in Process.GetProcessesByName(clientName)) {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                    finally { p.Dispose(); }
                }
            }
            // 终止内核
            foreach (string coreName in coreProcessNames) {
                foreach (Process p in Process.GetProcessesByName(coreName)) {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                    finally { p.Dispose(); }
                }
            }
        } catch { }

        Thread.Sleep(2000);
        
        // 启动客户端：优先使用检测到的路径
        bool started = false;
        if (!string.IsNullOrEmpty(detectedClientPath) && File.Exists(detectedClientPath)) {
            try {
                Process.Start(detectedClientPath);
                Log("已恢复: " + Path.GetFileName(detectedClientPath));
                started = true;
            } catch { }
        }
        
        // 如果检测路径失败，尝试默认路径列表
        if (!started) {
            foreach (string path in clientPaths) {
                if (File.Exists(path)) {
                    try {
                        Process.Start(path);
                        detectedClientPath = path;  // 记住成功的路径
                        Log("已恢复: " + Path.GetFileName(path));
                        started = true;
                        break;
                    } catch { }
                }
            }
        }
        
        if (!started) {
            Log("警告: 未找到客户端程序");
        }

        failCount = 0;
        cooldownCount = COOLDOWN_COUNT;
        
        // UI 操作必须在 UI 线程执行
        if (this.InvokeRequired) {
            this.BeginInvoke((Action)(() => {
                statusLabel.Text = "● 状态: 重启中...";
                statusLabel.ForeColor = COLOR_WARNING;
                timer.Interval = normalInterval;
            }));
        } else {
            statusLabel.Text = "● 状态: 重启中...";
            statusLabel.ForeColor = COLOR_WARNING;
            timer.Interval = normalInterval;
        }
    }

    void AdjustInterval(bool hasIssue) {
        if (hasIssue && timer.Interval != fastInterval) {
            timer.Interval = fastInterval;
        } else if (!hasIssue && consecutiveOK >= 3 && timer.Interval != normalInterval) {
            timer.Interval = normalInterval;
        }
    }

    // ==================== 主检测循环（后台线程模式） ====================
    
    // Timer.Tick 触发：启动后台检测任务
    void CheckStatus(object s, EventArgs e) {
        // 防止重复执行
        if (isChecking) return;
        isChecking = true;
        
        // 冷却期处理也移到后台线程（避免 UI 阻塞）
        if (cooldownCount > 0) {
            ThreadPool.QueueUserWorkItem(_ => DoCooldownCheck());
        } else {
            // 正常检测
            ThreadPool.QueueUserWorkItem(_ => DoCheckInBackground());
        }
    }
    
    // 冷却期后台检测
    void DoCooldownCheck() {
        try {
            // 检测内核进程是否已启动
            bool coreRunning = false;
            string foundCore = "";
            foreach (string coreName in coreProcessNames) {
                Process[] procs = Process.GetProcessesByName(coreName);
                if (procs.Length > 0) {
                    coreRunning = true;
                    foundCore = coreName;
                    foreach (var p in procs) p.Dispose(); // 释放进程对象
                    break;
                }
            }
            
            // 内核启动后，快速测试代理是否可用
            bool proxyReady = false;
            if (coreRunning) {
                bool tempOK;
                TestProxy(out tempOK, true);
                proxyReady = tempOK;
            }
            
            // 切回 UI 线程更新状态
            this.BeginInvoke((Action)(() => {
                if (!string.IsNullOrEmpty(foundCore)) detectedCoreName = foundCore;
                
                if (coreRunning && proxyReady) {
                    cooldownCount = 0;
                    statusLabel.Text = "● 状态: 运行中";
                    statusLabel.ForeColor = COLOR_OK;
                    lastStableTime = DateTime.Now;
                    Log("已恢复正常");
                } else {
                    cooldownCount--;
                    statusLabel.Text = "● 状态: 等待恢复... (" + (coreRunning ? "内核已启动" : "等待内核") + ")";
                    if (cooldownCount == 0) {
                        statusLabel.Text = "● 状态: 运行中";
                        statusLabel.ForeColor = COLOR_OK;
                        lastStableTime = DateTime.Now;
                    }
                }
            }));
        } catch { }
        finally {
            isChecking = false;
        }
    }
    
    // 后台线程：执行所有耗时操作
    void DoCheckInBackground() {
        try {
            Stopwatch perfSw = Stopwatch.StartNew();
            
            // 首次获取节点信息
            if (totalChecks == 0) {
                GetCurrentNode();
            }
            
            totalChecks++;
            
            // 获取进程状态（耗时操作）
            double mem;
            int handles;
            bool running = GetMihomoStats(out mem, out handles);
            LogPerf("GetMihomoStats", perfSw.ElapsedMilliseconds);
            
            // 测试代理连通性（耗时操作：网络请求）
            perfSw.Restart();
            bool proxyOK;
            int delay = TestProxy(out proxyOK);
            LogPerf("TestProxy", perfSw.ElapsedMilliseconds);
            
            // TCP 统计（耗时操作：netstat 命令）
            int[] tcp;
            if (totalChecks % TCP_CHECK_INTERVAL == 0) {
                perfSw.Restart();
                tcp = GetTcpStats();
                lastTcpStats = tcp;
                LogPerf("GetTcpStats", perfSw.ElapsedMilliseconds);
            } else {
                tcp = lastTcpStats;
            }
            
            // 定期更新节点信息和触发延迟测试
            // 首次检测或节点为空时立即获取，之后每 NODE_UPDATE_INTERVAL 次更新
            if (string.IsNullOrEmpty(currentNode) || totalChecks % NODE_UPDATE_INTERVAL == 0) GetCurrentNode();
            if (totalChecks % DELAY_TEST_INTERVAL == 0) TriggerDelayTest();
            
            // 切回 UI 线程更新界面
            this.BeginInvoke((Action)(() => UpdateUI(running, mem, handles, proxyOK, delay, tcp)));
        }
        catch (Exception ex) {
            // 记录异常但不崩溃
            try { LogPerf("CheckError: " + ex.Message, 0); } catch { }
        }
        finally {
            isChecking = false;
        }
    }
    
    // UI 线程：更新界面和执行决策
    void UpdateUI(bool running, double mem, int handles, bool proxyOK, int delay, int[] tcp) {
        int tw = tcp[0], est = tcp[1], cw = tcp[2];
        
        // 更新界面
        string delayStr = delay > 0 ? delay + "ms" : "--";
        string coreShort = string.IsNullOrEmpty(detectedCoreName) ? "未检测" : detectedCoreName;
        memLabel.Text = "内  核:  " + coreShort + "  |  " + mem.ToString("F1") + "MB" + (mem > memoryWarning ? "!" : "") + "  |  句柄: " + handles;
        string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "获取中..." : currentNode;
        string nodeShort = nodeDisplay.Length > 15 ? nodeDisplay.Substring(0, 15) + ".." : nodeDisplay;
        proxyLabel.Text = "代  理:  " + (proxyOK ? "OK" : "X") + " " + delayStr + " | " + nodeShort;
        proxyLabel.ForeColor = !proxyOK ? COLOR_ERROR : (delay > highDelayThreshold ? COLOR_WARNING : COLOR_OK);
        checkLabel.Text = "统  计:  检测 " + totalChecks + "  |  重启 " + totalRestarts + "  |  切换 " + totalSwitches + "  |  黑名单 " + nodeBlacklist.Count;

        TimeSpan stableTime = DateTime.Now - lastStableTime;
        TimeSpan runTime = DateTime.Now - startTime;
        double stableRate = totalChecks > 0 ? (double)(totalChecks - totalFails) / totalChecks * 100 : 100;
        stableLabel.Text = "稳定性:  连续 " + FormatTimeSpan(stableTime) + "  |  运行 " + FormatTimeSpan(runTime) + "  |  成功率 " + stableRate.ToString("F1") + "%";

        // 托盘显示
        string coreDisplay = string.IsNullOrEmpty(detectedCoreName) ? "?" : detectedCoreName;
        trayIcon.Text = coreDisplay + " | " + mem.ToString("F0") + "MB | " + (proxyOK ? delayStr : "!");

        // 决策逻辑
        bool needRestart = false, needSwitch = false;
        string reason = "", evt = "";
        bool hasIssue = false;

        if (!running) {
            needRestart = true; reason = "进程不存在"; evt = "ProcessDown"; hasIssue = true;
        }
        else if (mem > memoryThreshold) {
            needRestart = true; reason = "内存过高" + mem.ToString("F0") + "MB"; evt = "CriticalMemory"; hasIssue = true;
        }
        else if (mem > memoryWarning && !proxyOK) {
            needRestart = true; reason = "内存高+无响应"; evt = "HighMemoryNoProxy"; hasIssue = true;
        }
        else if (cw > 20 && !proxyOK) {
            needRestart = true; reason = "连接泄漏+无响应"; evt = "CloseWaitLeak"; hasIssue = true;
        }
        else if (!proxyOK) {
            failCount++; totalFails++; evt = "ProxyFail"; hasIssue = true;
            consecutiveOK = 0;
            lastStableTime = DateTime.Now;
            if (failCount == 2) { needSwitch = true; reason = "节点无响应"; evt = "NodeSwitch"; }
            else if (failCount >= 4) { needRestart = true; reason = "连续无响应"; evt = "ProxyTimeout"; }
        }
        else if (delay > highDelayThreshold) {
            failCount++; evt = "HighDelay"; hasIssue = true;
            if (failCount >= 2) { needSwitch = true; reason = "延迟过高" + delay + "ms"; evt = "HighDelaySwitch"; failCount = 0; }
        }
        else {
            failCount = 0;
            consecutiveOK++;
            if (mem > memoryWarning) evt = "HighMemoryOK";
        }

        AdjustInterval(hasIssue);
        LogData(proxyOK, delay, mem, handles, tw, est, cw, currentNode, evt);

        // 执行操作（在后台线程执行，避免阻塞 UI）
        if (needSwitch) {
            ThreadPool.QueueUserWorkItem(_ => {
                if (SwitchToBestNode()) {
                    // 切换成功后立即刷新 UI 显示
                    this.BeginInvoke((Action)(() => { 
                        failCount = 0;
                        RefreshNodeDisplay();
                    }));
                }
            });
        }
        if (needRestart) {
            ThreadPool.QueueUserWorkItem(_ => RestartClash(reason));
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
    }

    [STAThread]
    static void Main() {
        // 单实例检测：防止开机自启时启动多个实例
        bool createdNew;
        using (Mutex mutex = new Mutex(true, "ClashGuardianSingleInstance", out createdNew)) {
            if (!createdNew) {
                // 已有实例在运行，直接退出
                return;
            }
            
            Application.EnableVisualStyles();
            Application.Run(new ClashGuardian());
        }
    }
}
