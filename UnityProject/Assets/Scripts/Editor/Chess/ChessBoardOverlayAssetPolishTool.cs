#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using XNClient.ChessBoard;

// 盘面标记材质：形势为半透明墨/纸方块，最后一手为朱色圆点。
// 方块不加描边：落在同色活子上时与子面融为一体，只有死子上的异色方块会显出来。
// 形势方块的透明度由 RectGrid 按归属强度逐格覆盖，这里的 alpha 只是默认值。
public static class ChessBoardOverlayAssetPolishTool
{
    private const string ShaderPath = "Assets/Scenes/Duel/Materials/Shaders/BoardOverlay.shader";
    private const string BlackMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardBlack.mat";
    private const string WhiteMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardWhite.mat";
    private const string LatestMoveOnBlackStoneMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardLatestMoveOnBlackStone.mat";
    private const string LatestMoveOnWhiteStoneMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardLatestMoveOnWhiteStone.mat";
    private const float SquareShape = 0f;
    private const float DiscShape = 1f;
    private static readonly Color Accent = new Color32(0xA8, 0x43, 0x2F, 0xFF);
    // 黑子上的朱色提亮一档，保证在深色子面上可辨。
    private static readonly Color AccentOnBlackStone = new Color32(0xC8, 0x56, 0x3A, 0xFF);

    [MenuItem(CustomEditorMenuPaths.ChessBoard + "/应用棋盘覆盖层材质")]
    public static void Polish()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) {
            Debug.LogError($"Chess board overlay shader not found: {ShaderPath}");
            return;
        }

        ConfigureMaterial(BlackMaterialPath, shader, WithAlpha(BoardSurfaceMarker.Ink, 0.8f), SquareShape, Color.clear, 0f, 3000);
        ConfigureMaterial(WhiteMaterialPath, shader, WithAlpha(BoardSurfaceMarker.Paper, 0.8f), SquareShape, Color.clear, 0f, 3000);
        ConfigureMaterial(LatestMoveOnBlackStoneMaterialPath, shader, AccentOnBlackStone, DiscShape, Color.clear, 0f, 3100);
        ConfigureMaterial(LatestMoveOnWhiteStoneMaterialPath, shader, Accent, DiscShape, Color.clear, 0f, 3100);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Chess board overlay materials polished.");
    }

    private static void ConfigureMaterial(string materialPath, Shader shader, Color baseColor, float shape, Color outlineColor, float outlineWidth, int renderQueue)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.shader = shader;
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Shape", shape);
        material.SetColor("_OutlineColor", outlineColor);
        material.SetFloat("_OutlineWidth", outlineWidth);
        material.renderQueue = renderQueue;
        EditorUtility.SetDirty(material);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
#endif
