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
    private const string KataGoNativeOpenClEngineName = "native-opencl";
    private const string KataGoNativeCpuEngineName = "native-eigen";
    private const string KataGoNativeBridgeDllName = "katago_bridge.dll";
    private const string KataGoAndroidOpenClBridgeSoName = "libkatago_bridge_opencl.so";
    private const string KataGoAndroidCpuBridgeSoName = "libkatago_bridge_eigen.so";
    private const string KataGoModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
    private const string KataGoHumanSlModelFileName = "b18c384nbt-humanv0.bin.gz";
    private const string KataGoAnalysisConfigFileName = "analysis_example.cfg";
    private const string KataGoNoWriteAnalysisConfigFileName = "analysis_nowrite.cfg";
    private const string KataGoAndroidPackagedModelSuffix = ".bytes";

    [MenuItem(CustomEditorMenuPaths.Build + "/打PC包")]
    public static void BuildWindows()
    {
        BuildWindows(true);
    }

    [MenuItem(CustomEditorMenuPaths.Build + "/打PC包(非Development)")]
    public static void BuildWindowsNonDevelopment()
    {
        BuildWindows(false);
    }

    private static void BuildWindows(bool development)
    {
        GameConfig.Reload();
        GameConfig.KataGoConfig kataGoConfig = GameConfig.Current.kataGo;
        BuildAssetBundlesForTarget(BuildTarget.StandaloneWindows64);
        string kataGoSourceRoot = ResolveKataGoSourceRoot();
        ValidateWindowsKataGoRuntimeSource(kataGoSourceRoot, kataGoConfig);

        PrepareBuildOutputDirectory(Path.GetDirectoryName(BuildConfig.BUILD_PATH_WINDOWS));
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Master);
        PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);

        string buildOutputPath = Path.GetFullPath(BuildConfig.BUILD_PATH_WINDOWS);
        BuildOptions buildOptions = BuildPlayerOptions(development);
        BuildReport report = BuildPipeline.BuildPlayer(PlayerScenes, buildOutputPath, BuildTarget.StandaloneWindows64, buildOptions);
        if (report.summary.result != BuildResult.Succeeded) {
            throw new Exception($"Build windows player failed, outputPath: {buildOutputPath}, result: {report.summary.result}.");
        }

        CopyKataGoRuntimeToWindowsBuild(kataGoSourceRoot, kataGoConfig);
        CopyGameConfigToWindowsBuild();
        Debug.Log($"Windows Player打包完成！development: {development}, 输出路径：{buildOutputPath}");
    }

    [MenuItem(CustomEditorMenuPaths.Build + "/Build Android APK")]
    public static void BuildAndroid()
    {
        BuildAndroid(true);
    }

    [MenuItem(CustomEditorMenuPaths.Build + "/Build Android APK (Non-Development)")]
    public static void BuildAndroidNonDevelopment()
    {
        BuildAndroid(false);
    }

    private static void BuildAndroid(bool development)
    {
        AndroidBuildToolchainConfigurator.ConfigureForUnityEmbeddedTools();
        GameConfig.Reload();
        GameConfig.KataGoConfig kataGoConfig = GameConfig.Current.kataGo;
        BuildAssetBundlesForTarget(BuildTarget.Android);
        string kataGoSourceRoot = ResolveKataGoSourceRoot();
        ValidateAndroidKataGoRuntimeSource(kataGoSourceRoot, kataGoConfig);
        CopyAndroidKataGoRuntimeToStreamingAssets(kataGoSourceRoot, kataGoConfig);

        PrepareBuildOutputDirectory(Path.GetDirectoryName(BuildConfig.BUILD_PATH_ANDROID));
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Master);
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
        EditorUserBuildSettings.buildAppBundle = false;

        string buildOutputPath = Path.GetFullPath(BuildConfig.BUILD_PATH_ANDROID);
        BuildOptions buildOptions = BuildPlayerOptions(development);
        BuildReport report = BuildPipeline.BuildPlayer(PlayerScenes, buildOutputPath, BuildTarget.Android, buildOptions);
        if (report.summary.result != BuildResult.Succeeded) {
            throw new Exception($"Build Android player failed, outputPath: {buildOutputPath}, result: {report.summary.result}.");
        }

        Debug.Log($"Android Player build complete. development: {development}, outputPath: {buildOutputPath}");
    }

    [MenuItem(CustomEditorMenuPaths.Build + "/打WebGL包")]
    public static void BuildWebGL()
    {
        BuildAssetBundlesForTarget(BuildTarget.WebGL);

        string buildOutputPath = Path.GetFullPath(BuildConfig.BUILD_PATH_WEBGL);
        PrepareBuildOutputDirectory(BuildConfig.BUILD_PATH_WEBGL);

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

    private static BuildOptions BuildPlayerOptions(bool development)
    {
        BuildOptions options = BuildOptions.CompressWithLz4HC;
        if (development) {
            options |= BuildOptions.Development;
        }

        return options;
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

    private static void PrepareBuildOutputDirectory(string buildOutputDirectory)
    {
        string fullBuildOutputDirectory = Path.GetFullPath(buildOutputDirectory);
        if (Directory.Exists(fullBuildOutputDirectory)) {
            Directory.Delete(fullBuildOutputDirectory, true);
        }

        Directory.CreateDirectory(fullBuildOutputDirectory);
    }

    private static string ResolveKataGoSourceRoot()
    {
        return KataGoRuntimeEnvironment.Resolve().kataGoRoot;
    }

    private static void ValidateWindowsKataGoRuntimeSource(string kataGoRoot, GameConfig.KataGoConfig kataGoConfig)
    {
        KataGoBackendMode backendMode = kataGoConfig.ResolveWindowsPlayerBackend();
        if (backendMode == KataGoBackendMode.Disabled) {
            Debug.LogWarning("KataGo Windows player backend is disabled by game-config.json.");
            return;
        }

        if (backendMode == KataGoBackendMode.Native) {
            ValidateWindowsNativeKataGoRuntimeSource(kataGoRoot, kataGoConfig);
            return;
        }

        string cpuEngineRoot = Path.Combine(kataGoRoot, "engines", "win-x64", KataGoCpuEngineName);
        string openClEngineRoot = Path.Combine(kataGoRoot, "engines", "win-x64", KataGoOpenClEngineName);
        string cpuExePath = Path.Combine(cpuEngineRoot, "katago.exe");
        string cpuConfigPath = Path.Combine(cpuEngineRoot, KataGoAnalysisConfigFileName);
        string cpuNoWriteConfigPath = Path.Combine(cpuEngineRoot, KataGoNoWriteAnalysisConfigFileName);
        string modelPath = Path.Combine(kataGoRoot, "models", ResolveKataGoModelFileName(kataGoConfig));
        string humanSlModelPath = Path.Combine(kataGoRoot, "models", ResolveKataGoHumanSlModelFileName(kataGoConfig));

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

        if (!File.Exists(cpuNoWriteConfigPath)) {
            missingPaths.Add(cpuNoWriteConfigPath);
        }

        if (!File.Exists(modelPath)) {
            missingPaths.Add(modelPath);
        }

        if (!File.Exists(humanSlModelPath)) {
            missingPaths.Add(humanSlModelPath);
        }

        if (Directory.Exists(openClEngineRoot) && Directory.GetFiles(openClEngineRoot).Length > 0) {
            string openClExePath = Path.Combine(openClEngineRoot, "katago.exe");
            string openClConfigPath = Path.Combine(openClEngineRoot, KataGoAnalysisConfigFileName);
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
                "Windows build requires KataGo CPU fallback runtime and any bundled OpenCL entry files under the repository KataGo directory. Missing paths: "
                + string.Join(", ", missingPaths));
        }
    }

    private static void ValidateWindowsNativeKataGoRuntimeSource(string kataGoRoot, GameConfig.KataGoConfig kataGoConfig)
    {
        string modelPath = Path.Combine(kataGoRoot, "models", ResolveKataGoModelFileName(kataGoConfig));
        string humanSlModelPath = Path.Combine(kataGoRoot, "models", ResolveKataGoHumanSlModelFileName(kataGoConfig));
        NativeRuntimeCandidate[] candidates = ResolveWindowsNativeRuntimeCandidates(kataGoConfig);

        List<string> missingPaths = new List<string>();
        foreach (NativeRuntimeCandidate candidate in candidates) {
            string engineRoot = Path.Combine(kataGoRoot, "engines", "win-x64", candidate.engineName);
            string bridgePath = Path.Combine(engineRoot, KataGoNativeBridgeDllName);
            string configPath = Path.Combine(engineRoot, candidate.configFileName);
            List<string> candidateMissingPaths = new List<string>();

            if (!File.Exists(bridgePath)) {
                candidateMissingPaths.Add(bridgePath);
            }

            if (!File.Exists(configPath)) {
                candidateMissingPaths.Add(configPath);
            }

            if (candidateMissingPaths.Count > 0) {
                if (candidate.isRequired) {
                    missingPaths.AddRange(candidateMissingPaths);
                }
                else {
                    Debug.LogWarning(
                        "Optional Windows native KataGo candidate is incomplete and will be skipped by runtime fallback. Missing paths: "
                        + string.Join(", ", candidateMissingPaths));
                }
            }
        }

        if (!File.Exists(modelPath)) {
            missingPaths.Add(modelPath);
        }

        if (!File.Exists(humanSlModelPath)) {
            missingPaths.Add(humanSlModelPath);
        }

        if (missingPaths.Count > 0) {
            throw new FileNotFoundException(
                "Windows native KataGo build requires every configured native candidate bridge/config and model under the repository KataGo directory. Missing paths: "
                + string.Join(", ", missingPaths));
        }
    }

    private static void ValidateAndroidKataGoRuntimeSource(string kataGoRoot, GameConfig.KataGoConfig kataGoConfig)
    {
        KataGoBackendMode backendMode = kataGoConfig.ResolveAndroidPlayerBackend();
        if (backendMode == KataGoBackendMode.Disabled) {
            Debug.LogWarning("KataGo Android player backend is disabled by game-config.json.");
            return;
        }

        if (backendMode != KataGoBackendMode.Native) {
            throw new NotSupportedException($"Android KataGo backend is unsupported: {backendMode}. Only native is supported.");
        }

        string abi = string.IsNullOrWhiteSpace(kataGoConfig.androidAbi) ? "arm64-v8a" : kataGoConfig.androidAbi;
        string pluginRoot = Path.Combine(Application.dataPath, "Plugins", "Android", "libs", abi);
        string openClPluginPath = Path.Combine(pluginRoot, KataGoAndroidOpenClBridgeSoName);
        string cpuPluginPath = Path.Combine(pluginRoot, KataGoAndroidCpuBridgeSoName);
        string legacyPluginPath = Path.Combine(pluginRoot, "libkatago_bridge.so");
        string configPath = Path.Combine(kataGoRoot, "engines", "win-x64", KataGoNativeCpuEngineName, KataGoNoWriteAnalysisConfigFileName);
        string openClConfigPath = Path.Combine(kataGoRoot, "engines", "win-x64", KataGoNativeOpenClEngineName, KataGoAnalysisConfigFileName);
        string modelPath = Path.Combine(kataGoRoot, "models", ResolveKataGoModelFileName(kataGoConfig));
        string humanSlModelPath = Path.Combine(kataGoRoot, "models", ResolveKataGoHumanSlModelFileName(kataGoConfig));

        List<string> missingPaths = new List<string>();
        if (!File.Exists(cpuPluginPath)) {
            if (File.Exists(legacyPluginPath)) {
                File.Copy(legacyPluginPath, cpuPluginPath, true);
                AssetDatabase.Refresh();
                Debug.Log($"Legacy Android KataGo bridge copied as eigen fallback. source: {legacyPluginPath}, target: {cpuPluginPath}");
            }
            else {
                missingPaths.Add(cpuPluginPath);
            }
        }

        if (kataGoConfig.androidPreferOpenCl && File.Exists(openClPluginPath)) {
            if (!File.Exists(openClConfigPath)) {
                missingPaths.Add(openClConfigPath);
            }
            Debug.Log($"Android KataGo OpenCL bridge found. path: {openClPluginPath}");
        }
        else if (kataGoConfig.androidPreferOpenCl) {
            if (!kataGoConfig.androidAllowCpuFallback) {
                missingPaths.Add(openClPluginPath);
            }
            else {
                Debug.LogWarning($"Optional Android KataGo OpenCL bridge is not bundled. Android player will use eigen fallback only. path: {openClPluginPath}");
            }
        }

        if (!File.Exists(configPath)) {
            missingPaths.Add(configPath);
        }

        if (!File.Exists(modelPath)) {
            missingPaths.Add(modelPath);
        }

        if (!File.Exists(humanSlModelPath)) {
            missingPaths.Add(humanSlModelPath);
        }

        if (missingPaths.Count > 0) {
            throw new FileNotFoundException(
                "Android native KataGo build requires the arm64 bridge plugin, analysis config, and model. Missing paths: "
                + string.Join(", ", missingPaths));
        }

        Debug.Log($"Android KataGo runtime source checked. abi: {abi}, cpuBridge: {cpuPluginPath}, openClBridge: {openClPluginPath}");
    }

    private static void CopyAndroidKataGoRuntimeToStreamingAssets(string kataGoSourceRoot, GameConfig.KataGoConfig kataGoConfig)
    {
        KataGoBackendMode backendMode = kataGoConfig.ResolveAndroidPlayerBackend();
        if (backendMode == KataGoBackendMode.Disabled) {
            Debug.LogWarning("KataGo Android player backend is disabled, skip copying KataGo runtime.");
            return;
        }

        string modelFileName = ResolveKataGoModelFileName(kataGoConfig);
        string humanSlModelFileName = ResolveKataGoHumanSlModelFileName(kataGoConfig);
        string sourceConfigPath = Path.Combine(kataGoSourceRoot, "engines", "win-x64", KataGoNativeCpuEngineName, KataGoNoWriteAnalysisConfigFileName);
        string sourceOpenClConfigPath = Path.Combine(kataGoSourceRoot, "engines", "win-x64", KataGoNativeOpenClEngineName, KataGoAnalysisConfigFileName);
        string sourceModelPath = Path.Combine(kataGoSourceRoot, "models", modelFileName);
        string sourceHumanSlModelPath = Path.Combine(kataGoSourceRoot, "models", humanSlModelFileName);
        string targetRoot = Path.Combine(Application.streamingAssetsPath, KataGoRuntimeEnvironment.DirectoryName);
        string targetEngineRoot = Path.Combine(targetRoot, "engines", "android", kataGoConfig.androidAbi);
        string targetModelRoot = Path.Combine(targetRoot, "models");

        if (Directory.Exists(targetRoot)) {
            Directory.Delete(targetRoot, true);
        }

        Directory.CreateDirectory(targetEngineRoot);
        Directory.CreateDirectory(targetModelRoot);
        File.Copy(sourceConfigPath, Path.Combine(targetEngineRoot, KataGoNoWriteAnalysisConfigFileName), true);
        if (kataGoConfig.androidPreferOpenCl && File.Exists(sourceOpenClConfigPath)) {
            File.Copy(sourceOpenClConfigPath, Path.Combine(targetEngineRoot, KataGoAnalysisConfigFileName), true);
        }

        // Avoid Android aapt unpacking *.gz assets and changing the APK entry name.
        File.Copy(sourceModelPath, Path.Combine(targetModelRoot, modelFileName + KataGoAndroidPackagedModelSuffix), true);
        File.Copy(sourceHumanSlModelPath, Path.Combine(targetModelRoot, humanSlModelFileName + KataGoAndroidPackagedModelSuffix), true);
        AssetDatabase.Refresh();
        Debug.Log($"Android KataGo runtime copied to StreamingAssets. root: {targetRoot}");
    }

    private static NativeRuntimeCandidate[] ResolveWindowsNativeRuntimeCandidates(GameConfig.KataGoConfig kataGoConfig)
    {
        string nativeOpenClEngineName = string.IsNullOrWhiteSpace(kataGoConfig.windowsNativeOpenClEngineName)
            ? KataGoNativeOpenClEngineName
            : kataGoConfig.windowsNativeOpenClEngineName;
        string nativeCpuEngineName = string.IsNullOrWhiteSpace(kataGoConfig.windowsNativeCpuEngineName)
            ? KataGoNativeCpuEngineName
            : kataGoConfig.windowsNativeCpuEngineName;

        if (!kataGoConfig.windowsPreferOpenCl) {
            return new[]
            {
                new NativeRuntimeCandidate(nativeCpuEngineName, KataGoNoWriteAnalysisConfigFileName, true),
            };
        }

        if (!kataGoConfig.windowsAllowCpuFallback) {
            return new[]
            {
                new NativeRuntimeCandidate(nativeOpenClEngineName, KataGoAnalysisConfigFileName, true),
            };
        }

        return new[]
        {
            new NativeRuntimeCandidate(nativeOpenClEngineName, KataGoAnalysisConfigFileName, false),
            new NativeRuntimeCandidate(nativeCpuEngineName, KataGoNoWriteAnalysisConfigFileName, true),
        };
    }

    private static string ResolveKataGoModelFileName(GameConfig.KataGoConfig kataGoConfig)
    {
        return string.IsNullOrWhiteSpace(kataGoConfig.modelFileName)
            ? KataGoModelFileName
            : kataGoConfig.modelFileName;
    }

    private static string ResolveKataGoHumanSlModelFileName(GameConfig.KataGoConfig kataGoConfig)
    {
        return string.IsNullOrWhiteSpace(kataGoConfig.humanSlModelFileName)
            ? KataGoHumanSlModelFileName
            : kataGoConfig.humanSlModelFileName;
    }

    private static void CopyKataGoRuntimeToWindowsBuild(string kataGoSourceRoot, GameConfig.KataGoConfig kataGoConfig)
    {
        string buildOutputPath = Path.GetFullPath(BuildConfig.BUILD_PATH_WINDOWS);
        string buildRoot = Path.GetDirectoryName(buildOutputPath);
        string kataGoTargetRoot = Path.Combine(buildRoot, KataGoRuntimeEnvironment.DirectoryName);

        if (Directory.Exists(kataGoTargetRoot)) {
            string normalizedBuildRoot = Path.GetFullPath(buildRoot);
            string normalizedTargetRoot = Path.GetFullPath(kataGoTargetRoot);
            if (!normalizedTargetRoot.StartsWith(normalizedBuildRoot, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Refuse to delete KataGo target outside build root: {normalizedTargetRoot}");
            }

            Directory.Delete(kataGoTargetRoot, true);
        }

        KataGoBackendMode backendMode = kataGoConfig.ResolveWindowsPlayerBackend();
        if (backendMode == KataGoBackendMode.Disabled) {
            Debug.LogWarning("KataGo Windows player backend is disabled, skip copying KataGo runtime.");
            return;
        }

        if (backendMode == KataGoBackendMode.Native) {
            CopyWindowsNativeKataGoRuntimeToBuild(kataGoSourceRoot, kataGoTargetRoot, kataGoConfig);
            return;
        }

        CopyDirectory(kataGoSourceRoot, kataGoTargetRoot);
        Debug.Log($"KataGo exe runtime copied to Windows build root: {kataGoTargetRoot}");
    }

    private static void CopyWindowsNativeKataGoRuntimeToBuild(
        string kataGoSourceRoot,
        string kataGoTargetRoot,
        GameConfig.KataGoConfig kataGoConfig)
    {
        NativeRuntimeCandidate[] candidates = ResolveWindowsNativeRuntimeCandidates(kataGoConfig);
        string sourceModelsRoot = Path.Combine(kataGoSourceRoot, "models");
        string targetModelsRoot = Path.Combine(kataGoTargetRoot, "models");

        HashSet<string> copiedEngineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NativeRuntimeCandidate candidate in candidates) {
            if (!copiedEngineNames.Add(candidate.engineName)) {
                continue;
            }

            string sourceEngineRoot = Path.Combine(kataGoSourceRoot, "engines", "win-x64", candidate.engineName);
            string targetEngineRoot = Path.Combine(kataGoTargetRoot, "engines", "win-x64", candidate.engineName);
            string sourceBridgePath = Path.Combine(sourceEngineRoot, KataGoNativeBridgeDllName);
            string sourceConfigPath = Path.Combine(sourceEngineRoot, candidate.configFileName);
            if (!Directory.Exists(sourceEngineRoot)) {
                if (candidate.isRequired) {
                    throw new DirectoryNotFoundException($"Required Windows native KataGo engine directory not found: {sourceEngineRoot}");
                }

                Debug.LogWarning($"Optional Windows native KataGo engine directory not found, skip copying: {sourceEngineRoot}");
                continue;
            }

            if (!File.Exists(sourceBridgePath) || !File.Exists(sourceConfigPath)) {
                if (candidate.isRequired) {
                    throw new FileNotFoundException(
                        $"Required Windows native KataGo engine files are incomplete. bridge: {sourceBridgePath}, config: {sourceConfigPath}");
                }

                Debug.LogWarning($"Optional Windows native KataGo engine files are incomplete, skip copying: {sourceEngineRoot}");
                continue;
            }

            CopyDirectory(sourceEngineRoot, targetEngineRoot);
        }

        Directory.CreateDirectory(targetModelsRoot);

        string modelFileName = ResolveKataGoModelFileName(kataGoConfig);
        string humanSlModelFileName = ResolveKataGoHumanSlModelFileName(kataGoConfig);
        string sourceModelPath = Path.Combine(sourceModelsRoot, modelFileName);
        string targetModelPath = Path.Combine(targetModelsRoot, modelFileName);
        string sourceHumanSlModelPath = Path.Combine(sourceModelsRoot, humanSlModelFileName);
        string targetHumanSlModelPath = Path.Combine(targetModelsRoot, humanSlModelFileName);
        File.Copy(sourceModelPath, targetModelPath, true);
        File.Copy(sourceHumanSlModelPath, targetHumanSlModelPath, true);

        Debug.Log($"KataGo native runtime copied to Windows build root: {kataGoTargetRoot}");
    }

    private static void CopyGameConfigToWindowsBuild()
    {
        string sourcePath = GameConfig.ResolveConfigPath();
        if (!File.Exists(sourcePath)) {
            Debug.LogWarning($"game-config.json not found, skip copying to Windows build root. path: {sourcePath}");
            return;
        }

        string buildOutputPath = Path.GetFullPath(BuildConfig.BUILD_PATH_WINDOWS);
        string buildRoot = Path.GetDirectoryName(buildOutputPath);
        string targetPath = Path.Combine(buildRoot, GameConfig.FileName);
        File.Copy(sourcePath, targetPath, true);
        Debug.Log($"game-config.json copied to Windows build root: {targetPath}");
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (string sourceFilePath in Directory.GetFiles(sourceDirectory)) {
            if (ShouldSkipKataGoRuntimeCopy(sourceFilePath)) {
                continue;
            }

            string targetFilePath = Path.Combine(targetDirectory, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, targetFilePath, true);
        }

        foreach (string sourceSubDirectory in Directory.GetDirectories(sourceDirectory)) {
            if (ShouldSkipKataGoRuntimeCopy(sourceSubDirectory)) {
                continue;
            }

            string targetSubDirectory = Path.Combine(targetDirectory, Path.GetFileName(sourceSubDirectory));
            CopyDirectory(sourceSubDirectory, targetSubDirectory);
        }
    }

    private static bool ShouldSkipKataGoRuntimeCopy(string path)
    {
        string name = Path.GetFileName(path);
        if (string.Equals(name, "analysis_logs", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(name, "KataGoData", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return string.Equals(Path.GetExtension(path), ".meta", StringComparison.OrdinalIgnoreCase);
    }

    private readonly struct NativeRuntimeCandidate
    {
        public readonly string engineName;
        public readonly string configFileName;
        public readonly bool isRequired;

        public NativeRuntimeCandidate(string engineName, string configFileName, bool isRequired)
        {
            this.engineName = engineName;
            this.configFileName = configFileName;
            this.isRequired = isRequired;
        }
    }
}
