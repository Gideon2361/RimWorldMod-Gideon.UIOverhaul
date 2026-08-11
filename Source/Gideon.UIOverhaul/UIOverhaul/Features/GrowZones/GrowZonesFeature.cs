using Gideon.UIOverhaul.Features.GrowZones.UI;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    /// <summary>
    /// The growing-zones feature, ported from Growing Zones Plus.
    ///
    /// Growing Zones Plus carried its own <c>Verse.Mod</c> subclass. That could not come across:
    /// RimWorld instantiates every Mod subclass it finds, so a second one in this assembly would give
    /// the mod two entries in the settings list and would call PatchAll twice. What that class actually
    /// held -- the settings instance and the settings page -- lives here instead, and
    /// <see cref="UIOverhaulMod"/> owns the single Mod subclass and hands this its settings.
    ///
    /// Its Harmony patches needed nothing done to them. PatchAll resolves the calling assembly rather
    /// than a namespace, so the patches under Features/GrowZones/Patches are found by the call
    /// UIOverhaulMod already makes.
    /// </summary>
    public static class GrowZonesFeature
    {
        /// <summary>
        /// Settings for this feature, assigned by <see cref="UIOverhaulMod"/> during startup. Null
        /// until then, and every reader checks -- a patch can run before the Mod constructor has
        /// finished on a bad load, and the answer we want in that case is vanilla behavior.
        /// </summary>
        public static GzpSettings Settings { get; internal set; }

        private const string ExperimentalBody =
            "Unfinished features that need more testing. They can misbehave in ways that are hard "
            + "to undo, so back up your save before turning one on, and please report anything that "
            + "goes wrong.";

        /// <summary>Draws the growing-zone section of the mod's settings page.</summary>
        public static void DoSettingsContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, GzpPalette.BG);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect.ContractedBy(10f));

            Color previous = GUI.color;
            GUI.color = GzpPalette.Stat;

            listing.Gap(6f);
            DrawExperimentalBanner(listing);
            listing.Gap(8f);

            if (Settings != null)
            {
                GzpPalette.CheckboxRow(listing.GetRect(30f),
                    "Allow grow zones to be drawn anywhere",
                    ref Settings.allowZonesAnywhere,
                    "Lets a growing zone be drawn or expanded on any cell, ignoring the fertility "
                    + "minimum and things that normally refuse to share a cell with a zone -- walls and "
                    + "buildings included.\n\n"
                    + "Still refused: undiscovered ground, the reserved band around the map edge, and "
                    + "open space on an orbital map, which has nothing to grow on.\n\n"
                    + "This only changes where a zone may be drawn. Plants that cannot survive the "
                    + "terrain will still fail to grow, and a zone drawn over a wall will render on "
                    + "top of it.");
            }

            listing.End();
            GUI.color = previous;
        }

        /// <summary>
        /// The experimental heading as a notice banner, matching the caution treatment the add-bill
        /// window gives an unconfirmed hazard: the striped notice texture washed orange, a yellow
        /// stripe down the left edge and a thin yellow border around the whole block.
        /// </summary>
        private static void DrawExperimentalBanner(Listing_Standard listing)
        {
            const float padX = 12f;
            const float padY = 10f;
            const float stripe = 3f;
            const float titleHeight = 30f;

            float textX = padX + stripe;
            float textWidth = listing.ColumnWidth - textX - padX;

            Text.Font = GameFont.Small;
            float bodyHeight = Text.CalcHeight(ExperimentalBody, textWidth);

            Rect r = listing.GetRect(padY * 2f + titleHeight + bodyHeight);
            GzpPalette.NoticePanel(r, GzpPalette.Warn, GzpPalette.NoticeOrange);

            Color previous = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Warn;
            Widgets.Label(new Rect(r.x + textX, r.y + padY - 4f, textWidth, titleHeight),
                "Experimental Settings");

            Text.Font = GameFont.Small;
            GUI.color = GzpPalette.TextDim;
            Widgets.Label(new Rect(r.x + textX, r.y + padY + titleHeight - 2f, textWidth, bodyHeight),
                ExperimentalBody);

            GUI.color = previous;
        }
    }
}
