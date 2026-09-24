#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ChessStonePreviewAssetPolishTool
{
    private const string ShaderPath = "Assets/Models/Chess/Shaders/StoneGlossPreview.shader";
    private const string BlackMaterialPath = "Assets/Models/Chess/Materials/ChessBlackPreview.mat";
    private const string WhiteMaterialPath = "Assets/Models/Chess/Materials/ChessWhitePreview.mat";
    private const string BlackSourceMaterialPath = "Assets/Models/Chess/Materials/ChessBlack.mat";
    private const string WhiteSourceMaterialPath = "Assets/Models/Chess/Materials/ChessWhite.mat";
    private const string BlackPrefabPath = "Assets/Models/Chess/ChessBlackPreview.prefab";
    private const string WhitePrefabPath = "Assets/Models/Chess/ChessWhitePreview.prefab";
    private const string ModelNodePath = "VisualOffset/Model";

    [MenuItem(CustomEditorMenuPaths.ChessBoard + "/应用预览棋子透明配置")]
    public static void Polish()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) {
            Debug.LogError($"Chess preview stone shader not found: {ShaderPath}");
            return;
        }

        ConfigureMaterial(BlackMaterialPath, BlackSourceMaterialPath, shader, 0.8f);
        ConfigureMaterial(WhiteMaterialPath, WhiteSourceMaterialPath, shader, 0.85f);

        ConfigurePrefab(BlackPrefabPath, BlackMaterialPath);
        ConfigurePrefab(WhitePrefabPath, WhiteMaterialPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Chess preview stone assets polished.");
    }

    // 预览材质从正式棋子材质复制外观参数，只额外设置透明度，保证两者一致。
    private static void ConfigureMaterial(string materialPath, string sourceMaterialPath, Shader shader, float previewAlpha)
    {
        Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(sourceMaterialPath);
        if (sourceMaterial == null) {
            Debug.LogError($"Chess stone source material not found: {sourceMaterialPath}");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        // CopyPropertiesFromMaterial 会连同源材质的 shader 一起带过来，复制后必须重新指定预览 shader。
        material.CopyPropertiesFromMaterial(sourceMaterial);
        material.shader = shader;
        material.SetFloat("_PreviewAlpha", previewAlpha);
        EditorUtils.RemoveUnusedMaterialProperties(material);
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
            Transform model = prefabRoot.transform.Find(ModelNodePath);
            MeshRenderer renderer = model != null ? model.GetComponent<MeshRenderer>() : null;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (renderer != null && material != null) {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            } else {
                Debug.LogError($"Chess preview stone renderer or material not found: {prefabPath}");
                return;
            }

            // 预览棋子悬在落点上方示意，不压暗盘面；接触阴影节点继承自正式棋子 prefab，这里只关闭。
            Transform contactShadow = prefabRoot.transform.Find(ChessStoneAssetPolishTool.ContactShadowNodePath);
            if (contactShadow != null) {
                contactShadow.gameObject.SetActive(false);
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
#endif
