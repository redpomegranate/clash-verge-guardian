using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

/// <summary>
/// UI 初始化、按钮事件、托盘图标、开机自启
/// </summary>
public partial class ClashGuardian
{
    void InitializeUI() {
        this.Text = "Clash Guardian Pro v" + APP_VERSION;
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

        statusLabel = new Label();
        statusLabel.Text = "● 状态: 加速启动中，请稍等...";
        statusLabel.Location = new Point(padding, y);
        statusLabel.Size = new Size(360, 28);
        statusLabel.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        statusLabel.ForeColor = COLOR_WARNING;
        y += 36;

        Label line1 = CreateSeparator(padding, y);
        y += 12;

        memLabel = CreateInfoLabel("内  存:  --", padding, y, COLOR_TEXT);
        y += labelHeight + 4;

        proxyLabel = CreateInfoLabel("代  理:  --", padding, y, COLOR_TEXT);
        y += labelHeight + 4;

        checkLabel = CreateInfoLabel("统  计:  --", padding, y, COLOR_GRAY);
        y += labelHeight + 4;

        stableLabel = CreateInfoLabel("稳定性:  --", padding, y, COLOR_CYAN);
        y += labelHeight + 8;

        Label line2 = CreateSeparator(padding, y);
        y += 10;

        logLabel = new Label();
        logLabel.Text = "最近事件:  无";
        logLabel.Location = new Point(padding, y);
        logLabel.Size = new Size(360, 36);
        logLabel.ForeColor = Color.FromArgb(80, 80, 80);
        y += 44;

        int btnWidth = 110;
        int btnHeight = 32;
        int btnSpacing = 10;

        restartBtn = CreateButton("立即重启", padding, y, btnWidth, btnHeight, () => ThreadPool.QueueUserWorkItem(_ => RestartClash("手动")));
        logBtn = CreateButton("查看数据", padding + btnWidth + btnSpacing, y, btnWidth, btnHeight, () => OpenFileInNotepad(dataFile, "监控数据"));
        exitBtn = CreateButton("退出", padding + (btnWidth + btnSpacing) * 2, y, btnWidth, btnHeight, () => { trayIcon.Visible = false; Application.Exit(); });
        y += btnHeight + 8;

        Button testBtn = CreateButton("测速", padding, y, btnWidth, btnHeight, () => {
            ThreadPool.QueueUserWorkItem(_ => {
                TriggerDelayTest();
                bool ok;
                int d = TestProxy(out ok, true);
                GetCurrentNode();
                this.BeginInvoke((Action)(() => {
                    string ds = d > 0 ? d + "ms" : "--";
                    string nd = string.IsNullOrEmpty(currentNode) ? "--" : SafeNodeName(currentNode);
                    proxyLabel.Text = "代  理:  " + (ok ? "OK" : "X") + " " + ds + " | " + TruncateNodeName(nd);
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
        menu.Items.Add("暂停自动操作 10分钟", null, delegate { PauseAutoActionsFor(10); });
        menu.Items.Add("暂停自动操作 30分钟", null, delegate { PauseAutoActionsFor(30); });
        menu.Items.Add("暂停自动操作 60分钟", null, delegate { PauseAutoActionsFor(60); });
        menu.Items.Add("恢复自动操作", null, delegate { ResumeAutoActions(); });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("立即重启", null, delegate { ThreadPool.QueueUserWorkItem(_ => RestartClash("手动")); });
        menu.Items.Add("切换节点", null, delegate { ThreadPool.QueueUserWorkItem(_ => SwitchToBestNode()); });
        menu.Items.Add("触发测速", null, delegate { TriggerDelayTest(); });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("导出诊断包", null, delegate { ThreadPool.QueueUserWorkItem(_ => ExportDiagnostics()); });
        menu.Items.Add("打开配置", null, delegate { OpenFileInNotepad(configFile, "配置"); });
        menu.Items.Add("查看监控数据", null, delegate { OpenFileInNotepad(dataFile, "监控数据"); });
        menu.Items.Add("查看异常日志", null, delegate { OpenFileInNotepad(logFile, "异常日志"); });
        menu.Items.Add("检查更新", null, delegate { ThreadPool.QueueUserWorkItem(_ => CheckForUpdate(false)); });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("清空黑名单", null, delegate {
            ClearBlacklist();
            RefreshNodeDisplay();
            Log("黑名单已清空");
        });
        menu.Items.Add("移除当前节点黑名单", null, delegate {
            if (RemoveCurrentNodeFromBlacklist()) {
                RefreshNodeDisplay();
                Log("已移除当前节点黑名单");
            } else {
                Log("移除黑名单失败: 当前节点不在黑名单");
            }
        });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("退出", null, delegate { trayIcon.Visible = false; Application.Exit(); });
        trayIcon.ContextMenuStrip = menu;
    }

    void OpenFileInNotepad(string path, string label) {
        try {
            if (string.IsNullOrEmpty(path)) { Log("打开" + label + "失败: 路径为空"); return; }
            if (!File.Exists(path)) { Log("打开" + label + "失败: 文件不存在"); return; }
            Process.Start("notepad", "\"" + path + "\"");
        } catch (Exception ex) {
            Log("打开" + label + "失败: " + ex.Message);
        }
    }

    void PauseAutoActionsFor(int minutes) {
        if (minutes <= 0) return;
        pauseAutoActionsUntil = DateTime.Now.AddMinutes(minutes);
        lastSuppressedActionLog = DateTime.MinValue;
        try { if (timer != null) timer.Interval = normalInterval; } catch { /* ignore */ }
        Log("已暂停自动操作 " + minutes + "分钟");
    }

    void ResumeAutoActions() {
        pauseAutoActionsUntil = DateTime.MinValue;
        lastSuppressedActionLog = DateTime.MinValue;
        failCount = 0;
        consecutiveOK = 0;
        lastStableTime = DateTime.Now;
        try { if (timer != null) timer.Interval = normalInterval; } catch { /* ignore */ }
        Log("已恢复自动操作");
    }

    void ToggleAutoStart() {
        try {
            string appPath = Application.ExecutablePath;
            string keyName = "ClashGuardian";
            RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (rk.GetValue(keyName) != null) {
                rk.DeleteValue(keyName, false);
                Log("已关闭开机自启");
            } else {
                rk.SetValue(keyName, "\"" + appPath + "\"");
                Log("已启用开机自启");
            }
            rk.Close();
        } catch (Exception ex) {
            Log("自启设置失败: " + ex.Message);
        }
    }

    // 刷新节点和统计显示（UI 线程调用）
    void RefreshNodeDisplay() {
        string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "获取中..." : currentNode;
        int dl = Thread.VolatileRead(ref lastDelay);
        string delayStr = dl > 0 ? dl + "ms" : "--";
        proxyLabel.Text = "代  理:  OK " + delayStr + " | " + TruncateNodeName(nodeDisplay);
        proxyLabel.ForeColor = COLOR_OK;
        int blCount;
        lock (blacklistLock) { blCount = nodeBlacklist.Count; }
        checkLabel.Text = "统  计:  检测 " + totalChecks + "  |  重启 " + totalRestarts + "  |  切换 " + totalSwitches + "  |  黑名单 " + blCount;
    }

    string FormatTimeSpan(TimeSpan ts) {
        if (ts.TotalHours >= 1) return string.Format("{0:F1}h", ts.TotalHours);
        if (ts.TotalMinutes >= 1) return string.Format("{0:F0}m", ts.TotalMinutes);
        return string.Format("{0:F0}s", ts.TotalSeconds);
    }
}
