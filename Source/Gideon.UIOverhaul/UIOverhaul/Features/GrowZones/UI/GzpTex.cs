using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones.UI
{
    /// <summary>
    /// UI textures. Sources live under Gideon.UIOverhaul/Textures/UIOverhaul/GrowZones; see THIRD-PARTY-NOTICES.txt in
    /// the mod root for the icons taken from Modern Ideology Menu (MIT).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class GzpTex
    {
        /// <summary>The tab own mark, shared with the button that opens it.</summary>
        public static readonly Texture2D Mark =
            ContentFinder<Texture2D>.Get("UI/MainButtonIcons/GrowZones", false);

        public static readonly Texture2D Close = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/Close");
        public static readonly Texture2D Beauty = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/Beauty_128");
        public static readonly Texture2D Skill = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/Skill_128");
        public static readonly Texture2D Light = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/Light_128");
        public static readonly Texture2D Lifespan = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/Lifespan_128");
        public static readonly Texture2D MinTemp = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/MinTemp_128");
        public static readonly Texture2D IdealTemp = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/IdealTemp_128");
        public static readonly Texture2D MaxTemp = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/MaxTemp_128");
        public static readonly Texture2D Hazard = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/Hazard_128");
        public static readonly Texture2D Healthy = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/Healthy_128");
        public static readonly Texture2D PossibleHazard = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/PossibleHazard_128");
        public static readonly Texture2D NoticeBackground = ContentFinder<Texture2D>.Get("UIOverhaul/GrowZones/BillCardBackgroundNotice");

        private static Texture growTime;
        private static Texture nutrition;

        /// <summary>A fine meal, standing in for nutrition. Lazy for the same reason as GrowTime.</summary>
        public static Texture Nutrition
        {
            get
            {
                if (nutrition == null)
                    nutrition = ThingDefOf.MealFine?.uiIcon ?? (Texture) BaseContent.BadTex;
                return nutrition;
            }
        }

        /// <summary>
        /// Healroot's sapling art, used as the grow-time icon. Resolved lazily rather than in the
        /// static constructor: plant graphics are built during the same startup phase that runs
        /// StaticConstructorOnStartup classes, so reading immatureGraphic too early can yield null
        /// and permanently cache the fallback.
        /// </summary>
        public static Texture GrowTime => growTime ?? (growTime = ResolveHealrootSapling());

        private static Texture ResolveHealrootSapling()
        {
            ThingDef healroot = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Healroot");
            if (healroot?.plant == null)
                return BaseContent.BadTex;

            Graphic immature = healroot.plant.immatureGraphic;
            if (immature?.MatSingle != null && immature.MatSingle.mainTexture != null)
                return immature.MatSingle.mainTexture;

            // No sapling stage available -- the mature plant icon still reads as "growing".
            return healroot.uiIcon != null ? healroot.uiIcon : (Texture) BaseContent.BadTex;
        }
    }
}
