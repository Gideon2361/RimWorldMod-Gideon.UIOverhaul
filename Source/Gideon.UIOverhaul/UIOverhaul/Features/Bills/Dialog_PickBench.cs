using System;
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
    /// A window whose only job is to name a bench.
    ///
    /// <b>Used by the template flows,</b> which need a bench and nothing else: applying a bill template with no
    /// bill selected, and importing a bench template. The Add bill wizard asks the same question as its first step
    /// rather than opening this, because there it is one step of three rather than the whole interaction.
    ///
    /// The grid itself is <see cref="BenchGrid"/>, shared between the two so they cannot give different answers.
    ///
    /// <b>A click is the choice.</b> There is no confirm step: a bench is not a decision anybody reviews, and the
    /// window closes on the click, before the callback runs, so a caller that opens another window is not opening
    /// it underneath this one.
    /// </summary>
    public class Dialog_PickBench : Window
    {
        private const float HeaderHeight = 46f;
        private const float FooterHeight = 52f;
        private const float Pad = 12f;
        private const float EdgeInset = 8f;

        private readonly string title;
        private readonly string hint;
        private readonly Action<Building_WorkTable> chosen;

        /// <summary>Whether a bench at its bill cap can still be picked. False for anything that adds a bill.</summary>
        private readonly bool allowFull;

        private readonly BenchGrid grid = new BenchGrid();

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search benches...",
            MaxLength = 60
        };

        public Dialog_PickBench(string heading, string footerHint, bool full, Action<Building_WorkTable> picked)
        {
            title = heading;
            hint = footerHint;
            allowFull = full;
            chosen = picked;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(900f, 620f);

        protected override float Margin => 0f;

        public override void DoWindowContents(Rect inRect)
        {
            UIWindowDrag.TitleBarOnly(this, inRect.y + HeaderHeight);

            UIGuardedPanel.Draw("Bills.PickBench", inRect, () => Contents(inRect),
                "The bench list could not be drawn. Nothing has been changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            Widgets.DrawBoxSolid(inRect, GzpPalette.BGD);
            Text.Font = GameFont.Small;

            Header(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight));

            Rect body = new Rect(inRect.x + EdgeInset, inRect.y + HeaderHeight, inRect.width - EdgeInset * 2f,
                inRect.height - HeaderHeight - FooterHeight);

            Building_WorkTable picked = grid.Draw(body, search.Text, allowFull, palette);

            Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight));

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (picked == null)
                return;

            Close();

            chosen?.Invoke(picked);
        }

        private void Header(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            Color previous = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = GzpPalette.Stat;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 8f, rect.width - 320f, 30f), title);

            Text.Font = GameFont.Small;
            GUI.color = previous;

            Rect close = new Rect(rect.xMax - Pad - 24f, rect.y + 11f, 24f, 24f);

            search.Draw(new Rect(close.x - 10f - 240f, rect.y + 10f, 240f, 26f));

            if (GzpPalette.IconButton(close, GzpTex.Close, "Close"))
                Close();
        }

        private void Footer(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, GzpPalette.BGD);

            Color previous = GUI.color;
            GUI.color = GzpPalette.TextDim;

            Widgets.Label(new Rect(rect.x + Pad, rect.y + 16f, rect.width - 180f, 24f), hint);

            GUI.color = previous;

            if (GzpPalette.GrayButton(new Rect(rect.xMax - Pad - 110f, rect.y + 10f, 110f, 32f), "Cancel"))
                Close();
        }
    }
}
