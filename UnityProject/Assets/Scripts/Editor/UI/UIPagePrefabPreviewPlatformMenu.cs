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
            Type gameViewType = GetGameViewType();
            EditorWindow gameView = GetGameViewWindow(gameViewType);
            if (gameView == null) {
                return;
            }

            int sizeIndex = FindOrAddFixedGameViewSize(gameView, width, height, sizeText);
            SetGameViewSize(gameView, sizeIndex);
            Debug.Log($"Switch GameView resolution: {sizeText}, sizeIndex={sizeIndex}, activeBuildTarget={EditorUserBuildSettings.activeBuildTarget}");
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

    private static int FindOrAddFixedGameViewSize(EditorWindow gameView, int width, int height, string label)
    {
        object gameViewSizes = GetGameViewSizesInstance();
        object group = GetCurrentGameViewSizeGroup(gameViewSizes, gameView);
        MethodInfo getBuiltinCount = group.GetType().GetMethod("GetBuiltinCount");
        MethodInfo getCustomCount = group.GetType().GetMethod("GetCustomCount");
        MethodInfo getGameViewSize = group.GetType().GetMethod("GetGameViewSize");
        int builtinCount = (int)getBuiltinCount.Invoke(group, null);
        int customCount = (int)getCustomCount.Invoke(group, null);
        int totalCount = builtinCount + customCount;

        for (int i = 0; i < totalCount; i++) {
            object size = getGameViewSize.Invoke(group, new object[] { i });
            if (IsMatchingGameViewSize(size, width, height, label)) {
                return i;
            }
        }

        Type gameViewSizeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize");
        Type gameViewSizeTypeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
        if (gameViewSizeType == null || gameViewSizeTypeType == null) {
            throw new InvalidOperationException("UnityEditor.GameViewSize reflection type not found.");
        }

        object fixedResolutionType = Enum.Parse(gameViewSizeTypeType, "FixedResolution");
        object newSize = Activator.CreateInstance(gameViewSizeType, fixedResolutionType, width, height, label);
        MethodInfo addCustomSize = group.GetType().GetMethod("AddCustomSize");
        if (addCustomSize == null) {
            throw new InvalidOperationException("GameViewSizeGroup.AddCustomSize reflection method not found.");
        }

        addCustomSize.Invoke(group, new[] { newSize });
        return totalCount;
    }

    private static object GetGameViewSizesInstance()
    {
        Type sizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        if (sizesType == null) {
            throw new InvalidOperationException("UnityEditor.GameViewSizes reflection type not found.");
        }

        Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        PropertyInfo instanceProperty = singletonType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (instanceProperty == null) {
            throw new InvalidOperationException("GameViewSizes singleton instance reflection property not found.");
        }

        return instanceProperty.GetValue(null);
    }

    private static object GetCurrentGameViewSizeGroup(object gameViewSizes)
    {
        Type gameViewSizeGroupType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeGroupType");
        if (gameViewSizeGroupType == null) {
            throw new InvalidOperationException("UnityEditor.GameViewSizeGroupType reflection type not found.");
        }

        object groupType = ResolveGameViewSizeGroupType(gameViewSizeGroupType);
        MethodInfo getGroup = gameViewSizes.GetType().GetMethod("GetGroup");
        if (getGroup == null) {
            throw new InvalidOperationException("GameViewSizes.GetGroup reflection method not found.");
        }

        return getGroup.Invoke(gameViewSizes, new[] { groupType });
    }

    private static object GetCurrentGameViewSizeGroup(object gameViewSizes, EditorWindow gameView)
    {
        object currentSizeGroupType = GetCurrentGameViewSizeGroupType(gameView);
        if (currentSizeGroupType == null) {
            return GetCurrentGameViewSizeGroup(gameViewSizes);
        }

        MethodInfo getGroup = gameViewSizes.GetType().GetMethod("GetGroup");
        if (getGroup == null) {
            throw new InvalidOperationException("GameViewSizes.GetGroup reflection method not found.");
        }

        return getGroup.Invoke(gameViewSizes, new[] { currentSizeGroupType });
    }

    private static object GetCurrentGameViewSizeGroupType(EditorWindow gameView)
    {
        if (gameView == null) {
            return null;
        }

        Type gameViewType = gameView.GetType();
        PropertyInfo currentSizeGroupType = gameViewType.GetProperty("currentSizeGroupType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (currentSizeGroupType != null) {
            return currentSizeGroupType.GetValue(gameView);
        }

        FieldInfo currentSizeGroupTypeField = gameViewType.GetField("m_CurrentSizeGroupType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return currentSizeGroupTypeField != null ? currentSizeGroupTypeField.GetValue(gameView) : null;
    }

    private static object ResolveGameViewSizeGroupType(Type gameViewSizeGroupType)
    {
        string groupName;
        switch (EditorUserBuildSettings.activeBuildTarget) {
            case BuildTarget.Android:
                groupName = "Android";
                break;
            case BuildTarget.iOS:
                groupName = "iOS";
                break;
            case BuildTarget.WebGL:
                groupName = "WebGL";
                break;
            default:
                groupName = "Standalone";
                break;
        }

        return Enum.IsDefined(gameViewSizeGroupType, groupName)
            ? Enum.Parse(gameViewSizeGroupType, groupName)
            : Enum.Parse(gameViewSizeGroupType, "Standalone");
    }

    private static bool IsMatchingGameViewSize(object size, int width, int height, string label)
    {
        if (size == null) {
            return false;
        }

        Type sizeType = size.GetType();
        int sizeWidth = GetIntProperty(size, sizeType, "width");
        int sizeHeight = GetIntProperty(size, sizeType, "height");
        if (sizeWidth == width && sizeHeight == height) {
            return true;
        }

        string sizeText = GetStringProperty(size, sizeType, "displayText");
        return !string.IsNullOrEmpty(sizeText) && sizeText.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int GetIntProperty(object instance, Type instanceType, string propertyName)
    {
        PropertyInfo property = instanceType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null) {
            return 0;
        }

        object value = property.GetValue(instance);
        if (value is int intValue) {
            return intValue;
        }

        return value is float floatValue ? Mathf.RoundToInt(floatValue) : 0;
    }

    private static string GetStringProperty(object instance, Type instanceType, string propertyName)
    {
        PropertyInfo property = instanceType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property != null ? property.GetValue(instance) as string : null;
    }

    private static void SetGameViewSize(EditorWindow gameView, int sizeIndex)
    {
        if (gameView == null) {
            return;
        }

        Type gameViewType = gameView.GetType();
        MethodInfo sizeSelectionCallback = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sizeSelectionCallback != null) {
            sizeSelectionCallback.Invoke(gameView, new object[] { sizeIndex, null });
            RefreshPreviewViews(gameView);
            return;
        }

        PropertyInfo selectedSizeIndex = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (selectedSizeIndex == null) {
            throw new InvalidOperationException("GameView size selection reflection entry not found.");
        }

        selectedSizeIndex.SetValue(gameView, sizeIndex);
        RefreshPreviewViews(gameView);
    }

    private static Type GetGameViewType()
    {
        return typeof(Editor).Assembly.GetType("UnityEditor.GameView");
    }

    private static EditorWindow GetGameViewWindow(Type gameViewType)
    {
        if (gameViewType == null) {
            return null;
        }

        EditorWindow gameView = FindOpenWindow(gameViewType);
        return gameView != null ? gameView : EditorWindow.GetWindow(gameViewType);
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
