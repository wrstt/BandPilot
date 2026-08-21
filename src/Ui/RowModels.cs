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
        public string Generation { get { return Ap.PhyLabel; } }
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
