using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Research
{
    /// <summary>
    /// The glyphs an undiscovered Anomaly project is drawn in, and which projects get them.
    ///
    /// <b>Which projects: the ones RimWorld itself will not name.</b> <c>ResearchProjectDef.IsHidden</c> asks
    /// <c>Find.EntityCodex.Hidden</c>, which is true until the entity that explains the project has been
    /// discovered -- and until the monolith has been woken, for every knowledge project at once. Vanilla already
    /// refuses to name these: it draws "(Unknown research)" in grey and will not let the node be clicked. So this
    /// is not new secrecy, it is the same secret told properly.
    ///
    /// <b>Not <c>requiredAnalyzed</c>, which is what the mockup said and is a different thing.</b> That list is a
    /// Biotech mechanic -- <c>PostLoad</c> clears it outright without Biotech -- and it gates projects whose names
    /// the game shows you along with the thing to go and analyze. Masking those would hide information RimWorld
    /// gives away, and would leave the actual unknown-research state drawn as an ordinary grey box. The analyzed
    /// gate gets a chip naming the thing instead; see <see cref="ResearchFacts"/>.
    ///
    /// <b>A run of marks, not a substitution.</b> A mask carries no information about the text it stands in for:
    /// not its length, not its word count, not its letters. It is a random run drawn from the chosen script and
    /// fitted to the room the field has, so a masked node is exactly the size of an unmasked one and nothing in
    /// the graph moves when a discovery lands.
    ///
    /// <b>Keyed by project and field, not by the text.</b> Asked for on 2026-08-23: every masked string gets its
    /// own run and two projects never share one. Keying on the text would have given every project in the game
    /// the same run of marks for the word "Unlocks", which is the rubber-stamp look the instruction was about.
    ///
    /// <b>The run is stored as raw numbers rather than as indices into a script.</b> Each mark is chosen at draw
    /// time by taking the number modulo however many marks the running script has, which means changing the
    /// script re-letters every mask with no cache to clear and no chance of an index pointing past the end of a
    /// smaller alphabet.
    /// </summary>
    internal static class ResearchMask
    {
        /// <summary>
        /// How many marks a stored run holds.
        ///
        /// Generous rather than exact: the number actually drawn is however many fit the field, and the widest
        /// field this mod masks is the detail panel's title at two hundred and thirty pixels. Thirty-two is past
        /// that at any font size, so no field is ever short of marks.
        /// </summary>
        private const int RunLength = 32;

        /// <summary>Air between two marks, as a fraction of the cell.</summary>
        private const float Tracking = 0.18f;

        private static readonly Dictionary<string, int[]> Runs = new Dictionary<string, int[]>();

        private static readonly Dictionary<ResearchScript, ResearchScriptAtlas> Atlases =
            new Dictionary<ResearchScript, ResearchScriptAtlas>();

        /// <summary>
        /// One generator for the whole session, which is what makes every run different from every other.
        ///
        /// Seeded by the framework rather than from the clock, so nothing here reaches for <c>DateTime.Now</c>.
        /// A fresh <c>System.Random</c> per key would hand out the same sequence to every key created in the same
        /// millisecond, which is precisely the rubber stamp this is meant to avoid.
        /// </summary>
        private static readonly System.Random Roll = new System.Random();

        /// <summary>The script in force, or Off. Read from the settings on every ask; it is a field lookup.</summary>
        internal static ResearchScript Script
        {
            get
            {
                UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;
                ResearchScript wanted = settings == null ? ResearchScript.Generated : settings.anomalyScript;

                // An option whose atlas is not on disk reads as Generated rather than as nothing at all. The
                // picker does not offer it either, so this only happens to a hand-edited config or an install
                // missing its Fonts folder.
                if (wanted == ResearchScript.Off || wanted == ResearchScript.Generated)
                    return wanted;

                return Usable(wanted) ? wanted : ResearchScript.Generated;
            }
        }

        /// <summary>Whether a script can actually be drawn, which for the three faces means its atlas loaded.</summary>
        internal static bool Usable(ResearchScript script)
        {
            if (script == ResearchScript.Off)
                return true;

            if (script == ResearchScript.Generated)
                return ResearchGlyphs.MarkCount > 0;

            ResearchScriptAtlas atlas = AtlasFor(script);

            return atlas != null && atlas.Available;
        }

        /// <summary>
        /// Whether this project's text is hidden from the player.
        ///
        /// <b>Guarded, because <c>IsHidden</c> reads the entity codex and the anomaly tracker</b> -- two pieces of
        /// game state that do not exist outside a game with Anomaly active. A failure here means the project is
        /// treated as readable, which is what vanilla would have shown anyway.
        /// </summary>
        internal static bool Masked(ResearchProjectDef project)
        {
            if (project == null || Script == ResearchScript.Off)
                return false;

            return UIGuard.Try("Research.Masked", () => project.IsHidden, false, null);
        }

        /// <summary>
        /// Draws a masked field, filling the band it is given.
        ///
        /// <paramref name="key"/> identifies the field rather than describing it: a project's defName and the name
        /// of the field, so the same field on the same project is the same marks for the whole session and no two
        /// fields anywhere share a run.
        /// </summary>
        internal static void Draw(Rect band, string key, Color color, GameFont font = GameFont.Tiny)
        {
            Run(band, key, Script, color, font);
        }

        /// <summary>
        /// A sample of one script, whatever the setting currently is.
        ///
        /// For the picker, which has to show what a script looks like before it is chosen. Keyed by the script so
        /// the swatch does not re-letter itself while somebody is comparing four of them.
        /// </summary>
        internal static void Sample(Rect band, ResearchScript script, Color color)
        {
            Run(band, "sample/" + script, script, color, GameFont.Tiny);
        }

        private static void Run(Rect band, string key, ResearchScript script, Color color, GameFont font)
        {
            if (band.width <= 4f || band.height <= 2f || script == ResearchScript.Off)
                return;

            int[] run = Run(key);
            int alphabet = Alphabet(script);

            if (run == null || alphabet <= 0)
                return;

            // Sized from the line rather than from the band, so a mark in a tall row sits on the text's own
            // centre line at the same size as the letters it stands in for.
            float cell = Mathf.Min(band.height - 2f, Mathf.Max(7f, UIFonts.LineHeightOf(font) - 4f));
            float step = cell * (1f + Tracking);

            int room = Mathf.FloorToInt((band.width + cell * Tracking) / step);

            if (room <= 0)
                return;

            // Variable length, which is the other half of "it always fits": a run that filled every field to the
            // pixel would read as a bar rather than as writing. The fraction is drawn from the run itself, so a
            // field's length is as stable as its marks are.
            float fraction = 0.62f + (run[0] & 0xFF) / 255f * 0.38f;
            int count = Mathf.Clamp(Mathf.RoundToInt(room * fraction), 1, Mathf.Min(room, run.Length));

            float y = band.y + (band.height - cell) * 0.5f;

            Color previous = GUI.color;
            ResearchScriptAtlas atlas = script == ResearchScript.Generated ? null : AtlasFor(script);

            GUI.color = color;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    Rect at = new Rect(band.x + i * step, y, cell, cell);
                    int index = run[i] % alphabet;

                    if (atlas != null)
                        atlas.Draw(index, at);
                    else if (ResearchGlyphs.Marks != null)
                        GUI.DrawTexture(at, ResearchGlyphs.Marks[index]);
                }
            }
            finally
            {
                GUI.color = previous;
            }
        }

        /// <summary>
        /// How much room a masked field wants, for a caller laying one out beside something else.
        ///
        /// It is the whole band, always: a mask has no natural width, so anything asking is really asking how
        /// much it may have.
        /// </summary>
        internal static float WidthOf(float available)
        {
            return Mathf.Max(0f, available);
        }

        /// <summary>
        /// Fills the cache for everything currently hidden.
        ///
        /// <b>Called when the tab opens rather than from a static constructor,</b> because whether a project is
        /// hidden is a fact about a game in progress: the entity codex does not exist until a save is loaded, and
        /// it changes as entities are discovered. Once per open is the same practical effect as once per load --
        /// no run is ever built during a draw of a node that was already on screen -- without pretending this is
        /// static data.
        ///
        /// A project that becomes hidden later cannot happen; one that stops being hidden keeps its cache entry,
        /// which costs a few hundred bytes and saves the case of a discovery being undone by dev tools.
        /// </summary>
        internal static void Prime()
        {
            if (Script == ResearchScript.Off)
                return;

            UIGuard.Try("Research.PrimeMasks", () =>
            {
                List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

                for (int i = 0; i < all.Count; i++)
                {
                    ResearchProjectDef project = all[i];

                    if (!Masked(project))
                        continue;

                    Run(Key(project, "name"));
                    Run(Key(project, "cost"));
                    Run(Key(project, "meta"));
                }
            }, null);
        }

        /// <summary>The cache key for one field of one project.</summary>
        internal static string Key(ResearchProjectDef project, string field)
        {
            return (project == null ? "?" : project.defName) + "/" + field;
        }

        /// <summary>The cache key for a numbered line, such as the third thing a project unlocks.</summary>
        internal static string Key(ResearchProjectDef project, string field, int index)
        {
            return Key(project, field) + index.ToString();
        }

        private static int[] Run(string key)
        {
            if (key == null)
                key = string.Empty;

            int[] existing;

            if (Runs.TryGetValue(key, out existing))
                return existing;

            int[] made = new int[RunLength];

            for (int i = 0; i < RunLength; i++)
                made[i] = Roll.Next(0, 1 << 24);

            Runs[key] = made;

            return made;
        }

        private static int Alphabet(ResearchScript script)
        {
            if (script == ResearchScript.Off)
                return 0;

            if (script == ResearchScript.Generated)
                return ResearchGlyphs.MarkCount;

            ResearchScriptAtlas atlas = AtlasFor(script);

            return atlas == null ? 0 : atlas.Count;
        }

        private static ResearchScriptAtlas AtlasFor(ResearchScript script)
        {
            string file = ResearchScripts.AtlasFor(script);

            if (file == null)
                return null;

            ResearchScriptAtlas existing;

            if (Atlases.TryGetValue(script, out existing))
                return existing;

            existing = new ResearchScriptAtlas(file);
            Atlases[script] = existing;

            if (!existing.Available)
            {
                // Ours to ship, so a miss is a packaging fault rather than anything the player did. Said once,
                // because the picker asks this every frame it is open.
                Log.Error(UILogTag.Prefix + "Missing Fonts/" + file + ".png or .txt. The "
                          + ResearchScripts.Named(script) + " option is not offered and unknown research is "
                          + "written in the generated marks instead.");
            }

            return existing;
        }
    }
}
