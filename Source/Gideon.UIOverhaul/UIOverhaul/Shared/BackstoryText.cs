using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// A backstory's full description, built at most once every few seconds instead of once every frame.
    ///
    /// <b>What this is fixing, reported by Aaron on 2026-08-24 against the character editor's Backstory
    /// panel.</b> Two panels in this mod print a backstory's full text -- the editor's Backstory panel and the
    /// inspect pane's Bio body -- and both called <c>BackstoryDef.FullDescriptionFor(pawn).Resolve()</c> in the
    /// draw, which means twice a frame, every frame, for as long as the panel is open.
    ///
    /// <b>That method is not a getter.</b> It builds a <c>StringBuilder</c> and then, per call: runs the
    /// description through <c>Formatted</c>, <c>AdjustedFor</c> and <c>Resolve</c>, which is the grammar
    /// resolver -- the same machinery that generates flavour text; walks <em>every</em> <c>SkillDef</c> and, for
    /// each, every one of the backstory's skill gains; enumerates <c>DisabledWorkTypes</c> and
    /// <c>DisabledWorkGivers</c> through LINQ; and with Royalty installed walks <em>every</em>
    /// <c>MeditationFocusDef</c> and each one's required-backstory list. Our caller then resolves the
    /// <c>TaggedString</c> a second time. At sixty frames a second that is the grammar resolver running a
    /// hundred and twenty times to produce a paragraph nobody asked to change.
    ///
    /// <b>The same shape of fix as the architect's designator list,</b> which tanked for the same reason: real
    /// work per frame for an answer that only changes when the player changes it. Compare what the player can
    /// change, and keep a real-time backstop for anything not thought of.
    ///
    /// <b>Compared, not just timed.</b> The name and the gender are in the text -- the description reads
    /// "Aaron grew up on a glitterworld" and picks its pronouns off the gender -- and both are editable in the
    /// character editor, so both have to be part of the key or a rename would show the old sentence for as long
    /// as the timer runs. The backstory itself is the third, and the pawn is the fourth because the inspect pane
    /// and the editor can be looking at two different people at once.
    ///
    /// <b>Real time rather than ticks or frames,</b> for the reason the architect's cache records: RimWorld
    /// implements game speed as more ticks per frame, so a tick-based interval would expire faster on
    /// superspeed, which is precisely backwards -- and this panel is read while paused more than at any other
    /// time, where a tick clock never expires at all.
    /// </summary>
    internal static class BackstoryText
    {
        /// <summary>
        /// How long a built description is trusted.
        ///
        /// Everything known to change the text is compared instead, so this only governs how late something
        /// unaccounted for -- a pawn ageing out of childhood, a translation swapped at runtime -- would appear.
        /// Five seconds is imperceptible for a paragraph and still turns three hundred grammar resolutions into
        /// one.
        /// </summary>
        private const float TrustSeconds = 5f;

        /// <summary>
        /// How many descriptions are kept.
        ///
        /// Four is what is on screen at once in the worst case: the inspect pane and the editor each showing a
        /// childhood and an adulthood, for two different pawns. Eight is that with room to spare, and the search
        /// below is reference compares over a list that short.
        /// </summary>
        private const int Kept = 8;

        private sealed class Entry
        {
            internal Pawn Pawn;

            internal BackstoryDef Def;

            internal Gender Gender;

            internal string Name;

            internal float At;

            internal string Text;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>
        /// The description to print, or null when there is none.
        ///
        /// Guarded here rather than at each call site, so the two panels do not each carry their own guard for
        /// the same failure and cannot disagree about what to do when it happens.
        /// </summary>
        internal static string For(BackstoryDef def, Pawn pawn)
        {
            if (def == null || pawn == null)
                return null;

            return UIGuard.Try<string>("Shared.BackstoryText", () =>
            {
                Gender gender = pawn.gender;
                string name = pawn.LabelShort;
                float now = Time.realtimeSinceStartup;

                for (int i = 0; i < Entries.Count; i++)
                {
                    Entry held = Entries[i];

                    if (held.Pawn != pawn || held.Def != def || held.Gender != gender)
                        continue;

                    if (!string.Equals(held.Name, name, System.StringComparison.Ordinal))
                        continue;

                    if (now - held.At >= TrustSeconds)
                        break;

                    // Moved to the front so the cheapest thing to find is the thing drawn most recently, which
                    // is also what makes the eviction below take the least useful entry.
                    if (i > 0)
                    {
                        Entries.RemoveAt(i);
                        Entries.Insert(0, held);
                    }

                    return held.Text;
                }

                string text = def.FullDescriptionFor(pawn).Resolve();

                Remember(new Entry
                {
                    Pawn = pawn,
                    Def = def,
                    Gender = gender,
                    Name = name,
                    At = now,
                    Text = text
                });

                return text;
            }, null, null);
        }

        private static void Remember(Entry entry)
        {
            // Any entry for the same pawn and slot is now wrong by definition, and leaving it would let the list
            // fill with stale copies of one pawn and evict the other panel's.
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].Pawn == entry.Pawn && Entries[i].Def == entry.Def)
                    Entries.RemoveAt(i);
            }

            Entries.Insert(0, entry);

            while (Entries.Count > Kept)
                Entries.RemoveAt(Entries.Count - 1);
        }
    }
}
