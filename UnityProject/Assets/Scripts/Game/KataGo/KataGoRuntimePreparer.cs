using System;
using System.IO;
using UnityEngine;
using XNClient.Logger;

public sealed class KataGoRuntimePreparer
{
    private const string AndroidAbi = "arm64-v8a";
    private const string NoWriteAnalysisConfigFileName = "analysis_nowrite.cfg";
    private const string DefaultModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
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

        if (modelWasCached) {
            state = PrepareState.CachedCheck;
            cachedCheckStartTime = Time.realtimeSinceStartup;
            progress = 0.1f;
            detailText = MessageText.Get("katago_runtime_prepare_cached");
            return;
        }

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
        string abi = string.IsNullOrWhiteSpace(kataGoConfig.androidAbi)
            ? AndroidAbi
            : kataGoConfig.androidAbi;

        string root = Path.Combine(Application.persistentDataPath, KataGoRuntimeEnvironment.DirectoryName);
        string engineRoot = Path.Combine(root, "engines", "android", abi);
        string modelRoot = Path.Combine(root, "models");
        string configPath = Path.Combine(engineRoot, NoWriteAnalysisConfigFileName);
        string modelPath = Path.Combine(modelRoot, modelFileName);

        return new RuntimePaths(
            root,
            engineRoot,
            modelRoot,
            configPath,
            modelPath,
            modelFileName,
            abi);
    }

    private static bool IsRuntimePrepared(RuntimePaths paths)
    {
        if (!File.Exists(paths.configPath) || !File.Exists(paths.modelPath)) {
            return false;
        }

        string markerPath = BuildReadyMarkerPath(paths);
        if (!File.Exists(markerPath)) {
            return false;
        }

        FileInfo modelInfo = new FileInfo(paths.modelPath);
        return modelInfo.Length > 0;
    }

    private void StartCopyConfig()
    {
        Directory.CreateDirectory(runtimePaths.engineRoot);
        Directory.CreateDirectory(runtimePaths.modelRoot);
        string relativePath = $"KataGo/engines/android/{runtimePaths.abi}/{NoWriteAnalysisConfigFileName}";
        copyRequest = StreamingAssetsReader.Default.CopyToFile(relativePath, runtimePaths.configPath);
        state = PrepareState.CopyingConfig;
        progress = 0.05f;
        detailText = MessageText.Get("katago_runtime_prepare_config");
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

        progress = Mathf.Lerp(0.05f, 0.12f, copyRequest.Progress);
        if (!copyRequest.IsDone) {
            return;
        }

        if (!copyRequest.IsSuccess) {
            Fail("Copy Android KataGo config failed: " + copyRequest.Error);
            return;
        }

        copyRequest.Dispose();
        copyRequest = null;
        StartCopyModel();
    }

    private void StartCopyModel()
    {
        string relativePath = $"KataGo/models/{runtimePaths.modelFileName}{AndroidPackagedModelSuffix}";
        copyRequest = StreamingAssetsReader.Default.CopyToFile(relativePath, runtimePaths.modelPath);
        state = PrepareState.CopyingModel;
        progress = 0.12f;
        detailText = MessageText.Get("katago_runtime_prepare_model_first");
        XNLogger.LogInfo(
            "Start copying Android KataGo model.",
            ("source", copyRequest.SourcePathOrUrl),
            ("destination", copyRequest.DestinationPath));
    }

    private void UpdateCopyModel()
    {
        if (copyRequest == null) {
            Fail("Model copy request is null.");
            return;
        }

        progress = Mathf.Lerp(0.12f, 0.95f, copyRequest.Progress);
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
            ("modelPath", runtimePaths.modelPath),
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
        File.WriteAllText(markerPath, modelInfo.Length.ToString());
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
        public readonly string modelPath;
        public readonly string modelFileName;
        public readonly string abi;

        public RuntimePaths(
            string root,
            string engineRoot,
            string modelRoot,
            string configPath,
            string modelPath,
            string modelFileName,
            string abi)
        {
            this.root = root ?? string.Empty;
            this.engineRoot = engineRoot ?? string.Empty;
            this.modelRoot = modelRoot ?? string.Empty;
            this.configPath = configPath ?? string.Empty;
            this.modelPath = modelPath ?? string.Empty;
            this.modelFileName = modelFileName ?? string.Empty;
            this.abi = abi ?? string.Empty;
        }
    }
}
