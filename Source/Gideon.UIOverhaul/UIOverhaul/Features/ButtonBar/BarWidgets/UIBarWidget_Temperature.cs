using Gideon.UIFramework.Defs;
using RimWorld;
using UnityEngine;
using Verse;
using Gideon.UIOverhaul.Shared;

namespace Gideon.UIOverhaul.Features.ButtonBar.BarWidgets
{
    /// <summary>
    /// The current map's outdoor temperature.
    ///
    /// <b>Outdoors, always.</b> Vanilla's readout reports the room under the cursor and only falls back to
    /// the outdoor temperature when there is none, which is the right behavior for a readout you consult
    /// while pointing at something. On the bar it would be wrong: the number would change every time the
    /// pointer crossed a wall, so a glance at the bar would tell you about wherever the mouse happened to be
    /// rather than about the weather. The outdoor temperature is the one that is worth a permanent slot.
    /// </summary>
    public class UIBarWidget_Temperature : UIBarWidgetWorker
    {
        private string cached;
        private int cachedDegrees = int.MinValue;

        /// <summary>Null until the first reading, since the enum has no value meaning "not yet read".</summary>
        private TemperatureDisplayMode? cachedMode;

        /// <summary>
        /// The comfortable band for a human, read once. Cached because it is a stat lookup and it cannot
        /// change during a session, and read from the def rather than hardcoded so a mod that shifts human
        /// tolerances shifts the coloring with it.
        /// </summary>
        private static float comfyMin = float.NaN;

        private static float comfyMax;

        protected override bool ShouldShow => Find.CurrentMap != null;

        protected override float MeasureWidth()
        {
            return TextWidth(Reading()) + 16f;
        }

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            string reading = Reading();
            if (reading.NullOrEmpty())
                return;

            DrawReadout(rect, reading, ColorFor(palette), Tooltip());
        }

        /// <summary>
        /// Rebuilt only when the displayed whole number changes, the way vanilla caches the same string.
        /// The underlying temperature is a float that moves continuously, so comparing it directly would
        /// rebuild every frame for a readout that shows no decimals.
        /// </summary>
        private string Reading()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return cached ?? "";

            float celsius = map.mapTemperature.OutdoorTemp;
            int degrees = Mathf.RoundToInt(GenTemperature.CelsiusTo(celsius, Prefs.TemperatureMode));

            if (cached == null || degrees != cachedDegrees || cachedMode != Prefs.TemperatureMode)
            {
                cached = TemperatureText.Of(celsius);
                cachedDegrees = degrees;
                cachedMode = Prefs.TemperatureMode;
            }

            return cached;
        }

        /// <summary>
        /// Blue below the comfortable band, red above it, plain text inside it.
        ///
        /// The point of a temperature on the bar is noticing when it matters, and it matters exactly when
        /// colonists outside will start taking hypothermia or heatstroke.
        /// </summary>
        private static Color ColorFor(UIColorPaletteDef palette)
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return palette.TextSecondary;

            EnsureComfyRange();

            float celsius = map.mapTemperature.OutdoorTemp;

            if (celsius < comfyMin)
                return palette.Info;

            return celsius > comfyMax ? palette.Danger : palette.TextSecondary;
        }

        private static string Tooltip()
        {
            EnsureComfyRange();

            return "Outdoor temperature.\n\nColonists are comfortable between "
                   + TemperatureText.Of(comfyMin) + " and " + TemperatureText.Of(comfyMax)
                   + ".";
        }

        private static void EnsureComfyRange()
        {
            if (!float.IsNaN(comfyMin))
                return;

            // Read off the human def rather than written down here. The numbers are 16 and 26 in vanilla, but
            // they are stats, and a mod that changes what people can stand should change what this calls
            // cold.
            comfyMin = ThingDefOf.Human.GetStatValueAbstract(StatDefOf.ComfyTemperatureMin);
            comfyMax = ThingDefOf.Human.GetStatValueAbstract(StatDefOf.ComfyTemperatureMax);
        }
    }
}
