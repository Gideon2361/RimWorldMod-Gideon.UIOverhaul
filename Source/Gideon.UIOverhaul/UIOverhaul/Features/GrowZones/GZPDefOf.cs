using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.GrowZones
{
    [DefOf]
    public static class GZPDefOf
    {
        /// <summary>
        /// Placeholder recipe backing every <see cref="Bill_Growing"/>. Bill requires a non-null
        /// RecipeDef -- Bill.ExposeData dereferences recipe.fixedIngredientFilter -- but nothing in
        /// the bill row UI reads it, so one shared def is enough for every plant.
        /// </summary>
        public static RecipeDef GZP_GrowPlant;

        public static BillRepeatModeDef GZP_NutritionBelow;
        public static BillRepeatModeDef GZP_PlantNutritionBelow;

        static GZPDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(GZPDefOf));
        }
    }
}
