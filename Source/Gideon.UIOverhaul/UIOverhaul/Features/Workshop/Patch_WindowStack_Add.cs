using System;
using System.IO;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Workshop
{
    /// <summary>
    /// Streamlines the Steam Workshop upload flow.
    ///
    /// Moved here from Gideon's Misc Patches, unchanged in behavior, and removed from that mod rather than
    /// left in both. It belongs with the rest of the dialog work: what it fixes is a UI flow, not a game rule.
    ///
    /// There is deliberately no guard against Misc Patches also being installed. An earlier version had one --
    /// a Harmony Prepare that stood down when that mod was present -- which became a bug the moment the patch
    /// was removed from it: Misc Patches lives on for other patches, so the guard would see it installed, stand
    /// down, and leave nobody handling the dialog at all.
    ///
    /// Vanilla makes you clear two dialogs to publish a mod:
    ///
    ///   1. Dialog_ConfirmModUpload -- the Workshop terms-of-service prompt. Always kept; it also carries the
    ///      "tag as translation" checkbox.
    ///   2. A Dialog_MessageBox asking "Did you create this content yourself?", built in
    ///      Page_ModsConfig.DoModInfo with interactionDelay = 6f, which greys out the Yes button for six
    ///      seconds.
    ///
    /// Step 2 is handled two ways:
    ///   - Re-uploading a mod already published from this machine (About/PublishedFileId.txt exists): skipped
    ///     entirely, going straight to the upload.
    ///   - Anything else -- a first-time upload, or a case where the mod being published cannot be identified:
    ///     the prompt is shown as usual, just without the countdown.
    ///
    /// The split matters. The prompt is an authorship attestation, so answering it on the player's behalf is
    /// only defensible where they have already answered it for that exact mod. A first upload still asks.
    ///
    /// WindowStack.Add is the interception point because the delay is assigned after the constructor returns
    /// (so a ctor postfix would be overwritten) and because the code that builds the dialog lives in a lambda
    /// -- &lt;&gt;c__DisplayClass71_0.&lt;DoModInfo&gt;b__9 -- whose compiler-generated name shifts between game
    /// builds. WindowStack.Add is the first stable public member that runs afterwards.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class Patch_WindowStack_Add
    {
        /// <summary>
        /// <b>Guarded, with the dialog left in place if anything goes wrong.</b> WindowStack.Add is called for every
        /// window the game opens, so an escape from here would not be confined to mod uploads -- it would fire on
        /// whatever window happened to be opening.
        ///
        /// True is the fallback, which means vanilla's confirmation appears as though this patch were not installed.
        /// Note what that implies when the throw came from <c>buttonAAction</c> itself: the upload has already been
        /// started and failed, and the player then sees the prompt again. That is the right way round -- a visible
        /// prompt they can answer beats an upload that was skipped silently.
        /// </summary>
        private static bool Prefix(Window window)
        {
            try
            {
                return Decide(window);
            }
            catch (Exception ex)
            {
                UIGuard.Report("Workshop.SkipAuthorPrompt", ex,
                    "The Workshop authorship prompt and its countdown are shown as vanilla does.");
                return true;
            }
        }

        private static bool Decide(Window window)
        {
            if (!(window is Dialog_MessageBox box))
                return true;

            if (IsContentAuthorPrompt(box) && box.buttonAAction != null)
            {
                ModMetaData mod = ResolveTargetMod(box);

                if (mod != null && HasBeenPublishedBefore(mod))
                {
                    // Dialog_MessageBox.CreateConfirmation assigns the confirm delegate to both buttonAAction
                    // and acceptAction, so this is exactly "clicked Yes". Workshop.Upload adds its own progress
                    // window from in here; that re-entrant Add is not a Dialog_MessageBox and passes straight
                    // through.
                    box.buttonAAction();
                    return false;
                }
            }

            // Never make the player sit through a countdown, even when the prompt is shown. Also covers the
            // terms-of-service dialog, which carries no delay today.
            if (box.interactionDelay > 0f && (IsContentAuthorPrompt(box) || box is Dialog_ConfirmModUpload))
                box.interactionDelay = 0f;

            return true;
        }

        /// <summary>
        /// Matches on the translated text rather than the raw key so this holds up in every language, and so
        /// unrelated confirmation dialogs are left alone.
        /// </summary>
        private static bool IsContentAuthorPrompt(Dialog_MessageBox box)
        {
            // text is a TaggedString struct, but a message box built with no text at all leaves its RawText null,
            // so this is read defensively rather than assumed.
            string text = box.text.RawText;

            if (string.IsNullOrEmpty(text))
                return false;

            return text == "ConfirmContentAuthor".Translate().RawText;
        }

        /// <summary>
        /// A PublishedFileId.txt in the mod's About folder is written when a mod is first accepted by the
        /// Workshop, so its presence means this mod has been uploaded from this machine before and the
        /// authorship question has already been answered once for it.
        /// </summary>
        private static bool HasBeenPublishedBefore(ModMetaData mod)
        {
            // Mirrors the non-public ModMetaData.PublishedFileIdPath, which builds
            // <RootDir>\About\PublishedFileId.txt the same way.
            DirectoryInfo root = mod.RootDir;

            if (root == null)
                return false;

            return File.Exists(Path.Combine(root.FullName, "About", "PublishedFileId.txt"));
        }

        /// <summary>
        /// The mod being uploaded is captured in the closure behind the confirm delegate
        /// (Page_ModsConfig's &lt;&gt;c__DisplayClass71_0.mod). Located by field type rather than by name, since
        /// the generated names move between game builds. Returns null when there is no ModMetaData to be found
        /// -- the scenario editor reuses this same prompt for scenario uploads, and those are left alone.
        /// </summary>
        private static ModMetaData ResolveTargetMod(Dialog_MessageBox box)
        {
            object closure = box.buttonAAction.Target;

            if (closure == null)
                return null;

            try
            {
                FieldInfo[] fields = closure.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (FieldInfo field in fields)
                {
                    if (typeof(ModMetaData).IsAssignableFrom(field.FieldType))
                        return field.GetValue(closure) as ModMetaData;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(UILogTag.Prefix + "Could not resolve the mod behind the authorship prompt, "
                            + "leaving it in place: " + ex);
            }

            return null;
        }
    }
}
