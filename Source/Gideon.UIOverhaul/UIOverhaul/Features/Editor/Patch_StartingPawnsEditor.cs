using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Puts the character editor on RimWorld's starting characters page.
    ///
    /// <b>Asked for on 2026-08-23,</b> and it is the one screen in the game where a character editor is not a
    /// cheat at all: everybody there is being generated and regenerated on the spot, and the page already offers
    /// Randomize and a xenotype editor. What it does not offer is changing one thing. A player who likes the party
    /// except for one backstory has to reroll the pawn and lose the rest.
    ///
    /// <b>The whole party, selected and left behind.</b> <c>GameInitData.startingAndOptionalPawns</c> holds both,
    /// and both are worth editing -- somebody in the left behind column is one drag away from being in the colony.
    ///
    /// <b>Beside the xenotype editor, in its own words.</b> That button is the closest thing on the page to this
    /// one, so the two sit together and are drawn the same way, with vanilla's own button rather than one of ours:
    /// a differently styled button on a vanilla page reads as a different kind of thing. Without Biotech there is
    /// no xenotype button and the middle of the row is empty, so this takes the middle instead of sitting beside a
    /// gap.
    ///
    /// <b>A postfix rather than a replacement,</b> so the page draws exactly as Ludeon wrote it and this adds one
    /// button after the fact. Nothing about the page is suppressed.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents))]
    internal static class Patch_StartingPawnsEditor
    {
        /// <summary>
        /// <c>Page.BottomButSize</c> and <c>Page.BottomButHeight</c>, restated because the first is protected.
        ///
        /// Copied rather than reflected: they are a <c>static readonly Vector2</c> and a const that have not moved
        /// in years, and a reflection failure here would cost the button rather than misplace it.
        /// </summary>
        private const float ButtonWidth = 150f;

        private const float ButtonHeight = 38f;

        private const float Gap = 10f;

        /// <summary>
        /// Which pawn the page currently has open, so the editor starts on the same one.
        ///
        /// Private field, resolved once. A null here is survivable and handled: the editor opens on the first of
        /// the party instead, which is wrong rather than broken.
        /// </summary>
        private static readonly FieldInfo IndexField =
            AccessTools.Field(typeof(Page_ConfigureStartingPawns), "curPawnIndex");

        public static void Postfix(Page_ConfigureStartingPawns __instance, Rect rect)
        {
            UIGuard.Try("Editor.StartingPawnsButton", () => Draw(__instance, rect),
                "The character editor button is missing from the starting characters page. The page itself is "
                + "unaffected.");
        }

        private static void Draw(Page_ConfigureStartingPawns page, Rect rect)
        {
            if (!EditorGate.Enabled)
                return;

            List<Pawn> party = Party();

            if (party == null || party.Count == 0)
                return;

            // Both taken from vanilla's own xenotype button, and both immune to the yMin shift the page performs
            // on its rect before that button is drawn: raising yMin moves y and height together and leaves yMax
            // where it was, and the width is not touched at all. So this lands on the same row whichever value of
            // the rect the postfix is handed.
            float x = (rect.width - ButtonWidth) / 2f;

            if (ModsConfig.BiotechActive)
                x -= ButtonWidth + Gap;

            Rect button = new Rect(x, rect.yMax - ButtonHeight, ButtonWidth, ButtonHeight);

            GameFont previousFont = Text.Font;

            try
            {
                Text.Font = GameFont.Small;

                if (!Widgets.ButtonText(button, "Character editor"))
                    return;
            }
            finally
            {
                Text.Font = previousFont;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();

            Dialog_CharacterEditor.OpenGroup(Party, Chosen(page, party));
        }

        /// <summary>
        /// The starting party, read fresh every time.
        ///
        /// Handed to the editor as this method rather than as its result, because Randomize replaces a pawn with a
        /// newly generated object and the editor has to follow that rather than hold the discarded one.
        /// </summary>
        private static List<Pawn> Party()
        {
            GameInitData data = Current.Game == null ? null : Find.GameInitData;

            return data == null ? null : data.startingAndOptionalPawns;
        }

        private static Pawn Chosen(Page_ConfigureStartingPawns page, List<Pawn> party)
        {
            if (IndexField != null)
            {
                object value = IndexField.GetValue(page);

                if (value is int index && index >= 0 && index < party.Count)
                    return party[index];
            }

            return party[0];
        }
    }
}
