using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BandPilot.Wifi;

namespace BandPilot.Ui
{
    /// <summary>
    /// The band / access-point picker.
    ///
    /// On a large network every band and every AP shares one SSID, and Windows
    /// picks for you with no way to see or override the choice. This page lists
    /// each radio separately and pins the connection to the one you choose.
    /// </summary>
    public sealed class BandsPage : UserControl
    {
        private readonly WifiService _wifi;
        private readonly ComboBox _adapters = new ComboBox();
        private readonly Label _status = new Label();
        private readonly Label _detail = new Label();
        private readonly ListView _list = new ListView();
        private readonly Button _rescan;
        private readonly Button _pin;
        private readonly Button _auto;
        private readonly CheckBox _onlyCurrentSsid = new CheckBox();
        private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer();

        private List<WifiAdapter> _adapterList = new List<WifiAdapter>();
        private CurrentConnection _current;
        private List<string> _profiles = new List<string>();

        public event EventHandler<WifiAdapter> AdapterChanged;

        public BandsPage(WifiService wifi)
        {
            _wifi = wifi;
            _rescan = Theme.MakeButton("Rescan", false);
            _pin = Theme.MakeButton("Connect to this access point", true);
            _auto = Theme.MakeButton("Back to automatic", false);

            BackColor = Theme.Background;
            Dock = DockStyle.Fill;
            Padding = new Padding(16);

            BuildLayout();
            WireEvents();

            _refreshTimer.Interval = 5000;
            _refreshTimer.Tick += (s, e) => RefreshCurrentOnly();
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

            // --- adapter row -------------------------------------------------
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Background
            };
            Label lbl = Theme.MakeLabel("Wi-Fi adapter", true);
            lbl.Margin = new Padding(0, 8, 8, 0);
            top.Controls.Add(lbl);

            _adapters.DropDownStyle = ComboBoxStyle.DropDownList;
            _adapters.Width = 420;
            _adapters.BackColor = Theme.SurfaceAlt;
            _adapters.ForeColor = Theme.Text;
            _adapters.FlatStyle = FlatStyle.Flat;
            _adapters.Font = Theme.UiFont;
            _adapters.Margin = new Padding(0, 4, 12, 0);
            top.Controls.Add(_adapters);

            _onlyCurrentSsid.Text = "Only show the network I'm on";
            _onlyCurrentSsid.ForeColor = Theme.TextDim;
            _onlyCurrentSsid.Font = Theme.UiFont;
            _onlyCurrentSsid.AutoSize = true;
            _onlyCurrentSsid.Checked = false;
            _onlyCurrentSsid.Margin = new Padding(0, 8, 0, 0);
            top.Controls.Add(_onlyCurrentSsid);

            root.Controls.Add(top, 0, 0);

            // --- current connection banner -----------------------------------
            var banner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                Padding = new Padding(12, 8, 12, 8),
                Margin = new Padding(0, 4, 0, 8)
            };
            _status.Text = "Not connected";
            _status.ForeColor = Theme.Text;
            _status.Font = Theme.TitleFont;
            _status.AutoSize = true;
            _status.Location = new Point(12, 8);
            banner.Controls.Add(_status);

            _detail.Text = "";
            _detail.ForeColor = Theme.TextDim;
            _detail.Font = Theme.UiFont;
            _detail.AutoSize = true;
            _detail.Location = new Point(12, 38);
            banner.Controls.Add(_detail);

            root.Controls.Add(banner, 0, 1);

            // --- access point list -------------------------------------------
            Theme.StyleList(_list);
            _list.Dock = DockStyle.Fill;
            _list.ShowGroups = true;
            _list.Columns.Add("Band", 90);
            _list.Columns.Add("Ch", 55, HorizontalAlignment.Right);
            _list.Columns.Add("Signal", 90);
            _list.Columns.Add("dBm", 60, HorizontalAlignment.Right);
            _list.Columns.Add("Generation", 110);
            _list.Columns.Add("Access point (BSSID)", 170);
            _list.Columns.Add("Rating", 70, HorizontalAlignment.Right);
            root.Controls.Add(_list, 0, 2);

            // --- actions ------------------------------------------------------
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Background,
                Padding = new Padding(0, 8, 0, 0)
            };
            _rescan.Width = 110;
            _pin.Width = 240;
            _auto.Width = 170;
            _rescan.Margin = new Padding(0, 0, 8, 0);
            _pin.Margin = new Padding(0, 0, 8, 0);
            actions.Controls.Add(_rescan);
            actions.Controls.Add(_pin);
            actions.Controls.Add(_auto);
            root.Controls.Add(actions, 0, 3);

            Controls.Add(root);
        }

        private void WireEvents()
        {
            _adapters.SelectedIndexChanged += (s, e) =>
            {
                WifiAdapter a = Selected;
                if (a != null && AdapterChanged != null) AdapterChanged(this, a);
                RefreshAll(false);
            };
            _onlyCurrentSsid.CheckedChanged += (s, e) => RefreshAll(false);
            _rescan.Click += (s, e) => RefreshAll(true);
            _pin.Click += (s, e) => PinSelected();
            _auto.Click += (s, e) => GoAutomatic();
            _list.DoubleClick += (s, e) => PinSelected();
        }

        public WifiAdapter Selected
        {
            get { return _adapters.SelectedItem as WifiAdapter; }
        }

        public void Initialise()
        {
            try
            {
                _adapterList = _wifi.GetAdapters();
            }
            catch (Exception ex)
            {
                ShowError("Could not list Wi-Fi adapters", ex);
                return;
            }

            _adapters.Items.Clear();
            foreach (WifiAdapter a in _adapterList) _adapters.Items.Add(a);

            if (_adapterList.Count == 0)
            {
                _status.Text = "No Wi-Fi adapter found";
                _detail.Text = "BandPilot needs a wireless adapter and the WLAN AutoConfig service.";
                return;
            }

            // Prefer the Wi-Fi 7 part when the machine has more than one radio.
            int preferred = _adapterList.FindIndex(a => a.LooksLikeBe2xx);
            _adapters.SelectedIndex = preferred >= 0 ? preferred : 0;

            RefreshAll(true);
            _refreshTimer.Start();
        }

        private void RefreshCurrentOnly()
        {
            WifiAdapter a = Selected;
            if (a == null) return;
            try
            {
                _current = _wifi.GetCurrentConnection(a.Guid);
                UpdateBanner();
                MarkCurrentInList();
            }
            catch (Exception) { /* transient during a roam */ }
        }

        private void RefreshAll(bool triggerScan)
        {
            WifiAdapter a = Selected;
            if (a == null) return;

            Cursor = Cursors.WaitCursor;
            _rescan.Enabled = false;
            try
            {
                _current = _wifi.GetCurrentConnection(a.Guid);
                _profiles = _wifi.GetProfileNames(a.Guid);
                UpdateBanner();
                PopulateList();

                if (triggerScan)
                {
                    // A driver scan takes a few seconds. The cached list is
                    // already on screen, so refresh again once it lands rather
                    // than blocking the UI thread.
                    try { _wifi.StartScan(a.Guid); }
                    catch (Exception) { /* radio busy; cached results still shown */ }

                    var t = new System.Windows.Forms.Timer { Interval = 4500 };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        t.Dispose();
                        try
                        {
                            _current = _wifi.GetCurrentConnection(a.Guid);
                            UpdateBanner();
                            PopulateList();
                        }
                        catch (Exception) { }
                        _rescan.Enabled = true;
                    };
                    t.Start();
                }
            }
            catch (Exception ex)
            {
                ShowError("Could not read the Wi-Fi state", ex);
                _rescan.Enabled = true;
            }
            finally
            {
                Cursor = Cursors.Default;
                if (!triggerScan) _rescan.Enabled = true;
            }
        }

        private void UpdateBanner()
        {
            if (_current == null || !_current.Connected)
            {
                _status.Text = "Not connected";
                _status.ForeColor = Theme.TextDim;
                _detail.Text = "Join a network in Windows first, then come back to choose its band.";
                return;
            }

            // The band and channel are only known once the AP list has been
            // populated, since the connection API itself reports neither.
            string bandText = "";
            foreach (ListViewItem item in _list.Items)
            {
                var ap = item.Tag as AccessPoint;
                if (ap != null && ap.IsCurrent)
                {
                    bandText = ap.BandLabel + ", channel " + ap.Channel + "  ·  ";
                    break;
                }
            }

            _status.Text = _current.Ssid;
            _status.ForeColor = Theme.Text;
            _detail.Text = string.Format(
                "{0}{1}  ·  signal {2}%  ·  {3} down / {4} up  ·  AP {5}",
                bandText,
                BandTools.PhyLabel(_current.Phy),
                _current.SignalQuality,
                FormatRate(_current.RxRateKbps),
                FormatRate(_current.TxRateKbps),
                _current.Bssid);
        }

        private static string FormatRate(uint kbps)
        {
            if (kbps >= 1000) return (kbps / 1000.0).ToString("0") + " Mbps";
            return kbps + " kbps";
        }

        private void PopulateList()
        {
            WifiAdapter a = Selected;
            if (a == null) return;

            List<AccessPoint> aps;
            try
            {
                aps = _wifi.GetAccessPoints(a.Guid, _current);
            }
            catch (Exception ex)
            {
                ShowError("Could not read the access point list", ex);
                return;
            }

            if (_onlyCurrentSsid.Checked && _current != null && _current.Connected)
            {
                aps = aps.Where(x => string.Equals(x.Ssid, _current.Ssid, StringComparison.Ordinal)).ToList();
            }

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                _list.Groups.Clear();

                // Networks you are on or could plausibly use first, then the
                // rest by strength. Within a network, best radio first.
                IEnumerable<IGrouping<string, AccessPoint>> bySsid = aps
                    .GroupBy(x => x.Ssid ?? "")
                    .OrderByDescending(g => g.Any(x => x.IsCurrent))
                    .ThenByDescending(g => g.Max(x => x.Score));

                foreach (IGrouping<string, AccessPoint> g in bySsid)
                {
                    int radios = g.Count();
                    string header = g.Key + "   (" + radios + (radios == 1 ? " radio" : " radios") + ")";
                    if (g.Any(x => x.IsCurrent)) header += "   — connected";

                    var group = new ListViewGroup(header) { HeaderAlignment = HorizontalAlignment.Left };
                    _list.Groups.Add(group);

                    foreach (AccessPoint ap in g.OrderByDescending(x => x.Score))
                    {
                        var item = new ListViewItem(new[]
                        {
                            (ap.IsCurrent ? "● " : "   ") + ap.BandLabel,
                            ap.Channel > 0 ? ap.Channel.ToString() : "-",
                            Theme.Bars(ap.Bars),
                            ap.RssiDbm.ToString(),
                            ap.PhyLabel,
                            ap.Bssid,
                            ap.Score.ToString()
                        })
                        {
                            Tag = ap,
                            Group = group,
                            UseItemStyleForSubItems = false
                        };

                        item.SubItems[0].ForeColor = Theme.BandColor(ap.Band);
                        item.SubItems[2].ForeColor = Theme.ScoreColor(ap.Score);
                        item.SubItems[6].ForeColor = Theme.ScoreColor(ap.Score);
                        item.SubItems[5].Font = Theme.MonoFont;

                        if (ap.IsCurrent)
                        {
                            item.BackColor = Theme.SurfaceAlt;
                            item.SubItems[0].Font = Theme.UiFontBold;
                        }

                        _list.Items.Add(item);
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            UpdateBanner();
        }

        private void MarkCurrentInList()
        {
            foreach (ListViewItem item in _list.Items)
            {
                var ap = item.Tag as AccessPoint;
                if (ap == null) continue;

                bool isNow = _current != null && _current.Connected &&
                             BandTools.MacEquals(ap.BssidBytes, _current.BssidBytes);
                if (isNow == ap.IsCurrent) continue;

                ap.IsCurrent = isNow;
                item.SubItems[0].Text = (isNow ? "● " : "   ") + ap.BandLabel;
                item.BackColor = isNow ? Theme.SurfaceAlt : Theme.Surface;
            }
        }

        private void PinSelected()
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Pick an access point from the list first.",
                    "BandPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ap = _list.SelectedItems[0].Tag as AccessPoint;
            WifiAdapter a = Selected;
            if (ap == null || a == null) return;

            string profile = FindProfile(ap.Ssid);
            if (profile == null)
            {
                MessageBox.Show(this,
                    "There is no saved Windows profile for \"" + ap.Ssid + "\".\n\n" +
                    "Connect to it once through the normal Windows network list " +
                    "(so the password is stored), then come back here to choose its band.",
                    "BandPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _wifi.ConnectToBssid(a.Guid, profile, ap.BssidBytes);
                _detail.Text = "Connecting to " + ap.BandLabel + " channel " + ap.Channel +
                               " on " + ap.Bssid + " ...";

                var t = new System.Windows.Forms.Timer { Interval = 3500 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); RefreshAll(false); };
                t.Start();
            }
            catch (Exception ex)
            {
                ShowError("Could not connect to that access point", ex);
            }
        }

        private void GoAutomatic()
        {
            WifiAdapter a = Selected;
            if (a == null) return;

            string ssid = _current != null && _current.Connected ? _current.Ssid : null;
            string profile = ssid != null ? FindProfile(ssid) : null;
            if (profile == null)
            {
                MessageBox.Show(this, "Connect to a network first.", "BandPilot",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _wifi.ConnectAuto(a.Guid, profile);
                _detail.Text = "Letting Windows choose the access point again ...";
                var t = new System.Windows.Forms.Timer { Interval = 3500 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); RefreshAll(false); };
                t.Start();
            }
            catch (Exception ex)
            {
                ShowError("Could not reconnect", ex);
            }
        }

        /// <summary>
        /// Profile names normally equal the SSID, but a manually renamed
        /// profile will not, so fall back to a case-insensitive match.
        /// </summary>
        private string FindProfile(string ssid)
        {
            if (string.IsNullOrEmpty(ssid)) return null;
            foreach (string p in _profiles)
            {
                if (string.Equals(p, ssid, StringComparison.Ordinal)) return p;
            }
            foreach (string p in _profiles)
            {
                if (string.Equals(p, ssid, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        private void ShowError(string what, Exception ex)
        {
            MessageBox.Show(this, what + ":\n\n" + ex.Message, "BandPilot",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
