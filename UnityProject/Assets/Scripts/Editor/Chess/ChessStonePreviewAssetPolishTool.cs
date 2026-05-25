#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ChessStonePreviewAssetPolishTool
{
    private const string ShaderPath = "Assets/Models/Chess/Shaders/StoneGlossPreview.shader";
    private const string BlackMaterialPath = "Assets/Models/Chess/Materials/ChessBlackPreview.mat";
    private const string WhiteMaterialPath = "Assets/Models/Chess/Materials/ChessWhitePreview.mat";
    private const string BlackPrefabPath = "Assets/Models/Chess/ChessBlackPreview.prefab";
    private const string WhitePrefabPath = "Assets/Models/Chess/ChessWhitePreview.prefab";

    [MenuItem(CustomEditorMenuPaths.ChessBoard + "/应用预览棋子透明配置")]
    public static void Polish()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) {
            Debug.LogError($"Chess preview stone shader not found: {ShaderPath}");
            return;
        }

        ConfigureMaterial(
            BlackMaterialPath,
            shader,
            new Color(0.026f, 0.025f, 0.023f, 1f),
            new Color(0.008f, 0.008f, 0.007f, 1f),
            new Color(0.74f, 0.72f, 0.64f, 1f),
            0.8f,
            0.86f,
            0.58f,
            0.1f,
            0.008f,
            4.2f);

        ConfigureMaterial(
            WhiteMaterialPath,
            shader,
            new Color(0.88f, 0.855f, 0.775f, 1f),
            new Color(0.72f, 0.69f, 0.61f, 1f),
            new Color(1f, 0.965f, 0.88f, 1f),
            0.85f,
            0.52f,
            0.18f,
            0.045f,
            0.018f,
            2.8f);

        ConfigurePrefab(BlackPrefabPath, BlackMaterialPath);
        ConfigurePrefab(WhitePrefabPath, WhiteMaterialPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Chess preview stone assets polished.");
    }

    private static void ConfigureMaterial(
        string materialPath,
        Shader shader,
        Color baseColor,
        Color edgeColor,
        Color highlightColor,
        float previewAlpha,
        float smoothness,
        float specStrength,
        float rimStrength,
        float patternStrength,
        float patternScale)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.shader = shader;
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_EdgeColor", edgeColor);
        material.SetColor("_HighlightColor", highlightColor);
        material.SetFloat("_PreviewAlpha", previewAlpha);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_SpecStrength", specStrength);
        material.SetFloat("_RimStrength", rimStrength);
        material.SetFloat("_PatternStrength", patternStrength);
        material.SetFloat("_PatternScale", patternScale);
        EditorUtility.SetDirty(material);
    }

    private static void ConfigurePrefab(string prefabPath, string materialPath)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot == null) {
            Debug.LogError($"Chess preview stone prefab not found: {prefabPath}");
            return;
        }

        try {
            MeshRenderer renderer = prefabRoot.GetComponentInChildren<MeshRenderer>(true);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (renderer != null && material != null) {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            } else {
                Debug.LogError($"Chess preview stone renderer or material not found: {prefabPath}");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
#endif
