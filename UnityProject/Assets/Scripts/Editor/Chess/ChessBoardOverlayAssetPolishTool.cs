#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ChessBoardOverlayAssetPolishTool
{
    private const string ShaderPath = "Assets/Scenes/Duel/Materials/Shaders/BoardOverlay.shader";
    private const string BlackMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardBlack.mat";
    private const string WhiteMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardWhite.mat";
    private const string LatestMoveOnBlackStoneMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardLatestMoveOnBlackStone.mat";
    private const string LatestMoveOnWhiteStoneMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardLatestMoveOnWhiteStone.mat";

    [MenuItem(CustomEditorMenuPaths.ChessBoard + "/应用棋盘覆盖层材质")]
    public static void Polish()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) {
            Debug.LogError($"Chess board overlay shader not found: {ShaderPath}");
            return;
        }

        ConfigureMaterial(BlackMaterialPath, shader, new Color(0f, 0f, 0f, 0.8f), 3000);
        ConfigureMaterial(WhiteMaterialPath, shader, new Color(1f, 1f, 1f, 0.8f), 3000);
        ConfigureMaterial(LatestMoveOnBlackStoneMaterialPath, shader, new Color(1f, 1f, 1f, 1f), 3100);
        ConfigureMaterial(LatestMoveOnWhiteStoneMaterialPath, shader, new Color(0f, 0f, 0f, 1f), 3100);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Chess board overlay materials polished.");
    }

    private static void ConfigureMaterial(string materialPath, Shader shader, Color baseColor, int renderQueue)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.shader = shader;
        material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) {
            material.SetColor("_Color", Color.white);
        }

        material.renderQueue = renderQueue;
        EditorUtility.SetDirty(material);
    }
}
#endif
