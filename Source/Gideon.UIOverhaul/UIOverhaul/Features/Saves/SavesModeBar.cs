using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// The Save and Load toggle both save windows carry, and the rules about which of them can be used.
    ///
    /// <b>Shared so the two windows cannot disagree.</b> They are the same feature seen from two directions,
    /// and a toggle written twice would eventually be enabled in one and not the other.
    /// </summary>
    internal static class SavesModeBar
    {
        private const float ButtonWidth = 74f;
        private const float Gap = 4f;

        /// <summary>Width the bar needs, so a title row can leave room for it.</summary>
        internal const float Width = ButtonWidth * 2f + Gap;

        /// <summary>
        /// Whether a game can be saved at all right now.
        ///
        /// <b>Three separate reasons it cannot,</b> and they are worth keeping apart because each has its own
        /// sentence. There is no colony at the main menu; a colony in permadeath saves itself and refuses a
        /// manual save on purpose, which is the commitment mode chosen when the world was made; and the game
        /// blocks saving outright at certain moments, which is <c>Current.Game.Info.permadeathMode</c>'s
        /// neighbour rather than the same thing.
        /// </summary>
        internal static bool CanSave(out string why)
        {
            why = null;

            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
            {
                why = "There is no colony to save. Load one first.";

                return false;
            }

            bool permadeath = UIGuard.Try("Saves.ReadPermadeath",
                () => Find.GameInfo != null && Find.GameInfo.permadeathMode, false, null);

            if (permadeath)
            {
                why = "This colony is in commitment mode, so it saves itself and cannot be saved by hand.";

                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether another save can be loaded right now.
        ///
        /// Allowed everywhere except commitment mode, which exists precisely to stop somebody loading their
        /// way out of a decision.
        /// </summary>
        internal static bool CanLoad(out string why)
        {
            why = null;

            bool permadeath = UIGuard.Try("Saves.ReadPermadeath",
                () => Current.ProgramState == ProgramState.Playing && Find.GameInfo != null
                                                                   && Find.GameInfo.permadeathMode,
                false, null);

            if (!permadeath)
                return true;

            why = "This colony is in commitment mode, so another save cannot be loaded.";

            return false;
        }

        /// <summary>
        /// Draws the toggle. Switching mode closes the window that drew it and opens the other.
        /// </summary>
        /// <param name="saving">Which window is asking, so it can draw itself as the selected one.</param>
        /// <param name="owner">Closed when the other mode is chosen.</param>
        internal static void Draw(Rect rect, bool saving, Window owner, UIColorPaletteDef palette)
        {
            string saveWhy;
            string loadWhy;

            bool canSave = CanSave(out saveWhy);
            bool canLoad = CanLoad(out loadWhy);

            Rect save = new Rect(rect.x, rect.y, ButtonWidth, rect.height);
            Rect load = new Rect(save.xMax + Gap, rect.y, ButtonWidth, rect.height);

            if (Tab(save, "Save", saving, canSave, saveWhy, palette) && !saving)
                Switch(owner, new Dialog_SaveGame());

            if (Tab(load, "Load", !saving, canLoad, loadWhy, palette) && saving)
                Switch(owner, new Dialog_LoadGame());
        }

        private static void Switch(Window owner, Window opening)
        {
            UIGuard.Try("Saves.SwitchMode", () =>
            {
                // Opened before the old one closes, so the stack never momentarily has neither and something
                // behind them takes the focus.
                Find.WindowStack.Add(opening);

                if (owner != null)
                    Find.WindowStack.TryRemove(owner, false);

                SoundDefOf.Click.PlayOneShotOnCamera();
            }, "That mode could not be opened.");
        }

        /// <summary>
        /// One segment.
        ///
        /// <b>Unavailable is drawn differently from unselected,</b> which is the correction made on the XML
        /// Workbench's tabs and repeated here for the same reason: an unselected segment on a faded body with
        /// dimmed text reads as broken rather than as the other half of a choice. Unselected keeps full
        /// strength text on a panel; only genuinely unavailable gets the faded treatment, and it carries a
        /// tooltip saying why.
        /// </summary>
        private static bool Tab(Rect rect, string label, bool selected, bool available, string why,
            UIColorPaletteDef palette)
        {
            bool over = available && Mouse.IsOver(rect);

            if (selected)
            {
                // <b>A tab, not a button.</b> The selected segment used to be a filled accent pill, which put a
                // bright blue block in the title row while the footer's action button was another one. Two of
                // them at opposite ends of the same window read as two primary actions, and the player is left
                // working out which of them commits. The accent survives as a two pixel rule under the label,
                // which says "selected" without claiming to be the thing that acts.
                UIElementPainter.FillRounded(rect, palette.PanelBackground);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), palette.Accent);
            }
            else if (!available)
                UIElementPainter.OutlineRounded(rect, palette.Border, palette.ControlBackgroundFaded);
            else
                UIElementPainter.OutlineRounded(rect, palette.Border,
                    over ? palette.SurfaceRaised : palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            // Selected and unselected share one text color on purpose, which is the earlier decision this file
            // already records: dimming the unselected half made it read as broken rather than as the other side
            // of a choice. With the accent no longer filling the body, the rule underneath is what marks the
            // selection, so the text does not have to. Only genuinely unavailable is faded.
            GUI.color = available ? palette.TextPrimary : palette.TextDisabled;

            Widgets.Label(rect, label);

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (!available && !why.NullOrEmpty())
                TooltipHandler.TipRegion(rect, (TipSignal) why);

            return available && Widgets.ButtonInvisible(rect);
        }
    }
}
