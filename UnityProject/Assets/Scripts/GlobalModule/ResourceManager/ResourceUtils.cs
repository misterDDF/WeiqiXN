using System.Collections.Generic;
using UnityEngine;
using XNLogger = XNClient.Logger.XNLogger;

public static class ResourceUtils
{
    public static Dictionary<string, string> AssetExtendDict = new Dictionary<string, string>()
    {
        { typeof(GameObject).Name, ".prefab" },
        { typeof(Sprite).Name, ".png" },
        { typeof(Material).Name, ".mat" },
        { typeof(AudioClip).Name, ".ogg" },
    };

    public static string GetAssetFullPath<TAsset>(string path) where TAsset : UnityEngine.Object
    {
        if (AssetExtendDict.TryGetValue(typeof(TAsset).Name, out string ext)) {
            return $"Assets/{path}{ext}";
        } else {
            XNLogger.LogError("Invalid type for get asset full path.", ("type", typeof(TAsset).Name));
        }

        return string.Empty;
    }
}

