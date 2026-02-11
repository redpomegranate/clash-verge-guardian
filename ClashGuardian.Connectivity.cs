using System;
using System.Net;
using System.Diagnostics;
using System.Threading;

/// <summary>
/// 连接性探测：在高延迟/代理异常时，额外验证“真实网站连通性”，用于订阅切换前的综合判断。
/// </summary>
public partial class ClashGuardian
{
    enum ConnectivityVerdict
    {
        Unknown = 0,
        Ok = 1,
        Slow = 2,
        Down = 3
    }

    struct ConnectivitySnapshot
    {
        public ConnectivityVerdict Verdict;
        public int BestRttMs;
        public int SuccessCount;
        public int AttemptCount;
        public long UpdatedTicks;
    }

    int _isConnectivityProbeRunning = 0; // 0=idle 1=running
    long lastConnectivityProbeStartTicks = 0;
    long connectivityUpdatedTicks = 0;
    int connectivityVerdict = 0;
    int connectivityBestRttMs = 0;
    int connectivitySuccessCount = 0;
    int connectivityAttemptCount = 0;

    static string ConnectivityVerdictToString(ConnectivityVerdict v)
    {
        switch (v)
        {
            case ConnectivityVerdict.Ok: return "Ok";
            case ConnectivityVerdict.Slow: return "Slow";
            case ConnectivityVerdict.Down: return "Down";
            default: return "Unknown";
        }
    }

    void MaybeStartConnectivityProbe(string trigger, bool running, bool proxyOK, int delay)
    {
        try
        {
            if (_isDetectionPaused) return;
            if (_isRestarting) return;
            if (manualClientInterventionRequired) return;

            if (!running) return;
            if (proxyOK && delay > 0 && delay <= highDelayThreshold) return;
            if (proxyOK && delay <= 0) return;

            long nowTicks = DateTime.Now.Ticks;
            long lastStart = Interlocked.Read(ref lastConnectivityProbeStartTicks);
            try
            {
                if (lastStart > 0 && (new TimeSpan(nowTicks - lastStart)).TotalSeconds < connectivityProbeMinIntervalSeconds) return;
            }
            catch { /* ignore */ }

            if (Interlocked.CompareExchange(ref _isConnectivityProbeRunning, 1, 0) != 0) return;

            // Double-check throttle after gate acquired.
            lastStart = Interlocked.Read(ref lastConnectivityProbeStartTicks);
            try
            {
                if (lastStart > 0 && (new TimeSpan(nowTicks - lastStart)).TotalSeconds < connectivityProbeMinIntervalSeconds)
                {
                    Interlocked.Exchange(ref _isConnectivityProbeRunning, 0);
                    return;
                }
            }
            catch { /* ignore */ }

            Interlocked.Exchange(ref lastConnectivityProbeStartTicks, nowTicks);
            ThreadPool.QueueUserWorkItem(_ => RunConnectivityProbeWorker(trigger ?? ""));
        }
        catch
        {
            try { Interlocked.Exchange(ref _isConnectivityProbeRunning, 0); } catch { /* ignore */ }
        }
    }

    void RunConnectivityProbeWorker(string trigger)
    {
        try
        {
            // Allow https URLs in user config (TLS 1.2).
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { /* ignore */ }

            string[] urls = connectivityTestUrls;
            if (urls == null || urls.Length == 0)
            {
                urls = new string[] { "http://www.gstatic.com/generate_204" };
            }

            int attempts = 0;
            int successCount = 0;
            int best = int.MaxValue;

            int timeout = connectivityProbeTimeoutMs;
            if (timeout <= 0) timeout = 3000;

            foreach (string url in urls)
            {
                if (string.IsNullOrEmpty(url)) continue;
                attempts++;

                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    HttpWebRequest req = WebRequest.Create(url) as HttpWebRequest;
                    if (req == null) continue;

                    req.Proxy = new WebProxy("127.0.0.1", proxyPort);
                    req.Timeout = timeout;
                    req.ReadWriteTimeout = timeout;
                    req.UserAgent = "ClashGuardian/" + APP_VERSION;

                    using (WebResponse resp = req.GetResponse())
                    {
                        sw.Stop();
                        int rtt = (int)sw.ElapsedMilliseconds;
                        successCount++;
                        if (rtt > 0 && rtt < best) best = rtt;
                    }
                }
                catch { /* ignore probe failures */ }
            }

            ConnectivityVerdict verdict;
            int bestRtt = (best == int.MaxValue) ? 0 : best;
            int minOk = connectivityProbeMinSuccessCount;
            if (minOk < 1) minOk = 1;

            if (successCount < minOk) verdict = ConnectivityVerdict.Down;
            else if (bestRtt > connectivitySlowThresholdMs) verdict = ConnectivityVerdict.Slow;
            else verdict = ConnectivityVerdict.Ok;

            Interlocked.Exchange(ref connectivityBestRttMs, bestRtt);
            Interlocked.Exchange(ref connectivitySuccessCount, successCount);
            Interlocked.Exchange(ref connectivityAttemptCount, attempts);
            Interlocked.Exchange(ref connectivityVerdict, (int)verdict);
            Interlocked.Exchange(ref connectivityUpdatedTicks, DateTime.Now.Ticks);
        }
        finally
        {
            try { Interlocked.Exchange(ref _isConnectivityProbeRunning, 0); } catch { /* ignore */ }
        }
    }

    bool TryGetRecentConnectivity(out ConnectivityVerdict v, out int bestRttMs, out int ageSec, out int okCount, out int attempts)
    {
        v = ConnectivityVerdict.Unknown;
        bestRttMs = 0;
        ageSec = 0;
        okCount = 0;
        attempts = 0;

        long updated = Interlocked.Read(ref connectivityUpdatedTicks);
        if (updated <= 0) return false;

        try
        {
            ageSec = (int)(DateTime.Now - new DateTime(updated)).TotalSeconds;
        }
        catch
        {
            return false;
        }

        if (ageSec < 0) ageSec = 0;
        if (ageSec > connectivityResultMaxAgeSeconds) return false;

        try { v = (ConnectivityVerdict)Interlocked.CompareExchange(ref connectivityVerdict, 0, 0); } catch { v = ConnectivityVerdict.Unknown; }
        bestRttMs = Interlocked.CompareExchange(ref connectivityBestRttMs, 0, 0);
        okCount = Interlocked.CompareExchange(ref connectivitySuccessCount, 0, 0);
        attempts = Interlocked.CompareExchange(ref connectivityAttemptCount, 0, 0);
        return true;
    }

    ConnectivitySnapshot GetConnectivitySnapshot()
    {
        ConnectivitySnapshot s = new ConnectivitySnapshot();
        s.Verdict = ConnectivityVerdict.Unknown;
        s.BestRttMs = 0;
        s.SuccessCount = 0;
        s.AttemptCount = 0;
        s.UpdatedTicks = Interlocked.Read(ref connectivityUpdatedTicks);

        ConnectivityVerdict v;
        int bestRttMs, ageSec, okCount, attempts;
        if (TryGetRecentConnectivity(out v, out bestRttMs, out ageSec, out okCount, out attempts))
        {
            s.Verdict = v;
            s.BestRttMs = bestRttMs;
            s.SuccessCount = okCount;
            s.AttemptCount = attempts;
        }

        return s;
    }
}
