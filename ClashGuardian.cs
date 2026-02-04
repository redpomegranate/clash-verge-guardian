using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text;

public class ClashGuardian : Form
{ 
    private NotifyIcon trayIcon;
    private Label statusLabel, memLabel, proxyLabel, logLabel, checkLabel, stableLabel;
    private Button restartBtn, exitBtn, logBtn;
    private System.Windows.Forms.Timer timer;
    private string logFile, dataFile, baseDir;
    private int failCount = 0, totalChecks = 0, totalFails = 0, totalRestarts = 0, totalSwitches = 0;
    private string clashApi = "http://127.0.0.1:9097";
    private string clashSecret = "set-your-secret";
    private string currentNode = "";
    private int cooldownCount = 0;
    
    // 智能化新增
    private int normalInterval = 10000;   // 正常检测间隔：10秒
    private int fastInterval = 3000;      // 异常时快速检测：3秒
    private DateTime lastStableTime;      // 上次稳定时间
    private DateTime startTime;           // 启动时间
    private int consecutiveOK = 0;        // 连续正常次数
    private Dictionary<string, DateTime> nodeBlacklist = new Dictionary<string, DateTime>(); // 节点黑名单
    private int blacklistMinutes = 5;     // 黑名单时长（分钟）
    private int lastDelay = 0;            // 上次延迟
    private int highDelayThreshold = 3000; // 高延迟阈值
    private int delayTestInterval = 40;   // 每40次检测触发一次全节点延迟测试（约6-7分钟）

    public ClashGuardian()
    { 
        baseDir = AppDomain.CurrentDomain.BaseDirectory;
        logFile = Path.Combine(baseDir, "guardian.log");
        dataFile = Path.Combine(baseDir, "monitor_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        startTime = DateTime.Now;
        lastStableTime = DateTime.Now;
        CleanOldLogs();
        if (!File.Exists(dataFile))
            File.WriteAllText(dataFile, "Time,ProxyOK,Delay,MemMB,Handles,TimeWait,Established,CloseWait,Node,Event\n");

        this.Text = "Clash 守护服务 Pro";
        this.Size = new Size(360, 310);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = SystemIcons.Shield;

        statusLabel = new Label(); statusLabel.Text = "状态: 运行中"; statusLabel.Location = new Point(20, 15);
        statusLabel.Size = new Size(300, 25); statusLabel.Font = new Font("Microsoft YaHei", 11, FontStyle.Bold);
        statusLabel.ForeColor = Color.Green;

        memLabel = new Label(); memLabel.Text = "内存: --"; memLabel.Location = new Point(20, 45); memLabel.Size = new Size(300, 18);
        proxyLabel = new Label(); proxyLabel.Text = "代理: --"; proxyLabel.Location = new Point(20, 65); proxyLabel.Size = new Size(300, 18);
        checkLabel = new Label(); checkLabel.Text = "检测: 0"; checkLabel.Location = new Point(20, 85); checkLabel.Size = new Size(300, 18); checkLabel.ForeColor = Color.Gray;
        stableLabel = new Label(); stableLabel.Text = "稳定: --"; stableLabel.Location = new Point(20, 105); stableLabel.Size = new Size(300, 18); stableLabel.ForeColor = Color.DarkCyan;
        logLabel = new Label(); logLabel.Text = "最近: 无"; logLabel.Location = new Point(20, 130); logLabel.Size = new Size(300, 40); logLabel.ForeColor = Color.DimGray;

        restartBtn = new Button(); restartBtn.Text = "立即重启"; restartBtn.Location = new Point(20, 180); restartBtn.Size = new Size(90, 28);
        restartBtn.Click += delegate { RestartClash("手动"); };
        logBtn = new Button(); logBtn.Text = "查看日志"; logBtn.Location = new Point(120, 180); logBtn.Size = new Size(90, 28);
        logBtn.Click += delegate { Process.Start("notepad", dataFile); };
        exitBtn = new Button(); exitBtn.Text = "退出"; exitBtn.Location = new Point(220, 180); exitBtn.Size = new Size(80, 28);
        exitBtn.Click += delegate { trayIcon.Visible = false; Application.Exit(); };

        Button testBtn = new Button(); testBtn.Text = "测速"; testBtn.Location = new Point(20, 215); testBtn.Size = new Size(70, 26);
        testBtn.Click += delegate { TriggerDelayTest(); Log("手动测速"); };
        Button switchBtn = new Button(); switchBtn.Text = "切换节点"; switchBtn.Location = new Point(100, 215); switchBtn.Size = new Size(80, 26);
        switchBtn.Click += delegate { if(SwitchToBestNode()) Log("手动切换"); };
        Button clearBlBtn = new Button(); clearBlBtn.Text = "清除黑名单"; clearBlBtn.Location = new Point(190, 215); clearBlBtn.Size = new Size(90, 26);
        clearBlBtn.Click += delegate { nodeBlacklist.Clear(); Log("黑名单已清除"); };

        this.Controls.Add(statusLabel); this.Controls.Add(memLabel); this.Controls.Add(proxyLabel);
        this.Controls.Add(checkLabel); this.Controls.Add(stableLabel); this.Controls.Add(logLabel);
        this.Controls.Add(restartBtn); this.Controls.Add(logBtn); this.Controls.Add(exitBtn);
        this.Controls.Add(testBtn); this.Controls.Add(switchBtn); this.Controls.Add(clearBlBtn);

        trayIcon = new NotifyIcon(); trayIcon.Icon = SystemIcons.Shield; trayIcon.Text = "Clash 守护"; trayIcon.Visible = true;
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
        this.Resize += delegate { if (this.WindowState == FormWindowState.Minimized) this.Hide(); };

        timer = new System.Windows.Forms.Timer(); timer.Interval = normalInterval; timer.Tick += CheckStatus; timer.Start();
        Log("守护启动 Pro"); GetCurrentNode(); CheckStatus(null, null);
    }

    void CleanOldLogs() { 
        try { 
            DateTime cutoff = DateTime.Now.AddDays(-7);
            foreach (string file in Directory.GetFiles(baseDir, "monitor_*.csv")) { 
                FileInfo fi = new FileInfo(file); if (fi.LastWriteTime < cutoff) fi.Delete();
            }
            FileInfo logFi = new FileInfo(logFile); if (logFi.Exists && logFi.Length > 1024 * 1024) logFi.Delete();
        } catch { }
    }

    void Log(string msg) { 
        string line = "[" + DateTime.Now.ToString("MM-dd HH:mm:ss") + "] " + msg;
        try { File.AppendAllText(logFile, line + "\n"); } catch { }
        logLabel.Text = "最近: " + msg;
    }

    void LogData(bool proxyOK, int delay, double mem, int handles, int tw, int est, int cw, string node, string evt) { 
        string line = string.Format("{0},{1},{2},{3:F1},{4},{5},{6},{7},{8},{9}",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), proxyOK ? "OK" : "FAIL", delay, mem, handles, tw, est, cw, node, evt);
        try { File.AppendAllText(dataFile, line + "\n"); } catch { }
    }

    string ApiGet(string path) { 
        try { WebClient wc = new WebClient(); wc.Headers.Add("Authorization", "Bearer " + clashSecret);
            wc.Encoding = Encoding.UTF8; return wc.DownloadString(clashApi + path); } catch { return null; }
    }

    bool ApiPut(string path, string body) { 
        try { WebClient wc = new WebClient(); wc.Headers.Add("Authorization", "Bearer " + clashSecret);
            wc.Headers.Add("Content-Type", "application/json"); wc.Encoding = Encoding.UTF8;
            wc.UploadString(clashApi + path, "PUT", body); return true; } catch { return false; }
    }

    void GetCurrentNode() { 
        try { string json = ApiGet("/proxies/GLOBAL");
            if (json != null && json.Contains("\"now\":")) { 
                int start = json.IndexOf("\"now\":\"") + 7; int end = json.IndexOf("\"", start);
                if (start > 6 && end > start) currentNode = json.Substring(start, end - start);
            }
        } catch { }
    }

    // 触发全节点延迟测试
    void TriggerDelayTest() {
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + "/group/GLOBAL/delay?url=http://www.gstatic.com/generate_204&timeout=5000") as HttpWebRequest;
            req.Method = "GET";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.Timeout = 10000;
            req.GetResponse();
        } catch { }
    }

    // 清理过期黑名单
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
            string json = ApiGet("/proxies"); if (json == null) return false;
            List<string> candidates = new List<string>(); int idx = 0;
            while ((idx = json.IndexOf("\"name\":\"", idx)) >= 0) { 
                idx += 8; int end = json.IndexOf("\"", idx);
                if (end > idx) { 
                    string name = json.Substring(idx, end - idx);
                    // 跳过黑名单节点
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
            string bestNode = null; int bestDelay = 9999;
            foreach (string node in candidates) { 
                if (node == currentNode) continue;
                int histIdx = json.IndexOf("\"" + node + "\""); if (histIdx < 0) continue;
                int delayIdx = json.IndexOf("\"delay\":", histIdx);
                if (delayIdx > 0 && delayIdx < histIdx + 500) { 
                    int delayEnd = json.IndexOfAny(new char[] { ',', '}' }, delayIdx + 8);
                    string delayStr = json.Substring(delayIdx + 8, delayEnd - delayIdx - 8).Trim();
                    int delay; if (int.TryParse(delayStr, out delay) && delay > 0 && delay < bestDelay) { bestDelay = delay; bestNode = node; }
                }
            }
            if (bestNode != null && bestDelay < 2000) { 
                // 把当前节点加入黑名单
                if (!string.IsNullOrEmpty(currentNode)) nodeBlacklist[currentNode] = DateTime.Now;
                if (ApiPut("/proxies/GLOBAL", "{\"name\":\"" + bestNode + "\"}")) { 
                    Log("切换: " + bestNode + " (" + bestDelay + "ms)"); 
                    currentNode = bestNode; totalSwitches++; return true;
                }
            }
        } catch { }
        return false;
    }

    // 多目标连通性测试 + 延迟测量
    int TestProxyWithDelay(out bool success) { 
        string[] testUrls = new string[] {
            "http://www.gstatic.com/generate_204",
            "http://cp.cloudflare.com/generate_204"
        };
        int successCount = 0;
        int totalDelay = 0;
        int minDelay = 9999;
        
        foreach (string url in testUrls) {
            try { 
                Stopwatch sw = Stopwatch.StartNew();
                HttpWebRequest req = WebRequest.Create(url) as HttpWebRequest;
                req.Proxy = new WebProxy("127.0.0.1", 7897); 
                req.Timeout = 5000;
                using (WebResponse resp = req.GetResponse()) { 
                    sw.Stop();
                    int delay = (int)sw.ElapsedMilliseconds;
                    successCount++;
                    totalDelay += delay;
                    if (delay < minDelay) minDelay = delay;
                }
            } catch { }
        }
        
        success = successCount > 0;
        if (successCount > 0) {
            lastDelay = minDelay;
            return minDelay;
        }
        lastDelay = 0;
        return 0;
    }

    int[] GetTcpStats() { 
        int tw = 0, est = 0, cw = 0;
        try { ProcessStartInfo psi = new ProcessStartInfo("netstat", "-an");
            psi.RedirectStandardOutput = true; psi.UseShellExecute = false; psi.CreateNoWindow = true;
            Process p = Process.Start(psi); string output = p.StandardOutput.ReadToEnd();
            foreach (string line in output.Split('\n')) { 
                if (line.Contains(":7897")) { 
                    if (line.Contains("TIME_WAIT")) tw++; else if (line.Contains("ESTABLISHED")) est++; else if (line.Contains("CLOSE_WAIT")) cw++;
                }
            }
        } catch { }
        return new int[] { tw, est, cw };
    }

    void RestartClash(string reason) { 
        Log("重启: " + reason); totalRestarts++;
        consecutiveOK = 0;
        try { 
            foreach (Process p in Process.GetProcessesByName("clash-verge")) { p.Kill(); p.WaitForExit(3000); }
            foreach (Process p in Process.GetProcessesByName("verge-mihomo")) { p.Kill(); p.WaitForExit(3000); }
        } catch { }
        Thread.Sleep(2000);
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\clash-verge\clash-verge.exe");
        if (File.Exists(exe)) { Process.Start(exe); Log("已恢复"); }
        failCount = 0; 
        cooldownCount = 5;
        statusLabel.Text = "状态: 重启中..."; statusLabel.ForeColor = Color.Orange;
        // 重启后恢复正常检测间隔
        timer.Interval = normalInterval;
    }

    // 调整检测间隔
    void AdjustInterval(bool hasIssue) {
        if (hasIssue && timer.Interval != fastInterval) {
            timer.Interval = fastInterval;
        } else if (!hasIssue && consecutiveOK >= 3 && timer.Interval != normalInterval) {
            timer.Interval = normalInterval;
        }
    }

    // 格式化时间间隔
    string FormatTimeSpan(TimeSpan ts) {
        if (ts.TotalHours >= 1) return string.Format("{0:F1}h", ts.TotalHours);
        if (ts.TotalMinutes >= 1) return string.Format("{0:F0}m", ts.TotalMinutes);
        return string.Format("{0:F0}s", ts.TotalSeconds);
    }

    void CheckStatus(object s, EventArgs e) { 
        if (cooldownCount > 0) {
            cooldownCount--;
            if (cooldownCount == 0) { 
                statusLabel.Text = "状态: 运行中"; statusLabel.ForeColor = Color.Green;
                lastStableTime = DateTime.Now;
            }
            return;
        }
        
        totalChecks++; double mem = 0; int handles = 0; bool running = false;
        foreach (Process p in Process.GetProcesses()) { 
            if (p.ProcessName.Contains("mihomo")) { mem = p.WorkingSet64 / 1024.0 / 1024.0; handles = p.HandleCount; running = true; break; }
        }
        
        bool proxyOK;
        int delay = TestProxyWithDelay(out proxyOK);
        int[] tcp = GetTcpStats(); int tw = tcp[0], est = tcp[1], cw = tcp[2];
        
        // 定期更新节点信息和触发延迟测试
        if (totalChecks % 15 == 0) GetCurrentNode();
        if (totalChecks % delayTestInterval == 0) TriggerDelayTest();
        
        // 更新界面
        string delayStr = delay > 0 ? delay + "ms" : "--";
        memLabel.Text = "内存: " + mem.ToString("F1") + "MB" + (mem > 70 ? " !" : "") + " | 句柄: " + handles + " | TW: " + tw;
        string nodeShort = currentNode.Length > 12 ? currentNode.Substring(0, 12) + ".." : currentNode;
        proxyLabel.Text = "代理: " + (proxyOK ? "正常" : "异常") + " " + delayStr + " | " + nodeShort;
        proxyLabel.ForeColor = !proxyOK ? Color.Red : (delay > highDelayThreshold ? Color.Orange : Color.Green);
        checkLabel.Text = "检测: " + totalChecks + " | 重启: " + totalRestarts + " | 换节点: " + totalSwitches + " | 黑名单: " + nodeBlacklist.Count;
        
        TimeSpan stableTime = DateTime.Now - lastStableTime;
        TimeSpan runTime = DateTime.Now - startTime;
        double stableRate = totalChecks > 0 ? (double)(totalChecks - totalFails) / totalChecks * 100 : 100;
        stableLabel.Text = "稳定: " + FormatTimeSpan(stableTime) + " | 运行: " + FormatTimeSpan(runTime) + " | 成功率: " + stableRate.ToString("F1") + "%";
        
        trayIcon.Text = "Clash守护 | " + mem.ToString("F0") + "MB | " + (proxyOK ? delayStr : "!");
        
        bool needRestart = false, needSwitch = false; string reason = "", evt = "";
        bool hasIssue = false;
        
        if (!running) { 
            needRestart = true; reason = "进程不存在"; evt = "ProcessDown"; hasIssue = true;
        }
        else if (mem > 150) { 
            needRestart = true; reason = "内存过高" + mem.ToString("F0") + "MB"; evt = "CriticalMemory"; hasIssue = true;
        }
        else if (mem > 70 && !proxyOK) { 
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
            // 延迟过高，尝试切换节点
            failCount++; evt = "HighDelay"; hasIssue = true;
            if (failCount >= 2) { needSwitch = true; reason = "延迟过高" + delay + "ms"; evt = "HighDelaySwitch"; failCount = 0; }
        }
        else { 
            failCount = 0; 
            consecutiveOK++;
            if (mem > 70) evt = "HighMemoryOK"; 
        }
        
        // 调整检测间隔
        AdjustInterval(hasIssue);
        
        LogData(proxyOK, delay, mem, handles, tw, est, cw, currentNode, evt);
        if (needSwitch) { if (SwitchToBestNode()) failCount = 0; }
        if (needRestart) RestartClash(reason);
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { 
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
    }

    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new ClashGuardian()); }
}
