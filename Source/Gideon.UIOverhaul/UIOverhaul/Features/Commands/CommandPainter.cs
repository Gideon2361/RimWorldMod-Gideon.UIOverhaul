using System;
using System.Reflection;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Commands
{
    /// <summary>
    /// Draws a command button in this mod's theme.
    ///
    /// <b>We draw the chrome; the game draws the icon.</b> <c>DrawIcon</c> is called through to, so scale, angle,
    /// offset, colour and every subclass override of it behave exactly as they did. That is what lets a gizmo from
    /// another mod keep its own artwork inside our frame instead of losing it.
    ///
    /// <b>The interaction is vanilla's, reproduced rather than reinvented.</b> The hotkey registry, the custom
    /// activator, the tutor system, the refusal message on a disabled command and the right click float menu all
    /// behave as before, in the same order, because a button that looks better and acts differently is a worse
    /// button. Only the drawing is ours.
    ///
    /// <b>75 by 75 is not our choice.</b> <c>Command.GizmoOnGUI</c> hardcodes the height and the grid spaces its
    /// rows to match, so the taller card in the mockup would mean patching the grid as well. The restyle fits
    /// inside the footprint the game gives.
    /// </summary>
    internal static class CommandPainter
    {
        /// <summary>Height of the strip the label sits in, along the bottom.</summary>
        private const float LabelStrip = 20f;

        private const float HotkeySize = 14f;

        /// <summary>
        /// Vanilla's own icon drawing, reached once and reused.
        ///
        /// Protected and virtual, so it is invoked rather than copied: copying it would freeze today's behaviour
        /// and silently ignore every subclass that overrides it.
        /// </summary>
        private static readonly MethodInfo DrawIconMethod =
            AccessTools.Method(typeof(Command), "DrawIcon",
                new[] { typeof(Rect), typeof(Material), typeof(GizmoRenderParms) });

        /// <summary>Whether the reskin is available at all. A missing DrawIcon means we cannot draw a gizmo.</summary>
        internal static bool Available => DrawIconMethod != null;

        internal static GizmoResult Draw(Command command, Rect rect, GizmoRenderParms parms)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            bool over = Mouse.IsOver(rect);
            bool disabled = command.Disabled;
            bool held = over && Input.GetMouseButton(0) && !disabled;

            MouseoverSounds.DoRegion(rect, SoundDefOf.Mouseover_Command);

            if (parms.highLight)
                Widgets.DrawStrongHighlight(rect.ExpandedBy(4f));

            bool on = Toggled(command);

            Face(rect, palette, over, held, disabled, on, parms);

            Icon(command, rect, parms);

            if (!parms.shrunk)
                Label(command, rect, palette, disabled);

            bool pressed = Hotkey(command, rect, palette, parms);

            if (GizmoGridDrawer.customActivator != null && GizmoGridDrawer.customActivator(command))
                pressed = true;

            if (Widgets.ButtonInvisible(rect))
                pressed = true;

            Tip(command, rect, disabled);

            if (!command.HighlightTag.NullOrEmpty()
                && (Find.WindowStack.FloatMenu == null || !Find.WindowStack.FloatMenu.windowRect.Overlaps(rect)))
            {
                UIHighlighter.HighlightOpportunity(rect, command.HighlightTag);
            }

            Text.Font = GameFont.Small;

            return Result(command, pressed, over, disabled);
        }

        /// <summary>
        /// The button face: fill, border, and whatever the state has to say.
        ///
        /// A toggle that is on takes the accent border and an accent bar along the top, because vanilla draws an
        /// active toggle much like a hovered one and the Play settings row is nothing but toggles.
        /// </summary>
        private static void Face(Rect rect, UIColorPaletteDef palette, bool over, bool held, bool disabled, bool on,
            GizmoRenderParms parms)
        {
            Color fill = palette.PanelBackground;
            Color edge = palette.Border;

            if (disabled)
            {
                fill = palette.SurfaceSunken;
                edge = palette.Border;
            }
            else if (held)
            {
                fill = palette.AccentMuted;
                edge = palette.Accent;
            }
            else if (over)
            {
                fill = palette.SurfaceRaised;
                edge = palette.BorderFocused;
            }
            else if (on)
            {
                edge = palette.Accent;
            }

            if (parms.lowLight)
                fill = new Color(fill.r, fill.g, fill.b, fill.a * 0.55f);

            UIElementPainter.OutlineRounded(rect, edge, fill);

            if (on && !disabled)
                UIElementPainter.FillRounded(new Rect(rect.x, rect.y, rect.width, 2f), palette.Accent);
        }

        /// <summary>
        /// Hands the rect back to the game to put the icon in.
        ///
        /// Kept clear of the label strip, which is the whole reason for the strip: vanilla lays its label over the
        /// picture, so a two word command hides the thing that identifies it.
        /// </summary>
        private static void Icon(Command command, Rect rect, GizmoRenderParms parms)
        {
            Rect art = parms.shrunk
                ? rect
                : new Rect(rect.x, rect.y, rect.width, rect.height - LabelStrip);

            Color color = GUI.color;

            try
            {
                GUI.color = Color.white;

                DrawIconMethod.Invoke(command, new object[] { art, null, parms });
            }
            finally
            {
                GUI.color = color;
            }
        }

        private static void Label(Command command, Rect rect, UIColorPaletteDef palette, bool disabled)
        {
            string text = command.LabelCap;

            if (text.NullOrEmpty())
                return;

            Rect strip = new Rect(rect.x + 1f, rect.yMax - LabelStrip - 1f, rect.width - 2f, LabelStrip);

            UIElementPainter.FillRounded(strip, palette.SurfaceSunken);

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = disabled ? palette.TextDisabled : palette.TextPrimary;

            Widgets.Label(strip, text);

            GUI.color = color;
            Text.Anchor = anchor;
            Text.Font = font;
        }

        /// <summary>
        /// The hotkey chip, and the key press it stands for.
        ///
        /// <b>Registered with the game's own list of drawn keys.</b> Two commands sharing a key must only show and
        /// answer to it once, and that list is how the game decides which one wins. Skipping it would give a key to
        /// every command that wanted it.
        /// </summary>
        private static bool Hotkey(Command command, Rect rect, UIColorPaletteDef palette, GizmoRenderParms parms)
        {
            if (SteamDeck.IsSteamDeckInNonKeyboardMode)
            {
                if (!parms.isFirst)
                    return false;

                GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 3f, 21f, 21f), TexUI.SteamDeck_ButtonA);

                if (!KeyBindingDefOf.Accept.KeyDownEvent)
                    return false;

                Event.current.Use();

                return true;
            }

            KeyCode key = command.hotKey != null ? command.hotKey.MainKey : KeyCode.None;

            if (key == KeyCode.None || GizmoGridDrawer.drawnHotKeys.Contains(key))
                return false;

            string label = key.ToStringReadable();

            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;

            Text.Font = GameFont.Tiny;

            float width = Mathf.Max(HotkeySize, Text.CalcSize(label).x + 6f);
            Rect chip = new Rect(rect.x + 3f, rect.y + 3f, width, HotkeySize);

            UIElementPainter.OutlineRounded(chip, palette.Border, palette.SurfaceSunken);

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = palette.TextSecondary;

            Widgets.Label(chip, label);

            GUI.color = color;
            Text.Anchor = anchor;
            Text.Font = font;

            GizmoGridDrawer.drawnHotKeys.Add(key);

            if (!command.hotKey.KeyDownEvent)
                return false;

            Event.current.Use();

            return true;
        }

        /// <summary>Vanilla's tooltip, including the reason a disabled command gives.</summary>
        private static void Tip(Command command, Rect rect, bool disabled)
        {
            if (!Mouse.IsOver(rect) || !Tooltips(command))
                return;

            TipSignal tip = command.Desc;

            if (disabled && !command.disabledReason.NullOrEmpty())
            {
                tip.text += ("\n\n" + "DisabledCommand".Translate() + ": " + command.disabledReason)
                    .Colorize(ColorLibrary.RedReadable);
            }

            tip.text += command.DescPostfix;

            TooltipHandler.TipRegion(rect, tip);
        }

        /// <summary><c>DoTooltip</c> is protected, so it is read rather than assumed to be true.</summary>
        private static bool Tooltips(Command command)
        {
            PropertyInfo property = AccessTools.Property(command.GetType(), "DoTooltip");

            return property == null || (bool) property.GetValue(command, null);
        }

        /// <summary>
        /// Vanilla's outcome, in vanilla's order.
        ///
        /// A disabled command still complains when pressed, a right click still opens the float menu, and the
        /// tutor system still gets its say and can still refuse.
        /// </summary>
        private static GizmoResult Result(Command command, bool pressed, bool over, bool disabled)
        {
            if (pressed)
            {
                if (disabled)
                {
                    if (!command.disabledReason.NullOrEmpty())
                    {
                        Messages.Message("DisabledCommand".Translate() + ": " + command.disabledReason,
                            MessageTypeDefOf.RejectInput, false);
                    }

                    return new GizmoResult(GizmoState.Mouseover, null);
                }

                if (Event.current.button == 1)
                    return new GizmoResult(GizmoState.OpenedFloatMenu, Event.current);

                if (!TutorSystem.AllowAction(command.TutorTagSelect))
                    return new GizmoResult(GizmoState.Mouseover, null);

                GizmoResult result = new GizmoResult(GizmoState.Interacted, Event.current);

                TutorSystem.Notify_Event(command.TutorTagSelect);

                return result;
            }

            return over ? new GizmoResult(GizmoState.Mouseover, null) : new GizmoResult(GizmoState.Clear, null);
        }

        /// <summary>Whether this command is a toggle that is currently on.</summary>
        private static bool Toggled(Command command)
        {
            return command is Command_Toggle toggle && toggle.isActive != null && toggle.isActive();
        }
    }
}
