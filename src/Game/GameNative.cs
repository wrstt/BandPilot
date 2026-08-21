using System;
using System.Runtime.InteropServices;

namespace BandPilot.Game
{
    /// <summary>
    /// The Win32 surface Game Mode needs.
    ///
    /// Almost everything here was chosen because the kernel undoes it for us.
    /// A job object dies when its last handle closes, taking every limit with
    /// it — on a clean exit, on a crash, on TerminateProcess, on power loss.
    /// That is a stronger guarantee than any amount of restore code, and it is
    /// why this is built out of job objects and per-process throttling states
    /// rather than out of settings that outlive the process.
    /// </summary>
    internal static class GameNative
    {
        // ---- process access -------------------------------------------------

        [Flags]
        internal enum ProcessAccess : uint
        {
            SetInformation = 0x0200,
            QueryInformation = 0x0400,
            QueryLimitedInformation = 0x1000,
            SetQuota = 0x0100,
            Terminate = 0x0001
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(
            ProcessAccess desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        // ---- job objects ----------------------------------------------------

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            IntPtr job,
            JobObjectInfoClass infoClass,
            ref JobObjectCpuRateControlInformation info,
            int length);

        internal enum JobObjectInfoClass
        {
            CpuRateControlInformation = 15
        }

        /// <summary>
        /// Note what is deliberately absent: JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
        /// That flag inverts the whole safety property of a job object — instead
        /// of limits evaporating when BandPilot dies, every process in the job
        /// would be killed with it. It is not declared here so it cannot be set
        /// by accident, and a test asserts the flags stay weight-based.
        /// </summary>
        [Flags]
        internal enum CpuRateControlFlags : uint
        {
            Enable = 0x1,
            WeightBased = 0x2
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobObjectCpuRateControlInformation
        {
            internal CpuRateControlFlags ControlFlags;

            /// <summary>
            /// Union in the C header. Used here as Weight (1-9, 5 is neutral),
            /// never as a hard CpuRate cap: weight-based sharing degrades
            /// gracefully, whereas a hard cap can stall a process holding a lock
            /// the game is waiting on.
            /// </summary>
            internal uint WeightOrRate;
        }

        // ---- per-process throttling ----------------------------------------

        internal enum ProcessInformationClass
        {
            MemoryPriority = 0,
            PowerThrottling = 4
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessInformation(
            IntPtr process,
            ProcessInformationClass infoClass,
            ref ProcessPowerThrottlingState info,
            int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessInformation(
            IntPtr process,
            ProcessInformationClass infoClass,
            ref MemoryPriorityInformation info,
            int size);

        internal const uint ProcessPowerThrottlingCurrentVersion = 1;
        internal const uint ProcessPowerThrottlingExecutionSpeed = 0x1;

        /// <summary>
        /// EcoQoS. Preferred over IDLE_PRIORITY_CLASS because it is both more
        /// effective and safer: it parks a process on the efficiency cores and
        /// lets the scheduler throttle it, rather than starving it outright.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessPowerThrottlingState
        {
            internal uint Version;
            internal uint ControlMask;
            internal uint StateMask;
        }

        internal const uint MemoryPriorityLow = 2;
        internal const uint MemoryPriorityNormal = 5;

        /// <summary>
        /// Makes the memory manager trim the background process's pages under
        /// pressure instead of the game's — which is the part of "free up RAM"
        /// that actually helps, without touching anything the game owns.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryPriorityInformation
        {
            internal uint MemoryPriority;
        }

        // ---- power schemes --------------------------------------------------

        [DllImport("powrprof.dll")]
        internal static extern uint PowerGetActiveScheme(IntPtr userRoot, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        internal static extern uint PowerSetActiveScheme(IntPtr userRoot, ref Guid schemeGuid);

        [DllImport("powrprof.dll")]
        internal static extern uint PowerDuplicateScheme(
            IntPtr userRoot, ref Guid sourceScheme, ref IntPtr destinationScheme);

        [DllImport("powrprof.dll")]
        internal static extern uint PowerDeleteScheme(IntPtr userRoot, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>The built-in High Performance scheme, used only as a template.</summary>
        internal static readonly Guid HighPerformanceScheme =
            new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    }
}
