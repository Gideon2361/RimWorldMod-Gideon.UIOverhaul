using System;
using System.Collections.Generic;
using Gideon.UIFramework.Components.Colors;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Defs
{
    /// <summary>
    /// A named set of colors, authored in XML, that controls draw from instead of holding color
    /// values of their own.
    ///
    /// Every slot in <see cref="UIColorRole"/> has a built-in default, so a palette only has to name
    /// the roles it wants to change. Combined with RimWorld's own def inheritance -- an
    /// <c>Abstract="True"</c> palette as a parent, children declaring <c>ParentName</c> -- that is
    /// what makes templating work: a template supplies a whole look, and a variant overrides two
    /// colors and inherits the rest. Adding a palette or retuning one is an XML edit; no recompile.
    ///
    /// Reading colors:
    /// <code>
    /// Color bg = UIColorPaletteDef.Active.Get(UIColorRole.WindowBackground);
    /// Color bg = UIColorPaletteDef.Active.WindowBackground;              // same thing
    /// Color mine = UIColorPaletteDef.Active.Custom("MyMod.Highlight", Color.yellow);
    /// </code>
    ///
    /// A control should read <see cref="Active"/> at draw time rather than caching a color, so a
    /// palette change takes effect without the control being rebuilt. Controls that let a caller
    /// supply their own palette should default that parameter to <see cref="Active"/>, never to a
    /// hardcoded color.
    ///
    /// Full documentation, including the XML schema, is in the mod's Help folder.
    /// </summary>
    public class UIColorPaletteDef : Def
    {
        // ---------------------------------------------------------------------------------------
        // Authored fields. Strings rather than Color: hex is what a designer has to hand, and
        // UIColorParser can say which def and field were wrong. A null field means "keep the
        // built-in default for this role", which is what lets a palette name only what it changes.
        // ---------------------------------------------------------------------------------------

        public string windowBackground;
        public string panelBackground;
        public string surfaceRaised;
        public string surfaceSunken;
        public string border;
        public string borderFocused;
        public string textPrimary;
        public string textSecondary;
        public string textDisabled;
        public string accent;
        public string accentMuted;
        public string success;
        public string warning;
        public string danger;
        public string info;
        public string mood;
        public string tabTheDead;
        public string tabQuests;
        public string tabAnimals;
        public string tabPower;
        public string tabGrowing;
        public string tabBills;
        public string tabPawns;
        public string tabHospital;
        public string tabResearch;
        public string tabMechs;
        public string hoverOverlay;
        public string pressedOverlay;
        public string selectionOverlay;
        public string controlBackgroundFaded;
        public string hudBackground;

        /// <summary>
        /// Colors outside the fixed roles, for mods that need their own without a framework change.
        /// </summary>
        public List<UIColorEntry> custom = new List<UIColorEntry>();

        // ---------------------------------------------------------------------------------------
        // Optional button texture
        //
        // Unset -- the default -- means buttons are drawn flat from the palette's colors, which is the
        // whole point of the theme. A palette that wants textured buttons supplies a 9-slice atlas in
        // RimWorld's own layout, and it is used everywhere a button is drawn: vanilla buttons, option
        // rows, the main button bar and any control built on the framework. One texture, every button.
        //
        // Only buttonTexture is required. The hover and pressed variants fall back to it, and the
        // palette's hover and pressed washes are drawn over the top either way, so a single-image
        // palette still gets state feedback.
        // ---------------------------------------------------------------------------------------

        /// <summary>Texture path under any mod's Textures folder, without a file extension.</summary>
        public string buttonTexture;

        public string buttonTextureHover;
        public string buttonTexturePressed;

        /// <summary>
        /// Whether the state washes are drawn over a supplied texture. A palette whose hover and
        /// pressed images already carry their own feedback will want this off, or the wash doubles up.
        /// </summary>
        public bool buttonTextureUsesStateWash = true;

        private static readonly int RoleCount = Enum.GetNames(typeof(UIColorRole)).Length;

        private Color[] resolved;
        private Dictionary<string, Color> resolvedCustom;

        /// <summary>
        /// Throws away everything derived from the authored fields, so the next read re-parses them.
        ///
        /// For live editing: the framework can re-read a palette's XML and assign the string fields
        /// straight onto this instance, but the parsed colors are cached on first use and would go on
        /// being handed out. Nothing in normal play needs this.
        /// </summary>
        public void Invalidate()
        {
            resolved = null;
            resolvedCustom = null;
            buttonTexturesResolved = false;
            buttonTextureNormal = null;
            buttonTextureOver = null;
            buttonTextureDown = null;
        }

        private bool buttonTexturesResolved;
        private Texture2D buttonTextureNormal;
        private Texture2D buttonTextureOver;
        private Texture2D buttonTextureDown;

        /// <summary>
        /// True when this palette supplies a button texture, in which case a button is drawn as a
        /// 9-slice atlas rather than a flat fill.
        /// </summary>
        public bool HasButtonTexture
        {
            get
            {
                EnsureButtonTextures();
                return buttonTextureNormal != null;
            }
        }

        /// <summary>
        /// The atlas for the given button state, or null when this palette has no button texture.
        /// Hover and pressed fall back to the resting image, so one texture is enough.
        /// </summary>
        public Texture2D ButtonTexture(bool over, bool held)
        {
            EnsureButtonTextures();

            if (held && buttonTextureDown != null)
                return buttonTextureDown;

            if (over && buttonTextureOver != null)
                return buttonTextureOver;

            return buttonTextureNormal;
        }

        /// <summary>
        /// Resolved once. ContentFinder rather than UIImageLoader: a palette is a Def, so nothing can
        /// read it until def loading is finished, by which point mod textures are available through
        /// RimWorld's own cache and loading a second copy would only waste memory.
        /// </summary>
        private void EnsureButtonTextures()
        {
            if (buttonTexturesResolved)
                return;

            buttonTexturesResolved = true;
            buttonTextureNormal = Find(buttonTexture);
            buttonTextureOver = Find(buttonTextureHover);
            buttonTextureDown = Find(buttonTexturePressed);
        }

        private Texture2D Find(string path)
        {
            if (path.NullOrEmpty())
                return null;

            Texture2D found = ContentFinder<Texture2D>.Get(path, false);
            if (found == null)
                Log.ErrorOnce(UILogTag.Prefix + $"Palette '{defName}': no texture at '{path}'. "
                              + "Buttons will be drawn flat.", 0x17C0_10B2 ^ path.GetHashCode());

            return found;
        }

        // ---------------------------------------------------------------------------------------
        // Reading
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The color this palette assigns to <paramref name="role"/>. Never fails: an unnamed role
        /// gives the built-in default, and an unparseable one gives
        /// <see cref="UIColorParser.ErrorColor"/> after logging.
        /// </summary>
        public Color Get(UIColorRole role)
        {
            EnsureResolved();

            int index = (int) role;
            if (index < 0 || index >= resolved.Length)
                return UIColorParser.ErrorColor;

            return resolved[index];
        }

        /// <summary>
        /// A named color from <see cref="custom"/>, or <paramref name="fallback"/> when this palette
        /// does not define one. Supplying a fallback is required: a consumer's color is optional by
        /// nature, since another palette may not know about it.
        /// </summary>
        public Color Custom(string name, Color fallback)
        {
            return TryGetCustom(name, out Color color) ? color : fallback;
        }

        /// <summary>As <see cref="Custom"/>, but reports whether the palette defined the color.</summary>
        public bool TryGetCustom(string name, out Color color)
        {
            EnsureResolved();

            if (!name.NullOrEmpty() && resolvedCustom.TryGetValue(name, out color))
                return true;

            color = UIColorParser.ErrorColor;
            return false;
        }

        public Color WindowBackground => Get(UIColorRole.WindowBackground);
        public Color PanelBackground => Get(UIColorRole.PanelBackground);
        public Color SurfaceRaised => Get(UIColorRole.SurfaceRaised);
        public Color SurfaceSunken => Get(UIColorRole.SurfaceSunken);
        public Color Border => Get(UIColorRole.Border);
        public Color BorderFocused => Get(UIColorRole.BorderFocused);
        public Color TextPrimary => Get(UIColorRole.TextPrimary);
        public Color TextSecondary => Get(UIColorRole.TextSecondary);
        public Color TextDisabled => Get(UIColorRole.TextDisabled);
        public Color Accent => Get(UIColorRole.Accent);
        public Color AccentMuted => Get(UIColorRole.AccentMuted);
        public Color Success => Get(UIColorRole.Success);
        public Color Warning => Get(UIColorRole.Warning);
        public Color Danger => Get(UIColorRole.Danger);
        public Color Info => Get(UIColorRole.Info);

        /// <summary>A pawn's inner state: mood bars and the like. See <see cref="UIColorRole.Mood"/>.</summary>
        public Color Mood => Get(UIColorRole.Mood);

        /// <summary>The Dead tab's own color. See <see cref="UIColorRole.TabTheDead"/>.</summary>
        public Color TabTheDead => Get(UIColorRole.TabTheDead);

        /// <summary>The Quests tab's own color. See <see cref="UIColorRole.TabQuests"/>.</summary>
        public Color TabQuests => Get(UIColorRole.TabQuests);

        /// <summary>The Animals tab's own color. See <see cref="UIColorRole.TabAnimals"/>.</summary>
        public Color TabAnimals => Get(UIColorRole.TabAnimals);

        /// <summary>The Power tab's own color. See <see cref="UIColorRole.TabPower"/>.</summary>
        public Color TabPower => Get(UIColorRole.TabPower);

        /// <summary>The Growing Zones tab's own color. See <see cref="UIColorRole.TabGrowing"/>.</summary>
        public Color TabGrowing => Get(UIColorRole.TabGrowing);
        /// <summary>The Bills tab's own color. See <see cref="UIColorRole.TabBills"/>.</summary>
        public Color TabBills => Get(UIColorRole.TabBills);

        /// <summary>The Pawns tab's own color. See <see cref="UIColorRole.TabPawns"/>.</summary>
        public Color TabPawns => Get(UIColorRole.TabPawns);

        /// <summary>The Hospital tab's own color. See <see cref="UIColorRole.TabHospital"/>.</summary>
        public Color TabHospital => Get(UIColorRole.TabHospital);

        /// <summary>The Research tab's own color. See <see cref="UIColorRole.TabResearch"/>.</summary>
        public Color TabResearch => Get(UIColorRole.TabResearch);

        /// <summary>The Mechs tab's own color. See <see cref="UIColorRole.TabMechs"/>.</summary>
        public Color TabMechs => Get(UIColorRole.TabMechs);

        public Color HoverOverlay => Get(UIColorRole.HoverOverlay);
        public Color PressedOverlay => Get(UIColorRole.PressedOverlay);
        public Color SelectionOverlay => Get(UIColorRole.SelectionOverlay);

        /// <summary>
        /// The body of a control holding no value. See <see cref="UIColorRole.ControlBackgroundFaded"/> for why
        /// this is not one of the surface roles.
        /// </summary>
        public Color ControlBackgroundFaded => Get(UIColorRole.ControlBackgroundFaded);

        /// <summary>
        /// Fill for chrome drawn over the map. Carries its own alpha; see
        /// <see cref="UIColorRole.HudBackground"/>.
        /// </summary>
        public Color HudBackground => Get(UIColorRole.HudBackground);

        // ---------------------------------------------------------------------------------------
        // Resolution
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Parses the authored strings. Called from <see cref="ResolveReferences"/> so problems are
        /// logged once at startup, and lazily from <see cref="Get"/> so a palette built in code --
        /// including <see cref="BuiltIn"/>, which never passes through the def loader -- still works.
        /// </summary>
        private void EnsureResolved()
        {
            if (resolved != null)
                return;

            resolved = new Color[RoleCount];
            for (int i = 0; i < RoleCount; i++)
            {
                UIColorRole role = (UIColorRole) i;
                string authored = Authored(role);

                if (authored.NullOrEmpty())
                {
                    resolved[i] = DefaultFor(role);
                    continue;
                }

                if (UIColorParser.TryParse(authored, out Color parsed, out string error))
                {
                    resolved[i] = parsed;
                    continue;
                }

                Log.Error(UILogTag.Prefix + $"{defName}.{FieldNameOf(role)}: {error}. "
                          + "Using the error color so it is visible on screen.");
                resolved[i] = UIColorParser.ErrorColor;
            }

            resolvedCustom = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            if (custom == null)
                return;

            foreach (UIColorEntry entry in custom)
            {
                if (entry == null || entry.name.NullOrEmpty())
                {
                    Log.Error(UILogTag.Prefix + $"{defName} has a custom color with no name.");
                    continue;
                }

                if (UIColorParser.TryParse(entry.value, out Color parsed, out string error))
                {
                    resolvedCustom[entry.name] = parsed;
                    continue;
                }

                Log.Error(UILogTag.Prefix + $"{defName} custom color '{entry.name}': {error}.");
                resolvedCustom[entry.name] = UIColorParser.ErrorColor;
            }
        }

        /// <summary>
        /// Guarded even though <c>DefDatabase.ResolveAllReferences</c> catches per def. Vanilla's handler keeps the
        /// game loading, which is the important part, but it leaves this palette half resolved and says nothing about
        /// which mod or which theme -- and half a palette is worse than none, because the roles that did resolve look
        /// deliberate.
        ///
        /// Individual bad color strings are already handled inside EnsureResolved, which reports each one by field
        /// name and substitutes the error color. This is for whatever is not that.
        /// </summary>
        public override void ResolveReferences()
        {
            base.ResolveReferences();

            UIGuard.Try("Framework.ResolvePalette." + (defName ?? "unnamed"), EnsureResolved,
                "This theme is unusable and anything set to it falls back to the default palette.");
        }

        /// <summary>
        /// Load-time validation. RimWorld collects these into the startup log, which is where a
        /// palette author will look first, so every bad value is reported with its field name rather
        /// than only the first failure.
        /// </summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
                yield return error;

            for (int i = 0; i < RoleCount; i++)
            {
                UIColorRole role = (UIColorRole) i;
                string authored = Authored(role);
                if (authored.NullOrEmpty())
                    continue;

                if (!UIColorParser.TryParse(authored, out Color _, out string error))
                    yield return $"{FieldNameOf(role)}: {error}";
            }

            if (custom == null)
                yield break;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UIColorEntry entry in custom)
            {
                if (entry == null)
                {
                    yield return "custom contains an empty <li>";
                    continue;
                }

                if (entry.name.NullOrEmpty())
                {
                    yield return "custom entry has no <name>";
                    continue;
                }

                if (!seen.Add(entry.name))
                    yield return $"custom color '{entry.name}' is defined more than once";

                if (!UIColorParser.TryParse(entry.value, out Color _, out string error))
                    yield return $"custom color '{entry.name}': {error}";
            }
        }

        /// <summary>The authored string backing a role, or null when the palette does not name it.</summary>
        private string Authored(UIColorRole role)
        {
            return role switch
            {
                UIColorRole.WindowBackground => windowBackground,
                UIColorRole.PanelBackground => panelBackground,
                UIColorRole.SurfaceRaised => surfaceRaised,
                UIColorRole.SurfaceSunken => surfaceSunken,
                UIColorRole.Border => border,
                UIColorRole.BorderFocused => borderFocused,
                UIColorRole.TextPrimary => textPrimary,
                UIColorRole.TextSecondary => textSecondary,
                UIColorRole.TextDisabled => textDisabled,
                UIColorRole.Accent => accent,
                UIColorRole.AccentMuted => accentMuted,
                UIColorRole.Success => success,
                UIColorRole.Warning => warning,
                UIColorRole.Danger => danger,
                UIColorRole.Info => info,
                UIColorRole.Mood => mood,
                UIColorRole.TabTheDead => tabTheDead,
                UIColorRole.TabQuests => tabQuests,
                UIColorRole.TabAnimals => tabAnimals,
                UIColorRole.TabPower => tabPower,
                UIColorRole.TabGrowing => tabGrowing,
                UIColorRole.TabBills => tabBills,
                UIColorRole.TabPawns => tabPawns,
                UIColorRole.TabHospital => tabHospital,
                UIColorRole.TabResearch => tabResearch,
                UIColorRole.TabMechs => tabMechs,
                UIColorRole.HoverOverlay => hoverOverlay,
                UIColorRole.PressedOverlay => pressedOverlay,
                UIColorRole.SelectionOverlay => selectionOverlay,
                UIColorRole.ControlBackgroundFaded => controlBackgroundFaded,
                UIColorRole.HudBackground => hudBackground,
                _ => null
            };
        }

        /// <summary>The XML element name for a role, for error messages a palette author can act on.</summary>
        public static string FieldNameOf(UIColorRole role)
        {
            string name = role.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// The built-in value for a role, used whenever a palette leaves it unnamed.
        ///
        /// This is a complete, usable dark theme on its own, which matters for two reasons: a palette
        /// can be written as a two-line override of one color, and a control drawn before any def has
        /// loaded still gets sensible colors rather than transparent black.
        ///
        /// Values are 0xRRGGBBAA. The ramp is the one from Growing Zones Plus, itself following the
        /// Modern UI suite; see THIRD-PARTY-NOTICES.txt in the mod root.
        /// </summary>
        public static Color DefaultFor(UIColorRole role)
        {
            uint packed = role switch
            {
                UIColorRole.WindowBackground => 0x15191DFF,
                UIColorRole.PanelBackground => 0x1B1F23FF,
                UIColorRole.SurfaceRaised => 0x2F3337FF,
                UIColorRole.SurfaceSunken => 0x0E1013FF,
                UIColorRole.Border => 0x345673FF,
                UIColorRole.BorderFocused => 0x73BFFFFF,
                UIColorRole.TextPrimary => 0xE3E3E3FF,
                UIColorRole.TextSecondary => 0x9EA6B2FF,
                UIColorRole.TextDisabled => 0x6C7480FF,
                UIColorRole.Accent => 0x73BFFFFF,
                UIColorRole.AccentMuted => 0x274157FF,
                UIColorRole.Success => 0x61C461FF,
                UIColorRole.Warning => 0xCCA633FF,
                UIColorRole.Danger => 0xE54D33FF,
                UIColorRole.Info => 0x4A90D9FF,
                UIColorRole.Mood => 0x9B72D9FF,
                UIColorRole.TabTheDead => 0xA98FC8FF,
                UIColorRole.TabQuests => 0x7FA3C9FF,
                UIColorRole.TabAnimals => 0x98AC80FF,
                UIColorRole.TabPower => 0x74AFA6FF,
                UIColorRole.TabBills => 0xC4907AFF,
                UIColorRole.TabGrowing => 0xC0AE6AFF,
                UIColorRole.TabPawns => 0xC98BA4FF,
                UIColorRole.TabHospital => 0xCC8BC7FF,
                UIColorRole.TabResearch => 0x8B90CCFF,
                UIColorRole.TabMechs => 0x9FC6CEFF,
                UIColorRole.HoverOverlay => 0xFFFFFF0C,
                UIColorRole.PressedOverlay => 0xFFFFFF1F,
                UIColorRole.SelectionOverlay => 0x73BFFF24,

                // Deliberately not derived from SurfaceRaised. See the role's own notes: a control body and a
                // card surface want opposite things, and this ramp's raised surface is only twenty levels off
                // the panel -- close enough that a switch drawn in it has no visible extent.
                UIColorRole.ControlBackgroundFaded => 0x434A53FF,

                // WindowBackground at 0xCC, which is 80 percent. The darkest of the chrome fills rather than the
                // panel one: this is drawn over terrain rather than over another surface, so it has to carry its
                // own contrast for the text on it instead of borrowing a window's. Only the alpha marks it out as
                // over-the-map chrome. See the role's notes on why it is not lower.
                UIColorRole.HudBackground => 0x15191DCC,

                _ => 0xFF00FFFF
            };

            return new Color(
                ((packed >> 24) & 0xFF) / 255f,
                ((packed >> 16) & 0xFF) / 255f,
                ((packed >> 8) & 0xFF) / 255f,
                (packed & 0xFF) / 255f);
        }

        // ---------------------------------------------------------------------------------------
        // Registry
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The chosen palette, held as a defName rather than as a def reference. Two reasons: a def
        /// reload replaces every def instance, and a reference kept across one would style the UI from
        /// an object no longer in the database; and the theme selector has to persist the choice to
        /// settings, where a name is the only thing worth writing.
        /// </summary>
        private static string activeDefName;

        private static UIColorPaletteDef builtIn;

        /// <summary>
        /// defName of the palette this mod ships as its own look. Hardcoded rather than discovered:
        /// which palette is the default is this mod's decision, not something another mod should be
        /// able to win by declaring itself more important. A theme mod ships its palette and the
        /// player picks it; it does not get to take over on load.
        /// </summary>
        public const string DefaultPaletteDefName = "UIPalette_Default";

        /// <summary>
        /// The palette controls draw from. Assigning null reverts to <see cref="Default"/>.
        ///
        /// This is a global on purpose: a theme applies to everything, which is the point. A control
        /// that must not follow the global theme should take a palette as a parameter instead.
        ///
        /// This is the seam the theme selector uses. Nothing else should need to assign it.
        /// </summary>
        public static UIColorPaletteDef Active
        {
            get
            {
                if (!activeDefName.NullOrEmpty())
                {
                    // Missing means the theme's mod was removed, or defs have not loaded yet. Falling
                    // through to Default is right either way, and the name is kept rather than cleared
                    // so re-enabling the mod restores the choice.
                    UIColorPaletteDef chosen = Named(activeDefName);
                    if (chosen != null)
                        return chosen;
                }

                return Default;
            }
            set => activeDefName = value?.defName;
        }

        /// <summary>
        /// The chosen palette's defName, or null when none has been chosen. This is what the theme
        /// selector should save and load; it survives a def reload and an absent mod, which a
        /// <see cref="UIColorPaletteDef"/> reference does not.
        /// </summary>
        public static string ActiveDefName
        {
            get => activeDefName;
            set => activeDefName = value;
        }

        /// <summary>
        /// True when a palette was chosen but is not currently loaded, so <see cref="Active"/> is
        /// quietly serving <see cref="Default"/> instead.
        ///
        /// The name is deliberately not cleared when this happens. Disabling a theme's mod is usually
        /// temporary, and clearing would destroy the player's choice on a round trip they expected to
        /// be harmless. It also cannot be cleared safely on mismatch alone: during startup no def is
        /// loaded yet, so a clear-on-mismatch rule would wipe the setting on every launch.
        ///
        /// This exists so the theme selector can say so out loud -- "the theme you picked is not
        /// available, showing the default" -- rather than silently showing the wrong theme. It stays
        /// false until def loading has finished, because an unresolved name before then means nothing.
        /// </summary>
        public static bool ActiveIsMissing =>
            PlayDataLoader.Loaded && !activeDefName.NullOrEmpty() && Named(activeDefName) == null;

        /// <summary>
        /// The palette used when nothing has chosen one: <see cref="DefaultPaletteDefName"/>, falling
        /// back to <see cref="BuiltIn"/> if that def is missing.
        ///
        /// BuiltIn rather than "any palette that happens to be loaded": it carries the same colors as
        /// the shipped default, so the fallback looks right instead of looking like whichever theme mod
        /// sorted first.
        ///
        /// Not cached, so a def reload is picked up without anything having to invalidate a field. The
        /// lookup is a dictionary hit.
        /// </summary>
        public static UIColorPaletteDef Default
        {
            get
            {
                // Safe to call at any time, but only because Named refuses to touch the def database
                // while it is being written. It is not safe to read DefDatabase during a load; see
                // DefsReadable.
                UIColorPaletteDef shipped = Named(DefaultPaletteDefName);
                if (shipped != null)
                    return shipped;

                // No def yet is the ordinary state early in startup: defs are not created until
                // LoadedModManager.ParseAndProcessXML, which runs well after the loading screen starts
                // drawing. Complaining then would put a false "broken install" error in every log, so
                // the check waits until def loading has actually finished -- at which point a missing
                // palette really is broken. ErrorOnce because this is read while drawing.
                if (PlayDataLoader.Loaded)
                {
                    Log.ErrorOnce(
                        UILogTag.Prefix + $"No palette named '{DefaultPaletteDefName}' is loaded. "
                        + "Falling back to the compiled-in palette; check that the mod's Defs folder is intact.",
                        0x17C0_10AB);
                }

                return BuiltIn;
            }
        }

        /// <summary>
        /// The code-defined palette of <see cref="DefaultFor"/> values. Not in the def database and
        /// not overridable, so it is always available as a last resort.
        ///
        /// This is what covers the startup window before any def exists. RimWorld creates defs in
        /// LoadedModManager.ParseAndProcessXML, and the only mod code that runs earlier is the Mod
        /// constructor in CreateModClasses -- which is before LoadModXML has read a single file. No
        /// patch can move a def ahead of the XML it is made from, so anything drawn during the loading
        /// screen has to come from compiled-in values. These are those values, and they are kept equal
        /// to the shipped UIPalette_Default so the two are indistinguishable on screen.
        /// </summary>
        public static UIColorPaletteDef BuiltIn
        {
            get
            {
                if (builtIn == null)
                {
                    builtIn = new UIColorPaletteDef
                    {
                        defName = "UIPalette_BuiltIn",
                        label = "built-in",
                        description = "The framework's compiled-in fallback palette."
                    };
                }
                return builtIn;
            }
        }

        /// <summary>
        /// Whether the def database can be read right now.
        ///
        /// It cannot be read during a load, and the reason is threading rather than emptiness -- which is
        /// what an earlier version of this file assumed, at the cost of an intermittent crash on the
        /// loading screen.
        ///
        /// <c>LongEventHandler.UpdateCurrentAsynchronousEvent</c> starts the event action on its own
        /// thread and keeps calling <c>LongEventsOnGUI</c> on the main thread while it runs. Play data
        /// loading is one of those events, so <c>DefDatabase&lt;T&gt;.defsByName</c> is being *written* by
        /// the loader thread at the same time as the loading screen draws and reads it. The dictionary is
        /// never null -- it is assigned in DefDatabase's static constructor -- but a read that lands
        /// during a resize dereferences a half-swapped bucket array and throws NullReferenceException
        /// from inside <c>Dictionary.FindEntry</c>. Being a race, it strikes only sometimes, which makes
        /// it look like an unrelated regression when it does.
        ///
        /// No null check can fix that; the only fix is not to read at all until the writing has stopped.
        /// <see cref="BuiltIn"/> is what covers the gap, and it carries the same colors as the shipped
        /// default so the loading screen looks the same either way.
        ///
        /// One consequence worth knowing: a player using a custom theme gets the built-in colors on the
        /// startup loading screen rather than their theme, because no def exists to read yet. Long events
        /// later in a session -- generating a map, loading a save -- are after loading has finished, so
        /// those do use the chosen theme.
        /// </summary>
        private static bool DefsReadable => PlayDataLoader.Loaded;

        /// <summary>
        /// The palette with this defName, or null. Use it to read a specific theme's colors without
        /// disturbing <see cref="Active"/>.
        ///
        /// Returns null rather than throwing while defs are loading. This is the single choke point for
        /// every def lookup here -- <see cref="Active"/>, <see cref="Default"/> and
        /// <see cref="ActiveIsMissing"/> all come through it -- so guarding it guards all of them.
        /// </summary>
        public static UIColorPaletteDef Named(string defName)
        {
            return defName.NullOrEmpty() || !DefsReadable
                ? null
                : DefDatabase<UIColorPaletteDef>.GetNamedSilentFail(defName);
        }

        /// <summary>
        /// Every loaded palette. Abstract templates are not included; RimWorld never loads them as defs.
        ///
        /// Empty while defs are loading, for the same reason as <see cref="Named"/>: the backing list is
        /// being appended to on another thread. A static empty list rather than a new one per call, since
        /// this is reachable from drawing code.
        /// </summary>
        public static List<UIColorPaletteDef> All =>
            DefsReadable ? DefDatabase<UIColorPaletteDef>.AllDefsListForReading : none;

        private static readonly List<UIColorPaletteDef> none = new List<UIColorPaletteDef>();
    }
}
