using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>What the pane has to work with, which decides whether there is a body at all.</summary>
    internal enum InspectBodyKind
    {
        /// <summary>A zone, a plan, a multiple selection, or something that asked to be shown as text only.</summary>
        None,

        Pawn,

        Thing
    }

    /// <summary>
    /// Which body the pane draws, and the two readings the header needs.
    ///
    /// <b>A dispatcher and nothing else.</b> Every body is its own file, and the only thing they share is the
    /// contract here: given a column and a palette, draw and say how tall you came out. Nothing predicts a
    /// height and nothing writes outside the rect it is handed.
    /// </summary>
    internal static class InspectBodies
    {
        /// <summary>The gutter between the two columns.</summary>
        internal const float ColumnGap = 14f;

        /// <summary>Below this a second column has nothing but ellipses in it, so there is only one.</summary>
        private const float TwoColumnWidth = 400f;

        /// <summary>
        /// The pawn this selection is about, which is not always the thing selected.
        ///
        /// <b>A corpse is a person.</b> It carries the health, gear, social, bio and log tabs and they all read
        /// the pawn inside it -- <c>ITab_Pawn_Gear.SelPawnForGear</c> unwraps a corpse in one line and every
        /// other pawn tab does the same. Reading only <c>thing as Pawn</c> here is what left a dead raider with
        /// a single hit points bar while all five of his tabs still had everything to say about him.
        /// </summary>
        internal static Pawn PawnOf(Thing thing)
        {
            Pawn pawn = thing as Pawn;

            if (pawn != null)
                return pawn;

            Corpse corpse = thing as Corpse;

            return corpse != null ? corpse.InnerPawn : null;
        }

        /// <summary>What kind of body, if any, this selection gets.</summary>
        internal static InspectBodyKind KindOf(Thing thing)
        {
            if (thing == null)
                return InspectBodyKind.None;

            // A thing that says it wants only its inspect string means it, and a mod that sets this is usually
            // hiding something the player is not supposed to be reading yet.
            if (thing.def != null && thing.def.onlyShowInspectString)
                return InspectBodyKind.None;

            return PawnOf(thing) != null ? InspectBodyKind.Pawn : InspectBodyKind.Thing;
        }

        /// <summary>
        /// Splits a column in two, or hands back one that fills the width.
        ///
        /// <paramref name="right"/> comes back with no width when the pane is too narrow to divide, which every
        /// body tests before drawing into it: one column of readable rows beats two columns of ellipses.
        /// </summary>
        internal static void Columns(Rect view, out Rect left, out Rect right)
        {
            if (view.width < TwoColumnWidth)
            {
                left = view;
                right = new Rect(view.xMax, view.y, 0f, view.height);

                return;
            }

            float half = Mathf.Floor((view.width - ColumnGap) * 0.5f);

            left = new Rect(view.x, view.y, half, view.height);
            right = new Rect(view.x + half + ColumnGap, view.y, view.width - half - ColumnGap, view.height);
        }

        /// <summary>Whether a column is wide enough to have been given anything.</summary>
        internal static bool Live(Rect column)
        {
            return column.width >= 60f;
        }

        /// <summary>
        /// The line under the name: what this pawn is doing right now.
        ///
        /// <b>The job report rather than the whole inspect string,</b> which is the one line somebody selecting a
        /// colonist is looking for. The rest of what vanilla writes is still at the bottom of the pane, unchanged.
        /// </summary>
        internal static string Subline(Pawn pawn)
        {
            if (pawn == null)
                return null;

            return UIGuard.Try<string>("Inspector.Subline", () =>
            {
                if (pawn.Dead)
                    return "Dead".Translate().CapitalizeFirst();

                MentalState mental = pawn.MentalState;

                if (mental != null)
                    return mental.InspectLine;

                if (pawn.jobs != null && pawn.jobs.curDriver != null)
                {
                    string report = pawn.jobs.curDriver.GetReport();

                    if (!report.NullOrEmpty())
                        return report.CapitalizeFirst();
                }

                return null;
            }, null, null);
        }

        /// <summary>
        /// The dim qualifier after the name: what this pawn is, and how old.
        ///
        /// Two forms, because the useful answer differs. For a person it is their short backstory title and their
        /// age, which is how a colonist is told apart from the other eleven at a glance. For an animal it is the
        /// species and the age, since the name is a nickname and the species is the fact.
        ///
        /// <b>The sex is not here any more.</b> It moved to a glyph before the name in 14158, and saying it twice
        /// on one line spends the qualifier's width -- which was always tight -- on something the reader has
        /// already been told. See <see cref="GenderGlyphs"/>.
        /// </summary>
        internal static string Qualifier(Pawn pawn)
        {
            if (pawn == null)
                return null;

            return UIGuard.Try("Inspector.Qualifier", () =>
            {
                if (pawn.RaceProps != null && pawn.RaceProps.Humanlike)
                {
                    string age = pawn.ageTracker != null
                        ? pawn.ageTracker.AgeBiologicalYears.ToString()
                        : null;

                    return JoinAll(pawn.story != null ? pawn.story.TitleShortCap : null, age, FactionOf(pawn));
                }

                string kind = pawn.KindLabel.NullOrEmpty() ? pawn.def.label : pawn.KindLabel;
                string years = pawn.ageTracker != null
                    ? pawn.ageTracker.AgeBiologicalYearsFloat.ToString("0.0") + "y"
                    : null;

                return JoinAll(kind.NullOrEmpty() ? null : kind.CapitalizeFirst(), years, FactionOf(pawn));
            }, null, null);
        }

        /// <summary>
        /// Whose side this one is on, for the qualifier line.
        ///
        /// <b>Absent for our own.</b> Every colonist, every colony animal and every prisoner we hold belongs to
        /// the player, so printing it would put the same six words on nearly every row in the game and teach the
        /// eye to skip the place where a visitor's faction appears. The colour and the standing are on the Social
        /// body, which is where a faction is a fact rather than a label.
        /// </summary>
        private static string FactionOf(Pawn pawn)
        {
            Faction faction = pawn.Faction;

            return faction == null || faction.IsPlayer ? null : faction.Name;
        }

        /// <summary>Joins the parts of a qualifier that are actually there, skipping the ones that are not.</summary>
        private static string Join(string first, string second, string separator = " - ")
        {
            if (first.NullOrEmpty())
                return second;

            return second.NullOrEmpty() ? first : first + separator + second;
        }

        /// <summary>Three-part form of the same, so a missing middle does not leave two separators together.</summary>
        private static string JoinAll(string first, string second, string third)
        {
            return Join(Join(first, second), third);
        }

        /// <summary>
        /// Draws whichever body is selected, and returns how tall it came out.
        ///
        /// <b>Every body is behind the guard individually.</b> A throw in the gear reading should cost the gear
        /// body and leave the rest of the pane, the header and the inspect string working, which is what
        /// <c>UIGuard.Try</c> with a per-body site gives: the failure notice names the body that failed.
        /// </summary>
        internal static float Draw(Rect view, Thing thing, Pawn pawn, InspectBodyKind kind,
            UIColorPaletteDef palette)
        {
            if (kind == InspectBodyKind.Thing)
                return UIGuard.Try("Inspector.ThingBody",
                    () => InspectThingBody.Draw(view, thing, palette), 0f,
                    "The inspect pane shows only its description text for this thing.");

            if (kind != InspectBodyKind.Pawn || pawn == null)
                return 0f;

            switch (InspectPaneState.Selected)
            {
                case InspectBody.Health:
                    return UIGuard.Try("Inspector.HealthBody",
                        () => InspectHealthBody.Draw(view, pawn, palette), 0f,
                        "The inspect pane's health body is not shown. Its own tab still works.");

                case InspectBody.Gear:
                    return UIGuard.Try("Inspector.GearBody",
                        () => InspectGearBody.Draw(view, pawn, palette), 0f,
                        "The inspect pane's gear body is not shown. Its own tab still works.");

                case InspectBody.Social:
                    return UIGuard.Try("Inspector.SocialBody",
                        () => InspectSocialBody.Draw(view, pawn, palette), 0f,
                        "The inspect pane's social body is not shown. Its own tab still works.");

                case InspectBody.Needs:
                    return UIGuard.Try("Inspector.NeedsBody",
                        () => InspectNeedsBody.Draw(view, pawn, palette), 0f,
                        "The inspect pane's needs body is not shown. Its own tab still works.");

                case InspectBody.Bio:
                    return UIGuard.Try("Inspector.BioBody",
                        () => InspectBioBody.Draw(view, pawn, palette), 0f,
                        "The inspect pane's bio body is not shown. Its own tab still works.");

                case InspectBody.Log:
                    return UIGuard.Try("Inspector.LogBody",
                        () => InspectLogBody.Draw(view, pawn, palette), 0f,
                        "The inspect pane's log body is not shown. Its own tab still works.");

                default:
                    return UIGuard.Try("Inspector.OverviewBody",
                        () => InspectOverview.DrawPawn(view, pawn, palette), 0f,
                        "The inspect pane shows only its description text for this pawn.");
            }
        }
    }
}
