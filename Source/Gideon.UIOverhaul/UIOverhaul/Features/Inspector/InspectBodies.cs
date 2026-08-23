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
        /// colonist is looking for. Since 14160 it is also the only part of that string a pawn's pane still
        /// shows, so if anything else in there turns out to be worth keeping it belongs in a block of its own
        /// rather than appended here. See <see cref="InspectPaneFrame"/>.
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
        /// The dim qualifier after the name: whose this one is, and how old.
        ///
        /// Two forms, because the useful answer differs. For a person it is their faction and their age. For an
        /// animal it is the species and the age, since the name is a nickname and the species is the fact.
        ///
        /// <b>The backstory title is not here any more.</b> Asked for 2026-08-22: the header already reads
        /// "Brett, Handyman", so a small grey "Handyman" beside it was the same word twice in one line. The
        /// faction takes the space, coloured the way RimWorld colours a faction everywhere else, which is a
        /// thing the header never said and the deleted footer used to. A pawn with no faction at all -- a wild
        /// man, an escaped mechanoid -- falls back to the title, since something is better there than nothing.
        ///
        /// <b>Our own faction is named now too.</b> It used to be suppressed on the grounds that it would be the
        /// same six words on nearly every pane; that was right when the alternative was the title, and it is
        /// wrong now that the alternative is a blank. A colony has a name and this is where it belongs.
        ///
        /// <b>The sex is not here either.</b> It moved to a glyph before the name in 14158. See
        /// <see cref="GenderGlyphs"/>.
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

                    string whose = FactionOf(pawn)
                                   ?? Dim(pawn.story != null ? pawn.story.TitleShortCap.ToString() : null);

                    return Join(whose, Dim(age), Dim(" - "));
                }

                string kind = pawn.KindLabel.NullOrEmpty() ? pawn.def.label : pawn.KindLabel;
                string years = pawn.ageTracker != null
                    ? pawn.ageTracker.AgeBiologicalYearsFloat.ToString("0.0") + "y"
                    : null;

                string separator = Dim(" - ");

                return Join(
                    Join(Dim(kind.NullOrEmpty() ? null : kind.CapitalizeFirst()), Dim(years), separator),
                    WildFactionOf(pawn), separator);
            }, null, null);
        }

        /// <summary>
        /// Marks the parts of the qualifier that are furniture.
        ///
        /// <b>Every part carries its own colour now, and the header draws the line white.</b> IMGUI multiplies a
        /// colour tag by <c>GUI.color</c>, so a faction name drawn under the dim grey the qualifier used to be
        /// set to came out as a muddy version of the faction's colour rather than the colour. Setting the line to
        /// white and dimming the age and species here gives each half the shade it is supposed to have.
        /// </summary>
        private static string Dim(string text)
        {
            return text.NullOrEmpty() ? text : text.Colorize(UIColorPaletteDef.Active.TextDisabled);
        }

        /// <summary>
        /// Whose side this one is on, coloured.
        ///
        /// <b>Through <c>NameColored</c> and its own resolver rather than a colour of ours.</b> That is the call
        /// the deleted inspect string used, so the name comes out in exactly the shade the player is used to
        /// seeing a faction in, and it stays right if RimWorld ever changes how it distinguishes an ally from an
        /// enemy. The resolved string carries a colour tag, which is precisely what <c>UIRichText</c> exists to
        /// measure and shorten safely.
        /// </summary>
        private static string FactionOf(Pawn pawn)
        {
            Faction faction = pawn.Faction;

            return faction == null ? null : faction.NameColored.Resolve();
        }

        /// <summary>
        /// The same, but silent about our own.
        ///
        /// An animal's line already leads with its species, so adding the colony's name to every muffalo would
        /// be the same six words down the whole animals list. A visiting caravan's pack beast is the case worth
        /// marking, and that is the only case this prints.
        /// </summary>
        private static string WildFactionOf(Pawn pawn)
        {
            Faction faction = pawn.Faction;

            return faction == null || faction.IsPlayer ? null : faction.NameColored.Resolve();
        }

        /// <summary>Joins the parts of a qualifier that are actually there, skipping the ones that are not.</summary>
        private static string Join(string first, string second, string separator = " - ")
        {
            if (first.NullOrEmpty())
                return second;

            return second.NullOrEmpty() ? first : first + separator + second;
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
