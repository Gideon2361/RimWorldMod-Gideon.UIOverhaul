using Gideon.UIFramework.Controls;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Matching a pawn against a search box, shared by every tab that lists colonists.
    ///
    /// <b>One copy because the reasoning below is not obvious.</b> The work tab learned it first, and a second
    /// tab written from scratch would have reached for <c>LabelNoCount</c> and reintroduced the same fault. Two
    /// name matchers in one mod would drift, and they would drift into the version that looks right.
    /// </summary>
    internal static class PawnSearch
    {
        /// <summary>
        /// Whether a pawn survives the search, matched on name alone.
        ///
        /// Read off <c>Name</c> rather than any of the label properties, because a pawn's label is not their
        /// name. <c>Pawn.LabelNoCount</c> is the name followed by the backstory title -- "Maxwell, Sailor" --
        /// and the title half is run through <c>Colorize</c>, so the string also carries <c>&lt;color=#...&gt;</c>
        /// markup. Filtering on it matched a colonist's profession, which is how searching "sa" turned up a
        /// sailor named Maxwell alongside Sam, and would equally have matched "col" against every titled pawn
        /// in the colony.
        ///
        /// First, nick and last are each tested separately rather than against the assembled full name, so a
        /// search cannot match across the join between two of them.
        /// </summary>
        internal static bool Matches(UITextBoxControl search, Pawn pawn)
        {
            if (search == null || search.IsEmpty)
                return true;

            if (pawn == null)
                return false;

            if (pawn.Name is NameTriple triple)
            {
                return search.Matches(triple.First)
                       || search.Matches(triple.Nick)
                       || search.Matches(triple.Last);
            }

            if (pawn.Name is NameSingle single)
                return search.Matches(single.Name);

            // A Name subclass from a mod, or no name at all. ToStringShort is the nearest thing to a bare
            // name that every Name is required to have; LabelShortCap covers a pawn with no Name, where it
            // falls through to the kind label rather than dereferencing null.
            return pawn.Name != null
                ? search.Matches(pawn.Name.ToStringShort)
                : search.Matches(pawn.LabelShortCap);
        }
    }
}
