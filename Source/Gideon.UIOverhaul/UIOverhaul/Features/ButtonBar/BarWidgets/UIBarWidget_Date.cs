using System.Text;
using Gideon.UIFramework.Defs;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar.BarWidgets
{
    /// <summary>
    /// The colony date and hour, on one line.
    ///
    /// <b>One line, not three.</b> Vanilla's date readout stacks the hour, the date and the season in a 78px
    /// column, which the bar has no room for. <c>GenDate.DateFullStringWithHourAt</c> is the same information
    /// in one translated string, and the season moves into the tooltip along with the quadrum calendar.
    ///
    /// <b>Local time, not game time.</b> Everything here goes through the current map's longitude, as the
    /// vanilla readout does, so the hour shown is the hour where the colony is rather than an absolute tick
    /// count. Two colonies on opposite sides of the planet do not read the same clock.
    /// </summary>
    public class UIBarWidget_Date : UIBarWidgetWorker
    {
        /// <summary>Tooltip id, so the tip does not flicker as the string is rebuilt.</summary>
        private const int TooltipId = 0x71DE_0A7E;

        private string cached;
        private int cachedHour = int.MinValue;
        private float cachedLongitude = float.NaN;

        protected override bool ShouldShow => Location.HasValue;

        protected override float MeasureWidth()
        {
            Vector2? location = Location;
            return location.HasValue ? TextWidth(Reading(location.Value)) + 16f : 0f;
        }

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            Vector2? location = Location;
            if (!location.HasValue)
                return;

            DrawReadout(rect, Reading(location.Value), palette.TextSecondary);

            // Built only on hover. It is four translated lines and a StringBuilder, which is not something
            // to do every frame for a tooltip that is usually not being looked at.
            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, new TipSignal(Tooltip(location.Value), TooltipId));
        }

        /// <summary>
        /// The date string, rebuilt when the hour turns over or the colony's longitude changes.
        ///
        /// Keyed on the hour rather than compared against the last string: the string is the expensive part,
        /// and an in-game hour is 2500 ticks, so this rebuilds a few times a minute at normal speed instead
        /// of sixty times a second.
        /// </summary>
        private string Reading(Vector2 longLat)
        {
            TickManager ticks = Find.TickManager;
            if (ticks == null)
                return cached ?? "";

            int absTicks = ticks.TicksAbs;
            int hour = absTicks / GenDate.TicksPerHour;

            if (cached == null || hour != cachedHour || !Mathf.Approximately(longLat.x, cachedLongitude))
            {
                cached = GenDate.DateFullStringWithHourAt(absTicks, longLat);
                cachedHour = hour;
                cachedLongitude = longLat.x;
            }

            return cached;
        }

        /// <summary>
        /// Vanilla's own date tooltip, rather than one of ours: it is already translated, players already
        /// know it, and it carries the quadrum-to-season calendar that this widget has no room to show.
        /// </summary>
        private static string Tooltip(Vector2 longLat)
        {
            TickManager ticks = Find.TickManager;
            if (ticks == null)
                return "";

            int absTicks = ticks.TicksAbs;

            StringBuilder quadrums = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                Quadrum quadrum = (Quadrum) i;
                quadrums.AppendLine(quadrum.Label() + " - " + quadrum.GetSeason(longLat.y).LabelCap());
            }

            return "DateReadoutTip".Translate(
                GenDate.DaysPassed,
                GenDate.DaysPerQuadrum,
                GenDate.Season(absTicks, longLat).LabelCap(),
                GenDate.DaysPerSeason,
                GenDate.Quadrum(absTicks, longLat.x).Label(),
                quadrums.ToString());
        }

        /// <summary>
        /// Longitude and latitude to read the date at: the current map's tile, or the selected world tile
        /// when there is no map.
        ///
        /// The map is preferred even while the world view is open, unlike vanilla's readout which follows the
        /// selection. On the bar that would mean the date changing as the player clicked around the planet,
        /// and a readout beside the tabs should be about the colony rather than about the cursor.
        /// </summary>
        private static Vector2? Location
        {
            get
            {
                if (Find.World == null)
                    return null;

                WorldGrid grid = Find.WorldGrid;
                if (grid == null)
                    return null;

                Map map = Find.CurrentMap;
                if (map != null)
                    return grid.LongLatOf(map.Tile);

                WorldSelector selector = Find.WorldSelector;
                if (selector != null && selector.SelectedTile.Valid)
                    return grid.LongLatOf(selector.SelectedTile);

                return null;
            }
        }
    }
}
