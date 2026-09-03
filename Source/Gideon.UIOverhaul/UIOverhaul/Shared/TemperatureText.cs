using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// A temperature, with the degree sign RimWorld leaves out.
    ///
    /// <b>Vanilla prints a bare letter.</b> <c>GenText.ToStringTemperatureRaw</c> is the number and then C, F or
    /// K with nothing between them, in all three modes, so every temperature the game shows reads 33C. Nothing
    /// was wrong with the font: the string simply never had a degree sign in it.
    ///
    /// <b>Ours do, because this mod sets its figures in a real typeface and a bare 33C looks like a typo in
    /// one.</b> It is a deliberate divergence rather than a fix: vanilla's own panels still say 33C, and a
    /// player who notices the difference is seeing the two systems side by side rather than a bug.
    ///
    /// <b>Kelvin does not get one, and that is not an oversight.</b> The degree sign belongs to Celsius and
    /// Fahrenheit; the kelvin is an absolute unit and is written 306K. Printing 306 degrees K would be the kind
    /// of wrongness that is worse than the thing it was meant to fix.
    ///
    /// <b>Written as an escape rather than as the character itself.</b> Every source file in this mod is plain
    /// ASCII, and a literal degree sign is one careless re-encode away from becoming a mojibake pair in a UI
    /// nobody reads closely until it ships.
    /// </summary>
    internal static class TemperatureText
    {
        private const string Degree = "\u00B0";

        /// <summary>
        /// The temperature as it should be shown, converted to whatever unit the player has chosen.
        ///
        /// Whole degrees by default, which is what every caller in this mod wanted: a tenth of a degree is
        /// never the thing a player is deciding on, and it costs two characters in a column that is short of
        /// them.
        /// </summary>
        internal static string Of(float celsius, string format = "F0")
        {
            return UIGuard.Try<string>("Text.Temperature",
                () => GenTemperature.CelsiusTo(celsius, Prefs.TemperatureMode).ToString(format) + Suffix(),
                celsius.ToStringTemperature(format), null);
        }

        /// <summary>The unit, with its degree sign where the unit takes one.</summary>
        private static string Suffix()
        {
            switch (Prefs.TemperatureMode)
            {
                case TemperatureDisplayMode.Fahrenheit:
                    return Degree + "F";

                case TemperatureDisplayMode.Kelvin:
                    return "K";

                default:
                    return Degree + "C";
            }
        }
    }
}
