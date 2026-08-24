using System.Collections.Generic;
using Gideon.UIFramework.Defs;
using UnityEngine;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// What a research project is about, which is what the canvas is cut along.
    ///
    /// <b>Order is the priority order of the tests, and that is not incidental.</b> A project sits in exactly one
    /// band -- duplicating a node would break arrow drawing and selection -- so the classifier runs these top to
    /// bottom and takes the first match. Reordering this enum changes where projects land, so it is the one place
    /// the taxonomy's opinions live. See <see cref="ResearchTaxonomy"/> for what each test actually reads.
    /// </summary>
    internal enum ResearchBand
    {
        /// <summary>Anomaly's knowledge, and the mods that build on it. Tested first, because it is explicit.</summary>
        DarkKnowledge,

        Mechanoids,

        /// <summary>Before Power, or a starship reactor is a generator and nothing else.</summary>
        FlightAndSpace,

        /// <summary>Before Production, or a bionic arm is a crafting recipe and nothing else.</summary>
        MedicineAndGenetics,

        FarmingAndFood,

        WeaponsAndDefense,

        ApparelAndArmor,

        PowerAndElectronics,

        RecreationAndCulture,

        ProductionAndCrafting,

        BuildingAndComfort,

        /// <summary>Nothing above matched. Empty for vanilla; it exists so novel content has somewhere honest to sit.</summary>
        Other
    }

    /// <summary>One band's name, color and tooltip.</summary>
    internal sealed class ResearchBandInfo
    {
        internal ResearchBand Band;

        internal string Label;

        /// <summary>Short form for a chip on a cross-band arrow, where 140 pixels is the whole budget.</summary>
        internal string Short;

        internal string Tooltip;

        /// <summary>
        /// The band's color.
        ///
        /// <b>Literals rather than palette roles, and this is the one place in the mod that is right.</b> A
        /// palette role means something -- danger, success, accent -- and a band means nothing except "not the
        /// band next to it". Eleven distinguishable hues is a requirement no set of semantic roles can meet, and
        /// borrowing <c>danger</c> for Weapons would say a turret is a warning.
        ///
        /// Chosen to stay apart at a three pixel stripe, which is the smallest they are ever drawn.
        /// </summary>
        internal Color Color;
    }

    /// <summary>
    /// The bands, in test order, with their names and colors.
    ///
    /// <b>A static table rather than defs in XML,</b> the same choice the raid filters made and for the same
    /// reason: the list is the taxonomy, and the taxonomy is code -- <see cref="ResearchTaxonomy"/> has a test per
    /// band and a def whose test did not exist would be a band nothing could ever land in. Mods redirect a
    /// project through <see cref="ResearchBandOverrides"/>, which is data and is patchable.
    /// </summary>
    internal static class ResearchBands
    {
        private static readonly List<ResearchBandInfo> all = new List<ResearchBandInfo>
        {
            new ResearchBandInfo
            {
                Band = ResearchBand.DarkKnowledge,
                Label = "Dark Knowledge",
                Short = "Dark",
                Tooltip = "Anomaly's knowledge projects, and the mods that build on the same machinery.",
                Color = new Color(0.608f, 0.447f, 0.851f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.Mechanoids,
                Label = "Mechanoids",
                Short = "Mech",
                Tooltip = "Mechanitor work: gestation, bandwidth, and the mechs themselves.",
                Color = new Color(0.718f, 0.612f, 0.929f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.FlightAndSpace,
                Label = "Flight & Space",
                Short = "Flight",
                Tooltip = "Ship parts, grav engines, pods and shuttles. Anything that leaves the ground.",
                Color = new Color(0.424f, 0.553f, 0.925f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.MedicineAndGenetics,
                Label = "Medicine & Genetics",
                Short = "Med",
                Tooltip = "Surgery, prosthetics, medicine, and the machines that rewrite a body.",
                Color = new Color(0.373f, 0.788f, 0.753f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.FarmingAndFood,
                Label = "Farming, Food & Drugs",
                Short = "Food",
                Tooltip = "What you can sow, what you can cook, and what you can brew from it.",
                Color = new Color(0.498f, 0.690f, 0.412f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.WeaponsAndDefense,
                Label = "Weapons & Defense",
                Short = "War",
                Tooltip = "Weapons, turrets and traps.",
                Color = new Color(0.878f, 0.478f, 0.373f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.ApparelAndArmor,
                Label = "Apparel & Armor",
                Short = "Wear",
                Tooltip = "Anything worn, from a duster to cataphract plate.",
                Color = new Color(0.851f, 0.631f, 0.373f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.PowerAndElectronics,
                Label = "Power & Electronics",
                Short = "Power",
                Tooltip = "Things that make, store or carry power. Not things that merely draw it.",
                Color = new Color(0.910f, 0.773f, 0.278f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.RecreationAndCulture,
                Label = "Recreation & Culture",
                Short = "Joy",
                Tooltip = "Recreation, art, instruments and rituals.",
                Color = new Color(0.875f, 0.631f, 0.769f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.ProductionAndCrafting,
                Label = "Production & Crafting",
                Short = "Prod",
                Tooltip = "Work benches, mining, refining, and the recipes they run.",
                Color = new Color(0.690f, 0.537f, 0.408f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.BuildingAndComfort,
                Label = "Building & Comfort",
                Short = "Build",
                Tooltip = "Structure, floors, doors, furniture and temperature.",
                Color = new Color(0.561f, 0.639f, 0.749f)
            },
            new ResearchBandInfo
            {
                Band = ResearchBand.Other,
                Label = "Other",
                Short = "Other",
                Tooltip = "Nothing this mod could classify. Usually a project that unlocks no thing at all.",
                Color = new Color(0.424f, 0.455f, 0.502f)
            }
        };

        /// <summary>Every band, in test order. Never empty and never rebuilt.</summary>
        internal static List<ResearchBandInfo> All
        {
            get { return all; }
        }

        internal static ResearchBandInfo Info(ResearchBand band)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Band == band)
                    return all[i];
            }

            // Unreachable while the table covers the enum, and returning Other beats returning null into a
            // drawing path. See never-throw: a band nobody described is still a band that has to draw.
            return all[all.Count - 1];
        }

        internal static string LabelOf(ResearchBand band)
        {
            return Info(band).Label;
        }

        internal static Color ColorOf(ResearchBand band)
        {
            return Info(band).Color;
        }

        /// <summary>
        /// A band's color adjusted for the running theme.
        ///
        /// The literals above are picked against the dark palette, which is the default and what the mockup was
        /// drawn in. On a light theme they are all too pale to read as text on a near-white ground, so they are
        /// darkened rather than replaced -- a second table of eleven light-theme hues would be a second thing to
        /// keep in agreement with the first.
        /// </summary>
        internal static Color ColorFor(ResearchBand band, UIColorPaletteDef palette)
        {
            Color color = ColorOf(band);

            if (palette == null)
                return color;

            // The window's own background says which way round the theme is, without a flag to keep in step with
            // the palette defs. Anything darker than mid grey is a dark theme.
            float ground = (palette.WindowBackground.r + palette.WindowBackground.g + palette.WindowBackground.b)
                           / 3f;

            if (ground < 0.5f)
                return color;

            return new Color(color.r * 0.62f, color.g * 0.62f, color.b * 0.62f, color.a);
        }
    }
}
