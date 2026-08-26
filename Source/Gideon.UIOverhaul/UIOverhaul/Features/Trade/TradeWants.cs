using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Trade
{
    /// <summary>
    /// One standing want: a thing, and the number of it this colony always means to have.
    /// </summary>
    public class TradeWant : IExposable
    {
        public ThingDef thing;

        /// <summary>How many to keep. The shortfall against this is what gets pre-filled into a deal.</summary>
        public int keep = 1;

        /// <summary>The most anybody may ask to keep, so a typo cannot pre-fill a deal with six million steel.</summary>
        public const int Ceiling = 99999;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref thing, "thing");
            Scribe_Values.Look(ref keep, "keep", 1);
        }
    }

    /// <summary>
    /// The things this colony always wants, and the ones it wants to see first.
    ///
    /// <b>The problem this solves.</b> Every trade in vanilla starts from zero. The same player opens the same
    /// kind of ship and re-finds the same eight items -- medicine, components, chemfuel, whatever this colony is
    /// short of -- scrolling a flat list of two hundred rows to do it, every time, for the whole run. Nothing in
    /// the game remembers that a colony means to hold twenty glitterworld medicine.
    ///
    /// <b>Two mechanisms, deliberately different in weight.</b> A <i>favourite</i> is a bookmark: it floats the
    /// row to the top of its category and does nothing else. A <i>standing want</i> is an instruction: when the
    /// window opens, anything held below its number is pre-filled into the deal up to the shortfall. The first is
    /// for things you look at, the second for things you buy, and conflating them would mean either bookmarks
    /// that spend your silver or instructions you have to re-enter.
    ///
    /// <b>Pre-filled rows are marked as such.</b> A deal that had lines in it before the player touched anything
    /// is a deal they did not make, and the one thing that must never happen is somebody hitting accept on a
    /// purchase they never chose. The row carries a pill saying it was a standing want, and the reset button
    /// clears the deal to genuinely empty -- so the fill is a suggestion with a visible author and a one-click
    /// undo, rather than a default that hides.
    ///
    /// <b>Per save, on a GameComponent,</b> for the reasons already written up on <c>StudyAssignments</c> and
    /// <c>EditorSedation</c>: it needs no def, one is made for every game, and it scribes its own data. Defs are
    /// saved by name and resolve back to the same things; a mod removed between sessions leaves a null, which is
    /// swept on load rather than left to be dereferenced on the first draw.
    /// </summary>
    public class TradeWants : GameComponent
    {
        private List<TradeWant> wants = new List<TradeWant>();

        private List<string> favourites = new List<string>();

        /// <summary>Required by RimWorld: every GameComponent is constructed with the game it belongs to.</summary>
        public TradeWants(Game game)
        {
        }

        /// <summary>
        /// The component for the running game, or null outside one.
        ///
        /// Null is an ordinary answer rather than a fault: these windows can be reached from a caravan on the
        /// world map during a load, and every caller here treats a missing component as "no wants recorded".
        /// </summary>
        internal static TradeWants Current =>
            UIGuard.Try("Trade.WantsComponent",
                () => Verse.Current.Game?.GetComponent<TradeWants>(), null, null);

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref wants, "gideonTradeWants", LookMode.Deep);
            Scribe_Collections.Look(ref favourites, "gideonTradeFavourites", LookMode.Value);

            if (wants == null)
                wants = new List<TradeWant>();

            if (favourites == null)
                favourites = new List<string>();

            // A want whose def came back null belonged to a mod that is no longer installed. Swept here rather
            // than guarded at every use, because there is exactly one moment it can appear and a hundred places
            // that would otherwise have to remember it.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                wants.RemoveAll(want => want == null || want.thing == null || want.keep <= 0);
        }

        // -----------------------------------------------------------------------------------------------
        // Favourites
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Whether this thing floats to the top of its category.
        ///
        /// <b>Kept by def name rather than by def.</b> A favourite is a preference about a kind of thing and
        /// carries no data, so a name is the whole of it -- and a name survives a mod being removed and put back,
        /// where a scribed def reference would have been dropped in between.
        /// </summary>
        internal static bool IsFavourite(ThingDef def)
        {
            TradeWants component = Current;

            return def != null && component != null && component.favourites.Contains(def.defName);
        }

        internal static void ToggleFavourite(ThingDef def)
        {
            TradeWants component = Current;

            if (def == null || component == null)
                return;

            UIGuard.Try("Trade.ToggleFavourite", () =>
            {
                if (!component.favourites.Remove(def.defName))
                    component.favourites.Add(def.defName);
            }, "That favourite was not recorded.");
        }

        internal static int FavouriteCount
        {
            get
            {
                TradeWants component = Current;

                return component == null ? 0 : component.favourites.Count;
            }
        }

        // -----------------------------------------------------------------------------------------------
        // Standing wants
        // -----------------------------------------------------------------------------------------------

        /// <summary>Every standing want, in the order they were added. Never null.</summary>
        internal static List<TradeWant> All
        {
            get
            {
                TradeWants component = Current;

                return component == null ? new List<TradeWant>() : component.wants;
            }
        }

        internal static int WantCount
        {
            get { return All.Count; }
        }

        /// <summary>How many of this thing the colony means to hold, or zero when it is not a standing want.</summary>
        internal static int Wanted(ThingDef def)
        {
            if (def == null)
                return 0;

            List<TradeWant> all = All;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].thing == def)
                    return all[i].keep;
            }

            return 0;
        }

        /// <summary>
        /// Sets how many of this thing to keep. Zero or less drops the want entirely.
        ///
        /// One entry point for adding, editing and removing, because those are three names for writing a number
        /// and a want holding zero is a want that does nothing but take up a row.
        /// </summary>
        internal static void SetWanted(ThingDef def, int keep)
        {
            TradeWants component = Current;

            if (def == null || component == null)
                return;

            UIGuard.Try("Trade.SetWant", () =>
            {
                keep = Mathf.Clamp(keep, 0, TradeWant.Ceiling);

                for (int i = 0; i < component.wants.Count; i++)
                {
                    if (component.wants[i] == null || component.wants[i].thing != def)
                        continue;

                    if (keep <= 0)
                        component.wants.RemoveAt(i);
                    else
                        component.wants[i].keep = keep;

                    return;
                }

                if (keep > 0)
                    component.wants.Add(new TradeWant { thing = def, keep = keep });
            }, "That standing want was not recorded.");
        }

        /// <summary>
        /// Fills a fresh deal with the shortfall against every standing want, and reports which rows it touched.
        /// </summary>
        /// <returns>
        /// The tradeables it wrote a count into, so the table can mark them. An empty set rather than null when
        /// there is nothing to fill, so a caller never has to test before asking.
        /// </returns>
        /// <remarks>
        /// <b>Shortfall against what the colony holds, not against what is for sale.</b> A colony holding twelve
        /// of a wanted twenty is short eight, whether the trader has eighty or two -- and <c>AdjustTo</c> clamps
        /// the ask to what is actually there, so the second half needs no arithmetic here.
        ///
        /// <b>Nothing is spent that the colony cannot afford,</b> because nothing is spent at all: this writes
        /// counts into a deal that the player still has to accept, and the silver line moves with it in front of
        /// them. Refusing to pre-fill past the colony's funds would be second-guessing a player who may well be
        /// about to sell something.
        ///
        /// <b>Gift mode fills nothing.</b> There is no buying in a gift, so a standing want has nothing to say;
        /// pre-filling the colony's own goods as a present because it is short of them would be the opposite of
        /// what the number means.
        /// </remarks>
        internal static HashSet<Tradeable> Prefill(TradeDeal deal)
        {
            HashSet<Tradeable> touched = new HashSet<Tradeable>();

            if (deal == null || TradeSession.giftMode)
                return touched;

            UIGuard.Try("Trade.Prefill", () =>
            {
                List<TradeWant> all = All;

                if (all.Count == 0)
                    return;

                List<Tradeable> tradeables = deal.AllTradeables;

                for (int i = 0; tradeables != null && i < tradeables.Count; i++)
                {
                    Tradeable tradeable = tradeables[i];

                    if (tradeable == null || tradeable.IsCurrency || !tradeable.TraderWillTrade)
                        continue;

                    ThingDef def = tradeable.ThingDef;

                    if (def == null)
                        continue;

                    int keep = Wanted(def);

                    if (keep <= 0)
                        continue;

                    int shortfall = keep - tradeable.CountHeldBy(Transactor.Colony);

                    if (shortfall <= 0)
                        continue;

                    int ask = Mathf.Min(shortfall, tradeable.GetMaximumToTransfer());

                    if (ask <= 0 || !tradeable.CanAdjustTo(ask).Accepted)
                        continue;

                    tradeable.AdjustTo(ask);

                    touched.Add(tradeable);
                }
            }, "Standing wants were not pre-filled into this deal. Nothing else about it is affected.");

            return touched;
        }
    }
}
