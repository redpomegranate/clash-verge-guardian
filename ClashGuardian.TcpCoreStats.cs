using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// TCP 统计采样：保留全局连接态统计，并补充 core 进程级 CLOSE_WAIT 统计供决策使用。
/// </summary>
public partial class ClashGuardian
{
    struct TcpStatsSnapshot
    {
        public int TimeWait;
        public int Established;
        public int CloseWait;
        public int CoreCloseWait;
        public bool CoreCloseWaitKnown;
    }

    HashSet<int> GetCoreProcessPidSnapshot()
    {
        HashSet<int> pids = new HashSet<int>();
        try
        {
            string[] names = coreProcessNames;
            if (names == null || names.Length == 0) return pids;

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    Process[] procs = Process.GetProcessesByName(name);
                    if (procs == null) continue;
                    foreach (Process p in procs)
                    {
                        try
                        {
                            if (p != null && p.Id > 0) pids.Add(p.Id);
                        }
                        catch { /* ignore */ }
                        finally { try { if (p != null) p.Dispose(); } catch { /* ignore */ } }
                    }
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        return pids;
    }

    static bool TryParseNetstatTcpLine(string line, out string state, out int pid)
    {
        state = "";
        pid = 0;
        if (string.IsNullOrEmpty(line)) return false;
        string t = line.Trim();
        if (t.Length == 0) return false;
        if (!t.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) return false;

        string[] parts = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) return false;

        state = parts[parts.Length - 2].ToUpperInvariant();
        return int.TryParse(parts[parts.Length - 1], out pid);
    }

    TcpStatsSnapshot GetTcpStatsSnapshot()
    {
        TcpStatsSnapshot s = new TcpStatsSnapshot();
        s.TimeWait = 0;
        s.Established = 0;
        s.CloseWait = 0;
        s.CoreCloseWait = 0;
        s.CoreCloseWaitKnown = false;

        HashSet<int> corePids = GetCoreProcessPidSnapshot();

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("netstat", "-n -o -p TCP");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.Default;
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                try { p.WaitForExit(5000); } catch { /* ignore */ }

                foreach (string line in output.Split('\n'))
                {
                    string state;
                    int pid;
                    if (!TryParseNetstatTcpLine(line, out state, out pid)) continue;

                    if (state == "TIME_WAIT") s.TimeWait++;
                    else if (state == "ESTABLISHED") s.Established++;
                    else if (state == "CLOSE_WAIT")
                    {
                        s.CloseWait++;
                        if (corePids.Count > 0 && corePids.Contains(pid)) s.CoreCloseWait++;
                    }
                }
            }

            // 无法确认 core pid 时，决策侧将跳过 CloseWaitLeak 自动重启分支，避免误判。
            s.CoreCloseWaitKnown = corePids.Count > 0;
        }
        catch (Exception ex)
        {
            s.CoreCloseWaitKnown = false;
            Log("TCP统计异常: " + ex.Message);
        }

        return s;
    }
}
