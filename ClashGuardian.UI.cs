using System;
using System.Collections.Generic;
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
    Icon _cachedAppIcon;
    ToolStripMenuItem disabledNodesMenu;
    ToolStripMenuItem preferredNodesMenu;
    ToolStripMenuItem pauseDetectionMenuItem;
    ToolStripMenuItem uuRouteMenuItem;

    ToolStripDropDown disabledNodesDropDown;
    ToolStripDropDown preferredNodesDropDown;
    CheckedListBox disabledNodesListBox;
    CheckedListBox preferredNodesListBox;
    bool suppressDisabledNodesListEvent = false;
    bool suppressPreferredNodesListEvent = false;

    const int POPUP_WIDTH = 340;
    const int POPUP_HEIGHT = 360;
    const int MAIN_PADDING = 16;
    const int MAIN_CONTENT_WIDTH = 360;
    const int MAIN_INFO_HEIGHT = 22;
    const int MAIN_INFO_GAP = 4;
    const int MAIN_BUTTON_HEIGHT = 34;
    const int MAIN_BUTTON_HGAP = 9;
    const int MAIN_BUTTON_VGAP = 10;
    const int MAIN_BUTTON_SHIFT_UP = 6;
    const int MAIN_BOTTOM_PADDING = 20;
    DateTime lastDisabledNodesMenuRefresh = DateTime.MinValue;

    Icon AppIcon {
        get {
            if (_cachedAppIcon != null) return _cachedAppIcon;
            try { _cachedAppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _cachedAppIcon = SystemIcons.Shield; }
            if (_cachedAppIcon == null) _cachedAppIcon = SystemIcons.Shield;
            return _cachedAppIcon;
        }
    }

    void InitializeUI() {
        this.Text = "Clash Guardian Pro v" + APP_VERSION;
        this.ClientSize = new Size(MAIN_PADDING * 2 + MAIN_CONTENT_WIDTH, 320);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = AppIcon;
        this.Font = new Font("Microsoft YaHei UI", 9);
        this.BackColor = COLOR_FORM_BG;

        int padding = MAIN_PADDING;
        int contentWidth = MAIN_CONTENT_WIDTH;
        int y = padding;

        statusLabel = new Label();
        statusLabel.Text = "● 状态: 加速启动中，请稍等...";
        statusLabel.Location = new Point(padding, y);
        statusLabel.Size = new Size(contentWidth, 30);
        statusLabel.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        statusLabel.ForeColor = COLOR_WARNING;
        y += statusLabel.Height + 8;

        Label line1 = CreateSeparator(padding, y);
        y += 14;

        memLabel = CreateInfoLabel("内  存:  --", padding, y, COLOR_TEXT);
        y += MAIN_INFO_HEIGHT + MAIN_INFO_GAP;

        proxyLabel = CreateInfoLabel("代  理:  --", padding, y, COLOR_TEXT);
        y += MAIN_INFO_HEIGHT + MAIN_INFO_GAP;

        checkLabel = CreateInfoLabel("统  计:  --", padding, y, COLOR_GRAY);
        y += MAIN_INFO_HEIGHT + MAIN_INFO_GAP;

        stableLabel = CreateInfoLabel("稳定性:  --", padding, y, COLOR_CYAN);
        y += MAIN_INFO_HEIGHT + 8;

        Label line2 = CreateSeparator(padding, y);
        y += 12;

        logLabel = new Label();
        logLabel.Text = "最近事件:  无";
        logLabel.Location = new Point(padding, y);
        logLabel.Size = new Size(contentWidth, 34);
        logLabel.ForeColor = Color.FromArgb(80, 80, 80);
        y += logLabel.Height + 10;

        y -= MAIN_BUTTON_SHIFT_UP;

        int btnWidth = (contentWidth - MAIN_BUTTON_HGAP * 2) / 3;
        int btnHeight = MAIN_BUTTON_HEIGHT;
        int col2 = padding + btnWidth + MAIN_BUTTON_HGAP;
        int col3 = padding + (btnWidth + MAIN_BUTTON_HGAP) * 2;

        restartBtn = CreateButton("立即重启", padding, y, btnWidth, btnHeight, () => ThreadPool.QueueUserWorkItem(_ => RestartClash("手动")));
        pauseBtn = CreateButton("暂停检测", col2, y, btnWidth, btnHeight, ToggleDetectionPause);
        exitBtn = CreateButton("退出", col3, y, btnWidth, btnHeight, () => { trayIcon.Visible = false; Application.Exit(); });
        y += btnHeight + MAIN_BUTTON_VGAP;

        Button switchBtn = CreateButton("切换节点", padding, y, btnWidth, btnHeight, () => {
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
        followBtn = CreateButton(GetFollowClashButtonText(), col2, y, btnWidth, btnHeight,
            () => ThreadPool.QueueUserWorkItem(_ => ToggleFollowClashWatcher()));
        uuRouteBtn = CreateButton(GetUuRouteButtonText(), col3, y, btnWidth, btnHeight,
            () => ThreadPool.QueueUserWorkItem(_ => ToggleUuRouteWatcher()));

        int contentBottom = y + btnHeight;
        this.ClientSize = new Size(padding * 2 + contentWidth, contentBottom + MAIN_BOTTOM_PADDING);

        this.Controls.Add(statusLabel);
        this.Controls.Add(line1);
        this.Controls.Add(memLabel);
        this.Controls.Add(proxyLabel);
        this.Controls.Add(checkLabel);
        this.Controls.Add(stableLabel);
        this.Controls.Add(line2);
        this.Controls.Add(logLabel);
        this.Controls.Add(restartBtn);
        this.Controls.Add(pauseBtn);
        this.Controls.Add(exitBtn);
        this.Controls.Add(switchBtn);
        this.Controls.Add(followBtn);
        this.Controls.Add(uuRouteBtn);

        this.Resize += delegate { if (this.WindowState == FormWindowState.Minimized) this.Hide(); };

        ThreadPool.QueueUserWorkItem(_ => {
            try { CleanLegacyUuWatcherArtifacts(Application.ExecutablePath); } catch { /* ignore */ }
        });
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
        lbl.Size = new Size(MAIN_CONTENT_WIDTH, MAIN_INFO_HEIGHT);
        lbl.ForeColor = color;
        return lbl;
    }

    Label CreateSeparator(int x, int y) {
        Label line = new Label();
        line.BorderStyle = BorderStyle.Fixed3D;
        line.Location = new Point(x, y);
        line.Size = new Size(MAIN_CONTENT_WIDTH, 2);
        return line;
    }

    void InitializeTrayIcon() {
        trayIcon = new NotifyIcon();
        trayIcon.Icon = AppIcon;
        trayIcon.Text = "Clash 守护";
        trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); };

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Opening += delegate {
            try {
                if (pauseDetectionMenuItem != null) {
                    pauseDetectionMenuItem.Text = _isDetectionPaused ? "恢复检测" : "暂停检测";
                }
                if (uuRouteMenuItem != null) {
                    uuRouteMenuItem.Text = GetUuRouteMenuText();
                }
                if (followBtn != null) {
                    followBtn.Text = GetFollowClashButtonText();
                }
                if (uuRouteBtn != null) {
                    uuRouteBtn.Text = GetUuRouteButtonText();
                }
                if ((DateTime.Now - lastDisabledNodesMenuRefresh).TotalSeconds > 60) {
                    RefreshDisabledNodesMenuAsync(false);
                }
            } catch { /* ignore */ }
        };

        menu.Items.Add("显示窗口", null, delegate { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); });
        pauseDetectionMenuItem = new ToolStripMenuItem("暂停检测");
        pauseDetectionMenuItem.Click += delegate { ToggleDetectionPause(); };
        menu.Items.Add(pauseDetectionMenuItem);

        uuRouteMenuItem = new ToolStripMenuItem(GetUuRouteMenuText());
        uuRouteMenuItem.Click += delegate { ThreadPool.QueueUserWorkItem(_ => ToggleUuRouteWatcher()); };
        menu.Items.Add(uuRouteMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("立即重启", null, delegate { ThreadPool.QueueUserWorkItem(_ => RestartClash("手动")); });
        menu.Items.Add("切换节点", null, delegate {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    if (SwitchToBestNode()) {
                        this.BeginInvoke((Action)(() => { RefreshNodeDisplay(); Log("手动切换成功"); }));
                    } else {
                        this.BeginInvoke((Action)(() => Log("切换失败")));
                    }
                } catch (Exception ex) {
                    Log("切换异常: " + ex.Message);
                }
            });
        });
        menu.Items.Add("触发测速", null, delegate { TriggerDelayTest(); });

        disabledNodesMenu = new ToolStripMenuItem("禁用名单");
        disabledNodesDropDown = CreateScrollableNodeDropDown(out disabledNodesListBox, "刷新列表", () => RefreshDisabledNodesMenuAsync(true));
        disabledNodesMenu.DropDown = disabledNodesDropDown;
        menu.Items.Add(disabledNodesMenu);

        preferredNodesMenu = new ToolStripMenuItem("偏好节点");
        preferredNodesDropDown = CreateScrollableNodeDropDown(out preferredNodesListBox, "刷新列表", () => RefreshDisabledNodesMenuAsync(true));
        preferredNodesMenu.DropDown = preferredNodesDropDown;
        menu.Items.Add(preferredNodesMenu);

        if (disabledNodesListBox != null) {
            disabledNodesListBox.ItemCheck += (s, e) => {
                if (suppressDisabledNodesListEvent) return;
                int idx = e.Index;
                if (!this.IsHandleCreated) return;
                this.BeginInvoke((Action)(() => {
                    try {
                        if (suppressDisabledNodesListEvent) return;
                        if (disabledNodesListBox == null) return;
                        if (idx < 0 || idx >= disabledNodesListBox.Items.Count) return;
                        NodeListItem it = disabledNodesListBox.Items[idx] as NodeListItem;
                        if (it == null || string.IsNullOrEmpty(it.RawName)) return;
                        bool disabled = disabledNodesListBox.GetItemChecked(idx);
                        SetNodeDisabled(it.RawName, disabled);
                    } catch (Exception ex) {
                        Log("禁用名单操作失败: " + ex.Message);
                    }
                }));
            };
        }

        if (preferredNodesListBox != null) {
            preferredNodesListBox.ItemCheck += (s, e) => {
                if (suppressPreferredNodesListEvent) return;
                int idx = e.Index;
                if (!this.IsHandleCreated) return;
                this.BeginInvoke((Action)(() => {
                    try {
                        if (suppressPreferredNodesListEvent) return;
                        if (preferredNodesListBox == null) return;
                        if (idx < 0 || idx >= preferredNodesListBox.Items.Count) return;
                        NodeListItem it = preferredNodesListBox.Items[idx] as NodeListItem;
                        if (it == null || string.IsNullOrEmpty(it.RawName)) return;
                        bool preferred = preferredNodesListBox.GetItemChecked(idx);
                        SetNodePreferred(it.RawName, preferred);
                    } catch (Exception ex) {
                        Log("偏好节点操作失败: " + ex.Message);
                    }
                }));
            };
        }

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

        if (s_followClashMode) {
            InitializeFollowExitMonitor();
        }

        // 启动后异步拉取一次节点列表，生成“禁用名单”子菜单
        RefreshDisabledNodesMenuAsync(true);
    }

    class NodeListItem {
        public readonly string RawName;
        public readonly string DisplayName;

        public NodeListItem(string rawName, string displayName) {
            RawName = rawName ?? "";
            DisplayName = displayName ?? "";
        }

        public override string ToString() {
            return DisplayName;
        }
    }

    ToolStripDropDown CreateScrollableNodeDropDown(out CheckedListBox listBox, string refreshText, Action onRefresh) {
        ToolStripDropDown dd = new ToolStripDropDown();
        dd.AutoSize = false;
        dd.Margin = Padding.Empty;
        dd.Padding = Padding.Empty;

        Panel panel = new Panel();
        panel.Margin = Padding.Empty;
        panel.Padding = new Padding(8);
        panel.Size = new Size(POPUP_WIDTH, POPUP_HEIGHT);
        panel.BackColor = SystemColors.Window;

        Button refreshBtn = new Button();
        refreshBtn.Text = string.IsNullOrEmpty(refreshText) ? "刷新列表" : refreshText;
        refreshBtn.Location = new Point(8, 8);
        refreshBtn.Size = new Size(POPUP_WIDTH - 16, 26);
        refreshBtn.FlatStyle = FlatStyle.Flat;
        refreshBtn.FlatAppearance.BorderSize = 0;
        refreshBtn.BackColor = COLOR_BTN_BG;
        refreshBtn.ForeColor = COLOR_BTN_FG;
        refreshBtn.Cursor = Cursors.Hand;
        refreshBtn.Click += delegate {
            try { if (onRefresh != null) onRefresh(); }
            catch (Exception ex) { Log("刷新列表失败: " + ex.Message); }
        };

        listBox = new CheckedListBox();
        listBox.CheckOnClick = true;
        listBox.IntegralHeight = false;
        listBox.ScrollAlwaysVisible = true;
        listBox.HorizontalScrollbar = true;
        listBox.BorderStyle = BorderStyle.FixedSingle;
        listBox.Location = new Point(8, 8 + 26 + 6);
        listBox.Size = new Size(POPUP_WIDTH - 16, POPUP_HEIGHT - (8 + 26 + 6 + 8));
        try { listBox.Font = new Font("Microsoft YaHei UI", 9); } catch { /* ignore */ }

        panel.Controls.Add(refreshBtn);
        panel.Controls.Add(listBox);

        ToolStripControlHost host = new ToolStripControlHost(panel);
        host.Margin = Padding.Empty;
        host.Padding = Padding.Empty;
        host.AutoSize = false;
        host.Size = panel.Size;

        dd.Items.Add(host);
        dd.Size = panel.Size;
        return dd;
    }

    void RefreshDisabledNodesMenuAsync(bool force) {
        if (disabledNodesMenu == null && preferredNodesMenu == null) return;
        if (!force) {
            if ((DateTime.Now - lastDisabledNodesMenuRefresh).TotalSeconds < 60) return;
        }
        lastDisabledNodesMenuRefresh = DateTime.Now;

        ThreadPool.QueueUserWorkItem(_ => {
            List<string> nodes = new List<string>();
            try {
                string json = ApiRequest("/proxies");
                if (!string.IsNullOrEmpty(json)) {
                    string group = FindSelectorGroup(json);
                    nodes = GetCandidateNodesFromProxiesJson(json, group);
                }
            } catch (Exception ex) {
                Log("刷新禁用名单失败: " + ex.Message);
            }

            try {
                if (!this.IsHandleCreated) return;
                this.BeginInvoke((Action)(() => { BuildDisabledNodesMenu(nodes); BuildPreferredNodesMenu(nodes); }));
            } catch { /* ignore */ }
        });
    }

    void BuildDisabledNodesMenu(List<string> nodes) {
        if (disabledNodesListBox == null) return;

        suppressDisabledNodesListEvent = true;
        try {
            disabledNodesListBox.BeginUpdate();
            disabledNodesListBox.Items.Clear();

            if (nodes == null || nodes.Count == 0) {
                disabledNodesListBox.Enabled = false;
                disabledNodesListBox.Items.Add(new NodeListItem("", "无法获取节点列表"));
                return;
            }

            disabledNodesListBox.Enabled = true;
            foreach (string node in nodes) {
                if (string.IsNullOrEmpty(node)) continue;
                string dn = SafeNodeName(node);
                if (string.IsNullOrEmpty(dn)) dn = node;
                NodeListItem item = new NodeListItem(node, dn);
                int idx = disabledNodesListBox.Items.Add(item);
                bool chk = IsNodeDisabledForUi(node);
                try { disabledNodesListBox.SetItemChecked(idx, chk); } catch { /* ignore */ }
            }
        } finally {
            try { disabledNodesListBox.EndUpdate(); } catch { /* ignore */ }
            suppressDisabledNodesListEvent = false;
        }
    }
    void BuildPreferredNodesMenu(List<string> nodes) {
        if (preferredNodesListBox == null) return;

        suppressPreferredNodesListEvent = true;
        try {
            preferredNodesListBox.BeginUpdate();
            preferredNodesListBox.Items.Clear();

            if (nodes == null || nodes.Count == 0) {
                preferredNodesListBox.Enabled = false;
                preferredNodesListBox.Items.Add(new NodeListItem("", "无法获取节点列表"));
                return;
            }

            preferredNodesListBox.Enabled = true;
            foreach (string node in nodes) {
                if (string.IsNullOrEmpty(node)) continue;
                string dn = SafeNodeName(node);
                if (string.IsNullOrEmpty(dn)) dn = node;
                NodeListItem item = new NodeListItem(node, dn);
                int idx = preferredNodesListBox.Items.Add(item);
                bool chk = IsNodePreferredForUi(node);
                try { preferredNodesListBox.SetItemChecked(idx, chk); } catch { /* ignore */ }
            }
        } finally {
            try { preferredNodesListBox.EndUpdate(); } catch { /* ignore */ }
            suppressPreferredNodesListEvent = false;
        }
    }

    bool IsNodePreferredForUi(string nodeName) {
        if (string.IsNullOrEmpty(nodeName)) return false;
        if (disabledNodesExplicitMode) {
            lock (disabledNodesLock) { if (disabledNodes.Contains(nodeName)) return false; }
        }
        lock (preferredNodesLock) { return preferredNodes.Contains(nodeName); }
    }



    bool IsNodeDisabledForUi(string nodeName) {
        if (string.IsNullOrEmpty(nodeName)) return false;

        if (disabledNodesExplicitMode) {
            lock (disabledNodesLock) { return disabledNodes.Contains(nodeName); }
        }

        // 关键字模式下：若节点被标记为偏好，则允许其覆盖 excludeRegions
        lock (preferredNodesLock) {
            if (preferredNodes.Contains(nodeName)) return false;
        }

        if (excludeRegions == null) return false;
        foreach (string region in excludeRegions) {
            if (!string.IsNullOrEmpty(region) && nodeName.Contains(region)) return true;
        }
        return false;
    }

    int FindNodeIndexInList(CheckedListBox listBox, string nodeName) {
        if (listBox == null || string.IsNullOrEmpty(nodeName)) return -1;
        for (int i = 0; i < listBox.Items.Count; i++) {
            NodeListItem it = listBox.Items[i] as NodeListItem;
            if (it == null) continue;
            if (string.Equals(it.RawName, nodeName, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    void SetPreferredListChecked(string nodeName, bool isChecked) {
        if (preferredNodesListBox == null) return;
        int idx = FindNodeIndexInList(preferredNodesListBox, nodeName);
        if (idx < 0) return;

        suppressPreferredNodesListEvent = true;
        try {
            bool cur = false;
            try { cur = preferredNodesListBox.GetItemChecked(idx); } catch { /* ignore */ }
            if (cur != isChecked) preferredNodesListBox.SetItemChecked(idx, isChecked);
        } catch { /* ignore */ }
        finally { suppressPreferredNodesListEvent = false; }
    }

    void SetDisabledListChecked(string nodeName, bool isChecked) {
        if (disabledNodesListBox == null) return;
        int idx = FindNodeIndexInList(disabledNodesListBox, nodeName);
        if (idx < 0) return;

        suppressDisabledNodesListEvent = true;
        try {
            bool cur = false;
            try { cur = disabledNodesListBox.GetItemChecked(idx); } catch { /* ignore */ }
            if (cur != isChecked) disabledNodesListBox.SetItemChecked(idx, isChecked);
        } catch { /* ignore */ }
        finally { suppressDisabledNodesListEvent = false; }
    }

    void SetNodeDisabled(string nodeName, bool disabled) {
        if (string.IsNullOrEmpty(nodeName)) return;

        // 禁用与偏好互斥：禁用时自动取消偏好
        if (disabled) {
            bool removedPref = false;
            lock (preferredNodesLock) { removedPref = preferredNodes.Remove(nodeName); }
            if (removedPref) ThreadPool.QueueUserWorkItem(_ => SavePreferredNodes());
            SetPreferredListChecked(nodeName, false);
        }

        // 第一次在 UI 上修改时，将关键字模式“实体化”为显示名单 (disabledNodes)
        if (!disabledNodesExplicitMode) {
            disabledNodesExplicitMode = true;
            lock (disabledNodesLock) {
                disabledNodes.Clear();
                if (disabledNodesListBox != null) {
                    foreach (object obj in disabledNodesListBox.CheckedItems) {
                        NodeListItem it = obj as NodeListItem;
                        if (it == null || string.IsNullOrEmpty(it.RawName)) continue;
                        disabledNodes.Add(it.RawName);
                    }
                }
            }

            // 明确禁用名单后，确保其不包含偏好节点
            lock (preferredNodesLock) {
                lock (disabledNodesLock) {
                    foreach (string pn in preferredNodes) {
                        if (!string.IsNullOrEmpty(pn)) disabledNodes.Remove(pn);
                    }
                }
            }

            ThreadPool.QueueUserWorkItem(_ => SaveDisabledNodes());
            Log((disabled ? "禁用: " : "取消禁用: ") + SafeNodeName(nodeName));
            return;
        }

        lock (disabledNodesLock) {
            if (disabled) disabledNodes.Add(nodeName);
            else disabledNodes.Remove(nodeName);
        }
        ThreadPool.QueueUserWorkItem(_ => SaveDisabledNodes());
        Log((disabled ? "禁用: " : "取消禁用: ") + SafeNodeName(nodeName));
    }

    void SetNodePreferred(string nodeName, bool preferred) {
        if (string.IsNullOrEmpty(nodeName)) return;

        // 偏好与禁用互斥：设置偏好时自动取消禁用（显式禁用则移除；关键字禁用则由偏好覆盖）
        if (preferred && disabledNodesExplicitMode) {
            bool removedDisabled = false;
            lock (disabledNodesLock) { removedDisabled = disabledNodes.Remove(nodeName); }
            if (removedDisabled) ThreadPool.QueueUserWorkItem(_ => SaveDisabledNodes());
        }

        lock (preferredNodesLock) {
            if (preferred) preferredNodes.Add(nodeName);
            else preferredNodes.Remove(nodeName);
        }
        ThreadPool.QueueUserWorkItem(_ => SavePreferredNodes());

        // 同步 UI：禁用勾选状态按最新规则刷新（关键字模式下偏好可覆盖 excludeRegions）
        SetDisabledListChecked(nodeName, IsNodeDisabledForUi(nodeName));

        Log((preferred ? "偏好: " : "取消偏好: " ) + SafeNodeName(nodeName));
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

    void ToggleDetectionPause() {
        try {
            if (!this.IsHandleCreated) return;
            if (this.InvokeRequired) {
                this.BeginInvoke((Action)(() => ToggleDetectionPause()));
                return;
            }

            if (_isDetectionPaused) ResumeDetectionUi();
            else PauseDetectionUi();
        } catch { /* ignore */ }
    }

    void PauseDetectionUi() {
        _isDetectionPaused = true;
        try { if (timer != null) timer.Stop(); } catch { /* ignore */ }

        try { if (pauseBtn != null) pauseBtn.Text = "恢复检测"; } catch { /* ignore */ }
        try { if (pauseDetectionMenuItem != null) pauseDetectionMenuItem.Text = "恢复检测"; } catch { /* ignore */ }

        try {
            statusLabel.Text = "● 状态: 暂停检测";
            statusLabel.ForeColor = COLOR_WARNING;
        } catch { /* ignore */ }

        try { if (trayIcon != null) trayIcon.Text = "暂停检测"; } catch { /* ignore */ }
        Log("已暂停检测");
    }

    void ResumeDetectionUi() {
        _isDetectionPaused = false;
        failCount = 0;
        consecutiveOK = 0;
        cooldownCount = 0;
        lastStableTime = DateTime.Now;

        try {
            if (timer != null) {
                timer.Interval = effectiveNormalInterval;
                timer.Start();
            }
        } catch { /* ignore */ }

        try { if (pauseBtn != null) pauseBtn.Text = "暂停检测"; } catch { /* ignore */ }
        try { if (pauseDetectionMenuItem != null) pauseDetectionMenuItem.Text = "暂停检测"; } catch { /* ignore */ }

        try {
            statusLabel.Text = "● 状态: 运行中";
            statusLabel.ForeColor = COLOR_OK;
        } catch { /* ignore */ }

        try { if (trayIcon != null) trayIcon.Text = "恢复检测"; } catch { /* ignore */ }
        RefreshNodeDisplay();
        Log("已恢复检测");
    }

    const string FOLLOW_TASK_NAME = "ClashGuardianFollowClashWatcher";
    const string FOLLOW_RUN_VALUE = "ClashGuardianFollowClashWatcher";
    const string LEGACY_RUN_VALUE = "ClashGuardian";
    const string UU_ROUTE_TASK_NAME = "ClashGuardianUURouteWatcher";
    const string UU_ROUTE_RUN_VALUE = "ClashGuardianUURouteWatcher";
    const string UU_ROUTE_LEGACY_TASK_NAME = "ClashGuardian.UUWatcher";
    const string UU_ROUTE_LEGACY_RUN_VALUE = "ClashGuardian.UUWatcher";
    const string UU_ROUTE_STOP_EVENT = "ClashGuardianUuWatcherStopEvent";

    string GetFollowClashButtonText() {
        return IsFollowClashWatcherEnabled() ? "取消跟随" : "跟随 Clash";
    }

    bool IsFollowClashWatcherEnabled() {
        return IsScheduledTaskPresent(FOLLOW_TASK_NAME) || IsRunKeyPresent(FOLLOW_RUN_VALUE);
    }

    string GetUuRouteButtonText() {
        return IsUuRouteWatcherEnabled() ? "关闭UU联动" : "开启UU联动";
    }

    string GetUuRouteMenuText() {
        return "UU 联动（Steam/PUBG）: " + (IsUuRouteWatcherEnabled() ? "开" : "关");
    }

    bool IsUuRouteWatcherEnabled() {
        return IsScheduledTaskPresent(UU_ROUTE_TASK_NAME) || IsRunKeyPresent(UU_ROUTE_RUN_VALUE);
    }

    bool TryCreateUuRouteTask(string exePath, string runLevel) {
        try {
            string tr = "\"\\\"" + exePath + "\\\" --watch-uu-route\"";
            string args = "/Create /F /SC ONLOGON /RL " + runLevel + " /TN \"" + UU_ROUTE_TASK_NAME + "\" /TR " + tr;
            return RunProcessHidden("schtasks.exe", args) == 0;
        } catch { return false; }
    }

    void CleanLegacyUuWatcherArtifacts(string exePath) {
        try { RunProcessHidden("schtasks.exe", "/Delete /F /TN \"" + UU_ROUTE_LEGACY_TASK_NAME + "\""); } catch { /* ignore */ }
        try { RemoveRunKeyValueIfMatches(UU_ROUTE_LEGACY_RUN_VALUE, ""); } catch { /* ignore */ }
        try { RemoveRunKeyValueIfMatches("ClashGuardian.UUWatcher", ""); } catch { /* ignore */ }
        try { RemoveRunKeyValueIfMatches("ClashGuardianUUWatcher", ""); } catch { /* ignore */ }
    }

    void SignalUuRouteWatcherStop() {
        try {
            using (EventWaitHandle e = new EventWaitHandle(false, EventResetMode.ManualReset, UU_ROUTE_STOP_EVENT)) {
                e.Set();
            }
        } catch { /* ignore */ }
    }

    void StartUuRouteWatcherNow(string exePath) {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
        try {
            ProcessStartInfo psi = new ProcessStartInfo(exePath, "--watch-uu-route");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            try { psi.WorkingDirectory = Path.GetDirectoryName(exePath); } catch { /* ignore */ }
            Process.Start(psi);
        } catch { /* ignore */ }
    }

    void ToggleUuRouteWatcher() {
        string exePath = "";
        try { exePath = Application.ExecutablePath; } catch { exePath = ""; }

        bool enabled = IsUuRouteWatcherEnabled();
        if (enabled) {
            try { RunProcessHidden("schtasks.exe", "/Delete /F /TN \"" + UU_ROUTE_TASK_NAME + "\""); } catch { /* ignore */ }
            try { RemoveRunKeyValueIfMatches(UU_ROUTE_RUN_VALUE, ""); } catch { /* ignore */ }
            try { SignalUuRouteWatcherStop(); } catch { /* ignore */ }
            try { CleanLegacyUuWatcherArtifacts(exePath); } catch { /* ignore */ }
            Log("已关闭 UU 联动");
        } else {
            bool created = false;
            created = TryCreateUuRouteTask(exePath, "HIGHEST");
            if (!created) created = TryCreateUuRouteTask(exePath, "LIMITED");

            if (created) {
                try { RemoveRunKeyValueIfMatches(UU_ROUTE_RUN_VALUE, ""); } catch { /* ignore */ }
                Log("已启用 UU 联动 (计划任务)");
            } else {
                try {
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) {
                        if (rk != null) rk.SetValue(UU_ROUTE_RUN_VALUE, "\"" + exePath + "\" --watch-uu-route");
                    }
                    Log("已启用 UU 联动 (注册表自启)");
                } catch (Exception ex) {
                    Log("UU 联动设置失败: " + ex.Message);
                }
            }

            try { CleanLegacyUuWatcherArtifacts(exePath); } catch { /* ignore */ }
            try { StartUuRouteWatcherNow(exePath); } catch { /* ignore */ }
        }

        try {
            if (!this.IsHandleCreated) return;
            this.BeginInvoke((Action)(() => {
                try { if (uuRouteBtn != null) uuRouteBtn.Text = GetUuRouteButtonText(); } catch { /* ignore */ }
                try { if (uuRouteMenuItem != null) uuRouteMenuItem.Text = GetUuRouteMenuText(); } catch { /* ignore */ }
            }));
        } catch { /* ignore */ }
    }

    bool IsRunKeyPresent(string valueName) {
        try {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)) {
                if (rk == null) return false;
                return rk.GetValue(valueName) != null;
            }
        } catch { return false; }
    }

    void RemoveRunKeyValueIfMatches(string valueName, string exePath) {
        try {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) {
                if (rk == null) return;
                object v = rk.GetValue(valueName);
                string sv = v as string;
                if (v == null) return;
                if (string.IsNullOrEmpty(exePath)) { rk.DeleteValue(valueName, false); return; }
                if (!string.IsNullOrEmpty(sv) && sv.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) >= 0) {
                    rk.DeleteValue(valueName, false);
                }
            }
        } catch { /* ignore */ }
    }

    bool IsScheduledTaskPresent(string taskName) {
        try {
            int code = RunProcessHidden("schtasks.exe", "/Query /TN \"" + taskName + "\"");
            return code == 0;
        } catch { return false; }
    }

    int RunProcessHidden(string fileName, string arguments) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = System.Text.Encoding.Default;
            psi.StandardErrorEncoding = System.Text.Encoding.Default;

            using (Process p = Process.Start(psi)) {
                try { p.WaitForExit(8000); } catch { /* ignore */ }
                try { if (!p.HasExited) p.Kill(); } catch { /* ignore */ }
                try { if (!p.HasExited) return -1; } catch { /* ignore */ }
                return p.ExitCode;
            }
        } catch { return -1; }
    }

    void ToggleFollowClashWatcher() {
        string exePath = "";
        try { exePath = Application.ExecutablePath; } catch { exePath = ""; }

        bool enabled = IsFollowClashWatcherEnabled();
        if (enabled) {
            // Disable: delete task + remove run key fallback
            try { RunProcessHidden("schtasks.exe", "/Delete /F /TN \"" + FOLLOW_TASK_NAME + "\""); } catch { /* ignore */ }
            try { RemoveRunKeyValueIfMatches(FOLLOW_RUN_VALUE, ""); } catch { /* ignore */ }
            try { RemoveRunKeyValueIfMatches(LEGACY_RUN_VALUE, exePath); } catch { /* ignore */ }
            try { SignalWatcherStop(); } catch { /* ignore */ }
            Log("已关闭跟随 Clash");
        } else {
            // Enable: prefer scheduled task; fallback to HKCU\\Run
            bool created = false;
            try {
                string runAs = Environment.UserDomainName + "\\" + Environment.UserName;
                string tr = "\"\\\"" + exePath + "\\\" --watch-clash\"";
                string args = "/Create /F /SC ONLOGON /RL LIMITED /RU \"" + runAs + "\" /NP /TN \"" + FOLLOW_TASK_NAME + "\" /TR " + tr;
                created = RunProcessHidden("schtasks.exe", args) == 0;
            } catch { created = false; }

            if (created) {
                // Avoid duplicates from legacy autorun keys
                try { RemoveRunKeyValueIfMatches(FOLLOW_RUN_VALUE, ""); } catch { /* ignore */ }
                try { RemoveRunKeyValueIfMatches(LEGACY_RUN_VALUE, exePath); } catch { /* ignore */ }
                Log("已启用跟随 Clash (计划任务)");
            } else {
                try {
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) {
                        if (rk != null) {
                            rk.SetValue(FOLLOW_RUN_VALUE, "\"" + exePath + "\" --watch-clash");
                        }
                    }
                    try { RemoveRunKeyValueIfMatches(LEGACY_RUN_VALUE, exePath); } catch { /* ignore */ }
                    Log("已启用跟随 Clash (注册表自启)");
                } catch (Exception ex) {
                    Log("跟随 Clash 设置失败: " + ex.Message);
                }
            }

            // Make it effective immediately (no need to wait next logon)
            try { StartWatcherNow(exePath); } catch { /* ignore */ }
        }

        try {
            if (!this.IsHandleCreated) return;
            this.BeginInvoke((Action)(() => {
                try { if (followBtn != null) followBtn.Text = GetFollowClashButtonText(); } catch { /* ignore */ }
            }));
        } catch { /* ignore */ }
    }

    const string WATCHER_STOP_EVENT = "ClashGuardianWatcherStopEvent";

    void SignalWatcherStop() {
        try {
            using (EventWaitHandle e = new EventWaitHandle(false, EventResetMode.ManualReset, WATCHER_STOP_EVENT)) {
                e.Set();
            }
        } catch { /* ignore */ }
    }

    void StartWatcherNow(string exePath) {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
        try {
            ProcessStartInfo psi = new ProcessStartInfo(exePath, "--watch-clash");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            try { psi.WorkingDirectory = Path.GetDirectoryName(exePath); } catch { /* ignore */ }
            Process.Start(psi);
        } catch { /* ignore */ }
    }

    void InitializeFollowExitMonitor() {
        try {
            if (followExitTimer != null) return;
            followExitTimer = new System.Windows.Forms.Timer();
            followExitTimer.Interval = 1000;
            followExitTimer.Tick += delegate {
                try {
                    if (_isRestarting) { followMissingSince = DateTime.MinValue; return; }
                    if (IsAnyClientProcessRunning()) { followMissingSince = DateTime.MinValue; return; }

                    if (followMissingSince == DateTime.MinValue) followMissingSince = DateTime.Now;
                    if ((DateTime.Now - followMissingSince).TotalSeconds < 5) return;

                    try { if (trayIcon != null) trayIcon.Visible = false; } catch { /* ignore */ }
                    try { Application.Exit(); } catch { /* ignore */ }
                } catch { /* ignore */ }
            };
            followExitTimer.Start();
        } catch { /* ignore */ }
    }

    bool IsAnyClientProcessRunning() {
        string[] names = (clientProcessNamesExpanded != null && clientProcessNamesExpanded.Length > 0)
            ? clientProcessNamesExpanded
            : clientProcessNames;
        if (names == null || names.Length == 0) return false;
        foreach (string name in names) {
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

    // 刷新节点和统计显示（UI 线程调用）
    void RefreshNodeDisplay() {
        string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "获取中..." : SafeNodeName(currentNode);
        int dl = Thread.VolatileRead(ref lastDelay);
        string delayStr = dl > 0 ? dl + "ms" : "--";
        proxyLabel.Text = "代  理:  OK " + delayStr + " | " + TruncateNodeName(nodeDisplay);
        proxyLabel.ForeColor = COLOR_OK;
        int blCount;
        lock (blacklistLock) { blCount = nodeBlacklist.Count; }
        checkLabel.Text = "统  计:  问题 " + totalIssues + "  |  重启 " + totalRestarts + "  |  切换 " + totalSwitches + "  |  黑名单 " + blCount;
    }

    string FormatTimeSpan(TimeSpan ts) {
        if (ts.TotalHours >= 1) return string.Format("{0:F1}h", ts.TotalHours);
        if (ts.TotalMinutes >= 1) return string.Format("{0:F0}m", ts.TotalMinutes);
        return string.Format("{0:F0}s", ts.TotalSeconds);
    }
}

