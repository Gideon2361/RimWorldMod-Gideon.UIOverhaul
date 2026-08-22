using Gideon.UIFramework.Helpers;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gideon.UIOverhaul.Features.Factions
{
    /// <summary>
    /// Puts a faction's standing on every world object that belongs to one.
    ///
    /// <b>The gap this closes.</b> <c>WorldObject.GetInspectString</c> prints "Faction: Ancient Enclave" and
    /// stops. <c>Settlement</c> overrides it to add the relation and the goodwill, so a settlement tells you where
    /// you stand; nothing else does. That leaves work sites, quest sites, peace talks, trade caravans and
    /// everything else a faction owns naming a faction whose standing you have to open the Factions tab to learn,
    /// which is exactly what backlog 31 objects to and what the reference mod fixes for work sites.
    ///
    /// <b>Patched on the base rather than on each subclass,</b> because the list of world objects with a faction
    /// is open ended: DLCs add to it, mods add to it, and a patch per class is a list that goes stale silently. A
    /// postfix on the base runs after whatever the subclass appended, which is also the right place in the text.
    ///
    /// <b>Settlements are skipped, since theirs is already there.</b> Detected by asking whether the object is a
    /// <c>Settlement</c> rather than by searching the string for a relation word, which would be a test that
    /// works in English and quietly stops working in every other language.
    ///
    /// <b>Only where vanilla named the faction.</b> An object that suppresses its own faction line through
    /// <c>AppendFactionToInspectString</c> is hiding who owns it, usually because the player is not supposed to
    /// know yet; adding the standing there would leak the answer to a question the game declined to ask.
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.GetInspectString))]
    internal static class Patch_FactionStandingOnWorldObjects
    {
        public static void Postfix(WorldObject __instance, ref string __result)
        {
            string original = __result;

            __result = UIGuard.Try("Factions.WorldObject", () => WithStanding(__instance, original), original,
                "Faction standing is not shown on world objects. Nothing else is affected.");
        }

        private static string WithStanding(WorldObject worldObject, string text)
        {
            if (worldObject == null || worldObject is Settlement)
                return text;

            if (worldObject.Faction == null || !worldObject.AppendFactionToInspectString)
                return text;

            string standing = FactionStanding.Line(worldObject.Faction);

            if (standing.NullOrEmpty())
                return text;

            return text.NullOrEmpty() ? standing : text + "\n" + standing;
        }
    }

    /// <summary>
    /// Puts the standing in the letter stack's hover text as well.
    ///
    /// <b>Opening the letter already tells you.</b> <c>ChoiceLetter.OpenLetter</c> hands its
    /// <c>relatedFaction</c> to <c>Dialog_NodeTreeWithFactionInfo</c>, which draws the same block the quest card
    /// does. The hover is the half that does not: it shows the letter's text and nothing about who sent it, which
    /// is the moment somebody is deciding whether the letter is worth opening at all.
    ///
    /// <b>Patched on <c>ChoiceLetter</c> because that is where the method exists.</b> <c>Letter</c> declares it
    /// abstract, and <c>ChoiceLetter</c> is the implementation nearly every letter in the game inherits.
    /// </summary>
    [HarmonyPatch(typeof(ChoiceLetter), "GetMouseoverText")]
    internal static class Patch_FactionStandingOnLetters
    {
        public static void Postfix(ChoiceLetter __instance, ref string __result)
        {
            string original = __result;

            __result = UIGuard.Try("Factions.LetterHover", () => WithStanding(__instance, original), original,
                "Faction standing is not shown on letter hover text. The letter itself is unaffected.");
        }

        private static string WithStanding(ChoiceLetter letter, string text)
        {
            if (letter?.relatedFaction == null)
                return text;

            string standing = FactionStanding.Line(letter.relatedFaction);

            if (standing.NullOrEmpty())
                return text;

            // Named as well as rated, because a letter's text does not always say which faction it is about, and
            // "Hostile (-80)" on its own is a number attached to nothing.
            string line = letter.relatedFaction.Name + ": " + standing;

            return text.NullOrEmpty() ? line : text + "\n\n" + line;
        }
    }
}
