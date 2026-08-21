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
                    // Almost always a missing elevation token or the ETW session
                    // already being held by another tool.
                    Status("Could not open the ETW session. Run BandPilot as administrator, "
                         + "and close other network monitors that may hold the kernel session.",
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
            BtnStart.Content = "Stop monitoring";
            OffState.Visibility = Visibility.Collapsed;
            TableHeader.Visibility = Visibility.Visible;
            Totals.Visibility = Visibility.Visible;
            Status(string.Empty, "B.TextDim");
            _tick.Start();
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
