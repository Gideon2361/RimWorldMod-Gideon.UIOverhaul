using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Pawns.Templates;
using Gideon.UIOverhaul.Features.Work;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Clear, copy, paste, save and apply, for any one part of a pawn's settings.
    ///
    /// <b>One row for four scopes, because the work grid's row had already proved the shape.</b> That row --
    /// clear the priorities, copy them, paste them, save them as a template, apply a saved one -- turned out to
    /// be the thing everybody wanted next to the schedule and the policies as well, and then next to the pawn
    /// themselves for all of it at once. Writing it four times would have been four sets of tooltips drifting
    /// apart, so it is written once and told which part of the pawn it is operating on.
    ///
    /// <b>The scope is <see cref="PawnTemplateScope"/>, which already existed and already meant this.</b> The
    /// template system was built to capture and apply priorities, a schedule and policies independently or
    /// together; all this adds is the buttons that reach the two nobody had put buttons on yet.
    ///
    /// <b>One clipboard per scope, and priorities share the work tab's.</b> Copying a schedule must not throw
    /// away a set of priorities you copied a moment ago, so they are held separately. Priorities are the
    /// exception and deliberately so: <see cref="WorkPanel"/> already owns that clipboard and the whole point of
    /// it is that you can copy on the work tab and paste on the pawns tab.
    /// </summary>
    internal static class PawnTools
    {
        internal const float ButtonSize = 22f;

        internal const float ButtonGap = 4f;

        /// <summary>
        /// The clipboards, one per scope, held for the session and never written to disk.
        ///
        /// A template is the deliberate, named, kept version of this; persisting the clipboard would blur the two.
        /// </summary>
        private static readonly Dictionary<PawnTemplateScope, PawnTemplate> Clipboards =
            new Dictionary<PawnTemplateScope, PawnTemplate>();

        /// <summary>How wide a row of <paramref name="buttons"/> comes out.</summary>
        internal static float WidthFor(int buttons)
        {
            return buttons <= 0 ? 0f : buttons * ButtonSize + (buttons - 1) * ButtonGap;
        }

        /// <summary>
        /// How wide the row for this scope is, which is four buttons plus a clear where clearing means anything.
        ///
        /// <b>Policies have no clear button and that is not an omission.</b> Emptying a set of work priorities or
        /// a timetable is a state the game has -- everything at zero, every hour Anything -- and emptying a set of
        /// policies is not: there is no such thing as no food policy. A button that silently meant "put the
        /// default ones back" would be a different verb wearing the same icon.
        /// </summary>
        internal static float WidthFor(PawnTemplateScope scope)
        {
            return WidthFor(CanClear(scope) ? 5 : 4);
        }

        private static bool CanClear(PawnTemplateScope scope)
        {
            return scope == PawnTemplateScope.Priorities || scope == PawnTemplateScope.Schedule
                                                         || scope == PawnTemplateScope.Everything;
        }

        /// <summary>
        /// Draws the row, right-aligned in <paramref name="rect"/>, and runs whatever was clicked.
        ///
        /// Right-aligned because every caller puts it at the end of a band whose contents grow leftwards from it.
        /// </summary>
        /// <summary>
        /// How much of a resting toolbar is drawn, when a caller asks for one.
        ///
        /// Zero is not an option and that is the point: the buttons stay visible, stay where they are and stay
        /// clickable, so a click aimed from memory still lands on one. See <see cref="Strength"/>.
        /// </summary>
        private static float strength = 1f;

        /// <summary>
        /// The toolbar's drawing strength for the current call, between 0 and 1.
        ///
        /// A caller with many rows on screen at once can rest the toolbars it is not pointing at, which is
        /// what stops five buttons a row from becoming fifty glyphs competing with the data. Below full
        /// strength the button chrome is dropped and the icon is faded, so what is left is a hint of the tool
        /// rather than a shrunken copy of it.
        /// </summary>
        private static bool Resting
        {
            get { return strength < 1f; }
        }

        internal static void Row(Rect rect, Pawn pawn, PawnTemplateScope scope, UIColorPaletteDef palette)
        {
            Row(rect, pawn, scope, palette, 1f);
        }

        /// <inheritdoc cref="Row(Rect, Pawn, PawnTemplateScope, UIColorPaletteDef)"/>
        /// <param name="drawStrength">See <see cref="Resting"/>. One draws the toolbar as it always was.</param>
        internal static void Row(Rect rect, Pawn pawn, PawnTemplateScope scope, UIColorPaletteDef palette,
            float drawStrength)
        {
            if (pawn == null || rect.width <= 0f)
                return;

            float previousStrength = strength;

            strength = Mathf.Clamp01(drawStrength);

            try
            {
            UIGuard.Try("Pawns.Tools", () =>
            {
                float step = ButtonSize + ButtonGap;
                float x = rect.xMax - WidthFor(scope);
                float y = rect.y + (rect.height - ButtonSize) * 0.5f;

                if (CanClear(scope))
                {
                    if (Button(new Rect(x, y, ButtonSize, ButtonSize), WorkToolIcons.Clear, "0", palette,
                            ClearTooltip(pawn, scope)))
                        Clear(pawn, scope);

                    x += step;
                }

                if (Button(new Rect(x, y, ButtonSize, ButtonSize), WorkToolIcons.Copy, "C", palette,
                        "Copy " + pawn.LabelShortCap + "'s " + Noun(scope) + "."))
                    Copy(pawn, scope);

                x += step;

                // A disabled button rather than a hidden one: a tool that only appears once you have used another
                // tool is a tool nobody finds.
                if (Button(new Rect(x, y, ButtonSize, ButtonSize), WorkToolIcons.Paste, "P", palette,
                        PasteTooltip(pawn, scope), !HasClipboard(scope)))
                    Paste(pawn, scope);

                x += step;

                if (Button(new Rect(x, y, ButtonSize, ButtonSize), WorkToolIcons.Save, "S", palette,
                        "Save " + pawn.LabelShortCap + "'s " + Noun(scope) + " as a template."))
                {
                    PawnTemplate saved = PawnTemplateStore.CaptureFrom(pawn, scope);

                    Find.WindowStack.Add(new Dialog_PawnTemplates(null, scope, saved));
                }

                x += step;

                if (Button(new Rect(x, y, ButtonSize, ButtonSize), WorkToolIcons.Apply, "A", palette,
                        "Apply a saved " + Noun(scope) + " template to " + pawn.LabelShortCap + "."))
                    Find.WindowStack.Add(new Dialog_PawnTemplates(pawn, scope));
            }, "The copy and template buttons are missing from one of the pawn's bands.");
            }
            finally
            {
                strength = previousStrength;
            }
        }

        /// <summary>What this scope is called in a sentence.</summary>
        private static string Noun(PawnTemplateScope scope)
        {
            switch (scope)
            {
                case PawnTemplateScope.Priorities:
                    return "work priorities";

                case PawnTemplateScope.Schedule:
                    return "schedule";

                case PawnTemplateScope.Policies:
                    return "policies";

                default:
                    return "settings";
            }
        }

        internal static bool HasClipboard(PawnTemplateScope scope)
        {
            if (scope == PawnTemplateScope.Priorities)
                return WorkPanel.HasClipboard;

            PawnTemplate held;

            return Clipboards.TryGetValue(scope, out held) && held != null;
        }

        private static string PasteTooltip(Pawn pawn, PawnTemplateScope scope)
        {
            if (scope == PawnTemplateScope.Priorities)
                return WorkPanel.PasteTooltip(pawn);

            PawnTemplate held;

            if (!Clipboards.TryGetValue(scope, out held) || held == null)
                return "Nothing copied yet. Use the copy button on a colonist to pick up their " + Noun(scope)
                       + ".";

            return "Paste " + held.name + "'s " + Noun(scope) + " onto " + pawn.LabelShortCap + ".";
        }

        private static void Copy(Pawn pawn, PawnTemplateScope scope)
        {
            if (scope == PawnTemplateScope.Priorities)
            {
                WorkPanel.CopyPriorities(pawn);

                return;
            }

            Clipboards[scope] = PawnTemplate.From(pawn, pawn.LabelShortCap, scope);

            SoundDefOf.Tick_High.PlayOneShotOnCamera();

            Messages.Message("Copied " + pawn.LabelShortCap + "'s " + Noun(scope) + ".",
                MessageTypeDefOf.SilentInput, false);
        }

        private static void Paste(Pawn pawn, PawnTemplateScope scope)
        {
            if (scope == PawnTemplateScope.Priorities)
            {
                WorkPanel.PastePriorities(pawn);

                return;
            }

            PawnTemplate held;

            if (!Clipboards.TryGetValue(scope, out held) || held == null)
                return;

            PawnTemplateApplyResult result = held.ApplyTo(pawn, scope);

            // Everything includes the priorities, and pasting those invalidates the snapshot the work tab keeps
            // for putting manual priorities back. Left stale, it would undo the paste the next time manual
            // priorities were switched off and on again.
            if ((scope & PawnTemplateScope.Priorities) == PawnTemplateScope.Priorities)
                WorkPanel.ForgetRemembered(pawn);

            PawnAttributes.Invalidate(pawn);

            SoundDefOf.Tick_High.PlayOneShotOnCamera();

            string message = "Pasted " + held.name + "'s " + Noun(scope) + " onto " + pawn.LabelShortCap + ".";
            string trouble = result.Describe(pawn.LabelShortCap);

            if (!trouble.NullOrEmpty())
                message += " " + trouble;

            Messages.Message(message, MessageTypeDefOf.SilentInput, false);
        }

        private static string ClearTooltip(Pawn pawn, PawnTemplateScope scope)
        {
            switch (scope)
            {
                case PawnTemplateScope.Schedule:
                    return "Set every hour of " + pawn.LabelShortCap + "'s day to Anything.";

                case PawnTemplateScope.Everything:
                    return "Clear " + pawn.LabelShortCap + "'s work priorities and set every hour to Anything."
                           + "\n\nPolicies are left alone: there is no such thing as no food policy.";

                default:
                    return "Clear every work priority for " + pawn.LabelShortCap + ".";
            }
        }

        /// <summary>
        /// Clearing, which is the one thing on the row that destroys work the player did.
        ///
        /// Confirmed for that reason, and the confirmation lives with each half: the priorities half is
        /// <see cref="WorkPanel.ConfirmClearPriorities"/>, which already asks and already skips the question when
        /// there is nothing to lose.
        /// </summary>
        private static void Clear(Pawn pawn, PawnTemplateScope scope)
        {
            if ((scope & PawnTemplateScope.Priorities) == PawnTemplateScope.Priorities)
                WorkPanel.ConfirmClearPriorities(pawn);

            if ((scope & PawnTemplateScope.Schedule) != PawnTemplateScope.Schedule || pawn.timetable == null)
                return;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Set every hour of " + pawn.LabelShortCap + "'s day to Anything?",
                () => UIGuard.Try("Pawns.ClearSchedule", () =>
                {
                    for (int hour = 0; hour < GenDate.HoursPerDay; hour++)
                        pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Anything);

                    PawnAttributes.Invalidate(pawn);
                }, "That schedule was not cleared."), true));
        }

        /// <summary>
        /// A themed icon button, with a glyph where the art is missing and an optional disabled state.
        ///
        /// <b>Moved here from <see cref="PawnWorkGrid"/> once there were five callers.</b> Its own comment said a
        /// control earns its place once a third one wants it, and the schedule row, the policy row and the pawn
        /// row took it past that. Still not in the framework: it knows what our tool row looks like rather than
        /// what an icon button is in general.
        /// </summary>
        internal static bool Button(Rect r, Texture2D icon, string fallbackGlyph, UIColorPaletteDef palette,
            string tooltip, bool disabled = false)
        {
            TooltipHandler.TipRegion(r, (TipSignal) tooltip);

            bool over = !disabled && Mouse.IsOver(r);

            // A resting toolbar keeps its icons and drops its chrome. Painting the boxes too would put the
            // faded copy of a button beside the real ones, which reads as broken rather than as quiet.
            if (!Resting)
                UIElementPainter.PaintButton(r, palette, over, over && Input.GetMouseButton(0));

            Color previous = GUI.color;

            GUI.color = Rest(disabled ? palette.TextDisabled
                : over ? palette.TextPrimary : palette.TextSecondary);

            if (icon != null)
            {
                GUI.DrawTexture(r.ContractedBy(3f), icon, ScaleMode.ScaleToFit);
            }
            else
            {
                TextAnchor previousAnchor = Text.Anchor;

                Text.Anchor = TextAnchor.MiddleCenter;

                Widgets.Label(r, fallbackGlyph);

                Text.Anchor = previousAnchor;
            }

            GUI.color = previous;

            return !disabled && Widgets.ButtonInvisible(r);
        }

        /// <summary>The colour a resting toolbar draws in: the same colour, with the alpha taken down.</summary>
        private static Color Rest(Color color)
        {
            return Resting ? new Color(color.r, color.g, color.b, color.a * strength) : color;
        }
    }
}
