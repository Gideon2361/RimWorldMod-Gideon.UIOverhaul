using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Icons this mod supplies for the vanilla tabs that ship without one.
    ///
    /// A fallback resolved at draw time, not a stored setting. It is consulted only after the player's
    /// own choice for the bar entry and after the def's own <c>iconPath</c>, so a tab that already has
    /// art keeps it and a player who picked something else is never overridden.
    ///
    /// Deliberately not written into the saved layout. That file is meant to stay absent until the
    /// player changes something -- <see cref="UIButtonBarConfig.Resolve"/> builds the vanilla order when
    /// there is none -- so seeding it would create a config on first run and would need migrating every
    /// time this list grew or a path changed.
    ///
    /// Only vanilla defNames are mapped. Guessing at the defNames of mods that are not installed would
    /// be inventing a contract nobody agreed to; a modded tab gets its icon from the picker instead.
    /// </summary>
    public static class UIBarDefaultIcons
    {
        private const string Folder = UIBarIconSource.IconFolder + "/";

        private static readonly Dictionary<string, string> Paths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Inspect", Folder + "Inspect" },
                { "Architect", Folder + "Architect" },
                { "Work", Folder + "Work" },
                { "Schedule", Folder + "Schedule" },

                // The Assign tab is outfits, drug policies and food restrictions, so the policies
                // clipboard fits it better than anything named after assignment would.
                { "Assign", Folder + "Policies" },

                { "Animals", Folder + "Animals" },
                { "Wildlife", Folder + "Wildlife" },
                { "Research", Folder + "Research" },
                { "Quests", Folder + "Quests" },

                // A folded map rather than the globe, which is still in the folder and still offered by
                // the icon picker. A globe says "the planet"; the tab is the planet surface you travel
                // and settle, and at the size a bar button is drawn a sphere with continents on it turns
                // into a grey circle while a map keeps its shape.
                { "World", Folder + "Map" },

                { "Mechs", Folder + "Mechanoids" },

                // This mod's own tabs. The rule above is about not guessing at *other* mods' defNames --
                // these are ours, so there is no contract being invented, and a tab we ship should arrive
                // with the icon we drew for it rather than waiting for the player to find the picker.
                { "Gideon_Pawns", Folder + "Pawns" },
                { "GZP_GrowZones", Folder + "GrowZones" }
            };

        /// <summary>
        /// Resolved textures, including the misses. This runs once per button per frame, and a miss in
        /// ContentFinder walks loaded mod content rather than failing cheaply.
        /// </summary>
        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The icon this mod offers for a tab, or null when it does not have one.</summary>
        public static Texture2D For(string defName)
        {
            if (defName.NullOrEmpty())
                return null;

            if (Cache.TryGetValue(defName, out Texture2D cached))
                return cached;

            Texture2D resolved = null;
            if (Paths.TryGetValue(defName, out string path))
                resolved = ContentFinder<Texture2D>.Get(path, false);

            Cache[defName] = resolved;
            return resolved;
        }

        /// <summary>
        /// The icon a bar slot will show: the player's own choice, then the def's own art, then ours.
        ///
        /// The single definition of that order, called by both the bar and the editor's preview so the
        /// two cannot drift. Display mode is not considered here -- the bar suppresses icons in text-only
        /// mode, but the editor still has to show one so it can be changed.
        /// </summary>
        public static Texture2D Resolve(UIButtonBarEntry entry, MainButtonDef def)
        {
            if (entry != null && !entry.icon.NullOrEmpty())
            {
                Texture2D chosen = ContentFinder<Texture2D>.Get(entry.icon, false);
                if (chosen != null)
                    return chosen;
            }

            if (def == null)
                return null;

            // iconPath is tested rather than trusting Icon to be null without one, so a def that ships no
            // art never reaches the texture lookup at all.
            if (!def.iconPath.NullOrEmpty() && def.Icon != null)
                return def.Icon;

            return For(def.defName);
        }

        /// <summary>Drops the cache, for a def reload after which the old textures are gone.</summary>
        public static void Clear()
        {
            Cache.Clear();
        }
    }
}
