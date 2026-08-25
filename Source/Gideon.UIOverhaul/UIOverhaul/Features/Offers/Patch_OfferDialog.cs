using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Offers
{
    /// <summary>
    /// What the offer dialog currently has open, and the pawns it is offering.
    ///
    /// <b>One field is enough, and that is not an assumption.</b> <c>WindowStack.Add</c> begins by removing every
    /// window of the type being added, so a second offer letter opened while one is up replaces it rather than
    /// stacking beside it. There can only ever be one of these dialogs.
    /// </summary>
    internal static class OfferDialogState
    {
        /// <summary>Set while <c>OpenLetter</c> is running, because the window asks its size before it exists.</summary>
        internal static bool Opening;

        internal static Window Dialog;

        internal static List<Pawn> Pawns = new List<Pawn>();

        internal static Vector2 Scroll;

        /// <summary>
        /// Where the last draw ended, used as the scroll height for the next one.
        ///
        /// <b>Measured rather than predicted.</b> A formula for how tall the panel will be is wrong the first
        /// time a block is added to it and fails silently, which is a fault this codebase has paid for three
        /// times already. One frame of a slightly wrong scroll bar is the whole cost of not having it.
        /// </summary>
        internal static float Height;

        internal static bool Wanted
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings != null && settings.pawnDetailsOnOffers;
            }
        }

        internal static bool Showing(Window window)
        {
            return window != null && window == Dialog && Pawns.Count > 0 && Wanted;
        }
    }

    /// <summary>
    /// Notices when a letter that offers a pawn is opened, and remembers who it is offering.
    ///
    /// <b>Patched on the base class, which is where the window is actually built.</b> None of the four letters
    /// that offer a pawn override <c>OpenLetter</c> -- they override <c>Choices</c>, which is the list of
    /// buttons -- so the one patch here catches all of them.
    /// </summary>
    [HarmonyPatch(typeof(ChoiceLetter), nameof(ChoiceLetter.OpenLetter))]
    internal static class Patch_OfferLetterOpened
    {
        public static void Prefix(ChoiceLetter __instance)
        {
            UIGuard.Try("Offers.Open", () =>
            {
                OfferDialogState.Dialog = null;
                OfferDialogState.Scroll = Vector2.zero;
                OfferDialogState.Height = 0f;
                OfferDialogState.Pawns = OfferedPawns.For(__instance);

                // Raised before the body runs because the body adds the window, and adding a window asks it for
                // its initial size. By the time a postfix could set this the size has already been decided.
                OfferDialogState.Opening = OfferDialogState.Pawns.Count > 0;
            }, "Offer dialogs are drawn the way RimWorld draws them.");
        }

        public static void Postfix()
        {
            UIGuard.Try("Offers.Opened", () =>
            {
                if (OfferDialogState.Opening)
                    OfferDialogState.Dialog = Find.WindowStack.WindowOfType<Dialog_NodeTreeWithFactionInfo>();

                OfferDialogState.Opening = false;
            }, "Offer dialogs are drawn the way RimWorld draws them.");
        }
    }

    /// <summary>
    /// Widens the dialog to leave room for the panel.
    ///
    /// <b>Both tests are needed.</b> While the window is being constructed it is not yet the one in
    /// <c>OfferDialogState</c>, so the opening flag answers for it; every later read -- a resolution change, a
    /// caller asking the window its size again -- has the flag down and the identity to go on instead. Answering
    /// only the first would leave the window able to shrink back over its own panel.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_NodeTree), nameof(Dialog_NodeTree.InitialSize), MethodType.Getter)]
    internal static class Patch_OfferDialogSize
    {
        public static void Postfix(Dialog_NodeTree __instance, ref Vector2 __result)
        {
            Vector2 size = __result;

            __result = UIGuard.Try("Offers.Size", () =>
            {
                if (!OfferDialogState.Wanted)
                    return size;

                bool mine = OfferDialogState.Opening
                    ? __instance is Dialog_NodeTreeWithFactionInfo
                    : OfferDialogState.Showing(__instance);

                if (!mine)
                    return size;

                // Clamped to the screen the way vanilla clamps its own height: RimWorld caps this dialog at
                // UI.screenHeight and leaves the width alone, because 620 fits anywhere. Ours does not
                // necessarily, and a window wider than the screen puts its own buttons off the edge.
                return new Vector2(Mathf.Min(size.x + OfferPawnPanel.Width + OfferPawnPanel.Gap, UI.screenWidth),
                    size.y);
            }, size, null);
        }
    }
    /// <summary>
    /// Reserves the right hand column before RimWorld draws, and fills it afterwards.
    ///
    /// <b>The rect is narrowed rather than the dialog reimplemented.</b> Everything RimWorld puts in this
    /// window -- the letter text, its scroll view, the option buttons and their disabled reasons -- is laid out
    /// against the rect it is handed, so handing it a narrower one moves all of it at once and leaves none of
    /// the vanilla dialog rewritten here.
    ///
    /// <b>Patched on the subclass, not on <c>Dialog_NodeTree</c> where the drawing lives, and that is the whole
    /// reason this class exists twice over.</b> <c>Dialog_NodeTreeWithFactionInfo</c> overrides
    /// <c>DoWindowContents</c> to call the base and then draw the related faction block against
    /// <c>inRect.height - 79f</c> -- using its own copy of the rect, which a narrowing applied to the base call
    /// never reaches, since the rect is passed by value. Patching the base would therefore have moved the text
    /// and the buttons out of the panel's way and left the faction block painting straight across it.
    ///
    /// Narrowing the override instead fixes both at once: the base receives the already-narrowed rect through
    /// the ordinary call, and the faction block is laid out against the same one. Every letter that offers a
    /// pawn opens this exact type, so nothing is missed by not patching the base as well.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_NodeTreeWithFactionInfo), nameof(Dialog_NodeTreeWithFactionInfo.DoWindowContents))]
    internal static class Patch_OfferDialogContents
    {
        private static Rect whole;
        private static bool reserved;

        public static void Prefix(Dialog_NodeTree __instance, ref Rect inRect)
        {
            Rect given = inRect;

            reserved = false;

            inRect = UIGuard.Try("Offers.Reserve", () =>
            {
                if (!OfferDialogState.Showing(__instance))
                    return given;

                whole = given;
                reserved = true;

                return new Rect(given.x, given.y, given.width - OfferPawnPanel.Width - OfferPawnPanel.Gap,
                    given.height);
            }, given, null);
        }

        public static void Postfix(Dialog_NodeTree __instance)
        {
            if (!reserved)
                return;

            reserved = false;

            UIGuard.Try("Offers.Panel", () =>
            {
                if (!OfferDialogState.Showing(__instance))
                    return;

                UIColorPaletteDef palette = UIColorPaletteDef.Active;

                if (palette == null)
                    return;

                Rect column = new Rect(whole.xMax - OfferPawnPanel.Width, whole.y, OfferPawnPanel.Width,
                    whole.height);

                Rect content = new Rect(0f, 0f, column.width - 16f, Mathf.Max(OfferDialogState.Height, 1f));

                Widgets.BeginScrollView(column, ref OfferDialogState.Scroll, content);

                float used = OfferPawnPanel.Draw(new Rect(0f, 0f, content.width, content.height),
                    OfferDialogState.Pawns, palette);

                Widgets.EndScrollView();

                if (used > 0f)
                    OfferDialogState.Height = used;
            }, "The offer dialog is drawn without its pawn panel.");
        }
    }
}
