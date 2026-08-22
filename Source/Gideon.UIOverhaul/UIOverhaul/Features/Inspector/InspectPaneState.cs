using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// Which of the rebuilt bodies the pane is showing.
    ///
    /// <b>Overview is ours and the other six are RimWorld's tabs rebuilt in place.</b> Each of the six has a
    /// vanilla <c>InspectTabBase</c> behind it in the tab strip; selecting one here means the pane draws our
    /// version and the vanilla ITab window is not opened. Anything not on this list, including every modded tab
    /// and the building tabs, still opens its own window exactly as it does today.
    /// </summary>
    internal enum InspectBody
    {
        Overview,
        Health,
        Gear,
        Social,
        Needs,
        Bio,
        Log
    }

    /// <summary>
    /// What the pane remembers between frames: which body is showing, where each one is scrolled to, and which
    /// way the log is filtered.
    ///
    /// <b>Static, because there is one inspect pane.</b> <c>MainTabWindow_Inspect</c> is a single instance owned
    /// by <c>MainButtonDefOf.Inspect</c> and the world map's pane is a second one we do not draw, so a field per
    /// pane object would be a dictionary with one entry in it.
    ///
    /// <b>Nothing here is saved.</b> Which tab was open when you quit is not a setting; the height is, because
    /// that is a decision about the screen rather than about a moment.
    /// </summary>
    internal static class InspectPaneState
    {
        /// <summary>Which filter chip the log is on.</summary>
        internal enum LogFilter
        {
            All,
            Combat,
            Social,
            Medical
        }

        private static InspectBody selected = InspectBody.Overview;

        /// <summary>The thing the current selection was made against, so switching subject can reset the tab.</summary>
        private static Thing lastSubject;

        internal static Vector2 Scroll;

        internal static LogFilter Log = LogFilter.All;

        /// <summary>
        /// Which body is showing.
        ///
        /// <b>Reading it clears it if a vanilla ITab is open,</b> because the two cannot both be the answer to
        /// "what is the pane showing". Opening the training tab puts a window over the pane, and the pane
        /// underneath goes back to the overview rather than staying on a body the player can no longer see the
        /// chip for.
        /// </summary>
        internal static InspectBody Selected
        {
            get { return selected; }
        }

        /// <summary>
        /// Chooses a body. Scroll goes back to the top, since the previous body's offset means nothing here.
        /// </summary>
        internal static void Select(InspectBody body)
        {
            if (selected == body)
                return;

            selected = body;
            Scroll = Vector2.zero;
        }

        /// <summary>
        /// Called once per draw with what the pane is looking at.
        ///
        /// <b>The tab survives a change of pawn and does not survive a change of kind.</b> Clicking through four
        /// colonists with the Health body open is one question asked four times, so the body stays. Clicking from
        /// a colonist to a wall is a different question, and leaving the pane on a body that wall has no chip for
        /// would strand it, so the selection goes back to the overview whenever the body it names is not on
        /// offer.
        /// </summary>
        internal static void Notify(Thing subject, bool bodyStillAvailable)
        {
            if (!ReferenceEquals(subject, lastSubject))
            {
                lastSubject = subject;
                Scroll = Vector2.zero;
            }

            if (!bodyStillAvailable && selected != InspectBody.Overview)
            {
                selected = InspectBody.Overview;
                Scroll = Vector2.zero;
            }
        }

        /// <summary>
        /// Forgets everything. For the pane being closed and for a new game, so a stale scroll offset does not
        /// arrive with the first thing clicked in the next colony.
        /// </summary>
        internal static void Reset()
        {
            selected = InspectBody.Overview;
            lastSubject = null;
            Scroll = Vector2.zero;
            Log = LogFilter.All;
        }

        /// <summary>
        /// The vanilla tab a body replaces, or null for the overview, which has none.
        ///
        /// <b>Matched on the exact type rather than on assignability.</b> A mod that subclasses
        /// <c>ITab_Pawn_Health</c> to add its own rows to it is saying it wants its content shown, and treating
        /// the subclass as ours would throw that away silently. An exact match means we take over RimWorld's own
        /// tab and leave everybody else's alone.
        /// </summary>
        internal static bool Replaces(InspectTabBase tab, out InspectBody body)
        {
            body = InspectBody.Overview;

            if (tab == null)
                return false;

            System.Type type = tab.GetType();

            if (type == typeof(ITab_Pawn_Health))
                body = InspectBody.Health;
            else if (type == typeof(ITab_Pawn_Gear))
                body = InspectBody.Gear;
            else if (type == typeof(ITab_Pawn_Social))
                body = InspectBody.Social;
            else if (type == typeof(ITab_Pawn_Needs))
                body = InspectBody.Needs;
            else if (type == typeof(ITab_Pawn_Character))
                body = InspectBody.Bio;
            else if (type == typeof(ITab_Pawn_Log))
                body = InspectBody.Log;
            else
                return false;

            return true;
        }

        /// <summary>What a body's chip says.</summary>
        internal static string LabelOf(InspectBody body)
        {
            switch (body)
            {
                case InspectBody.Health:
                    return "TabHealth".Translate();

                case InspectBody.Gear:
                    return "TabGear".Translate();

                case InspectBody.Social:
                    return "TabSocial".Translate();

                case InspectBody.Needs:
                    return "TabNeeds".Translate();

                case InspectBody.Bio:
                    return "TabCharacter".Translate();

                case InspectBody.Log:
                    return "TabLog".Translate();

                default:
                    return "Overview";
            }
        }
    }
}
