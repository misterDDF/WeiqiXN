#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XNClient.ChessBoard;

// 编辑态生成对局画面截图，供美术迭代对比；棋盘在运行时才生成，这里用反射补齐编辑态不会触发的 Awake 和三角化。
// 截图结束后重新打开原场景丢弃临时对象，因此要求执行前场景没有未保存改动。
// 盘上标记（形势、AI 推荐点、最后一手、手数）用合成数据绘制，只用于检查外观，不代表真实分析结果。
public static class DuelLookPreviewCaptureTool
{
    private enum BoardMarkers
    {
        None,
        Analysis,
        MoveNumbers,
        Motion,
    }

    private const string DuelScenePath = "Assets/Scenes/Duel/Duel.unity";
    private const string OutputFolder = "Temp/WeiqiXN/LookPreview";
    private const string BlackStonePrefabPath = "Assets/Models/Chess/ChessBlack.prefab";
    private const string WhiteStonePrefabPath = "Assets/Models/Chess/ChessWhite.prefab";
    private const string OwnershipBlackMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardBlack.mat";
    private const string OwnershipWhiteMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardWhite.mat";
    private const string LatestMoveOnBlackStoneMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardLatestMoveOnBlackStone.mat";
    private const string LatestMoveOnWhiteStoneMaterialPath = "Assets/Scenes/Duel/Materials/ChessBoardLatestMoveOnWhiteStone.mat";
    private const float CameraDistance = 30f;
    private const float FramePadding = 1.08f;
    private const float PortraitFramePadding = 1.02f;
    // 特写取景：以左下角星位附近为中心放大，便于观察木纹、线条与棋子质感。
    private const float CloseupFrameScale = 0.3f;
    private static readonly Vector2 CloseupCenter = new Vector2(4f, 4f);
    // 分析特写：左上角，包含推荐点、最后一手、形势方块与两条边的坐标。
    private static readonly Vector2 AnalysisCloseupCenter = new Vector2(4.5f, 2.3f);
    // 合成形势：每颗棋子按高斯衰减向周围辐射归属，再压到 [-1, 1]。
    private const float OwnershipInfluenceSigma = 2.2f;
    private const float OwnershipInfluenceGain = 1.6f;
    // 三位数手数的截图从 90 手开始编号。
    private const int LateGameMoveNumberBase = 90;
    private const int CaptureMsaaSamples = 4;

    // 19 路中盘局面：包含相邻棋子，便于观察接触阴影和棋子间投影。
    private static readonly Vector2Int[] Black19 =
    {
        new Vector2Int(3, 3), new Vector2Int(15, 3), new Vector2Int(3, 15), new Vector2Int(16, 15),
        new Vector2Int(2, 5), new Vector2Int(3, 5), new Vector2Int(4, 6), new Vector2Int(5, 2),
        new Vector2Int(9, 9), new Vector2Int(10, 10), new Vector2Int(9, 10), new Vector2Int(13, 16),
        new Vector2Int(14, 16), new Vector2Int(15, 14), new Vector2Int(6, 15), new Vector2Int(7, 16),
        new Vector2Int(16, 9), new Vector2Int(16, 10), new Vector2Int(2, 11),
    };

    private static readonly Vector2Int[] White19 =
    {
        new Vector2Int(15, 15), new Vector2Int(3, 16), new Vector2Int(16, 3), new Vector2Int(2, 2),
        new Vector2Int(2, 3), new Vector2Int(4, 4), new Vector2Int(5, 5), new Vector2Int(10, 9),
        new Vector2Int(11, 10), new Vector2Int(14, 15), new Vector2Int(15, 16), new Vector2Int(5, 16),
        new Vector2Int(6, 16), new Vector2Int(15, 9), new Vector2Int(15, 10), new Vector2Int(3, 12),
        new Vector2Int(4, 13),
    };

    // 空点上的推荐：(x, z, 胜率)，按胜率从高到低排序；第一推荐用三位数，近景里检查最宽的胜率文字。
    private static readonly Vector3Int[] AiRecommendations19 =
    {
        new Vector3Int(6, 3, 100), new Vector3Int(12, 12, 53), new Vector3Int(6, 9, 49), new Vector3Int(3, 9, 47),
    };

    private static readonly Vector2Int LatestMoveOnWhite19 = new Vector2Int(4, 4);
    private static readonly Vector2Int LatestMoveOnBlack19 = new Vector2Int(4, 6);

    // 落子关键帧（落子开始后的秒数）：刚出手、下落途中、将要着盘、着盘后摇晃峰值，放在同一张特写里对比投影偏移与倾斜高光。
    private static readonly (Vector2Int point, bool isBlackStone, float elapsed)[] PlacementKeyframes19 =
    {
        (new Vector2Int(5, 2), true, 0f),
        (new Vector2Int(4, 4), false, 0.05f),
        (new Vector2Int(4, 6), true, 0.08f),
        (new Vector2Int(3, 3), true, 0.127f),
    };
    private static readonly Vector3 PlacementKeyframeTiltAxis = new Vector3(1f, 0f, 1f).normalized;

    private static readonly Vector2Int[] Black9 =
    {
        new Vector2Int(2, 2), new Vector2Int(6, 6), new Vector2Int(4, 4), new Vector2Int(3, 4),
        new Vector2Int(5, 2), new Vector2Int(2, 6),
    };

    private static readonly Vector2Int[] White9 =
    {
        new Vector2Int(6, 2), new Vector2Int(2, 5), new Vector2Int(4, 5), new Vector2Int(5, 4),
        new Vector2Int(6, 3),
    };

    [MenuItem(CustomEditorMenuPaths.Scene + "/生成对局画面预览截图")]
    public static void Capture()
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.isDirty) {
            Debug.LogError("Look preview capture aborted: active scene has unsaved changes.");
            return;
        }

        string restoreScenePath = activeScene.path;
        Directory.CreateDirectory(OutputFolder);
        try {
            CaptureBoard(19, Black19, White19, 1600, 900, "board19_landscape");
            CaptureBoard(9, Black9, White9, 1600, 900, "board9_landscape");
            CaptureBoard(19, Black19, White19, 720, 1280, "board19_portrait");
            CaptureBoard(19, Black19, White19, 1600, 900, "board19_closeup", CloseupFrameScale, CloseupCenter);
            CaptureBoard(19, Black19, White19, 1600, 900, "board19_analysis", markers: BoardMarkers.Analysis);
            CaptureBoard(19, Black19, White19, 1600, 900, "board19_analysis_closeup", CloseupFrameScale, AnalysisCloseupCenter, BoardMarkers.Analysis);
            CaptureBoard(13, Black19, White19, 1600, 900, "board13_numbers", markers: BoardMarkers.MoveNumbers);
            CaptureBoard(19, Black19, White19, 1600, 900, "board19_numbers_closeup", CloseupFrameScale, CloseupCenter, BoardMarkers.MoveNumbers, LateGameMoveNumberBase);
            CaptureBoard(19, Black19, White19, 1600, 900, "board19_motion_closeup", CloseupFrameScale, CloseupCenter, BoardMarkers.Motion);
        }
        finally {
            if (!string.IsNullOrEmpty(restoreScenePath)) {
                EditorSceneManager.OpenScene(restoreScenePath, OpenSceneMode.Single);
            }
        }

        Debug.Log($"Look preview captured to {Path.GetFullPath(OutputFolder)}");
    }

    private static void CaptureBoard(
        int gridSize,
        Vector2Int[] blackStones,
        Vector2Int[] whiteStones,
        int width,
        int height,
        string fileName,
        float frameScale = 1f,
        Vector2 frameCenter = default,
        BoardMarkers markers = BoardMarkers.None,
        int moveNumberBase = 0)
    {
        EditorSceneManager.OpenScene(DuelScenePath, OpenSceneMode.Single);
        RectGrid grid = Object.FindObjectOfType<RectGrid>();
        Camera camera = Camera.main;
        if (grid == null || camera == null) {
            Debug.LogError("Look preview capture failed: RectGrid or main camera not found in Duel scene.");
            return;
        }

        grid.InitGrid(gridSize);
        BuildBoardMeshes(grid);

        Random.InitState(gridSize * 7919);
        List<ChessStoneView> blackViews = PlaceStones(grid, BlackStonePrefabPath, blackStones);
        List<ChessStoneView> whiteViews = PlaceStones(grid, WhiteStonePrefabPath, whiteStones);
        if (markers == BoardMarkers.Analysis) {
            DrawAnalysisMarkers(grid, blackStones, whiteStones, blackViews, whiteViews);
        } else if (markers == BoardMarkers.MoveNumbers) {
            DrawMoveNumbers(blackViews, whiteViews, moveNumberBase);
        } else if (markers == BoardMarkers.Motion) {
            ApplyMotionKeyframes(blackStones, whiteStones, blackViews, whiteViews);
        }

        FrameCamera(camera, grid.GetGridBounds(), (float)width / height);
        if (frameScale < 1f) {
            float cellSize = ChessBoardConfig.rectCellSideLength;
            Vector3 localCenter = grid.GetCellCenterLocalPosition(0, 0) + new Vector3(frameCenter.x * cellSize, 0f, -frameCenter.y * cellSize);
            Vector3 center = grid.transform.TransformPoint(localCenter);
            camera.orthographicSize *= frameScale;
            camera.transform.position = new Vector3(center.x, camera.transform.position.y, center.z);
        }
        RenderToPng(camera, width, height, Path.Combine(OutputFolder, fileName + ".png"));
    }

    private static void BuildBoardMeshes(RectGrid grid)
    {
        MethodInfo meshAwake = typeof(RectMesh).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo triangulate = typeof(RectGridChunk).GetMethod("TriangulateChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (RectMesh mesh in grid.GetComponentsInChildren<RectMesh>(true)) {
            meshAwake?.Invoke(mesh, null);
        }
        foreach (RectGridChunk chunk in grid.GetComponentsInChildren<RectGridChunk>(true)) {
            if (chunk.gridSize > 0) {
                triangulate?.Invoke(chunk, null);
            }
        }
    }

    // 返回与 points 一一对应的棋子视图，超出棋盘的点为 null。
    private static List<ChessStoneView> PlaceStones(RectGrid grid, string prefabPath, Vector2Int[] points)
    {
        List<ChessStoneView> views = new List<ChessStoneView>(points.Length);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) {
            Debug.LogError($"Look preview stone prefab not found: {prefabPath}");
            return views;
        }

        MethodInfo randomize = typeof(ChessStoneVisualRandomizer).GetMethod("ApplyRandomOffset", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (Vector2Int point in points) {
            if (point.x >= grid.gridSize || point.y >= grid.gridSize) {
                views.Add(null);
                continue;
            }

            GameObject stone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            stone.transform.SetParent(grid.transform, false);
            stone.transform.localPosition = grid.GetCellCenterLocalPosition(point.x, point.y);
            ChessStoneVisualRandomizer randomizer = stone.GetComponent<ChessStoneVisualRandomizer>();
            if (randomizer != null) {
                randomize?.Invoke(randomizer, null);
            }
            views.Add(stone.AddComponent<ChessStoneView>());
        }

        return views;
    }

    private static void DrawAnalysisMarkers(
        RectGrid grid,
        Vector2Int[] blackStones,
        Vector2Int[] whiteStones,
        List<ChessStoneView> blackViews,
        List<ChessStoneView> whiteViews)
    {
        Material latestOnBlack = AssetDatabase.LoadAssetAtPath<Material>(LatestMoveOnBlackStoneMaterialPath);
        Material latestOnWhite = AssetDatabase.LoadAssetAtPath<Material>(LatestMoveOnWhiteStoneMaterialPath);
        grid.SetBoardMaterials(
            AssetDatabase.LoadAssetAtPath<Material>(OwnershipBlackMaterialPath),
            AssetDatabase.LoadAssetAtPath<Material>(OwnershipWhiteMaterialPath));
        grid.SetLatestMoveMarkerMaterials(latestOnBlack, latestOnWhite);
        grid.DrawOwnership(BuildSyntheticOwnership(grid.gridSize, blackStones, whiteStones), DuelOwnershipQueryService.OwnershipThreshold);

        List<RectGridAiRecommendationMarker> recommendations = new List<RectGridAiRecommendationMarker>();
        for (int i = 0; i < AiRecommendations19.Length; i++) {
            Vector3Int recommendation = AiRecommendations19[i];
            recommendations.Add(new RectGridAiRecommendationMarker(recommendation.x, recommendation.y, recommendation.z, i + 1));
        }
        grid.DrawAiRecommendationMarkers(recommendations);

        SetLatestMoveMarker(blackStones, blackViews, LatestMoveOnBlack19, true, latestOnBlack, latestOnWhite);
        SetLatestMoveMarker(whiteStones, whiteViews, LatestMoveOnWhite19, false, latestOnBlack, latestOnWhite);
    }

    private static void SetLatestMoveMarker(
        Vector2Int[] points,
        List<ChessStoneView> views,
        Vector2Int target,
        bool isBlackStone,
        Material latestOnBlack,
        Material latestOnWhite)
    {
        int index = System.Array.IndexOf(points, target);
        if (index < 0 || views[index] == null) {
            return;
        }

        views[index].SetLatestMoveMarkerMaterials(latestOnBlack, latestOnWhite);
        views[index].SetMarker(StoneMarkerIntent.LatestMove(isBlackStone));
    }

    private static void ApplyMotionKeyframes(
        Vector2Int[] blackStones,
        Vector2Int[] whiteStones,
        List<ChessStoneView> blackViews,
        List<ChessStoneView> whiteViews)
    {
        foreach ((Vector2Int point, bool isBlackStone, float elapsed) in PlacementKeyframes19) {
            FindStoneView(point, isBlackStone, blackStones, whiteStones, blackViews, whiteViews)?.ApplyPlacementPose(elapsed, PlacementKeyframeTiltAxis);
        }
    }

    private static ChessStoneView FindStoneView(
        Vector2Int point,
        bool isBlackStone,
        Vector2Int[] blackStones,
        Vector2Int[] whiteStones,
        List<ChessStoneView> blackViews,
        List<ChessStoneView> whiteViews)
    {
        Vector2Int[] points = isBlackStone ? blackStones : whiteStones;
        List<ChessStoneView> views = isBlackStone ? blackViews : whiteViews;
        int index = System.Array.IndexOf(points, point);
        return index >= 0 ? views[index] : null;
    }

    // 黑白交替编号：黑棋第 k 颗为 base + 2k + 1，白棋第 k 颗为 base + 2k + 2。
    private static void DrawMoveNumbers(List<ChessStoneView> blackViews, List<ChessStoneView> whiteViews, int moveNumberBase)
    {
        for (int i = 0; i < blackViews.Count; i++) {
            blackViews[i]?.SetMarker(StoneMarkerIntent.MoveNumber(moveNumberBase + i * 2 + 1, true));
        }
        for (int i = 0; i < whiteViews.Count; i++) {
            whiteViews[i]?.SetMarker(StoneMarkerIntent.MoveNumber(moveNumberBase + i * 2 + 2, false));
        }
    }

    // 与 KataGo ownership 相同的排布：按行从上到下（z 从 0 开始）、正值归黑。
    private static JArray BuildSyntheticOwnership(int gridSize, Vector2Int[] blackStones, Vector2Int[] whiteStones)
    {
        JArray ownership = new JArray();
        float inverseSigmaSquared = 1f / (OwnershipInfluenceSigma * OwnershipInfluenceSigma);
        for (int z = 0; z < gridSize; z++) {
            for (int x = 0; x < gridSize; x++) {
                Vector2Int point = new Vector2Int(x, z);
                float influence = 0f;
                foreach (Vector2Int stone in blackStones) {
                    influence += Mathf.Exp(-(stone - point).sqrMagnitude * inverseSigmaSquared);
                }
                foreach (Vector2Int stone in whiteStones) {
                    influence -= Mathf.Exp(-(stone - point).sqrMagnitude * inverseSigmaSquared);
                }
                ownership.Add(System.Math.Tanh(influence * OwnershipInfluenceGain));
            }
        }

        return ownership;
    }

    // 与 ChessBoardSystem.InitDuelVCam 的正交俯视取景口径保持一致（不含竖屏上移和复盘横移）。
    private static void FrameCamera(Camera camera, Bounds bounds, float aspect)
    {
        bool portrait = aspect < 1f;
        float orthographicSize = portrait
            ? bounds.extents.x / aspect
            : Mathf.Max(bounds.extents.z, bounds.extents.x / aspect);
        camera.orthographic = true;
        camera.orthographicSize = orthographicSize * (portrait ? PortraitFramePadding : FramePadding);
        camera.transform.SetPositionAndRotation(
            bounds.center + Vector3.up * CameraDistance,
            Quaternion.LookRotation(Vector3.down, Vector3.forward));
    }

    private static void RenderToPng(Camera camera, int width, int height, string path)
    {
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.antiAliasing = CaptureMsaaSamples;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Texture2D readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply();
            File.WriteAllBytes(path, readback.EncodeToPNG());
        }
        finally {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(readback);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
#endif
