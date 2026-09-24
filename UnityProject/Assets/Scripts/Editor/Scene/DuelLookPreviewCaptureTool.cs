#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XNClient.ChessBoard;

// 编辑态生成对局画面截图，供美术迭代对比；棋盘在运行时才生成，这里用反射补齐编辑态不会触发的 Awake 和三角化。
// 截图结束后重新打开原场景丢弃临时对象，因此要求执行前场景没有未保存改动。
public static class DuelLookPreviewCaptureTool
{
    private const string DuelScenePath = "Assets/Scenes/Duel/Duel.unity";
    private const string OutputFolder = "Temp/WeiqiXN/LookPreview";
    private const string BlackStonePrefabPath = "Assets/Models/Chess/ChessBlack.prefab";
    private const string WhiteStonePrefabPath = "Assets/Models/Chess/ChessWhite.prefab";
    private const float CameraDistance = 30f;
    private const float FramePadding = 1.08f;
    private const float PortraitFramePadding = 1.02f;
    // 特写取景：以左下角星位附近为中心放大，便于观察木纹、线条与棋子质感。
    private const float CloseupFrameScale = 0.3f;
    private static readonly Vector2Int CloseupCenter = new Vector2Int(4, 4);
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
            CaptureBoard(19, Black19, White19, 1600, 900, "board19_closeup", CloseupFrameScale);
        }
        finally {
            if (!string.IsNullOrEmpty(restoreScenePath)) {
                EditorSceneManager.OpenScene(restoreScenePath, OpenSceneMode.Single);
            }
        }

        Debug.Log($"Look preview captured to {Path.GetFullPath(OutputFolder)}");
    }

    private static void CaptureBoard(int gridSize, Vector2Int[] blackStones, Vector2Int[] whiteStones, int width, int height, string fileName, float frameScale = 1f)
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
        PlaceStones(grid, BlackStonePrefabPath, blackStones);
        PlaceStones(grid, WhiteStonePrefabPath, whiteStones);

        FrameCamera(camera, grid.GetGridBounds(), (float)width / height);
        if (frameScale < 1f) {
            Vector3 center = grid.transform.TransformPoint(grid.GetCellCenterLocalPosition(CloseupCenter.x, CloseupCenter.y));
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

    private static void PlaceStones(RectGrid grid, string prefabPath, Vector2Int[] points)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) {
            Debug.LogError($"Look preview stone prefab not found: {prefabPath}");
            return;
        }

        MethodInfo randomize = typeof(ChessStoneVisualRandomizer).GetMethod("ApplyRandomOffset", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (Vector2Int point in points) {
            if (point.x >= grid.gridSize || point.y >= grid.gridSize) {
                continue;
            }

            GameObject stone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            stone.transform.SetParent(grid.transform, false);
            stone.transform.localPosition = grid.GetCellCenterLocalPosition(point.x, point.y);
            ChessStoneVisualRandomizer randomizer = stone.GetComponent<ChessStoneVisualRandomizer>();
            if (randomizer != null) {
                randomize?.Invoke(randomizer, null);
            }
        }
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
