using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Shared
{
    /// <summary>
    /// What to call a map, for any panel that groups things by the map they are on.
    ///
    /// Shared rather than copied, because the pocket-map half below is the kind of logic that gets fixed once
    /// and then stays broken everywhere else. The work tab and the pawns tab both group by map and must agree
    /// on what a group is called.
    /// </summary>
    internal static class MapLabels
    {
        /// <summary>
        /// MapParent.LabelCap is what the world view shows when a tile is selected, which covers colonies by
        /// their given name and everything else -- caravan sites, ships -- by whatever that parent calls
        /// itself. Going through the parent rather than special-casing Settlement is what makes mod-added map
        /// kinds work without naming them here.
        ///
        /// Pocket maps are the exception and are handled first. A PocketMapParent's label comes from its def,
        /// so every one of them is called "Pocket map" no matter which entrance opened it -- useless when
        /// there is more than one, which is the normal case for the mods that add them.
        /// </summary>
        public static string NameOf(Map map)
        {
            if (map.IsPocketMap)
            {
                string entrance = EntranceLabel(map);
                if (!entrance.NullOrEmpty())
                    return entrance;
            }

            if (map.Parent != null && !map.Parent.LabelCap.NullOrEmpty())
                return map.Parent.LabelCap;

            return "Unknown location";
        }

        /// <summary>
        /// What opened a pocket map, named the way the player named it.
        ///
        /// Nothing here knows about any particular mod. A pocket map can only be generated through
        /// <c>MapPortal.GeneratePocketMap</c>, so every entrance -- vanilla pit gates and ancient hatches, and
        /// whatever a mod adds -- is a <c>MapPortal</c>, and the same lookup finds all of them. Referencing a
        /// mod's own types would have meant a hard dependency, a version to track, and no help at all for the
        /// next mod that adds pocket maps.
        ///
        /// The exit standing inside the pocket map is asked first, because <c>PocketMapExit.entrance</c> is a
        /// direct reference and costs nothing to follow. The fallback covers a pocket map generated without an
        /// exit, which the vanilla API permits, by asking the source map which of its portals leads back here.
        ///
        /// Both use the <c>MapPortal</c> ThingRequestGroup rather than walking every thing on the map: this is
        /// called while drawing, once per group per frame, and a group lookup is a cached list.
        ///
        /// <b>Renames need no detection.</b> The label is read every frame rather than cached anywhere, so
        /// renaming an entrance shows up on the next frame by construction. A cache would have needed an
        /// invalidation hook, and there is no vanilla notification for a rename to hang one on.
        /// </summary>
        private static string EntranceLabel(Map pocket)
        {
            MapPortal entrance = null;

            List<Thing> inside = pocket.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal);

            for (int i = 0; i < inside.Count; i++)
            {
                if (inside[i] is PocketMapExit exit && exit.entrance != null)
                {
                    entrance = exit.entrance;
                    break;
                }
            }

            if (entrance == null)
            {
                Map source = pocket.PocketMapParent?.sourceMap;

                if (source != null)
                {
                    List<Thing> outside = source.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal);

                    for (int i = 0; i < outside.Count; i++)
                    {
                        // The public property rather than the protected field, which also means the portal is
                        // only matched once its map actually exists -- PocketMap null-guards on HasMap.
                        if (outside[i] is MapPortal portal && portal.PocketMap == pocket)
                        {
                            entrance = portal;
                            break;
                        }
                    }
                }
            }

            if (entrance == null)
                return null;

            // A renamed building keeps its given name in RenamableLabel; Thing.LabelCap only reflects it if the
            // subclass happens to route LabelNoCount through it, which is not guaranteed. Asking the interface
            // first is what makes a renamed entrance actually show its new name.
            if (entrance is IRenameable renamable && !renamable.RenamableLabel.NullOrEmpty())
                return renamable.RenamableLabel.CapitalizeFirst();

            return entrance.LabelCap;
        }
    }
}
