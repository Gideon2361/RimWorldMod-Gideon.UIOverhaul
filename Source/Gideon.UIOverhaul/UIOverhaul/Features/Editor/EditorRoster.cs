using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Everybody the colony is responsible for, as cards, so the editor can be moved from one to the next.
    ///
    /// <b>Only when the editor was opened from its own tab.</b> Asked for 2026-08-23. Reached from a pawn's bio
    /// panel the window is about that pawn and a list of eleven others would be an invitation to edit the wrong
    /// one; opened from the tab there is no pawn in mind yet, so the first thing the window has to do is ask which.
    ///
    /// <b>Colonists, prisoners and slaves, in that order.</b> Those are the three groups the player is answerable
    /// for and the three the editor is for. Guests, visitors and raiders are not listed: a passing trader is not
    /// somebody whose backstory you are rewriting, and putting them on this list would bury the colony under
    /// whoever happens to be standing on the map.
    ///
    /// <b>The card carries the five things that tell two colonists apart</b> -- face, name, sex, age and faction --
    /// because a column of names alone is exactly the list this replaced on the genes panel.
    /// </summary>
    internal static class EditorRoster
    {
        internal const float ColumnWidth = 214f;

        private const float CardHeight = 56f;

        private const float CardGap = 4f;

        private const float PortraitSize = 44f;

        private const float HeaderHeight = 30f;

        private const float ButtonsHeight = 30f;

        /// <summary>Scratch for one draw. Never held across frames, since a pawn can die between two of them.</summary>
        private static readonly List<Pawn> Listed = new List<Pawn>();

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        private static Vector2 scroll;

        /// <summary>
        /// Draws the column.
        ///
        /// <paramml name="chosen"/> is called with whoever was clicked, and <paramref name="templates"/> with
        /// nothing when the save or load button was pressed -- both of which are the host's business rather than
        /// this column's.
        /// </summary>
        internal static void Draw(Rect column, Pawn current, UIColorPaletteDef palette, Action<Pawn> chosen,
            Action templates, Func<List<Pawn>> source = null)
        {
            Widgets.DrawBoxSolid(column, palette.PanelBackground);

            Rect inner = column.ContractedBy(6f);

            Rect header = new Rect(inner.x, inner.y, inner.width, HeaderHeight - 4f);

            Search.Draw(header, palette);

            float y = header.yMax + 6f;

            Rect buttons = new Rect(inner.x, y, inner.width, ButtonsHeight - 4f);

            // One dialog behind both words. Save and load are the two directions of the same folder, and giving
            // them separate windows would have been two windows listing the same files.
            float half = Mathf.Floor((buttons.width - 4f) * 0.5f);

            if (TabParts.Button(new Rect(buttons.x, buttons.y, half, buttons.height), "Save", palette,
                    current != null, false,
                    "Writes whoever is open in the editor out to a file you can keep or send."))
                templates();

            if (TabParts.Button(new Rect(buttons.x + half + 4f, buttons.y, half, buttons.height), "Load",
                    palette, true, false,
                    "Applies a saved character to whoever is open, or lands a new colonist built from one."))
                templates();

            y = buttons.yMax + 6f;

            Gather(source);

            Rect list = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));

            float height = Listed.Count * (CardHeight + CardGap) + 4f;

            Rect view = new Rect(0f, 0f, list.width - (height > list.height ? 18f : 0f), height);

            Widgets.BeginScrollView(list, ref scroll, view);

            float cardY = 0f;

            for (int i = 0; i < Listed.Count; i++)
            {
                Card(new Rect(0f, cardY, view.width, CardHeight), Listed[i], current, palette, chosen);

                cardY += CardHeight + CardGap;
            }

            Widgets.EndScrollView();

            if (Listed.Count == 0)
            {
                GameFont previousFont = Text.Font;
                Color previousColor = GUI.color;

                try
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = palette.TextDisabled;

                    Widgets.Label(new Rect(list.x + 2f, list.y + 4f, list.width - 4f, 40f),
                        Search.IsEmpty ? "Nobody here." : "Nobody here matches that.");
                }
                finally
                {
                    GUI.color = previousColor;
                    Text.Font = previousFont;
                }
            }

            Listed.Clear();
        }

        /// <summary>
        /// Colonists, then prisoners, then slaves, across every loaded map -- unless the host names its own list.
        ///
        /// Every map rather than the current one, since the pawns and animals tabs already work that way and a
        /// colonist on a gravship is still somebody you might be editing.
        ///
        /// <b>A host that supplies a source replaces the search entirely rather than adding to it.</b> The one
        /// that does is the starting characters page, where the people being edited are on no map at all -- they
        /// are held in <c>GameInitData</c> until the game begins -- so a map walk finds nothing and adding to it
        /// would find nothing either. Asking the host each frame rather than taking a snapshot is deliberate:
        /// that page's Randomize button replaces a pawn with a different object, and a snapshot would leave the
        /// column listing somebody who no longer exists.
        /// </summary>
        private static void Gather(Func<List<Pawn>> source)
        {
            Listed.Clear();

            UIGuard.Try("Editor.Roster", () =>
            {
                if (source != null)
                {
                    Take(source());

                    return;
                }

                List<Map> maps = Find.Maps;

                Take(maps, map => map.mapPawns.FreeColonistsSpawned);
                Take(maps, map => map.mapPawns.PrisonersOfColonySpawned);
                Take(maps, map => map.mapPawns.SlavesOfColonySpawned);
            }, null);
        }

        private static void Take(List<Map> maps, Func<Map, List<Pawn>> from)
        {
            for (int m = 0; maps != null && m < maps.Count; m++)
            {
                Map map = maps[m];

                if (map == null || map.mapPawns == null)
                    continue;

                Take(from(map));
            }
        }

        /// <summary>
        /// Adds one list, skipping the dead, the duplicated and whatever the search box rules out.
        ///
        /// Shared by both sources so the supplied list is filtered exactly as a map walk would be. A separate copy
        /// of these three tests is how the search box ends up working on one host and not the other.
        /// </summary>
        private static void Take(List<Pawn> found)
        {
            for (int i = 0; found != null && i < found.Count; i++)
            {
                Pawn pawn = found[i];

                if (pawn == null || pawn.Dead || Listed.Contains(pawn))
                    continue;

                if (!Search.IsEmpty && !Search.Matches(pawn.LabelShortCap))
                    continue;

                Listed.Add(pawn);
            }
        }

        private static void Card(Rect card, Pawn pawn, Pawn current, UIColorPaletteDef palette,
            Action<Pawn> chosen)
        {
            bool open = pawn == current;

            // Composited, because an outline is two fills and a translucent inside would land on the border
            // colour rather than on the surface.
            UIElementPainter.OutlineRounded(card, open ? palette.Accent : palette.Border,
                open
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : Mouse.IsOver(card)
                        ? palette.SurfaceRaised
                        : palette.PanelBackground);

            Rect portrait = new Rect(card.x + 4f, card.y + (card.height - PortraitSize) * 0.5f, PortraitSize,
                PortraitSize);

            // Behind is the card's own fill rather than the flat panel colour, since the circular crop is done by
            // painting over the corners and has to paint what is actually there.
            //
            // jumpOnClick: false, because this window never calls PawnCameraJump.Resolve. A request left pending
            // here would fire the next time any other panel resolved one, closing that panel and jumping the
            // camera for a click made in a different window.
            PawnPortraitCell.Draw(portrait, pawn, palette,
                open
                    ? UIElementPainter.Composite(palette.PanelBackground, palette.SelectionOverlay)
                    : palette.PanelBackground, false);

            Rect text = new Rect(portrait.xMax + 6f, card.y + 5f,
                Mathf.Max(20f, card.xMax - portrait.xMax - 10f), card.height - 10f);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;

                float x = text.x;

                float glyph = UIGuard.Try("Editor.CardGlyph",
                    () => GenderGlyphs.Draw(new Rect(text.x, text.y, text.width,
                        UIFonts.LineHeightOf(GameFont.Small)), pawn, palette), 0f, null);

                x += glyph;

                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                UIRichText.Label(new Rect(x, text.y, Mathf.Max(20f, text.xMax - x),
                    UIFonts.LineHeightOf(GameFont.Small)), pawn.LabelShortCap);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                UIRichText.Label(new Rect(text.x, text.y + UIFonts.LineHeightOf(GameFont.Small), text.width,
                    UIFonts.LineHeightOf(GameFont.Tiny)), Line(pawn));
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            // The portrait draws its own camera jump, which is the wrong thing from inside this window: it would
            // close the tab. So the whole card is one target and the click stays here.
            if (Widgets.ButtonInvisible(card) && !open)
                chosen(pawn);
        }

        /// <summary>Age, standing and faction: the line that tells two colonists called Mei apart.</summary>
        private static string Line(Pawn pawn)
        {
            return UIGuard.Try<string>("Editor.CardLine", () =>
            {
                string age = pawn.ageTracker != null ? pawn.ageTracker.AgeBiologicalYears.ToString() : null;

                string standing = pawn.IsSlaveOfColony
                    ? "slave"
                    : pawn.IsPrisonerOfColony
                        ? "prisoner"
                        : null;

                string faction = pawn.Faction != null ? pawn.Faction.NameColored.Resolve() : null;

                List<string> parts = new List<string>();

                if (!age.NullOrEmpty())
                    parts.Add(age);

                if (standing != null)
                    parts.Add(standing);

                if (faction != null)
                    parts.Add(faction);

                return parts.Count == 0 ? null : string.Join(" - ", parts.ToArray());
            }, null, null);
        }
    }
}
