using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIOverhaul.Shared;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// The line under a queued project saying why it cannot start yet.
    ///
    /// <b>An element of its own rather than part of the row above it.</b> It is a second line of a different
    /// shape -- shorter, indented past the badge, and only present sometimes -- so folding it into the entry
    /// would have meant every rail entry everywhere carrying an optional second line it never uses.
    ///
    /// <b>This is the element list earning itself.</b> A rail is a list of parts, and a feature that needs a
    /// part the shared controls do not have writes one, which costs a class rather than a fork of the rail.
    /// </summary>
    internal sealed class QueueBlockedNote : UIRailElement
    {
        internal string Text;

        /// <summary>Indented past the grip and badge, so it reads as belonging to the row above.</summary>
        private const float Indent = 34f;

        internal override float Height
        {
            get { return 16f; }
        }

        internal override bool Draw(Rect rect, UIColorPaletteDef palette, bool selected)
        {
            if (Text.NullOrEmpty())
                return false;

            TabParts.RowLabel(new Rect(rect.x + Indent, rect.y - 2f,
                Mathf.Max(0f, rect.width - Indent - 4f), Height), Text, palette.Warning,
                ResearchFaces.Condensed, ResearchFaces.Size.Chip);

            return false;
        }
    }
}
