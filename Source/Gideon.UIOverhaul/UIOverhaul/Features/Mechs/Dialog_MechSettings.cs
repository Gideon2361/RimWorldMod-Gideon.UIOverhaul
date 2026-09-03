using System.Collections.Generic;
using System.Linq;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Mechs
{
    /// <summary>
    /// The mech tab's own settings: hibernation, and this colony's mech color.
    ///
    /// <b>A window rather than a popover,</b> because that is how this mod opens everything else:
    /// <c>Dialog_ResearchBands</c>, <c>Dialog_StandingOrder</c>, <c>Dialog_AddOperation</c>. There is no
    /// popover idiom here and this is not the place to invent one.
    ///
    /// <b>Two settings of different kinds, which is why the dialog exists at all.</b> Hibernation is a mod
    /// preference and lives in <see cref="UIOverhaulSettingsFile"/>, so it also appears in the options
    /// dialog alongside everything else. The mech color is <c>Faction.AllegianceColor</c>, which is save
    /// data, so the options dialog is the wrong home for it however tidy the symmetry would look. RimWorld
    /// puts it on a 240 by 32 button over the top left of its own table; it belongs here.
    ///
    /// <b>The zone line is the point of having the toggle on the tab.</b>
    /// <c>AreaManager.GetLabeled</c> compares <c>Area.Label == s</c> with no trimming and no case folding,
    /// so a player who typed the name differently gets no zone and a setting that silently does half of what
    /// it says. The options dialog cannot see a map. This can.
    /// </summary>
    public class Dialog_MechSettings : Window
    {
        private const float Width = 460f;

        private const float Height = 320f;

        private List<Color> swatches = new List<Color>();

        public Dialog_MechSettings()
        {
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(Width, Height); }
        }

        protected override float Margin
        {
            get { return 16f; }
        }

        public override void PreOpen()
        {
            base.PreOpen();

            // Vanilla's own list, gathered the way its MainTabWindow_Mechs gathers it: every color def plus
            // every visible faction's, distinct and sorted. Built on open rather than per frame, because it
            // walks two def databases.
            UIGuard.Try("Mechs.Swatches", () =>
            {
                swatches = DefDatabase<ColorDef>.AllDefsListForReading.Select(def => def.color)
                    .Concat(Find.FactionManager.AllFactionsVisible.Select(faction => faction.Color))
                    .Distinct()
                    .ToList();

                swatches.SortByColor(color => color);
            }, "The mech color picker has no palette this session. Everything else in this dialog works.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            float y = inRect.y;

            TabParts.RowLabel(new Rect(inRect.x, y, inRect.width, 26f), "Mech settings",
                MechsFaces.AccentOf(palette), MechsFaces.Display, MechsFaces.Size.DialogTitle);

            y += 28f;

            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);

            y += 12f;

            y = Hibernation(inRect, y, palette);

            y += 14f;

            Color(inRect, y, palette);
        }

        // -------------------------------------------------------------------------------------------
        // Hibernation
        // -------------------------------------------------------------------------------------------

        private static float Hibernation(Rect inRect, float y, UIColorPaletteDef palette)
        {
            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null)
                return y;

            bool value = settings.mechHibernation;
            Rect row = new Rect(inRect.x, y, inRect.width, 26f);

            if (UICheckboxControl.Draw(row, ref value, palette, "Enable mech hibernation", Tooltip))
            {
                settings.mechHibernation = value;

                settings.Save();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += 28f;

            Color tint;
            string found = Zone(palette, out tint);

            TabParts.RowLabel(new Rect(inRect.x + 24f, y, inRect.width - 24f, 16f), found, tint,
                MechsFaces.Mono, MechsFaces.Size.Caption);

            y += 20f;

            return TabParts.Note(inRect, y,
                "Idle mechs wander between jobs, and every wander ends in a full re-scan for work. "
                + "Hibernating replaces the wander with one long wait. It is a nap rather than a coma: "
                + "danger, being drafted, and work becoming available all end it, and a hibernating mech "
                + "still shoots at anything that walks up to it.",
                palette, MechsFaces.Body, MechsFaces.Size.Prose);
        }

        /// <summary>The exact tooltip, as specified. Copy is not this file's to rewrite.</summary>
        private const string Tooltip =
            "When mechs have no work to do, they will save performance by hibernating for 1200 ticks "
            + "instead of polling the job board every tick.  Mechs will hibernate in-place, but you can "
            + "create an area named MechHibernateZone and they will go there to hibernate instead.";

        /// <summary>
        /// What the setting actually found on this map, and the color to say it in.
        ///
        /// Reported rather than assumed, because the name is matched exactly: <c>Mech Hibernate Zone</c> and
        /// <c>mechhibernatezone</c> both find nothing, and a typo that looks like a bug is worse than a
        /// missing feature.
        /// </summary>
        private static string Zone(UIColorPaletteDef palette, out Color tint)
        {
            Map map = Find.CurrentMap;

            if (map == null || map.areaManager == null)
            {
                tint = palette.TextDisabled;

                return "NO MAP TO CHECK";
            }

            Area zone = map.areaManager.GetLabeled(JobGiver_MechHibernate.ZoneLabel);

            if (zone == null)
            {
                tint = palette.Warning;

                return "NO " + JobGiver_MechHibernate.ZoneLabel.ToUpperInvariant()
                       + " ON THIS MAP  -  HIBERNATING IN PLACE";
            }

            tint = palette.Success;

            return JobGiver_MechHibernate.ZoneLabel.ToUpperInvariant() + "  -  " + zone.TrueCount + " CELLS";
        }

        // -------------------------------------------------------------------------------------------
        // Mech color
        // -------------------------------------------------------------------------------------------

        private void Color(Rect inRect, float y, UIColorPaletteDef palette)
        {
            Faction player = Find.FactionManager == null ? null : Find.FactionManager.OfPlayer;

            if (player == null)
                return;

            TabParts.RowLabel(new Rect(inRect.x, y, inRect.width - 40f, 22f), "Mech color",
                palette.TextPrimary, MechsFaces.Condensed, MechsFaces.Size.RailName);

            Rect swatch = new Rect(inRect.xMax - 34f, y + 2f, 30f, 18f);

            Widgets.DrawBoxSolid(swatch, player.AllegianceColor);
            Widgets.DrawBox(swatch);

            y += 24f;

            TabParts.RowLabel(new Rect(inRect.x, y, inRect.width, 16f),
                "USED BY EVERY MECH THIS COLONY BUILDS", palette.TextDisabled, MechsFaces.Mono,
                MechsFaces.Size.Caption);

            y += 22f;

            if (!TabParts.Button(new Rect(inRect.x, y, 200f, 26f), "Choose mech color", palette))
                return;

            SoundDefOf.Click.PlayOneShotOnCamera();

            // Vanilla's own dialog, with vanilla's own effect: set the faction's allegiance color and dirty
            // every mech's portrait. Copied from MainTabWindow_Mechs because that is the behaviour being
            // moved rather than replaced.
            UIGuard.Try("Mechs.ChooseColor", () => Find.WindowStack.Add(new Dialog_ChooseColor(
                "ChooseMechAccentColor".Translate(), player.AllegianceColor, swatches, chosen =>
                {
                    player.AllegianceColor = chosen;

                    foreach (Pawn mech in MechanitorUtility.MechsInPlayerFaction())
                        PortraitsCache.SetDirty(mech);
                })), "The color picker could not be opened.");
        }
    }
}
