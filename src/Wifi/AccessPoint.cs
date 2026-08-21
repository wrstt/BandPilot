using BandPilot.Native;

namespace BandPilot.Wifi
{
    /// <summary>One BSS: a single radio on a single access point.</summary>
    public sealed class AccessPoint
    {
        public string Ssid { get; set; }
        public byte[] BssidBytes { get; set; }
        public string Bssid { get; set; }
        public int RssiDbm { get; set; }
        public int LinkQuality { get; set; }
        public int FrequencyMhz { get; set; }
        public int Channel { get; set; }
        public WifiBand Band { get; set; }
        public Dot11PhyType Phy { get; set; }
        public bool IsCurrent { get; set; }

        public string BandLabel { get { return BandTools.BandLabel(Band); } }
        public string PhyLabel { get { return BandTools.PhyLabel(Phy); } }
        public int Score { get { return BandTools.QualityScore(RssiDbm, Band, Phy); } }
        public int Bars { get { return BandTools.SignalBars(RssiDbm); } }

        /// <summary>
        /// The last three MAC octets, which is what actually distinguishes the
        /// radios of one AP from another on the same SSID.
        /// </summary>
        public string ShortBssid
        {
            get { return Bssid != null && Bssid.Length >= 8 ? Bssid.Substring(9) : Bssid; }
        }
    }

    public sealed class WifiAdapter
    {
        public System.Guid Guid { get; set; }
        public string Description { get; set; }
        public WlanInterfaceState State { get; set; }

        public override string ToString()
        {
            return Description;
        }

        /// <summary>
        /// True for the Wi-Fi 7 parts this tool was written for. Used only to
        /// preselect the right adapter when a machine has more than one radio.
        /// </summary>
        public bool LooksLikeBe2xx
        {
            get
            {
                if (string.IsNullOrEmpty(Description)) return false;
                string d = Description.ToUpperInvariant();
                return d.Contains("BE200") || d.Contains("BE201") || d.Contains("BE202")
                    || d.Contains("BE1750") || d.Contains("WI-FI 7") || d.Contains("WIFI 7")
                    || d.Contains("BE21") || d.Contains("BE22");
            }
        }
    }

    public sealed class CurrentConnection
    {
        public bool Connected { get; set; }
        public string ProfileName { get; set; }
        public string Ssid { get; set; }
        public byte[] BssidBytes { get; set; }
        public string Bssid { get; set; }
        public int SignalQuality { get; set; }
        public uint RxRateKbps { get; set; }
        public uint TxRateKbps { get; set; }
        public Dot11PhyType Phy { get; set; }
    }
}
