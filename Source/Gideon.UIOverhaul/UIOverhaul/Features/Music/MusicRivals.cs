using System;
using System.Collections.Generic;
using System.Reflection;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// Whether another mod is already managing the music, in which case ours stands down.
    ///
    /// <b>Why it matters:</b> two music players both driving RimWorld's one audio source means two songs
    /// competing for it, and the symptom -- music cutting itself off every few seconds -- reads as a bug in
    /// whichever mod the player happens to blame. Standing down is the only sane answer, and it has to happen
    /// without being asked, because somebody installing this alongside RimTunes has no reason to expect the
    /// conflict.
    ///
    /// <b>Two tests, because either alone is not enough.</b> A list of package ids is exact and can name the mod
    /// it found, which is what the settings window needs to say; but a list goes stale, and the mods here have
    /// already been forked once each. So there is also a scan for anybody else patching the music manager, which
    /// catches a music mod nobody has told us about -- at the cost of not knowing what to call it beyond the
    /// Harmony id its author chose.
    ///
    /// <b>Answered once, late.</b> The scan has to run after every other mod has applied its patches, so this is
    /// not touched from a constructor: the first thing to ask is the settings window or the first frame of music,
    /// both of which are long past load. Cached afterwards, because a Harmony patch scan is not a per-frame
    /// question.
    /// </summary>
    internal static class MusicRivals
    {
        /// <summary>
        /// The mods known to manage music, and what to call them.
        ///
        /// The three the player named. Each of these two Continued forks carries a different package id from the
        /// original it replaced, and the originals are not listed because their ids are not known here -- the
        /// patch scan below is what covers them, and anything else of the kind.
        /// </summary>
        private static readonly string[][] Known =
        {
            new[] { "DepsCian.RimTunes", "RimTunes" },
            new[] { "zal.musicmanager", "Music Manager" },
            new[] { "zal.mef", "Music Expanded Framework" }
        };

        /// <summary>
        /// The methods a mod has to touch to be managing music rather than merely adding songs.
        ///
        /// Choosing what plays, starting it, or forcing a particular song. A mod that only ships
        /// <c>SongDef</c>s -- which is nearly everything on the Workshop tagged music, including the three
        /// Odyssey soundtracks this was tested against -- patches none of these and is not a rival at all. It is
        /// content, and our player is the thing that finally shows it.
        ///
        /// Touching one of these is necessary and not sufficient; see <see cref="CanTakeOver"/>.
        /// </summary>
        private static readonly string[] Guarded = { "MusicUpdate", "StartNewSong", "ForcePlaySong" };

        private static bool answered;

        private static string found;

        /// <summary>
        /// Whether to stand down: a rival is loaded and the player has not insisted.
        ///
        /// Separate from <see cref="Detected"/>, which stays the factual answer. The settings window needs to
        /// name the rival even when it has been overridden, because a line saying nothing was found would be a
        /// lie about why the checkbox is unlocked.
        /// </summary>
        internal static bool Any => Detected != null && !Overridden;

        /// <summary>Whether the player has said to run ours anyway.</summary>
        internal static bool Overridden
        {
            get
            {
                return UIGuard.Try("Music.Override", () =>
                {
                    UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                    return settings != null && settings.musicPlayerOverride;
                }, false, null);
            }
        }

        /// <summary>The rival's name for the settings window, or null when there is none.</summary>
        internal static string Detected
        {
            get
            {
                if (answered)
                    return found;

                answered = true;

                found = UIGuard.Try("Music.DetectRivals", Detect, null,
                    "The music player could not check for other music mods, so it assumes it is the only one.");

                return found;
            }
        }

        /// <summary>
        /// Re-asks the question.
        ///
        /// For the settings window's own use after the player has been told to remove a mod: without this the
        /// answer would be stuck until a restart, which is correct for the game state and wrong for a player who
        /// wants to see the checkbox come back.
        /// </summary>
        internal static void Forget()
        {
            answered = false;
            found = null;
        }

        private static string Detect()
        {
            string named = ByPackageId();

            return named ?? ByPatch();
        }

        private static string ByPackageId()
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];

                if (mod == null || mod.PackageId.NullOrEmpty())
                    continue;

                for (int k = 0; k < Known.Length; k++)
                {
                    // PackageId is lower cased by RimWorld, and PackageIdPlayerFacing is not, so the comparison
                    // ignores case rather than relying on which one this is.
                    if (mod.PackageId.Equals(Known[k][0], StringComparison.OrdinalIgnoreCase))
                        return Known[k][1];
                }
            }

            return null;
        }

        /// <summary>
        /// Looks for anyone but us with a patch on the music manager.
        ///
        /// Harmony can only report the ids owners chose for themselves, so the name here is that id. It is enough
        /// for the player to recognise which mod is meant, and better than a message that says only that
        /// something else is in charge.
        /// </summary>
        private static string ByPatch()
        {
            for (int i = 0; i < Guarded.Length; i++)
            {
                MethodInfo method = AccessTools.Method(typeof(MusicManagerPlay), Guarded[i]);

                if (method == null)
                    continue;

                Patches info = Harmony.GetPatchInfo(method);

                if (info == null)
                    continue;

                // Prefixes are judged on their return type; a transpiler is a rewrite whatever it returns.
                // Postfixes are not asked at all, because a postfix cannot prevent anything.
                string owner = ForeignOwner(info.Prefixes, true) ?? ForeignOwner(info.Transpilers, false);

                if (owner != null)
                    return owner;
            }

            return null;
        }

        private static string ForeignOwner(IEnumerable<Patch> patches, bool canSkip)
        {
            if (patches == null)
                return null;

            foreach (Patch patch in patches)
            {
                if (patch == null || patch.owner.NullOrEmpty())
                    continue;

                if (patch.owner == UIOverhaulMod.HarmonyId)
                    continue;

                if (!CanTakeOver(patch, canSkip))
                    continue;

                return patch.owner;
            }

            return null;
        }

        /// <summary>
        /// Whether this patch could actually stop RimWorld's music from running, rather than decorate it.
        ///
        /// <b>Being on one of the guarded methods is not enough, and assuming it was misread a real mod.</b>
        /// Reported 2026-09-01 against Isekai RPG Leveling, which patches <c>MusicUpdate</c> twice: a prefix
        /// that keeps one of its own tracks from being faded out when the danger flag flips, and a postfix that
        /// applies its fade envelope and volume. Both return void, both return early unless the song playing is
        /// one of its own, and neither takes anything over. It was called a rival anyway and our player switched
        /// itself off, which is the opposite of what the player wanted from either mod.
        ///
        /// <b>The test is the shape of the patch, not the name of the method.</b> Only two things can stop the
        /// original running: a prefix that returns <c>bool</c>, and a transpiler, which rewrites the method
        /// outright. That is exactly what our own takeover is -- a <c>bool</c> prefix on <c>MusicUpdate</c> --
        /// so it is the honest definition of somebody doing what we do. A void prefix cannot skip anything and
        /// a postfix runs after the work is already done; both are adjusting music that is playing, which is a
        /// thing two mods can do at once.
        ///
        /// <b>The cost of being wrong is asymmetric,</b> which is why this is narrow rather than cautious. Two
        /// real players fighting over the audio source is loud and obvious and the player can switch one off.
        /// A false positive is silent: the feature is simply absent, and nothing tells them the mod they
        /// installed it for is the reason.
        /// </summary>
        private static bool CanTakeOver(Patch patch, bool canSkip)
        {
            if (!canSkip)
                return true;

            MethodInfo method = patch.PatchMethod;

            return method != null && method.ReturnType == typeof(bool);
        }
    }
}
