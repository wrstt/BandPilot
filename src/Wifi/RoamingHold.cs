using System;

namespace BandPilot.Wifi
{
    /// <summary>
    /// Holds a pinned radio in place without dropping the connection.
    ///
    /// The obvious approach — turning down the driver's roaming keyword — works,
    /// but applying it restarts the miniport and drops the link, tearing down
    /// the very pin it was meant to protect. So the gentle rungs come first:
    ///
    ///   1. Media streaming mode. Instant, reversible, no disconnect. Drivers
    ///      respond by suppressing off-channel scans and raising the roam
    ///      threshold.
    ///   2. Background scan off. Stronger — a driver with no candidate list has
    ///      nothing to roam to, whatever its aggressiveness says.
    ///
    /// Only if the user asks for something permanent does the driver keyword
    /// come into it, and then with the disconnect stated plainly up front.
    ///
    /// Rung 2 is the dangerous one. It is a machine-global interface setting
    /// that outlives this process: leaving it off silently stops the laptop
    /// finding networks at all, with nothing pointing at BandPilot as the cause.
    /// Everything here exists to guarantee it gets turned back on.
    /// </summary>
    public sealed class RoamingHold : IDisposable
    {
        private readonly WifiService _wifi;
        private Guid _adapter;
        private bool _mediaStreamingSet;
        private bool _backgroundScanDisabled;

        public bool IsHeld { get { return _mediaStreamingSet || _backgroundScanDisabled; } }

        /// <summary>Which rungs actually took, for honest reporting in the UI.</summary>
        public string AppliedDescription { get; private set; }

        public RoamingHold(WifiService wifi)
        {
            _wifi = wifi;

            // Belt and braces. A normal exit runs Dispose; this covers the rest,
            // because the failure mode is a machine that quietly stops roaming.
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Release();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Release();
        }

        /// <summary>
        /// Applies the non-disruptive rungs. Returns false only if neither took,
        /// which means this driver ignores both opcodes.
        /// </summary>
        public bool Apply(Guid adapter)
        {
            Release();
            _adapter = adapter;

            string applied = null;

            try
            {
                _wifi.SetMediaStreamingMode(adapter, true);
                _mediaStreamingSet = true;
                applied = "media streaming mode";
            }
            catch (Exception)
            {
                // Not every driver implements this opcode. The next rung may.
            }

            try
            {
                _wifi.SetBackgroundScan(adapter, false);
                _backgroundScanDisabled = true;
                applied = applied == null
                    ? "background scanning off"
                    : applied + " and background scanning off";
            }
            catch (Exception)
            {
            }

            AppliedDescription = applied;
            return IsHeld;
        }

        /// <summary>
        /// Puts everything back. Safe to call repeatedly and safe to call when
        /// nothing was ever applied, because it is invoked from exit handlers
        /// that cannot know the state.
        /// </summary>
        public void Release()
        {
            if (_backgroundScanDisabled)
            {
                // Re-enabled first: it is the setting that would actually harm
                // the user if it were left behind.
                try { _wifi.SetBackgroundScan(_adapter, true); }
                catch (Exception) { }
                _backgroundScanDisabled = false;
            }

            if (_mediaStreamingSet)
            {
                try { _wifi.SetMediaStreamingMode(_adapter, false); }
                catch (Exception) { }
                _mediaStreamingSet = false;
            }

            AppliedDescription = null;
        }

        public void Dispose()
        {
            Release();
        }
    }
}
