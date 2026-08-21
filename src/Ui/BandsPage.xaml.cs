using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BandPilot.Adapter;
using BandPilot.Wifi;

namespace BandPilot.Ui
{
    public partial class BandsPage : UserControl
    {
        private readonly MainWindow _shell;
        private readonly WifiService _wifi;
        private readonly DispatcherTimer _tick;

        private List<WifiAdapter> _adapters = new List<WifiAdapter>();
        private List<AccessPoint> _scan = new List<AccessPoint>();
        private CurrentConnection _current;
        private readonly SignalHistory _history = new SignalHistory();
        private bool _busy;
        private bool _loading;

        public WifiAdapter SelectedAdapter
        {
            get { return AdapterBox.SelectedItem as WifiAdapter; }
        }

        public BandsPage(MainWindow shell)
        {
            _shell = shell;
            _wifi = shell.Wifi;
            InitializeComponent();

            // Keeps the banner honest without re-scanning: the connection can
            // change underneath us at any time, but a full scan is expensive.
            _tick = new DispatcherTimer();
            _tick.Interval = TimeSpan.FromSeconds(5);
            _tick.Tick += (s, e) => RefreshCurrentOnly();

            Loaded += OnFirstLoad;

            // Row colours come from CLR properties on ApRow, which WPF reads
            // once at bind time. Without a rebuild the band pills and rating
            // bars keep the previous palette's brushes after a theme swap.
            ThemeManager.Changed += OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (_started) Rebuild();
        }

        public void Shutdown()
        {
            if (_tick != null) _tick.Stop();
            ThemeManager.Changed -= OnThemeChanged;
        }

        private bool _started;

        private void OnFirstLoad(object sender, RoutedEventArgs e)
        {
            if (_started) return;
            _started = true;
            LoadAdapters();
            _tick.Start();
        }

        // ------------------------------------------------------------------
        // adapters
        // ------------------------------------------------------------------

        private void LoadAdapters()
        {
            try
            {
                _adapters = _wifi.GetAdapters();
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
                return;
            }

            _loading = true;
            AdapterBox.ItemsSource = _adapters;
            AdapterBox.DisplayMemberPath = "Description";

            if (_adapters.Count > 0)
            {
                // Highest ranked rather than first found, so a built-in Wi-Fi 6E
                // card wins over a USB dongle without naming either.
                WifiAdapter best = _adapters.OrderByDescending(a => a.PreferenceRank).First();
                AdapterBox.SelectedItem = best;
            }
            _loading = false;

            if (_adapters.Count == 0)
            {
                ShowEmpty("No wireless adapter found", "BandPilot needs a Wi-Fi card to work with.");
                BtnRescan.IsEnabled = false;
                BtnConnect.IsEnabled = false;
                BtnAuto.IsEnabled = false;
                CapChipWrap.Visibility = Visibility.Collapsed;
                return;
            }

            ApplyCapability();
            Rescan();
        }

        private void OnAdapterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || SelectedAdapter == null) return;
            ApplyCapability();
            Rescan();
        }

        private void ApplyCapability()
        {
            WifiAdapter a = SelectedAdapter;
            if (a == null || a.Capability == null)
            {
                CapChipWrap.Visibility = Visibility.Collapsed;
                return;
            }

            CapChipWrap.Visibility = Visibility.Visible;
            CapChip.Text = a.Capability.CapabilityLabel;

            bool sixOk = a.Capability.Supports6Ghz;
            CapChipWrap.Background = (Brush)FindResource(sixOk ? "B.Accent2Tint" : "B.AccentTint");
            CapChip.Foreground = (Brush)FindResource(sixOk ? "B.Accent2" : "B.Accent");

            // A driver that cannot take a desired-BSSID list cannot be pinned,
            // so say so once rather than letting the button silently no-op.
            if (!a.Capability.CanPinBssid)
            {
                BtnConnect.IsEnabled = false;
                Status(a.Capability.PinWarning, "B.WarnText");
            }
        }

        // ------------------------------------------------------------------
        // scanning
        // ------------------------------------------------------------------

        private async void OnRescan(object sender, RoutedEventArgs e)
        {
            Rescan();
            await Task.Yield();
        }

        private async void Rescan()
        {
            WifiAdapter a = SelectedAdapter;
            if (a == null || _busy) return;

            _busy = true;
            SetScanning(true);
            Guid guid = a.Guid;

            try
            {
                await Task.Run(() =>
                {
                    try { _wifi.StartScan(guid); }
                    catch (Exception) { /* a refused scan still leaves the cached list usable */ }
                });

                // The driver reports scan results asynchronously; there is no
                // completion callback on this path, so this is a wait rather
                // than a poll.
                await Task.Delay(4500);

                var result = await Task.Run(() =>
                {
                    CurrentConnection cur = _wifi.GetCurrentConnection(guid);
                    List<AccessPoint> aps = _wifi.GetAccessPoints(guid, cur);
                    return new Tuple<CurrentConnection, List<AccessPoint>>(cur, aps);
                });

                _current = result.Item1;
                _scan = result.Item2;
                _history.Record(_scan);

                if (a.Capability != null) a.Capability.LearnFrom(_scan);
                ApplyCapability();

                Rebuild();
                Status(string.Empty, "B.TextDim");
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
            finally
            {
                _busy = false;
                SetScanning(false);
            }
        }

        private void SetScanning(bool on)
        {
            ScanBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ApList.Opacity = on ? 0.40 : 1.0;
            BtnRescan.IsEnabled = !on;
            BtnRescan.Content = on ? "Scanning…" : "Rescan";
            UpdateConnectEnabled();
        }

        /// <summary>
        /// Runs on the page timer. Reads the driver's cached BSS list, which is
        /// cheap and does not trigger a scan, so the trend line keeps filling in
        /// without disturbing the connection.
        /// </summary>
        private void RefreshCurrentOnly()
        {
            WifiAdapter a = SelectedAdapter;
            if (a == null || _busy) return;

            try
            {
                _current = _wifi.GetCurrentConnection(a.Guid);
                _scan = _wifi.GetAccessPoints(a.Guid, _current);
                _history.Record(_scan);
                Rebuild();
            }
            catch (Exception)
            {
                // A failed background refresh should never interrupt the page;
                // the next tick will try again.
            }
        }

        // ------------------------------------------------------------------
        // list
        // ------------------------------------------------------------------

        private void Rebuild()
        {
            // The list is rebuilt on every tick now that it carries a trend
            // line, so losing the selection each time would make the connect
            // button impossible to aim.
            ApRow previous = ApList.SelectedItem as ApRow;
            string keepBssid = previous != null ? previous.Bssid : null;

            UpdateBanner();

            WifiAdapter a = SelectedAdapter;
            bool six = a != null && a.Capability != null && a.Capability.Supports6Ghz;
            bool sixUnknown = a != null && a.Capability != null && a.Capability.SixGhzUnknown;

            IEnumerable<AccessPoint> source = _scan;
            if (OnlyMine.IsChecked == true && _current != null && _current.Connected
                && !string.IsNullOrEmpty(_current.Ssid))
            {
                source = source.Where(x => x.Ssid == _current.Ssid);
            }

            List<AccessPoint> list = source.Where(x => !string.IsNullOrEmpty(x.Ssid)).ToList();

            if (list.Count == 0)
            {
                ApList.ItemsSource = null;
                ShowEmpty("No networks in range", "Move closer to an access point, or rescan.");
                UpdateConnectEnabled();
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;

            // Connected network first, then by strongest radio, so the network
            // being used is always at the top of the list.
            var groups = list
                .GroupBy(x => x.Ssid)
                .Select(g => new
                {
                    Ssid = g.Key,
                    Items = g.OrderByDescending(x => x.Score).ToList(),
                    Connected = _current != null && _current.Connected && g.Key == _current.Ssid
                })
                .OrderByDescending(g => g.Connected)
                .ThenByDescending(g => g.Items[0].Score)
                .ToList();

            var rows = new List<ListRow>();
            foreach (var g in groups)
            {
                rows.Add(new GroupRow
                {
                    Ssid = g.Ssid,
                    RadioCount = g.Items.Count,
                    NetworkIsConnected = g.Connected
                });

                // "Usable" excludes 6 GHz radios this card cannot join, so the
                // best-available badge never points at something unreachable.
                // Unknown 6 GHz support counts as usable: refusing to recommend
                // a 6 GHz AP on a card that may well support it is the worse error.
                AccessPoint best = g.Items
                    .Where(x => x.Band != WifiBand.Band6 || six || sixUnknown)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                foreach (AccessPoint ap in g.Items)
                {
                    bool unusable = ap.Band == WifiBand.Band6 && !six && !sixUnknown;
                    rows.Add(new ApRow
                    {
                        Ap = ap,
                        Current = ap.IsCurrent,
                        IsBest = best != null && ReferenceEquals(ap, best) && !ap.IsCurrent,
                        Unusable = unusable,
                        History = _history.For(ap.Bssid),
                        Spread = _history.SpreadFor(ap.Bssid)
                    });
                }
            }

            ApList.ItemsSource = rows;
            if (keepBssid != null)
            {
                foreach (ApRow candidate in rows.OfType<ApRow>())
                {
                    if (string.Equals(candidate.Bssid, keepBssid, StringComparison.OrdinalIgnoreCase))
                    {
                        ApList.SelectedItem = candidate;
                        break;
                    }
                }
            }

            UpdateVerdict(groups.FirstOrDefault(g => g.Connected)?.Items, six, sixUnknown);
            UpdateConnectEnabled();
        }

        private void ShowEmpty(string title, string body)
        {
            EmptyTitle.Text = title;
            EmptyBody.Text = body;
            EmptyState.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------
        // banner and verdict
        // ------------------------------------------------------------------

        private void UpdateBanner()
        {
            bool on = _current != null && _current.Connected;

            BannerSsid.Text = on ? _current.Ssid : "Not connected";
            BannerPillWrap.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            StatsRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            if (!on)
            {
                VerdictWrap.Visibility = Visibility.Collapsed;
                return;
            }

            AccessPoint me = _scan.FirstOrDefault(x => x.IsCurrent);
            string bandText = me != null ? me.BandLabel : "";

            BannerPill.Text = string.IsNullOrEmpty(bandText) ? "connected" : "connected · " + bandText;
            StatBand.Text = me != null ? me.BandLabel + ", ch " + me.Channel : "—";
            StatSignal.Text = me != null
                ? _current.SignalQuality + "% · " + me.RssiDbm + " dBm"
                : _current.SignalQuality + "%";
            StatGen.Text = BandTools.PhyLabel(_current.Phy);
            StatRate.Text = Mbps(_current.RxRateKbps) + " down · " + Mbps(_current.TxRateKbps) + " up";
            StatBssid.Text = _current.Bssid ?? "—";
        }

        private static string Mbps(uint kbps)
        {
            return (kbps / 1000).ToString() + " Mbps";
        }

        /// <summary>
        /// The product's whole argument in one strip: if a better radio is
        /// available on the network you are already on, say so in plain words
        /// and offer the one-click fix.
        /// </summary>
        private void UpdateVerdict(List<AccessPoint> myNetwork, bool six, bool sixUnknown)
        {
            BtnSwitch.Visibility = Visibility.Collapsed;

            if (myNetwork == null || _current == null || !_current.Connected)
            {
                VerdictWrap.Visibility = Visibility.Collapsed;
                return;
            }

            List<AccessPoint> usable = myNetwork
                .Where(x => x.Band != WifiBand.Band6 || six || sixUnknown)
                .OrderByDescending(x => x.Score)
                .ToList();

            AccessPoint me = usable.FirstOrDefault(x => x.IsCurrent);
            if (me == null || usable.Count < 2)
            {
                VerdictWrap.Visibility = Visibility.Collapsed;
                return;
            }

            int rank = usable.IndexOf(me);
            VerdictWrap.Visibility = Visibility.Visible;

            if (rank == 0)
            {
                VerdictRail.Background = (Brush)FindResource("B.Good");
                VerdictText.Text = "You are on the best radio this card can use here.";
                return;
            }

            AccessPoint best = usable[0];
            VerdictRail.Background = (Brush)FindResource("B.WarnRail");
            VerdictText.Text = string.Format(
                "Full signal does not mean fast. This is the {0} best of {1} usable radios here — "
                + "the {2} radio on channel {3} rates {4} against your {5}.",
                Ordinal(rank + 1), usable.Count, best.BandLabel, best.Channel, best.Score, me.Score);

            BtnSwitch.Content = "Switch to " + best.BandLabel + " ch " + best.Channel;
            BtnSwitch.Tag = best;
            BtnSwitch.Visibility = SelectedAdapter != null && SelectedAdapter.Capability != null
                && !SelectedAdapter.Capability.CanPinBssid
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static string Ordinal(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13) return n + "th";
            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }

        // ------------------------------------------------------------------
        // actions
        // ------------------------------------------------------------------

        private void OnFilterToggled(object sender, RoutedEventArgs e)
        {
            Rebuild();
        }

        private void OnRowSelected(object sender, SelectionChangedEventArgs e)
        {
            UpdateConnectEnabled();
        }

        private void UpdateConnectEnabled()
        {
            bool pinnable = SelectedAdapter == null
                || SelectedAdapter.Capability == null
                || SelectedAdapter.Capability.CanPinBssid;

            ApRow row = ApList.SelectedItem as ApRow;
            BtnConnect.IsEnabled = !_busy && pinnable
                && (row != null ? !row.Unusable : ApList.Items.Count > 0);
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ApRow row = ApList.SelectedItem as ApRow;
            if (row != null) Connect(row);
        }

        private void OnConnect(object sender, RoutedEventArgs e)
        {
            ApRow row = ApList.SelectedItem as ApRow;
            if (row == null)
            {
                // No explicit pick: take the best usable radio, which is what
                // the button implies when nothing is selected.
                row = ApList.Items.OfType<ApRow>().FirstOrDefault(r => r.IsBest)
                   ?? ApList.Items.OfType<ApRow>().FirstOrDefault(r => !r.Unusable);
            }
            if (row != null) Connect(row);
        }

        private void OnSwitchToBest(object sender, RoutedEventArgs e)
        {
            AccessPoint ap = BtnSwitch.Tag as AccessPoint;
            if (ap == null) return;

            ApRow row = ApList.Items.OfType<ApRow>().FirstOrDefault(r => ReferenceEquals(r.Ap, ap));
            if (row != null) Connect(row);
        }

        private async void Connect(ApRow row)
        {
            WifiAdapter a = SelectedAdapter;
            if (a == null || _busy) return;

            if (row.Unusable)
            {
                _shell.ShowNotice("6 GHz is not available on this card",
                    "This adapter has no 6 GHz radio, so it cannot join "
                    + row.Ap.Ssid + " on " + row.Ap.BandLabel + ". The 5 GHz and 2.4 GHz "
                    + "radios on the same network are still available.");
                return;
            }

            string profile = FindProfile(a.Guid, row.Ap.Ssid);
            if (profile == null)
            {
                _shell.ShowNotice("No saved profile for this network",
                    "There is no saved Windows profile for " + row.Ap.Ssid
                    + ". Connect to it once through the normal Windows network list, then "
                    + "come back here to choose its band.");
                return;
            }

            _busy = true;
            row.Connecting = true;
            Status("Connecting to " + row.Ap.BandLabel + " channel " + row.Ap.Channel + " …", "B.Accent");
            UpdateConnectEnabled();

            try
            {
                Guid guid = a.Guid;
                byte[] bssid = row.Ap.BssidBytes;
                await Task.Run(() => _wifi.ConnectToBssid(guid, profile, bssid));

                // Association takes a moment; re-reading immediately would just
                // report the old radio.
                await Task.Delay(3500);
                _current = _wifi.GetCurrentConnection(guid);
                _scan = _wifi.GetAccessPoints(guid, _current);

                Rebuild();
                Status(string.Empty, "B.TextDim");
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
            finally
            {
                row.Connecting = false;
                _busy = false;
                UpdateConnectEnabled();
            }
        }

        private string FindProfile(Guid adapter, string ssid)
        {
            try
            {
                List<string> profiles = _wifi.GetProfileNames(adapter);
                return profiles.FirstOrDefault(p =>
                    string.Equals(p, ssid, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async void OnBackToAuto(object sender, RoutedEventArgs e)
        {
            WifiAdapter a = SelectedAdapter;
            if (a == null || _busy) return;

            string ssid = _current != null ? _current.Ssid : null;
            string profile = ssid != null ? FindProfile(a.Guid, ssid) : null;
            if (profile == null)
            {
                Status("Nothing to hand back — not connected to a saved network.", "B.WarnText");
                return;
            }

            _busy = true;
            Status("Handing the choice back to Windows …", "B.Accent");
            try
            {
                Guid guid = a.Guid;
                await Task.Run(() => _wifi.ConnectAuto(guid, profile));
                await Task.Delay(3500);
                _current = _wifi.GetCurrentConnection(guid);
                _scan = _wifi.GetAccessPoints(guid, _current);
                Rebuild();
                Status(string.Empty, "B.TextDim");
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
            finally
            {
                _busy = false;
                UpdateConnectEnabled();
            }
        }

        /// <summary>
        /// Pinning a BSSID only holds until the driver decides to roam, so this
        /// is the other half of the feature: find whatever this particular
        /// driver calls its roaming control and turn it down.
        /// </summary>
        private async void OnHoldRadio(object sender, RoutedEventArgs e)
        {
            WifiAdapter a = SelectedAdapter;
            if (a == null || _busy) return;

            BtnHold.IsEnabled = false;
            Status("Looking for this driver's roaming setting…", "B.TextDim");

            try
            {
                Guid guid = a.Guid;
                string desc = a.Description;

                var found = await Task.Run(() =>
                {
                    string key = AdapterRegistry.FindInstanceKey(guid, desc);
                    if (key == null) return null;
                    return RoamingLock.Find(AdapterRegistry.Read(key));
                });

                if (found == null)
                {
                    Status("Could not find this radio's driver key in the registry.", "B.WarnText");
                    return;
                }

                if (found.Count == 0)
                {
                    _shell.ShowNotice("No roaming control on this driver",
                        "This driver does not expose a roaming setting BandPilot can recognise, "
                        + "so there is nothing to hold down. Your pinned radio will still last "
                        + "for the current connection — it just cannot be made to stick across "
                        + "a roam.\n\nThe Adapter page lists everything this driver does expose.");
                    Status(string.Empty, "B.TextDim");
                    return;
                }

                var pending = found.FindAll(c => !c.AlreadySet);
                if (pending.Count == 0)
                {
                    Status("Roaming is already turned down as far as this driver allows.", "B.Good");
                    return;
                }

                string list = string.Join("\n", pending.ConvertAll(c => "    " + c.Describe()));
                _shell.ShowConfirm(
                    "Hold this radio",
                    "Turn down this driver's roaming so Windows stops drifting off the access "
                    + "point you chose:\n\n" + list
                    + "\n\nThe Wi-Fi connection will drop briefly while the radio restarts. "
                    + "You can undo it any time from the Adapter page.",
                    "Hold the radio",
                    () => ApplyHold(pending));
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
            finally
            {
                BtnHold.IsEnabled = true;
            }
        }

        private async void ApplyHold(System.Collections.Generic.List<RoamingLock.Candidate> pending)
        {
            WifiAdapter a = SelectedAdapter;
            if (a == null) return;

            BtnHold.IsEnabled = false;
            Status("Holding the radio…", "B.Accent");

            try
            {
                Guid guid = a.Guid;
                string desc = a.Description;

                int applied = await Task.Run(() =>
                {
                    string name = AdapterProperties.ResolveAdapterName(guid, desc);
                    if (name == null)
                    {
                        throw new InvalidOperationException(
                            "Could not match this radio to a Windows network adapter.");
                    }

                    int count = 0;
                    foreach (RoamingLock.Candidate c in pending)
                    {
                        AdapterProperties.SetAdvanced(name, c.Property.RegistryKeyword, c.TargetValue);
                        count++;
                    }
                    return count;
                });

                Status(applied == 1
                    ? "Roaming turned down. Your chosen radio should now stick."
                    : applied + " roaming settings turned down. Your chosen radio should now stick.",
                    "B.Good");
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
            finally
            {
                BtnHold.IsEnabled = true;
            }
        }

        private void Status(string text, string brushKey)
        {
            StatusLine.Text = text ?? string.Empty;
            StatusLine.Foreground = (Brush)FindResource(brushKey);
        }
    }
}
