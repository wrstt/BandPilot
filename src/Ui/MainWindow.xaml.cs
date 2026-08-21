using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BandPilot.Qos;
using BandPilot.Wifi;

namespace BandPilot.Ui
{
    public partial class MainWindow : Window
    {
        private readonly WifiService _wifi;

        private BandsPage _bands;
        private AdapterPage _adapter;
        private PriorityPage _priority;
        private MonitorPage _monitor;
        private AboutPage _about;

        private Action _modalConfirm;

        public WifiService Wifi { get { return _wifi; } }

        public MainWindow(WifiService wifi)
        {
            _wifi = wifi;
            InitializeComponent();

            _bands = new BandsPage(this);
            PageHost.Content = _bands;

            RefreshQosIndicator();
        }

        // ------------------------------------------------------------------
        // window chrome
        // ------------------------------------------------------------------

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Windows 11 rounds the corners for us when asked. On Windows 10 the
            // call simply fails and the window stays square, which is the
            // native look there anyway, so the failure needs no handling.
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch (Exception) { }
        }

        private void OnMinimise(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximise(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Glyphs are "maximise" and "restore" from Segoe MDL2 Assets.
            BtnMax.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_monitor != null) _monitor.StopMonitoring();
            if (_bands != null) _bands.Shutdown();
            if (_wifi != null) _wifi.Dispose();
            base.OnClosed(e);
        }

        // ------------------------------------------------------------------
        // navigation
        // ------------------------------------------------------------------

        private void OnNavChecked(object sender, RoutedEventArgs e)
        {
            // Fires once during InitializeComponent, before the pages exist.
            if (PageHost == null) return;

            // An ETW session is a machine-wide resource. Leaving it open while
            // the user is on another page kept a kernel trace running, and the
            // per-second timer ticking, for no benefit.
            if (_monitor != null && sender != NavTraffic) _monitor.Suspend();

            if (sender == NavBands)
            {
                if (_bands == null) _bands = new BandsPage(this);
                PageHost.Content = _bands;
            }
            else if (sender == NavAdapter)
            {
                if (_adapter == null) _adapter = new AdapterPage(this);
                PageHost.Content = _adapter;
                _adapter.OnShown(SelectedAdapter);
            }
            else if (sender == NavPriority)
            {
                if (_priority == null) _priority = new PriorityPage(this);
                PageHost.Content = _priority;
                _priority.OnShown();
            }
            else if (sender == NavTraffic)
            {
                if (_monitor == null) _monitor = new MonitorPage(this);
                PageHost.Content = _monitor;
            }
            else if (sender == NavAbout)
            {
                if (_about == null) _about = new AboutPage();
                PageHost.Content = _about;
            }
        }

        /// <summary>The adapter chosen on the Bands page, shared with Adapter.</summary>
        public WifiAdapter SelectedAdapter
        {
            get { return _bands != null ? _bands.SelectedAdapter : null; }
        }

        /// <summary>
        /// Used by Live traffic's "Create a rule for this app", which is the one
        /// place a page needs to hand work to another page.
        /// </summary>
        public void GoToPriorityWith(string exeName)
        {
            if (_priority == null) _priority = new PriorityPage(this);
            NavPriority.IsChecked = true;
            _priority.PrefillApplication(exeName);
        }

        // ------------------------------------------------------------------
        // modal
        // ------------------------------------------------------------------

        /// <summary>A message with a single dismiss button.</summary>
        public void ShowNotice(string title, string body, string note = null, string code = null)
        {
            Prepare(title, body, note, code);
            ModalCancel.Visibility = Visibility.Collapsed;
            ModalConfirm.Content = "Close";
            ModalConfirm.Style = (Style)FindResource("Btn.Secondary");
            _modalConfirm = null;
            ModalLayer.Visibility = Visibility.Visible;
        }

        /// <summary>A question with Cancel plus a named confirm action.</summary>
        public void ShowConfirm(string title, string body, string confirmText, Action onConfirm,
                                bool danger = false, string note = null, string code = null)
        {
            Prepare(title, body, note, code);
            ModalCancel.Visibility = Visibility.Visible;
            ModalConfirm.Content = confirmText;
            ModalConfirm.Style = (Style)FindResource(danger ? "Btn.Danger" : "Btn.Primary");
            _modalConfirm = onConfirm;
            ModalLayer.Visibility = Visibility.Visible;
        }

        private void Prepare(string title, string body, string note, string code)
        {
            ModalTitle.Text = title;
            ModalBody.Text = body;

            ModalNote.Text = note ?? string.Empty;
            ModalNote.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;

            ModalCode.Text = code ?? string.Empty;
            ModalCodeWrap.Visibility = string.IsNullOrEmpty(code) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnModalCancel(object sender, RoutedEventArgs e)
        {
            ModalLayer.Visibility = Visibility.Collapsed;
            _modalConfirm = null;
        }

        private void OnModalConfirm(object sender, RoutedEventArgs e)
        {
            Action act = _modalConfirm;
            ModalLayer.Visibility = Visibility.Collapsed;
            _modalConfirm = null;
            if (act != null) act();
        }

        // ------------------------------------------------------------------
        // QoS switch
        // ------------------------------------------------------------------

        private async void EnableQosMarking()
        {
            try
            {
                QosManager.EnableNlaBypass();
                RefreshQosIndicator();
                if (_priority != null) _priority.OnShown();
            }
            catch (Exception ex)
            {
                ShowNotice("Could not enable QoS marking", ex.Message);
                return;
            }

            // Off the dispatcher thread: gpupdate is slow enough to look like a
            // hang if it runs here.
            string problem = await System.Threading.Tasks.Task.Run(
                () => QosManager.RefreshPolicy());

            if (problem != null)
            {
                ShowNotice("QoS marking enabled",
                    "The switch is set, but Windows would not refresh its policy just now: "
                    + problem + "\n\nRestarting will apply it regardless.");
            }
        }

        public void RefreshQosIndicator()
        {
            bool on = false;
            try { on = QosManager.IsNlaBypassEnabled(); }
            catch (Exception) { }

            QosDot.Fill = (System.Windows.Media.Brush)FindResource(on ? "B.Good" : "B.TextFaint");
            QosLabel.Text = on ? "QoS marking enabled" : "Enable QoS marking";
        }

        private void OnQosClick(object sender, RoutedEventArgs e)
        {
            bool on = false;
            try { on = QosManager.IsNlaBypassEnabled(); }
            catch (Exception) { }

            if (on)
            {
                ShowNotice("QoS marking is already on",
                    "Windows is honouring the priority rules on this PC. Nothing further is needed.");
                return;
            }

            ShowConfirm(
                "Enable QoS marking",
                "Windows ignores priority rules on PCs that are not domain-joined until a "
                + "registry switch is set. BandPilot will set it for you:",
                "Enable QoS marking",
                () => EnableQosMarking(),
                false,
                "A restart is needed before rules take effect.",
                "HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\QoS\n\"Do not use NLA\" = 1");
        }
    }
}
