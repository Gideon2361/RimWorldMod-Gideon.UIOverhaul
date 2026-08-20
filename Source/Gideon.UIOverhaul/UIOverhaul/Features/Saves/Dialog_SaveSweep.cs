using System;
using System.Collections.Generic;
using System.IO;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Examining a save and writing a repaired copy of it.
    ///
    /// <b>The original is never written to, and the window says so before anything else.</b> That banner is the
    /// first thing in the layout because it is the one fact that decides whether a player is willing to press the
    /// button at all.
    ///
    /// <b>Broken references default on, everything reclaimable defaults off.</b> The first group is data that
    /// cannot load: RimWorld logs an error for each and discards it, so removing it changes nothing about the
    /// colony. The second group trades something real for bytes, so it is never on by choice of this mod. The
    /// footer reading almost nothing on open is the honest consequence of that, not an oversight.
    ///
    /// <b>Bytes are the least of it.</b> Aaron's framing, and the reason each row says what the game keeps paying
    /// for rather than only what it costs on disk: "improving performance for really long-running games by removing
    /// garbage and errors that eat up memory and CPU through game mechanisms."
    ///
    /// <b>Scanning happens inside a long event.</b> A 47 MB save takes seconds to walk, and doing that on the UI
    /// thread would look exactly like the game hanging, which is the bug this feature is supposed to reduce.
    /// </summary>
    public class Dialog_SaveSweep : Window
    {
        private const float TitleHeight = 30f;
        /// <summary>
        /// Tall enough for a title line plus two lines of explanation.
        ///
        /// The explanations are the point of this window rather than decoration on it, so a row that clips one is
        /// worse than a window that is a little taller. <see cref="InitialSize"/> is sized from this, not the
        /// other way round.
        /// </summary>
        private const float RowHeight = 56f;
        private const float HeaderHeight = 26f;
        private const float FooterHeight = 44f;
        private const float CountColumn = 76f;
        private const float SizeColumn = 92f;

        /// <summary>Appended to the save's name to make the copy's name. Mockup wording, kept verbatim.</summary>
        private const string Suffix = " (swept)";

        private readonly FileInfo file;

        /// <summary>
        /// Told when a copy has been written, so the window underneath can list it.
        ///
        /// <b>A callback rather than the sweep window reaching for the save window.</b> Two different windows open
        /// this one and they refresh themselves differently; handing each one's own refresh in keeps this window
        /// from knowing either of them exists. Null is allowed, for a caller with nothing to update.
        /// </summary>
        private readonly Action written;

        private readonly SaveSweepOptions options = new SaveSweepOptions
        {
            RemoveMissingThings = true,
            RemoveDiscardedRecords = true,
            RenumberDuplicates = true,
            RemoveDeadPawns = false,
            RemoveHistory = false
        };

        private SaveSweepReport report;
        private Dictionary<string, HashSet<string>> missing;

        /// <summary>
        /// A preview taken with every removal switched on.
        ///
        /// <b>Once, on open, rather than per click.</b> Each preview is a full walk of the file, so recomputing one
        /// whenever a switch moves would freeze the game for seconds at a time. The footer instead adds up the rows
        /// that are on, which is why it is worded as an estimate: a record removed for two reasons at once is
        /// attributed to the outer one, so a subset can come out slightly larger than the sum suggests. It reads
        /// low rather than high, and the exact figures are reported after the copy is written.
        /// </summary>
        private SaveSweepOutcome full;

        private SaveSweepOutcome done;
        private string problem;
        private bool examined;

        public Dialog_SaveSweep(FileInfo save, Action onWritten = null)
        {
            file = save;
            written = onWritten;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        /// <summary>
        /// Sized to hold every row above the footer, rather than sized first and trusted to fit.
        ///
        /// The footer sits at the bottom edge whatever else happens, so content that runs past it is drawn
        /// underneath it rather than pushed down. Nine rows at <see cref="RowHeight"/>, two group headers, the
        /// banner and the title come to about 690, and the window is the margin plus that plus the footer.
        /// </summary>
        public override Vector2 InitialSize => new Vector2(860f, 800f);

        public override void PostOpen()
        {
            base.PostOpen();

            LongEventHandler.QueueLongEvent(Examine, "Gideon_SweepExamining", false, null);
        }

        /// <summary>Reads the save, resolves its defs and takes the all-on preview. Never throws into the game.</summary>
        private void Examine()
        {
            UIGuard.Try("Saves.Sweep.Examine", () =>
            {
                report = SaveSweepScan.Scan(file.FullName);

                if (!report.Shaped)
                {
                    problem = "This file could not be read as a save, so nothing is offered.";

                    return;
                }

                missing = SaveSweepDefs.Missing(report);

                full = SaveSweepWriter.Preview(file.FullName, new SaveSweepOptions
                {
                    RemoveMissingThings = true,
                    RemoveDiscardedRecords = true,
                    RenumberDuplicates = true,
                    RepairDangling = true,
                    RemoveDeadPawns = true,
                    RemoveMothballed = true,
                    RemoveUnusedPolicies = true,
                    RemoveHistory = true
                }, report, missing);
            }, "The save could not be examined. Nothing was changed.");

            examined = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Saves.Sweep", inRect, () => Contents(inRect),
                "The sweep window failed to draw. Your save is untouched.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.UpperLeft;

                float y = Title(inRect, palette);

                y = Banner(inRect, y, palette);

                if (!examined)
                {
                    Text.Font = GameFont.Small;
                    GUI.color = palette.TextSecondary;
                    Widgets.Label(new Rect(inRect.x, y + 8f, inRect.width, 24f), "Examining the save...");

                    Footer(inRect, palette);

                    return;
                }

                if (report == null || !report.Shaped)
                {
                    Text.Font = GameFont.Small;
                    GUI.color = palette.Danger;
                    Widgets.Label(new Rect(inRect.x, y + 8f, inRect.width, 48f),
                        problem ?? "This file could not be read as a save.");

                    Footer(inRect, palette);

                    return;
                }

                y = Broken(inRect, y, palette);
                y = Reclaimable(inRect, y, palette);

                Footer(inRect, palette);
            }
            finally
            {
                GUI.color = color;
                Text.Anchor = anchor;
                Text.Font = font;
            }
        }

        private float Title(Rect inRect, UIColorPaletteDef palette)
        {
            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;

            string name = Path.GetFileNameWithoutExtension(file.Name);

            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, TitleHeight), "Sweep \"" + name + "\"");

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            string where = SaveFolders.FolderOf(file);

            Widgets.Label(new Rect(inRect.x, inRect.y + TitleHeight, inRect.width - 30f, 18f),
                SavesChrome.Size(file.Length) + "   " + SavesChrome.Ago(file.LastWriteTime) + "   "
                + (where.NullOrEmpty() ? "Saves" : where));

            return inRect.y + TitleHeight + 24f;
        }

        private float Banner(Rect inRect, float y, UIColorPaletteDef palette)
        {
            Rect rect = new Rect(inRect.x, y + 6f, inRect.width, 40f);

            UIElementPainter.FillRounded(rect, palette.PanelBackground);
            UIElementPainter.FillRounded(new Rect(rect.x, rect.y, 3f, rect.height), palette.Accent);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x + 12f, rect.y + 5f, rect.width - 20f, rect.height - 8f),
                "Nothing here writes to this save. A cleaned copy is written under a new name and the original "
                + "stays exactly as it is. Load the copy and satisfy yourself it is sound before you rely on it.");

            return rect.yMax;
        }

        private float Broken(Rect inRect, float y, UIColorPaletteDef palette)
        {
            y = Header(inRect, y, palette, "Broken references", "on by default, this data cannot load");

            int things = Names("ThingDef");
            int discarded = Discarded();

            y = Row(inRect, y, palette, ref options.RemoveMissingThings,
                "Things whose def is no longer installed",
                "Resolved against the live def database, so the list depends on the mods loaded now",
                things == 0 ? "none" : things + " defs", Bytes(Reason("ThingDef")), false, null);

            y = Row(inRect, y, palette, ref options.RemoveDiscardedRecords,
                "Hediffs, traits, thoughts and genes with no def",
                "Each is a line RimWorld logs an error for and then discards on load anyway",
                discarded == 0 ? "none" : discarded + " defs", DiscardedBytes(), false, "safe");

            y = Row(inRect, y, palette, ref options.RepairDangling,
                "Dangling references to removed pawns and things",
                report.DanglingReferences == 0
                    ? "None here. All " + report.ResolvedReferences + " references resolve"
                    : "Ids pointing at records not in the file, out of " + report.ResolvedReferences
                      + " that resolve. Each is a failed resolve, and some are retried every tick rather than "
                      + "once. Pointed at nothing, not deleted",
                report.DanglingReferences == 0 ? "none" : report.DanglingReferences.ToString(), "", false,
                report.DanglingReferences == 0 ? null : "safe");

            y = Row(inRect, y, palette, ref options.RenumberDuplicates,
                "Records sharing one load id",
                report.Duplicates == 0
                    ? "None here. Every load id in this save is unique"
                    : "Two records claim one id, so the second fails to register and references resolve to the "
                      + "first. Repaired by giving the second a fresh id, never by deleting it",
                report.Duplicates == 0 ? "none" : report.Duplicates.ToString(), "", false, "safe");

            return y;
        }

        private float Reclaimable(Rect inRect, float y, UIColorPaletteDef palette)
        {
            y = Header(inRect, y, palette, "Reclaimable", "off by default, each costs something");

            bool never = false;
            int free = report.RemovableMothballed.Count;

            y = Row(inRect, y, palette, ref options.RemoveMothballed,
                "Mothballed world pawns that nothing refers to",
                free == 0
                    ? "None qualify. " + report.MothballedReferenced + " are named by a relationship, quest or "
                      + "memory somewhere, and " + report.MothballedPlayer + " belong to your faction"
                    : free + " of " + Count("pawnsMothballed") + " are named by nothing at all. The other "
                      + (report.MothballedReferenced + report.MothballedPlayer) + " stay",
                free == 0 ? "none" : free.ToString(), Bytes(Reason("Mothballed world pawns")), free == 0,
                "changes the colony");

            int loose = report.RemovableDeadPawns.Count;

            y = Row(inRect, y, palette, ref options.RemoveDeadPawns,
                "Dead pawn records no corpse still holds",
                loose == 0
                    ? "None qualify. All " + report.DeadPawnsHeld + " are the body inside a corpse on one of your "
                      + "maps, and a corpse whose body has gone cannot be placed on the map at all"
                    : loose + " of " + Count("pawnsDead") + " are kept only for memorials, relations and "
                      + "resurrection. The other " + report.DeadPawnsHeld + " are bodies inside corpses and stay",
                loose == 0 ? "none" : loose.ToString(), Bytes(Reason("Dead pawn records")), loose == 0,
                "changes the colony");

            y = Row(inRect, y, palette, ref options.RemoveHistory,
                "History graphs and the play log",
                "The History tab loses its curves and combat logs lose their detail",
                "", Bytes(Reason("History and logs")), false, null);

            int idle = report.RemovablePolicies.Count;

            y = Row(inRect, y, palette, ref options.RemoveUnusedPolicies,
                "Unused policies and filters",
                idle == 0
                    ? "All " + report.Policies + " food, drug, apparel and reading policies are assigned to somebody"
                    : idle + " of " + report.Policies + " food, drug, apparel and reading policies are assigned to "
                      + "nobody. Their allowed-item lists are the bulk of the size",
                idle == 0 ? "none" : idle + " of " + report.Policies,
                Bytes(Reason("Unused policies and filters")), idle == 0, null);

            y = Row(inRect, y, palette, ref never,
                "Maps and terrain",
                "The largest part of the file, and all of it is the colony. Nothing here is stale",
                "", Section("maps"), true, "not offered");

            return y;
        }

        private float Header(Rect inRect, float y, UIColorPaletteDef palette, string title, string note)
        {
            Rect rect = new Rect(inRect.x, y + 10f, inRect.width, HeaderHeight);

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;
            Text.Anchor = TextAnchor.LowerLeft;

            Widgets.Label(rect, title.ToUpperInvariant());

            Text.Anchor = TextAnchor.LowerRight;
            GUI.color = palette.TextDisabled;

            Widgets.Label(rect, note);

            Text.Anchor = TextAnchor.UpperLeft;

            UIElementPainter.FillRounded(new Rect(rect.x, rect.yMax + 2f, rect.width, 1f), palette.Border);

            return rect.yMax + 4f;
        }

        /// <summary>One switch, its label, what it costs, and the numbers beside it.</summary>
        private float Row(Rect inRect, float y, UIColorPaletteDef palette, ref bool value, string label,
            string detail, string count, string size, bool locked, string tag)
        {
            Rect rect = new Rect(inRect.x, y, inRect.width, RowHeight);
            Rect box = new Rect(rect.x, rect.y + 4f, UICheckboxControl.BoxWidth + 8f, 24f);

            UICheckboxControl.Draw(box, ref value, palette, null, null, UICheckboxSide.Left, locked);

            float textX = box.xMax + 8f;
            float textWidth = rect.width - textX + rect.x - CountColumn - SizeColumn - 8f;

            Text.Font = GameFont.Small;
            GUI.color = locked ? palette.TextDisabled : palette.TextPrimary;

            // Measured while the font is still the one the label is drawn in. Measuring after switching to Tiny
            // for the tag returns a width for text nobody drew, and the tag lands on top of the label.
            float labelWidth = Mathf.Min(Text.CalcSize(label).x, textWidth);

            Widgets.Label(new Rect(textX, rect.y + 2f, textWidth, 22f), label);

            if (!tag.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = tag == "safe" ? palette.Success : palette.Warning;

                float at = textX + labelWidth + 10f;
                float room = rect.xMax - CountColumn - SizeColumn - at - 8f;

                // Dropped rather than squeezed into the numbers. A tag is a note, and the count beside it is not.
                if (room >= 40f)
                    Widgets.Label(new Rect(at, rect.y + 4f, room, 18f), tag);
            }

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(textX, rect.y + 22f, textWidth, RowHeight - 24f), detail);

            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.xMax - CountColumn - SizeColumn, rect.y + 4f, CountColumn, 20f), count);

            GUI.color = locked ? palette.TextDisabled : palette.TextPrimary;

            Widgets.Label(new Rect(rect.xMax - SizeColumn, rect.y + 4f, SizeColumn, 20f), size);

            Text.Anchor = TextAnchor.UpperLeft;

            UIElementPainter.FillRounded(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), palette.Border);

            return rect.yMax;
        }

        private void Footer(Rect inRect, UIColorPaletteDef palette)
        {
            Rect rect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);

            Rect cancel = new Rect(rect.x, rect.y + 6f, 90f, 30f);
            Rect write = new Rect(rect.xMax - 170f, rect.y + 6f, 170f, 30f);

            if (SavesChrome.Button(cancel, done == null ? "Cancel" : "Close", palette))
                Close();

            Text.Font = GameFont.Tiny;
            GUI.color = done != null && done.Problem != null ? palette.Danger : palette.TextSecondary;
            Text.Anchor = TextAnchor.MiddleLeft;

            Widgets.Label(new Rect(cancel.xMax + 14f, rect.y, write.x - cancel.xMax - 24f, FooterHeight), Outcome());

            Text.Anchor = TextAnchor.UpperLeft;

            bool ready = examined && report != null && report.Shaped && done == null;

            if (!ready)
            {
                SavesChrome.Disabled(write, "Write cleaned copy", palette);

                return;
            }

            if (SavesChrome.Button(write, "Write cleaned copy", palette, true))
                Commit();
        }

        /// <summary>
        /// Writes the copy, compresses it if the player compresses their saves, and tells the window behind this
        /// one that there is a new file.
        ///
        /// <b>Compression is a separate step after the write, not part of it.</b> <see cref="SaveSweepWriter"/>
        /// deliberately knows nothing about encoders, which is what lets the whole sweep be verified outside the
        /// game. Reading the setting is guarded on its own so a settings file that cannot be read leaves a
        /// perfectly good plain save rather than no save.
        ///
        /// <b>The refresh runs on the main thread, not in the long event.</b> Everything it touches is interface
        /// state belonging to another window, and the long event's action runs on a worker thread.
        /// </summary>
        private void Commit()
        {
            string target = Target();

            LongEventHandler.QueueLongEvent(() =>
            {
                done = SaveSweepWriter.Write(file.FullName, target, options, report, missing);

                if (done == null || done.Problem != null || done.Path.NullOrEmpty())
                    return;

                SaveCompressor.AfterWrite(done.Path,
                    UIGuard.Try("Saves.SweepReadCompressSetting",
                        () => UIOverhaulSettingsFile.Current?.compressSaves ?? false, false, null));
            }, "Gideon_SweepWriting", false, null);

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (done != null && done.Problem == null)
                    written?.Invoke();
            });
        }

        /// <summary>Where the copy goes: beside the original, named after it.</summary>
        private string Target()
        {
            string folder = file.DirectoryName ?? GenFilePaths.SaveDataFolderPath;

            return Path.Combine(folder, Path.GetFileNameWithoutExtension(file.Name) + Suffix + ".rws");
        }

        private string Outcome()
        {
            if (!examined)
                return "Reading the save.";

            if (report == null || !report.Shaped)
                return problem ?? "Nothing can be offered for this file.";

            if (done != null)
            {
                if (done.Problem != null)
                    return done.Problem;

                return "Wrote " + Path.GetFileName(done.Path) + ". Removed " + done.RecordsRemoved
                       + " records, moved " + done.Renumbered + " colliding load ids, cleared "
                       + done.Repaired + " broken references and reclaimed "
                       + SavesChrome.Size(done.BytesRemoved) + ". The original is unchanged.";
            }

            int records = 0;
            long bytes = 0L;

            if (full != null)
            {
                foreach (KeyValuePair<string, int> pair in full.RemovedByReason)
                {
                    if (!Enabled(pair.Key))
                        continue;

                    records += pair.Value;
                    bytes += full.BytesByReason.TryGetValue(pair.Key, out long held) ? held : 0L;
                }
            }

            int repairs = options.RenumberDuplicates ? report.Duplicates : 0;
            int cleared = options.RepairDangling ? report.DanglingReferences : 0;

            return "Writes " + Path.GetFileName(Target()) + " beside the original. Moves " + repairs
                   + " colliding load ids, clears " + cleared + " broken references and removes about "
                   + records + " records, reclaiming about " + SavesChrome.Size(bytes) + ".";
        }

        /// <summary>Whether the row that produces this reason is currently switched on.</summary>
        private bool Enabled(string reason)
        {
            if (reason == "Dead pawn records")
                return options.RemoveDeadPawns;

            if (reason == "Mothballed world pawns")
                return options.RemoveMothballed;

            if (reason == "History and logs")
                return options.RemoveHistory;

            if (reason == "Unused policies and filters")
                return options.RemoveUnusedPolicies;

            const string tail = " no longer installed";

            if (!reason.EndsWith(tail, StringComparison.Ordinal))
                return false;

            string kind = reason.Substring(0, reason.Length - tail.Length);

            return SaveSweepXml.Discarded(kind) ? options.RemoveDiscardedRecords : options.RemoveMissingThings;
        }

        /// <summary>
        /// The key the writer tallies a removal under.
        ///
        /// A def kind gets the suffix the writer builds from it; anything else is already a whole label.
        /// </summary>
        private static string Reason(string kind)
        {
            return kind.EndsWith("Def", StringComparison.Ordinal) ? kind + " no longer installed" : kind;
        }

        private int Names(string kind)
        {
            return missing != null && missing.TryGetValue(kind, out HashSet<string> gone) ? gone.Count : 0;
        }

        /// <summary>How many def names are missing across every kind RimWorld would discard anyway.</summary>
        private int Discarded()
        {
            int total = 0;

            if (missing == null)
                return 0;

            foreach (KeyValuePair<string, HashSet<string>> pair in missing)
            {
                if (SaveSweepXml.Discarded(pair.Key))
                    total += pair.Value.Count;
            }

            return total;
        }

        private string DiscardedBytes()
        {
            long bytes = 0L;

            if (full == null)
                return "";

            foreach (KeyValuePair<string, long> pair in full.BytesByReason)
            {
                const string tail = " no longer installed";

                if (!pair.Key.EndsWith(tail, StringComparison.Ordinal))
                    continue;

                if (SaveSweepXml.Discarded(pair.Key.Substring(0, pair.Key.Length - tail.Length)))
                    bytes += pair.Value;
            }

            return bytes == 0L ? "" : SavesChrome.Size(bytes);
        }

        private string Bytes(string reason)
        {
            if (full == null || !full.BytesByReason.TryGetValue(reason, out long bytes))
                return "";

            return SavesChrome.Size(bytes);
        }

        private string Count(string list)
        {
            SaveSweepFinding finding = Finding(list);

            return finding == null ? "" : finding.Count.ToString();
        }

        private SaveSweepFinding Finding(string list)
        {
            if (report == null)
                return null;

            for (int i = 0; i < report.Reclaimable.Count; i++)
            {
                if (report.Reclaimable[i].Key == list)
                    return report.Reclaimable[i];
            }

            return null;
        }

        private string Section(string name)
        {
            if (report == null)
                return "";

            for (int i = 0; i < report.Sections.Count; i++)
            {
                if (report.Sections[i].Key == name)
                    return SavesChrome.Size(report.Sections[i].Bytes);
            }

            return "";
        }
    }
}
