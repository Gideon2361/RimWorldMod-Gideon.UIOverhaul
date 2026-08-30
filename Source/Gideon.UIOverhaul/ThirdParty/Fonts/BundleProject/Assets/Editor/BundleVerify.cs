using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reads a built bundle back and reports what is actually inside it.
///
/// <b>The manifest only lists names.</b> It cannot tell you that a texture kept its dimensions, that a DDS
/// passed through as the block format it was authored in, or that Unity did not quietly halve something
/// against the importer's size ceiling. Those are exactly the failures that produce art which looks wrong
/// rather than art which errors, and they are worth catching before the originals leave the mod folder.
///
///   Unity.exe -batchmode -nographics -projectPath BundleProject -executeMethod BundleVerify.Run -logFile log
/// </summary>
public static class BundleVerify
{
    public static void Run()
    {
        try
        {
            string path = Path.Combine("AssetBundles", "textures");

            AssetBundle bundle = AssetBundle.LoadFromFile(path);

            if (bundle == null)
                throw new Exception("Could not load " + path);

            string[] names = bundle.GetAllAssetNames();
            Dictionary<string, int> formats = new Dictionary<string, int>();

            long pixels = 0;
            int checked_ = 0;
            int suspicious = 0;

            foreach (string name in names)
            {
                Texture2D texture = bundle.LoadAsset<Texture2D>(name);

                if (texture == null)
                {
                    Debug.Log("VERIFY: NOT A TEXTURE  " + name);

                    suspicious++;

                    continue;
                }

                string format = texture.format.ToString();

                formats[format] = (formats.ContainsKey(format) ? formats[format] : 0) + 1;

                pixels += (long) texture.width * texture.height;
                checked_++;

                // An uncompressed format in a bundle meant to hold block-compressed art means the importer
                // silently declined to compress, which costs several times the memory at runtime.
                if (format.StartsWith("RGBA") || format.StartsWith("ARGB") || format.StartsWith("RGB2"))
                {
                    Debug.Log(string.Format("VERIFY: UNCOMPRESSED {0} {1}x{2}  {3}",
                        format, texture.width, texture.height, name));

                    suspicious++;
                }
            }

            Debug.Log("VERIFY: " + checked_ + " textures, " + pixels + " total pixels, " + suspicious + " suspicious");

            foreach (KeyValuePair<string, int> pair in formats)
                Debug.Log("VERIFY: format " + pair.Key + " x" + pair.Value);

            bundle.Unload(true);

            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("BundleVerify failed: " + e);

            EditorApplication.Exit(1);
        }
    }
}
