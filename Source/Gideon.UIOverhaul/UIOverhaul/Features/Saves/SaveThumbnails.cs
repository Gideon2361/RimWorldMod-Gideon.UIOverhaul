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
        /// scene either way, and asking for fewer pixels is the cheaper request. Large enough to recognize a
        /// base by its shape, which is all this has to do.
        /// </summary>
        private const int Width = 512;

        /// <summary>The picture that belongs to a save file.</summary>
        internal static string PathFor(string savePath)
        {
            return savePath.NullOrEmpty() ? null : savePath + ".png";
        }

        /// <summary>
        /// Arranges for a picture of the map to be written beside the save.
        ///
        /// <b>This does not render anything itself, and the first version's attempt to is why it produced a
        /// black rectangle.</b> That version asked the map camera to render on demand into a texture of our
        /// own, which sounds right and cannot work: RimWorld does not draw the map through renderers a camera
        /// finds in the scene, it issues <c>Graphics.DrawMesh</c> calls from <c>Map.MapUpdate</c> every frame.
        /// Those submissions belong to the frame that made them and are consumed by the normal render, so an
        /// extra <c>Render()</c> from inside <c>OnGUI</c> -- which runs after the frame has already been drawn
        /// -- finds an empty queue and produces the clear color and nothing else. The symptom was two saves
        /// whose pictures were byte-for-byte the same size.
        ///
        /// <b>So the frame is taken rather than made.</b> A one-shot component on the camera reads the screen
        /// in <c>OnPostRender</c>, which Unity runs after the camera has drawn and before <c>OnGUI</c> draws
        /// the interface. That is the one moment in a frame where the map exists and the windows do not, which
        /// is exactly the picture wanted -- and it needs nothing hidden first.
        ///
        /// <b>It costs a frame, and that is harmless.</b> The picture arrives a frame or two after the button
        /// was pressed, by which time the save may already be written; the png is a separate file, so the
        /// order the two land in does not matter.
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

                // Replaced rather than added to, so pressing save twice in quick succession cannot leave two
                // grabbers reading the same frame into different files.
                Grabber existing = camera.gameObject.GetComponent<Grabber>();

                if (existing != null)
                    Object.Destroy(existing);

                camera.gameObject.AddComponent<Grabber>().Target = target;
            }, "This save has no preview picture. Nothing else is affected.");
        }

        /// <summary>
        /// Reads one frame off the screen, writes it, and removes itself.
        ///
        /// <b><c>OnPostRender</c> is the whole point of this class existing.</b> It is the only hook that runs
        /// with the map drawn and the interface not, so the picture comes out clean without hiding a single
        /// window first. A screen grab from anywhere else -- including
        /// <c>ScreenCapture.CaptureScreenshotAsTexture</c> -- takes the composited frame, save dialog and all.
        ///
        /// <b>It destroys itself on the first frame it runs,</b> so nothing here is paid for during play. A
        /// component left attached would read and encode the screen every frame for the rest of the session.
        /// </summary>
        private sealed class Grabber : MonoBehaviour
        {
            internal string Target;

            private void OnPostRender()
            {
                string target = Target;

                // Cleared first, so any failure below still takes this component out of the frame loop
                // instead of retrying and failing again on every frame that follows.
                Target = null;
                Object.Destroy(this);

                if (target.NullOrEmpty())
                    return;

                UIGuard.Try("Saves.GrabFrame", () => Write(target),
                    "This save has no preview picture. Nothing else is affected.");
            }

            /// <summary>
            /// Reads the screen and writes it out at thumbnail size.
            ///
            /// <b>Read at screen size and then scaled down, which is the opposite of what the first version
            /// tried.</b> Rendering small was cheaper and is not available here: the pixels being taken are
            /// the ones already on the screen, so their size is not ours to choose. The scaling is a
            /// <c>Graphics.Blit</c> rather than a manual resample, since that is the GPU doing what it is for.
            /// </summary>
            private static void Write(string target)
            {
                int screenWidth = Mathf.Max(1, Screen.width);
                int screenHeight = Mathf.Max(1, Screen.height);
                int height = Mathf.Max(1, Mathf.RoundToInt(Width * (float) screenHeight / screenWidth));

                Texture2D full = null;
                Texture2D small = null;
                RenderTexture scaled = null;
                RenderTexture previousActive = RenderTexture.active;

                try
                {
                    full = new Texture2D(screenWidth, screenHeight, TextureFormat.RGB24, false);
                    full.ReadPixels(new Rect(0f, 0f, screenWidth, screenHeight), 0, 0);
                    full.Apply();

                    scaled = RenderTexture.GetTemporary(Width, height, 0);
                    Graphics.Blit(full, scaled);

                    RenderTexture.active = scaled;

                    small = new Texture2D(Width, height, TextureFormat.RGB24, false);
                    small.ReadPixels(new Rect(0f, 0f, Width, height), 0, 0);
                    small.Apply();

                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? SaveFolders.Root);
                    File.WriteAllBytes(target, small.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previousActive;

                    if (scaled != null)
                        RenderTexture.ReleaseTemporary(scaled);

                    // Both are unmanaged and neither is reclaimed by the collector. The full screen copy is
                    // the one that matters: at 4K it is around twenty-five megabytes.
                    if (full != null)
                        Object.Destroy(full);

                    if (small != null)
                        Object.Destroy(small);
                }
            }
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
