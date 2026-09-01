using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// The window the factions tab opens.
    ///
    /// <b>It inherits from RimWorld's own factions window, and that is not decoration.</b> Two places in the
    /// game open this tab and then cast whatever is open to <c>MainTabWindow_Factions</c>: the info card, when
    /// the thing being inspected is a faction, and the pawn table's faction icon. Both do
    /// <c>SetCurrentTab</c> first, which goes through <c>ToggleTab</c>, which is where our redirect swaps the
    /// def -- so the window they then cast is ours. Inheriting is what keeps that cast legal. Replacing the
    /// window without it turns two vanilla clicks into an invalid cast.
    ///
    /// <b>Nothing of the base is drawn.</b> <c>DoWindowContents</c> never calls up, so vanilla's list, its one
    /// heading and its eighty pixel rows are all replaced rather than layered over.
    ///
    /// <b>The scroll request is honoured through the base's own field.</b> <c>ScrollToFaction</c> is not
    /// virtual, so those two call sites set a private field on us; reading it here is what turns their request
    /// into our card opening. Failing to read it costs the scroll and nothing else, which is why it is a
    /// guarded read rather than a hard dependency.
    ///
    /// Both axes are clamped to the screen, for the reason the ideoligions tab needed it: a tab that asks for
    /// more than the display has cannot be dragged back into view.
    /// </summary>
    public class MainTabWindow_Factions : RimWorld.MainTabWindow_Factions
    {
        /// <summary>
        /// The base's own <c>scrollToFaction</c>, which vanilla's two call sites write into.
        ///
        /// Resolved once and left null when it cannot be found, which is what a rename in a future version
        /// would look like. The tab still opens; it just opens on the list rather than on the faction.
        /// </summary>
        private static readonly AccessTools.FieldRef<RimWorld.MainTabWindow_Factions, Faction> Requested =
            UIGuard.Try("Factions.ScrollField",
                () => AccessTools.FieldRefAccess<RimWorld.MainTabWindow_Factions, Faction>("scrollToFaction"),
                null,
                "The factions tab cannot follow a request to show one faction. It opens on the whole list "
                + "instead.");

        public override Vector2 RequestedTabSize
        {
            get
            {
                return UIGuard.Try("Factions.RequestedSize",
                    () => new Vector2(
                        Mathf.Min(FactionsPanel.WindowWidth, UI.screenWidth - 40f),
                        Mathf.Min(FactionsPanel.WindowHeight, UI.screenHeight - 90f)),
                    new Vector2(1000f, 640f), null);
            }
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Factions.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                Consume();

                FactionsPanel.Draw(inRect);
            }, "The factions tab shows a failure notice. No standing with anyone has been changed.");
        }

        /// <summary>
        /// Takes a pending "show me this faction" request and turns it into an open card.
        ///
        /// Cleared as it is read, the same as the base does, so the request opens the card once rather than
        /// dragging the selection back every frame the tab is up.
        /// </summary>
        private void Consume()
        {
            if (Requested == null)
                return;

            Faction faction = Requested(this);

            if (faction == null)
                return;

            Requested(this) = null;

            FactionsPanel.Reveal(faction);
        }
    }
}
