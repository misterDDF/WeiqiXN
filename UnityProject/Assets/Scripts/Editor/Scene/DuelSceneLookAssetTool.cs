#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using XNClient.ChessBoard;

// 对局与复盘场景的画面配置（视觉美化 V1，见 doc-governance/UnityProject/modules/14-visual-polish-plan.md）：
// 贴图导入、棋盘/桌布材质、URP 抗锯齿、主光与环境光、桌布、后处理；重复执行结果一致。
// 场景不使用实时阴影：棋子阴影由接触阴影面片（StoneContactShadow.shader）表现，盘体在桌面上的投影由 Table.shader 解析计算。
public static class DuelSceneLookAssetTool
{
    private static readonly string[] LookScenePaths =
    {
        "Assets/Scenes/Duel/Duel.unity",
        "Assets/Scenes/Replay/Replay.unity",
    };

    private const string ProfileFolderPath = "Assets/Scenes/Duel/Profiles";
    private const string ProfilePath = ProfileFolderPath + "/DuelLookProfile.asset";
    private const string VolumeName = "DuelLookVolume";
    private const string RenderPipelineAssetPath = "Assets/Graphics/URPAssetDefault.asset";

    private const string BoardMaterialPath = "Assets/Scenes/Duel/Materials/Ground.mat";
    private const string BoardShaderPath = "Assets/Scenes/Duel/Materials/Shaders/Ground.shader";
    private const string BoardTexturePath = "Assets/Scenes/Duel/Materials/Textures/KayaBoard.png";
    private const string TableMaterialPath = "Assets/Scenes/Duel/Materials/LinenTable.mat";
    private const string TableShaderPath = "Assets/Scenes/Duel/Materials/Shaders/Table.shader";
    private const string TableTexturePath = "Assets/Scenes/Duel/Materials/Textures/LinenTable.png";
    private const string StoneDetailTexturePath = "Assets/Models/Chess/Textures/StoneDetail.png";

    private const string TableObjectName = "DuelTable";
    // 桌面低于盘面，盘面投影偏出盘边，暗示棋盘厚度。
    private const float TableHeight = -6f;
    private const float TableSize = 400f;
    // 一张桌布贴图覆盖的世界尺寸：贴图一根线 4 像素，对应约 0.6 毫米粗的麻线（一格 4.5 单位约 22 毫米）。
    private const float TableTextureWorldSize = 32f;
    private const float TableBoardContactStrength = 0.35f;
    private const float TableBoardContactWidth = 2.5f;
    private const float TableBoardShadowSoftness = 1.6f;
    private static readonly Color TableBackgroundColor = new Color(0.16f, 0.145f, 0.125f, 1f);

    private const int MsaaSampleCount = 4;
    // 室内顶光：主光从画面左上方高角度照下，棋子中的柔光窗反射与接触阴影方向都取自主光。
    // 环境光占比接近主光，让明暗过渡柔和；地面色取棋盘木色的反光，使棋子下缘带出暖色。
    private static readonly Vector3 MainLightEuler = new Vector3(62f, 150f, 0f);
    // 光色与环境光接近中性：木色、棋子固有色已经偏暖，光再偏暖会让白子泛黄、棋盘发橙。
    private static readonly Color MainLightColor = new Color(1f, 0.98f, 0.95f, 1f);
    private const float MainLightIntensity = 0.8f;
    private static readonly Color AmbientSkyColor = new Color(0.68f, 0.675f, 0.66f, 1f);
    private static readonly Color AmbientEquatorColor = new Color(0.52f, 0.5f, 0.46f, 1f);
    private static readonly Color AmbientGroundColor = new Color(0.46f, 0.33f, 0.18f, 1f);

    [MenuItem(CustomEditorMenuPaths.Scene + "/应用对局场景画面配置")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            Debug.LogError("Duel scene look apply aborted: exit play mode first.");
            return;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.isDirty) {
            Debug.LogError("Duel scene look apply aborted: active scene has unsaved changes.");
            return;
        }

        if (!EnsureProfileFolder()) {
            return;
        }

        ConfigureTextureImporters();
        if (!ConfigureRenderPipelineAsset()) {
            return;
        }

        Material boardMaterial = ConfigureBoardMaterial();
        Material tableMaterial = ConfigureTableMaterial();
        VolumeProfile profile = LoadOrCreateProfile();
        if (boardMaterial == null || tableMaterial == null || profile == null) {
            return;
        }

        ConfigureProfile(profile);
        MarkProfileDirty(profile);
        AssetDatabase.SaveAssets();

        string restoreScenePath = activeScene.path;
        try {
            foreach (string scenePath in LookScenePaths) {
                if (!ApplyScene(scenePath, profile, tableMaterial)) {
                    return;
                }
            }
        }
        finally {
            if (!string.IsNullOrEmpty(restoreScenePath)) {
                EditorSceneManager.OpenScene(restoreScenePath, OpenSceneMode.Single);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("Duel scene look assets applied.");
    }

    private static bool ApplyScene(string scenePath, VolumeProfile profile, Material tableMaterial)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (!scene.IsValid() || !scene.isLoaded) {
            Debug.LogError($"Look scene not loaded: {scenePath}");
            return false;
        }

        if (!ConfigureMainCamera(scenePath)
            || !ConfigureSceneVolume(scene, profile)
            || !ConfigureLighting(scene)
            || !ConfigureBoardChunkTemplate(scene)) {
            return false;
        }

        ConfigureTable(scene, tableMaterial);
        EditorSceneManager.MarkSceneDirty(scene);
        return EditorSceneManager.SaveScene(scene);
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

    private static void ConfigureTextureImporters()
    {
        // 棋盘整盘只铺一次：Clamp 避免边缘采样到对边；竖屏 1024 已接近 1:1 像素。
        ConfigureTexture(BoardTexturePath, true, TextureWrapMode.Clamp, 2048, 1024);
        ConfigureTexture(TableTexturePath, true, TextureWrapMode.Repeat, 1024, 1024);
        // 棋子细节图是数据通道，不做 sRGB 转换。
        ConfigureTexture(StoneDetailTexturePath, false, TextureWrapMode.Repeat, 256, 256);
    }

    private static void ConfigureTexture(string path, bool sRGB, TextureWrapMode wrapMode, int maxSize, int androidMaxSize)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) {
            Debug.LogError($"Look texture importer not found: {path}");
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = sRGB;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = true;
        importer.wrapMode = wrapMode;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 1;
        importer.maxTextureSize = maxSize;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = androidMaxSize;
        android.format = TextureImporterFormat.ASTC_4x4;
        importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
    }

    private static bool ConfigureRenderPipelineAsset()
    {
        UniversalRenderPipelineAsset pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineAssetPath);
        if (pipelineAsset == null) {
            Debug.LogError($"URP asset not found: {RenderPipelineAssetPath}");
            return false;
        }

        SerializedObject serializedAsset = new SerializedObject(pipelineAsset);
        if (!TrySetInt(serializedAsset, "m_MSAA", MsaaSampleCount)) {
            return false;
        }

        serializedAsset.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipelineAsset);
        return true;
    }

    private static bool TrySetInt(SerializedObject serializedObject, string propertyPath, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyPath);
        if (property == null) {
            Debug.LogError($"Serialized property not found: {serializedObject.targetObject.name}.{propertyPath}");
            return false;
        }

        if (property.propertyType == SerializedPropertyType.Boolean) {
            property.boolValue = value != 0;
        } else {
            property.intValue = value;
        }
        return true;
    }

    private static Material ConfigureBoardMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(BoardMaterialPath);
        Shader shader = LoadShader(BoardShaderPath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BoardTexturePath);
        if (material == null || shader == null || texture == null) {
            Debug.LogError($"Board material, shader or texture missing: {BoardMaterialPath}");
            return null;
        }

        material.shader = shader;
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetTextureOffset("_BaseMap", Vector2.zero);
        material.SetColor("_BaseColor", Color.white);
        // 墨线接近真实棋盘的漆线：约 1 毫米宽（一格约 22 毫米），近黑，透出少许木色。
        material.SetColor("_InkColor", new Color(0.05f, 0.042f, 0.035f, 0.92f));
        material.SetFloat("_InkGrain", 0.25f);
        material.SetFloat("_LineWidth", 0.045f);
        material.SetFloat("_EdgeLineWidth", 0.09f);
        material.SetFloat("_StarRadius", 0.11f);
        material.SetFloat("_BevelWidth", 0.5f);
        material.SetFloat("_BevelSlope", 1.1f);
        material.SetFloat("_AmbientStrength", 1f);
        EditorUtils.RemoveUnusedMaterialProperties(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material ConfigureTableMaterial()
    {
        Shader shader = LoadShader(TableShaderPath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TableTexturePath);
        if (shader == null || texture == null) {
            Debug.LogError($"Table shader or texture missing: {TableShaderPath}");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(TableMaterialPath);
        if (material == null) {
            material = new Material(shader) { name = "LinenTable" };
            AssetDatabase.CreateAsset(material, TableMaterialPath);
        }

        material.shader = shader;
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", Vector2.one / TableTextureWorldSize);
        material.SetTextureOffset("_BaseMap", Vector2.zero);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_AmbientStrength", 1f);
        material.SetFloat("_BoardContactStrength", TableBoardContactStrength);
        material.SetFloat("_BoardContactWidth", TableBoardContactWidth);
        material.SetFloat("_BoardCastSoftness", TableBoardShadowSoftness);
        EditorUtils.RemoveUnusedMaterialProperties(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Shader LoadShader(string path)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null) {
            Debug.LogError($"Shader not found: {path}");
            return null;
        }

        if (ShaderUtil.ShaderHasError(shader)) {
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader)) {
                Debug.LogError($"{path}:{message.line} {message.message} ({message.platform})");
            }
            return null;
        }

        return shader;
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

    private static bool ConfigureMainCamera(string scenePath)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) {
            Debug.LogError($"Main camera not found: {scenePath}");
            return false;
        }

        UniversalAdditionalCameraData cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null) {
            cameraData = mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }

        // 盘面线条已在 shader 内解析抗锯齿，MSAA 负责棋子轮廓；FXAA 会糊掉木纹细节。
        mainCamera.allowMSAA = true;
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = TableBackgroundColor;
        cameraData.renderPostProcessing = true;
        cameraData.renderShadows = true;
        cameraData.antialiasing = AntialiasingMode.None;
        EditorUtility.SetDirty(mainCamera);
        EditorUtility.SetDirty(cameraData);
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
        volume.weight = 1f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
        return true;
    }

    private static Volume FindOrCreateVolume(Scene scene)
    {
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

    private static bool ConfigureLighting(Scene scene)
    {
        Light mainLight = null;
        foreach (GameObject root in scene.GetRootGameObjects()) {
            foreach (Light light in root.GetComponentsInChildren<Light>(true)) {
                if (light.type == LightType.Directional) {
                    mainLight = light;
                    break;
                }
            }
            if (mainLight != null) {
                break;
            }
        }

        if (mainLight == null) {
            Debug.LogError($"Directional light not found: {scene.path}");
            return false;
        }

        mainLight.transform.rotation = Quaternion.Euler(MainLightEuler);
        mainLight.useColorTemperature = false;
        mainLight.color = MainLightColor;
        mainLight.intensity = MainLightIntensity;
        mainLight.shadows = LightShadows.None;
        EditorUtility.SetDirty(mainLight);
        EditorUtility.SetDirty(mainLight.transform);

        RenderSettings.sun = mainLight;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbientSkyColor;
        RenderSettings.ambientEquatorColor = AmbientEquatorColor;
        RenderSettings.ambientGroundColor = AmbientGroundColor;
        RenderSettings.ambientIntensity = 1f;
        DynamicGI.UpdateEnvironment();
        return true;
    }

    // 网格线与星位改由 Ground.shader 绘制；RoadMesh 只保留碰撞体供落子射线检测。
    private static bool ConfigureBoardChunkTemplate(Scene scene)
    {
        RectGrid grid = null;
        foreach (GameObject root in scene.GetRootGameObjects()) {
            grid = root.GetComponentInChildren<RectGrid>(true);
            if (grid != null) {
                break;
            }
        }

        RectGridChunk chunk = grid != null && grid.chunkPrefab != null ? grid.chunkPrefab.GetComponent<RectGridChunk>() : null;
        if (chunk == null || chunk.groundMesh == null || chunk.roadMesh == null) {
            Debug.LogError($"RectGrid chunk template not found: {scene.path}");
            return false;
        }

        MeshRenderer groundRenderer = chunk.groundMesh.GetComponent<MeshRenderer>();
        MeshRenderer roadRenderer = chunk.roadMesh.GetComponent<MeshRenderer>();
        if (groundRenderer == null || roadRenderer == null) {
            Debug.LogError($"RectGrid chunk renderers not found: {scene.path}");
            return false;
        }

        groundRenderer.shadowCastingMode = ShadowCastingMode.Off;
        groundRenderer.receiveShadows = false;
        roadRenderer.enabled = false;
        EditorUtility.SetDirty(groundRenderer);
        EditorUtility.SetDirty(roadRenderer);
        return true;
    }

    private static void ConfigureTable(Scene scene, Material tableMaterial)
    {
        GameObject table = null;
        foreach (GameObject root in scene.GetRootGameObjects()) {
            if (root.name == TableObjectName) {
                table = root;
                break;
            }
        }

        if (table == null) {
            table = new GameObject(TableObjectName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(table, scene);
        }

        // 不加碰撞体：落子射线只应命中棋盘。
        MeshFilter meshFilter = table.GetComponent<MeshFilter>();
        if (meshFilter == null) {
            meshFilter = table.AddComponent<MeshFilter>();
        }
        meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        MeshRenderer meshRenderer = table.GetComponent<MeshRenderer>();
        if (meshRenderer == null) {
            meshRenderer = table.AddComponent<MeshRenderer>();
        }
        meshRenderer.sharedMaterial = tableMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = true;
        meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        float boardCenter = 19 * ChessBoardConfig.rectCellSideLength / 2f;
        table.transform.SetPositionAndRotation(new Vector3(boardCenter, TableHeight, boardCenter), Quaternion.Euler(90f, 0f, 0f));
        table.transform.localScale = new Vector3(TableSize, TableSize, 1f);
        EditorUtility.SetDirty(table);
    }

    private static void ConfigureProfile(VolumeProfile profile)
    {
        // 画面动态范围很低（没有自发光和硬高光），不做色调映射，保持线性响应：
        // Neutral 曲线会把线性 1.0 压到约 0.54，白子、棋盘、桌布的明度层次全被压灰。
        Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.None);

        ColorAdjustments colorAdjustments = GetOrAdd<ColorAdjustments>(profile);
        colorAdjustments.postExposure.Override(0f);
        colorAdjustments.contrast.Override(8f);
        colorAdjustments.saturation.Override(0f);
        colorAdjustments.colorFilter.Override(Color.white);

        WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>(profile);
        whiteBalance.temperature.Override(0f);
        whiteBalance.tint.Override(0f);

        // 朴素风格不需要辉光。
        if (profile.TryGet(out Bloom bloom)) {
            profile.Remove<Bloom>();
            Object.DestroyImmediate(bloom, true);
        }

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.color.Override(new Color(0.07f, 0.06f, 0.05f, 1f));
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.55f);
    }

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) {
            return component;
        }

        component = profile.Add<T>();
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
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
