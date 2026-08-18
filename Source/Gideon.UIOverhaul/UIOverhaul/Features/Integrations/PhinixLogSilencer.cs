using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Drops Phinix's routine information logging, leaving its warnings and errors alone.
    ///
    /// <b>Phinix narrates itself.</b> Every login, logout, display name change, created trade and received chat
    /// message goes to <c>Log.Message</c> as it happens. On a populated server that is a steady stream, and it
    /// pushes everything else out of a log somebody is reading for another reason.
    ///
    /// <b>Phinix's own calls are rewritten, rather than Verse.Log being filtered.</b> The obvious approach is a
    /// prefix on <c>Log.Message</c> that works out who called, and it is worse in the way that matters: the only
    /// thing available to judge by is the stack, Phinix writes plain sentences with no tag of its own, and a
    /// stack can legitimately have Phinix beneath another mod's logging call. That version would eventually eat
    /// somebody else's line. Replacing the call instruction inside Phinix's own methods cannot: the only calls
    /// redirected are the ones written in their assembly.
    ///
    /// <b>It also costs nothing while running.</b> A stack walk per <c>Log.Message</c> would be paid by every mod
    /// in the game for the life of the session. This is a one-off at startup.
    ///
    /// <b>Their warnings and errors are untouched,</b> deliberately. This hides narration, not problems. The
    /// switch is about a noisy log, and a suppressed error is a bug report nobody can answer.
    /// </summary>
    internal static class PhinixLogSilencer
    {
        /// <summary>
        /// Stands in for <c>Log.Message</c> inside Phinix's methods.
        ///
        /// <b>The setting is read here rather than at patch time,</b> so the switch takes effect on the next
        /// message instead of on the next launch. The patch is permanent; whether it swallows anything is not.
        ///
        /// Signature-identical to what it replaces, which is what makes the substitution safe: the IL that
        /// pushed the string is left exactly as it was, and nothing after the call has to be rebalanced.
        /// </summary>
        public static void Swallow(string text)
        {
            bool suppress;

            try
            {
                suppress = UIOverhaulSettingsFile.Current?.suppressPhinixInfoLog ?? true;
            }
            catch (Exception)
            {
                // Unreadable settings must not lose a log line. Anything other than a clear yes shows it.
                suppress = false;
            }

            if (!suppress)
                Log.Message(text);
        }
    }

    /// <summary>
    /// Redirects every <c>Log.Message</c> call written inside Phinix's client to
    /// <see cref="PhinixLogSilencer.Swallow"/>.
    ///
    /// <b>The methods are found by what they do, not by what they are called.</b> Most of these calls sit in
    /// lambdas registered in Phinix's constructor, which the compiler names things like
    /// <c>&lt;.ctor&gt;b__12_3</c>. Matching those by name would break the first time their author added a line
    /// above them. Reading each method's IL and patching the ones that actually call <c>Log.Message</c> survives
    /// any amount of reordering, and it picks up their central <c>ILoggableHandler</c> in the same pass.
    ///
    /// <b>Scoped to their client type and its nested types.</b> That is where the logging is, and a narrow scan
    /// means this cannot start rewriting parts of their networking stack that were never the point.
    ///
    /// <b>Yields nothing when Phinix is absent,</b> which is the normal case for most players, and Harmony then
    /// patches nothing at all.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PhinixInfoLog
    {
        /// <summary>Phinix's client class, which is where every one of these calls lives.</summary>
        private const string ClientTypeName = "PhinixClient.Client";

        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            List<MethodBase> found = new List<MethodBase>();

            UIGuard.Try("Integrations.FindPhinixLogging", () => Collect(found),
                "Phinix's information logging is not suppressed. Phinix itself is unaffected.");

            UIDebug.Log(found.Count == 0
                ? "No Phinix methods to silence: either Phinix is absent or its logging has moved."
                : "Silencing information logging in " + found.Count + " Phinix method(s).");

            return found;
        }

        private static void Collect(List<MethodBase> found)
        {
            if (!ModIntegrations.Loaded(ModIntegrations.PhinixPackageId))
                return;

            Type client = AccessTools.TypeByName(ClientTypeName);

            if (client == null)
                return;

            MethodInfo target = AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) });

            if (target == null)
                return;

            foreach (Type type in Scanned(client))
            {
                foreach (MethodBase method in Methods(type))
                {
                    if (Calls(method, target))
                        found.Add(method);
                }
            }
        }

        /// <summary>Phinix's client type and anything the compiler nested inside it.</summary>
        private static IEnumerable<Type> Scanned(Type client)
        {
            yield return client;

            Type[] nested = client.GetNestedTypes(AccessTools.all);

            if (nested == null)
                yield break;

            foreach (Type type in nested)
                yield return type;
        }

        /// <summary>
        /// Every method and constructor declared on a type, including the compiler-generated ones.
        ///
        /// <c>DeclaredOnly</c> matters: without it the scan walks up into base types and would offer Harmony a
        /// method that is not Phinix's to patch.
        /// </summary>
        private static IEnumerable<MethodBase> Methods(Type type)
        {
            // Spelled out rather than AccessTools.all, which is a static field and so cannot seed a const.
            // DeclaredOnly matters: without it the scan walks up into base types and would offer Harmony a
            // method that is not Phinix's to patch.
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static
                                       | BindingFlags.DeclaredOnly;

            foreach (MethodInfo method in type.GetMethods(Flags))
            {
                // Abstract and generic definitions have nothing to read and nothing to patch.
                if (!method.IsAbstract && !method.ContainsGenericParameters)
                    yield return method;
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(Flags))
                yield return constructor;
        }

        /// <summary>
        /// Whether a method contains a call to <paramref name="target"/>.
        ///
        /// <c>ReadMethodBody</c> rather than the full instruction reader, because this only needs to know
        /// whether an opcode operand names one method, and that reader needs no ILGenerator and cannot fail on
        /// branch fixups.
        /// </summary>
        private static bool Calls(MethodBase method, MethodInfo target)
        {
            return UIGuard.Try("Integrations.ReadPhinixIL", () =>
            {
                foreach (KeyValuePair<OpCode, object> instruction in PatchProcessor.ReadMethodBody(method))
                {
                    if (instruction.Key == OpCodes.Call && ReferenceEquals(instruction.Value, target))
                        return true;
                }

                return false;
            }, false, null);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            MethodInfo target = AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) });
            MethodInfo replacement = AccessTools.Method(typeof(PhinixLogSilencer),
                nameof(PhinixLogSilencer.Swallow));

            int replaced = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && ReferenceEquals(instruction.operand, target))
                {
                    replaced++;

                    // Labels and exception blocks carried across, or a branch into this instruction and the
                    // surrounding try would both be lost.
                    yield return new CodeInstruction(OpCodes.Call, replacement)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };

                    continue;
                }

                yield return instruction;
            }

            // Reported rather than ignored: the method was chosen because the IL reader saw the call, so finding
            // none here means the two disagree, and a silent no-op would look like the setting not working.
            if (replaced == 0)
                UIDebug.Warning("Found no Log.Message call to replace in "
                                + (original == null ? "an unknown Phinix method" : original.Name) + ".");
        }
    }
}
