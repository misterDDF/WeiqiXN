using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using XNClient.ChessBoard;

public static class MainMenuBoardBackdropTool
{
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu/MainMenu.unity";
    private const string DuelScenePath = "Assets/Scenes/Duel/Duel.unity";
    private const string BlackStonePath = "Assets/Models/Chess/ChessBlack.prefab";
    private const string WhiteStonePath = "Assets/Models/Chess/ChessWhite.prefab";
    private const string MainMenuPagePath = "Assets/UI/Prefab/Page/MainMenuPage.prefab";
    private const string PreviewFolder = "Temp/WeiqiXN/MainMenuPreview";

    [MenuItem(CustomEditorMenuPaths.UI + "/安装主菜单真实棋盘背景")]
    public static void Install()
    {
        Scene originalScene = EditorSceneManager.GetActiveScene();
        if (originalScene.isDirty) {
            Debug.LogError("Main menu board installation requires the active scene to be saved first.");
            return;
        }

        string restoreScenePath = originalScene.path;
        Scene mainMenuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        Scene duelScene = EditorSceneManager.OpenScene(DuelScenePath, OpenSceneMode.Additive);
        try {
            GameObject sourceBoard = FindRoot(duelScene, "ChessBoardGrid");
            GameObject sourceLightObject = FindRoot(duelScene, "Directional Light");
            GameObject cameraObject = FindRoot(mainMenuScene, "Main Camera");
            GameObject lightObject = FindRoot(mainMenuScene, "Directional Light");
            GameObject blackStone = AssetDatabase.LoadAssetAtPath<GameObject>(BlackStonePath);
            GameObject whiteStone = AssetDatabase.LoadAssetAtPath<GameObject>(WhiteStonePath);
            if (sourceBoard == null || sourceLightObject == null || cameraObject == null || lightObject == null || blackStone == null || whiteStone == null) {
                Debug.LogError("Main menu board installation is missing a Duel board, light, camera or stone prefab.");
                return;
            }

            UnityEngine.SceneManagement.SceneManager.SetActiveScene(duelScene);
            Material skybox = RenderSettings.skybox;
            Color ambientSky = RenderSettings.ambientSkyColor;
            Color ambientEquator = RenderSettings.ambientEquatorColor;
            Color ambientGround = RenderSettings.ambientGroundColor;
            var ambientMode = RenderSettings.ambientMode;
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(mainMenuScene);

            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            Light sourceLight = sourceLightObject.GetComponent<Light>();
            Light light = lightObject.GetComponent<Light>();
            lightObject.transform.rotation = sourceLightObject.transform.rotation;
            light.color = sourceLight.color;
            light.intensity = sourceLight.intensity;
            light.shadows = LightShadows.None;

            GameObject oldBoard = FindRoot(mainMenuScene, "MainMenuBoard");
            if (oldBoard != null) {
                Object.DestroyImmediate(oldBoard);
            }

            GameObject boardObject = Object.Instantiate(sourceBoard);
            boardObject.name = "MainMenuBoard";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(boardObject, mainMenuScene);

            MainMenuBoardBackdrop backdrop = cameraObject.GetComponent<MainMenuBoardBackdrop>();
            if (backdrop == null) {
                backdrop = cameraObject.AddComponent<MainMenuBoardBackdrop>();
            }
            backdrop.board = boardObject.GetComponent<RectGrid>();
            backdrop.blackStonePrefab = blackStone;
            backdrop.whiteStonePrefab = whiteStone;

            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = UIPalette.Table;
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            EditorSceneManager.MarkSceneDirty(mainMenuScene);
            EditorSceneManager.SaveScene(mainMenuScene);
        }
        finally {
            EditorSceneManager.CloseScene(duelScene, true);
            if (!string.IsNullOrEmpty(restoreScenePath)) {
                EditorSceneManager.OpenScene(restoreScenePath, OpenSceneMode.Single);
            }
        }
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/生成主菜单棋盘预览截图")]
    public static void Capture()
    {
        Scene originalScene = EditorSceneManager.GetActiveScene();
        if (originalScene.isDirty) {
            Debug.LogError("Main menu board capture requires the active scene to be saved first.");
            return;
        }

        string restoreScenePath = originalScene.path;
        try {
            Scene mainMenuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            GameObject cameraObject = FindRoot(mainMenuScene, "Main Camera");
            MainMenuBoardBackdrop backdrop = cameraObject != null ? cameraObject.GetComponent<MainMenuBoardBackdrop>() : null;
            if (backdrop == null) {
                Debug.LogError("Main menu board backdrop is not installed.");
                return;
            }

            backdrop.Build();
            MethodInfo meshAwake = typeof(RectMesh).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo triangulate = typeof(RectGridChunk).GetMethod("TriangulateChunk", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (RectMesh mesh in backdrop.board.GetComponentsInChildren<RectMesh>(true)) {
                meshAwake?.Invoke(mesh, null);
            }
            foreach (RectGridChunk chunk in backdrop.board.GetComponentsInChildren<RectGridChunk>(true)) {
                if (chunk.gridSize > 0) {
                    triangulate?.Invoke(chunk, null);
                }
            }

            Directory.CreateDirectory(PreviewFolder);
            Camera camera = cameraObject.GetComponent<Camera>();
            backdrop.ApplyFrame(1600, 900);
            EditorUtils.RenderCameraToPng(camera, 1600, 900, 4, Path.Combine(PreviewFolder, "board_landscape.png"));
            backdrop.ApplyFrame(720, 1280);
            EditorUtils.RenderCameraToPng(camera, 720, 1280, 4, Path.Combine(PreviewFolder, "board_portrait.png"));

            GameObject pagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainMenuPagePath);
            GameObject page = (GameObject)PrefabUtility.InstantiatePrefab(pagePrefab, mainMenuScene);
            CapturePage(camera, backdrop, page, 1600, 900, "Landscape", "main_landscape.png");
            CapturePage(camera, backdrop, page, 720, 1280, "Portrait", "main_portrait.png");
        }
        finally {
            if (!string.IsNullOrEmpty(restoreScenePath)) {
                EditorSceneManager.OpenScene(restoreScenePath, OpenSceneMode.Single);
            }
        }
    }

    private static void CapturePage(Camera camera, MainMenuBoardBackdrop backdrop, GameObject page, int width, int height, string stateName, string fileName)
    {
        backdrop.ApplyFrame(width, height);
        foreach (StateRoot stateRoot in page.GetComponentsInChildren<StateRoot>(true)) {
            stateRoot.SetState(stateName, true);
        }

        Canvas canvas = page.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        RectTransform rectTransform = (RectTransform)page.transform;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(width, height);
        rectTransform.rotation = camera.transform.rotation;
        rectTransform.localScale = Vector3.one * (camera.orthographicSize * 2f / height);
        rectTransform.position = camera.transform.position + camera.transform.forward;
        Canvas.ForceUpdateCanvases();
        EditorUtils.RenderCameraToPng(camera, width, height, 4, Path.Combine(PreviewFolder, fileName));
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects()) {
            if (root.name == name) {
                return root;
            }
        }
        return null;
    }
}
