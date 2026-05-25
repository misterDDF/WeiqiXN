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
            Debug.LogWarning($"Game config not found, using defaults. path: {configPath}");
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
#else
        return Directory.GetCurrentDirectory();
#endif
    }

    public sealed class KataGoConfig
    {
        private const string DefaultWindowsNativeOpenClEngineName = "native-opencl";
        private const string DefaultWindowsNativeCpuEngineName = "native-eigen";
        private const string DefaultModelFileName = "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz";

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
            "katago_bridge",
            "eigen");

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
        public readonly string androidNativeLibraryName;
        public readonly string androidNeuralNetBackend;

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
            string androidNativeLibraryName,
            string androidNeuralNetBackend)
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
            this.androidNativeLibraryName = string.IsNullOrWhiteSpace(androidNativeLibraryName) ? "katago_bridge" : androidNativeLibraryName;
            this.androidNeuralNetBackend = string.IsNullOrWhiteSpace(androidNeuralNetBackend) ? "eigen" : androidNeuralNetBackend;
        }

        public static KataGoConfig Parse(JObject katagoRoot)
        {
            if (katagoRoot == null) {
                return Default;
            }

            JObject backend = katagoRoot["backend"] as JObject;
            JObject model = katagoRoot["model"] as JObject;
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
                android?.Value<string>("nativeLibraryName") ?? Default.androidNativeLibraryName,
                android?.Value<string>("neuralNetBackend") ?? Default.androidNeuralNetBackend);
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
