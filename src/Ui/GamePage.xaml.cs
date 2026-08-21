using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BandPilot.Game;

namespace BandPilot.Ui
{
    public partial class GamePage : UserControl
    {
        /// <summary>A candidate process the user might be playing.</summary>
        public sealed class GameChoice
        {
            public Process Process { get; set; }
            public string Label { get; set; }
        }

        /// <summary>Log row, with its colour resolved for the template.</summary>
        public sealed class LogRow
        {
            public string Description { get; set; }
            public string Detail { get; set; }
            public Brush Dot { get; set; }

            public Visibility DetailVisibility
            {
                get
                {
                    return string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        private readonly MainWindow _shell;
        private readonly GameMode _mode = new GameMode();

        public GamePage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();

            _mode.GameExited += (s, e) => Dispatcher.Invoke(() =>
            {
                RefreshState();
                Status("The game exited, so everything has been put back.", "B.Good");
            });

            LoadGames();
        }

        public void OnShown()
        {
            LoadGames();
            RefreshState();
        }

        public void ShutDown()
        {
            _mode.Dispose();
        }

        /// <summary>
        /// Anything with a visible window, which is a far better filter than a
        /// hardcoded list of game executables that would be wrong for everything
        /// not on it.
        /// </summary>
        private void LoadGames()
        {
            var choices = new List<GameChoice>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) { p.Dispose(); continue; }
                    if (string.IsNullOrEmpty(p.MainWindowTitle)) { p.Dispose(); continue; }
                    if (p.Id == Environment.ProcessId) { p.Dispose(); continue; }

                    choices.Add(new GameChoice
                    {
                        Process = p,
                        Label = p.ProcessName + "  —  " + Trim(p.MainWindowTitle)
                    });
                }
                catch (Exception)
                {
                    p.Dispose();
                }
            }

            choices.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

            object previous = GameBox.SelectedItem as GameChoice;
            GameBox.ItemsSource = choices;

            GameChoice keep = previous as GameChoice;
            if (keep != null)
            {
                GameBox.SelectedItem = choices.FirstOrDefault(c => c.Process.Id == keep.Process.Id);
            }
            if (GameBox.SelectedItem == null && choices.Count > 0) GameBox.SelectedIndex = 0;
        }

        private static string Trim(string title)
        {
            if (title.Length <= 48) return title;
            return title.Substring(0, 45) + "…";
        }

        private void OnRefreshGames(object sender, RoutedEventArgs e)
        {
            LoadGames();
        }

        private void OnToggle(object sender, RoutedEventArgs e)
        {
            if (_mode.IsActive)
            {
                _mode.Stop();
                RefreshState();
                Status("Game mode off. Everything has been put back.", "B.Good");
                return;
            }

            GameChoice choice = GameBox.SelectedItem as GameChoice;
            if (choice == null)
            {
                Status("Pick the game you are playing first.", "B.WarnText");
                return;
            }

            _shell.ShowConfirm(
                "Start game mode",
                "BandPilot will ease background apps off the CPU and memory, raise "
                + choice.Process.ProcessName + " above them, and apply the options you ticked.\n\n"
                + "Nothing here stops a Windows service. Everything is listed as it happens and "
                + "reversed when the game exits, when you turn this off, or if BandPilot stops "
                + "for any reason.",
                "Start game mode",
                () => Start(choice));
        }

        private void Start(GameChoice choice)
        {
            try
            {
                _mode.Start(choice.Process, OptNetwork.IsChecked == true, OptPower.IsChecked == true);
                RefreshState();

                int failed = _mode.Log.Count(a => !a.Succeeded);
                Status(failed == 0
                    ? "Game mode is on. It will lift when " + choice.Process.ProcessName + " exits."
                    : "Game mode is on, but " + failed + " step(s) did not apply — see the list above.",
                    failed == 0 ? "B.Good" : "B.WarnText");
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
                RefreshState();
            }
        }

        private void RefreshState()
        {
            bool on = _mode.IsActive;

            BtnToggle.Content = on ? "Stop game mode" : "Start game mode";
            StateChipText.Text = on ? "active" : "off";

            Brush chip = (Brush)FindResource(on ? "B.Good" : "B.TextFaint");
            StateChipText.Foreground = chip;
            StateChip.BorderBrush = chip;

            GameBox.IsEnabled = !on;
            OptNetwork.IsEnabled = !on;
            OptPower.IsEnabled = !on;

            ShowLog(_mode.Log);
        }

        /// <summary>
        /// The full list, not a summary. A tool that changes machine state
        /// should show exactly what it changed while it is changed.
        /// </summary>
        public void ShowLog(IEnumerable<GameModeAction> actions)
        {
            var rows = new List<LogRow>();
            foreach (GameModeAction a in actions ?? Enumerable.Empty<GameModeAction>())
            {
                rows.Add(new LogRow
                {
                    Description = a.Description,
                    Detail = a.Detail,
                    Dot = (Brush)FindResource(a.Succeeded ? "B.Good" : "B.WarnRail")
                });
            }

            LogList.ItemsSource = rows;
            EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Status(string text, string brushKey)
        {
            StatusLine.Text = text ?? string.Empty;
            StatusLine.Foreground = (Brush)FindResource(brushKey);
        }
    }
}
