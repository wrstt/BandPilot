using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace BandPilot.Qos
{
    public sealed class QosRule
    {
        public string Name { get; set; }
        /// <summary>Executable name ("game.exe") or a full path.</summary>
        public string Application { get; set; }
        public string Protocol { get; set; }          // "*", "TCP", "UDP"
        public string RemotePort { get; set; }        // "*", "443", "27015-27020"
        public int Dscp { get; set; }                 // -1 leaves marking alone
        public long ThrottleBytesPerSecond { get; set; } // -1 for no limit

        public string DscpLabel { get { return QosManager.DescribeDscp(Dscp); } }

        public string ThrottleLabel
        {
            get
            {
                if (ThrottleBytesPerSecond <= 0) return "unlimited";
                double mbits = ThrottleBytesPerSecond * 8.0 / 1000000.0;
                return mbits >= 1.0
                    ? mbits.ToString("0.##") + " Mbit/s"
                    : (ThrottleBytesPerSecond * 8.0 / 1000.0).ToString("0.##") + " kbit/s";
            }
        }
    }

    /// <summary>
    /// Reads and writes Windows QoS policies.
    ///
    /// These are the same policies Group Policy would create, written straight
    /// to the registry so they work on Home editions where gpedit.msc is
    /// absent. Values are all REG_SZ, including the numeric ones, because that
    /// is what the QoS policy engine expects.
    /// </summary>
    public static class QosManager
    {
        private const string PolicyRoot = @"SOFTWARE\Policies\Microsoft\Windows\QoS";
        private const string TcpipQos = @"SYSTEM\CurrentControlSet\Services\Tcpip\QoS";

        public static readonly Dictionary<int, string> DscpPresets = new Dictionary<int, string>
        {
            { 46, "46 - EF, highest (voice/games)" },
            { 40, "40 - CS5, very high (video)" },
            { 34, "34 - AF41, high" },
            { 26, "26 - AF31, above normal" },
            { 18, "18 - AF21, slightly raised" },
            {  0, "0 - default / best effort" },
            {  8, "8 - CS1, background (deprioritise)" }
        };

        public static string DescribeDscp(int dscp)
        {
            if (dscp < 0) return "unchanged";
            string label;
            return DscpPresets.TryGetValue(dscp, out label) ? label : dscp.ToString();
        }

        public static List<QosRule> GetRules()
        {
            var rules = new List<QosRule>();
            using (RegistryKey root = Registry.LocalMachine.OpenSubKey(PolicyRoot, false))
            {
                if (root == null) return rules;

                foreach (string name in root.GetSubKeyNames())
                {
                    using (RegistryKey k = root.OpenSubKey(name, false))
                    {
                        if (k == null) continue;
                        rules.Add(new QosRule
                        {
                            Name = name,
                            Application = k.GetValue("Application Name") as string ?? "*",
                            Protocol = k.GetValue("Protocol") as string ?? "*",
                            RemotePort = k.GetValue("Remote Port") as string ?? "*",
                            Dscp = ParseInt(k.GetValue("DSCP Value") as string, -1),
                            ThrottleBytesPerSecond = ParseLong(k.GetValue("Throttle Rate") as string, -1)
                        });
                    }
                }
            }
            return rules;
        }

        public static void SaveRule(QosRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.Name))
                throw new ArgumentException("A policy name is required.");
            if (rule.Name.IndexOfAny(new[] { '\\', '/' }) >= 0)
                throw new ArgumentException("A policy name cannot contain slashes.");

            using (RegistryKey root = Registry.LocalMachine.CreateSubKey(PolicyRoot))
            {
                if (root == null) throw new InvalidOperationException("Could not open the QoS policy key. Run BandPilot as administrator.");

                using (RegistryKey k = root.CreateSubKey(rule.Name))
                {
                    if (k == null) throw new InvalidOperationException("Could not create the policy key.");

                    k.SetValue("Version", "1.0", RegistryValueKind.String);
                    k.SetValue("Application Name", string.IsNullOrWhiteSpace(rule.Application) ? "*" : rule.Application.Trim(), RegistryValueKind.String);
                    k.SetValue("Protocol", string.IsNullOrWhiteSpace(rule.Protocol) ? "*" : rule.Protocol, RegistryValueKind.String);
                    k.SetValue("Local Port", "*", RegistryValueKind.String);
                    k.SetValue("Local IP", "*", RegistryValueKind.String);
                    k.SetValue("Local IP Prefix Length", "*", RegistryValueKind.String);
                    k.SetValue("Remote Port", string.IsNullOrWhiteSpace(rule.RemotePort) ? "*" : rule.RemotePort.Trim(), RegistryValueKind.String);
                    k.SetValue("Remote IP", "*", RegistryValueKind.String);
                    k.SetValue("Remote IP Prefix Length", "*", RegistryValueKind.String);
                    k.SetValue("DSCP Value", rule.Dscp < 0 ? "-1" : rule.Dscp.ToString(), RegistryValueKind.String);
                    k.SetValue("Throttle Rate", rule.ThrottleBytesPerSecond <= 0 ? "-1" : rule.ThrottleBytesPerSecond.ToString(), RegistryValueKind.String);
                }
            }
        }

        /// <returns>
        /// True when a policy of that name existed and was removed. False means
        /// there was nothing to delete, which the caller must not report as
        /// success — a "Deleted." message for a rule that was never there is how
        /// a stale list gets mistaken for a working one.
        /// </returns>
        public static bool DeleteRule(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            using (RegistryKey root = Registry.LocalMachine.OpenSubKey(PolicyRoot, true))
            {
                if (root == null) return false;

                bool existed = false;
                foreach (string existing in root.GetSubKeyNames())
                {
                    if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    {
                        existed = true;
                        name = existing;
                        break;
                    }
                }
                if (!existed) return false;

                root.DeleteSubKeyTree(name, false);
                return true;
            }
        }

        /// <summary>
        /// On a machine that is not domain-joined, the QoS engine ignores DSCP
        /// policies unless this flag is set. Without it the rules look applied
        /// but nothing is ever marked, which is the single most common reason
        /// hand-made QoS policies appear to do nothing.
        /// </summary>
        public static bool IsNlaBypassEnabled()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(TcpipQos, false))
            {
                if (k == null) return false;

                // Read the value whatever its type. Microsoft documents this as
                // REG_SZ and that is what EnableNlaBypass writes, but plenty of
                // tuning guides and .reg files set it as a DWORD. Treating those
                // as "off" would leave the warning banner up forever on a machine
                // where marking is in fact already enabled.
                object raw = k.GetValue("Do not use NLA");
                if (raw == null) return false;

                string text = Convert.ToString(raw, CultureInfo.InvariantCulture);
                return text != null && text.Trim() == "1";
            }
        }

        public static void EnableNlaBypass()
        {
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(TcpipQos))
            {
                if (k == null) throw new InvalidOperationException("Could not write the Tcpip\\QoS key. Run as administrator.");
                k.SetValue("Do not use NLA", "1", RegistryValueKind.String);
            }
        }

        /// <summary>
        /// Nudges the policy engine to re-read what was just written. The rule
        /// itself is already on disk by this point, so a failure here is not
        /// fatal — but it is worth telling the user about, because the symptom
        /// (a saved rule that changes nothing until the next reboot) is
        /// indistinguishable from the rule being wrong.
        ///
        /// This shells out to gpupdate and can take tens of seconds. Never call
        /// it on the UI thread.
        /// </summary>
        /// <returns>Null when the refresh completed, otherwise why it did not.</returns>
        public static string RefreshPolicy()
        {
            try
            {
                // Deliberately not /force: a full forced refresh reprocesses
                // every policy on the machine and can take a minute, when all
                // that is needed is for the QoS extension to re-read its keys.
                Adapter.AdapterProperties.RunPowerShell(
                    "gpupdate /target:computer | Out-Null", 45000);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        private static long ParseLong(string s, long fallback)
        {
            long v;
            return long.TryParse(s, out v) ? v : fallback;
        }
    }
}
