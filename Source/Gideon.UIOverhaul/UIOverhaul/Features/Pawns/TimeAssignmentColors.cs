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
    /// It also means mod-added assignments are covered without knowing anything about the mod. Nothing here
    /// enumerates a fixed five: the whole database is walked, the ones we have opinions about are matched by
    /// defName, and anything else is given a color generated to fit the same family. A mod that adds "Study" or
    /// "Socialize" gets a color that belongs beside ours rather than sitting in whatever hue it shipped with.
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

        /// <summary>
        /// Saturation and value every generated color shares with the hand-picked ones, so a mod's assignment
        /// lands in the same family instead of beside it.
        /// </summary>
        private const float GeneratedSaturation = 0.55f;

        private const float GeneratedValue = 0.72f;

        static TimeAssignmentColors()
        {
            Apply();
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

            foreach (TimeAssignmentDef def in defs)
            {
                if (def?.defName == null)
                    continue;

                Color color = Colors.TryGetValue(def.defName, out string hex)
                              && UIColorParser.TryParse(hex, out Color parsed, out _)
                    ? parsed
                    : Generated(def.defName);

                def.color = color;

                // The cache behind ColorTexture has to be dropped, or every vanilla schedule widget keeps
                // drawing the old color: the property builds its texture once, on first read, and never
                // reconsiders. Setting color alone would have recolored our strip -- which reads the field --
                // and nothing else, which is a subtler bug than no recolor at all.
                InvalidateColorTexture(def);
            }

            UIDebug.Log($"Restyled {defs.Count} time assignment colors.");
        }

        /// <summary>
        /// A color for an assignment we have no opinion about.
        ///
        /// The hue comes from the defName's stable hash, so a given mod's assignment is the same color every
        /// session -- a color that shuffled between launches would make the schedule unreadable as a habit.
        /// Saturation and value are fixed, which is what keeps it in the family however the hue lands.
        ///
        /// The hash is folded into 0-1 by absolute value and modulus rather than by a cast, because
        /// StableStringHash is signed and a negative hue silently clamps to red -- next to the danger color,
        /// which is the one hue this should never accidentally pick.
        /// </summary>
        private static Color Generated(string defName)
        {
            int hash = GenText.StableStringHash(defName);
            float hue = Mathf.Abs(hash % 360) / 360f;

            return Color.HSVToRGB(hue, GeneratedSaturation, GeneratedValue);
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
