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
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private const string OpenClEngineName = "opencl";
    private const string CpuEngineName = "eigenavx2";
    private const string ModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
    private const int OpenClSmokeTestTimeoutMs = 300000;
    private const int CpuSmokeTestTimeoutMs = 45000;
    private const int AnalyzeTimeoutMs = 45000;
    private const bool HumanSlProfileEnabled = false;

    private static Win32KataGoProcess process;
    private static readonly SemaphoreSlim analysisSemaphore = new SemaphoreSlim(1, 1);
    private static KataGoPaths[] engineCandidates;
    private static KataGoPaths activePaths;
    private static int activeCandidateIndex = -1;
    private static bool hasActivePaths;
    private static bool isStarted;
#endif
    private static CancellationTokenSource cancellationTokenSource;
    private static Task startupTask;

    public static void Start()
    {
        if (startupTask != null && !startupTask.IsCompleted) {
            return;
        }

        PlatformConfig platformConfig = ResolvePlatformConfig();
        if (!platformConfig.isSupported) {
            XNLogger.LogWarn("KataGo startup skipped.", ("reason", platformConfig.unsupportedReason));
            return;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        KataGoPaths[] candidates = ResolveCandidatePaths(platformConfig);
        if (candidates.Length == 0) {
            XNLogger.LogError("KataGo startup skipped.", ("reason", "No KataGo engine candidates resolved."));
            return;
        }

        StartProcessTask(candidates);
#else
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

    private static PlatformConfig ResolvePlatformConfig()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string kataGoRoot = Path.Combine(Application.streamingAssetsPath, "KataGo");
        return new PlatformConfig
        {
            isSupported = true,
            unsupportedReason = null,
            kataGoRoot = kataGoRoot,
            engineRoot = Path.Combine(kataGoRoot, "engines", "win-x64"),
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
        return new[]
        {
            ResolvePaths(platformConfig, OpenClEngineName, OpenClSmokeTestTimeoutMs),
            ResolvePaths(platformConfig, CpuEngineName, CpuSmokeTestTimeoutMs),
        };
    }

    private static KataGoPaths ResolvePaths(PlatformConfig platformConfig, string engineName, int smokeTestTimeoutMs)
    {
        string engineRoot = Path.Combine(platformConfig.engineRoot, engineName);
        return new KataGoPaths
        {
            exePath = Path.Combine(engineRoot, "katago.exe"),
            configPath = Path.Combine(engineRoot, "analysis_example.cfg"),
            modelPath = Path.Combine(platformConfig.kataGoRoot, "models", ModelFileName),
            workingDirectory = engineRoot,
            engineName = engineName,
            smokeTestTimeoutMs = smokeTestTimeoutMs,
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

    private static void StartFirstAvailableEngine(KataGoPaths[] candidates, int startCandidateIndex, CancellationToken cancellationToken)
    {
        StopProcess();
        hasActivePaths = false;
        activeCandidateIndex = -1;

        if (candidates == null || candidates.Length == 0) {
            XNLogger.LogError("KataGo startup failed, no engine candidate is available.");
            return;
        }

        int safeStartIndex = Math.Max(0, startCandidateIndex);
        for (int i = safeStartIndex; i < candidates.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                StopProcess();
                return;
            }

            KataGoPaths paths = candidates[i];
            if (!paths.IsValid(out string invalidReason)) {
                XNLogger.LogWarn(
                    "KataGo engine candidate skipped.",
                    ("engine", paths.engineName),
                    ("reason", invalidReason));
                continue;
            }

            if (TryStartAndSmokeTest(paths, cancellationToken)) {
                if (cancellationToken.IsCancellationRequested) {
                    StopProcess();
                    return;
                }

                activePaths = paths;
                activeCandidateIndex = i;
                hasActivePaths = true;
                XNLogger.LogInfo(
                    "KataGo engine selected.",
                    ("engine", paths.engineName),
                    ("exePath", paths.exePath),
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

        XNLogger.LogError("KataGo startup failed, no engine candidate is available.");
    }

    private static bool TryStartAndSmokeTest(KataGoPaths paths, CancellationToken cancellationToken)
    {
        try {
            StartProcess(paths);
            XNLogger.LogInfo(
                "KataGo process started.",
                ("engine", paths.engineName),
                ("exePath", paths.exePath),
                ("modelPath", paths.modelPath));

            RunSmokeTest(paths, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) {
            StopProcess();
            return false;
        }
        catch (Exception ex) {
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

    private static void RunSmokeTest(KataGoPaths paths, CancellationToken cancellationToken)
    {
        string query = "{\"id\":\"editor-smoke-001\",\"initialStones\":[],\"moves\":[[\"B\",\"Q16\"],[\"W\",\"D4\"],[\"B\",\"Q4\"],[\"W\",\"D16\"]],\"rules\":\"chinese\",\"komi\":7.5,\"boardXSize\":19,\"boardYSize\":19,\"maxVisits\":4,\"includeOwnership\":true,\"includePolicy\":false}";
        process.WriteLine(query);

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(paths.smokeTestTimeoutMs);
        while (DateTime.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();

            if (process == null || !process.IsRunning) {
                throw new InvalidOperationException("KataGo exited before returning smoke test result.");
            }

            string line = ReadOutputLineBefore(deadline, cancellationToken, "KataGo smoke test", paths.smokeTestTimeoutMs);
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            if (!line.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
                continue;
            }

            JObject result = JObject.Parse(line);
            if ((bool?)result["isDuringSearch"] == true) {
                continue;
            }

            JObject rootInfo = result["rootInfo"] as JObject;
            JArray ownership = result["ownership"] as JArray;

            XNLogger.LogInfo(
                "KataGo editor smoke test success.",
                ("engine", paths.engineName),
                ("id", (string)result["id"]),
                ("scoreLead", rootInfo?["scoreLead"]?.ToString() ?? "null"),
                ("visits", rootInfo?["visits"]?.ToString() ?? "null"),
                ("ownershipLength", ownership?.Count.ToString() ?? "null"));
            return;
        }

        throw new TimeoutException($"KataGo smoke test timed out after {paths.smokeTestTimeoutMs}ms.");
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

    private struct KataGoPaths
    {
        public string exePath;
        public string configPath;
        public string modelPath;
        public string workingDirectory;
        public string engineName;
        public int smokeTestTimeoutMs;

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

    private struct PlatformConfig
    {
        public bool isSupported;
        public string unsupportedReason;
        public string kataGoRoot;
        public string engineRoot;
    }
}
