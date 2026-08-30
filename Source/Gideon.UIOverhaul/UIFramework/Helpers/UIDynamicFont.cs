using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Loads a shipped TTF as a live dynamic font, which is the machinery the game's own text runs on.
    ///
    /// <b>The claim this class tests is written all over this repo, and it is false.</b> The baked-atlas
    /// pipeline was built on "Unity cannot load a font from a file": the constructor surface reads as
    /// name-only. But <c>Font(string)</c> routes any string with a directory in it to
    /// <c>Internal_CreateFontFromPath</c>, and that method ships in the player build RimWorld runs on --
    /// verified by decompiling UnityEngine.TextRenderingModule.dll out of RimWorld's own Managed folder,
    /// 2026-08-30. Whether the native side honours it in a player is what the font spike's TTF section shows.
    ///
    /// <b>If it renders, it is everything the atlas pipeline fought for, natively.</b> A dynamic font is
    /// rasterized by FreeType on demand, hinted, at whatever size a style asks -- the exact machinery behind
    /// vanilla text. Correct line metrics, rich text, wrapping, measurement, every code point the file covers,
    /// and per-size sharpness with no sheets, no baker and no rounding arithmetic at all.
    /// </summary>
    internal static class UIDynamicFont
    {
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        /// <summary>
        /// The font for one TTF in the mod's Fonts folder, by file name without the extension.
        ///
        /// Null when the file is missing or the engine could not read it, and the null is cached: a file that
        /// failed will fail the same way every frame.
        /// </summary>
        internal static Font FromFile(string fileName)
        {
            Font existing;

            if (Fonts.TryGetValue(fileName, out existing))
                return existing;

            Font loaded = UIGuard.Try("UIText.LoadTtf", () => Load(fileName), null, null);

            Fonts[fileName] = loaded;

            return loaded;
        }

        private static Font Load(string fileName)
        {
            string folder = OurFontsFolder();

            if (folder == null)
                return null;

            string path = Path.Combine(folder, fileName + ".ttf");

            if (!File.Exists(path))
                return null;

            // Not saved and not destroyed on a scene load, for the reason the runtime font gives: RimWorld
            // loads a scene between the menu and a game, and a font that quietly died on the way in would draw
            // nothing in a session where it had just worked.
            Font font = new Font(path) { hideFlags = HideFlags.HideAndDontSave };

            // A file the engine cannot read comes back as an empty font rather than as an exception, so the
            // test is whether it can actually answer for a letter.
            return font.HasCharacter('A') ? font : null;
        }

        /// <summary>
        /// Our own mod folder's Fonts directory, found through the running mod list rather than assumed,
        /// because the folder name is whatever the player or Steam called it.
        /// </summary>
        private static string OurFontsFolder()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod == null || mod.assemblies == null || mod.assemblies.loadedAssemblies == null)
                    continue;

                foreach (System.Reflection.Assembly loaded in mod.assemblies.loadedAssemblies)
                {
                    if (loaded != typeof(UIDynamicFont).Assembly)
                        continue;

                    string folder = Path.Combine(mod.RootDir, "Fonts");

                    return Directory.Exists(folder) ? folder : null;
                }
            }

            return null;
        }
    }
}
