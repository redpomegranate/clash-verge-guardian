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
        while (objEnd < json.Length && braceCount > 0) {
            if (json[objEnd] == '{') braceCount++;
            else if (json[objEnd] == '}') braceCount--;
            objEnd++;
        }
        return true;
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

    void GetCurrentNode() {
        try {
            string json = ApiRequest("/proxies", API_TIMEOUT_NORMAL);
            if (string.IsNullOrEmpty(json)) return;

            string node = ResolveActualNode(json, "GLOBAL", 0);
            if (!string.IsNullOrEmpty(node)) {
                currentNode = SafeNodeName(node);
                return;
            }

            foreach (string selector in SELECTOR_NAMES) {
                if (selector == "GLOBAL") continue;
                node = ResolveActualNode(json, selector, 0);
                if (!string.IsNullOrEmpty(node)) {
                    currentNode = SafeNodeName(node);
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

            List<KeyValuePair<string, int>> nodesWithDelay = new List<KeyValuePair<string, int>>();
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

                // 排除可配置的地区节点
                bool excluded = false;
                foreach (string region in excludeRegions) {
                    if (nodeName.Contains(region)) { excluded = true; break; }
                }
                if (excluded) continue;

                bool isBlacklisted;
                lock (blacklistLock) { isBlacklisted = nodeBlacklist.ContainsKey(nodeName); }
                if (isBlacklisted) continue;

                int delay = GetNodeDelay(json, nodeName);
                if (delay > 0) {
                    nodesWithDelay.Add(new KeyValuePair<string, int>(nodeName, delay));
                }
            }

            if (nodesWithDelay.Count == 0) {
                Log("切换失败: 无可用节点(请先测速) group=" + group + " allCount=" + allNodes.Count);
                return false;
            }

            nodesWithDelay.Sort((a, b) => a.Value.CompareTo(b.Value));

            string bestNode = null;
            int bestDelay = int.MaxValue;
            string cn = currentNode; // volatile read
            foreach (var kv in nodesWithDelay) {
                if (kv.Key != cn) {
                    bestNode = kv.Key;
                    bestDelay = kv.Value;
                    break;
                }
            }

            if (bestNode != null && bestDelay < MAX_ACCEPTABLE_DELAY) {
                if (!string.IsNullOrEmpty(cn)) {
                    lock (blacklistLock) { nodeBlacklist[cn] = DateTime.Now; }
                }

                string url = "/proxies/" + Uri.EscapeDataString(group);
                if (ApiPut(url, "{\"name\":\"" + bestNode + "\"}")) {
                    Log("切换: " + SafeNodeName(bestNode) + " (" + bestDelay + "ms) @" + group);
                    currentNode = bestNode;
                    Interlocked.Exchange(ref lastDelay, bestDelay);
                    Interlocked.Increment(ref totalSwitches);
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

    // ==================== 延迟测试 ====================
    void TriggerDelayTest() {
        string group = string.IsNullOrEmpty(nodeGroup) ? "GLOBAL" : nodeGroup;
        try {
            HttpWebRequest req = WebRequest.Create(clashApi + "/group/" + Uri.EscapeDataString(group) + "/delay?url=http://www.gstatic.com/generate_204&timeout=5000") as HttpWebRequest;
            req.Method = "GET";
            req.Headers.Add("Authorization", "Bearer " + clashSecret);
            req.Timeout = 2000;
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
