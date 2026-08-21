using System;
using System.Runtime.InteropServices;

namespace BandPilot.Native
{
    /// <summary>
    /// P/Invoke surface for the Windows Native Wifi API (wlanapi.dll).
    ///
    /// The only reason this file exists is <see cref="WlanConnect"/> with a
    /// desired-BSSID list. Neither the Windows network flyout nor "netsh wlan"
    /// can target one specific access point / band inside an SSID, which is the
    /// whole point of BandPilot.
    /// </summary>
    internal static class WlanApi
    {
        private const string Dll = "wlanapi.dll";

        internal const uint ERROR_SUCCESS = 0;
        internal const uint CLIENT_VERSION_VISTA_OR_LATER = 2;

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanOpenHandle(
            uint dwClientVersion,
            IntPtr pReserved,
            out uint pdwNegotiatedVersion,
            out IntPtr phClientHandle);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport(Dll)]
        internal static extern void WlanFreeMemory(IntPtr pMemory);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanGetInterfaceCapability(
            IntPtr hClientHandle,
            [In] ref Guid pInterfaceGuid,
            IntPtr pReserved,
            out IntPtr ppCapability);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanEnumInterfaces(
            IntPtr hClientHandle,
            IntPtr pReserved,
            out IntPtr ppInterfaceList);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanScan(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            IntPtr pDot11Ssid,
            IntPtr pIeData,
            IntPtr pReserved);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanGetNetworkBssList(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            IntPtr pDot11Ssid,
            Dot11BssType dot11BssType,
            [MarshalAs(UnmanagedType.Bool)] bool bSecurityEnabled,
            IntPtr pReserved,
            out IntPtr ppWlanBssList);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanQueryInterface(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            WlanIntfOpcode OpCode,
            IntPtr pReserved,
            out uint pdwDataSize,
            out IntPtr ppData,
            IntPtr pWlanOpcodeValueType);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanSetInterface(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            WlanIntfOpcode OpCode,
            uint dwDataSize,
            IntPtr pData,
            IntPtr pReserved);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanConnect(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            ref WlanConnectionParameters pConnectionParameters,
            IntPtr pReserved);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanDisconnect(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            IntPtr pReserved);

        [DllImport(Dll, SetLastError = true)]
        internal static extern uint WlanGetProfileList(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            IntPtr pReserved,
            out IntPtr ppProfileList);
    }

    internal enum Dot11BssType
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3
    }

    /// <summary>Public because it surfaces on the Wifi model types.</summary>
    public enum Dot11PhyType
    {
        Unknown = 0,
        Fhss = 1,
        Dsss = 2,
        IrBaseband = 3,
        Ofdm = 4,        // 802.11a
        Hrdsss = 5,      // 802.11b
        Erp = 6,         // 802.11g
        Ht = 7,          // 802.11n   (Wi-Fi 4)
        Vht = 8,         // 802.11ac  (Wi-Fi 5)
        Dmg = 9,         // 802.11ad
        He = 10,         // 802.11ax  (Wi-Fi 6/6E)
        Eht = 11         // 802.11be  (Wi-Fi 7)  <- BE200 / BE201 / BE202
    }

    internal enum WlanIntfOpcode
    {
        AutoconfEnabled = 1,
        BackgroundScanEnabled = 2,
        MediaStreamingMode = 3,
        RadioState = 4,
        BssType = 5,
        InterfaceState = 6,
        CurrentConnection = 7,
        ChannelNumber = 8
    }

    /// <summary>Public because it surfaces on the Wifi model types.</summary>
    public enum WlanInterfaceState
    {
        NotReady = 0,
        Connected = 1,
        AdHocNetworkFormed = 2,
        Disconnecting = 3,
        Disconnected = 4,
        Associating = 5,
        Discovering = 6,
        Authenticating = 7
    }

    internal enum WlanConnectionMode
    {
        Profile = 0,
        TemporaryProfile = 1,
        DiscoverySecure = 2,
        DiscoveryUnsecure = 3,
        Auto = 4,
        Invalid = 5
    }

    // CharSet.Unicode is required wherever ByValTStr appears: these are WCHAR
    // arrays, and the default (Ansi) would halve every string field's footprint
    // and shift everything after it.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanInterfaceInfo
    {
        internal Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string strInterfaceDescription;
        internal WlanInterfaceState isState;
    }

    /// <summary>
    /// DOT11_SSID. uSSIDLength (4) + ucSSID[32] = 36 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Dot11Ssid
    {
        internal uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] ucSSID;
    }

    /// <summary>
    /// WLAN_RATE_SET: uRateSetLength (4) + usRateSet[126] (252) = 256 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanRateSet
    {
        internal uint uRateSetLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
        internal ushort[] usRateSet;
    }

    /// <summary>
    /// WLAN_BSS_ENTRY. Expected size on x64 is 360 bytes; the layout is
    /// verified at runtime by <c>WifiService</c> before any pointer walking so
    /// that a marshalling mistake surfaces as a clear message rather than
    /// garbage signal readings.
    ///
    /// Note bInRegDomain is a 1-byte BOOLEAN, not a 4-byte Win32 BOOL. Declaring
    /// it as <c>bool</c> would shift every field after it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanBssEntry
    {
        internal Dot11Ssid dot11Ssid;                 //   0 .. 35
        internal uint uPhyId;                         //  36 .. 39
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        internal byte[] dot11Bssid;                   //  40 .. 45   (+2 pad)
        internal Dot11BssType dot11BssType;           //  48 .. 51
        internal Dot11PhyType dot11BssPhyType;        //  52 .. 55
        internal int lRssi;                           //  56 .. 59
        internal uint uLinkQuality;                   //  60 .. 63
        internal byte bInRegDomain;                   //  64        (+1 pad)
        internal ushort usBeaconPeriod;               //  66 .. 67  (+4 pad)
        internal ulong ullTimestamp;                  //  72 .. 79
        internal ulong ullHostTimestamp;              //  80 .. 87
        internal ushort usCapabilityInformation;      //  88 .. 89  (+2 pad)
        internal uint ulChCenterFrequency;            //  92 .. 95   (kHz)
        internal WlanRateSet wlanRateSet;             //  96 .. 351
        internal uint ulIeOffset;                     // 352 .. 355
        internal uint ulIeSize;                       // 356 .. 359
    }

    /// <summary>
    /// WLAN_ASSOCIATION_ATTRIBUTES, 68 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanAssociationAttributes
    {
        internal Dot11Ssid dot11Ssid;                 //  0 .. 35
        internal Dot11BssType dot11BssType;           // 36 .. 39
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        internal byte[] dot11Bssid;                   // 40 .. 45  (+2 pad)
        internal Dot11PhyType dot11PhyType;           // 48 .. 51
        internal uint uDot11PhyIndex;                 // 52 .. 55
        internal uint wlanSignalQuality;              // 56 .. 59  (0..100)
        internal uint ulRxRate;                       // 60 .. 63  (kbps)
        internal uint ulTxRate;                       // 64 .. 67  (kbps)
    }

    /// <summary>
    /// WLAN_SECURITY_ATTRIBUTES, 16 bytes. These are real 4-byte Win32 BOOLs.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanSecurityAttributes
    {
        internal int bSecurityEnabled;
        internal int bOneXEnabled;
        internal int dot11AuthAlgorithm;
        internal int dot11CipherAlgorithm;
    }

    /// <summary>
    /// WLAN_CONNECTION_ATTRIBUTES, 604 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanConnectionAttributes
    {
        internal WlanInterfaceState isState;              //   0 ..   3
        internal WlanConnectionMode wlanConnectionMode;   //   4 ..   7
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string strProfileName;                   //   8 .. 519
        internal WlanAssociationAttributes wlanAssociationAttributes; // 520 .. 587
        internal WlanSecurityAttributes wlanSecurityAttributes;       // 588 .. 603
    }

    /// <summary>
    /// WLAN_CONNECTION_PARAMETERS. 40 bytes on x64.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanConnectionParameters
    {
        internal WlanConnectionMode wlanConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string strProfile;
        internal IntPtr pDot11Ssid;
        internal IntPtr pDesiredBssidList;
        internal Dot11BssType dot11BssType;
        internal uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanProfileInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string strProfileName;
        internal uint dwFlags;
    }

    internal enum WlanInterfaceType
    {
        EmulatedDot11 = 0,
        Dot11 = 1,
        Irda = 2,
        Invalid = 3
    }

    /// <summary>
    /// WLAN_INTERFACE_CAPABILITY. dwMaxDesiredBssidListSize is the field that
    /// matters most here: a driver reporting 0 cannot honour a desired-BSSID
    /// list at all, which means pinning to a specific radio is impossible on
    /// that card no matter what the UI offers.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanInterfaceCapability
    {
        internal WlanInterfaceType interfaceType;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool bDot11DSupported;
        internal uint dwMaxDesiredSsidListSize;
        internal uint dwMaxDesiredBssidListSize;
        internal uint dwNumberOfSupportedPhys;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        internal Dot11PhyType[] dot11PhyTypes;
    }
}
