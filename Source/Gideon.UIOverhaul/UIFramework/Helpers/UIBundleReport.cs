using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Gideon.UIFramework.Stages;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// One block in the log saying exactly why the asset bundle did or did not deliver its art.
    ///
    /// <b>Written because the failure was silent.</b> A bundle that will not load, a bundle that loads but
    /// holds nothing we recognize, and a manifest whose baked folder name disagrees with the package id all
    /// produced the same thing on a player's machine: no art, and nothing in the log beyond RimWorld's own
    /// one line. Three very different causes with one symptom is a support conversation measured in days,
    /// because every question has to travel to the player and back.
    ///
    /// <b>Every line here exists to rule something out.</b> The runtime line separates a Unity mismatch from
    /// everything else; the pack line separates a Workshop install from a local one and shows whether the
    /// Steam suffix has moved the package id out from under the manifest lookup; the file lines separate a
    /// corrupt or partial download from a valid one by reading the bundle's own header rather than trusting
    /// that a file of the right name is the right file; the bundle lines say whether Unity accepted it; and
    /// the manifest line says whether the path we look up is the path that was baked in, printing the real
    /// asset names when it is not.
    ///
    /// <b>It runs only when something is actually wrong,</b> so a working install pays nothing for it. The
    /// hash is the one deliberately expensive part, and it is here because it is the only way to answer "is
    /// your copy the same as mine" without another round trip: SHA256 is what <c>Get-FileHash</c> and
    /// <c>certutil</c> produce, so the number in the log can be compared with one a player runs themselves.
    /// </summary>
    internal static class UIBundleReport
    {
        /// <summary>The folder RimWorld looks in, from <c>GenFilePaths.ContentPath</c>.</summary>
        private const string BundleFolder = "AssetBundles/";

        private const string ManifestName = "_paths.txt";

        /// <summary>
        /// Whether <paramref name="mod"/> is this mod, so the report never speaks for somebody else's
        /// content. The registration patch runs for every pack that loads.
        /// </summary>
        internal static bool IsOurs(ModContentPack mod)
        {
            return mod != null
                   && string.Equals(mod.PackageIdPlayerFacing, UILoadingScreenConfig.OwnerPackageId,
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes the report for <paramref name="mod"/>. <paramref name="registered"/> is what
        /// <see cref="UIBundledTextures.Register"/> managed to add, and leads the block because it is the
        /// symptom everything below is explaining.
        /// </summary>
        internal static string Compose(ModContentPack mod, int registered)
        {
            StringBuilder report = new StringBuilder();

            report.Append("Asset bundle report: registered ").Append(registered)
                .Append(registered == 1 ? " texture." : " textures.");

            Runtime(report);
            Pack(report, mod);
            Files(report, mod);
            Bundles(report, mod);

            return report.ToString();
        }

        private static void Runtime(StringBuilder report)
        {
            // Unity first, because a bundle is built by a specific Unity and refuses to load in an older one.
            // The game's own version is here rather than assumed from it: a player on an earlier RimWorld is
            // on an earlier Unity, and that single pairing explains a whole class of reports.
            Line(report, "runtime", "Unity " + Application.unityVersion
                                              + "   RimWorld " + Version()
                                              + "   " + Application.platform);
        }

        private static string Version()
        {
            return UIGuard.Try("Bundle.Version",
                () => RimWorld.VersionControl.CurrentVersionStringWithRev, "unknown", null);
        }

        private static void Pack(StringBuilder report, ModContentPack mod)
        {
            // Both ids, because they differ on a Workshop install: PackageId gains a _steam suffix and
            // PackageIdPlayerFacing does not. The manifest is looked up under the player facing one, so
            // seeing the pair side by side is what proves the lookup is using the name that was baked in.
            Line(report, "pack", mod.PackageIdPlayerFacing
                                 + "   id " + mod.PackageId
                                 + "   load order " + mod.loadOrder);

            Line(report, "root", mod.RootDir.NullOrEmpty() ? "none" : mod.RootDir);

            Duplicates(report, mod);
        }

        /// <summary>
        /// Any other loaded pack claiming the same player facing id, which is a subscribed copy and a manual
        /// copy installed at once. Two copies of one mod is its own family of impossible looking bugs.
        /// </summary>
        private static void Duplicates(StringBuilder report, ModContentPack mod)
        {
            List<string> others = new List<string>();

            UIGuard.Try("Bundle.Duplicates", () =>
            {
                List<ModContentPack> running = LoadedModManager.RunningModsListForReading;

                for (int i = 0; running != null && i < running.Count; i++)
                {
                    ModContentPack other = running[i];

                    if (other == null || other == mod)
                        continue;

                    if (string.Equals(other.PackageIdPlayerFacing, mod.PackageIdPlayerFacing,
                            StringComparison.OrdinalIgnoreCase))
                        others.Add(other.RootDir.NullOrEmpty() ? other.PackageId : other.RootDir);
                }
            });

            if (others.Count > 0)
                Line(report, "DUPLICATE", "this mod is also loaded from " + string.Join("  and  ", others.ToArray()));
        }

        /// <summary>
        /// Every file RimWorld itself would consider a bundle, with its size, its hash and the version
        /// stamped in its own header.
        ///
        /// <b>Enumerated exactly the way the game enumerates it,</b> through <c>GetAllFilesForMod</c> with
        /// the same empty extension rule <c>ModAssetBundlesHandler</c> uses. A report that walked the folder
        /// its own way could list a file the game never looked at, which would send the reader somewhere the
        /// bug is not. If nothing is listed here, the game found nothing to load, and the question is the
        /// folder rather than the bundle.
        /// </summary>
        private static void Files(StringBuilder report, ModContentPack mod)
        {
            Dictionary<string, FileInfo> found = UIGuard.Try("Bundle.Files",
                () => ModContentPack.GetAllFilesForMod(mod, BundleFolder, extension => extension.NullOrEmpty()),
                null, null);

            if (found == null || found.Count == 0)
            {
                Line(report, "files", "none found under " + BundleFolder
                                      + "  (a bundle needs no file extension to be seen)");

                return;
            }

            foreach (KeyValuePair<string, FileInfo> entry in found)
            {
                FileInfo file = entry.Value;

                if (file == null)
                    continue;

                Line(report, "file", file.Name
                                     + "   " + file.Length.ToString("N0") + " bytes"
                                     + "   " + Header(file)
                                     + "   sha256 " + Hash(file));
            }
        }

        /// <summary>
        /// The Unity version written into the file's own first bytes.
        ///
        /// A bundle begins <c>UnityFS</c>, a null, a four byte format number, then two null terminated
        /// strings: a generation and the exact editor revision that built it. Reading them costs sixty odd
        /// bytes and answers the two questions worth asking of a file that would not load, which are whether
        /// it is a bundle at all and which Unity expects to open it.
        /// </summary>
        private static string Header(FileInfo file)
        {
            return UIGuard.Try("Bundle.Header", () =>
            {
                using (FileStream stream = file.OpenRead())
                {
                    byte[] buffer = new byte[96];
                    int read = stream.Read(buffer, 0, buffer.Length);

                    if (read < 16)
                        return "TRUNCATED, only " + read + " bytes readable";

                    if (Encoding.ASCII.GetString(buffer, 0, 7) != "UnityFS")
                        return "NOT A BUNDLE, begins " + Opening(buffer, read);

                    // Signature and its null, then the four byte format number, then the two strings.
                    int at = 12;

                    string generation = Text(buffer, ref at, read);
                    string revision = Text(buffer, ref at, read);

                    return "built by Unity " + (revision.Length == 0 ? "unstated" : revision)
                                             + " (" + generation + ")";
                }
            }, "unreadable", null);
        }

        /// <summary>Reads a null terminated ASCII string out of <paramref name="buffer"/>.</summary>
        private static string Text(byte[] buffer, ref int at, int length)
        {
            int start = at;

            while (at < length && buffer[at] != 0)
                at++;

            string value = Encoding.ASCII.GetString(buffer, start, at - start);

            // Step past the terminator, so the next read starts on the next string.
            if (at < length)
                at++;

            return value;
        }

        /// <summary>The first bytes as hex, for a file that is not what it claims to be.</summary>
        private static string Opening(byte[] buffer, int read)
        {
            StringBuilder hex = new StringBuilder();

            for (int i = 0; i < 8 && i < read; i++)
                hex.Append(buffer[i].ToString("x2")).Append(' ');

            return hex.ToString().Trim();
        }

        /// <summary>
        /// SHA256 of the whole file, so a player's copy can be compared with the one that was published.
        /// The choice of algorithm is practical rather than cryptographic: it is what <c>Get-FileHash</c>
        /// defaults to, so the comparison can be run on the other machine without any tool being installed.
        /// </summary>
        private static string Hash(FileInfo file)
        {
            return UIGuard.Try("Bundle.Hash", () =>
            {
                using (FileStream stream = file.OpenRead())
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(stream);
                    StringBuilder hex = new StringBuilder(digest.Length * 2);

                    for (int i = 0; i < digest.Length; i++)
                        hex.Append(digest[i].ToString("x2"));

                    return hex.ToString();
                }
            }, "unreadable", null);
        }

        /// <summary>
        /// What Unity actually loaded, and whether the manifest we look for is in it.
        ///
        /// <b>The real asset names are printed when the manifest is missing,</b> because that is the case
        /// where the reader needs to see the prefix the bundle was baked with rather than the one being
        /// asked for. The two disagreeing is a silent total failure, and a sample of three names makes it
        /// obvious at a glance.
        /// </summary>
        private static void Bundles(StringBuilder report, ModContentPack mod)
        {
            List<AssetBundle> loaded = mod.assetBundles == null ? null : mod.assetBundles.loadedAssetBundles;

            if (loaded == null || loaded.Count == 0)
            {
                Line(report, "loaded", "no bundle loaded, so Unity rejected every file above");

                return;
            }

            string manifestPath = "Assets/Data/" + mod.PackageIdPlayerFacing + "/" + ManifestName;

            Line(report, "manifest", "looking for " + manifestPath);

            for (int i = 0; i < loaded.Count; i++)
            {
                AssetBundle bundle = loaded[i];

                if (bundle == null)
                {
                    Line(report, "loaded", "a null entry, which is a bundle that failed to load");

                    continue;
                }

                string[] names = UIGuard.Try("Bundle.Names", () => bundle.GetAllAssetNames(), null, null);
                bool manifest = UIGuard.Try("Bundle.Manifest",
                    () => bundle.LoadAsset<TextAsset>(manifestPath) != null, false, null);

                Line(report, "loaded", bundle.name
                                       + "   " + (names == null ? 0 : names.Length) + " assets"
                                       + "   manifest " + (manifest ? "FOUND" : "missing"));

                if (manifest || names == null || names.Length == 0)
                    continue;

                for (int n = 0; n < 3 && n < names.Length; n++)
                    Line(report, "  holds", names[n]);
            }
        }

        private static void Line(StringBuilder report, string label, string value)
        {
            report.Append('\n').Append("  ").Append(label.PadRight(10)).Append("  ").Append(value);
        }
    }
}
