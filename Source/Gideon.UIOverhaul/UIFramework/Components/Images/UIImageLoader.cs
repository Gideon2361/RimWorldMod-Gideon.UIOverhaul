using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Components.Images
{
    /// <summary>
    /// Loads a texture from a mod's Textures folder without going through RimWorld's content system.
    ///
    /// This exists for one reason: timing. <c>ModContentPack.ReloadContent</c> wraps all content
    /// loading in <c>LongEventHandler.ExecuteWhenFinished</c>, and that queue is not drained until the
    /// long event ends -- so for the entire duration of a load, <c>ContentFinder&lt;Texture2D&gt;.Get</c>
    /// returns null for every mod texture, including our own. Anything that has to draw an image
    /// *during* loading must load it itself. That is the whole story behind a loading-screen backdrop
    /// that only appears in the instant before the main menu.
    ///
    /// The reader is deliberately self-contained: files are found with the same folder rules RimWorld
    /// uses, decoded with Unity for PNG and JPG, and parsed here for DDS. Nothing private to the game
    /// is touched, so a game update cannot quietly break it.
    ///
    /// Must be called from the main thread -- creating a Texture2D off it is not allowed. Every
    /// drawing path qualifies, which is where this is used.
    /// </summary>
    public static class UIImageLoader
    {
        /// <summary>
        /// Tried in this order. DDS first because it is the cheapest to load and the right format for
        /// anything large: the bytes go to the GPU still compressed, with no decode step.
        ///
        /// PSD is missing on purpose. RimWorld lists it, but Unity's runtime decoder does not handle
        /// it, so a PSD would fail here whatever we did.
        /// </summary>
        private static readonly string[] Extensions = { ".dds", ".png", ".jpg", ".jpeg" };

        private const string TexturesFolder = "Textures";

        private static readonly Dictionary<string, UIImage> Cache =
            new Dictionary<string, UIImage>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The image at <paramref name="texturePath"/> -- a path under any active mod's Textures
        /// folder, without a file extension, exactly as RimWorld writes them
        /// ("UIOverhaul/UI/LoadingScreen.Default").
        ///
        /// Never null. A path that cannot be found or decoded yields an image whose
        /// <see cref="UIImage.IsValid"/> is false, reported once, so a caller can fall back without
        /// having to null-check twice.
        ///
        /// Results are cached, misses included: the files are on disk from the moment the process
        /// starts, so a miss is a real miss and re-scanning every frame would only cost time.
        /// </summary>
        public static UIImage Load(string texturePath)
        {
            if (texturePath.NullOrEmpty())
                return new UIImage(null, false, null);

            if (Cache.TryGetValue(texturePath, out UIImage cached))
                return cached;

            UIImage image = LoadUncached(texturePath);
            Cache[texturePath] = image;
            return image;
        }

        /// <summary>
        /// Drops every cached image, destroying the textures. For development, and for a mod list
        /// change that makes the previous lookups meaningless.
        /// </summary>
        public static void Clear()
        {
            foreach (UIImage image in Cache.Values)
            {
                if (image?.Texture != null)
                    UnityEngine.Object.Destroy(image.Texture);
            }

            Cache.Clear();
        }

        private static UIImage LoadUncached(string texturePath)
        {
            string file = FindFile(texturePath);
            if (file == null)
            {
                Log.Warning($"[Gideon.UIFramework] No texture found for '{texturePath}'. Looked for "
                            + string.Join(", ", Extensions) + " under every active mod's Textures folder.");
                return new UIImage(null, false, null);
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                string extension = Path.GetExtension(file).ToLowerInvariant();

                Texture2D texture = extension == ".dds"
                    ? LoadDds(bytes, file)
                    : LoadViaUnity(bytes);

                if (texture == null)
                    return new UIImage(null, false, file);

                texture.name = texturePath;
                texture.filterMode = FilterMode.Bilinear;

                // Repeat rather than Clamp so UIImageFit.Tile works. Every other fit produces texture
                // coordinates inside 0..1, where the wrap mode has no effect, so this costs nothing.
                texture.wrapMode = TextureWrapMode.Repeat;

                return new UIImage(texture, extension == ".dds", file);
            }
            catch (Exception ex)
            {
                Log.Error($"[Gideon.UIFramework] Could not load texture '{texturePath}' from {file}\n{ex}");
                return new UIImage(null, false, file);
            }
        }

        private static Texture2D LoadViaUnity(byte[] bytes)
        {
            // Size and format are replaced by LoadImage; these arguments only exist because the
            // constructor demands them.
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (texture.LoadImage(bytes))
                return texture;

            UnityEngine.Object.Destroy(texture);
            return null;
        }

        // ---------------------------------------------------------------------------------------
        // File lookup
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Walks active mods in reverse load order, so a mod loaded later overrides an earlier one's
        /// texture at the same path -- the rule RimWorld's own content lookup follows.
        /// </summary>
        private static string FindFile(string texturePath)
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            if (mods == null)
                return null;

            string relative = texturePath.Replace('\\', '/').TrimStart('/');

            for (int i = mods.Count - 1; i >= 0; i--)
            {
                foreach (string root in ContentRoots(mods[i]))
                {
                    foreach (string extension in Extensions)
                    {
                        string candidate;
                        try
                        {
                            candidate = Path.Combine(root, TexturesFolder, relative + extension);
                        }
                        catch
                        {
                            continue;
                        }

                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The folders a mod's content may live in, most specific first -- the versioned folder, then
        /// Common, then the mod root, as declared by loadFolders.xml or inferred by the game.
        ///
        /// Entries are normally relative to the mod root, but are accepted absolute as well, because
        /// the field is public and nothing guarantees which a given mod put there.
        /// </summary>
        private static IEnumerable<string> ContentRoots(ModContentPack mod)
        {
            if (mod == null || mod.RootDir.NullOrEmpty())
                yield break;

            List<string> folders = mod.foldersToLoadDescendingOrder;
            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    string trimmed = folder?.Trim();
                    if (trimmed.NullOrEmpty())
                        continue;

                    if (Directory.Exists(trimmed))
                    {
                        yield return trimmed;
                        continue;
                    }

                    string combined;
                    try
                    {
                        combined = Path.Combine(mod.RootDir, trimmed.TrimStart('/', '\\'));
                    }
                    catch
                    {
                        continue;
                    }

                    yield return combined;
                }
            }

            // Always tried, even when the list above covered it: a duplicate costs one File.Exists,
            // and a missing root would cost the caller its image.
            yield return mod.RootDir;
        }

        // ---------------------------------------------------------------------------------------
        // DDS
        // ---------------------------------------------------------------------------------------

        private const uint DdsMagic = 0x20534444;      // "DDS "
        private const int DdsHeaderSize = 128;         // magic + 124-byte header
        private const int Dx10HeaderSize = 20;
        private const uint PixelFormatFlagFourCc = 0x4;
        private const uint PixelFormatFlagRgb = 0x40;

        /// <summary>
        /// Parses a DDS and hands its bytes to the GPU unchanged.
        ///
        /// Only the container is read here: block-compressed data is uploaded exactly as stored, which
        /// is why this is fast enough to do while the game is loading. Formats Unity cannot represent
        /// are reported rather than guessed at.
        /// </summary>
        private static Texture2D LoadDds(byte[] bytes, string file)
        {
            if (bytes.Length < DdsHeaderSize || BitConverter.ToUInt32(bytes, 0) != DdsMagic)
            {
                Log.Error($"[Gideon.UIFramework] {file} is not a DDS file.");
                return null;
            }

            int height = BitConverter.ToInt32(bytes, 12);
            int width = BitConverter.ToInt32(bytes, 16);
            int declaredMips = BitConverter.ToInt32(bytes, 28);

            uint pixelFormatFlags = BitConverter.ToUInt32(bytes, 80);
            uint fourCc = BitConverter.ToUInt32(bytes, 84);
            uint rgbBitCount = BitConverter.ToUInt32(bytes, 88);
            uint redMask = BitConverter.ToUInt32(bytes, 92);

            if (width <= 0 || height <= 0)
            {
                Log.Error($"[Gideon.UIFramework] {file}: bad DDS dimensions {width}x{height}.");
                return null;
            }

            int dataOffset = DdsHeaderSize;
            TextureFormat format;

            if ((pixelFormatFlags & PixelFormatFlagFourCc) != 0)
            {
                switch (fourCc)
                {
                    case 0x31545844: format = TextureFormat.DXT1; break;   // "DXT1"
                    case 0x35545844: format = TextureFormat.DXT5; break;   // "DXT5"

                    case 0x30315844:                                        // "DX10"
                        if (bytes.Length < DdsHeaderSize + Dx10HeaderSize)
                        {
                            Log.Error($"[Gideon.UIFramework] {file}: DX10 header is truncated.");
                            return null;
                        }

                        dataOffset = DdsHeaderSize + Dx10HeaderSize;
                        uint dxgi = BitConverter.ToUInt32(bytes, DdsHeaderSize);
                        switch (dxgi)
                        {
                            case 71: case 72: format = TextureFormat.DXT1; break;   // BC1_UNORM(_SRGB)
                            case 77: case 78: format = TextureFormat.DXT5; break;   // BC3_UNORM(_SRGB)
                            case 98: case 99: format = TextureFormat.BC7; break;    // BC7_UNORM(_SRGB)
                            case 28: format = TextureFormat.RGBA32; break;          // R8G8B8A8_UNORM
                            case 87: format = TextureFormat.BGRA32; break;          // B8G8R8A8_UNORM
                            default:
                                Log.Error($"[Gideon.UIFramework] {file}: unsupported DXGI format {dxgi}. "
                                          + "Save as BC1 (DXT1), BC3 (DXT5) or BC7.");
                                return null;
                        }
                        break;

                    default:
                        Log.Error($"[Gideon.UIFramework] {file}: unsupported DDS fourCC "
                                  + $"'{FourCcText(fourCc)}'. DXT3 in particular has no Unity equivalent; "
                                  + "save as DXT1 or DXT5.");
                        return null;
                }
            }
            else if ((pixelFormatFlags & PixelFormatFlagRgb) != 0 && rgbBitCount == 32)
            {
                // A red mask in the low byte means the bytes are already R,G,B,A in memory.
                format = redMask == 0x000000FF ? TextureFormat.RGBA32 : TextureFormat.BGRA32;
            }
            else
            {
                Log.Error($"[Gideon.UIFramework] {file}: unsupported DDS pixel format "
                          + $"(flags 0x{pixelFormatFlags:X}, {rgbBitCount} bpp).");
                return null;
            }

            int available = bytes.Length - dataOffset;
            if (available <= 0)
            {
                Log.Error($"[Gideon.UIFramework] {file}: DDS has a header but no pixel data.");
                return null;
            }

            // Unity allocates a full mip chain or none; a DDS may carry a partial one. Ask for mips
            // only when the file actually holds every level Unity would expect, and fall back to the
            // base level otherwise -- a slightly blurrier downscale beats refusing to draw.
            bool useMips = declaredMips > 1 && available >= ChainSize(width, height, format, true);
            int required = ChainSize(width, height, format, useMips);

            if (available < required)
            {
                Log.Error($"[Gideon.UIFramework] {file}: DDS is truncated -- {available} bytes of pixel "
                          + $"data, {required} needed for {width}x{height} {format}.");
                return null;
            }

            Texture2D texture = new Texture2D(width, height, format, useMips);

            // Pinned rather than copied: a 4K backdrop is several megabytes, and there is no reason to
            // duplicate it just to skip a header.
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                texture.LoadRawTextureData(IntPtr.Add(handle.AddrOfPinnedObject(), dataOffset), required);
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
            finally
            {
                handle.Free();
            }

            // Mips are already in the data, and the CPU copy is not needed once uploaded.
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Bytes in one mip level.</summary>
        private static int LevelSize(int width, int height, TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                    return Mathf.Max(1, (width + 3) / 4) * Mathf.Max(1, (height + 3) / 4) * 8;

                case TextureFormat.DXT5:
                case TextureFormat.BC7:
                    return Mathf.Max(1, (width + 3) / 4) * Mathf.Max(1, (height + 3) / 4) * 16;

                default:
                    return width * height * 4;
            }
        }

        /// <summary>Bytes in the whole chain Unity will allocate.</summary>
        private static int ChainSize(int width, int height, TextureFormat format, bool mipped)
        {
            int total = 0;
            int w = width;
            int h = height;

            while (true)
            {
                total += LevelSize(w, h, format);

                if (!mipped || (w == 1 && h == 1))
                    break;

                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
            }

            return total;
        }

        private static string FourCcText(uint fourCc)
        {
            char[] characters =
            {
                (char) (fourCc & 0xFF),
                (char) ((fourCc >> 8) & 0xFF),
                (char) ((fourCc >> 16) & 0xFF),
                (char) ((fourCc >> 24) & 0xFF)
            };

            return new string(characters);
        }
    }
}
