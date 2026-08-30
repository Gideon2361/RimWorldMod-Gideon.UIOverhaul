using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Offers;
using Gideon.UIOverhaul.Features.Options;
using Gideon.UIOverhaul.Shared;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Quests
{
    /// <summary>
    /// A person a quest is offering, read the way a colonist is read, with nothing changeable.
    ///
    /// <b>The panel is called, not written again.</b> <see cref="OfferPawnPanel"/> shipped in 14167 for the
    /// letters that offer a pawn, and it draws from the inspect pane's own blocks. Reaching it from the quests
    /// tab as well means a person read here and the same person read on their letter, or after they join,
    /// cannot disagree; a second implementation would have drifted the first time either was touched.
    ///
    /// <b>Gated on the same setting for the same reason.</b> Somebody who turned pawn details off on letters did
    /// not ask for them on quests, and a feature that respects a switch in one place and ignores it in another
    /// is a switch that has stopped meaning anything.
    ///
    /// <b>Read only, and it says so.</b> The window carries a line across the top saying nothing can be changed
    /// until they join, because the panels are the ones the character editor draws and somebody who has used
    /// that editor will reasonably expect to be able to touch them.
    /// </summary>
    internal static class QuestPawnSheet
    {
        /// <summary>Whether an offered person can be opened at all.</summary>
        internal static bool Enabled
        {
            get
            {
                return UIGuard.Try("Quests.SheetGate", () =>
                {
                    UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                    return settings != null && settings.pawnDetailsOnOffers;
                }, false, null);
            }
        }

        internal static void Open(Pawn pawn)
        {
            if (pawn == null || !Enabled)
                return;

            UIGuard.Try("Quests.OpenSheet", () => Find.WindowStack.Add(new Dialog_QuestPawn(pawn)),
                "The offered person's details could not be opened. The quest itself is unaffected.");
        }
    }

    /// <summary>The window itself.</summary>
    public class Dialog_QuestPawn : Window
    {
        private readonly Pawn pawn;
        private readonly List<Pawn> one = new List<Pawn>();

        private Vector2 scroll;
        private float contentHeight = 1f;

        public Dialog_QuestPawn(Pawn subject)
        {
            pawn = subject;
            one.Add(subject);

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
            draggable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(OfferPawnPanel.Width + 60f, 620f); }
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Quests.PawnSheet", inRect, () =>
            {
                UIColorPaletteDef palette = UIColorPaletteDef.Active;

                Widgets.DrawBoxSolid(inRect, palette.WindowBackground);

                Rect body = inRect.ContractedBy(12f);

                TabParts.RowLabel(new Rect(body.x, body.y, body.width - 30f, 28f),
                    UIGuard.Try("Quests.SheetName", () => pawn.LabelShortCap.ToString(), "Offered", null),
                    palette.Accent, GameFont.Medium, QuestFaces.Display, QuestFaces.Size.Title);

                Rect notice = new Rect(body.x, body.y + 30f, body.width, 22f);

                UIElementPainter.FillRounded(notice,
                    UIElementPainter.Composite(palette.PanelBackground, palette.HoverOverlay));

                TabParts.RowLabel(new Rect(notice.x + 8f, notice.y, notice.width - 16f, notice.height),
                    QuestFaces.Caps("Read only until they join you"), palette.Warning, GameFont.Tiny,
                    QuestFaces.Mono, QuestFaces.Size.BlockHead);

                Rect outer = new Rect(body.x, notice.yMax + 8f, body.width, body.yMax - notice.yMax - 8f);
                Rect view = new Rect(0f, 0f, outer.width - 18f, contentHeight);

                Widgets.BeginScrollView(outer, ref scroll, view);

                float used = OfferPawnPanel.Draw(view, one, palette);

                if (Event.current.type == EventType.Layout)
                    contentHeight = Mathf.Max(1f, used);

                Widgets.EndScrollView();
            }, "This person's details could not be drawn. The quest offering them is unaffected.");
        }
    }
}
