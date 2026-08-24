using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.WorldSites
{
    /// <summary>
    /// One kind of leftover marker the player can put a clock on.
    ///
    /// <b>Named by def rather than by class.</b> Three of the four share the plain <c>WorldObject</c> class and
    /// two of them come from Odyssey, so the def name is the only thing that identifies a kind on every install.
    /// A def that is not in the database -- Odyssey absent -- means the row is not drawn, which is the rule the
    /// rest of this mod's options follow for expansion content.
    ///
    /// <b>The reader and the writer are delegates.</b> Four settings fields differing only in which one a row
    /// writes is exactly the shape that ends up saving the wrong one, and the options window already learned that
    /// lesson with its checkboxes.
    /// </summary>
    internal class SiteFadeKind
    {
        internal string DefName;

        internal string Label;

        internal string Tooltip;

        /// <summary>
        /// Days this kind starts on for a player who never opens the section. Thirty for all four, on Aaron's
        /// instruction of 2026-08-23: "set the default to 30 days for all that don't clean up already".
        /// </summary>
        internal int Default;

        /// <summary>
        /// What RimWorld does with this kind if nobody interferes: 0 for keep it forever, or the clock it starts
        /// itself.
        ///
        /// <b>Separate from <see cref="Default"/> and the distinction earns its keep.</b> This is the value that
        /// means "there is nothing for us to do here", which is not the same as the value a fresh install starts
        /// on now that three of the four defaults are an instruction rather than a shrug. <c>SiteFade.Asked</c>
        /// compares against this one, so the abandoned camp is left entirely alone while it sits on thirty and
        /// the other three are clocked from the first sweep.
        /// </summary>
        internal int Vanilla;

        internal Func<UIOverhaulSettingsFile, int> Read;

        internal Action<UIOverhaulSettingsFile, int> Write;
    }

    /// <summary>
    /// The four kinds, and the lifespans a player may pick.
    ///
    /// <b>What accumulates and what does not was read out of the game rather than guessed,</b> and the guess would
    /// have been wrong. A destroyed enemy settlement looks like the biggest offender on a long game and is not one
    /// at all: <c>DestroyedSettlement.ShouldRemoveMapNow</c> hands back <c>alsoRemoveWorldObject</c> as true, so
    /// that marker leaves with its map and there is nothing to expire. The ones that stay forever are the
    /// player's own leavings -- an abandoned colony, a gravship launch site, and an abandoned camp that happened
    /// to be pitched on a landmark.
    ///
    /// <b>The abandoned camp is the one RimWorld already handles,</b> at thirty days, started in
    /// <c>Camp.Notify_MyMapRemoved</c>. The same method is why a camp on a landmark is forever: that branch makes
    /// an <c>AbandonedLandmark</c> instead and starts no clock on it at all.
    ///
    /// <b>So thirty days is the default for all four,</b> asked for on 2026-08-23 in those terms: the game's own
    /// figure for the one case it handles, applied to the three it does not. The camp is therefore the only kind
    /// whose default changes nothing, and the other three start clearing up without being asked -- including on a
    /// save that has been accumulating markers for years, which will lose the old ones within an hour of loading.
    /// That is the point of the setting rather than a side effect of it, and Keep is how it is turned off.
    /// </summary>
    internal static class SiteFadeKinds
    {
        /// <summary>The lifespans offered, in days. Zero keeps the marker forever.</summary>
        internal static readonly int[] Choices = { 0, 15, 30, 60, 120 };

        internal static readonly SiteFadeKind AbandonedSettlement = new SiteFadeKind
        {
            DefName = "AbandonedSettlement",
            Label = "Abandoned colonies",
            Default = 30,
            Vanilla = 0,
            Read = settings => settings.siteFadeSettlementDays,
            Write = (settings, days) => settings.siteFadeSettlementDays = days,
            Tooltip = "The marker left where you abandoned one of your own colonies. RimWorld keeps it for the "
                + "rest of the game.\n\nIt holds nothing: no map, no gear, no pawns. Whatever you left behind was "
                + "already gone the moment you abandoned the place, and the marker says only where you were and "
                + "how long ago, so a long game is a planet slowly filling with dots you have no use for."
        };

        internal static readonly SiteFadeKind GravshipLaunch = new SiteFadeKind
        {
            DefName = "GravshipLaunch",
            Label = "Gravship launch sites",
            Default = 30,
            Vanilla = 0,
            Read = settings => settings.siteFadeLaunchDays,
            Write = (settings, days) => settings.siteFadeLaunchDays = days,
            Tooltip = "The marker left where a gravship took off. One per launch, kept for the rest of the "
                + "game.\n\nA colony that moves every few quarters leaves a trail of these across the planet, "
                + "each one recording that you were once there and when."
        };

        internal static readonly SiteFadeKind AbandonedCamp = new SiteFadeKind
        {
            DefName = "AbandonedCamp",
            Label = "Abandoned camps",
            Default = 30,
            Vanilla = 30,
            Read = settings => settings.siteFadeCampDays,
            Write = (settings, days) => settings.siteFadeCampDays = days,
            Tooltip = "The marker left when a caravan camp's map is packed up.\n\nThis is the one RimWorld "
                + "already clears up, thirty days later, and thirty days is where this row sits -- so on this one "
                + "row the setting starts out doing nothing at all. Move it and you are changing the game's own "
                + "clock. Set it to Keep and the marker stays for good, which is the one thing the game will not "
                + "do on its own."
        };

        internal static readonly SiteFadeKind AbandonedLandmark = new SiteFadeKind
        {
            DefName = "AbandonedLandmark",
            Label = "Camps at landmarks",
            Default = 30,
            Vanilla = 0,
            Read = settings => settings.siteFadeLandmarkDays,
            Write = (settings, days) => settings.siteFadeLandmarkDays = days,
            Tooltip = "A camp pitched on a landmark leaves this instead of an abandoned camp, and unlike the "
                + "abandoned camp it is never cleared up.\n\nThat difference is RimWorld's rather than ours: the "
                + "same method that starts a thirty day clock on an ordinary camp makes this one and starts "
                + "nothing. Camping at the geysers is why there is still a marker there four years later."
        };

        private static readonly List<SiteFadeKind> all = new List<SiteFadeKind>
        {
            AbandonedSettlement, GravshipLaunch, AbandonedCamp, AbandonedLandmark
        };

        internal static List<SiteFadeKind> All => all;

        /// <summary>
        /// The def a kind names, or null on an install that does not have it.
        ///
        /// Looked up on every call rather than cached in a static: the def database is rebuilt whenever the mod
        /// list changes, and a def held from the previous load is a reference to something no longer in the game.
        /// This is a dictionary hit, and it is called from a settings row and an hourly sweep.
        /// </summary>
        internal static WorldObjectDef DefOf(SiteFadeKind kind)
        {
            if (kind == null)
                return null;

            return DefDatabase<WorldObjectDef>.GetNamedSilentFail(kind.DefName);
        }

        internal static bool Available(SiteFadeKind kind)
        {
            return DefOf(kind) != null;
        }

        /// <summary>Which kind a world object belongs to, or null if it is not one of ours to touch.</summary>
        internal static SiteFadeKind For(WorldObjectDef def)
        {
            if (def == null)
                return null;

            for (int i = 0; i < all.Count; i++)
            {
                if (def.defName == all[i].DefName)
                    return all[i];
            }

            return null;
        }

        /// <summary>
        /// The lifespan in days a kind is set to, or its default when there are no settings to read.
        ///
        /// Matched against the offered list rather than trusted, because this file is hand editable and a
        /// lifespan of one tick would take a marker away in the same second it appeared.
        /// </summary>
        internal static int Days(SiteFadeKind kind)
        {
            if (kind == null)
                return 0;

            UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

            if (settings == null)
                return kind.Default;

            int days = kind.Read(settings);

            for (int i = 0; i < Choices.Length; i++)
            {
                if (Choices[i] == days)
                    return days;
            }

            return kind.Default;
        }

        internal static void Set(SiteFadeKind kind, int days)
        {
            UIGuard.Try("WorldSites.SetDays", () =>
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                if (settings == null || kind == null)
                    return;

                kind.Write(settings, days);
                settings.Save();

                // Applied at once rather than at the next sweep, so the count drawn under the rows is the truth
                // about the choice just made rather than about the one before it.
                SiteFade.ReconcileAll();
            }, "That lifespan is not saved.");
        }

        /// <summary>
        /// How a lifespan reads on a segment.
        ///
        /// Years past sixty days, because sixty days is a year in this game and nobody thinks about their colony
        /// in hundreds of days.
        /// </summary>
        internal static string LabelOf(int days)
        {
            switch (days)
            {
                case 0:
                    return "Keep";

                case 60:
                    return "1 year";

                case 120:
                    return "2 years";

                default:
                    return days + " days";
            }
        }
    }
}
