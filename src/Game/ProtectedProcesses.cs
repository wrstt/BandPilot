using System;
using System.Collections.Generic;

namespace BandPilot.Game
{
    /// <summary>
    /// Processes Game Mode never touches.
    ///
    /// Two reasons appear here. Some would degrade the machine visibly if eased
    /// off the CPU — the shell, the compositor, the audio engine, the input
    /// stack. Others are load-bearing for BandPilot itself: throttling our own
    /// process, or the service the Wi-Fi pages depend on, would break the tool
    /// doing the throttling.
    ///
    /// The list is deliberately short. Everything in it is something whose
    /// absence a user would notice within seconds.
    /// </summary>
    public static class ProtectedProcesses
    {
        private static readonly HashSet<string> Names =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // kernel and session plumbing
                "system", "idle", "registry", "memory compression",
                "csrss", "wininit", "winlogon", "services", "lsass", "smss",

                // anything the user would see stutter
                "dwm", "explorer", "sihost", "fontdrvhost", "audiodg",
                "ctfmon", "taskhostw", "runtimebroker",

                // ourselves
                "bandpilot"
            };

        public static bool Contains(string processName)
        {
            return !string.IsNullOrEmpty(processName) && Names.Contains(processName);
        }

        public static int Count { get { return Names.Count; } }
    }
}
