using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BandPilot.Monitor;

namespace BandPilot.Ui
{
    public partial class MonitorPage : UserControl
    {
        /// <summary>Flattened for display so the template binds to strings only.</summary>
        public sealed class Row
        {
            public string Name { get; set; }
            public int Pid { get; set; }
            public string Down { get; set; }
            public string Up { get; set; }
            public string TotalDown { get; set; }
            public string TotalUp { get; set; }
        }

        private readonly MainWindow _shell;
        private readonly DispatcherTimer _tick;
        private BandwidthMonitor _monitor;
        private bool _running;
        private int _ticks;

        /// <summary>
        /// The last message CheckHealth put on the status line. Health notices
        /// have to be cleared when they stop being true, but the status line is
        /// shared with messages the user triggered, and blanking it on a timer
        /// wiped those before they could be read. Tracking what was written is
        /// what makes "clear only my own text" possible.
        /// </summary>
        private string _healthMessage;

        public MonitorPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();

            _tick = new DispatcherTimer();
            _tick.Interval = TimeSpan.FromSeconds(1);
            _tick.Tick += (s, e) => Sample();
        }

        private void OnToggle(object sender, RoutedEventArgs e)
        {
            if (_running) StopMonitoring();
            else StartMonitoring();
        }

        private void StartMonitoring()
        {
            try
            {
                _monitor = new BandwidthMonitor();
                if (!_monitor.Start())
                {
                    // Almost always a missing elevation token or the session
                    // already being held by another tool. The driver's own words
                    // are far more useful than a generic message, so they lead.
                    string why = _monitor.LastError;
                    Status(string.IsNullOrEmpty(why)
                        ? "Could not open the trace session. Run BandPilot as administrator, and "
                          + "close other network monitors that may hold it."
                        : "Could not open the trace session: " + why,
                        "B.WarnText");
                    _monitor.Dispose();
                    _monitor = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                Status("Per-process monitoring is unavailable: " + ex.Message, "B.Bad");
                _monitor = null;
                return;
            }

            _running = true;
            _ticks = 0;
            _healthMessage = null;
            BtnStart.Content = "Stop monitoring";
            OffState.Visibility = Visibility.Collapsed;
            TableHeader.Visibility = Visibility.Visible;
            Totals.Visibility = Visibility.Visible;
            Status(string.Empty, "B.TextDim");
            _tick.Start();
        }

        /// <summary>
        /// Releases the trace session when the page is navigated away from,
        /// while remembering that it was running so returning here can say so.
        /// </summary>
        public void Suspend()
        {
            if (!_running) return;
            StopMonitoring();
            Status("Monitoring stopped when you left the page — press Start to resume.",
                   "B.TextDim");
        }

        public void StopMonitoring()
        {
            _tick.Stop();
            _running = false;

            if (_monitor != null)
            {
                _monitor.Dispose();
                _monitor = null;
            }

            if (!IsLoaded) return;

            BtnStart.Content = "Start monitoring";
            OffTitle.Text = "Monitoring is off";
            OffBody.Text = "Start monitoring to see per-process bandwidth, updated every second.";
            OffState.Visibility = Visibility.Visible;
            TableHeader.Visibility = Visibility.Collapsed;
            Totals.Visibility = Visibility.Collapsed;
            ProcList.ItemsSource = null;
        }

        private void Sample()
        {
            if (_monitor == null) return;

            List<ProcessTraffic> data;
            try { data = _monitor.Sample(); }
            catch (Exception ex) { Status(ex.Message, "B.Bad"); return; }

            _ticks++;

            // CheckHealth can tear the page down. Without this bail-out the rest
            // of Sample carried on and repainted the rows and totals it had just
            // cleared, leaving the "Monitoring is off" card sitting on top of a
            // live-looking table.
            if (!CheckHealth()) return;

            long down = 0, up = 0;
            foreach (ProcessTraffic p in data)
            {
                down += p.ReceivedBytesPerSecond;
                up += p.SentBytesPerSecond;
            }

            TotalDown.Text = BandwidthMonitor.FormatRate(down);
            TotalUp.Text = BandwidthMonitor.FormatRate(up);
            ProcCount.Text = data.Count + (data.Count == 1 ? " process seen" : " processes seen");

            // Preserve the selection across the rebuild, otherwise "create a
            // rule for this app" becomes impossible to click at one update a second.
            Row selected = ProcList.SelectedItem as Row;
            int keepPid = selected != null ? selected.Pid : -1;

            List<Row> rows = data
                .OrderByDescending(p => p.ReceivedBytesPerSecond + p.SentBytesPerSecond)
                .Select(p => new Row
                {
                    Name = p.ProcessName,
                    Pid = p.ProcessId,
                    Down = BandwidthMonitor.FormatRate(p.ReceivedBytesPerSecond),
                    Up = BandwidthMonitor.FormatRate(p.SentBytesPerSecond),
                    TotalDown = BandwidthMonitor.FormatBytes(p.TotalReceived),
                    TotalUp = BandwidthMonitor.FormatBytes(p.TotalSent)
                })
                .ToList();

            ProcList.ItemsSource = rows;
            if (keepPid >= 0)
            {
                ProcList.SelectedItem = rows.FirstOrDefault(r => r.Pid == keepPid);
            }
        }

        /// <summary>
        /// A trace session can open successfully and then hear nothing — the
        /// pump thread dies, or the provider yields no events. Both look exactly
        /// like an idle network unless the difference is spelled out, which is
        /// how this page could sit at zero with nothing to explain it.
        /// </summary>
        /// <returns>False when the session was stopped and the caller must not
        /// keep painting.</returns>
        private bool CheckHealth()
        {
            if (_monitor == null) return false;

            if (!_monitor.IsRunning)
            {
                string why = _monitor.LastError;
                Status("The trace session stopped"
                    + (string.IsNullOrEmpty(why) ? "." : ": " + why), "B.Bad");
                _healthMessage = null;
                StopMonitoring();
                return false;
            }

            if (_monitor.EventsSeen > 0)
            {
                // Clear only what this method wrote. The null test matters: once
                // cleared, comparing "" to "" would match every tick and wipe the
                // user's own messages, which is the bug this replaced.
                if (_healthMessage != null && StatusLine.Text == _healthMessage)
                {
                    Status(string.Empty, "B.TextDim");
                }
                _healthMessage = null;
                return true;
            }

            // Give it a few seconds before saying anything: a genuinely quiet
            // machine takes a moment to produce its first packet.
            if (_ticks == 5)
            {
                _healthMessage = "Session open on " + _monitor.Mode
                               + ", waiting for the first network event…";
                Status(_healthMessage, "B.TextDim");
            }
            else if (_ticks == 15)
            {
                _healthMessage = "No network events after 15 seconds. The session is open on "
                               + _monitor.Mode + " but receiving nothing — this usually means "
                               + "BandPilot is not running elevated, or another tool holds the "
                               + "trace session.";
                Status(_healthMessage, "B.WarnText");
            }
            return true;
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            if (_monitor != null) _monitor.Reset();
        }

        private void OnCreateRule(object sender, RoutedEventArgs e)
        {
            Row r = ProcList.SelectedItem as Row;
            if (r == null)
            {
                Status("Select a process first.", "B.WarnText");
                return;
            }

            string exe = r.Name;
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            _shell.GoToPriorityWith(exe);
        }

        private void Status(string text, string brushKey)
        {
            StatusLine.Text = text ?? string.Empty;
            StatusLine.Foreground = (Brush)FindResource(brushKey);
        }
    }
}
