using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>
    /// Chooses who a grave is being kept for.
    ///
    /// <b>The dead come first and the living after, because those are two different decisions.</b> Reserving a
    /// grave for a body already on the ground is an instruction to go and bury that one; reserving it for
    /// somebody still walking about is a plan for later, and the game supports both through the same call. A
    /// list that mixed them would make the urgent half invisible.
    ///
    /// <b>Anyone at all is listed, not only colonists.</b> Vanilla's own assign gizmo offers colonists and dead
    /// colonists, which is why a guest who died in your hospital cannot be given a grave without opening its
    /// storage settings and reasoning about special filters. Reserving is per body and overrides the filter, so
    /// it is the shortest honest route to burying somebody who is not one of yours.
    /// </summary>
    internal class Dialog_PickBurial : Window
    {
        private const float HeaderHeight = 28f;

        private const float RowHeight = 30f;

        private const float FooterHeight = 34f;

        private const float StateWidth = 110f;

        private const float Pad = 8f;

        private readonly Map map;

        private readonly Pawn current;

        private readonly Action<Pawn> chosen;

        private readonly UITextBoxControl search = new UITextBoxControl
        {
            Placeholder = "Search the living and the dead",
            Icon = TexButton.Search,
            MaxLength = 40
        };

        /// <summary>The list as drawn: dead first, then the living, rebuilt whenever the search changes.</summary>
        private readonly List<Pawn> candidates = new List<Pawn>();

        /// <summary>How many of the leading entries are dead, so the divider knows where to go.</summary>
        private int deadCount;

        private Vector2 scroll;

        private Dialog_PickBurial(Map map, Pawn current, Action<Pawn> chosen)
        {
            this.map = map;
            this.current = current;
            this.chosen = chosen;

            doCloseX = false;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = false;
            drawShadow = true;
        }

        internal static void For(Map map, Pawn current, Action<Pawn> chosen)
        {
            if (map == null || chosen == null)
                return;

            Find.WindowStack.Add(new Dialog_PickBurial(map, current, chosen));
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(340f, 440f); }
        }

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
            UIGuardedPanel.Draw("Corpses.PickBurial", inRect, () => Contents(inRect),
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

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), "Keep this grave for");

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
                    Row(new Rect(0f, y, view.width, RowHeight - 2f), candidates[i], i < deadCount, palette);

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

        private void Gather()
        {
            candidates.Clear();
            deadCount = 0;

            UIGuard.Try("Corpses.PickGather", () =>
            {
                List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);

                for (int i = 0; corpses != null && i < corpses.Count; i++)
                {
                    Corpse corpse = corpses[i] as Corpse;

                    if (corpse == null || corpse.Bugged)
                        continue;

                    Pawn pawn = corpse.InnerPawn;

                    if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                        continue;

                    if (!Matches(pawn))
                        continue;

                    candidates.Add(pawn);
                }

                // Longest unburied first, which is the order in which they became a problem.
                candidates.Sort((a, b) => Age(b).CompareTo(Age(a)));

                deadCount = candidates.Count;

                List<Pawn> living = map.mapPawns.FreeColonistsSpawned;

                int before = candidates.Count;

                for (int i = 0; living != null && i < living.Count; i++)
                {
                    Pawn pawn = living[i];

                    if (pawn == null || pawn.Dead || !Matches(pawn))
                        continue;

                    candidates.Add(pawn);
                }

                candidates.Sort(before, candidates.Count - before,
                    Comparer<Pawn>.Create((a, b) =>
                        string.Compare(a.LabelShortCap, b.LabelShortCap, StringComparison.Ordinal)));
            }, null);
        }

        private bool Matches(Pawn pawn)
        {
            return search.IsEmpty || search.Matches(pawn.LabelShortCap);
        }

        private static int Age(Pawn pawn)
        {
            return UIGuard.Try("Corpses.PickAge",
                () => pawn.Corpse != null ? CorpseFacts.AgeOf(pawn.Corpse) : 0, 0, null);
        }

        private void Row(Rect rect, Pawn pawn, bool dead, UIColorPaletteDef palette)
        {
            bool chosenNow = pawn == current;

            UIElementPainter.OutlineRounded(rect, chosenNow ? palette.Accent : palette.Border,
                chosenNow
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : Mouse.IsOver(rect)
                        ? palette.SurfaceRaised
                        : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(rect.x + 6f, rect.y, rect.width - StateWidth - 10f, rect.height),
                    pawn.LabelShortCap);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = dead ? palette.Warning : palette.TextSecondary;

                Widgets.Label(new Rect(rect.xMax - StateWidth, rect.y, StateWidth - 6f, rect.height),
                    dead ? "dead " + Age(pawn).ToStringTicksToPeriodVague() : "alive");
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
            if (TabParts.Button(new Rect(rect.x, rect.y, rect.width * 0.5f - 4f, 28f), "Anyone", palette))
            {
                chosen(null);

                Close();
            }

            if (TabParts.Button(new Rect(rect.x + rect.width * 0.5f + 4f, rect.y, rect.width * 0.5f - 4f, 28f),
                    "Cancel", palette))
                Close();
        }
    }
}
