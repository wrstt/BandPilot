using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BandPilot.Monitor;

namespace BandPilot.Ui
{
    /// <summary>Live per-process bandwidth, fed by the ETW kernel session.</summary>
    public sealed class MonitorPage : UserControl
    {
        private readonly BandwidthMonitor _monitor = new BandwidthMonitor();
        private readonly ListView _list = new ListView();
        private readonly Label _totals = new Label();
        private readonly Label _hint = new Label();
        private readonly Button _startStop;
        private readonly Button _reset;
        private readonly Button _prioritise;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        /// <summary>Raised when the user asks to prioritise the selected process.</summary>
        public event EventHandler<string> PrioritiseRequested;

        public MonitorPage()
        {
            _startStop = Theme.MakeButton("Start monitoring", true);
            _reset = Theme.MakeButton("Reset totals", false);
            _prioritise = Theme.MakeButton("Create a rule for this app", false);

            BackColor = Theme.Background;
            Dock = DockStyle.Fill;
            Padding = new Padding(16);

            BuildLayout();

            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Tick();
            _startStop.Click += (s, e) => ToggleMonitoring();
            _reset.Click += (s, e) => { _monitor.Reset(); _list.Items.Clear(); };
            _prioritise.Click += (s, e) => RaisePrioritise();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Background
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

            var head = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            Label title = Theme.MakeLabel("Live traffic", false);
            title.Font = Theme.TitleFont;
            title.Location = new Point(0, 0);
            head.Controls.Add(title);

            _totals.ForeColor = Theme.TextDim;
            _totals.Font = Theme.UiFont;
            _totals.AutoSize = true;
            _totals.Location = new Point(0, 30);
            _totals.Text = "Not running.";
            head.Controls.Add(_totals);
            root.Controls.Add(head, 0, 0);

            Theme.StyleList(_list);
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("Process", 210);
            _list.Columns.Add("PID", 70, HorizontalAlignment.Right);
            _list.Columns.Add("Download", 110, HorizontalAlignment.Right);
            _list.Columns.Add("Upload", 110, HorizontalAlignment.Right);
            _list.Columns.Add("Total down", 110, HorizontalAlignment.Right);
            _list.Columns.Add("Total up", 110, HorizontalAlignment.Right);
            root.Controls.Add(_list, 0, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Background,
                Padding = new Padding(0, 8, 0, 0)
            };
            _startStop.Width = 150;
            _reset.Width = 120;
            _prioritise.Width = 210;
            _startStop.Margin = new Padding(0, 0, 8, 0);
            _reset.Margin = new Padding(0, 0, 8, 0);
            actions.Controls.Add(_startStop);
            actions.Controls.Add(_reset);
            actions.Controls.Add(_prioritise);
            root.Controls.Add(actions, 0, 2);

            _hint.Dock = DockStyle.Fill;
            _hint.ForeColor = Theme.TextDim;
            _hint.Font = Theme.UiFont;
            _hint.Text = "Counts every packet the kernel attributes to a process, on any adapter.";
            root.Controls.Add(_hint, 0, 3);

            Controls.Add(root);
        }

        private void ToggleMonitoring()
        {
            if (_monitor.IsRunning)
            {
                StopMonitoring();
                return;
            }

            if (_monitor.Start())
            {
                _timer.Start();
                _startStop.Text = "Stop monitoring";
                _hint.Text = "Running. Figures update once a second.";
                _hint.ForeColor = Theme.TextDim;
            }
            else
            {
                _hint.Text = "Could not start the trace session: " + (_monitor.LastError ?? "unknown error") +
                             "  ·  This needs BandPilot to be running as administrator.";
                _hint.ForeColor = Theme.Bad;
            }
        }

        private void StopMonitoring()
        {
            _timer.Stop();
            _monitor.Dispose();
            _startStop.Text = "Start monitoring";
            _totals.Text = "Stopped.";
            _hint.Text = "Counts every packet the kernel attributes to a process, on any adapter.";
            _hint.ForeColor = Theme.TextDim;
        }

        private void Tick()
        {
            if (!_monitor.IsRunning)
            {
                // The pump thread can die on its own if the session is killed
                // externally; reflect that instead of showing stale numbers.
                StopMonitoring();
                if (!string.IsNullOrEmpty(_monitor.LastError))
                {
                    _hint.Text = "Monitoring stopped: " + _monitor.LastError;
                    _hint.ForeColor = Theme.Warn;
                }
                return;
            }

            List<ProcessTraffic> rows = _monitor.Sample();
            long downTotal = 0, upTotal = 0;

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (ProcessTraffic r in rows)
                {
                    downTotal += r.ReceivedBytesPerSecond;
                    upTotal += r.SentBytesPerSecond;

                    var item = new ListViewItem(new[]
                    {
                        r.ProcessName ?? "?",
                        r.ProcessId.ToString(),
                        BandwidthMonitor.FormatRate(r.ReceivedBytesPerSecond),
                        BandwidthMonitor.FormatRate(r.SentBytesPerSecond),
                        BandwidthMonitor.FormatBytes(r.TotalReceived),
                        BandwidthMonitor.FormatBytes(r.TotalSent)
                    })
                    {
                        Tag = r,
                        UseItemStyleForSubItems = false
                    };

                    if (r.ReceivedBytesPerSecond > 0) item.SubItems[2].ForeColor = Theme.Accent;
                    if (r.SentBytesPerSecond > 0) item.SubItems[3].ForeColor = Theme.Good;
                    item.SubItems[4].ForeColor = Theme.TextDim;
                    item.SubItems[5].ForeColor = Theme.TextDim;

                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            _totals.Text = "Down " + BandwidthMonitor.FormatRate(downTotal) +
                           "   ·   Up " + BandwidthMonitor.FormatRate(upTotal) +
                           "   ·   " + rows.Count + " processes seen";
        }

        private void RaisePrioritise()
        {
            if (_list.SelectedItems.Count == 0)
            {
                _hint.Text = "Pick a process from the list first.";
                _hint.ForeColor = Theme.Warn;
                return;
            }
            var r = _list.SelectedItems[0].Tag as ProcessTraffic;
            if (r == null) return;
            if (PrioritiseRequested != null) PrioritiseRequested(this, r.ProcessName);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _monitor.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
