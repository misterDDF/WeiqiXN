using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.Logger;
using Debug = UnityEngine.Debug;

public static class KataGoBootstrap
{
    private static readonly object startupStatusLock = new object();
    private static KataGoStartupStatus startupStatus = KataGoStartupStatus.NotStarted;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_ANDROID
    private const string OpenClEngineName = "opencl";
    private const string CpuEngineName = "eigenavx2";
    private const string NativeBridgeDllName = "katago_bridge.dll";
    private const string AndroidNativeOpenClEngineName = "android-opencl";
    private const string AndroidNativeCpuEngineName = "android-eigen";
    private const string ModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
    private const string AnalysisConfigFileName = "analysis_example.cfg";
    private const string NoWriteAnalysisConfigFileName = "analysis_nowrite.cfg";
    private const int OpenClSmokeTestTimeoutMs = 300000;
    private const int AndroidOpenClSmokeTestTimeoutMs = int.MaxValue;
    private const int CpuSmokeTestTimeoutMs = 45000;
    private const int SmokeTestProgressPollMs = 250;
    private const int OpenClWarmupEstimatedMs = 55000;
    private const int AndroidOpenClWarmupEstimatedMs = 120000;
    private const int CpuWarmupEstimatedMs = 10000;
    private const int SmokeTestMaxVisits = 1;
    private const int AnalyzeTimeoutMs = 45000;
    private const int DefaultAnalyzeRetryCount = 1;
    private const int DefaultAnalyzeRetryDelayMs = 500;
#if UNITY_ANDROID && !UNITY_EDITOR
    private const int AndroidAnalyzePollMs = 250;
    private const int DefaultAndroidAnalyzeBackgroundGraceMs = 300000;
#endif
    private const bool HumanSlProfileEnabled = false;
    private static readonly int[] SmokeTestBoardSizes = { 9, 13, 19 };
    private static readonly float[] SmokeTestBoardProgressWeights = { 0.90f, 0.05f, 0.05f };
    private static readonly float[] AndroidOpenClSmokeTestProgressWeights = { 1f, 0f, 0f };

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static Win32KataGoProcess process;
    private static Win32NativeKataGoEngine windowsNativeEngine;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidNativeKataGoEngine androidNativeEngine;
#endif
    private static readonly SemaphoreSlim analysisSemaphore = new SemaphoreSlim(1, 1);
    private static KataGoPaths[] engineCandidates;
    private static KataGoPaths activePaths;
    private static int activeCandidateIndex = -1;
    private static bool hasActivePaths;
    private static bool isStarted;
    private static bool activeBackendIsNative;
    private static bool gameRootWriteWarningShown;
#endif
    private static CancellationTokenSource cancellationTokenSource;
    private static Task startupTask;

    public static KataGoStartupStatus GetStartupStatus()
    {
        lock (startupStatusLock) {
            return startupStatus;
        }
    }

    public static void Start()
    {
        if (startupTask != null && !startupTask.IsCompleted) {
            return;
        }

        PlatformConfig platformConfig = ResolvePlatformConfig();
        if (!platformConfig.isSupported) {
            SetStartupStatus(MessageText.Get("katago_unavailable_status"), platformConfig.unsupportedReason, 1f, true, false, true, null);
            XNLogger.LogWarn("KataGo startup skipped.", ("reason", platformConfig.unsupportedReason));
            return;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_ANDROID
        KataGoBackendMode backendMode = GameConfig.Current.kataGo.ResolveCurrentBackend();
        if (backendMode == KataGoBackendMode.Disabled) {
            SetStartupStatus(MessageText.Get("katago_unavailable_status"), "KataGo backend is disabled by game-config.json.", 1f, true, false, true, null);
            XNLogger.LogWarn("KataGo startup skipped.", ("reason", "Backend disabled by game-config.json."));
            return;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!platformConfig.canWriteGameRoot) {
            ShowGameRootWriteWarning(platformConfig);
            XNLogger.LogWarn(
                "KataGo game root write check failed, OpenCL will be skipped.",
                ("gameRoot", platformConfig.gameRoot),
                ("reason", platformConfig.writeFailureReason));
        }
#endif

        if (backendMode == KataGoBackendMode.Native) {
#if UNITY_ANDROID && !UNITY_EDITOR
            KataGoPaths[] nativeCandidates = ResolveAndroidNativeCandidatePaths(platformConfig);
#else
            KataGoPaths[] nativeCandidates = ResolveNativeCandidatePaths(platformConfig);
#endif
            if (nativeCandidates.Length == 0) {
                SetStartupStatus(MessageText.Get("katago_failed_status"), MessageText.Get("katago_no_engine_candidates"), 1f, true, true, false, null);
                XNLogger.LogError("KataGo native startup skipped.", ("reason", "No KataGo native engine candidates resolved."));
                return;
            }

            StartNativeTask(nativeCandidates);
            return;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        KataGoPaths[] candidates = ResolveCandidatePaths(platformConfig);
        if (candidates.Length == 0) {
            SetStartupStatus(MessageText.Get("katago_failed_status"), MessageText.Get("katago_no_engine_candidates"), 1f, true, true, false, null);
            XNLogger.LogError("KataGo startup skipped.", ("reason", "No KataGo engine candidates resolved."));
            return;
        }

        StartProcessTask(candidates);
#else
        SetStartupStatus(MessageText.Get("katago_unavailable_status"), "Android KataGo only supports native backend.", 1f, true, false, true, null);
        XNLogger.LogWarn("KataGo startup skipped.", ("reason", "Android KataGo only supports native backend."));
#endif
#else
        SetStartupStatus(MessageText.Get("katago_unavailable_status"), MessageText.Get("katago_local_process_not_compiled"), 1f, true, false, true, null);
        XNLogger.LogWarn("KataGo startup skipped.", ("reason", "Local process startup is not compiled for this platform."));
#endif
    }

    public static void Stop()
    {
        StopProcessTask();
    }

    public static async Task<JArray> AnalyzeOwnershipAsync(JObject query)
    {
        JObject result = await AnalyzeAsync(query);
        return result?["ownership"] as JArray;
    }

    public static async Task<JObject> AnalyzeAsync(JObject query)
    {
        return await AnalyzeAsync(query, CreateDefaultAnalyzeOptions("default"), CancellationToken.None);
    }

    public static async Task<JObject> AnalyzeAsync(JObject query, KataGoAnalyzeOptions options, CancellationToken cancellationToken)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_ANDROID
        if (query == null) {
            XNLogger.LogError("KataGo analyze failed, query is null.");
            return null;
        }

        KataGoAnalyzeOptions safeOptions = NormalizeAnalyzeOptions(options);
        try {
            if (activeBackendIsNative) {
                return await AnalyzeNativeWithRetryAsync(query, safeOptions, cancellationToken);
            }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            else {
                return await AnalyzeProcessWithRetryAsync(query, safeOptions, cancellationToken);
            }
#else
            XNLogger.LogError("KataGo analyze failed, non-native backend is unsupported on Android.");
            return null;
#endif
        }
        catch (OperationCanceledException) {
            XNLogger.LogWarn(
                "KataGo analyze canceled.",
                ("id", query["id"]?.ToString() ?? string.Empty),
                ("kind", safeOptions.requestKind ?? string.Empty));
            if (cancellationToken.IsCancellationRequested) {
                throw;
            }

            return null;
        }
#else
        await Task.CompletedTask;
        XNLogger.LogWarn("KataGo analyze skipped.", ("reason", "Local process analyze is not compiled for this platform."));
        return null;
#endif
    }

    public static KataGoAnalyzeOptions CreateDefaultAnalyzeOptions(string requestKind)
    {
        return new KataGoAnalyzeOptions
        {
            timeoutMs = AnalyzeTimeoutMs,
            retryCount = DefaultAnalyzeRetryCount,
            retryDelayMs = DefaultAnalyzeRetryDelayMs,
            retryUntilCanceled = false,
            restartEngineBeforeRetry = true,
            waitForegroundOnAndroid = true,
            androidBackgroundGraceMs = GetDefaultAndroidAnalyzeBackgroundGraceMs(),
            requestKind = requestKind ?? string.Empty,
        };
    }

    public static KataGoAnalyzeOptions CreateRetryUntilCanceledAnalyzeOptions(string requestKind)
    {
        KataGoAnalyzeOptions options = CreateDefaultAnalyzeOptions(requestKind);
        options.retryUntilCanceled = true;
        return options;
    }

    public static KataGoAnalyzeOptions CreateSingleAttemptAnalyzeOptions(string requestKind)
    {
        KataGoAnalyzeOptions options = CreateDefaultAnalyzeOptions(requestKind);
        options.retryCount = 0;
        options.retryUntilCanceled = false;
        return options;
    }

    private static KataGoAnalyzeOptions NormalizeAnalyzeOptions(KataGoAnalyzeOptions options)
    {
        if (options.timeoutMs <= 0) {
            options.timeoutMs = AnalyzeTimeoutMs;
        }

        options.retryCount = Math.Max(0, options.retryCount);
        options.retryDelayMs = Math.Max(0, options.retryDelayMs);
        if (options.androidBackgroundGraceMs < 0) {
            options.androidBackgroundGraceMs = 0;
        }

        if (string.IsNullOrEmpty(options.requestKind)) {
            options.requestKind = "default";
        }

        return options;
    }

    private static int GetDefaultAndroidAnalyzeBackgroundGraceMs()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return DefaultAndroidAnalyzeBackgroundGraceMs;
#else
        return 0;
#endif
    }

    public static bool CanUseHumanSlProfile()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        return HumanSlProfileEnabled;
#else
        return false;
#endif
    }

    private static void SetStartupStatus(
        string statusText,
        string detailText,
        float progress,
        bool isFinished,
        bool isFailed,
        bool isSkipped,
        string engineName)
    {
        lock (startupStatusLock) {
            startupStatus = new KataGoStartupStatus(
                statusText,
                detailText,
                Mathf.Clamp01(progress),
                isFinished,
                isFailed,
                isSkipped,
                engineName);
        }
    }

    private static PlatformConfig ResolvePlatformConfig()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        KataGoRuntimeEnvironment.RuntimeInfo runtimeInfo = KataGoRuntimeEnvironment.Resolve();
        string kataGoRoot = runtimeInfo.kataGoRoot;
        GameConfig.KataGoConfig kataGoConfig = GameConfig.Current.kataGo;
        return new PlatformConfig
        {
            isSupported = true,
            unsupportedReason = null,
            gameRoot = runtimeInfo.gameRoot,
            kataGoRoot = kataGoRoot,
            engineRoot = Path.Combine(kataGoRoot, "engines", "win-x64"),
            canWriteGameRoot = runtimeInfo.canWriteGameRoot,
            writeFailureReason = runtimeInfo.writeFailureReason,
            modelFileName = string.IsNullOrWhiteSpace(kataGoConfig.modelFileName) ? ModelFileName : kataGoConfig.modelFileName,
            windowsPreferOpenCl = kataGoConfig.windowsPreferOpenCl,
            windowsAllowCpuFallback = kataGoConfig.windowsAllowCpuFallback,
            windowsNativeOpenClEngineName = kataGoConfig.windowsNativeOpenClEngineName,
            windowsNativeCpuEngineName = kataGoConfig.windowsNativeCpuEngineName,
        };
#elif UNITY_ANDROID
        KataGoRuntimePreparer.RuntimePaths runtimePaths = KataGoRuntimePreparer.ResolveRuntimePaths();
        GameConfig.KataGoConfig kataGoConfig = GameConfig.Current.kataGo;
        return new PlatformConfig
        {
            isSupported = true,
            unsupportedReason = null,
            gameRoot = Application.persistentDataPath,
            kataGoRoot = runtimePaths.root,
            engineRoot = runtimePaths.engineRoot,
            canWriteGameRoot = true,
            writeFailureReason = null,
            modelFileName = runtimePaths.modelFileName,
            androidAbi = runtimePaths.abi,
            androidPreferOpenCl = kataGoConfig.androidPreferOpenCl,
            androidAllowCpuFallback = kataGoConfig.androidAllowCpuFallback,
            androidNativeOpenClLibraryName = kataGoConfig.androidNativeOpenClLibraryName,
            androidNativeCpuLibraryName = kataGoConfig.androidNativeCpuLibraryName,
        };
#else
        return new PlatformConfig
        {
            isSupported = false,
            unsupportedReason = "No bundled KataGo engine is configured for this platform.",
        };
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static KataGoPaths[] ResolveCandidatePaths(PlatformConfig platformConfig)
    {
        if (!platformConfig.windowsPreferOpenCl) {
            return new[]
            {
                ResolvePaths(platformConfig, CpuEngineName, CpuSmokeTestTimeoutMs, !platformConfig.canWriteGameRoot),
            };
        }

        if (!platformConfig.canWriteGameRoot) {
            XNLogger.LogWarn(
                "KataGo OpenCL skipped because game root is not writable.",
                ("gameRoot", platformConfig.gameRoot),
                ("reason", platformConfig.writeFailureReason));
            return new[]
            {
                ResolvePaths(platformConfig, CpuEngineName, CpuSmokeTestTimeoutMs, true),
            };
        }

        if (!platformConfig.windowsAllowCpuFallback) {
            return new[]
            {
                ResolvePaths(platformConfig, OpenClEngineName, OpenClSmokeTestTimeoutMs, false),
            };
        }

        return new[]
        {
            ResolvePaths(platformConfig, OpenClEngineName, OpenClSmokeTestTimeoutMs, false),
            ResolvePaths(platformConfig, CpuEngineName, CpuSmokeTestTimeoutMs, false),
        };
    }

    private static KataGoPaths ResolvePaths(PlatformConfig platformConfig, string engineName, int smokeTestTimeoutMs, bool noWriteMode)
    {
        string engineRoot = Path.Combine(platformConfig.engineRoot, engineName);
        string configFileName = noWriteMode && engineName == CpuEngineName
            ? NoWriteAnalysisConfigFileName
            : AnalysisConfigFileName;
        return new KataGoPaths
        {
            exePath = Path.Combine(engineRoot, "katago.exe"),
            configPath = Path.Combine(engineRoot, configFileName),
            modelPath = Path.Combine(platformConfig.kataGoRoot, "models", platformConfig.modelFileName),
            workingDirectory = engineRoot,
            engineName = engineName,
            smokeTestTimeoutMs = smokeTestTimeoutMs,
            noWriteMode = noWriteMode,
            isNative = false,
        };
    }

    private static KataGoPaths[] ResolveNativeCandidatePaths(PlatformConfig platformConfig)
    {
        string nativeCpuEngineName = string.IsNullOrWhiteSpace(platformConfig.windowsNativeCpuEngineName)
            ? GameConfig.KataGoConfig.Default.windowsNativeCpuEngineName
            : platformConfig.windowsNativeCpuEngineName;
        string nativeOpenClEngineName = string.IsNullOrWhiteSpace(platformConfig.windowsNativeOpenClEngineName)
            ? GameConfig.KataGoConfig.Default.windowsNativeOpenClEngineName
            : platformConfig.windowsNativeOpenClEngineName;

        if (!platformConfig.windowsPreferOpenCl) {
            return new[]
            {
                ResolveNativePaths(platformConfig, nativeCpuEngineName, CpuSmokeTestTimeoutMs, true),
            };
        }

        if (!platformConfig.canWriteGameRoot) {
            XNLogger.LogWarn(
                "KataGo native OpenCL skipped because game root is not writable.",
                ("gameRoot", platformConfig.gameRoot),
                ("reason", platformConfig.writeFailureReason));
            return new[]
            {
                ResolveNativePaths(platformConfig, nativeCpuEngineName, CpuSmokeTestTimeoutMs, true),
            };
        }

        if (!platformConfig.windowsAllowCpuFallback) {
            return new[]
            {
                ResolveNativePaths(platformConfig, nativeOpenClEngineName, OpenClSmokeTestTimeoutMs, false),
            };
        }

        return new[]
        {
            ResolveNativePaths(platformConfig, nativeOpenClEngineName, OpenClSmokeTestTimeoutMs, false),
            ResolveNativePaths(platformConfig, nativeCpuEngineName, CpuSmokeTestTimeoutMs, true),
        };
    }

    private static KataGoPaths ResolveNativePaths(PlatformConfig platformConfig, string engineName, int smokeTestTimeoutMs, bool noWriteMode)
    {
        string engineRoot = Path.Combine(platformConfig.engineRoot, engineName);
        string configFileName = noWriteMode ? NoWriteAnalysisConfigFileName : AnalysisConfigFileName;
        return new KataGoPaths
        {
            exePath = null,
            nativeLibraryPath = Path.Combine(engineRoot, NativeBridgeDllName),
            configPath = Path.Combine(engineRoot, configFileName),
            modelPath = Path.Combine(platformConfig.kataGoRoot, "models", platformConfig.modelFileName),
            workingDirectory = engineRoot,
            engineName = engineName,
            smokeTestTimeoutMs = smokeTestTimeoutMs,
            noWriteMode = noWriteMode,
            isNative = true,
        };
    }

#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    private static KataGoPaths[] ResolveAndroidNativeCandidatePaths(PlatformConfig platformConfig)
    {
        if (!platformConfig.androidPreferOpenCl) {
            return new[]
            {
                ResolveAndroidNativePaths(platformConfig, AndroidNativeCpuEngineName, platformConfig.androidNativeCpuLibraryName, CpuSmokeTestTimeoutMs, true),
            };
        }

        if (!platformConfig.androidAllowCpuFallback) {
            return new[] 
            {
                ResolveAndroidNativePaths(platformConfig, AndroidNativeOpenClEngineName, platformConfig.androidNativeOpenClLibraryName, AndroidOpenClSmokeTestTimeoutMs, false),
            };
        }

        return new[]
        {
            ResolveAndroidNativePaths(platformConfig, AndroidNativeOpenClEngineName, platformConfig.androidNativeOpenClLibraryName, AndroidOpenClSmokeTestTimeoutMs, false),
            ResolveAndroidNativePaths(platformConfig, AndroidNativeCpuEngineName, platformConfig.androidNativeCpuLibraryName, CpuSmokeTestTimeoutMs, true),
        };
    }

    private static KataGoPaths ResolveAndroidNativePaths(
        PlatformConfig platformConfig,
        string engineName,
        string nativeLibraryName,
        int smokeTestTimeoutMs,
        bool noWriteMode)
    {
        string configFileName = noWriteMode ? NoWriteAnalysisConfigFileName : AnalysisConfigFileName;
        return new KataGoPaths
        {
            exePath = null,
            nativeLibraryPath = nativeLibraryName,
            configPath = Path.Combine(platformConfig.engineRoot, configFileName),
            modelPath = Path.Combine(platformConfig.kataGoRoot, "models", platformConfig.modelFileName),
            workingDirectory = platformConfig.engineRoot,
            engineName = engineName,
            smokeTestTimeoutMs = smokeTestTimeoutMs,
            noWriteMode = noWriteMode,
            isNative = true,
            skipNativeLibraryFileCheck = true,
        };
    }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static void StartProcessTask(KataGoPaths[] candidates)
    {
        engineCandidates = candidates;
        activeCandidateIndex = -1;
        hasActivePaths = false;
        isStarted = true;
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        SetStartupStatus(MessageText.Get("katago_warmup_status"), MessageText.Get("katago_detecting_engine"), 0.05f, false, false, false, null);
        startupTask = Task.Run(() => StartFirstAvailableEngine(candidates, 0, cancellationTokenSource.Token));
    }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_ANDROID
    private static void StopProcessTask()
    {
        try {
            cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException) {
        }

        StopProcess();
        StopNativeEngine();

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        startupTask = null;
        engineCandidates = null;
        activeCandidateIndex = -1;
        hasActivePaths = false;
        isStarted = false;
        activeBackendIsNative = false;
        SetStartupStatus(MessageText.Get("katago_not_started_status"), string.Empty, 0f, true, false, true, null);
    }

    private static void StartNativeTask(KataGoPaths[] candidates)
    {
        engineCandidates = candidates;
        activeCandidateIndex = -1;
        hasActivePaths = false;
        isStarted = true;
        activeBackendIsNative = true;
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        SetStartupStatus(MessageText.Get("katago_warmup_status"), MessageText.Get("katago_detecting_engine"), 0.05f, false, false, false, null);
        startupTask = Task.Run(() => StartFirstAvailableNativeEngine(candidates, 0, cancellationTokenSource.Token));
    }

    private static async Task<bool> EnsureNativeReadyAsync()
    {
        if (startupTask != null && !startupTask.IsCompleted) {
            await startupTask;
        }

        if (IsNativeEngineRunning()) {
            return true;
        }

        KataGoPaths[] candidates = engineCandidates;
        if (!isStarted || candidates == null || candidates.Length == 0) {
            return false;
        }

        int restartIndex = hasActivePaths ? activeCandidateIndex : 0;
        XNLogger.LogWarn(
            "KataGo native engine is not running, restarting before analyze.",
            ("engine", hasActivePaths ? activePaths.engineName : "none"));

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        SetStartupStatus(MessageText.Get("katago_warmup_status"), MessageText.Get("katago_restarting_process"), 0.05f, false, false, false, hasActivePaths ? activePaths.engineName : null);
        startupTask = Task.Run(() => StartFirstAvailableNativeEngine(candidates, restartIndex, cancellationTokenSource.Token));
        await startupTask;

        return IsNativeEngineRunning();
    }

    private static void MarkNativeEngineForRestart()
    {
        StopNativeEngine();
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static async Task<bool> EnsureProcessReadyAsync()
    {
        if (startupTask != null && !startupTask.IsCompleted) {
            await startupTask;
        }

        if (IsProcessRunning()) {
            return true;
        }

        KataGoPaths[] candidates = engineCandidates;
        if (!isStarted || candidates == null || candidates.Length == 0) {
            return false;
        }

        int restartIndex = hasActivePaths ? activeCandidateIndex : 0;
        XNLogger.LogWarn(
            "KataGo process is not running, restarting before analyze.",
            ("engine", hasActivePaths ? activePaths.engineName : "none"));

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        SetStartupStatus(MessageText.Get("katago_warmup_status"), MessageText.Get("katago_restarting_process"), 0.05f, false, false, false, hasActivePaths ? activePaths.engineName : null);
        startupTask = Task.Run(() => StartFirstAvailableEngine(candidates, restartIndex, cancellationTokenSource.Token));
        await startupTask;

        return IsProcessRunning();
    }

    private static void MarkProcessForRestart()
    {
        StopProcess();
    }

    private static bool IsProcessRunning()
    {
        try {
            return process != null && process.IsRunning;
        }
        catch (Exception) {
            return false;
        }
    }
#endif

    private static async Task<JObject> AnalyzeNativeWithRetryAsync(JObject query, KataGoAnalyzeOptions options, CancellationToken cancellationToken)
    {
        string requestId = query["id"]?.ToString() ?? string.Empty;
        int attempt = 0;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            JObject result = await AnalyzeNativeOnceAsync(query, options, cancellationToken);
            if (result != null) {
                if (attempt > 0) {
                    XNLogger.LogInfo(
                        "KataGo native analyze retry succeeded.",
                        ("id", requestId),
                        ("attempt", (attempt + 1).ToString()),
                        ("kind", options.requestKind ?? string.Empty));
                }
                return result;
            }

            if (!ShouldRetryAnalyze(options, attempt)) {
                break;
            }

            XNLogger.LogWarn(
                "KataGo native analyze failed, restarting engine and retrying.",
                ("id", requestId),
                ("attempt", (attempt + 1).ToString()),
                ("kind", options.requestKind ?? string.Empty),
                ("engine", hasActivePaths ? activePaths.engineName : "unknown"));
            if (options.restartEngineBeforeRetry) {
                MarkNativeEngineForRestart();
            }
            await DelayBeforeAnalyzeRetry(options, cancellationToken);
            attempt += 1;
        }

        return null;
    }

    private static async Task<JObject> AnalyzeNativeOnceAsync(JObject query, KataGoAnalyzeOptions options, CancellationToken cancellationToken)
    {
        await analysisSemaphore.WaitAsync(cancellationToken);
        try {
            if (!await EnsureNativeReadyAsync()) {
                XNLogger.LogError("KataGo analyze failed, native engine is not running.", ("id", query["id"]?.ToString() ?? string.Empty));
                return null;
            }

            JObject result = await Task.Run(() => AnalyzeNative(query, ResolveAnalyzeTimeoutMs(options)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally {
            analysisSemaphore.Release();
        }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static async Task<JObject> AnalyzeProcessWithRetryAsync(JObject query, KataGoAnalyzeOptions options, CancellationToken cancellationToken)
    {
        string requestId = query["id"]?.ToString() ?? string.Empty;
        int attempt = 0;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            JObject result = await AnalyzeProcessOnceAsync(query, options, cancellationToken);
            if (result != null) {
                if (attempt > 0) {
                    XNLogger.LogInfo(
                        "KataGo analyze retry succeeded.",
                        ("id", requestId),
                        ("attempt", (attempt + 1).ToString()),
                        ("kind", options.requestKind ?? string.Empty));
                }
                return result;
            }

            if (!ShouldRetryAnalyze(options, attempt)) {
                break;
            }

            XNLogger.LogWarn(
                "KataGo analyze failed, restarting process and retrying.",
                ("id", requestId),
                ("attempt", (attempt + 1).ToString()),
                ("kind", options.requestKind ?? string.Empty),
                ("engine", hasActivePaths ? activePaths.engineName : "unknown"));
            if (options.restartEngineBeforeRetry) {
                MarkProcessForRestart();
            }
            await DelayBeforeAnalyzeRetry(options, cancellationToken);
            attempt += 1;
        }

        return null;
    }

    private static async Task<JObject> AnalyzeProcessOnceAsync(JObject query, KataGoAnalyzeOptions options, CancellationToken cancellationToken)
    {
        await analysisSemaphore.WaitAsync(cancellationToken);
        try {
            if (!await EnsureProcessReadyAsync()) {
                XNLogger.LogError("KataGo analyze failed, process is not running.", ("id", query["id"]?.ToString() ?? string.Empty));
                return null;
            }

            JObject result = await Task.Run(() => Analyze(query, ResolveAnalyzeTimeoutMs(options)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally {
            analysisSemaphore.Release();
        }
    }
#endif

    private static bool ShouldRetryAnalyze(KataGoAnalyzeOptions options, int completedRetryCount)
    {
        return options.retryUntilCanceled || completedRetryCount < options.retryCount;
    }

    private static async Task DelayBeforeAnalyzeRetry(KataGoAnalyzeOptions options, CancellationToken cancellationToken)
    {
        if (options.retryDelayMs <= 0) {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        await Task.Delay(options.retryDelayMs, cancellationToken);
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static void ShowGameRootWriteWarning(PlatformConfig platformConfig)
    {
        if (gameRootWriteWarningShown) {
            return;
        }

        gameRootWriteWarningShown = true;
        string title = MessageText.Get("katago_permission_title");
        string message =
            MessageText.Format("katago_permission_content", platformConfig.gameRoot, platformConfig.writeFailureReason);

        ConfirmPopup.ShowTip(title, message, null, MessageText.Get("common_got_it"));
    }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static void StartFirstAvailableEngine(KataGoPaths[] candidates, int startCandidateIndex, CancellationToken cancellationToken)
    {
        StopProcess();
        hasActivePaths = false;
        activeCandidateIndex = -1;

        if (candidates == null || candidates.Length == 0) {
            SetStartupStatus(MessageText.Get("katago_failed_status"), MessageText.Get("katago_no_engine_candidates"), 1f, true, true, false, null);
            XNLogger.LogError("KataGo startup failed, no engine candidate is available.");
            return;
        }

        int safeStartIndex = Math.Max(0, startCandidateIndex);
        for (int i = safeStartIndex; i < candidates.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                StopProcess();
                SetStartupStatus(MessageText.Get("katago_cancelled_status"), string.Empty, 1f, true, true, false, null);
                return;
            }

            KataGoPaths paths = candidates[i];
            float candidateStartProgress = GetNativeCandidateStartProgress(paths, i, candidates.Length);
            float candidateEndProgress = GetNativeCandidateEndProgress(paths, i, candidates.Length);
            float candidateFailureProgress = GetNativeCandidateFailureProgress(paths, i, candidates.Length);
            SetStartupStatus(
                MessageText.Get("katago_warmup_status"),
                BuildCandidateDetail(paths, i > 0),
                candidateStartProgress,
                false,
                false,
                false,
                paths.engineName);

            if (!paths.IsValid(out string invalidReason)) {
                SetStartupStatus(
                    MessageText.Get("katago_warmup_status"),
                    MessageText.Format("katago_skip_engine", paths.engineName, invalidReason),
                    candidateEndProgress,
                    false,
                    false,
                    false,
                    paths.engineName);
                XNLogger.LogWarn(
                    "KataGo engine candidate skipped.",
                    ("engine", paths.engineName),
                    ("reason", invalidReason));
                continue;
            }

            if (TryStartAndSmokeTest(paths, candidateStartProgress, candidateEndProgress, candidateFailureProgress, cancellationToken)) {
                if (cancellationToken.IsCancellationRequested) {
                    StopProcess();
                    SetStartupStatus(MessageText.Get("katago_cancelled_status"), string.Empty, 1f, true, true, false, null);
                    return;
                }

                activePaths = paths;
                activeCandidateIndex = i;
                hasActivePaths = true;
                SetStartupStatus(MessageText.Get("katago_complete_status"), MessageText.Format("katago_all_boards_complete", paths.engineName), 1f, true, false, false, paths.engineName);
                XNLogger.LogInfo(
                    "KataGo engine selected.",
                    ("engine", paths.engineName),
                    ("exePath", paths.exePath),
                    ("configPath", paths.configPath),
                    ("noWriteMode", paths.noWriteMode.ToString()),
                    ("modelPath", paths.modelPath));
                return;
            }

            if (i + 1 < candidates.Length) {
                XNLogger.LogWarn(
                    "KataGo engine fallback.",
                    ("from", paths.engineName),
                    ("to", candidates[i + 1].engineName));
            }
        }

        SetStartupStatus(MessageText.Get("katago_failed_status"), MessageText.Get("katago_all_engines_unavailable"), 1f, true, true, false, null);
        XNLogger.LogError("KataGo startup failed, no engine candidate is available.");
    }
#endif

    private static void StartFirstAvailableNativeEngine(KataGoPaths[] candidates, int startCandidateIndex, CancellationToken cancellationToken)
    {
        StopProcess();
        StopNativeEngine();
        hasActivePaths = false;
        activeCandidateIndex = -1;

        if (candidates == null || candidates.Length == 0) {
            SetStartupStatus(MessageText.Get("katago_failed_status"), MessageText.Get("katago_no_engine_candidates"), 1f, true, true, false, null);
            XNLogger.LogError("KataGo native startup failed, no native engine candidate is available.");
            return;
        }

        int safeStartIndex = Math.Max(0, startCandidateIndex);
        for (int i = safeStartIndex; i < candidates.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                StopNativeEngine();
                SetStartupStatus(MessageText.Get("katago_cancelled_status"), string.Empty, 1f, true, true, false, null);
                return;
            }

            KataGoPaths paths = candidates[i];
            float candidateStartProgress = GetNativeCandidateStartProgress(paths, i, candidates.Length);
            float candidateEndProgress = GetNativeCandidateEndProgress(paths, i, candidates.Length);
            float candidateFailureProgress = GetNativeCandidateFailureProgress(paths, i, candidates.Length);
            SetStartupStatus(
                MessageText.Get("katago_warmup_status"),
                BuildCandidateDetail(paths, i > 0),
                candidateStartProgress,
                false,
                false,
                false,
                paths.engineName);

            if (!paths.IsValid(out string invalidReason)) {
                SetStartupStatus(
                    MessageText.Get("katago_warmup_status"),
                    MessageText.Format("katago_skip_engine", paths.engineName, invalidReason),
                    candidateEndProgress,
                    false,
                    false,
                    false,
                    paths.engineName);
                XNLogger.LogWarn(
                    "KataGo native engine candidate skipped.",
                    ("engine", paths.engineName),
                    ("reason", invalidReason),
                    ("candidateIndex", i.ToString()),
                    ("candidateCount", candidates.Length.ToString()),
                    ("libraryPath", paths.nativeLibraryPath),
                    ("configPath", paths.configPath),
                    ("modelPath", paths.modelPath),
                    ("noWriteMode", paths.noWriteMode.ToString()));
                continue;
            }

            if (TryStartNativeAndSmokeTest(paths, candidateStartProgress, candidateEndProgress, candidateFailureProgress, cancellationToken)) {
                if (cancellationToken.IsCancellationRequested) {
                    StopNativeEngine();
                    SetStartupStatus(MessageText.Get("katago_cancelled_status"), string.Empty, 1f, true, true, false, null);
                    return;
                }

                activePaths = paths;
                activeCandidateIndex = i;
                hasActivePaths = true;
                SetStartupStatus(MessageText.Get("katago_complete_status"), MessageText.Format("katago_all_boards_complete", paths.engineName), 1f, true, false, false, paths.engineName);
                XNLogger.LogInfo(
                    "KataGo native engine selected.",
                    ("engine", paths.engineName),
                    ("candidateIndex", i.ToString()),
                    ("candidateCount", candidates.Length.ToString()),
                    ("libraryPath", paths.nativeLibraryPath),
                    ("bridgeBackend", GetNativeBridgeBackendForLog()),
                    ("configPath", paths.configPath),
                    ("noWriteMode", paths.noWriteMode.ToString()),
                    ("modelPath", paths.modelPath));
                return;
            }

            if (i + 1 < candidates.Length) {
                XNLogger.LogWarn(
                    "KataGo native engine fallback.",
                    ("from", paths.engineName),
                    ("to", candidates[i + 1].engineName),
                    ("fromLibraryPath", paths.nativeLibraryPath),
                    ("toLibraryPath", candidates[i + 1].nativeLibraryPath));
            }
        }

        SetStartupStatus(MessageText.Get("katago_failed_status"), MessageText.Get("katago_all_engines_unavailable"), 1f, true, true, false, null);
        XNLogger.LogError("KataGo native startup failed, no native engine candidate is available.", ("candidateCount", candidates.Length.ToString()));
    }

    private static bool TryStartNativeAndSmokeTest(KataGoPaths paths, float progressStart, float progressEnd, float failureProgress, CancellationToken cancellationToken)
    {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            SetStartupStatus(
                MessageText.Get("katago_warmup_status"),
                MessageText.Format("katago_engine_started_loading", paths.engineName),
                progressStart,
                false,
                false,
                false,
                paths.engineName);

            RunNativeSmokeTest(paths, progressStart, progressEnd, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) {
            StopNativeEngine();
            SetStartupStatus(MessageText.Get("katago_cancelled_status"), string.Empty, 1f, true, true, false, paths.engineName);
            return false;
        }
        catch (Exception ex) {
            StopNativeEngine();
            SetStartupStatus(
                MessageText.Get("katago_warmup_status"),
                MessageText.Format("katago_engine_unavailable_next", paths.engineName),
                failureProgress,
                false,
                false,
                false,
                paths.engineName);
            XNLogger.LogError(
                "KataGo native engine candidate failed.",
                ("engine", paths.engineName),
                ("errType", ex.GetType().Name),
                ("err", ex.Message),
                ("libraryPath", paths.nativeLibraryPath),
                ("libraryExists", File.Exists(paths.nativeLibraryPath).ToString()),
                ("configExists", File.Exists(paths.configPath).ToString()),
                ("modelExists", File.Exists(paths.modelPath).ToString()),
                ("noWriteMode", paths.noWriteMode.ToString()),
                ("workingDirectory", paths.workingDirectory),
                ("workingDirectoryExists", Directory.Exists(paths.workingDirectory).ToString()));
            return false;
        }
    }

    private static void ValidateNativeBridgeBackend(KataGoPaths paths, string bridgeInfo)
    {
        string normalizedInfo = string.IsNullOrWhiteSpace(bridgeInfo) ? string.Empty : bridgeInfo.Trim();
        string expectedBackend = IsOpenClEngine(paths) ? "opencl" : "eigen";
        if (!string.Equals(normalizedInfo, expectedBackend, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Native bridge backend mismatch. expected: {expectedBackend}, actual: {normalizedInfo}");
        }
    }

    private static bool IsNativeEngineRunning()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return androidNativeEngine != null && androidNativeEngine.IsRunning;
#else
        return windowsNativeEngine != null && windowsNativeEngine.IsRunning;
#endif
    }

    private static string GetNativeBridgeBackend()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return androidNativeEngine?.BridgeBackend ?? string.Empty;
#else
        return windowsNativeEngine?.BridgeBackend ?? string.Empty;
#endif
    }

    private static string GetNativeBridgeBackendForLog()
    {
        string backend = GetNativeBridgeBackend();
        return string.IsNullOrEmpty(backend) ? "null" : backend;
    }

    private static JObject AnalyzeWithNativeEngine(JObject query, int timeoutMs)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidNativeKataGoEngine currentEngine = androidNativeEngine;
#else
        Win32NativeKataGoEngine currentEngine = windowsNativeEngine;
#endif
        if (currentEngine == null || !currentEngine.IsRunning) {
            throw new InvalidOperationException("KataGo native engine is not running.");
        }

        return currentEngine.Analyze(query, timeoutMs);
    }

    private static int ResolveAnalyzeTimeoutMs(KataGoAnalyzeOptions options)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return ResolveAndroidAnalyzeTimeoutMs(options);
#else
        return options.timeoutMs;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static int ResolveAndroidAnalyzeTimeoutMs(KataGoAnalyzeOptions options)
    {
        if (options.waitForegroundOnAndroid && Global.IsApplicationInBackground) {
            WaitForAndroidForeground(options.androidBackgroundGraceMs);
        }

        return AddAndroidBackgroundGrace(options.timeoutMs, options.androidBackgroundGraceMs);
    }

    private static int AddAndroidBackgroundGrace(int foregroundTimeoutMs, int backgroundGraceMs)
    {
        if (backgroundGraceMs <= 0) {
            return foregroundTimeoutMs;
        }

        if (foregroundTimeoutMs >= int.MaxValue - backgroundGraceMs) {
            return int.MaxValue;
        }

        return foregroundTimeoutMs + backgroundGraceMs;
    }

    private static void WaitForAndroidForeground(int maxWaitMs)
    {
        if (maxWaitMs <= 0) {
            return;
        }

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
        bool loggedBackgroundWait = false;
        while (Global.IsApplicationInBackground && DateTime.UtcNow < deadline) {
            if (!loggedBackgroundWait) {
                loggedBackgroundWait = true;
                XNLogger.LogWarn(
                    "KataGo analyze is waiting for Android foreground before starting timeout.",
                    ("maxWaitMs", maxWaitMs.ToString()));
            }

            Thread.Sleep(AndroidAnalyzePollMs);
        }

        if (Global.IsApplicationInBackground) {
            XNLogger.LogWarn("KataGo analyze background wait reached grace limit.");
        }
    }
#endif

    private static void StartNativeEngine(KataGoPaths paths)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        androidNativeEngine = new AndroidNativeKataGoEngine(paths.nativeLibraryPath);
        androidNativeEngine.Start(
            paths.configPath,
            paths.modelPath,
            paths.workingDirectory);
#else
        windowsNativeEngine = new Win32NativeKataGoEngine();
        windowsNativeEngine.Start(
            paths.nativeLibraryPath,
            paths.configPath,
            paths.modelPath,
            paths.workingDirectory);
#endif
    }

    private static void RunNativeSmokeTest(KataGoPaths paths, float progressStart, float progressEnd, CancellationToken cancellationToken)
    {
        for (int i = 0; i < SmokeTestBoardSizes.Length; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            int boardSize = SmokeTestBoardSizes[i];
            float boardProgressStart = Mathf.Lerp(progressStart, progressEnd, GetSmokeTestProgressWeightBefore(paths, i));
            float boardProgressEnd = Mathf.Lerp(progressStart, progressEnd, GetSmokeTestProgressWeightBefore(paths, i + 1));
            RunNativeBoardSmokeTest(paths, boardSize, i, boardProgressStart, boardProgressEnd, cancellationToken);
        }

        SetStartupStatus(
            MessageText.Get("katago_warmup_status"),
            MessageText.Format("katago_all_boards_complete", paths.engineName),
            progressEnd,
            false,
            false,
            false,
            paths.engineName);
    }

    private static void RunNativeBoardSmokeTest(KataGoPaths paths, int boardSize, int boardIndex, float progressStart, float progressEnd, CancellationToken cancellationToken)
    {
        string requestId = $"native-smoke-{boardSize}";
        JObject query = BuildSmokeTestQuery(requestId, boardSize);
        SetStartupStatus(
            MessageText.Get("katago_warmup_status"),
            BuildSmokeTestStageDetail(paths, boardSize, boardIndex),
            progressStart,
            false,
            false,
            false,
            paths.engineName);

        cancellationToken.ThrowIfCancellationRequested();
        DateTime startTime = DateTime.UtcNow;
        JObject result;
        if (boardIndex == 0) {
            result = RunStartupOperationWithProgress(
                paths,
                () =>
                {
                    StartNativeEngine(paths);
                    return AnalyzeWithNativeEngine(query, paths.smokeTestTimeoutMs);
                },
                () => BuildSmokeTestProgressDetail(paths, boardSize, boardIndex),
                startTime,
                progressStart,
                progressEnd,
                cancellationToken,
                GetEstimatedBoardWarmupMs(paths, boardIndex));
            ValidateNativeBridgeBackend(paths, GetNativeBridgeBackend());
            XNLogger.LogInfo(
                "KataGo native engine started.",
                ("engine", paths.engineName),
                ("libraryPath", paths.nativeLibraryPath),
                ("bridgeBackend", GetNativeBridgeBackend()),
                ("configPath", paths.configPath),
                ("noWriteMode", paths.noWriteMode.ToString()),
                ("modelPath", paths.modelPath));
        }
        else {
            result = RunStartupOperationWithProgress(
                paths,
                () => AnalyzeWithNativeEngine(query, paths.smokeTestTimeoutMs),
                () => BuildSmokeTestProgressDetail(paths, boardSize, boardIndex),
                startTime,
                progressStart,
                progressEnd,
                cancellationToken,
                GetEstimatedBoardWarmupMs(paths, boardIndex));
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (result["id"]?.ToString() != requestId) {
            throw new InvalidOperationException($"KataGo native smoke test returned mismatched id. expected: {requestId}, actual: {result["id"]}");
        }

        JObject rootInfo = result["rootInfo"] as JObject;
        JArray ownership = result["ownership"] as JArray;

        SetStartupStatus(
            MessageText.Get("katago_warmup_status"),
            MessageText.Format("katago_board_complete", paths.engineName, boardSize),
            progressEnd,
            false,
            false,
            false,
            paths.engineName);
        XNLogger.LogInfo(
            "KataGo native smoke test success.",
            ("engine", paths.engineName),
            ("boardSize", boardSize.ToString()),
            ("id", (string)result["id"]),
            ("scoreLead", rootInfo?["scoreLead"]?.ToString() ?? "null"),
            ("visits", rootInfo?["visits"]?.ToString() ?? "null"),
            ("ownershipLength", ownership?.Count.ToString() ?? "null"));
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static bool TryStartAndSmokeTest(KataGoPaths paths, float progressStart, float progressEnd, float failureProgress, CancellationToken cancellationToken)
    {
        try {
            StartProcess(paths);
            SetStartupStatus(
                MessageText.Get("katago_warmup_status"),
                MessageText.Format("katago_engine_started_loading", paths.engineName),
                progressStart,
                false,
                false,
                false,
                paths.engineName);
            XNLogger.LogInfo(
                "KataGo process started.",
                ("engine", paths.engineName),
                ("exePath", paths.exePath),
                ("configPath", paths.configPath),
                ("noWriteMode", paths.noWriteMode.ToString()),
                ("modelPath", paths.modelPath));

            RunSmokeTest(paths, progressStart, progressEnd, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) {
            StopProcess();
            SetStartupStatus(MessageText.Get("katago_cancelled_status"), string.Empty, 1f, true, true, false, paths.engineName);
            return false;
        }
        catch (Exception ex) {
            SetStartupStatus(
                MessageText.Get("katago_warmup_status"),
                MessageText.Format("katago_engine_unavailable_next", paths.engineName),
                failureProgress,
                false,
                false,
                false,
                paths.engineName);
            XNLogger.LogError(
                "KataGo engine candidate failed.",
                ("engine", paths.engineName),
                ("errType", ex.GetType().Name),
                ("err", ex.Message),
                ("exePath", paths.exePath),
                ("exeExists", File.Exists(paths.exePath).ToString()),
                ("configExists", File.Exists(paths.configPath).ToString()),
                ("modelExists", File.Exists(paths.modelPath).ToString()),
                ("workingDirectory", paths.workingDirectory),
                ("workingDirectoryExists", Directory.Exists(paths.workingDirectory).ToString()));
            StopProcess();
            return false;
        }
    }

    private static void StartProcess(KataGoPaths paths)
    {
        StopProcess();

        process = new Win32KataGoProcess();
        process.Start(
            paths.exePath,
            $"analysis -config \"{paths.configPath}\" -model \"{paths.modelPath}\"",
            paths.workingDirectory);
    }

    private static void RunSmokeTest(KataGoPaths paths, float progressStart, float progressEnd, CancellationToken cancellationToken)
    {
        for (int i = 0; i < SmokeTestBoardSizes.Length; i++) {
            int boardSize = SmokeTestBoardSizes[i];
            float boardProgressStart = Mathf.Lerp(progressStart, progressEnd, GetSmokeTestProgressWeightBefore(paths, i));
            float boardProgressEnd = Mathf.Lerp(progressStart, progressEnd, GetSmokeTestProgressWeightBefore(paths, i + 1));
            RunBoardSmokeTest(paths, boardSize, i, boardProgressStart, boardProgressEnd, cancellationToken);
        }

        SetStartupStatus(
            MessageText.Get("katago_warmup_status"),
            MessageText.Format("katago_all_boards_complete", paths.engineName),
            progressEnd,
            false,
            false,
            false,
            paths.engineName);
    }

    private static void RunBoardSmokeTest(KataGoPaths paths, int boardSize, int boardIndex, float progressStart, float progressEnd, CancellationToken cancellationToken)
    {
        string requestId = $"editor-smoke-{boardSize}";
        JObject query = BuildSmokeTestQuery(requestId, boardSize);
        SetStartupStatus(
            MessageText.Get("katago_warmup_status"),
            BuildSmokeTestStageDetail(paths, boardSize, boardIndex),
            progressStart,
            false,
            false,
            false,
            paths.engineName);
        process.WriteLine(query.ToString(Newtonsoft.Json.Formatting.None));

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(paths.smokeTestTimeoutMs);
        DateTime startTime = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();

            if (process == null || !process.IsRunning) {
                throw new InvalidOperationException("KataGo exited before returning smoke test result.");
            }

            UpdateSmokeTestProgress(paths, boardSize, boardIndex, startTime, progressStart, progressEnd);
            if (!TryReadOutputLineBefore(
                    deadline,
                    cancellationToken,
                    $"KataGo {boardSize}x{boardSize} smoke test",
                    paths.smokeTestTimeoutMs,
                    SmokeTestProgressPollMs,
                    out string line)) {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            if (!line.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
                continue;
            }

            JObject result = JObject.Parse(line);
            if (result["id"]?.ToString() != requestId) {
                continue;
            }

            if ((bool?)result["isDuringSearch"] == true) {
                continue;
            }

            JObject rootInfo = result["rootInfo"] as JObject;
            JArray ownership = result["ownership"] as JArray;

            SetStartupStatus(
                MessageText.Get("katago_warmup_status"),
                MessageText.Format("katago_board_complete", paths.engineName, boardSize),
                progressEnd,
                false,
                false,
                false,
                paths.engineName);
            XNLogger.LogInfo(
                "KataGo editor smoke test success.",
                ("engine", paths.engineName),
                ("boardSize", boardSize.ToString()),
                ("id", (string)result["id"]),
                ("scoreLead", rootInfo?["scoreLead"]?.ToString() ?? "null"),
                ("visits", rootInfo?["visits"]?.ToString() ?? "null"),
                ("ownershipLength", ownership?.Count.ToString() ?? "null"));
            return;
        }

        throw new TimeoutException($"KataGo {boardSize}x{boardSize} smoke test timed out after {paths.smokeTestTimeoutMs}ms.");
    }
#endif

    private static JObject BuildSmokeTestQuery(string requestId, int boardSize)
    {
        return new JObject
        {
            ["id"] = requestId,
            ["initialStones"] = new JArray(),
            ["moves"] = new JArray(),
            ["rules"] = "chinese",
            ["komi"] = 7.5f,
            ["boardXSize"] = boardSize,
            ["boardYSize"] = boardSize,
            ["maxVisits"] = SmokeTestMaxVisits,
            ["includeOwnership"] = true,
            ["includePolicy"] = false,
        };
    }

    private static void UpdateSmokeTestProgress(KataGoPaths paths, int boardSize, int boardIndex, DateTime startTime, float progressStart, float progressEnd)
    {
        UpdateStartupPhaseProgress(
            paths,
            BuildSmokeTestProgressDetail(paths, boardSize, boardIndex),
            startTime,
            progressStart,
            progressEnd,
            GetEstimatedBoardWarmupMs(paths, boardIndex));
    }

    private static T RunStartupOperationWithProgress<T>(
        KataGoPaths paths,
        Func<T> operation,
        Func<string> detailTextFactory,
        DateTime startTime,
        float progressStart,
        float progressEnd,
        CancellationToken cancellationToken,
        int estimatedMs)
    {
        Task<T> task = Task.Run(operation);
        while (!task.Wait(SmokeTestProgressPollMs)) {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateStartupPhaseProgress(
                paths,
                detailTextFactory(),
                startTime,
                progressStart,
                progressEnd,
                estimatedMs);
        }

        return task.GetAwaiter().GetResult();
    }

    private static void UpdateStartupPhaseProgress(KataGoPaths paths, string detailText, DateTime startTime, float progressStart, float progressEnd, int estimatedMs)
    {
        double elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
        float phaseProgress = Mathf.Clamp01((float)(elapsedMs / Math.Max(1, estimatedMs)));
        float progress = Mathf.Lerp(progressStart, progressEnd, phaseProgress);
        SetStartupStatus(
            MessageText.Get("katago_warmup_status"),
            detailText,
            progress,
            false,
            false,
            false,
            paths.engineName);
    }

    private static string BuildSmokeTestStageDetail(KataGoPaths paths, int boardSize, int boardIndex)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths)) {
            if (IsOpenClEngine(paths)) {
                return MessageText.Get("katago_android_opencl_first_init");
            }

            return MessageText.Format("katago_android_native_verify_board", boardSize);
        }
#endif

        if (IsOpenClInitializationStage(paths, boardIndex)) {
            return MessageText.Get("katago_opencl_first_init");
        }

        if (paths.noWriteMode && IsCpuEngine(paths)) {
            return MessageText.Format("katago_cpu_verify_no_write", boardSize);
        }

        return MessageText.Format("katago_engine_verify_board", paths.engineName, boardSize);
    }

    private static string BuildSmokeTestProgressDetail(KataGoPaths paths, int boardSize, int boardIndex)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths)) {
            if (IsOpenClEngine(paths)) {
                return MessageText.Get("katago_android_opencl_progress_init");
            }

            return MessageText.Format("katago_android_native_verify_board", boardSize);
        }
#endif

        if (IsOpenClInitializationStage(paths, boardIndex)) {
            return MessageText.Get("katago_opencl_progress_init");
        }

        if (paths.noWriteMode && IsCpuEngine(paths)) {
            return MessageText.Format("katago_cpu_verify_no_write", boardSize);
        }

        return MessageText.Format("katago_engine_verify_board", paths.engineName, boardSize);
    }

    private static bool IsOpenClInitializationStage(KataGoPaths paths, int boardIndex)
    {
        return IsOpenClEngine(paths) && boardIndex == 0;
    }

    private static bool IsOpenClEngine(KataGoPaths paths)
    {
        return paths.engineName != null
            && paths.engineName.IndexOf("opencl", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsCpuEngine(KataGoPaths paths)
    {
        return !IsOpenClEngine(paths);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static bool IsAndroidNativeEngine(KataGoPaths paths)
    {
        return paths.isNative
            && (string.Equals(paths.engineName, AndroidNativeOpenClEngineName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(paths.engineName, AndroidNativeCpuEngineName, StringComparison.OrdinalIgnoreCase));
    }
#endif

    private static float GetSmokeTestProgressWeightBefore(KataGoPaths paths, int boardIndex)
    {
        float weight = 0f;
        float[] weights = GetSmokeTestProgressWeights(paths);
        int safeCount = Math.Min(boardIndex, weights.Length);
        for (int i = 0; i < safeCount; i++) {
            weight += weights[i];
        }

        return Mathf.Clamp01(weight);
    }

    private static float GetSmokeTestProgressWeight(KataGoPaths paths, int boardIndex)
    {
        float[] weights = GetSmokeTestProgressWeights(paths);
        return boardIndex >= 0 && boardIndex < weights.Length
            ? weights[boardIndex]
            : 1f / Mathf.Max(1f, SmokeTestBoardSizes.Length);
    }

    private static float[] GetSmokeTestProgressWeights(KataGoPaths paths)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths) && IsOpenClEngine(paths)) {
            return AndroidOpenClSmokeTestProgressWeights;
        }
#endif

        return SmokeTestBoardProgressWeights;
    }

    private static int GetEstimatedBoardWarmupMs(KataGoPaths paths, int boardIndex)
    {
        int totalEstimatedMs = GetEstimatedWarmupMs(paths);
        float weight = GetSmokeTestProgressWeight(paths, boardIndex);
        return Math.Max(1, Mathf.RoundToInt(totalEstimatedMs * weight));
    }

    private static int GetEstimatedWarmupMs(KataGoPaths paths)
    {
        if (!IsOpenClEngine(paths)) {
            return CpuWarmupEstimatedMs;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths)) {
            return AndroidOpenClWarmupEstimatedMs;
        }
#endif

        return OpenClWarmupEstimatedMs;
    }

    private static float GetNativeCandidateStartProgress(KataGoPaths paths, int candidateIndex, int candidateCount)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths)) {
            if (candidateIndex > 0) {
                return 0.98f;
            }

            if (IsOpenClEngine(paths)) {
                return 0f;
            }
        }
#endif

        return GetCandidateStartProgress(candidateIndex, candidateCount);
    }

    private static float GetNativeCandidateEndProgress(KataGoPaths paths, int candidateIndex, int candidateCount)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths)) {
            return 1f;
        }
#endif

        return GetCandidateEndProgress(candidateIndex, candidateCount);
    }

    private static float GetNativeCandidateFailureProgress(KataGoPaths paths, int candidateIndex, int candidateCount)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths) && candidateIndex == 0 && candidateCount > 1) {
            return 0.98f;
        }
#endif

        return GetCandidateFailureProgress(candidateIndex, candidateCount);
    }

    private static float GetCandidateStartProgress(int candidateIndex, int candidateCount)
    {
        if (candidateCount <= 1) {
            return 0.08f;
        }

        return candidateIndex == 0 ? 0.08f : 0.82f;
    }

    private static float GetCandidateEndProgress(int candidateIndex, int candidateCount)
    {
        if (candidateCount <= 1 || candidateIndex == 0) {
            return 0.94f;
        }

        return 0.94f;
    }

    private static float GetCandidateFailureProgress(int candidateIndex, int candidateCount)
    {
        if (candidateCount <= 1 || candidateIndex > 0) {
            return GetCandidateEndProgress(candidateIndex, candidateCount);
        }

        return 0.82f;
    }

    private static string BuildCandidateDetail(KataGoPaths paths, bool isFallback)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsAndroidNativeEngine(paths)) {
            if (IsOpenClEngine(paths)) {
                return MessageText.Get("katago_android_opencl_warmup");
            }

            if (isFallback) {
                return MessageText.Get("katago_android_opencl_unavailable_native_warmup");
            }

            return MessageText.Get("katago_android_native_warmup");
        }
#endif

        if (paths.noWriteMode && IsCpuEngine(paths)) {
            return MessageText.Get("katago_no_write_cpu_warmup");
        }

        if (IsOpenClEngine(paths)) {
            return MessageText.Get("katago_gpu_warmup");
        }

        if (isFallback) {
            return MessageText.Get("katago_gpu_unavailable_cpu_warmup");
        }

        return MessageText.Format("katago_engine_warmup", paths.engineName);
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static JObject Analyze(JObject query, int timeoutMs)
    {
        try {
            string requestId = query["id"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(requestId)) {
                XNLogger.LogError("KataGo analyze failed, query id is empty.");
                return null;
            }

            Win32KataGoProcess currentProcess = process;
            if (currentProcess == null || !currentProcess.IsRunning) {
                XNLogger.LogError("KataGo analyze failed, process exited.");
                return null;
            }

            currentProcess.WriteLine(query.ToString(Newtonsoft.Json.Formatting.None));

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline) {
                if (process == null || !currentProcess.IsRunning) {
                    XNLogger.LogError("KataGo analyze failed, process exited.");
                    return null;
                }

                string line = ReadOutputLineBefore(deadline, CancellationToken.None, $"KataGo analyze request {requestId}", timeoutMs);
                if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
                    continue;
                }

                JObject result = JObject.Parse(line);
                if (result["id"]?.ToString() != requestId) {
                    continue;
                }

                LogKataGoResultDiagnostics(result, requestId);

                if (!HasAnalysisPayload(result)) {
                    if (result["error"] != null) {
                        return result;
                    }

                    continue;
                }

                if ((bool?)result["isDuringSearch"] == true) {
                    continue;
                }

                XNLogger.LogInfo(
                    "KataGo analyze success.",
                    ("engine", hasActivePaths ? activePaths.engineName : "unknown"),
                    ("id", requestId),
                    ("hasOwnership", (result["ownership"] != null).ToString()),
                    ("moveInfoCount", ((result["moveInfos"] as JArray)?.Count ?? 0).ToString()));
                return result;
            }

            XNLogger.LogError("KataGo analyze failed, request timed out.", ("id", requestId));
            return null;
        }
        catch (Exception ex) {
            XNLogger.LogError("KataGo analyze failed.", ("err", ex.Message));
            return null;
        }
    }
#endif

    private static JObject AnalyzeNative(JObject query, int timeoutMs)
    {
        try {
            string requestId = query["id"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(requestId)) {
                XNLogger.LogError("KataGo native analyze failed, query id is empty.");
                return null;
            }

            JObject result = AnalyzeWithNativeEngine(query, timeoutMs);
            LogKataGoResultDiagnostics(result, requestId);

            if (!HasAnalysisPayload(result) && result["error"] == null) {
                XNLogger.LogError("KataGo native analyze failed, result has no analysis payload.", ("id", requestId));
                return null;
            }

            XNLogger.LogInfo(
                "KataGo native analyze success.",
                ("engine", hasActivePaths ? activePaths.engineName : "unknown"),
                ("id", requestId),
                ("hasOwnership", (result["ownership"] != null).ToString()),
                ("moveInfoCount", ((result["moveInfos"] as JArray)?.Count ?? 0).ToString()));
            return result;
        }
        catch (Exception ex) {
            XNLogger.LogError("KataGo native analyze failed.", ("err", ex.Message));
            StopNativeEngine();
            return null;
        }
    }

    private static bool HasAnalysisPayload(JObject result)
    {
        return result["moveInfos"] != null
            || result["ownership"] != null
            || result["rootInfo"] != null
            || result["policy"] != null;
    }

    private static void LogKataGoResultDiagnostics(JObject result, string requestId)
    {
        string warning = result["warning"]?.ToString();
        if (!string.IsNullOrEmpty(warning)) {
            XNLogger.LogWarn(
                "KataGo analyze warning.",
                ("id", requestId),
                ("warning", warning),
                ("resultKeys", BuildResultKeysLog(result)));
        }

        string error = result["error"]?.ToString();
        if (!string.IsNullOrEmpty(error)) {
            XNLogger.LogError(
                "KataGo analyze returned error.",
                ("id", requestId),
                ("error", error),
                ("resultKeys", BuildResultKeysLog(result)));
        }
    }

    private static string BuildResultKeysLog(JObject result)
    {
        if (result == null) {
            return string.Empty;
        }

        return string.Join(",", result.Properties().Select(property => property.Name));
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static void StopProcess()
    {
        Win32KataGoProcess currentProcess = process;
        process = null;

        if (currentProcess == null) {
            return;
        }

        try {
            currentProcess.Stop();
        }
        catch (Exception ex) {
            Debug.LogWarning($"Stop KataGo process failed: {ex.Message}");
        }
        finally {
            currentProcess.Dispose();
            XNLogger.LogInfo("KataGo process stopped.");
        }
    }
#else
    private static void StopProcess()
    {
    }
#endif

    private static void StopNativeEngine()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidNativeKataGoEngine currentEngine = androidNativeEngine;
        androidNativeEngine = null;
#else
        Win32NativeKataGoEngine currentEngine = windowsNativeEngine;
        windowsNativeEngine = null;
#endif

        if (currentEngine == null) {
            return;
        }

        try {
            currentEngine.Stop();
        }
        catch (Exception ex) {
            Debug.LogWarning($"Stop KataGo native engine failed: {ex.Message}");
        }
        finally {
            currentEngine.Dispose();
            XNLogger.LogInfo("KataGo native engine stopped.");
        }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static string ReadOutputLineBefore(DateTime deadline, CancellationToken cancellationToken, string operationName, int timeoutMs)
    {
        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) {
            throw new TimeoutException($"{operationName} timed out after {timeoutMs}ms.");
        }

        Win32KataGoProcess currentProcess = process;
        if (currentProcess == null) {
            throw new InvalidOperationException($"{operationName} failed because KataGo process is not running.");
        }

        return currentProcess.ReadOutputLineBefore(deadline, cancellationToken, operationName, timeoutMs);
    }

    private static bool TryReadOutputLineBefore(
        DateTime deadline,
        CancellationToken cancellationToken,
        string operationName,
        int timeoutMs,
        int maxWaitMs,
        out string line)
    {
        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) {
            throw new TimeoutException($"{operationName} timed out after {timeoutMs}ms.");
        }

        Win32KataGoProcess currentProcess = process;
        if (currentProcess == null) {
            throw new InvalidOperationException($"{operationName} failed because KataGo process is not running.");
        }

        return currentProcess.TryReadOutputLineBefore(deadline, cancellationToken, operationName, timeoutMs, maxWaitMs, out line);
    }
#endif

    private struct KataGoPaths
    {
        public string exePath;
        public string nativeLibraryPath;
        public string configPath;
        public string modelPath;
        public string workingDirectory;
        public string engineName;
        public int smokeTestTimeoutMs;
        public bool noWriteMode;
        public bool isNative;
        public bool skipNativeLibraryFileCheck;

        public bool IsValid(out string reason)
        {
            if (isNative) {
                if (!skipNativeLibraryFileCheck && !File.Exists(nativeLibraryPath)) {
                    reason = $"katago_bridge.dll not found: {nativeLibraryPath}";
                    return false;
                }
            }
            else {
                if (!File.Exists(exePath)) {
                    reason = $"katago.exe not found: {exePath}";
                    return false;
                }
            }

            if (!File.Exists(configPath)) {
                reason = $"analysis config not found: {configPath}";
                return false;
            }

            if (!File.Exists(modelPath)) {
                reason = $"model not found: {modelPath}";
                return false;
            }

            reason = null;
            return true;
        }
    }
#else
    private static void StopProcessTask()
    {
        try {
            cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException) {
        }

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        startupTask = null;
    }
#endif

    public struct KataGoStartupStatus
    {
        public static readonly KataGoStartupStatus NotStarted = new KataGoStartupStatus(
            MessageText.Get("katago_wait_start_status"),
            string.Empty,
            0f,
            false,
            false,
            false,
            null);

        public readonly string statusText;
        public readonly string detailText;
        public readonly float progress;
        public readonly bool isFinished;
        public readonly bool isFailed;
        public readonly bool isSkipped;
        public readonly string engineName;

        public KataGoStartupStatus(
            string statusText,
            string detailText,
            float progress,
            bool isFinished,
            bool isFailed,
            bool isSkipped,
            string engineName)
        {
            this.statusText = statusText ?? string.Empty;
            this.detailText = detailText ?? string.Empty;
            this.progress = progress;
            this.isFinished = isFinished;
            this.isFailed = isFailed;
            this.isSkipped = isSkipped;
            this.engineName = engineName ?? string.Empty;
        }
    }

    private struct PlatformConfig
    {
        public bool isSupported;
        public string unsupportedReason;
        public string gameRoot;
        public string kataGoRoot;
        public string engineRoot;
        public bool canWriteGameRoot;
        public string writeFailureReason;
        public string modelFileName;
        public bool windowsPreferOpenCl;
        public bool windowsAllowCpuFallback;
        public string windowsNativeOpenClEngineName;
        public string windowsNativeCpuEngineName;
        public string androidAbi;
        public bool androidPreferOpenCl;
        public bool androidAllowCpuFallback;
        public string androidNativeOpenClLibraryName;
        public string androidNativeCpuLibraryName;
    }
}

public struct KataGoAnalyzeOptions
{
    public int timeoutMs;
    public int retryCount;
    public int retryDelayMs;
    public bool retryUntilCanceled;
    public bool restartEngineBeforeRetry;
    public bool waitForegroundOnAndroid;
    public int androidBackgroundGraceMs;
    public string requestKind;
}
