using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BandPilot.Game
{
    public enum RestoreAction
    {
        /// <summary>Delete the value: Windows had none before we wrote one.</summary>
        Delete,

        /// <summary>Put the previous value back.</summary>
        Write
    }

    /// <summary>One reversible change, recorded before it is made.</summary>
    public sealed class Mutation
    {
        public string Kind { get; set; }          // "registry" or "powerscheme"
        public string Target { get; set; }        // key path, or the scheme GUID
        public string ValueName { get; set; }
        public string PriorData { get; set; }

        /// <summary>
        /// False means there was nothing here before, so restoring means
        /// DELETING the value rather than writing a zero. Writing an explicit
        /// value where Windows had none changes the effective default, and is
        /// the single most common way tools in this category leave a machine
        /// subtly altered forever.
        /// </summary>
        public bool Existed { get; set; }

        /// <summary>
        /// What undoing this actually means. Deliberately a named decision
        /// rather than an inline ternary, because getting it backwards is the
        /// single most common restore bug in tools like this: writing a zero
        /// where there was no value at all changes the effective default and
        /// silently leaves the machine altered forever.
        /// </summary>
        public RestoreAction Action
        {
            get { return Existed ? RestoreAction.Write : RestoreAction.Delete; }
        }
    }

    /// <summary>
    /// A write-ahead journal for the small residue of Game Mode state that
    /// survives a reboot and therefore cannot be left to the kernel to undo.
    ///
    /// Almost all of Game Mode is built from job objects and per-process
    /// throttling, which evaporate on their own when this process dies. Only two
    /// things outlive it — a machine-wide registry value and the active power
    /// scheme — and those are what this file exists for.
    ///
    /// The rule that makes it work: every mutation is written and flushed to
    /// disk BEFORE it is applied, never after. A journal that lists a change
    /// that was never made is harmless, because restoring it is a no-op. A
    /// change made without a journal entry is unrecoverable.
    ///
    /// The file existing at startup is itself the signal that the last session
    /// ended badly. It is replayed and only then deleted.
    /// </summary>
    public sealed class SessionJournal
    {
        public string SessionId { get; set; }
        public int Pid { get; set; }
        public string CreatedUtc { get; set; }

        /// <summary>
        /// A session found older than this is restored unconditionally, which
        /// covers the case where the machine lost power mid-session.
        /// </summary>
        public string HardExpiryUtc { get; set; }

        public List<Mutation> Mutations { get; set; }

        public SessionJournal()
        {
            Mutations = new List<Mutation>();
        }

        private static readonly JsonSerializerOptions Options =
            new JsonSerializerOptions { WriteIndented = true };

        public static string Path
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return System.IO.Path.Combine(root, "BandPilot", "gamemode-session.json");
            }
        }

        public static SessionJournal Begin(TimeSpan maxSession)
        {
            return new SessionJournal
            {
                SessionId = Guid.NewGuid().ToString("N"),
                Pid = Environment.ProcessId,
                CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                HardExpiryUtc = DateTime.UtcNow.Add(maxSession).ToString("o", CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Records a change and forces it to disk. Callers must await nothing
        /// and apply the change only after this returns.
        /// </summary>
        public void RecordAndFlush(Mutation mutation)
        {
            Mutations.Add(mutation);
            Save();
        }

        public void Save()
        {
            string path = Path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

            string json = JsonSerializer.Serialize(this, Options);

            // FlushFileBuffers via FileStream.Flush(true): without it the journal
            // can sit in the OS cache while the mutation it describes has already
            // hit the registry, which is precisely the window this guards.
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
        }

        public static SessionJournal Load()
        {
            try
            {
                string path = Path;
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<SessionJournal>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                // A corrupt journal is worse than none only if it stops the app
                // starting. Treat it as absent.
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                string path = Path;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { }
        }

        public bool IsExpired
        {
            get
            {
                DateTime expiry;
                if (!DateTime.TryParse(HardExpiryUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out expiry))
                {
                    return true;   // unreadable expiry means restore it
                }
                return DateTime.UtcNow > expiry;
            }
        }
    }
}
