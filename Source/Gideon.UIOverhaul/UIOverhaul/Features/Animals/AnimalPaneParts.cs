using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The pieces both animal panes are built from: a section heading, a fact, and a chip that opens something.
    ///
    /// <b>Extracted when the second pane arrived</b> on 2026-08-22. The species card had these as private methods,
    /// and the individual animal's card needs the same three shapes: two copies would have read as two designers
    /// working on one panel, and the first divergence would have been somebody adjusting a gap in one of them.
    /// <see cref="AnimalSpeciesPane"/> keeps its own names for them and forwards here, so its call sites are
    /// unchanged and there is still exactly one implementation.
    /// </summary>
    internal static class AnimalPaneParts
    {
        internal const float RowGap = 3f;

        /// <summary>A rule with a small caps label under it, which is what divides a pane into sections.</summary>
        internal static float Heading(Rect view, float y, string text, UIColorPaletteDef palette)
        {
            GUI.color = palette.Border;

            Widgets.DrawLineHorizontal(view.x, y, view.width);

            y += 6f;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextDisabled;

            Widgets.Label(new Rect(view.x, y, view.width, UIFonts.LineHeightOf(GameFont.Tiny)), text);

            return y + UIFonts.LineHeightOf(GameFont.Tiny) + 2f;
        }

        /// <summary>One fact: a name on the left and its value on the right, which is most of either pane.</summary>
        internal static float Pair(Rect view, float y, string name, string value, Color color,
            UIColorPaletteDef palette)
        {
            float height = UIFonts.LineHeightOf(GameFont.Small);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = palette.TextSecondary;

            Widgets.LabelEllipses(new Rect(view.x, y, view.width * 0.55f, height), name);

            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = color;

            Widgets.LabelEllipses(new Rect(view.x + view.width * 0.55f, y, view.width * 0.45f, height), value);

            Text.Anchor = TextAnchor.UpperLeft;

            return y + height + RowGap;
        }

        /// <summary>
        /// A setting as a chip: what it is on the left, what it currently says on the right, and a click that
        /// opens the picker.
        ///
        /// <b>The same shape as the pawns tab's policy chips and the animal row's master chip,</b> deliberately: a
        /// player who has learned that a bordered pill with a value in it opens a list has learned it everywhere in
        /// this mod. The caption is dim and the value bright, because the value is what somebody scanning the card
        /// is reading.
        /// </summary>
        /// <param name="reason">
        /// Why this cannot be set, or null when it can. A chip with a reason is drawn dead and says the reason in
        /// place of its value, which is how a card explains a control it is not offering rather than hiding it and
        /// leaving the player to wonder.
        /// </param>
        internal static float Chip(Rect view, float y, string caption, string value, UIColorPaletteDef palette,
            Action open, string reason = null)
        {
            Rect chip = new Rect(view.x, y, view.width, 26f);
            bool live = reason == null && open != null;
            bool over = live && Mouse.IsOver(chip);

            // <b>A null value is drawn as a dash rather than thrown at IMGUI.</b> Several of the readers behind
            // these chips answer null for "nothing to report": GroupActions.Shared does when no member has an
            // answer, and the pen reader did when there was no pen. Widgets.LabelEllipses does not survive a null,
            // and it took the species panel down on 2026-08-22. The readers are being fixed where the null was
            // wrong; this is here so the next one that slips through costs a dash instead of the panel.
            if (value.NullOrEmpty())
                value = "-";

            UIElementPainter.OutlineRounded(chip, over ? palette.Accent : palette.Border,
                over ? palette.SurfaceRaised : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = palette.TextDisabled;

                float captionWidth = Mathf.Min(chip.width * 0.5f, Text.CalcSize(caption).x + 4f);

                Widgets.Label(new Rect(chip.x + 8f, chip.y, captionWidth, chip.height), caption);

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = live ? palette.TextPrimary : palette.TextDisabled;

                Rect right = new Rect(chip.x + 8f + captionWidth, chip.y,
                    Mathf.Max(0f, chip.width - captionWidth - 22f), chip.height);

                Widgets.LabelEllipses(right, live ? value : reason);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (live && TexButton.Reveal != null)
            {
                GUI.color = over ? palette.Accent : palette.TextDisabled;

                GUI.DrawTexture(new Rect(chip.xMax - 16f, chip.center.y - 5f, 10f, 10f), TexButton.Reveal);

                GUI.color = Color.white;
            }

            if (live && Widgets.ButtonInvisible(chip))
                open();

            return chip.yMax + RowGap;
        }

        /// <summary>
        /// A switch for a setting that belongs to several animals at once, which can therefore disagree with
        /// itself.
        ///
        /// <b>Three states, because a herd has three answers.</b> All of them, none of them, or some: a plain
        /// checkbox reading one member's answer as if it spoke for the group is the one thing a group control
        /// must not do, since the whole reason to have one is to avoid opening every animal to find out. Partial
        /// resolves upwards on click, so the gesture is always "make them all do this" until they all are.
        ///
        /// The switch is the framework's own at the framework's own size, so a species setting looks like every
        /// other setting in this mod. Only the tri-state and the resolution rule are here.
        /// </summary>
        /// <param name="write">Given the value every member should take.</param>
        internal static float TriToggle(Rect view, float y, string label, MultiCheckboxState state, bool enabled,
            UIColorPaletteDef palette, Action<bool> write, string tooltip = null)
        {
            Rect row = new Rect(view.x, y, view.width, 24f);
            bool hover = enabled && Mouse.IsOver(row);

            if (hover)
                Widgets.DrawBoxSolid(row, palette.HoverOverlay);

            Rect box = new Rect(row.x + 4f, row.y + (row.height - UICheckboxControl.BoxSize) * 0.5f,
                UICheckboxControl.BoxWidth, UICheckboxControl.BoxSize);

            UICheckboxControl.DrawBox(box, state, palette, !enabled);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = !enabled ? palette.TextDisabled : hover ? palette.TextPrimary : palette.TextSecondary;

                Widgets.LabelEllipses(new Rect(box.xMax + 8f, row.y, Mathf.Max(0f, row.xMax - box.xMax - 12f),
                    row.height), label);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(row, (TipSignal) tooltip);

            // Consumed either way, so a dead switch does not let the click through to whatever is underneath and
            // so the button's id is allocated on every frame rather than only on the frames it is live.
            bool clicked = Widgets.ButtonInvisible(row);

            if (clicked && enabled)
            {
                bool wanted = state != MultiCheckboxState.On;

                write(wanted);

                (wanted ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            }

            return row.yMax + RowGap;
        }

        /// <summary>
        /// How a group stands on one boolean: all, none, or some.
        ///
        /// Members the question does not apply to are skipped rather than counted as a no, which is what stops a
        /// herd with one juvenile in it reading as permanently mixed.
        /// </summary>
        internal static MultiCheckboxState StateOf(List<Pawn> members, Func<Pawn, bool?> read)
        {
            int on = 0;
            int counted = 0;

            for (int i = 0; i < members.Count; i++)
            {
                bool? answer = read(members[i]);

                if (!answer.HasValue)
                    continue;

                counted++;

                if (answer.Value)
                    on++;
            }

            if (counted == 0 || on == 0)
                return MultiCheckboxState.Off;

            return on >= counted ? MultiCheckboxState.On : MultiCheckboxState.Partial;
        }
    }
}
