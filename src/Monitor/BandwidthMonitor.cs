using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace BandPilot.Monitor
{
    public sealed class ProcessTraffic
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public long SentBytesPerSecond { get; set; }
        public long ReceivedBytesPerSecond { get; set; }
        public long TotalSent { get; set; }
        public long TotalReceived { get; set; }
    }

    /// <summary>
    /// Per-process network accounting via an ETW kernel session.
    ///
    /// Windows exposes no per-process network performance counter, so the only
    /// way to attribute bytes to a PID is to listen to the kernel's TCP/IP
    /// events directly. Requires elevation.
    /// </summary>
    public sealed class BandwidthMonitor : IDisposable
    {
        private sealed class Counters
        {
            public long Sent;
            public long Received;
            public long LastSent;
            public long LastReceived;
            public string Name;
        }

        private const string SessionName = "BandPilotNetMonitor";

        private readonly ConcurrentDictionary<int, Counters> _counters =
            new ConcurrentDictionary<int, Counters>();

        private TraceEventSession _session;
        private Thread _thread;
        private volatile bool _running;
        private DateTime _lastSample = DateTime.UtcNow;

        public string LastError { get; private set; }
        public bool IsRunning { get { return _running; } }

        public bool Start()
        {
            if (_running) return true;
            try
            {
                // An orphaned session from a previous crash would block this one.
                try { TraceEventSession.GetActiveSession(SessionName)?.Dispose(); }
                catch (Exception) { /* nothing to clean up */ }

                _session = new TraceEventSession(SessionName);
                _session.StopOnDispose = true;
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                _session.Source.Kernel.TcpIpSend += d => Add(d.ProcessID, d.ProcessName, d.size, true);
                _session.Source.Kernel.TcpIpRecv += d => Add(d.ProcessID, d.ProcessName, d.size, false);
                _session.Source.Kernel.TcpIpSendIPV6 += d => Add(d.ProcessID, d.ProcessName, d.size, true);
                _session.Source.Kernel.TcpIpRecvIPV6 += d => Add(d.ProcessID, d.ProcessName, d.size, false);
                _session.Source.Kernel.UdpIpSend += d => Add(d.ProcessID, d.ProcessName, d.size, true);
                _session.Source.Kernel.UdpIpRecv += d => Add(d.ProcessID, d.ProcessName, d.size, false);
                _session.Source.Kernel.UdpIpSendIPV6 += d => Add(d.ProcessID, d.ProcessName, d.size, true);
                _session.Source.Kernel.UdpIpRecvIPV6 += d => Add(d.ProcessID, d.ProcessName, d.size, false);

                _running = true;
                _lastSample = DateTime.UtcNow;

                _thread = new Thread(Pump);
                _thread.IsBackground = true;
                _thread.Name = "BandPilot ETW pump";
                _thread.Start();

                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                _running = false;
                LastError = ex.Message;
                DisposeSession();
                return false;
            }
        }

        private void Pump()
        {
            try
            {
                _session.Source.Process();   // blocks until the session stops
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                _running = false;
            }
        }

        private void Add(int pid, string name, int size, bool sent)
        {
            if (pid <= 0 || size <= 0) return;

            Counters c = _counters.GetOrAdd(pid, _ => new Counters { Name = name });
            if (string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(name)) c.Name = name;

            if (sent) Interlocked.Add(ref c.Sent, size);
            else Interlocked.Add(ref c.Received, size);
        }

        /// <summary>
        /// Rates since the previous call. The caller decides the interval, so
        /// the elapsed time is measured rather than assumed.
        /// </summary>
        public List<ProcessTraffic> Sample()
        {
            DateTime now = DateTime.UtcNow;
            double seconds = (now - _lastSample).TotalSeconds;
            if (seconds < 0.05) seconds = 0.05;
            _lastSample = now;

            var list = new List<ProcessTraffic>();

            foreach (KeyValuePair<int, Counters> kv in _counters)
            {
                Counters c = kv.Value;
                long sent = Interlocked.Read(ref c.Sent);
                long recv = Interlocked.Read(ref c.Received);

                long dSent = sent - c.LastSent;
                long dRecv = recv - c.LastReceived;
                c.LastSent = sent;
                c.LastReceived = recv;

                if (dSent <= 0 && dRecv <= 0 && sent == 0 && recv == 0) continue;

                list.Add(new ProcessTraffic
                {
                    ProcessId = kv.Key,
                    ProcessName = ResolveName(kv.Key, c),
                    SentBytesPerSecond = (long)(dSent / seconds),
                    ReceivedBytesPerSecond = (long)(dRecv / seconds),
                    TotalSent = sent,
                    TotalReceived = recv
                });
            }

            list.Sort((a, b) =>
                (b.ReceivedBytesPerSecond + b.SentBytesPerSecond)
                .CompareTo(a.ReceivedBytesPerSecond + a.SentBytesPerSecond));

            return list;
        }

        private static string ResolveName(int pid, Counters c)
        {
            if (!string.IsNullOrEmpty(c.Name)) return c.Name;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    c.Name = p.ProcessName;
                }
            }
            catch (Exception)
            {
                c.Name = "pid " + pid;   // process already exited
            }
            return c.Name;
        }

        public void Reset()
        {
            _counters.Clear();
            _lastSample = DateTime.UtcNow;
        }

        public static string FormatRate(long bytesPerSecond)
        {
            double bits = bytesPerSecond * 8.0;
            if (bits >= 1000000.0) return (bits / 1000000.0).ToString("0.0") + " Mbit/s";
            if (bits >= 1000.0) return (bits / 1000.0).ToString("0") + " kbit/s";
            return bytesPerSecond > 0 ? "<1 kbit/s" : "-";
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        private void DisposeSession()
        {
            try
            {
                if (_session != null)
                {
                    _session.Dispose();
                    _session = null;
                }
            }
            catch (Exception) { /* already gone */ }
        }

        public void Dispose()
        {
            _running = false;
            DisposeSession();
        }
    }
}
