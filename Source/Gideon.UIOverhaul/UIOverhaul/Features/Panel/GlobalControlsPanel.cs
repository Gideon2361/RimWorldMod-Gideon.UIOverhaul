using System;
using System.Collections.Generic;
using System.Text;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.ButtonBar.BarWidgets;
using Gideon.UIOverhaul.Features.Calendar;
using Gideon.UIOverhaul.Features.Notifications;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Panel
{
    /// <summary>
    /// The bottom right corner, redrawn as a panel.
    ///
    /// <b>What was wrong with the corner.</b> Vanilla writes eight or nine right-aligned labels straight onto the
    /// map -- weather, temperature, the date, the season, conditions, counters -- with nothing behind them but
    /// <c>GenUI.DrawTextWinterShadow</c>, a gradient whose entire job is making bare text survive whatever is
    /// underneath it. It half works. Over dark soil the readout is fine; over snow, sand or a lit workshop floor
    /// it is a smear.
    ///
    /// <b>A panel, but a transparent one.</b> The obvious fix is an opaque background, and it is the wrong one:
    /// this is the only chrome in the mod drawn over the map rather than inside a window, so a solid fill is
    /// playable ground the player has lost for as long as the game runs. It draws on
    /// <see cref="UIColorRole.HudBackground"/>, which carries an alpha for exactly this, with an opaque border --
    /// a defined edge is what lets the fill stay faint. Vanilla's winter shadow is dropped along with it; a
    /// gradient behind a panel is a fix for a problem the panel already solved.
    ///
    /// <b>Weather and temperature share a row.</b> They are two readings of the same thing and cost two full rows
    /// in vanilla. Everything else keeps vanilla's order, which is deliberate: a player who has played this game
    /// for a thousand hours knows where the date is.
    ///
    /// <b>Every row answers to its own setting,</b> including the three that could not before. The temperature,
    /// the weather and the conditions are drawn inline by <c>GlobalControlsOnGUI</c> rather than through a call of
    /// their own, so there was no seam to hide them at and their checkboxes sat greyed out. Replacing the whole
    /// method is what makes them reachable.
    ///
    /// <b>What is still vanilla's, and why.</b> The date is drawn by <c>DateReadout.DateOnGUI</c>, the weather by
    /// <c>WeatherManager.DoWeatherGUI</c>, the conditions by <c>GameConditionManager.DoConditionsUI</c>, the
    /// counters by <c>GlobalControlsUtility</c>, and the temperature string comes from vanilla's own cached
    /// builder. Each of those carries something worth more than the layout freedom of reimplementing it: the date
    /// derives the hour, quadrum and season from the map's longitude and caches the result; the temperature walks
    /// the cells around the cursor to decide which room the player is asking about. Getting either subtly wrong
    /// produces a readout that is confidently incorrect, which is worse than one arranged differently.
    /// </summary>
    internal static class GlobalControlsPanel
    {
        /// <summary>
        /// Wider than vanilla's 200 so the weather and the temperature fit on one line without either being
        /// shortened. The 40 comes off the map, which is the trade.
        /// </summary>
        internal const float Width = 240f;

        private const float Pad = 6f;
        private const float RowGap = 4f;
        private const float BlockGap = 4f;

        /// <summary>Height of the merged weather and temperature row.</summary>
        private const float VitalsHeight = 24f;

        /// <summary>
        /// Height of the date row: the hour beside two stacked lines for the date and the season.
        ///
        /// Shorter than vanilla's <c>DateReadout.Height</c>, which is 48 or 74 because it stacks all three lines.
        /// Putting the hour alongside rather than above buys a row back.
        /// </summary>
        private const float DateHeight = 40f;

        /// <summary>Height of one game condition card.</summary>
        private const float ConditionHeight = 24f;

        /// <summary>Size of the weather, temperature and condition glyphs.</summary>
        private const float IconSize = 16f;

        /// <summary>Vanilla's height for each of the counter and clock readouts.</summary>
        private const float ReadoutHeight = 26f;

        /// <summary>Vanilla's own height for the memory block, which is seven lines rather than one.</summary>
        private const float MemoryHeight = 104f;

        /// <summary>The gap vanilla leaves between the top of this column and the letter stack.</summary>
        private const float LetterGap = 10f;

        /// <summary>
        /// Far enough off screen that nothing lands on a visible pixel at any resolution or UI scale.
        /// Shared with the hide patches, which use the same trick for the same reason.
        /// </summary>
        private const float OffScreen = Patch_GlobalControlsUtility_DoTimespeedControls.OffScreen;

        /// <summary>
        /// Vanilla's own row object, kept between frames as vanilla keeps its own.
        ///
        /// A <c>WidgetRow</c> holds its cursor across a draw, and the toggles are laid out by moving it, so a
        /// fresh one per frame would be equivalent -- but this also carries <c>FinalY</c>, which is the only way
        /// to find out how tall the row came out after mods have added to it.
        /// </summary>
        private static readonly WidgetRow toggleRow = new WidgetRow();

        /// <summary>
        /// How tall the toggle row was last time it drew, for reserving its space before it draws.
        ///
        /// Measured rather than declared for the same reason the Global Controls tab measures it: the count is not
        /// ours to know. <c>DoPlaySettingsGlobalControls</c> draws rather than reports, and every mod that
        /// postfixes it adds buttons without telling anyone.
        /// </summary>
        private static float measuredToggleHeight = TimeControls.TimeButSize.y;

        /// <summary>
        /// Vanilla's cached temperature string builder, which is private.
        ///
        /// Borrowed rather than reimplemented because it is not a format call: it walks the nine cells around the
        /// cursor looking for the room the player means, falls back to the edifice under it, and caches the result
        /// against the rounded temperature and the display mode. A copy would be fifty lines that has to agree
        /// with vanilla's about which room the mouse is in, and would silently disagree at the edges.
        /// </summary>
        private static readonly Func<string> TemperatureString = ResolveTemperatureString();

        private static Func<string> ResolveTemperatureString()
        {
            try
            {
                return AccessTools.MethodDelegate<Func<string>>(
                    AccessTools.Method(typeof(GlobalControls), "TemperatureString"));
            }
            catch
            {
                return null;
            }
        }

        internal static void Draw()
        {
            // Vanilla skips its whole corner on layout passes, letter stack included. Kept, because the letter
            // stack's own drawing assumes it.
            if (Event.current.type == EventType.Layout)
                return;

            Map map = Find.CurrentMap;

            if (map == null)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            UIOverhaulSettingsFile settings = UIGuard.Try("Panel.ReadCornerSettings",
                () => UIOverhaulSettingsFile.Current, null,
                "The corner shows every readout, which is the closest thing to vanilla.");

            float x = UI.screenWidth - Width;

            // The same anchor vanilla uses: the bottom of the screen less the main button bar. This mod's own
            // button bar is laid out from MainButtonDef.ButtonHeight too, so the two cannot drift apart.
            float y = UI.screenHeight - MainButtonDef.ButtonHeight - 4f;

            y = DrawToggleRow(y, settings);
            y -= BlockGap;

            y = DrawMainBlock(x, y, map, palette, settings);

            // Immediately above the date and weather, which is the readout it belongs next to: what is playing
            // is the same kind of fact as what the weather is doing. Below the conditions, because a cold snap is
            // something to act on and a song is not.
            y = Music.MusicStrip.Draw(x, y, palette, settings);

            y = DrawConditions(x, y, map, palette, settings);
            y = DrawReadouts(x, y, map, palette, settings);

            y -= LetterGap;

            // Where this corner ended, which is the anchor for anything docked at the bottom right. Reported
            // here as well as from the letter stack's own replacement, because that one does not run when the
            // player has asked for vanilla letters -- and the alerts column still needs to know where the
            // readouts stopped.
            NotificationLayout.Notify_CornerTop(y);

            // The corner's last act in vanilla too. Everything above sets where the letters begin, which is why
            // hiding rows moves them down the screen.
            Find.LetterStack.LettersOnGUI(y);
        }

        /// <summary>
        /// Vanilla's play settings toggles, drawn bare rather than on the panel.
        ///
        /// Bare on purpose: these are icon buttons with their own artwork and their own backgrounds, and a panel
        /// behind them would be a second frame around things that already have one. It also sidesteps having to
        /// know the row's height before drawing a background for it.
        ///
        /// <b>Hidden means aimed off screen, never skipped.</b> <c>DoPlaySettingsGlobalControls</c> handles the
        /// beauty display, room stats and map search shortcuts in the same pass that draws the buttons, so not
        /// calling it takes three keyboard shortcuts away from anyone who switched the row off.
        /// </summary>
        /// <summary>Whether the Global Controls tab is on screen, and therefore drawing these toggles itself.</summary>
        private static bool TabOpen()
        {
            return UIGuard.Try("Panel.GlobalControlsTabOpen",
                () => Find.WindowStack != null
                      && Find.WindowStack.IsOpen(typeof(MainTabWindow_GlobalControls)), false, null);
        }

        private static float DrawToggleRow(float y, UIOverhaulSettingsFile settings)
        {
            // Hidden while the tab is open, whatever the setting says, because the tab now draws in this very
            // corner: its Right anchor puts it on top of the button bar, under these widgets, which is the space
            // this row occupies. Two copies of the same toggles overlapping is worse than either alone, and the
            // tab is the copy the player asked for by opening it.
            //
            // Hidden the same way as ever, by aiming the row off screen rather than skipping the call, for the
            // reason in this method's summary: the corner keeps handling the three keyboard shortcuts even when
            // none of it can be seen.
            bool show = (settings == null || settings.showGlobalControlsWidget) && !TabOpen();

            if (!show)
            {
                UIGuard.Try("Panel.PlaySettingShortcuts", () =>
                    {
                        toggleRow.Init(OffScreen, OffScreen, UIDirection.RightThenDown);
                        Find.PlaySettings.DoPlaySettingsGlobalControls(toggleRow, false);
                    },
                    "The beauty, room stats and map search keyboard shortcuts do not work while the corner's "
                    + "toggle row is hidden.");

                return y;
            }

            float top = y - measuredToggleHeight;

            UIGuard.Try("Panel.PlaySettingsRow", () =>
                {
                    // LeftThenUp from the right edge, as vanilla lays it out, so a row long enough to wrap grows
                    // upward into the space above rather than off the side of the screen.
                    toggleRow.Init(UI.screenWidth, y - TimeControls.TimeButSize.y, UIDirection.LeftThenUp,
                        Width - Pad);

                    Find.PlaySettings.DoPlaySettingsGlobalControls(toggleRow, false);
                },
                "The corner's toggle row is missing. The same toggles are in the Global Controls tab.");

            // Read after the draw, since it is the drawing that moves the cursor. With LeftThenUp, FinalY is the
            // top of the last row reached.
            measuredToggleHeight = Mathf.Max(TimeControls.TimeButSize.y,
                y - toggleRow.FinalY);

            return top;
        }

        /// <summary>
        /// The panel proper: speed controls at the bottom, then the date, then weather and temperature.
        ///
        /// Bottom to top, keeping vanilla's order. The block draws nothing at all when every row inside it is
        /// switched off, rather than leaving an empty frame.
        /// </summary>
        private static float DrawMainBlock(float x, float y, Map map, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            bool showSpeed = settings == null || settings.showSpeedControlsWidget;
            bool showDate = settings == null || settings.showDateWidget;
            bool showWeather = (settings == null || settings.showWeatherWidget) && !map.IsPocketMap;
            bool showTemperature = settings == null || settings.showTemperatureWidget;
            bool showVitals = showWeather || showTemperature;
            bool showCalendar = settings == null || settings.showCalendarWidget;

            // The speed controls have to run even when hidden, because DoTimeControlsGUI handles the pause key
            // and the three speed shortcuts after it finishes drawing. This is the same mistake that cost the
            // pause key once already; see Patch_HideCornerRows.
            if (!showSpeed)
                UIGuard.Try("Panel.SpeedControlShortcuts", () =>
                    {
                        Vector2 size = TimeControls.TimeButSize;
                        TimeControls.DoTimeControlsGUI(new Rect(OffScreen, OffScreen, size.x * 5f, size.y));
                    },
                    "The pause and speed keyboard shortcuts do not work while the speed controls are hidden.");

            float dateHeight = showDate ? DateHeight : 0f;
            float speedHeight = showSpeed ? TimeControls.TimeButSize.y : 0f;
            float vitalsHeight = showVitals ? VitalsHeight : 0f;
            float calendarHeight = showCalendar ? CalendarWidget.Height : 0f;

            int rows = (showDate ? 1 : 0) + (showSpeed ? 1 : 0) + (showVitals ? 1 : 0)
                       + (showCalendar ? 1 : 0);

            if (rows == 0)
                return y;

            float height = Pad * 2f + dateHeight + speedHeight + vitalsHeight + calendarHeight
                           + RowGap * (rows - 1);
            Rect block = new Rect(x, y - height, Width, height);

            PaintBlock(block, palette);

            float cursor = block.yMax - Pad;

            if (showSpeed)
            {
                cursor -= speedHeight;

                // Right-aligned inside the panel, which is where it sits in vanilla and where the eye already
                // looks for it. Five buttons at vanilla's own size, so the glyph restyling still lines up.
                Rect speed = new Rect(block.xMax - Pad - TimeControls.TimeButSize.x * 5f, cursor,
                    TimeControls.TimeButSize.x * 5f, speedHeight);

                UIGuard.Try("Panel.SpeedControls", () => TimeControls.DoTimeControlsGUI(speed),
                    "The speed controls are missing from the corner. The pause and speed keys still work.");

                cursor -= RowGap;
            }

            if (showDate)
            {
                cursor -= dateHeight;

                Rect date = new Rect(block.x + Pad, cursor, block.width - Pad * 2f, dateHeight);

                UIGuard.Try("Panel.DateReadout", () => DrawDate(date, map, palette),
                    "The date is missing from the corner.");

                cursor -= RowGap;
            }

            if (showVitals)
            {
                cursor -= vitalsHeight;
                DrawVitals(new Rect(block.x + Pad, cursor, block.width - Pad * 2f, vitalsHeight), map, palette,
                    showWeather, showTemperature);

                cursor -= RowGap;
            }

            if (showCalendar)
            {
                cursor -= calendarHeight;

                UIGuard.Try("Panel.Calendar",
                    () => CalendarWidget.Draw(
                        new Rect(block.x + Pad, cursor, block.width - Pad * 2f, calendarHeight), map, palette),
                    "The calendar bar is missing from the corner.");
            }

            return block.y - BlockGap;
        }

        /// <summary>
        /// Weather on the left with its glyph, temperature on the right with its own.
        ///
        /// <b>Drawn here rather than handed to <c>DoWeatherGUI</c>.</b> Vanilla's version right-aligns a bare
        /// label into whatever rect it is given, which cannot produce an icon, a left-aligned label, or a shared
        /// row with the temperature. Only the label is reproduced; the weather's tooltip is rebuilt from the same
        /// public strings.
        /// </summary>
        private static void DrawVitals(Rect rect, Map map, UIColorPaletteDef palette, bool showWeather,
            bool showTemperature)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;

                if (showWeather)
                    UIGuard.Try("Panel.Weather", () =>
                        {
                            WeatherDef weather = map.weatherManager.curWeather;

                            if (weather == null)
                                return;

                            Rect icon = new Rect(rect.x, rect.y + (rect.height - IconSize) * 0.5f,
                                IconSize, IconSize);

                            GUI.color = palette.TextSecondary;
                            GUI.DrawTexture(icon, WeatherIcon(weather));
                            GUI.color = palette.TextSecondary;

                            Text.Anchor = TextAnchor.MiddleLeft;

                            Rect label = new Rect(icon.xMax + 6f, rect.y, rect.width * 0.55f, rect.height);
                            Widgets.LabelEllipses(label, weather.LabelCap);

                            GUI.color = previousColor;

                            if (Mouse.IsOver(label))
                                TooltipHandler.TipRegion(label, (TipSignal) weather.description);
                        },
                        "The weather is missing from the corner.");

                if (!showTemperature)
                    return;

                UIGuard.Try("Panel.Temperature", () =>
                    {
                        string text = TemperatureString != null
                            ? TemperatureString()
                            : map.mapTemperature.OutdoorTemp.ToStringTemperature("F0");

                        Text.Anchor = TextAnchor.MiddleRight;
                        GUI.color = palette.TextPrimary;

                        Rect label = new Rect(rect.center.x, rect.y, rect.width * 0.5f, rect.height);
                        Widgets.LabelEllipses(label, text);

                        float used = Mathf.Min(Text.CalcSize(text).x, label.width);

                        GUI.color = palette.TextSecondary;
                        GUI.DrawTexture(
                            new Rect(label.xMax - used - IconSize - 4f,
                                rect.y + (rect.height - IconSize) * 0.5f, IconSize, IconSize),
                            NotificationIcons.Thermometer);

                        GUI.color = previousColor;
                    },
                    "The temperature is missing from the corner.");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// The hour alongside the date and season rather than above them.
        ///
        /// <b>Reimplemented, and this is the one row where that took some deciding.</b> <c>DateOnGUI</c> stacks
        /// three right-aligned lines and there is no way to rearrange them from outside it, so the mockup's layout
        /// could not be had by calling it. What made the copy safe is that every input is public: the hour, the
        /// date string, the season and the quadrum all come from <c>GenDate</c> given the map's longitude and
        /// latitude, which is the part that would have been dangerous to approximate. Nothing here computes a date;
        /// it asks the same functions vanilla asks and arranges the answers differently.
        ///
        /// Vanilla's per-frame string caching is not reproduced. It exists because <c>DateReadoutStringAt</c>
        /// formats a string every call, and it is worth having -- but the rest of this panel already calls into
        /// vanilla readouts that do the same, and one formatted string per frame is not what makes a corner slow.
        /// </summary>
        private static void DrawDate(Rect rect, Map map, UIColorPaletteDef palette)
        {
            Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
            int ticks = Find.TickManager.TicksAbs;

            string dateString = GenDate.DateReadoutStringAt(ticks, longLat);
            Season season = GenDate.Season(ticks, longLat);

            // The player's chosen clock, not vanilla's bare hour. UIClock is the same formatter the bar's own
            // clock widget uses, so the two readouts cannot disagree about what time it is or how to write it --
            // and choosing the Vanilla format still gives back the "6h" this used to show, character for
            // character. Falls back to 24 hour if the settings cannot be read, which is this mod's default.
            UITimeFormat format = UIGuard.Try("Panel.ReadTimeFormat",
                () => UIOverhaulSettingsFile.Current.timeFormat, UITimeFormat.TwentyFourHour,
                "The corner's clock is written as a 24 hour time.");

            string hourLabel = UIClock.Time(ticks, longLat.x, format);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                // The hour is the thing glanced at most often, so it is the one element given a larger face.
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                // Wider than the old bare hour needed: "12:30 PM" is a good deal longer than "6h".
                Widgets.Label(new Rect(rect.x, rect.y, rect.width * 0.5f, rect.height), hourLabel);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = palette.TextSecondary;

                float half = rect.height * 0.5f;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, half), dateString);

                Text.Anchor = TextAnchor.UpperRight;
                GUI.color = palette.TextDisabled;

                if (season != Season.Undefined)
                    Widgets.Label(new Rect(rect.x, rect.y + half, rect.width, half), season.LabelCap());
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!Mouse.IsOver(rect))
                return;

            // Vanilla's own tooltip, rebuilt from the same public calls: the four quadrums and which season each
            // falls in at this latitude, which is the thing a player actually opens it for.
            StringBuilder quadrums = new StringBuilder();

            for (int i = 0; i < 4; i++)
            {
                Quadrum quadrum = (Quadrum) i;
                quadrums.AppendLine(quadrum.Label() + " - " + quadrum.GetSeason(longLat.y).LabelCap());
            }

            TooltipHandler.TipRegion(rect, new TipSignal(
                "DateReadoutTip".Translate(GenDate.DaysPassed, 15, season.LabelCap(), 15,
                    GenDate.Quadrum(ticks, longLat.x).Label(), quadrums.ToString()), 86423));
        }

        /// <summary>
        /// The active game conditions, on a panel of their own.
        ///
        /// Separate from the main block because the set of them changes while the game runs -- a toxic fallout
        /// starts and ends -- and a block that grows and shrinks should not push the readouts below it around.
        /// </summary>
        private static float DrawConditions(float x, float y, Map map, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            if (settings != null && !settings.showConditionsWidget)
                return y;

            List<GameCondition> conditions = UIGuard.Try("Panel.ConditionsList",
                () => map.gameConditionManager.ActiveConditions, null,
                "The active conditions are missing from the corner.");

            if (conditions == null || conditions.Count == 0)
                return y;

            // Newest at the bottom, nearest the rest of the panel, since a condition that just started is the one
            // the player is reacting to.
            for (int i = conditions.Count - 1; i >= 0; i--)
            {
                GameCondition condition = conditions[i];

                if (condition == null)
                    continue;

                Rect card = new Rect(x, y - ConditionHeight, Width, ConditionHeight);

                UIGuard.Try("Panel.Condition", () => DrawCondition(card, condition, palette),
                    "One of the active conditions is missing from the corner.");

                y = card.y - BlockGap;
            }

            return y;
        }

        /// <summary>
        /// One condition: its glyph, its name, and how long it has left.
        ///
        /// <b>A card each rather than vanilla's stacked text block.</b> <c>DoConditionsUI</c> draws them as bare
        /// right-aligned lines with the remaining time folded into the label, which is the same legibility problem
        /// as the rest of the corner and gives no room for a glyph. Each one is its own small panel here, so a
        /// condition arriving or ending moves nothing but itself.
        ///
        /// The tooltip is the condition's own <c>TooltipString</c>, which is what vanilla shows too.
        /// </summary>
        private static void DrawCondition(Rect rect, GameCondition condition, UIColorPaletteDef palette)
        {
            PaintBlock(rect, palette);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                Rect icon = new Rect(rect.x + Pad, rect.y + (rect.height - IconSize) * 0.5f, IconSize, IconSize);

                GUI.color = palette.Warning;
                GUI.DrawTexture(icon, ConditionIcon(condition));

                // The remaining time is measured first, so the label can be given exactly the room left over
                // rather than a guess -- a long condition name beside a long duration is the case that overflows.
                string remaining = condition.Permanent
                    ? string.Empty
                    : condition.TicksLeft.ToStringTicksToPeriod(shortForm: true);

                float remainingWidth = remaining.NullOrEmpty()
                    ? 0f
                    : Text.CalcSize(remaining).x + Pad;

                if (!remaining.NullOrEmpty())
                {
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = palette.TextDisabled;

                    Widgets.Label(new Rect(rect.xMax - Pad - remainingWidth, rect.y, remainingWidth,
                        rect.height), remaining);

                    Text.Anchor = TextAnchor.MiddleLeft;
                }

                GUI.color = palette.TextPrimary;

                float labelX = icon.xMax + 6f;
                Widgets.LabelEllipses(
                    new Rect(labelX, rect.y, Mathf.Max(0f, rect.xMax - Pad - remainingWidth - labelX),
                        rect.height),
                    condition.LabelCap);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, (TipSignal) condition.TooltipString);
        }

        /// <summary>
        /// A glyph for a weather, chosen by name.
        ///
        /// <b>By defName rather than by a table of every weather in the game,</b> because the game is not the only
        /// thing that adds weather. A mod's blood rain has no entry anywhere we could write one, so the match is
        /// on what its name contains and anything unrecognized falls through to the overcast glyph -- a neutral
        /// mark rather than a wrong one. <c>WeatherDef</c> carries no icon of its own; if it ever does, this
        /// should prefer it.
        ///
        /// <b>The exact matches come first, and death pall is why they have to.</b> Its label reads "death pall"
        /// but its defName is <c>UnnaturalFog</c>, so the substring rules below would have called it fog and given
        /// it the overcast glyph. That is the standing hazard with matching on names: a def's identifier and the
        /// words a player sees are allowed to disagree, and here they do. Anything matched exactly is matched
        /// before the loose rules get a chance to be wrong about it.
        /// </summary>
        private static Texture2D WeatherIcon(WeatherDef weather)
        {
            string name = weather.defName ?? string.Empty;

            // Anomaly's death pall. Exact, because the name says fog and the weather does not.
            if (name.Equals("UnnaturalFog", StringComparison.OrdinalIgnoreCase))
                return NotificationIcons.Skull;

            if (name.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.Snow;

            // Before the rain test, since a dry thunderstorm is the one storm that drops nothing.
            if (name.IndexOf("Dry", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.Wind;

            if (name.IndexOf("Rain", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Thunder", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.Rain;

            if (name.IndexOf("Clear", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.Clear;

            return NotificationIcons.Overcast;
        }

        /// <summary>
        /// A glyph for a condition, by the same rule and for the same reason: three that have a mark of their own,
        /// and the hazard triangle for everything else.
        /// </summary>
        private static Texture2D ConditionIcon(GameCondition condition)
        {
            string name = condition.def?.defName ?? string.Empty;

            if (name.IndexOf("Toxic", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.Toxic;

            if (name.IndexOf("Eclipse", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.Eclipse;

            if (name.IndexOf("SolarFlare", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.SolarFlare;

            // The condition that accompanies the death pall weather. Given the same glyph deliberately: the card
            // and the weather row sit inches apart during one event, and two different marks for it would read as
            // two different events. Note this one really is called DeathPall -- it is the weather def, not the
            // condition, whose name says fog.
            if (name.IndexOf("DeathPall", StringComparison.OrdinalIgnoreCase) >= 0)
                return NotificationIcons.Skull;

            return NotificationIcons.Hazard;
        }

        /// <summary>
        /// The optional readouts: memory, ticks, frames, the clock, and a raid countdown when one is running.
        ///
        /// <b>These are not colony information,</b> which is why they sit apart from the main block rather than
        /// inside it. A frame counter is a diagnostic; the date is something the colony is played by.
        ///
        /// The counters no longer need <c>DebugViewSettings</c> raised around them. That was a workaround for
        /// vanilla gating them behind the developer view settings, and replacing the method that did the gating
        /// removed the need for it -- see the note in the patch that used to do it.
        /// </summary>
        private static float DrawReadouts(float x, float y, Map map, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            bool performance = settings != null && settings.showPerformanceWidget;
            bool showFps = performance || DebugViewSettings.showFpsCounter;
            bool showTps = performance || DebugViewSettings.showTpsCounter;
            bool showClock = settings == null ? Prefs.ShowRealtimeClock : settings.showTimeWidget;
            bool showMemory = DebugViewSettings.showMemoryInfo;

            TimedDetectionRaids raids = UIGuard.Try("Panel.RaidCountdownLookup",
                () => Find.CurrentMap?.Parent?.GetComponent<TimedDetectionRaids>(), null,
                "The raid countdown is missing from the corner.");

            bool showCountdown = raids != null && raids.NextRaidCountdownActiveAndVisible;

            float height = (showMemory ? MemoryHeight : 0f)
                           + (showTps ? ReadoutHeight : 0f)
                           + (showFps ? ReadoutHeight : 0f)
                           + (showClock ? ReadoutHeight : 0f)
                           + (showCountdown ? ReadoutHeight : 0f);

            if (height <= 0f)
                return y;

            Rect block = new Rect(x, y - height - Pad * 2f, Width, height + Pad * 2f);

            PaintBlock(block, palette);

            // Vanilla's own drawing, and vanilla's own order. Each of these takes the cursor by reference and
            // moves it up by its own height, which is exactly the contract this loop wants.
            float cursor = block.yMax - Pad;
            float left = block.x + Pad;
            float inner = Width - Pad * 2f;

            UIGuard.Try("Panel.Readouts", () =>
                {
                    if (showMemory)
                        GlobalControlsUtility.DrawMemoryInfo(left, inner, ref cursor);

                    if (showTps)
                        GlobalControlsUtility.DrawTpsCounter(left, inner, ref cursor);

                    if (showFps)
                        GlobalControlsUtility.DrawFpsCounter(left, inner, ref cursor);

                    if (showClock)
                        GlobalControlsUtility.DoRealtimeClock(left, inner, ref cursor);

                    if (showCountdown)
                        DrawCountdown(new Rect(left, cursor - ReadoutHeight, inner, ReadoutHeight), raids);
                },
                "Some of the corner's counters are missing.");

            return block.y - BlockGap;
        }

        /// <summary>
        /// The "raiders arrive in" countdown, which vanilla draws from a private method.
        ///
        /// Reimplemented rather than borrowed, unlike the temperature: this is a label, a tooltip and a highlight
        /// with no cached state behind it, so a copy cannot drift from the original in any way that matters.
        /// </summary>
        private static void DrawCountdown(Rect rect, TimedDetectionRaids raids)
        {
            string left = raids.DetectionCountdownTimeLeftString;
            string text = "CaravanDetectedRaidCountdown".Translate(left);

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;

            if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);

            TooltipHandler.TipRegionByKey(rect, "CaravanDetectedRaidCountdownTip", left);
            Widgets.Label(rect, text);

            Text.Anchor = previousAnchor;
        }

        /// <summary>
        /// One block's background: a translucent fill and an opaque one pixel border.
        ///
        /// The border is not decoration. <c>HudBackground</c> is deliberately see-through, and over bright terrain
        /// a fill alone has no edge to say where the panel stops -- see the role's notes.
        /// </summary>
        private static void PaintBlock(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, palette.HudBackground);

            Color previous = GUI.color;
            GUI.color = palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previous;
        }
    }
}
