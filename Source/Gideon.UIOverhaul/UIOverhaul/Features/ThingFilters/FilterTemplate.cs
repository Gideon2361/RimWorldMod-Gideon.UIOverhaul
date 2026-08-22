using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.ThingFilters
{
    /// <summary>
    /// One saved filter: which things are allowed, which special filters are on, and the two ranges.
    ///
    /// <b>Asked for on 2026-08-22</b> for the storage tab, where the same filter gets rebuilt by hand every time a
    /// colony grows another shelf. Because our panel replaces every filter in the game, the same two buttons serve
    /// a bill's ingredients and a stockpile's contents, which is a bonus rather than the point.
    ///
    /// <b>Stored as def names rather than as a scribed <c>ThingFilter</c>.</b> Scribe belongs to a save file and
    /// this lives beside the mod's settings, outside any colony, which is what makes a template worth having: it
    /// is for the next base as much as this one. Def names also survive a mod being removed, since an unknown name
    /// is skipped on load rather than taking the file down with it.
    ///
    /// <b>What is allowed is stored, not what is disallowed.</b> A filter's own universe differs from place to
    /// place: a bill's ingredient filter is rooted where its recipe puts it, a shelf allows only storable things.
    /// Applying is therefore "disallow everything, then allow these where they are permitted here", which is the
    /// only reading that behaves the same in both.
    /// </summary>
    internal class FilterTemplate
    {
        internal string Name;

        /// <summary>What was being edited when this was saved, so the list can say where it came from.</summary>
        internal string Origin;

        internal string Saved;

        internal List<string> Defs = new List<string>();

        /// <summary>The special filters that were on: allow rotten, allow non-smoothed, and the rest.</summary>
        internal List<string> Specials = new List<string>();

        internal FloatRange HitPoints = FloatRange.ZeroToOne;

        internal QualityRange Quality = QualityRange.All;

        internal int Count => Defs.Count;

        /// <summary>
        /// Reads a filter into a template.
        ///
        /// The ranges are taken whether or not this filter offers them, because whether they can be edited is a
        /// property of where the filter lives rather than of the filter: a template saved from a stockpile that
        /// allows quality is still worth applying to one that does not, minus the part that does not apply.
        /// </summary>
        internal static FilterTemplate Capture(ThingFilter filter, string origin)
        {
            if (filter == null)
                return null;

            FilterTemplate template = new FilterTemplate
            {
                Origin = origin,
                Saved = DateTime.Now.ToString("yyyy-MM-dd"),
                HitPoints = filter.AllowedHitPointsPercents,
                Quality = filter.AllowedQualityLevels
            };

            foreach (ThingDef def in filter.AllowedThingDefs)
            {
                if (def != null)
                    template.Defs.Add(def.defName);
            }

            List<SpecialThingFilterDef> specials = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading;

            for (int i = 0; i < specials.Count; i++)
            {
                if (filter.Allows(specials[i]))
                    template.Specials.Add(specials[i].defName);
            }

            return template;
        }

        /// <summary>
        /// Writes this template into a filter.
        ///
        /// <b>Intersected with what the destination permits, in both directions.</b> A def this filter has never
        /// heard of is skipped, and a def the parent filter disallows is skipped too: applying a stockpile's
        /// template to a bill's ingredients must not offer the bill a material its recipe cannot take. The result
        /// is the template as far as it fits, which is the only useful answer.
        ///
        /// <b>The ranges are only written where they can be edited.</b> Those two flags live on the parent filter
        /// and are exactly the test our panel uses to decide whether to draw the sliders, so a template cannot set
        /// something the player is not allowed to see.
        /// </summary>
        internal void ApplyTo(ThingFilter filter, ThingFilter parent)
        {
            if (filter == null)
                return;

            UIGuard.Try("Filters.ApplyTemplate", () =>
            {
                filter.SetDisallowAll();

                for (int i = 0; i < Defs.Count; i++)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(Defs[i]);

                    if (def == null || (parent != null && !parent.Allows(def)))
                        continue;

                    filter.SetAllow(def, true);
                }

                List<SpecialThingFilterDef> specials = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading;

                for (int i = 0; i < specials.Count; i++)
                {
                    SpecialThingFilterDef special = specials[i];

                    // A filter that hides a special filter is saying it has nothing for it to apply to, so it is
                    // left alone rather than set either way.
                    if (filter.hiddenSpecialFilters != null && filter.hiddenSpecialFilters.Contains(special))
                        continue;

                    filter.SetAllow(special, Specials.Contains(special.defName));
                }

                if (parent == null || parent.allowedHitPointsConfigurable)
                    filter.AllowedHitPointsPercents = HitPoints;

                if (parent == null || parent.allowedQualitiesConfigurable)
                    filter.AllowedQualityLevels = Quality;
            }, "The template was not applied, so the filter is as it was.");
        }
    }
}
