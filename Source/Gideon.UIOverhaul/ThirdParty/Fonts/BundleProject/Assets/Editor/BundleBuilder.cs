using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the mod's AssetBundle, headless.
///
/// Run from the repo, never by opening the editor:
///   Unity.exe -batchmode -nographics -projectPath BundleProject -executeMethod BundleBuilder.Build -logFile log
///
/// Every TTF under Assets/Fonts is imported as a dynamic font with its file data included, which is what makes
/// the result a real FreeType-backed font at runtime -- the same machinery RimWorld's own text uses.
///
/// <b>Only the file named <see cref="Bundle"/> may be copied into the mod.</b> The output folder also gets a
/// per-bundle .manifest, which RimWorld ignores, and a root manifest bundle named after the output folder
/// itself -- and that one has no extension either, so copying it would have RimWorld load a bundle of build
/// bookkeeping. Copy the one file, nothing else.
///
/// The bundle is written suffix-less, because RimWorld loads a bundle without an OS suffix on every platform
/// and font data is platform agnostic. Fonts are what it carries today; the name is deliberately not about
/// fonts, so textures can join them without another rename.
/// </summary>
public static class BundleBuilder
{
    /// <summary>
    /// The shipped bundle's file name. Nothing in the mod's C# names it -- the font loader searches every
    /// bundle our mod has loaded -- so this string and the file on disk are the only places it appears.
    /// </summary>
    private const string Bundle = "ui_overhaul_assets";

    public static void Build()
    {
        try
        {
            foreach (string file in Directory.GetFiles("Assets/Fonts", "*.ttf"))
            {
                string asset = file.Replace('\\', '/');

                TrueTypeFontImporter importer = (TrueTypeFontImporter) AssetImporter.GetAtPath(asset);

                if (importer == null)
                    throw new Exception("No importer for " + asset);

                importer.fontTextureCase = FontTextureCase.Dynamic;
                importer.includeFontData = true;
                importer.assetBundleName = Bundle;
                importer.SaveAndReimport();
            }

            Directory.CreateDirectory("AssetBundles");

            var manifest = BuildPipeline.BuildAssetBundles(
                "AssetBundles", BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);

            if (manifest == null)
                throw new Exception("BuildAssetBundles returned null.");

            Debug.Log("BundleBuilder: built " + string.Join(", ", manifest.GetAllAssetBundles()));

            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("BundleBuilder failed: " + e);

            EditorApplication.Exit(1);
        }
    }
}
