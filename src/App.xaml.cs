using System;
using System.Windows;
using System.Windows.Threading;
using BandPilot.Ui;
using BandPilot.Wifi;

namespace BandPilot
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // A crash in a UI callback should say what happened rather than
            // vanishing the window, since most of this app's work happens in
            // event handlers talking to drivers that can fail in novel ways.
            DispatcherUnhandledException += OnDispatcherException;

            // A game-mode journal left on disk means the last session ended
            // badly. Replay it before anything else runs, and support a headless
            // "BandPilot.exe --restore" so someone stuck can fix it without the
            // GUI. The journal's presence is the only signal; there is no flag.
            bool restoreOnly = e.Args != null
                && Array.Exists(e.Args, a => string.Equals(a, "--restore", StringComparison.OrdinalIgnoreCase));

            System.Collections.Generic.List<Game.GameModeAction> recovered =
                Game.GameMode.RecoverIfNeeded();

            if (restoreOnly)
            {
                MessageBox.Show(
                    recovered.Count == 0
                        ? "Nothing needed restoring."
                        : "Restored " + recovered.Count + " setting(s) from an interrupted game-mode session.",
                    "BandPilot", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            // Before any window exists, so the first paint is already correct
            // rather than flashing light and then switching.
            ThemeManager.Initialise();

            WifiService wifi;
            try
            {
                wifi = new WifiService();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "BandPilot could not talk to the Wi-Fi service.\n\n" + ex.Message +
                    "\n\nCheck that the \"WLAN AutoConfig\" service is running and that this " +
                    "machine has a wireless adapter.",
                    "BandPilot", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            MainWindow = new MainWindow(wifi);
            MainWindow.Show();
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                "BandPilot hit an unexpected error:\n\n" + e.Exception.Message,
                "BandPilot", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
