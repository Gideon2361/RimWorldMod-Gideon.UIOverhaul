using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Factions;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Inspector
{
    /// <summary>
    /// The Social body: who this pawn cares about, who cares about them, and how that is going.
    ///
    /// <b>Sorted by the strength of the opinion rather than alphabetically,</b> which is the change that makes
    /// the block readable. A colonist knows twelve people and has an opinion worth acting on about three of them;
    /// an alphabetical list buries those three among the nine who are merely present.
    ///
    /// <b>The opinion is drawn as a signed bar out from zero.</b> A rival reads as a rival without anybody
    /// reading the number, which a bar filling from the left cannot do: it would make a minus fifty and a plus
    /// fifty look like the same amount of something.
    ///
    /// <b>The faction row is the same reading the world map now shows.</b> It comes from
    /// <see cref="FactionStanding"/>, which is backlog 31's work, so a guest's faction is worded and coloured the
    /// same here, on the planet, and in the letter that announced them.
    /// </summary>
    internal static class InspectSocialBody
    {
        /// <summary>How many relationships the block lists before it stops.</summary>
        private const int RelationsShown = 8;

        /// <summary>How many log lines the recent-interactions block shows.</summary>
        private const int InteractionsShown = 10;

        /// <summary>The opinion at which the bar is full, which is also RimWorld's own limit.</summary>
        private const float OpinionScale = 100f;

        /// <summary>
        /// Our own copy of the candidate list, reused between frames.
        ///
        /// <b>Copied rather than sorted in place, and that is not tidiness.</b> The list handed back by
        /// <c>SocialCardUtility.PawnsForSocialInfo</c> belongs to RimWorld's own cache, so reordering it would
        /// reorder the social tab as a side effect of drawing this one. Reused rather than allocated fresh
        /// because the pane redraws every frame.
        /// </summary>
        private static readonly List<Pawn> Sorted = new List<Pawn>();

        internal static float Draw(Rect view, Pawn pawn, UIColorPaletteDef palette)
        {
            if (pawn.relations == null)
                return 0f;

            Rect left;
            Rect right;

            InspectBodies.Columns(view, out left, out right);

            bool split = InspectBodies.Live(right);

            float leftY = Relations(left, view.y, pawn, palette);

            Rect second = split ? right : left;
            float secondY = split ? view.y : leftY;

            secondY = Faith(second, secondY, pawn, palette);
            secondY = Standing(second, secondY, pawn, palette);
            secondY = Interactions(second, secondY, pawn, palette);

            return (split ? Mathf.Max(leftY, secondY) : secondY) - view.y;
        }

        /// <summary>
        /// What this pawn believes and how firmly, when Ideology is loaded.
        ///
        /// <b>Certainty belongs on the social tab because losing it is a social event.</b> It moves on what a
        /// pawn sees other people do, and it is the number that says whether they are about to stop being one
        /// of yours. Vanilla keeps it on the Bio tab's ideoligion strip and in the ideoligions window, neither
        /// of which is open while you are reading somebody's relationships.
        ///
        /// <b>Absent rather than empty without the expansion.</b> <c>pawn.ideo</c> is null in an install with
        /// no Ideology, and the tracker is also null for a pawn who has no faith at all, so one test covers
        /// both and the block simply does not appear.
        ///
        /// <b>The thresholds are the ideoligions tab's own,</b> so a colonist called doubting here is called
        /// doubting there. Two screens disagreeing about the same pawn on the same tick is the fault this
        /// avoids, and it costs one reference to do it.
        /// </summary>
        private static float Faith(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            if (!ModsConfig.IdeologyActive)
                return y;

            Ideo ideo = UIGuard.Try("Inspector.Ideo", () => pawn.ideo?.Ideo, null, null);

            if (ideo == null)
                return y;

            float certainty = UIGuard.Try("Inspector.Certainty", () => pawn.ideo.Certainty, 0f, null);

            Color tint = certainty < Ideoligions.IdeoFacts.ConvertingBelow
                ? palette.Danger
                : certainty < Ideoligions.IdeoFacts.DoubtingBelow
                    ? palette.Warning
                    : certainty >= Ideoligions.IdeoFacts.DevoutFrom
                        ? palette.Success
                        : palette.Accent;

            string word = certainty < Ideoligions.IdeoFacts.ConvertingBelow
                ? "slipping"
                : certainty < Ideoligions.IdeoFacts.DoubtingBelow
                    ? "doubting"
                    : certainty >= Ideoligions.IdeoFacts.DevoutFrom
                        ? "devout"
                        : "settled";

            y = InspectPaneParts.Cap(view, y, "Faith", word, palette);

            // The faith's own colour, which is how it reads everywhere else in the game.
            y = InspectPaneParts.Fact(view, y, "Ideoligion", ideo.name,
                UIGuard.Try("Inspector.IdeoColor", () => ideo.TextColor, palette.TextPrimary, null), palette);

            y = InspectPaneParts.Need(view, y, "Certainty", InspectPaneParts.Percent(certainty), tint,
                certainty, tint, null, null, palette);

            // Read through a guard of its own: the drift is recomputed from the pawn's situational thoughts
            // and their role, and it is the one read here that walks somebody else's data.
            float drift = UIGuard.Try("Inspector.Drift", () => pawn.ideo.CertaintyChangePerDay, 0f, null);

            y = InspectPaneParts.Fact(view, y, "Per day",
                drift > 0.0005f
                    ? "+" + drift.ToStringPercent("0")
                    : drift < -0.0005f
                        ? drift.ToStringPercent("0")
                        : "steady",
                drift > 0.0005f
                    ? palette.Success
                    : drift < -0.0005f
                        ? palette.Danger
                        : palette.TextDisabled,
                palette);

            Precept_Role role = UIGuard.Try("Inspector.Role",
                () => ideo.GetRole(pawn), null, null);

            if (role != null)
            {
                y = InspectPaneParts.Fact(view, y, "Role", role.LabelCap, palette.Accent, palette);
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>
        /// The people this pawn has an opinion about, strongest first.
        ///
        /// <b>The candidate list is <c>SocialCardUtility.PawnsForSocialInfo</c>, RimWorld's own.</b> Working out
        /// who counts as related is a real piece of logic -- family, bonded animals, anybody on the map with a
        /// non-zero opinion either way, minus the ones the game is hiding -- and reimplementing it would be a
        /// second answer to a question the game has already answered.
        /// </summary>
        private static float Relations(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            List<Pawn> others = UIGuard.Try("Inspector.SocialPawns",
                () => SocialCardUtility.PawnsForSocialInfo(pawn), null,
                "The inspect pane cannot list this pawn's relationships.");

            y = InspectPaneParts.Cap(view, y, "Relationships",
                others == null || others.Count == 0 ? null : "strongest first", palette);

            if (others == null || others.Count == 0)
                return InspectPaneParts.Note(view, y, "Nobody has an opinion either way.", palette)
                       + InspectPaneParts.BlockGap;

            Sorted.Clear();
            Sorted.AddRange(others);
            Sorted.SortByDescending(other => Mathf.Abs(Opinion(pawn, other)));

            int shown = Mathf.Min(Sorted.Count, RelationsShown);

            for (int i = 0; i < shown; i++)
                y = Relation(view, y, pawn, Sorted[i], palette);

            if (Sorted.Count > shown)
                y = InspectPaneParts.Note(view, y,
                    (Sorted.Count - shown) + " more with weaker opinions.", palette) + InspectPaneParts.RowGap;

            Sorted.Clear();

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>One relationship: who, what they are, the number, and the bar that says which side of zero.</summary>
        private static float Relation(Rect view, float y, Pawn pawn, Pawn other, UIColorPaletteDef palette)
        {
            int opinion = Opinion(pawn, other);

            string relation = UIGuard.Try("Inspector.SocialRelation", () =>
            {
                PawnRelationDef def = pawn.GetMostImportantRelation(other);

                return def != null ? def.GetGenderSpecificLabelCap(other) : null;
            }, null, null);

            string label = relation.NullOrEmpty()
                ? other.LabelShortCap.ToString()
                : other.LabelShortCap + " - " + relation;

            float before = y;

            y = InspectPaneParts.Entry(view, y, label,
                (opinion >= 0 ? "+" : string.Empty) + opinion,
                opinion > 0 ? palette.Success : opinion < 0 ? palette.Danger : palette.TextDisabled, null,
                palette);

            Rect lane = new Rect(view.x, y - 2f, view.width, InspectPaneParts.TrackHeight);

            InspectPaneParts.SignedBar(lane, opinion / OpinionScale,
                opinion >= 0 ? palette.Success : palette.Danger, palette);

            y = lane.yMax + InspectPaneParts.RowGap;

            Rect row = new Rect(view.x, before, view.width, y - before);

            if (Mouse.IsOver(row))
                TooltipHandler.TipRegion(row, (TipSignal) Tip(pawn, other, opinion));

            // Clicking a name takes you to them, which is the one thing a relationship row is for and the thing
            // vanilla's social tab makes you go and find by hand.
            if (Widgets.ButtonInvisible(row))
                PawnCameraJump.Request(other);

            return y;
        }

        /// <summary>What one relationship says on hover: both opinions, since they are rarely the same.</summary>
        private static string Tip(Pawn pawn, Pawn other, int opinion)
        {
            return UIGuard.Try("Inspector.SocialTip", () =>
            {
                int back = other.relations != null ? other.relations.OpinionOf(pawn) : 0;

                return pawn.LabelShortCap + " thinks " + opinion + " of " + other.LabelShortCap + ".\n"
                       + other.LabelShortCap + " thinks " + back + " of " + pawn.LabelShortCap + ".";
            }, other.LabelShortCap, null);
        }

        private static int Opinion(Pawn pawn, Pawn other)
        {
            return UIGuard.Try("Inspector.Opinion", () => pawn.relations.OpinionOf(other), 0, null);
        }

        /// <summary>
        /// Whose side this pawn is on, for the ones who are not ours.
        ///
        /// Absent for a colonist, deliberately: a row saying the player's own faction is neutral towards its own
        /// colonist is noise, and the block exists for guests, prisoners and visitors.
        /// </summary>
        private static float Standing(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            Faction faction = pawn.Faction;

            if (faction == null || faction.IsPlayer)
                return y;

            string standing = UIGuard.Try("Inspector.FactionStanding",
                () => FactionStanding.Line(faction), null, null);

            if (standing.NullOrEmpty())
                return y;

            y = InspectPaneParts.Cap(view, y, "Faction", null, palette);

            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextSecondary;

                float height = Text.CalcHeight(faction.Name + ": " + standing, view.width);

                // The standing arrives already carrying a colour tag from FactionStanding, which is why it is
                // drawn rather than passed through Fact: a Fact would paint the whole line one colour and the
                // tag would fight it.
                Widgets.Label(new Rect(view.x, y, view.width, height), faction.Name + ": " + standing);

                y += height;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            return y + InspectPaneParts.BlockGap;
        }

        /// <summary>The social half of the log, which is what "how is this going" looks like as a list of events.</summary>
        private static float Interactions(Rect view, float y, Pawn pawn, UIColorPaletteDef palette)
        {
            y = InspectPaneParts.Cap(view, y, "Recent interactions", null, palette);

            return InspectLogBody.Stream(view, y, pawn, false, true, InteractionsShown, palette)
                   + InspectPaneParts.BlockGap;
        }
    }
}
