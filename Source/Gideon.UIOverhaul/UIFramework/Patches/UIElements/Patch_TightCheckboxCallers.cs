using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Patches.UIElements
{
    /// <summary>Which way a checkbox row may grow, because that is the side with nothing on it.</summary>
    internal enum CheckboxGrowth
    {
        Left,
        Right
    }

    /// <summary>
    /// Widens the rect at vanilla call sites whose checkbox row was sized around vanilla's box.
    ///
    /// <b>The problem this exists for.</b> Vanilla draws a 24 pixel box after its label, so a label gets the row
    /// less 24. A switch is 40 and sits before the label with a 10 pixel gap, so the label gets the row less 50 --
    /// twenty-six pixels less than the caller allowed for. Rows with room to spare never notice. Rows sized
    /// tightly around vanilla's box truncate, and nothing <see cref="Patch_Widgets_CheckboxLabeled"/> can do at
    /// the draw site invents the missing space: the rect belongs to the caller's layout, and growing it blindly
    /// would put the label on top of whatever is beside it.
    ///
    /// <b>So the rect is fixed where the caller can be seen.</b> Every site below was found by decompiling the
    /// game and reading all fifty-two <c>Widgets.CheckboxLabeled</c> call sites, then checking each one's
    /// neighbours to see which side is empty. Thirty-seven of those go through <c>Listing_Standard</c>, which
    /// hands over a full column and is never at risk; most of the rest have room already. What is left is here.
    ///
    /// <b>Direction is per call site, not per method,</b> which several of these require. The log tab lays three
    /// checkboxes across one row: the first two have space to their right, the third is boxed in on the right by
    /// the second and has to grow left instead. A single direction for the method would have pushed one of them
    /// through its neighbour.
    ///
    /// <b>Listing a site that turns out to fit costs nothing.</b> The shim measures with <c>Text.CalcSize</c> and
    /// returns the rect untouched when the label already fits, so these entries are permission to grow rather than
    /// an instruction to. That matters because label widths depend on the font, the UI scale and the translation,
    /// none of which can be predicted from a decompile.
    ///
    /// <b>This is deliberately a list and not a rule.</b> Whether a row has room beside it is a fact about a
    /// layout, not something derivable from a rect. Modded checkboxes get no entry and are not meant to: a mod
    /// that sizes a row tightly around vanilla's box is assuming a control this mod replaced, and the place to fix
    /// that is the mod.
    ///
    /// <b>The call is swapped rather than the arithmetic rewritten.</b> A transpiler that tried to find and edit
    /// each rect construction would have to understand every caller's math and would break whenever Ludeon
    /// touched it. Replacing the <c>call</c> operand with <see cref="Widened"/> leaves every instruction that
    /// computes the rect exactly as it was, and the shim widens the finished value on its way past. If a target
    /// method is renamed the patch simply does not apply, which costs a shortened label rather than a broken tab.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_TightCheckboxCallers
    {
        /// <summary>
        /// Breathing room past the measured text, so a widened row lands clear of the fits-or-ellipsis boundary
        /// rather than exactly on it.
        /// </summary>
        private const float Margin = 4f;

        /// <summary>
        /// One vanilla method whose checkbox rects may grow, and which way each of them may go.
        ///
        /// <see cref="Directions"/> is indexed by the order the calls appear in the method. A method with more
        /// calls than entries leaves the rest alone, which is the safe direction to be wrong in.
        /// </summary>
        private sealed class Site
        {
            internal Type Type;
            internal string Method;
            internal CheckboxGrowth[] Directions;
        }

        private static readonly Site[] Sites =
        {
            // Quests tab. "DEV: Show all" in a 120 wide rect at rect.width - 135, and "DEV: Show debug info" in a
            // 110 wide rect at innerRect.xMax - 110. Both hang off the right edge of a header with nothing to
            // their left.
            new Site
            {
                Type = typeof(MainTabWindow_Quests), Method = "DoQuestsList",
                Directions = new[] { CheckboxGrowth.Left }
            },
            new Site
            {
                Type = typeof(MainTabWindow_Quests), Method = "DoDebugInfoToggle",
                Directions = new[] { CheckboxGrowth.Left }
            },

            // Factions tab. "DEV: Show all" in a 120 wide rect at rect.width - 120, drawn at y 0 while the
            // faction list starts at y 50, so the whole row to its left is empty.
            new Site
            {
                Type = typeof(FactionUIUtility), Method = "DoWindowContents",
                Directions = new[] { CheckboxGrowth.Left }
            },

            // Styling station. "DEV: Show all" in a 120 wide rect against the dialog's right edge; there is no
            // room to the right of it at all, so left is the only way it can go.
            new Site
            {
                Type = typeof(Dialog_StylingStation), Method = "DoWindowContents",
                Directions = new[] { CheckboxGrowth.Left }
            },

            // Social card. "DEV: AllRelations" in a 145 wide rect at x 0 of a group that is the full card width
            // and holds nothing else, so it has the entire card to grow into on the right.
            new Site
            {
                Type = typeof(SocialCardUtility), Method = "DrawDebugOptions",
                Directions = new[] { CheckboxGrowth.Right }
            },

            // Log tab, three across one 630 wide row, in call order:
            //   "Show all"    at x 60,  100 wide -> next neighbour starts at 330, so 170 spare on the right
            //   "Show combat" at x 445, 115 wide -> ends at 560 with the tab edge at 630, so 70 spare right
            //   "Show social" at x 330, 105 wide -> ends at 435 and the combat box starts at 445, so it grows left
            new Site
            {
                Type = typeof(ITab_Pawn_Log), Method = "FillTab",
                Directions = new[] { CheckboxGrowth.Right, CheckboxGrowth.Right, CheckboxGrowth.Left }
            }
        };

        private static MethodInfo Original => AccessTools.Method(typeof(Widgets),
            nameof(Widgets.CheckboxLabeled),
            new[]
            {
                typeof(Rect), typeof(string), typeof(bool).MakeByRefType(), typeof(bool), typeof(Texture2D),
                typeof(Texture2D), typeof(bool), typeof(bool)
            });

        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Site site in Sites)
            {
                MethodBase found = AccessTools.Method(site.Type, site.Method);

                if (found != null)
                {
                    yield return found;
                    continue;
                }

                // Reported rather than thrown. A missing target means RimWorld moved or renamed a method, which
                // is worth noticing and is not a reason to fail the patch pass for every other site.
                UIGuard.Report("Framework.TightCheckboxSite",
                    new MissingMethodException(site.Type.FullName, site.Method),
                    "One vanilla checkbox may show a shortened label.");
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = Original;
            MethodInfo replacement = AccessTools.Method(typeof(Patch_TightCheckboxCallers), nameof(Widened));

            CheckboxGrowth[] directions = DirectionsFor(__originalMethod);
            int seen = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (original == null || replacement == null || !instruction.Calls(original))
                {
                    yield return instruction;
                    continue;
                }

                int index = seen++;

                if (directions == null || index >= directions.Length)
                {
                    // More calls in the method than the site accounted for. Left as vanilla rather than guessed
                    // at, because a guessed direction is what puts a label through its neighbour.
                    yield return instruction;
                    continue;
                }

                // Pushed after the eight arguments the call site already put on the stack and immediately before
                // the call, which is exactly where a ninth parameter belongs.
                //
                // Labels and exception blocks move to this instruction because it now stands where the call
                // stood; leaving them on the call would let a branch jump past the argument that was just pushed.
                yield return new CodeInstruction(OpCodes.Ldc_I4, (int) directions[index])
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);

                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
        }

        private static CheckboxGrowth[] DirectionsFor(MethodBase method)
        {
            if (method == null)
                return null;

            foreach (Site site in Sites)
                if (site.Type == method.DeclaringType && site.Method == method.Name)
                    return site.Directions;

            return null;
        }

        /// <summary>
        /// Stands in for <c>Widgets.CheckboxLabeled</c> at the listed call sites, widening the rect first.
        ///
        /// The first eight parameters match the original exactly, including the ones the C# compiler fills in
        /// from defaults, because the call site pushes all eight and the stack has to balance. The ninth is added
        /// by the transpiler.
        /// </summary>
        public static void Widened(Rect rect, string label, ref bool checkOn, bool disabled,
            Texture2D texChecked, Texture2D texUnchecked, bool placeCheckboxNearText, bool paintable, int growth)
        {
            // placeCheckboxNearText already shrinks itself to its own text, so widening it first would be undone
            // a moment later. Only fixed rects need this.
            if (!placeCheckboxNearText)
                rect = Widen(rect, label, (CheckboxGrowth) growth);

            Widgets.CheckboxLabeled(rect, label, ref checkOn, disabled, texChecked, texUnchecked,
                placeCheckboxNearText, paintable);
        }

        /// <summary>
        /// Extends a rect on its empty side until the label fits beside the switch, leaving the other edge alone.
        ///
        /// A label too long even after growing is left to be shortened by the draw patch, which is the honest
        /// outcome: at that point there is genuinely nowhere left to put it.
        /// </summary>
        private static Rect Widen(Rect rect, string label, CheckboxGrowth growth)
        {
            if (label.NullOrEmpty())
                return rect;

            bool previousWrap = Text.WordWrap;

            try
            {
                // Measured unwrapped, which is how a single-line checkbox label is drawn. With wrapping on,
                // CalcSize answers about a paragraph rather than a line.
                Text.WordWrap = false;

                // Taken from the draw patch's own constants rather than restated, so a change to the switch's
                // slot or its gap moves this with it instead of leaving the two quietly disagreeing.
                float needed = Text.CalcSize(label).x + UICheckboxControl.BoxWidth
                                                      + Patch_Widgets_CheckboxLabeled.Gap + Margin;

                if (needed <= rect.width)
                    return rect;

                float grow = needed - rect.width;

                if (growth == CheckboxGrowth.Left)
                {
                    // Clamped at the container's left edge. Coordinates here are local to whatever group is
                    // current, so zero is that group's edge rather than the screen's.
                    rect.xMin = Mathf.Max(0f, rect.xMin - grow);
                    return rect;
                }

                rect.xMax += grow;

                return rect;
            }
            finally
            {
                Text.WordWrap = previousWrap;
            }
        }
    }
}
