#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class DuelSceneLookAssetTool
{
    private const string DuelScenePath = "Assets/Scenes/Duel/Duel.unity";
    private const string ProfileFolderPath = "Assets/Scenes/Duel/Profiles";
    private const string ProfilePath = ProfileFolderPath + "/DuelLookProfile.asset";
    private const string VolumeName = "DuelLookVolume";

    [MenuItem(CustomEditorMenuPaths.Scene + "/应用对局场景画面配置")]
    public static void Apply()
    {
        if (!EnsureProfileFolder()) {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(DuelScenePath, OpenSceneMode.Single);
        VolumeProfile profile = LoadOrCreateProfile();
        if (profile == null) {
            return;
        }

        ConfigureProfile(profile);

        if (!ConfigureMainCamera() || !ConfigureSceneVolume(scene, profile)) {
            return;
        }

        MarkProfileDirty(profile);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.Refresh();
        Debug.Log("Duel scene look assets applied.");
    }

    private static bool EnsureProfileFolder()
    {
        if (AssetDatabase.IsValidFolder(ProfileFolderPath)) {
            return true;
        }

        string parentFolder = Path.GetDirectoryName(ProfileFolderPath)?.Replace('\\', '/');
        string folderName = Path.GetFileName(ProfileFolderPath);
        if (!string.IsNullOrEmpty(parentFolder) && !AssetDatabase.IsValidFolder(parentFolder)) {
            Debug.LogError($"Profile parent folder not found: {parentFolder}");
            return false;
        }
        if (string.IsNullOrEmpty(parentFolder) || string.IsNullOrEmpty(folderName)) {
            Debug.LogError($"Profile folder path invalid: {ProfileFolderPath}");
            return false;
        }

        AssetDatabase.CreateFolder(parentFolder, folderName);
        return AssetDatabase.IsValidFolder(ProfileFolderPath);
    }

    private static VolumeProfile LoadOrCreateProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile != null) {
            return profile;
        }

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "DuelLookProfile";
        AssetDatabase.CreateAsset(profile, ProfilePath);
        return profile;
    }

    private static bool ConfigureMainCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) {
            Debug.LogError("Duel scene main camera not found.");
            return false;
        }

        UniversalAdditionalCameraData cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null) {
            cameraData = mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }

        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        cameraData.antialiasingQuality = AntialiasingQuality.Medium;
        EditorUtility.SetDirty(mainCamera.gameObject);
        return true;
    }

    private static bool ConfigureSceneVolume(Scene scene, VolumeProfile profile)
    {
        Volume volume = FindOrCreateVolume(scene);
        if (volume == null) {
            return false;
        }

        volume.isGlobal = true;
        volume.priority = 0f;
        volume.weight = 0.65f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume.gameObject);
        return true;
    }

    private static Volume FindOrCreateVolume(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) {
            Debug.LogError($"Duel scene not loaded: {DuelScenePath}");
            return null;
        }

        foreach (GameObject root in scene.GetRootGameObjects()) {
            foreach (Volume volume in root.GetComponentsInChildren<Volume>(true)) {
                if (volume.gameObject.name == VolumeName) {
                    return volume;
                }
            }
        }

        GameObject volumeGO = new GameObject(VolumeName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(volumeGO, scene);
        return volumeGO.AddComponent<Volume>();
    }

    private static void ConfigureProfile(VolumeProfile profile)
    {
        if (!profile.TryGet(out Tonemapping tonemapping)) {
            tonemapping = profile.Add<Tonemapping>();
            AssetDatabase.AddObjectToAsset(tonemapping, profile);
        }
        tonemapping.mode.Override(TonemappingMode.ACES);

        if (!profile.TryGet(out ColorAdjustments colorAdjustments)) {
            colorAdjustments = profile.Add<ColorAdjustments>();
            AssetDatabase.AddObjectToAsset(colorAdjustments, profile);
        }
        colorAdjustments.postExposure.Override(-0.08f);
        colorAdjustments.contrast.Override(8f);
        colorAdjustments.saturation.Override(-4f);
        colorAdjustments.colorFilter.Override(new Color(1f, 0.985f, 0.94f, 1f));

        if (!profile.TryGet(out Bloom bloom)) {
            bloom = profile.Add<Bloom>();
            AssetDatabase.AddObjectToAsset(bloom, profile);
        }
        bloom.threshold.Override(1.15f);
        bloom.intensity.Override(0.08f);
        bloom.scatter.Override(0.42f);

        if (!profile.TryGet(out Vignette vignette)) {
            vignette = profile.Add<Vignette>();
            AssetDatabase.AddObjectToAsset(vignette, profile);
        }
        vignette.color.Override(new Color(0.09f, 0.075f, 0.055f, 1f));
        vignette.intensity.Override(0.12f);
        vignette.smoothness.Override(0.45f);
    }

    private static void MarkProfileDirty(VolumeProfile profile)
    {
        foreach (VolumeComponent component in profile.components) {
            if (component != null) {
                EditorUtility.SetDirty(component);
            }
        }

        EditorUtility.SetDirty(profile);
    }
}
#endif
