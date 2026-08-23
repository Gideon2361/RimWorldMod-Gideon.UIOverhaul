using System;
using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// What the editor has done to this pawn, and how to put each of it back.
    ///
    /// <b>An undo log rather than a snapshot, which is a departure from the proposal and a better answer.</b> The
    /// mockup said Revert all restores from a copy taken when the window opened. Taking a real copy of a pawn
    /// means serialising it -- there is no other way to capture a hediff set, a worn apparel list, a memory list
    /// and a relations graph -- and reloading one into a live map is how a save gets corrupted. Recording the
    /// inverse of each edit as it happens is exact instead of approximate, cannot drift from the fields the panels
    /// actually touch, and the footer's list of what has changed falls straight out of it.
    ///
    /// <b>An operation with no inverse records none and says so.</b> Resurrection is the case: a dead pawn who is
    /// now alive is a different object in a different world, and no closure puts that back. It registers as an
    /// unreversible entry, which is what makes the footer's warning true rather than decorative.
    ///
    /// <b>Undone in reverse,</b> since two edits to the same field leave two inverses that only compose one way
    /// round.
    ///
    /// <b>Consecutive edits to one field are one entry.</b> Typing a nickname is one keystroke per frame and
    /// dragging a need slider is one value per frame; logged straight they would read as "14 changes: name" and
    /// revert in fourteen steps. When the newest entry carries the same label the new inverse is thrown away and
    /// the older one kept, since that is the one reaching the original value. An edit to a different field in
    /// between breaks the run, so hair, then skin, then hair again is three entries and still reverts correctly.
    /// </summary>
    internal sealed class EditorChanges
    {
        private struct Entry
        {
            /// <summary>What the player sees in the footer. Lower case, since it is read in a list.</summary>
            internal string Label;

            /// <summary>Puts this one edit back, or null when nothing can.</summary>
            internal Action Undo;
        }

        private readonly List<Entry> entries = new List<Entry>();

        /// <summary>How many edits have been made since the window opened.</summary>
        internal int Count
        {
            get { return entries.Count; }
        }

        /// <summary>Whether anything done here can no longer be taken back.</summary>
        internal bool AnyPermanent
        {
            get
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Undo == null)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Records an edit and how to reverse it.
        ///
        /// The caller has already made the change. That order matters: a panel that recorded first and then failed
        /// would offer to undo something that never happened.
        /// </summary>
        internal void Record(string label, Action undo)
        {
            entries.Add(new Entry { Label = label, Undo = undo });
        }

        /// <summary>Records an edit that cannot be reversed.</summary>
        internal void RecordPermanent(string label)
        {
            entries.Add(new Entry { Label = label, Undo = null });
        }

        /// <summary>
        /// A shorthand for the commonest shape: read the old value, write the new one, record the inverse.
        ///
        /// Here rather than in each panel because there are upwards of thirty fields in this window and every one
        /// of them would otherwise be four lines that have to agree with each other.
        /// </summary>
        internal void Set<T>(string label, Func<T> read, Action<T> write, T value)
        {
            T before = read();

            if (Equals(before, value))
                return;

            write(value);

            // Coalesced against the newest entry only. Keeping the older inverse is the whole trick: it is the
            // one that reaches the value the field had when the run of edits started.
            if (entries.Count > 0 && entries[entries.Count - 1].Label == label
                                 && entries[entries.Count - 1].Undo != null)
                return;

            Record(label, () => write(before));
        }

        /// <summary>
        /// Puts everything back, newest first.
        ///
        /// Guarded per entry rather than around the whole loop: one field whose inverse fails must not strand the
        /// other twenty. What is left after a failure is exactly what the log says it is, since a failed entry
        /// stays recorded.
        /// </summary>
        internal void RevertAll()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];

                if (entry.Undo == null)
                    continue;

                bool reverted = UIGuard.Try("Editor.Revert", entry.Undo,
                    "One of the changes could not be put back. The rest were.");

                if (reverted)
                    entries.RemoveAt(i);
            }
        }

        /// <summary>
        /// The footer's line: how many changes, and which ones.
        ///
        /// <b>Named rather than counted alone,</b> because the question somebody reads a footer to answer is "did
        /// I touch something I did not mean to". Duplicates collapse, so nudging a slider twenty times is one
        /// entry rather than twenty.
        /// </summary>
        internal string Summary()
        {
            if (entries.Count == 0)
                return "No changes.";

            List<string> named = new List<string>();

            for (int i = 0; i < entries.Count; i++)
            {
                string label = entries[i].Label;

                if (!label.NullOrEmpty() && !named.Contains(label))
                    named.Add(label);
            }

            StringBuilder text = new StringBuilder();

            text.Append(entries.Count);
            text.Append(entries.Count == 1 ? " change" : " changes");

            if (named.Count == 0)
                return text.Append('.').ToString();

            text.Append(": ");

            // Four names is what the footer can hold before it starts ellipsing, and an ellipsed list of what
            // you changed is worse than a count of the rest.
            int shown = Math.Min(4, named.Count);

            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                    text.Append(", ");

                text.Append(named[i]);
            }

            if (named.Count > shown)
                text.Append(" and ").Append(named.Count - shown).Append(" more");

            return text.Append('.').ToString();
        }
    }
}
