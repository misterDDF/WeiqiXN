using System;
using System.IO;
using UnityEngine;
using XNClient.Logger;

public sealed class KataGoRuntimePreparer
{
    private const string AndroidAbi = "arm64-v8a";
    private const string AnalysisConfigFileName = "analysis_example.cfg";
    private const string NoWriteAnalysisConfigFileName = "analysis_nowrite.cfg";
    private const string DefaultModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
    private const string DefaultHumanSlModelFileName = "b18c384nbt-humanv0.bin.gz";
    private const string AndroidPackagedModelSuffix = ".bytes";
    private const float CachedCheckEstimatedSeconds = 1.5f;

    private enum PrepareState
    {
        None,
        CopyingConfig,
        CopyingModel,
        CachedCheck,
        Done,
        Failed,
    }

    private PrepareState state = PrepareState.None;
    private IStreamingAssetsFileCopyRequest copyRequest;
    private string[] configFileNamesToCopy = Array.Empty<string>();
    private string[] modelFileNamesToCopy = Array.Empty<string>();
    private int configCopyIndex;
    private int modelCopyIndex;
    private string currentConfigDestinationPath = string.Empty;
    private string currentModelDestinationPath = string.Empty;
    private string currentModelFileName = string.Empty;
    private float cachedCheckStartTime;
    private RuntimePaths runtimePaths;
    private string statusText = string.Empty;
    private string detailText = string.Empty;
    private float progress;
    private string error = string.Empty;
    private bool modelWasCached;

    public bool IsDone => state == PrepareState.Done || state == PrepareState.Failed;
    public bool IsFailed => state == PrepareState.Failed;
    public string StatusText => statusText;
    public string DetailText => detailText;
    public float Progress => progress;
    public string Error => error;
    public RuntimePaths Paths => runtimePaths;

    public void Start()
    {
        error = string.Empty;
        progress = 0f;
        statusText = MessageText.Get("katago_runtime_prepare_status");
        detailText = MessageText.Get("katago_runtime_prepare_checking");
        runtimePaths = ResolveRuntimePaths();
        modelWasCached = IsRuntimePrepared(runtimePaths);

        StartCopyConfig();
    }

    public void Update()
    {
        switch (state) {
            case PrepareState.CopyingConfig:
                UpdateCopyConfig();
                break;
            case PrepareState.CopyingModel:
                UpdateCopyModel();
                break;
            case PrepareState.CachedCheck:
                UpdateCachedCheck();
                break;
        }
    }

    public void Dispose()
    {
        copyRequest?.Dispose();
        copyRequest = null;
    }

    public static RuntimePaths ResolveRuntimePaths()
    {
        GameConfig.KataGoConfig kataGoConfig = GameConfig.Current.kataGo;
        string modelFileName = string.IsNullOrWhiteSpace(kataGoConfig.modelFileName)
            ? DefaultModelFileName
            : kataGoConfig.modelFileName;
        string humanSlModelFileName = string.IsNullOrWhiteSpace(kataGoConfig.humanSlModelFileName)
            ? DefaultHumanSlModelFileName
            : kataGoConfig.humanSlModelFileName;
        string abi = string.IsNullOrWhiteSpace(kataGoConfig.androidAbi)
            ? AndroidAbi
            : kataGoConfig.androidAbi;

        string root = Path.Combine(Application.persistentDataPath, KataGoRuntimeEnvironment.DirectoryName);
        string engineRoot = Path.Combine(root, "engines", "android", abi);
        string modelRoot = Path.Combine(root, "models");
        string configPath = Path.Combine(engineRoot, NoWriteAnalysisConfigFileName);
        string openClConfigPath = Path.Combine(engineRoot, AnalysisConfigFileName);
        string modelPath = Path.Combine(modelRoot, modelFileName);
        string humanSlModelPath = Path.Combine(modelRoot, humanSlModelFileName);

        return new RuntimePaths(
            root,
            engineRoot,
            modelRoot,
            configPath,
            openClConfigPath,
            modelPath,
            humanSlModelPath,
            modelFileName,
            humanSlModelFileName,
            abi);
    }

    private static bool IsRuntimePrepared(RuntimePaths paths)
    {
        if (!File.Exists(paths.configPath) || !File.Exists(paths.modelPath) || !File.Exists(paths.humanSlModelPath)) {
            return false;
        }

        if (GameConfig.Current.kataGo.androidPreferOpenCl) {
            if (!File.Exists(paths.openClConfigPath)) {
                return false;
            }
        }

        string markerPath = BuildReadyMarkerPath(paths);
        if (!File.Exists(markerPath)) {
            return false;
        }

        FileInfo modelInfo = new FileInfo(paths.modelPath);
        FileInfo humanSlModelInfo = new FileInfo(paths.humanSlModelPath);
        return modelInfo.Length > 0 && humanSlModelInfo.Length > 0;
    }

    private void StartCopyConfig()
    {
        Directory.CreateDirectory(runtimePaths.engineRoot);
        Directory.CreateDirectory(runtimePaths.modelRoot);
        configFileNamesToCopy = GameConfig.Current.kataGo.androidPreferOpenCl
            ? new[] { NoWriteAnalysisConfigFileName, AnalysisConfigFileName }
            : new[] { NoWriteAnalysisConfigFileName };
        configCopyIndex = 0;
        StartCurrentConfigCopy();
        state = PrepareState.CopyingConfig;
        progress = 0.05f;
        detailText = MessageText.Get("katago_runtime_prepare_config");
    }

    private void StartCurrentConfigCopy()
    {
        string configFileName = configFileNamesToCopy[configCopyIndex];
        currentConfigDestinationPath = Path.Combine(runtimePaths.engineRoot, configFileName);
        string relativePath = $"KataGo/engines/android/{runtimePaths.abi}/{configFileName}";
        copyRequest = StreamingAssetsReader.Default.CopyToFile(relativePath, currentConfigDestinationPath);
        XNLogger.LogInfo(
            "Start copying Android KataGo config.",
            ("source", copyRequest.SourcePathOrUrl),
            ("destination", copyRequest.DestinationPath));
    }

    private void UpdateCopyConfig()
    {
        if (copyRequest == null) {
            Fail("Config copy request is null.");
            return;
        }

        float configProgressStep = (configCopyIndex + copyRequest.Progress) / Mathf.Max(1f, configFileNamesToCopy.Length);
        progress = Mathf.Lerp(0.05f, 0.12f, configProgressStep);
        if (!copyRequest.IsDone) {
            return;
        }

        if (!copyRequest.IsSuccess) {
            Fail("Copy Android KataGo config failed: " + copyRequest.Error);
            return;
        }

        copyRequest.Dispose();
        copyRequest = null;
        configCopyIndex++;
        if (configCopyIndex < configFileNamesToCopy.Length) {
            StartCurrentConfigCopy();
            return;
        }

        ContinueAfterConfigFiles();
    }

    private void ContinueAfterConfigFiles()
    {
        if (modelWasCached) {
            state = PrepareState.CachedCheck;
            cachedCheckStartTime = Time.realtimeSinceStartup;
            progress = 0.12f;
            detailText = MessageText.Get("katago_runtime_prepare_cached");
            return;
        }

        StartCopyModel();
    }

    private void StartCopyModel()
    {
        modelFileNamesToCopy = new[] { runtimePaths.modelFileName, runtimePaths.humanSlModelFileName };
        modelCopyIndex = 0;
        StartCurrentModelCopy();
        state = PrepareState.CopyingModel;
        progress = 0.12f;
    }

    private void StartCurrentModelCopy()
    {
        currentModelFileName = modelFileNamesToCopy[modelCopyIndex];
        currentModelDestinationPath = Path.Combine(runtimePaths.modelRoot, currentModelFileName);
        string relativePath = $"KataGo/models/{currentModelFileName}{AndroidPackagedModelSuffix}";
        copyRequest = StreamingAssetsReader.Default.CopyToFile(relativePath, currentModelDestinationPath);
        detailText = MessageText.Get("katago_runtime_prepare_model_first");
        XNLogger.LogInfo(
            "Start copying Android KataGo model.",
            ("source", copyRequest.SourcePathOrUrl),
            ("destination", copyRequest.DestinationPath),
            ("modelFileName", currentModelFileName));
    }

    private void UpdateCopyModel()
    {
        if (copyRequest == null) {
            Fail("Model copy request is null.");
            return;
        }

        float modelProgressStep = (modelCopyIndex + copyRequest.Progress) / Mathf.Max(1f, modelFileNamesToCopy.Length);
        progress = Mathf.Lerp(0.12f, 0.95f, modelProgressStep);
        detailText = MessageText.Format("katago_runtime_prepare_model_progress", FormatBytes(copyRequest.BytesCopied));
        if (!copyRequest.IsDone) {
            return;
        }

        if (!copyRequest.IsSuccess) {
            Fail("Copy Android KataGo model failed: " + copyRequest.Error);
            return;
        }

        copyRequest.Dispose();
        copyRequest = null;
        modelCopyIndex++;
        if (modelCopyIndex < modelFileNamesToCopy.Length) {
            StartCurrentModelCopy();
            return;
        }

        WriteReadyMarker(runtimePaths);
        Complete();
    }

    private void UpdateCachedCheck()
    {
        float elapsed = Time.realtimeSinceStartup - cachedCheckStartTime;
        progress = Mathf.Lerp(0.1f, 0.95f, Mathf.Clamp01(elapsed / CachedCheckEstimatedSeconds));
        if (elapsed < 0.2f) {
            return;
        }

        Complete();
    }

    private void Complete()
    {
        state = PrepareState.Done;
        progress = 1f;
        detailText = modelWasCached
            ? MessageText.Get("katago_runtime_prepare_cached_done")
            : MessageText.Get("katago_runtime_prepare_done");
        XNLogger.LogInfo(
            "Android KataGo runtime prepared.",
            ("root", runtimePaths.root),
            ("configPath", runtimePaths.configPath),
            ("openClConfigPath", runtimePaths.openClConfigPath),
            ("modelPath", runtimePaths.modelPath),
            ("humanSlModelPath", runtimePaths.humanSlModelPath),
            ("cached", modelWasCached.ToString()));
    }

    private void Fail(string message)
    {
        state = PrepareState.Failed;
        error = message ?? string.Empty;
        progress = 1f;
        detailText = error;
        copyRequest?.Dispose();
        copyRequest = null;
        XNLogger.LogError("Android KataGo runtime prepare failed.", ("error", error));
    }

    private static void WriteReadyMarker(RuntimePaths paths)
    {
        string markerPath = BuildReadyMarkerPath(paths);
        string markerDirectory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(markerDirectory)) {
            Directory.CreateDirectory(markerDirectory);
        }

        FileInfo modelInfo = new FileInfo(paths.modelPath);
        FileInfo humanSlModelInfo = new FileInfo(paths.humanSlModelPath);
        File.WriteAllText(markerPath, $"{modelInfo.Length}:{humanSlModelInfo.Length}");
    }

    private static string BuildReadyMarkerPath(RuntimePaths paths)
    {
        return paths.modelPath + ".ready";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L) {
            return $"{bytes} B";
        }

        double kb = bytes / 1024d;
        if (kb < 1024d) {
            return $"{kb:0.0} KB";
        }

        double mb = kb / 1024d;
        return $"{mb:0.0} MB";
    }

    public readonly struct RuntimePaths
    {
        public readonly string root;
        public readonly string engineRoot;
        public readonly string modelRoot;
        public readonly string configPath;
        public readonly string openClConfigPath;
        public readonly string modelPath;
        public readonly string humanSlModelPath;
        public readonly string modelFileName;
        public readonly string humanSlModelFileName;
        public readonly string abi;

        public RuntimePaths(
            string root,
            string engineRoot,
            string modelRoot,
            string configPath,
            string openClConfigPath,
            string modelPath,
            string humanSlModelPath,
            string modelFileName,
            string humanSlModelFileName,
            string abi)
        {
            this.root = root ?? string.Empty;
            this.engineRoot = engineRoot ?? string.Empty;
            this.modelRoot = modelRoot ?? string.Empty;
            this.configPath = configPath ?? string.Empty;
            this.openClConfigPath = openClConfigPath ?? string.Empty;
            this.modelPath = modelPath ?? string.Empty;
            this.humanSlModelPath = humanSlModelPath ?? string.Empty;
            this.modelFileName = modelFileName ?? string.Empty;
            this.humanSlModelFileName = humanSlModelFileName ?? string.Empty;
            this.abi = abi ?? string.Empty;
        }
    }
}
