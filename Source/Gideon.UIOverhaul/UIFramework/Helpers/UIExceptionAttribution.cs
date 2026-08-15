using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Names the mods an escaping UI exception passed through, for the reports that otherwise name nobody.
    ///
    /// <b>The problem this exists for.</b> RimWorld draws its whole interface inside one Unity callback, and an
    /// exception that gets out of it is logged by Unity with a stack trace and nothing else. That trace is mostly
    /// vanilla frames, because vanilla is what calls everything -- so the log says a null reference happened
    /// somewhere under <c>UIRootOnGUI</c> and leaves the reader to work out which of their hundred and forty mods
    /// put it there. The usual outcome is that the most visible mod gets blamed, which for a mod that repaints the
    /// entire interface means this one.
    ///
    /// <b>Two different questions, and both are worth answering.</b> A mod can be implicated by having its own
    /// code on the stack, and separately by having <i>patched</i> something on the stack -- a prefix that ran and
    /// left a field null, a transpiler that changed what a method does. The second kind never appears in a stack
    /// trace at all: Harmony's patched method carries vanilla's name, so a transpiler's damage is attributed to
    /// the method it damaged. Asking Harmony who patched each frame is the only way to see it.
    ///
    /// <b>This does not catch anything.</b> <see cref="Gideon.UIFramework.Helpers.UIGuard"/> exists to stop
    /// exceptions from our own code; this is the opposite case -- somebody else's exception, on its way past, which
    /// is not ours to swallow. Suppressing it would take the original error out of the log and replace it with our
    /// summary, and a diagnostic that destroys the evidence it is describing is worse than no diagnostic. So the
    /// exception carries on exactly as it would have, and this adds one line beside it.
    ///
    /// <b>Warning rather than Error, deliberately.</b> The error is already in the log, written by whoever the
    /// exception reached. This is commentary on it, and giving commentary the same severity as the fault would
    /// make every UI exception look like two.
    ///
    /// <b>Flood control is <see cref="UIGuard"/>'s.</b> An exception on a draw path repeats every frame, and the
    /// counting that turns that into six lines instead of a hundred thousand is already written and already tested.
    /// This calls <c>UIGuard.ShouldReport</c> rather than keeping its own, so the two can never disagree about
    /// what counts as the same fault.
    /// </summary>
    public static class UIExceptionAttribution
    {
        /// <summary>
        /// How many stack frames are examined.
        ///
        /// A UI stack during a deep draw is long, and everything that matters is near the throw. Past this depth
        /// the frames are the game's own bootstrapping, which implicates nobody.
        /// </summary>
        private const int FramesExamined = 40;

        /// <summary>How many names are printed per group before the rest are counted rather than listed.</summary>
        private const int NamesListed = 6;

        /// <summary>
        /// Which mod each loaded assembly belongs to.
        ///
        /// Built once. Mods cannot be loaded or unloaded without a restart, so this cannot go stale, and building
        /// it per exception would mean walking every mod's assembly list on a path that only runs when something
        /// is already going wrong.
        /// </summary>
        private static Dictionary<Assembly, string> owners;

        /// <summary>
        /// Writes the attribution line for <paramref name="ex"/>, if this one is due a report.
        ///
        /// Silent when nothing outside the game is implicated. A UI exception with only vanilla on the stack and
        /// no patches anywhere near it is one this has nothing to add to, and a line saying so on every frame
        /// would be noise standing exactly where the useful version would have gone.
        /// </summary>
        public static void Note(string site, Exception ex)
        {
            // Runs while an exception is already in flight, so a fault here would replace somebody's real error
            // with ours. Nothing in this method may throw, for the same reason UIGuard.Report may not.
            try
            {
                if (ex == null)
                    return;

                int failures;
                bool novel;

                // Asked before the stack is examined, not after, and the order is the whole performance story
                // here. A UI exception repeats every frame, and walking forty frames, resolving each through
                // Harmony's registry and mapping assemblies to mods is real work to do sixty times a second on a
                // game that is already in trouble. Flood control answers in a dictionary lookup, so letting it
                // decide first means the expensive half runs only on the handful of frames that produce a line.
                if (!UIGuard.ShouldReport(site, ex, out failures, out novel))
                    return;

                List<string> onStack = new List<string>();
                List<string> patchers = new List<string>();

                Examine(ex, onStack, patchers);

                // Nothing outside the game was involved, so there is nothing to add that the error report does
                // not already say. The count above still advanced, which is correct: this fault happened, and a
                // later one at the same site should not present itself as the first.
                if (onStack.Count == 0 && patchers.Count == 0)
                    return;

                Log.Warning(Describe(site, onStack, patchers, failures, novel));
            }
            catch
            {
                // Deliberately bare and deliberately silent. This method is decoration on somebody else's error
                // report; failing to decorate it is not worth a line of its own, and the error it was describing
                // has already been logged by whoever caught it.
            }
        }

        /// <summary>
        /// Walks the stack, collecting mods with code on it and mods that have patched what is on it.
        ///
        /// Innermost frame first, and the order is kept: the mod nearest the throw is the one to look at first,
        /// and sorting the names alphabetically would throw that away.
        /// </summary>
        private static void Examine(Exception ex, List<string> onStack, List<string> patchers)
        {
            StackTrace trace = new StackTrace(ex, false);
            int count = Math.Min(trace.FrameCount, FramesExamined);

            for (int i = 0; i < count; i++)
            {
                StackFrame frame = trace.GetFrame(i);

                if (frame == null)
                    continue;

                // Each frame is examined independently, because either half can fail on a frame the other
                // handles: a dynamic method has no declaring assembly, and Harmony cannot resolve a frame that
                // did not come from one of its own replacements. Losing one frame should not lose the rest.
                Collect(onStack, ModOfFrame(frame));
                CollectPatchers(patchers, frame);
            }
        }

        /// <summary>
        /// The mod that wrote the method in this frame, or null for the game's own code and for anything with no
        /// assembly to ask about.
        /// </summary>
        private static string ModOfFrame(StackFrame frame)
        {
            try
            {
                MethodBase method = frame.GetMethod();
                Assembly assembly = method?.DeclaringType?.Assembly;

                if (assembly == null)
                    return null;

                string mod;

                return Owners.TryGetValue(assembly, out mod) ? mod : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Who has patched the method this frame is running.
        ///
        /// <b>Harmony's own lookup, not a guess.</b> A patched method executes as a generated replacement whose
        /// name and declaring type are the original's, so the frame looks exactly like vanilla.
        /// <c>GetOriginalMethodFromStackframe</c> is what turns that back into the method Harmony replaced, and
        /// <c>GetPatchInfo</c> then says which Harmony instances have work attached to it.
        ///
        /// <b>Every patcher of the method is named, not the one that failed,</b> and the report says so. There is
        /// no way from here to tell which of four prefixes left the field null; what can be said honestly is that
        /// these four had a hand in this method, which is four names to look at instead of a hundred and forty.
        /// </summary>
        private static void CollectPatchers(List<string> patchers, StackFrame frame)
        {
            try
            {
                MethodBase original = Harmony.GetOriginalMethodFromStackframe(frame);

                if (original == null)
                    return;

                // Qualified, because this assembly has a Gideon.UIFramework.Patches namespace and an unqualified
                // Patches resolves to that from inside it.
                HarmonyLib.Patches info = Harmony.GetPatchInfo(original);

                if (info?.Owners == null)
                    return;

                foreach (string owner in info.Owners)
                    Collect(patchers, owner);
            }
            catch
            {
                // Harmony resolves this from its own registry and is entitled to not recognize a frame. A frame
                // it cannot place is one with nothing to report rather than an error.
            }
        }

        private static void Collect(List<string> into, string name)
        {
            if (!name.NullOrEmpty() && !into.Contains(name))
                into.Add(name);
        }

        /// <summary>
        /// Assembly to mod name, built from the mod list once.
        ///
        /// Core and the official expansions are included rather than filtered out. A frame in
        /// <c>Assembly-CSharp</c> says "this is the game's own code", which is worth being able to see stated
        /// rather than inferred from a name's absence.
        /// </summary>
        private static Dictionary<Assembly, string> Owners
        {
            get
            {
                if (owners != null)
                    return owners;

                owners = new Dictionary<Assembly, string>();

                try
                {
                    List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

                    if (mods == null)
                        return owners;

                    foreach (ModContentPack mod in mods)
                    {
                        List<Assembly> loaded = mod?.assemblies?.loadedAssemblies;

                        if (loaded == null)
                            continue;

                        foreach (Assembly assembly in loaded)
                        {
                            if (assembly != null && !owners.ContainsKey(assembly))
                                owners[assembly] = mod.Name ?? mod.PackageId ?? "an unnamed mod";
                        }
                    }
                }
                catch
                {
                    // A partial map is still worth having: whatever was read before the failure still attributes
                    // its own frames, and the rest simply go unnamed.
                }

                return owners;
            }
        }

        private static string Describe(string site, List<string> onStack, List<string> patchers, int failures,
            bool novel)
        {
            StringBuilder text = new StringBuilder();

            text.Append(UILogTag.Prefix)
                .Append("An exception escaped ").Append(site)
                .Append(". The error itself is logged separately; this line only says who was involved.");

            if (novel && failures > 1)
                text.Append(" This is a different exception from the last one seen here.");
            else if (failures > 1)
                text.Append(" This has now happened ").Append(failures)
                    .Append(" times; the next note is at ").Append(failures * 10).Append(".");

            if (onStack.Count > 0)
                text.Append("\nCode on the stack, nearest the throw first: ").Append(List(onStack));

            if (patchers.Count > 0)
                text.Append("\nHarmony patches on methods in that stack: ").Append(List(patchers))
                    .Append(". These are Harmony ids rather than mod names, and being listed means having "
                            + "patched one of those methods -- not having caused this.");

            text.Append("\nNone of this is proof. A mod appears here because its code ran or its patch is "
                        + "attached, which is where to look first rather than a verdict.");

            return text.ToString();
        }

        private static string List(List<string> names)
        {
            if (names.Count <= NamesListed)
                return string.Join(", ", names.ToArray());

            string[] shown = names.GetRange(0, NamesListed).ToArray();

            return string.Join(", ", shown) + " and " + (names.Count - NamesListed) + " more";
        }
    }
}
