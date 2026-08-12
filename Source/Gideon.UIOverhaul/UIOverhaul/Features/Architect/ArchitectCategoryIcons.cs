using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Architect
{
    /// <summary>
    /// Icons for the architect's category list.
    ///
    /// DesignationCategoryDef carries no icon field, so there is nothing to read off the def. Two
    /// sources are tried instead: a texture at UI/ArchitectIcons/&lt;defName&gt; under any loaded mod's
    /// Textures folder, which is the path the community's architect icon packs already use, and failing
    /// that the icon of the first designator in the category, which every category has by definition.
    ///
    /// Misses are cached alongside hits. ContentFinder walks loaded mod content on a miss, which is far
    /// too slow to repeat for every category on every frame.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ArchitectCategoryIcons
    {
        /// <summary>
        /// Where an icon pack puts its art. Matches the existing convention rather than inventing one,
        /// so a player who already has a pack installed gets icons without doing anything.
        /// </summary>
        public const string IconFolder = "UI/ArchitectIcons/";

        private static readonly Dictionary<string, Texture> Cache = new Dictionary<string, Texture>();

        /// <summary>The icon to show for a category, or null when neither source produced one.</summary>
        public static Texture For(DesignationCategoryDef category)
        {
            if (category == null)
                return null;

            if (Cache.TryGetValue(category.defName, out Texture cached))
                return cached;

            Texture resolved = Resolve(category);
            Cache[category.defName] = resolved;
            return resolved;
        }

        /// <summary>
        /// Drops every cached lookup. For a def reload, after which the designator instances behind the
        /// fallback icons no longer exist.
        /// </summary>
        public static void Clear()
        {
            Cache.Clear();
        }

        private static Texture Resolve(DesignationCategoryDef category)
        {
            Texture2D authored = ContentFinder<Texture2D>.Get(IconFolder + category.defName, false);
            if (Usable(authored))
                return authored;

            Texture expansion = ExpansionIcon(category);
            if (expansion != null)
                return expansion;

            // AllResolvedDesignators rather than ResolvedAllowedDesignators: the allowed set shrinks and
            // grows with research, and a category's icon changing as the game progresses would be worse
            // than a slightly odd choice that stays put.
            foreach (Designator designator in category.AllResolvedDesignators)
            {
                if (designator != null && Usable(designator.icon))
                    return designator.icon;
            }

            return Fallback;
        }

        /// <summary>
        /// The placeholder for a category nothing else produced art for: corner brackets around a dot.
        ///
        /// Reached only when a category's every designator also lacks art, which is rare -- a category
        /// with no designators is not Visible in the first place -- so this is mostly for modded
        /// categories that ship none.
        ///
        /// Loaded in the static constructor rather than lazily on first draw. It was lazy, and RimWorld's
        /// startup check flagged it: a static Texture2D field has to be filled on the main thread, and a
        /// lazy load is only main-thread by luck of who reads it first. The attribute on this class is what
        /// makes that a guarantee, and it runs after content loading so ContentFinder is ready.
        /// </summary>
        private static readonly Texture2D Fallback;

        static ArchitectCategoryIcons()
        {
            Fallback = ContentFinder<Texture2D>.Get(IconFolder + "CategoryFallback", false);
        }

        /// <summary>
        /// The icon of the expansion a category came from -- the same art the main menu shows for that
        /// DLC. Official art beats anything drawn here for Anomaly, Biotech, Ideology, Odyssey and
        /// Royalty, and it carries the association a player already recognizes.
        ///
        /// The core expansion is skipped on purpose. Its icon is the RimWorld logo, and matching it would
        /// stamp that logo onto Structure, Furniture, Production and every other base-game category --
        /// sixteen identical rows, which is the outcome to avoid.
        /// </summary>
        private static Texture ExpansionIcon(DesignationCategoryDef category)
        {
            string packageId = category.modContentPack?.PackageId;
            if (packageId.NullOrEmpty())
                return null;

            foreach (ExpansionDef expansion in DefDatabase<ExpansionDef>.AllDefsListForReading)
            {
                if (expansion.isCore || expansion.linkedMod.NullOrEmpty())
                    continue;

                if (!string.Equals(expansion.linkedMod, packageId, StringComparison.OrdinalIgnoreCase))
                    continue;

                return Usable(expansion.Icon) ? expansion.Icon : null;
            }

            return null;
        }

        /// <summary>
        /// Whether a texture is real art rather than a placeholder.
        ///
        /// BadTex is the red-cross image RimWorld substitutes for missing art, and Command.icon holds it
        /// outright for a designator whose def shipped none -- so the fallback would happily hand back a
        /// red cross and the card would draw it. Nothing is better than a placeholder here: the category's
        /// name is beside it and carries the row on its own.
        /// </summary>
        private static bool Usable(Texture texture)
        {
            return texture != null && texture != BaseContent.BadTex;
        }
    }
}
