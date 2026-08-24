using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// Whether this mod draws the research tab.
    ///
    /// <b>Asked in one place because three patches ask it,</b> and because a setting read from three call sites is
    /// a setting that ends up half applied: the contents replaced but the window sized for vanilla's tree.
    /// </summary>
    internal static class ResearchTabFeature
    {
        internal static bool Enabled
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings == null || settings.researchTab;
            }
        }
    }

    /// <summary>
    /// Hands the research tab over to <see cref="ResearchPanel"/>.
    ///
    /// <b>The vanilla window is kept and its contents replaced, rather than a tab of our own being added.</b>
    /// Research already has a button on the bar, an icon, a place in the order and a worker that draws progress on
    /// it, and everything in the game that opens the research screen -- the completion dialog's "Research screen"
    /// option, the architect tab's locked-building hint, the tutorial -- goes through
    /// <c>MainButtonDefOf.Research</c>. Redirecting all of that to a second def would mean chasing every caller;
    /// replacing the contents means none of them notice.
    ///
    /// <b>The prefix returns false whether or not our drawing worked.</b> Per the rule this mod has had since
    /// 2026-08-17: a window we replace never quietly hands back to RimWorld's, because a silent swap hides the
    /// defect that caused it. <see cref="UIGuardedPanel"/> shows a failure notice in our own window instead.
    ///
    /// <b><c>CurTab</c> is left alone and still works.</b> <c>FinishProject</c> sets it when the player takes the
    /// "Research screen" option out of the completion dialog. We do not read it -- there are no tabs -- and
    /// writing to it costs nothing.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.DoWindowContents))]
    internal static class Patch_MainTabWindow_Research_DoWindowContents
    {
        public static bool Prefix(Rect inRect)
        {
            if (!ResearchTabFeature.Enabled)
                return true;

            UIGuardedPanel.Draw("Research.Tab", inRect, () =>
            {
                Widgets.DrawBoxSolid(inRect, UIColorPaletteDef.Active.WindowBackground);

                ResearchPanel.Draw(inRect);
            }, "The research tab shows a failure notice. Nothing about your colony's research has changed: "
               + "whatever it is working on carries on, and the queue is untouched.");

            return false;
        }
    }

    /// <summary>
    /// Sizes the window for our layout instead of for vanilla's tree.
    ///
    /// Patched on the derived override rather than on <c>MainTabWindow.RequestedTabSize</c>, because the research
    /// window overrides <c>InitialSize</c> itself and measures every tab's authored coordinates to do it -- work
    /// whose answer we do not use and whose size we would then be overridden by.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), "get_InitialSize")]
    internal static class Patch_MainTabWindow_Research_InitialSize
    {
        public static void Postfix(ref Vector2 __result)
        {
            if (!ResearchTabFeature.Enabled)
                return;

            __result = UIGuard.Try("Research.TabSize",
                () => new Vector2(ResearchPanel.WindowWidth, ResearchPanel.WindowHeight), __result, null);
        }
    }

    /// <summary>
    /// Takes the window's margin away, so the panel insets itself the way this mod's other tabs do.
    ///
    /// <b>Patched on the base class with an instance test, because the research window does not override
    /// it.</b> Every tab this mod owns sets <c>Margin</c> to zero and lays out its own edges; this one is
    /// vanilla's window with our contents in it, and eighteen pixels of RimWorld's own background around a panel
    /// that paints its own is a frame nothing else in the mod has.
    ///
    /// The test is on the exact window type rather than on assignability, so a mod subclassing the research
    /// window to add its own drawing keeps the margin its layout was written against.
    /// </summary>
    [HarmonyPatch(typeof(Window), "get_Margin")]
    internal static class Patch_Window_Margin_Research
    {
        public static void Postfix(Window __instance, ref float __result)
        {
            if (__instance != null && __instance.GetType() == typeof(MainTabWindow_Research)
                                   && ResearchTabFeature.Enabled)
                __result = 0f;
        }
    }

    /// <summary>
    /// Builds the masks and drops the cached measurements when the tab opens.
    ///
    /// A postfix rather than a prefix: vanilla's own <c>PreOpen</c> resets its search widget and its caches, none
    /// of which we use but all of which are cheap, and letting it run keeps the window in a sane state for the
    /// frame where this mod's setting is switched off mid-session.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.PreOpen))]
    internal static class Patch_MainTabWindow_Research_PreOpen
    {
        public static void Postfix()
        {
            if (!ResearchTabFeature.Enabled)
                return;

            UIGuard.Try("Research.PreOpen", ResearchPanel.Notify_Opened, null);
        }
    }

    /// <summary>
    /// What the Research button on the bar shows: how far along, in what colour, with how many queued.
    ///
    /// <b>A widget rather than a patch,</b> because this mod's own button bar renderer already draws these
    /// buttons and already carries badges and a progress fill. All that was missing was a reason for the fill to
    /// be a colour other than one.
    ///
    /// <b>Three colours, and each says something the bar could not say before.</b> Accent for normal progress;
    /// the mood colour when the only thing running is an Anomaly project, since that is progress the research
    /// bench has nothing to do with; and the warning colour when the colony has a project but nobody who will
    /// work on it, which is the failure this bar exists to catch -- a research bar that has not moved in a season
    /// looks exactly like one that has.
    /// </summary>
    internal static class ResearchTabButton
    {
        /// <summary>How long an answer about the colony's researchers is trusted, in frames.</summary>
        private const int Frames = 60;

        private static bool stalled;

        private static int stamped = -1;

        /// <summary>Whether this def is the research button, which is the only one any of this applies to.</summary>
        internal static bool Is(MainButtonDef def)
        {
            return def != null && def == MainButtonDefOf.Research;
        }

        /// <summary>The queue length, for the badge. Zero draws nothing.</summary>
        internal static int Queued
        {
            get
            {
                GameComponent_ResearchQueue queue = GameComponent_ResearchQueue.Current;

                return queue == null ? 0 : queue.Count;
            }
        }

        /// <summary>
        /// The colour the progress fill takes, or null to leave the renderer's own.
        ///
        /// Null rather than the accent when nothing is running: the renderer draws no bar at zero percent anyway,
        /// and returning a colour for a bar that is not there is how a caller ends up drawing one.
        /// </summary>
        internal static Color? BarColor(MainButtonDef def, UIColorPaletteDef palette)
        {
            if (!Is(def) || !ResearchTabFeature.Enabled)
                return null;

            return UIGuard.Try<Color?>("Research.BarColor", () =>
            {
                ResearchManager manager = Find.ResearchManager;

                if (manager == null)
                    return null;

                if (manager.GetProject() == null)
                    return AnyKnowledgeProject(manager) ? palette.Mood : (Color?) null;

                return Stalled() ? palette.Warning : palette.Accent;
            }, null, null);
        }

        /// <summary>
        /// How full the bar is.
        ///
        /// Vanilla's worker answers this already and answers it well for the main project; what it does not do is
        /// notice that an Anomaly project is the only thing running, which is a colony making progress and a bar
        /// reading zero.
        /// </summary>
        internal static float Percent(MainButtonDef def, float vanilla)
        {
            if (!Is(def) || !ResearchTabFeature.Enabled || vanilla > 0f)
                return vanilla;

            return UIGuard.Try("Research.BarPercent", () =>
            {
                ResearchManager manager = Find.ResearchManager;

                if (manager == null || manager.GetProject() != null || !ModsConfig.AnomalyActive)
                    return vanilla;

                var lanes = manager.CurrentAnomalyKnowledgeProjects;

                if (lanes == null)
                    return vanilla;

                float best = 0f;

                for (int i = 0; i < lanes.Count; i++)
                {
                    if (lanes[i]?.project != null)
                        best = Mathf.Max(best, lanes[i].project.ProgressPercent);
                }

                return best;
            }, vanilla, null);
        }

        private static bool AnyKnowledgeProject(ResearchManager manager)
        {
            if (!ModsConfig.AnomalyActive)
                return false;

            var lanes = manager.CurrentAnomalyKnowledgeProjects;

            if (lanes == null)
                return false;

            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i]?.project != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the colony has a project and nobody who will work on it.
        ///
        /// The rate already answers this: it counts the colonists allowed to research, at their own speed, and
        /// comes out at zero when there are none. Asking it here rather than walking the maps again means one
        /// definition of "nobody is researching" instead of two that can disagree.
        /// </summary>
        private static bool Stalled()
        {
            if (Time.frameCount - stamped <= Frames)
                return stalled;

            stamped = Time.frameCount;
            stalled = ResearchRate.PointsPerTick <= 0f;

            return stalled;
        }
    }
}
