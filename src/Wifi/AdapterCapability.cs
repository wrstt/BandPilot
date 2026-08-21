using System;
using System.Collections.Generic;
using BandPilot.Native;

namespace BandPilot.Wifi
{
    /// <summary>
    /// What a given wireless card can actually do, asked of the driver rather
    /// than inferred from its marketing name.
    ///
    /// BandPilot started life targeting Intel BE2xx, but the same code has to
    /// behave on the AX2xx parts and on the Realtek / MediaTek / Qualcomm radios
    /// that ship in mainstream laptops. Matching on model strings would mean a
    /// growing table that is wrong for every card nobody thought of, so nothing
    /// here branches on vendor: the capability comes from
    /// WlanGetInterfaceCapability and from what the radio itself reports seeing.
    /// </summary>
    public sealed class AdapterCapability
    {
        /// <summary>Highest PHY the driver claims to support.</summary>
        public Dot11PhyType MaxPhy { get; set; }

        /// <summary>
        /// How many BSSIDs the driver will accept in a desired-BSSID list. This
        /// is the single most important number in this class: a driver
        /// reporting 0 cannot be pinned to a specific radio at all, which is
        /// BandPilot's core feature. Better to say so plainly than to offer a
        /// button that quietly does nothing.
        /// </summary>
        public int MaxBssidListSize { get; set; }

        /// <summary>True once a 6 GHz BSS has actually been seen by this radio.</summary>
        public bool SixGhzObserved { get; set; }

        public bool CanPinBssid { get { return MaxBssidListSize > 0; } }

        /// <summary>
        /// Wi-Fi 7 hardware is 6 GHz capable in every shipping part, so EHT is
        /// taken as proof. Otherwise the only trustworthy evidence is having
        /// seen a 6 GHz BSS: scan results come from the card, so if it reported
        /// one it can tune there.
        /// </summary>
        public bool Supports6Ghz
        {
            get { return MaxPhy == Dot11PhyType.Eht || SixGhzObserved; }
        }

        /// <summary>
        /// Deliberately not the inverse of <see cref="Supports6Ghz"/>. A Wi-Fi 6
        /// card may be 6E capable, and an absence of 6 GHz networks nearby looks
        /// identical to a card that cannot use them. Claiming "no 6 GHz" would
        /// be wrong for an AX211 in a building with no 6 GHz APs, so unknown
        /// stays unknown.
        /// </summary>
        public bool SixGhzUnknown
        {
            get { return !Supports6Ghz && MaxPhy >= Dot11PhyType.He; }
        }

        public string GenerationLabel
        {
            get
            {
                switch (MaxPhy)
                {
                    case Dot11PhyType.Eht: return "Wi-Fi 7";
                    case Dot11PhyType.He: return "Wi-Fi 6";
                    case Dot11PhyType.Vht: return "Wi-Fi 5";
                    case Dot11PhyType.Ht: return "Wi-Fi 4";
                    default: return "Wi-Fi";
                }
            }
        }

        /// <summary>The chip shown beside the adapter picker.</summary>
        public string CapabilityLabel
        {
            get
            {
                if (Supports6Ghz) return GenerationLabel + " · 6 GHz ready";
                if (SixGhzUnknown) return GenerationLabel + " · no 6 GHz seen";
                return GenerationLabel;
            }
        }

        /// <summary>Null when pinning is available; otherwise why it is not.</summary>
        public string PinWarning
        {
            get
            {
                if (CanPinBssid) return null;
                return "This driver does not accept a preferred access point, so BandPilot "
                     + "cannot pin this card to one radio. Everything else still works, and "
                     + "the Adapter page can still steer which band Windows prefers.";
            }
        }

        public static AdapterCapability Unknown()
        {
            AdapterCapability c = new AdapterCapability();
            c.MaxPhy = Dot11PhyType.Unknown;
            // Assume pinning works until the driver says otherwise: a failed
            // query should not disable the main feature on a card that supports it.
            c.MaxBssidListSize = 1;
            return c;
        }

        internal static AdapterCapability FromNative(WlanInterfaceCapability native)
        {
            AdapterCapability c = new AdapterCapability();
            c.MaxBssidListSize = (int)native.dwMaxDesiredBssidListSize;

            Dot11PhyType max = Dot11PhyType.Unknown;
            if (native.dot11PhyTypes != null)
            {
                int n = (int)native.dwNumberOfSupportedPhys;
                if (n > native.dot11PhyTypes.Length) n = native.dot11PhyTypes.Length;
                for (int i = 0; i < n; i++)
                {
                    Dot11PhyType p = native.dot11PhyTypes[i];
                    // Rank by the enum order, which happens to run oldest to
                    // newest, but skip DMG (60 GHz) since it is not on this ladder.
                    if (p != Dot11PhyType.Dmg && p > max) max = p;
                }
            }
            c.MaxPhy = max;
            return c;
        }

        /// <summary>
        /// Upgrades the 6 GHz verdict from a completed scan. Called after every
        /// scan so a 6E card proves itself the moment a 6 GHz AP is in range.
        /// </summary>
        public void LearnFrom(IEnumerable<AccessPoint> scan)
        {
            if (scan == null) return;
            foreach (AccessPoint ap in scan)
            {
                if (ap.Band == WifiBand.Band6) { SixGhzObserved = true; return; }
            }
        }
    }
}
