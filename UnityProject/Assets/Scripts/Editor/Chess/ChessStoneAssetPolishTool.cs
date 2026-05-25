#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ChessStoneAssetPolishTool
{
    private const string ShaderPath = "Assets/Models/Chess/Shaders/StoneGloss.shader";
    private const string BlackMaterialPath = "Assets/Models/Chess/Materials/ChessBlack.mat";
    private const string WhiteMaterialPath = "Assets/Models/Chess/Materials/ChessWhite.mat";
    private const string BlackPrefabPath = "Assets/Models/Chess/ChessBlack.prefab";
    private const string WhitePrefabPath = "Assets/Models/Chess/ChessWhite.prefab";

    [MenuItem("Tools/WeiqiXN/Polish Chess Stone Assets")]
    public static void Polish()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) {
            Debug.LogError($"Chess stone shader not found: {ShaderPath}");
            return;
        }

        ConfigureMaterial(
            BlackMaterialPath,
            shader,
            new Color(0.014f, 0.014f, 0.013f, 1f),
            new Color(0.006f, 0.021f, 0.018f, 1f),
            new Color(0.36f, 0.4f, 0.37f, 1f),
            0.7f,
            0.24f,
            0.055f,
            0.008f,
            4.2f);

        ConfigureMaterial(
            WhiteMaterialPath,
            shader,
            new Color(0.88f, 0.855f, 0.775f, 1f),
            new Color(0.72f, 0.69f, 0.61f, 1f),
            new Color(1f, 0.965f, 0.88f, 1f),
            0.52f,
            0.18f,
            0.045f,
            0.018f,
            2.8f);

        ConfigurePrefab(BlackPrefabPath, BlackMaterialPath);
        ConfigurePrefab(WhitePrefabPath, WhiteMaterialPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Chess stone assets polished.");
    }

    private static void ConfigureMaterial(
        string materialPath,
        Shader shader,
        Color baseColor,
        Color edgeColor,
        Color highlightColor,
        float smoothness,
        float specStrength,
        float rimStrength,
        float patternStrength,
        float patternScale)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) {
            Debug.LogError($"Chess stone material not found: {materialPath}");
            return;
        }

        material.shader = shader;
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_EdgeColor", edgeColor);
        material.SetColor("_HighlightColor", highlightColor);
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
            Debug.LogError($"Chess stone prefab not found: {prefabPath}");
            return;
        }

        try {
            Transform model = prefabRoot.transform.Find("Model");
            if (model == null) {
                Debug.LogError($"Chess stone model node not found: {prefabPath}");
                return;
            }

            model.localPosition = new Vector3(0f, 0.8f, 0f);
            model.localRotation = Quaternion.identity;
            model.localScale = new Vector3(3.9f, 1.6f, 3.9f);

            MeshRenderer renderer = model.GetComponent<MeshRenderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (renderer != null && material != null) {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
#endif
