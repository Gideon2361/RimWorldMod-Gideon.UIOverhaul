using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the mod's two AssetBundles, headless.
///
/// Run from the repo, never by opening the editor:
///   Unity.exe -batchmode -nographics -projectPath BundleProject -executeMethod BundleBuilder.Build -logFile log
///
/// <b>Two bundles, because they change on different schedules.</b> Fonts are added once in a while and are
/// small; textures are edited constantly and are most of the download. Separating them means re-baking art
/// does not rewrite the font bundle, and a subscriber re-downloads only what moved.
///
/// <b>Only the files named <see cref="Fonts"/> and <see cref="Textures"/> may be copied into the mod.</b> The
/// output folder also gets a per-bundle .manifest, which RimWorld ignores, and a root manifest bundle named
/// after the output folder itself -- and that one has no extension either, so copying it would have RimWorld
/// load a bundle of build bookkeeping.
///
/// Both are written suffix-less, because RimWorld loads a bundle without an OS suffix on every platform and
/// neither font nor texture data is platform specific.
/// </summary>
public static class BundleBuilder
{
    /// <summary>
    /// The typeface bundle. Nothing in the mod's C# names it: the font loader searches every bundle our mod
    /// has loaded, so this string and the file on disk are the only places it appears.
    /// </summary>
    private const string Fonts = "ui_overhaul_assets";

    /// <summary>
    /// The art bundle. Unlike the fonts, this name never appears in our C# either, but for a different
    /// reason: RimWorld resolves these itself through ContentFinder, which walks every loaded bundle.
    /// </summary>
    private const string Textures = "gideon_uioverhaul_textures";

    /// <summary>
    /// Where the art has to live inside the project, and it is not a matter of taste.
    /// <c>ContentFinder&lt;Texture2D&gt;.TryFindAssetInModBundles</c> probes exactly
    /// <c>Assets/Data/&lt;packageId&gt;/Textures/&lt;path&gt;</c>, so the folder tree under
    /// <c>Assets/Data</c> mirrors the mod's old Textures folder one for one. Rename a folder here and the
    /// texture stops resolving, with no error until something draws it.
    ///
    /// The package id rather than the mod's folder name: RimWorld accepts either, but the folder name becomes
    /// the numeric Workshop id once a subscriber installs it, which would work locally and break for everyone
    /// else.
    /// </summary>
    private const string ArtRoot = "Assets/Data/gideon.uioverhaul/Textures";

    public static void Build()
    {
        try
        {
            ImportFonts();
            ImportTextures();

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

    /// <summary>
    /// Every TTF as a dynamic font with its file data included, which is what makes the result a real
    /// FreeType-backed font at runtime rather than a fixed set of rasterized glyphs.
    /// </summary>
    private static void ImportFonts()
    {
        foreach (string file in Directory.GetFiles("Assets/Fonts", "*.ttf"))
        {
            string asset = file.Replace('\\', '/');

            TrueTypeFontImporter importer = (TrueTypeFontImporter) AssetImporter.GetAtPath(asset);

            if (importer == null)
                throw new Exception("No importer for " + asset);

            importer.fontTextureCase = FontTextureCase.Dynamic;
            importer.includeFontData = true;
            importer.assetBundleName = Fonts;
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// The art, in whichever format each file already is.
    ///
    /// <b>A DDS passes straight through and a PNG gets compressed here.</b> Unity hands a DDS to
    /// <c>IHVImageFormatImporter</c>, which keeps the block-compressed payload exactly as authored -- no
    /// decode, no re-encode, no generation loss. A PNG goes to <c>TextureImporter</c>, which compresses it to
    /// DXT on the way in. Both end up as GPU-ready texture data in the bundle, which is the whole point: the
    /// mod ships no PNG for the game to decode at startup.
    ///
    /// The settings below mirror what RimWorld itself does to a texture it loads off disk
    /// (<c>ModContentLoader.LoadTextureViaImageConversion</c>): mipmaps on, trilinear, aniso 2, DXT when the
    /// dimensions allow. Matching it matters -- these textures used to travel that path, and anything
    /// different here shows up as art that looks subtly wrong rather than as an error.
    /// </summary>
    private static void ImportTextures()
    {
        if (!Directory.Exists(ArtRoot))
            throw new Exception("No art at " + ArtRoot);

        foreach (string file in Directory.GetFiles(ArtRoot, "*.*", SearchOption.AllDirectories))
        {
            string asset = file.Replace('\\', '/');
            string extension = Path.GetExtension(asset).ToLowerInvariant();

            if (extension == ".meta")
                continue;

            AssetImporter importer = AssetImporter.GetAtPath(asset);

            if (importer == null)
                throw new Exception("No importer for " + asset);

            TextureImporter texture = importer as TextureImporter;

            if (texture != null)
            {
                texture.textureType = TextureImporterType.Default;
                texture.alphaIsTransparency = true;
                texture.mipmapEnabled = true;
                texture.filterMode = FilterMode.Trilinear;
                texture.anisoLevel = 2;
                texture.textureCompression = TextureImporterCompression.Compressed;
                texture.crunchedCompression = false;

                // RimWorld never rescales a texture it loads, it only clamps mipmaps on odd sizes. Letting
                // Unity round to a power of two would silently resize art that has always drawn at its own
                // dimensions.
                texture.npotScale = TextureImporterNPOTScale.None;

                // The default ceiling is 2048, which would quietly halve anything larger.
                texture.maxTextureSize = 8192;
            }
            else
            {
                // A DDS: IHVImageFormatImporter exposes no scripted properties, so the sampling settings are
                // reached through the serialized object or not at all. Guarded, because a Unity upgrade that
                // renames these fields should cost sharper filtering, not the whole bake.
                TrySetSampling(importer);
            }

            importer.assetBundleName = Textures;
            importer.SaveAndReimport();
        }
    }

    private static void TrySetSampling(AssetImporter importer)
    {
        try
        {
            SerializedObject serialized = new SerializedObject(importer);

            SerializedProperty filter = serialized.FindProperty("m_Output.textureSettings.m_FilterMode")
                                        ?? serialized.FindProperty("m_TextureSettings.m_FilterMode");

            SerializedProperty aniso = serialized.FindProperty("m_Output.textureSettings.m_Aniso")
                                       ?? serialized.FindProperty("m_TextureSettings.m_Aniso");

            if (filter == null || aniso == null)
            {
                Debug.LogWarning("BundleBuilder: DDS sampling properties not found on "
                                 + importer.assetPath + "; leaving importer defaults.");

                return;
            }

            filter.intValue = (int) FilterMode.Trilinear;
            aniso.intValue = 2;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        catch (Exception e)
        {
            Debug.LogWarning("BundleBuilder: could not set DDS sampling on " + importer.assetPath + ": " + e.Message);
        }
    }
}
