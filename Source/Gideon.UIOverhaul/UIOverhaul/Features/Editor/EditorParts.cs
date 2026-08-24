using System;
using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The editor's form vocabulary: headings, labelled fields, pickers, swatches and sliders.
    ///
    /// <b>A form, which nothing else in this mod is.</b> Every other window here is a list of live facts with
    /// controls attached; this one is twelve panels of fields that each read one value and write one value, and
    /// they have to look like each other or the window reads as twelve windows. So the layout is fixed here and
    /// the panels only say what goes in it.
    ///
    /// <b>A caption above its control, never beside it.</b> Beside means choosing a split, and the labels here
    /// run from "hair" to "resurrection psychosis" -- any fraction that suits one starves the other. See the
    /// note in <see cref="Gideon.UIOverhaul.Shared.TabParts"/> on the same decision.
    /// </summary>
    internal static class EditorParts
    {
        /// <summary>Height of one control: a picker, a text box, a segment strip.</summary>
        internal const float ControlHeight = 26f;

        internal const float RowGap = 8f;

        internal const float BlockGap = 14f;

        /// <summary>Gap between fields laid side by side.</summary>
        internal const float FieldGap = 10f;

        internal static float CaptionHeight
        {
            get { return TabParts.CaptionHeight; }
        }

        /// <summary>A whole field: its caption and the control under it.</summary>
        internal static float FieldHeight
        {
            get { return CaptionHeight + 2f + ControlHeight; }
        }

        // ---------------------------------------------------------------------------------------
        // Structure
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// A block heading, with an optional note pushed to the right.
        ///
        /// The note is where a fact about the whole block goes -- "4 of 8 layers used", "not applied, Clean is
        /// selected". It is a fact rather than an instruction, which is the test for whether a line belongs on
        /// screen at all.
        /// </summary>
        internal static float Heading(Rect view, float y, string text, UIColorPaletteDef palette,
            string note = null, Color? noteColor = null)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                GUI.color = palette.Border;

                Widgets.DrawLineHorizontal(view.x, y, view.width);

                float top = y + 5f;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextPrimary;

                float height = UIFonts.LineHeightOf(GameFont.Small);

                float room = view.width;

                if (!note.NullOrEmpty())
                {
                    Text.Font = GameFont.Tiny;

                    float noteWidth = UIRichText.WidthOf(note) + 4f;

                    room = Mathf.Max(60f, view.width - noteWidth - 8f);

                    Text.Anchor = TextAnchor.UpperRight;
                    GUI.color = noteColor ?? palette.TextDisabled;

                    UIRichText.Label(new Rect(view.xMax - noteWidth, top + 2f, noteWidth, height), note);

                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = palette.TextPrimary;
                }

                UIRichText.Label(new Rect(view.x, top, room, height), text);

                return top + height + 6f;
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// A paragraph, as tall as it actually is.
        ///
        /// Measured rather than guessed, for the reason spelled out on <c>TabParts.Note</c>: a literal height is
        /// right until the wording grows and then the last line and a half is simply gone.
        /// </summary>
        internal static float Note(Rect view, float y, string text, UIColorPaletteDef palette, Color? color = null)
        {
            if (text.NullOrEmpty())
                return y;

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = color ?? palette.TextDisabled;

                float height = Text.CalcHeight(text, view.width);

                Widgets.Label(new Rect(view.x, y, view.width, height), text);

                return y + height + 4f;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Splits a row into equal fields.
        ///
        /// Equal rather than measured, because a form whose columns move as its values change is a form whose
        /// controls are never in the same place twice.
        /// </summary>
        internal static Rect Column(Rect row, int index, int of)
        {
            if (of <= 1)
                return row;

            float width = Mathf.Floor((row.width - (of - 1) * FieldGap) / of);

            return new Rect(row.x + index * (width + FieldGap), row.y, width, row.height);
        }

        /// <summary>The caption over a control, and the rect the control gets.</summary>
        internal static Rect Field(Rect cell, string caption, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                GUI.color = palette.TextDisabled;

                UIRichText.Label(new Rect(cell.x + 2f, cell.y, Mathf.Max(10f, cell.width - 4f), CaptionHeight),
                    caption ?? string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            return new Rect(cell.x, cell.y + CaptionHeight + 2f, cell.width, ControlHeight);
        }

        // ---------------------------------------------------------------------------------------
        // Controls
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// A field whose value opens a picker.
        ///
        /// <b>It shows what it is set to without being touched,</b> which is the whole reason this is a button
        /// carrying its value rather than a labelled button that opens a menu. The arrow is the only part that
        /// says "there is a list behind this".
        /// </summary>
        internal static bool Picker(Rect cell, string caption, string value, UIColorPaletteDef palette,
            string tooltip = null, bool enabled = true)
        {
            Rect control = Field(cell, caption, palette);

            bool over = enabled && Mouse.IsOver(control);

            UIElementPainter.OutlineRounded(control, over ? palette.BorderFocused : palette.Border,
                !enabled ? palette.PanelBackground : over ? palette.SurfaceRaised : palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = enabled ? palette.TextPrimary : palette.TextDisabled;

                UIRichText.Label(new Rect(control.x + 6f, control.y, Mathf.Max(10f, control.width - 24f),
                    control.height), value ?? "none");

                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(control.xMax - 18f, control.y, 12f, control.height), "v");
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(control, (TipSignal) tooltip);

            if (!enabled || !Widgets.ButtonInvisible(control))
                return false;

            SoundDefOf.Click.PlayOneShotOnCamera();

            return true;
        }

        /// <summary>
        /// A strip of exclusive choices, all of them visible.
        ///
        /// Segments rather than a float menu everywhere a set is small and fixed, which is the standing
        /// preference across this mod: a menu hides the whole choice behind a click and carries no state back.
        /// Returns the index chosen, or -1 when nothing was.
        /// </summary>
        internal static int Segments(Rect cell, string caption, string[] labels, int current,
            UIColorPaletteDef palette)
        {
            Rect control = Field(cell, caption, palette);

            int chosen = -1;

            float width = Mathf.Floor((control.width - (labels.Length - 1) * 3f) / labels.Length);

            for (int i = 0; i < labels.Length; i++)
            {
                Rect slot = new Rect(control.x + i * (width + 3f), control.y, width, control.height);

                int index = i;

                TabParts.Segment(slot, labels[i], current == i, palette, () => chosen = index);
            }

            return chosen;
        }

        /// <summary>
        /// A row of colour swatches with the current one ringed.
        ///
        /// <b>Swatches rather than three number boxes,</b> because a skin tone or a hair colour is chosen by
        /// looking at it. Returns the colour picked, or null when none was.
        /// </summary>
        internal static Color? Swatches(Rect cell, string caption, IList<Color> options, Color current,
            UIColorPaletteDef palette)
        {
            Rect control = Field(cell, caption, palette);

            Color? chosen = null;

            if (options == null || options.Count == 0)
                return null;

            float side = Mathf.Min(control.height, Mathf.Floor((control.width - (options.Count - 1) * 4f)
                                                               / options.Count));

            side = Mathf.Max(10f, side);

            for (int i = 0; i < options.Count; i++)
            {
                Rect swatch = new Rect(control.x + i * (side + 4f), control.y + (control.height - side) * 0.5f,
                    side, side);

                if (swatch.xMax > control.xMax)
                    break;

                bool on = Near(options[i], current);

                // The ring is the border colour rather than the accent: a selected swatch has to be legible
                // against every colour in the row, and an accent ring vanishes on an accent-coloured swatch.
                UIElementPainter.OutlineRounded(swatch, on ? palette.TextPrimary : palette.Border, options[i],
                    on ? 2f : 1f);

                if (Widgets.ButtonInvisible(swatch))
                    chosen = options[i];
            }

            return chosen;
        }

        /// <summary>Whether two colours are the same to the precision a swatch can show.</summary>
        internal static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
        }

        /// <summary>
        /// A slider with its value on the caption line.
        ///
        /// Returns the new value, which equals the old one on any frame it was not dragged. Callers compare
        /// before recording, since a slider reports its value every frame whether or not it moved.
        /// </summary>
        internal static float Slider(Rect cell, string caption, float value, float min, float max,
            UIColorPaletteDef palette, string readout = null)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;

                GUI.color = palette.TextDisabled;
                Text.Anchor = TextAnchor.UpperLeft;

                UIRichText.Label(new Rect(cell.x + 2f, cell.y, Mathf.Max(10f, cell.width - 60f), CaptionHeight),
                    caption ?? string.Empty);

                GUI.color = palette.TextSecondary;
                Text.Anchor = TextAnchor.UpperRight;

                Widgets.Label(new Rect(cell.xMax - 56f, cell.y, 54f, CaptionHeight),
                    readout ?? value.ToString("0.##"));
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Rect lane = new Rect(cell.x, cell.y + CaptionHeight + 6f, cell.width, 14f);

            return Widgets.HorizontalSlider(lane, value, min, max);
        }

        /// <summary>
        /// A row that names something and offers to remove it.
        ///
        /// The shape every list in this window uses -- traits, hediffs, memories, apparel, relations -- so a
        /// player learns one row and reads five panels. Returns true when the remove button was pressed.
        /// </summary>
        /// <summary>
        /// One row of a list: a name, an optional note on the right, and an optional remove cross.
        ///
        /// <b><paramref name="icon"/> turns the row into a card,</b> asked for on 2026-08-23 against the
        /// equipment panel: a weapon, a coat and a stack of wood are things with pictures, and three lines of
        /// grey text is the same complaint the gene list drew. Rows that name something without a picture --
        /// a hediff, a memory, a relation -- pass nothing and are drawn exactly as before, because there is no
        /// icon for "ate without a table" and a blank square would be worse than none.
        ///
        /// <paramref name="tint"/> is the colour to draw the icon in, which for apparel is its dye or its
        /// material. Null leaves the icon its own colour.
        /// </summary>
        internal static bool Row(Rect view, float y, string left, string right, Color rightColor,
            UIColorPaletteDef palette, out Rect row, string tooltip = null, bool removable = true,
            ThingDef icon = null, Color? tint = null)
        {
            float height = Mathf.Max(24f, UIFonts.LineHeightOf(GameFont.Small) + 4f);

            if (icon != null)
                height = Mathf.Max(height, IconSize + 8f);

            row = new Rect(view.x, y, view.width, height);

            bool over = Mouse.IsOver(row);

            UIElementPainter.OutlineRounded(row, palette.Border,
                over ? palette.SurfaceRaised : palette.PanelBackground);

            float indent = 0f;

            if (icon != null)
            {
                Icon(new Rect(row.x + 4f, row.y + (row.height - IconSize) * 0.5f, IconSize, IconSize), icon,
                    tint);

                indent = IconSize + 6f;
            }

            float cross = removable ? 22f : 4f;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            float rightWidth = 0f;

            try
            {
                Text.WordWrap = false;

                if (!right.NullOrEmpty())
                {
                    Text.Font = GameFont.Tiny;

                    rightWidth = UIRichText.WidthOf(right) + 8f;

                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = rightColor;

                    UIRichText.Label(new Rect(row.xMax - cross - rightWidth, row.y, rightWidth, row.height),
                        right);
                }

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextPrimary;

                UIRichText.Label(new Rect(row.x + 6f + indent, row.y,
                    Mathf.Max(10f, row.width - cross - rightWidth - 10f - indent), row.height),
                    left ?? string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(row, (TipSignal) tooltip);

            if (!removable)
                return false;

            return Widgets.ButtonImage(new Rect(row.xMax - 20f, row.y + (row.height - 14f) * 0.5f, 14f, 14f),
                TexButton.Delete);
        }

        /// <summary>The side of an item icon on a row or a card.</summary>
        internal const float IconSize = 28f;

        /// <summary>
        /// A thing's own icon, in the colour it should be.
        ///
        /// <b>Guarded, because a def's icon is resolved lazily and can be missing.</b> A mod that ships a def
        /// without its texture produces a null here rather than an exception, but the resolve itself can throw
        /// on a broken graphic, and a list of forty items must not lose the other thirty-nine to one of them.
        ///
        /// Drawn with <c>ScaleToFit</c> so a non-square icon keeps its shape, which most weapons are.
        /// </summary>
        internal static void Icon(Rect rect, ThingDef def, Color? tint = null)
        {
            if (def == null)
                return;

            UIGuard.Try("Editor.ItemIcon", () =>
            {
                Texture texture = def.uiIcon;

                if (texture == null)
                    return;

                Color previous = GUI.color;

                GUI.color = tint ?? def.uiIconColor;

                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);

                GUI.color = previous;
            }, null);
        }

        /// <summary>An add button sized to its own label, for the end of a list.</summary>
        internal static bool Add(Rect view, float y, string label, UIColorPaletteDef palette,
            bool enabled = true, string tooltip = null)
        {
            float width = Mathf.Min(view.width, TabParts.PillWidth(label) + 30f);

            return TabParts.Button(new Rect(view.x, y, width, ControlHeight - 2f), label, palette, enabled,
                false, tooltip);
        }

        /// <summary>
        /// Everything that has to be told a pawn's appearance changed.
        ///
        /// <b>Three calls, because two of them are caches that do not watch each other.</b> The renderer holds
        /// resolved graphics, the portrait cache holds rendered textures, and a colonist whose hair has changed
        /// keeps the old hair in both until each is dirtied. Missing the portrait one is why an edited pawn used
        /// to look right on the map and wrong in every list in the game until something else invalidated it.
        /// </summary>
        internal static void Redraw(Pawn pawn)
        {
            // Null is a normal answer here, not a fault. This is called on close, and the window can legitimately
            // have nobody in it by then -- the pawn was resurrected out from under it, or the corpse was cremated
            // while it sat open. Guarding it as an exception filled the log with a stack trace for closing a
            // window.
            if (pawn == null)
                return;

            UIGuard.Try("Editor.Redraw", () =>
            {
                if (pawn.Drawer != null && pawn.Drawer.renderer != null)
                {
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                    pawn.Drawer.renderer.WoundOverlays.ClearCache();
                }

                PortraitsCache.SetDirty(pawn);
                GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
            }, null);
        }

        /// <summary>A label for anything with a def, falling back rather than throwing on a def with no label.</summary>
        internal static string LabelOf(Def def, string absent = "none")
        {
            if (def == null)
                return absent;

            return UIGuard.Try<string>("Editor.DefLabel",
                () => def.LabelCap.NullOrEmpty() ? def.defName : def.LabelCap.ToString(), def.defName, null);
        }

        /// <summary>A def's description for a hover, or null when it has none worth showing.</summary>
        internal static string DescriptionOf(Def def)
        {
            if (def == null)
                return null;

            return UIGuard.Try<string>("Editor.DefDescription",
                () => def.description.NullOrEmpty() ? null : def.description, null, null);
        }

        /// <summary>
        /// Warns without refusing.
        ///
        /// <b>The editor's whole posture in one helper.</b> A fifth trait, a skill a backstory disabled, a gene
        /// the xenotype does not carry: it says what the consequence is and then does it. This is the tool for
        /// doing things the game would not, and a tool that silently declines is worse than no tool.
        /// </summary>
        internal static void Warn(string text)
        {
            if (text.NullOrEmpty())
                return;

            UIGuard.Try("Editor.Warn",
                () => Messages.Message(text, MessageTypeDefOf.CautionInput, false), null);
        }
    }
}
