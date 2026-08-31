using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Says, once, whether every typeface the mod draws with actually arrived.
    ///
    /// <b>Written because a missing face is invisible.</b> <see cref="UITextControl"/> falls through to
    /// RimWorld's own font whenever a bundled one cannot be resolved, which is the right behavior, and it is
    /// also completely silent: a screen set in Oswald and IBM Plex Mono renders in Arial and looks like a
    /// screen somebody forgot to style rather than one whose fonts did not load. The same words, the same
    /// layout, the same colors. There is nothing to notice.
    ///
    /// <b>The names are the point.</b> Unity lower cases the names it stores and the mapping in
    /// <c>UIFaces</c> spells them as the files are spelled, so a face can fail for nothing more than a
    /// capital letter. Printing what was asked for beside what the bundles actually hold turns that from a
    /// guess into a diff anybody can read.
    ///
    /// <b>Quiet when everything works.</b> One line on success, the full block only on failure, so the cost
    /// to a healthy install is a single message.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class UIFontReport
    {
        static UIFontReport()
        {
            UIGuard.Try("Fonts.Report", Write,
                "The font report did not run. It is a diagnostic only, and nothing else depends on it.");
        }

        private static void Write()
        {
            List<string> missing = new List<string>();
            StringBuilder rows = new StringBuilder();

            int total = 0;

            foreach (UIFace face in Enum.GetValues(typeof(UIFace)))
            {
                // The game's own font is not ours to resolve and has no asset behind it.
                if (face == UIFace.Game)
                    continue;

                total++;

                FontStyle ignored;
                Font font = UIGuard.Try("Fonts.Report.Face",
                    () => UIFaces.FontFor(face, FontStyle.Normal, out ignored), null, null);

                bool ok = font != null;

                if (!ok)
                    missing.Add(UIFaces.Named(face));

                rows.Append('\n').Append("  ").Append(face.ToString().PadRight(20))
                    .Append(ok ? "resolved as " + font.name : "MISSING");
            }

            if (missing.Count == 0)
            {
                Log.Message(UILogTag.Prefix + "All " + total
                            + " bundled typefaces resolved. Text is drawing in the mod's own faces.");

                return;
            }

            StringBuilder report = new StringBuilder();

            report.Append("Font report: ").Append(missing.Count).Append(" of ").Append(total)
                .Append(" typefaces did not resolve, so anything set in them is drawing in RimWorld's font ")
                .Append("instead: ").Append(string.Join(", ", missing.ToArray()));

            report.Append(rows);

            Holdings(report);

            Log.Warning(UILogTag.Prefix + report);
        }

        /// <summary>
        /// The font assets our bundles actually carry.
        ///
        /// <b>Names rather than loaded fonts,</b> because <c>GetAllAssetNames</c> reads the bundle's own
        /// directory and loading every face to list them would cost more than the whole report. The spelling
        /// is what matters here anyway: this list is meant to be compared against the names above it.
        /// </summary>
        private static void Holdings(StringBuilder report)
        {
            UIGuard.Try("Fonts.Report.Holdings", () =>
            {
                foreach (ModContentPack mod in LoadedModManager.RunningMods)
                {
                    if (mod == null || mod.assemblies == null || mod.assemblies.loadedAssemblies == null)
                        continue;

                    if (!Ours(mod) || mod.assetBundles == null || mod.assetBundles.loadedAssetBundles == null)
                        continue;

                    if (mod.assetBundles.loadedAssetBundles.Count == 0)
                    {
                        report.Append('\n').Append("  bundles             none loaded, which is the whole answer");

                        return;
                    }

                    foreach (AssetBundle bundle in mod.assetBundles.loadedAssetBundles)
                    {
                        if (bundle == null)
                            continue;

                        string[] names = bundle.GetAllAssetNames();
                        int fonts = 0;

                        for (int i = 0; names != null && i < names.Length; i++)
                        {
                            if (!names[i].EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                                && !names[i].EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                                continue;

                            fonts++;

                            report.Append('\n').Append("  holds               ").Append(names[i]);
                        }

                        if (fonts == 0)
                        {
                            report.Append('\n').Append("  bundle              ").Append(bundle.name)
                                .Append(" holds no font at all (")
                                .Append(names == null ? 0 : names.Length).Append(" assets)");
                        }
                    }

                    return;
                }

                report.Append('\n').Append("  bundles             this mod's content pack was not found");
            });
        }

        /// <summary>Whether <paramref name="mod"/> is the pack this assembly shipped in.</summary>
        private static bool Ours(ModContentPack mod)
        {
            foreach (System.Reflection.Assembly loaded in mod.assemblies.loadedAssemblies)
            {
                if (loaded == typeof(UIFontReport).Assembly)
                    return true;
            }

            return false;
        }
    }
}
