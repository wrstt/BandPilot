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
