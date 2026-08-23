using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Corpses
{
    /// <summary>Which reading of a body the pane is showing.</summary>
    internal enum CorpsePaneBody
    {
        Remains,

        Health,

        Gear,

        Bio,

        Social
    }

    /// <summary>
    /// One body in full, drawn by the inspect pane's own bodies.
    ///
    /// <b>Nothing new is written here to describe a corpse.</b> The inspect pane learned in 14157 to read a
    /// corpse as the person it was -- its overview carries the Remains block with time dead and rot stage, and
    /// health, gear, bio and social all unwrap the corpse to the pawn inside. A second description would be a
    /// second thing to keep in agreement with the first, and the two would disagree within a month.
    ///
    /// <b>The one thing this owns is which body is showing, and it owns it separately from the real pane.</b>
    /// <c>InspectPaneState</c> is the map selection's own state; borrowing it would mean opening a corpse here
    /// silently changed which tab a colonist opens with out there.
    /// </summary>
    internal static class CorpseBodyPane
    {
        internal const float PaneWidth = 360f;

        private const float HeaderHeight = 52f;

        private const float StripHeight = 26f;

        private const float PortraitSize = 44f;

        private static readonly CorpsePaneBody[] Bodies =
        {
            CorpsePaneBody.Remains, CorpsePaneBody.Health, CorpsePaneBody.Gear, CorpsePaneBody.Bio,
            CorpsePaneBody.Social
        };

        private static CorpsePaneBody selected = CorpsePaneBody.Remains;

        private static Vector2 scroll;

        /// <summary>Height the column came to last frame, remembered rather than predicted.</summary>
        private static float measured;

        private static Corpse measuredFor;

        /// <summary>
        /// Draws the pane, and answers whether the body is still worth drawing.
        ///
        /// A false return means close: the corpse was butchered, cremated, or hauled off the map while the pane
        /// was open. The caller closes rather than falling back to some other body.
        /// </summary>
        internal static bool Draw(Rect rect, CorpseEntry entry, UIColorPaletteDef palette, Action close)
        {
            if (entry == null || entry.Corpse == null || entry.Corpse.Destroyed || entry.Pawn == null)
                return false;

            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(10f);

            Header(new Rect(inner.x, inner.y, inner.width, HeaderHeight), entry, palette, close);

            Rect strip = new Rect(inner.x, inner.y + HeaderHeight, inner.width, StripHeight);

            Strip(strip, entry, palette);

            float top = strip.yMax + 6f;

            top = Raise(new Rect(inner.x, top, inner.width, 26f), entry, palette);

            Rect body = new Rect(inner.x, top, inner.width, Mathf.Max(0f, inner.yMax - top));

            if (measuredFor != entry.Corpse)
            {
                measuredFor = entry.Corpse;
                measured = 0f;
                scroll = Vector2.zero;
            }

            Rect view = new Rect(0f, 0f, body.width - 18f, measured > 0f ? measured : body.height);

            Widgets.BeginScrollView(body, ref scroll, view);

            Rect column = new Rect(0f, 0f, view.width, view.height);

            measured = Body(column, entry, palette) + 8f;

            Widgets.EndScrollView();

            return true;
        }

        private static float Body(Rect column, CorpseEntry entry, UIColorPaletteDef palette)
        {
            switch (selected)
            {
                case CorpsePaneBody.Health:
                    return InspectHealthBody.Draw(column, entry.Pawn, palette, false);

                case CorpsePaneBody.Gear:
                    return InspectGearBody.Draw(column, entry.Pawn, palette);

                case CorpsePaneBody.Bio:
                    return InspectBioBody.Draw(column, entry.Pawn, palette);

                case CorpsePaneBody.Social:
                    return InspectSocialBody.Draw(column, entry.Pawn, palette);

                default:
                    return InspectOverview.DrawPawn(column, entry.Pawn, palette);
            }
        }

        private static void Header(Rect rect, CorpseEntry entry, UIColorPaletteDef palette, Action close)
        {
            Rect portrait = new Rect(rect.x, rect.y, PortraitSize, PortraitSize);

            PawnPortraitCell.Draw(portrait, entry.Pawn, palette, palette.SurfaceSunken);

            Rect text = new Rect(rect.x + PortraitSize + 8f, rect.y, rect.width - PortraitSize - 34f,
                rect.height);

            float y = TabParts.Line(text, text.y, entry.Name, palette.TextPrimary);

            Rect pill = TabParts.Pill(text, text.x, y + 1f, CorpseFacts.StageTag(entry.Stage),
                CorpseFacts.StageColor(entry.Stage, palette), palette);

            TabParts.Line(new Rect(pill.xMax + 4f, y, Mathf.Max(20f, text.xMax - pill.xMax - 4f), 0f), y + 1f,
                entry.RotNote, palette.TextDisabled, GameFont.Tiny);

            if (Widgets.ButtonImage(new Rect(rect.xMax - 24f, rect.y, 24f, 24f), TexButton.CloseXSmall))
                close();
        }

        /// <summary>
        /// The way into the character editor's resurrect panel, when that tool is switched on.
        ///
        /// <b>In the pane rather than on the row,</b> which is a departure from the proposal. It drew a Bring back
        /// action on the row itself; the row's action column holds two buttons and the third would have been
        /// forty pixels wide, and more to the point the proposal's own argument was to keep the one irreversible
        /// operation away from a button next to Strip. Reaching it means clicking the body first, which is one
        /// deliberate step before the window that asks for another.
        ///
        /// Returns the y the body should start at, unchanged when nothing was drawn.
        /// </summary>
        private static float Raise(Rect rect, CorpseEntry entry, UIColorPaletteDef palette)
        {
            if (!Editor.EditorGate.Enabled)
                return rect.y;

            if (entry.Kind == CorpseKind.Animals || entry.Kind == CorpseKind.Mechanoids)
                return rect.y;

            if (TabParts.Button(rect, "Bring " + entry.Pawn.LabelShortCap + " back", palette, true, false,
                    "Opens the character editor on them, at the panel that resurrects. Nothing happens until "
                    + "you press the button there."))
                Editor.Dialog_CharacterEditor.Open(entry.Corpse);

            return rect.yMax + 6f;
        }

        private static void Strip(Rect rect, CorpseEntry entry, UIColorPaletteDef palette)
        {
            // Social has nothing to say about an animal or a mechanoid, and a tab that is always empty is worse
            // than one that is absent.
            bool social = entry.Kind == CorpseKind.Ours || entry.Kind == CorpseKind.Guests
                                                        || entry.Kind == CorpseKind.Hostiles;

            int count = social ? Bodies.Length : Bodies.Length - 1;

            float width = Mathf.Floor((rect.width - (count - 1) * 3f) / count);

            for (int i = 0; i < count; i++)
            {
                CorpsePaneBody body = Bodies[i];

                Rect slot = new Rect(rect.x + i * (width + 3f), rect.y, width, rect.height);

                CorpsePaneBody chosen = body;

                TabParts.Segment(slot, body.ToString(), selected == body, palette, () => selected = chosen);
            }

            if (!social && selected == CorpsePaneBody.Social)
                selected = CorpsePaneBody.Remains;
        }
    }
}
