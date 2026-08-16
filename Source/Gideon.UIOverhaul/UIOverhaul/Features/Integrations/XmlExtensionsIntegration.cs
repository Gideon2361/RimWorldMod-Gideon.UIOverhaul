using System;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Draws XML Extensions' settings menu inside our Options window instead of letting it open its own.
    ///
    /// <b>Why this one mod gets special treatment.</b> XML Extensions is not a mod with a settings page; it is
    /// the settings page for a large part of a modded game. Every mod that defines its options in XML rather than
    /// in code has its menu registered with XML Extensions under a mod id, with no <c>Mod</c> handle of its own,
    /// which means those menus exist nowhere in <c>LoadedModManager.ModHandles</c> and cannot appear in our
    /// category column at all. Their window is the only route to them. Sending the player out of our Options
    /// window to reach a large fraction of their settings is not something worth doing tidily.
    ///
    /// <b>What their page actually is.</b> <c>XmlMod.DoSettingsWindowContents</c> ignores the rect it is handed
    /// and its entire body is <c>Find.WindowStack.Add(new XmlExtensionsMenuModSettings(null, true))</c>. Their
    /// window then closes its host from its own constructor, which is what stops that call happening twice. It
    /// looks for <c>Dialog_ModSettings</c> by type, so hosted anywhere else the call repeats every pass -- see
    /// the redirect note on <c>Dialog_UIOptions.DrawModSettings</c> for what that did.
    ///
    /// <b>So their window is built and drawn, but never opened.</b> The instance is created and <c>PreOpen</c> is
    /// called on it -- which is what loads the mod list, the textures and the previous selection -- and then its
    /// <c>DoWindowContents</c> is called straight into our pane every frame. It is never added to the window
    /// stack, so the redirect it performs on open never runs, and nothing of ours is closed. Their layout arrives
    /// unchanged because it is handed exactly the rect their own window would have given it.
    ///
    /// <b>Their content picks up our theme by drawing through the same widgets we already patch.</b> Their menus
    /// are built from <c>Widgets</c> and <c>Listing_Standard</c>, so the buttons, checkboxes, scrollbars and
    /// rounding are ours the moment their code runs inside our window. Nothing here restyles their internals.
    ///
    /// <b>Reflection throughout, and it stands down rather than fails.</b> Their assembly cannot be referenced
    /// without making this mod depend on theirs. If any piece is missing, or drawing throws even once, the whole
    /// integration switches off for the session and the caller falls back to asking the mod to draw its own page
    /// -- which opens their window exactly as it always did. A broken integration must never be a locked door.
    /// </summary>
    internal static class XmlExtensionsIntegration
    {
        private const string ModTypeName = "XmlExtensions.XmlMod";
        private const string PageTypeName = "XmlExtensions.XmlExtensionsMenuModSettings";

        /// <summary>
        /// The rect their window gives its own contents, which is the size their layout was written against.
        ///
        /// <b>Derived from their window rather than guessed.</b> <c>InitialSize</c> is
        /// <c>900 + ListWidth + 6</c> by <c>700</c> -- 1162 by 700 -- and <c>Window.InnerWindowOnGUI</c> hands
        /// <c>DoWindowContents</c> that rect contracted by <c>Margin</c>, which is 18 on every side.
        ///
        /// The width matters more than it looks. Their layout takes the right 864 pixels for the settings body
        /// and the left 256 for the mod list, so anything under 1120 overlaps the two. Vanilla gives a settings
        /// page 900, which is why their page could never have been drawn in one.
        ///
        /// The bottom 40 is where their own window would have put its close button; their content already
        /// reserves it, so in our pane it is simply empty rather than cut off.
        /// </summary>
        internal static readonly Vector2 AuthoredPane = new Vector2(1126f, 664f);

        private static bool probed;
        private static Type modType;
        private static Type pageType;
        private static FieldInfo shouldCloseField;

        /// <summary>Their window, built and drawn but never added to the stack.</summary>
        private static Window page;

        /// <summary>Set when anything at all goes wrong, and never cleared for the rest of the session.</summary>
        private static bool stoodDown;

        /// <summary>Whether their menu can be hosted, decided once.</summary>
        internal static bool Available
        {
            get
            {
                Probe();

                return !stoodDown && pageType != null;
            }
        }

        /// <summary>Whether this mod's settings page is the one we draw ourselves.</summary>
        internal static bool Hosts(Mod mod)
        {
            return mod != null && Available && mod.GetType() == modType;
        }

        /// <summary>
        /// Draws their menu into the rect.
        /// </summary>
        /// <returns>True if their menu asked for the window to close.</returns>
        internal static bool Draw(Rect rect)
        {
            bool finished = false;

            bool drew = UIGuard.Try("Integrations.XmlExtensions.Draw", () =>
            {
                if (page == null)
                {
                    // Both arguments given explicitly: they are optional parameters, and reflection does not
                    // fill those in. (null, true) is what their own page passes -- no starting menu, and "this
                    // is XML Extensions itself", which opens on their options rather than on a mod's.
                    page = (Window) Activator.CreateInstance(pageType, new object[] { null, true });

                    // Their PreOpen is where the mod list is built, the textures resolved and the previously
                    // selected mod restored. Called once, here, because this instance is kept between frames.
                    page.PreOpen();
                }

                page.DoWindowContents(rect);

                if (shouldCloseField == null)
                    return;

                object requested = shouldCloseField.GetValue(page);

                if (!(requested is int) || (int) requested <= 0)
                    return;

                // Cleared as well as acted on. Their own window would be thrown away at this point; ours is
                // built fresh each visit but the flag is read before that happens, and a flag left set would
                // shut the window again the instant it reopened.
                shouldCloseField.SetValue(page, 0);

                finished = true;
            }, "XML Extensions' settings open in a window of their own instead of inside this one.");

            if (!drew)
            {
                // One failure stands the integration down for the session rather than throwing once per frame
                // forever. The caller then asks the mod to draw its own page, which opens their window as usual.
                stoodDown = true;

                Leave();
            }

            return finished;
        }

        /// <summary>
        /// Finishes with their menu, the way closing their window would.
        ///
        /// Their <c>PreClose</c> is what saves the settings the player just changed and remembers which mod was
        /// selected for next time. Skipping it would quietly lose both.
        /// </summary>
        internal static void Leave()
        {
            Window leaving = page;
            page = null;

            if (leaving == null)
                return;

            UIGuard.Try("Integrations.XmlExtensions.Leave", () => leaving.PreClose(),
                "XML Extensions may not have saved what was just changed; its own settings window from the mod "
                + "list writes those on close.");
        }

        private static void Probe()
        {
            if (probed)
                return;

            probed = true;

            UIGuard.Try("Integrations.XmlExtensions.Probe", () =>
            {
                modType = AccessTools.TypeByName(ModTypeName);
                pageType = AccessTools.TypeByName(PageTypeName);

                // Both halves are needed: the mod type to recognize the page, the window type to draw it. A
                // window type that is not a Window is a name collision with some other mod, not theirs.
                if (modType == null || pageType == null || !typeof(Window).IsAssignableFrom(pageType))
                {
                    modType = null;
                    pageType = null;

                    return;
                }

                // Optional. Missing only costs the close-on-request behavior, so it is not grounds for standing
                // the whole integration down.
                shouldCloseField = AccessTools.Field(pageType, "shouldClose");
            }, "XML Extensions' settings open in a window of their own instead of inside this one.");
        }
    }
}
