using System.Collections.Generic;
using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Restyles the schedule assignment colors to suit the rest of the UI.
    ///
    /// <b>Why the defs and not our own lookup.</b> <c>TimeAssignmentDef.color</c> is a plain field on a def, and
    /// every schedule widget in the game reads it -- vanilla's schedule tab, the inspect pane's timetable strip,
    /// the assignment selector, and our own strip. Recoloring the defs restyles all of them from one place. A
    /// private table inside the pawns tab would have left the vanilla views on the old colors, which is exactly
    /// the split-personality look an overhaul is supposed to remove.
    ///
    /// <b>Mod-added assignments are left alone.</b> The whole database is walked, but only the assignments this
    /// mod has an opinion about are recolored; anything else keeps the color its author chose. An earlier version
    /// generated a harmonized hue for those too, which was the wrong call: a mod author picking a color for their
    /// own assignment has made a decision, and overriding it to suit our palette is a worse outcome than one
    /// swatch that does not match. The five below are vanilla's, where the "author" is the base game and
    /// restyling is the entire point of this mod.
    ///
    /// <b>StaticConstructorOnStartup, not a Harmony patch.</b> There is nothing to intercept -- this is a
    /// one-time rewrite of loaded data, and the attribute is the game's own hook for "run after defs exist".
    /// A patch would need a method to attach to and would run no earlier.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TimeAssignmentColors
    {
        /// <summary>
        /// The colors, by defName.
        ///
        /// Hand-picked rather than taken from palette roles, because these are not palette meanings: an
        /// assignment is a category, not a state, and mapping them onto Success/Warning/Danger would say things
        /// that are not true -- recreation is not a warning. What they do share with the palette is the family:
        /// mid saturation, similar lightness, and nothing close to the danger red, so a schedule strip never
        /// looks like it is reporting a problem.
        ///
        /// Anything is deliberately the odd one out. It means "no preference", so it is a dim neutral rather
        /// than a hue -- which also lets a mostly-unscheduled day read as mostly empty at a glance.
        /// </summary>
        private static readonly Dictionary<string, string> Colors = new Dictionary<string, string>
        {
            { "Anything", "#39434F" },   // dim slate: no preference
            { "Work", "#4A90D9" },       // the palette's information blue: the productive default
            { "Joy", "#D98C3F" },        // warm amber, clear of the danger red
            { "Sleep", "#5B4A99" },      // deep violet: night, and a sibling of the mood color
            { "Meditate", "#3FA39B" }    // teal: calm, and distinct from both the blue and the green
        };

        static TimeAssignmentColors()
        {
            UIGuard.Try("Pawns.RecolorSchedule", Apply,
                "Schedule assignments keep their vanilla colors.");
        }

        /// <summary>
        /// Rewrites every loaded assignment's color.
        ///
        /// Public so a def reload can call it again: reloading defs rebuilds every def instance from XML, which
        /// restores the vanilla colors and would otherwise leave the schedule looking vanilla until a restart.
        /// </summary>
        public static void Apply()
        {
            List<TimeAssignmentDef> defs = DefDatabase<TimeAssignmentDef>.AllDefsListForReading;

            if (defs == null)
                return;

            int restyled = 0;

            foreach (TimeAssignmentDef def in defs)
            {
                if (def?.defName == null)
                    continue;

                // No entry means someone else's assignment, and their color stands.
                if (!Colors.TryGetValue(def.defName, out string hex))
                    continue;

                if (!UIColorParser.TryParse(hex, out Color parsed, out string error))
                {
                    // Our own table, so a bad value here is a mistake in this file rather than anything the
                    // player did. Said out loud instead of silently leaving that one assignment vanilla.
                    Log.Error($"[Gideon.UIOverhaul] Schedule color for '{def.defName}' is unusable: {error}");
                    continue;
                }

                def.color = parsed;
                restyled++;

                // The cache behind ColorTexture has to be dropped, or every vanilla schedule widget keeps
                // drawing the old color: the property builds its texture once, on first read, and never
                // reconsiders. Setting color alone would have recolored our strip -- which reads the field --
                // and nothing else, which is a subtler bug than no recolor at all.
                InvalidateColorTexture(def);
            }

            UIDebug.Log($"Restyled {restyled} of {defs.Count} time assignments; the rest keep their "
                        + "authored colors.");
        }

        /// <summary>
        /// Drops the def's cached swatch texture so the getter rebuilds it from the new color.
        ///
        /// Reflection because <c>colorTextureInt</c> is not public. Resolved once into a static, so the cost is
        /// a field write per def rather than a lookup per def, and a null result is tolerated: if the field is
        /// ever renamed, the colors still change and only the cached swatches stay stale, which is a cosmetic
        /// fault in vanilla's views rather than a crash in ours.
        /// </summary>
        private static void InvalidateColorTexture(TimeAssignmentDef def)
        {
            CachedTextureField?.SetValue(def, null);
        }

        private static readonly System.Reflection.FieldInfo CachedTextureField =
            AccessTools.Field(typeof(TimeAssignmentDef), "colorTextureInt");
    }
}
