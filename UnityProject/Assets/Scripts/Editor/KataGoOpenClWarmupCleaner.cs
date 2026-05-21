using System.IO;
using UnityEditor;
using UnityEngine;

public static class KataGoOpenClWarmupCleaner
{
    private const string OpenClWarmupRelativePath = "KataGo/engines/win-x64/opencl/KataGoData";

    [MenuItem(CustomEditorMenuPaths.KataGo + "/清除opencl预热文件")]
    public static void ClearOpenClWarmupFiles()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            EditorUtility.DisplayDialog("无法清除", "请先停止 Play，再清除 opencl 预热文件。", "确定");
            return;
        }

        string warmupDirectoryPath = Path.Combine(Application.streamingAssetsPath, OpenClWarmupRelativePath);
        string warmupMetaPath = warmupDirectoryPath + ".meta";
        bool deleted = DeleteFileOrDirectory(warmupDirectoryPath);
        deleted |= DeleteFileOrDirectory(warmupMetaPath);

        AssetDatabase.Refresh();

        if (deleted) {
            Debug.Log($"已清除 KataGo opencl 预热文件：{warmupDirectoryPath}");
            return;
        }

        Debug.Log($"没有找到 KataGo opencl 预热文件：{warmupDirectoryPath}");
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
