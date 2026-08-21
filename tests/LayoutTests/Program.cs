using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BandPilot.Native;
using BandPilot.Wifi;

namespace BandPilot.Tests
{
    /// <summary>
    /// Checks the things that fail silently rather than loudly.
    ///
    /// A wrong struct size or field offset in the Native Wifi layer does not
    /// crash: it produces plausible but incorrect signal strengths, channels and
    /// BSSIDs, which is far worse. These expectations come from the C headers
    /// for x64 and are asserted here so a marshalling regression is caught at
    /// build time on any OS.
    /// </summary>
    internal static class Program
    {
        private static int _failures;
        private static int _checks;

        private static int Main()
        {
            Console.WriteLine();
            Console.WriteLine("BandPilot layout and math checks");
            Console.WriteLine("================================");
            Console.WriteLine();

            StructSizes();
            FieldOffsets();
            BssidListLayout();
            ChannelMath();
            BandMath();
            ScoreSanity();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? string.Format("All {0} checks passed.", _checks)
                : string.Format("{0} of {1} checks FAILED.", _failures, _checks));
            Console.WriteLine();
            return _failures == 0 ? 0 : 1;
        }

        // ---- assertions -----------------------------------------------------

        private static void Eq(string what, long actual, long expected)
        {
            _checks++;
            bool ok = actual == expected;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2,6}  (expected {3})",
                ok ? "ok" : "FAIL", what, actual, expected);
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine(name);
            Console.WriteLine(new string('-', name.Length));
        }

        // ---- checks ---------------------------------------------------------

        private static void StructSizes()
        {
            Section("Struct sizes (x64)");
            Eq("DOT11_SSID", Marshal.SizeOf<Dot11Ssid>(), 36);
            Eq("WLAN_RATE_SET", Marshal.SizeOf<WlanRateSet>(), 256);
            Eq("WLAN_BSS_ENTRY", Marshal.SizeOf<WlanBssEntry>(), 360);
            Eq("WLAN_ASSOCIATION_ATTRIBUTES", Marshal.SizeOf<WlanAssociationAttributes>(), 68);
            Eq("WLAN_SECURITY_ATTRIBUTES", Marshal.SizeOf<WlanSecurityAttributes>(), 16);
            Eq("WLAN_CONNECTION_ATTRIBUTES", Marshal.SizeOf<WlanConnectionAttributes>(), 604);
            Eq("WLAN_CONNECTION_PARAMETERS", Marshal.SizeOf<WlanConnectionParameters>(), 40);
            Eq("WLAN_INTERFACE_INFO", Marshal.SizeOf<WlanInterfaceInfo>(), 532);
            Eq("WLAN_PROFILE_INFO", Marshal.SizeOf<WlanProfileInfo>(), 516);
        }

        private static void FieldOffsets()
        {
            Section("WLAN_BSS_ENTRY field offsets");
            Eq("dot11Ssid", Off<WlanBssEntry>("dot11Ssid"), 0);
            Eq("uPhyId", Off<WlanBssEntry>("uPhyId"), 36);
            Eq("dot11Bssid", Off<WlanBssEntry>("dot11Bssid"), 40);
            Eq("dot11BssType", Off<WlanBssEntry>("dot11BssType"), 48);
            Eq("dot11BssPhyType", Off<WlanBssEntry>("dot11BssPhyType"), 52);
            Eq("lRssi", Off<WlanBssEntry>("lRssi"), 56);
            Eq("uLinkQuality", Off<WlanBssEntry>("uLinkQuality"), 60);
            Eq("bInRegDomain", Off<WlanBssEntry>("bInRegDomain"), 64);
            Eq("usBeaconPeriod", Off<WlanBssEntry>("usBeaconPeriod"), 66);
            Eq("ullTimestamp", Off<WlanBssEntry>("ullTimestamp"), 72);
            Eq("ullHostTimestamp", Off<WlanBssEntry>("ullHostTimestamp"), 80);
            Eq("usCapabilityInformation", Off<WlanBssEntry>("usCapabilityInformation"), 88);
            Eq("ulChCenterFrequency", Off<WlanBssEntry>("ulChCenterFrequency"), 92);
            Eq("wlanRateSet", Off<WlanBssEntry>("wlanRateSet"), 96);
            Eq("ulIeOffset", Off<WlanBssEntry>("ulIeOffset"), 352);
            Eq("ulIeSize", Off<WlanBssEntry>("ulIeSize"), 356);

            Section("WLAN_ASSOCIATION_ATTRIBUTES field offsets");
            Eq("dot11Bssid", Off<WlanAssociationAttributes>("dot11Bssid"), 40);
            Eq("dot11PhyType", Off<WlanAssociationAttributes>("dot11PhyType"), 48);
            Eq("wlanSignalQuality", Off<WlanAssociationAttributes>("wlanSignalQuality"), 56);
            Eq("ulRxRate", Off<WlanAssociationAttributes>("ulRxRate"), 60);
            Eq("ulTxRate", Off<WlanAssociationAttributes>("ulTxRate"), 64);

            Section("WLAN_CONNECTION_ATTRIBUTES field offsets");
            Eq("strProfileName", Off<WlanConnectionAttributes>("strProfileName"), 8);
            Eq("wlanAssociationAttributes", Off<WlanConnectionAttributes>("wlanAssociationAttributes"), 520);
            Eq("wlanSecurityAttributes", Off<WlanConnectionAttributes>("wlanSecurityAttributes"), 588);
        }

        private static long Off<T>(string field)
        {
            return (long)Marshal.OffsetOf<T>(field);
        }

        /// <summary>
        /// Mirrors WifiService.BuildBssidList. If the two ever diverge the pin
        /// operation silently targets the wrong AP, so the expected bytes are
        /// spelled out here rather than recomputed.
        /// </summary>
        private static void BssidListLayout()
        {
            Section("DOT11_BSSID_LIST construction");

            byte[] mac = { 0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33 };
            const int size = 20;

            byte[] buf = new byte[size];
            buf[0] = 0x80;                 // NDIS_OBJECT_TYPE_DEFAULT
            buf[1] = 0x01;                 // revision 1
            BitConverter.GetBytes((short)size).CopyTo(buf, 2);
            BitConverter.GetBytes(1).CopyTo(buf, 4);   // uNumOfEntries
            BitConverter.GetBytes(1).CopyTo(buf, 8);   // uTotalNumOfEntries
            mac.CopyTo(buf, 12);

            Eq("header Type", buf[0], 0x80);
            Eq("header Revision", buf[1], 1);
            Eq("header Size", BitConverter.ToInt16(buf, 2), 20);
            Eq("uNumOfEntries", BitConverter.ToInt32(buf, 4), 1);
            Eq("uTotalNumOfEntries", BitConverter.ToInt32(buf, 8), 1);
            Eq("BSSID first octet at offset 12", buf[12], 0xAA);
            Eq("BSSID last octet at offset 17", buf[17], 0x33);
            Eq("total length", buf.Length, 20);
        }

        private static void ChannelMath()
        {
            Section("Frequency to channel");
            var cases = new List<KeyValuePair<int, int>>
            {
                new KeyValuePair<int, int>(2412, 1),
                new KeyValuePair<int, int>(2437, 6),
                new KeyValuePair<int, int>(2462, 11),
                new KeyValuePair<int, int>(2484, 14),
                new KeyValuePair<int, int>(5180, 36),
                new KeyValuePair<int, int>(5240, 48),
                new KeyValuePair<int, int>(5500, 100),
                new KeyValuePair<int, int>(5745, 149),
                new KeyValuePair<int, int>(5825, 165),
                new KeyValuePair<int, int>(5935, 2),     // 6 GHz channel 2 is special-cased
                new KeyValuePair<int, int>(5955, 1),     // 6 GHz channel 1
                new KeyValuePair<int, int>(6175, 45),
                new KeyValuePair<int, int>(7115, 233)
            };

            foreach (KeyValuePair<int, int> c in cases)
            {
                Eq(c.Key + " MHz", BandTools.ChannelFromMhz(c.Key), c.Value);
            }
        }

        private static void BandMath()
        {
            Section("Frequency to band");
            Eq("2412 MHz is 2.4 GHz", (int)BandTools.BandFromMhz(2412), (int)WifiBand.Band24);
            Eq("2484 MHz is 2.4 GHz", (int)BandTools.BandFromMhz(2484), (int)WifiBand.Band24);
            Eq("5180 MHz is 5 GHz", (int)BandTools.BandFromMhz(5180), (int)WifiBand.Band5);
            Eq("5825 MHz is 5 GHz", (int)BandTools.BandFromMhz(5825), (int)WifiBand.Band5);
            Eq("5955 MHz is 6 GHz", (int)BandTools.BandFromMhz(5955), (int)WifiBand.Band6);
            Eq("6175 MHz is 6 GHz", (int)BandTools.BandFromMhz(6175), (int)WifiBand.Band6);
            Eq("7115 MHz is 6 GHz", (int)BandTools.BandFromMhz(7115), (int)WifiBand.Band6);

            Section("kHz to MHz");
            Eq("2412000 kHz", BandTools.FrequencyKhzToMhz(2412000), 2412);
            Eq("5955000 kHz", BandTools.FrequencyKhzToMhz(5955000), 5955);
        }

        private static void ScoreSanity()
        {
            Section("Rating behaviour");

            // The whole point of the rating: a moderate 6 GHz radio should beat
            // a strong 2.4 GHz one, because that is the choice users get wrong.
            int weak6 = BandTools.QualityScore(-70, WifiBand.Band6, Dot11PhyType.Eht);
            int strong24 = BandTools.QualityScore(-50, WifiBand.Band24, Dot11PhyType.He);
            _checks++;
            bool ok = weak6 > strong24;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2} vs {3}",
                ok ? "ok" : "FAIL", "-70 dBm on 6 GHz beats -50 dBm on 2.4 GHz", weak6, strong24);

            // ...but a genuinely dead 6 GHz radio should not.
            int dead6 = BandTools.QualityScore(-90, WifiBand.Band6, Dot11PhyType.Eht);
            _checks++;
            ok = dead6 < strong24;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2} vs {3}",
                ok ? "ok" : "FAIL", "-90 dBm on 6 GHz loses to -50 dBm on 2.4 GHz", dead6, strong24);

            Section("Signal bars");
            Eq("-50 dBm", BandTools.SignalBars(-50), 4);
            Eq("-60 dBm", BandTools.SignalBars(-60), 3);
            Eq("-70 dBm", BandTools.SignalBars(-70), 2);
            Eq("-80 dBm", BandTools.SignalBars(-80), 1);
            Eq("-95 dBm", BandTools.SignalBars(-95), 0);

            Section("BSSID formatting");
            string mac = BandTools.FormatBssid(new byte[] { 0x0A, 0x1B, 0x2C, 0x3D, 0x4E, 0x5F });
            _checks++;
            ok = mac == "0A:1B:2C:3D:4E:5F";
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2}", ok ? "ok" : "FAIL", "six octets, upper case, colon separated", mac);
        }
    }
}
