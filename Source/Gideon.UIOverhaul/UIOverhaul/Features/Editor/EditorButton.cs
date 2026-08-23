using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// The editor's icon in the inspect pane's corner, beside the info card button.
    ///
    /// <b>Here rather than on the Bio panel, asked for 2026-08-23.</b> A button on Bio can only be reached on
    /// something that has a Bio panel, and a corpse does not -- which made a dead pawn, the one selection you most
    /// want the editor for, the one selection that could not open it. The corner is where every per-selection tool
    /// already lives, so it works on a colonist, a raider, an animal and a body without any of them being a
    /// special case.
    ///
    /// <b>To the left of vanilla's buttons and it reports its width,</b> which is the contract
    /// <c>DoInspectPaneButtons</c> already uses: the header lays the name out around whatever the corner took, so
    /// an icon that appeared without saying so would have a long name run underneath it.
    ///
    /// <b>Nothing is drawn when the editor is switched off,</b> not even a disabled icon. That is the whole of the
    /// setting: with it off there is no evidence the tool exists.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class EditorButton
    {
        /// <summary>Vanilla's own corner button size, so the row of them lines up.</summary>
        private const float Size = InspectPaneUtility.CornerButtonsSize;

        /// <summary>
        /// The tab's icon, reused at corner size.
        ///
        /// The same picture in both places on purpose: somebody who has found the tab recognises the button, and
        /// somebody who has found the button recognises the tab. Loaded once at startup rather than per frame,
        /// since a miss in ContentFinder walks every loaded mod's content.
        /// </summary>
        private static readonly Texture2D Icon;

        static EditorButton()
        {
            Icon = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/CharacterEditor", false);
        }

        /// <summary>
        /// Draws the button and returns how much of the right edge it used.
        ///
        /// Zero when nothing was drawn, which is the answer for a selection with no pawn in it and for a game
        /// where the editor is switched off.
        /// </summary>
        internal static float Draw(Rect header, float usedAlready, Thing thing, Pawn pawn,
            UIColorPaletteDef palette)
        {
            if (!EditorGate.Enabled || pawn == null || Icon == null)
                return 0f;

            return UIGuard.Try("Editor.CornerButton", () =>
            {
                Rect button = new Rect(header.width - usedAlready - Size, 0f, Size, Size);

                MouseoverSounds.DoRegion(button);

                bool over = Mouse.IsOver(button);

                Color previous = GUI.color;

                try
                {
                    // Tinted rather than drawn white: the icon is flat greyscale like every main button icon in
                    // this mod, so it takes the palette the same way they do, and brightening on hover is the
                    // only feedback a bare icon can carry.
                    GUI.color = over ? palette.TextPrimary : palette.TextSecondary;

                    GUI.DrawTexture(button, Icon, ScaleMode.ScaleToFit);
                }
                finally
                {
                    GUI.color = previous;
                }

                TooltipHandler.TipRegion(button, (TipSignal) Tip(pawn));

                if (Widgets.ButtonInvisible(button))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();

                    // The thing rather than the pawn, so a corpse opens the editor with its resurrect panel
                    // available. Open unwraps it either way.
                    Dialog_CharacterEditor.Open(thing ?? pawn);
                }

                return Size;
            }, 0f, null);
        }

        private static string Tip(Pawn pawn)
        {
            return UIGuard.Try<string>("Editor.CornerTip", () =>
            {
                string who = pawn.LabelShortCap;

                return pawn.Dead
                    ? "Open the character editor on " + who + ". A dead pawn can also be brought back from it."
                    : "Open the character editor on " + who + ".";
            }, "Open the character editor.", null);
        }
    }
}
