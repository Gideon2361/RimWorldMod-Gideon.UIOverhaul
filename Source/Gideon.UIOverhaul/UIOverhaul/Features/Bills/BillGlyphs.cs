using System;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// The two glyphs a bill row acts with: pause for suspend, and a trashcan for delete.
    ///
    /// <b>Drawn rather than shipped, for the reason <see cref="UIIconCanvas"/> exists.</b> A generated mask is a
    /// white shape in an alpha channel, so it takes whatever colour it is drawn with. That matters here more than
    /// usual: delete is the danger red and suspend is the warning amber, and both of those come from the active
    /// palette, so a PNG would need a variant per theme or would be wrong on one of them.
    ///
    /// <b>Neither vanilla texture was usable.</b> <c>TexButton.Suspend</c> is not a pause symbol, and
    /// <c>TexButton.Delete</c> is <c>UI/Buttons/Dismiss</c>, which is an X. An X beside a suspend control reads as
    /// "close this row" rather than "destroy this bill", and destroying a bill is the one action on the row that
    /// cannot be undone, so it is worth a glyph that says bin.
    ///
    /// <b>Baked at 64 for a button that draws at 22.</b> Masks are bilinear, so a mask larger than its slot
    /// resolves smoothly while one baked at the drawn size shimmers as the row scrolls.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BillGlyphs
    {
        /// <summary>Authored in the canvas's own 32 unit square and baked at twice the largest drawn size.</summary>
        private const int Baked = 64;

        /// <summary>Two upright bars. Null only if the bake failed, which the drawers test for.</summary>
        internal static readonly Texture2D Pause;

        /// <summary>A lidded bin with two slots. Null only if the bake failed.</summary>
        internal static readonly Texture2D Trash;

        /// <summary>
        /// <b>Written out rather than through <c>UIGuard.Try</c>,</b> because a static constructor that throws
        /// leaves the CLR marking the whole type as unusable, and every later read of these fields would throw
        /// again from wherever it was called. Catching here leaves both fields null instead, which the row drawers
        /// already treat as "draw no button".
        /// </summary>
        static BillGlyphs()
        {
            try
            {
                Pause = BuildPause();
                Trash = BuildTrash();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Bills.Glyphs", ex,
                    "Bill rows show no suspend or delete button. Both actions are still on the bill's own "
                    + "settings window.");
            }
        }

        /// <summary>
        /// The pause bars, matching the speed control's own pause so the two read as the same symbol.
        ///
        /// Proportions are the speed glyph's, re-centred in a square: bars a shade over a tenth of the width, a
        /// gap the same, and half the height again in length. Narrower bars start to disappear at 22 pixels and
        /// wider ones close the gap up into a single block.
        /// </summary>
        private static Texture2D BuildPause()
        {
            return new UIIconCanvas(Baked)
                .Rect(10.5f, 8f, 4f, 16f)
                .Rect(17.5f, 8f, 4f, 16f)
                .ToTexture("Gideon.Icon.BillSuspend");
        }

        /// <summary>
        /// The bin: a handle, a lid, and a tapered body with two slots cut out of it.
        ///
        /// <b>The taper is what makes it a bin rather than a box,</b> and it is two cut triangles down the sides
        /// rather than a trapezoid, because filled primitives with pieces removed is how this canvas draws. The
        /// slots are cuts for the same reason; painting them a second tone would defeat the tinting this class
        /// exists for.
        ///
        /// Y increases downward in this space, so the handle's small number puts it above the lid.
        /// </summary>
        private static Texture2D BuildTrash()
        {
            return new UIIconCanvas(Baked)
                .Rect(13f, 4.5f, 6f, 2.5f)
                .Rect(6.5f, 7.5f, 19f, 3f)
                .Rect(9f, 12f, 14f, 15f)
                .CutTriangle(9f, 12f, 9f, 27f, 10.8f, 27f)
                .CutTriangle(23f, 12f, 23f, 27f, 21.2f, 27f)
                .CutRect(12.7f, 15.5f, 2f, 8f)
                .CutRect(17.3f, 15.5f, 2f, 8f)
                .ToTexture("Gideon.Icon.BillDelete");
        }
    }
}
