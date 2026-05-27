using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public static class UICanvasResolutionProfile
{
    private static readonly Vector2 PcReferenceResolution = new Vector2(1600f, 900f);
    private static readonly Vector2 MobilePortraitReferenceResolution = new Vector2(720f, 1280f);
#if UNITY_EDITOR
    private const string EditorConfigRelativePath = "UserSettings/UIRuntimeCanvasResolution.json";
    private static bool hasWarnedInvalidEditorConfig;
#endif

    public static Vector2 RuntimeReferenceResolution
    {
        get
        {
#if UNITY_EDITOR
            return EditorReferenceResolution;
#elif UNITY_ANDROID || UNITY_IOS
            return MobilePortraitReferenceResolution;
#else
            return PcReferenceResolution;
#endif
        }
    }

    public static void ApplyRuntimeResolution(CanvasScaler canvasScaler)
    {
        if (canvasScaler == null) {
            return;
        }

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = RuntimeReferenceResolution;
    }

#if UNITY_EDITOR
    public static Vector2 EditorDefaultReferenceResolution => PcReferenceResolution;
    public static Vector2 EditorMobilePreviewReferenceResolution => MobilePortraitReferenceResolution;

    public static string EditorConfigPath
    {
        get
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, EditorConfigRelativePath);
        }
    }

    public static Vector2 EditorReferenceResolution
    {
        get
        {
            return TryReadEditorReferenceResolution(out Vector2 referenceResolution)
                ? referenceResolution
                : EditorDefaultReferenceResolution;
        }
    }

    public static void WriteEditorReferenceResolution(Vector2 referenceResolution, string profileName)
    {
        if (!IsValidReferenceResolution(referenceResolution)) {
            throw new ArgumentException($"Invalid UI reference resolution: {referenceResolution}", nameof(referenceResolution));
        }

        string configPath = EditorConfigPath;
        string directoryPath = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }

        var config = new EditorReferenceResolutionConfig {
            profileName = string.IsNullOrEmpty(profileName) ? "Custom" : profileName,
            width = referenceResolution.x,
            height = referenceResolution.y,
        };
        File.WriteAllText(configPath, JsonUtility.ToJson(config, true));
    }

    private static bool TryReadEditorReferenceResolution(out Vector2 referenceResolution)
    {
        referenceResolution = Vector2.zero;
        string configPath = EditorConfigPath;
        if (!File.Exists(configPath)) {
            return false;
        }

        try {
            string json = File.ReadAllText(configPath);
            EditorReferenceResolutionConfig config = JsonUtility.FromJson<EditorReferenceResolutionConfig>(json);
            var configuredResolution = new Vector2(config.width, config.height);
            if (!IsValidReferenceResolution(configuredResolution)) {
                WarnInvalidEditorConfig(configPath);
                return false;
            }

            referenceResolution = configuredResolution;
            return true;
        } catch (Exception e) {
            WarnInvalidEditorConfig(configPath, e.Message);
            return false;
        }
    }

    private static bool IsValidReferenceResolution(Vector2 referenceResolution)
    {
        return referenceResolution.x > 0f
            && referenceResolution.y > 0f
            && !float.IsNaN(referenceResolution.x)
            && !float.IsNaN(referenceResolution.y)
            && !float.IsInfinity(referenceResolution.x)
            && !float.IsInfinity(referenceResolution.y);
    }

    private static void WarnInvalidEditorConfig(string configPath, string detail = null)
    {
        if (hasWarnedInvalidEditorConfig) {
            return;
        }

        hasWarnedInvalidEditorConfig = true;
        string suffix = string.IsNullOrEmpty(detail) ? string.Empty : $"\n{detail}";
        Debug.LogWarning($"Invalid editor UI runtime resolution config, fallback to PC resolution: {configPath}{suffix}");
    }

    [Serializable]
    private class EditorReferenceResolutionConfig
    {
        public string profileName;
        public float width;
        public float height;
    }
#endif
}
