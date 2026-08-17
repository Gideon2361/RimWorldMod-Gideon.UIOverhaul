using System.IO;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Saves
{
    /// <summary>
    /// A picture of the map as it was when a save was written, and the reading of it back.
    ///
    /// <b>The UI is absent for free, which is the whole reason this is cheap.</b> RimWorld draws its
    /// interface through IMGUI in <c>OnGUI</c>, which is a separate pass from anything a camera renders. So
    /// asking the map camera to render into a texture of our own produces terrain, buildings, pawns and
    /// weather with no window, no colonist bar and no cursor -- without hiding anything first, and without
    /// the save dialog that is on screen at that moment appearing in its own thumbnail.
    ///
    /// A screen grab would have been the obvious approach and is the wrong one:
    /// <c>ScreenCapture.CaptureScreenshotAsTexture</c> takes the composited frame, interface and all.
    ///
    /// <b>Stored beside the save rather than inside it.</b> <c>Riverbend.rws</c> gets
    /// <c>Riverbend.rws.png</c>. Inside the file it would be base64 in a document this feature exists to make
    /// smaller; in a cache folder somewhere it would go stale the moment somebody moved a save in Explorer.
    /// Beside it, the picture travels with the save, is obvious for what it is, and can be deleted by anybody
    /// who does not want it.
    /// </summary>
    internal static class SaveThumbnails
    {
        /// <summary>
        /// Width the thumbnail is rendered at, with height following the screen's shape.
        ///
        /// Rendered small rather than captured large and scaled down: the camera is being asked to draw the
        /// scene either way, and asking for fewer pixels is the cheaper request. Large enough to recognise a
        /// base by its shape, which is all this has to do.
        /// </summary>
        private const int Width = 512;

        /// <summary>The picture that belongs to a save file.</summary>
        internal static string PathFor(string savePath)
        {
            return savePath.NullOrEmpty() ? null : savePath + ".png";
        }

        /// <summary>
        /// Renders the map camera into a PNG beside the save.
        ///
        /// <b>Called before the save is queued, not during it.</b> The long event puts a full screen overlay
        /// up and, more to the point, saving can take seconds during which the world keeps its last rendered
        /// state; capturing first means the picture is the frame the player was looking at when they decided
        /// to save.
        ///
        /// Everything here is main thread only. Unity refuses to render or read pixels off it, and the
        /// failure is a hard crash rather than an exception, which is why this is never handed to a worker.
        /// </summary>
        internal static void Capture(string savePath)
        {
            string target = PathFor(savePath);

            if (target.NullOrEmpty())
                return;

            UIGuard.Try("Saves.Capture", () =>
            {
                Camera camera = Find.Camera;

                if (camera == null || Current.ProgramState != ProgramState.Playing)
                    return;

                int height = Mathf.Max(1, Mathf.RoundToInt(Width * (float) UI.screenHeight / UI.screenWidth));

                RenderTexture buffer = RenderTexture.GetTemporary(Width, height, 24);
                RenderTexture previousTarget = camera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;

                Texture2D shot = null;

                try
                {
                    camera.targetTexture = buffer;
                    camera.Render();

                    RenderTexture.active = buffer;

                    shot = new Texture2D(Width, height, TextureFormat.RGB24, false);
                    shot.ReadPixels(new Rect(0f, 0f, Width, height), 0, 0);
                    shot.Apply();

                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? SaveFolders.Root);
                    File.WriteAllBytes(target, shot.EncodeToPNG());
                }
                finally
                {
                    // Put the camera back before anything else can draw through it. Leaving a target texture
                    // attached would send the entire game's rendering into our buffer and leave the screen
                    // black, which is a spectacular way for a screenshot feature to fail.
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;

                    RenderTexture.ReleaseTemporary(buffer);

                    if (shot != null)
                        Object.Destroy(shot);
                }
            }, "This save has no preview picture. Nothing else is affected.");
        }

        /// <summary>Moves a save's picture when the save moves, so the two never come apart.</summary>
        internal static void Move(string fromSavePath, string toSavePath)
        {
            UIGuard.Try("Saves.MoveThumbnail", () =>
            {
                string from = PathFor(fromSavePath);
                string to = PathFor(toSavePath);

                if (from.NullOrEmpty() || to.NullOrEmpty() || !File.Exists(from))
                    return;

                if (File.Exists(to))
                    File.Delete(to);

                File.Move(from, to);
            }, null);
        }

        /// <summary>Removes a save's picture, for when the save itself is gone.</summary>
        internal static void Remove(string savePath)
        {
            UIGuard.Try("Saves.RemoveThumbnail", () =>
            {
                string picture = PathFor(savePath);

                if (!picture.NullOrEmpty() && File.Exists(picture))
                    File.Delete(picture);
            }, null);
        }

        /// <summary>
        /// The picture for a save, decoded, or null when it has none.
        ///
        /// <b>The caller owns what comes back and must destroy it.</b> A <c>Texture2D</c> is unmanaged memory
        /// that the garbage collector will not reclaim, so one built per selection and forgotten is a leak
        /// that grows for as long as somebody browses. <see cref="Dialog_LoadGame"/> holds exactly one and
        /// releases it whenever the selection changes or the window closes.
        /// </summary>
        internal static Texture2D Load(string savePath)
        {
            return UIGuard.Try("Saves.LoadThumbnail", () =>
            {
                string picture = PathFor(savePath);

                if (picture.NullOrEmpty() || !File.Exists(picture))
                    return null;

                // The size is a placeholder: LoadImage replaces the texture's dimensions with the file's.
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);

                if (texture.LoadImage(File.ReadAllBytes(picture)))
                    return texture;

                Object.Destroy(texture);

                return null;
            }, null, "A save preview could not be shown.");
        }
    }
}
