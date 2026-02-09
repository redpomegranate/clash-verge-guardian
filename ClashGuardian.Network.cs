using System;
using System.Net;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

/// <summary>
/// API 通信、JSON 解析、节点管理、代理测试
/// </summary>
public partial class ClashGuardian
{
    // ==================== API 通信 ====================
    string ApiRequest(string path, int timeout = API_TIMEOUT_NORMAL) {
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + path) as HttpWebRequest;
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            // 本地 API 不应走系统代理（避免 PAC/全局代理导致 127.0.0.1 被代理转发）
            try { if (req.RequestUri != null && req.RequestUri.IsLoopback) req.Proxy = null; } catch { /* 忽略 */ }
            using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse)
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                return reader.ReadToEnd();
            }
        } catch { return null; /* API 超时/不可达属正常探测场景 */ }
    }

    bool ApiPut(string path, string body) {
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + path) as HttpWebRequest;
            req.Method = "PUT";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = API_TIMEOUT_NORMAL;
            // 本地 API 不应走系统代理（避免 PAC/全局代理导致 127.0.0.1 被代理转发）
            try { if (req.RequestUri != null && req.RequestUri.IsLoopback) req.Proxy = null; } catch { /* 忽略 */ }
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
                using (HttpWebResponse errResp = wex.Response as HttpWebResponse)
                using (StreamReader reader = new StreamReader(errResp.GetResponseStream())) {
                    Log("API错误: " + (int)errResp.StatusCode + " " + reader.ReadToEnd());
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

    // ==================== JSON 解析工具（统一入口，消除重复） ====================

    /// <summary>
    /// 查找 JSON 中命名对象的边界: "name":{...}
    /// 返回 true 表示找到，objStart 指向 '{'，objEnd 指向 '}' 之后
    /// </summary>
    bool FindObjectBounds(string json, string name, out int objStart, out int objEnd) {
        objStart = 0; objEnd = 0;
        string search = "\"" + name + "\":{";
        int idx = json.IndexOf(search);
        if (idx < 0) {
            search = "\"" + name + "\": {";
            idx = json.IndexOf(search);
        }
        if (idx < 0) return false;

        objStart = idx + search.Length - 1;
        int braceCount = 1;
        objEnd = objStart + 1;
        bool inString = false;
        bool escape = false;
        while (objEnd < json.Length) {
            char c = json[objEnd];
            if (inString) {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
            } else {
                if (c == '"') inString = true;
                else if (c == '{') braceCount++;
                else if (c == '}') braceCount--;
                if (braceCount == 0) { objEnd++; return true; }
            }
            objEnd++;
        }
        return false;
    }

    /// <summary>
    /// 在已知对象范围内查找字符串字段值（处理 "key":"value" 和 "key": "value" 两种格式）
    /// </summary>
    string FindFieldValue(string json, int objStart, int objEnd, string fieldName) {
        string search1 = "\"" + fieldName + "\":\"";
        int idx = json.IndexOf(search1, objStart);
        if (idx >= 0 && idx < objEnd) {
            return ExtractJsonStringAt(json, idx + search1.Length);
        }
        string search2 = "\"" + fieldName + "\": \"";
        idx = json.IndexOf(search2, objStart);
        if (idx >= 0 && idx < objEnd) {
            return ExtractJsonStringAt(json, idx + search2.Length);
        }
        return "";
    }

    string FindProxyNow(string json, string proxyName) {
        int objStart, objEnd;
        if (!FindObjectBounds(json, proxyName, out objStart, out objEnd)) return "";
        return FindFieldValue(json, objStart, objEnd, "now");
    }

    string FindProxyType(string json, string proxyName) {
        int objStart, objEnd;
        if (!FindObjectBounds(json, proxyName, out objStart, out objEnd)) return "";
        return FindFieldValue(json, objStart, objEnd, "type");
    }

    // ==================== JSON 字符串提取（处理 Unicode 转义） ====================
    string ExtractJsonString(string json, string key) {
        string search = "\"" + key + "\":\"";
        int start = json.IndexOf(search);
        if (start < 0) return "";
        return ExtractJsonStringAt(json, start + search.Length);
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
            if (char.IsHighSurrogate(c)) {
                if (i + 1 < name.Length && char.IsLowSurrogate(name[i + 1])) i++;
                continue;
            }
            if (char.IsLowSurrogate(c)) continue;
            if ((c >= 0x20 && c <= 0x7E) ||
                (c >= 0x4E00 && c <= 0x9FFF) ||
                (c >= 0x3040 && c <= 0x30FF) ||
                (c >= 0xAC00 && c <= 0xD7AF) ||
                (c >= 0x2000 && c <= 0x206F) ||
                (c >= 0xFF00 && c <= 0xFFEF)) {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    // ==================== 节点管理 ====================
    private static readonly string[] SELECTOR_NAMES = new string[] {
        "GLOBAL", "节点选择", "Proxy", "代理模式", "手动切换", "Select", "🚀 节点选择"
    };

    private static readonly string[] SKIP_GROUPS = new string[] {
        "DIRECT", "REJECT", "GLOBAL", "Proxy", "节点选择", "代理模式",
        "手动切换", "Select", "自动选择", "故障转移", "负载均衡",
        "🚀 节点选择", "♻️ 自动选择", "🎯 全球直连", "🛑 全球拦截"
    };

    // Used to rotate live delay probes across large node sets (avoid probing the same few nodes every time).
    int delayProbeCursor = 0;

    void GetCurrentNode(int timeout = API_TIMEOUT_NORMAL) {
        try {
            string json = ApiRequest("/proxies", timeout);
            if (string.IsNullOrEmpty(json)) return;

            // Prefer the actual selector group discovered from GLOBAL's "all" list,
            // so manual switches and nested selectors can be reflected correctly.
            string group = FindSelectorGroup(json);
            if (!string.IsNullOrEmpty(group)) nodeGroup = group;

            string node = ResolveActualNode(json, string.IsNullOrEmpty(group) ? "GLOBAL" : group, 0);
            if (string.IsNullOrEmpty(node) && group != "GLOBAL") {
                node = ResolveActualNode(json, "GLOBAL", 0);
            }
            if (!string.IsNullOrEmpty(node)) {
                currentNode = node; // keep raw; UI will SafeNodeName() for display
                return;
            }

            // Fallback: try common selector names
            foreach (string selector in SELECTOR_NAMES) {
                if (selector == "GLOBAL") continue;
                node = ResolveActualNode(json, selector, 0);
                if (!string.IsNullOrEmpty(node)) {
                    nodeGroup = selector;
                    currentNode = node;
                    return;
                }
            }
        } catch (Exception ex) { Log("节点获取异常: " + ex.Message); }
    }

    string ResolveActualNode(string json, string proxyName, int depth) {
        if (depth > MAX_RECURSE_DEPTH) return proxyName;

        string nowValue = FindProxyNow(json, proxyName);
        if (string.IsNullOrEmpty(nowValue)) return "";

        bool isGroup = false;
        foreach (string skip in SKIP_GROUPS) {
            if (nowValue == skip || nowValue.Contains(skip)) { isGroup = true; break; }
        }

        string proxyType = FindProxyType(json, nowValue);

        if (proxyType == "Selector" || proxyType == "URLTest" ||
            proxyType == "Fallback" || proxyType == "LoadBalance") {
            return ResolveActualNode(json, nowValue, depth + 1);
        }

        if (!isGroup && !string.IsNullOrEmpty(proxyType)) return nowValue;
        if (!isGroup) return nowValue;

        return ResolveActualNode(json, nowValue, depth + 1);
    }

    List<string> GetGroupAllNodes(string json, string groupName) {
        List<string> nodes = new List<string>();
        int objStart, objEnd;
        if (!FindObjectBounds(json, groupName, out objStart, out objEnd)) return nodes;

        int allIdx = json.IndexOf("\"all\":[", objStart);
        if (allIdx < 0 || allIdx >= objEnd) return nodes;
        int arrStart = allIdx + 6;
        int arrEnd = json.IndexOf("]", arrStart);
        if (arrEnd < 0) return nodes;

        string arrStr = json.Substring(arrStart, arrEnd - arrStart);
        int pos = 0;
        while (pos < arrStr.Length) {
            int qStart = arrStr.IndexOf('"', pos);
            if (qStart < 0) break;
            string name = ExtractJsonStringAt(arrStr, qStart + 1);
            if (!string.IsNullOrEmpty(name)) nodes.Add(name);
            int qEnd = qStart + 1;
            while (qEnd < arrStr.Length) {
                if (arrStr[qEnd] == '"' && arrStr[qEnd - 1] != '\\') break;
                qEnd++;
            }
            pos = qEnd + 1;
        }
        return nodes;
    }

    int GetNodeDelay(string json, string nodeName) {
        int objStart, objEnd;
        if (!FindObjectBounds(json, nodeName, out objStart, out objEnd)) return 0;

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

    string FindSelectorGroup(string json) {
        List<string> globalAll = GetGroupAllNodes(json, "GLOBAL");
        foreach (string entry in globalAll) {
            string t = FindProxyType(json, entry);
            if (t == "Selector" || t == "URLTest" || t == "Fallback") {
                return entry;
            }
        }
        return "GLOBAL";
    }

    // ==================== 节点切换 ====================
    void CleanBlacklist() {
        lock (blacklistLock) {
            List<string> toRemove = new List<string>();
            DateTime now = DateTime.Now;
            foreach (var kv in nodeBlacklist) {
                if ((now - kv.Value).TotalMinutes > blacklistMinutes) toRemove.Add(kv.Key);
            }
            foreach (string key in toRemove) nodeBlacklist.Remove(key);
        }
    }

    void ClearBlacklist() {
        lock (blacklistLock) { nodeBlacklist.Clear(); }
    }

    bool RemoveCurrentNodeFromBlacklist() {
        string cn = currentNode; // volatile read
        if (string.IsNullOrEmpty(cn)) return false;
        lock (blacklistLock) { return nodeBlacklist.Remove(cn); }
    }

    // 获取 selector 组里可供手动禁用/启用的节点候选（过滤策略组与非节点类型）
    List<string> GetCandidateNodesFromProxiesJson(string json, string selectorGroup) {
        List<string> allNodes = GetGroupAllNodes(json, selectorGroup);
        List<string> candidates = new List<string>();
        string[] skipTypes = new string[] { "Selector", "URLTest", "Fallback", "LoadBalance", "Direct", "Reject" };

        foreach (string nodeName in allNodes) {
            if (string.IsNullOrEmpty(nodeName) || nodeName.Length > MAX_NODE_NAME_LENGTH) continue;

            bool skip = false;
            foreach (string sg in SKIP_GROUPS) { if (nodeName == sg) { skip = true; break; } }
            if (skip) continue;

            string nodeType = FindProxyType(json, nodeName);
            foreach (string st in skipTypes) { if (nodeType == st) { skip = true; break; } }
            if (skip) continue;

            candidates.Add(nodeName);
        }

        return candidates;
    }

    bool HasAnyDelayHistoryInGroup(string proxiesJson, string selectorGroup) {
        if (string.IsNullOrEmpty(proxiesJson) || string.IsNullOrEmpty(selectorGroup)) return false;
        try {
            List<string> allNodes = GetGroupAllNodes(proxiesJson, selectorGroup);
            if (allNodes == null || allNodes.Count == 0) return false;
            string[] skipTypes = new string[] { "Selector", "URLTest", "Fallback", "LoadBalance", "Direct", "Reject" };
            foreach (string nodeName in allNodes) {
                if (string.IsNullOrEmpty(nodeName) || nodeName.Length > MAX_NODE_NAME_LENGTH) continue;

                bool skip = false;
                foreach (string sg in SKIP_GROUPS) { if (nodeName == sg) { skip = true; break; } }
                if (skip) continue;

                string nodeType = FindProxyType(proxiesJson, nodeName);
                foreach (string st in skipTypes) { if (nodeType == st) { skip = true; break; } }
                if (skip) continue;

                int delay = GetNodeDelay(proxiesJson, nodeName);
                if (delay > 0) return true;
            }
        } catch { /* ignore */ }
        return false;
    }

    string RefreshProxiesDelayHistory(string selectorGroup, int maxWaitMs) {
        // Trigger delay test, then poll /proxies until some history.delay becomes available.
        if (maxWaitMs <= 0) maxWaitMs = DELAY_REFRESH_MAX_WAIT_MS;
        try {
            nodeGroup = selectorGroup;
            TriggerDelayTest();

            Stopwatch sw = Stopwatch.StartNew();
            string lastJson = null;
            while (sw.ElapsedMilliseconds < maxWaitMs) {
                Thread.Sleep(500);
                lastJson = ApiRequest("/proxies", API_TIMEOUT_FAST);
                if (string.IsNullOrEmpty(lastJson)) continue;

                // Group may change after restart; keep it fresh.
                string g2 = FindSelectorGroup(lastJson);
                if (!string.IsNullOrEmpty(g2)) {
                    selectorGroup = g2;
                    nodeGroup = g2;
                }

                if (HasAnyDelayHistoryInGroup(lastJson, selectorGroup)) return lastJson;
            }
            return lastJson;
        } catch {
            return null;
        }
    }

    int ParseDelayValue(string json) {
        if (string.IsNullOrEmpty(json)) return 0;
        int idx = json.IndexOf("\"delay\":", StringComparison.Ordinal);
        if (idx < 0) return 0;
        int start = idx + 8;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
        int end = start;
        while (end < json.Length && json[end] >= '0' && json[end] <= '9') end++;
        if (end <= start) return 0;
        int d;
        if (int.TryParse(json.Substring(start, end - start), out d) && d > 0) return d;
        return 0;
    }

    int GetProxyDelayLive(string proxyName, int timeoutMs) {
        if (string.IsNullOrEmpty(proxyName)) return 0;
        if (timeoutMs <= 0) timeoutMs = 5000;

        // mihomo/meta portable delay endpoint: /proxies/{name}/delay
        string urlEnc = Uri.EscapeDataString("http://www.gstatic.com/generate_204");
        string path = "/proxies/" + Uri.EscapeDataString(proxyName) + "/delay?url=" + urlEnc + "&timeout=" + timeoutMs;
        int apiTimeout = Math.Max(2000, timeoutMs + 1500);
        string json = ApiRequest(path, apiTimeout);
        return ParseDelayValue(json);
    }

    Dictionary<string, int> ProbeDelaysLive(List<string> nodes, int timeoutMs, int maxConcurrency) {
        Dictionary<string, int> result = new Dictionary<string, int>();
        if (nodes == null || nodes.Count == 0) return result;
        if (timeoutMs <= 0) timeoutMs = 5000;
        if (maxConcurrency <= 0) maxConcurrency = 4;
        if (maxConcurrency > nodes.Count) maxConcurrency = nodes.Count;
        if (maxConcurrency < 1) maxConcurrency = 1;

        object lockObj = new object();
        using (SemaphoreSlim sem = new SemaphoreSlim(maxConcurrency, maxConcurrency))
        using (CountdownEvent done = new CountdownEvent(nodes.Count)) {
            foreach (string node in nodes) {
                ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        sem.Wait();
                        int d = GetProxyDelayLive(node, timeoutMs);
                        if (d > 0) {
                            lock (lockObj) { result[node] = d; }
                        }
                    } catch { /* ignore */ }
                    finally {
                        try { sem.Release(); } catch { /* ignore */ }
                        try { done.Signal(); } catch { /* ignore */ }
                    }
                });
            }

            // Wait for all delay probes to finish; API timeouts bound the total wait time.
            try { done.Wait(); } catch { /* ignore */ }
        }

        return result;
    }

    bool SwitchToBestNodeRefreshed() {
        return SwitchToBestNodeInternal(true);
    }

    bool SwitchToBestNode() {
        return SwitchToBestNodeInternal(false);
    }

    bool SwitchToBestNodeInternal(bool forceDelayRefresh) {
        CleanBlacklist();
        try {
            string json = ApiRequest("/proxies");
            if (string.IsNullOrEmpty(json)) {
                Log("切换失败: API无响应");
                return false;
            }

            string group = FindSelectorGroup(json);
            nodeGroup = group;

            if (forceDelayRefresh) {
                // Seed delay info quickly; group delay endpoints are not portable across cores.
                try { nodeGroup = group; TriggerDelayTest(); } catch { /* ignore */ }
                try { Thread.Sleep(500); } catch { /* ignore */ }
                try {
                    string refreshed = ApiRequest("/proxies", API_TIMEOUT_FAST);
                    if (!string.IsNullOrEmpty(refreshed)) {
                        json = refreshed;
                        group = FindSelectorGroup(json);
                        nodeGroup = group;
                    }
                } catch { /* ignore */ }
            }

            for (;;) {
                List<string> allNodes = GetGroupAllNodes(json, group);

                HashSet<string> preferredSnapshot = null;
                lock (preferredNodesLock) {
                    if (preferredNodes != null && preferredNodes.Count > 0) {
                        preferredSnapshot = new HashSet<string>(preferredNodes);
                    }
                }

                List<KeyValuePair<string, int>> preferredWithDelay = new List<KeyValuePair<string, int>>();
                List<KeyValuePair<string, int>> normalWithDelay = new List<KeyValuePair<string, int>>();
                List<string> preferredCandidates = new List<string>();
                List<string> normalCandidates = new List<string>();
                string[] skipTypes = new string[] { "Selector", "URLTest", "Fallback", "LoadBalance", "Direct", "Reject" };
                int candidatesTotal = 0;

                string cn = currentNode; // volatile read

                foreach (string nodeName in allNodes) {
                    if (string.IsNullOrEmpty(nodeName) || nodeName.Length > MAX_NODE_NAME_LENGTH) continue;

                    // 跳过策略组
                    bool skip = false;
                    foreach (string sg in SKIP_GROUPS) { if (nodeName == sg) { skip = true; break; } }
                    if (skip) continue;

                    // 跳过策略组类型
                    string nodeType = FindProxyType(json, nodeName);
                    foreach (string st in skipTypes) { if (nodeType == st) { skip = true; break; } }
                    if (skip) continue;

                    bool isPreferred = preferredSnapshot != null && preferredSnapshot.Contains(nodeName);

                    // 排除可配置的地区节点（关键字模式下：偏好节点可覆盖关键字排除）
                    bool excluded = false;
                    if (disabledNodesExplicitMode) {
                        lock (disabledNodesLock) { excluded = disabledNodes.Contains(nodeName); }
                    } else if (excludeRegions != null && !isPreferred) {
                        foreach (string region in excludeRegions) {
                            if (!string.IsNullOrEmpty(region) && nodeName.Contains(region)) { excluded = true; break; }
                        }
                    }
                    if (excluded) continue;

                    bool isBlacklisted;
                    lock (blacklistLock) { isBlacklisted = nodeBlacklist.ContainsKey(nodeName); }
                    if (isBlacklisted) continue;

                    candidatesTotal++;

                    int delay = GetNodeDelay(json, nodeName);
                    if (delay > 0) {
                        if (isPreferred) preferredWithDelay.Add(new KeyValuePair<string, int>(nodeName, delay));
                        else normalWithDelay.Add(new KeyValuePair<string, int>(nodeName, delay));
                    }

                    // Candidate list for live probing when delay history is missing (prefer switching away from current node)
                    if (nodeName != cn) {
                        if (isPreferred) preferredCandidates.Add(nodeName);
                        else normalCandidates.Add(nodeName);
                    }
                }

                preferredWithDelay.Sort((a, b) => a.Value.CompareTo(b.Value));
                normalWithDelay.Sort((a, b) => a.Value.CompareTo(b.Value));
                int delaySamples = preferredWithDelay.Count + normalWithDelay.Count;
                string bestNode = null;
                int bestDelay = int.MaxValue;
                bool bestPreferred = false;

                foreach (var kv in preferredWithDelay) {
                    if (kv.Key != cn) {
                        bestNode = kv.Key;
                        bestDelay = kv.Value;
                        bestPreferred = true;
                        break;
                    }
                }

                // 偏好节点不可用或延迟不可接受时，回退到非偏好节点
                if (bestNode == null || bestDelay >= MAX_ACCEPTABLE_DELAY) {
                    foreach (var kv in normalWithDelay) {
                        if (kv.Key != cn) {
                            bestNode = kv.Key;
                            bestDelay = kv.Value;
                            bestPreferred = false;
                            break;
                        }
                    }
                }

                // If delay history is missing/insufficient, probe a subset live to avoid "请先测速" loops.
                bool shouldProbe = false;
                if (candidatesTotal > 0 && (bestNode == null || bestDelay >= MAX_ACCEPTABLE_DELAY)) {
                    if (forceDelayRefresh) shouldProbe = true;
                    else if (delaySamples == 0) shouldProbe = true;
                    else if (bestNode == null && delaySamples <= 1) shouldProbe = true;
                }

                if (shouldProbe) {
                    HashSet<string> seen = new HashSet<string>();
                    List<string> probeList = new List<string>();

                    int preferredMax = 4;
                    foreach (string n in preferredCandidates) {
                        if (probeList.Count >= preferredMax) break;
                        if (seen.Add(n)) probeList.Add(n);
                    }

                    int maxProbe = 10;
                    int need = maxProbe - probeList.Count;
                    if (need > 0 && normalCandidates.Count > 0) {
                        int start = 0;
                        try {
                            int c = Interlocked.Increment(ref delayProbeCursor);
                            start = (int)((long)(c * maxProbe) % normalCandidates.Count);
                            if (start < 0) start = 0;
                        } catch { start = 0; }

                        for (int i = 0; i < normalCandidates.Count && need > 0; i++) {
                            string n = normalCandidates[(start + i) % normalCandidates.Count];
                            if (seen.Add(n)) { probeList.Add(n); need--; }
                        }
                    }

                    if (probeList.Count > 0) {
                        Dictionary<string, int> live = ProbeDelaysLive(probeList, 5000, 4);
                        if (live != null && live.Count > 0) {
                            List<KeyValuePair<string, int>> preferredLive = new List<KeyValuePair<string, int>>();
                            List<KeyValuePair<string, int>> normalLive = new List<KeyValuePair<string, int>>();
                            foreach (var kv in live) {
                                bool isPreferred = preferredSnapshot != null && preferredSnapshot.Contains(kv.Key);
                                if (isPreferred) preferredLive.Add(kv);
                                else normalLive.Add(kv);
                            }
                            preferredLive.Sort((a, b) => a.Value.CompareTo(b.Value));
                            normalLive.Sort((a, b) => a.Value.CompareTo(b.Value));

                            bestNode = null;
                            bestDelay = int.MaxValue;
                            bestPreferred = false;

                            foreach (var kv in preferredLive) {
                                if (kv.Key != cn) { bestNode = kv.Key; bestDelay = kv.Value; bestPreferred = true; break; }
                            }
                            if (bestNode == null || bestDelay >= MAX_ACCEPTABLE_DELAY) {
                                foreach (var kv in normalLive) {
                                    if (kv.Key != cn) { bestNode = kv.Key; bestDelay = kv.Value; bestPreferred = false; break; }
                                }
                            }
                        }
                    }
                }

                if (bestNode != null && bestDelay < MAX_ACCEPTABLE_DELAY) {
                    if (!string.IsNullOrEmpty(cn)) {
                        lock (blacklistLock) { nodeBlacklist[cn] = DateTime.Now; }
                    }

                    string url = "/proxies/" + Uri.EscapeDataString(group);
                    if (ApiPut(url, "{\"name\":\"" + bestNode + "\"}")) {
                        string prefMark = bestPreferred ? " [偏好]" : "";
                        Log("切换: " + SafeNodeName(bestNode) + " (" + bestDelay + "ms) @" + group + prefMark);
                        currentNode = bestNode;
                        Interlocked.Exchange(ref lastDelay, bestDelay);
                        Interlocked.Increment(ref totalSwitches);
                        return true;
                    } else {
                        Log("切换失败: PUT " + group + " node=" + SafeNodeName(bestNode));
                    }
                } else if (bestNode == null) {
                    if (candidatesTotal > 0 && delaySamples == 0)
                        Log("切换失败: 无可用节点(请先测速) group=" + group + " allCount=" + allNodes.Count);
                    else
                        Log("切换失败: 无更优节点");
                } else {
                    Log("切换失败: 延迟过高 " + bestDelay + "ms");
                }

                return false;
            }
        } catch (Exception ex) {
            Log("切换异常: " + ex.Message);
        }
        return false;
    }


    // ==================== 延迟测试 ====================
    void TriggerDelayTest() {
        string group = string.IsNullOrEmpty(nodeGroup) ? "GLOBAL" : nodeGroup;
        try {
            // mihomo/meta: /proxies/{name}/delay is the portable delay endpoint (group delay endpoint may not exist)
            HttpWebRequest req = WebRequest.Create(clashApi + "/proxies/" + Uri.EscapeDataString(group) + "/delay?url=http://www.gstatic.com/generate_204&timeout=5000") as HttpWebRequest;
            req.Method = "GET";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            // 异步测速：不阻塞主流程，但也不要因为超时过短导致测速尚未完成就中断请求
            req.Timeout = 7000;
            req.ReadWriteTimeout = 7000;
            // 本地 API 不应走系统代理
            try { if (req.RequestUri != null && req.RequestUri.IsLoopback) req.Proxy = null; } catch { /* ignore */ }
            req.BeginGetResponse(ar => { try { req.EndGetResponse(ar).Close(); } catch { /* 测速异步回调异常可忽略 */ } }, null);
        } catch { /* 测速请求发送失败不影响主流程 */ }
    }

    // ==================== 代理测试 ====================
    int TestProxy(out bool success, bool fast = false) {
        string[] testUrls = fast
            ? new string[] { "http://www.gstatic.com/generate_204" }
            : new string[] { "http://www.gstatic.com/generate_204", "http://cp.cloudflare.com/generate_204" };

        int successCount = 0;
        int minDelay = int.MaxValue;
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
                    if (fast) break;
                }
            } catch { /* 代理测试超时属正常探测场景 */ }
        }

        success = successCount > 0;
        int result = success ? minDelay : 0;
        Interlocked.Exchange(ref lastDelay, result);
        return result;
    }
}
