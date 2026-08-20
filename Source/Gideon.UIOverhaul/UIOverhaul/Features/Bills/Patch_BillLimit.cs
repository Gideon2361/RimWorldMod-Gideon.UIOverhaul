using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// Raises RimWorld's limit of fifteen bills per workbench.
    ///
    /// <b>Why this is a transpiler and not a one line change.</b> <c>BillStack.MaxCount</c> is a
    /// <c>public const int</c>, and a const in C# is written into every place that reads it at compile time,
    /// the field in the assembly is documentation, and nothing at runtime consults it. So the number has to be
    /// replaced where it was inlined, which is the IL of the two methods that gate on it.
    ///
    /// <b>Two gates, and missing either one leaves the feature half done.</b>
    /// <list type="bullet">
    /// <item><c>BillStack.DoListing</c> hides the Add bill button once the stack reaches the limit.</item>
    /// <item><c>ITab_Bills.FillTab</c> greys out the paste button, with its own tooltip saying the limit is
    /// reached.</item>
    /// </list>
    /// Nothing else in the game enforces it: <c>AddBill</c> itself is uncapped, which is what makes raising the
    /// two interface gates sufficient rather than merely cosmetic.
    ///
    /// <b>The replacement is narrow on purpose.</b> A pass that changed every fifteen in those methods would be
    /// a coin toss, because there is no telling what else might be fifteen. This only rewrites a load of the constant
    /// that is immediately compared or branched on, which is what a bounds test looks like and what an unrelated
    /// number does not.
    ///
    /// <b>A silent failure is reported.</b> If RimWorld changes the limit or restructures the check, the
    /// transform finds nothing and the cap quietly stays at fifteen, so finding nothing is treated as the
    /// fault it is rather than shrugged off.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_BillLimit
    {
        /// <summary>
        /// The limit vanilla compiles in.
        ///
        /// Matched exactly, so if a future RimWorld raises its own cap this stops matching and reports rather
        /// than lowering a limit that had already gone up.
        /// </summary>
        private const int VanillaLimit = 15;

        /// <summary>
        /// What it becomes.
        ///
        /// Chosen to be past anybody's real use rather than as a considered maximum. It also stays inside a
        /// signed byte, so the instruction that loaded the old value can load this one unchanged.
        /// </summary>
        private const int RaisedLimit = 120;

        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            yield return AccessTools.Method(typeof(BillStack), nameof(BillStack.DoListing));
            yield return AccessTools.Method(typeof(ITab_Bills), "FillTab");
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int replaced = 0;

            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];

                if (!Loads(instruction, VanillaLimit) || !Tested(code, i))
                    continue;

                // Rewritten in place rather than swapped for a new instruction, so any label or exception block
                // attached to it stays attached. A branch target landing on this offset is common.
                instruction.opcode = OpCodes.Ldc_I4;
                instruction.operand = RaisedLimit;

                replaced++;
            }

            if (replaced == 0)
            {
                UIGuard.Report("Bills.RaiseLimit",
                    new MissingFieldException("No bill limit comparison found in "
                                              + (original == null ? "an unknown method" : original.Name)),
                    "Workbenches still allow only fifteen bills.");
            }

            return code;
        }

        /// <summary>Whether this instruction pushes <paramref name="value"/> as an integer constant.</summary>
        private static bool Loads(CodeInstruction instruction, int value)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_S || instruction.opcode == OpCodes.Ldc_I4)
                return instruction.operand != null && ToInt(instruction.operand) == value;

            return false;
        }

        private static int ToInt(object operand)
        {
            if (operand is int)
                return (int) operand;

            if (operand is sbyte)
                return (sbyte) operand;

            if (operand is byte)
                return (byte) operand;

            return int.MinValue;
        }

        /// <summary>
        /// Whether the next instruction compares or branches on what was just pushed.
        ///
        /// This is the whole safety of the transform. A bounds test always compares immediately; a fifteen that
        /// happens to be a width, a count of columns or a tick interval is stored, added or passed instead.
        /// </summary>
        private static bool Tested(List<CodeInstruction> code, int at)
        {
            if (at + 1 >= code.Count)
                return false;

            OpCode next = code[at + 1].opcode;

            return next == OpCodes.Blt || next == OpCodes.Blt_S || next == OpCodes.Blt_Un
                   || next == OpCodes.Blt_Un_S || next == OpCodes.Bge || next == OpCodes.Bge_S
                   || next == OpCodes.Bge_Un || next == OpCodes.Bge_Un_S || next == OpCodes.Bgt
                   || next == OpCodes.Bgt_S || next == OpCodes.Ble || next == OpCodes.Ble_S
                   || next == OpCodes.Beq || next == OpCodes.Beq_S || next == OpCodes.Bne_Un
                   || next == OpCodes.Bne_Un_S || next == OpCodes.Clt || next == OpCodes.Clt_Un
                   || next == OpCodes.Cgt || next == OpCodes.Cgt_Un || next == OpCodes.Ceq;
        }
    }
}
