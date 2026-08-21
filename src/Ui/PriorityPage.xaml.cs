using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BandPilot.Qos;

namespace BandPilot.Ui
{
    public partial class PriorityPage : UserControl
    {
        /// <summary>
        /// DSCP presented as a ranked scale rather than raw numbers. The order
        /// matters and is not numeric: DSCP 8 (CS1) is *lower* priority than 0,
        /// which reads as a mistake unless the list says so.
        /// </summary>
        private sealed class PriorityChoice
        {
            public int Dscp { get; set; }
            public string Label { get; set; }
            public override string ToString() { return Label; }
        }

        private static readonly PriorityChoice[] Choices =
        {
            new PriorityChoice { Dscp = 46, Label = "1 · Highest — voice & games (EF 46)" },
            new PriorityChoice { Dscp = 40, Label = "2 · Very high — video (CS5 40)" },
            new PriorityChoice { Dscp = 34, Label = "3 · High (AF41 34)" },
            new PriorityChoice { Dscp = 26, Label = "4 · Above normal (AF31 26)" },
            new PriorityChoice { Dscp = 18, Label = "5 · Slightly raised (AF21 18)" },
            new PriorityChoice { Dscp = 0,  Label = "6 · Default / best effort (0)" },
            new PriorityChoice { Dscp = 8,  Label = "7 · Background — deprioritise (CS1 8)" }
        };

        private readonly MainWindow _shell;

        public PriorityPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();

            FProto.ItemsSource = new[] { "* — any", "TCP", "UDP" };
            FProto.SelectedIndex = 0;
            FDscp.ItemsSource = Choices;
            FDscp.SelectedIndex = 5;
            FPort.Text = "*";
        }

        public void OnShown()
        {
            bool on = false;
            try { on = QosManager.IsNlaBypassEnabled(); }
            catch (Exception) { }
            QosWarn.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            Load();
        }

        public void PrefillApplication(string exeName)
        {
            RuleList.SelectedItem = null;
            FApp.Text = exeName ?? string.Empty;

            string stem = exeName ?? "app";
            int dot = stem.LastIndexOf('.');
            if (dot > 0) stem = stem.Substring(0, dot);
            FName.Text = stem + " priority";

            FDscp.SelectedIndex = 0;
            FPort.Text = "*";
            FProto.SelectedIndex = 0;
            FLimit.Text = string.Empty;
            Status("Prefilled from Live traffic — review and save.", "B.Accent");
        }

        private void Load()
        {
            try
            {
                List<QosRule> rules = QosManager.GetRules();
                RuleList.ItemsSource = rules;
                EmptyState.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
        }

        private void OnRuleSelected(object sender, SelectionChangedEventArgs e)
        {
            QosRule r = RuleList.SelectedItem as QosRule;
            if (r == null) return;

            FName.Text = r.Name;
            FApp.Text = r.Application;
            FPort.Text = string.IsNullOrEmpty(r.RemotePort) ? "*" : r.RemotePort;

            string proto = (r.Protocol ?? "*").ToUpperInvariant();
            FProto.SelectedIndex = proto == "TCP" ? 1 : proto == "UDP" ? 2 : 0;

            PriorityChoice match = Choices.FirstOrDefault(c => c.Dscp == r.Dscp);
            FDscp.SelectedItem = match ?? Choices[5];

            FLimit.Text = r.ThrottleBytesPerSecond > 0
                ? (r.ThrottleBytesPerSecond * 8.0 / 1000000.0).ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*";
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog() == true)
            {
                // Store the bare filename: QoS policy matches on it, and a full
                // path breaks the moment the game updates into a new folder.
                FApp.Text = System.IO.Path.GetFileName(dlg.FileName);
                if (string.IsNullOrWhiteSpace(FName.Text))
                {
                    FName.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) + " priority";
                }
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FName.Text))
            {
                Status("Give the rule a name first.", "B.WarnText");
                return;
            }
            if (string.IsNullOrWhiteSpace(FApp.Text))
            {
                Status("Name the application the rule applies to.", "B.WarnText");
                return;
            }

            long throttle = -1;
            string limit = (FLimit.Text ?? string.Empty).Trim();
            if (limit.Length > 0)
            {
                double mbits;
                if (!double.TryParse(limit, NumberStyles.Float, CultureInfo.InvariantCulture, out mbits)
                    || mbits <= 0)
                {
                    Status("The limit must be a positive number of Mbit/s, or empty.", "B.WarnText");
                    return;
                }
                throttle = (long)(mbits * 1000000.0 / 8.0);
            }

            PriorityChoice choice = FDscp.SelectedItem as PriorityChoice ?? Choices[5];
            string proto = FProto.SelectedIndex == 1 ? "TCP" : FProto.SelectedIndex == 2 ? "UDP" : "*";
            string port = string.IsNullOrWhiteSpace(FPort.Text) ? "*" : FPort.Text.Trim();

            try
            {
                QosManager.SaveRule(new QosRule
                {
                    Name = FName.Text.Trim(),
                    Application = FApp.Text.Trim(),
                    Protocol = proto,
                    RemotePort = port,
                    Dscp = choice.Dscp,
                    ThrottleBytesPerSecond = throttle
                });
                QosManager.RefreshPolicy();
                Load();
                Status("Saved.", "B.Good");
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            QosRule r = RuleList.SelectedItem as QosRule;
            string name = r != null ? r.Name : (FName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                Status("Select a rule to delete.", "B.WarnText");
                return;
            }

            _shell.ShowConfirm(
                "Delete rule",
                "Delete the rule \"" + name + "\"? Windows will stop applying it immediately.",
                "Delete rule",
                () =>
                {
                    try
                    {
                        QosManager.DeleteRule(name);
                        QosManager.RefreshPolicy();
                        Load();
                        Status("Deleted.", "B.Good");
                    }
                    catch (Exception ex)
                    {
                        Status(ex.Message, "B.Bad");
                    }
                },
                true);
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            Load();
            Status(string.Empty, "B.TextDim");
        }

        private void OnEnableQos(object sender, RoutedEventArgs e)
        {
            // Same wording as the sidebar's switch, kept in one method so the
            // two entry points can never drift apart.
            _shell.ShowConfirm(
                "Enable QoS marking",
                "Windows ignores priority rules on PCs that are not domain-joined until a "
                + "registry switch is set. BandPilot will set it for you:",
                "Enable QoS marking",
                () =>
                {
                    try
                    {
                        QosManager.EnableNlaBypass();
                        QosManager.RefreshPolicy();
                        _shell.RefreshQosIndicator();
                        OnShown();
                    }
                    catch (Exception ex)
                    {
                        Status(ex.Message, "B.Bad");
                    }
                },
                false,
                "A restart is needed before rules take effect.",
                "HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\QoS\n\"Do not use NLA\" = 1");
        }

        private void OnStarterGame(object sender, RoutedEventArgs e)
        {
            RuleList.SelectedItem = null;
            FName.Text = "Game priority";
            FApp.Text = string.Empty;
            FProto.SelectedIndex = 0;
            FPort.Text = "*";
            FDscp.SelectedIndex = 0;
            FLimit.Text = string.Empty;
            Status("Pick the game's .exe with Browse, then save.", "B.Accent");
        }

        private void OnStarterBackup(object sender, RoutedEventArgs e)
        {
            RuleList.SelectedItem = null;
            FName.Text = "Backup throttle";
            FApp.Text = string.Empty;
            FProto.SelectedIndex = 1;
            FPort.Text = "*";
            FDscp.SelectedIndex = 6;
            FLimit.Text = "20";
            Status("Pick the backup app's .exe with Browse, then save.", "B.Accent");
        }

        private void Status(string text, string brushKey)
        {
            StatusLine.Text = text ?? string.Empty;
            StatusLine.Foreground = (Brush)FindResource(brushKey);
        }
    }
}
