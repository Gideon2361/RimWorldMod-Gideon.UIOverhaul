using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Architect
{
    /// <summary>
    /// Allows every forbidden item on the map, in one click.
    ///
    /// <b>This is vanilla's own feature, promoted to a button.</b> <c>Designator_Unforbid</c> sets
    /// <c>hasDesignateAllFloatMenuOption</c>, so the base <c>Designator</c> already offers exactly this as a
    /// right-click option on the Allow tile. Almost nobody finds it: it is a right click on a tool whose left
    /// click does something else entirely, and there is no hint on the tile that a menu exists.
    ///
    /// <b>Nothing about what "allowed" means is decided here.</b> The rules come from a real
    /// <c>Designator_Unforbid</c> held below: it answers which things qualify and it does the unforbidding.
    /// Reimplementing either would mean this button and the Allow tile could disagree -- and they would, the
    /// first time a DLC or a mod changed what carries a forbiddable comp.
    ///
    /// <b>The label is deliberately not vanilla's.</b> Vanilla's own string for this is "Unforbid all items",
    /// which does not match the tile it sits beside: that one is called Allow. Using vanilla's key would buy
    /// free translation and would put two words for one concept next to each other in the same category.
    /// </summary>
    public class Designator_AllowAll : Designator
    {
        /// <summary>
        /// The real Allow designator, kept only to be asked questions.
        ///
        /// Never selected and never put on the cursor. It exists so that <see cref="Designator.CanDesignateThing"/>
        /// and <see cref="Designator.DesignateThing"/> are vanilla's implementations rather than copies.
        /// </summary>
        private readonly Designator_Unforbid rule = new Designator_Unforbid();

        public Designator_AllowAll()
        {
            defaultLabel = "Allow all";
            defaultDesc = "Allow every forbidden item on this map at once.\n\n"
                          + "The same as dragging the Allow tool over the whole map, without the dragging. "
                          + "Items are the only thing affected: doors, buildings and anything else you have "
                          + "forbidden by hand are left as they are.";

            // The Allow tile's own texture, so the pair reads as one idea. Asked for by path rather than
            // copied into this mod's textures, which would leave us with a stale icon the first time RimWorld
            // redraws its own.
            icon = ContentFinder<Texture2D>.Get("UI/Designators/ForbidOff");

            soundSucceeded = SoundDefOf.Checkbox_TurnedOn;

            // This never goes on the cursor: it acts the moment it is clicked, so there is no drag to draw and
            // no mouse attachment to show.
            useMouseIcon = false;
        }

        /// <summary>
        /// Never. This designator does its work on click and is never selected, so no cell can be handed to it.
        /// </summary>
        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            return false;
        }

        /// <summary>
        /// Runs the sweep instead of becoming the selected tool.
        ///
        /// <b>Deliberately does not call base.</b> <c>Designator.ProcessInput</c> selects the designator, which
        /// is exactly what should not happen: there is nothing to then click on. Both architects reach a
        /// designator through this method -- vanilla's gizmo and this mod's own grid -- so overriding it is
        /// what makes the button behave the same in either.
        /// </summary>
        public override void ProcessInput(Event ev)
        {
            UIGuard.Try("Architect.AllowAll", Sweep,
                "Nothing was changed. Items can still be allowed with the Allow tool.");
        }

        private void Sweep()
        {
            Map map = Find.CurrentMap;

            if (map == null)
                return;

            // Copied before iterating rather than walked live. Vanilla's version closes over the lister's own
            // list and iterates that, which is safe for vanilla's comp -- but unforbidding raises
            // notifications, and a modded comp reacting to one by spawning or destroying something would be
            // mutating the list mid-loop. A snapshot costs one allocation per click.
            List<Thing> things = new List<Thing>(map.listerThings.AllThings);
            int allowed = 0;

            foreach (Thing thing in things)
            {
                // Fogged things are excluded because vanilla excludes them: what is under unrevealed fog is
                // not something the colony knows about yet, and allowing it would be reaching into terrain
                // the player has not explored.
                if (thing == null || thing.Destroyed || thing.Fogged())
                    continue;

                if (!rule.CanDesignateThing(thing).Accepted)
                    continue;

                rule.DesignateThing(thing);
                allowed++;
            }

            Report(allowed);
        }

        /// <summary>
        /// Says what happened, because a button that sweeps the whole map otherwise looks like it did nothing.
        ///
        /// The zero case is worth a message of its own rather than silence: "nothing was forbidden" and "the
        /// button is broken" look identical without it.
        /// </summary>
        private void Report(int allowed)
        {
            if (allowed == 0)
            {
                Messages.Message("Nothing on this map was forbidden.", MessageTypeDefOf.RejectInput, false);

                return;
            }

            if (soundSucceeded != null)
                soundSucceeded.PlayOneShotOnCamera();

            Messages.Message(
                allowed == 1 ? "Allowed 1 item." : "Allowed " + allowed + " items.",
                MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
