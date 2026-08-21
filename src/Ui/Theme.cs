using System.Drawing;
using System.Windows.Forms;

namespace BandPilot.Ui
{
    /// <summary>Shared colours and small helpers for the dark UI.</summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(22, 24, 29);
        public static readonly Color Surface = Color.FromArgb(30, 33, 40);
        public static readonly Color SurfaceAlt = Color.FromArgb(38, 42, 51);
        public static readonly Color Border = Color.FromArgb(52, 57, 68);
        public static readonly Color Text = Color.FromArgb(226, 230, 238);
        public static readonly Color TextDim = Color.FromArgb(146, 154, 168);
        public static readonly Color Accent = Color.FromArgb(88, 166, 255);
        public static readonly Color Good = Color.FromArgb(86, 211, 138);
        public static readonly Color Warn = Color.FromArgb(232, 179, 76);
        public static readonly Color Bad = Color.FromArgb(238, 106, 106);

        public static readonly Color Band6 = Color.FromArgb(167, 139, 250);
        public static readonly Color Band5 = Color.FromArgb(88, 166, 255);
        public static readonly Color Band24 = Color.FromArgb(232, 179, 76);

        public static Font UiFont { get { return new Font("Segoe UI", 9f); } }
        public static Font UiFontBold { get { return new Font("Segoe UI", 9f, FontStyle.Bold); } }
        public static Font TitleFont { get { return new Font("Segoe UI Semibold", 13f); } }
        public static Font MonoFont { get { return new Font("Consolas", 9f); } }

        public static Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : SurfaceAlt,
                ForeColor = primary ? Color.FromArgb(12, 16, 24) : Text,
                Font = primary ? UiFontBold : UiFont,
                AutoSize = false,
                Height = 30,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        public static Label MakeLabel(string text, bool dim)
        {
            return new Label
            {
                Text = text,
                ForeColor = dim ? TextDim : Text,
                Font = UiFont,
                AutoSize = true,
                BackColor = Color.Transparent
            };
        }

        public static void StyleList(ListView lv)
        {
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.HideSelection = false;
            lv.BackColor = Surface;
            lv.ForeColor = Text;
            lv.BorderStyle = BorderStyle.FixedSingle;
            lv.Font = UiFont;
            lv.MultiSelect = false;
        }

        public static Color BandColor(Wifi.WifiBand band)
        {
            switch (band)
            {
                case Wifi.WifiBand.Band6: return Band6;
                case Wifi.WifiBand.Band5: return Band5;
                case Wifi.WifiBand.Band24: return Band24;
                default: return TextDim;
            }
        }

        /// <summary>
        /// Thresholds track the weighting in BandTools.QualityScore: a healthy
        /// 5 GHz radio around -60 dBm should read as good, and any 2.4 GHz radio
        /// should read as middling at best.
        /// </summary>
        public static Color ScoreColor(int score)
        {
            if (score >= 58) return Good;
            if (score >= 38) return Warn;
            return Bad;
        }

        /// <summary>A compact four-slot signal meter, e.g. "▮▮▮▯".</summary>
        public static string Bars(int bars)
        {
            var sb = new System.Text.StringBuilder(4);
            for (int i = 0; i < 4; i++) sb.Append(i < bars ? '▮' : '▯');
            return sb.ToString();
        }
    }
}
