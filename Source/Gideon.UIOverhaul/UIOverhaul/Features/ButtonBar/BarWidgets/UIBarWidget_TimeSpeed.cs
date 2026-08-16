using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.ButtonBar.BarWidgets
{
    /// <summary>
    /// Pause and the three play speeds, as buttons on the bar.
    ///
    /// <b>Clicks only.</b> Vanilla's <c>TimeControls.DoTimeControlsGUI</c> is two things at once: the four
    /// buttons in the bottom right, and the keyboard handling for every time-related key binding. Only the
    /// buttons are reproduced here. The vanilla control still draws and still runs, so space, the number
    /// keys, faster, slower and the dev-mode single tick all keep working exactly as they did, and this
    /// widget cannot disagree with them about what the speed is.
    ///
    /// <b>Ultrafast is not offered</b>, matching vanilla: the enum has five values and its own GUI skips the
    /// fifth, leaving it reachable only by its key binding in dev mode. A button the base game deliberately
    /// does not draw is not ours to add.
    /// </summary>
    public class UIBarWidget_TimeSpeed : UIBarWidgetWorker
    {
        /// <summary>
        /// Width of one speed button.
        ///
        /// 26 rather than 30. At 30 the four buttons filled the tray almost edge to edge and read as a block
        /// rather than as four controls sitting in a frame; the tray is meant to be visible around them.
        /// </summary>
        private const float ButtonWidth = 26f;

        /// <summary>Inset from the tray at the sides and the bottom.</summary>
        private const float Pad = 3f;

        /// <summary>
        /// Inset from the top of the tray, which is more than <see cref="Pad"/> because the bar paints its
        /// accent rule across the first few pixels of every slot.
        ///
        /// Taken off the renderer's own constant rather than written as a number, so the buttons keep clear of
        /// the rule if its thickness ever changes. At a plain <see cref="Pad"/> they began exactly where the
        /// rule ended and appeared welded to it.
        /// </summary>
        private static float TopInset => UIButtonBarRenderer.AccentRuleHeight + Pad;

        /// <summary>Thickness of the rule under the speed that is in effect.</summary>
        private const float RuleHeight = 2f;

        private static readonly TimeSpeed[] Shown =
        {
            TimeSpeed.Paused,
            TimeSpeed.Normal,
            TimeSpeed.Fast,
            TimeSpeed.Superfast
        };

        protected override bool ShouldShow =>
            Current.ProgramState == ProgramState.Playing && Find.TickManager != null;

        protected override float MeasureWidth()
        {
            return Shown.Length * ButtonWidth + Pad * 2f;
        }

        public override void Draw(Rect rect, UIColorPaletteDef palette)
        {
            TickManager ticks = Find.TickManager;
            if (ticks == null)
                return;

            // What the game is actually doing, which is not always what CurTimeSpeed says: a window that
            // forces pause leaves the stored speed alone and overrides it. Showing the stored value there
            // would light up "normal" while the colony sat still.
            TimeSpeed active = ticks.ForcePaused ? TimeSpeed.Paused : ticks.CurTimeSpeed;

            float height = Mathf.Max(1f, rect.height - TopInset - Pad);
            float y = rect.y + TopInset;

            // Centered rather than left-aligned, so a tray wider than the buttons (a widget cannot shrink
            // below its high-water width) keeps them in the middle of it instead of leaving all the slack
            // on one side.
            float x = rect.center.x - Shown.Length * ButtonWidth * 0.5f;
            float firstX = x;

            for (int i = 0; i < Shown.Length; i++)
            {
                DrawSpeedButton(new Rect(x, y, ButtonWidth, height), Shown[i], active, ticks, palette);
                x += ButtonWidth;
            }

            // Struck through when something else is deciding the speed, the way vanilla does it: the buttons
            // are still there and still say what they are, but a rule across them says they are not in
            // charge at the moment. Warning rather than Danger -- being overridden is a state to notice, not
            // a fault.
            if (ticks.ForcePaused)
                DrawStrike(firstX, y, height, 1, palette);
            else if (ticks.slower.ForcedNormalSpeed)
                DrawStrike(firstX, y, height, 2, palette);
        }

        /// <summary>
        /// A rule across the buttons from <paramref name="fromIndex"/> to the end, marking them as overridden.
        /// </summary>
        private static void DrawStrike(float firstX, float y, float height, int fromIndex,
            UIColorPaletteDef palette)
        {
            float x = firstX + fromIndex * ButtonWidth;
            float width = (Shown.Length - fromIndex) * ButtonWidth;

            Widgets.DrawBoxSolid(new Rect(x, y + height * 0.5f - 1f, width, 2f), palette.Warning);
        }

        private static void DrawSpeedButton(Rect button, TimeSpeed speed, TimeSpeed active,
            TickManager ticks, UIColorPaletteDef palette)
        {
            bool over = Mouse.IsOver(button);
            bool held = over && Input.GetMouseButton(0);
            bool isActive = active == speed;

            // Borderless, like the bar's tab buttons: four of these abut, and each one's outline would
            // double against its neighbor's into a heavy grid.
            UIElementPainter.PaintButton(button, palette, over, held, false, false);

            if (isActive)
            {
                Widgets.DrawBoxSolid(button, palette.SelectionOverlay);
                Widgets.DrawBoxSolid(
                    new Rect(button.x, button.yMax - RuleHeight, button.width, RuleHeight),
                    palette.Success);
            }

            Texture2D icon = TexButton.SpeedButtonTextures[(int) speed];

            if (icon != null)
            {
                Color previous = GUI.color;
                GUI.color = isActive || over ? palette.TextPrimary : palette.TextSecondary;
                GUI.DrawTexture(button.ContractedBy(4f), icon, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }

            // Built only while the pointer is on this button. The hotkey lookup and the string it builds are
            // cheap individually and pointless four times a frame for tooltips nobody is reading.
            if (over)
                TooltipHandler.TipRegion(button, (TipSignal) Tooltip(speed));

            if (!Widgets.ButtonInvisible(button))
                return;

            // Vanilla ignores clicks entirely while pause is forced, rather than storing a speed that would
            // take effect later. Same here: the buttons are visibly struck through, so a click that appeared
            // to do nothing is explained by what is already on screen.
            if (ticks.ForcePaused)
                return;

            if (speed == TimeSpeed.Paused)
            {
                ticks.TogglePaused();
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.Pause,
                    KnowledgeAmount.SpecificInteraction);
            }
            else
            {
                ticks.CurTimeSpeed = speed;
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.TimeControls,
                    KnowledgeAmount.SpecificInteraction);
            }

            // Both of these are vanilla's, kept so using this widget instead of the bottom-right control is
            // indistinguishable: the clock sounds are how the game confirms a speed change, and the concept
            // tracking is what stops the tutorial nagging about a control the player has clearly found.
            PlaySoundOf(ticks.CurTimeSpeed);
        }

        private static void PlaySoundOf(TimeSpeed speed)
        {
            SoundDef sound;

            switch (speed)
            {
                case TimeSpeed.Paused:
                    sound = SoundDefOf.Clock_Stop;
                    break;
                case TimeSpeed.Normal:
                    sound = SoundDefOf.Clock_Normal;
                    break;
                case TimeSpeed.Fast:
                    sound = SoundDefOf.Clock_Fast;
                    break;
                default:
                    sound = SoundDefOf.Clock_Superfast;
                    break;
            }

            sound?.PlayOneShotOnCamera();
        }

        private static string Tooltip(TimeSpeed speed)
        {
            string label = LabelOf(speed);
            KeyBindingDef binding = BindingFor(speed);

            if (binding == null)
                return label;

            // The bound key rather than the def's default, and slot A because that is the one the options
            // page treats as the primary binding.
            string key = KeyPrefs.KeyPrefsData
                .GetBoundKeyCode(binding, KeyPrefs.BindingSlot.A)
                .ToStringReadable();

            return label + "\n" + "HotKeyTip".Translate() + ": " + key;
        }

        private static string LabelOf(TimeSpeed speed)
        {
            switch (speed)
            {
                case TimeSpeed.Paused: return "Pause";
                case TimeSpeed.Normal: return "Normal speed";
                case TimeSpeed.Fast: return "Fast";
                default: return "Superfast";
            }
        }

        private static KeyBindingDef BindingFor(TimeSpeed speed)
        {
            switch (speed)
            {
                case TimeSpeed.Paused: return KeyBindingDefOf.TogglePause;
                case TimeSpeed.Normal: return KeyBindingDefOf.TimeSpeed_Normal;
                case TimeSpeed.Fast: return KeyBindingDefOf.TimeSpeed_Fast;
                case TimeSpeed.Superfast: return KeyBindingDefOf.TimeSpeed_Superfast;
                default: return null;
            }
        }
    }
}
