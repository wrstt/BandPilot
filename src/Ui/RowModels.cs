using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using BandPilot.Wifi;

namespace BandPilot.Ui
{
    /// <summary>
    /// The AP list is a flat list of two row kinds rather than a grouped
    /// ItemsControl: it keeps the template simple, and the header rows can be
    /// made inert with one trigger instead of a container style selector.
    /// </summary>
    public abstract class ListRow
    {
        public virtual bool IsHeader { get { return false; } }
        public virtual bool IsConnected { get { return false; } }

        protected static Brush Res(string key)
        {
            return (Brush)Application.Current.Resources[key];
        }
    }

    public sealed class GroupRow : ListRow
    {
        public string Ssid { get; set; }
        public int RadioCount { get; set; }
        public bool NetworkIsConnected { get; set; }

        public override bool IsHeader { get { return true; } }

        public string CountLabel
        {
            get { return RadioCount == 1 ? "1 radio" : RadioCount + " radios"; }
        }

        public Visibility ConnectedVisibility
        {
            get { return NetworkIsConnected ? Visibility.Visible : Visibility.Collapsed; }
        }
    }

    public sealed class ApRow : ListRow
    {
        public AccessPoint Ap { get; set; }

        public bool Current { get; set; }
        public bool IsBest { get; set; }
        public bool Connecting { get; set; }

        /// <summary>
        /// True when the radio is visible but this card cannot join it, which in
        /// practice means a 6 GHz BSS on a card with no 6 GHz radio. Shown
        /// rather than hidden, because "why can't I see the 6 GHz one" is a
        /// worse question than a greyed row that explains itself.
        /// </summary>
        public bool Unusable { get; set; }

        public override bool IsConnected { get { return Current; } }

        public string BandLabel { get { return Ap.BandLabel; } }
        public string Channel { get { return Ap.Channel.ToString(); } }
        public string Dbm { get { return Ap.RssiDbm.ToString(); } }
        /// <summary>
        /// Generation and width together, because on their own neither says how
        /// fast a radio can go. "Wi-Fi 7" at 20 MHz is slower than "Wi-Fi 6" at
        /// 160, and width is invisible everywhere else in Windows.
        /// </summary>
        public string Generation
        {
            get
            {
                string phy = Ap.PhyLabel;
                int cut = phy.IndexOf(" (", StringComparison.Ordinal);
                if (cut > 0) phy = phy.Substring(0, cut);
                return Ap.WidthKnown ? phy + " · " + Ap.WidthMhz : phy;
            }
        }

        /// <summary>
        /// The access point's own measurement of how busy its channel is. This
        /// is the most honest congestion figure obtainable, because it counts
        /// airtime lost to hidden nodes, interference and slow legacy clients —
        /// none of which any signal-strength model can detect. Plenty of
        /// consumer routers never send it, so absence has to read as unknown
        /// rather than as zero.
        /// </summary>
        public string BusyText
        {
            get { return Ap.HasAirtimeData ? Ap.ChannelUtilisationPercent + "%" : "—"; }
        }

        public Brush BusyBrush
        {
            get
            {
                if (!Ap.HasAirtimeData) return Res("B.TextFaint");
                int busy = Ap.ChannelUtilisationPercent;
                if (busy >= 60) return Res("B.Bad");
                if (busy >= 30) return Res("B.WarnRail");
                return Res("B.Good");
            }
        }

        public string BusyTooltip
        {
            get
            {
                if (!Ap.HasAirtimeData)
                {
                    return "This access point does not report channel utilisation, so its "
                         + "rating uses a band-typical estimate instead.";
                }

                string clients = Ap.StationCount >= 0
                    ? Ap.StationCount + (Ap.StationCount == 1 ? " client" : " clients") + " · "
                    : string.Empty;
                return clients + "the AP measured its channel busy "
                     + Ap.ChannelUtilisationPercent + "% of the time";
            }
        }
        public string Bssid { get { return Ap.Bssid; } }
        public int Score { get { return Ap.Score; } }

        public string SignalGlyphs
        {
            get
            {
                int bars = Ap.Bars;
                string s = string.Empty;
                for (int i = 0; i < 4; i++) s += i < bars ? "▮" : "▯";
                return s;
            }
        }

        public double RowOpacity { get { return Unusable ? 0.55 : 1.0; } }

        public double RatingBarWidth
        {
            get
            {
                double w = Score / 100.0 * 52.0;
                if (w < 0) w = 0;
                if (w > 52) w = 52;
                return w;
            }
        }

        /// <summary>Thresholds match BandTools.QualityScore's weighting.</summary>
        public Brush RatingBrush
        {
            get
            {
                if (Score >= 58) return Res("B.Good");
                if (Score >= 38) return Res("B.WarnRail");
                return Res("B.Bad");
            }
        }

        public Brush BandForeground
        {
            get
            {
                switch (Ap.Band)
                {
                    case WifiBand.Band6: return Res("B.Band6");
                    case WifiBand.Band5: return Res("B.Band5");
                    default: return Res("B.Band24");
                }
            }
        }

        public Brush BandBackground
        {
            get
            {
                switch (Ap.Band)
                {
                    case WifiBand.Band6: return Res("B.Band6Bg");
                    case WifiBand.Band5: return Res("B.Band5Bg");
                    default: return Res("B.Band24Bg");
                }
            }
        }

        /// <summary>Recent RSSI readings for this radio, oldest first.</summary>
        public IList<int> History { get; set; }

        /// <summary>Spread in dB across the samples held.</summary>
        public int Spread { get; set; }

        public const double SparkWidth = 58;
        public const double SparkHeight = 16;

        /// <summary>
        /// The history plotted into a fixed box. Scaled against a fixed -90..-40
        /// dBm window rather than the series' own min and max: auto-scaling makes
        /// a rock-steady radio look wildly erratic, because a 1 dB wobble would
        /// fill the whole height.
        /// </summary>
        public PointCollection SparklinePoints
        {
            get
            {
                var points = new PointCollection();
                if (History == null || History.Count < 2) return points;

                int n = History.Count;
                double stepX = SparkWidth / (n - 1);

                for (int i = 0; i < n; i++)
                {
                    double normalised = (History[i] + 90) / 50.0;
                    if (normalised < 0) normalised = 0;
                    if (normalised > 1) normalised = 1;

                    points.Add(new Point(i * stepX, SparkHeight - (normalised * SparkHeight)));
                }
                return points;
            }
        }

        public Visibility SparklineVisibility
        {
            get
            {
                return History != null && History.Count >= 2
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Flags a radio whose signal swings more than 8 dB. That is roughly the
        /// point where a reading stops predicting the next one.
        /// </summary>
        public Brush SparklineBrush
        {
            get { return Spread >= 8 ? Res("B.WarnRail") : RatingBrush; }
        }

        public string SparklineTooltip
        {
            get
            {
                if (History == null || History.Count < 2) return "Not enough samples yet";
                return History.Count + " samples · " + Spread + " dB spread"
                     + (Spread >= 8 ? " — unstable" : " — steady");
            }
        }

        public string ChipText
        {
            get
            {
                if (Connecting) return "connecting";
                if (Unusable) return "needs 6 GHz card";
                if (Current) return "you are here";
                if (IsBest) return "best available";
                return null;
            }
        }

        public Visibility ChipVisibility
        {
            get { return ChipText == null ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Brush ChipBrush
        {
            get
            {
                if (Connecting) return Res("B.Accent2");
                if (Unusable) return Res("B.TextFaint");
                if (Current) return Res("B.WarnText");
                return Res("B.Accent");
            }
        }
    }
}
