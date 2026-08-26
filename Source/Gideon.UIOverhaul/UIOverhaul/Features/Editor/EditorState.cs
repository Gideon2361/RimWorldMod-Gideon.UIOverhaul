using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The five panels that are state rather than character: health, needs, thoughts, equipment, relationships.
    ///
    /// <b>Separate from <see cref="EditorWho"/> because the risk is different.</b> Giving somebody a trait changes
    /// who they are for the rest of the game; clearing a thought changes something that was going to expire on its
    /// own within the day. A player who understands that split can experiment in the safe half and be careful in
    /// the other, and a rail that mixed them would offer no way to tell which was which.
    /// </summary>
    internal static class EditorState
    {
        // ---------------------------------------------------------------------------------------
        // Health
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Every hediff with its severity, and the two buttons behind half the visits to this panel.
        ///
        /// <b>Removing goes through <c>HealthUtility.Cure</c> rather than <c>RemoveHediff</c>.</b> Cure handles
        /// the defs that come in groups -- <c>cureAllAtOnceIfCuredByItem</c>, which is how a disease with a
        /// hediff per body part behaves -- so removing one carcinoma removes the carcinoma rather than one of six
        /// and leaving five that still kill the pawn.
        /// </summary>
        internal static float Health(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            Pawn_HealthTracker health = UIGuard.Try("Editor.Health", () => pawn.health, null, null);

            if (health == null || health.hediffSet == null)
                return EditorParts.Note(view, view.y, "This pawn has no health to edit.", palette) - view.y;

            float y = EditorParts.Heading(view, view.y, "Conditions", palette, Summary(pawn));

            List<Hediff> held = new List<Hediff>(health.hediffSet.hediffs);

            for (int i = 0; i < held.Count; i++)
            {
                Hediff hediff = held[i];

                if (hediff == null)
                    continue;

                Rect row;

                if (EditorParts.Row(view, y, Label(hediff), Severity(hediff), Colour(hediff, palette), palette,
                        out row, Tip(hediff)))
                    Cure(context, hediff);

                y = row.yMax + 4f;
            }

            if (held.Count == 0)
                y = EditorParts.Note(view, y, "Nothing at all. Not even a scar.", palette);

            y += EditorParts.RowGap;

            Rect buttons = new Rect(view.x, y, view.width, EditorParts.ControlHeight);

            if (EditorParts.Add(EditorParts.Column(buttons, 0, 3), y, "Heal everything", palette, true,
                    "Cures every injury, disease, addiction and chronic condition and restores every missing "
                    + "part. Implants and prosthetics are left alone, since an arm is not an ailment."))
                Heal(context);

            if (EditorParts.Add(EditorParts.Column(buttons, 1, 3), y, "Add a condition", palette))
                Offer(context);

            // The third column, which the row was already laid out for and nothing had claimed.
            if (EditorParts.Add(EditorParts.Column(buttons, 2, 3), y, "Sedate", palette, true,
                    "Anesthetic at full strength, which puts them straight out.\n\nTops up the dose already "
                    + "there rather than stacking a second one, so pressing it twice is the same as pressing it "
                    + "once."))
                Sedate(context);

            y += EditorParts.ControlHeight + EditorParts.RowGap;

            bool keep = EditorSedation.Kept(pawn);

            if (UICheckboxControl.Draw(new Rect(view.x, y, view.width, 22f), ref keep, palette, "Keep sedated",
                    "Puts the dose back every 250 ticks, so they stay under until this is switched off.\n\n"
                    + "Needed because anesthetic is not a switch: it fades on its own and then removes itself "
                    + "after a day or two, so holding somebody under means saying so more than once.\n\nThis "
                    + "keeps working while the editor is closed and survives a save. It stops by itself if they "
                    + "die."))
                EditorSedation.SetKept(pawn, keep);

            y += 24f + EditorParts.BlockGap;

            return y - view.y;
        }

        /// <summary>
        /// Anesthetic at full strength, recorded so Revert puts the dose back where it was.
        ///
        /// <b>Undone to the previous severity rather than by removing the hediff,</b> because somebody who was
        /// already under before the button was pressed should not be woken by undoing a top-up that changed
        /// almost nothing. Only a dose that was not there at all is undone by taking it away.
        ///
        /// The application itself lives in <see cref="EditorSedation"/>, since the keep-sedated tick has to do
        /// exactly the same thing without an editor open to record anything.
        /// </summary>
        private static void Sedate(EditorContext context)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.SedateButton", () =>
            {
                Hediff before = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic);

                // Negative marks "there was none", which is a state no real severity can be confused with.
                float previous = before != null ? before.Severity : -1f;

                if (!EditorSedation.Sedate(pawn))
                    return;

                EditorParts.Redraw(pawn);

                context.Changes.Record("health", () =>
                {
                    Hediff now = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic);

                    if (now == null)
                        return;

                    if (previous < 0f)
                        pawn.health.RemoveHediff(now);
                    else
                        now.Severity = previous;

                    EditorParts.Redraw(pawn);
                });
            }, "That pawn could not be sedated.");
        }

        private static string Summary(Pawn pawn)
        {
            return UIGuard.Try<string>("Editor.HealthSummary",
                () => HealthUtility.GetGeneralConditionLabel(pawn, true), null, null);
        }

        private static string Label(Hediff hediff)
        {
            return UIGuard.Try<string>("Editor.HediffLabel", () =>
            {
                string label = hediff.LabelCap;

                return hediff.Part == null ? label : label + "  (" + hediff.Part.Label + ")";
            }, "?", null);
        }

        private static string Severity(Hediff hediff)
        {
            return UIGuard.Try<string>("Editor.Severity", () =>
            {
                if (hediff is Hediff_MissingPart)
                    return "missing";

                if (hediff.def != null && hediff.def.countsAsAddedPartOrImplant)
                    return "implant";

                if (hediff.Bleeding)
                    return "bleeding";

                return hediff.Severity >= 0.01f ? hediff.Severity.ToString("0.##") : null;
            }, null, null);
        }

        private static Color Colour(Hediff hediff, UIColorPaletteDef palette)
        {
            return UIGuard.Try("Editor.HediffColour", () =>
            {
                if (hediff.def != null && hediff.def.countsAsAddedPartOrImplant)
                    return palette.Info;

                if (hediff.Bleeding || hediff.IsLethal)
                    return palette.Danger;

                return hediff.def != null && hediff.def.isBad ? palette.Warning : palette.Success;
            }, palette.TextSecondary, null);
        }

        private static string Tip(Hediff hediff)
        {
            return UIGuard.Try<string>("Editor.HediffTip",
                () => hediff.def != null ? hediff.def.description : null, null, null);
        }

        private static void Cure(EditorContext context, Hediff hediff)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.Cure", () =>
            {
                HediffDef def = hediff.def;
                BodyPartRecord part = hediff.Part;
                float severity = hediff.Severity;

                bool missing = hediff is Hediff_MissingPart;

                if (missing)
                {
                    if (part == null)
                        return;

                    pawn.health.RestorePart(part);
                }
                else
                {
                    HealthUtility.Cure(hediff);
                }

                if (part != null && part.def == BodyPartDefOf.Lung && !missing)
                {
                    // Nothing to warn about here; removing a lung is on the adding side. Left as a marker of
                    // where that check belongs if the panel ever gains a "destroy part" button.
                }

                EditorParts.Redraw(pawn);

                context.Changes.Record("health", () =>
                {
                    if (missing)
                    {
                        Hediff back = HediffMaker.MakeHediff(def, pawn, part);

                        pawn.health.AddHediff(back, part);
                    }
                    else
                    {
                        Hediff back = HediffMaker.MakeHediff(def, pawn, part);

                        back.Severity = severity;

                        pawn.health.AddHediff(back, part);
                    }

                    EditorParts.Redraw(pawn);
                });
            }, "That condition could not be removed.");
        }

        /// <summary>
        /// Cures everything bad and restores everything missing, leaving implants alone.
        ///
        /// <b>Shared with the Clean resurrection, which is the whole reason it is a method.</b> "Clean means
        /// whole" and "Heal everything" are the same operation stated twice, and two implementations of it would
        /// disagree the first time either was fixed.
        ///
        /// <b>What vanilla's own resurrection leaves behind is exactly what this exists to catch.</b>
        /// <c>Notify_Resurrected</c> clears diseases, lethal conditions and fresh injuries, and it does not clear
        /// permanent scars on intact parts or chronic conditions that are not curable by an item -- a bad back, a
        /// cataract, frailty. Somebody who asked for their colonist back did not ask for a frail one.
        /// </summary>
        internal static int Heal(EditorContext context)
        {
            Pawn pawn = context.Pawn;

            int cured = 0;

            UIGuard.Try("Editor.HealAll", () =>
            {
                // A snapshot, because curing walks the same list and one def can take several hediffs with it.
                List<Hediff> held = new List<Hediff>(pawn.health.hediffSet.hediffs);

                for (int i = 0; i < held.Count; i++)
                {
                    Hediff hediff = held[i];

                    if (hediff == null || !Bad(pawn, hediff))
                        continue;

                    // Already gone, taken by an earlier cure that cleared its whole def.
                    if (!pawn.health.hediffSet.hediffs.Contains(hediff))
                        continue;

                    HealthUtility.Cure(hediff);

                    cured++;
                }

                // After the cures, since restoring a part removes the missing-part hediffs under it and would
                // have invalidated the list above.
                int guard = 0;

                while (guard++ < 200)
                {
                    Hediff_MissingPart missing = null;

                    List<Hediff> now = pawn.health.hediffSet.hediffs;

                    for (int i = 0; i < now.Count; i++)
                    {
                        Hediff_MissingPart candidate = now[i] as Hediff_MissingPart;

                        if (candidate == null || candidate.Part == null)
                            continue;

                        // An implant's own part is not missing in the sense that matters: restoring it would
                        // take the prosthetic off.
                        if (pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(candidate.Part))
                            continue;

                        missing = candidate;

                        break;
                    }

                    if (missing == null)
                        break;

                    pawn.health.RestorePart(missing.Part);

                    cured++;
                }

                EditorParts.Redraw(pawn);
            }, "The pawn could not be fully healed.");

            if (cured > 0)
                context.Changes.RecordPermanent("healed everything");

            return cured;
        }

        /// <summary>
        /// Whether a hediff is something "heal everything" should take.
        ///
        /// Implants and anything the game does not call bad stay. Missing parts are handled separately, since the
        /// answer to one is to restore the part rather than to remove the record of its absence.
        /// </summary>
        private static bool Bad(Pawn pawn, Hediff hediff)
        {
            return UIGuard.Try("Editor.IsBad", () =>
            {
                if (hediff.def == null || !hediff.def.isBad)
                    return false;

                if (hediff.def.countsAsAddedPartOrImplant || hediff is Hediff_AddedPart)
                    return false;

                if (hediff is Hediff_MissingPart)
                    return false;

                if (hediff.Part != null
                    && pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(hediff.Part))
                    return false;

                return true;
            }, false, null);
        }

        /// <summary>
        /// Opens the add-a-condition wizard.
        ///
        /// <b>One window with steps, not a chain of pick lists, from 2026-08-23.</b> This used to open a list of
        /// conditions, close it, open a list of body parts, close it, and add the hediff at whatever severity the
        /// def happened to start at. There was no point in that sequence where a third question could be asked,
        /// which is why One with Death's control expansion could only ever be added at level one. See
        /// <see cref="Dialog_AddCondition"/>.
        /// </summary>
        private static void Offer(EditorContext context)
        {
            Dialog_AddCondition.Open(context);
        }

        /// <summary>
        /// Adds one condition to the pawn, at a level or a severity, and records the undo.
        ///
        /// <b>The one place a condition is added,</b> which is what makes the part guard reliable: whatever route
        /// asked for it, a hediff that needs a body part gets one here. See <see cref="HediffPlacement"/>.
        ///
        /// <paramref name="level"/> is zero for anything that is not a <c>Hediff_Level</c>, and
        /// <paramref name="severity"/> is null when the def has nothing worth choosing -- in which case the hediff
        /// is added exactly as the game would add it.
        /// </summary>
        internal static void AddCondition(EditorContext context, HediffDef def, BodyPartRecord part, int level,
            float? severity)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.AddHediff", () =>
            {
                if (pawn == null || def == null)
                    return;

                BodyPartRecord on = HediffPlacement.Resolve(pawn, def, part);

                if (on == null && HediffPlacement.NeedsPart(def))
                {
                    EditorParts.Warn(EditorParts.LabelOf(def) + " needs a body part and "
                                     + pawn.LabelShortCap + " has none it can go on.");

                    return;
                }

                Hediff made = HediffMaker.MakeHediff(def, pawn, on);

                if (made == null)
                    return;

                if (severity.HasValue)
                    made.Severity = severity.Value;

                bool fatal = pawn.health.WouldDieAfterAddingHediff(made);

                pawn.health.AddHediff(made, on);

                // After AddHediff: SetLevelTo goes through ChangeLevel, which clamps against the def, and a level
                // set before the hediff is on the pawn is one PostAdd may overwrite.
                if (level > 0 && made is Hediff_Level levelled)
                    levelled.SetLevelTo(level);

                EditorParts.Redraw(pawn);

                if (fatal)
                    EditorParts.Warn(pawn.LabelShortCap + " will not survive that.");

                context.Changes.Record("health", () =>
                {
                    if (pawn.health.hediffSet.hediffs.Contains(made))
                        pawn.health.RemoveHediff(made);

                    EditorParts.Redraw(pawn);
                });
            }, "That condition could not be added.");
        }

        // ---------------------------------------------------------------------------------------
        // Needs
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Each need as a slider.
        ///
        /// <b>The safe panel, and worth saying so once here rather than on screen:</b> every need moves on its own
        /// within the day, so nothing set here is permanent even without Revert.
        /// </summary>
        internal static float Needs(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            float y = EditorParts.Heading(view, view.y, "Needs", palette);

            List<Need> needs = UIGuard.Try("Editor.NeedList",
                () => pawn.needs != null ? pawn.needs.AllNeeds : null, null, null);

            if (needs == null || needs.Count == 0)
                return EditorParts.Note(view, y, "This pawn has no needs.", palette) - view.y;

            for (int i = 0; i < needs.Count; i++)
            {
                Need need = needs[i];

                if (need == null)
                    continue;

                y = Slider(view, y, need, context, palette);
            }

            return y + EditorParts.BlockGap - view.y;
        }

        /// <summary>Which need is being dragged, by def name, or null. One at a time by definition.</summary>
        private static string draggingNeed;

        /// <summary>
        /// How far above and below the track a click still counts.
        ///
        /// The track is six pixels tall, which is the right height to look at and the wrong height to hit. The
        /// band is invisible and the bar keeps its own size.
        /// </summary>
        private const float NeedGrab = 7f;

        /// <summary>
        /// One need, drawn the way the inspect pane draws it and dragged the way a slider is.
        ///
        /// <b>The same call the inspector makes, not a copy of it.</b> <c>InspectPaneParts.Need</c> lays out the
        /// caption, the value, the track and the break-point ticks; this passes the same arguments and gets the
        /// track's rectangle back to hang the drag on. Two panels showing a colonist's needs that do not look
        /// alike is the thing worth avoiding here, and the only way to keep them alike is for one of them to draw
        /// the other's row.
        ///
        /// <b>Mood brings its ticks with it.</b> Those marks are this pawn's own three break points, and they are
        /// the difference between a mood bar you can read and a percentage you cannot -- which matters more here
        /// than in the inspect pane, since here you are about to drag it somewhere.
        ///
        /// <b>The handle appears on hover and not before.</b> At rest the page reads as the inspect pane; the
        /// moment the pointer is near a bar, the knob says the bar is grabbable. A knob drawn permanently would
        /// undo the match this was asked for, and no knob at all would leave the panel looking like a readout.
        /// </summary>
        private static float Slider(Rect view, float y, Need need, EditorContext context,
            UIColorPaletteDef palette)
        {
            float max = UIGuard.Try("Editor.NeedMax", () => need.MaxLevel, 1f, null);

            float current = UIGuard.Try("Editor.NeedNow", () => need.CurLevel, 0f, null);

            float fraction = Mathf.Clamp01(current / Mathf.Max(0.0001f, max));

            bool isMood = need is Need_Mood;

            Color fill = isMood ? palette.Mood : InspectPaneParts.Level(fraction, palette);

            Color readout = isMood
                ? InspectOverview.MoodColor(context.Pawn, fraction, palette)
                : InspectPaneParts.Level(fraction, palette);

            float[] ticks = isMood ? InspectOverview.MoodTicks(context.Pawn) : null;

            Rect track;

            float next = InspectPaneParts.Need(view, y, need.LabelCap, fraction.ToStringPercent(), readout,
                fraction, fill, ticks, null, palette, out track);

            Drag(track, need, max, context);

            return next;
        }

        /// <summary>
        /// Turns the drawn track into something you can set by pointing at it.
        ///
        /// <b>Hand-rolled rather than an invisible slider laid over the top.</b> RimWorld's slider draws its own
        /// art unconditionally, so borrowing it means covering the row we just drew; and its return value is the
        /// answer for this frame only, which is the wrong shape for a change that has to be recorded once with an
        /// undo entry rather than every pass of the mouse.
        ///
        /// The key is the need's def name, so the drag survives the row being re-laid out under it and cannot be
        /// confused with the bar above or below.
        /// </summary>
        private static void Drag(Rect track, Need need, float max, EditorContext context)
        {
            UIGuard.Try("Editor.NeedDrag", () =>
            {
                string key = need.def != null ? need.def.defName : null;

                if (key == null)
                    return;

                Rect grab = new Rect(track.x, track.y - NeedGrab, track.width, track.height + NeedGrab * 2f);

                Event input = Event.current;
                bool over = Mouse.IsOver(grab);

                if (input.type == EventType.MouseDown && input.button == 0 && over)
                {
                    draggingNeed = key;
                    input.Use();
                }
                else if (input.type == EventType.MouseUp && input.button == 0 && draggingNeed == key)
                {
                    draggingNeed = null;
                    input.Use();
                }

                if (draggingNeed != key)
                {
                    if (over && draggingNeed == null)
                        Knob(track, need.CurLevel / Mathf.Max(0.0001f, max), context.Palette);

                    return;
                }

                float wanted = Mathf.Clamp01((input.mousePosition.x - track.x)
                                             / Mathf.Max(1f, track.width)) * max;

                Knob(track, wanted / Mathf.Max(0.0001f, max), context.Palette);

                // Recorded rather than assigned, so Revert all puts the need back and the footer counts it.
                // Idempotent on a repeat: the change tracker keeps one entry per need whatever the mouse does.
                if (Mathf.Abs(wanted - need.CurLevel) > 0.001f)
                    context.Changes.Set(need.def.label, () => need.CurLevel, v => need.CurLevel = v, wanted);
            }, null);
        }

        /// <summary>The grab handle: a square on the track at the value, in the palette's accent.</summary>
        private static void Knob(Rect track, float fraction, UIColorPaletteDef palette)
        {
            const float size = 9f;

            float x = track.x + Mathf.Round(track.width * Mathf.Clamp01(fraction));

            Widgets.DrawBoxSolid(
                new Rect(x - size * 0.5f, track.center.y - size * 0.5f, size, size), palette.Accent);
        }

        // ---------------------------------------------------------------------------------------
        // Thoughts
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The live memory list, with what each one is doing to the mood.
        ///
        /// <b>This is the panel to experiment in.</b> Most memories expire on their own, so removing the one
        /// making somebody miserable is the least consequential thing in the window -- and it is the request
        /// behind most of the visits to it.
        /// </summary>
        internal static float Thoughts(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            MemoryThoughtHandler memories = UIGuard.Try("Editor.Memories",
                () => pawn.needs != null && pawn.needs.mood != null
                    ? pawn.needs.mood.thoughts.memories
                    : null, null, null);

            if (memories == null)
                return EditorParts.Note(view, view.y, "This pawn has no mood to think with.", palette) - view.y;

            float mood = UIGuard.Try("Editor.Mood", () => pawn.needs.mood.CurLevelPercentage, 0f, null);

            float y = EditorParts.Heading(view, view.y, "Memories", palette,
                "mood " + mood.ToStringPercent());

            List<Thought_Memory> held = new List<Thought_Memory>(memories.Memories);

            for (int i = 0; i < held.Count; i++)
            {
                Thought_Memory memory = held[i];

                if (memory == null)
                    continue;

                Rect row;

                float offset = UIGuard.Try("Editor.MoodOffset", () => memory.MoodOffset(), 0f, null);

                Color colour = offset > 0f
                    ? palette.Success
                    : offset < 0f
                        ? palette.Warning
                        : palette.TextDisabled;

                if (EditorParts.Row(view, y, Name(memory, palette), Offset(memory, offset), colour, palette,
                        out row, EditorParts.DescriptionOf(memory.def)))
                    Forget(context, memories, memory);

                y = row.yMax + 4f;
            }

            if (held.Count == 0)
                y = EditorParts.Note(view, y, "Nothing on their mind.", palette);

            y += EditorParts.RowGap;

            if (EditorParts.Add(view, y, "Add a memory", palette))
                OfferThought(context, memories);

            return y + EditorParts.ControlHeight + EditorParts.BlockGap - view.y;
        }

        /// <summary>
        /// The memory's label, and who it is about when it is about somebody.
        ///
        /// <b>The label alone is not enough for a social memory.</b> Three rows reading Chitchat and two reading
        /// Slighted say nothing about which relationship is in trouble, and the whole reason to open this panel
        /// on a miserable colonist is to find out. RimWorld's own needs tab has the same gap; it groups
        /// identical thoughts into one line and counts them instead, which loses the same information.
        ///
        /// <b>Read off <c>otherPawn</c>, which lives on <c>Thought_Memory</c> rather than on the social
        /// subclass.</b> So a memory of somebody that is not a social thought -- a colonist killed, a prisoner
        /// sold -- names them too, which is the same question asked about a different row.
        ///
        /// Dimmed rather than separated by a glyph. The row already tells the eye where a field ends by changing
        /// color, on its right-hand side, and doing it the same way twice is one rule instead of two.
        /// </summary>
        private static string Name(Thought_Memory memory, UIColorPaletteDef palette)
        {
            return UIGuard.Try<string>("Editor.MemoryLabel", () =>
            {
                string label = memory.LabelCap;

                Pawn other = memory.otherPawn;

                if (other == null)
                    return label;

                string who = other.LabelShort.NullOrEmpty() ? other.LabelCap : other.LabelShort;

                if (who.NullOrEmpty())
                    return label;

                return label + "  <color=#" + ColorUtility.ToHtmlStringRGB(palette.TextSecondary) + ">" + who
                       + "</color>";
            }, "?", null);
        }

        /// <summary>The mood offset and how long is left, which is the pair that says whether to bother.</summary>
        private static string Offset(Thought_Memory memory, float offset)
        {
            return UIGuard.Try<string>("Editor.MemoryOffset", () =>
            {
                // Three-section format: positive, negative, zero. A plus sign on a mood offset is the whole
                // difference between "this is helping" and "this is the problem".
                string mood = offset.ToString("+0.#;-0.#;0");

                if (memory.permanent)
                    return mood + "  permanent";

                int left = memory.DurationTicks - memory.age;

                return left <= 0 ? mood : mood + "  " + left.ToStringTicksToPeriod(false, false, false);
            }, null, null);
        }

        private static void Forget(EditorContext context, MemoryThoughtHandler memories, Thought_Memory memory)
        {
            UIGuard.Try("Editor.Forget", () =>
            {
                ThoughtDef def = memory.def;
                Pawn other = memory.otherPawn;
                int age = memory.age;

                memories.RemoveMemory(memory);

                context.Changes.Record("memories", () =>
                {
                    memories.TryGainMemory(def, other);

                    Thought_Memory back = memories.GetFirstMemoryOfDef(def);

                    if (back != null)
                        back.age = age;
                });
            }, "That memory could not be removed.");
        }

        private static void OfferThought(EditorContext context, MemoryThoughtHandler memories)
        {
            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.ThoughtOptions", () =>
            {
                List<ThoughtDef> all = DefDatabase<ThoughtDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    ThoughtDef def = all[i];

                    // Situational thoughts are recomputed from the world every tick, so one added by hand is
                    // gone before the window closes. Only memories are worth offering.
                    if (def.thoughtClass != null && !typeof(Thought_Memory).IsAssignableFrom(def.thoughtClass))
                        continue;

                    if (def.stages == null || def.stages.Count == 0)
                        continue;

                    ThoughtDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Note = def.stages[0] != null
                            ? def.stages[0].baseMoodEffect.ToString("0.#")
                            : null,
                        Tooltip = def.stages[0] != null ? def.stages[0].description : null,
                        Chosen = () => UIGuard.Try("Editor.GainMemory", () =>
                        {
                            memories.TryGainMemory(captured);

                            context.Changes.Record("memories", () =>
                            {
                                Thought_Memory added = memories.GetFirstMemoryOfDef(captured);

                                if (added != null)
                                    memories.RemoveMemory(added);
                            });
                        }, "That memory could not be added.")
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Add a memory", options, "Search memories");
        }

        // ---------------------------------------------------------------------------------------
        // Equipment
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The weapon and what they are carrying.
        ///
        /// <b>Worn clothing is not here; it is on Appearance beside the render that shows it.</b> That splits what
        /// vanilla treats as one Gear tab, and it is the decision in this window I would most readily reverse.
        ///
        /// <b>Nothing is spawned into the world.</b> Items are made onto the pawn and, when removed, held in the
        /// undo entry rather than dropped at their feet -- so a weapon taken off here does not become loot on the
        /// floor of wherever they happened to be standing.
        /// </summary>
        internal static float Equipment(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            float y = EditorParts.Heading(view, view.y, "Weapon", palette);

            ThingWithComps primary = UIGuard.Try("Editor.Primary",
                () => pawn.equipment != null ? pawn.equipment.Primary : null, null, null);

            if (primary != null)
            {
                Rect row;

                if (EditorParts.Row(view, y, primary.LabelCapNoCount, Made(primary), palette.TextSecondary,
                        palette, out row, EditorParts.DescriptionOf(primary.def), true, primary.def,
                        primary.DrawColor))
                    Disarm(context, primary);

                y = row.yMax + 4f;
            }
            else
            {
                y = EditorParts.Note(view, y, "Unarmed.", palette);
            }

            y += EditorParts.RowGap;

            if (pawn.equipment != null && EditorParts.Add(view, y, "Give them a weapon", palette))
                OfferWeapon(context);

            y += EditorParts.ControlHeight + EditorParts.BlockGap;

            y = Apparel(view, y, context, palette);

            y = EditorParts.Heading(view, y, "Carrying", palette);

            ThingOwner<Thing> carried = UIGuard.Try("Editor.Inventory",
                () => pawn.inventory != null ? pawn.inventory.innerContainer : null, null, null);

            if (carried == null || carried.Count == 0)
                return EditorParts.Note(view, y, "Nothing.", palette) + EditorParts.BlockGap - view.y;

            List<Thing> held = new List<Thing>();

            for (int i = 0; i < carried.Count; i++)
                held.Add(carried[i]);

            for (int i = 0; i < held.Count; i++)
            {
                Thing thing = held[i];

                Rect row;

                if (EditorParts.Row(view, y, thing.LabelCap, Made(thing), palette.TextSecondary, palette,
                        out row, EditorParts.DescriptionOf(thing.def), true, thing.def, thing.DrawColor))
                    Drop(context, thing);

                y = row.yMax + 4f;
            }

            return y + EditorParts.BlockGap - view.y;
        }

        /// <summary>
        /// What they are wearing, and the way to add to it.
        ///
        /// <b>This block was missing entirely until 2026-08-23.</b> The panel had a weapon and an inventory, so
        /// the one thing about a pawn's kit that is visible on the map from across the room was the one thing the
        /// editor could not touch.
        ///
        /// Rows are the same shape as the weapon and the carried items above -- name, how it was made, a remove
        /// button -- because they are the same question about a different container.
        /// </summary>
        private static float Apparel(Rect view, float y, EditorContext context, UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            y = EditorParts.Heading(view, y, "Apparel", palette);

            List<RimWorld.Apparel> worn = UIGuard.Try("Editor.WornApparel",
                () => pawn.apparel != null ? pawn.apparel.WornApparel : null, null, null);

            if (worn == null)
                return EditorParts.Note(view, y, "This one cannot wear anything.", palette)
                       + EditorParts.BlockGap;

            if (worn.Count == 0)
            {
                y = EditorParts.Note(view, y, "Naked.", palette);
            }
            else
            {
                // Copied before the loop, because removing a piece writes to the list being walked and the
                // remove button fires mid-draw.
                List<RimWorld.Apparel> listed = new List<RimWorld.Apparel>(worn);

                for (int i = 0; i < listed.Count; i++)
                {
                    RimWorld.Apparel apparel = listed[i];

                    Rect row;

                    if (EditorParts.Row(view, y, apparel.LabelCapNoCount, Made(apparel), palette.TextSecondary,
                            palette, out row, EditorParts.DescriptionOf(apparel.def), true, apparel.def,
                            apparel.DrawColor))
                        EditorApparel.Strip(context, apparel);

                    y = row.yMax + 4f;
                }
            }

            y += EditorParts.RowGap;

            if (pawn.apparel != null && EditorParts.Add(view, y, "Add apparel", palette))
                Dialog_AddApparel.Open(context);

            return y + EditorParts.ControlHeight + EditorParts.BlockGap;
        }

        private static string Made(Thing thing)
        {
            return UIGuard.Try<string>("Editor.ThingMade", () =>
            {
                QualityCategory quality;

                string made = thing.TryGetQuality(out quality) ? quality.GetLabel() : null;

                string stuff = thing.Stuff != null ? thing.Stuff.LabelAsStuff : null;

                if (made.NullOrEmpty())
                    return stuff;

                return stuff.NullOrEmpty() ? made : made + ", " + stuff;
            }, null, null);
        }

        private static void Disarm(EditorContext context, ThingWithComps weapon)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.Disarm", () =>
            {
                pawn.equipment.Remove(weapon);

                context.Changes.Record("weapon", () => pawn.equipment.AddEquipment(weapon));
            }, "That weapon could not be taken away.");
        }

        private static void OfferWeapon(EditorContext context)
        {
            Pawn pawn = context.Pawn;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.WeaponOptions", () =>
            {
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef def = all[i];

                    if (!def.IsWeapon || def.destroyOnDrop)
                        continue;

                    ThingDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Note = def.IsRangedWeapon ? "ranged" : "melee",
                        Tooltip = EditorParts.DescriptionOf(def),
                        Chosen = () => Arm(context, captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Give them a weapon", options, "Search weapons");
        }

        private static void Arm(EditorContext context, ThingDef def)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.Arm", () =>
            {
                ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;

                ThingWithComps made = ThingMaker.MakeThing(def, stuff) as ThingWithComps;

                if (made == null)
                    return;

                ThingWithComps was = pawn.equipment.Primary;

                if (was != null)
                    pawn.equipment.Remove(was);

                pawn.equipment.AddEquipment(made);

                context.Changes.Record("weapon", () =>
                {
                    pawn.equipment.Remove(made);

                    made.Destroy();

                    if (was != null)
                        pawn.equipment.AddEquipment(was);
                });
            }, "That weapon could not be given.");
        }

        private static void Drop(EditorContext context, Thing thing)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.DropCarried", () =>
            {
                int count = thing.stackCount;

                pawn.inventory.innerContainer.Remove(thing);

                context.Changes.Record("carried", () =>
                {
                    thing.stackCount = count;

                    pawn.inventory.innerContainer.TryAdd(thing, false);
                });
            }, "That could not be taken off them.");
        }

        // ---------------------------------------------------------------------------------------
        // Relationships
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Direct relations, settable, and opinions, not.
        ///
        /// <b>Opinion is read-only, which is a departure from the proposal.</b> It said both were settable;
        /// opinion is not a field. <c>OpinionOf</c> sums the social thoughts one pawn holds about another and a
        /// couple of relation bonuses, so there is nothing to write -- the way to move an opinion is to add or
        /// remove the memories driving it, which is the Thoughts panel. It is shown here because it is the number
        /// somebody came to this panel to look at.
        ///
        /// <b>Adding a relation adds it on both sides,</b> because <c>AddDirectRelation</c> does: a one-way
        /// marriage is a save-file bug and the game's own call is what avoids it.
        /// </summary>
        internal static float Relationships(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            Pawn_RelationsTracker relations = UIGuard.Try("Editor.Relations",
                () => pawn.relations, null, null);

            if (relations == null)
                return EditorParts.Note(view, view.y, "This pawn has no relationships.", palette) - view.y;

            float y = EditorParts.Heading(view, view.y, "Relations", palette);

            List<DirectPawnRelation> held = new List<DirectPawnRelation>(relations.DirectRelations);

            for (int i = 0; i < held.Count; i++)
            {
                DirectPawnRelation relation = held[i];

                if (relation == null || relation.def == null || relation.otherPawn == null)
                    continue;

                Rect row;

                int opinion = UIGuard.Try("Editor.Opinion", () => relations.OpinionOf(relation.otherPawn), 0,
                    null);

                Color colour = opinion > 20
                    ? palette.Success
                    : opinion < -20
                        ? palette.Warning
                        : palette.TextSecondary;

                string label = UIGuard.Try<string>("Editor.RelationLabel",
                    () => relation.def.GetGenderSpecificLabelCap(relation.otherPawn) + "  "
                          + relation.otherPawn.LabelShortCap, "?", null);

                if (EditorParts.Row(view, y, label, "opinion " + opinion.ToStringWithSign(), colour, palette,
                        out row, "Opinion is the sum of what they think about each other. It moves by adding or "
                                 + "removing memories, not by being set."))
                    Sever(context, relations, relation);

                y = row.yMax + 4f;
            }

            if (held.Count == 0)
                y = EditorParts.Note(view, y, "None.", palette);

            y += EditorParts.RowGap;

            if (EditorParts.Add(view, y, "Add a relation", palette))
                OfferRelation(context, relations);

            return y + EditorParts.ControlHeight + EditorParts.BlockGap - view.y;
        }

        private static void Sever(EditorContext context, Pawn_RelationsTracker relations,
            DirectPawnRelation relation)
        {
            UIGuard.Try("Editor.Sever", () =>
            {
                PawnRelationDef def = relation.def;
                Pawn other = relation.otherPawn;

                relations.TryRemoveDirectRelation(def, other);

                context.Changes.Record("relations", () => relations.AddDirectRelation(def, other));
            }, "That relation could not be removed.");
        }

        private static void OfferRelation(EditorContext context, Pawn_RelationsTracker relations)
        {
            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.RelationDefs", () =>
            {
                List<PawnRelationDef> all = DefDatabase<PawnRelationDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    PawnRelationDef def = all[i];

                    if (def.implied)
                        continue;

                    PawnRelationDef captured = def;

                    options.Add(new EditorOption
                    {
                        Label = EditorParts.LabelOf(def),
                        Chosen = () => OfferOther(context, relations, captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Add a relation", options, "Search relations");
        }

        /// <summary>
        /// Who the new relation is with.
        ///
        /// <b>Everybody the game knows about, not only the colonists here.</b> A dead spouse, somebody in another
        /// faction, a pawn on a caravan: the relations graph spans all of them, and offering only the ones
        /// standing on this map would make half the legitimate edits impossible.
        /// </summary>
        private static void OfferOther(EditorContext context, Pawn_RelationsTracker relations,
            PawnRelationDef def)
        {
            Pawn pawn = context.Pawn;

            List<EditorOption> options = new List<EditorOption>();

            UIGuard.Try("Editor.RelationOthers", () =>
            {
                List<Pawn> candidates = new List<Pawn>();

                foreach (Pawn other in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (other != pawn && other.RaceProps != null && other.RaceProps.Humanlike)
                        candidates.Add(other);
                }

                foreach (Pawn other in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
                {
                    if (other != pawn && other.RaceProps != null && other.RaceProps.Humanlike
                        && !candidates.Contains(other))
                        candidates.Add(other);
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    Pawn other = candidates[i];
                    Pawn captured = other;

                    options.Add(new EditorOption
                    {
                        Label = other.LabelShortCap,
                        Note = Where(other),
                        Marked = relations.DirectRelationExists(def, other) ? "already related this way" : null,
                        Chosen = () => Bind(context, relations, def, captured)
                    });
                }

                options.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.Ordinal));
            }, null);

            Dialog_PickFrom.Open("Who is their " + EditorParts.LabelOf(def), options, "Search people");
        }

        private static string Where(Pawn other)
        {
            return UIGuard.Try<string>("Editor.RelationWhere", () =>
            {
                if (other.Dead)
                    return "dead";

                if (other.Faction != null && other.Faction.IsPlayer)
                    return "ours";

                return other.Faction != null ? other.Faction.Name : "no faction";
            }, null, null);
        }

        private static void Bind(EditorContext context, Pawn_RelationsTracker relations, PawnRelationDef def,
            Pawn other)
        {
            UIGuard.Try("Editor.Bind", () =>
            {
                relations.AddDirectRelation(def, other);

                context.Changes.Record("relations",
                    () => relations.TryRemoveDirectRelation(def, other));
            }, "That relation could not be added.");
        }
    }
}
