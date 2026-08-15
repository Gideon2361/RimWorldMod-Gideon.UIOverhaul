using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.ButtonBar.BarWidgets;
using Gideon.UIOverhaul.Features.Notifications;
using Verse;

namespace Gideon.UIOverhaul.Features.Options
{
    /// <summary>
    /// This mod's player-facing settings, stored as XML in RimWorld's config folder beside the game's
    /// own settings and the button bar layout.
    ///
    /// Not ModSettings. These are preferences that have to be readable before defs finish loading -- the
    /// chosen theme in particular -- and keeping them in a plain file next to the bar layout means one
    /// place to look, one format, and something a player can inspect or share.
    /// </summary>
    public class UIOverhaulSettingsFile
    {
        public const string FileName = "UIOverhaul_Settings.xml";

        /// <summary>
        /// defName of the palette the player chose. Empty means the shipped default.
        /// </summary>
        public string activePalette = "";

        /// <summary>
        /// Whether this mod writes diagnostic detail to the log.
        ///
        /// Off by default, and deliberately not tied to RimWorld's own dev mode: dev mode is on for whole
        /// sessions for unrelated reasons, and this is noisy enough that it should be something asked for
        /// rather than something inherited.
        ///
        /// Pushed into <see cref="UIDebug"/>, which is what the framework's instrumentation actually reads --
        /// the framework cannot see this file, so the value has to be handed to it.
        /// </summary>
        public bool debugLogging;

        /// <summary>
        /// Whether to force fullscreen at the display's native resolution on every launch.
        ///
        /// Off by default, and it has to be: this overrides a display preference the player set, and someone who
        /// plays windowed on purpose would find the game fighting them every launch with no obvious culprit.
        /// See <c>Features.Display.StartupFullscreen</c>.
        /// </summary>
        public bool fullscreenOnStartup;

        /// <summary>
        /// How the date widget writes the time of day.
        ///
        /// 24-hour with minutes by default. RimWorld's own readout shows the bare hour, which is a clock
        /// that cannot tell you how long is left of it; a colonist's shift, a caravan's arrival and a
        /// growing season are all read off this, and "14h" rounds away most of what makes that useful.
        /// The vanilla form is still on offer for anyone who prefers it.
        /// </summary>
        public UITimeFormat timeFormat = UITimeFormat.TwentyFourHour;

        /// <summary>
        /// Whether an open main tab can be dragged to a different size.
        ///
        /// On by default. RimWorld gives every tab one fixed size chosen by whoever wrote it, and the one that
        /// suits a three colonist camp is not the one that suits a colony of twenty -- the work grid and the
        /// pawn tables are the obvious cases, but every list tab has the same problem at some colony size.
        ///
        /// Sizes themselves live in their own file rather than here; see <c>Features.Tabs.TabSizes</c> for why.
        /// </summary>
        public bool resizableTabs = true;

        // ---------------------------------------------------------------------------------------
        // Notifications
        //
        // The three surfaces RimWorld raises things on: transient messages, the letter stack, and the alerts
        // readout. Each has two settings -- whether this mod draws it at all, and which corner it lives in.
        //
        // The defaults reproduce where the base game puts all three, deliberately. Installing this mod changes
        // how they look; where they are is the player's decision to make afterwards.
        // ---------------------------------------------------------------------------------------

        public bool restyleMessages = true;

        public bool restyleLetters = true;

        public bool restyleAlerts = true;

        /// <summary>Messages start where vanilla puts them: top left, clear of the resource readout.</summary>
        public NotificationDock messageDock = NotificationDock.TopLeft;

        public NotificationDock letterDock = NotificationDock.BottomRight;

        public NotificationDock alertDock = NotificationDock.BottomRight;

        /// <summary>
        /// How wide a letter row is drawn.
        ///
        /// <b>A setting because the trade is the player's.</b> These rows sit over the map, so width is bought
        /// with playable screen, and how much that costs depends on the display and on how somebody plays.
        /// 250 matches the corner panel underneath, which lines the two columns up and fits most letter labels.
        ///
        /// Clamped where it is read rather than where it is written, so a hand-edited file with a silly number
        /// gives an odd looking stack instead of an unusable screen.
        /// </summary>
        public float letterRowWidth = 250f;

        // ---------------------------------------------------------------------------------------
        // Desktop widgets
        //
        // The readouts this mod draws in the corner of the screen. Each is independently switchable because they
        // are independently useful: somebody who wants the season but not the weather is not an odd case, they are
        // someone whose colony is in a biome where the weather never changes.
        //
        // Every one defaults to on. A widget nobody can see is a widget nobody knows to turn on, and the whole set
        // is one checkbox away from gone for anyone who wants their corner back.
        // ---------------------------------------------------------------------------------------

        // showSpeedGlyphs was here, and is retired rather than defaulted: the drawn speed glyphs are simply how
        // this mod looks now. It was never a real choice -- the two options were this mod's icons and the ones
        // they were drawn to replace -- and a switch that nobody has a reason to move is a line of settings a
        // player has to read past to reach the ones that matter.

        /// <summary>
        /// The year bar in the corner: the growing season, today's place in the year, and the door to the
        /// calendar window.
        ///
        /// The first widget in the corner that is this mod's own rather than a restyling of RimWorld's.
        /// </summary>
        public bool showCalendarWidget = true;

        /// <summary>
        /// Whether the calendar names what the storyteller has scheduled, rather than only its kind.
        ///
        /// <b>Off by default because the honest default is vague.</b> The storyteller settles an incident's
        /// timing well before it fires and settles which incident only at the last moment, so the calendar can
        /// say "major threat on day 43" as a fact and cannot say "raid on day 43" at all. Switching this on adds
        /// the most specific true thing available: the exact incident where a component fires only one, and the
        /// category and component where it picks from a pool.
        ///
        /// Some players will read a spoiler into knowing a threat is coming. That is the point of the switch,
        /// and it is why the default is the coarse view rather than this one.
        /// </summary>
        public bool showExplicitStoryEvents;

        /// <summary>
        /// The real time clock: vanilla's HH:mm line, drawn by <c>DoRealtimeClock</c>.
        ///
        /// <b>This switch governs, in both directions.</b> Ticked shows the clock even when vanilla's own
        /// preference is off; cleared hides it even when that preference is on. An earlier version only ever
        /// added, and that was wrong for the obvious reason: a cleared box with a clock sitting above it reads as
        /// a broken setting, whatever the reasoning behind it was.
        ///
        /// <b>Seeded from the vanilla preference rather than from a constant,</b> which is what makes governing
        /// safe to do. A fixed default is wrong whichever way it points -- true forces a clock onto every colony
        /// that installs this mod, false takes it away from everyone who had asked vanilla for one. Reading
        /// <c>Prefs.ShowRealtimeClock</c> when this mod first writes its config means installing changes nothing,
        /// and the switch takes over from there.
        ///
        /// In the field initializer so it covers every path into <see cref="Load"/> at once: no config file, a
        /// config file predating this setting, and an unreadable one all construct the object and then overwrite
        /// only what they actually read.
        ///
        /// This is deliberately not what the performance meter does, and the difference is the flag rather than
        /// the feature. <c>Prefs.ShowRealtimeClock</c> is a saved player preference, so there is something
        /// meaningful to inherit. <c>DebugViewSettings</c> is session state that resets every launch, so there
        /// would be nothing to seed from and nothing a player had deliberately kept -- which is why that one only
        /// ever adds.
        /// </summary>
        public bool showTimeWidget = InheritedRealtimeClock();

        /// <summary>
        /// Vanilla's real time clock preference, or false if it cannot be read.
        ///
        /// Guarded because this runs from a field initializer, which is as early as this type can be touched.
        /// <c>Prefs.ShowRealtimeClock</c> reads through <c>Prefs.data</c>, and a null there would throw out of a
        /// constructor -- taking the whole settings object with it, over a clock.
        /// </summary>
        private static bool InheritedRealtimeClock()
        {
            try
            {
                return Prefs.ShowRealtimeClock;
            }
            catch
            {
                return false;
            }
        }

        public bool showSpeedControlsWidget = true;

        /// <summary>
        /// Vanilla's date block, which is one switch because it is one call.
        ///
        /// <c>DoDate</c> hands the whole thing to <c>DateReadout.DateOnGUI</c>, which draws the hour, the date and
        /// the season together and reports one height for all three. Separate switches for the date and the season
        /// would mean reimplementing that readout, and a readout that shows the wrong day is worse than one that
        /// shows a line somebody did not ask for.
        /// </summary>
        public bool showDateWidget = true;

        // Kept, with no switch in front of them yet, because the rows they name are drawn inside
        // GlobalControlsOnGUI itself rather than through a call of their own -- the temperature is an inline
        // Widgets.Label, and the weather and the conditions have their layout cursor moved by the caller rather
        // than by the method being called, so skipping either leaves a hole where it was. Both become reachable
        // when that method is replaced, and the choices a player makes now should still be here when it is.
        public bool showTemperatureWidget = true;

        public bool showWeatherWidget = true;

        public bool showConditionsWidget = true;

        /// <summary>
        /// Whether vanilla's own row of play settings toggles is drawn in the corner.
        ///
        /// The one switch here that hides something the base game draws rather than something this mod adds, because
        /// the Global Controls tab holds the same toggles and a player who uses the tab has no reason to keep the row
        /// over their map.
        ///
        /// <b>Hiding this and removing the tab from the bar at the same time is allowed.</b> It looks like it strands
        /// the toggles, and does not: this mod's settings are always reachable from the bar's options button, which
        /// deliberately cannot be hidden, so either can be restored from there. No combination of these settings
        /// produces a state a player cannot get out of.
        /// </summary>
        public bool showGlobalControlsWidget = true;

        /// <summary>
        /// Whether the performance meter is drawn: frames per second and ticks per second.
        ///
        /// <b>The one widget here that defaults to off</b>, unlike the six above. A readout of the game's own frame
        /// rate is a diagnostic rather than something a colony is played with, and a permanent number in the corner
        /// invites watching it. Someone chasing late game slowdown will go looking for this; nobody else needs it
        /// sitting there.
        /// </summary>
        public bool showPerformanceWidget;

        // There was a master switch here, and a ShowsWidget helper that folded it into every read. Both are gone:
        // one box that clears the rest is a different control from a box per widget, and having both meant a
        // player who cleared one thing and a player who cleared everything left the settings in states that read
        // the same. Each widget answers for itself now.

        // There is deliberately no option to hide the bar's UI options button. It used to exist, back when
        // these settings were also reachable from the vanilla Options window; that route turned out to be
        // impossible -- Dialog_Options ignores any OptionCategoryDef from a mod -- which leaves the bar
        // button as the only way in. An option to remove the only way in is a trap, not a preference.

        public static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        private static UIOverhaulSettingsFile current;

        /// <summary>
        /// The loaded settings, read from disk on first use and after any <see cref="Reload"/>.
        ///
        /// Handing the debug flag to <see cref="UIDebug"/> happens here rather than in <see cref="ApplyTheme"/>,
        /// because unlike the theme it does not need defs and so should not wait for them -- instrumentation is
        /// most wanted during startup, which is over before ApplyTheme can run.
        ///
        /// This also covers the config watcher: it calls Reload, which drops the instance, so the next read
        /// re-pushes whatever the edited file now says.
        /// </summary>
        public static UIOverhaulSettingsFile Current
        {
            get
            {
                if (current == null)
                {
                    current = Load();
                    UIDebug.Enabled = current.debugLogging;
                }

                return current;
            }
        }

        public static void Reload()
        {
            current = null;
        }

        /// <summary>
        /// Pushes the stored theme into the framework. Called once the def database exists, since a
        /// palette is a Def and cannot be resolved before then.
        /// </summary>
        public void ApplyTheme()
        {
            UIColorPaletteDef.ActiveDefName = activePalette.NullOrEmpty() ? null : activePalette;

            if (UIColorPaletteDef.ActiveIsMissing)
            {
                Log.Warning(UILogTag.Prefix + $"Palette '{activePalette}' is not loaded -- the mod that "
                            + "supplied it may be disabled. Falling back to the default theme.");
                UIColorPaletteDef.ActiveDefName = null;
            }
        }

        /// <summary>
        /// A dock name from the file, falling back rather than complaining.
        ///
        /// Same reasoning as the clock format above: this is a hand-editable file, and a misspelled corner is not
        /// worth a warning popup on the way into the game. The fallback is the surface's own default, which is
        /// where RimWorld would have put it.
        /// </summary>
        private static NotificationDock ParseDock(string value, NotificationDock fallback)
        {
            if (value.NullOrEmpty())
                return fallback;

            foreach (NotificationDock dock in (NotificationDock[]) Enum.GetValues(typeof(NotificationDock)))
            {
                if (dock.ToString().EqualsIgnoreCase(value))
                    return dock;
            }

            return fallback;
        }

        private static UIOverhaulSettingsFile Load()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                    return new UIOverhaulSettingsFile();

                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                UIOverhaulSettingsFile settings = new UIOverhaulSettingsFile();
                XmlElement root = doc.DocumentElement;
                if (root == null)
                    return settings;

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (!(node is XmlElement field))
                        continue;

                    string value = field.InnerText?.Trim();

                    switch (field.Name)
                    {
                        case "activePalette":
                            settings.activePalette = value ?? "";
                            break;

                        case "debugLogging":
                            settings.debugLogging = value.EqualsIgnoreCase("true");
                            break;

                        case "fullscreenOnStartup":
                            settings.fullscreenOnStartup = value.EqualsIgnoreCase("true");
                            break;

                        // Anything unrecognized parses back to the default rather than raising a problem.
                        // This is a hand-editable file and a misspelled clock format is not worth a warning
                        // popup on the way into the game.
                        case "timeFormat":
                            settings.timeFormat = UIClock.Parse(value);
                            break;

                        // The widget switches. Absent means on, which is what makes a config file written before
                        // these existed read as "show everything" rather than silently hiding the lot.
                        // showDesktopWidgets, the old master switch, is retired rather than read; it is listed
                        // with the other retired names below. A file written before it was removed loads with
                        // each widget's own choice intact -- which is the right answer even for someone who had
                        // the master off, because the control they used to turn everything off no longer exists
                        // to turn it back on.
                        case "showConditionsWidget":
                            settings.showConditionsWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "resizableTabs":
                            settings.resizableTabs = !value.EqualsIgnoreCase("false");
                            break;

                        // The notification settings. The three restyle switches read "absent means on", so a
                        // config written before they existed keeps the drawing the player already had.
                        case "restyleMessages":
                            settings.restyleMessages = !value.EqualsIgnoreCase("false");
                            break;

                        case "restyleLetters":
                            settings.restyleLetters = !value.EqualsIgnoreCase("false");
                            break;

                        case "restyleAlerts":
                            settings.restyleAlerts = !value.EqualsIgnoreCase("false");
                            break;

                        case "messageDock":
                            settings.messageDock = ParseDock(value, NotificationDock.TopLeft);
                            break;

                        case "letterDock":
                            settings.letterDock = ParseDock(value, NotificationDock.BottomRight);
                            break;

                        case "alertDock":
                            settings.alertDock = ParseDock(value, NotificationDock.BottomRight);
                            break;

                        case "letterRowWidth":
                            float width;

                            // Invariant, not the machine's locale. A settings file is shared and hand-edited, and
                            // a number written with a decimal point should not stop parsing because the game is
                            // running in a language that writes it with a comma.
                            settings.letterRowWidth = float.TryParse(value, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out width)
                                ? width
                                : 250f;

                            break;

                        // Reads the opposite way round to the widgets below it, because this one defaults to off:
                        // absent means off here.
                        case "showTimeWidget":
                            settings.showTimeWidget = value.EqualsIgnoreCase("true");
                            break;

                        case "showTemperatureWidget":
                            settings.showTemperatureWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "showSpeedControlsWidget":
                            settings.showSpeedControlsWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "showDateWidget":
                            settings.showDateWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "showWeatherWidget":
                            settings.showWeatherWidget = !value.EqualsIgnoreCase("false");
                            break;

                        // showSeasonWidget is retired, and listed with the other retired names below. The season
                        // is drawn by DateReadout as part of the date block, so it is the date switch that
                        // governs it.

                        case "showGlobalControlsWidget":
                            settings.showGlobalControlsWidget = !value.EqualsIgnoreCase("false");
                            break;

                        // Reads the opposite way round to the others, because this one defaults to off: absent
                        // means off here, where absent means on for every widget above.
                        case "showPerformanceWidget":
                            settings.showPerformanceWidget = value.EqualsIgnoreCase("true");
                            break;

                        // Retired settings. Accepted silently so an older config file does not raise a warning
                        // about something the player never chose to write -- these were written by a previous
                        // version of this mod, not typed by anyone, so there is nothing for them to act on.
                        //
                        // A name has to be listed here rather than merely dropped from the switch above: falling
                        // through to default is what produces the warning, and a setting this mod removed is not
                        // an unknown one. The warning is worth keeping for names that really are unrecognized,
                        // which is what a typo or a config from a newer version looks like.
                        //
                        // They stay listed permanently. The file is only rewritten when something is saved, so a
                        // player who never changes a setting keeps the old element indefinitely, and a list that
                        // was pruned after a release or two would start warning about it again.
                        case "showCalendarWidget":
                            settings.showCalendarWidget = !value.EqualsIgnoreCase("false");
                            break;

                        case "showExplicitStoryEvents":
                            settings.showExplicitStoryEvents = value.EqualsIgnoreCase("true");
                            break;

                        case "showBarButton":
                        case "showDesktopWidgets":
                        case "showSeasonWidget":
                        case "showSpeedGlyphs":
                            break;

                        default:
                            Log.Warning(UILogTag.Prefix + $"Unknown setting <{field.Name}>; ignored.");
                            break;
                    }
                }

                return settings;
            }
            catch (Exception ex)
            {
                // Reported rather than logged and forgotten. Discarding the file silently would look
                // like a hand-edit had no effect.
                UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so the previous settings are still in use: " + ex.Message
                });

                return new UIOverhaulSettingsFile();
            }
        }

        public void Save()
        {
            string path = FilePath;

            try
            {
                // So the watcher does not mistake our own write for someone editing the file.
                UIConfigWatcher.NotifySelfWrite();

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                XmlWriterSettings writerSettings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new UTF8Encoding(false)
                };

                using (XmlWriter writer = XmlWriter.Create(path, writerSettings))
                {
                    writer.WriteStartDocument();
                    writer.WriteComment(" Settings for Gideon's UI Overhaul. Written by the UI options "
                                        + "page; safe to hand-edit. ");
                    writer.WriteStartElement("UIOverhaulSettings");
                    writer.WriteElementString("activePalette", activePalette ?? "");
                    writer.WriteElementString("debugLogging", debugLogging ? "true" : "false");
                    writer.WriteElementString("fullscreenOnStartup",
                        fullscreenOnStartup ? "true" : "false");
                    writer.WriteElementString("timeFormat", timeFormat.ToString());

                    writer.WriteElementString("resizableTabs", resizableTabs ? "true" : "false");

                    writer.WriteElementString("restyleMessages", restyleMessages ? "true" : "false");
                    writer.WriteElementString("restyleLetters", restyleLetters ? "true" : "false");
                    writer.WriteElementString("restyleAlerts", restyleAlerts ? "true" : "false");
                    writer.WriteElementString("messageDock", messageDock.ToString());
                    writer.WriteElementString("letterDock", letterDock.ToString());
                    writer.WriteElementString("alertDock", alertDock.ToString());

                    // Invariant, matching how it is read. A width written with the machine's decimal separator
                    // would fail to parse on a machine that writes it differently, which is a settings file that
                    // silently resets when it is shared.
                    writer.WriteElementString("letterRowWidth",
                        letterRowWidth.ToString(CultureInfo.InvariantCulture));

                    writer.WriteElementString("showCalendarWidget", showCalendarWidget ? "true" : "false");
                    writer.WriteElementString("showExplicitStoryEvents",
                        showExplicitStoryEvents ? "true" : "false");
                    writer.WriteElementString("showTimeWidget", showTimeWidget ? "true" : "false");
                    writer.WriteElementString("showTemperatureWidget", showTemperatureWidget ? "true" : "false");
                    writer.WriteElementString("showSpeedControlsWidget",
                        showSpeedControlsWidget ? "true" : "false");
                    writer.WriteElementString("showDateWidget", showDateWidget ? "true" : "false");
                    writer.WriteElementString("showWeatherWidget", showWeatherWidget ? "true" : "false");
                    writer.WriteElementString("showConditionsWidget",
                        showConditionsWidget ? "true" : "false");
                    writer.WriteElementString("showGlobalControlsWidget",
                        showGlobalControlsWidget ? "true" : "false");
                    writer.WriteElementString("showPerformanceWidget",
                        showPerformanceWidget ? "true" : "false");
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Error(UILogTag.Prefix + $"Could not write {path}.\n{ex}");
            }
        }
    }
}
