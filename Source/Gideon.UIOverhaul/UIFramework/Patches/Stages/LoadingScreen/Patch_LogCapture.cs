using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIFramework.Stages;
using HarmonyLib;
using Verse;

namespace Gideon.UIFramework.Patches.Stages.LoadingScreen
{
    /// <summary>
    /// Copies errors and warnings into the loading console, so they sit in position among the phases that raised
    /// them.
    ///
    /// <b>Position is the whole point.</b> RimWorld's own log has all of these already, and the reason it is hard
    /// to work with during a load is that it is a flat list: a missing texture, an XML parse failure and a def
    /// error all look alike and none of them says which phase was running or which definition was being processed
    /// at the time. Interleaved with the load's own sequence, an error lands directly after the definition that
    /// caused it and directly under the phase it happened in, which is usually the entire diagnosis.
    ///
    /// <b>Only two methods need patching.</b> <c>ErrorOnce</c> and <c>WarningOnce</c> both delegate to
    /// <c>Error</c> and <c>Warning</c> after checking their key, so covering those two covers every path that
    /// reaches the log, including the ones a mod uses to avoid flooding it.
    ///
    /// <b>Reentrancy is guarded, and it is not a theoretical worry.</b> These postfixes run inside
    /// <c>Log</c>'s own lock, and <c>UIGuard.Report</c> writes to <c>Log.Error</c> -- so a fault while recording
    /// a captured error would come straight back through this method on the same thread. The flag stops that
    /// becoming an unbounded recursion. Per thread, because loading errors arrive on the loading thread while
    /// everything else arrives on the main one.
    ///
    /// <b>Nothing is suppressed.</b> This is a postfix that reads and returns; the message goes to RimWorld's log
    /// exactly as it would have. A diagnostic that quietly swallowed errors to show them somewhere prettier would
    /// be worse than not having one.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_LogCapture
    {
        [ThreadStatic] private static bool capturing;

        [HarmonyPatch(typeof(Log), nameof(Log.Error), typeof(string))]
        [HarmonyPostfix]
        public static void CaptureError(string text)
        {
            Capture(UILoadingLogKind.Error, text);
        }

        [HarmonyPatch(typeof(Log), nameof(Log.Warning), typeof(string))]
        [HarmonyPostfix]
        public static void CaptureWarning(string text)
        {
            Capture(UILoadingLogKind.Warning, text);
        }

        /// <summary>
        /// Written out rather than going through <c>UIGuard.Try</c>: a closure per logged line is avoidable
        /// garbage, and reporting a failure here through the very method that failed is what the flag above
        /// exists to survive.
        /// </summary>
        private static void Capture(UILoadingLogKind kind, string text)
        {
            if (capturing)
                return;

            // A postfix still runs when a prefix has skipped the original, so this has to test for a replay
            // itself rather than rely on the line never being written. Without it, running the bug hunt at the
            // main menu would file every re-raised parse error into the loading console as though the load had
            // just produced it. See UILogReplay.
            if (UILogReplay.Active)
                return;

            try
            {
                capturing = true;

                // Active is false for the whole of a colony's life, so during play this is one boolean read on a
                // path that is already taking a lock and building a stack trace.
                if (!UILoadingLog.Active || text.NullOrEmpty())
                    return;

                // The file being parsed on this thread, if one is. A def error names a field and a type and
                // never the file it is in, which is the single most useful thing to know about it.
                string path = Patch_DefSources.CurrentPath;

                // Nothing is being parsed, so this arrived from a later phase. Cross-reference failures are the
                // common case and they are worth chasing, because the request that failed was registered back
                // when a file was open. See CrossRefPathFor.
                if (path.NullOrEmpty())
                    path = CrossRefPathFor(text);

                // Anything Scribe is reading -- a mod's settings, a save, a world -- has a file open for the
                // whole of the read, so a failure inside it belongs to that file even though nothing about the
                // message says so.
                if (path.NullOrEmpty())
                    path = Patch_ScribeSources.CurrentFile;

                // Messages that name a type or a patch element instead of a file. One of these is exact and the
                // other narrow; both answer null instantly for anything that is not theirs.
                if (path.NullOrEmpty())
                    path = Patch_ModSources.PathFor(text);

                // Last and broadest: any definition named anywhere in the message. This is a deduction over the
                // whole text rather than a rule about one wording, so it goes after everything that knows.
                if (path.NullOrEmpty())
                    path = UILoadingLog.PathMentionedIn(text);

                UILoadingLog.Record(kind, text, path);
            }
            catch
            {
                // Deliberately bare, and deliberately silent. The only way to report a failure here is the log,
                // which is the thing that just failed; a message about it would arrive through this method again.
                // The captured line is lost and RimWorld's own log still has it, which is the important half.
            }
            finally
            {
                capturing = false;
            }
        }

        /// <summary>The prefix of every cross-reference failure vanilla raises, from <c>TryResolveDef</c>.</summary>
        private const string CrossRefPrefix = "Could not resolve cross-reference";

        /// <summary>
        /// The file that asked for the def a cross-reference failure names, if it was recorded.
        ///
        /// <b>Read out of the message, because the message is all there is.</b> The failure is raised from a
        /// phase with no file context and no reference back to the request that produced it, so the only handle
        /// on which def was wanted is the name vanilla wrote into the text. Matching on that against what was
        /// recorded during parsing is what turns "something wanted this" into "this file wanted this".
        ///
        /// Deliberately narrow: it only fires on vanilla's own wording and gives up on anything unexpected. A
        /// diagnostic that guesses a file path is worse than one that admits it does not know, because the guess
        /// is what somebody will go and edit.
        /// </summary>
        private static string CrossRefPathFor(string text)
        {
            if (text == null || !text.StartsWith(CrossRefPrefix))
                return null;

            // "... named SomeDefName" optionally followed by " (wanter=...)". The name is what sits between.
            const string marker = " named ";

            int start = text.IndexOf(marker, StringComparison.Ordinal);

            if (start < 0)
                return null;

            start += marker.Length;

            int end = text.IndexOf(" (", start, StringComparison.Ordinal);

            if (end < 0)
                end = text.IndexOf('\n', start);

            if (end < 0)
                end = text.Length;

            string defName = text.Substring(start, end - start).Trim();

            return Patch_DefSources.WanterOf(defName);
        }
    }

    /// <summary>
    /// Records which file wanted each cross-referenced def, while the file is still open.
    ///
    /// <b>Only the non-generic registrations are covered, and the reason is worth stating.</b>
    /// <c>DirectXmlCrossRefLoader</c> registers single-def references through non-generic overloads, which patch
    /// cleanly, and list and dictionary references through <c>RegisterListWantsCrossRef&lt;T&gt;</c> and
    /// <c>RegisterDictionaryWantsCrossRef&lt;K, V&gt;</c>, which are generic methods. Harmony cannot patch an
    /// open generic method, and the usual workaround -- closing it over <c>Def</c> and relying on every reference
    /// type sharing one compiled body -- is a runtime implementation detail rather than a guarantee. Betting a
    /// shipped mod's def loading on it to improve a log line is not a trade worth making.
    ///
    /// So a failed reference from a list-valued field, which is what <c>genes</c> or <c>comps</c> is, still has
    /// no file against it. That is a known and stated limitation rather than an oversight.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CrossRefSources
    {
        [HarmonyPatch(typeof(DirectXmlCrossRefLoader), nameof(DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef),
            typeof(object), typeof(FieldInfo), typeof(string), typeof(string), typeof(string), typeof(Type))]
        [HarmonyPostfix]
        public static void ByField(string targetDefName)
        {
            Note(targetDefName);
        }

        [HarmonyPatch(typeof(DirectXmlCrossRefLoader), nameof(DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef),
            typeof(object), typeof(string), typeof(string), typeof(string), typeof(string), typeof(Type))]
        [HarmonyPostfix]
        public static void ByName(string targetDefName)
        {
            Note(targetDefName);
        }

        /// <summary>
        /// Written out rather than going through <c>UIGuard.Try</c>: this runs once per cross-reference in the
        /// game, which is a great many times, and a closure each would be avoidable garbage on the loading thread.
        /// </summary>
        private static void Note(string targetDefName)
        {
            try
            {
                if (UILoadingLog.Active)
                    Patch_DefSources.NoteWantedDef(targetDefName);
            }
            catch (Exception ex)
            {
                UIGuard.Report("LoadingScreen.CrossRefSource", ex,
                    "Cross-reference errors in the loading console do not name the file that wanted the def.");
            }
        }
    }

    /// <summary>
    /// Publishes the file Scribe is reading, for anything that logs a problem while it reads.
    ///
    /// <b>The third and broadest way a captured error gets a path,</b> and it covers a class the other two
    /// cannot see at all. Scribe is not the def loader: it reads mod settings, saves and worlds, and a failure
    /// inside it is reported from deep in <c>ScribeExtractor</c> with no idea which file it came out of. The
    /// message is typically a type name and a subnode -- accurate, and useless for finding the file to edit.
    ///
    /// A real one, from a load this was built against:
    /// <c>Can't load abstract class ModSettingsFramework.PatchOperationWorker</c>, with a subnode naming a
    /// settings class from a mod that had been switched off. The stale entry was in one file in the config
    /// folder, and nothing in the error said so.
    ///
    /// <c>InitLoading</c> is handed the path and holds it open until <c>FinalizeLoading</c>, which is exactly
    /// the shape the def parser's ambient has, so it is done the same way.
    ///
    /// <b>Cleared in a finalizer as well as on the normal path,</b> so a read that throws does not leave every
    /// later error in the load attributed to a file that stopped being read. Returning void rather than
    /// <c>Exception</c> is deliberate: a finalizer declared to return one <i>replaces</i> the exception, and
    /// returning null there would swallow Scribe failures wholesale.
    /// </summary>
    public static class Patch_ScribeSources
    {
        [ThreadStatic] private static string currentFile;

        /// <summary>The file Scribe is reading on this thread, or null.</summary>
        public static string CurrentFile => currentFile;

        /// <summary>
        /// The two openers, which share both of their patches.
        ///
        /// <b>Split into its own class because the targets have to be listed in code.</b> Two
        /// <c>[HarmonyPatch]</c> attributes on one patch method are merged by Harmony into a single target
        /// rather than producing two, so the version of this that carried one attribute per opener only ever
        /// patched <c>InitLoadingMetaHeaderOnly</c> -- and <c>InitLoading</c>, the one that opens an actual
        /// save, was never patched at all. The cost was Scribe errors during a real load having no file
        /// attributed to them, which is the one thing this class exists to supply.
        ///
        /// <c>[HarmonyTargetMethods]</c> governs every patch method in its class, which is why the closer
        /// below cannot sit in here: it targets a different method.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_Openers
        {
            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> Targets()
            {
                yield return AccessTools.Method(typeof(ScribeLoader), nameof(ScribeLoader.InitLoading));
                yield return AccessTools.Method(typeof(ScribeLoader),
                    nameof(ScribeLoader.InitLoadingMetaHeaderOnly));
            }

            [HarmonyPrefix]
            public static void Opening(string filePath)
            {
                currentFile = UILoadingLog.Active ? filePath : null;
            }

            /// <summary>
            /// Clears the path again when the file never actually opened.
            ///
            /// <b>Both openers log and then rethrow,</b> having called <c>ForceStop</c> first, so a failed open
            /// leaves <c>Scribe.mode</c> at <c>Inactive</c> and never reaches a matching
            /// <c>FinalizeLoading</c>. Without this the path would stay published for the rest of the thread's
            /// life and every later error in the load would be blamed on a file that was never read. Testing
            /// the mode is what tells the two apart: a successful open sets it to <c>LoadingVars</c>.
            /// </summary>
            [HarmonyFinalizer]
            public static void OpeningFailed()
            {
                if (Scribe.mode != LoadSaveMode.LoadingVars)
                    currentFile = null;
            }
        }

        [HarmonyPatch(typeof(ScribeLoader), nameof(ScribeLoader.FinalizeLoading))]
        public static class Patch_Closer
        {
            [HarmonyPostfix]
            public static void Closed()
            {
                currentFile = null;
            }
        }
    }

    /// <summary>
    /// Records the full path of any XML file that could not be read at all.
    ///
    /// <b>The one error where vanilla has the path and does not print it.</b> <c>LoadableXmlAsset</c> catches its
    /// own parse failure and logs "Exception reading Foo.xml as XML", with the file's bare name and not its
    /// folder -- and the folder is the entire question, because on a large mod list a dozen mods ship a file
    /// called <c>Buildings.xml</c> and knowing that one of them is malformed narrows nothing. The object being
    /// constructed knows exactly where it came from, so this reads it off and records it beside vanilla's own
    /// message.
    ///
    /// A postfix on the constructor, testing the result rather than intercepting the failure: the asset sets its
    /// document to null when it could not parse, so a null document after construction is precisely the case
    /// worth reporting, and nothing here interferes with how vanilla handles it.
    /// </summary>
    [HarmonyPatch(typeof(LoadableXmlAsset))]
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(System.IO.FileInfo), typeof(ModContentPack) })]
    public static class Patch_LoadableXmlAsset_Ctor
    {
        public static void Postfix(LoadableXmlAsset __instance)
        {
            UIGuard.Try("LoadingScreen.XmlAssetRead", () =>
                {
                    if (__instance?.xmlDoc != null || !UILoadingLog.Active)
                        return;

                    string path = __instance == null ? null : __instance.FullFilePath;

                    UILoadingLog.Record(UILoadingLogKind.Error,
                        "This XML file could not be parsed and none of its contents were loaded. RimWorld's own "
                        + "log has the parser's message.", path);
                },
                "An unreadable XML file is not listed in the loading console. RimWorld's log still reports it.");
        }
    }

    /// <summary>
    /// Stops the console recording and gives back everything it held, once a game is actually running.
    ///
    /// <b>The console is a main menu diagnostic and nothing else.</b> Per-definition logging on a heavy mod list
    /// is tens of thousands of entries, which is a fair price while somebody is reading it at the menu and no
    /// price worth paying for the rest of a colony's life. <c>FinalizeInit</c> is where a game becomes playable,
    /// so it is where this hands the memory back.
    ///
    /// The path cache goes with it. Those strings exist only for the log's entries to point at, and the
    /// dictionary is keyed by vanilla's asset objects, so keeping it would hold every one of them alive long
    /// after the game has finished with them.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_Game_FinalizeInit_StopConsole
    {
        public static void Postfix()
        {
            UIGuard.Try("LoadingScreen.ReleaseConsole", () =>
                {
                    UILoadingLog.Deactivate();
                    Patch_DefSources.ClearCache();
                },
                "The loading console's memory is held until the game is restarted.");
        }
    }

    /// <summary>
    /// Starts the console recording again when the main menu comes back.
    ///
    /// Quitting to the menu and loading a different save is a second load worth being able to read, and without
    /// this the console would stay switched off for the rest of the session after the first game started.
    ///
    /// <c>Init</c> rather than the menu's drawing: it is called once on the way in rather than every frame, and
    /// it also runs on the way to the menu at startup, where turning recording on is already a no-op.
    /// </summary>
    [HarmonyPatch(typeof(RimWorld.MainMenuDrawer), nameof(RimWorld.MainMenuDrawer.Init))]
    public static class Patch_MainMenuDrawer_Init_ResumeConsole
    {
        public static void Postfix()
        {
            UIGuard.Try("LoadingScreen.ResumeConsole", UILoadingLog.Activate,
                "The loading console does not record anything further this session.");
        }
    }
}
