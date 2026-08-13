using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar.BarWidgets
{
    /// <summary>
    /// The current map's weather.
    ///
    /// <b>Perceived, not actual.</b> <c>CurWeatherPerceived</c> is what the weather manager reports through a
    /// transition: weather changes over 4000 ticks, and during that time the actual <c>curWeather</c> has
    /// already flipped while the sky and the sound are still arriving. The perceived value is the one that
    /// agrees with what the player is looking at, which is why vanilla's own readout uses it too.
    ///
    /// <b>Hidden on pocket maps</b>, as vanilla hides its weather line there. A pocket map has a weather
    /// manager because every map does, but it has no sky, so reporting "clear" for the inside of an
    /// undercave would be inventing information.
    ///
    /// <b>Icon and name.</b> <c>WeatherDef</c> carries no art of its own, so the glyph comes from
    /// <see cref="UIBarGlyphs"/>, which generates one per kind of weather rather than shipping art files or
    /// borrowing another mod's set. A mod that wants drawn art for its own weather can supply a texture at
    /// <c>UI/WeatherIcons/&lt;defName&gt;</c> and it wins.
    /// </summary>
    public class UIBarWidget_Weather : UIBarWidgetWorker
    {
        protected override bool ShouldShow
        {
            get
            {
                Map map = Find.CurrentMap;
                return map != null && !map.IsPocketMap && map.weatherManager != null;
            }
        }

        protected override float MeasureWidth()
        {
            WeatherDef weather = Current;
            return weather == null
                ? 0f
                : IconReadoutWidth(UIBarGlyphs.ForWeather(weather), weather.LabelCap) + 16f;
        }

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            WeatherDef weather = Current;
            if (weather == null)
                return;

            // The def's own description, which is what vanilla puts on its readout. Bad weather is not
            // recolored: "bad" covers everything from a light rain to a toxic fallout, and a warning color on
            // a drizzle would cry wolf. The glyph takes the same color as the text for the same reason.
            DrawIconReadout(rect, UIBarGlyphs.ForWeather(weather), weather.LabelCap, palette.TextSecondary,
                weather.description.NullOrEmpty() ? null : weather.description);
        }

        private static WeatherDef Current
        {
            get
            {
                Map map = Find.CurrentMap;
                return map == null || map.IsPocketMap ? null : map.weatherManager?.CurWeatherPerceived;
            }
        }
    }
}
