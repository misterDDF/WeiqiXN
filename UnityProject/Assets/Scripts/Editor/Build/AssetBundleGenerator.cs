using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Build.Pipeline;

public class AssetBundleGenerator
{
    private static readonly string[] PlayerScenes = { "Assets/Scenes/Main.unity" };
    private static readonly string[] RequiredKataGoDirectories =
    {
        "engines/win-x64/eigenavx2",
        "models",
    };

    private const string KataGoOpenClEngineName = "opencl";
    private const string KataGoCpuEngineName = "eigenavx2";
    private const string KataGoModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";

    [MenuItem(CustomEditorMenuPaths.Build + "/打PC包")]
    public static void BuildWindows()
    {
        BuildAssetBundlesForTarget(BuildTarget.StandaloneWindows64);
        ValidateWindowsKataGoStreamingAssets();

        PrepareBuildRootDirectory(BuildConfig.BUILD_PATH_ROOT);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Master);
        PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);

        string buildOutputPath = Path.GetFullPath(BuildConfig.BUILD_PATH_WINDOWS);
        var buildOptions = BuildOptions.CompressWithLz4HC | BuildOptions.Development;
        BuildReport report = BuildPipeline.BuildPlayer(PlayerScenes, buildOutputPath, BuildTarget.StandaloneWindows64, buildOptions);
        if (report.summary.result != BuildResult.Succeeded) {
            throw new Exception($"Build windows player failed, outputPath: {buildOutputPath}, result: {report.summary.result}.");
        }

        Debug.Log($"Windows Player打包完成！输出路径：{buildOutputPath}");
    }

    [MenuItem(CustomEditorMenuPaths.Build + "/打WebGL包")]
    public static void BuildWebGL()
    {
        BuildAssetBundlesForTarget(BuildTarget.WebGL);

        string buildOutputPath = Path.GetFullPath(BuildConfig.BUILD_PATH_WEBGL);
        PrepareBuildRootDirectory(BuildConfig.BUILD_PATH_WEBGL);

        PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);

        BuildReport report = BuildPipeline.BuildPlayer(PlayerScenes, buildOutputPath, BuildTarget.WebGL, BuildOptions.Development);
        if (report.summary.result != BuildResult.Succeeded) {
            throw new Exception($"Build WebGL player failed, outputPath: {buildOutputPath}, result: {report.summary.result}.");
        }

        Debug.Log($"WebGL Player打包完成！输出路径：{buildOutputPath}");
    }

    private static void BuildAssetBundlesForTarget(BuildTarget target)
    {
        SwitchActiveBuildTarget(target);
        PrepareAssetBundleOutputDirectory();
        PackAllJsonCfgFiles();
        PackAllModelFiles();
        PackAllSceneFiles();
        PackAllUIPrefabFiles();
        PackDebugConsolePrefab();
        PackRuntimeAssetTable();

        BuildAssetBundleOptions options = BuildAssetBundleOptions.None;
        if (BuildConfig.BUILD_BUNDLE_DISABLE_WRITE_TYPE_TREE) {
            options |= BuildAssetBundleOptions.DisableWriteTypeTree;
        }

        options |= BuildAssetBundleOptions.DisableLoadAssetByFileName;
        options |= BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension;
        options |= BuildAssetBundleOptions.ChunkBasedCompression;

        var manifest = CompatibilityBuildPipeline.BuildAssetBundles(BuildConfig.PATH_BUILDIN_ASSETBUNDLE, options, target);
        if (manifest == null) {
            throw new Exception($"Build asset bundle failed, target: {target}, outputPath: {BuildConfig.PATH_BUILDIN_ASSETBUNDLE}.");
        }

        WriteAssetBundleManifestFile(manifest);
        AssetDatabase.Refresh();
        Debug.Log($"AssetBundle打包完成！target: {target}, 输出路径：{BuildConfig.PATH_BUILDIN_ASSETBUNDLE}");
    }

    private static void SwitchActiveBuildTarget(BuildTarget target)
    {
        BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
        if (EditorUserBuildSettings.activeBuildTarget != target) {
            bool switchSuccess = EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            if (!switchSuccess) {
                throw new Exception($"Switch active build target failed. target: {target}, group: {group}");
            }
        }
    }

    private static void PrepareAssetBundleOutputDirectory()
    {
        string outputPath = BuildConfig.PATH_BUILDIN_ASSETBUNDLE;
        if (!Directory.Exists(outputPath)) {
            Directory.CreateDirectory(outputPath);
            return;
        }

        foreach (string filePath in Directory.GetFiles(outputPath)) {
            File.Delete(filePath);
        }

        foreach (string subDirectoryPath in Directory.GetDirectories(outputPath)) {
            Directory.Delete(subDirectoryPath, true);
        }
    }

    private static void WriteAssetBundleManifestFile(CompatibilityAssetBundleManifest manifest)
    {
        string manifestFilePath = Path.Combine(BuildConfig.PATH_BUILDIN_ASSETBUNDLE, BuildConfig.ASSET_BUNDLE_MANIFEST_FILE_NAME);
        JArray bundleNames = new JArray();
        foreach (string bundleName in manifest.GetAllAssetBundles()) {
            if (!string.IsNullOrEmpty(bundleName)) {
                bundleNames.Add(bundleName);
            }
        }

        File.WriteAllText(manifestFilePath, bundleNames.ToString());
        Debug.Log($"AssetBundle清单生成完成：{manifestFilePath}");
    }

    [MenuItem(CustomEditorMenuPaths.BuildPreprocess + "/检查json表打包标签")]
    public static void PackAllJsonCfgFiles()
    {
        PackAssetsByType(BuildConfig.PATH_PACK_JSON, "TextAsset", BuildConfig.AB_LABEL_JSON);
    }

    [MenuItem(CustomEditorMenuPaths.BuildPreprocess + "/检查model资源打包标签")]
    public static void PackAllModelFiles()
    {
        string rootFolderPath = FullPathToAssetPath(BuildConfig.PATH_PACK_MODEL);
        if (string.IsNullOrEmpty(rootFolderPath) || !Directory.Exists(BuildConfig.PATH_PACK_MODEL)) {
            Debug.LogWarning($"找不到资源目录：{BuildConfig.PATH_PACK_MODEL}");
            return;
        }

        string[] modelFolderFullPaths = Directory.GetDirectories(BuildConfig.PATH_PACK_MODEL);
        int newImportCount = 0;
        int packedModelTypeCount = 0;

        foreach (string folderFullPath in modelFolderFullPaths) {
            string folderPath = FullPathToAssetPath(folderFullPath);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)) {
                continue;
            }

            string modelTypeName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(modelTypeName)) {
                continue;
            }

            string assetBundleName = $"{BuildConfig.AB_LABEL_MODEL}_{modelTypeName}".ToLowerInvariant();
            string[] assetGuids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            int modelAssetCount = 0;
            foreach (string assetGuid in assetGuids) {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (AssetDatabase.IsValidFolder(assetPath)) {
                    continue;
                }

                AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null) {
                    continue;
                }

                if (importer.assetBundleName != assetBundleName) {
                    importer.assetBundleName = assetBundleName;
                    newImportCount++;
                }

                modelAssetCount++;
            }

            if (modelAssetCount > 0) {
                packedModelTypeCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"新增/更新 {newImportCount} 个model资源标签，按类型打包 {packedModelTypeCount} 个目录");
    }

    [MenuItem(CustomEditorMenuPaths.BuildPreprocess + "/检查scene资源打包标签")]
    public static void PackAllSceneFiles()
    {
        PackAssetsByType(BuildConfig.PATH_PACK_SCENE, "SceneAsset", BuildConfig.AB_LABEL_SCENE);

        string rootFolderPath = FullPathToAssetPath(BuildConfig.PATH_PACK_SCENE);
        if (string.IsNullOrEmpty(rootFolderPath) || !Directory.Exists(BuildConfig.PATH_PACK_SCENE)) {
            Debug.LogError($"找不到资源目录：{BuildConfig.PATH_PACK_SCENE}");
            return;
        }

        string[] sceneFolderFullPaths = Directory.GetDirectories(BuildConfig.PATH_PACK_SCENE);
        int newImportCount = 0;
        int packedSceneCount = 0;

        foreach (string folderFullPath in sceneFolderFullPaths) {
            string folderPath = FullPathToAssetPath(folderFullPath);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)) {
                continue;
            }

            string sceneName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(sceneName)) {
                continue;
            }

            string assetBundleName = $"{BuildConfig.AB_LABEL_SCENE}_{sceneName}".ToLowerInvariant();
            string[] assetGuids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            int sceneAssetCount = 0;
            foreach (string assetGuid in assetGuids) {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (AssetDatabase.IsValidFolder(assetPath)) {
                    continue;
                }

                if (string.Equals(Path.GetExtension(assetPath), ".unity", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null) {
                    continue;
                }

                if (importer.assetBundleName != assetBundleName) {
                    importer.assetBundleName = assetBundleName;
                    newImportCount++;
                }

                sceneAssetCount++;
            }

            if (sceneAssetCount > 0) {
                packedSceneCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"新增/更新 {newImportCount} 个资源标签，按场景打包 {packedSceneCount} 个目录");
    }

    [MenuItem(CustomEditorMenuPaths.BuildPreprocess + "/检查UI资源打包标签")]
    public static void PackAllUIPrefabFiles()
    {
        PackAssetsByType(BuildConfig.PATH_PACK_UI_PREFAB, "GameObject", BuildConfig.AB_LABEL_UI_PREFAB);
        PackAssetsByType(BuildConfig.PATH_PACK_UI_TEXTUER, "Texture2D", BuildConfig.AB_LABEL_UI_TEXTURE);
    }

    [MenuItem(CustomEditorMenuPaths.BuildPreprocess + "/检查调试资源打包标签")]
    public static void PackDebugConsolePrefab()
    {
        AssetImporter importer = AssetImporter.GetAtPath(BuildConfig.PATH_DEBUG_CONSOLE_PREFAB);
        if (importer == null) {
            Debug.LogWarning($"找不到调试控制台预制体：{BuildConfig.PATH_DEBUG_CONSOLE_PREFAB}");
            return;
        }

        if (importer.assetBundleName != BuildConfig.AB_LABEL_DEBUG) {
            importer.assetBundleName = BuildConfig.AB_LABEL_DEBUG;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"设置调试资源 AB 标签：{BuildConfig.AB_LABEL_DEBUG}");
        }
    }

    [MenuItem(CustomEditorMenuPaths.BuildPreprocess + "/检查运行时显式资源表")]
    public static void PackRuntimeAssetTable()
    {
        TextAsset configAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(BuildConfig.PATH_RUNTIME_ASSET_CONFIG);
        if (configAsset == null) {
            throw new FileNotFoundException($"Runtime asset config not found: {BuildConfig.PATH_RUNTIME_ASSET_CONFIG}");
        }

        JObject configRoot;
        try {
            configRoot = JObject.Parse(configAsset.text);
        }
        catch (Exception ex) {
            throw new Exception($"Parse runtime asset config failed: {BuildConfig.PATH_RUNTIME_ASSET_CONFIG}, err: {ex.Message}", ex);
        }

        int newImportCount = 0;
        int assetCount = 0;
        foreach (JProperty property in configRoot.Properties()) {
            string id = property.Name;
            string assetType = property.Value.Value<string>("assetType");
            string resPath = property.Value.Value<string>("resPath");
            string bundleName = property.Value.Value<string>("bundleName");
            string assetPath = GetRuntimeAssetPath(id, assetType, resPath);

            ValidateRuntimeAsset(id, assetType, bundleName, assetPath);
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null) {
                throw new FileNotFoundException($"Runtime asset importer not found. id: {id}, assetPath: {assetPath}");
            }

            string normalizedBundleName = bundleName.ToLowerInvariant();
            if (importer.assetBundleName != normalizedBundleName) {
                importer.assetBundleName = normalizedBundleName;
                newImportCount++;
            }

            assetCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Runtime asset table checked. assetCount: {assetCount}, updatedBundleLabels: {newImportCount}");
    }

    private static string GetRuntimeAssetPath(string id, string assetType, string resPath)
    {
        if (string.IsNullOrWhiteSpace(assetType)) {
            throw new Exception($"Runtime asset type is empty. id: {id}");
        }
        if (string.IsNullOrWhiteSpace(resPath)) {
            throw new Exception($"Runtime asset path is empty. id: {id}");
        }

        if (!ResourceUtils.AssetExtendDict.TryGetValue(assetType, out string extension)) {
            throw new Exception($"Runtime asset type is unsupported. id: {id}, assetType: {assetType}");
        }

        return $"Assets/{resPath}{extension}";
    }

    private static void ValidateRuntimeAsset(string id, string assetType, string bundleName, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new Exception("Runtime asset id is empty.");
        }
        if (string.IsNullOrWhiteSpace(bundleName)) {
            throw new Exception($"Runtime asset bundle name is empty. id: {id}");
        }

        if (assetType == typeof(GameObject).Name) {
            ValidateRuntimeAssetType<GameObject>(id, assetType, assetPath);
            return;
        }
        if (assetType == typeof(Sprite).Name) {
            ValidateRuntimeAssetType<Sprite>(id, assetType, assetPath);
            return;
        }
        if (assetType == typeof(Material).Name) {
            ValidateRuntimeAssetType<Material>(id, assetType, assetPath);
            return;
        }

        throw new Exception($"Runtime asset type is unsupported. id: {id}, assetType: {assetType}");
    }

    private static void ValidateRuntimeAssetType<TAsset>(string id, string assetType, string assetPath) where TAsset : UnityEngine.Object
    {
        TAsset asset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
        if (asset == null) {
            throw new FileNotFoundException($"Runtime asset not found or type mismatch. id: {id}, assetPath: {assetPath}, expected: {assetType}");
        }
    }

    private static void PackAssetsByType(string rootFolderFullPath, string typeName, string assetBundleName)
    {
        string rootFolderPath = FullPathToAssetPath(rootFolderFullPath);
        if (string.IsNullOrEmpty(rootFolderPath)) {
            Debug.LogWarning($"找不到资源目录：{rootFolderFullPath}");
            return;
        }

        string[] guids = AssetDatabase.FindAssets($"t:{typeName}", new[] { rootFolderPath });
        int newImportCount = 0;
        foreach (string guid in guids) {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null) {
                continue;
            }

            if (importer.assetBundleName != assetBundleName) {
                importer.assetBundleName = assetBundleName;
                newImportCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"新增 {newImportCount} 个 {typeName} 资源，设置 AB 标签：{assetBundleName}");
    }

    private static string FullPathToAssetPath(string fullPath)
    {
        string dataPath = Application.dataPath.Replace('\\', '/');
        string normalizedPath = fullPath.Replace('\\', '/');
        if (!normalizedPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return "Assets" + normalizedPath.Substring(dataPath.Length);
    }

    private static void PrepareBuildRootDirectory(string buildRootPath)
    {
        string fullBuildRootPath = Path.GetFullPath(buildRootPath);
        if (Directory.Exists(fullBuildRootPath)) {
            Directory.Delete(fullBuildRootPath, true);
        }

        Directory.CreateDirectory(fullBuildRootPath);
    }

    private static void ValidateWindowsKataGoStreamingAssets()
    {
        string kataGoRoot = Path.Combine(Application.streamingAssetsPath, "KataGo");
        string cpuEngineRoot = Path.Combine(kataGoRoot, "engines", "win-x64", KataGoCpuEngineName);
        string openClEngineRoot = Path.Combine(kataGoRoot, "engines", "win-x64", KataGoOpenClEngineName);
        string cpuExePath = Path.Combine(cpuEngineRoot, "katago.exe");
        string cpuConfigPath = Path.Combine(cpuEngineRoot, "analysis_example.cfg");
        string modelPath = Path.Combine(kataGoRoot, "models", KataGoModelFileName);

        List<string> missingPaths = new List<string>();
        foreach (string directoryPath in RequiredKataGoDirectories) {
            string fullDirectoryPath = Path.Combine(kataGoRoot, directoryPath);
            if (!Directory.Exists(fullDirectoryPath) || Directory.GetFiles(fullDirectoryPath).Length == 0) {
                missingPaths.Add(fullDirectoryPath);
            }
        }

        if (!File.Exists(cpuExePath)) {
            missingPaths.Add(cpuExePath);
        }

        if (!File.Exists(cpuConfigPath)) {
            missingPaths.Add(cpuConfigPath);
        }

        if (!File.Exists(modelPath)) {
            missingPaths.Add(modelPath);
        }

        if (Directory.Exists(openClEngineRoot) && Directory.GetFiles(openClEngineRoot).Length > 0) {
            string openClExePath = Path.Combine(openClEngineRoot, "katago.exe");
            string openClConfigPath = Path.Combine(openClEngineRoot, "analysis_example.cfg");
            if (!File.Exists(openClExePath)) {
                missingPaths.Add(openClExePath);
            }

            if (!File.Exists(openClConfigPath)) {
                missingPaths.Add(openClConfigPath);
            }
        }
        else {
            Debug.LogWarning($"KataGo OpenCL engine is not bundled. Windows player will use CPU fallback only. path: {openClEngineRoot}");
        }

        if (missingPaths.Count > 0) {
            throw new FileNotFoundException(
                "Windows build requires KataGo CPU fallback runtime and any bundled OpenCL entry files under Assets/StreamingAssets/KataGo. Missing paths: "
                + string.Join(", ", missingPaths));
        }
    }
}
