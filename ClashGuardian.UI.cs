using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Security.Principal;
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
    ToolStripMenuItem uuMonitorPanelMenuItem;

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
    const int UU_ROUTE_HEARTBEAT_STALE_SECONDS = 20;
    const int UU_ROUTE_WATCHDOG_INTERVAL_MS = 15000;
    const int UU_ROUTE_SELF_HEAL_LOG_THROTTLE_SECONDS = 30;
    const int UU_ROUTE_TASK_DELETE_VERIFY_WAIT_MS = 3000;
    const int UU_MONITOR_REFRESH_INTERVAL_MS = 2000;
    const int UU_MONITOR_HEADER_HEIGHT = 30;
    const int UU_MONITOR_CONTENT_HEIGHT = 220;
    const int UU_MONITOR_FAULT_ALERT_WINDOW_SECONDS = 600;
    const int UU_MONITOR_LOG_TAIL_LINES = 200;
    const int UU_MONITOR_LABEL_MIN_HEIGHT = 20;
    const int UU_MONITOR_LABEL_GAP = 6;
    DateTime lastDisabledNodesMenuRefresh = DateTime.MinValue;
    DateTime lastUuRouteSelfHealLogAt = DateTime.MinValue;
    System.Windows.Forms.Timer uuRouteWatchdogTimer;
    System.Windows.Forms.Timer uuMonitorTimer;
    int _uuRouteWatchdogBusy = 0;
    int _uuMonitorRefreshBusy = 0;
    bool _uuMonitorExpanded = false;
    int _mainWindowCollapsedHeight = 0;
    int _mainWindowExpandedHeight = 0;
    int _mainWindowFixedOuterWidth = 0;
    int _mainWindowChromeHeight = 0;
    bool _mainWindowSizingLock = false;
    Panel uuMonitorPanel;
    Button uuMonitorToggleBtn;
    Label uuMonitorSummaryLabel;
    Label uuMonitorHealthLabel;
    Label uuMonitorStateLabel;
    Label uuMonitorRollbackLabel;
    Label uuMonitorRouteLabel;
    Label uuFaultAdminLabel;
    Label uuFaultIsolationLabel;
    Label uuFaultLocal7897Label;
    Label uuFaultProxyLeakLabel;
    Label uuFaultSteamTakeoverLabel;

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
        this.FormBorderStyle = FormBorderStyle.Sizable;
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

        restartBtn = CreateButton("立即重启", padding, y, btnWidth, btnHeight, () => QueueRestartAction("手动", false, "Manual"));
        pauseBtn = CreateButton("暂停检测", col2, y, btnWidth, btnHeight, ToggleDetectionPause);
        exitBtn = CreateButton("退出", col3, y, btnWidth, btnHeight, () => { trayIcon.Visible = false; Application.Exit(); });
        y += btnHeight + MAIN_BUTTON_VGAP;

        Button switchBtn = CreateButton("切换节点", padding, y, btnWidth, btnHeight, () => {
            QueueManualSwitchAction(false);
        });
        followBtn = CreateButton(GetFollowClashButtonText(), col2, y, btnWidth, btnHeight,
            () => ThreadPool.QueueUserWorkItem(_ => ToggleFollowClashWatcher()));
        uuRouteBtn = CreateButton(GetUuRouteButtonText(), col3, y, btnWidth, btnHeight,
            () => ThreadPool.QueueUserWorkItem(_ => ToggleUuRouteWatcher()));

        int contentBottom = y + btnHeight;
        int monitorHeaderY = contentBottom + 8;
        int monitorPanelY = monitorHeaderY + UU_MONITOR_HEADER_HEIGHT + 6;

        uuMonitorToggleBtn = CreateButton("UU监控: 展开", padding, monitorHeaderY, contentWidth, UU_MONITOR_HEADER_HEIGHT,
            () => ToggleUuMonitorPanel(!_uuMonitorExpanded));

        uuMonitorPanel = new Panel();
        uuMonitorPanel.Location = new Point(padding, monitorPanelY);
        uuMonitorPanel.Size = new Size(contentWidth, UU_MONITOR_CONTENT_HEIGHT);
        uuMonitorPanel.BorderStyle = BorderStyle.FixedSingle;
        uuMonitorPanel.BackColor = Color.FromArgb(250, 250, 250);
        uuMonitorPanel.AutoScroll = true;
        uuMonitorPanel.Visible = false;
        uuMonitorPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        uuMonitorToggleBtn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        int my = 8;
        uuMonitorSummaryLabel = CreateUuMonitorLabel("联动状态: --", my, COLOR_TEXT); my += 21;
        uuMonitorHealthLabel = CreateUuMonitorLabel("运行健康: --", my, COLOR_TEXT); my += 21;
        uuMonitorStateLabel = CreateUuMonitorLabel("状态机: --", my, COLOR_TEXT); my += 21;
        uuMonitorRollbackLabel = CreateUuMonitorLabel("回滚链路: --", my, COLOR_TEXT); my += 21;
        uuMonitorRouteLabel = CreateUuMonitorLabel("实时路由: --", my, COLOR_TEXT); my += 25;
        uuFaultAdminLabel = CreateUuMonitorLabel("● ADMIN_REQUIRED_FOR_UU: --", my, COLOR_OK); my += 19;
        uuFaultIsolationLabel = CreateUuMonitorLabel("● HARD_ISOLATION_APPLY_FAIL: --", my, COLOR_OK); my += 19;
        uuFaultLocal7897Label = CreateUuMonitorLabel("● LOCAL_7897_FAULT_SIGNAL: --", my, COLOR_OK); my += 19;
        uuFaultProxyLeakLabel = CreateUuMonitorLabel("● PROXY_CHAIN_LEAK_DETECTED: --", my, COLOR_OK); my += 19;
        uuFaultSteamTakeoverLabel = CreateUuMonitorLabel("● STEAM_UU_TAKEOVER_NOT_COMPLETE: --", my, COLOR_OK);

        uuMonitorPanel.Controls.Add(uuMonitorSummaryLabel);
        uuMonitorPanel.Controls.Add(uuMonitorHealthLabel);
        uuMonitorPanel.Controls.Add(uuMonitorStateLabel);
        uuMonitorPanel.Controls.Add(uuMonitorRollbackLabel);
        uuMonitorPanel.Controls.Add(uuMonitorRouteLabel);
        uuMonitorPanel.Controls.Add(uuFaultAdminLabel);
        uuMonitorPanel.Controls.Add(uuFaultIsolationLabel);
        uuMonitorPanel.Controls.Add(uuFaultLocal7897Label);
        uuMonitorPanel.Controls.Add(uuFaultProxyLeakLabel);
        uuMonitorPanel.Controls.Add(uuFaultSteamTakeoverLabel);

        _mainWindowCollapsedHeight = monitorHeaderY + UU_MONITOR_HEADER_HEIGHT + MAIN_BOTTOM_PADDING;
        _mainWindowExpandedHeight = monitorPanelY + UU_MONITOR_CONTENT_HEIGHT + MAIN_BOTTOM_PADDING;
        this.ClientSize = new Size(padding * 2 + contentWidth, _mainWindowCollapsedHeight);

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
        this.Controls.Add(uuMonitorToggleBtn);
        this.Controls.Add(uuMonitorPanel);
        LayoutUuMonitorLabels();
        ConfigureMainWindowResizeBounds();
        this.SizeChanged += delegate { OnMainWindowSizeChanged(); };

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

    Label CreateUuMonitorLabel(string text, int y, Color color) {
        Label lbl = new Label();
        lbl.Text = text;
        lbl.Location = new Point(8, y);
        lbl.Size = new Size(MAIN_CONTENT_WIDTH - 16, 20);
        lbl.ForeColor = color;
        PrepareUuMonitorLabelForWrap(lbl);
        return lbl;
    }

    void PrepareUuMonitorLabelForWrap(Label lbl) {
        if (lbl == null) return;
        lbl.AutoSize = false;
        lbl.TextAlign = ContentAlignment.TopLeft;
        lbl.Margin = Padding.Empty;
        int width = MAIN_CONTENT_WIDTH - 16;
        try {
            if (uuMonitorPanel != null) width = Math.Max(80, uuMonitorPanel.ClientSize.Width - 16);
        } catch { /* ignore */ }
        lbl.MaximumSize = new Size(width, 0);
    }

    int MeasureUuMonitorLabelHeight(Label lbl, int width) {
        if (lbl == null) return UU_MONITOR_LABEL_MIN_HEIGHT;
        if (width < 80) width = 80;
        string text = string.IsNullOrEmpty(lbl.Text) ? " " : lbl.Text;
        try {
            Size sz = TextRenderer.MeasureText(text, lbl.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            int h = sz.Height + 2;
            if (h < UU_MONITOR_LABEL_MIN_HEIGHT) h = UU_MONITOR_LABEL_MIN_HEIGHT;
            return h;
        } catch {
            return UU_MONITOR_LABEL_MIN_HEIGHT;
        }
    }

    int GetUuMonitorContentRequiredHeight() {
        if (uuMonitorPanel == null) return UU_MONITOR_CONTENT_HEIGHT;
        int panelWidth = Math.Max(80, uuMonitorPanel.ClientSize.Width - 16);
        Label[] labels = new Label[] {
            uuMonitorSummaryLabel,
            uuMonitorHealthLabel,
            uuMonitorStateLabel,
            uuMonitorRollbackLabel,
            uuMonitorRouteLabel,
            uuFaultAdminLabel,
            uuFaultIsolationLabel,
            uuFaultLocal7897Label,
            uuFaultProxyLeakLabel,
            uuFaultSteamTakeoverLabel
        };
        int y = 8;
        for (int i = 0; i < labels.Length; i++) {
            Label lbl = labels[i];
            if (lbl == null) continue;
            y += MeasureUuMonitorLabelHeight(lbl, panelWidth);
            if (i < labels.Length - 1) y += UU_MONITOR_LABEL_GAP;
        }
        y += 8;
        return y;
    }

    void LayoutUuMonitorLabels() {
        if (uuMonitorPanel == null) return;
        int panelWidth = Math.Max(80, uuMonitorPanel.ClientSize.Width - 16);
        Label[] labels = new Label[] {
            uuMonitorSummaryLabel,
            uuMonitorHealthLabel,
            uuMonitorStateLabel,
            uuMonitorRollbackLabel,
            uuMonitorRouteLabel,
            uuFaultAdminLabel,
            uuFaultIsolationLabel,
            uuFaultLocal7897Label,
            uuFaultProxyLeakLabel,
            uuFaultSteamTakeoverLabel
        };

        int y = 8;
        for (int i = 0; i < labels.Length; i++) {
            Label lbl = labels[i];
            if (lbl == null) continue;
            PrepareUuMonitorLabelForWrap(lbl);
            int h = MeasureUuMonitorLabelHeight(lbl, panelWidth);
            lbl.Location = new Point(8, y);
            lbl.Size = new Size(panelWidth, h);
            y += h;
            if (i < labels.Length - 1) y += UU_MONITOR_LABEL_GAP;
        }
        y += 8;
        try { uuMonitorPanel.AutoScrollMinSize = new Size(0, y); } catch { /* ignore */ }
    }

    void ConfigureMainWindowResizeBounds() {
        try {
            _mainWindowChromeHeight = this.Height - this.ClientSize.Height;
            _mainWindowFixedOuterWidth = this.Width;
            int minOuterHeight = _mainWindowChromeHeight + _mainWindowCollapsedHeight;
            int maxOuterHeight;
            try {
                maxOuterHeight = Screen.FromControl(this).WorkingArea.Height;
            } catch {
                maxOuterHeight = minOuterHeight + 600;
            }
            if (maxOuterHeight < minOuterHeight + 80) maxOuterHeight = minOuterHeight + 80;
            this.MinimumSize = new Size(_mainWindowFixedOuterWidth, minOuterHeight);
            this.MaximumSize = new Size(_mainWindowFixedOuterWidth, maxOuterHeight);
        } catch { /* ignore */ }
    }

    void OnMainWindowSizeChanged() {
        if (_mainWindowSizingLock) return;
        _mainWindowSizingLock = true;
        try {
            // lock width while allowing vertical resize by user
            if (_mainWindowFixedOuterWidth > 0 && this.Width != _mainWindowFixedOuterWidth) {
                this.Size = new Size(_mainWindowFixedOuterWidth, this.Height);
            }

            if (_uuMonitorExpanded && uuMonitorPanel != null) {
                int desired = this.ClientSize.Height - (uuMonitorPanel.Top + MAIN_BOTTOM_PADDING);
                int minNeeded = GetUuMonitorContentRequiredHeight();
                if (desired > UU_MONITOR_CONTENT_HEIGHT) {
                    uuMonitorPanel.Height = desired;
                } else {
                    uuMonitorPanel.Height = UU_MONITOR_CONTENT_HEIGHT;
                }
                if (minNeeded > uuMonitorPanel.Height) {
                    try { uuMonitorPanel.AutoScroll = true; } catch { /* ignore */ }
                }
            }

            LayoutUuMonitorLabels();
        } catch { /* ignore */ }
        _mainWindowSizingLock = false;
    }

    Label CreateSeparator(int x, int y) {
        Label line = new Label();
        line.BorderStyle = BorderStyle.Fixed3D;
        line.Location = new Point(x, y);
        line.Size = new Size(MAIN_CONTENT_WIDTH, 2);
        return line;
    }

    void ShowMainWindowFromTray() {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    void ApplyManualSwitchUiResult(bool switched) {
        if (switched) {
            this.BeginInvoke((Action)(() => { RefreshNodeDisplay(); Log("手动切换成功"); }));
            return;
        }
        this.BeginInvoke((Action)(() => Log("切换失败")));
    }

    void QueueManualSwitchAction(bool logExceptions) {
        ThreadPool.QueueUserWorkItem(_ => {
            if (!logExceptions) {
                ApplyManualSwitchUiResult(SwitchToBestNode());
                return;
            }

            try {
                ApplyManualSwitchUiResult(SwitchToBestNode());
            } catch (Exception ex) {
                Log("切换异常: " + ex.Message);
            }
        });
    }

    void InitializeTrayIcon() {
        trayIcon = new NotifyIcon();
        trayIcon.Icon = AppIcon;
        trayIcon.Text = "Clash 守护";
        trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { ShowMainWindowFromTray(); };

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Opening += delegate {
            try {
                if (pauseDetectionMenuItem != null) {
                    pauseDetectionMenuItem.Text = _isDetectionPaused ? "恢复检测" : "暂停检测";
                }
                if (followBtn != null) {
                    followBtn.Text = GetFollowClashButtonText();
                }
                RefreshUuRouteUiTextSafe();
                RefreshUuMonitorMenuTextSafe();
                if ((DateTime.Now - lastDisabledNodesMenuRefresh).TotalSeconds > 60) {
                    RefreshDisabledNodesMenuAsync(false);
                }
            } catch { /* ignore */ }
        };

        menu.Items.Add("显示窗口", null, delegate { ShowMainWindowFromTray(); });
        pauseDetectionMenuItem = new ToolStripMenuItem("暂停检测");
        pauseDetectionMenuItem.Click += delegate { ToggleDetectionPause(); };
        menu.Items.Add(pauseDetectionMenuItem);

        uuRouteMenuItem = new ToolStripMenuItem(GetUuRouteMenuText());
        uuRouteMenuItem.Click += delegate { ThreadPool.QueueUserWorkItem(_ => ToggleUuRouteWatcher()); };
        menu.Items.Add(uuRouteMenuItem);

        uuMonitorPanelMenuItem = new ToolStripMenuItem(GetUuMonitorPanelMenuText());
        uuMonitorPanelMenuItem.Click += delegate { ToggleUuMonitorPanel(!_uuMonitorExpanded); };
        menu.Items.Add(uuMonitorPanelMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("立即重启", null, delegate { QueueRestartAction("手动", false, "Manual"); });
        menu.Items.Add("切换节点", null, delegate { QueueManualSwitchAction(true); });
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

        EnsureUuRouteWatchdogStarted();
        ThreadPool.QueueUserWorkItem(_ => RunUuRouteWatchdog());
        EnsureUuMonitorTimerStarted();
        ThreadPool.QueueUserWorkItem(_ => RunUuMonitorRefresh());

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
        highDelayCount = 0;
        closeWaitFailCount = 0;
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
        UuRouteWatcherRuntimeState st = GetUuRouteWatcherRuntimeState();
        if (!st.Enabled) return "开启UU联动";
        return "关闭UU联动";
    }

    string GetUuRouteMenuText() {
        UuRouteWatcherRuntimeState st = GetUuRouteWatcherRuntimeState();
        if (!st.Enabled) return "UU 联动（Steam/PUBG）: 关";
        if (st.RequireAdmin) return "UU 联动（Steam/PUBG）: 开-需管理员";
        if (st.RunningHealthy) return "UU 联动（Steam/PUBG）: 开-运行中";
        return "UU 联动（Steam/PUBG）: 开-未运行(自愈中)";
    }

    bool HasUuRouteRunArtifacts() {
        return IsRunKeyPresent(UU_ROUTE_RUN_VALUE)
            || IsRunKeyPresent(UU_ROUTE_LEGACY_RUN_VALUE)
            || IsRunKeyPresent("ClashGuardian.UUWatcher")
            || IsRunKeyPresent("ClashGuardianUUWatcher");
    }

    bool IsUuRouteTaskEnabled() {
        return IsScheduledTaskPresent(UU_ROUTE_TASK_NAME);
    }

    bool IsUuRouteWatcherEnabled() {
        return IsUuRouteTaskEnabled();
    }

    class UuRouteWatcherRuntimeState {
        public bool Enabled = false;
        public bool RunningHealthy = false;
        public bool HardIsolationUnavailable = false;
        public bool RequireAdmin = false;
        public string Mode = "";
        public string DesiredMode = "";
    }

    class UuFaultSignalStatus {
        public string EventName = "";
        public bool Seen = false;
        public bool Active = false;
        public DateTime LastSeen = DateTime.MinValue;

        public UuFaultSignalStatus(string eventName) {
            EventName = eventName ?? "";
        }
    }

    class UuMonitorSnapshot {
        public bool Enabled = false;
        public bool RunningHealthy = false;
        public string Mode = "";
        public string DesiredMode = "";
        public bool RequireAdmin = false;
        public bool HardIsolationUnavailable = false;
        public bool RollbackPending = false;
        public int RetryCount = 0;
        public string NextRetryAt = "";
        public string SwitchId = "";
        public string LastSwitchAt = "";
        public string RouteNow = "";
        public string HeartbeatLastSeen = "";
        public int HeartbeatAgeSec = -1;
        public int WatcherPid = 0;
        public bool WatcherPidRunning = false;
        public bool UuRunning = false;
        public UuFaultSignalStatus AdminRequired = new UuFaultSignalStatus("ADMIN_REQUIRED_FOR_UU");
        public UuFaultSignalStatus HardIsolationFail = new UuFaultSignalStatus("HARD_ISOLATION_APPLY_FAIL");
        public UuFaultSignalStatus Local7897Fault = new UuFaultSignalStatus("LOCAL_7897_FAULT_SIGNAL");
        public UuFaultSignalStatus ProxyChainLeak = new UuFaultSignalStatus("PROXY_CHAIN_LEAK_DETECTED");
        public UuFaultSignalStatus SteamTakeoverResidual = new UuFaultSignalStatus("STEAM_UU_TAKEOVER_NOT_COMPLETE");
    }

    string GetUuMonitorPanelMenuText() {
        return _uuMonitorExpanded ? "UU监控面板: 收起" : "UU监控面板: 展开";
    }

    void RefreshUuMonitorMenuTextSafe() {
        try {
            if (uuMonitorPanelMenuItem != null) uuMonitorPanelMenuItem.Text = GetUuMonitorPanelMenuText();
        } catch { /* ignore */ }
    }

    void ToggleUuMonitorPanel(bool expand) {
        try {
            if (this.InvokeRequired) {
                this.BeginInvoke((Action)(() => ToggleUuMonitorPanel(expand)));
                return;
            }

            _uuMonitorExpanded = expand;
            try { if (uuMonitorPanel != null) uuMonitorPanel.Visible = _uuMonitorExpanded; } catch { /* ignore */ }
            try { if (uuMonitorToggleBtn != null) uuMonitorToggleBtn.Text = _uuMonitorExpanded ? "UU监控: 收起" : "UU监控: 展开"; } catch { /* ignore */ }
            RefreshUuMonitorMenuTextSafe();

            int targetMinHeight = _uuMonitorExpanded ? _mainWindowExpandedHeight : _mainWindowCollapsedHeight;
            if (targetMinHeight > 0 && this.ClientSize.Height < targetMinHeight) {
                this.ClientSize = new Size(this.ClientSize.Width, targetMinHeight);
            }

            if (_uuMonitorExpanded && uuMonitorPanel != null) {
                int desired = this.ClientSize.Height - (uuMonitorPanel.Top + MAIN_BOTTOM_PADDING);
                if (desired > UU_MONITOR_CONTENT_HEIGHT) uuMonitorPanel.Height = desired;
                else uuMonitorPanel.Height = UU_MONITOR_CONTENT_HEIGHT;
            }

            LayoutUuMonitorLabels();
            if (_uuMonitorExpanded) ThreadPool.QueueUserWorkItem(_ => RunUuMonitorRefresh());
        } catch { /* ignore */ }
    }

    void EnsureUuMonitorTimerStarted() {
        try {
            if (uuMonitorTimer != null) return;
            uuMonitorTimer = new System.Windows.Forms.Timer();
            uuMonitorTimer.Interval = UU_MONITOR_REFRESH_INTERVAL_MS;
            uuMonitorTimer.Tick += delegate {
                ThreadPool.QueueUserWorkItem(_ => RunUuMonitorRefresh());
            };
            uuMonitorTimer.Start();
        } catch { /* ignore */ }
    }

    void RunUuMonitorRefresh() {
        if (Interlocked.CompareExchange(ref _uuMonitorRefreshBusy, 1, 0) != 0) return;
        try {
            UuMonitorSnapshot snapshot = CollectUuMonitorSnapshot();
            try {
                if (this.IsHandleCreated) {
                    this.BeginInvoke((Action)(() => {
                        RenderUuMonitorSnapshot(snapshot);
                        RefreshUuMonitorMenuTextSafe();
                    }));
                }
            } catch { /* ignore */ }
        } finally {
            Interlocked.Exchange(ref _uuMonitorRefreshBusy, 0);
        }
    }

    UuMonitorSnapshot CollectUuMonitorSnapshot() {
        UuMonitorSnapshot s = new UuMonitorSnapshot();
        UuRouteWatcherRuntimeState st = GetUuRouteWatcherRuntimeState();
        s.Enabled = st.Enabled;
        s.RunningHealthy = st.RunningHealthy;
        s.Mode = st.Mode;
        s.DesiredMode = st.DesiredMode;
        s.RequireAdmin = st.RequireAdmin;
        s.HardIsolationUnavailable = st.HardIsolationUnavailable;

        string root = GetUuWatcherRootDir();
        if (!string.IsNullOrEmpty(root)) {
            try {
                string statePath = Path.Combine(root, "state.json");
                if (File.Exists(statePath)) {
                    string raw = File.ReadAllText(statePath, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(raw)) {
                        s.RollbackPending = GetJsonBoolValueStatic(raw, "rollbackPending", false);
                        s.RetryCount = GetJsonIntValueStatic(raw, "retryCount", 0);
                        s.NextRetryAt = GetJsonStringValueStatic(raw, "nextRetryAt", "");
                        s.SwitchId = GetJsonStringValueStatic(raw, "switchId", "");
                        s.LastSwitchAt = GetJsonStringValueStatic(raw, "lastSwitchAt", "");
                    }
                }
            } catch { /* ignore */ }

            try {
                string hbPath = Path.Combine(root, "heartbeat.json");
                if (File.Exists(hbPath)) {
                    string hb = File.ReadAllText(hbPath, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(hb)) {
                        s.HeartbeatLastSeen = GetJsonStringValueStatic(hb, "lastSeen", "");
                        DateTime seen = ParseUuWatcherTime(s.HeartbeatLastSeen);
                        if (seen != DateTime.MinValue) {
                            try { s.HeartbeatAgeSec = (int)(DateTime.Now - seen).TotalSeconds; } catch { s.HeartbeatAgeSec = -1; }
                        }
                        s.WatcherPid = GetJsonIntValueStatic(hb, "pid", 0);
                    }
                }
            } catch { /* ignore */ }

            try {
                string logPath = Path.Combine(root, "watcher.log");
                PopulateUuFaultSignals(logPath, s);
            } catch { /* ignore */ }
        }

        s.RouteNow = GetUuRouteNowForMonitor();
        s.WatcherPidRunning = IsProcessAliveSafe(s.WatcherPid);
        s.UuRunning = IsNamedProcessRunningSafe("uu");
        return s;
    }

    bool IsProcessAliveSafe(int pid) {
        if (pid <= 0) return false;
        try {
            using (Process p = Process.GetProcessById(pid)) {
                return p != null && !p.HasExited;
            }
        } catch {
            return false;
        }
    }

    bool IsNamedProcessRunningSafe(string processName) {
        if (string.IsNullOrEmpty(processName)) return false;
        try {
            Process[] procs = Process.GetProcessesByName(processName);
            bool running = procs != null && procs.Length > 0;
            if (procs != null) {
                for (int i = 0; i < procs.Length; i++) {
                    try { procs[i].Dispose(); } catch { /* ignore */ }
                }
            }
            return running;
        } catch {
            return false;
        }
    }

    string GetUuRouteNowForMonitor() {
        try {
            string json = ApiRequest("/proxies", API_TIMEOUT_FAST);
            if (string.IsNullOrEmpty(json)) return "--";
            string now = FindProxyNow(json, UU_WATCHER_ROUTE_GROUP);
            if (string.IsNullOrEmpty(now)) return "--";
            return now;
        } catch {
            return "--";
        }
    }

    Queue<string> ReadTailLinesSafe(string path, int maxLines) {
        Queue<string> q = new Queue<string>();
        if (string.IsNullOrEmpty(path) || maxLines <= 0) return q;
        try {
            using (StreamReader sr = new StreamReader(path, Encoding.UTF8, true)) {
                string line;
                while ((line = sr.ReadLine()) != null) {
                    q.Enqueue(line);
                    while (q.Count > maxLines) q.Dequeue();
                }
            }
        } catch { /* ignore */ }
        return q;
    }

    DateTime ParseWatcherLogLineTime(string line) {
        if (string.IsNullOrEmpty(line) || line.Length < 19) return DateTime.MinValue;
        DateTime ts;
        if (DateTime.TryParse(line.Substring(0, 19), out ts)) return ts;
        return DateTime.MinValue;
    }

    void TouchFaultSignal(UuFaultSignalStatus status, DateTime when) {
        if (status == null) return;
        status.Seen = true;
        if (when != DateTime.MinValue && (status.LastSeen == DateTime.MinValue || when > status.LastSeen)) {
            status.LastSeen = when;
        }
    }

    void FinalizeFaultSignal(UuFaultSignalStatus status, DateTime now) {
        if (status == null) return;
        status.Active = false;
        if (status.LastSeen == DateTime.MinValue) return;
        try {
            status.Active = (now - status.LastSeen).TotalSeconds <= UU_MONITOR_FAULT_ALERT_WINDOW_SECONDS;
        } catch {
            status.Active = false;
        }
    }

    void PopulateUuFaultSignals(string logPath, UuMonitorSnapshot s) {
        if (s == null || string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return;
        Queue<string> lines = ReadTailLinesSafe(logPath, UU_MONITOR_LOG_TAIL_LINES);
        foreach (string line in lines) {
            if (string.IsNullOrEmpty(line)) continue;
            DateTime ts = ParseWatcherLogLineTime(line);
            if (line.IndexOf(s.AdminRequired.EventName, StringComparison.OrdinalIgnoreCase) >= 0) TouchFaultSignal(s.AdminRequired, ts);
            if (line.IndexOf(s.HardIsolationFail.EventName, StringComparison.OrdinalIgnoreCase) >= 0) TouchFaultSignal(s.HardIsolationFail, ts);
            if (line.IndexOf(s.Local7897Fault.EventName, StringComparison.OrdinalIgnoreCase) >= 0) TouchFaultSignal(s.Local7897Fault, ts);
            if (line.IndexOf(s.ProxyChainLeak.EventName, StringComparison.OrdinalIgnoreCase) >= 0) TouchFaultSignal(s.ProxyChainLeak, ts);
            if (line.IndexOf(s.SteamTakeoverResidual.EventName, StringComparison.OrdinalIgnoreCase) >= 0) TouchFaultSignal(s.SteamTakeoverResidual, ts);
        }
        DateTime now = DateTime.Now;
        FinalizeFaultSignal(s.AdminRequired, now);
        FinalizeFaultSignal(s.HardIsolationFail, now);
        FinalizeFaultSignal(s.Local7897Fault, now);
        FinalizeFaultSignal(s.ProxyChainLeak, now);
        FinalizeFaultSignal(s.SteamTakeoverResidual, now);
    }

    string FormatMonitorTimeForUi(string value) {
        if (string.IsNullOrEmpty(value)) return "--";
        DateTime dt = ParseUuWatcherTime(value);
        if (dt == DateTime.MinValue) return "--";
        return dt.ToString("MM-dd HH:mm:ss");
    }

    string FormatFaultRecentForUi(UuFaultSignalStatus status) {
        if (status == null) return "--";
        if (status.LastSeen == DateTime.MinValue) {
            if (status.Seen) return "已发生(时间未知)";
            return "--";
        }
        return status.LastSeen.ToString("MM-dd HH:mm:ss");
    }

    void ApplyFaultLabelUi(Label lbl, UuFaultSignalStatus status) {
        if (lbl == null || status == null) return;
        lbl.Text = "● " + status.EventName + ": " + (status.Active ? "告警" : "正常") + " | 最近: " + FormatFaultRecentForUi(status);
        lbl.ForeColor = status.Active ? COLOR_ERROR : COLOR_OK;
    }

    void RenderUuMonitorSnapshot(UuMonitorSnapshot s) {
        if (s == null) return;
        try {
            if (uuMonitorSummaryLabel != null) {
                if (!s.Enabled) {
                    uuMonitorSummaryLabel.Text = "联动状态: 未启用（任务不存在）";
                    uuMonitorSummaryLabel.ForeColor = COLOR_WARNING;
                } else {
                    uuMonitorSummaryLabel.Text = "联动状态: 已启用 | 健康: " + (s.RunningHealthy ? "运行中" : "未运行(自愈中)");
                    uuMonitorSummaryLabel.ForeColor = s.RunningHealthy ? COLOR_OK : COLOR_WARNING;
                }
            }

            if (uuMonitorHealthLabel != null) {
                string age = s.HeartbeatAgeSec >= 0 ? s.HeartbeatAgeSec + "s" : "--";
                string pid = s.WatcherPid > 0 ? (s.WatcherPid.ToString() + (s.WatcherPidRunning ? "(存活)" : "(失活)")) : "--";
                string hb = FormatMonitorTimeForUi(s.HeartbeatLastSeen);
                uuMonitorHealthLabel.Text = "运行健康: 心跳=" + (s.RunningHealthy ? "正常" : "异常")
                    + " | lastSeen=" + hb + " | age=" + age + " | pid=" + pid + " | uu.exe=" + (s.UuRunning ? "运行中" : "未运行");
            }

            if (uuMonitorStateLabel != null) {
                string mode = string.IsNullOrEmpty(s.Mode) ? "--" : s.Mode;
                string desired = string.IsNullOrEmpty(s.DesiredMode) ? "--" : s.DesiredMode;
                uuMonitorStateLabel.Text = "状态机: mode=" + mode + " | desired=" + desired
                    + " | 需管理员=" + (s.RequireAdmin ? "是" : "否")
                    + " | 隔离不可用=" + (s.HardIsolationUnavailable ? "是" : "否");
            }

            if (uuMonitorRollbackLabel != null) {
                string next = FormatMonitorTimeForUi(s.NextRetryAt);
                string lastSwitch = FormatMonitorTimeForUi(s.LastSwitchAt);
                string sid = string.IsNullOrEmpty(s.SwitchId) ? "--" : s.SwitchId;
                uuMonitorRollbackLabel.Text = "回滚链路: pending=" + (s.RollbackPending ? "1" : "0")
                    + " | retry=" + s.RetryCount
                    + " | next=" + next
                    + " | switchId=" + sid
                    + " | lastSwitch=" + lastSwitch;
            }

            if (uuMonitorRouteLabel != null) {
                string routeNow = string.IsNullOrEmpty(s.RouteNow) ? "--" : s.RouteNow;
                uuMonitorRouteLabel.Text = "实时路由: GAME_STEAM_ROUTE=" + routeNow + " | 刷新=" + DateTime.Now.ToString("HH:mm:ss");
            }

            ApplyFaultLabelUi(uuFaultAdminLabel, s.AdminRequired);
            ApplyFaultLabelUi(uuFaultIsolationLabel, s.HardIsolationFail);
            ApplyFaultLabelUi(uuFaultLocal7897Label, s.Local7897Fault);
            ApplyFaultLabelUi(uuFaultProxyLeakLabel, s.ProxyChainLeak);
            ApplyFaultLabelUi(uuFaultSteamTakeoverLabel, s.SteamTakeoverResidual);
            LayoutUuMonitorLabels();
        } catch { /* ignore */ }
    }

    string GetUuWatcherRootDir() {
        try {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) return Path.Combine(local, "ClashGuardian", "uu-watcher");
        } catch { /* ignore */ }
        return "";
    }

    static bool IsUuActiveLikeMode(string mode) {
        if (string.IsNullOrEmpty(mode)) return false;
        return string.Equals(mode, "UU_ACTIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "ENTERING_UU", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "DEGRADED_EXIT_PENDING", StringComparison.OrdinalIgnoreCase);
    }

    UuRouteWatcherRuntimeState GetUuRouteWatcherRuntimeState() {
        UuRouteWatcherRuntimeState st = new UuRouteWatcherRuntimeState();
        st.Enabled = IsUuRouteWatcherEnabled();
        if (!st.Enabled) return st;

        string root = GetUuWatcherRootDir();
        if (!string.IsNullOrEmpty(root)) {
            try {
                string statePath = Path.Combine(root, "state.json");
                if (File.Exists(statePath)) {
                    string raw = File.ReadAllText(statePath, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(raw)) {
                        st.Mode = GetJsonStringValueStatic(raw, "mode", "");
                        st.DesiredMode = GetJsonStringValueStatic(raw, "desiredMode", "");
                        st.HardIsolationUnavailable = GetJsonBoolValueStatic(raw, "hardIsolationUnavailable", false);
                    }
                }
            } catch { /* ignore */ }

            try {
                string hbPath = Path.Combine(root, "heartbeat.json");
                if (File.Exists(hbPath)) {
                    string hb = File.ReadAllText(hbPath, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(hb)) {
                        DateTime seen = ParseUuWatcherTime(GetJsonStringValueStatic(hb, "lastSeen", ""));
                        if (seen != DateTime.MinValue) {
                            st.RunningHealthy = (DateTime.Now - seen).TotalSeconds <= UU_ROUTE_HEARTBEAT_STALE_SECONDS;
                        }
                    }
                }
            } catch { /* ignore */ }
        }

        bool desiredActive = string.Equals(st.DesiredMode, "UU_ACTIVE", StringComparison.OrdinalIgnoreCase);
        bool uuRunning = false;
        try {
            Process[] uu = Process.GetProcessesByName("uu");
            uuRunning = uu != null && uu.Length > 0;
            if (uu != null) foreach (Process p in uu) p.Dispose();
        } catch { /* ignore */ }
        st.RequireAdmin = st.HardIsolationUnavailable && (desiredActive || IsUuActiveLikeMode(st.Mode) || uuRunning);
        return st;
    }

    void RefreshUuRouteUiTextSafe() {
        try { if (uuRouteBtn != null) uuRouteBtn.Text = GetUuRouteButtonText(); } catch { /* ignore */ }
        try { if (uuRouteMenuItem != null) uuRouteMenuItem.Text = GetUuRouteMenuText(); } catch { /* ignore */ }
        RefreshUuMonitorMenuTextSafe();
    }

    void EnsureUuRouteWatchdogStarted() {
        try {
            if (uuRouteWatchdogTimer != null) return;
            uuRouteWatchdogTimer = new System.Windows.Forms.Timer();
            uuRouteWatchdogTimer.Interval = UU_ROUTE_WATCHDOG_INTERVAL_MS;
            uuRouteWatchdogTimer.Tick += delegate {
                ThreadPool.QueueUserWorkItem(_ => RunUuRouteWatchdog());
            };
            uuRouteWatchdogTimer.Start();
        } catch { /* ignore */ }
    }

    void RunUuRouteWatchdog() {
        if (Interlocked.CompareExchange(ref _uuRouteWatchdogBusy, 1, 0) != 0) return;
        try {
            bool taskPresent = IsUuRouteTaskEnabled();
            UuRouteWatcherRuntimeState st = GetUuRouteWatcherRuntimeState();
            string exePath = "";
            try { exePath = Application.ExecutablePath; } catch { exePath = ""; }

            if (taskPresent && !st.RunningHealthy) {
                StartUuRouteWatcherNow(exePath);

                DateTime now = DateTime.Now;
                if (lastUuRouteSelfHealLogAt == DateTime.MinValue || (now - lastUuRouteSelfHealLogAt).TotalSeconds >= UU_ROUTE_SELF_HEAL_LOG_THROTTLE_SECONDS) {
                    Log("UU 联动 watcher 未运行，已尝试自愈拉起");
                    lastUuRouteSelfHealLogAt = now;
                }
            }

            try {
                if (this.IsHandleCreated) {
                    this.BeginInvoke((Action)(() => RefreshUuRouteUiTextSafe()));
                }
            } catch { /* ignore */ }
        } finally {
            Interlocked.Exchange(ref _uuRouteWatchdogBusy, 0);
        }
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

    bool IsCurrentProcessAdmin() {
        try {
            WindowsPrincipal wp = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return wp.IsInRole(WindowsBuiltInRole.Administrator);
        } catch { return false; }
    }

    enum ElevationLaunchResult {
        Success,
        UserCancelled,
        Failed
    }

    ElevationLaunchResult StartElevatedCommand(string fileName, string arguments, int timeoutMs, out int exitCode, out string error) {
        exitCode = -1;
        error = "";
        if (timeoutMs <= 0) timeoutMs = 30000;
        try {
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            try {
                string wd = Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(wd)) psi.WorkingDirectory = wd;
            } catch { /* ignore */ }
            Process p = Process.Start(psi);
            if (p == null) {
                error = "提权进程启动失败";
                return ElevationLaunchResult.Failed;
            }
            using (p) {
                try { p.WaitForExit(timeoutMs); } catch { /* ignore */ }
                try { if (!p.HasExited) p.Kill(); } catch { /* ignore */ }
                if (!p.HasExited) {
                    error = "提权安装超时";
                    return ElevationLaunchResult.Failed;
                }
                exitCode = p.ExitCode;
            }
            return ElevationLaunchResult.Success;
        } catch (System.ComponentModel.Win32Exception ex) {
            if (ex.NativeErrorCode == 1223) {
                error = "你取消了管理员授权";
                return ElevationLaunchResult.UserCancelled;
            }
            error = ex.Message;
            return ElevationLaunchResult.Failed;
        } catch (Exception ex) {
            error = ex.Message;
            return ElevationLaunchResult.Failed;
        }
    }

    ElevationLaunchResult StartElevatedInstaller(string exePath, string arguments, out int exitCode, out string error) {
        exitCode = -1;
        error = "";
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) {
            error = "程序路径无效";
            return ElevationLaunchResult.Failed;
        }
        ElevationLaunchResult r = StartElevatedCommand(exePath, arguments, 30000, out exitCode, out error);
        if (r != ElevationLaunchResult.Success) return r;
        if (exitCode == UU_ROUTE_INSTALL_EXIT_OK) return ElevationLaunchResult.Success;
        error = "提权安装退出码: " + exitCode;
        return ElevationLaunchResult.Failed;
    }

    void ShowUuRouteHint(string text, MessageBoxIcon icon) {
        if (string.IsNullOrEmpty(text)) return;
        try {
            if (!this.IsHandleCreated) return;
            this.BeginInvoke((Action)(() => {
                try { MessageBox.Show(this, text, "UU 联动", MessageBoxButtons.OK, icon); } catch { /* ignore */ }
            }));
        } catch { /* ignore */ }
    }

    bool EnsureUuRouteTaskStrictAdmin(string exePath, bool tryElevate, bool userTriggered, out string error) {
        error = "";
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) {
            error = "找不到 ClashGuardian 可执行文件";
            return false;
        }

        try { CleanLegacyUuWatcherArtifacts(exePath); } catch { /* ignore */ }
        try { RemoveRunKeyValueIfMatches(UU_ROUTE_RUN_VALUE, ""); } catch { /* ignore */ }

        if (IsUuRouteTaskEnabled()) return true;

        bool isAdmin = IsCurrentProcessAdmin();
        if (isAdmin) {
            bool created = TryCreateUuRouteTask(exePath, "HIGHEST");
            if (!created) {
                error = "创建管理员计划任务失败";
                return false;
            }
            if (!IsUuRouteTaskEnabled()) {
                error = "任务创建后校验失败";
                return false;
            }
            try { RemoveRunKeyValueIfMatches(UU_ROUTE_RUN_VALUE, ""); } catch { /* ignore */ }
            return true;
        }

        if (!tryElevate) {
            error = "需要管理员权限";
            return false;
        }

        int installExit;
        string installError;
        ElevationLaunchResult result = StartElevatedInstaller(exePath, "--install-uu-route-task", out installExit, out installError);
        if (result == ElevationLaunchResult.Success) {
            Thread.Sleep(500);
            if (IsUuRouteTaskEnabled()) {
                try { RemoveRunKeyValueIfMatches(UU_ROUTE_RUN_VALUE, ""); } catch { /* ignore */ }
                return true;
            }
            error = "提权安装已执行，但未检测到计划任务";
            return false;
        }

        if (result == ElevationLaunchResult.UserCancelled) {
            error = "已取消管理员授权，UU 联动未启用";
            if (userTriggered) ShowUuRouteHint(error, MessageBoxIcon.Warning);
            return false;
        }

        if (installExit == UU_ROUTE_INSTALL_EXIT_NOT_ADMIN) error = "提权安装未获得管理员权限";
        else if (installExit == UU_ROUTE_INSTALL_EXIT_TASK_CREATE_FAILED) error = "提权安装失败：计划任务创建失败";
        else if (installExit == UU_ROUTE_INSTALL_EXIT_TASK_VERIFY_FAILED) error = "提权安装失败：任务校验失败";
        else if (string.IsNullOrEmpty(error)) error = string.IsNullOrEmpty(installError) ? "提权安装失败" : installError;
        if (userTriggered) ShowUuRouteHint(error, MessageBoxIcon.Error);
        return false;
    }

    bool WaitUuRouteTasksAbsent(int waitMs) {
        if (waitMs <= 0) waitMs = UU_ROUTE_TASK_DELETE_VERIFY_WAIT_MS;
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < waitMs) {
            if (!IsScheduledTaskPresent(UU_ROUTE_TASK_NAME) && !IsScheduledTaskPresent(UU_ROUTE_LEGACY_TASK_NAME)) return true;
            Thread.Sleep(200);
        }
        return !IsScheduledTaskPresent(UU_ROUTE_TASK_NAME) && !IsScheduledTaskPresent(UU_ROUTE_LEGACY_TASK_NAME);
    }

    bool DisableUuRouteTaskStrictAdmin(bool tryElevate, bool userTriggered, out string error) {
        error = "";

        if (IsCurrentProcessAdmin()) {
            try { RunProcessHidden("schtasks.exe", "/Delete /F /TN \"" + UU_ROUTE_TASK_NAME + "\""); } catch { /* ignore */ }
            try { RunProcessHidden("schtasks.exe", "/Delete /F /TN \"" + UU_ROUTE_LEGACY_TASK_NAME + "\""); } catch { /* ignore */ }
        } else {
            if (!tryElevate) {
                error = "需要管理员权限";
                return false;
            }

            int exitCode;
            string elevateError;
            string cmdArgs =
                "/c " +
                "schtasks /Delete /F /TN \"" + UU_ROUTE_TASK_NAME + "\" >nul 2>&1 & " +
                "schtasks /Delete /F /TN \"" + UU_ROUTE_LEGACY_TASK_NAME + "\" >nul 2>&1 & " +
                "exit /b 0";
            ElevationLaunchResult result = StartElevatedCommand("cmd.exe", cmdArgs, 30000, out exitCode, out elevateError);
            if (result == ElevationLaunchResult.UserCancelled) {
                error = "已取消管理员授权，UU 联动未关闭";
                if (userTriggered) ShowUuRouteHint(error, MessageBoxIcon.Warning);
                return false;
            }
            if (result != ElevationLaunchResult.Success) {
                error = string.IsNullOrEmpty(elevateError) ? "提权关闭失败" : elevateError;
                if (userTriggered) ShowUuRouteHint(error, MessageBoxIcon.Error);
                return false;
            }
        }

        bool absent = WaitUuRouteTasksAbsent(UU_ROUTE_TASK_DELETE_VERIFY_WAIT_MS);
        if (!absent) {
            error = "计划任务删除失败或校验未通过";
            if (userTriggered) ShowUuRouteHint(error, MessageBoxIcon.Error);
            return false;
        }

        try { RemoveRunKeyValueIfMatches(UU_ROUTE_RUN_VALUE, ""); } catch { /* ignore */ }
        try { RemoveRunKeyValueIfMatches(UU_ROUTE_LEGACY_RUN_VALUE, ""); } catch { /* ignore */ }
        try { RemoveRunKeyValueIfMatches("ClashGuardian.UUWatcher", ""); } catch { /* ignore */ }
        try { RemoveRunKeyValueIfMatches("ClashGuardianUUWatcher", ""); } catch { /* ignore */ }
        return true;
    }

    bool StartUuRouteWatcherNow(string exePath) {
        if (IsUuRouteTaskEnabled()) {
            try {
                int code = RunProcessHidden("schtasks.exe", "/Run /TN \"" + UU_ROUTE_TASK_NAME + "\"");
                if (code == 0) return true;
            } catch { /* ignore */ }
        }

        // 严格模式下，非管理员进程不直接拉起 watcher，避免再次进入 non-admin 循环。
        if (!IsCurrentProcessAdmin()) return false;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
        try {
            ProcessStartInfo psi = new ProcessStartInfo(exePath, "--watch-uu-route");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            try { psi.WorkingDirectory = Path.GetDirectoryName(exePath); } catch { /* ignore */ }
            Process p = Process.Start(psi);
            return p != null;
        } catch { return false; }
    }

    void ToggleUuRouteWatcher() {
        string exePath = "";
        try { exePath = Application.ExecutablePath; } catch { exePath = ""; }

        bool enabled = IsUuRouteWatcherEnabled();
        bool enableSucceeded = false;
        if (enabled) {
            string disableError;
            bool ok = DisableUuRouteTaskStrictAdmin(true, true, out disableError);
            if (ok) {
                try { SignalUuRouteWatcherStop(); } catch { /* ignore */ }
                try { CleanLegacyUuWatcherArtifacts(exePath); } catch { /* ignore */ }
                Log("已关闭 UU 联动");
            } else {
                if (string.IsNullOrEmpty(disableError)) disableError = "未知错误";
                Log("UU 联动关闭失败: " + disableError);
            }
        } else {
            string setupError;
            bool ok = EnsureUuRouteTaskStrictAdmin(exePath, true, true, out setupError);
            if (ok) {
                try { CleanLegacyUuWatcherArtifacts(exePath); } catch { /* ignore */ }
                try { RemoveRunKeyValueIfMatches(UU_ROUTE_RUN_VALUE, ""); } catch { /* ignore */ }
                try { StartUuRouteWatcherNow(exePath); } catch { /* ignore */ }
                Log("已启用 UU 联动 (管理员计划任务)");
                enableSucceeded = true;
            } else {
                if (string.IsNullOrEmpty(setupError)) setupError = "未知错误";
                Log("UU 联动设置失败: " + setupError);
            }
        }

        try {
            if (this.IsHandleCreated) {
                this.BeginInvoke((Action)(() => {
                    if (enableSucceeded) ToggleUuMonitorPanel(true);
                    RefreshUuRouteUiTextSafe();
                }));
            }
        } catch { /* ignore */ }

        ThreadPool.QueueUserWorkItem(_ => RunUuRouteWatchdog());
        ThreadPool.QueueUserWorkItem(_ => RunUuMonitorRefresh());
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

