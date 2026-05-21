using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class KataGoOpenClWarmupCleaner
{
    private const string OpenClWarmupRelativePath = "engines/win-x64/opencl/KataGoData";
    private const string LegacyOpenClWarmupRelativePath = "StreamingAssets/KataGo/engines/win-x64/opencl/KataGoData";

    [MenuItem(CustomEditorMenuPaths.KataGo + "/清除opencl预热文件")]
    public static void ClearOpenClWarmupFiles()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            EditorUtility.DisplayDialog("无法清除", "请先停止 Play，再清除 opencl 预热文件。", "确定");
            return;
        }

        List<string> targetPaths = ResolveWarmupTargetPaths();
        List<string> deletedPaths = new List<string>();
        List<string> missingPaths = new List<string>();

        try {
            foreach (string targetPath in targetPaths) {
                if (DeleteFileOrDirectory(targetPath)) {
                    deletedPaths.Add(targetPath);
                }
                else {
                    missingPaths.Add(targetPath);
                }

                string metaPath = targetPath + ".meta";
                if (DeleteFileOrDirectory(metaPath)) {
                    deletedPaths.Add(metaPath);
                }
            }

            AssetDatabase.Refresh();
        }
        catch (Exception ex) {
            string failureMessage = $"清除 KataGo opencl 预热文件失败：{ex.GetType().Name}: {ex.Message}";
            Debug.LogError(failureMessage);
            EditorUtility.DisplayDialog("清除失败", failureMessage, "确定");
            return;
        }

        if (deletedPaths.Count > 0) {
            string message = "已清除 KataGo opencl 预热文件：\n" + string.Join("\n", deletedPaths);
            Debug.Log(message);
            EditorUtility.DisplayDialog("清除完成", message, "确定");
            return;
        }

        string notFoundMessage = "没有找到 KataGo opencl 预热文件：\n" + string.Join("\n", missingPaths);
        Debug.Log(notFoundMessage);
        EditorUtility.DisplayDialog("未找到预热文件", notFoundMessage, "确定");
    }

    private static List<string> ResolveWarmupTargetPaths()
    {
        KataGoRuntimeEnvironment.RuntimeInfo runtimeInfo = KataGoRuntimeEnvironment.Resolve();
        return new List<string>
        {
            Path.Combine(runtimeInfo.kataGoRoot, OpenClWarmupRelativePath),
            Path.Combine(Application.dataPath, LegacyOpenClWarmupRelativePath),
        };
    }

    private static bool DeleteFileOrDirectory(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) {
            return false;
        }

        FileUtil.DeleteFileOrDirectory(path);
        return true;
    }
}
