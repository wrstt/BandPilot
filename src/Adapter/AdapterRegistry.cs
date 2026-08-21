using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace BandPilot.Adapter
{
    /// <summary>
    /// Reads a network adapter's advanced properties straight from the driver's
    /// registry keys.
    ///
    /// This replaces spawning powershell.exe for Get-NetAdapterAdvancedProperty.
    /// That worked but was a poor foundation: every read cost a process launch of
    /// a second or more, and it could fail silently a dozen ways — the NetAdapter
    /// module missing, CIM refusing, JSON shape changing between one row and
    /// many, an execution policy in the way. When it failed the page simply came
    /// up empty with nothing to explain it.
    ///
    /// The registry is where those properties actually live; the cmdlet is just a
    /// wrapper over the same data. Reading it directly is instant, needs no
    /// PowerShell, and cannot half-succeed.
    ///
    /// Layout, for reference:
    ///   HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-...}\NNNN
    ///     NetCfgInstanceId = "{interface guid}"   <- how an adapter is matched
    ///     DriverDesc       = "Intel(R) Wi-Fi 7 BE200 320MHz"
    ///     &lt;Keyword&gt;        = current raw value, e.g. RoamAggressiveness = "3"
    ///     Ndi\Params\&lt;Keyword&gt;
    ///       ParamDesc = "Roaming Aggressiveness"   <- the human name
    ///       default   = "3"
    ///       type      = "enum" | "int" | "edit"
    ///       Enum\     = raw value -> display text, e.g. "3" -> "3. Medium"
    /// </summary>
    public static class AdapterRegistry
    {
        // The class GUID for network adapters. Fixed by Windows, not by vendor.
        private const string ClassRoot =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        /// <summary>
        /// Finds the driver instance key for a WLAN interface GUID. Returns null
        /// when no adapter matches, which is the honest answer for a radio the
        /// class key does not describe.
        /// </summary>
        public static string FindInstanceKey(Guid interfaceGuid, string description)
        {
            string target = interfaceGuid.ToString("B").ToUpperInvariant();

            using (RegistryKey root = Registry.LocalMachine.OpenSubKey(ClassRoot))
            {
                if (root == null) return null;

                string descriptionMatch = null;

                foreach (string name in root.GetSubKeyNames())
                {
                    // Skip "Properties" and anything that is not an NNNN instance.
                    if (name.Length != 4) continue;

                    using (RegistryKey inst = root.OpenSubKey(name))
                    {
                        if (inst == null) continue;

                        string id = inst.GetValue("NetCfgInstanceId") as string;
                        if (!string.IsNullOrEmpty(id) &&
                            string.Equals(id.Trim().ToUpperInvariant(), target, StringComparison.Ordinal))
                        {
                            return ClassRoot + "\\" + name;
                        }

                        // Kept as a second pass: a handful of drivers report the
                        // interface GUID inconsistently, and the description is
                        // a weaker but usable match.
                        if (descriptionMatch == null && !string.IsNullOrEmpty(description))
                        {
                            string desc = inst.GetValue("DriverDesc") as string;
                            if (string.Equals(desc, description, StringComparison.OrdinalIgnoreCase))
                            {
                                descriptionMatch = ClassRoot + "\\" + name;
                            }
                        }
                    }
                }

                return descriptionMatch;
            }
        }

        /// <summary>
        /// Every adjustable driver setting on this adapter. Empty list rather
        /// than null when the driver exposes none, which is normal on some
        /// Realtek and MediaTek parts.
        /// </summary>
        public static List<AdvancedProperty> Read(string instanceKeyPath)
        {
            var result = new List<AdvancedProperty>();
            if (string.IsNullOrEmpty(instanceKeyPath)) return result;

            using (RegistryKey inst = Registry.LocalMachine.OpenSubKey(instanceKeyPath))
            {
                if (inst == null) return result;

                using (RegistryKey parms = inst.OpenSubKey(@"Ndi\Params"))
                {
                    if (parms == null) return result;

                    foreach (string keyword in parms.GetSubKeyNames())
                    {
                        using (RegistryKey p = parms.OpenSubKey(keyword))
                        {
                            if (p == null) continue;

                            AdvancedProperty prop = ReadOne(inst, p, keyword);
                            if (prop != null) result.Add(prop);
                        }
                    }
                }
            }

            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static AdvancedProperty ReadOne(RegistryKey instance, RegistryKey param, string keyword)
        {
            string label = param.GetValue("ParamDesc") as string;

            // A parameter with no human-readable name is an internal knob the
            // driver does not intend to expose. Showing the raw keyword instead
            // would fill the table with noise.
            if (string.IsNullOrEmpty(label)) return null;

            var prop = new AdvancedProperty
            {
                DisplayName = label,
                RegistryKeyword = keyword,
                ValidValues = new List<string>()
            };

            // The live value lives on the instance key; "default" is the
            // fallback for a setting never changed from the driver's default.
            string raw = ValueToString(instance.GetValue(keyword));
            if (raw == null) raw = ValueToString(param.GetValue("default"));

            var displayByRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (RegistryKey en = param.OpenSubKey("Enum"))
            {
                if (en != null)
                {
                    foreach (string rawValue in en.GetValueNames())
                    {
                        string text = ValueToString(en.GetValue(rawValue));
                        if (string.IsNullOrEmpty(text)) continue;

                        displayByRaw[rawValue] = text;
                        prop.ValidValues.Add(text);
                    }
                }
            }

            if (raw != null && displayByRaw.ContainsKey(raw))
            {
                prop.DisplayValue = displayByRaw[raw];
            }
            else
            {
                // Numeric and free-text parameters have no Enum block, so the
                // stored value is already what the user should see.
                prop.DisplayValue = raw ?? string.Empty;
            }

            return prop;
        }

        private static string ValueToString(object value)
        {
            if (value == null) return null;

            string s = value as string;
            if (s != null) return s;

            // Some drivers store these as DWORD rather than REG_SZ.
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
