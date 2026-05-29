using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public enum KataGoBackendMode
{
    Disabled,
    Exe,
    Native,
}

public sealed class GameConfig
{
    public const string FileName = "game-config.json";

    private static GameConfig cachedConfig;

    public readonly KataGoConfig kataGo;

    private GameConfig(KataGoConfig kataGo)
    {
        this.kataGo = kataGo ?? KataGoConfig.Default;
    }

    public static GameConfig Current
    {
        get
        {
            if (cachedConfig == null) {
                cachedConfig = Load();
            }

            return cachedConfig;
        }
    }

    public static void Reload()
    {
        cachedConfig = Load();
    }

    public static string ResolveConfigPath()
    {
        return Path.Combine(ResolveGameRoot(), FileName);
    }

    private static GameConfig Load()
    {
        string configPath = ResolveConfigPath();
        if (!File.Exists(configPath)) {
#if !UNITY_ANDROID || UNITY_EDITOR
            Debug.LogWarning($"Game config not found, using defaults. path: {configPath}");
#endif
            return new GameConfig(KataGoConfig.Default);
        }

        try {
            JObject root = JObject.Parse(File.ReadAllText(configPath));
            return new GameConfig(KataGoConfig.Parse(root["katago"] as JObject));
        }
        catch (Exception ex) {
            Debug.LogWarning($"Game config parse failed, using defaults. path: {configPath}, error: {ex.Message}");
            return new GameConfig(KataGoConfig.Default);
        }
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
#elif UNITY_ANDROID
        return Application.persistentDataPath;
#else
        return Directory.GetCurrentDirectory();
#endif
    }

    public sealed class KataGoConfig
    {
        private const string DefaultWindowsNativeOpenClEngineName = "native-opencl";
        private const string DefaultWindowsNativeCpuEngineName = "native-eigen";
        private const string DefaultModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";
        private const string DefaultAndroidNativeOpenClLibraryName = "katago_bridge_opencl";
        private const string DefaultAndroidNativeCpuLibraryName = "katago_bridge_eigen";
        private const int DefaultMaxConcurrentNativeRequests = 1;
        private const int MaxConcurrentNativeRequestsLimit = 4;

        public static readonly KataGoConfig Default = new KataGoConfig(
            "native",
            "native",
            "native",
            "native",
            DefaultModelFileName,
            true,
            true,
            DefaultWindowsNativeOpenClEngineName,
            DefaultWindowsNativeCpuEngineName,
            "arm64-v8a",
            true,
            true,
            DefaultAndroidNativeOpenClLibraryName,
            DefaultAndroidNativeCpuLibraryName,
            DefaultMaxConcurrentNativeRequests);

        public readonly string windowsEditorBackend;
        public readonly string windowsPlayerBackend;
        public readonly string androidPlayerBackend;
        public readonly string iosPlayerBackend;
        public readonly string modelFileName;
        public readonly bool windowsPreferOpenCl;
        public readonly bool windowsAllowCpuFallback;
        public readonly string windowsNativeOpenClEngineName;
        public readonly string windowsNativeCpuEngineName;
        public readonly string androidAbi;
        public readonly bool androidPreferOpenCl;
        public readonly bool androidAllowCpuFallback;
        public readonly string androidNativeOpenClLibraryName;
        public readonly string androidNativeCpuLibraryName;
        public readonly int maxConcurrentNativeRequests;

        private KataGoConfig(
            string windowsEditorBackend,
            string windowsPlayerBackend,
            string androidPlayerBackend,
            string iosPlayerBackend,
            string modelFileName,
            bool windowsPreferOpenCl,
            bool windowsAllowCpuFallback,
            string windowsNativeOpenClEngineName,
            string windowsNativeCpuEngineName,
            string androidAbi,
            bool androidPreferOpenCl,
            bool androidAllowCpuFallback,
            string androidNativeOpenClLibraryName,
            string androidNativeCpuLibraryName,
            int maxConcurrentNativeRequests)
        {
            this.windowsEditorBackend = NormalizeBackend(windowsEditorBackend, "native");
            this.windowsPlayerBackend = NormalizeBackend(windowsPlayerBackend, "native");
            this.androidPlayerBackend = NormalizeBackend(androidPlayerBackend, "native");
            this.iosPlayerBackend = NormalizeBackend(iosPlayerBackend, "native");
            this.modelFileName = string.IsNullOrWhiteSpace(modelFileName) ? DefaultModelFileName : modelFileName;
            this.windowsPreferOpenCl = windowsPreferOpenCl;
            this.windowsAllowCpuFallback = windowsAllowCpuFallback;
            this.windowsNativeOpenClEngineName = string.IsNullOrWhiteSpace(windowsNativeOpenClEngineName) ? DefaultWindowsNativeOpenClEngineName : windowsNativeOpenClEngineName;
            this.windowsNativeCpuEngineName = string.IsNullOrWhiteSpace(windowsNativeCpuEngineName) ? DefaultWindowsNativeCpuEngineName : windowsNativeCpuEngineName;
            this.androidAbi = string.IsNullOrWhiteSpace(androidAbi) ? "arm64-v8a" : androidAbi;
            this.androidPreferOpenCl = androidPreferOpenCl;
            this.androidAllowCpuFallback = androidAllowCpuFallback;
            this.androidNativeOpenClLibraryName = string.IsNullOrWhiteSpace(androidNativeOpenClLibraryName) ? DefaultAndroidNativeOpenClLibraryName : androidNativeOpenClLibraryName;
            this.androidNativeCpuLibraryName = string.IsNullOrWhiteSpace(androidNativeCpuLibraryName) ? DefaultAndroidNativeCpuLibraryName : androidNativeCpuLibraryName;
            this.maxConcurrentNativeRequests = ClampNativeRequestConcurrency(maxConcurrentNativeRequests);
        }

        public static KataGoConfig Parse(JObject katagoRoot)
        {
            if (katagoRoot == null) {
                return Default;
            }

            JObject backend = katagoRoot["backend"] as JObject;
            JObject model = katagoRoot["model"] as JObject;
            JObject analysis = katagoRoot["analysis"] as JObject;
            JObject windows = katagoRoot["windows"] as JObject;
            JObject android = katagoRoot["android"] as JObject;
            string legacyNativeEngineName = windows?.Value<string>("nativeEngineName");

            return new KataGoConfig(
                backend?.Value<string>("windowsEditor") ?? Default.windowsEditorBackend,
                backend?.Value<string>("windowsPlayer") ?? Default.windowsPlayerBackend,
                backend?.Value<string>("androidPlayer") ?? Default.androidPlayerBackend,
                backend?.Value<string>("iosPlayer") ?? Default.iosPlayerBackend,
                model?.Value<string>("fileName") ?? Default.modelFileName,
                windows?.Value<bool?>("preferOpenCl") ?? Default.windowsPreferOpenCl,
                windows?.Value<bool?>("allowCpuFallback") ?? Default.windowsAllowCpuFallback,
                windows?.Value<string>("nativeOpenClEngineName") ?? Default.windowsNativeOpenClEngineName,
                windows?.Value<string>("nativeCpuEngineName") ?? legacyNativeEngineName ?? Default.windowsNativeCpuEngineName,
                android?.Value<string>("abi") ?? Default.androidAbi,
                android?.Value<bool?>("preferOpenCl") ?? Default.androidPreferOpenCl,
                android?.Value<bool?>("allowCpuFallback") ?? Default.androidAllowCpuFallback,
                android?.Value<string>("nativeOpenClLibraryName") ?? Default.androidNativeOpenClLibraryName,
                android?.Value<string>("nativeCpuLibraryName") ?? android?.Value<string>("nativeLibraryName") ?? Default.androidNativeCpuLibraryName,
                analysis?.Value<int?>("maxConcurrentNativeRequests") ?? Default.maxConcurrentNativeRequests);
        }

        private static int ClampNativeRequestConcurrency(int value)
        {
            return Math.Max(1, Math.Min(MaxConcurrentNativeRequestsLimit, value));
        }

        public KataGoBackendMode ResolveCurrentBackend()
        {
#if UNITY_EDITOR_WIN
            return ParseBackendMode(windowsEditorBackend);
#elif UNITY_STANDALONE_WIN
            return ParseBackendMode(windowsPlayerBackend);
#elif UNITY_ANDROID
            return ParseBackendMode(androidPlayerBackend);
#elif UNITY_IOS
            return ParseBackendMode(iosPlayerBackend);
#else
            return KataGoBackendMode.Disabled;
#endif
        }

        public KataGoBackendMode ResolveWindowsPlayerBackend()
        {
            return ParseBackendMode(windowsPlayerBackend);
        }

        public KataGoBackendMode ResolveAndroidPlayerBackend()
        {
            return ParseBackendMode(androidPlayerBackend);
        }

        private static string NormalizeBackend(string value, string fallback)
        {
            string normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            KataGoBackendMode mode = ParseBackendMode(normalizedValue);
            if (mode == KataGoBackendMode.Disabled && !string.Equals(normalizedValue, "disabled", StringComparison.OrdinalIgnoreCase)) {
                return fallback;
            }

            return normalizedValue.ToLowerInvariant();
        }

        private static KataGoBackendMode ParseBackendMode(string value)
        {
            string normalizedValue = value?.Trim();
            if (string.Equals(normalizedValue, "exe", StringComparison.OrdinalIgnoreCase)) {
                return KataGoBackendMode.Exe;
            }
            if (string.Equals(normalizedValue, "native", StringComparison.OrdinalIgnoreCase)) {
                return KataGoBackendMode.Native;
            }

            return KataGoBackendMode.Disabled;
        }
    }
}
