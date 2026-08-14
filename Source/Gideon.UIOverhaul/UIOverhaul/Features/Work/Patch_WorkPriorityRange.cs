using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// Widens the range a work priority may hold from vanilla's 1-4 to 1-9.
    ///
    /// Why this needs a patch at all: <c>Pawn_WorkSettings.LowestPriority</c> is a public <c>const</c>, so
    /// every consumer in the game compiled its own copy of the literal 4. There is no field to reassign --
    /// each place that cares has to be reached separately.
    ///
    /// What does not need a patch, which is what makes this tractable: the values live in a
    /// <c>DefMap&lt;WorkTypeDef, int&gt;</c> holding plain ints, and <c>ExposeData</c> writes that map
    /// directly rather than routing through <see cref="Pawn_WorkSettings.SetPriority"/>. Priorities of 5
    /// through 9 therefore save and load with no involvement from us.
    ///
    /// Priority 0 also needs nothing: vanilla already treats 0 as "not assigned", which is exactly the
    /// disabled state the work tab draws faded.
    /// </summary>
    public static class WorkPriorityRange
    {
        /// <summary>Highest priority this mod allows. Vanilla's is 4.</summary>
        public const int Lowest = 9;

        /// <summary>Vanilla's limit, kept as its own name so the patches read against something meaningful.</summary>
        public const int VanillaLowest = 4;
    }

    /// <summary>
    /// Replaces the upper bound in SetPriority's range check.
    ///
    /// A transpiler rather than a prefix on purpose. After validating, the method stores into its private
    /// <c>priorities</c> map and then does its own bookkeeping -- marking work givers dirty so the pawn
    /// re-sorts. A prefix that reimplemented the store would have to reproduce that bookkeeping from the
    /// outside and would silently rot the moment vanilla added a step to it. Swapping one operand leaves
    /// every other thing the method does untouched.
    ///
    /// The edit is narrow enough to assert: SetPriority is 159 bytes and contains exactly one
    /// <c>ldc.i4.4</c>, the upper bound of the <c>priority &lt; 0 || priority &gt; 4</c> guard. If a future
    /// version of the game changes that, the count check below fails loudly rather than patching the wrong
    /// instruction.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
    public static class Patch_Pawn_WorkSettings_SetPriority
    {
        /// <summary>
        /// <b>Nothing is rewritten unless there is exactly one candidate.</b> The method is found and counted
        /// first, and the edit is only applied if the count is one.
        ///
        /// This matters more here than the usual "fail loudly" would suggest. A transpiler that guesses wrong does
        /// not produce a missing feature, it produces a method body that does something else -- and a wrong
        /// constant in <c>SetPriority</c> would be a silent corruption of pawn work assignment. Two matches means
        /// the method is no longer the one this patch was written against, and the only honest response is to
        /// leave it exactly as vanilla wrote it and say so.
        ///
        /// The whole thing is also caught, because a throwing transpiler fails the patch at Harmony's level:
        /// returning the original instructions keeps the method valid, and keeps any other mod's patches on it
        /// from being taken down alongside ours.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            try
            {
                int target = -1;
                int found = 0;

                for (int i = 0; i < code.Count; i++)
                {
                    if (!IsLoadOfVanillaLowest(code[i]))
                        continue;

                    if (found == 0)
                        target = i;

                    found++;
                }

                if (found != 1)
                {
                    Log.Error(UILogTag.Prefix + $"Expected exactly one priority bound in "
                              + $"Pawn_WorkSettings.SetPriority but found {found}, so the method has been left "
                              + $"as vanilla wrote it. Work priorities above {WorkPriorityRange.VanillaLowest} "
                              + "will be rejected; the rest of the work tab still works.");

                    return code;
                }

                code[target] = new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte) WorkPriorityRange.Lowest)
                {
                    labels = code[target].labels,
                    blocks = code[target].blocks
                };

                return code;
            }
            catch (Exception ex)
            {
                UIGuard.Report("Work.PriorityRangeTranspiler", ex);
                return code;
            }
        }

        /// <summary>
        /// Whether an instruction loads the constant 4.
        ///
        /// Both encodings are checked because which one the compiler emits is its choice, not something to
        /// rely on: small integers usually get the compact <c>ldc.i4.4</c>, but a recompile with different
        /// settings could emit <c>ldc.i4.s 4</c> or <c>ldc.i4 4</c> instead.
        /// </summary>
        private static bool IsLoadOfVanillaLowest(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_4)
                return true;

            if (instruction.opcode == OpCodes.Ldc_I4_S || instruction.opcode == OpCodes.Ldc_I4)
                return instruction.operand is int value && value == WorkPriorityRange.VanillaLowest;

            return false;
        }
    }
}
