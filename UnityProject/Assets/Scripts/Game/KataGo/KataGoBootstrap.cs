using System;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Diagnostics;
#endif
using System.IO;
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
    private const int OwnershipAnalyzeTimeoutMs = 45000;

    private static Process process;
    private static readonly SemaphoreSlim analysisSemaphore = new SemaphoreSlim(1, 1);
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
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (query == null) {
            XNLogger.LogError("KataGo ownership analyze failed, query is null.");
            return null;
        }

        if (startupTask != null && !startupTask.IsCompleted) {
            await startupTask;
        }

        if (process == null || process.HasExited) {
            XNLogger.LogError("KataGo ownership analyze failed, process is not running.");
            return null;
        }

        return await Task.Run(() => AnalyzeOwnership(query, OwnershipAnalyzeTimeoutMs));
#else
        await Task.CompletedTask;
        XNLogger.LogWarn("KataGo ownership analyze skipped.", ("reason", "Local process analyze is not compiled for this platform."));
        return null;
#endif
    }

    private static PlatformConfig ResolvePlatformConfig()
    {
#if UNITY_EDITOR_WIN
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string kataGoRoot = Path.Combine(projectRoot, "ExternalTools", "KataGo");
        return new PlatformConfig
        {
            isSupported = true,
            kataGoRoot = kataGoRoot,
            engineRoot = Path.Combine(kataGoRoot, "engines", "win-x64", EngineName),
            engineName = EngineName,
        };
#elif UNITY_STANDALONE_WIN
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
            XNLogger.LogError("KataGo startup failed.", ("err", ex.Message));
            StopProcess();
        }
    }

    private static void StartProcess(KataGoPaths paths)
    {
        StopProcess();

        var startInfo = new ProcessStartInfo
        {
            FileName = paths.exePath,
            Arguments = $"analysis -config \"{paths.configPath}\" -model \"{paths.modelPath}\"",
            WorkingDirectory = paths.workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.ErrorDataReceived += OnKataGoErrorDataReceived;
        process.Exited += OnKataGoExited;

        if (!process.Start()) {
            process = null;
            throw new InvalidOperationException("Process.Start returned false.");
        }

        process.BeginErrorReadLine();
    }

    private static void RunSmokeTest(CancellationToken cancellationToken)
    {
        string query = "{\"id\":\"editor-smoke-001\",\"initialStones\":[],\"moves\":[[\"B\",\"Q16\"],[\"W\",\"D4\"],[\"B\",\"Q4\"],[\"W\",\"D16\"]],\"rules\":\"chinese\",\"komi\":7.5,\"boardXSize\":19,\"boardYSize\":19,\"maxVisits\":4,\"includeOwnership\":true,\"includePolicy\":false}";
        process.StandardInput.WriteLine(query);
        process.StandardInput.Flush();

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(SmokeTestTimeoutMs);
        while (DateTime.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();

            if (process == null || process.HasExited) {
                throw new InvalidOperationException("KataGo exited before returning smoke test result.");
            }

            string line = ReadOutputLineBefore(deadline, cancellationToken);
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

    private static JArray AnalyzeOwnership(JObject query, int timeoutMs)
    {
        analysisSemaphore.Wait();
        try {
            string requestId = query["id"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(requestId)) {
                XNLogger.LogError("KataGo ownership analyze failed, query id is empty.");
                return null;
            }

            process.StandardInput.WriteLine(query.ToString(Newtonsoft.Json.Formatting.None));
            process.StandardInput.Flush();

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline) {
                if (process == null || process.HasExited) {
                    XNLogger.LogError("KataGo ownership analyze failed, process exited.");
                    return null;
                }

                string line = ReadOutputLineBefore(deadline, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
                    continue;
                }

                JObject result = JObject.Parse(line);
                if (result["id"]?.ToString() != requestId) {
                    continue;
                }

                if ((bool?)result["isDuringSearch"] == true) {
                    continue;
                }

                JArray ownership = result["ownership"] as JArray;
                if (ownership == null) {
                    XNLogger.LogError("KataGo ownership analyze failed, ownership missing.", ("id", requestId));
                    return null;
                }

                XNLogger.LogInfo(
                    "KataGo ownership analyze success.",
                    ("id", requestId),
                    ("ownershipLength", ownership.Count.ToString()));
                return ownership;
            }

            XNLogger.LogError("KataGo ownership analyze failed, request timed out.", ("id", requestId));
            return null;
        }
        catch (Exception ex) {
            XNLogger.LogError("KataGo ownership analyze failed.", ("err", ex.Message));
            return null;
        }
        finally {
            analysisSemaphore.Release();
        }
    }

    private static void StopProcess()
    {
        Process currentProcess = process;
        process = null;

        if (currentProcess == null) {
            return;
        }

        try {
            currentProcess.ErrorDataReceived -= OnKataGoErrorDataReceived;
            currentProcess.Exited -= OnKataGoExited;

            if (!currentProcess.HasExited) {
                try {
                    currentProcess.StandardInput.Close();
                }
                catch (Exception) {
                }

                if (!currentProcess.WaitForExit(2000)) {
                    currentProcess.Kill();
                }
            }
        }
        catch (Exception ex) {
            Debug.LogWarning($"Stop KataGo editor process failed: {ex.Message}");
        }
        finally {
            currentProcess.Dispose();
            XNLogger.LogInfo("KataGo process stopped.");
        }
    }

    private static string ReadOutputLineBefore(DateTime deadline, CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) {
            throw new TimeoutException($"KataGo smoke test timed out after {SmokeTestTimeoutMs}ms.");
        }

        Task<string> readTask = process.StandardOutput.ReadLineAsync();
        while (!readTask.Wait(100)) {
            cancellationToken.ThrowIfCancellationRequested();

            if (process == null || process.HasExited) {
                throw new InvalidOperationException("KataGo exited before returning smoke test result.");
            }

            if (DateTime.UtcNow >= deadline) {
                StopProcess();
                throw new TimeoutException($"KataGo smoke test timed out after {SmokeTestTimeoutMs}ms.");
            }
        }

        return readTask.Result;
    }

    private static void OnKataGoErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) {
            return;
        }

        Debug.Log($"[KataGo] {e.Data}");
    }

    private static void OnKataGoExited(object sender, EventArgs e)
    {
        Process exitedProcess = sender as Process;
        XNLogger.LogWarn("KataGo process exited.", ("exitCode", exitedProcess?.ExitCode.ToString() ?? "unknown"));
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
