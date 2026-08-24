using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// One piece of apparel as it is being specified, before it exists.
    ///
    /// <b>A specification rather than a Thing.</b> The browser lets somebody try four materials and three
    /// qualities before committing, and making a real item for each of those would leave a trail of orphaned
    /// things in the save for every one they did not take. Nothing is created until Wear is pressed.
    /// </summary>
    internal sealed class ApparelChoice
    {
        internal ThingDef Def;

        /// <summary>What it is made from, or null for an item that is not made from stuff.</summary>
        internal ThingDef Stuff;

        internal QualityCategory Quality = QualityCategory.Normal;

        /// <summary>Health as a fraction of the item's maximum, so it survives a change of material.</summary>
        internal float Health = 1f;

        /// <summary>The dye, or null to leave it the colour its material gives it.</summary>
        internal Color? Colour;
    }

    /// <summary>
    /// Browsing, making and taking off apparel.
    ///
    /// <b>The Equipment panel had a weapon and an inventory and no apparel at all,</b> which Aaron found on
    /// 2026-08-23. The picker's own documentation had listed apparel as one of the things it served since the
    /// panel was written; nothing ever called it.
    ///
    /// <b>Four properties, because those are the four that make an item what it is.</b> Material, quality, health
    /// and colour: a legendary plasteel duster is a different object from an awful cloth one, and an editor that
    /// could only hand over the default of everything would be a list of names rather than a way to dress
    /// somebody.
    ///
    /// <b>Nothing is filtered out for being a bad idea,</b> the same posture as every other panel here. Apparel a
    /// body cannot wear is listed and marked, because a xenotype with no legs is exactly the case somebody opens
    /// an editor to look at.
    /// </summary>
    internal static class EditorApparel
    {
        private static List<ThingDef> catalogue;

        /// <summary>
        /// Every apparel def in the game, sorted by name and worked out once.
        ///
        /// Nothing is excluded. A def that cannot be worn by this pawn, or at all, still appears with a note --
        /// see <see cref="Refusal"/> -- because a list that quietly omits things cannot be searched for what is
        /// missing.
        /// </summary>
        internal static List<ThingDef> All()
        {
            if (catalogue != null)
                return catalogue;

            catalogue = UIGuard.Try("Editor.ApparelCatalogue", () =>
            {
                List<ThingDef> found = new List<ThingDef>();
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].IsApparel)
                        found.Add(all[i]);
                }

                found.Sort((a, b) => string.Compare(EditorParts.LabelOf(a), EditorParts.LabelOf(b),
                    System.StringComparison.OrdinalIgnoreCase));

                return found;
            }, new List<ThingDef>(), null);

            return catalogue;
        }

        /// <summary>The materials this def may be made from, or an empty list when it is not made from stuff.</summary>
        internal static List<ThingDef> StuffsFor(ThingDef def)
        {
            return UIGuard.Try("Editor.ApparelStuffs", () =>
            {
                List<ThingDef> stuffs = new List<ThingDef>();

                if (def == null || !def.MadeFromStuff)
                    return stuffs;

                foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(def))
                    stuffs.Add(stuff);

                stuffs.Sort((a, b) => string.Compare(EditorParts.LabelOf(a), EditorParts.LabelOf(b),
                    System.StringComparison.OrdinalIgnoreCase));

                return stuffs;
            }, new List<ThingDef>(), null);
        }

        /// <summary>Whether this def carries a quality, which not all apparel does.</summary>
        internal static bool HasQuality(ThingDef def)
        {
            return UIGuard.Try("Editor.ApparelQuality",
                () => def != null && def.HasComp(typeof(CompQuality)), false, null);
        }

        /// <summary>Whether this def can be dyed.</summary>
        internal static bool HasColour(ThingDef def)
        {
            return UIGuard.Try("Editor.ApparelColour",
                () => def != null && def.HasComp(typeof(CompColorable)), false, null);
        }

        /// <summary>
        /// The colours offered, starting with the one the material gives it.
        ///
        /// <b>From the game's own <c>ColorDef</c> list rather than a palette of mine.</b> Those are the dyes the
        /// game recognises, they are what the styling system uses, and a mod that adds a dye appears here without
        /// this file knowing about it. The first swatch is the undyed colour, so there is a way back.
        /// </summary>
        internal static List<Color> Palette(ThingDef def, ThingDef stuff)
        {
            return UIGuard.Try("Editor.ApparelPalette", () =>
            {
                List<Color> colours = new List<Color> { Undyed(def, stuff) };

                List<ColorDef> dyes = DefDatabase<ColorDef>.AllDefsListForReading;

                for (int i = 0; i < dyes.Count && colours.Count < 10; i++)
                {
                    if (!EditorParts.Near(dyes[i].color, colours[0]))
                        colours.Add(dyes[i].color);
                }

                return colours;
            }, new List<Color> { Color.white }, null);
        }

        /// <summary>The colour an item is when nothing has dyed it: its material's, or the def's own.</summary>
        internal static Color Undyed(ThingDef def, ThingDef stuff)
        {
            if (stuff != null && stuff.stuffProps != null)
                return stuff.stuffProps.color;

            return def?.graphicData != null ? def.graphicData.color : Color.white;
        }

        /// <summary>
        /// The maximum health an item of this def and material would have.
        ///
        /// Worked out from the stat rather than from <c>def.BaseMaxHitPoints</c>, because material multiplies it:
        /// a plasteel duster has three times the hit points of a cloth one and a health slider that ignored that
        /// would offer numbers the item cannot hold.
        /// </summary>
        internal static int MaxHealth(ThingDef def, ThingDef stuff)
        {
            return UIGuard.Try("Editor.ApparelMaxHealth",
                () => Mathf.Max(1, Mathf.RoundToInt(def.GetStatValueAbstract(StatDefOf.MaxHitPoints, stuff))),
                1, null);
        }

        /// <summary>What sits on the right of a row in the browser: the layer it goes on.</summary>
        internal static string Note(ThingDef def)
        {
            return UIGuard.Try<string>("Editor.ApparelNote", () =>
            {
                if (def?.apparel == null)
                    return null;

                ApparelLayerDef layer = def.apparel.LastLayer;

                return layer != null ? layer.label : null;
            }, null, null);
        }

        /// <summary>
        /// Why this pawn cannot wear this, or null when they can.
        ///
        /// <b>Checked here rather than left to <c>Wear</c>,</b> which writes a warning to the log and returns. A
        /// player choosing a helmet for something with no head should be told by the row, not by a red line in a
        /// file they will never read.
        /// </summary>
        internal static string Refusal(Pawn pawn, ThingDef def)
        {
            return UIGuard.Try<string>("Editor.ApparelRefusal", () =>
            {
                if (pawn == null || def == null)
                    return null;

                if (!ApparelUtility.HasPartsToWear(pawn, def))
                    return "this body cannot wear it";

                return null;
            }, null, null);
        }

        /// <summary>
        /// Makes the item and puts it on, remembering what it displaced.
        ///
        /// <b>Conflicting apparel is removed rather than dropped, and kept.</b> <c>Wear</c> with
        /// <c>dropReplacedApparel</c> false calls <c>Remove</c>, which takes an item out of the worn container
        /// without destroying it -- so the displaced pieces can be handed back if this is reverted. Dropping them
        /// would put them on the floor of whatever map the pawn is standing on, which is not something an edit
        /// should do and not something a revert could undo.
        /// </summary>
        internal static void Wear(EditorContext context, ApparelChoice choice)
        {
            Pawn pawn = context?.Pawn;

            if (pawn == null || choice?.Def == null)
                return;

            UIGuard.Try("Editor.Wear", () =>
            {
                if (pawn.apparel == null)
                    return;

                Apparel made = ThingMaker.MakeThing(choice.Def, choice.Stuff) as Apparel;

                if (made == null)
                    return;

                CompQuality quality = made.TryGetComp<CompQuality>();

                if (quality != null)
                    quality.SetQuality(choice.Quality, ArtGenerationContext.Colony);

                // After the quality, because quality moves the maximum: a legendary item has more hit points
                // than a normal one, so a health set first would be clamped against the wrong ceiling.
                made.HitPoints = Mathf.Clamp(Mathf.RoundToInt(made.MaxHitPoints * choice.Health), 1,
                    made.MaxHitPoints);

                if (choice.Colour.HasValue)
                {
                    CompColorable colourable = made.TryGetComp<CompColorable>();

                    if (colourable != null)
                        colourable.SetColor(choice.Colour.Value);
                }

                List<Apparel> displaced = new List<Apparel>();
                List<Apparel> worn = pawn.apparel.WornApparel;

                for (int i = 0; i < worn.Count; i++)
                {
                    if (!ApparelUtility.CanWearTogether(made.def, worn[i].def, pawn.RaceProps.body))
                        displaced.Add(worn[i]);
                }

                pawn.apparel.Wear(made, false);

                context.Changes.Record("apparel", () =>
                {
                    pawn.apparel.Remove(made);

                    made.Destroy();

                    for (int i = 0; i < displaced.Count; i++)
                        pawn.apparel.Wear(displaced[i], false);
                });

                EditorParts.Redraw(pawn);
            }, "That apparel could not be put on. Nothing has been changed.");
        }

        /// <summary>Takes a piece off, keeping it so the change can be reverted.</summary>
        internal static void Strip(EditorContext context, Apparel apparel)
        {
            Pawn pawn = context?.Pawn;

            if (pawn == null || apparel == null)
                return;

            UIGuard.Try("Editor.Strip", () =>
            {
                if (pawn.apparel == null)
                    return;

                pawn.apparel.Remove(apparel);

                context.Changes.Record("apparel", () => pawn.apparel.Wear(apparel, false));

                EditorParts.Redraw(pawn);
            }, "That apparel could not be taken off. Nothing has been changed.");
        }
    }
}
