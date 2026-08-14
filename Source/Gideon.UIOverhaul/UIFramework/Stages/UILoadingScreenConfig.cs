using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Components.Images;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using UnityEngine;
using Verse;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIFramework.Stages
{
    /// <summary>
    /// A loading screen: a background image, what text to show, and which class draws it.
    ///
    ///
    /// The file lives at the mod root, outside every directory the game scans:
    ///
    ///   Mods/gideon.uioverhaul/LoadingScreen.xml
    ///
    /// That placement is the point. A file under Defs would be parsed twice -- once by us, once by the
    /// def loader -- and would invite PatchOperations that cannot possibly run in time to matter.
    ///
    /// Full documentation is in the mod's Help folder.
    /// </summary>
    public class UILoadingScreenConfig
    {
        /// <summary>
        /// packageId of the mod that owns this framework. Contributed data is filed under it, so a
        /// mod handing us a loading screen says plainly who it is handing it to.
        /// </summary>
        public const string OwnerPackageId = "gideon.uioverhaul";

        /// <summary>Filename looked for inside <see cref="OwnerPackageId"/>'s folder.</summary>
        public const string FileName = "LoadingScreen.xml";

        /// <summary>
        /// Path searched inside every active mod, relative to that mod's own root:
        ///
        ///   Mods/YourMod/Mods/gideon.uioverhaul/LoadingScreen.xml
        ///
        /// The nested "Mods/&lt;packageId&gt;" folder is the convention for data one mod contributes to
        /// another: the folder names the consumer, so a mod carrying data for several frameworks keeps
        /// them apart, and a reader only ever walks its own subtree.
        /// </summary>
        public static string RelativePath => Path.Combine("Mods", OwnerPackageId, FileName);

        /// <summary>Name of the screen this mod ships, and the one used when nothing else is chosen.</summary>
        public const string DefaultScreenName = "UILoadingScreen_Default";

        /// <summary>Lookup key, and what a selector persists. Unique across all mods.</summary>
        public string name;

        /// <summary>Display name for a selector. Falls back to <see cref="name"/>.</summary>
        public string label;

        public string description;

        /// <summary>
        /// Texture path under any mod's Textures folder, e.g. "UIOverhaul/UI/LoadingScreen.Default".
        /// Null leaves a flat fill of the palette's window background, which reads as deliberate
        /// where a missing-texture box would not.
        /// </summary>
        public string background;

        public UIImageFit backgroundFit = UIImageFit.Cover;

        /// <summary>
        /// Overrides whether the background is mirrored vertically. Null -- the normal case -- lets
        /// <see cref="UIImageLoader"/> decide from the file format, which is right for every image
        /// written by a normal tool. Set it only if a DDS of yours comes out upside down, which means
        /// it was saved bottom-up.
        /// </summary>
        public bool? backgroundFlipVertical;

        /// <summary>Color washed over the background, in any form <see cref="UIColorParser"/> takes.</summary>
        public string overlay;

        public bool showStage = true;
        public bool showStep = true;
        public bool showProgressBar = true;

        /// <summary>
        /// Draws a filled panel behind the stage, step and bar. On by default, because text laid
        /// straight over a photographic background is unreadable wherever the image happens to be
        /// light -- and which part that is changes with every backdrop.
        /// </summary>
        public bool showPanel = true;

        /// <summary>
        /// Panel fill, in any form <see cref="UIColorParser"/> takes. Null uses the active palette's
        /// panel background at 85% alpha, so the backdrop still reads through it.
        /// </summary>
        public string panelColor;

        /// <summary>How far the panel extends beyond the text and bar it sits behind.</summary>
        public float panelPadding = 16f;

        /// <summary>
        /// Assembly-qualified or bare name of a <see cref="UILoadingScreenControl"/> subclass that
        /// draws this screen. Null uses the stock drawer.
        /// </summary>
        public string drawerClass;

        /// <summary>Mod this screen came from, for error messages.</summary>
        public string sourceMod;

        private UILoadingScreenControl drawerInstance;
        private UIImage backgroundImage;
        private bool overlayParsed;
        private Color overlayColor;
        private bool panelParsed;
        private Color? panelColorValue;

        /// <summary>
        /// The drawer, built on first use. Never null: a bad drawerClass logs once and falls back to
        /// the stock drawer rather than leaving the screen unable to paint.
        /// </summary>
        public UILoadingScreenControl Drawer
        {
            get
            {
                if (drawerInstance != null)
                    return drawerInstance;

                if (drawerClass.NullOrEmpty())
                    return drawerInstance = new UILoadingScreenControl();

                // GenTypes rather than Harmony's AccessTools: this reader has no other reason to
                // depend on Harmony, and GenTypes is what RimWorld itself uses to resolve a type name
                // written in XML.
                Type type = GenTypes.GetTypeInAnyAssembly(drawerClass);
                if (type == null || !typeof(UILoadingScreenControl).IsAssignableFrom(type))
                {
                    Log.ErrorOnce(
                        UILogTag.Prefix + $"Loading screen '{name}': drawerClass '{drawerClass}' "
                        + "is not a UILoadingScreenControl. Using the stock drawer.", 0x17C0_10AC);
                    return drawerInstance = new UILoadingScreenControl();
                }

                try
                {
                    drawerInstance = (UILoadingScreenControl) Activator.CreateInstance(type);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce(
                        UILogTag.Prefix + $"Loading screen '{name}': could not create "
                        + $"drawerClass '{drawerClass}'. Using the stock drawer.\n" + ex, 0x17C0_10AE);
                    drawerInstance = new UILoadingScreenControl();
                }

                return drawerInstance;
            }
        }

        /// <summary>
        /// The background image. Never null; check <see cref="UIImage.IsValid"/>.
        ///
        /// Loaded by <see cref="UIImageLoader"/> straight off disk rather than through
        /// ContentFinder, and that is not an optimization -- it is the only way this can work.
        /// RimWorld defers all mod content loading to LongEventHandler.ExecuteWhenFinished, whose
        /// queue does not run until the long event ends, so during a load ContentFinder has no mod
        /// textures at all. A backdrop fetched that way appears for one frame, after the loading
        /// screen it was meant to decorate has gone.
        /// </summary>
        public UIImage BackgroundImage
        {
            get
            {
                if (backgroundImage == null)
                    backgroundImage = UIImageLoader.Load(background);

                return backgroundImage;
            }
        }

        /// <summary>
        /// Whether to mirror the background when drawing: the <see cref="backgroundFlipVertical"/>
        /// override if one was given, otherwise what the image's format implies.
        /// </summary>
        public bool BackgroundFlipVertical =>
            backgroundFlipVertical ?? BackgroundImage.FlipVertical;

        /// <summary>The parsed <see cref="overlay"/>, or null when there is none.</summary>
        public Color? OverlayColor
        {
            get
            {
                if (!overlayParsed)
                {
                    overlayParsed = true;
                    overlayColor = default;

                    // Split rather than combined with &&: short-circuiting past TryParse would leave
                    // its out parameters unassigned, which the compiler rejects outright.
                    if (!overlay.NullOrEmpty())
                    {
                        if (UIColorParser.TryParse(overlay, out Color parsed, out string error))
                        {
                            overlayColor = parsed;
                        }
                        else
                        {
                            Log.ErrorOnce(UILogTag.Prefix + $"Loading screen '{name}' overlay: {error}",
                                0x17C0_10AF);
                        }
                    }
                }

                return overlayColor.a > 0f ? overlayColor : (Color?) null;
            }
        }

        /// <summary>
        /// The panel fill. Falls back to the palette's panel background at 85% alpha rather than a
        /// fixed color, so a panel stays part of the theme; the alpha is what makes it a panel over a
        /// photograph instead of a slab covering it.
        /// </summary>
        public Color PanelColor(UIColorPaletteDef palette)
        {
            if (!panelParsed)
            {
                panelParsed = true;
                panelColorValue = null;

                if (!panelColor.NullOrEmpty())
                {
                    if (UIColorParser.TryParse(panelColor, out Color parsed, out string error))
                        panelColorValue = parsed;
                    else
                        Log.ErrorOnce(UILogTag.Prefix + $"Loading screen '{name}' panelColor: {error}",
                            0x17C0_10B0);
                }
            }

            if (panelColorValue.HasValue)
                return panelColorValue.Value;

            Color fromPalette = (palette ?? UIColorPaletteDef.Active).PanelBackground;
            return new Color(fromPalette.r, fromPalette.g, fromPalette.b, fromPalette.a * 0.85f);
        }

        // ---------------------------------------------------------------------------------------
        // Registry
        // ---------------------------------------------------------------------------------------

        private static List<UILoadingScreenConfig> all;
        private static string activeName;
        private static UILoadingScreenConfig builtIn;

        /// <summary>
        /// The screen that gets drawn. Assigning null reverts to <see cref="Default"/>.
        /// This is the seam a theme selector uses.
        /// </summary>
        public static UILoadingScreenConfig Active
        {
            get
            {
                if (!activeName.NullOrEmpty())
                {
                    UILoadingScreenConfig chosen = Named(activeName);
                    if (chosen != null)
                        return chosen;
                }

                return Default;
            }
            set => activeName = value?.name;
        }

        /// <summary>The chosen screen's name. Persist this, not an object reference.</summary>
        public static string ActiveName
        {
            get => activeName;
            set => activeName = value;
        }

        /// <summary>
        /// <see cref="DefaultScreenName"/> if it was found, else any screen that was, else
        /// <see cref="BuiltIn"/>.
        /// </summary>
        public static UILoadingScreenConfig Default
        {
            get
            {
                UILoadingScreenConfig shipped = Named(DefaultScreenName);
                if (shipped != null)
                    return shipped;

                List<UILoadingScreenConfig> list = All;
                return list.Count > 0 ? list[0] : BuiltIn;
            }
        }

        /// <summary>
        /// The compiled-in screen: no background, palette colors, all text shown. Used when no file
        /// was found at all, which for a correctly installed mod should never happen.
        /// </summary>
        public static UILoadingScreenConfig BuiltIn =>
            builtIn ?? (builtIn = new UILoadingScreenConfig
            {
                name = "UILoadingScreen_BuiltIn",
                label = "built-in",
                description = "The framework's default loading screen.",
                sourceMod = "Gideon.UIFramework"
            });

        /// <summary>A screen by name, or null.</summary>
        public static UILoadingScreenConfig Named(string screenName)
        {
            if (screenName.NullOrEmpty())
                return null;

            foreach (UILoadingScreenConfig config in All)
            {
                if (string.Equals(config.name, screenName, StringComparison.OrdinalIgnoreCase))
                    return config;
            }

            return null;
        }

        /// <summary>
        /// Every screen found, across all active mods. Read from disk on first access.
        ///
        /// Lazy rather than driven from a Mod constructor on purpose: the framework must not depend on
        /// a consumer remembering to initialize it, and by the time anything asks for a screen the mod
        /// list is populated. Reading it here also means the very first frame that draws already has
        /// the real configuration.
        /// </summary>
        public static List<UILoadingScreenConfig> All
        {
            get
            {
                if (all == null)
                    all = LoadAll();
                return all;
            }
        }

        /// <summary>Forces a re-read from disk. For development; nothing in normal play needs it.</summary>
        public static void Reload()
        {
            all = null;

            // The images belong to the configs being discarded; leaving them cached would keep the
            // old textures alive and hand them straight back to the replacements.
            UIImageLoader.Clear();
        }

        private static List<UILoadingScreenConfig> LoadAll()
        {
            List<UILoadingScreenConfig> found = new List<UILoadingScreenConfig>();

            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            if (mods == null)
                return found;

            foreach (ModContentPack mod in mods)
            {
                string path;
                try
                {
                    path = Path.Combine(mod.RootDir, RelativePath);
                    if (!File.Exists(path))
                        continue;
                }
                catch
                {
                    continue;
                }

                try
                {
                    ReadFile(path, mod, found);
                }
                catch (Exception ex)
                {
                    // One malformed file must not cost every other mod its loading screen.
                    Log.Error(UILogTag.Prefix + $"Could not read {path}\n{ex}");
                }
            }

            return found;
        }

        private static void ReadFile(string path, ModContentPack mod, List<UILoadingScreenConfig> into)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(path);

            XmlElement root = doc.DocumentElement;
            if (root == null)
                return;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (!(node is XmlElement element))
                    continue;

                UILoadingScreenConfig config = Read(element);
                config.sourceMod = mod.Name;

                if (config.name.NullOrEmpty())
                {
                    Log.Error(UILogTag.Prefix + $"{path}: a loading screen has no <name>; skipped.");
                    continue;
                }

                // Later mods win, matching how load order works everywhere else in RimWorld.
                int existing = into.FindIndex(c =>
                    string.Equals(c.name, config.name, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                {
                    Log.Warning(UILogTag.Prefix + $"Loading screen '{config.name}' from {mod.Name} "
                                + $"replaces the one from {into[existing].sourceMod}.");
                    into[existing] = config;
                }
                else
                {
                    into.Add(config);
                }
            }
        }

        private static UILoadingScreenConfig Read(XmlElement element)
        {
            UILoadingScreenConfig config = new UILoadingScreenConfig();

            foreach (XmlNode node in element.ChildNodes)
            {
                if (!(node is XmlElement field))
                    continue;

                string value = field.InnerText?.Trim();

                switch (field.Name)
                {
                    case "name": config.name = value; break;
                    case "label": config.label = value; break;
                    case "description": config.description = value; break;
                    case "background": config.background = value; break;
                    case "overlay": config.overlay = value; break;
                    case "drawerClass": config.drawerClass = value; break;
                    case "panelColor": config.panelColor = value; break;
                    case "backgroundFit": config.backgroundFit = ParseEnum(value, config.backgroundFit); break;
                    case "showStage": config.showStage = ParseBool(value, true); break;
                    case "showStep": config.showStep = ParseBool(value, true); break;
                    case "showProgressBar": config.showProgressBar = ParseBool(value, true); break;
                    case "showPanel": config.showPanel = ParseBool(value, true); break;
                    case "panelPadding": config.panelPadding = ParseFloat(value, config.panelPadding); break;
                    case "backgroundFlipVertical":
                        // Nullable on purpose: "unset" is a third state meaning "work it out from the
                        // file format", and it is the one almost every screen wants.
                        if (bool.TryParse(value, out bool flip))
                            config.backgroundFlipVertical = flip;
                        else
                            Log.Warning(UILogTag.Prefix + $"'{value}' is not a bool for "
                                        + "<backgroundFlipVertical>; leaving it automatic.");
                        break;
                    default:
                        Log.Warning(UILogTag.Prefix + $"Unknown loading screen field <{field.Name}>; ignored.");
                        break;
                }
            }

            return config;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        private static UIImageFit ParseEnum(string value, UIImageFit fallback)
        {
            if (value.NullOrEmpty())
                return fallback;

            try
            {
                return (UIImageFit) Enum.Parse(typeof(UIImageFit), value, true);
            }
            catch
            {
                Log.Warning(UILogTag.Prefix + $"'{value}' is not a backgroundFit; using {fallback}.");
                return fallback;
            }
        }
    }
}
