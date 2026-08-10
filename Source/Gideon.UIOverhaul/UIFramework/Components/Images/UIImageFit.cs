namespace Gideon.UIFramework.Components.Images
{
    /// <summary>How an image is mapped onto a rect that is not its own shape.</summary>
    public enum UIImageFit
    {
        /// <summary>
        /// Scale until the rect is covered, cropping the overflow. Keeps the aspect ratio and leaves
        /// no bars, at the cost of losing the edges. The right choice for a full-screen backdrop,
        /// which has to fill whatever aspect the player's monitor happens to be.
        /// </summary>
        Cover,

        /// <summary>Scale until the whole image fits, keeping the aspect ratio and leaving bars.</summary>
        Contain,

        /// <summary>Stretch to the rect exactly, distorting the image.</summary>
        Stretch,

        /// <summary>Draw at native size, centered. Anything larger than the rect is clipped.</summary>
        Center,

        /// <summary>Repeat at native size from the top-left. For textures that are meant to tile.</summary>
        Tile
    }
}
