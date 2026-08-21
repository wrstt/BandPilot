using System;
using System.Windows.Forms;
using BandPilot.Ui;
using BandPilot.Wifi;

namespace BandPilot
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show(
                    "BandPilot hit an unexpected error:\n\n" + (ex != null ? ex.ToString() : "unknown"),
                    "BandPilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

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
                    "BandPilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm(wifi));
        }
    }
}
