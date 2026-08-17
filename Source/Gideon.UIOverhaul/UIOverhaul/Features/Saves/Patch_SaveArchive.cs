using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// Lets the game open a save whatever it was compressed with.
    ///
    /// <b>Three methods read a save from disk, and all three do it the same way:</b>
    /// <c>ScribeLoader.InitLoading</c>, <c>ScribeLoader.InitLoadingMetaHeaderOnly</c> and
    /// <c>ScribeMetaHeaderUtility.GameVersionOf</c> each begin with <c>new StreamReader(path)</c>. Replacing
    /// that one construction with <see cref="SaveArchive.OpenReader"/> is the whole patch, and it covers
    /// loading, the version check before loading, and the version shown in the load list.
    ///
    /// <b>A transpiler rather than a prefix, deliberately.</b> A prefix would have to reimplement the method
    /// it replaced, including the XML reading and the careful <c>ForceStop</c> error handling around it, and
    /// then keep that copy correct across RimWorld updates. Swapping a single instruction leaves every line
    /// of vanilla's logic exactly where it was. It is also the same seam AmCh's Save File Compression uses,
    /// so the two mods transform the same instruction rather than fighting over the method.
    ///
    /// <b>The substitution is type-exact.</b> <c>OpenReader</c> returns a <c>StreamReader</c>, which is what
    /// the following IL expects to find on the stack, so nothing downstream can tell the difference. A plain
    /// XML save gets a plain <c>StreamReader</c> over the file, exactly as before.
    ///
    /// <b>If the transform ever fails to find its target, that is reported rather than ignored.</b> A
    /// silently unpatched method means compressed saves stop opening, and the symptom would be an XML parse
    /// error a long way from the cause.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SaveArchive
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            yield return AccessTools.Method(typeof(ScribeLoader), nameof(ScribeLoader.InitLoading));
            yield return AccessTools.Method(typeof(ScribeLoader),
                nameof(ScribeLoader.InitLoadingMetaHeaderOnly));
            yield return AccessTools.Method(typeof(ScribeMetaHeaderUtility),
                nameof(ScribeMetaHeaderUtility.GameVersionOf));
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            ConstructorInfo target = AccessTools.Constructor(typeof(StreamReader), new[] { typeof(string) });
            MethodInfo replacement = AccessTools.Method(typeof(SaveArchive), nameof(SaveArchive.OpenReader));

            int replaced = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj && ReferenceEquals(instruction.operand, target))
                {
                    replaced++;

                    // Labels and exception blocks are carried across, or a branch into this instruction and
                    // the surrounding try would both be lost.
                    yield return new CodeInstruction(OpCodes.Call, replacement)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };

                    continue;
                }

                yield return instruction;
            }

            if (replaced == 0)
            {
                UIGuard.Report("Saves.ArchiveTranspiler",
                    new MissingMethodException("No StreamReader(string) construction found in "
                                               + (original == null ? "an unknown method" : original.Name)),
                    "Compressed saves cannot be opened. Saves written without compression are unaffected.");
            }
        }
    }
}
