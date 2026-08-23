using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// Picks one person out of the colony, sorted by how good they are at the thing being asked of them.
    ///
    /// <b>A window rather than a float menu, and the same one the animals tab's master picker established.</b>
    /// Choosing a nurse is a comparison between people, so the list has to say what distinguishes them: their
    /// Medicine skill, and whether doctoring is switched off. A float menu could show neither, and the answer to
    /// "who should do this" is not a list of eight names.
    ///
    /// <b>Somebody with doctoring switched off is listed and labelled, not hidden.</b> They are still the best
    /// answer some of the time, and turning the priority back on is one click; leaving them out of a list of
    /// eight names is how a player concludes the colony has nobody.
    /// </summary>
    internal class Dialog_PickColonist : Window
    {
        private const float HeaderHeight = 28f;

        private const float RowHeight = 30f;

        private const float FooterHeight = 34f;

        private const float SkillWidth = 78f;

        private const float Pad = 8f;

        private readonly Map map;

        private readonly string heading;

        private readonly Action<Pawn> chosen;

        private readonly Pawn current;

        /// <summary>Whether "anyone" is an answer. True for a nurse, false for a patient.</summary>
        private readonly bool optional;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search colonists",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        private readonly List<Pawn> candidates = new List<Pawn>();

        private Vector2 scroll;

        private Dialog_PickColonist(Map map, string heading, Action<Pawn> chosen, Pawn current, bool optional)
        {
            this.map = map;
            this.heading = heading;
            this.chosen = chosen;
            this.current = current;
            this.optional = optional;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = false;
            drawShadow = true;
        }

        internal static void For(Map map, string heading, Action<Pawn> chosen, Pawn current,
            bool optional = false)
        {
            if (map == null || chosen == null)
                return;

            Find.WindowStack.Add(new Dialog_PickColonist(map, heading, chosen, current, optional));
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(320f, 420f); }
        }

        /// <summary>
        /// Opened at the cursor and clamped so it cannot hang off an edge.
        ///
        /// The placement rule a float menu follows, for the same reason: this window is the answer to a control
        /// that was just clicked and has to appear next to it.
        /// </summary>
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
            UIGuardedPanel.Draw("Hospital.PickColonist", inRect, () => Contents(inRect),
                "The picker failed to draw. Nothing has been changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), heading);

                Rect box = new Rect(inRect.x, inRect.y + HeaderHeight, inRect.width, 26f);

                search.Draw(box, palette);

                Gather();

                Rect list = new Rect(inRect.x, box.yMax + Pad, inRect.width,
                    Mathf.Max(0f, inRect.height - HeaderHeight - 26f - FooterHeight - Pad * 2f));

                Rect view = new Rect(0f, 0f, list.width - 18f, candidates.Count * RowHeight + 4f);

                Widgets.BeginScrollView(list, ref scroll, view);

                float y = 0f;

                for (int i = 0; i < candidates.Count; i++)
                {
                    Row(new Rect(0f, y, view.width, RowHeight - 2f), candidates[i], palette);

                    y += RowHeight;
                }

                Widgets.EndScrollView();

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>Everybody the colony could ask, best at medicine first.</summary>
        private void Gather()
        {
            candidates.Clear();

            UIGuard.Try("Hospital.PickGather", () =>
            {
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;

                if (colonists == null)
                    return;

                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn pawn = colonists[i];

                    if (pawn == null || pawn.Dead)
                        continue;

                    if (!search.IsEmpty && !search.Matches(pawn.LabelShortCap))
                        continue;

                    candidates.Add(pawn);
                }

                candidates.SortByDescending(HospitalSurgery.SkillOf);
            }, null);
        }

        private void Row(Rect rect, Pawn pawn, UIColorPaletteDef palette)
        {
            bool chosenNow = pawn == current;

            // Composited rather than translucent, for the reason spelled out on UIElementPainter.Composite: an
            // outline is two fills, and an overlay handed in as the inside lands on the border colour.
            UIElementPainter.OutlineRounded(rect, chosenNow ? palette.Accent : palette.Border,
                chosenNow
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : Mouse.IsOver(rect)
                        ? palette.SurfaceRaised
                        : palette.PanelBackground);

            bool disabled = UIGuard.Try("Hospital.PickDisabled",
                () => pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor), false, null);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = disabled ? palette.TextDisabled : palette.TextPrimary;

                Widgets.Label(new Rect(rect.x + 6f, rect.y, rect.width - SkillWidth - 10f, rect.height),
                    pawn.LabelShortCap);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = disabled ? palette.Warning : palette.TextSecondary;

                Widgets.Label(new Rect(rect.xMax - SkillWidth, rect.y, SkillWidth - 6f, rect.height),
                    disabled ? "no doctoring" : "Medicine " + HospitalSurgery.SkillOf(pawn));
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            if (!Widgets.ButtonInvisible(rect))
                return;

            chosen(pawn);

            Close();
        }

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            if (optional && HospitalParts.Button(new Rect(rect.x, rect.y, rect.width * 0.5f - 4f, 28f), "Anyone",
                    palette))
            {
                chosen(null);

                Close();
            }

            float x = optional ? rect.x + rect.width * 0.5f + 4f : rect.x + rect.width * 0.5f;
            float width = optional ? rect.width * 0.5f - 4f : rect.width * 0.5f;

            if (HospitalParts.Button(new Rect(x, rect.y, width, 28f), "Cancel", palette))
                Close();
        }
    }
}
