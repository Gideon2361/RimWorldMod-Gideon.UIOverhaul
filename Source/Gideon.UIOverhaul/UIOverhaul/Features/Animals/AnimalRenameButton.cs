using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The rename icon in the inspect pane's corner, for a colony animal.
    ///
    /// <b>Vanilla has this button and almost never shows it.</b> <c>MainTabWindow_Inspect.DoInspectPaneButtons</c>
    /// draws a pawn's rename icon only when
    /// <c>p.Faction == Faction.OfPlayer &amp;&amp; p.RaceProps.Animal &amp;&amp; p.RaceProps.hideTrainingTab</c>, or
    /// for a colony mech. That last condition is the problem: <c>hideTrainingTab</c> is declared by two defs in
    /// the whole game -- one Ideology special animal and one Odyssey drone -- so for every ordinary tamed animal
    /// the button is suppressed, and there is no other way to rename one from the pane. Asked for by Aaron on
    /// 2026-08-25, on a barn owl that could not be renamed.
    ///
    /// <b>The only thing widened is when it draws.</b> The icon, the tooltip and the dialog all come from
    /// <see cref="RenameUIUtility.DrawRenameButton(Rect, Pawn)"/>, which is public and which calls
    /// <c>pawn.NamePawnDialog()</c> -- so the naming window is vanilla's, the <c>RenameAnimal</c> tooltip is
    /// already translated everywhere the game is, and if Ludeon changes how a pawn is named this follows without
    /// being touched. Reimplementing any of that would be inventing a second answer to a question the game has
    /// already answered.
    ///
    /// <b>Deliberately not drawn for the two defs vanilla does cover,</b> or the pane would carry two rename
    /// icons side by side on exactly those selections.
    ///
    /// <b>To the left of vanilla's buttons and it reports its width,</b> the same contract
    /// <c>DoInspectPaneButtons</c> and <see cref="Editor.EditorButton"/> use: the header lays the name out around
    /// whatever the corner took, so an icon that appeared without saying so would have a long name run underneath
    /// it.
    /// </summary>
    internal static class AnimalRenameButton
    {
        /// <summary>Vanilla's own corner button size, so the row of them lines up.</summary>
        private const float Size = InspectPaneUtility.CornerButtonsSize;

        /// <summary>
        /// Draws the button and returns how much of the right edge it used.
        ///
        /// Zero when nothing was drawn: a selection with no pawn in it, anything that is not a tamed animal, and
        /// the two defs whose button vanilla already draws.
        /// </summary>
        internal static float Draw(Rect header, float usedAlready, Pawn pawn)
        {
            if (!Renameable(pawn))
                return 0f;

            return UIGuard.Try("Animals.RenameButton", () =>
            {
                Rect button = new Rect(header.width - usedAlready - Size, 0f, Size, Size);

                MouseoverSounds.DoRegion(button);

                // Vanilla's helper: its icon, its tooltip key, and its naming dialog.
                RenameUIUtility.DrawRenameButton(button, pawn);

                return Size;
            }, 0f, null);
        }

        /// <summary>
        /// Whether this selection is an animal of ours that vanilla has left without a rename button.
        ///
        /// A wild animal has no faction, so the faction test is also what keeps this off everything roaming the
        /// map. Mechanoids never reach the animal test, which is why there is nothing here about colony mechs:
        /// vanilla's own branch covers those and <c>RaceProps.Animal</c> is false for them.
        /// </summary>
        private static bool Renameable(Pawn pawn)
        {
            return pawn != null
                   && pawn.RaceProps != null
                   && pawn.RaceProps.Animal
                   && !pawn.RaceProps.hideTrainingTab
                   && pawn.Faction == Faction.OfPlayer;
        }
    }
}
