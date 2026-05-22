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

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private const string OpenClEngineName = "opencl";
    private const string CpuEngineName = "eigenavx2";
    private const string ModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
    private const string AnalysisConfigFileName = "analysis_example.cfg";
    private const string NoWriteAnalysisConfigFileName = "analysis_nowrite.cfg";
    private const int OpenClSmokeTestTimeoutMs = 300000;
    private const int CpuSmokeTestTimeoutMs = 45000;
    private const int SmokeTestProgressPollMs = 250;
    private const int OpenClWarmupEstimatedMs = 75000;
    private const int CpuWarmupEstimatedMs = 12000;
    private const int SmokeTestMaxVisits = 1;
    private const int AnalyzeTimeoutMs = 45000;
    private const bool HumanSlProfileEnabled = false;
    private static readonly int[] SmokeTestBoardSizes = { 9, 13, 19 };
    private static readonly float[] SmokeTestBoardProgressWeights = { 0.80f, 0.10f, 0.10f };

    private static Win32KataGoProcess process;
    private static readonly SemaphoreSlim analysisSemaphore = new SemaphoreSlim(1, 1);
    private static KataGoPaths[] engineCandidates;
    private static KataGoPaths activePaths;
    private static int activeCandidateIndex = -1;
    private static bool hasActivePaths;
    private static bool isStarted;
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

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!platformConfig.canWriteGameRoot) {
            ShowGameRootWriteWarning(platformConfig);
            XNLogger.LogWarn(
                "KataGo game root write check failed, OpenCL will be skipped.",
                ("gameRoot", platformConfig.gameRoot),
                ("reason", platformConfig.writeFailureReason));
        }

        KataGoPaths[] candidates = ResolveCandidatePaths(platformConfig);
        if (candidates.Length == 0) {
            SetStartupStatus(MessageText.Get("katago_failed_status"), MessageText.Get("katago_no_engine_candidates"), 1f, true, true, false, null);
            XNLogger.LogError("KataGo startup skipped.", ("reason", "No KataGo engine candidates resolved."));
            return;
        }

        StartProcessTask(candidates);
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
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (query == null) {
            XNLogger.LogError("KataGo analyze failed, query is null.");
            return null;
        }

        await analysisSemaphore.WaitAsync();
        try {
            if (!await EnsureProcessReadyAsync()) {
                XNLogger.LogError("KataGo analyze failed, process is not running.");
                return null;
            }

            return await Task.Run(() => Analyze(query, AnalyzeTimeoutMs));
        }
        finally {
            analysisSemaphore.Release();
        }
#else
        await Task.CompletedTask;
        XNLogger.LogWarn("KataGo analyze skipped.", ("reason", "Local process analyze is not compiled for this platform."));
        return null;
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
        return new PlatformConfig
        {
            isSupported = true,
            unsupportedReason = null,
            gameRoot = runtimeInfo.gameRoot,
            kataGoRoot = kataGoRoot,
            engineRoot = Path.Combine(kataGoRoot, "engines", "win-x64"),
            canWriteGameRoot = runtimeInfo.canWriteGameRoot,
            writeFailureReason = runtimeInfo.writeFailureReason,
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
            modelPath = Path.Combine(platformConfig.kataGoRoot, "models", ModelFileName),
            workingDirectory = engineRoot,
            engineName = engineName,
            smokeTestTimeoutMs = smokeTestTimeoutMs,
            noWriteMode = noWriteMode,
        };
    }

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

    private static void StopProcessTask()
    {
        try {
            cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException) {
        }

        StopProcess();

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        startupTask = null;
        engineCandidates = null;
        activeCandidateIndex = -1;
        hasActivePaths = false;
        isStarted = false;
        SetStartupStatus(MessageText.Get("katago_not_started_status"), string.Empty, 0f, true, false, true, null);
    }

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

    private static bool IsProcessRunning()
    {
        try {
            return process != null && process.IsRunning;
        }
        catch (Exception) {
            return false;
        }
    }

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
            float candidateStartProgress = GetCandidateStartProgress(i, candidates.Length);
            float candidateEndProgress = GetCandidateEndProgress(i, candidates.Length);
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

            if (TryStartAndSmokeTest(paths, candidateStartProgress, candidateEndProgress, cancellationToken)) {
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

    private static bool TryStartAndSmokeTest(KataGoPaths paths, float progressStart, float progressEnd, CancellationToken cancellationToken)
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
                progressEnd,
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
            float boardProgressStart = Mathf.Lerp(progressStart, progressEnd, GetSmokeTestProgressWeightBefore(i));
            float boardProgressEnd = Mathf.Lerp(progressStart, progressEnd, GetSmokeTestProgressWeightBefore(i + 1));
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
        double elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
        int estimatedBoardMs = GetEstimatedBoardWarmupMs(paths, boardIndex);
        float smokeProgress = Mathf.Clamp01((float)(elapsedMs / estimatedBoardMs));
        float progress = Mathf.Lerp(progressStart, progressEnd, smokeProgress);
        SetStartupStatus(
            MessageText.Get("katago_warmup_status"),
            BuildSmokeTestProgressDetail(paths, boardSize, boardIndex),
            progress,
            false,
            false,
            false,
            paths.engineName);
    }

    private static string BuildSmokeTestStageDetail(KataGoPaths paths, int boardSize, int boardIndex)
    {
        if (IsOpenClInitializationStage(paths, boardIndex)) {
            return MessageText.Get("katago_opencl_first_init");
        }

        if (paths.noWriteMode && paths.engineName == CpuEngineName) {
            return MessageText.Format("katago_cpu_verify_no_write", boardSize);
        }

        return MessageText.Format("katago_engine_verify_board", paths.engineName, boardSize);
    }

    private static string BuildSmokeTestProgressDetail(KataGoPaths paths, int boardSize, int boardIndex)
    {
        if (IsOpenClInitializationStage(paths, boardIndex)) {
            return MessageText.Get("katago_opencl_progress_init");
        }

        if (paths.noWriteMode && paths.engineName == CpuEngineName) {
            return MessageText.Format("katago_cpu_verify_no_write", boardSize);
        }

        return MessageText.Format("katago_engine_verify_board", paths.engineName, boardSize);
    }

    private static bool IsOpenClInitializationStage(KataGoPaths paths, int boardIndex)
    {
        return paths.engineName == OpenClEngineName && boardIndex == 0;
    }

    private static float GetSmokeTestProgressWeightBefore(int boardIndex)
    {
        float weight = 0f;
        int safeCount = Math.Min(boardIndex, SmokeTestBoardProgressWeights.Length);
        for (int i = 0; i < safeCount; i++) {
            weight += SmokeTestBoardProgressWeights[i];
        }

        return Mathf.Clamp01(weight);
    }

    private static int GetEstimatedBoardWarmupMs(KataGoPaths paths, int boardIndex)
    {
        int totalEstimatedMs = paths.engineName == OpenClEngineName
            ? OpenClWarmupEstimatedMs
            : CpuWarmupEstimatedMs;
        float weight = boardIndex >= 0 && boardIndex < SmokeTestBoardProgressWeights.Length
            ? SmokeTestBoardProgressWeights[boardIndex]
            : 1f / Mathf.Max(1f, SmokeTestBoardSizes.Length);
        return Math.Max(1, Mathf.RoundToInt(totalEstimatedMs * weight));
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
        if (candidateCount <= 1) {
            return 0.94f;
        }

        return candidateIndex == 0 ? 0.82f : 0.94f;
    }

    private static string BuildCandidateDetail(KataGoPaths paths, bool isFallback)
    {
        if (paths.noWriteMode && paths.engineName == CpuEngineName) {
            return MessageText.Get("katago_no_write_cpu_warmup");
        }

        if (paths.engineName == OpenClEngineName) {
            return MessageText.Get("katago_gpu_warmup");
        }

        if (isFallback) {
            return MessageText.Get("katago_gpu_unavailable_cpu_warmup");
        }

        return MessageText.Format("katago_engine_warmup", paths.engineName);
    }

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

    private struct KataGoPaths
    {
        public string exePath;
        public string configPath;
        public string modelPath;
        public string workingDirectory;
        public string engineName;
        public int smokeTestTimeoutMs;
        public bool noWriteMode;

        public bool IsValid(out string reason)
        {
            if (!File.Exists(exePath)) {
                reason = $"katago.exe not found: {exePath}";
                return false;
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
    }
}
