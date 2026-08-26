using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade
{
    /// <summary>
    /// Puts our trade window up in place of RimWorld's, wherever the trade was started from.
    ///
    /// <b>Patched where the window is added, not where it is built.</b> <c>Dialog_Trade</c> is constructed in
    /// eight places -- a caravan meeting, a trade ship's comms call, a settlement visit, two arrival actions, a
    /// gift offer, a transport pod landing and the pawn trade job -- and every one of them ends in
    /// <c>Find.WindowStack.Add</c>. Patching the eight would mean eight patches to keep and a ninth to miss when
    /// a mod adds its own way in; patching the funnel catches all of them, ours and anybody else's.
    ///
    /// <b>The discarded dialog has already done the useful half of its work, which is why this is cheap.</b>
    /// <c>Dialog_Trade</c>'s constructor calls <c>TradeSession.SetupWith</c> and
    /// <c>SetupPlayerCaravanVariables</c> before it is ever added to the stack. So by the time we see it the
    /// session is live, the deal is built, its cannot-sell message has been posted once, and the caravan has been
    /// told trading started. Our window reads that session and the vanilla object is garbage. Constructing ours
    /// from scratch instead would call <c>SetupWith</c> a second time and post that message twice.
    ///
    /// <b>Exact type, not assignability.</b> A mod that subclasses <c>Dialog_Trade</c> to add its own behaviour
    /// has done a great deal more than change how the window looks, and replacing it would throw that away
    /// silently. Ours stands in for vanilla's window only.
    ///
    /// <b>The setting is an escape hatch, not a fallback.</b> This mod's rule is that a feature failing at
    /// runtime must not quietly hand off to vanilla, because that hides the defect. This is the other thing: a
    /// player who runs mods that patch the trade dialog can turn ours off and keep theirs working. It shipped
    /// with the window rather than after the first bug report, because retrofitting one is worse than building
    /// it.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class Patch_TradeWindow
    {
        public static void Prefix(ref Window window)
        {
            Window replacement = Replacement(window);

            if (replacement != null)
                window = replacement;
        }

        /// <summary>
        /// Our window when this one should be replaced, or null to leave the argument alone.
        ///
        /// <b>Split out from the prefix because a ref parameter cannot be touched inside a lambda,</b> and the
        /// guard's body is one. Deciding here and assigning there keeps the whole decision inside the guard,
        /// which is what matters: nothing may throw out of a prefix on the method every window in the game goes
        /// through. The same shape the animals tab redirect uses, for the same compiler reason.
        ///
        /// <b>A throw leaves vanilla's window in place,</b> which is the right failure: the session is already
        /// set up, so the player gets a working trade screen rather than none.
        /// </summary>
        private static Window Replacement(Window window)
        {
            return UIGuard.Try<Window>("Trade.Redirect", () =>
            {
                if (window == null || window.GetType() != typeof(Dialog_Trade))
                    return null;

                if (!TradeWindowSettings.CustomTradeWindow)
                    return null;

                // The session must genuinely be live before we discard the object that set it up. It always is
                // by this point -- the constructor is what does it -- but a mod that builds a Dialog_Trade and
                // holds it while starting some other session would otherwise hand us a window with nothing
                // behind it.
                if (!TradeSession.Active || TradeSession.deal == null)
                    return null;

                return new Dialog_UITrade();
            }, null, null);
        }
    }

    /// <summary>
    /// Whether each of the trade screens is ours or RimWorld's.
    ///
    /// <b>One setting per replaced window rather than one for the set.</b> The compatibility risk is per window:
    /// a mod that adds a column to the trade dialog has nothing to do with the caravan packer, and somebody who
    /// has to switch one off should not lose the other three with it. Read on every use rather than cached,
    /// because the settings window is open while a player decides and a cached answer would need a restart to
    /// take effect.
    /// </summary>
    internal static class TradeWindowSettings
    {
        internal static bool CustomTradeWindow => Read(settings => settings.customTradeWindow);

        internal static bool CustomCaravanWindow => Read(settings => settings.customCaravanWindow);

        internal static bool CustomCommsWindow => Read(settings => settings.customCommsWindow);

        internal static bool BeaconReadout => Read(settings => settings.beaconReadout);

        /// <summary>
        /// Reads one flag, defaulting to on when the settings file is not there yet.
        ///
        /// On rather than off, because a missing settings file means a fresh install rather than a considered
        /// choice, and this mod's whole business is replacing windows.
        /// </summary>
        private static bool Read(System.Func<UIOverhaulSettingsFile, bool> read)
        {
            return UIGuard.Try("Trade.Setting", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings == null || read(settings);
            }, true, null);
        }
    }
}
