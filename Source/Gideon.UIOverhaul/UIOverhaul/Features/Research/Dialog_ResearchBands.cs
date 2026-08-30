using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// Every research project in the game, the band the classifier put it in, and the sentence saying why.
    ///
    /// <b>This exists because the taxonomy is a guess and has to be checkable.</b> The band table was written
    /// against a hand pass over 354 projects; the shipped classifier reads
    /// <c>ResearchProjectDef.UnlockedDefs</c> instead and will disagree on some of them. Arguing about that from
    /// a spreadsheet is useless -- what settles it is a list of every project and the reason the code gave,
    /// sorted so the disagreements are next to each other. So the listing was built before the layout was
    /// touched, which is what was promised when the mockup was approved.
    ///
    /// <b>It is also the fastest way to see whether a new mod needs an override.</b> Filter to Other, and what
    /// is left is every project the rules could not read. If a mod's whole tab is sitting there, it wants a line
    /// in <see cref="ResearchBandOverrides"/>.
    ///
    /// <b>Reachable from the developer tools category and nowhere else.</b> Nothing here changes anything, and a
    /// player has no question this answers -- the detail panel already says which band the one project they are
    /// looking at landed in, and why.
    /// </summary>
    public class Dialog_ResearchBands : Window
    {
        private const float TitleHeight = 34f;
        private const float BarHeight = 30f;
        private const float RowHeight = 24f;
        private const float Gap = 8f;
        private const float SwatchWidth = 4f;
        private const float BandColumn = 152f;
        private const float SourceColumn = 128f;
        private const float TabColumn = 108f;

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        /// <summary>One project as this window shows it. Built once per open, since defs cannot change mid-session.</summary>
        private sealed class Row
        {
            internal ResearchProjectDef Project;
            internal ResearchBand Band;
            internal string Label;
            internal string Reason;
            internal string Source;
            internal string Tab;
        }

        private readonly List<Row> rows = new List<Row>();

        private readonly List<Row> shown = new List<Row>();

        /// <summary>Null means every band.</summary>
        private ResearchBand? only;

        private string query = string.Empty;

        private Vector2 scroll;

        public Dialog_ResearchBands()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Min(1080f, UI.screenWidth - 80f), Mathf.Min(640f, UI.screenHeight - 80f));

        internal static void Open()
        {
            UIGuard.Try("Research.OpenBandListing",
                () => Find.WindowStack.Add(new Dialog_ResearchBands()), null);
        }

        public override void PostOpen()
        {
            base.PostOpen();

            Build();
        }

        private void Build()
        {
            UIGuard.Try("Research.BuildBandListing", () =>
            {
                rows.Clear();

                List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    ResearchProjectDef project = all[i];

                    if (project == null)
                        continue;

                    rows.Add(new Row
                    {
                        Project = project,
                        Band = ResearchTaxonomy.BandOf(project),
                        Label = project.label.NullOrEmpty() ? project.defName : project.label,
                        Reason = ResearchTaxonomy.ReasonFor(project),
                        Source = project.modContentPack == null ? "?" : project.modContentPack.Name,
                        Tab = project.tab == null
                            ? "-"
                            : (project.tab.label.NullOrEmpty() ? project.tab.defName : project.tab.label)
                    });
                }

                // Band first, then source, then name. Band-first is the whole point: a mod whose projects
                // scattered shows up as its name repeating down several bands, and a mod that wants an override
                // shows up as its name filling the bottom of Other.
                rows.Sort((left, right) =>
                {
                    int byBand = ((int) left.Band).CompareTo((int) right.Band);

                    if (byBand != 0)
                        return byBand;

                    int bySource = string.Compare(left.Source, right.Source,
                        System.StringComparison.OrdinalIgnoreCase);

                    return bySource != 0
                        ? bySource
                        : string.Compare(left.Label, right.Label, System.StringComparison.OrdinalIgnoreCase);
                });

                Filter();
            }, "The research band listing could not be built.");
        }

        private void Filter()
        {
            shown.Clear();

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];

                if (only.HasValue && row.Band != only.Value)
                    continue;

                if (!query.NullOrEmpty()
                    && row.Label.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) < 0
                    && row.Source.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) < 0
                    && row.Tab.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                shown.Add(row);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + TitleHeight);

            UIGuardedPanel.Draw("Research.BandListing", inRect, () => Contents(inRect),
                "The research band listing could not finish drawing.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Text.Font = GameFont.Medium;
            GUI.color = palette.TextPrimary;

            // Oswald for the title. It draws smaller than the other faces at the same GameFont -- its line box is
            // 1.48 ems against Barlow's 1.20, and every face is scaled to fit RimWorld's line height -- so this is
            // a title that reads as tall and narrow rather than as large.
            UITextControl.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, TitleHeight), "Research bands",
                UIFace.Oswald, GameFont.Medium);

            Text.Font = GameFont.Small;

            float y = inRect.y + TitleHeight + Gap;

            Bar(new Rect(inRect.x, y, inRect.width, BarHeight), palette);

            y += BarHeight + Gap;

            List(new Rect(inRect.x, y, inRect.width, inRect.yMax - y), palette);

            GUI.color = Color.white;
        }

        /// <summary>Search, then one pill per band with its count, then the totals.</summary>
        private void Bar(Rect bar, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(bar.x, bar.y + 2f, 190f, 26f), palette);

            if (Search.Text != query)
            {
                query = Search.Text ?? string.Empty;

                Filter();
            }

            float x = bar.x + 198f;

            x = Chip(bar, x, "All", rows.Count, !only.HasValue, palette.TextSecondary, palette,
                () =>
                {
                    only = null;

                    Filter();
                });

            List<ResearchBandInfo> bands = ResearchBands.All;

            for (int i = 0; i < bands.Count; i++)
            {
                ResearchBandInfo info = bands[i];
                int count = Count(info.Band);

                // A band nothing landed in is left out rather than shown at zero. Eleven pills is already a lot
                // of bar, and an empty one is a control that can only tell you it has nothing to say.
                if (count == 0)
                    continue;

                ResearchBand captured = info.Band;

                x = Chip(bar, x, info.Short, count, only.HasValue && only.Value == captured,
                    ResearchBands.ColorFor(captured, palette), palette,
                    () =>
                    {
                        only = captured;

                        Filter();
                    });

                if (x > bar.xMax - 60f)
                    break;
            }
        }

        private float Chip(Rect bar, float x, string label, int count, bool on, Color tint,
            UIColorPaletteDef palette, System.Action chosen)
        {
            string text = label + " " + count;
            float width = TabParts.ButtonWidth(text, 14f);
            Rect rect = new Rect(x, bar.y + 2f, width, 26f);

            if (on)
                UIElementPainter.OutlineRounded(rect, tint, palette.SurfaceSunken);
            else if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = on ? palette.TextPrimary : tint;

            Widgets.Label(rect, text);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;

            if (Widgets.ButtonInvisible(rect))
                chosen();

            return x + width + 4f;
        }

        private int Count(ResearchBand band)
        {
            int count = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Band == band)
                    count++;
            }

            return count;
        }

        private void List(Rect rect, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(4f);
            Rect view = new Rect(0f, 0f, inner.width - 18f, shown.Count * RowHeight);

            Widgets.BeginScrollView(inner, ref scroll, view);

            try
            {
                float y = 0f;

                for (int i = 0; i < shown.Count; i++)
                {
                    // Only what is on screen. This list is 354 rows on Aaron's load order and every row measures
                    // text, so drawing the whole thing costs the same whether or not anybody can see it.
                    if (y + RowHeight >= scroll.y && y <= scroll.y + inner.height)
                        Draw(new Rect(0f, y, view.width, RowHeight - 1f), shown[i], palette);

                    y += RowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void Draw(Rect row, Row entry, UIColorPaletteDef palette)
        {
            if (Mouse.IsOver(row))
                Widgets.DrawHighlight(row);

            Color tint = ResearchBands.ColorFor(entry.Band, palette);

            Widgets.DrawBoxSolid(new Rect(row.x, row.y + 3f, SwatchWidth, row.height - 6f), tint);

            float x = row.x + SwatchWidth + 8f;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;

            GUI.color = tint;
            UIRichText.Label(new Rect(x, row.y, BandColumn - 8f, row.height),
                ResearchBands.LabelOf(entry.Band));

            x += BandColumn;

            GUI.color = palette.TextPrimary;
            Text.Font = GameFont.Small;

            float names = 210f;

            UIRichText.Label(new Rect(x, row.y, names - 8f, row.height), entry.Label);

            x += names;

            Text.Font = GameFont.Tiny;
            GUI.color = palette.TextSecondary;

            UIRichText.Label(new Rect(x, row.y, SourceColumn - 8f, row.height), entry.Source);

            x += SourceColumn;

            GUI.color = palette.TextDisabled;

            UIRichText.Label(new Rect(x, row.y, TabColumn - 8f, row.height), entry.Tab);

            x += TabColumn;

            UIRichText.Label(new Rect(x, row.y, Mathf.Max(0f, row.xMax - x), row.height), entry.Reason);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = palette.TextPrimary;

            // The reason is the column most likely to be cut off, so it is the tooltip as well.
            TooltipHandler.TipRegion(row, new TipSignal(
                () => entry.Label + "\n\n" + ResearchBands.LabelOf(entry.Band) + "\n" + entry.Reason
                      + "\n\nFrom " + entry.Source + ", on the " + entry.Tab + " tab.",
                entry.Project.GetHashCode()));
        }
    }
}
