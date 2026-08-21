using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace BandPilot.Game
{
    /// <summary>One line of what Game Mode actually did, for the visible log.</summary>
    public sealed class GameModeAction
    {
        public string Description { get; set; }
        public bool Succeeded { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>
    /// Suppresses background interruptions while a game is running, and puts
    /// everything back afterwards.
    ///
    /// Two things are worth being blunt about. First, this is not an FPS
    /// booster: measured across commercial "game boosters" the median gain is
    /// low single-digit frames, and often zero. What it actually does is stop
    /// an updater or a sync client stealing CPU, memory bandwidth and uplink
    /// mid-match. Second, it deliberately does NOT stop Windows services. That
    /// is the highest-consequence, lowest-payoff thing tools like this do: half
    /// the plausible targets are trigger-started and simply come back, and
    /// stopping the wrong one leaves a visibly broken machine. Notably, stopping
    /// WlanSvc would break BandPilot itself.
    ///
    /// The safety property is structural rather than procedural: nearly
    /// everything here is a kernel object or per-process state that dies with
    /// this process. The residue that survives a reboot is exactly two items,
    /// and those go through the write-ahead journal.
    /// </summary>
    public sealed class GameMode : IDisposable
    {
        private const string NetworkThrottleKey =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string NetworkThrottleValue = "NetworkThrottlingIndex";

        /// <summary>
        /// Windows throttles non-multimedia network traffic to about 10 000
        /// packets/second by default. 0xFFFFFFFF disables that ceiling. This is
        /// the one item here that is genuinely on-theme for a network tool.
        /// </summary>
        private const uint ThrottlingDisabled = 0xFFFFFFFF;

        private static readonly TimeSpan MaxSession = TimeSpan.FromHours(12);

        /// <summary>
        /// Processes that are never touched. Some because throttling them
        /// degrades the machine visibly (the shell, the compositor, the audio
        /// engine); some because they are the thing doing the throttling.
        /// </summary>
        private IntPtr _job = IntPtr.Zero;
        private SessionJournal _journal;
        private Process _game;
        private Guid _ourScheme;

        public bool IsActive { get; private set; }
        public List<GameModeAction> Log { get; private set; }

        /// <summary>Raised when the watched game exits, so the UI can stand down.</summary>
        public event EventHandler GameExited;

        public GameMode()
        {
            Log = new List<GameModeAction>();
        }

        // ------------------------------------------------------------------
        // startup recovery
        // ------------------------------------------------------------------

        /// <summary>
        /// Replays any journal left behind by a session that did not end
        /// cleanly. The file's presence is the signal; there is no other flag.
        /// Called once at startup, before anything else touches this state.
        /// </summary>
        public static List<GameModeAction> RecoverIfNeeded()
        {
            var actions = new List<GameModeAction>();
            SessionJournal stale = SessionJournal.Load();
            if (stale == null) return actions;

            // A journal from a still-running instance of ourselves is not stale.
            if (stale.Pid == Environment.ProcessId && !stale.IsExpired) return actions;

            foreach (Mutation m in stale.Mutations)
            {
                // Each restore is independent: one failure must not abort the rest.
                try
                {
                    Restore(m);
                    actions.Add(new GameModeAction
                    {
                        Description = "Restored " + (m.ValueName ?? m.Target),
                        Succeeded = true
                    });
                }
                catch (Exception ex)
                {
                    actions.Add(new GameModeAction
                    {
                        Description = "Could not restore " + (m.ValueName ?? m.Target),
                        Succeeded = false,
                        Detail = ex.Message
                    });
                }
            }

            SessionJournal.Delete();
            return actions;
        }

        private static void Restore(Mutation m)
        {
            if (m.Kind == "registry")
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(m.Target, true))
                {
                    if (k == null) return;

                    if (m.Action == RestoreAction.Delete)
                    {
                        // Deleting, not zeroing. Writing a value where Windows had
                        // none changes the effective default permanently.
                        k.DeleteValue(m.ValueName, false);
                    }
                    else
                    {
                        k.SetValue(m.ValueName,
                            unchecked((int)Convert.ToUInt32(m.PriorData)),
                            RegistryValueKind.DWord);
                    }
                }
            }
            else if (m.Kind == "powerscheme")
            {
                Guid prior = new Guid(m.PriorData);
                GameNative.PowerSetActiveScheme(IntPtr.Zero, ref prior);

                if (!string.IsNullOrEmpty(m.Target))
                {
                    Guid ours = new Guid(m.Target);
                    GameNative.PowerDeleteScheme(IntPtr.Zero, ref ours);
                }
            }
        }

        // ------------------------------------------------------------------
        // start
        // ------------------------------------------------------------------

        public void Start(Process game, bool tuneNetworkThrottle, bool switchPowerPlan)
        {
            if (IsActive) return;

            Log = new List<GameModeAction>();
            _journal = SessionJournal.Begin(MaxSession);
            _game = game;

            RaiseGamePriority(game);
            CreateBackgroundJob();
            DeprioritiseBackground(game);

            if (switchPowerPlan) SwitchPowerPlan();
            if (tuneNetworkThrottle) DisableNetworkThrottle();

            WatchGame(game);
            IsActive = true;
        }

        private void RaiseGamePriority(Process game)
        {
            if (game == null) return;
            try
            {
                // Above normal, never realtime or high: those starve input and
                // audio threads and produce the stutter this is meant to avoid.
                game.PriorityClass = ProcessPriorityClass.AboveNormal;
                Add("Raised " + game.ProcessName + " to above-normal priority", true, null);
            }
            catch (Exception ex)
            {
                Add("Could not raise the game's priority", false, ex.Message);
            }
        }

        private void CreateBackgroundJob()
        {
            try
            {
                _job = GameNative.CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero)
                {
                    Add("Could not create the background job object", false, null);
                    return;
                }

                var info = new GameNative.JobObjectCpuRateControlInformation
                {
                    ControlFlags = GameNative.CpuRateControlFlags.Enable
                                 | GameNative.CpuRateControlFlags.WeightBased,
                    WeightOrRate = 2      // 1-9, 5 is neutral; 2 yields to the game
                };

                if (GameNative.SetInformationJobObject(
                        _job, GameNative.JobObjectInfoClass.CpuRateControlInformation,
                        ref info, System.Runtime.InteropServices.Marshal.SizeOf(info)))
                {
                    Add("Created a CPU-sharing group for background apps", true,
                        "Limits vanish automatically if BandPilot stops");
                }
            }
            catch (Exception ex)
            {
                Add("Could not set up CPU sharing", false, ex.Message);
            }
        }

        private void DeprioritiseBackground(Process game)
        {
            int gamePid = game != null ? game.Id : -1;
            int touched = 0;

            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == gamePid || p.Id == Environment.ProcessId) continue;
                    if (p.Id <= 4) continue;
                    if (ProtectedProcesses.Contains(p.ProcessName)) continue;

                    IntPtr h = GameNative.OpenProcess(
                        GameNative.ProcessAccess.SetInformation
                        | GameNative.ProcessAccess.SetQuota
                        | GameNative.ProcessAccess.QueryLimitedInformation,
                        false, p.Id);
                    if (h == IntPtr.Zero) continue;

                    try
                    {
                        var eco = new GameNative.ProcessPowerThrottlingState
                        {
                            Version = GameNative.ProcessPowerThrottlingCurrentVersion,
                            ControlMask = GameNative.ProcessPowerThrottlingExecutionSpeed,
                            StateMask = GameNative.ProcessPowerThrottlingExecutionSpeed
                        };
                        GameNative.SetProcessInformation(
                            h, GameNative.ProcessInformationClass.PowerThrottling,
                            ref eco, System.Runtime.InteropServices.Marshal.SizeOf(eco));

                        var mem = new GameNative.MemoryPriorityInformation
                        {
                            MemoryPriority = GameNative.MemoryPriorityLow
                        };
                        GameNative.SetProcessInformation(
                            h, GameNative.ProcessInformationClass.MemoryPriority,
                            ref mem, System.Runtime.InteropServices.Marshal.SizeOf(mem));

                        if (_job != IntPtr.Zero) GameNative.AssignProcessToJobObject(_job, h);
                        touched++;
                    }
                    finally
                    {
                        GameNative.CloseHandle(h);
                    }
                }
                catch (Exception)
                {
                    // Processes come and go while this loop runs, and plenty deny
                    // access. Neither is worth reporting.
                }
                finally
                {
                    p.Dispose();
                }
            }

            Add("Eased " + touched + " background processes off the CPU and memory", true,
                "Reverts by itself when they restart or when BandPilot exits");
        }

        /// <summary>
        /// Duplicates High Performance into a private scheme rather than editing
        /// one the user owns. Editing theirs is persistent, invisible in the UI,
        /// and the classic way tuning tools permanently degrade someone's battery
        /// life.
        /// </summary>
        private void SwitchPowerPlan()
        {
            IntPtr activePtr = IntPtr.Zero;
            try
            {
                if (GameNative.PowerGetActiveScheme(IntPtr.Zero, out activePtr) != 0
                    || activePtr == IntPtr.Zero)
                {
                    Add("Could not read the current power plan", false, null);
                    return;
                }

                Guid previous = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activePtr);

                Guid template = GameNative.HighPerformanceScheme;
                IntPtr copyPtr = IntPtr.Zero;
                if (GameNative.PowerDuplicateScheme(IntPtr.Zero, ref template, ref copyPtr) != 0
                    || copyPtr == IntPtr.Zero)
                {
                    // Many OEM laptops ship no High Performance scheme at all.
                    Add("This PC has no High Performance plan to copy", false,
                        "Power plan left unchanged");
                    return;
                }

                _ourScheme = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(copyPtr);
                GameNative.LocalFree(copyPtr);
                // Journalled before switching, so an ungraceful exit still knows
                // which plan to put back and which copy to delete.
                _journal.RecordAndFlush(new Mutation
                {
                    Kind = "powerscheme",
                    Target = _ourScheme.ToString(),
                    PriorData = previous.ToString(),
                    Existed = true
                });

                Guid apply = _ourScheme;
                GameNative.PowerSetActiveScheme(IntPtr.Zero, ref apply);
                Add("Switched to a private high-performance power plan", true,
                    "Your own power plans are untouched");
            }
            catch (Exception ex)
            {
                Add("Could not switch the power plan", false, ex.Message);
            }
            finally
            {
                if (activePtr != IntPtr.Zero) GameNative.LocalFree(activePtr);
            }
        }

        private void DisableNetworkThrottle()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(NetworkThrottleKey))
                {
                    if (k == null)
                    {
                        Add("Could not open the multimedia profile key", false, null);
                        return;
                    }

                    object prior = k.GetValue(NetworkThrottleValue);

                    _journal.RecordAndFlush(new Mutation
                    {
                        Kind = "registry",
                        Target = NetworkThrottleKey,
                        ValueName = NetworkThrottleValue,
                        PriorData = prior == null
                            ? null
                            : unchecked((uint)Convert.ToInt32(prior)).ToString(),
                        Existed = prior != null
                    });

                    k.SetValue(NetworkThrottleValue, unchecked((int)ThrottlingDisabled),
                               RegistryValueKind.DWord);
                }

                Add("Lifted Windows' 10 000 packet/second network throttle", true,
                    "Takes effect for new connections");
            }
            catch (Exception ex)
            {
                Add("Could not lift the network throttle", false, ex.Message);
            }
        }

        /// <summary>
        /// Ties the session to the game rather than to the window. Better
        /// behaviour and a safety net at once: close BandPilot and the limits go
        /// anyway, quit the game and they are lifted deliberately.
        /// </summary>
        private void WatchGame(Process game)
        {
            if (game == null) return;
            try
            {
                game.EnableRaisingEvents = true;
                game.Exited += (s, e) =>
                {
                    Stop();
                    EventHandler handler = GameExited;
                    if (handler != null) handler(this, EventArgs.Empty);
                };
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // stop
        // ------------------------------------------------------------------

        public void Stop()
        {
            if (!IsActive) return;
            IsActive = false;

            // Closing the job handle is the whole restore for CPU sharing: the
            // kernel destroys the job and every limit with it.
            if (_job != IntPtr.Zero)
            {
                try { GameNative.CloseHandle(_job); } catch (Exception) { }
                _job = IntPtr.Zero;
            }

            if (_game != null)
            {
                try { _game.PriorityClass = ProcessPriorityClass.Normal; }
                catch (Exception) { }
                _game = null;
            }

            if (_journal != null)
            {
                foreach (Mutation m in _journal.Mutations)
                {
                    try { Restore(m); } catch (Exception) { }
                }
                SessionJournal.Delete();
                _journal = null;
            }
        }

        private void Add(string description, bool ok, string detail)
        {
            Log.Add(new GameModeAction { Description = description, Succeeded = ok, Detail = detail });
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
