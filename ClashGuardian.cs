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
    private const int DEFAULT_NORMAL_INTERVAL = 10000;    // 正常检测间隔：10秒
    private const int DEFAULT_FAST_INTERVAL = 3000;       // 异常时快速检测：3秒
    private const int DEFAULT_MEMORY_THRESHOLD = 150;     // 内存阈值 (MB)
    private const int DEFAULT_MEMORY_WARNING = 70;        // 内存警告阈值 (MB)
    private const int DEFAULT_HIGH_DELAY = 3000;          // 高延迟阈值 (ms)
    private const int DEFAULT_BLACKLIST_MINUTES = 20;     // 黑名单时长（分钟）
    private const int DEFAULT_PROXY_PORT = 7897;          // 代理端口
    private const int DEFAULT_API_PORT = 9097;            // API 端口
    private const int TCP_CHECK_INTERVAL = 5;             // TCP 统计检测间隔
    private const int NODE_UPDATE_INTERVAL = 15;          // 节点信息更新间隔
    private const int DELAY_TEST_INTERVAL = 40;           // 延迟测试间隔
    private const int LOG_RETENTION_DAYS = 7;             // 日志保留天数
    private const int COOLDOWN_COUNT = 5;                 // 重启后冷却次数

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
    private int cooldownCount = 0;
    private DateTime lastStableTime;
    private DateTime startTime;
    private int consecutiveOK = 0;
    private Dictionary<string, DateTime> nodeBlacklist = new Dictionary<string, DateTime>();
    private int lastDelay = 0;
    private int[] lastTcpStats = new int[] { 0, 0, 0 };  // TCP 统计缓存

    public ClashGuardian()
    { 
        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        logFile = Path.Combine(baseDir, "guardian.log");
        dataFile = Path.Combine(baseDir, "monitor_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        configFile = Path.Combine(baseDir, "config.json");
        startTime = DateTime.Now;
        lastStableTime = DateTime.Now;

        // 加载配置
        LoadConfig();
        CleanOldLogs();

        if (!File.Exists(dataFile))
            File.WriteAllText(dataFile, "Time,ProxyOK,Delay,MemMB,Handles,TimeWait,Established,CloseWait,Node,Event\n");

        InitializeUI();
        InitializeTrayIcon();

        timer = new System.Windows.Forms.Timer();
        timer.Interval = normalInterval;
        timer.Tick += CheckStatus;
        timer.Start();

        // 启动日志：显示检测信息
        string coreInfo = string.IsNullOrEmpty(detectedCoreName) ? "未检测到" : detectedCoreName;
        string clientInfo = string.IsNullOrEmpty(detectedClientPath) ? "未检测到" : Path.GetFileName(detectedClientPath);
        Log("守护启动 Pro | 内核: " + coreInfo);
        
        GetCurrentNode();
        CheckStatus(null, null);
    }

    // ==================== 配置管理 ====================
    void LoadConfig() {
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
            SaveDefaultConfig();
        }
        
        // 启动时自动探测
        DetectRunningCore();
        if (string.IsNullOrEmpty(detectedCoreName)) {
            // 未检测到运行中的内核，尝试自动发现 API
            AutoDiscoverApi();
        }
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
    
    // 自动发现 API 端口
    void AutoDiscoverApi() {
        foreach (int port in DEFAULT_API_PORTS) {
            try {
                string testApi = "http://127.0.0.1:" + port;
                HttpWebRequest req = WebRequest.Create(testApi + "/version") as HttpWebRequest;
                req.Headers.Add("Authorization", "Bearer " + clashSecret);
                req.Timeout = 2000;
                using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse) {
                    if (resp.StatusCode == HttpStatusCode.OK) {
                        clashApi = testApi;
                        return;
                    }
                }
            } catch { }
        }
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
        statusLabel.Text = "● 状态: 运行中";
        statusLabel.Location = new Point(padding, y);
        statusLabel.Size = new Size(360, 28);
        statusLabel.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        statusLabel.ForeColor = COLOR_OK;
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
        Button testBtn = CreateButton("测速", padding, y, btnWidth, btnHeight, () => { TriggerDelayTest(); Log("手动测速"); });
        Button switchBtn = CreateButton("切换节点", padding + btnWidth + btnSpacing, y, btnWidth, btnHeight, () => { if (SwitchToBestNode()) Log("手动切换"); });
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

    bool IsAutoStartEnabled() {
        try {
            RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            bool enabled = rk.GetValue("ClashGuardian") != null;
            rk.Close();
            return enabled;
        } catch { return false; }
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
        logLabel.Text = "最近事件:  " + msg;
    }

    void LogData(bool proxyOK, int delay, double mem, int handles, int tw, int est, int cw, string node, string evt) {
        // 优化：空事件不写入
        if (string.IsNullOrEmpty(evt)) return;
        string line = string.Format("{0},{1},{2},{3:F1},{4},{5},{6},{7},{8},{9}",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), proxyOK ? "OK" : "FAIL", delay, mem, handles, tw, est, cw, node, evt);
        try { File.AppendAllText(dataFile, line + "\n"); } catch { }
    }

    // ==================== API 通信 ====================
    string ApiGet(string path) {
        try {
            WebClient wc = new WebClient();
            wc.Headers.Add("Authorization", "Bearer " + clashSecret);
            wc.Encoding = Encoding.UTF8;
            return wc.DownloadString(clashApi + path);
        } catch { return null; }
    }

    bool ApiPut(string path, string body) {
        try {
            WebClient wc = new WebClient();
            wc.Headers.Add("Authorization", "Bearer " + clashSecret);
            wc.Headers.Add("Content-Type", "application/json");
            wc.Encoding = Encoding.UTF8;
            wc.UploadString(clashApi + path, "PUT", body);
            return true;
        } catch { return false; }
    }

    // ==================== 工具函数 ====================
    string CleanString(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder();
        foreach (char c in s) {
            if ((c >= 0x20 && c <= 0x7E) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3040 && c <= 0x30FF)) {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    string FormatTimeSpan(TimeSpan ts) {
        if (ts.TotalHours >= 1) return string.Format("{0:F1}h", ts.TotalHours);
        if (ts.TotalMinutes >= 1) return string.Format("{0:F0}m", ts.TotalMinutes);
        return string.Format("{0:F0}s", ts.TotalSeconds);
    }

    // ==================== 节点管理 ====================
    void GetCurrentNode() {
        try {
            string json = ApiGet("/proxies/GLOBAL");
            if (json != null && json.Contains("\"now\":")) {
                int start = json.IndexOf("\"now\":\"") + 7;
                int end = json.IndexOf("\"", start);
                if (start > 6 && end > start) currentNode = CleanString(json.Substring(start, end - start));
            }
        } catch { }
    }

    void TriggerDelayTest() {
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + "/group/GLOBAL/delay?url=http://www.gstatic.com/generate_204&timeout=5000") as HttpWebRequest;
            req.Method = "GET";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.Timeout = 10000;
            req.GetResponse();
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

    bool SwitchToBestNode() {
        CleanBlacklist();
        try {
            string json = ApiGet("/proxies");
            if (json == null) return false;

            List<string> candidates = new List<string>();
            int idx = 0;
            while ((idx = json.IndexOf("\"name\":\"", idx)) >= 0) {
                idx += 8;
                int end = json.IndexOf("\"", idx);
                if (end > idx) {
                    string name = json.Substring(idx, end - idx);
                    if (nodeBlacklist.ContainsKey(name)) continue;
                    if (!name.Contains("HK") && !name.Contains("TW") && !name.Contains("香港") && !name.Contains("台湾") &&
                        !name.Equals("DIRECT") && !name.Equals("REJECT") && !name.Equals("GLOBAL") &&
                        !name.Contains("自动") && !name.Contains("故障") && !name.Contains("负载")) {
                        int typeIdx = json.IndexOf("\"type\":\"", end);
                        if (typeIdx > 0 && typeIdx < end + 200) {
                            int typeEnd = json.IndexOf("\"", typeIdx + 8);
                            string type = json.Substring(typeIdx + 8, typeEnd - typeIdx - 8);
                            if (type == "ss" || type == "vmess" || type == "trojan" || type == "vless" || type == "hysteria" || type == "hysteria2")
                                candidates.Add(name);
                        }
                    }
                }
            }

            string bestNode = null;
            int bestDelay = 9999;
            foreach (string node in candidates) {
                if (node == currentNode) continue;
                int histIdx = json.IndexOf("\"" + node + "\"");
                if (histIdx < 0) continue;
                int delayIdx = json.IndexOf("\"delay\":", histIdx);
                if (delayIdx > 0 && delayIdx < histIdx + 500) {
                    int delayEnd = json.IndexOfAny(new char[] { ',', '}' }, delayIdx + 8);
                    string delayStr = json.Substring(delayIdx + 8, delayEnd - delayIdx - 8).Trim();
                    int delay;
                    if (int.TryParse(delayStr, out delay) && delay > 0 && delay < bestDelay) {
                        bestDelay = delay;
                        bestNode = node;
                    }
                }
            }

            if (bestNode != null && bestDelay < 2000) {
                if (!string.IsNullOrEmpty(currentNode)) nodeBlacklist[currentNode] = DateTime.Now;
                if (ApiPut("/proxies/GLOBAL", "{\"name\":\"" + bestNode + "\"}")) {
                    Log("切换: " + bestNode + " (" + bestDelay + "ms)");
                    currentNode = bestNode;
                    totalSwitches++;
                    return true;
                }
            }
        } catch { }
        return false;
    }

    // ==================== 代理测试 ====================
    int TestProxyWithDelay(out bool success) {
        string[] testUrls = new string[] {
            "http://www.gstatic.com/generate_204",
            "http://cp.cloudflare.com/generate_204"
        };
        int successCount = 0;
        int minDelay = 9999;

        foreach (string url in testUrls) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                HttpWebRequest req = WebRequest.Create(url) as HttpWebRequest;
                req.Proxy = new WebProxy("127.0.0.1", proxyPort);
                req.Timeout = 5000;
                using (WebResponse resp = req.GetResponse()) {
                    sw.Stop();
                    int delay = (int)sw.ElapsedMilliseconds;
                    successCount++;
                    if (delay < minDelay) minDelay = delay;
                }
            } catch { }
        }

        success = successCount > 0;
        lastDelay = success ? minDelay : 0;
        return success ? minDelay : 0;
    }

    // ==================== 系统监控 ====================
    int[] GetTcpStats() {
        int tw = 0, est = 0, cw = 0;
        try {
            ProcessStartInfo psi = new ProcessStartInfo("netstat", "-an");
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            string portStr = ":" + proxyPort;
            foreach (string line in output.Split('\n')) {
                if (line.Contains(portStr)) {
                    if (line.Contains("TIME_WAIT")) tw++;
                    else if (line.Contains("ESTABLISHED")) est++;
                    else if (line.Contains("CLOSE_WAIT")) cw++;
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
        Log("重启: " + reason);
        totalRestarts++;
        consecutiveOK = 0;

        // 终止所有已知的客户端和内核进程
        try {
            // 终止客户端
            foreach (string clientName in clientProcessNames) {
                foreach (Process p in Process.GetProcessesByName(clientName)) {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            // 终止内核
            foreach (string coreName in coreProcessNames) {
                foreach (Process p in Process.GetProcessesByName(coreName)) {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
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
        statusLabel.Text = "● 状态: 重启中...";
        statusLabel.ForeColor = COLOR_WARNING;
        timer.Interval = normalInterval;
    }

    void AdjustInterval(bool hasIssue) {
        if (hasIssue && timer.Interval != fastInterval) {
            timer.Interval = fastInterval;
        } else if (!hasIssue && consecutiveOK >= 3 && timer.Interval != normalInterval) {
            timer.Interval = normalInterval;
        }
    }

    // ==================== 主检测循环 ====================
    void CheckStatus(object s, EventArgs e) {
        if (cooldownCount > 0) {
            cooldownCount--;
            if (cooldownCount == 0) {
                statusLabel.Text = "● 状态: 运行中";
                statusLabel.ForeColor = COLOR_OK;
                lastStableTime = DateTime.Now;
            }
            return;
        }

        totalChecks++;

        // 优化：使用 GetProcessesByName
        double mem;
        int handles;
        bool running = GetMihomoStats(out mem, out handles);

        bool proxyOK;
        int delay = TestProxyWithDelay(out proxyOK);

        // 优化：降低 netstat 调用频率
        int[] tcp;
        if (totalChecks % TCP_CHECK_INTERVAL == 0) {
            tcp = GetTcpStats();
            lastTcpStats = tcp;
        } else {
            tcp = lastTcpStats;
        }
        int tw = tcp[0], est = tcp[1], cw = tcp[2];

        // 定期更新节点信息和触发延迟测试
        if (totalChecks % NODE_UPDATE_INTERVAL == 0) GetCurrentNode();
        if (totalChecks % DELAY_TEST_INTERVAL == 0) TriggerDelayTest();

        // 更新界面
        string delayStr = delay > 0 ? delay + "ms" : "--";
        string coreShort = string.IsNullOrEmpty(detectedCoreName) ? "未检测" : detectedCoreName;
        memLabel.Text = "内  核:  " + coreShort + "  |  " + mem.ToString("F1") + "MB" + (mem > memoryWarning ? "!" : "") + "  |  句柄: " + handles;
        string nodeShort = currentNode.Length > 15 ? currentNode.Substring(0, 15) + ".." : currentNode;
        proxyLabel.Text = "代  理:  " + (proxyOK ? "OK" : "X") + " " + delayStr + " | " + nodeShort;
        proxyLabel.ForeColor = !proxyOK ? COLOR_ERROR : (delay > highDelayThreshold ? COLOR_WARNING : COLOR_OK);
        checkLabel.Text = "统  计:  检测 " + totalChecks + "  |  重启 " + totalRestarts + "  |  切换 " + totalSwitches + "  |  黑名单 " + nodeBlacklist.Count;

        TimeSpan stableTime = DateTime.Now - lastStableTime;
        TimeSpan runTime = DateTime.Now - startTime;
        double stableRate = totalChecks > 0 ? (double)(totalChecks - totalFails) / totalChecks * 100 : 100;
        stableLabel.Text = "稳定性:  连续 " + FormatTimeSpan(stableTime) + "  |  运行 " + FormatTimeSpan(runTime) + "  |  成功率 " + stableRate.ToString("F1") + "%";

        // 托盘和状态显示包含内核信息
        string coreDisplay = string.IsNullOrEmpty(detectedCoreName) ? "?" : detectedCoreName;
        trayIcon.Text = coreDisplay + " | " + mem.ToString("F0") + "MB | " + (proxyOK ? delayStr : "!");

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

        if (needSwitch) { if (SwitchToBestNode()) failCount = 0; }
        if (needRestart) RestartClash(reason);
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
