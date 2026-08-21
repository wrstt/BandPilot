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
        /// What the driver says this card can do. Populated by WifiService when
        /// the adapter list is built, so nothing downstream has to guess from
        /// the model name.
        /// </summary>
        public AdapterCapability Capability { get; set; }

        public bool IsConnected { get { return State == WlanInterfaceState.Connected; } }

        /// <summary>
        /// Preselection rank when a machine has more than one radio. An earlier
        /// version matched Intel BE2xx model strings, which picked the wrong
        /// adapter on every card that was not on the list. Ranking on what the
        /// driver reports keeps a USB Wi-Fi 5 dongle from winning over the
        /// built-in Wi-Fi 6E card without naming either of them.
        /// </summary>
        public int PreferenceRank
        {
            get
            {
                int rank = 0;
                if (IsConnected) rank += 1000;
                if (Capability != null)
                {
                    rank += (int)Capability.MaxPhy * 10;
                    if (Capability.Supports6Ghz) rank += 50;
                    if (Capability.CanPinBssid) rank += 25;
                }
                return rank;
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
