using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Pawns
{
    /// <summary>
    /// Textures for the pawns tab.
    ///
    /// <see cref="StaticConstructorOnStartupAttribute"/> rather than plain field initializers: a
    /// <c>ContentFinder</c> lookup before the game has loaded its content returns nothing, and a null mark
    /// caches itself for the session.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class PawnTex
    {
        /// <summary>The tab's own mark, shared with the button that opens it.</summary>
        internal static readonly Texture2D Mark =
            ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Pawns", false);
    }
}
