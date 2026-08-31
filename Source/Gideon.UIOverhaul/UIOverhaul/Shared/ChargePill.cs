using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>Which way stored energy is going, which is what the pill says.</summary>
    internal enum ChargeFlow
    {
        /// <summary>Nothing left. Not a direction, and it outranks one.</summary>
        Empty,

        /// <summary>At capacity. Also not a direction: there is nowhere for a gain to go.</summary>
        Full,

        Charging,

        Draining
    }

    /// <summary>
    /// The little pill that says which way a battery or a grid is going, and moves the way it is going.
    ///
    /// <b>The motion is the state, not decoration.</b> Fading the letters up and down, which is what this did
    /// first, is the one animation that cannot encode anything: no direction in it and no shape to read, so
    /// every state moved identically. It also looked like a rendering fault, because text going dim and bright
    /// is what a broken draw does. Aaron picked the sweep on 2026-08-30 from four candidates.
    ///
    /// <b>So charging sweeps right and draining sweeps left,</b> and the direction of travel is the reading.
    /// Empty blinks, because empty is not a direction. Full breathes, because full is a place rather than a
    /// movement.
    ///
    /// <b>Shared rather than copied.</b> The inspect pane says this about one battery and the power tab says
    /// it about a whole grid; they are the same sentence about different amounts of the same thing, and two
    /// implementations of it would have drifted the first time either was touched.
    /// </summary>
    internal static class ChargePill
    {
        private const float SweepPeriod = 1.9f;
        private const float BreathePeriod = 2.4f;
        private const float BlinkPeriod = 1.15f;

        /// <summary>How many slices the sweep is drawn in.</summary>
        private const int Slices = 7;

        /// <summary>
        /// Which state a store is in, from what it holds and which way it is moving.
        ///
        /// <b>Empty and full both outrank the direction.</b> A store at zero on a gaining grid is still a
        /// store with nothing in it; one at capacity is not charging, because there is nowhere for the gain to
        /// go. A store below capacity with no gain reads as draining, which is honest: a battery
        /// self-discharges whatever the grid is doing.
        /// </summary>
        internal static ChargeFlow Flow(float stored, float capacity, float watts)
        {
            if (stored <= 0.01f)
                return ChargeFlow.Empty;

            if (capacity > 0f && stored / capacity >= 0.999f)
                return ChargeFlow.Full;

            return watts > 0f ? ChargeFlow.Charging : ChargeFlow.Draining;
        }

        internal static string Word(ChargeFlow flow)
        {
            switch (flow)
            {
                case ChargeFlow.Empty: return "EMPTY";
                case ChargeFlow.Full: return "FULL";
                case ChargeFlow.Charging: return "CHARGE";
                default: return "DRAIN";
            }
        }

        internal static Color Tint(ChargeFlow flow, UIColorPaletteDef palette)
        {
            switch (flow)
            {
                case ChargeFlow.Empty: return palette.Danger;
                case ChargeFlow.Full: return palette.Accent;
                case ChargeFlow.Charging: return palette.Success;
                default: return palette.Warning;
            }
        }

        /// <summary>How wide the pill will be, so a caller can lay a bar out beside it.</summary>
        internal static float Width(ChargeFlow flow, float points)
        {
            return TabParts.PillWidth(Word(flow), 9999f, UIFace.IBMPlexMono, points);
        }

        /// <summary>How tall it will be, so a row can be sized to hold it.</summary>
        internal static float Height(float points)
        {
            return UIFramework.Controls.UITextControl.LineHeight(UIFace.IBMPlexMono, points) + 2f;
        }

        /// <summary>
        /// Draws it, and answers the rect it took.
        ///
        /// <paramref name="band"/> is only there because <c>TabParts.Pill</c> asks for one; the pill is placed
        /// at <paramref name="x"/> and <paramref name="y"/>.
        /// </summary>
        internal static Rect Draw(Rect band, float x, float y, ChargeFlow flow, UIColorPaletteDef palette,
            float points)
        {
            Color tint = Tint(flow, palette);
            string word = Word(flow);

            float clock = UIGuard.Try("Charge.Clock", () => Time.realtimeSinceStartup, 0f, null);

            // Blink is the one state that changes the pill itself rather than overlaying it, because what it
            // wants to say is that the pill is going away.
            Color drawn = flow == ChargeFlow.Empty && clock % BlinkPeriod > BlinkPeriod * 0.62f
                ? UIElementPainter.Composite(palette.PanelBackground,
                    new Color(tint.r, tint.g, tint.b, 0.34f))
                : tint;

            Rect pill = TabParts.Pill(band, x, y, word, drawn, palette, 9999f, null, UIFace.IBMPlexMono,
                points);

            Highlight(pill, tint, flow, clock);

            return pill;
        }

        /// <summary>
        /// The moving part, drawn over the finished pill.
        ///
        /// <b>An overlay rather than a second copy of the pill.</b> Redrawing the pill brighter behind a clip
        /// would mean measuring and laying out the text again every frame; a translucent wash over the top
        /// lifts the border, the fill and the letters together, which is the whole point of the sweep, and
        /// costs no text work at all.
        ///
        /// <b>The band is drawn in slices with a sine of alpha across it,</b> because a single rect gives a
        /// hard-edged block sliding past rather than a highlight. Seven is enough that the edges are not
        /// countable at this size and few enough that the whole effect is seven filled rects.
        ///
        /// <b>It never leaves the pill.</b> Each slice is clamped to the pill's own rect, so the highlight
        /// arrives and departs by being cut off at the ends rather than by overhanging the border.
        /// </summary>
        private static void Highlight(Rect pill, Color tint, ChargeFlow flow, float clock)
        {
            if (flow == ChargeFlow.Empty)
                return;

            if (flow == ChargeFlow.Full)
            {
                float swell = 0.06f + 0.16f * (1f - Mathf.Cos(clock / BreathePeriod * Mathf.PI * 2f)) * 0.5f;

                Widgets.DrawBoxSolid(pill.ContractedBy(1f), new Color(tint.r, tint.g, tint.b, swell));

                return;
            }

            float width = Mathf.Max(10f, pill.width * 0.42f);
            float travel = pill.width + width;
            float phase = clock % SweepPeriod / SweepPeriod;

            if (flow == ChargeFlow.Draining)
                phase = 1f - phase;

            float head = pill.x - width + travel * phase;

            for (int i = 0; i < Slices; i++)
            {
                float across = (i + 0.5f) / Slices;

                // Brightest in the middle of the band and nothing at either end, so it reads as a highlight
                // passing over rather than as a block sliding past.
                float alpha = Mathf.Sin(across * Mathf.PI) * 0.3f;

                float left = head + width * across - width / (Slices * 2f);
                float right = left + width / Slices;

                left = Mathf.Max(left, pill.x + 1f);
                right = Mathf.Min(right, pill.xMax - 1f);

                if (right <= left)
                    continue;

                Widgets.DrawBoxSolid(new Rect(left, pill.y + 1f, right - left, pill.height - 2f),
                    new Color(tint.r, tint.g, tint.b, alpha));
            }
        }
    }
}
