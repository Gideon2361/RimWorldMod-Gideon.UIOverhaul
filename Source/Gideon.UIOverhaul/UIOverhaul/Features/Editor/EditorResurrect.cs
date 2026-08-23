using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Corpses;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Bringing somebody back, with its arithmetic on screen before it acts.
    ///
    /// <b>The one operation in the mod that cannot be undone,</b> and the footer says so in red before it happens
    /// rather than after. Everything else in this window is a closure away from being reversed; a dead pawn who is
    /// now alive is a different object in a different world state.
    ///
    /// <b>Clean is the default, and clean means whole.</b> Somebody who opened this window to bring a colonist
    /// back asked for their colonist back -- not for one with dementia, and not for one who stands up bleeding
    /// from the wound that killed him and has to be carried to a bed. So Clean resurrects with missing parts
    /// restored and then runs the same heal the health panel's own button does, because vanilla's resurrection
    /// deliberately leaves permanent scars and the chronic conditions no item can cure.
    ///
    /// <b>The odds are read, not written.</b> <c>TryResurrectWithSideEffects</c> evaluates three curves against
    /// one number -- rot progress in days -- and those are the numbers on the panel. They stay on screen under
    /// Clean, drawn as not applying, because that figure is also the answer to how long a body is worth keeping
    /// and it is the same number the corpses tab sorts by.
    /// </summary>
    internal static class EditorResurrect
    {
        private static readonly string[] Methods = { "Clean", "With side effects" };

        private static readonly string[] Parts = { "Restore", "Leave missing" };

        /// <summary>Clean, because that is what somebody opening this panel asked for. Asked for 2026-08-22.</summary>
        private static bool clean = true;

        private static bool restoreParts = true;

        internal static float Draw(Rect view, EditorContext context)
        {
            Pawn pawn = context.Pawn;
            UIColorPaletteDef palette = context.Palette;

            Corpse corpse = UIGuard.Try("Editor.Corpse", () => pawn.Corpse, null, null);

            float y = Header(view, view.y, context, corpse, palette);

            y = EditorParts.Heading(view, y, "How", palette);

            Rect row = new Rect(view.x, y, view.width, EditorParts.FieldHeight);

            int method = EditorParts.Segments(EditorParts.Column(row, 0, 2), "method", Methods, clean ? 0 : 1,
                palette);

            if (method >= 0)
                clean = method == 0;

            int parts = EditorParts.Segments(EditorParts.Column(row, 1, 2), "missing parts", Parts,
                restoreParts ? 0 : 1, palette);

            if (parts >= 0)
                restoreParts = parts == 0;

            y = row.yMax + EditorParts.RowGap;

            y = EditorParts.Note(view, y, clean
                ? "Clean cures every injury, disease and chronic condition and keeps their implants."
                : "With side effects leaves the wounds where they are, adds resurrection sickness, and rolls "
                  + "the three chances below.", palette);

            y += EditorParts.RowGap;

            float days = UIGuard.Try("Editor.RotDays",
                () => corpse != null ? CorpseFacts.DaysRotted(corpse) : 0f, 0f, null);

            y = Odds(view, y, days, palette);

            y = Consequences(view, y, context, corpse, palette);

            y += EditorParts.RowGap;

            Rect button = new Rect(view.x, y, Mathf.Min(view.width, 260f), 30f);

            if (Shared.TabParts.Button(button, "Bring " + pawn.LabelShortCap + " back", palette, true, true,
                    "This cannot be undone, and Revert all will not reach it."))
                Raise(context, corpse, days);

            return button.yMax + EditorParts.BlockGap - view.y;
        }

        // ---------------------------------------------------------------------------------------
        // Reading
        // ---------------------------------------------------------------------------------------

        private static float Header(Rect view, float y, EditorContext context, Corpse corpse,
            UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            y = EditorParts.Heading(view, y, "How they died", palette);

            string when = UIGuard.Try<string>("Editor.DiedWhen",
                () => corpse != null
                    ? "Died " + CorpseFacts.AgeOf(corpse).ToStringTicksToPeriodVague() + " ago"
                    : "Died. There is no body on any map.", null, null);

            y = EditorParts.Note(view, y, when, palette, palette.TextSecondary);

            if (corpse == null)
                return y + EditorParts.RowGap;

            string where;
            string note;

            CorpseFacts.Where(corpse, out where, out note);

            string state = CorpseFacts.StageTag(CorpseFacts.StageOf(corpse)).ToLower() + ", "
                           + (CorpseFacts.RotNote(corpse) ?? "unknown");

            y = EditorParts.Note(view, y, where + (note.NullOrEmpty() ? string.Empty : " - " + note)
                                                + ". " + state.CapitalizeFirst() + ".", palette);

            return y + EditorParts.RowGap;
        }

        /// <summary>
        /// The three side effects and the one number behind all of them.
        ///
        /// <b>One figure, not three, which is a departure from the proposal.</b> The mockup showed 2 percent, 1
        /// percent and 7 percent as though the three rolled on different curves. They do not: dementia, blindness
        /// and resurrection psychosis are three identically shaped curves in <c>ResurrectionUtility</c>, so at any
        /// given rot the chance is the same for each. Printing three copies of one number would have implied a
        /// distinction that does not exist.
        /// </summary>
        private static float Odds(Rect view, float y, float days, UIColorPaletteDef palette)
        {
            float chance = Chance(days);

            y = EditorParts.Heading(view, y, "What side effects would roll", palette,
                clean ? "not applied, Clean is selected" : null,
                clean ? palette.TextDisabled : palette.Warning);

            Color colour = clean ? palette.TextDisabled : palette.Warning;

            y = EditorParts.Note(view, y,
                "At " + days.ToString("0.00") + " days of rot, each of dementia, blindness and resurrection "
                + "psychosis has a " + chance.ToStringPercent() + " chance. Resurrection sickness is not a "
                + "chance; With side effects always adds it.", palette, colour);

            y = EditorParts.Note(view, y,
                "At two days rotted that figure is " + Chance(2f).ToStringPercent() + ", and at five days it is "
                + Chance(5f).ToStringPercent() + ".", palette);

            return y + EditorParts.RowGap;
        }

        /// <summary>
        /// Vanilla's own curve, read off the field rather than copied.
        ///
        /// <b>Through reflection, and it falls back to a copy.</b> The three curves are private statics on
        /// <c>ResurrectionUtility</c>, so a hard-coded pair of points here would go quietly wrong the first time
        /// Ludeon retuned them and the panel would print confident nonsense. Read the field when it is there;
        /// when it is not -- renamed, or a mod replaced the class -- fall back to the values as of 1.6 and accept
        /// that they might be stale, which is still better than crashing on a panel whose whole job is arithmetic.
        /// </summary>
        private static SimpleCurve curve;

        private static bool looked;

        private static float Chance(float days)
        {
            return UIGuard.Try("Editor.Odds", () =>
            {
                if (!looked)
                {
                    looked = true;

                    FieldInfo field = typeof(ResurrectionUtility).GetField(
                        "DementiaChancePerRotDaysCurve",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                    curve = field != null ? field.GetValue(null) as SimpleCurve : null;

                    if (curve == null)
                        curve = new SimpleCurve
                        {
                            new CurvePoint(0.1f, 0.02f),
                            new CurvePoint(5f, 0.8f)
                        };
                }

                return Mathf.Clamp01(curve.Evaluate(days));
            }, 0f, null);
        }

        /// <summary>
        /// What comes back with them, stated before it happens.
        ///
        /// Counted off the body rather than described in general terms: eleven injuries and five relationships is
        /// a fact about this pawn, and "some injuries" is not.
        /// </summary>
        private static float Consequences(Rect view, float y, EditorContext context, Corpse corpse,
            UIColorPaletteDef palette)
        {
            Pawn pawn = context.Pawn;

            y = EditorParts.Heading(view, y, "What comes back with them", palette);

            int bad = 0;
            int implants = 0;
            int missing = 0;

            UIGuard.Try("Editor.ResurrectCounts", () =>
            {
                List<Hediff> held = pawn.health.hediffSet.hediffs;

                for (int i = 0; i < held.Count; i++)
                {
                    Hediff hediff = held[i];

                    if (hediff == null || hediff.def == null)
                        continue;

                    if (hediff.def.countsAsAddedPartOrImplant)
                        implants++;
                    else if (hediff is Hediff_MissingPart)
                        missing++;
                    else if (hediff.def.isBad)
                        bad++;
                }
            }, null);

            y = EditorParts.Note(view, y, clean
                ? bad + " conditions clear and " + missing + " missing parts are restored. "
                  + implants + " implants stay."
                : bad + " conditions stay where they are, including whatever killed them. "
                  + (restoreParts
                      ? missing + " missing parts are restored."
                      : missing + " missing parts stay missing."), palette);

            int relations = UIGuard.Try("Editor.ResurrectRelations",
                () => pawn.relations != null ? pawn.relations.DirectRelations.Count : 0, 0, null);

            y = EditorParts.Note(view, y,
                relations + " relationships survive. The memories of their death are cleared from everybody who "
                + "holds one.", palette);

            int gear = 0;

            UIGuard.Try("Editor.ResurrectGear", () =>
            {
                int count;
                int value;

                CorpseFacts.Gear(pawn, out count, out value);

                gear = count;
            }, null);

            if (gear > 0)
                y = EditorParts.Note(view, y,
                    gear + " items are still on the body and stay on it. They will be wearing them when they "
                         + "stand up.", palette);

            return y;
        }

        // ---------------------------------------------------------------------------------------
        // Doing it
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Resurrects, then finishes the job vanilla leaves half done.
        ///
        /// <b>Clean is two steps and has to be.</b> <c>TryResurrect</c> with <c>restoreMissingParts</c> clears
        /// diseases, lethal conditions and fresh injuries and restores lost limbs; it deliberately keeps permanent
        /// scars on intact parts and every chronic condition that no item can cure. The second step is the
        /// health panel's own heal, so "Clean" and "Heal everything" cannot drift apart.
        ///
        /// <b>Recorded as permanent whichever way it goes,</b> including when it fails: a half-completed
        /// resurrection is exactly the state a player most needs the footer to stop claiming is reversible.
        /// </summary>
        private static void Raise(EditorContext context, Corpse corpse, float days)
        {
            Pawn pawn = context.Pawn;

            UIGuard.Try("Editor.Resurrect", () =>
            {
                context.Changes.RecordPermanent("resurrection");

                bool raised;

                if (clean)
                {
                    raised = ResurrectionUtility.TryResurrect(pawn, new ResurrectionParams
                    {
                        restoreMissingParts = restoreParts,
                        gettingScarsChance = 0f,
                        removeDiedThoughts = true
                    });

                    if (raised)
                        EditorState.Heal(context);
                }
                else
                {
                    raised = ResurrectionUtility.TryResurrectWithSideEffects(pawn);
                }

                if (!raised)
                {
                    EditorParts.Warn("The game refused to bring " + pawn.LabelShortCap
                                     + " back. Nothing was changed.");

                    return;
                }

                Messages.Message(pawn.LabelShortCap + " is alive.", pawn, MessageTypeDefOf.PositiveEvent, false);

                EditorParts.Redraw(pawn);

                CorpseRoster.Invalidate();
                GraveRoster.Invalidate();
            }, "The resurrection could not be completed. Check the pawn's health before doing anything else "
               + "with them.");
        }
    }
}
