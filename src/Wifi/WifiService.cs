using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using BandPilot.Native;

namespace BandPilot.Wifi
{
    /// <summary>
    /// Managed wrapper over the Native Wifi handle. One instance owns one
    /// WlanOpenHandle for the lifetime of the app.
    /// </summary>
    public sealed class WifiService : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed;

        public WifiService()
        {
            VerifyStructLayout();

            uint negotiated;
            IntPtr handle;
            uint rc = WlanApi.WlanOpenHandle(
                WlanApi.CLIENT_VERSION_VISTA_OR_LATER, IntPtr.Zero, out negotiated, out handle);

            if (rc != WlanApi.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)rc,
                    "Could not open a Wi-Fi handle (WlanOpenHandle failed with " + rc + "). " +
                    "The WLAN AutoConfig service must be running.");
            }
            _handle = handle;
        }

        /// <summary>
        /// Marshalling mistakes in WLAN_BSS_ENTRY produce plausible-looking but
        /// wrong signal and frequency values rather than a crash, so the sizes
        /// are checked up front where a failure is unambiguous.
        /// </summary>
        private static void VerifyStructLayout()
        {
            int bss = Marshal.SizeOf(typeof(WlanBssEntry));
            int conn = Marshal.SizeOf(typeof(WlanConnectionAttributes));
            if (bss != 360 || conn != 604)
            {
                throw new InvalidOperationException(
                    "Unexpected native struct layout (WLAN_BSS_ENTRY=" + bss + " expected 360, " +
                    "WLAN_CONNECTION_ATTRIBUTES=" + conn + " expected 604). " +
                    "BandPilot must be built and run as x64.");
            }
        }

        public List<WifiAdapter> GetAdapters()
        {
            var result = new List<WifiAdapter>();
            IntPtr list;
            uint rc = WlanApi.WlanEnumInterfaces(_handle, IntPtr.Zero, out list);
            if (rc != WlanApi.ERROR_SUCCESS) throw new Win32Exception((int)rc, "WlanEnumInterfaces failed.");

            try
            {
                int count = Marshal.ReadInt32(list, 0);
                // dwNumberOfItems(4) + dwIndex(4), then the array.
                IntPtr cursor = IntPtr.Add(list, 8);
                int stride = Marshal.SizeOf(typeof(WlanInterfaceInfo));

                for (int i = 0; i < count; i++)
                {
                    var info = (WlanInterfaceInfo)Marshal.PtrToStructure(
                        IntPtr.Add(cursor, i * stride), typeof(WlanInterfaceInfo));

                    result.Add(new WifiAdapter
                    {
                        Guid = info.InterfaceGuid,
                        Description = info.strInterfaceDescription,
                        State = info.isState
                    });
                }
            }
            finally
            {
                WlanApi.WlanFreeMemory(list);
            }
            return result;
        }

        /// <summary>
        /// Asks the driver to run a fresh scan. This returns immediately; the
        /// results land in the BSS list a few seconds later, so callers should
        /// wait before calling <see cref="GetAccessPoints"/> again.
        /// </summary>
        public void StartScan(Guid adapter)
        {
            uint rc = WlanApi.WlanScan(_handle, ref adapter, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            // ERROR_BUSY (170) just means a scan is already running, which is fine.
            if (rc != WlanApi.ERROR_SUCCESS && rc != 170)
            {
                throw new Win32Exception((int)rc, "WlanScan failed.");
            }
        }

        /// <summary>
        /// Every BSS the adapter can currently hear, one entry per AP radio.
        /// Two entries with the same SSID and different BSSIDs are what makes
        /// band pinning possible.
        /// </summary>
        public List<AccessPoint> GetAccessPoints(Guid adapter, CurrentConnection current)
        {
            var result = new List<AccessPoint>();
            IntPtr bssList;

            uint rc = WlanApi.WlanGetNetworkBssList(
                _handle, ref adapter, IntPtr.Zero, Dot11BssType.Any, false, IntPtr.Zero, out bssList);

            if (rc != WlanApi.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)rc, "WlanGetNetworkBssList failed.");
            }

            try
            {
                int count = Marshal.ReadInt32(bssList, 4);   // dwNumberOfItems
                IntPtr cursor = IntPtr.Add(bssList, 8);      // entries start here
                int stride = Marshal.SizeOf(typeof(WlanBssEntry));

                for (int i = 0; i < count; i++)
                {
                    var e = (WlanBssEntry)Marshal.PtrToStructure(
                        IntPtr.Add(cursor, i * stride), typeof(WlanBssEntry));

                    int mhz = BandTools.FrequencyKhzToMhz(e.ulChCenterFrequency);
                    var ap = new AccessPoint
                    {
                        Ssid = DecodeSsid(e.dot11Ssid),
                        BssidBytes = e.dot11Bssid,
                        Bssid = BandTools.FormatBssid(e.dot11Bssid),
                        RssiDbm = e.lRssi,
                        LinkQuality = (int)e.uLinkQuality,
                        FrequencyMhz = mhz,
                        Channel = BandTools.ChannelFromMhz(mhz),
                        Band = BandTools.BandFromMhz(mhz),
                        Phy = e.dot11BssPhyType
                    };

                    if (current != null && current.Connected)
                    {
                        ap.IsCurrent = BandTools.MacEquals(ap.BssidBytes, current.BssidBytes);
                    }
                    result.Add(ap);
                }
            }
            finally
            {
                WlanApi.WlanFreeMemory(bssList);
            }
            return result;
        }

        public CurrentConnection GetCurrentConnection(Guid adapter)
        {
            uint size;
            IntPtr data;
            uint rc = WlanApi.WlanQueryInterface(
                _handle, ref adapter, WlanIntfOpcode.CurrentConnection,
                IntPtr.Zero, out size, out data, IntPtr.Zero);

            // ERROR_INVALID_STATE (5023) is the normal answer when not associated.
            if (rc != WlanApi.ERROR_SUCCESS || data == IntPtr.Zero)
            {
                return new CurrentConnection { Connected = false };
            }

            try
            {
                var attrs = (WlanConnectionAttributes)Marshal.PtrToStructure(
                    data, typeof(WlanConnectionAttributes));
                var assoc = attrs.wlanAssociationAttributes;

                return new CurrentConnection
                {
                    Connected = attrs.isState == WlanInterfaceState.Connected,
                    ProfileName = attrs.strProfileName,
                    Ssid = DecodeSsid(assoc.dot11Ssid),
                    BssidBytes = assoc.dot11Bssid,
                    Bssid = BandTools.FormatBssid(assoc.dot11Bssid),
                    SignalQuality = (int)assoc.wlanSignalQuality,
                    RxRateKbps = assoc.ulRxRate,
                    TxRateKbps = assoc.ulTxRate,
                    Phy = assoc.dot11PhyType
                };
            }
            finally
            {
                WlanApi.WlanFreeMemory(data);
            }
        }

        public List<string> GetProfileNames(Guid adapter)
        {
            var names = new List<string>();
            IntPtr list;
            uint rc = WlanApi.WlanGetProfileList(_handle, ref adapter, IntPtr.Zero, out list);
            if (rc != WlanApi.ERROR_SUCCESS) return names;

            try
            {
                int count = Marshal.ReadInt32(list, 0);
                IntPtr cursor = IntPtr.Add(list, 8);
                int stride = Marshal.SizeOf(typeof(WlanProfileInfo));
                for (int i = 0; i < count; i++)
                {
                    var info = (WlanProfileInfo)Marshal.PtrToStructure(
                        IntPtr.Add(cursor, i * stride), typeof(WlanProfileInfo));
                    names.Add(info.strProfileName);
                }
            }
            finally
            {
                WlanApi.WlanFreeMemory(list);
            }
            return names;
        }

        /// <summary>
        /// Connects using a saved profile but restricted to a single BSSID.
        ///
        /// This is the operation the whole tool is built around: it is how you
        /// land on the 5 GHz radio of a particular access point instead of
        /// whichever radio Windows happened to prefer.
        /// </summary>
        public void ConnectToBssid(Guid adapter, string profileName, byte[] bssid)
        {
            if (string.IsNullOrEmpty(profileName))
                throw new ArgumentException("A saved Windows profile is required to pin an AP.", "profileName");
            if (bssid == null || bssid.Length < 6)
                throw new ArgumentException("A 6-byte BSSID is required.", "bssid");

            IntPtr bssidList = BuildBssidList(bssid);
            try
            {
                var p = new WlanConnectionParameters
                {
                    wlanConnectionMode = WlanConnectionMode.Profile,
                    strProfile = profileName,
                    pDot11Ssid = IntPtr.Zero,
                    pDesiredBssidList = bssidList,
                    dot11BssType = Dot11BssType.Infrastructure,
                    dwFlags = 0
                };

                uint rc = WlanApi.WlanConnect(_handle, ref adapter, ref p, IntPtr.Zero);
                if (rc != WlanApi.ERROR_SUCCESS)
                {
                    throw new Win32Exception((int)rc, "WlanConnect failed with code " + rc + ".");
                }
            }
            finally
            {
                if (bssidList != IntPtr.Zero) Marshal.FreeHGlobal(bssidList);
            }
        }

        /// <summary>Reconnects to the profile with no BSSID restriction.</summary>
        public void ConnectAuto(Guid adapter, string profileName)
        {
            var p = new WlanConnectionParameters
            {
                wlanConnectionMode = WlanConnectionMode.Profile,
                strProfile = profileName,
                pDot11Ssid = IntPtr.Zero,
                pDesiredBssidList = IntPtr.Zero,
                dot11BssType = Dot11BssType.Infrastructure,
                dwFlags = 0
            };
            uint rc = WlanApi.WlanConnect(_handle, ref adapter, ref p, IntPtr.Zero);
            if (rc != WlanApi.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)rc, "WlanConnect failed with code " + rc + ".");
            }
        }

        public void Disconnect(Guid adapter)
        {
            WlanApi.WlanDisconnect(_handle, ref adapter, IntPtr.Zero);
        }

        /// <summary>
        /// Builds a DOT11_BSSID_LIST holding exactly one entry.
        ///
        /// Layout: NDIS_OBJECT_HEADER { Type, Revision, Size } at 0..3,
        /// uNumOfEntries at 4, uTotalNumOfEntries at 8, BSSIDs[] at 12.
        /// sizeof() in C pads the single-entry form to 20 bytes and the Size
        /// field must agree with that, not with the 18 bytes actually used.
        /// </summary>
        private static IntPtr BuildBssidList(byte[] bssid)
        {
            const byte NDIS_OBJECT_TYPE_DEFAULT = 0x80;
            const byte DOT11_BSSID_LIST_REVISION_1 = 1;
            const int size = 20;

            IntPtr p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);

            Marshal.WriteByte(p, 0, NDIS_OBJECT_TYPE_DEFAULT);
            Marshal.WriteByte(p, 1, DOT11_BSSID_LIST_REVISION_1);
            Marshal.WriteInt16(p, 2, size);
            Marshal.WriteInt32(p, 4, 1);   // uNumOfEntries
            Marshal.WriteInt32(p, 8, 1);   // uTotalNumOfEntries
            for (int i = 0; i < 6; i++) Marshal.WriteByte(p, 12 + i, bssid[i]);

            return p;
        }

        private static string DecodeSsid(Dot11Ssid ssid)
        {
            if (ssid.ucSSID == null || ssid.uSSIDLength == 0) return "(hidden network)";
            int len = (int)Math.Min(ssid.uSSIDLength, 32u);
            string s = Encoding.UTF8.GetString(ssid.ucSSID, 0, len);
            return string.IsNullOrWhiteSpace(s) ? "(hidden network)" : s;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                WlanApi.WlanCloseHandle(_handle, IntPtr.Zero);
                _handle = IntPtr.Zero;
            }
        }
    }
}
