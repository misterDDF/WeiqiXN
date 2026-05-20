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
    private const string EngineName = "eigenavx2";
    private const string ModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
    private const int SmokeTestTimeoutMs = 45000;
    private const int AnalyzeTimeoutMs = 45000;
    private const bool HumanSlProfileEnabled = false;

    private static Win32KataGoProcess process;
    private static readonly SemaphoreSlim analysisSemaphore = new SemaphoreSlim(1, 1);
    private static KataGoPaths activePaths;
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
        KataGoPaths paths = ResolvePaths(platformConfig);
        if (!paths.IsValid(out string invalidReason)) {
            XNLogger.LogError("KataGo startup skipped.", ("reason", invalidReason));
            return;
        }

        StartProcessTask(paths);
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
            kataGoRoot = kataGoRoot,
            engineRoot = Path.Combine(kataGoRoot, "engines", "win-x64", EngineName),
            engineName = EngineName,
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
    private static KataGoPaths ResolvePaths(PlatformConfig platformConfig)
    {
        return new KataGoPaths
        {
            exePath = Path.Combine(platformConfig.engineRoot, "katago.exe"),
            configPath = Path.Combine(platformConfig.engineRoot, "analysis_example.cfg"),
            modelPath = Path.Combine(platformConfig.kataGoRoot, "models", ModelFileName),
            workingDirectory = platformConfig.engineRoot,
            engineName = platformConfig.engineName,
        };
    }

    private static void StartProcessTask(KataGoPaths paths)
    {
        activePaths = paths;
        hasActivePaths = true;
        isStarted = true;
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        startupTask = Task.Run(() => StartAndSmokeTest(paths, cancellationTokenSource.Token));
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

        if (!isStarted || !hasActivePaths) {
            return false;
        }

        XNLogger.LogWarn("KataGo process is not running, restarting before analyze.");
        StartProcessTask(activePaths);
        if (startupTask != null) {
            await startupTask;
        }

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

    private static void StartAndSmokeTest(KataGoPaths paths, CancellationToken cancellationToken)
    {
        try {
            StartProcess(paths);
            XNLogger.LogInfo(
                "KataGo process started.",
                ("engine", paths.engineName),
                ("exePath", paths.exePath),
                ("modelPath", paths.modelPath));

            RunSmokeTest(cancellationToken);
        }
        catch (Exception ex) {
            XNLogger.LogError(
                "KataGo startup failed.",
                ("errType", ex.GetType().Name),
                ("err", ex.Message),
                ("exePath", paths.exePath),
                ("exeExists", File.Exists(paths.exePath).ToString()),
                ("configExists", File.Exists(paths.configPath).ToString()),
                ("modelExists", File.Exists(paths.modelPath).ToString()),
                ("workingDirectory", paths.workingDirectory),
                ("workingDirectoryExists", Directory.Exists(paths.workingDirectory).ToString()));
            StopProcess();
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

    private static void RunSmokeTest(CancellationToken cancellationToken)
    {
        string query = "{\"id\":\"editor-smoke-001\",\"initialStones\":[],\"moves\":[[\"B\",\"Q16\"],[\"W\",\"D4\"],[\"B\",\"Q4\"],[\"W\",\"D16\"]],\"rules\":\"chinese\",\"komi\":7.5,\"boardXSize\":19,\"boardYSize\":19,\"maxVisits\":4,\"includeOwnership\":true,\"includePolicy\":false}";
        process.WriteLine(query);

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(SmokeTestTimeoutMs);
        while (DateTime.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();

            if (process == null || !process.IsRunning) {
                throw new InvalidOperationException("KataGo exited before returning smoke test result.");
            }

            string line = ReadOutputLineBefore(deadline, cancellationToken, "KataGo smoke test", SmokeTestTimeoutMs);
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
                ("id", (string)result["id"]),
                ("scoreLead", rootInfo?["scoreLead"]?.ToString() ?? "null"),
                ("visits", rootInfo?["visits"]?.ToString() ?? "null"),
                ("ownershipLength", ownership?.Count.ToString() ?? "null"));
            return;
        }

        throw new TimeoutException($"KataGo smoke test timed out after {SmokeTestTimeoutMs}ms.");
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
        public string engineName;
    }
}
