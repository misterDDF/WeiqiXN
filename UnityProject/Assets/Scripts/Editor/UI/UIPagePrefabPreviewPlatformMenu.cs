#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class UIPagePrefabPreviewPlatformMenu
{
    private const string PagePrefabFolder = "Assets/UI/Prefab/Page/";
    private const double RestoreFocusDelaySeconds = 0.5d;
    private const string PcPreviewProfileName = "PC";
    private const string MobilePreviewProfileName = "Mobile";
    private static readonly Vector2 PcPreviewResolution = UICanvasResolutionProfile.EditorDefaultReferenceResolution;
    private static readonly Vector2 MobilePreviewResolution = UICanvasResolutionProfile.EditorMobilePreviewReferenceResolution;

    [MenuItem("Assets/切换预览平台/PC端", false, 2000)]
    private static void ApplyPcPreview()
    {
        ApplyPreviewResolution(PcPreviewResolution, PcPreviewProfileName);
    }

    [MenuItem("Assets/切换预览平台/PC端", true)]
    private static bool ValidatePcPreview()
    {
        return IsSelectedPagePrefabWithCanvasScaler();
    }

    [MenuItem("Assets/切换预览平台/移动端", false, 2001)]
    private static void ApplyMobilePreview()
    {
        ApplyPreviewResolution(MobilePreviewResolution, MobilePreviewProfileName);
    }

    [MenuItem("Assets/切换预览平台/移动端", true)]
    private static bool ValidateMobilePreview()
    {
        return IsSelectedPagePrefabWithCanvasScaler();
    }

    [MenuItem("GameObject/切换预览平台/PC端", false, 2000)]
    private static void ApplyHierarchyPcPreview()
    {
        ApplyPreviewResolution(GetCurrentPagePrefabStagePath(), PcPreviewResolution, PcPreviewProfileName);
    }

    [MenuItem("GameObject/切换预览平台/PC端", true)]
    private static bool ValidateHierarchyPcPreview()
    {
        return IsCurrentPagePrefabStageWithCanvasScaler();
    }

    [MenuItem("GameObject/切换预览平台/移动端", false, 2001)]
    private static void ApplyHierarchyMobilePreview()
    {
        ApplyPreviewResolution(GetCurrentPagePrefabStagePath(), MobilePreviewResolution, MobilePreviewProfileName);
    }

    [MenuItem("GameObject/切换预览平台/移动端", true)]
    private static bool ValidateHierarchyMobilePreview()
    {
        return IsCurrentPagePrefabStageWithCanvasScaler();
    }

    private static void ApplyPreviewResolution(Vector2 referenceResolution, string profileName)
    {
        string assetPath = GetSelectedPagePrefabPath();
        ApplyPreviewResolution(assetPath, referenceResolution, profileName);
    }

    private static void ApplyPreviewResolution(string assetPath, Vector2 referenceResolution, string profileName)
    {
        if (string.IsNullOrEmpty(assetPath)) {
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        CanvasScaler canvasScaler = prefab != null ? prefab.GetComponent<CanvasScaler>() : null;
        if (canvasScaler == null) {
            Debug.LogWarning($"CanvasScaler not found: {assetPath}");
            return;
        }

        SerializedObject serializedCanvasScaler = new SerializedObject(canvasScaler);
        serializedCanvasScaler.Update();
        serializedCanvasScaler.FindProperty("m_UiScaleMode").intValue = (int)CanvasScaler.ScaleMode.ScaleWithScreenSize;
        serializedCanvasScaler.FindProperty("m_ReferenceResolution").vector2Value = referenceResolution;
        serializedCanvasScaler.ApplyModifiedProperties();

        EditorUtility.SetDirty(canvasScaler);
        ApplyOpenPrefabStagePreview(assetPath, referenceResolution);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        UICanvasResolutionProfile.WriteEditorReferenceResolution(referenceResolution, profileName);
        ApplyGameViewResolution(referenceResolution);
    }

    private static void ApplyGameViewResolution(Vector2 referenceResolution)
    {
        int width = Mathf.RoundToInt(referenceResolution.x);
        int height = Mathf.RoundToInt(referenceResolution.y);
        string sizeText = $"{width}x{height}";
        try {
            int sizeIndex = FindOrAddFixedGameViewSize(width, height, sizeText);
            SetGameViewSize(sizeIndex);
        } catch (Exception e) {
            Debug.LogWarning($"Switch GameView resolution failed: {sizeText}\n{e.Message}");
        }
    }

    private static void ApplyOpenPrefabStagePreview(string assetPath, Vector2 referenceResolution)
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage == null || prefabStage.assetPath.Replace('\\', '/') != assetPath) {
            return;
        }

        CanvasScaler stageCanvasScaler = prefabStage.prefabContentsRoot.GetComponent<CanvasScaler>();
        if (stageCanvasScaler == null) {
            return;
        }

        stageCanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        stageCanvasScaler.referenceResolution = referenceResolution;
        EditorUtility.SetDirty(stageCanvasScaler);
        EditorSceneManager.MarkSceneDirty(prefabStage.scene);
    }

    private static int FindOrAddFixedGameViewSize(int width, int height, string label)
    {
        object gameViewSizes = GetGameViewSizesInstance();
        object group = GetCurrentGameViewSizeGroup(gameViewSizes);
        MethodInfo getBuiltinCount = group.GetType().GetMethod("GetBuiltinCount");
        MethodInfo getCustomCount = group.GetType().GetMethod("GetCustomCount");
        MethodInfo getGameViewSize = group.GetType().GetMethod("GetGameViewSize");
        int builtinCount = (int)getBuiltinCount.Invoke(group, null);
        int customCount = (int)getCustomCount.Invoke(group, null);
        int totalCount = builtinCount + customCount;

        for (int i = 0; i < totalCount; i++) {
            object size = getGameViewSize.Invoke(group, new object[] { i });
            int sizeWidth = (int)size.GetType().GetProperty("width").GetValue(size);
            int sizeHeight = (int)size.GetType().GetProperty("height").GetValue(size);
            if (sizeWidth == width && sizeHeight == height) {
                return i;
            }
        }

        Type gameViewSizeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize");
        Type gameViewSizeTypeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
        object fixedResolutionType = Enum.Parse(gameViewSizeTypeType, "FixedResolution");
        object newSize = Activator.CreateInstance(gameViewSizeType, fixedResolutionType, width, height, label);
        MethodInfo addCustomSize = group.GetType().GetMethod("AddCustomSize");
        addCustomSize.Invoke(group, new[] { newSize });
        return totalCount;
    }

    private static object GetGameViewSizesInstance()
    {
        Type sizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        PropertyInfo instanceProperty = singletonType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return instanceProperty.GetValue(null);
    }

    private static object GetCurrentGameViewSizeGroup(object gameViewSizes)
    {
        Type gameViewSizeGroupType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeGroupType");
        object standaloneType = Enum.Parse(gameViewSizeGroupType, "Standalone");
        MethodInfo getGroup = gameViewSizes.GetType().GetMethod("GetGroup");
        return getGroup.Invoke(gameViewSizes, new[] { standaloneType });
    }

    private static void SetGameViewSize(int sizeIndex)
    {
        Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
        if (gameViews.Length == 0) {
            return;
        }

        EditorWindow gameView = gameViews[0] as EditorWindow;
        if (gameView == null) {
            return;
        }

        PropertyInfo selectedSizeIndex = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        selectedSizeIndex.SetValue(gameView, sizeIndex);
        RefreshPreviewViews(gameView);
    }

    private static void RefreshPreviewViews(EditorWindow gameView)
    {
        EditorWindow restoreWindow = ResolveRestoreWindow(gameView);
        Canvas.ForceUpdateCanvases();
        EditorApplication.QueuePlayerLoopUpdate();
        InternalEditorUtility.RepaintAllViews();
        gameView.Repaint();
        FocusPreviewWindow(gameView, restoreWindow);
        EditorApplication.delayCall += () =>
        {
            Canvas.ForceUpdateCanvases();
            EditorApplication.QueuePlayerLoopUpdate();
            InternalEditorUtility.RepaintAllViews();
            if (gameView != null) {
                gameView.Repaint();
            }
            RestoreFocusedWindowAfterDelay(restoreWindow, RestoreFocusDelaySeconds);
        };
    }

    private static EditorWindow ResolveRestoreWindow(EditorWindow gameView)
    {
        return SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView : FindOpenWindow(typeof(SceneView));
    }

    private static void FocusPreviewWindow(EditorWindow gameView, EditorWindow restoreWindow)
    {
        if (gameView == null || gameView == restoreWindow) {
            return;
        }

        gameView.Focus();
    }

    private static void RestoreFocusedWindow(EditorWindow focusedWindow)
    {
        if (focusedWindow == null) {
            return;
        }

        focusedWindow.Show();
        focusedWindow.Focus();
    }

    private static void RestoreFocusedWindowAfterDelay(EditorWindow focusedWindow, double delaySeconds)
    {
        double restoreTime = EditorApplication.timeSinceStartup + delaySeconds;
        void RestoreWhenReady()
        {
            if (EditorApplication.timeSinceStartup < restoreTime) {
                return;
            }

            EditorApplication.update -= RestoreWhenReady;
            RestoreFocusedWindow(focusedWindow);
        }

        EditorApplication.update += RestoreWhenReady;
    }

    private static EditorWindow FindOpenWindow(Type windowType)
    {
        if (windowType == null) {
            return null;
        }

        UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(windowType);
        return windows.Length > 0 ? windows[0] as EditorWindow : null;
    }

    private static bool IsSelectedPagePrefabWithCanvasScaler()
    {
        string assetPath = GetSelectedPagePrefabPath();
        return IsPagePrefabWithCanvasScaler(assetPath);
    }

    private static bool IsCurrentPagePrefabStageWithCanvasScaler()
    {
        string assetPath = GetCurrentPagePrefabStagePath();
        return IsPagePrefabWithCanvasScaler(assetPath);
    }

    private static bool IsPagePrefabWithCanvasScaler(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) {
            return false;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        return prefab != null && prefab.GetComponent<CanvasScaler>() != null;
    }

    private static string GetCurrentPagePrefabStagePath()
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage == null) {
            return string.Empty;
        }

        string assetPath = prefabStage.assetPath.Replace('\\', '/');
        if (!IsPagePrefabPath(assetPath)) {
            return string.Empty;
        }

        return assetPath;
    }

    private static string GetSelectedPagePrefabPath()
    {
        UnityEngine.Object selectedObject = Selection.activeObject;
        if (selectedObject == null) {
            return string.Empty;
        }

        string assetPath = AssetDatabase.GetAssetPath(selectedObject);
        if (string.IsNullOrEmpty(assetPath)) {
            return string.Empty;
        }

        assetPath = assetPath.Replace('\\', '/');
        if (!IsPagePrefabPath(assetPath)) {
            return string.Empty;
        }

        return assetPath;
    }

    private static bool IsPagePrefabPath(string assetPath)
    {
        return assetPath.StartsWith(PagePrefabFolder, StringComparison.Ordinal) && Path.GetExtension(assetPath) == ".prefab";
    }
}
#endif
