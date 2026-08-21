using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BandPilot.Native;
using BandPilot.Adapter;
using BandPilot.Game;
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
            CapabilityRules();
            RoamingLockRules();
            SignalHistoryRules();
            BeaconParsing();
            GameModeSafety();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? string.Format("All {0} checks passed.", _checks)
                : string.Format("{0} of {1} checks FAILED.", _failures, _checks));
            Console.WriteLine();
            return _failures == 0 ? 0 : 1;
        }

        // ---- assertions -----------------------------------------------------

        /// <summary>
        /// The capability layer decides whether a 6 GHz row is offered or greyed
        /// out, and whether the connect button works at all. Getting it wrong
        /// silently removes the app's main feature on hardware that supports it,
        /// so the fail-open choices are pinned here.
        /// </summary>
        private static void CapabilityRules()
        {
            Section("Adapter capability");

            AdapterCapability wifi7 = new AdapterCapability
            { MaxPhy = Dot11PhyType.Eht, MaxBssidListSize = 1 };
            Bool("Wi-Fi 7 counts as 6 GHz capable", wifi7.Supports6Ghz, true);
            Str("Wi-Fi 7 chip text", wifi7.CapabilityLabel, "Wi-Fi 7 · 6 GHz ready");

            AdapterCapability wifi6 = new AdapterCapability
            { MaxPhy = Dot11PhyType.He, MaxBssidListSize = 1 };
            Bool("Wi-Fi 6 without evidence is not claimed capable", wifi6.Supports6Ghz, false);
            Bool("...but is reported unknown, not incapable", wifi6.SixGhzUnknown, true);
            Str("Wi-Fi 6 chip text", wifi6.CapabilityLabel, "Wi-Fi 6 · no 6 GHz seen");

            // A 6E card proves itself the moment it reports a 6 GHz BSS, since
            // scan results can only come from a radio that can tune there.
            wifi6.LearnFrom(new List<AccessPoint>
            {
                new AccessPoint { Band = WifiBand.Band24 },
                new AccessPoint { Band = WifiBand.Band6 }
            });
            Bool("seeing a 6 GHz BSS proves capability", wifi6.Supports6Ghz, true);
            Bool("and clears the unknown state", wifi6.SixGhzUnknown, false);

            AdapterCapability wifi5 = new AdapterCapability
            { MaxPhy = Dot11PhyType.Vht, MaxBssidListSize = 1 };
            Bool("Wi-Fi 5 is not flagged unknown, it simply has no 6 GHz", wifi5.SixGhzUnknown, false);
            Str("Wi-Fi 5 chip text", wifi5.CapabilityLabel, "Wi-Fi 5");

            AdapterCapability noPin = new AdapterCapability
            { MaxPhy = Dot11PhyType.He, MaxBssidListSize = 0 };
            Bool("a driver reporting 0 BSSIDs cannot pin", noPin.CanPinBssid, false);
            Bool("and explains why", noPin.PinWarning != null, true);

            // A failed capability query must not disable pinning on a card that
            // supports it, so the unknown case deliberately fails open.
            Bool("unknown capability still allows pinning", AdapterCapability.Unknown().CanPinBssid, true);

            Section("Highest PHY selection");
            WlanInterfaceCapability native = new WlanInterfaceCapability();
            native.dwMaxDesiredBssidListSize = 4;
            native.dwNumberOfSupportedPhys = 5;
            native.dot11PhyTypes = new Dot11PhyType[64];
            native.dot11PhyTypes[0] = Dot11PhyType.Erp;
            native.dot11PhyTypes[1] = Dot11PhyType.Ht;
            native.dot11PhyTypes[2] = Dot11PhyType.Vht;
            native.dot11PhyTypes[3] = Dot11PhyType.He;
            native.dot11PhyTypes[4] = Dot11PhyType.Dmg;   // 60 GHz, not on the ladder
            AdapterCapability built = AdapterCapability.FromNative(native);
            Eq("picks HE over DMG and older PHYs", (int)built.MaxPhy, (int)Dot11PhyType.He);
            Eq("carries the BSSID list size through", built.MaxBssidListSize, 4);

            // Entries past dwNumberOfSupportedPhys are uninitialised padding and
            // must not be read, or an empty slot could outrank a real one.
            WlanInterfaceCapability shortList = new WlanInterfaceCapability();
            shortList.dwNumberOfSupportedPhys = 1;
            shortList.dot11PhyTypes = new Dot11PhyType[64];
            shortList.dot11PhyTypes[0] = Dot11PhyType.Ht;
            shortList.dot11PhyTypes[5] = Dot11PhyType.Eht;   // beyond the count
            Eq("ignores entries past the reported count",
                (int)AdapterCapability.FromNative(shortList).MaxPhy, (int)Dot11PhyType.Ht);
        }

        /// <summary>
        /// The roaming control has a different name and a different value set on
        /// every vendor's driver, so this picks the calmest option by inspection.
        /// Choosing wrongly here does the opposite of what the button promises:
        /// it would make the adapter roam harder, off the AP the user just pinned.
        /// </summary>
        private static void RoamingLockRules()
        {
            Section("Roaming lockdown - calmest value");

            Str("Intel's numbered ladder picks the lowest",
                RoamingLock.CalmestValue(Prop("Roaming Aggressiveness", "RoamAggressiveness",
                    "1. Lowest", "2. Medium-Low", "3. Medium", "4. Medium-High", "5. Highest")),
                "1. Lowest");

            Str("a plain toggle picks Disabled",
                RoamingLock.CalmestValue(Prop("Roaming", "Roam", "Enabled", "Disabled")),
                "Disabled");

            Str("worded scales pick Low",
                RoamingLock.CalmestValue(Prop("Roam Tendency", "RoamTendency", "Low", "Medium", "High")),
                "Low");

            // The trap: a substring test for "low" matches "Allow", which would
            // select the most aggressive option instead of the calmest. Refusing
            // is an acceptable outcome here; picking "Allow roaming" is not.
            string decision = RoamingLock.CalmestValue(Prop("Roaming Decision", "RoamDecision",
                "Allow roaming", "Block roaming"));
            Bool("\"Allow roaming\" is never chosen for the hint \"low\"",
                decision != "Allow roaming", true);

            // Refusing beats guessing: picking blind could select the most
            // aggressive setting and make the problem worse.
            Bool("unrecognisable value sets are refused",
                RoamingLock.CalmestValue(Prop("Roaming Mode", "RoamMode", "Alpha", "Beta")) == null,
                true);
            Bool("an empty value set is refused",
                RoamingLock.CalmestValue(Prop("Roaming Mode", "RoamMode")) == null, true);

            Section("Roaming lockdown - discovery");
            var props = new List<AdvancedProperty>
            {
                Prop("Roaming Aggressiveness", "RoamAggressiveness", "1. Lowest", "3. Medium"),
                Prop("Preferred Band", "RoamingPreferredBandType", "No Preference", "Prefer 5GHz"),
                Prop("Transmit Power", "TxPower", "1. Lowest", "5. Highest")
            };
            List<RoamingLock.Candidate> found = RoamingLock.Find(props);

            // Transmit Power also has a "1. Lowest", so a value-shaped match would
            // wrongly cripple the radio. And Preferred Band is keyed
            // "RoamingPreferredBandType", so a name-only match would force a band
            // preference that contradicts whatever the user just pinned.
            Eq("only genuine roaming controls match", found.Count, 1);
            Bool("transmit power is not touched",
                found.TrueForAll(c => c.Property.DisplayName != "Transmit Power"), true);
            Bool("band preference is not touched",
                found.TrueForAll(c => c.Property.DisplayName != "Preferred Band"), true);
            Str("the aggressiveness target is the lowest rung",
                found[0].TargetValue, "1. Lowest");

            Prop("Roaming Aggressiveness", "RoamAggressiveness", "1. Lowest");
            var already = RoamingLock.Find(new List<AdvancedProperty> { Current("Roaming Aggressiveness",
                "RoamAggressiveness", "1. Lowest", "1. Lowest", "3. Medium") });
            Bool("a setting already at its calmest is reported as such",
                already.Count == 1 && already[0].AlreadySet, true);
        }

        private static AdvancedProperty Prop(string name, string keyword, params string[] values)
        {
            return new AdvancedProperty
            {
                DisplayName = name,
                RegistryKeyword = keyword,
                DisplayValue = values.Length > 0 ? values[0] : null,
                ValidValues = new List<string>(values)
            };
        }

        private static AdvancedProperty Current(string name, string keyword, string current,
                                                params string[] values)
        {
            AdvancedProperty p = Prop(name, keyword, values);
            p.DisplayValue = current;
            return p;
        }

        private static void SignalHistoryRules()
        {
            Section("Signal history");

            var history = new SignalHistory();
            var ap = new AccessPoint { Bssid = "AA:BB:CC:DD:EE:FF", RssiDbm = -60 };

            Eq("an unseen radio has no samples", history.For("00:00:00:00:00:00").Count, 0);
            Eq("and no spread", history.SpreadFor("00:00:00:00:00:00"), 0);

            for (int i = 0; i < 5; i++)
            {
                ap.RssiDbm = -60 - i;
                history.Record(new List<AccessPoint> { ap });
            }
            Eq("samples accumulate", history.For(ap.Bssid).Count, 5);
            Eq("spread is max minus min", history.SpreadFor(ap.Bssid), 4);

            // The buffer has to stay bounded: this records forever while the app
            // is open, and an unbounded list would grow without limit.
            for (int i = 0; i < SignalHistory.Capacity * 2; i++)
            {
                ap.RssiDbm = -55;
                history.Record(new List<AccessPoint> { ap });
            }
            Eq("the ring buffer is capped", history.For(ap.Bssid).Count, SignalHistory.Capacity);
            Eq("old samples are evicted, not kept", history.SpreadFor(ap.Bssid), 0);

            Eq("lookup is case-insensitive", history.For("aa:bb:cc:dd:ee:ff").Count, SignalHistory.Capacity);
        }

        /// <summary>
        /// Beacon parsing reads bytes written by whatever access point happens
        /// to be in range, so the inputs are hostile by default. These fixtures
        /// are hand-built to the 802.11 field layouts; a width read wrongly
        /// produces a confidently incorrect ranking rather than an error, which
        /// is the failure mode this whole suite exists to catch.
        /// </summary>
        private static void BeaconParsing()
        {
            Section("Beacon - malformed input is survivable");

            Eq("null blob defaults to 20 MHz", InformationElements.Parse(null).WidthMhz, 20);
            Eq("empty blob defaults to 20 MHz", InformationElements.Parse(new byte[0]).WidthMhz, 20);
            Bool("nothing is claimed to be known",
                InformationElements.Parse(null).WidthKnown, false);

            // A length field running past the buffer is the classic overread.
            byte[] truncated = { 61, 200, 0x24, 0x05 };
            Eq("an over-long element does not read past the end",
                InformationElements.Parse(truncated).WidthMhz, 20);

            byte[] ragged = { 0 };
            Eq("a one-byte blob is ignored", InformationElements.Parse(ragged).WidthMhz, 20);

            Section("Beacon - BSS Load (element 11)");

            // 12 stations, utilisation 128/255 = 50%, admission capacity 0.
            byte[] load = Ie(11, 0x0C, 0x00, 128, 0x00, 0x00);
            InformationElements.Parsed p = InformationElements.Parse(load);
            Eq("station count is little-endian", p.StationCount, 12);
            Eq("utilisation is scaled from 255, not 100", p.ChannelUtilisationPercent, 50);
            Bool("airtime data is flagged present", p.HasBssLoad, true);

            Eq("a fully busy channel reads 100",
                InformationElements.Parse(Ie(11, 0, 0, 255, 0, 0)).ChannelUtilisationPercent, 100);
            Eq("an idle channel reads 0",
                InformationElements.Parse(Ie(11, 0, 0, 0, 0, 0)).ChannelUtilisationPercent, 0);
            Bool("absent BSS Load is reported as unknown, not as zero",
                InformationElements.Parse(Ie(61, 0x24, 0x05)).HasBssLoad, false);

            Section("Beacon - channel width");

            // HT Operation: secondary channel present AND wide operation allowed.
            Eq("HT with a secondary channel is 40 MHz",
                InformationElements.Parse(Ie(61, 36, 0x05)).WidthMhz, 40);
            Eq("HT with no secondary channel is 20 MHz",
                InformationElements.Parse(Ie(61, 36, 0x00)).WidthMhz, 20);
            // Offset set but wide operation withheld: still 20.
            Eq("HT needs both bits to mean 40 MHz",
                InformationElements.Parse(Ie(61, 36, 0x01)).WidthMhz, 20);

            // VHT Operation: width 1 with no second segment is 80 MHz.
            Eq("VHT width 1 is 80 MHz",
                InformationElements.Parse(Ie(192, 1, 42, 0)).WidthMhz, 80);
            // Two segments eight apart is a contiguous 160.
            Eq("VHT segments 8 apart are 160 MHz",
                InformationElements.Parse(Ie(192, 1, 42, 50)).WidthMhz, 160);
            // Width 0 defers to HT rather than claiming anything.
            Eq("VHT width 0 defers to the HT element",
                InformationElements.Parse(Concat(Ie(61, 36, 0x05), Ie(192, 0, 0, 0))).WidthMhz, 40);

            // EHT Operation: params bit 0 set, 4 bytes MCS, then control = 4.
            Eq("EHT control 4 is 320 MHz",
                InformationElements.Parse(Ext(106, 0x01, 0, 0, 0, 0, 0x04, 0, 0)).WidthMhz, 320);
            Eq("EHT control 3 is 160 MHz",
                InformationElements.Parse(Ext(106, 0x01, 0, 0, 0, 0, 0x03, 0, 0)).WidthMhz, 160);
            Bool("EHT without operation info present is not trusted",
                InformationElements.Parse(Ext(106, 0x00, 0, 0, 0, 0, 0x04, 0, 0)).WidthKnown, false);

            // Wi-Fi 7 APs carry the older elements too, describing a narrower
            // channel. The newest element has to win or every 320 MHz AP is
            // reported as 80.
            byte[] mixed = Concat(
                Ie(61, 36, 0x05),                                 // HT says 40
                Ie(192, 1, 42, 0),                                // VHT says 80
                Ext(106, 0x01, 0, 0, 0, 0, 0x04, 0, 0));          // EHT says 320
            Eq("EHT beats VHT and HT on a Wi-Fi 7 beacon",
                InformationElements.Parse(mixed).WidthMhz, 320);

            Section("Beacon - HE 6 GHz operation");

            // HE Operation Parameters bit 21 = 6 GHz info present. Bits 18 and
            // 19 clear, so the 6 GHz block sits immediately after the fixed part.
            // params = 1 << 21 = 0x200000 -> bytes 00 00 20
            byte[] he6 = Ext(36, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00,
                             40, 0x02, 0, 0, 0);
            Eq("HE 6 GHz control 2 is 80 MHz", InformationElements.Parse(he6).WidthMhz, 80);

            byte[] he6wide = Ext(36, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00,
                                 40, 0x03, 0, 0, 0);
            Eq("HE 6 GHz control 3 is 160 MHz", InformationElements.Parse(he6wide).WidthMhz, 160);

            // Bit 21 clear: there is no 6 GHz block to read, and stepping into
            // one anyway would parse whatever bytes follow.
            byte[] heNo6 = Ext(36, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
            Bool("HE without the 6 GHz flag claims no width",
                InformationElements.Parse(heNo6).WidthKnown, false);

            Section("Beacon - SSID recovery");
            byte[] hidden = Concat(Ie(0, (byte)'H', (byte)'i'), Ie(11, 1, 0, 51, 0, 0));
            Str("a hidden network's SSID is recovered from the beacon",
                InformationElements.Parse(hidden).Ssid, "Hi");
        }

        /// <summary>Builds one information element: id, length, body.</summary>
        private static byte[] Ie(byte id, params byte[] body)
        {
            var b = new byte[2 + body.Length];
            b[0] = id;
            b[1] = (byte)body.Length;
            Array.Copy(body, 0, b, 2, body.Length);
            return b;
        }

        /// <summary>Builds an extension element (id 255) with its sub-id.</summary>
        private static byte[] Ext(byte extId, params byte[] body)
        {
            var full = new byte[1 + body.Length];
            full[0] = extId;
            Array.Copy(body, 0, full, 1, body.Length);
            return Ie(255, full);
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (byte[] part in parts) n += part.Length;

            var result = new byte[n];
            int at = 0;
            foreach (byte[] part in parts)
            {
                Array.Copy(part, 0, result, at, part.Length);
                at += part.Length;
            }
            return result;
        }

        /// <summary>
        /// Game Mode is the only feature here that changes machine-wide state,
        /// so its safety properties are asserted rather than assumed. Each of
        /// these corresponds to a specific way tools in this category leave
        /// people with a subtly broken machine.
        /// </summary>
        private static void GameModeSafety()
        {
            Section("Game mode - job object flags");

            // THE rule. JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE inverts the safety
            // property of a job object: instead of limits evaporating when
            // BandPilot dies, every background process the user had open would
            // be killed along with it. The flag is not even declared, so it
            // cannot be set by a typo.
            var flagValues = (GameNative.CpuRateControlFlags[])
                Enum.GetValues(typeof(GameNative.CpuRateControlFlags));
            Eq("only two rate-control flags exist at all", flagValues.Length, 2);
            Bool("no flag has the KILL_ON_JOB_CLOSE bit (0x2000)",
                Array.TrueForAll(flagValues, f => ((uint)f & 0x2000) == 0), true);

            uint used = (uint)(GameNative.CpuRateControlFlags.Enable
                             | GameNative.CpuRateControlFlags.WeightBased);
            Eq("the flags used are Enable|WeightBased", (long)used, 3);

            // Weight-based sharing, never a hard cap: a hard cap can stall a
            // process holding a lock the game is waiting on.
            Bool("weight-based sharing is requested",
                (used & (uint)GameNative.CpuRateControlFlags.WeightBased) != 0, true);

            Section("Game mode - restore decisions");

            // The classic bug: restoring a value that never existed by writing
            // zero. That changes the effective default and leaves the setting
            // altered forever in a way the user never chose.
            var neverExisted = new Mutation { Existed = false, PriorData = null };
            Bool("a value that did not exist is DELETED, not zeroed",
                neverExisted.Action == RestoreAction.Delete, true);

            var hadValue = new Mutation { Existed = true, PriorData = "10" };
            Bool("a value that existed is written back",
                hadValue.Action == RestoreAction.Write, true);

            // A prior of zero is still a prior. Treating "0" as "absent" would
            // delete a value the user deliberately set.
            var priorZero = new Mutation { Existed = true, PriorData = "0" };
            Bool("a prior value of zero is restored, not deleted",
                priorZero.Action == RestoreAction.Write, true);

            Section("Game mode - session expiry");

            SessionJournal fresh = SessionJournal.Begin(TimeSpan.FromHours(12));
            Bool("a new session is not expired", fresh.IsExpired, false);
            Bool("a session records the owning process", fresh.Pid == Environment.ProcessId, true);
            Eq("a new session has no mutations yet", fresh.Mutations.Count, 0);

            SessionJournal past = SessionJournal.Begin(TimeSpan.FromHours(-1));
            Bool("a session past its expiry is restorable", past.IsExpired, true);

            // An unreadable expiry has to fail towards restoring. The opposite
            // would leave a corrupt journal stranding machine state forever.
            var corrupt = new SessionJournal { HardExpiryUtc = "not a date" };
            Bool("an unreadable expiry is treated as expired", corrupt.IsExpired, true);

            var missing = new SessionJournal { HardExpiryUtc = null };
            Bool("a missing expiry is treated as expired", missing.IsExpired, true);

            Section("Game mode - protected processes");

            // Throttling any of these produces a visibly broken desktop.
            string[] mustProtect = { "csrss", "wininit", "winlogon", "services", "lsass",
                                     "dwm", "explorer", "audiodg" };
            foreach (string name in mustProtect)
            {
                Bool("never throttles " + name, ProtectedProcesses.Contains(name), true);
            }

            // Throttling ourselves would have the tool slow down the tool.
            Bool("never throttles BandPilot itself",
                ProtectedProcesses.Contains("BandPilot"), true);
            Bool("matching ignores case", ProtectedProcesses.Contains("ExPlOrEr"), true);

            // ...but the list must stay narrow, or Game Mode does nothing at all.
            Bool("an ordinary app is not protected",
                ProtectedProcesses.Contains("chrome"), false);
            Bool("a null name is handled", ProtectedProcesses.Contains(null), false);
        }

        private static void Bool(string what, bool actual, bool expected)
        {
            _checks++;
            bool ok = actual == expected;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2,6}  (expected {3})",
                ok ? "ok" : "FAIL", what, actual, expected);
        }

        private static void Str(string what, string actual, string expected)
        {
            _checks++;
            bool ok = actual == expected;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2}", ok ? "ok" : "FAIL", what,
                ok ? actual : actual + "  (expected " + expected + ")");
        }

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
            // 4 + 4 + 4 + 4 + 4 + (64 * 4) = 276
            Eq("WLAN_INTERFACE_CAPABILITY", Marshal.SizeOf<WlanInterfaceCapability>(), 276);
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

            Section("Rating - measured congestion");

            // The point of parsing beacons: a busy wide channel is worse than a
            // quiet narrow one, and no signal-strength model can see that.
            int busyWide = BandTools.QualityScore(-60, WifiBand.Band6, Dot11PhyType.Eht, 320, 90);
            int quietNarrow = BandTools.QualityScore(-60, WifiBand.Band5, Dot11PhyType.He, 80, 5);
            _checks++;
            ok = quietNarrow > busyWide;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2} vs {3}", ok ? "ok" : "FAIL",
                "a quiet 80 MHz beats a congested 320 MHz", quietNarrow, busyWide);

            // ...but with the air clear, width wins decisively.
            int quietWide = BandTools.QualityScore(-60, WifiBand.Band6, Dot11PhyType.Eht, 320, 5);
            _checks++;
            ok = quietWide > quietNarrow;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2} vs {3}", ok ? "ok" : "FAIL",
                "with clear air, 320 MHz beats 80 MHz", quietWide, quietNarrow);

            // Signal still gates everything: an unreachable radio scores nothing
            // no matter how good its specification is.
            Eq("an unreachable 320 MHz radio scores zero",
                BandTools.QualityScore(-92, WifiBand.Band6, Dot11PhyType.Eht, 320, 0), 0);

            // Measured data must override the band estimate, or parsing beacons
            // would have been pointless.
            int measuredBusy6 = BandTools.QualityScore(-55, WifiBand.Band6, Dot11PhyType.Eht, 160, 95);
            int estimated6 = BandTools.QualityScore(-55, WifiBand.Band6, Dot11PhyType.Eht, 0, -1);
            _checks++;
            ok = measuredBusy6 < estimated6;
            if (!ok) _failures++;
            Console.WriteLine("  [{0}] {1,-52} {2} vs {3}", ok ? "ok" : "FAIL",
                "a measured-busy 6 GHz scores below the estimate", measuredBusy6, estimated6);

            Eq("width is capped, not extrapolated past 320 MHz",
                BandTools.QualityScore(-40, WifiBand.Band6, Dot11PhyType.Eht, 1280, 0),
                BandTools.QualityScore(-40, WifiBand.Band6, Dot11PhyType.Eht, 320, 0));

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
