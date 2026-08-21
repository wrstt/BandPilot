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
        /// Legacy shape, kept for callers with no beacon data. Width and airtime
        /// are estimated from the band and PHY.
        /// </summary>
        public static int QualityScore(int rssiDbm, WifiBand band, Dot11PhyType phy)
        {
            return QualityScore(rssiDbm, band, phy, 0, -1);
        }

        /// <summary>
        /// How good a radio is actually likely to be, not how loud it is.
        ///
        /// The model is throughput potential gated by whether you can hear the
        /// AP at all:
        ///
        ///     score = signal + (width + free airtime) x usability
        ///
        /// The important part is that band membership carries no bonus of its
        /// own any more. It used to: 6 GHz scored +32 and 5 GHz +18, which was
        /// really a stand-in for "those bands are usually emptier and wider".
        /// Now that width and airtime can be measured from the beacon, keeping
        /// that bonus would count the same advantage twice and over-favour
        /// 6 GHz. Where a beacon is silent the band is still used — but as an
        /// estimate of width and busyness, which is what it was always standing
        /// in for.
        ///
        /// The consequence worth knowing: wider is not better when busy. An
        /// 80 MHz channel only transmits when all four of its 20 MHz subchannels
        /// are clear, so a congested 320 MHz radio deservedly loses to a quiet
        /// 80 MHz one — exactly the judgement this tool exists to make.
        /// </summary>
        /// <param name="widthMhz">Occupied width, or 0 when the beacon did not say.</param>
        /// <param name="busyPercent">Measured channel utilisation, or -1 when unknown.</param>
        public static int QualityScore(int rssiDbm, WifiBand band, Dot11PhyType phy,
                                       int widthMhz, int busyPercent)
        {
            // Signal is worth 40 of 100. Enough to matter, not enough to let a
            // loud, narrow, crowded 2.4 GHz radio win on volume alone.
            double signal = Clamp01((rssiDbm + 90) / 50.0) * 40.0;

            // Potential is worthless if the link will not hold. This falls to
            // zero at -90 dBm, so an unreachable 320 MHz radio scores nothing
            // rather than coasting on its specification.
            double usability = Clamp01((rssiDbm + 90) / 30.0);

            int width = widthMhz > 0 ? widthMhz : EstimateWidth(band, phy);
            double widthPart = WidthScore(width) * 25.0;

            double busy = busyPercent >= 0
                ? Clamp01(busyPercent / 100.0)
                : EstimateBusy(band);
            double airtimePart = (1.0 - busy) * 35.0;

            double score = signal + (widthPart + airtimePart) * usability;

            if (score < 0) score = 0;
            if (score > 100) score = 100;
            return (int)Math.Round(score);
        }

        /// <summary>
        /// Doubling the width does not double throughput in practice, so this is
        /// logarithmic: 20 MHz scores 0 and 320 MHz scores 1.
        /// </summary>
        private static double WidthScore(int widthMhz)
        {
            if (widthMhz <= 20) return 0.0;
            double steps = Math.Log(widthMhz / 20.0, 2.0);   // 40 -> 1 ... 320 -> 4
            return Clamp01(steps / 4.0);
        }

        /// <summary>
        /// What a radio of this generation on this band is typically running
        /// when the beacon does not say. 2.4 GHz is capped at 20 MHz on purpose:
        /// 40 MHz there is antisocial, frequently disabled, and rarely achieved.
        /// </summary>
        private static int EstimateWidth(WifiBand band, Dot11PhyType phy)
        {
            if (band == WifiBand.Band24) return 20;

            switch (phy)
            {
                case Dot11PhyType.Eht: return band == WifiBand.Band6 ? 160 : 80;
                case Dot11PhyType.He: return band == WifiBand.Band6 ? 160 : 80;
                case Dot11PhyType.Vht: return 80;
                case Dot11PhyType.Ht: return 40;
                default: return 20;
            }
        }

        /// <summary>
        /// Typical busyness per band, used only when no AP on the channel
        /// reports the real figure. These numbers are the honest content of what
        /// used to be an arbitrary band bonus: 2.4 GHz is crowded, 6 GHz is
        /// nearly empty, and that is the whole reason the bands rank as they do.
        /// </summary>
        private static double EstimateBusy(WifiBand band)
        {
            switch (band)
            {
                case WifiBand.Band6: return 0.08;
                case WifiBand.Band5: return 0.25;
                case WifiBand.Band24: return 0.60;
                default: return 0.40;
            }
        }

        private static double Clamp01(double v)
        {
            if (v < 0.0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }

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
