using System;
using System.Collections.Generic;

namespace BandPilot.Wifi
{
    /// <summary>
    /// A short rolling history of signal strength per access point radio.
    ///
    /// A single RSSI reading says nothing about whether a radio is worth moving
    /// to. An access point that reads -60 dBm because you caught it at a good
    /// moment, and one that sits steadily at -60, look identical in a table and
    /// behave nothing alike. The history is what distinguishes them.
    ///
    /// Samples come from the cached BSS list, which the driver refreshes on its
    /// own schedule, so this never forces a disruptive full scan.
    /// </summary>
    public sealed class SignalHistory
    {
        /// <summary>
        /// Sixty samples at the page's five-second tick is five minutes, which
        /// is long enough to show a trend and short enough to still reflect
        /// where you are standing now.
        /// </summary>
        public const int Capacity = 60;

        private readonly Dictionary<string, List<int>> _byBssid =
            new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        public void Record(IEnumerable<AccessPoint> scan)
        {
            if (scan == null) return;

            foreach (AccessPoint ap in scan)
            {
                if (string.IsNullOrEmpty(ap.Bssid)) continue;

                List<int> series;
                if (!_byBssid.TryGetValue(ap.Bssid, out series))
                {
                    series = new List<int>(Capacity);
                    _byBssid[ap.Bssid] = series;
                }

                series.Add(ap.RssiDbm);
                if (series.Count > Capacity) series.RemoveAt(0);
            }
        }

        /// <summary>Oldest first. Empty when this radio has not been seen yet.</summary>
        public IList<int> For(string bssid)
        {
            List<int> series;
            if (bssid != null && _byBssid.TryGetValue(bssid, out series)) return series;
            return Array.Empty<int>();
        }

        /// <summary>
        /// How much the signal has moved across the samples held, in dB. A large
        /// spread means an unstable radio whose current reading should not be
        /// trusted on its own.
        /// </summary>
        public int SpreadFor(string bssid)
        {
            IList<int> series = For(bssid);
            if (series.Count < 2) return 0;

            int min = int.MaxValue, max = int.MinValue;
            foreach (int v in series)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return max - min;
        }

        public void Clear()
        {
            _byBssid.Clear();
        }
    }
}
