using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Gideon.UIFramework.Caching;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Notifications
{
    /// <summary>
    /// What this mod remembers about an alert that vanilla does not: how long it has gone unaddressed, whether it is
    /// snoozed, and whether the player has hidden it for good.
    ///
    /// <b>Keyed by the alert's type, not its label.</b> A label changes as the situation does -- "3 colonists need
    /// treatment" becomes "1 colonist needs treatment" -- so keying by it would silently lose the snooze the moment
    /// the thing being snoozed got slightly better. The type name is stable across sessions and across mod updates,
    /// and is what "this kind of warning" actually means.
    ///
    /// <b>Time is measured in running seconds, through the cache controller's clock.</b> An alert you are ignoring
    /// while the game is paused is not being ignored -- you are reading it. Reusing that clock means the urgency bar
    /// stops draining while paused for the same reason the caches stop rebuilding, and both stay consistent with
    /// each other without a second definition of "how long has this been going on".
    ///
    /// <b>Only the hidden set is written to disk.</b> Hiding is a decision about what the player never wants to see,
    /// so it has to outlive the session. Urgency and snoozes are about the sitting you are in: a snooze that
    /// survived a restart would silence a warning the player has forgotten they silenced, and an urgency bar
    /// restored from last week would present a fresh problem as one that had been ignored for a season -- which is
    /// exactly backwards from what the bar is for.
    /// </summary>
    internal static class AlertState
    {
        /// <summary>
        /// How long an alert takes to drain from fresh to fully stale, in running seconds.
        ///
        /// Two minutes of unpaused play. Long enough that a warning you are actively dealing with still looks fresh,
        /// short enough that something you have walked away from reads as old by the time you come back to it.
        /// </summary>
        internal const float DrainSeconds = 120f;

        /// <summary>How long a snooze lasts, in running seconds. Long enough to finish what you were doing.</summary>
        internal const float SnoozeSeconds = 300f;

        public const string FileName = "UIOverhaul_HiddenAlerts.xml";

        public static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        /// <summary>When each alert type was first seen active, in running seconds.</summary>
        private static readonly Dictionary<string, float> FirstSeen = new Dictionary<string, float>();

        /// <summary>Running-second stamps past which each snoozed alert speaks again.</summary>
        private static readonly Dictionary<string, float> SnoozedUntil = new Dictionary<string, float>();

        private static HashSet<string> hidden;

        /// <summary>
        /// The identity of a kind of alert.
        ///
        /// <c>FullName</c> rather than <c>Name</c>, so two mods that each define a <c>ThreatAlert</c> do not share a
        /// snooze. Null only if reflection has gone very wrong, in which case the alert is treated as unkeyable and
        /// simply never gets any of this behavior.
        /// </summary>
        internal static string KeyOf(Alert alert)
        {
            return alert?.GetType().FullName;
        }

        private static float Now => UICacheController.UnpausedSeconds;

        // ---------------------------------------------------------------------------------------
        // Urgency
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// How fresh this alert is, from 1 the moment it appears to 0 once it has been ignored for
        /// <see cref="DrainSeconds"/>.
        ///
        /// The value the drawing uses for both the length and the brightness of the left edge, so a fresh problem
        /// and one you have been sitting on look nothing like each other.
        /// </summary>
        internal static float Freshness(Alert alert)
        {
            string key = KeyOf(alert);

            if (key == null)
                return 1f;

            if (!FirstSeen.TryGetValue(key, out float started))
            {
                // First sighting. Stamped on read rather than from a hook, because vanilla's Notify_Started is not
                // something this can subscribe to -- and an alert that is active is one that is being shown, so the
                // first draw is the first moment it mattered.
                FirstSeen[key] = Now;
                return 1f;
            }

            return 1f - Mathf.Clamp01((Now - started) / DrainSeconds);
        }

        /// <summary>
        /// Forgets the timing for alerts that are no longer active, so one that comes back reads as new.
        ///
        /// Called with the currently active set rather than per alert, because "no longer active" is only knowable
        /// by comparing against the whole list -- an alert that has gone quiet is absent, not marked.
        /// </summary>
        internal static void Prune(List<Alert> active)
        {
            if (FirstSeen.Count == 0 || active == null)
                return;

            HashSet<string> live = new HashSet<string>();

            foreach (Alert alert in active)
            {
                string key = KeyOf(alert);

                if (key != null)
                    live.Add(key);
            }

            List<string> gone = null;

            foreach (KeyValuePair<string, float> entry in FirstSeen)
            {
                if (live.Contains(entry.Key))
                    continue;

                if (gone == null)
                    gone = new List<string>();

                gone.Add(entry.Key);
            }

            if (gone == null)
                return;

            foreach (string key in gone)
            {
                FirstSeen.Remove(key);

                // The snooze goes with it. An alert that resolved itself and later returns is a new problem, and
                // silencing it because its predecessor was snoozed would hide something the player never dismissed.
                SnoozedUntil.Remove(key);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Snoozing
        // ---------------------------------------------------------------------------------------

        internal static void Snooze(Alert alert)
        {
            string key = KeyOf(alert);

            if (key != null)
                SnoozedUntil[key] = Now + SnoozeSeconds;
        }

        internal static bool IsSnoozed(Alert alert)
        {
            string key = KeyOf(alert);

            if (key == null || !SnoozedUntil.TryGetValue(key, out float until))
                return false;

            if (Now < until)
                return true;

            SnoozedUntil.Remove(key);
            return false;
        }

        /// <summary>
        /// Ends a snooze early, because the player asked for it back.
        ///
        /// The counterpart to <see cref="Snooze"/> rather than a convenience: a silence with no way to undo it is
        /// how somebody ends up wondering why a warning they need is not appearing.
        /// </summary>
        internal static void Wake(Alert alert)
        {
            string key = KeyOf(alert);

            if (key != null)
                SnoozedUntil.Remove(key);
        }

        /// <summary>How much of the snooze is left, 0 to 1, for showing that something is snoozed rather than gone.</summary>
        internal static float SnoozeRemaining(Alert alert)
        {
            string key = KeyOf(alert);

            if (key == null || !SnoozedUntil.TryGetValue(key, out float until))
                return 0f;

            return Mathf.Clamp01((until - Now) / SnoozeSeconds);
        }

        // ---------------------------------------------------------------------------------------
        // Hiding
        // ---------------------------------------------------------------------------------------

        private static HashSet<string> Hidden => hidden ?? (hidden = Load());

        internal static bool IsHidden(Alert alert)
        {
            string key = KeyOf(alert);

            return key != null && Hidden.Contains(key);
        }

        internal static void Hide(Alert alert)
        {
            string key = KeyOf(alert);

            if (key == null || !Hidden.Add(key))
                return;

            Save();
        }

        internal static void Unhide(string key)
        {
            if (key == null || !Hidden.Remove(key))
                return;

            Save();
        }

        /// <summary>
        /// Everything currently hidden, for the settings screen's restore list.
        ///
        /// A copy, so the caller can iterate it while offering a button that unhides one -- which would otherwise
        /// modify the set being walked.
        /// </summary>
        internal static List<string> HiddenKeys() => new List<string>(Hidden);

        /// <summary>Drops the in-memory copy so the next read takes the file again, for the config watcher.</summary>
        internal static void Reload()
        {
            hidden = null;
        }

        // ---------------------------------------------------------------------------------------
        // Persistence
        //
        // Hand-written XML in the config folder, the same as the other files this mod keeps there: it has to be
        // readable with no game loaded and editable by a player who has hidden something they cannot find again.
        // ---------------------------------------------------------------------------------------

        private static HashSet<string> Load()
        {
            HashSet<string> result = new HashSet<string>();
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                    return result;

                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                XmlElement root = doc.DocumentElement;

                if (root == null)
                    return result;

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (node is XmlElement element && element.Name == "alert")
                    {
                        string key = element.InnerText?.Trim();

                        if (!key.NullOrEmpty())
                            result.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                // Reported rather than thrown: a broken file costs the player their hidden list, which means some
                // alerts they had silenced come back. That is recoverable and visible. Failing the read outright
                // would take the alerts readout down with it, which is neither.
                Features.Options.UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so previously hidden alerts are showing again: " + ex.Message
                });
            }

            return result;
        }

        private static void Save()
        {
            string path = FilePath;

            try
            {
                Features.Options.UIConfigWatcher.NotifySelfWrite();

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new UTF8Encoding(false)
                };

                using (XmlWriter writer = XmlWriter.Create(path, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteComment(
                        " Alerts hidden by shift-clicking them, for Gideon's UI Overhaul. Each entry is the full"
                        + " type name of an alert class. Delete a line to make that alert appear again, or clear"
                        + " them all from the mod's settings. ");

                    writer.WriteStartElement("HiddenAlerts");

                    foreach (string key in Hidden)
                        writer.WriteElementString("alert", key);

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(UILogTag.Prefix + "Could not save hidden alerts to " + path + ".\n" + ex);
            }
        }
    }
}
