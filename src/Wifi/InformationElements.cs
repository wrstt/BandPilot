using System;

namespace BandPilot.Wifi
{
    /// <summary>
    /// Parses the beacon information elements attached to each BSS entry.
    ///
    /// Windows exposes the raw blob and nothing else, so everything that makes a
    /// radio worth choosing — how wide its channel is, how busy the air actually
    /// is — has to be read out of it by hand. These are the two numbers that
    /// matter most after signal strength, and Windows surfaces neither.
    ///
    /// Every read here is bounds-checked. This is the one place in the app where
    /// a mistake is a crash rather than a wrong number, and the input is written
    /// by whatever access point happens to be in range.
    /// </summary>
    public static class InformationElements
    {
        // Element IDs, from 802.11.
        private const byte IdSsid = 0;
        private const byte IdBssLoad = 11;
        private const byte IdHtOperation = 61;
        private const byte IdVhtOperation = 192;
        private const byte IdExtension = 255;

        // Sub-IDs behind the extension element.
        private const byte ExtHeOperation = 36;
        private const byte ExtEhtOperation = 106;

        public sealed class Parsed
        {
            /// <summary>Occupied channel width in MHz. 20 when nothing says otherwise.</summary>
            public int WidthMhz = 20;

            /// <summary>True when a width was actually declared rather than assumed.</summary>
            public bool WidthKnown;

            /// <summary>Stations associated with this AP, from the BSS Load element.</summary>
            public int StationCount = -1;

            /// <summary>
            /// Percentage of time the AP measured the medium as busy. This is the
            /// single most honest congestion number available, because it is the
            /// AP's own measurement — it captures hidden nodes, interference and
            /// airtime wasted on slow legacy clients, none of which any
            /// signal-strength model can see. Frequently absent on consumer gear.
            /// </summary>
            public int ChannelUtilisationPercent = -1;

            public bool HasBssLoad { get { return ChannelUtilisationPercent >= 0; } }

            /// <summary>SSID from the IE, which is how a hidden network shows up empty.</summary>
            public string Ssid;
        }

        public static Parsed Parse(byte[] ies)
        {
            var result = new Parsed();
            if (ies == null || ies.Length < 2) return result;

            // Best-first: a Wi-Fi 7 AP carries the older elements too, and they
            // describe a narrower channel than it is really using.
            bool widthResolved = false;

            int i = 0;
            while (i + 2 <= ies.Length)
            {
                byte id = ies[i];
                int len = ies[i + 1];
                int body = i + 2;

                // A length running past the buffer means a malformed or truncated
                // blob; stop rather than read whatever follows in memory.
                if (body + len > ies.Length) break;

                switch (id)
                {
                    case IdSsid:
                        if (len > 0) result.Ssid = DecodeSsid(ies, body, len);
                        break;

                    case IdBssLoad:
                        ReadBssLoad(ies, body, len, result);
                        break;

                    case IdHtOperation:
                        if (!widthResolved && ReadHtWidth(ies, body, len, result)) { }
                        break;

                    case IdVhtOperation:
                        if (ReadVhtWidth(ies, body, len, result)) widthResolved = true;
                        break;

                    case IdExtension:
                        if (len >= 1)
                        {
                            byte ext = ies[body];
                            if (ext == ExtEhtOperation)
                            {
                                if (ReadEhtWidth(ies, body + 1, len - 1, result)) widthResolved = true;
                            }
                            else if (ext == ExtHeOperation && !widthResolved)
                            {
                                if (ReadHeWidth(ies, body + 1, len - 1, result)) widthResolved = true;
                            }
                        }
                        break;
                }

                i = body + len;
            }

            return result;
        }

        // ------------------------------------------------------------------
        // BSS Load (element 11)
        //   station count      uint16 LE  @0
        //   channel util       uint8      @2   (0..255 scaled to percent)
        //   admission capacity uint16 LE  @3
        // ------------------------------------------------------------------
        private static void ReadBssLoad(byte[] b, int at, int len, Parsed r)
        {
            if (len < 3) return;

            r.StationCount = b[at] | (b[at + 1] << 8);

            // The field is a fraction of 255, not a percentage.
            r.ChannelUtilisationPercent = b[at + 2] * 100 / 255;
        }

        // ------------------------------------------------------------------
        // HT Operation (element 61): 40 MHz only when a secondary channel is
        // present AND wide operation is permitted.
        // ------------------------------------------------------------------
        private static bool ReadHtWidth(byte[] b, int at, int len, Parsed r)
        {
            if (len < 2) return false;

            int info = b[at + 1];
            bool hasSecondary = (info & 0x03) != 0;
            bool wideAllowed = (info & 0x04) != 0;

            r.WidthMhz = hasSecondary && wideAllowed ? 40 : 20;
            r.WidthKnown = true;
            return true;
        }

        // ------------------------------------------------------------------
        // VHT Operation (element 192)
        //   channel width  uint8 @0
        //   CCFS0          uint8 @1
        //   CCFS1          uint8 @2
        // Width 2 and 3 are deprecated; modern APs signal 160 and 80+80 through
        // width 1 plus the gap between the two centre-frequency segments.
        // ------------------------------------------------------------------
        private static bool ReadVhtWidth(byte[] b, int at, int len, Parsed r)
        {
            if (len < 3) return false;

            int width = b[at];
            int ccfs0 = b[at + 1];
            int ccfs1 = b[at + 2];

            int mhz;
            switch (width)
            {
                case 0:
                    return false;                 // defer to HT for 20 or 40
                case 2:
                    mhz = 160;
                    break;
                case 3:
                    mhz = 80;                     // 80+80, treated as 80 contiguous
                    break;
                default:
                    if (ccfs1 == 0) mhz = 80;
                    else if (Math.Abs(ccfs1 - ccfs0) == 8) mhz = 160;
                    else mhz = 80;
                    break;
            }

            r.WidthMhz = mhz;
            r.WidthKnown = true;
            return true;
        }

        // ------------------------------------------------------------------
        // HE Operation (extension 36). The fixed part is 6 bytes, then a run of
        // optional fields whose presence is announced in the parameters, so the
        // 6 GHz block can only be located by stepping over whatever precedes it.
        //   HE Operation Parameters  3 bytes
        //   BSS Color Information    1 byte
        //   Basic HE-MCS and NSS     2 bytes
        //   [VHT Operation Info      3 bytes]  if bit 18
        //   [Max Co-Hosted BSSID     1 byte ]  if bit 19
        //   [6 GHz Operation Info    5 bytes]  if bit 21
        // ------------------------------------------------------------------
        private static bool ReadHeWidth(byte[] b, int at, int len, Parsed r)
        {
            if (len < 6) return false;

            int p = b[at] | (b[at + 1] << 8) | (b[at + 2] << 16);
            bool vhtPresent = ((p >> 18) & 1) != 0;
            bool coHosted = ((p >> 19) & 1) != 0;
            bool sixGhzPresent = ((p >> 21) & 1) != 0;

            int cursor = at + 6;

            if (vhtPresent)
            {
                if (cursor + 3 > at + len) return false;
                if (ReadVhtWidth(b, cursor, 3, r)) { /* width taken from the VHT block */ }
                cursor += 3;
            }

            if (coHosted)
            {
                if (cursor + 1 > at + len) return r.WidthKnown;
                cursor += 1;
            }

            if (!sixGhzPresent) return r.WidthKnown;
            if (cursor + 5 > at + len) return r.WidthKnown;

            // 6 GHz Operation Information: primary channel, control, CCFS0,
            // CCFS1, minimum rate.
            int control = b[cursor + 1];
            switch (control & 0x03)
            {
                case 0: r.WidthMhz = 20; break;
                case 1: r.WidthMhz = 40; break;
                case 2: r.WidthMhz = 80; break;
                default: r.WidthMhz = 160; break;
            }
            r.WidthKnown = true;
            return true;
        }

        // ------------------------------------------------------------------
        // EHT Operation (extension 106)
        //   EHT Operation Parameters  1 byte   (B0 = operation info present)
        //   Basic EHT-MCS and NSS     4 bytes
        //   [Control                  1 byte ] B0-B2 = width, 4 means 320 MHz
        //   [CCFS0, CCFS1             2 bytes]
        // ------------------------------------------------------------------
        private static bool ReadEhtWidth(byte[] b, int at, int len, Parsed r)
        {
            if (len < 5) return false;

            bool infoPresent = (b[at] & 0x01) != 0;
            if (!infoPresent) return false;

            int cursor = at + 5;
            if (cursor + 1 > at + len) return false;

            switch (b[cursor] & 0x07)
            {
                case 0: r.WidthMhz = 20; break;
                case 1: r.WidthMhz = 40; break;
                case 2: r.WidthMhz = 80; break;
                case 3: r.WidthMhz = 160; break;
                case 4: r.WidthMhz = 320; break;
                default: return false;
            }

            r.WidthKnown = true;
            return true;
        }

        private static string DecodeSsid(byte[] b, int at, int len)
        {
            try
            {
                string s = System.Text.Encoding.UTF8.GetString(b, at, len);
                return s.TrimEnd('\0');
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
