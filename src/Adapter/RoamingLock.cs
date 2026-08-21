using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BandPilot.Adapter
{
    /// <summary>
    /// Finds and holds down whatever setting makes a driver wander off a chosen
    /// access point.
    ///
    /// Pinning a BSSID only lasts until the adapter decides to roam, so the pin
    /// and this belong together — but every vendor names the control differently
    /// and offers a different value set. Intel calls it "Roaming Aggressiveness"
    /// with five numbered levels; others use "Roaming Sensitivity Level",
    /// "Roam Tendency" or a plain enable/disable. Hard-coding any of those means
    /// working on one card and doing nothing on the rest, so everything here is
    /// discovered from what the driver reports.
    /// </summary>
    public static class RoamingLock
    {
        /// <summary>A driver setting that controls roaming, and the value that calms it.</summary>
        public sealed class Candidate
        {
            public AdvancedProperty Property { get; set; }
            public string TargetValue { get; set; }
            public bool AlreadySet { get; set; }

            public string Describe()
            {
                return Property.DisplayName + " → " + TargetValue;
            }
        }

        private static readonly string[] NameHints =
        {
            "roaming aggressiveness", "roaming sensitivity", "roam tendency",
            "roaming tendency", "roaming decision", "roam", "roaming"
        };

        /// <summary>
        /// Settings whose names mention roaming but which control something else.
        /// Intel's band preference is keyed "RoamingPreferredBandType", and
        /// forcing it would directly contradict a user who has just pinned a
        /// radio on another band — the opposite of holding their choice.
        /// </summary>
        private static readonly string[] NameExclusions =
        {
            "band", "channel", "power", "throughput", "transmit"
        };

        /// <summary>
        /// Ranked best-first. More than one can match — Intel exposes both
        /// aggressiveness and a band preference — so the caller decides how many
        /// to apply.
        /// </summary>
        public static List<Candidate> Find(IEnumerable<AdvancedProperty> properties)
        {
            var found = new List<Candidate>();
            if (properties == null) return found;

            foreach (AdvancedProperty p in properties)
            {
                string haystack = ((p.DisplayName ?? "") + " " + (p.RegistryKeyword ?? ""))
                    .ToLowerInvariant();

                bool excluded = false;
                foreach (string bad in NameExclusions)
                {
                    if (haystack.Contains(bad)) { excluded = true; break; }
                }
                if (excluded) continue;

                int rank = -1;
                for (int i = 0; i < NameHints.Length; i++)
                {
                    if (haystack.Contains(NameHints[i])) { rank = i; break; }
                }
                if (rank < 0) continue;

                string target = CalmestValue(p);
                if (target == null) continue;

                found.Add(new Candidate
                {
                    Property = p,
                    TargetValue = target,
                    AlreadySet = string.Equals(p.DisplayValue, target, StringComparison.OrdinalIgnoreCase)
                });
            }

            return found;
        }

        /// <summary>
        /// Picks the value that roams least. Word matches come first because
        /// they are unambiguous; only then does it fall back to the lowest
        /// leading number, which is the convention Intel and several others use
        /// ("1. Lowest" through "5. Highest").
        /// </summary>
        public static string CalmestValue(AdvancedProperty p)
        {
            if (p == null || p.ValidValues == null || p.ValidValues.Count == 0) return null;

            // Ordered most-specific first, and matched on word boundaries.
            // A substring test would pick "Allow roaming" for the hint "low",
            // which is the exact opposite of what is wanted.
            string[] calmWords =
            {
                "disabled", "lowest", "none", "off", "no roaming", "sticky", "low", "least"
            };

            foreach (string word in calmWords)
            {
                foreach (string v in p.ValidValues)
                {
                    if (v == null) continue;
                    if (Regex.IsMatch(v, @"\b" + Regex.Escape(word) + @"\b", RegexOptions.IgnoreCase))
                    {
                        return v;
                    }
                }
            }

            string best = null;
            int bestNumber = int.MaxValue;
            foreach (string v in p.ValidValues)
            {
                int n;
                if (TryLeadingNumber(v, out n) && n < bestNumber)
                {
                    bestNumber = n;
                    best = v;
                }
            }
            if (best != null) return best;

            // No word match and nothing numeric: refuse rather than guess. Picking
            // the first entry could just as easily select the most aggressive
            // setting and make the problem worse.
            return null;
        }

        private static bool TryLeadingNumber(string value, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(value)) return false;

            Match m = Regex.Match(value.Trim(), @"^(\d+)");
            return m.Success
                && int.TryParse(m.Groups[1].Value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out number);
        }
    }
}
