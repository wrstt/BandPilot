using System;
using BandPilot.Native;

namespace BandPilot.Wifi
{
    public enum WifiBand
    {
        Unknown = 0,
        Band24 = 24,
        Band5 = 5,
        Band6 = 6
    }

    /// <summary>
    /// Frequency/channel/PHY interpretation. The Native Wifi API reports a
    /// centre frequency in kHz and nothing else useful for band identification,
    /// so everything the UI shows about bands is derived here.
    /// </summary>
    public static class BandTools
    {
        public static int FrequencyKhzToMhz(uint khz)
        {
            return (int)Math.Round(khz / 1000.0);
        }

        public static WifiBand BandFromMhz(int mhz)
        {
            if (mhz >= 2400 && mhz <= 2500) return WifiBand.Band24;
            if (mhz >= 4900 && mhz <= 5895) return WifiBand.Band5;
            // 6 GHz (UNII-5..8) runs 5925-7125. The 5925-5945 sliver overlaps
            // nothing in practice, so an upper-open test is safe here.
            if (mhz >= 5925 && mhz <= 7125) return WifiBand.Band6;
            return WifiBand.Unknown;
        }

        public static string BandLabel(WifiBand band)
        {
            switch (band)
            {
                case WifiBand.Band24: return "2.4 GHz";
                case WifiBand.Band5: return "5 GHz";
                case WifiBand.Band6: return "6 GHz";
                default: return "?";
            }
        }

        /// <summary>
        /// Converts a centre frequency in MHz to an 802.11 channel number.
        /// Returns 0 when the frequency falls outside the known plans.
        /// </summary>
        public static int ChannelFromMhz(int mhz)
        {
            if (mhz == 2484) return 14;                       // Japan-only 11b
            if (mhz >= 2412 && mhz <= 2472) return (mhz - 2407) / 5;
            if (mhz >= 5000 && mhz <= 5895) return (mhz - 5000) / 5;
            if (mhz == 5935) return 2;                        // 6 GHz channel 2
            if (mhz >= 5955 && mhz <= 7115) return (mhz - 5950) / 5;
            if (mhz >= 4900 && mhz < 5000) return (mhz - 4000) / 5;
            return 0;
        }

        public static string PhyLabel(Dot11PhyType phy)
        {
            switch (phy)
            {
                case Dot11PhyType.Eht: return "Wi-Fi 7 (be)";
                case Dot11PhyType.He: return "Wi-Fi 6 (ax)";
                case Dot11PhyType.Vht: return "Wi-Fi 5 (ac)";
                case Dot11PhyType.Ht: return "Wi-Fi 4 (n)";
                case Dot11PhyType.Erp: return "802.11g";
                case Dot11PhyType.Ofdm: return "802.11a";
                case Dot11PhyType.Hrdsss: return "802.11b";
                case Dot11PhyType.Dmg: return "802.11ad";
                case Dot11PhyType.Dsss: return "DSSS";
                case Dot11PhyType.Fhss: return "FHSS";
                default: return "unknown";
            }
        }

        public static string FormatBssid(byte[] mac)
        {
            if (mac == null || mac.Length < 6) return "??:??:??:??:??:??";
            return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
        }

        public static bool MacEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length < 6 || b.Length < 6) return false;
            for (int i = 0; i < 6; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// A 0-100 score used only for sorting and colouring the AP list. It is
        /// deliberately not "signal strength": a strong 2.4 GHz AP is usually a
        /// worse choice than a moderate 6 GHz one on a crowded hotel network,
        /// which is exactly the judgement call this tool exists to make easier.
        /// </summary>
        public static int QualityScore(int rssiDbm, WifiBand band, Dot11PhyType phy)
        {
            // Signal is deliberately worth only 60 of the 100 points. A 2.4 GHz
            // radio at -50 dBm is typically slower in practice than a 6 GHz one
            // at -70, because 2.4 GHz is narrow and crowded, so letting raw RSSI
            // dominate would recommend exactly the wrong radio. The weights are
            // pinned by the tests in tests/LayoutTests.
            double normalised = (rssiDbm + 90) / 50.0;      // -90..-40 dBm -> 0..1
            if (normalised < 0.0) normalised = 0.0;
            if (normalised > 1.0) normalised = 1.0;
            int signal = (int)Math.Round(normalised * 60.0);

            int bandBonus;
            switch (band)
            {
                case WifiBand.Band6: bandBonus = 32; break;   // widest, least congested
                case WifiBand.Band5: bandBonus = 18; break;
                case WifiBand.Band24: bandBonus = 0; break;   // range at the cost of throughput
                default: bandBonus = 0; break;
            }

            int phyBonus;
            switch (phy)
            {
                case Dot11PhyType.Eht: phyBonus = 8; break;
                case Dot11PhyType.He: phyBonus = 5; break;
                case Dot11PhyType.Vht: phyBonus = 2; break;
                default: phyBonus = 0; break;
            }

            int score = signal + bandBonus + phyBonus;
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            return score;
        }

        /// <summary>Signal bars 0-4, from raw RSSI only.</summary>
        public static int SignalBars(int rssiDbm)
        {
            if (rssiDbm >= -55) return 4;
            if (rssiDbm >= -67) return 3;
            if (rssiDbm >= -75) return 2;
            if (rssiDbm >= -85) return 1;
            return 0;
        }
    }
}
