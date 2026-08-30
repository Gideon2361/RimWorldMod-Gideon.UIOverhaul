using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Loads a shipped TTF as a live dynamic font, which is the machinery the game's own text runs on.
    ///
    /// <b>The direct road is closed and this is the detour, suggested by Aaron on 2026-08-30.</b>
    /// <c>Font(string)</c> routes a path to <c>Internal_CreateFontFromPath</c>, which turned out to be a stub
    /// in RimWorld's player: the font comes back empty. It cannot be Harmony patched into working, because an
    /// internal call has no managed body to patch -- the stub is the engine itself.
    ///
    /// <b>But <c>CreateDynamicFontFromOSFont</c> is not a stub; it just wants the OS to know the font.</b>
    /// Windows has a hook for exactly that: <c>AddFontResourceEx</c> with <c>FR_PRIVATE</c> registers a font
    /// file with GDI for this process only -- nothing is installed, nothing needs elevation, and the
    /// registration dies with the process. Register our shipped TTF, then ask for it by family name through
    /// the OS route, and Unity hands back a real dynamic font: FreeType rasterizing hinted glyphs at whatever
    /// size a style requests, exactly as vanilla text does.
    ///
    /// <b>Windows only, and deliberately quiet about it.</b> There is no gdi32 on Linux or macOS, so the
    /// registration throws <c>DllNotFoundException</c> there and this returns null without reporting -- the
    /// baked-atlas pipeline is the fallback everywhere this cannot serve, and a Mac player's log should not
    /// carry an error about the game working as designed.
    /// </summary>
    internal static class UIDynamicFont
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

        /// <summary>GDI's flag for a registration visible to this process alone.</summary>
        private const uint FrPrivate = 0x10;

        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        /// <summary>
        /// A dynamic font for one shipped TTF: the file name without extension, and the family name written
        /// inside the file, which is what the OS route looks it up by.
        ///
        /// Null when the file is missing, the platform has no GDI, or the engine could not produce a working
        /// font. The null is cached; a file that failed will fail the same way every frame.
        /// </summary>
        internal static Font FromFile(string fileName, string familyName)
        {
            Font existing;

            if (Fonts.TryGetValue(fileName, out existing))
                return existing;

            Font loaded = UIGuard.Try("UIText.LoadTtf", () => Load(fileName, familyName), null, null);

            Fonts[fileName] = loaded;

            return loaded;
        }

        private static Font Load(string fileName, string familyName)
        {
            string folder = OurFontsFolder();

            if (folder == null)
                return null;

            string path = Path.Combine(folder, fileName + ".ttf");

            if (!File.Exists(path))
                return null;

            // Zero fonts registered means GDI could not read the file; no GDI at all means not Windows. Both
            // are ordinary states with a working fallback, not faults.
            try
            {
                if (AddFontResourceEx(path, FrPrivate, IntPtr.Zero) == 0)
                    return null;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }

            // The size is only the default request; a dynamic font rasterizes at whatever size each style asks
            // for, which is the whole point of taking this road.
            Font font = Font.CreateDynamicFontFromOSFont(familyName, 16);

            if (font == null)
                return null;

            // Not saved and not destroyed on a scene load: RimWorld loads a scene between the menu and a game,
            // and a font that quietly died on the way in would draw nothing right after having worked.
            font.hideFlags = HideFlags.HideAndDontSave;

            // The OS route hands back a font object even for a family it never found -- it would silently
            // substitute Arial. HasCharacter answers from the real face, so an empty answer means the
            // registration did not take.
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
