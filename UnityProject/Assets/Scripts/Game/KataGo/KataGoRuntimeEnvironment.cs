using System;
using System.IO;
using UnityEngine;

public static class KataGoRuntimeEnvironment
{
    public const string DirectoryName = "KataGo";

    private const string WriteTestFileName = ".weiqixn_write_test.tmp";

    public static RuntimeInfo Resolve()
    {
        string gameRoot = ResolveGameRoot();
        bool canWriteGameRoot = CanWriteDirectory(gameRoot, out string writeFailureReason);
        return new RuntimeInfo(
            gameRoot,
            Path.Combine(gameRoot, DirectoryName),
            canWriteGameRoot,
            writeFailureReason);
    }

    private static string ResolveGameRoot()
    {
#if UNITY_EDITOR
        DirectoryInfo assetDirectory = Directory.GetParent(Application.dataPath);
        DirectoryInfo repositoryDirectory = assetDirectory?.Parent;
        return repositoryDirectory?.FullName ?? Directory.GetCurrentDirectory();
#elif UNITY_STANDALONE_WIN
        DirectoryInfo dataDirectory = Directory.GetParent(Application.dataPath);
        return dataDirectory?.FullName ?? Directory.GetCurrentDirectory();
#else
        return Directory.GetCurrentDirectory();
#endif
    }

    private static bool CanWriteDirectory(string directoryPath, out string failureReason)
    {
        failureReason = null;
        try {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath)) {
                failureReason = $"Directory not found: {directoryPath}";
                return false;
            }

            string testFilePath = Path.Combine(directoryPath, WriteTestFileName);
            File.WriteAllText(testFilePath, DateTime.UtcNow.Ticks.ToString());
            File.Delete(testFilePath);
            return true;
        }
        catch (Exception ex) {
            failureReason = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public readonly struct RuntimeInfo
    {
        public readonly string gameRoot;
        public readonly string kataGoRoot;
        public readonly bool canWriteGameRoot;
        public readonly string writeFailureReason;

        public RuntimeInfo(string gameRoot, string kataGoRoot, bool canWriteGameRoot, string writeFailureReason)
        {
            this.gameRoot = gameRoot ?? string.Empty;
            this.kataGoRoot = kataGoRoot ?? string.Empty;
            this.canWriteGameRoot = canWriteGameRoot;
            this.writeFailureReason = writeFailureReason ?? string.Empty;
        }
    }
}
