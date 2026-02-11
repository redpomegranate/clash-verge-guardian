using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// 配置安全补全：仅补全不会改变语义的字段，避免旧配置缺 key 导致行为偏差。
/// </summary>
public partial class ClashGuardian
{
    static string BuildJsonStringArrayLiteral(string[] arr)
    {
        if (arr == null || arr.Length == 0) return "[]";
        StringBuilder sb = new StringBuilder();
        sb.Append("[");
        for (int i = 0; i < arr.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("\"").Append(EscapeJsonString(arr[i] ?? "")).Append("\"");
        }
        sb.Append("]");
        return sb.ToString();
    }

    static string InsertJsonFieldIfMissing(string json, string key, string rawValueLiteral)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return json;
        if (json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0) return json;

        int lastBrace = json.LastIndexOf('}');
        if (lastBrace <= 0) return json;

        int p = lastBrace - 1;
        while (p >= 0 && char.IsWhiteSpace(json[p])) p--;
        bool needComma = p >= 0 && json[p] != '{' && json[p] != ',';

        string insert = (needComma ? "," : "") + "\n  \"" + key + "\": " + rawValueLiteral + "\n";
        return json.Substring(0, lastBrace) + insert + json.Substring(lastBrace);
    }

    void BackfillConfigIfMissing()
    {
        try
        {
            lock (configLock)
            {
                if (!File.Exists(configFile))
                {
                    SaveDefaultConfig();
                    return;
                }

                string json = File.ReadAllText(configFile, Encoding.UTF8);
                string original = json;

                Dictionary<string, string> safeDefaults = new Dictionary<string, string>();
                safeDefaults["fastInterval"] = fastInterval.ToString();
                safeDefaults["speedFactor"] = speedFactor.ToString();
                safeDefaults["memoryWarning"] = memoryWarning.ToString();
                safeDefaults["proxyTestTimeoutMs"] = proxyTestTimeoutMs.ToString();
                safeDefaults["connectivityTestUrls"] = BuildJsonStringArrayLiteral(connectivityTestUrls);
                safeDefaults["connectivityProbeTimeoutMs"] = connectivityProbeTimeoutMs.ToString();
                safeDefaults["connectivityProbeMinSuccessCount"] = connectivityProbeMinSuccessCount.ToString();
                safeDefaults["connectivitySlowThresholdMs"] = connectivitySlowThresholdMs.ToString();
                safeDefaults["connectivityProbeMinIntervalSeconds"] = connectivityProbeMinIntervalSeconds.ToString();
                safeDefaults["connectivityResultMaxAgeSeconds"] = connectivityResultMaxAgeSeconds.ToString();

                foreach (KeyValuePair<string, string> kv in safeDefaults)
                {
                    json = InsertJsonFieldIfMissing(json, kv.Key, kv.Value);
                }

                if (!string.Equals(json, original, StringComparison.Ordinal))
                {
                    File.WriteAllText(configFile, json, Encoding.UTF8);
                }
            }
        }
        catch (Exception ex)
        {
            Log("配置补全失败: " + ex.Message);
        }
    }
}

