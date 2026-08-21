using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BandPilot.Qos;

namespace BandPilot.Ui
{
    /// <summary>
    /// Traffic prioritisation and per-application bandwidth caps, backed by
    /// Windows QoS policies.
    /// </summary>
    public sealed class PriorityPage : UserControl
    {
        private readonly ListView _list = new ListView();
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _app = new TextBox();
        private readonly ComboBox _protocol = new ComboBox();
        private readonly TextBox _remotePort = new TextBox();
        private readonly ComboBox _dscp = new ComboBox();
        private readonly TextBox _limit = new TextBox();
        private readonly Label _hint = new Label();
        private readonly Button _browse;
        private readonly Button _save;
        private readonly Button _delete;
        private readonly Button _reload;

        public PriorityPage()
        {
            _browse = Theme.MakeButton("Browse…", false);
            _save = Theme.MakeButton("Save rule", true);
            _delete = Theme.MakeButton("Delete rule", false);
            _reload = Theme.MakeButton("Reload", false);

            BackColor = Theme.Background;
            Dock = DockStyle.Fill;
            Padding = new Padding(16);

            BuildLayout();

            _list.SelectedIndexChanged += (s, e) => LoadSelected();
            _browse.Click += (s, e) => BrowseForApp();
            _save.Click += (s, e) => SaveRule();
            _delete.Click += (s, e) => DeleteRule();
            _reload.Click += (s, e) => Reload();
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

            Label title = Theme.MakeLabel("Priority and limits", false);
            title.Font = Theme.TitleFont;
            root.Controls.Add(title, 0, 0);

            Theme.StyleList(_list);
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("Rule", 170);
            _list.Columns.Add("Application", 200);
            _list.Columns.Add("Protocol", 80);
            _list.Columns.Add("Remote port", 100);
            _list.Columns.Add("Priority", 210);
            _list.Columns.Add("Speed limit", 120);
            root.Controls.Add(_list, 0, 1);

            root.Controls.Add(BuildEditor(), 0, 2);

            _hint.Dock = DockStyle.Fill;
            _hint.ForeColor = Theme.TextDim;
            _hint.Font = Theme.UiFont;
            root.Controls.Add(_hint, 0, 3);

            Controls.Add(root);
        }

        private Control BuildEditor()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
                BackColor = Theme.Surface,
                Padding = new Padding(12, 10, 12, 10),
                Margin = new Padding(0, 8, 0, 8)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            StyleBox(_name);
            StyleBox(_app);
            StyleBox(_remotePort);
            StyleBox(_limit);

            _protocol.DropDownStyle = ComboBoxStyle.DropDownList;
            _protocol.Items.AddRange(new object[] { "*", "TCP", "UDP" });
            _protocol.SelectedIndex = 0;
            StyleCombo(_protocol);

            _dscp.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (KeyValuePair<int, string> kv in QosManager.DscpPresets)
            {
                _dscp.Items.Add(new DscpChoice { Value = kv.Key, Label = kv.Value });
            }
            _dscp.SelectedIndex = 0;
            StyleCombo(_dscp);

            _remotePort.Text = "*";
            _limit.Text = "";

            grid.Controls.Add(Cell("Rule name"), 0, 0);
            grid.Controls.Add(_name, 1, 0);
            grid.Controls.Add(Cell("Protocol"), 2, 0);
            grid.Controls.Add(_protocol, 3, 0);

            var appRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            appRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            appRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88f));
            appRow.Controls.Add(_app, 0, 0);
            _browse.Width = 80;
            _browse.Margin = new Padding(6, 2, 0, 2);
            appRow.Controls.Add(_browse, 1, 0);

            grid.Controls.Add(Cell("Application"), 0, 1);
            grid.Controls.Add(appRow, 1, 1);
            grid.Controls.Add(Cell("Remote port"), 2, 1);
            grid.Controls.Add(_remotePort, 3, 1);

            grid.Controls.Add(Cell("Priority"), 0, 2);
            grid.Controls.Add(_dscp, 1, 2);
            grid.Controls.Add(Cell("Limit (Mbit/s)"), 2, 2);
            grid.Controls.Add(_limit, 3, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 6, 0, 0)
            };
            _save.Width = 110;
            _delete.Width = 110;
            _reload.Width = 90;
            _save.Margin = new Padding(0, 0, 8, 0);
            _delete.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(_save);
            buttons.Controls.Add(_delete);
            buttons.Controls.Add(_reload);

            grid.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 3);
            grid.Controls.Add(buttons, 1, 3);
            grid.SetColumnSpan(buttons, 3);

            return grid;
        }

        private static Label Cell(string text)
        {
            var l = Theme.MakeLabel(text, true);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 8, 6, 0);
            return l;
        }

        private static void StyleBox(TextBox t)
        {
            t.BackColor = Theme.SurfaceAlt;
            t.ForeColor = Theme.Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = Theme.UiFont;
            t.Dock = DockStyle.Fill;
            t.Margin = new Padding(0, 4, 8, 4);
        }

        private static void StyleCombo(ComboBox c)
        {
            c.BackColor = Theme.SurfaceAlt;
            c.ForeColor = Theme.Text;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Theme.UiFont;
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 4, 8, 4);
        }

        private sealed class DscpChoice
        {
            public int Value;
            public string Label;
            public override string ToString() { return Label; }
        }

        public void Reload()
        {
            try
            {
                List<QosRule> rules = QosManager.GetRules();
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (QosRule r in rules)
                {
                    var item = new ListViewItem(new[]
                    {
                        r.Name, r.Application, r.Protocol, r.RemotePort, r.DscpLabel, r.ThrottleLabel
                    })
                    { Tag = r };
                    _list.Items.Add(item);
                }
                _list.EndUpdate();

                if (!QosManager.IsNlaBypassEnabled())
                {
                    _hint.Text = "Priority marking is currently inactive on this PC. Use Tools ▸ " +
                                 "Enable QoS marking to turn it on.";
                    _hint.ForeColor = Theme.Warn;
                }
                else
                {
                    _hint.Text = rules.Count + (rules.Count == 1 ? " rule" : " rules") + " active.";
                    _hint.ForeColor = Theme.TextDim;
                }
            }
            catch (Exception ex)
            {
                _hint.Text = "Could not read QoS policies: " + ex.Message;
                _hint.ForeColor = Theme.Bad;
            }
        }

        private void LoadSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var r = _list.SelectedItems[0].Tag as QosRule;
            if (r == null) return;

            _name.Text = r.Name;
            _app.Text = r.Application;
            _protocol.SelectedItem = r.Protocol;
            if (_protocol.SelectedIndex < 0) _protocol.SelectedIndex = 0;
            _remotePort.Text = r.RemotePort;

            for (int i = 0; i < _dscp.Items.Count; i++)
            {
                var c = _dscp.Items[i] as DscpChoice;
                if (c != null && c.Value == r.Dscp) { _dscp.SelectedIndex = i; break; }
            }

            _limit.Text = r.ThrottleBytesPerSecond > 0
                ? (r.ThrottleBytesPerSecond * 8.0 / 1000000.0).ToString("0.###")
                : "";
        }

        private void BrowseForApp()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Pick the program this rule applies to";
                dlg.Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                // The bare file name matches the program wherever it is
                // installed, which survives updates that move the folder.
                _app.Text = Path.GetFileName(dlg.FileName);
                if (string.IsNullOrWhiteSpace(_name.Text))
                {
                    _name.Text = Path.GetFileNameWithoutExtension(dlg.FileName) + " priority";
                }
            }
        }

        private void SaveRule()
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _hint.Text = "Give the rule a name.";
                _hint.ForeColor = Theme.Warn;
                return;
            }

            long throttle = -1;
            string limitText = (_limit.Text ?? "").Trim();
            if (limitText.Length > 0)
            {
                double mbits;
                if (!double.TryParse(limitText, out mbits) || mbits <= 0)
                {
                    _hint.Text = "The speed limit must be a positive number of Mbit/s, or empty for no limit.";
                    _hint.ForeColor = Theme.Warn;
                    return;
                }
                throttle = (long)(mbits * 1000000.0 / 8.0);   // policy wants bytes/sec
            }

            var choice = _dscp.SelectedItem as DscpChoice;
            var rule = new QosRule
            {
                Name = _name.Text.Trim(),
                Application = string.IsNullOrWhiteSpace(_app.Text) ? "*" : _app.Text.Trim(),
                Protocol = _protocol.SelectedItem as string ?? "*",
                RemotePort = string.IsNullOrWhiteSpace(_remotePort.Text) ? "*" : _remotePort.Text.Trim(),
                Dscp = choice != null ? choice.Value : 0,
                ThrottleBytesPerSecond = throttle
            };

            try
            {
                QosManager.SaveRule(rule);
                QosManager.RefreshPolicy();
                _hint.Text = "Saved \"" + rule.Name + "\".";
                _hint.ForeColor = Theme.Good;
                Reload();
            }
            catch (Exception ex)
            {
                _hint.Text = "Could not save: " + ex.Message;
                _hint.ForeColor = Theme.Bad;
            }
        }

        private void DeleteRule()
        {
            if (_list.SelectedItems.Count == 0)
            {
                _hint.Text = "Pick a rule to delete.";
                _hint.ForeColor = Theme.Warn;
                return;
            }

            var r = _list.SelectedItems[0].Tag as QosRule;
            if (r == null) return;

            DialogResult ok = MessageBox.Show(this, "Delete the rule \"" + r.Name + "\"?",
                "BandPilot", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;

            try
            {
                QosManager.DeleteRule(r.Name);
                QosManager.RefreshPolicy();
                _hint.Text = "Deleted \"" + r.Name + "\".";
                _hint.ForeColor = Theme.Good;
                Reload();
            }
            catch (Exception ex)
            {
                _hint.Text = "Could not delete: " + ex.Message;
                _hint.ForeColor = Theme.Bad;
            }
        }

        /// <summary>Pre-fills the editor from a process seen on the Monitor page.</summary>
        public void PrefillFromProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            string exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : processName + ".exe";
            _app.Text = exe;
            _name.Text = Path.GetFileNameWithoutExtension(exe) + " priority";
            _hint.Text = "Filled in from the Monitor tab. Choose a priority or a limit, then save.";
            _hint.ForeColor = Theme.TextDim;
        }
    }
}
