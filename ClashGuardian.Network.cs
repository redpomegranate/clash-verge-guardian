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

    bool SwitchToBestNode() {
        CleanBlacklist();
        try {
            string json = ApiRequest("/proxies");
            if (string.IsNullOrEmpty(json)) {
                Log("切换失败: API无响应");
                return false;
            }

            string group = FindSelectorGroup(json);
            nodeGroup = group;

            List<string> allNodes = GetGroupAllNodes(json, group);

            HashSet<string> preferredSnapshot = null;
            lock (preferredNodesLock) {
                if (preferredNodes != null && preferredNodes.Count > 0) {
                    preferredSnapshot = new HashSet<string>(preferredNodes);
                }
            }

            List<KeyValuePair<string, int>> preferredWithDelay = new List<KeyValuePair<string, int>>();
            List<KeyValuePair<string, int>> normalWithDelay = new List<KeyValuePair<string, int>>();
            string[] skipTypes = new string[] { "Selector", "URLTest", "Fallback", "LoadBalance", "Direct", "Reject" };

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

                int delay = GetNodeDelay(json, nodeName);
                if (delay > 0) {
                    if (isPreferred) preferredWithDelay.Add(new KeyValuePair<string, int>(nodeName, delay));
                    else normalWithDelay.Add(new KeyValuePair<string, int>(nodeName, delay));
                }
            }

            if (preferredWithDelay.Count + normalWithDelay.Count == 0) {
                Log("切换失败: 无可用节点(请先测速) group=" + group + " allCount=" + allNodes.Count);
                return false;
            }

            preferredWithDelay.Sort((a, b) => a.Value.CompareTo(b.Value));
            normalWithDelay.Sort((a, b) => a.Value.CompareTo(b.Value));

            string cn = currentNode; // volatile read
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
                Log("切换失败: 无更优节点");
            } else {
                Log("切换失败: 延迟过高 " + bestDelay + "ms");
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
            HttpWebRequest req = WebRequest.Create(clashApi + "/group/" + Uri.EscapeDataString(group) + "/delay?url=http://www.gstatic.com/generate_204&timeout=5000") as HttpWebRequest;
            req.Method = "GET";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.Timeout = 2000;
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

