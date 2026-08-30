using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asks the built bundle for one texture using the exact strings RimWorld builds, so a lookup failure can be
/// pinned on the name rather than guessed at.
///
///   Unity.exe -batchmode -nographics -projectPath BundleProject -executeMethod BundleProbe.Run -logFile log
/// </summary>
public static class BundleProbe
{
    public static void Run()
    {
        try
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(
                Path.Combine("AssetBundles", "textures"));

            if (bundle == null)
                throw new Exception("bundle would not load");

            string[] names = bundle.GetAllAssetNames();

            Debug.Log("PROBE: bundle holds " + names.Length + " assets");

            foreach (string name in names)
            {
                if (name.IndexOf("OptionsUIOverhaul", StringComparison.OrdinalIgnoreCase) >= 0)
                    Debug.Log("PROBE: stored name = [" + name + "]");
            }

            // Exactly what ContentFinder builds: Path.Combine for the leading segments, which is a backslash
            // on Windows, then the content path's own trailing forward slash, then the item path.
            string viaCombine = Path.Combine(
                Path.Combine(Path.Combine("Assets", "Data"), "gideon.uioverhaul"), "Textures/");

            string item = "UIOverhaul/UI/OptionsUIOverhaul";

            Try(bundle, "vanilla shape + .dds ", Path.Combine(viaCombine, item) + ".dds");
            Try(bundle, "vanilla shape + .png ", Path.Combine(viaCombine, item) + ".png");
            Try(bundle, "forward slashes .dds ", "Assets/Data/gideon.uioverhaul/Textures/" + item + ".dds");
            Try(bundle, "lowercased .dds      ",
                ("Assets/Data/gideon.uioverhaul/Textures/" + item + ".dds").ToLowerInvariant());
            Try(bundle, "folder-name spelling ",
                "Assets/Data/Gideon.UIOverhaul/Textures/" + item + ".dds");

            bundle.Unload(true);

            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("BundleProbe failed: " + e);

            EditorApplication.Exit(1);
        }
    }

    private static void Try(AssetBundle bundle, string label, string name)
    {
        Texture2D found = bundle.LoadAsset<Texture2D>(name);

        Debug.Log("PROBE: " + label + (found != null ? "HIT " : "miss") + "  [" + name + "]");
    }
}
