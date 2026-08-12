using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Work
{
    /// <summary>
    /// Art for the work tab's per-pawn edit tools.
    ///
    /// Loaded in a static constructor under <see cref="StaticConstructorOnStartupAttribute"/> rather than
    /// lazily on first draw. A static Texture2D field has to be filled on the main thread, and a lazy load is
    /// only main-thread by luck of who reads it first; the attribute makes it a guarantee, and it runs after
    /// content loading so ContentFinder is ready.
    ///
    /// Misses are kept as nulls rather than as BaseContent.BadTex. Every caller falls back to a text glyph, so
    /// a renamed art file leaves a working button rather than a red cross on one.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WorkToolIcons
    {
        private const string Folder = "UIOverhaul/Work/";

        /// <summary>Slashed circle: sets every priority on the pawn to 0.</summary>
        public static readonly Texture2D Clear;

        /// <summary>Floppy disk: saves the pawn's priorities as a template.</summary>
        public static readonly Texture2D Save;

        /// <summary>Arrow into a tray: puts a saved template onto the pawn.</summary>
        public static readonly Texture2D Apply;

        /// <summary>Two offset sheets: lifts the pawn's priorities onto the session clipboard.</summary>
        public static readonly Texture2D Copy;

        /// <summary>A clipboard: writes the copied priorities onto the pawn.</summary>
        public static readonly Texture2D Paste;

        static WorkToolIcons()
        {
            Clear = ContentFinder<Texture2D>.Get(Folder + "Clear", false);
            Save = ContentFinder<Texture2D>.Get(Folder + "SaveTemplate", false);
            Apply = ContentFinder<Texture2D>.Get(Folder + "ApplyTemplate", false);
            Copy = ContentFinder<Texture2D>.Get(Folder + "Copy", false);
            Paste = ContentFinder<Texture2D>.Get(Folder + "Paste", false);
        }
    }
}
