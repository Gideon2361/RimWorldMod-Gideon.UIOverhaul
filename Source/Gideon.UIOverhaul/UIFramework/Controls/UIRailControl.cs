using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// One entry on a rail: what it is called, how much is behind it, and the key the caller filters by.
    /// </summary>
    internal struct UIRailEntry
    {
        /// <summary>What the caller selects by. Null makes this a group caption rather than a choice.</summary>
        internal string Key;

        /// <summary>What the player reads.</summary>
        internal string Label;

        /// <summary>How many rows are behind it. Negative hides the number entirely.</summary>
        internal int Count;

        /// <summary>Color for the count, for a rail that wants to flag one of its entries.</summary>
        internal Color? CountColor;

        /// <summary>
        /// Typeface for the label. <see cref="UIFace.Game"/> uses RimWorld's own, which is what almost every
        /// rail wants; the font picker sets this per row so each entry previews itself.
        /// </summary>
        internal UIFace Face;

        /// <summary>Drawn to the left of the label when set.</summary>
        internal Texture2D Icon;

        /// <summary>
        /// Dimmed and unclickable. Prefer this to removing the row: a rail whose contents change shape while a
        /// search box is typed into moves the thing the player is reaching for.
        /// </summary>
        internal bool Disabled;

        /// <summary>Hover text, or null.</summary>
        internal string Tooltip;

        internal static UIRailEntry Group(string label)
        {
            return new UIRailEntry { Key = null, Label = label, Count = -1 };
        }

        internal static UIRailEntry Of(string key, string label, int count = -1)
        {
            return new UIRailEntry { Key = key, Label = label, Count = count };
        }
    }

    /// <summary>
    /// The list down the side of a screen: what you are looking at, and how much of it there is.
    ///
    /// <b>Promoted from the trade screens, where this design had already earned itself.</b> Thirteen screens had
    /// each hand-rolled the same thing -- sunken panel, scroll view, group captions, hover, selection wash --
    /// and the trade one was the only version that had been through enough revisions to be worth keeping. What
    /// this adds over that one is a typeface per row, an optional icon, and an explicit disabled flag.
    ///
    /// <b>Stateless.</b> The caller keeps the selection and the scroll offset, which is what lets one screen own
    /// two rails; the beacon screen's "which beacon" and "what to show" are the same code.
    ///
    /// <b>Groups are captions, not collapsibles.</b> A rail with eight entries in two groups does not need to
    /// fold, and something that folds is something a player can hide from themselves and then not find. The
    /// caption is drawn dim and unclickable and the entries under it behave identically to any other.
    /// </summary>
    internal static class UIRailControl
    {
        private const float DefaultEntryHeight = 26f;
        private const float GroupHeight = 24f;
        private const float IconSize = 20f;
        private const float Pad = 6f;

        /// <summary>
        /// Draws the rail and returns the key the player picked, or null if they picked nothing this frame.
        ///
        /// <paramref name="entryHeight"/> exists for rails whose rows carry more than a line of text -- the font
        /// picker draws its labels at Medium so the preview is legible, which needs the room.
        /// </summary>
        internal static string Draw(Rect rect, List<UIRailEntry> entries, string selected, ref Vector2 scroll,
            ref bool dragging, ref float dragOffset, UIColorPaletteDef palette = null,
            float entryHeight = DefaultEntryHeight, bool frame = true)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            if (palette == null || entries == null || entries.Count == 0)
                return null;

            if (frame)
            {
                Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);
                Widgets.DrawBox(rect);
            }

            Rect inner = frame ? rect.ContractedBy(1f) : rect;

            float height = 0f;

            for (int i = 0; i < entries.Count; i++)
                height += entries[i].Key == null ? GroupHeight : entryHeight;

            Rect view = new Rect(0f, 0f, UIScrollBarControl.ContentWidth(inner), height + 2f);

            string picked = null;

            Widgets.BeginScrollView(inner, ref scroll, view, false);

            float y = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                UIRailEntry entry = entries[i];

                if (entry.Key == null)
                {
                    Caption(new Rect(0f, y, view.width, GroupHeight), entry.Label, palette);

                    y += GroupHeight;

                    continue;
                }

                Rect row = new Rect(0f, y, view.width, entryHeight);

                if (Entry(row, entry, entry.Key == selected, palette))
                    picked = entry.Key;

                y += entryHeight;
            }

            Widgets.EndScrollView();

            UIScrollBarControl.Draw(inner, height + 2f, ref scroll, ref dragging, ref dragOffset, palette);

            return picked;
        }

        private static void Caption(Rect rect, string label, UIColorPaletteDef palette)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x + Pad, rect.y, rect.width - Pad * 2f, rect.height), label);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        /// <summary>Returns true when the row was clicked this frame.</summary>
        private static bool Entry(Rect rect, UIRailEntry entry, bool selected, UIColorPaletteDef palette)
        {
            bool over = !entry.Disabled && Mouse.IsOver(rect);

            if (selected)
            {
                Color wash = palette.Accent;

                wash.a = 0.22f;

                Widgets.DrawBoxSolid(rect, wash);
            }
            else if (over)
            {
                Widgets.DrawBoxSolid(rect, palette.HoverOverlay);
            }

            float x = rect.x + Pad;

            if (entry.Icon != null)
            {
                Rect icon = new Rect(x, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);

                GUI.color = entry.Disabled ? palette.TextDisabled : Color.white;

                GUI.DrawTexture(icon, entry.Icon, ScaleMode.ScaleToFit);

                GUI.color = Color.white;

                x = icon.xMax + Pad;
            }

            // The count is measured first so the label can be trimmed around it rather than drawn under it.
            float countWidth = 0f;
            string count = entry.Count >= 0 ? entry.Count.ToString() : null;

            if (count != null)
            {
                Text.Font = GameFont.Tiny;

                countWidth = Text.CalcSize(count).x + Pad;
            }

            Rect label = new Rect(x, rect.y, rect.xMax - Pad - countWidth - x, rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = entry.Disabled ? palette.TextDisabled : palette.TextPrimary;

            if (entry.Face == UIFace.Game)
            {
                Text.Font = GameFont.Small;

                Widgets.LabelEllipses(label, entry.Label);
            }
            else
            {
                UITextControl.LabelEllipses(label, entry.Label, entry.Face, GameFont.Medium);
            }

            if (count != null)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = entry.CountColor
                            ?? (entry.Disabled ? palette.TextDisabled : palette.TextSecondary);

                Widgets.Label(new Rect(rect.xMax - Pad - countWidth, rect.y, countWidth, rect.height), count);
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (!entry.Tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, entry.Tooltip);

            return !entry.Disabled && Widgets.ButtonInvisible(rect);
        }
    }
}
