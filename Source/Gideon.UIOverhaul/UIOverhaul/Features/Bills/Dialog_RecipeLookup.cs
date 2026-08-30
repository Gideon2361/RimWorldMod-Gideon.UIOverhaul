using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// "How do I make that?", asked of the whole game rather than of one bench.
    ///
    /// <b>Keyed on the thing, not on the recipe.</b> The player knows what they want to end up holding and does
    /// not know the verb, the bench or the mod -- those are the answers. A first cut listed recipes, so somebody
    /// searching for "component" matched "make component" by luck of the wording and would have missed a thing
    /// whose recipe was named differently. Corrected on 2026-08-29. See <see cref="RecipeLookupCatalog"/>, which
    /// collapses every route to one thing into a single row.
    ///
    /// <b>The inverse of every other bill screen.</b> Adding a bill starts from a bench and offers what it can
    /// produce, which is the right shape when you are standing at a bench and the wrong one when you know the
    /// thing and not the bench. A player who wants a component, or a shirt in a material they have never used,
    /// is holding the answer to the second question and has to open benches one at a time to find the first.
    ///
    /// <b>Everything the game can make, not just what your benches can.</b> That is the point: it answers "what
    /// would I have to build" as readily as "which of mine does this". A bench the colony does not own is still
    /// listed, because not owning it is exactly the fact the player came here to learn.
    ///
    /// <b>A lookup and nothing else -- it adds no bills.</b> Adding one needs a bench instance, a count, a
    /// filter and a worker, and that flow already exists and is three steps long. Bolting a shortcut onto a
    /// reference window would make it a second, worse copy of the wizard.
    /// </summary>
    public class Dialog_RecipeLookup : Window
    {
        private const float HeaderHeight = 46f;
        private const float FooterHeight = 52f;
        private const float Pad = 12f;
        private const float EdgeInset = 8f;

        private const float CardHeight = 72f;
        private const float CardGap = 6f;
        private const float IconSize = 48f;
        private const float LineHeight = 22f;

        private readonly UICardControl card = new UICardControl();

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search for a thing to make...",
            MaxLength = 60
        };

        private Vector2 scroll;

        /// <summary>
        /// The matches for the current query, rebuilt only when the query changes.
        ///
        /// The catalogue itself never changes -- defs are fixed for the session -- so the only work per frame
        /// would be the filter, and even that is wasted on the frames where nothing was typed.
        /// </summary>
        private readonly List<RecipeEntry> shown = new List<RecipeEntry>();

        private string filtered;

        public Dialog_RecipeLookup()
        {
            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(760f, 640f);

        protected override float Margin => 0f;

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + HeaderHeight);

            UIGuardedPanel.Draw("Bills.RecipeLookup", inRect, () => Contents(inRect),
                "The lookup could not be drawn. Nothing has been changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(inRect, GzpPalette.BGD);
            Text.Font = GameFont.Small;

            Header(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight));

            Refilter();

            Rect body = new Rect(inRect.x + EdgeInset, inRect.y + HeaderHeight, inRect.width - EdgeInset * 2f,
                inRect.height - HeaderHeight - FooterHeight);

            Body(body, palette);

            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>Rebuilds the match list when, and only when, the query has changed since the last frame.</summary>
        private void Refilter()
        {
            string query = search.Text ?? string.Empty;

            if (filtered == query)
                return;

            filtered = query;
            scroll.y = 0f;

            shown.Clear();

            List<RecipeEntry> all = RecipeLookupCatalog.All;
            string needle = query.Trim().ToLowerInvariant();

            for (int i = 0; i < all.Count; i++)
            {
                if (needle.Length == 0 || all[i].Haystack.Contains(needle))
                    shown.Add(all[i]);
            }
        }

        private void Body(Rect rect, UIColorPaletteDef palette)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BG);

            if (shown.Count == 0)
            {
                Color previous = GUI.color;
                GUI.color = GzpPalette.TextDim;

                Widgets.Label(rect.ContractedBy(Pad),
                    RecipeLookupCatalog.All.Count == 0
                        ? "Nothing in this game can be made at a workbench, which should not be possible."
                        : "Nothing matches that. The search reads the thing's name, its mod, the bench and the "
                          + "bill that makes it.");

                GUI.color = previous;

                return;
            }

            Rect view = new Rect(0f, 0f, rect.width - 18f, shown.Count * (CardHeight + CardGap));

            Widgets.BeginScrollView(rect, ref scroll, view);

            float y = 0f;

            for (int i = 0; i < shown.Count; i++)
            {
                Rect row = new Rect(0f, y, view.width, CardHeight);

                // Everything above the fold and nothing below it. The catalogue runs to hundreds of rows and
                // every one of them draws an icon, which is a texture lookup apiece.
                if (row.yMax >= scroll.y && row.y <= scroll.y + rect.height)
                    Card(row, shown[i], palette);

                y += CardHeight + CardGap;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// One result: what it looks like, what it is called, who added it, and where it is made.
        ///
        /// <b>Four facts and no figures.</b> Work, skill and yield belong on the Add bill card, where the
        /// question is whether to commit to the recipe. Here the question is only which thing this is and where
        /// to go, so anything else would be noise between the name and the bench.
        /// </summary>
        private void Card(Rect rect, RecipeEntry entry, UIColorPaletteDef palette)
        {
            card.Padding = 0f;
            card.AccentColor = entry.Owned ? GzpPalette.Accent : null;
            card.BackgroundColor = GzpPalette.PanelBG;
            card.Selected = false;

            card.DrawChrome(rect, palette);

            Rect icon = new Rect(rect.x + 10f, rect.y + (rect.height - IconSize) * 0.5f, IconSize, IconSize);

            // The thing itself, which is the whole reason the row is keyed on the product: this is the picture
            // somebody is scanning for, and it is the same one they will see in their stockpile afterwards.
            if (entry.Product != null)
                Widgets.DefIcon(icon, entry.Product);

            float textX = icon.xMax + 12f;
            float width = rect.xMax - 12f - textX;

            GzpPalette.CardLabel(new Rect(textX, rect.y + 6f, width, LineHeight), entry.Label, GzpPalette.Stat);

            GzpPalette.CardLabel(new Rect(textX, rect.y + 6f + LineHeight, width, LineHeight), entry.Mod,
                GzpPalette.TextDim);

            GzpPalette.CardLabel(new Rect(textX, rect.y + 6f + LineHeight * 2f, width, LineHeight), entry.Benches,
                entry.Owned ? GzpPalette.Accent : GzpPalette.TextDim);

            if (!entry.Tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, entry.Tip);
        }

        private void Header(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            Color previous = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Stat;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 8f, rect.width - 320f, 30f), "Lookup");

            Text.Font = GameFont.Small;
            GUI.color = previous;

            Rect close = new Rect(rect.xMax - Pad - 24f, rect.y + 11f, 24f, 24f);

            search.Draw(new Rect(close.x - 10f - 260f, rect.y + 10f, 260f, 26f));

            if (GzpPalette.IconButton(close, GzpTex.Close, "Close"))
                Close();
        }

        private void Footer(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;

            int total = RecipeLookupCatalog.All.Count;

            string line = shown.Count == total
                ? total + " things can be made at a bench. A bench you own is highlighted."
                : shown.Count + " of " + total + " match. A bench you own is highlighted.";

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 16f, rect.width - 180f, 24f), line);

            GUI.color = previous;

            if (UIActionButtonControl.Draw(new Rect(rect.xMax - Pad - 110f, rect.y + 10f, 110f, 32f), "Close"))
                Close();
        }
    }
}
