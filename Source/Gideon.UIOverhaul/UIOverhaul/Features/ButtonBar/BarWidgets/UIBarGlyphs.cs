using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar.BarWidgets
{
    /// <summary>
    /// The pictures the bar widgets draw: one per kind of weather, plus a calendar for the date.
    ///
    /// <b>Generated, not shipped.</b> These are built from <see cref="UIGlyphCanvas"/> at startup rather than
    /// authored as PNGs, for the reasons <see cref="UIShapes"/> already gives: a generated glyph tints to
    /// whatever palette role the widget draws in, so it is legible on a light theme and a dark one without a
    /// second set of files, and there is nothing on disk for another mod to shadow with a texture of the same
    /// path.
    ///
    /// <b>Kinds, not weathers.</b> There are twenty-two weather defs across Core and the DLCs and thirteen
    /// glyphs, because the icon sits immediately to the left of the weather's own name. Its job is to be
    /// recognizable at sixteen pixels, not to be uniquely decodable -- blood rain and toxic rain both get the
    /// rain glyph, and the word beside it says which. Drawing a distinct picture for every def would mean
    /// thirteen of them differing by details that vanish at this size.
    ///
    /// <b>Overridable with art.</b> <see cref="ForWeather"/> looks for a texture at
    /// <c>UI/WeatherIcons/&lt;defName&gt;</c> before it falls back to a generated glyph, so a mod -- or this
    /// one later -- can drop in drawn art for any weather without a code change.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class UIBarGlyphs
    {
        /// <summary>Where a drawn override for a weather icon is looked for.</summary>
        private const string WeatherIconFolder = "UI/WeatherIcons/";

        internal static readonly Texture2D Sun;
        internal static readonly Texture2D Cloud;
        internal static readonly Texture2D Rain;
        internal static readonly Texture2D HeavyRain;
        internal static readonly Texture2D Thunder;
        internal static readonly Texture2D ThunderRain;
        internal static readonly Texture2D Snow;
        internal static readonly Texture2D Blizzard;
        internal static readonly Texture2D Fog;
        internal static readonly Texture2D Wind;
        internal static readonly Texture2D Sand;
        internal static readonly Texture2D Cave;
        internal static readonly Texture2D Orbit;
        internal static readonly Texture2D Calendar;

        /// <summary>
        /// Guarded because everything here is generated rather than loaded: fourteen textures rasterized in code,
        /// which is a good deal more that can go wrong than a <c>ContentFinder</c> miss. A throw would otherwise
        /// leave the type failed, and every weather readout would then throw on every frame while reaching for a
        /// glyph. The fields stay null instead, and the widgets fall back to text alone.
        ///
        /// The whole body sits in the try because these fields are readonly and C# will only let the static
        /// constructor itself assign them -- moving the work into a guarded helper would not compile. A glyph
        /// built before the failure is kept; the rest stay null.
        /// </summary>
        static UIBarGlyphs()
        {
            try
            {
                Sun = BuildSun();
                Cloud = BuildCloud();
                Rain = BuildRain(3, false);
                HeavyRain = BuildRain(4, true);
                Thunder = BuildThunder(false);
                ThunderRain = BuildThunder(true);
                Snow = BuildSnow();
                Blizzard = BuildBlizzard();
                Fog = BuildFog();
                Wind = BuildWind();
                Sand = BuildSand();
                Cave = BuildCave();
                Orbit = BuildOrbit();
                Calendar = BuildCalendar();

                // Populated here, not in a field initializer. Static field initializers all run before the
                // static constructor body, so a map written as an initializer would capture the glyph fields
                // while every one of them was still null.
                ByDefName = BuildDefNameMap();
            }
            catch (Exception ex)
            {
                UIGuard.Report("ButtonBar.BuildGlyphs", ex,
                    "The weather and date widgets show their text without an icon.");
            }
        }

        /// <summary>
        /// Which glyph a named weather gets.
        ///
        /// Every Core, Anomaly and Odyssey weather is listed. Anything not here -- a modded weather -- is
        /// classified from the def's own fields instead, in <see cref="Classify"/>, so it still gets
        /// something sensible without this mod having to know the name.
        /// </summary>
        private static readonly Dictionary<string, Texture2D> ByDefName;

        private static Dictionary<string, Texture2D> BuildDefNameMap()
        {
            return new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase)
            {
                // Core
                { "Clear", Sun },
                { "Fog", Fog },
                { "Rain", Rain },
                { "DryThunderstorm", Thunder },
                { "RainyThunderstorm", ThunderRain },
                { "FoggyRain", Rain },
                { "SnowGentle", Snow },
                { "SnowHard", Snow },
                { "Underground", Cave },

                // Anomaly. The unnatural weathers borrow the ordinary glyph for what they look like: gray
                // pall is a pall, blood rain falls like rain. Their names are alarming enough on their own.
                { "BloodRain", Rain },
                { "Undercave", Cave },
                { "MetalHell", Cave },
                { "UnnaturalFog", Fog },
                { "GrayPall", Fog },

                // Odyssey
                { "Windy", Wind },
                { "ToxRain", Rain },
                { "Sandstorm", Sand },
                { "BlindFog", Fog },
                { "Overcast", Cloud },
                { "Blizzard", Blizzard },
                { "TorrentialRain", HeavyRain },
                { "Orbit", Orbit }
            };
        }

        /// <summary>
        /// Resolved icons, the misses included. This runs once per frame while the widget is on the bar, and
        /// a ContentFinder miss walks every loaded mod's content rather than failing cheaply.
        /// </summary>
        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The icon for a weather: drawn art if any mod supplies it, otherwise a generated glyph.</summary>
        internal static Texture2D ForWeather(WeatherDef weather)
        {
            if (weather == null)
                return null;

            if (Cache.TryGetValue(weather.defName, out Texture2D cached))
                return cached;

            // ByDefName is null if the static constructor above did not finish. Reading it unguarded would turn a
            // contained startup failure into a NullReferenceException on the frame the weather first changes.
            Texture2D resolved = ContentFinder<Texture2D>.Get(WeatherIconFolder + weather.defName, false)
                                 ?? (ByDefName != null
                                     && ByDefName.TryGetValue(weather.defName, out Texture2D known)
                                     ? known
                                     : Classify(weather));

            Cache[weather.defName] = resolved;
            return resolved;
        }

        /// <summary>
        /// A glyph for a weather nobody here has heard of, read off what the weather actually does.
        ///
        /// Ordered by what dominates the look of it. Snow before rain because a def that does both is a
        /// sleet and reads as winter; sand before wind because a sandstorm is windy by construction and the
        /// sand is the part you can see.
        /// </summary>
        private static Texture2D Classify(WeatherDef weather)
        {
            if (weather.snowRate > 0f)
                return weather.windSpeedFactor > 1.5f ? Blizzard : Snow;

            if (weather.sandRate > 0f)
                return Sand;

            if (weather.rainRate > 0f)
                return weather.rainRate >= 1.5f ? HeavyRain : Rain;

            if (weather.windSpeedFactor > 1.5f)
                return Wind;

            // Neither precipitation nor wind. Overcast is the honest guess: a clear sky is a specific claim,
            // and the sun would be wrong for the darkness weathers that several mods add.
            return Cloud;
        }

        // -------------------------------------------------------------------------------------------
        // The glyphs
        //
        // Coordinates are 0 to 1 with y downward. Every glyph keeps inside roughly 0.1 to 0.9 so that none
        // of them touches the edge of its slot, and so a cloud and a sun look the same weight beside each
        // other on the bar.
        // -------------------------------------------------------------------------------------------

        private static Texture2D BuildSun()
        {
            UIGlyphCanvas canvas = new UIGlyphCanvas().Disc(0.5f, 0.5f, 0.19f);

            for (int i = 0; i < 8; i++)
            {
                float angle = Mathf.PI * 2f * i / 8f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                // Thicker than the rays look like they need. Anything finer survives the authored size and
                // then fades to a smudge once the glyph is drawn at sixteen pixels.
                canvas.Capsule(0.5f + cos * 0.28f, 0.5f + sin * 0.28f,
                    0.5f + cos * 0.43f, 0.5f + sin * 0.43f, 0.07f);
            }

            return canvas.ToTexture("Gideon.UIOverhaul.Weather.Sun");
        }

        private static Texture2D BuildCloud()
        {
            return new UIGlyphCanvas()
                .Cloud(0.5f, 0.5f, 1.05f)
                .ToTexture("Gideon.UIOverhaul.Weather.Cloud");
        }

        /// <param name="streaks">How many falling lines. Four reads as heavier than three at this size.</param>
        /// <param name="heavy">Longer, thicker streaks that reach further down the glyph.</param>
        private static Texture2D BuildRain(int streaks, bool heavy)
        {
            UIGlyphCanvas canvas = new UIGlyphCanvas().Cloud(0.5f, 0.36f, 0.95f);

            float thickness = heavy ? 0.07f : 0.055f;
            float length = heavy ? 0.24f : 0.18f;
            float first = streaks > 3 ? 0.28f : 0.34f;
            float step = streaks > 3 ? 0.15f : 0.16f;

            for (int i = 0; i < streaks; i++)
            {
                float x = first + step * i;

                // Slanted, because vertical lines under a cloud read as icicles.
                canvas.Capsule(x, 0.62f, x - 0.05f, 0.62f + length, thickness);
            }

            return canvas.ToTexture(heavy
                ? "Gideon.UIOverhaul.Weather.HeavyRain"
                : "Gideon.UIOverhaul.Weather.Rain");
        }

        /// <param name="withRain">Adds streaks either side of the bolt, for a storm that is also wet.</param>
        private static Texture2D BuildThunder(bool withRain)
        {
            UIGlyphCanvas canvas = new UIGlyphCanvas().Cloud(0.5f, 0.34f, 0.92f);

            // A zigzag rather than a filled bolt: a filled one needs a polygon, and at sixteen pixels the
            // stroke reads as the same shape.
            canvas.Polyline(0.06f, 0.56f, 0.58f, 0.44f, 0.74f, 0.56f, 0.74f, 0.42f, 0.92f);

            if (withRain)
            {
                canvas.Capsule(0.30f, 0.62f, 0.25f, 0.82f, 0.055f);
                canvas.Capsule(0.72f, 0.62f, 0.67f, 0.82f, 0.055f);
            }

            return canvas.ToTexture(withRain
                ? "Gideon.UIOverhaul.Weather.ThunderRain"
                : "Gideon.UIOverhaul.Weather.Thunder");
        }

        private static Texture2D BuildSnow()
        {
            return new UIGlyphCanvas()
                .Cloud(0.5f, 0.36f, 0.95f)
                .Snowflake(0.36f, 0.76f, 0.11f, 0.05f)
                .Snowflake(0.64f, 0.76f, 0.11f, 0.05f)
                .ToTexture("Gideon.UIOverhaul.Weather.Snow");
        }

        /// <summary>
        /// Driven snow: one large flake with wind behind it.
        ///
        /// No cloud on purpose. A blizzard glyph that led with a cloud would be the snow glyph with extra
        /// clutter in it; leading with the drive is what makes the two tell apart in a sixteen pixel slot.
        ///
        /// One big flake rather than the two small ones the snow glyph uses, and the wind lines kept clear
        /// of it. Three lines and two flakes was legible at authored size and turned to mush at sixteen
        /// pixels, which is the only size that counts.
        /// </summary>
        private static Texture2D BuildBlizzard()
        {
            return new UIGlyphCanvas()
                .Capsule(0.06f, 0.26f, 0.38f, 0.26f, 0.075f)
                .Capsule(0.06f, 0.50f, 0.30f, 0.50f, 0.075f)
                .Capsule(0.06f, 0.74f, 0.36f, 0.74f, 0.075f)
                .Snowflake(0.66f, 0.50f, 0.26f, 0.08f)
                .ToTexture("Gideon.UIOverhaul.Weather.Blizzard");
        }

        private static Texture2D BuildFog()
        {
            return new UIGlyphCanvas()
                .Capsule(0.20f, 0.30f, 0.78f, 0.30f, 0.085f)
                .Capsule(0.12f, 0.47f, 0.88f, 0.47f, 0.085f)
                .Capsule(0.22f, 0.64f, 0.72f, 0.64f, 0.085f)
                .Capsule(0.16f, 0.81f, 0.82f, 0.81f, 0.085f)
                .ToTexture("Gideon.UIOverhaul.Weather.Fog");
        }

        /// <summary>
        /// Wind: long lines with a curl at the end, which is what distinguishes them from fog's flat bars.
        ///
        /// The curl is two short capsules rather than an arc. A ring segment would need the canvas to clip
        /// by angle, and at this size three straight pieces bend indistinguishably from a curve.
        /// </summary>
        private static Texture2D BuildWind()
        {
            return new UIGlyphCanvas()
                .Capsule(0.12f, 0.32f, 0.64f, 0.32f, 0.07f)
                .Polyline(0.07f, 0.64f, 0.32f, 0.78f, 0.26f, 0.74f, 0.42f)
                .Capsule(0.10f, 0.54f, 0.82f, 0.54f, 0.07f)
                .Capsule(0.16f, 0.76f, 0.58f, 0.76f, 0.07f)
                .Polyline(0.07f, 0.58f, 0.76f, 0.72f, 0.70f, 0.68f, 0.86f)
                .ToTexture("Gideon.UIOverhaul.Weather.Wind");
        }

        /// <summary>
        /// Wind carrying grit: the same lines, with the grit between them.
        ///
        /// The grit sits on the rows the lines do not use. Dots level with a line merge into it once the
        /// glyph is scaled down, which turns the whole thing back into the wind glyph with lumps.
        /// </summary>
        private static Texture2D BuildSand()
        {
            return new UIGlyphCanvas()
                .Capsule(0.10f, 0.24f, 0.74f, 0.24f, 0.07f)
                .Capsule(0.16f, 0.50f, 0.84f, 0.50f, 0.07f)
                .Capsule(0.10f, 0.76f, 0.66f, 0.76f, 0.07f)
                .Disc(0.30f, 0.37f, 0.05f)
                .Disc(0.66f, 0.37f, 0.05f)
                .Disc(0.44f, 0.63f, 0.05f)
                .Disc(0.80f, 0.63f, 0.05f)
                .ToTexture("Gideon.UIOverhaul.Weather.Sand");
        }

        /// <summary>
        /// Rock overhead and ground underfoot, for the weathers that are not weather: underground,
        /// undercave, metal hell.
        ///
        /// A peak over a line, not a filled dome with the opening erased. The erased version read as a
        /// bitten blob rather than a cave, and once scaled down the bite closed up entirely.
        /// </summary>
        private static Texture2D BuildCave()
        {
            return new UIGlyphCanvas()
                .Polyline(0.10f, 0.10f, 0.66f, 0.5f, 0.24f, 0.90f, 0.66f)
                .Capsule(0.10f, 0.84f, 0.90f, 0.84f, 0.08f)
                .ToTexture("Gideon.UIOverhaul.Weather.Cave");
        }

        /// <summary>
        /// A ringed world, for orbit -- where there is no weather and no sky to have any.
        ///
        /// The ring is cut where the planet passes in front of it, which is the whole trick: a complete
        /// ellipse around a disc reads as an eye. Erasing before the planet is drawn means the bite is taken
        /// out of the ring alone, and the disc then fills the gap.
        /// </summary>
        private static Texture2D BuildOrbit()
        {
            return new UIGlyphCanvas()
                .Ring(0.5f, 0.52f, 0.44f, 0.18f, 0.065f)
                .Erase(0.5f, 0.44f, 0.27f)
                .Disc(0.5f, 0.46f, 0.24f)
                .ToTexture("Gideon.UIOverhaul.Weather.Orbit");
        }

        /// <summary>
        /// The calendar beside the date.
        ///
        /// An outline rather than a filled block, so it reads as a page at the same weight as the text it
        /// sits next to instead of as a solid tile pulling the eye off the date.
        /// </summary>
        private static Texture2D BuildCalendar()
        {
            return new UIGlyphCanvas()
                // Two binding rings above the page, which is what says "calendar" rather than "window".
                .Capsule(0.34f, 0.12f, 0.34f, 0.26f, 0.075f)
                .Capsule(0.66f, 0.12f, 0.66f, 0.26f, 0.075f)

                // The page.
                .Capsule(0.16f, 0.24f, 0.84f, 0.24f, 0.075f)
                .Capsule(0.16f, 0.86f, 0.84f, 0.86f, 0.075f)
                .Capsule(0.16f, 0.24f, 0.16f, 0.86f, 0.075f)
                .Capsule(0.84f, 0.24f, 0.84f, 0.86f, 0.075f)

                // The rule under the header, and the days below it.
                .Capsule(0.16f, 0.42f, 0.84f, 0.42f, 0.06f)
                .Disc(0.32f, 0.58f, 0.055f)
                .Disc(0.50f, 0.58f, 0.055f)
                .Disc(0.68f, 0.58f, 0.055f)
                .Disc(0.32f, 0.74f, 0.055f)
                .Disc(0.50f, 0.74f, 0.055f)
                .ToTexture("Gideon.UIOverhaul.Bar.Calendar");
        }
    }
}
