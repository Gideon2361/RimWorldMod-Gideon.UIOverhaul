using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
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
    /// The world map's bottom right corner, drawn as a panel like the colony map's.
    ///
    /// <b>The world map had been missed entirely.</b> RimWorld draws that corner from
    /// <c>WorldGlobalControls.WorldGlobalControlsOnGUI</c>, a different class from the one
    /// <see cref="GlobalControlsPanel"/> replaced -- so every Desktop Widget setting silently stopped applying
    /// the moment the player opened the planet, and the readouts went back to bare labels over the globe with
    /// nothing behind them. Reported on 2026-08-25 from a screenshot of exactly that.
    ///
    /// <b>The same panel, minus what a planet does not have and plus what only it does.</b> There is no weather
    /// and no temperature out here -- those are readings taken somewhere, and the world view is not anywhere --
    /// so the vitals row and the growing-season calendar are simply absent rather than blank. What is added is
    /// the route planner button and the compass, both of which are controls rather than readouts and both of
    /// which vanilla draws in this corner.
    ///
    /// <b>The date is not the current map's.</b> <c>DateReadout</c> takes its longitude from the selected tile
    /// first, then the first selected world object, and only then the current map, so selecting a settlement on
    /// the far side of the planet really does change the hour shown. That rule is reproduced in
    /// <see cref="LongLat"/>, because getting it wrong produces a readout that is confidently incorrect -- the
    /// same reason the colony panel borrows vanilla's temperature builder rather than writing its own.
    ///
    /// <b>Every row still answers to the same setting as its colony-map twin.</b> There is deliberately no second
    /// set of switches: a player who hid the speed controls hid the speed controls, and having to hide them twice
    /// because they walked out to the planet would be the settings page failing to mean anything.
    /// </summary>
    internal static class WorldControlsPanel
    {
        /// <summary>
        /// Draws the corner. Mirrors <see cref="GlobalControlsPanel.Draw"/>, bottom upward.
        /// </summary>
        internal static void Draw()
        {
            // Vanilla skips its whole corner on layout passes, letter stack included. Kept, because the letter
            // stack's own drawing assumes it.
            if (Event.current.type == EventType.Layout)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            UIOverhaulSettingsFile settings = UIGuard.Try("Panel.ReadWorldCornerSettings",
                () => UIOverhaulSettingsFile.Current, null,
                "The world corner shows every readout, which is the closest thing to vanilla.");

            bool playing = Current.ProgramState == ProgramState.Playing;

            float x = UI.screenWidth - GlobalControlsPanel.Width;

            // Vanilla starts at the bottom less four, and takes another 35 off while a game is running to clear
            // the main button bar. Ours reads that height from MainButtonDef rather than restating 35, the same
            // way the colony panel does, so the two corners and this mod's own button bar cannot drift apart.
            float y = UI.screenHeight - 4f;

            if (playing)
                y -= MainButtonDef.ButtonHeight;

            y = GlobalControlsPanel.DrawToggleRow(y, settings, true);
            y -= GlobalControlsPanel.BlockGap;

            if (playing)
            {
                y = MainBlock(x, y, palette, settings);

                // In the same place as on the colony corner: above the date, below the conditions. Music keeps
                // playing while the player is looking at the planet, so a strip that vanished out here would be
                // the readout disagreeing with the speakers.
                y = Music.MusicStrip.Draw(x, y, palette, settings);

                y = Conditions(x, y, palette, settings);
            }

            y = GlobalControlsPanel.DrawReadouts(x, y, null, palette, settings);

            y = Controls(y);

            if (!playing)
                return;

            y -= GlobalControlsPanel.LetterGap;

            // Where this corner ended, which is the anchor for anything docked at the bottom right. The world map
            // has its own letter stack position and the alert column has to follow it out here as well.
            NotificationLayout.Notify_CornerTop(y);

            Find.LetterStack.LettersOnGUI(y);
        }

        /// <summary>
        /// Speed controls and the date, on one panel.
        ///
        /// <b>No vitals row and no calendar,</b> unlike the colony corner: weather and temperature are readings
        /// taken at a place, and the growing-season bar is about one tile's soil. A planet has none of those, so
        /// the rows are absent rather than empty.
        /// </summary>
        private static float MainBlock(float x, float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            bool showSpeed = settings == null || settings.showSpeedControlsWidget;

            // Vanilla's own condition for the date out here: without a map or a selection there is nowhere to
            // take the reading, and DateReadout returns without drawing anything at all.
            bool anywhere = UIGuard.Try("Panel.WorldDatePlace",
                () => Find.CurrentMap != null || Find.WorldSelector.AnyObjectOrTileSelected, false, null);

            bool showDate = (settings == null || settings.showDateWidget) && anywhere;

            // The speed controls have to run even when hidden, because DoTimeControlsGUI handles the pause key
            // and the three speed shortcuts after it finishes drawing. Losing them on the world map would be the
            // same defect the colony corner already carries a note about.
            if (!showSpeed)
            {
                UIGuard.Try("Panel.WorldSpeedShortcuts", () =>
                    {
                        Vector2 size = TimeControls.TimeButSize;

                        TimeControls.DoTimeControlsGUI(new Rect(GlobalControlsPanel.OffScreen,
                            GlobalControlsPanel.OffScreen, size.x * 5f, size.y));
                    },
                    "The pause and speed keyboard shortcuts do not work on the world map while the speed "
                    + "controls are hidden.");
            }

            float dateHeight = showDate ? GlobalControlsPanel.DateHeight : 0f;
            float speedHeight = showSpeed ? TimeControls.TimeButSize.y : 0f;

            int rows = (showDate ? 1 : 0) + (showSpeed ? 1 : 0);

            if (rows == 0)
                return y;

            float height = GlobalControlsPanel.Pad * 2f + dateHeight + speedHeight
                           + GlobalControlsPanel.RowGap * (rows - 1);

            Rect block = new Rect(x, y - height, GlobalControlsPanel.Width, height);

            GlobalControlsPanel.PaintBlock(block, palette);

            float cursor = block.yMax - GlobalControlsPanel.Pad;

            if (showSpeed)
            {
                cursor -= speedHeight;

                Rect speed = new Rect(block.xMax - GlobalControlsPanel.Pad - TimeControls.TimeButSize.x * 5f,
                    cursor, TimeControls.TimeButSize.x * 5f, speedHeight);

                UIGuard.Try("Panel.WorldSpeedControls", () => TimeControls.DoTimeControlsGUI(speed),
                    "The speed controls are missing from the world corner. The pause and speed keys still work.");

                cursor -= GlobalControlsPanel.RowGap;
            }

            if (showDate)
            {
                cursor -= dateHeight;

                Rect date = new Rect(block.x + GlobalControlsPanel.Pad, cursor,
                    block.width - GlobalControlsPanel.Pad * 2f, dateHeight);

                UIGuard.Try("Panel.WorldDateReadout", () => GlobalControlsPanel.DrawDate(date, LongLat(), palette),
                    "The date is missing from the world corner.");
            }

            return block.y - GlobalControlsPanel.BlockGap;
        }

        /// <summary>
        /// Where on the planet the date is being read, following <c>DateReadout</c>'s own order.
        ///
        /// Selected tile, then the first selected object's tile, then the current map. Reproduced rather than
        /// borrowed because the vanilla method that applies it also draws, so there is no way to ask it the
        /// question without getting a readout as well.
        /// </summary>
        private static Vector2 LongLat()
        {
            return UIGuard.Try("Panel.WorldLongLat", () =>
            {
                WorldSelector selector = Find.WorldSelector;

                if (selector != null && selector.SelectedTile.Valid)
                    return Find.WorldGrid.LongLatOf(selector.SelectedTile);

                if (selector != null && selector.NumSelectedObjects > 0)
                    return Find.WorldGrid.LongLatOf(selector.FirstSelectedObject.Tile);

                return Find.CurrentMap != null
                    ? Shared.MapTile.LongLatOf(Find.CurrentMap)
                    : Vector2.zero;
            }, Vector2.zero, null);
        }

        /// <summary>
        /// The planet's own conditions, which are a different set from any one map's.
        ///
        /// <b>Read from <c>Find.World.gameConditionManager</c> rather than a map's.</b> A world condition -- a
        /// solar flare over the whole planet, say -- is held there, and a map's manager only carries what is
        /// happening at that map. The card is the colony panel's, unchanged, so a condition looks the same
        /// wherever the player happens to be standing when they read it.
        /// </summary>
        private static float Conditions(float x, float y, UIColorPaletteDef palette,
            UIOverhaulSettingsFile settings)
        {
            if (settings != null && !settings.showConditionsWidget)
                return y;

            List<GameCondition> conditions = UIGuard.Try("Panel.WorldConditionsList",
                () => Find.World?.gameConditionManager?.ActiveConditions, null,
                "The active conditions are missing from the world corner.");

            if (conditions == null || conditions.Count == 0)
                return y;

            // Newest at the bottom, nearest the rest of the panel, matching the colony corner.
            for (int i = conditions.Count - 1; i >= 0; i--)
            {
                GameCondition condition = conditions[i];

                if (condition == null)
                    continue;

                Rect card = new Rect(x, y - GlobalControlsPanel.ConditionHeight, GlobalControlsPanel.Width,
                    GlobalControlsPanel.ConditionHeight);

                UIGuard.Try("Panel.WorldCondition",
                    () => GlobalControlsPanel.DrawCondition(card, condition, palette),
                    "One of the active conditions is missing from the world corner.");

                y = card.y - GlobalControlsPanel.BlockGap;
            }

            return y;
        }

        /// <summary>
        /// The two world-only controls: the route planner and the compass.
        ///
        /// <b>Drawn bare and never hidden by a widget setting,</b> which is the same call the colony corner makes
        /// about its toggle row. These are controls, not readouts -- the route planner is how a caravan's path is
        /// chosen, and the compass is how a rotated globe is put back -- and a widget switch that quietly removed
        /// a control would be a settings page taking away a way to play. Both keep vanilla's own conditions:
        /// the planner is hidden while the world targeter is picking something, and the compass only appears when
        /// north is not locked up.
        /// </summary>
        private static float Controls(float y)
        {
            UIGuard.Try("Panel.WorldRoutePlannerButton", () =>
            {
                if (Find.WorldTargeter != null && !Find.WorldTargeter.IsTargeting)
                    Find.WorldRoutePlanner.DoRoutePlannerButton(ref y);
            }, "The route planner button is missing from the world corner. The planner is still reachable from "
               + "a caravan.");

            UIGuard.Try("Panel.WorldCompass", () =>
            {
                if (Find.PlaySettings != null && !Find.PlaySettings.lockNorthUp)
                    CompassWidget.CompassOnGUI(ref y);
            }, "The compass is missing from the world corner.");

            return y;
        }
    }

    /// <summary>
    /// Replaces the world map's bottom right corner with <see cref="WorldControlsPanel"/>.
    ///
    /// <b>Why the whole method, as with the colony corner.</b> <c>WorldGlobalControlsOnGUI</c> walks a cursor up
    /// the right hand side and draws everything against it, and the conditions block is inline -- its rect is
    /// built by the caller rather than inside the call -- so there is no seam to hide that row at. It also owns
    /// where the world's letter stack begins, which is what makes letter docking work out here.
    ///
    /// <b>Guarded with <c>TryOnce</c>.</b> This draws several nested groups through vanilla's own readouts, and a
    /// throw partway through one leaves Unity's clip stack unbalanced for everything after it. Retrying every
    /// frame would repeat that indefinitely, so the site retires on its first failure and vanilla's own corner
    /// takes over for the rest of the session.
    ///
    /// The stand-down is real rather than nominal: the hide patches on <c>GlobalControlsUtility</c> are shared
    /// with the colony corner and fire on this path too, so a player whose world panel has retired still has
    /// working settings for the speed controls, the date, the clock and the toggle row.
    ///
    /// A prefix returns false to suppress the original, so success is false here and failure is true.
    /// </summary>
    [HarmonyPatch(typeof(WorldGlobalControls), nameof(WorldGlobalControls.WorldGlobalControlsOnGUI))]
    public static class Patch_WorldGlobalControls_OnGUI
    {
        public static bool Prefix()
        {
            return !UIGuard.TryOnce("Panel.WorldCorner", WorldControlsPanel.Draw,
                "The world map's bottom right corner is drawn RimWorld's own way for the rest of this session, "
                + "and the conditions cannot be hidden while it is.");
        }
    }
}
