using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar.BarWidgets
{
    /// <summary>How the time of day is written.</summary>
    public enum UITimeFormat
    {
        /// <summary>24-hour clock with minutes: <c>14:30</c>. The default.</summary>
        TwentyFourHour,

        /// <summary>12-hour clock with minutes: <c>2:30 PM</c>.</summary>
        TwelveHour,

        /// <summary>RimWorld's own: the hour alone, <c>14h</c>.</summary>
        Vanilla
    }

    /// <summary>
    /// The time of day, in whichever form the player asked for.
    ///
    /// <b>Minutes are derived, not read.</b> RimWorld has no minute. A day is 60000 ticks and an hour is
    /// 2500, so the position within the current hour is the only thing a minute can be, and it is divided
    /// into sixty here purely so the readout looks like a clock. Nothing in the game changes on that
    /// boundary, which is worth knowing before reading anything into <c>:59</c>.
    ///
    /// <b>Everything takes a longitude</b>, because the hour is local. That is how vanilla's own readout
    /// works, and it is why two colonies on opposite sides of the planet do not read the same clock.
    /// </summary>
    public static class UIClock
    {
        private const int MinutesPerHour = 60;

        /// <summary>
        /// Minutes since local midnight, 0 to 1439.
        ///
        /// A cache key rather than something to display: it changes exactly when the readout would, so a
        /// caller can rebuild its string on the change instead of once a frame.
        /// </summary>
        public static int MinuteOfDay(long absTicks, float longitude)
        {
            int dayTick = GenDate.DayTick(absTicks, longitude);
            return dayTick * MinutesPerHour / GenDate.TicksPerHour;
        }

        /// <summary>The time of day, written in the given format.</summary>
        public static string Time(long absTicks, float longitude, UITimeFormat format)
        {
            // Hour and minute come from the same tick rather than from GenDate.HourInteger and a separate
            // remainder. The two agree, but reading them apart would leave a window at the turn of the hour
            // where one had advanced and the other had not, which is how a clock shows 14:00 as 13:00.
            int dayTick = GenDate.DayTick(absTicks, longitude);
            int hour = dayTick / GenDate.TicksPerHour;
            int minute = dayTick % GenDate.TicksPerHour * MinutesPerHour / GenDate.TicksPerHour;

            switch (format)
            {
                case UITimeFormat.TwelveHour:
                    int hour12 = hour % 12;
                    if (hour12 == 0)
                        hour12 = 12;

                    // AM and PM are not translated. RimWorld has no key for them, and this mod's own text is
                    // English throughout; a player on another language who wants a translated clock has the
                    // vanilla format, which is translated.
                    return hour12 + ":" + minute.ToString("00") + (hour < 12 ? " AM" : " PM");

                case UITimeFormat.Vanilla:
                    // Character for character what GenDate.DateFullStringWithHourAt appends, so choosing this
                    // gives back exactly the readout the widget had before there was a choice. ToString on
                    // the hour first, so this is string plus TaggedString -- the combination vanilla itself
                    // uses, and the one with an operator defined for it.
                    return hour.ToString() + "LetterHour".Translate();

                default:
                    return hour.ToString("00") + ":" + minute.ToString("00");
            }
        }

        /// <summary>The name shown for a format in the options window.</summary>
        public static string Label(UITimeFormat format)
        {
            switch (format)
            {
                case UITimeFormat.TwelveHour:
                    return "12-hour";
                case UITimeFormat.Vanilla:
                    return "RimWorld's";
                default:
                    return "24-hour";
            }
        }

        /// <summary>
        /// An example of the format, for the options window.
        ///
        /// A fixed time rather than the current one. The setting is reachable from the main menu where there
        /// is no map and so no local hour to read, and an example that read "0:00" before a game was loaded
        /// would look like the format was broken.
        /// </summary>
        public static string Example(UITimeFormat format)
        {
            switch (format)
            {
                case UITimeFormat.TwelveHour:
                    return "2:30 PM";
                case UITimeFormat.Vanilla:
                    return "14" + "LetterHour".Translate();
                default:
                    return "14:30";
            }
        }

        /// <summary>Parses a stored setting, falling back to the default for anything unrecognized.</summary>
        public static UITimeFormat Parse(string value)
        {
            if (value.EqualsIgnoreCase("TwelveHour"))
                return UITimeFormat.TwelveHour;

            if (value.EqualsIgnoreCase("Vanilla"))
                return UITimeFormat.Vanilla;

            return UITimeFormat.TwentyFourHour;
        }
    }
}
