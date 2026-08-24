using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// Lets the storage tab lay out at the size the inspect pane gave it.
    ///
    /// <b>Stretching the tab's <c>size</c> field is not enough for this one.</b> Most tabs lay out from that
    /// field, so <see cref="InspectForeignTab"/> writing it for the length of a draw is all they need.
    /// <c>ITab_Storage</c> does not read it: <c>FillTab</c> builds its rect from a private
    /// <c>static readonly Vector2 WinSize</c> of 300 by 480, opens a group at that size, and derives the filter's
    /// rect from the group. Everything past that is clipped, so no amount of room offered outside the group can
    /// reach it. Reported on 2026-08-23 as a filter in a narrow column inside a much wider pane.
    ///
    /// <b>The field the IL reads is swapped, not the arithmetic.</b> Reading a static struct field compiles to
    /// <c>ldsflda</c> followed by <c>ldfld x</c>, twice, and every rect on the tab is derived from those two
    /// reads. Pointing that one operand at a field of ours leaves every instruction that computes a rect exactly
    /// as Ludeon wrote it -- including the ten pixel contraction and the thirty-five pixel top area -- and
    /// changes only where the two numbers come from. The alternative, replacing <c>FillTab</c>, would have meant
    /// owning the priority menu and the bill-invalidation warning it raises, for a layout change.
    ///
    /// <b>Our field holds vanilla's own value except while the pane is hosting the tab.</b> So with the inspect
    /// pane rebuild switched off, or with the tab in a window of its own, the numbers are the ones the game
    /// shipped and nothing about the tab is different.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Storage), "FillTab")]
    public static class Patch_StorageTabSize
    {
        /// <summary>
        /// What the tab lays out from. Public because Harmony's transpiled IL loads its address directly.
        ///
        /// Seeded with vanilla's own figure so that a failure to read the real one -- a rename, a mod that got
        /// there first -- leaves the tab exactly as it is today rather than at zero.
        /// </summary>
        public static Vector2 Size = new Vector2(300f, 480f);

        private static Vector2 vanilla = new Vector2(300f, 480f);

        private static bool read;

        /// <summary>
        /// Points the tab at the pane's size for this draw, or back at the game's own when nothing is hosting it.
        ///
        /// A prefix rather than a pair of prefix and postfix: every call sets the value, so there is no state
        /// left behind to unwind and no ordering to get wrong if the tab throws.
        /// </summary>
        public static void Prefix()
        {
            UIGuard.Try("Inspector.StorageTabSize", () =>
            {
                Vector2 hosting = InspectForeignTab.Hosting;

                Size = hosting.x > 1f && hosting.y > 1f ? hosting : Vanilla();
            }, null);
        }

        /// <summary>
        /// Vanilla's <c>WinSize</c>, read once from the field the transpiler is about to stop the tab from using.
        ///
        /// Read lazily rather than in a static constructor: this class is touched by the patch pass, which runs
        /// before defs and before anything else, and there is no reason to reflect that early.
        /// </summary>
        private static Vector2 Vanilla()
        {
            if (read)
                return vanilla;

            read = true;

            FieldInfo field = Field();

            if (field != null)
                vanilla = (Vector2) field.GetValue(null);

            return vanilla;
        }

        private static FieldInfo Field()
        {
            return AccessTools.Field(typeof(ITab_Storage), "WinSize");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo winSize = Field();
            FieldInfo ours = AccessTools.Field(typeof(Patch_StorageTabSize), nameof(Size));

            if (winSize == null || ours == null)
            {
                // Reported rather than thrown, and the tab keeps its 300 by 480 layout inside the pane. That is
                // today's behaviour, which is worse than the fix and much better than a tab that will not draw.
                UIGuard.Report("Inspector.StorageTabSizeField",
                    new MissingFieldException(typeof(ITab_Storage).FullName, "WinSize"),
                    "The storage tab does not fill the inspect pane.");

                foreach (CodeInstruction instruction in instructions)
                    yield return instruction;

                yield break;
            }

            foreach (CodeInstruction instruction in instructions)
            {
                FieldInfo loaded = instruction.operand as FieldInfo;

                // Matched by name and owner rather than by reference: the FieldInfo Harmony hands over comes
                // from reading the method body and is not guaranteed to be the same instance AccessTools
                // returns, so a reference comparison can be quietly false for the right field.
                bool loadsWinSize = (instruction.opcode == OpCodes.Ldsflda || instruction.opcode == OpCodes.Ldsfld)
                                    && loaded != null && loaded.DeclaringType == winSize.DeclaringType
                                    && loaded.Name == winSize.Name;

                if (loadsWinSize)
                    instruction.operand = ours;

                yield return instruction;
            }
        }
    }
}
