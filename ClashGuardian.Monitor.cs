using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Forms;

/// <summary>
/// 日志管理、系统监控、重启管理、检测循环、UI更新、决策逻辑
/// </summary>
public partial class ClashGuardian
{
    // ==================== 日志管理 ====================
    void Log(string msg) {
        string entry = DateTime.Now.ToString("HH:mm:ss") + " " + msg;
        try {
            if (File.Exists(logFile) && new FileInfo(logFile).Length > MAX_LOG_SIZE) {
                File.WriteAllText(logFile, "");
            }
            File.AppendAllText(logFile, entry + "\n");
        } catch { /* 日志写入失败不影响程序运行 */ }

        if (logLabel != null && logLabel.IsHandleCreated) {
            try {
                if (logLabel.InvokeRequired)
                    logLabel.BeginInvoke((Action)(() => logLabel.Text = "最近事件:  " + msg));
                else
                    logLabel.Text = "最近事件:  " + msg;
            } catch { /* UI 可能已销毁 */ }
        }
    }

    void LogPerf(string method, long elapsed, long threshold = PERF_LOG_DEFAULT_THRESHOLD) {
        if (method == "TestProxy" && elapsed > PERF_LOG_PROXY_THRESHOLD) {
            Log("[PERF] " + method + " " + elapsed + "ms");
        } else if (method != "TestProxy" && elapsed > threshold) {
            Log("[PERF] " + method + " " + elapsed + "ms");
        }
    }

    void LogData(bool proxyOK, int delay, double mem, int handles, int tw, int est, int cw, string node, string evt) {
        try {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}\n",
                time, proxyOK ? 1 : 0, delay, mem.ToString("F1"), handles, tw, est, cw,
                string.IsNullOrEmpty(node) ? "" : SafeNodeName(node), evt);
            File.AppendAllText(dataFile, line);
        } catch { /* 数据日志写入失败不影响程序运行 */ }
    }

    // 周期性采样节流（提速后改为按时间触发，避免频率失控）
    DateTime lastTcpStatsAt = DateTime.MinValue;
    DateTime lastDelayTestAt = DateTime.MinValue;
    DateTime lastNodeRefreshAt = DateTime.MinValue;

    // 应急/激进策略节流与统计（ticks 用于 Interlocked）
    long lastRestartTicks = 0;
    long lastEmergencyTicks = 0;
    long lastHighMemHighDelayTicks = 0;
    long prevProxyTimeoutRestartTicks = 0;
    long lastProxyTimeoutRestartTicks = 0;
    int cooldownProxyFailStreak = 0;

    // 禁止自动重启/启动客户端时，用于抑制后续激进行为（避免反复打扰/弹窗风险）
    volatile bool manualClientInterventionRequired = false;
    long lastManualInterventionLogTicks = 0;

    void CleanOldLogs() {
        try {
            string dir = string.IsNullOrEmpty(monitorDir) ? baseDir : monitorDir;
            string[] files = Directory.GetFiles(dir, "monitor_*.csv");
            foreach (string f in files) {
                FileInfo fi = new FileInfo(f);
                if ((DateTime.Now - fi.LastWriteTime).TotalDays > LOG_RETENTION_DAYS) {
                    try { fi.Delete(); } catch { /* 旧日志清理：文件占用可忽略 */ }
                }
            }
        } catch { /* 日志清理遍历失败不影响程序运行 */ }
    }

    // ==================== 诊断导出（用户触发） ====================
    string MaskJsonStringValue(string json, string key, out bool ok) {
        ok = false;
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return json;

        string search = "\"" + key + "\"";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) { ok = true; return json; } // 未找到字段，无需脱敏

        int colon = json.IndexOf(':', idx + search.Length);
        if (colon < 0) return json;

        int pos = colon + 1;
        while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        if (pos >= json.Length || json[pos] != '"') return json;

        int valueStart = pos + 1;
        int i = valueStart;
        bool escape = false;
        while (i < json.Length) {
            char c = json[i];
            if (escape) { escape = false; i++; continue; }
            if (c == '\\') { escape = true; i++; continue; }
            if (c == '"') break;
            i++;
        }
        if (i >= json.Length) return json;

        int valueEnd = i; // 指向 closing quote
        ok = true;
        return json.Substring(0, valueStart) + "***" + json.Substring(valueEnd);
    }

    void ExportDiagnostics() {
        try {
            string root = diagnosticsDir;
            if (string.IsNullOrEmpty(root)) {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClashGuardian", "diagnostics");
            }
            Directory.CreateDirectory(root);
            string dir = Path.Combine(root, "diagnostics_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);

            // 1) 导出 config（脱敏）
            bool maskOk = false;
            if (File.Exists(configFile)) {
                try {
                    string cfg = File.ReadAllText(configFile, Encoding.UTF8);
                    string masked = MaskJsonStringValue(cfg, "clashSecret", out maskOk);
                    if (maskOk) {
                        File.WriteAllText(Path.Combine(dir, "config.masked.json"), masked, Encoding.UTF8);
                    }
                } catch { /* 配置读取失败：仅记录到 summary */ }
            }

            // 2) 复制日志
            if (File.Exists(logFile)) {
                try { File.Copy(logFile, Path.Combine(dir, Path.GetFileName(logFile)), true); } catch { /* 文件占用可忽略 */ }
            }

            // 3) 复制最近 2 份监控数据
            try {
                string scanDir = string.IsNullOrEmpty(monitorDir) ? baseDir : monitorDir;
                string[] monitors = Directory.GetFiles(scanDir, "monitor_*.csv");
                List<FileInfo> fis = new List<FileInfo>();
                foreach (string f in monitors) {
                    try { fis.Add(new FileInfo(f)); } catch { /* ignore */ }
                }
                fis.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                int copied = 0;
                foreach (FileInfo fi in fis) {
                    if (copied >= 2) break;
                    try {
                        File.Copy(fi.FullName, Path.Combine(dir, fi.Name), true);
                        copied++;
                    } catch { /* 文件占用可忽略 */ }
                }
            } catch { /* ignore */ }

            // 4) summary.txt（最后写，确保反映真实导出结果）
            int blCount = 0;
            lock (blacklistLock) { blCount = nodeBlacklist.Count; }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ClashGuardian Pro v" + APP_VERSION);
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("BaseDir: " + baseDir);
            sb.AppendLine("AppDataDir: " + appDataDir);
            sb.AppendLine("ConfigFile: " + configFile);
            sb.AppendLine("LogFile: " + logFile);
            sb.AppendLine("MonitorDir: " + monitorDir);
            sb.AppendLine("DiagnosticsDir: " + diagnosticsDir);
            sb.AppendLine("OS: " + Environment.OSVersion);
            sb.AppendLine(".NET: " + Environment.Version);
            sb.AppendLine("Process: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine();

            sb.AppendLine("[Config]");
            sb.AppendLine("clashApi=" + clashApi);
            sb.AppendLine("proxyPort=" + proxyPort);
            sb.AppendLine("normalInterval=" + normalInterval);
            sb.AppendLine("speedFactor=" + speedFactor);
            sb.AppendLine("effectiveNormalInterval=" + effectiveNormalInterval);
            sb.AppendLine("effectiveFastInterval=" + effectiveFastInterval);
            sb.AppendLine("memoryWarning=" + memoryWarning);
            sb.AppendLine("memoryThreshold=" + memoryThreshold);
            sb.AppendLine("highDelayThreshold=" + highDelayThreshold);
            sb.AppendLine("blacklistMinutes=" + blacklistMinutes);
            sb.AppendLine("config.masked.json=" + (maskOk ? "OK" : (File.Exists(configFile) ? "MASK_FAILED" : "MISSING")));
            sb.AppendLine();

            sb.AppendLine("[Runtime]");
            sb.AppendLine("detectedCoreName=" + detectedCoreName);
            sb.AppendLine("detectedClientPath=" + detectedClientPath);
            sb.AppendLine("currentNode=" + currentNode);
            sb.AppendLine("nodeGroup=" + nodeGroup);
            sb.AppendLine("lastDelay=" + Thread.VolatileRead(ref lastDelay));
            sb.AppendLine("blacklistCount=" + blCount);
            sb.AppendLine("totalChecks=" + totalChecks);
            sb.AppendLine("totalIssues=" + totalIssues);
            sb.AppendLine("totalRestarts=" + totalRestarts);
            sb.AppendLine("totalSwitches=" + totalSwitches);
            sb.AppendLine("failCount=" + failCount);
            sb.AppendLine("cooldownCount=" + cooldownCount);
            sb.AppendLine("isRestarting=" + _isRestarting);
            sb.AppendLine("detectionPaused=" + _isDetectionPaused);

            try { File.WriteAllText(Path.Combine(dir, "summary.txt"), sb.ToString(), Encoding.UTF8); }
            catch { /* summary 写入失败可忽略 */ }

            Log("诊断包已导出: " + dir);
            try { Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception ex) { Log("打开诊断目录失败: " + ex.Message); }
        } catch (Exception ex) {
            Log("导出诊断包失败: " + ex.Message);
        }
    }

    // ==================== 系统监控 ====================
    int[] GetTcpStats() {
        int timeWait = 0, established = 0, closeWait = 0;
        try {
            ProcessStartInfo psi = new ProcessStartInfo("netstat", "-n -p TCP");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.Default;
            using (Process p = Process.Start(psi)) {
                string output = p.StandardOutput.ReadToEnd();
                foreach (string line in output.Split('\n')) {
                    string trimmed = line.Trim();
                    if (trimmed.Contains("TIME_WAIT")) timeWait++;
                    else if (trimmed.Contains("ESTABLISHED")) established++;
                    else if (trimmed.Contains("CLOSE_WAIT")) closeWait++;
                }
            }
        } catch (Exception ex) { Log("TCP统计异常: " + ex.Message); }
        return new int[] { timeWait, established, closeWait };
    }

    bool GetMihomoStats(out double memoryMB, out int handles) {
        memoryMB = 0;
        handles = 0;
        string coreName = detectedCoreName; // volatile read

        if (string.IsNullOrEmpty(coreName)) {
            foreach (string name in coreProcessNames) {
                try {
                    Process[] procs = Process.GetProcessesByName(name);
                    if (procs.Length > 0) {
                        detectedCoreName = name;
                        coreName = name;
                        foreach (var p in procs) p.Dispose();
                        DetectRunningClient();
                        break;
                    }
                } catch { /* 进程探测：未找到属正常情况 */ }
            }
        }

        if (string.IsNullOrEmpty(coreName)) return false;

        try {
            Process[] procs = Process.GetProcessesByName(coreName);
            if (procs.Length > 0) {
                try {
                    memoryMB = procs[0].WorkingSet64 / 1024.0 / 1024.0;
                    handles = procs[0].HandleCount;
                } catch (Exception ex) { Log("进程状态获取异常: " + ex.Message); }
                foreach (var p in procs) p.Dispose();
                return true;
            }
            foreach (var p in procs) p.Dispose();
        } catch { /* 进程检测可能因权限失败 */ }
        return false;
    }

    // ==================== 重启管理 ====================
    bool WaitForApiReady(int maxWaitMs) {
        if (maxWaitMs <= 0) return false;
        int waited = 0;
        while (waited < maxWaitMs) {
            try {
                string v = ApiRequest("/version", API_TIMEOUT_FAST);
                if (!string.IsNullOrEmpty(v)) return true;
            } catch { /* ignore */ }

            int sleep = 500;
            if (waited + sleep > maxWaitMs) sleep = maxWaitMs - waited;
            if (sleep <= 0) break;
            Thread.Sleep(sleep);
            waited += sleep;
        }
        return false;
    }

    void KillCoreProcessesOnce() {
        if (coreProcessNames == null || coreProcessNames.Length == 0) return;
        foreach (string name in coreProcessNames) {
            if (string.IsNullOrEmpty(name)) continue;
            try {
                Process[] procs = Process.GetProcessesByName(name);
                foreach (var p in procs) {
                    try { p.Kill(); p.WaitForExit(PROCESS_KILL_TIMEOUT); }
                    catch { /* 进程可能已退出 */ }
                    try { p.Dispose(); } catch { /* ignore */ }
                }
            } catch { /* 终止失败可忽略 */ }
        }
    }

    bool WaitForCoreBack(int maxWaitMs) {
        if (maxWaitMs <= 0) maxWaitMs = CORE_RECOVERY_MAX_WAIT_MS;
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxWaitMs) {
            if (coreProcessNames != null) {
                foreach (string name in coreProcessNames) {
                    if (string.IsNullOrEmpty(name)) continue;
                    try {
                        Process[] procs = Process.GetProcessesByName(name);
                        if (procs != null && procs.Length > 0) {
                            detectedCoreName = name;
                            foreach (var p in procs) { try { p.Dispose(); } catch { /* ignore */ } }
                            return true;
                        }
                        if (procs != null) foreach (var p in procs) { try { p.Dispose(); } catch { /* ignore */ } }
                    } catch { /* 探测失败可忽略 */ }
                }
            }
            Thread.Sleep(250);
        }
        return false;
    }

    bool TryTaskKillPidTree(int pid) {
        if (pid <= 0) return false;
        try {
            ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi)) {
                if (p != null) {
                    try { p.WaitForExit(PROCESS_KILL_TIMEOUT + 5000); } catch { /* ignore */ }
                }
            }
            return true;
        } catch {
            return false;
        }
    }

    void KillClientProcessesAggressively() {
        try {
            HashSet<int> pids = new HashSet<int>();
            string[] killNames = (clientProcessNamesExpanded != null && clientProcessNamesExpanded.Length > 0)
                ? clientProcessNamesExpanded
                : clientProcessNames;

            if (killNames != null) {
                foreach (string clientName in killNames) {
                    if (string.IsNullOrEmpty(clientName)) continue;
                    try {
                        Process[] procs = Process.GetProcessesByName(clientName);
                        foreach (var p in procs) {
                            try { pids.Add(p.Id); } catch { /* ignore */ }
                            try { p.Dispose(); } catch { /* ignore */ }
                        }
                    } catch { /* ignore */ }
                }
            }

            // Extra safety: also kill any process that matches the detected client exe path.
            string clientPath = detectedClientPath; // volatile read
            if (!string.IsNullOrEmpty(clientPath)) {
                try {
                    Process[] all = Process.GetProcesses();
                    foreach (var p in all) {
                        try {
                            string fp = "";
                            try { fp = p.MainModule.FileName; } catch { fp = ""; }
                            if (!string.IsNullOrEmpty(fp) && string.Equals(fp, clientPath, StringComparison.OrdinalIgnoreCase)) {
                                try { pids.Add(p.Id); } catch { /* ignore */ }
                            }
                        } catch { /* ignore */ }
                        finally { try { p.Dispose(); } catch { /* ignore */ } }
                    }
                } catch { /* ignore */ }
            }

            foreach (int pid in pids) {
                try {
                    if (!TryTaskKillPidTree(pid)) {
                        try {
                            Process p = Process.GetProcessById(pid);
                            try { p.Kill(); } catch { /* ignore */ }
                            try { p.WaitForExit(PROCESS_KILL_TIMEOUT); } catch { /* ignore */ }
                            try { p.Dispose(); } catch { /* ignore */ }
                        } catch { /* ignore */ }
                    }
                } catch { /* ignore */ }
            }
        } catch { /* ignore */ }
    }

    bool TryRefreshDelayAndSwitchBestNode() {
        try {
            if (!WaitForApiReady(2000)) {
                try { AutoDiscoverApi(); } catch { /* ignore */ }
                if (!WaitForApiReady(2000)) return false;
            }
            bool ok = false;
            try { ok = SwitchToBestNodeRefreshed(); } catch { ok = false; }
            if (ok) {
                try {
                    if (this.IsHandleCreated) {
                        this.BeginInvoke((Action)(() => { failCount = 0; RefreshNodeDisplay(); }));
                    }
                } catch { /* ignore */ }
            }
            return ok;
        } catch {
            return false;
        }
    }

    bool RestartClientOnce(bool requireGoodDelay) {
        // Full "exit and re-enter" effect as much as possible: kill core + kill client tree + start client.
        try {
            KillCoreProcessesOnce();
        } catch { /* ignore */ }

        try { Thread.Sleep(300); } catch { /* ignore */ }

        try {
            KillClientProcessesAggressively();
        } catch { /* ignore */ }

        try { Thread.Sleep(1000); } catch { /* ignore */ }

        // 启动客户端（客户端会自动启动内核）
        string clientPath = detectedClientPath; // volatile read
        bool started = false;

        if (!string.IsNullOrEmpty(clientPath) && File.Exists(clientPath)) {
            started = StartClientProcess(clientPath);
        }

        // 如果已知路径无效，尝试默认路径
        if (!started) {
            foreach (string path in clientPaths) {
                if (File.Exists(path)) {
                    started = StartClientProcess(path);
                    if (started) { detectedClientPath = path; break; }
                }
            }
        }

        if (!started) {
            Log("未找到客户端路径，无法恢复");
            return false;
        }

        // 等待客户端启动内核 + API 可用
        WaitForCoreBack(CORE_RECOVERY_MAX_WAIT_MS + 6000);
        if (!WaitForApiReady(6000)) {
            try { AutoDiscoverApi(); } catch { /* ignore */ }
            WaitForApiReady(3000);
        }
        DetectRunningCore();

        // 刷新测速并切节点（用于高延迟恢复管线）
        if (requireGoodDelay) {
            TryRefreshDelayAndSwitchBestNode();
            return WaitForProxyRecoveryAndDelay(PROXY_RECOVERY_MAX_WAIT_MS, highDelayThreshold);
        }

        return WaitForProxyRecovery(PROXY_RECOVERY_MAX_WAIT_MS);
    }

    bool ValidateRecovery(bool requireGoodDelay) {
        if (requireGoodDelay) return WaitForProxyRecoveryAndDelay(PROXY_RECOVERY_MAX_WAIT_MS, highDelayThreshold);
        return WaitForProxyRecovery(PROXY_RECOVERY_MAX_WAIT_MS);
    }

    bool TrySwitchSubscriptionForRecovery(string reasonEvent, out string oldName, out string newName) {
        oldName = "";
        newName = "";

        if (!autoSwitchSubscription) return false;
        if (subscriptionWhitelist == null || subscriptionWhitelist.Length < 2) return false;

        DateTime lastSub = new DateTime(Interlocked.Read(ref lastSubscriptionSwitchTicks));
        if ((DateTime.Now - lastSub).TotalMinutes < subscriptionSwitchCooldownMinutes) return false;

        lock (subscriptionLock) {
            if (_isSwitchingSubscription) return false;
            _isSwitchingSubscription = true;
        }

        try {
            if (!TrySwitchClashVergeRevSubscription(subscriptionWhitelist, out oldName, out newName)) return false;

            Interlocked.Exchange(ref lastSubscriptionSwitchTicks, DateTime.Now.Ticks);
            Log("订阅切换: " + oldName + " -> " + newName + " (reason=" + (reasonEvent ?? "") + ")");
            return true;
        } catch (Exception ex) {
            Log("订阅切换异常: " + ex.Message);
            return false;
        } finally {
            _isSwitchingSubscription = false;
        }
    }

    void RestartClash(string reason, bool forceRestartClient = false, string eventName = "") {
        // 防止并发重启（手动+自动 或 多次自动）
        lock (restartLock) {
            if (_isRestarting) return;
            _isRestarting = true;
        }

        try {
            bool requireGoodDelay = string.Equals(eventName, "HighMemoryHighDelay", StringComparison.Ordinal);

            // 高内存+高延迟：节流，避免短时间反复重置内核/重启客户端
            if (requireGoodDelay) {
                long nowTicks0 = DateTime.Now.Ticks;
                long lastTicks0 = Interlocked.Read(ref lastHighMemHighDelayTicks);
                bool throttled = false;
                try {
                    if (lastTicks0 > 0 && (new TimeSpan(nowTicks0 - lastTicks0)).TotalSeconds < HMHD_COOLDOWN_SECONDS) throttled = true;
                } catch { throttled = false; }

                if (throttled) {
                    Log("节流: 内存高+延迟过高，刷新测速并切节点");
                    TryRefreshDelayAndSwitchBestNode();
                    return;
                }
                Interlocked.Exchange(ref lastHighMemHighDelayTicks, nowTicks0);
            }

            if (string.Equals(eventName, "ProxyTimeout", StringComparison.Ordinal)) {
                long nowTicks = DateTime.Now.Ticks;
                long prevLast = Interlocked.Exchange(ref lastProxyTimeoutRestartTicks, nowTicks);
                if (prevLast > 0) Interlocked.Exchange(ref prevProxyTimeoutRestartTicks, prevLast);
            }

            bool clientRunning = true;
            try { clientRunning = IsAnyClientProcessRunning(); } catch { clientRunning = true; }
            if (!clientRunning) {
                // 用户主动退出/关闭客户端时，不干涉，不尝试拉起 Clash。
                manualClientInterventionRequired = false;
                try {
                    if (this.IsHandleCreated) {
                        this.BeginInvoke((Action)(() => {
                            statusLabel.Text = "● 状态: 等待 Clash...";
                            statusLabel.ForeColor = COLOR_WARNING;
                            try { if (trayIcon != null) trayIcon.Text = "等待 Clash"; } catch { /* ignore */ }
                        }));
                    }
                } catch { /* ignore */ }
                Log("重启被抑制: 未检测到 Clash 客户端");
                return;
            }

            Interlocked.Exchange(ref lastRestartTicks, DateTime.Now.Ticks);
            Log("重启: " + reason);
            Interlocked.Increment(ref totalRestarts);

            if (this.IsHandleCreated) {
                this.BeginInvoke((Action)(() => {
                    statusLabel.Text = "● 状态: 重启中...";
                    statusLabel.ForeColor = COLOR_WARNING;
                }));
            }

            // ===== 恢复管线：Kill core -> 等待恢复 -> (可选)刷新测速+切节点 -> 验证 =====
            bool coreBack = false;
            bool needRestartClient = forceRestartClient;

            if (!needRestartClient) {
                int attempts = requireGoodDelay ? HMHD_CORE_RESET_ATTEMPTS : 1;
                for (int i = 1; i <= attempts; i++) {
                    if (requireGoodDelay && attempts > 1) {
                        Log("快速重置内核: " + i + "/" + attempts);
                    }

                    KillCoreProcessesOnce();
                    coreBack = WaitForCoreBack(CORE_RECOVERY_MAX_WAIT_MS);

                    if (!coreBack) {
                        needRestartClient = true;
                        break;
                    }

                    // 每次重置内核后：刷新延迟历史 -> 切到当前可用低延迟节点
                    if (requireGoodDelay) {
                        TryRefreshDelayAndSwitchBestNode();
                        if (WaitForProxyRecoveryAndDelay(PROXY_RECOVERY_MAX_WAIT_MS, highDelayThreshold)) {
                            manualClientInterventionRequired = false;
                            Log("内核已自动恢复");
                            break;
                        }
                        if (i >= attempts) {
                            Log("内核已恢复但代理/延迟未恢复，升级为重启客户端");
                            needRestartClient = true;
                        }
                        continue;
                    }

                    // 常规恢复：只要代理恢复即可
                    if (WaitForProxyRecovery(PROXY_RECOVERY_MAX_WAIT_MS)) {
                        manualClientInterventionRequired = false;
                        Log("内核已自动恢复");
                    } else {
                        Log("内核已自动恢复但代理未恢复，升级为重启客户端");
                        needRestartClient = true;
                    }
                    break;
                }
            }

            if (!coreBack || needRestartClient) {
                if (!allowAutoStartClient) {
                    // 默认静音策略：禁止自动启动/重启客户端（避免弹出 UI 干扰用户）
                    manualClientInterventionRequired = true;

                    try {
                        long nowTicks2 = DateTime.Now.Ticks;
                        long lastTicks2 = Interlocked.Read(ref lastManualInterventionLogTicks);
                        if (lastTicks2 <= 0 || (new TimeSpan(nowTicks2 - lastTicks2)).TotalSeconds >= 60) {
                            Interlocked.Exchange(ref lastManualInterventionLogTicks, nowTicks2);
                            Log("需要手动重启 Clash 客户端 (allowAutoStartClient=false)");
                        }
                    } catch { /* ignore */ }

                    try {
                        if (this.IsHandleCreated) {
                            this.BeginInvoke((Action)(() => {
                                statusLabel.Text = "● 状态: 需要手动重启 Clash";
                                statusLabel.ForeColor = COLOR_ERROR;
                                try { if (trayIcon != null) trayIcon.Text = "需要手动重启 Clash"; } catch { /* ignore */ }
                            }));
                        }
                    } catch { /* ignore */ }
                } else {
                    manualClientInterventionRequired = false;

                    // ===== 客户端级恢复：尽量做到“手动退出重进”的效果 =====
                    if (needRestartClient) Log("强制重启客户端");
                    else Log("内核未自动恢复，重启客户端");

                    bool ok = RestartClientOnce(requireGoodDelay);
                    if (!ok) Log("客户端重启后代理/延迟仍未恢复");

                    // 客户端重启仍无效：先尝试检测可用节点并切换（最多 2 次）。
                    // 若无可用节点（无 delay 历史/无法测速），或切换两次仍不恢复，则进入订阅切换（如已启用）。
                    if (!ok) {
                        bool hasUsableNodes = true;
                        for (int i = 1; i <= 2; i++) {
                            if (!TryRefreshDelayAndSwitchBestNode()) {
                                hasUsableNodes = false;
                                break;
                            }
                            if (ValidateRecovery(requireGoodDelay)) { ok = true; break; }
                        }

                        if (!ok) {
                            string oldName, newName;
                            if (TrySwitchSubscriptionForRecovery(eventName, out oldName, out newName)) {
                                Log("强制重启客户端(订阅切换后生效)");
                                ok = RestartClientOnce(requireGoodDelay);
                                if (!ok) Log("订阅切换后代理/延迟仍未恢复");

                                // 切换订阅后再尝试切换节点（最多 2 次），给新订阅一次机会。
                                if (!ok) {
                                    for (int i = 1; i <= 2; i++) {
                                        if (!TryRefreshDelayAndSwitchBestNode()) break;
                                        if (ValidateRecovery(requireGoodDelay)) { ok = true; break; }
                                    }
                                }

                                if (!ok) {
                                    // 订阅切换 + 客户端重启 + 节点切换仍失败：避免无限循环打扰用户，降级为手动介入。
                                    manualClientInterventionRequired = true;
                                    Log("订阅切换后仍未恢复，建议手动检查 Clash/订阅");
                                }
                            } else if (!hasUsableNodes) {
                                // 已明确无可用节点且无法切换订阅：停止激进行为，避免无限循环打扰用户。
                                manualClientInterventionRequired = true;
                                Log("无可用节点且订阅不可切换，建议手动检查 Clash/订阅");
                            }

                            if (!ok && autoSwitchSubscription && allowAutoStartClient && hasUsableNodes) {
                                // 有节点可切换但仍未恢复：避免无限重启风暴，降级为需要手动介入。
                                manualClientInterventionRequired = true;
                                Log("多次恢复仍未成功，建议手动检查 Clash/订阅");
                            }
                        }
                    }
                }
            }

            // ===== 第 5 步：设置冷却期 =====
            if (this.IsHandleCreated) {
                this.BeginInvoke((Action)(() => {
                    failCount = 0;
                    consecutiveOK = 0;
                    cooldownCount = COOLDOWN_COUNT;
                    timer.Interval = effectiveNormalInterval;
                }));
            }
        } finally {
            _isRestarting = false;
        }
    }

    bool WaitForProxyRecovery(int maxWaitMs) {
        if (maxWaitMs <= 0) return false;
        int waited = 0;
        while (waited < maxWaitMs) {
            bool ok = false;
            try { TestProxy(out ok, true); } catch { ok = false; }
            if (ok) return true;

            int sleep = 1000;
            if (waited + sleep > maxWaitMs) sleep = maxWaitMs - waited;
            if (sleep <= 0) break;
            Thread.Sleep(sleep);
            waited += sleep;
        }
        return false;
    }

    bool WaitForProxyRecoveryAndDelay(int maxWaitMs, int maxDelayMs) {
        if (maxWaitMs <= 0) return false;
        int waited = 0;
        while (waited < maxWaitMs) {
            bool ok = false;
            int d = 0;
            try { d = TestProxy(out ok, true); } catch { ok = false; d = 0; }
            if (ok) {
                if (maxDelayMs <= 0) return true;
                if (d > 0 && d <= maxDelayMs) return true;
            }

            int sleep = 1000;
            if (waited + sleep > maxWaitMs) sleep = maxWaitMs - waited;
            if (sleep <= 0) break;
            Thread.Sleep(sleep);
            waited += sleep;
        }
        return false;
    }

    enum EmergencyAction {
        None,
        RestartClient,
        RestartCore,
        RediscoverApi,
        ClearBlacklistAndSwitch,
    }

    struct EmergencySnapshot {
        public string Trigger;
        public bool CoreRunning;
        public bool ApiOk;
        public bool ProxyOk;
        public bool ProxyPortOk;
        public double MemMB;
        public int Handles;
        public string SelectorGroup;
        public string Node;
    }

    bool IsLocalPortOpen(int port, int timeoutMs) {
        try {
            using (TcpClient c = new TcpClient()) {
                IAsyncResult ar = c.BeginConnect("127.0.0.1", port, null, null);
                WaitHandle wh = ar.AsyncWaitHandle;
                try {
                    if (!wh.WaitOne(timeoutMs)) return false;
                    try { c.EndConnect(ar); } catch { /* ignore */ }
                    return c.Connected;
                } finally {
                    try { wh.Close(); } catch { /* ignore */ }
                }
            }
        } catch { return false; }
    }

    EmergencySnapshot TakeEmergencySnapshot(string trigger) {
        EmergencySnapshot s = new EmergencySnapshot();
        s.Trigger = trigger ?? "";
        s.MemMB = 0;
        s.Handles = 0;
        s.SelectorGroup = "";
        s.Node = "";

        try {
            double mem; int handles;
            s.CoreRunning = GetMihomoStats(out mem, out handles);
            s.MemMB = mem;
            s.Handles = handles;
        } catch { s.CoreRunning = false; }

        try {
            string v = ApiRequest("/version", API_TIMEOUT_FAST);
            s.ApiOk = !string.IsNullOrEmpty(v);
        } catch { s.ApiOk = false; }

        try { s.ProxyPortOk = IsLocalPortOpen(proxyPort, 200); }
        catch { s.ProxyPortOk = false; }

        try {
            bool ok;
            TestProxy(out ok, true);
            s.ProxyOk = ok;
        } catch { s.ProxyOk = false; }

        try {
            string json = ApiRequest("/proxies", API_TIMEOUT_FAST);
            if (!string.IsNullOrEmpty(json)) {
                string group = FindSelectorGroup(json);
                s.SelectorGroup = group;
                string node = ResolveActualNode(json, string.IsNullOrEmpty(group) ? "GLOBAL" : group, 0);
                s.Node = node;
            }
        } catch { /* ignore */ }

        return s;
    }

    EmergencyAction DecideEmergencyAction(EmergencySnapshot s) {
        if (!s.CoreRunning) return EmergencyAction.None;

        // core 在但 proxyPort 不通：优先重启内核
        if (!s.ProxyPortOk) return EmergencyAction.RestartCore;

        // API 不可达：先尝试重新发现端口
        if (!s.ApiOk) return EmergencyAction.RediscoverApi;

        if (!s.ProxyOk) {
            // 连续重启仍不恢复：尝试清空黑名单+切节点（需要历史延迟）
            long last = Interlocked.Read(ref lastProxyTimeoutRestartTicks);
            long prev = Interlocked.Read(ref prevProxyTimeoutRestartTicks);
            if (last > 0 && prev > 0) {
                try {
                    if ((new TimeSpan(last - prev)).TotalMinutes <= 10) {
                        return EmergencyAction.ClearBlacklistAndSwitch;
                    }
                } catch { /* ignore */ }
            }

            // 刚发生过一次重启仍不恢复：升级为重启客户端
            long rt = Interlocked.Read(ref lastRestartTicks);
            if (rt > 0) {
                try {
                    if ((DateTime.Now - new DateTime(rt)).TotalMinutes < 2) return EmergencyAction.RestartClient;
                } catch { /* ignore */ }
            }
            return EmergencyAction.RestartCore;
        }

        return EmergencyAction.None;
    }

    void RunEmergency(string trigger) {
        if (_isRestarting) return;
        if (manualClientInterventionRequired) return;

        // 客户端不在时不干涉（避免误判导致启动/重启 Clash）
        bool clientRunning = true;
        try { clientRunning = IsAnyClientProcessRunning(); } catch { clientRunning = true; }
        if (!clientRunning) return;

        long nowTicks = DateTime.Now.Ticks;
        long last = Interlocked.Read(ref lastEmergencyTicks);
        try {
            if (last > 0 && (new TimeSpan(nowTicks - last)).TotalSeconds < 60) return;
        } catch { /* ignore */ }
        Interlocked.Exchange(ref lastEmergencyTicks, nowTicks);

        EmergencySnapshot s = TakeEmergencySnapshot(trigger);
        try {
            Log("应急: trigger=" + s.Trigger +
                " core=" + (s.CoreRunning ? 1 : 0) +
                " api=" + (s.ApiOk ? 1 : 0) +
                " proxyPort=" + (s.ProxyPortOk ? 1 : 0) +
                " proxyOK=" + (s.ProxyOk ? 1 : 0) +
                " group=" + SafeNodeName(s.SelectorGroup) +
                " node=" + SafeNodeName(s.Node));
        } catch { /* ignore */ }

        EmergencyAction act = DecideEmergencyAction(s);
        if (act == EmergencyAction.None) return;

        if (!allowAutoStartClient && act == EmergencyAction.RestartClient) {
            // 默认静音策略：禁止自动重启客户端（可能弹出 UI），降级为 core 级动作。
            act = EmergencyAction.RestartCore;
        }

        if (act == EmergencyAction.RediscoverApi) {
            try {
                AutoDiscoverApi();
                string v = ApiRequest("/version", API_TIMEOUT_FAST);
                if (!string.IsNullOrEmpty(v)) return;
            } catch { /* ignore */ }
            act = EmergencyAction.RestartClient;
        }

        if (!allowAutoStartClient && act == EmergencyAction.RestartClient) {
            act = EmergencyAction.RestartCore;
        }

        if (act == EmergencyAction.RestartClient) {
            ThreadPool.QueueUserWorkItem(_ => RestartClash("应急: 代理未恢复", true));
            return;
        }

        if (act == EmergencyAction.RestartCore) {
            ThreadPool.QueueUserWorkItem(_ => RestartClash("应急: 代理异常"));
            return;
        }

        if (act == EmergencyAction.ClearBlacklistAndSwitch) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    ClearBlacklist();
                    TriggerDelayTest();
                    if (SwitchToBestNode()) {
                        this.BeginInvoke((Action)(() => { failCount = 0; RefreshNodeDisplay(); }));
                    }
                } catch (Exception ex) {
                    Log("应急切换异常: " + ex.Message);
                }
            });
            return;
        }
    }

    bool StartClientProcess(string path) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo(path);
            try { psi.WorkingDirectory = Path.GetDirectoryName(path); } catch { /* ignore */ }
            psi.WindowStyle = ProcessWindowStyle.Minimized;
            psi.UseShellExecute = true;
            Process.Start(psi);
            Log("客户端已启动: " + path);
            return true;
        } catch (Exception ex) {
            Log("客户端启动失败: " + ex.Message);
            return false;
        }
    }

    // ==================== 检测循环 ====================
    void AdjustInterval(bool hasIssue) {
        if (hasIssue) {
            timer.Interval = effectiveFastInterval;
            consecutiveOK = 0;
        } else if (consecutiveOK >= CONSECUTIVE_OK_THRESHOLD && timer.Interval != effectiveNormalInterval) {
            timer.Interval = effectiveNormalInterval;
        }
    }

    void CheckStatus(object sender, EventArgs e) {
        if (_isDetectionPaused) return; // 暂停检测期间不运行检查
        if (_isRestarting) return; // 重启进行中，跳过本轮检测
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0) return;
        if (cooldownCount > 0) {
            cooldownCount--;
            ThreadPool.QueueUserWorkItem(_ => {
                try { DoCooldownCheck(); }
                catch (Exception ex) { Log("冷却检测异常: " + ex.Message); }
                finally { Interlocked.Exchange(ref _isChecking, 0); }
            });
        } else {
            ThreadPool.QueueUserWorkItem(_ => {
                try { DoCheckInBackground(); }
                catch (Exception ex) { Log("后台检测异常: " + ex.Message); }
                finally { Interlocked.Exchange(ref _isChecking, 0); }
            });
        }
    }

    void DoCooldownCheck() {
        if (_isDetectionPaused) return;
        Stopwatch sw = Stopwatch.StartNew();

        // 客户端不在时不干涉（避免误判导致启动/重启 Clash）
        bool clientRunning = true;
        try { clientRunning = IsAnyClientProcessRunning(); } catch { clientRunning = true; }
        if (!clientRunning) {
            manualClientInterventionRequired = false;
            cooldownProxyFailStreak = 0;
            try {
                if (this.IsHandleCreated) {
                    this.BeginInvoke((Action)(() => {
                        statusLabel.Text = "● 状态: 等待 Clash...";
                        statusLabel.ForeColor = COLOR_WARNING;
                        try { if (trayIcon != null) trayIcon.Text = "等待 Clash"; } catch { /* ignore */ }
                    }));
                }
            } catch { /* ignore */ }
            return;
        }
        double mem = 0; int handles = 0;
        bool running = GetMihomoStats(out mem, out handles);

        bool proxyOK = false;
        int delay = TestProxy(out proxyOK, true);

        // 冷却期：核心已恢复但代理仍不可用时，触发应急策略（节流在 RunEmergency 内）
        if (running && !proxyOK) {
            cooldownProxyFailStreak++;
            if (cooldownProxyFailStreak >= 2) {
                cooldownProxyFailStreak = 0;
                ThreadPool.QueueUserWorkItem(_ => RunEmergency("CooldownProxyFail"));
            }
        } else {
            cooldownProxyFailStreak = 0;
        }

        if (running && proxyOK) {
            manualClientInterventionRequired = false;
            // 内核已恢复，立即结束冷却，回到正常模式
            GetCurrentNode();
            this.BeginInvoke((Action)(() => {
                cooldownCount = 0; // 提前结束冷却
                string delayStr = delay > 0 ? delay + "ms" : "--";
                string coreShort = string.IsNullOrEmpty(detectedCoreName) ? "?" : detectedCoreName;
                memLabel.Text = "内  核:  " + coreShort + "  |  " + mem.ToString("F1") + "MB  |  句柄: " + handles;

                string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "--" : SafeNodeName(currentNode);
                proxyLabel.Text = "代  理:  OK " + delayStr + " | " + TruncateNodeName(nodeDisplay);
                proxyLabel.ForeColor = delay > highDelayThreshold ? COLOR_WARNING : COLOR_OK;

                statusLabel.Text = "● 状态: 运行中";
                statusLabel.ForeColor = COLOR_OK;

                lastStableTime = DateTime.Now;
                consecutiveOK++;
            }));
        } else {
            this.BeginInvoke((Action)(() => {
                statusLabel.Text = "● 状态: 等待内核...";
                statusLabel.ForeColor = COLOR_WARNING;
            }));
        }

        LogPerf("DoCooldownCheck", sw.ElapsedMilliseconds);
    }

    void DoCheckInBackground() {
        if (_isDetectionPaused) return;
        Stopwatch sw = Stopwatch.StartNew();

        // 客户端不在时不干涉（避免误判导致启动/重启 Clash）
        bool clientRunning = true;
        try { clientRunning = IsAnyClientProcessRunning(); } catch { clientRunning = true; }
        if (!clientRunning) {
            manualClientInterventionRequired = false;
            cooldownProxyFailStreak = 0;
            try {
                if (this.IsHandleCreated) {
                    this.BeginInvoke((Action)(() => {
                        statusLabel.Text = "● 状态: 等待 Clash...";
                        statusLabel.ForeColor = COLOR_WARNING;
                        try { if (trayIcon != null) trayIcon.Text = "等待 Clash"; } catch { /* ignore */ }
                    }));
                }
            } catch { /* ignore */ }
            return;
        }

        Interlocked.Increment(ref totalChecks);

        double mem = 0; int handles = 0;
        bool running = GetMihomoStats(out mem, out handles);

        bool proxyOK = false;
        int delay = TestProxy(out proxyOK, true);
        LogPerf("TestProxy", sw.ElapsedMilliseconds);

        DateTime now = DateTime.Now;

        int[] tcp = lastTcpStats;
        if ((now - lastTcpStatsAt).TotalSeconds >= 50) { tcp = GetTcpStats(); lastTcpStats = tcp; lastTcpStatsAt = now; }

        int nodeRefreshMs = (!running || !proxyOK || (delay > highDelayThreshold)) ? 1000 : 5000;
        if ((now - lastNodeRefreshAt).TotalMilliseconds >= nodeRefreshMs) { GetCurrentNode(API_TIMEOUT_FAST); lastNodeRefreshAt = now; }

        if ((now - lastDelayTestAt).TotalMinutes >= 6) { TriggerDelayTest(); lastDelayTestAt = now; }

        this.BeginInvoke((Action)(() => UpdateUI(running, mem, handles, proxyOK, delay, tcp)));
        LogPerf("DoCheckInBackground", sw.ElapsedMilliseconds);
    }

    // ==================== UI 更新（仅渲染，不含业务判断） ====================
    void UpdateUI(bool running, double mem, int handles, bool proxyOK, int delay, int[] tcp) {
        if (_isDetectionPaused) return;
        int tw = tcp[0], est = tcp[1], cw = tcp[2];
        int dl = delay;

        // paused detection: no UI update here (handled by PauseDetectionUi)

        // 纯逻辑决策（不修改任何 UI 或实例状态）
        StatusDecision decision = EvaluateStatus(running, mem, proxyOK, dl, cw, failCount);

        // 应用状态变更（全部在 UI 线程，线程安全）
        failCount = decision.NewFailCount;
        if (decision.IncrementTotalFails) totalFails++;
        if (decision.ResetConsecutiveOK) consecutiveOK = 0;
        else if (decision.IncrementConsecutiveOK) consecutiveOK++;
        if (decision.ResetStableTime) lastStableTime = DateTime.Now;

        // “问题段落次数”：从正常->异常记 1 次，异常持续期间不累加
        if (decision.HasIssue && !lastHadIssue) totalIssues++;
        lastHadIssue = decision.HasIssue;

        // 健康时清零“连续成功切换节点次数”（用于订阅级自动切换触发）
        if (running && proxyOK && dl > 0 && dl <= highDelayThreshold) {
            consecutiveSuccessfulAutoSwitchesWithoutRecovery = 0;
        }

        // 渲染 UI
        string delayStr = dl > 0 ? dl + "ms" : "--";
        string coreShort = string.IsNullOrEmpty(detectedCoreName) ? "未检测" : detectedCoreName;
        memLabel.Text = "内  核:  " + coreShort + "  |  " + mem.ToString("F1") + "MB" + (mem > memoryWarning ? "!" : "") + "  |  句柄: " + handles;
        string nodeDisplay = string.IsNullOrEmpty(currentNode) ? "获取中..." : SafeNodeName(currentNode);
        proxyLabel.Text = "代  理:  " + (proxyOK ? "OK" : "X") + " " + delayStr + " | " + TruncateNodeName(nodeDisplay);
        proxyLabel.ForeColor = !proxyOK ? COLOR_ERROR : (dl > highDelayThreshold ? COLOR_WARNING : COLOR_OK);
        int blCount;
        lock (blacklistLock) { blCount = nodeBlacklist.Count; }
        checkLabel.Text = "统  计:  问题 " + totalIssues + "  |  重启 " + totalRestarts + "  |  切换 " + totalSwitches + "  |  黑名单 " + blCount;

        TimeSpan stableTime = DateTime.Now - lastStableTime;
        TimeSpan runTime = DateTime.Now - startTime;
        stableLabel.Text = "稳定性:  连续 " + FormatTimeSpan(stableTime) + "  |  运行 " + FormatTimeSpan(runTime) + "  |  问题 " + totalIssues;

        // 状态指示
        string cn = currentNode;
        if (!running) {
            statusLabel.Text = "● 状态: 内核未运行";
            statusLabel.ForeColor = COLOR_ERROR;
        } else if (!proxyOK) {
            statusLabel.Text = "● 状态: 代理异常(F" + failCount + ")";
            statusLabel.ForeColor = COLOR_ERROR;
        } else {
            statusLabel.Text = "● 状态: 运行中";
            statusLabel.ForeColor = COLOR_OK;
        }

        if (running && proxyOK) {
            manualClientInterventionRequired = false;
        }

        // 托盘
        string coreDisplay = string.IsNullOrEmpty(detectedCoreName) ? "?" : detectedCoreName;
        string trayText = coreDisplay + " | " + mem.ToString("F0") + "MB | " + (proxyOK ? delayStr : "!");
        if (trayText.Length > 63) trayText = trayText.Substring(0, 63);
        trayIcon.Text = trayText;

        // 调整检测频率（需要手动介入时保持普通频率，避免激进行为打扰用户）
        if (manualClientInterventionRequired) {
            try { timer.Interval = effectiveNormalInterval; } catch { /* ignore */ }
        } else {
            AdjustInterval(decision.HasIssue);
        }

        // 记录数据
        LogData(proxyOK, dl, mem, handles, tw, est, cw, cn, decision.Event);

        if (manualClientInterventionRequired) {
            statusLabel.Text = "● 状态: 需要手动重启 Clash";
            statusLabel.ForeColor = COLOR_ERROR;
            try { trayIcon.Text = "需要手动重启 Clash"; } catch { /* ignore */ }
            return;
        }

        // 订阅级自动切换（Clash Verge Rev）：连续成功切换多个节点仍不可用，则切换订阅并强制重启客户端
        if (autoSwitchSubscription) {
            bool whitelistOk = subscriptionWhitelist != null && subscriptionWhitelist.Length >= 2;
            bool networkBad = running && mem <= memoryWarning && (!proxyOK || (dl > 0 && dl > highDelayThreshold));
            DateTime lastSub = new DateTime(Interlocked.Read(ref lastSubscriptionSwitchTicks));
            bool cooldownOk = (DateTime.Now - lastSub).TotalMinutes >= subscriptionSwitchCooldownMinutes;
            bool switchingOk = !_isRestarting && !_isSwitchingSubscription;

            if (switchingOk && whitelistOk && cooldownOk && networkBad &&
                consecutiveSuccessfulAutoSwitchesWithoutRecovery >= subscriptionSwitchThreshold) {

                int switches = consecutiveSuccessfulAutoSwitchesWithoutRecovery;
                consecutiveSuccessfulAutoSwitchesWithoutRecovery = 0;
                ThreadPool.QueueUserWorkItem(_ => SwitchSubscriptionAndRestart(decision.Event, switches));
                return;
            }
        }

        // 执行决策（在后台线程，避免阻塞 UI）
        // 注意：若需要重启，则不并行触发切节点，避免重启与切换并发导致竞态。
        if (decision.NeedRestart) {
            bool clientRunning = true;
            try { clientRunning = IsAnyClientProcessRunning(); } catch { clientRunning = true; }
            if (!clientRunning) return;
            ThreadPool.QueueUserWorkItem(_ => RestartClash(decision.Reason, false, decision.Event));
            return;
        }
        if (decision.NeedSwitch) {
            bool clientRunning = true;
            try { clientRunning = IsAnyClientProcessRunning(); } catch { clientRunning = true; }
            if (!clientRunning) return;
            QueueSwitchToBestNode(true);
        }
    }

    void QueueSwitchToBestNode(bool isAuto) {
        ThreadPool.QueueUserWorkItem(_ => {
            try {
                if (SwitchToBestNode()) {
                    this.BeginInvoke((Action)(() => {
                        failCount = 0;
                        RefreshNodeDisplay();
                        if (isAuto) consecutiveSuccessfulAutoSwitchesWithoutRecovery++;
                    }));
                }
            } catch (Exception ex) {
                Log("切换节点异常: " + ex.Message);
            }
        });
    }

    void SwitchSubscriptionAndRestart(string reasonEvent, int switches) {
        lock (subscriptionLock) {
            if (_isSwitchingSubscription) return;
            if (_isRestarting) return;
            _isSwitchingSubscription = true;
        }

        try {
            if (!autoSwitchSubscription) return;
            if (subscriptionWhitelist == null || subscriptionWhitelist.Length < 2) return;

            DateTime lastSub = new DateTime(Interlocked.Read(ref lastSubscriptionSwitchTicks));
            if ((DateTime.Now - lastSub).TotalMinutes < subscriptionSwitchCooldownMinutes) return;

            string oldName, newName;
            if (!TrySwitchClashVergeRevSubscription(subscriptionWhitelist, out oldName, out newName)) {
                Log("订阅切换失败: 未找到可切换的订阅或 profiles.yaml 不可用");
                return;
            }

            Interlocked.Exchange(ref lastSubscriptionSwitchTicks, DateTime.Now.Ticks);
            Log("订阅切换: " + oldName + " -> " + newName + " (reason=" + reasonEvent + ", switches=" + switches + ")");

            // 强制重启客户端以生效（不依赖内核自愈）
            RestartClash("订阅切换: " + oldName + " -> " + newName, true);
        } catch (Exception ex) {
            Log("订阅切换异常: " + ex.Message);
        } finally {
            _isSwitchingSubscription = false;
        }
    }

    bool TrySwitchClashVergeRevSubscription(string[] whitelist, out string oldName, out string newName) {
        oldName = "";
        newName = "";

        try {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData)) return false;
            string profilesFile = Path.Combine(appData, "io.github.clash-verge-rev.clash-verge-rev", "profiles.yaml");
            if (!File.Exists(profilesFile)) return false;

            string text = File.ReadAllText(profilesFile, Encoding.UTF8);
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

            string currentUid = "";
            List<string> remoteUids = new List<string>();
            List<string> remoteNames = new List<string>();

            string uid = null;
            string type = null;
            string name = null;

            Action flush = delegate {
                if (string.IsNullOrEmpty(uid)) return;
                if (string.Equals(type, "remote", StringComparison.OrdinalIgnoreCase)) {
                    remoteUids.Add(uid);
                    remoteNames.Add(string.IsNullOrEmpty(name) ? uid : name);
                }
                uid = null; type = null; name = null;
            };

            for (int i = 0; i < lines.Length; i++) {
                string t = lines[i].Trim();
                if (t.StartsWith("current:", StringComparison.OrdinalIgnoreCase)) {
                    currentUid = t.Substring("current:".Length).Trim();
                    continue;
                }

                if (t.StartsWith("- uid:", StringComparison.OrdinalIgnoreCase)) {
                    flush();
                    uid = t.Substring("- uid:".Length).Trim();
                    continue;
                }

                if (uid != null) {
                    if (t.StartsWith("type:", StringComparison.OrdinalIgnoreCase)) {
                        type = t.Substring("type:".Length).Trim();
                    } else if (t.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) {
                        name = t.Substring("name:".Length).Trim();
                        if (string.Equals(name, "null", StringComparison.OrdinalIgnoreCase)) name = "";
                        if (name.StartsWith("\"") && name.EndsWith("\"") && name.Length >= 2) name = name.Substring(1, name.Length - 2);
                        if (name.StartsWith("'") && name.EndsWith("'") && name.Length >= 2) name = name.Substring(1, name.Length - 2);
                    }
                }
            }
            flush();

            if (remoteUids.Count == 0) return false;

            List<int> candidates = new List<int>();
            for (int i = 0; i < remoteUids.Count; i++) {
                if (IsWhitelisted(remoteUids[i], remoteNames[i], whitelist)) candidates.Add(i);
            }
            if (candidates.Count < 2) return false;

            int curCandidatePos = -1;
            for (int i = 0; i < candidates.Count; i++) {
                int ridx = candidates[i];
                if (string.Equals(remoteUids[ridx], currentUid, StringComparison.OrdinalIgnoreCase)) { curCandidatePos = i; break; }
            }

            int nextPos = curCandidatePos >= 0 ? (curCandidatePos + 1) % candidates.Count : 0;
            int nextIdx = candidates[nextPos];

            // oldName: 尝试从列表里找到 current 对应名称
            oldName = currentUid;
            for (int i = 0; i < remoteUids.Count; i++) {
                if (string.Equals(remoteUids[i], currentUid, StringComparison.OrdinalIgnoreCase)) {
                    oldName = string.IsNullOrEmpty(remoteNames[i]) ? remoteUids[i] : remoteNames[i];
                    break;
                }
            }

            string newUid = remoteUids[nextIdx];
            newName = string.IsNullOrEmpty(remoteNames[nextIdx]) ? newUid : remoteNames[nextIdx];

            // 修改 current: 行
            bool replaced = false;
            for (int i = 0; i < lines.Length; i++) {
                string raw = lines[i];
                string t = raw.TrimStart();
                if (t.StartsWith("current:", StringComparison.OrdinalIgnoreCase)) {
                    int pos = raw.IndexOf("current:", StringComparison.OrdinalIgnoreCase);
                    string indent = pos > 0 ? raw.Substring(0, pos) : "";
                    lines[i] = indent + "current: " + newUid;
                    replaced = true;
                    break;
                }
            }
            if (!replaced) return false;

            string newText = string.Join(nl, lines);
            string tmp = profilesFile + ".tmp";
            File.WriteAllText(tmp, newText, Encoding.UTF8);
            File.Copy(tmp, profilesFile, true);
            try { File.Delete(tmp); } catch { /* ignore */ }

            return true;
        } catch {
            return false;
        }
    }

    static bool IsWhitelisted(string uid, string name, string[] whitelist) {
        if (whitelist == null || whitelist.Length == 0) return false;
        foreach (string w in whitelist) {
            if (string.IsNullOrEmpty(w)) continue;
            if (!string.IsNullOrEmpty(uid) && string.Equals(uid, w, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(name) && string.Equals(name, w, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ==================== 纯决策逻辑（无副作用，可独立测试） ====================
    StatusDecision EvaluateStatus(bool running, double mem, bool proxyOK, int delay, int cw, int currentFailCount) {
        StatusDecision d = new StatusDecision();
        d.NewFailCount = currentFailCount;
        d.Event = "";
        d.Reason = "";

        if (!running) {
            d.NeedRestart = true;
            d.Reason = "进程不存在";
            d.Event = "ProcessDown";
            d.HasIssue = true;
        }
        else if (mem > memoryThreshold) {
            d.NeedRestart = true;
            d.Reason = "内存过高" + mem.ToString("F0") + "MB";
            d.Event = "CriticalMemory";
            d.HasIssue = true;
        }
        else if (mem > memoryWarning && !proxyOK) {
            d.NeedRestart = true;
            d.Reason = "内存高+无响应";
            d.Event = "HighMemoryNoProxy";
            d.HasIssue = true;
        }
        else if (mem > memoryWarning && proxyOK && delay > highDelayThreshold) {
            // 从第一次检测到“内存高+高延迟”就触发快速恢复管线（重置内核->刷新测速->切节点...）
            d.NeedRestart = true;
            d.Reason = "内存高+延迟过高" + delay + "ms";
            d.Event = "HighMemoryHighDelay";
            d.HasIssue = true;
            d.ResetConsecutiveOK = true;
            d.ResetStableTime = true;
        }
        else if (cw > CLOSE_WAIT_THRESHOLD && !proxyOK) {
            d.NeedRestart = true;
            d.Reason = "连接泄漏+无响应";
            d.Event = "CloseWaitLeak";
            d.HasIssue = true;
        }
        else if (!proxyOK) {
            d.NewFailCount++;
            d.IncrementTotalFails = true;
            d.ResetStableTime = true;
            d.ResetConsecutiveOK = true;
            d.Event = "ProxyFail";
            d.HasIssue = true;
            if (d.NewFailCount == 2) { d.NeedSwitch = true; d.Reason = "节点无响应"; d.Event = "NodeSwitch"; }
            else if (d.NewFailCount >= 4) { d.NeedRestart = true; d.Reason = "连续无响应"; d.Event = "ProxyTimeout"; }
        }
        else if (delay > highDelayThreshold) {
            d.NewFailCount++;
            d.Event = "HighDelay";
            d.HasIssue = true;
            if (d.NewFailCount >= 2) { d.NeedSwitch = true; d.Reason = "延迟过高" + delay + "ms"; d.Event = "HighDelaySwitch"; d.NewFailCount = 0; }
        }
        else {
            d.NewFailCount = 0;
            d.IncrementConsecutiveOK = true;
            if (mem > memoryWarning) d.Event = "HighMemoryOK";
        }

        return d;
    }
}
