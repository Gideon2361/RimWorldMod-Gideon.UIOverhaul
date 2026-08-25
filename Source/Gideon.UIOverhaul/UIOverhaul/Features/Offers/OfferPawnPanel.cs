using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Inspector;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Offers
{
    /// <summary>
    /// Draws what somebody needs to know about a pawn they are being offered: who they are, what they are good
    /// at, what they are like, and what they will refuse to do.
    ///
    /// <b>Built from the inspect pane's own blocks.</b> The traits and skills here are the same two methods the
    /// inspect pane's Overview draws, called rather than reimplemented, so a colonist read on this panel and the
    /// same colonist read after they join cannot disagree. The one block that is local is the work capabilities,
    /// which the Overview has no reason to carry: for a colonist the Work tab answers it, and a pawn who has not
    /// joined has no row on the Work tab.
    ///
    /// <b>Every pawn is drawn, one under another, rather than one at a time behind a picker.</b> The letter that
    /// offers several is the letter that exists to be compared, and a picker turns a comparison into a memory
    /// test. Two or three pawns is what these letters carry.
    /// </summary>
    internal static class OfferPawnPanel
    {
        /// <summary>How wide the panel is, and therefore how much the dialog grows by.</summary>
        internal const float Width = 330f;

        /// <summary>Space between the dialog's own text and this panel.</summary>
        internal const float Gap = 12f;

        private static readonly List<WorkTypeDef> Refused = new List<WorkTypeDef>();

        /// <summary>Draws every offered pawn down the column and answers with the height used.</summary>
        internal static float Draw(Rect view, List<Pawn> pawns, UIColorPaletteDef palette)
        {
            float y = view.y;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn == null)
                    continue;

                y = One(view, y, pawn, palette);
            }

            return y - view.y;
        }

        private static float One(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            y = InspectPaneParts.Cap(view, y,
                UIGuard.Try("Offers.Name", () => pawn.LabelShortCap.ToString(), "?", null),
                Identity(pawn), palette);

            string title = UIGuard.Try("Offers.Title",
                () => pawn.story?.TitleCap, null, null);

            if (!title.NullOrEmpty())
                y = InspectPaneParts.Fact(view, y, "Role", title, palette.TextSecondary, palette);

            y = InspectOverview.Skills(view, y, pawn, palette);
            y = InspectOverview.Traits(view, y, pawn, palette);
            y = Capabilities(view, y, pawn, palette);

            return y;
        }

        /// <summary>Age and gender on one line, which is the rest of the answer to "who is this".</summary>
        private static string Identity(Pawn pawn)
        {
            return UIGuard.Try("Offers.Identity", () =>
            {
                string age = pawn.ageTracker == null
                    ? null
                    : pawn.ageTracker.AgeBiologicalYears.ToString() + "y";

                string gender = pawn.gender == Gender.None ? null : pawn.gender.GetLabel(pawn.AnimalOrWildMan());

                if (age == null)
                    return gender;

                return gender == null ? age : age + " " + gender;
            }, null, null);
        }

        /// <summary>
        /// The kinds of work this pawn will never do.
        ///
        /// <b>Stated as a refusal rather than as a list of what they can do,</b> because the refusals are the
        /// short list and they are what changes the decision. A colonist who cannot do Intellectual work is a
        /// different proposition from one who can, and the other twelve work types being open is the ordinary
        /// case that needs no words.
        ///
        /// <b>Invisible work types are left out</b> for the same reason they are left out of the idle alert: a
        /// work type that never appears on the Work tab is not something the player can act on, so naming it
        /// here would describe a restriction they cannot see anywhere else.
        /// </summary>
        private static float Capabilities(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            Refused.Clear();

            bool known = UIGuard.Try("Offers.Work", () =>
            {
                List<WorkTypeDef> disabled = pawn.GetDisabledWorkTypes();

                if (disabled == null)
                    return false;

                for (int i = 0; i < disabled.Count; i++)
                {
                    WorkTypeDef type = disabled[i];

                    if (type != null && type.visible)
                        Refused.Add(type);
                }

                return true;
            }, false, null);

            if (!known)
                return y;

            y = InspectPaneParts.Cap(view, y, "Will not do",
                Refused.Count == 0 ? "nothing" : Refused.Count.ToString(), palette);

            if (Refused.Count == 0)
                return InspectPaneParts.Note(view, y, "Every kind of work is open to them.", palette)
                       + InspectPaneParts.BlockGap;

            float x = view.x;
            float rowHeight = 0f;

            for (int i = 0; i < Refused.Count; i++)
            {
                InspectPaneParts.Chip(view, ref x, ref y, ref rowHeight,
                    UIGuard.Try("Offers.WorkLabel", () => Refused[i].labelShort.NullOrEmpty()
                        ? Refused[i].LabelCap.ToString()
                        : Refused[i].labelShort.CapitalizeFirst(), "?", null),
                    palette.Danger, false, palette);
            }

            return y + rowHeight + InspectPaneParts.BlockGap;
        }
    }
}
