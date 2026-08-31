using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reports the name each built bundle calls itself, which is the name Unity keys its loaded-bundle table by
/// and therefore the only thing that decides whether two mods collide. The file name is irrelevant to that.
///
///   Unity.exe -batchmode -nographics -projectPath BundleProject -executeMethod BundleNames.Run -logFile log
/// </summary>
public static class BundleNames
{
    public static void Run()
    {
        try
        {
            foreach (string path in Directory.GetFiles("AssetBundles"))
            {
                if (Path.GetExtension(path).Length > 0)
                    continue;

                AssetBundle bundle = AssetBundle.LoadFromFile(path);

                if (bundle == null)
                {
                    Debug.Log("NAMES: could not load " + Path.GetFileName(path));

                    continue;
                }

                Debug.Log("NAMES: file '" + Path.GetFileName(path) + "' calls itself '" + bundle.name + "'");

                bundle.Unload(true);
            }

            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("BundleNames failed: " + e);

            EditorApplication.Exit(1);
        }
    }
}
