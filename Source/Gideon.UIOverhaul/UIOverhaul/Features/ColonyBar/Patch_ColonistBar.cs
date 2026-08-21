using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// Whether the grouped bar is standing in for vanilla's this frame.
    ///
    /// One place to ask, because six patches have to agree: a frame where the drawing is replaced but the
    /// hit-testing is not would put the tiles in our layout and the clicks in vanilla's.
    /// </summary>
    internal static class BarReplacement
    {
        internal static bool Active
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                return settings != null && settings.showGroupedColonistBar;
            }
        }
    }

    /// <summary>
    /// Draws the grouped bar instead of vanilla's.
    ///
    /// <b>Returns what <c>UIGuard.Replaced</c> returns, unnegated.</b> False means we drew and vanilla's body must
    /// be skipped; true hands the method back. Getting this inverted is what stacked two bill tabs on top of each
    /// other in 14123, and the fix note on that is worth reading before touching this line.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBar_OnGUI
    {
        public static bool Prefix()
        {
            if (!BarReplacement.Active)
                return true;

            return UIGuard.Replaced("Bar.Replace", ColonistBarPanel.Draw,
                "The colonist bar could not be drawn this frame.");
        }
    }

    /// <summary>
    /// Answers "which pawn is under this point" from our layout rather than vanilla's.
    ///
    /// <b>Necessary, not tidying.</b> Vanilla's <c>Entries</c> getter recaches on demand, so its draw positions
    /// stay populated even though its drawing never runs, and they describe a bar that is not on the screen. The
    /// selector asks these methods to decide whether a click landed on the bar, so leaving them alone would mean
    /// clicks near the top of the screen selecting whichever pawn vanilla would have drawn there.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistOrCorpseAt))]
    public static class Patch_ColonistBar_ColonistOrCorpseAt
    {
        public static bool Prefix(Vector2 pos, ref Thing __result)
        {
            if (!BarReplacement.Active)
                return true;

            __result = UIGuard.Try("Bar.HitPoint", () => (Thing) ColonistBarPanel.At(pos), null, null);

            return false;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.AnyColonistOrCorpseAt))]
    public static class Patch_ColonistBar_AnyColonistOrCorpseAt
    {
        public static bool Prefix(Vector2 pos, ref bool __result)
        {
            if (!BarReplacement.Active)
                return true;

            __result = UIGuard.Try("Bar.HitAny", () => ColonistBarPanel.At(pos) != null, false, null);

            return false;
        }
    }

    /// <summary>
    /// Stops box-selection over the bar reporting vanilla's layout.
    ///
    /// <b>Empty rather than translated to our layout, and that is a stated gap.</b> Dragging a selection box across
    /// vanilla's bar selects the colonists it touches; that is not reproduced here, because a grouped bar makes
    /// "the pawns this box crossed" a much less obvious set than a single row did. Selecting a whole group is on the
    /// group's own menu, and shift-clicking tiles builds any other selection. Answering empty is what keeps the
    /// alternative -- silently selecting the wrong pawns -- off the table.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistsOrCorpsesInScreenRect))]
    public static class Patch_ColonistBar_InScreenRect
    {
        public static bool Prefix(ref List<Thing> __result)
        {
            if (!BarReplacement.Active)
                return true;

            __result = new List<Thing>();

            return false;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.MapColonistsOrCorpsesInScreenRect))]
    public static class Patch_ColonistBar_MapInScreenRect
    {
        public static bool Prefix(ref List<Thing> __result)
        {
            if (!BarReplacement.Active)
                return true;

            __result = new List<Thing>();

            return false;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.CaravanMembersInScreenRect))]
    public static class Patch_ColonistBar_CaravanMembersInScreenRect
    {
        public static bool Prefix(ref List<Pawn> __result)
        {
            if (!BarReplacement.Active)
                return true;

            __result = new List<Pawn>();

            return false;
        }
    }
}
