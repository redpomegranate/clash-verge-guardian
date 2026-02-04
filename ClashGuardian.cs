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
    private Label statusLabel, memLabel, proxyLabel, logLabel, checkLabel;
    private Button restartBtn, exitBtn, logBtn;
    private System.Windows.Forms.Timer timer;
    private string logFile, dataFile, baseDir;
    private int failCount = 0, totalChecks = 0, totalFails = 0, totalRestarts = 0, totalSwitches = 0;
    private string clashApi = "http://127.0.0.1:9097";
    private string clashSecret = "set-your-secret";
    private string currentNode = "";
    private int cooldownCount = 0;  // 重启后冷却计数

    public ClashGuardian()
    { 
        baseDir = @"F:\Clash verge守护进程";
        logFile = Path.Combine(baseDir, "guardian.log");
        dataFile = Path.Combine(baseDir, "monitor_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        CleanOldLogs();
        if (!File.Exists(dataFile))
            File.WriteAllText(dataFile, "Time,ProxyOK,MemMB,Handles,TimeWait,Established,CloseWait,Node,Event\n");

        this.Text = "Clash 守护服务";
        this.Size = new Size(340, 280);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = SystemIcons.Shield;

        statusLabel = new Label(); statusLabel.Text = "状态: 运行中"; statusLabel.Location = new Point(20, 15);
        statusLabel.Size = new Size(280, 25); statusLabel.Font = new Font("Microsoft YaHei", 11, FontStyle.Bold);
        statusLabel.ForeColor = Color.Green;

        memLabel = new Label(); memLabel.Text = "内存: --"; memLabel.Location = new Point(20, 45); memLabel.Size = new Size(280, 18);
        proxyLabel = new Label(); proxyLabel.Text = "代理: --"; proxyLabel.Location = new Point(20, 65); proxyLabel.Size = new Size(280, 18);
        checkLabel = new Label(); checkLabel.Text = "检测: 0"; checkLabel.Location = new Point(20, 85); checkLabel.Size = new Size(280, 18); checkLabel.ForeColor = Color.Gray;
        logLabel = new Label(); logLabel.Text = "最近: 无"; logLabel.Location = new Point(20, 110); logLabel.Size = new Size(280, 40); logLabel.ForeColor = Color.DimGray;

        restartBtn = new Button(); restartBtn.Text = "立即重启"; restartBtn.Location = new Point(20, 160); restartBtn.Size = new Size(90, 28);
        restartBtn.Click += delegate { RestartClash("手动"); };
        logBtn = new Button(); logBtn.Text = "查看日志"; logBtn.Location = new Point(120, 160); logBtn.Size = new Size(90, 28);
        logBtn.Click += delegate { Process.Start("notepad", dataFile); };
        exitBtn = new Button(); exitBtn.Text = "退出"; exitBtn.Location = new Point(220, 160); exitBtn.Size = new Size(80, 28);
        exitBtn.Click += delegate { trayIcon.Visible = false; Application.Exit(); };

        this.Controls.Add(statusLabel); this.Controls.Add(memLabel); this.Controls.Add(proxyLabel);
        this.Controls.Add(checkLabel); this.Controls.Add(logLabel);
        this.Controls.Add(restartBtn); this.Controls.Add(logBtn); this.Controls.Add(exitBtn);

        trayIcon = new NotifyIcon(); trayIcon.Icon = SystemIcons.Shield; trayIcon.Text = "Clash 守护"; trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); };
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, delegate { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); });
        menu.Items.Add("立即重启", null, delegate { RestartClash("手动"); });
        menu.Items.Add("切换节点", null, delegate { SwitchToBestNode(); });
        menu.Items.Add("查看日志", null, delegate { Process.Start("notepad", dataFile); });
        menu.Items.Add("-");
        menu.Items.Add("退出", null, delegate { trayIcon.Visible = false; Application.Exit(); });
        trayIcon.ContextMenuStrip = menu;
        this.Resize += delegate { if (this.WindowState == FormWindowState.Minimized) this.Hide(); };

        timer = new System.Windows.Forms.Timer(); timer.Interval = 8000; timer.Tick += CheckStatus; timer.Start();
        Log("守护启动"); GetCurrentNode(); CheckStatus(null, null);
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

    void LogData(bool proxyOK, double mem, int handles, int tw, int est, int cw, string node, string evt) { 
        string line = string.Format("{0},{1},{2:F1},{3},{4},{5},{6},{7},{8}",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), proxyOK ? "OK" : "FAIL", mem, handles, tw, est, cw, node, evt);
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

    bool SwitchToBestNode() { 
        try { 
            string json = ApiGet("/proxies"); if (json == null) return false;
            List<string> candidates = new List<string>(); int idx = 0;
            while ((idx = json.IndexOf("\"name\":\"", idx)) >= 0) { 
                idx += 8; int end = json.IndexOf("\"", idx);
                if (end > idx) { 
                    string name = json.Substring(idx, end - idx);
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
                if (ApiPut("/proxies/GLOBAL", "{\"name\":\"" + bestNode + "\"}")) { 
                    Log("切换: " + bestNode); currentNode = bestNode; totalSwitches++; return true;
                }
            }
        } catch { }
        return false;
    }

    bool TestProxy() { 
        try { HttpWebRequest req = WebRequest.Create("http://www.gstatic.com/generate_204") as HttpWebRequest;
            req.Proxy = new WebProxy("127.0.0.1", 7897); req.Timeout = 3000;
            using (WebResponse resp = req.GetResponse()) { return true; } } catch { return false; }
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
        try { 
            foreach (Process p in Process.GetProcessesByName("clash-verge")) { p.Kill(); p.WaitForExit(3000); }
            foreach (Process p in Process.GetProcessesByName("verge-mihomo")) { p.Kill(); p.WaitForExit(3000); }
        } catch { }
        Thread.Sleep(2000);
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\clash-verge\clash-verge.exe");
        if (File.Exists(exe)) { Process.Start(exe); Log("已恢复"); }
        failCount = 0; 
        cooldownCount = 5;  // 重启后跳过5次检测（10秒冷却）
        statusLabel.Text = "状态: 重启中..."; statusLabel.ForeColor = Color.Orange;
    }

    void CheckStatus(object s, EventArgs e) { 
        // 冷却期间跳过检测
        if (cooldownCount > 0) {
            cooldownCount--;
            if (cooldownCount == 0) { statusLabel.Text = "状态: 运行中"; statusLabel.ForeColor = Color.Green; }
            return;
        }
        totalChecks++; double mem = 0; int handles = 0; bool running = false;
        foreach (Process p in Process.GetProcesses()) { 
            if (p.ProcessName.Contains("mihomo")) { mem = p.WorkingSet64 / 1024.0 / 1024.0; handles = p.HandleCount; running = true; break; }
        }
        bool proxyOK = TestProxy(); int[] tcp = GetTcpStats(); int tw = tcp[0], est = tcp[1], cw = tcp[2];
        if (totalChecks % 15 == 0) GetCurrentNode();
        memLabel.Text = "内存: " + mem.ToString("F1") + "MB" + (mem > 60 ? " !" : "") + " | 句柄: " + handles + " | TW: " + tw;
        string nodeShort = currentNode.Length > 15 ? currentNode.Substring(0, 15) + ".." : currentNode;
        proxyLabel.Text = "代理: " + (proxyOK ? "正常" : "无响应") + " | " + nodeShort;
        proxyLabel.ForeColor = proxyOK ? Color.Green : Color.Red;
        checkLabel.Text = "检测: " + totalChecks + " | 重启: " + totalRestarts + " | 换节点: " + totalSwitches;
        trayIcon.Text = "Clash守护 | " + mem.ToString("F0") + "MB | " + (proxyOK ? "OK" : "!");
        bool needRestart = false, needSwitch = false; string reason = "", evt = "";
        if (!running) { needRestart = true; reason = "进程不存在"; evt = "ProcessDown"; }
        else if (mem > 70) { needRestart = true; reason = "内存" + mem.ToString("F0") + "MB"; evt = "HighMemory"; }
        else if (cw > 20) { needRestart = true; reason = "连接泄漏"; evt = "CloseWaitLeak"; }
        else if (!proxyOK) { failCount++; totalFails++; evt = "ProxyFail";
            if (failCount == 2) { needSwitch = true; reason = "节点无响应"; evt = "NodeSwitch"; }
            else if (failCount >= 4) { needRestart = true; reason = "连续无响应"; evt = "ProxyTimeout"; }
        } else { failCount = 0; }
        LogData(proxyOK, mem, handles, tw, est, cw, currentNode, evt);
        if (needSwitch) { if (SwitchToBestNode()) failCount = 0; }
        if (needRestart) RestartClash(reason);
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { 
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
    }

    [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new ClashGuardian()); }
}
