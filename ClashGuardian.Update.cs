using System;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using System.Threading;

/// <summary>
/// 自动更新：版本检查、下载、热替换、回滚
/// </summary>
public partial class ClashGuardian
{
    void CheckForUpdate(bool silent) {
        try {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            string url = string.Format(UPDATE_API, GITHUB_REPO);

            HttpWebRequest req = WebRequest.Create(url) as HttpWebRequest;
            req.UserAgent = "ClashGuardian/" + APP_VERSION;
            req.Timeout = UPDATE_CHECK_TIMEOUT;
            req.Proxy = new WebProxy("127.0.0.1", proxyPort);

            string json;
            try {
                using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse)
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                    json = reader.ReadToEnd();
                }
            } catch {
                // 代理失败，回退直连
                req = WebRequest.Create(url) as HttpWebRequest;
                req.UserAgent = "ClashGuardian/" + APP_VERSION;
                req.Timeout = UPDATE_CHECK_TIMEOUT;
                req.Proxy = null;
                using (HttpWebResponse resp = req.GetResponse() as HttpWebResponse)
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                    json = reader.ReadToEnd();
                }
            }

            string latestVersion = ExtractJsonString(json, "tag_name");
            if (string.IsNullOrEmpty(latestVersion)) return;

            if (latestVersion.StartsWith("v")) latestVersion = latestVersion.Substring(1);

            if (CompareVersions(latestVersion, APP_VERSION) > 0) {
                string assetUrl = ExtractAssetUrl(json);
                if (string.IsNullOrEmpty(assetUrl)) {
                    if (!silent) Log("更新: 未找到下载链接");
                    return;
                }

                this.BeginInvoke((Action)(() => {
                    DialogResult result = MessageBox.Show(
                        "发现新版本 v" + latestVersion + "\n当前版本 v" + APP_VERSION + "\n\n是否下载更新？",
                        "Clash Guardian 更新",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes) {
                        ThreadPool.QueueUserWorkItem(_ => DownloadAndUpdate(assetUrl, latestVersion));
                    }
                }));
            } else {
                if (!silent) Log("已是最新版本 v" + APP_VERSION);
            }
        } catch (Exception ex) {
            if (!silent) Log("更新检查失败: " + ex.Message);
        }
    }

    int CompareVersions(string v1, string v2) {
        string[] p1 = v1.Split('.');
        string[] p2 = v2.Split('.');
        int len = Math.Max(p1.Length, p2.Length);
        for (int i = 0; i < len; i++) {
            int n1 = i < p1.Length ? int.Parse(p1[i]) : 0;
            int n2 = i < p2.Length ? int.Parse(p2[i]) : 0;
            if (n1 > n2) return 1;
            if (n1 < n2) return -1;
        }
        return 0;
    }

    string ExtractAssetUrl(string json) {
        int assetsIdx = json.IndexOf("\"assets\":");
        if (assetsIdx < 0) return null;

        int searchStart = assetsIdx;
        while (true) {
            int urlIdx = json.IndexOf("\"browser_download_url\":\"", searchStart);
            if (urlIdx < 0) break;
            int urlStart = urlIdx + 24;
            int urlEnd = json.IndexOf('"', urlStart);
            if (urlEnd > urlStart) {
                string url = json.Substring(urlStart, urlEnd - urlStart);
                if (url.EndsWith(".exe")) return url;
                searchStart = urlEnd;
            } else {
                break;
            }
        }
        return null;
    }

    void DownloadAndUpdate(string downloadUrl, string version) {
        try {
            this.BeginInvoke((Action)(() => {
                statusLabel.Text = "● 状态: 正在下载更新 v" + version + "...";
                statusLabel.ForeColor = COLOR_WARNING;
            }));

            string exePath = Application.ExecutablePath;
            string updatePath = exePath + ".update";
            string oldPath = exePath + ".old";

            // 下载新版本（代理优先，直连回退）
            bool downloaded = false;

            // 尝试代理下载
            try {
                WebClient wc = new WebClient();
                wc.Proxy = new WebProxy("127.0.0.1", proxyPort);
                wc.Headers.Add("User-Agent", "ClashGuardian/" + APP_VERSION);
                wc.DownloadFile(downloadUrl, updatePath);
                downloaded = true;
                Log("更新: 代理下载完成");
            } catch {
                Log("更新: 代理下载失败，切换直连");
            }

            // 回退直连下载
            if (!downloaded) {
                try {
                    WebClient wc = new WebClient();
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "ClashGuardian/" + APP_VERSION);
                    wc.DownloadFile(downloadUrl, updatePath);
                    downloaded = true;
                    Log("更新: 直连下载完成");
                } catch (Exception ex) {
                    Log("更新: 直连下载也失败: " + ex.Message);
                }
            }

            if (!downloaded) {
                this.BeginInvoke((Action)(() => {
                    statusLabel.Text = "● 状态: 运行中";
                    statusLabel.ForeColor = COLOR_OK;
                }));
                return;
            }

            // 验证下载文件
            FileInfo fi = new FileInfo(updatePath);
            if (!fi.Exists || fi.Length < MIN_UPDATE_FILE_SIZE) {
                Log("更新: 下载文件无效 size=" + (fi.Exists ? fi.Length.ToString() : "0"));
                try { File.Delete(updatePath); } catch { /* 清理临时文件失败不影响 */ }
                this.BeginInvoke((Action)(() => {
                    statusLabel.Text = "● 状态: 运行中";
                    statusLabel.ForeColor = COLOR_OK;
                }));
                return;
            }

            // 热替换：重命名运行中的 exe（NTFS 允许），放置新版本
            try {
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(exePath, oldPath);
                File.Move(updatePath, exePath);
                Log("更新: 文件替换成功 v" + version);
            } catch (Exception ex) {
                // 回滚
                Log("更新: 替换失败，回滚: " + ex.Message);
                try {
                    if (!File.Exists(exePath) && File.Exists(oldPath))
                        File.Move(oldPath, exePath);
                } catch { /* 回滚失败属极端情况 */ }
                try { File.Delete(updatePath); } catch { /* 清理临时文件失败不影响 */ }

                this.BeginInvoke((Action)(() => {
                    statusLabel.Text = "● 状态: 运行中";
                    statusLabel.ForeColor = COLOR_OK;
                }));
                return;
            }

            // 启动新版本，传递当前 PID 让新进程等待
            try {
                Process.Start(exePath, "--wait-pid " + Process.GetCurrentProcess().Id);
                this.BeginInvoke((Action)(() => {
                    trayIcon.Visible = false;
                    Application.Exit();
                }));
            } catch (Exception ex) {
                Log("更新: 启动新版本失败: " + ex.Message);
                // 回滚
                try {
                    File.Delete(exePath);
                    File.Move(oldPath, exePath);
                } catch { /* 回滚失败属极端情况 */ }
            }
        } catch (Exception ex) {
            Log("更新异常: " + ex.Message);
            this.BeginInvoke((Action)(() => {
                statusLabel.Text = "● 状态: 运行中";
                statusLabel.ForeColor = COLOR_OK;
            }));
        }
    }
}
