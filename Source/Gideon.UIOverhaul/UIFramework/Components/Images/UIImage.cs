using UnityEngine;

namespace Gideon.UIFramework.Components.Images
{
    /// <summary>
    /// A texture together with what a drawer needs to know in order to draw it correctly.
    ///
    /// The only such thing at present is <see cref="FlipVertical"/>, and it exists because DDS and
    /// Unity disagree about which end of the image row zero is. Baking the answer into the image, at
    /// the point where the format is known, keeps every caller from having to care: ask
    /// <see cref="UIImageLoader"/> for an image and draw it.
    /// </summary>
    public sealed class UIImage
    {
        /// <summary>The loaded texture. Null when loading failed.</summary>
        public readonly Texture2D Texture;

        /// <summary>
        /// True when the texture's rows are stored top-down and must be mirrored to draw right way up.
        ///
        /// Unity's texture origin is the bottom-left, so raw pixel data is expected bottom-up; DDS
        /// stores scanline zero at the top. Handing DDS bytes straight to the GPU therefore produces a
        /// mirrored image, which is corrected when drawing rather than by rewriting the pixel data --
        /// flipping a block-compressed image means unpacking and repacking every 4x4 block, and a
        /// mirrored GUI matrix costs nothing.
        ///
        /// PNG and JPG are decoded by Unity, which already resolves this, so they load unflipped.
        /// </summary>
        public readonly bool FlipVertical;

        /// <summary>Path this was loaded from, for error messages.</summary>
        public readonly string SourcePath;

        public UIImage(Texture2D texture, bool flipVertical, string sourcePath)
        {
            Texture = texture;
            FlipVertical = flipVertical;
            SourcePath = sourcePath;
        }

        /// <summary>True when there is something to draw.</summary>
        public bool IsValid => Texture != null;

        public int Width => Texture != null ? Texture.width : 0;
        public int Height => Texture != null ? Texture.height : 0;
    }
}
