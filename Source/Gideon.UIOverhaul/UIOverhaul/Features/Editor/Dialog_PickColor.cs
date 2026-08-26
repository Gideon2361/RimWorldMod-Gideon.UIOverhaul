using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Every colour the styling station offers, in a grid.
    ///
    /// <b>The editor used to show six.</b> They were <c>PawnHairColors</c>' named statics, which is what that
    /// class exposes -- the rest of RimWorld's palette lives in <c>ColorDef</c>s that the styling station reads
    /// and nothing else did. So the editor could set a colour the game itself offers freely, and could not.
    /// Reported on 2026-08-25.
    ///
    /// <b>The list is built exactly as the styling station builds it,</b> including its own deduplication: any
    /// colour within a 0.15 difference of one already taken is dropped, which is what stops a dozen browns that
    /// nobody can tell apart. Reproduced rather than delegated because
    /// <c>Dialog_StylingStation.AllHairColors</c> is private, and it is four lines of list building rather than
    /// any rule about the game.
    ///
    /// <b>Drawn by <c>Widgets.ColorSelector</c>,</b> which is vanilla's own swatch grid -- so the squares, the
    /// spacing and the ring on the chosen one are the ones a player already knows from the styling station.
    /// </summary>
    internal sealed class Dialog_PickColor : Window
    {
        private const float HeaderHeight = 28f;

        private const float FooterHeight = 34f;

        private static List<Color> cached;

        private readonly string heading;

        private readonly Action<Color> chosen;

        private Color colour;

        private float gridHeight = 200f;

        private Vector2 scroll;

        private Dialog_PickColor(string heading, Color current, Action<Color> chosen)
        {
            this.heading = heading;
            this.chosen = chosen;

            colour = current;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = false;
            drawShadow = true;
        }

        internal static void Open(string heading, Color current, Action<Color> chosen)
        {
            Find.WindowStack.Add(new Dialog_PickColor(heading, current, chosen));
        }

        /// <summary>
        /// The styling station's colours, built once.
        ///
        /// <b>Cached because it is a def scan with a quadratic dedupe in it,</b> and it cannot change while a game
        /// is running: defs are loaded before any of this is reachable.
        /// </summary>
        internal static List<Color> Colors
        {
            get
            {
                if (cached != null)
                    return cached;

                cached = UIGuard.Try("Editor.ColorList", Build, new List<Color>(), null);

                return cached;
            }
        }

        private static List<Color> Build()
        {
            List<Color> found = new List<Color>();

            foreach (ColorDef def in DefDatabase<ColorDef>.AllDefs)
            {
                if (!def.displayInStylingStationUI)
                    continue;

                Color colour = def.color;
                bool near = false;

                for (int i = 0; i < found.Count; i++)
                {
                    if (!found[i].WithinDiffThresholdFrom(colour, 0.15f))
                        continue;

                    near = true;

                    break;
                }

                if (!near)
                    found.Add(colour);
            }

            found.SortByColor(colour => colour);

            return found;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(420f, 460f); }
        }

        /// <summary>At the cursor, as the other pickers are. See <see cref="Dialog_PickFrom"/>.</summary>
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            Vector2 mouse = UI.MousePositionOnUIInverted;

            windowRect = new Rect(
                Mathf.Clamp(mouse.x, 0f, UI.screenWidth - size.x),
                Mathf.Clamp(mouse.y, 0f, UI.screenHeight - size.y),
                size.x, size.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.ColorPicker", inRect, () => Contents(inRect),
                "The colour picker failed to draw. Nothing has been changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), heading);

                GUI.color = Color.white;

                Rect list = new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width,
                    Mathf.Max(0f, inRect.height - HeaderHeight - FooterHeight - 4f));

                Rect view = new Rect(0f, 0f, list.width - 18f, gridHeight);

                Widgets.BeginScrollView(list, ref scroll, view);

                List<Color> colours = Colors;

                float measured;

                // Its return value says a swatch was clicked this frame. Taken immediately rather than on a
                // confirm button: a colour picker that needs an OK is a colour picker you cannot try things in.
                if (Widgets.ColorSelector(new Rect(0f, 0f, view.width, gridHeight), ref colour, colours,
                        out measured))
                {
                    Take();
                }

                // Measured on layout only. Reading it every frame makes the view rect chase the content and the
                // scroll bar flicker between two heights.
                if (Event.current.type == EventType.Layout && measured > 0f)
                    gridHeight = measured;

                Widgets.EndScrollView();

                if (TabParts.Button(new Rect(inRect.xMax - 90f, inRect.yMax - FooterHeight, 90f, 28f), "Cancel",
                        palette))
                    Close();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        private void Take()
        {
            Color picked = colour;

            Close();

            if (chosen == null)
                return;

            UIGuard.Try("Editor.ColorChosen", () => chosen(picked), "That colour could not be set.");
        }
    }
}
