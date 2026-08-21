using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BandPilot.Qos;
using BandPilot.Wifi;

namespace BandPilot.Ui
{
    public sealed class MainForm : Form
    {
        private readonly WifiService _wifi;

        private readonly Panel _content = new Panel();
        private readonly Panel _nav = new Panel();
        private readonly List<Button> _navButtons = new List<Button>();

        private BandsPage _bands;
        private AdapterPage _adapter;
        private PriorityPage _priority;
        private MonitorPage _monitor;
        private Control _aboutPage;

        public MainForm(WifiService wifi)
        {
            _wifi = wifi;

            Text = "BandPilot";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 640);
            Size = new Size(1120, 720);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;

            BuildPages();
            BuildChrome();

            Shown += (s, e) => _bands.Initialise();
        }

        private void BuildPages()
        {
            _bands = new BandsPage(_wifi);
            _adapter = new AdapterPage();
            _priority = new PriorityPage();
            _monitor = new MonitorPage();
            _aboutPage = BuildAbout();

            _bands.AdapterChanged += (s, a) => _adapter.SetAdapter(a);
            _monitor.PrioritiseRequested += (s, procName) =>
            {
                _priority.PrefillFromProcess(procName);
                Show(2);
            };
        }

        private void BuildChrome()
        {
            _content.Dock = DockStyle.Fill;
            _content.BackColor = Theme.Background;

            _nav.Dock = DockStyle.Left;
            _nav.Width = 208;
            _nav.BackColor = Theme.Surface;
            _nav.Padding = new Padding(0, 16, 0, 0);

            Label brand = Theme.MakeLabel("BandPilot", false);
            brand.Font = new Font("Segoe UI Semibold", 15f);
            brand.Location = new Point(20, 18);
            _nav.Controls.Add(brand);

            Label sub = Theme.MakeLabel("Wi-Fi control for Intel BE2xx", true);
            sub.Font = new Font("Segoe UI", 8f);
            sub.Location = new Point(20, 44);
            sub.MaximumSize = new Size(170, 0);
            _nav.Controls.Add(sub);

            string[] names = { "Bands & APs", "Adapter", "Priority & limits", "Live traffic", "About" };
            int y = 84;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var b = new Button
                {
                    Text = "   " + names[i],
                    TextAlign = ContentAlignment.MiddleLeft,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.Surface,
                    ForeColor = Theme.TextDim,
                    Font = Theme.UiFont,
                    Location = new Point(0, y),
                    Size = new Size(208, 40),
                    Cursor = Cursors.Hand,
                    UseVisualStyleBackColor = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (s, e) => Show(index);
                _nav.Controls.Add(b);
                _navButtons.Add(b);
                y += 42;
            }

            var tools = Theme.MakeButton("Enable QoS marking", false);
            tools.Location = new Point(16, y + 24);
            tools.Size = new Size(176, 32);
            tools.Click += (s, e) => EnableQosMarking();
            _nav.Controls.Add(tools);

            Controls.Add(_content);
            Controls.Add(_nav);

            Show(0);
        }

        private void Show(int index)
        {
            Control page;
            switch (index)
            {
                case 1: page = _adapter; break;
                case 2: page = _priority; _priority.Reload(); break;
                case 3: page = _monitor; break;
                case 4: page = _aboutPage; break;
                default: page = _bands; break;
            }

            _content.SuspendLayout();
            _content.Controls.Clear();
            page.Dock = DockStyle.Fill;
            _content.Controls.Add(page);
            _content.ResumeLayout();

            for (int i = 0; i < _navButtons.Count; i++)
            {
                bool active = i == index;
                _navButtons[i].BackColor = active ? Theme.SurfaceAlt : Theme.Surface;
                _navButtons[i].ForeColor = active ? Theme.Accent : Theme.TextDim;
                _navButtons[i].Font = active ? Theme.UiFontBold : Theme.UiFont;
            }
        }

        private void EnableQosMarking()
        {
            if (QosManager.IsNlaBypassEnabled())
            {
                MessageBox.Show(this,
                    "QoS marking is already enabled on this PC.",
                    "BandPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult ok = MessageBox.Show(this,
                "On a PC that is not joined to a company domain, Windows ignores QoS " +
                "priority rules until this switch is set.\n\n" +
                "BandPilot will set:\n" +
                "  HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\QoS\n" +
                "  \"Do not use NLA\" = \"1\"\n\n" +
                "A restart is needed for it to take effect. Continue?",
                "Enable QoS marking", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;

            try
            {
                QosManager.EnableNlaBypass();
                QosManager.RefreshPolicy();
                MessageBox.Show(this,
                    "Done. Restart Windows for priority rules to start applying.",
                    "BandPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not write the setting:\n\n" + ex.Message,
                    "BandPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static Control BuildAbout()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Background,
                Padding = new Padding(24),
                AutoScroll = true
            };

            var text = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                ForeColor = Theme.Text,
                Font = Theme.UiFont,
                Location = new Point(24, 24),
                Text =
"BandPilot\r\n\r\n" +
"An open-source Wi-Fi control panel for Intel BE200 / BE201 / BE202 adapters.\r\n\r\n" +
"WHAT THE BANDS PAGE DOES\r\n" +
"A big network hands out one name for many radios. A hotel or campus SSID is\r\n" +
"typically a 2.4 GHz radio, a 5 GHz radio and often a 6 GHz radio on every access\r\n" +
"point in the building, all sharing one name. Windows picks one for you and gives\r\n" +
"you no way to see which, or to change it.\r\n\r\n" +
"The Bands page lists every radio separately with its band, channel, signal and\r\n" +
"Wi-Fi generation, and connects you to the one you pick. The rating column is a\r\n" +
"rough guide that favours the higher bands, since a strong 2.4 GHz signal is\r\n" +
"usually slower in practice than a moderate 5 or 6 GHz one.\r\n\r\n" +
"Requirements: the network must already be saved in Windows, because BandPilot\r\n" +
"reuses the stored credentials rather than asking for them.\r\n\r\n" +
"IF WINDOWS WANDERS BACK\r\n" +
"Pinning applies to the current connection. To make it stick, open the Adapter\r\n" +
"page and lower Roaming Aggressiveness, or set a Preferred Band if the driver\r\n" +
"offers one.\r\n\r\n" +
"PRIORITY AND LIMITS\r\n" +
"Rules are ordinary Windows QoS policies. Priority marking needs the switch under\r\n" +
"\"Enable QoS marking\" plus a restart, and the marking is only honoured end to end\r\n" +
"if your router respects DSCP. Speed limits apply locally and work regardless.\r\n\r\n" +
"LIVE TRAFFIC\r\n" +
"Per-process byte counts come from an ETW kernel session, which is the only way\r\n" +
"Windows exposes this. It needs administrator rights.\r\n\r\n" +
"NOT AFFILIATED WITH INTEL\r\n" +
"BandPilot is independent software written against public Windows APIs. It shares\r\n" +
"no code with Intel Killer Performance Suite and is not endorsed by Intel."
            };
            panel.Controls.Add(text);
            return panel;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_wifi != null) _wifi.Dispose();
        }
    }
}
