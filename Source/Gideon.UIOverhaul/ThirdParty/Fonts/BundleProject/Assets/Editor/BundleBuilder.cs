using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the mod's font AssetBundle, headless.
///
/// Run from the repo, never by opening the editor:
///   Unity.exe -batchmode -nographics -projectPath BundleProject -executeMethod BundleBuilder.Build -logFile log
///
/// Every TTF under Assets/Fonts is imported as a dynamic font with its file data included, which is what makes
/// the result a real FreeType-backed font at runtime -- the same machinery RimWorld's own text uses. The bundle
/// is written suffix-less, because RimWorld loads a bundle without an OS suffix on every platform and font data
/// is platform agnostic.
/// </summary>
public static class BundleBuilder
{
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
                importer.assetBundleName = "gideonfonts";
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
