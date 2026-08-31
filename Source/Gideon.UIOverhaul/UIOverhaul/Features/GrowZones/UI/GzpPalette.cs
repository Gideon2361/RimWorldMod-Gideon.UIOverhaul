// Color values and drawing idioms follow the Modern UI suite so this tab sits alongside those
// mods visually. Reimplemented from Modern Ideology Menu, which is MIT licensed:
//
// Full license text ships with the mod in THIRD-PARTY-NOTICES.txt.
//
//   Copyright (c) 2026 Astryl
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software
//   and associated documentation files (the "Software"), to deal in the Software without
//   restriction, including without limitation the rights to use, copy, modify, merge, publish,
//   distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions:
//   The above copyright notice and this permission notice shall be included in all copies or
//   substantial portions of the Software.
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.

using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.GrowZones.UI
{
    /// <summary>
    /// The growing-zone UI's colors and drawing idioms, taken from the active
    /// <see cref="UIColorPaletteDef"/>.
    ///
    /// These were hardcoded hex values when this came over from Growing Zones Plus, and the framework
    /// palette was originally derived from them -- #15191D, #1B1F23, #0E1013, #2F3337, #E3E3E3 and
    /// #9EA6B2 are all still there in UIPalette_Default. So the mapping below is not an approximation:
    /// each name resolves to the role that already held its old value. What changes is that a player
    /// switching theme now moves this UI with everything else, instead of leaving it stranded on the
    /// dark values.
    ///
    /// Kept as properties rather than fields on purpose. Read once into statics, they would capture
    /// whichever palette happened to be active at class initialization and never follow a theme change.
    /// </summary>
    public static class GzpPalette
    {
        private static UIColorPaletteDef Palette => UIColorPaletteDef.Active;

        /// <summary>Fill for the panels inset into the window chrome. Was #15191D.</summary>
        public static Color BG => Palette.WindowBackground;

        /// <summary>Fill for a card or row sitting on a panel. Was #1B1F23.</summary>
        public static Color PanelBG => Palette.PanelBackground;

        /// <summary>
        /// Button fills and dividers. Was #2F3337, which the palette still carries as its border
        /// color -- but this resolves to SurfaceRaised, the same role the vanilla button patch paints
        /// with, so a button in this UI and a vanilla one beside it match.
        /// </summary>
        public static Color BGL => Palette.SurfaceRaised;

        /// <summary>Window chrome: the outer border, the header, the footer, meter troughs. Was #0E1013.</summary>
        public static Color BGD => Palette.SurfaceSunken;

        public static Color Stat => Palette.TextPrimary;
        public static Color TextDim => Palette.TextSecondary;
        public static Color Accent => Palette.Accent;
        public static Color Good => Palette.Success;
        public static Color Bad => Palette.Danger;
        public static Color Warn => Palette.Warning;

        /// <summary>Too cold. The palette's cold end of any hot/cold scale.</summary>
        public static Color Cold => Palette.Info;

        public static Color FromHex(int hex)
        {
            return new Color(
                ((hex >> 16) & 0xFF) / 255f,
                ((hex >> 8) & 0xFF) / 255f,
                (hex & 0xFF) / 255f,
                1f);
        }

        // Notice washes. Kept here rather than in the add-bill window because the settings screen
        // uses the orange one too, and two copies of the same color would drift apart.
        //
        // Derived from the palette's status colors at the alpha the originals used, so a theme that
        // restyles Danger restyles the hazard wash with it. A wash has to stay translucent to work --
        // it goes over the striped notice texture -- so the alpha is ours, not the palette's.
        public static Color NoticeRed => Wash(Palette.Danger, 0.24f);
        public static Color NoticeOrange => Wash(Palette.Warning, 0.22f);
        public static Color NoticeGreen => Wash(Palette.Success, 0.20f);

        /// <summary>
        /// Scrim laid over a row that is present but not in play, to push it back without hiding it.
        ///
        /// Built from the window background rather than from flat black: on a light theme a black
        /// scrim reads as a hole punched in the page, where the window color reads as the row simply
        /// receding into it.
        /// </summary>
        public static Color DimScrim => Wash(Palette.WindowBackground, 0.55f);

        private static Color Wash(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        public static void DrawCard(Rect r)
        {
            Widgets.DrawBoxSolid(r, PanelBG);
        }

        /// <summary>
        /// Washes the striped notice texture over a rect. The same treatment a flagged plant's card
        /// gets, so anything drawing attention to itself reads as part of one family.
        /// </summary>
        public static void NoticeWash(Rect r, Color wash)
        {
            Color previous = GUI.color;
            GUI.color = wash;
            GUI.DrawTexture(r, GzpTex.NoticeBackground, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        /// <summary>
        /// Notice wash inside a thin border of <paramref name="accent"/>, with a thicker stripe of
        /// it down the left edge.
        /// </summary>
        public static void NoticePanel(Rect r, Color accent, Color wash)
        {
            Widgets.DrawBoxSolid(r, PanelBG);
            NoticeWash(r, wash);

            Color previous = GUI.color;
            GUI.color = accent;
            Widgets.DrawBox(r, 1);
            GUI.color = previous;

            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), accent);
        }

        /// <summary>Panel with a colored accent stripe down its left edge.</summary>
        public static void Card(Rect r, Color stripe, bool hover)
        {
            Widgets.DrawBoxSolid(r, PanelBG);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), stripe);
            if (hover)
                Widgets.DrawBoxSolid(r, Palette.HoverOverlay);
        }

        public static void SelectedFill(Rect r)
        {
            Widgets.DrawBoxSolid(r, Palette.SelectionOverlay);
        }

        /// <summary>Horizontal meter, <paramref name="fill"/> in 0..1.</summary>
        public static void Bar(Rect r, float fill, Color col)
        {
            Widgets.DrawBoxSolid(r, Palette.SurfaceSunken);
            if (fill > 0f)
                Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), col);
        }

        // GrayButton lived here and is gone as of 2026-08-25. It drew a square flat fill, and the bills
        // toolbar's rounded accent button drew something else, so the mod shipped two ideas of what a
        // button is -- both of them visible in one screenshot. Everything now goes through
        // UIActionButtonControl, whose four argument overload takes this method's exact parameter order so
        // the twenty-nine call sites converted by name alone. Do not add another one here.

        /// <summary>
        /// Border for input fields. The palette carries a role for exactly this -- a dimmed accent for
        /// field borders that would overpower at full strength -- so it is taken from there rather than
        /// scaled down from Accent by hand as it used to be.
        /// </summary>
        public static Color FieldBorder => Palette.AccentMuted;

        private static GUIStyle flatTextFieldStyle;

        /// <summary>
        /// Text field with a flat fill and a single-pixel border, in place of RimWorld's textured
        /// default. The stock style paints its own background, so it is cloned with that removed
        /// rather than drawn over.
        /// </summary>
        public static string FlatTextField(Rect rect, string text)
        {
            Widgets.DrawBoxSolid(rect, BGD);

            Color previous = GUI.color;
            GUI.color = FieldBorder;
            Widgets.DrawBox(rect, 1);
            GUI.color = previous;

            if (flatTextFieldStyle == null)
            {
                flatTextFieldStyle = new GUIStyle(Text.CurTextFieldStyle);
                flatTextFieldStyle.normal.background = null;
                flatTextFieldStyle.focused.background = null;
                flatTextFieldStyle.hover.background = null;
                flatTextFieldStyle.active.background = null;
            }

            return GUI.TextField(rect.ContractedBy(5f, 2f), text, flatTextFieldStyle);
        }

        private static GUIStyle cardLabelStyle;
        private static Font cardLabelFont;

        /// <summary>
        /// The small font, with word wrap off so a long plant name is clipped rather than pushed onto
        /// a second line inside a fixed-height card.
        ///
        /// fontSize is deliberately left alone. Verse.Text builds each of its font styles as a clone of
        /// GUI.skin.label and assigns only .font -- it never assigns fontSize -- so fontSize is 0, which
        /// Unity reads as "use the size built into the font asset". This used to do fontSize += 1 to get
        /// a label a point larger than body text, which set it to 1: card text was drawn one pixel tall,
        /// indistinguishable from not being drawn at all. Leaving it at 0 renders at exactly the size
        /// Widgets.Label uses.
        ///
        /// Rebuilt whenever RimWorld swaps its font asset, which it does when the UI scale changes. A
        /// style cached for the whole session would keep a stale font after the player moves that slider.
        /// </summary>
        public static GUIStyle CardLabelStyle
        {
            get
            {
                GameFont previous = Text.Font;
                Text.Font = GameFont.Small;
                GUIStyle source = Text.CurFontStyle;

                if (cardLabelStyle == null || cardLabelFont != source.font)
                {
                    cardLabelFont = source.font;
                    cardLabelStyle = new GUIStyle(source) { wordWrap = false };
                }

                Text.Font = previous;
                return cardLabelStyle;
            }
        }

        /// <summary>
        /// Label drawn in the card font.
        /// </summary>
        /// <param name="alignment">
        /// How the text sits in <paramref name="rect"/>. Set explicitly rather than inherited: the
        /// cached style would otherwise keep whichever Text.Anchor happened to be current when it was
        /// first built, which is not something a caller can reason about.
        /// </param>
        public static void CardLabel(Rect rect, string text, Color color,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            GUIStyle style = CardLabelStyle;
            style.alignment = alignment;

            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        /// <summary>Drawn width of the bar. The framework control owns the value now.</summary>
        public const float ScrollBarWidth = UIScrollBarControl.ScrollBarWidth;

        /// <summary>Clear space between the content and the bar.</summary>
        public const float ScrollBarGutter = UIScrollBarControl.ScrollBarGutter;

        /// <summary>The width a view rect should be, leaving room for the bar and its gutter.</summary>
        public static float ContentWidth(Rect outRect)
        {
            return UIScrollBarControl.ContentWidth(outRect);
        }

        /// <summary>
        /// Slim draggable scrollbar. <b>The implementation moved to <see cref="UIScrollBarControl"/></b> when
        /// the rail control needed it: the framework cannot reference a feature, so it had to come up a layer.
        /// This forwarder stays because several screens already call it by this name.
        /// </summary>
        public static void FlatScrollbar(Rect outRect, float viewHeight, ref Vector2 scroll, ref bool dragging,
            ref float dragOffset)
        {
            UIScrollBarControl.Draw(outRect, viewHeight, ref scroll, ref dragging, ref dragOffset);
        }

        /// <summary>
        /// Checkbox row in the mod's own styling, in place of Widgets.CheckboxLabeled.
        ///
        /// Now a thin wrapper over <see cref="UICheckboxControl"/>, which draws the identical row -- label
        /// first, box against the right edge, the whole row as the hit target. It had been a hand-rolled
        /// copy, written before that control existed; keeping it as one meant two definitions of the same
        /// look, and this feature's settings page and the work tab would have drifted apart.
        ///
        /// Kept as a method rather than deleted because it is the name every call site in this feature uses,
        /// and its <c>ref bool</c> signature is already what the control takes.
        /// </summary>
        public static bool CheckboxRow(Rect r, string label, ref bool value, string tooltip = null)
        {
            return UICheckboxControl.Draw(r, ref value, Palette, label, tooltip, UICheckboxSide.Right);
        }

        /// <summary>
        /// Flat icon button that tints on hover. Silent by design -- the caller plays whatever sound
        /// fits the action. See <see cref="GrayButton"/>.
        /// </summary>
        public static bool IconButton(Rect r, Texture2D icon, string tooltip, Color? tint = null)
        {
            bool hover = Mouse.IsOver(r);
            if (hover)
                Widgets.DrawBoxSolid(r, Palette.HoverOverlay);

            Color previous = GUI.color;
            GUI.color = hover ? Stat : (tint ?? TextDim);
            GUI.DrawTexture(r.ContractedBy(3f), icon);
            GUI.color = previous;

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(r, (TipSignal) tooltip);

            return Widgets.ButtonInvisible(r);
        }

        /// <summary>Dim label / bright value row. Advances <paramref name="y"/>.</summary>
        public static void InfoLine(ref float y, float x, float width, string label, string value, Color? valueColor = null)
        {
            Rect row = new Rect(x, y, width, 22f);
            Color previous = GUI.color;

            GUI.color = TextDim;
            Widgets.Label(new Rect(row.x, row.y, row.width * 0.5f, row.height), label);

            GUI.color = valueColor ?? Stat;
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(row.x + row.width * 0.5f, row.y, row.width * 0.5f, row.height), value);
            Text.Anchor = anchor;

            GUI.color = previous;
            y += row.height;
        }

        /// <summary>Collapsible section header. Returns true when the section is expanded.</summary>
        public static bool SectionHeader(ref float y, float x, float width, string title, HashSet<string> collapsed)
        {
            Rect header = new Rect(x, y, width, 26f);
            bool isCollapsed = collapsed.Contains(title);
            bool hover = Mouse.IsOver(header);

            Widgets.DrawBoxSolid(header, hover ? BGL : BGD);

            Color previous = GUI.color;
            GUI.color = hover ? Accent : TextDim;
            Widgets.Label(new Rect(header.x + 8f, header.y + 2f, 16f, 22f), isCollapsed ? "▸" : "▾");
            GUI.color = hover ? Stat : TextDim;
            Widgets.Label(new Rect(header.x + 26f, header.y + 2f, header.width - 30f, 22f), title);
            GUI.color = previous;

            if (Widgets.ButtonInvisible(header))
            {
                if (isCollapsed)
                    collapsed.Remove(title);
                else
                    collapsed.Add(title);
                SoundDefOf.Click.PlayOneShotOnCamera();
                isCollapsed = !isCollapsed;
            }

            y += header.height + 4f;
            return !isCollapsed;
        }
    }
}
