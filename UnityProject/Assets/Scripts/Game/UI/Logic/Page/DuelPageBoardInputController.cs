using UnityEngine;
using XNClient.ChessBoard;

public class DuelPageBoardInputController
{
    private const float StoneRemovalHoverMarkerYOffset = 1.74f;
    private const float StoneRemovalHoverMarkerSizeFactor = 0.48f;

    private GameObject aimChessPreview;
    private GameObject stoneRemovalHoverMarker;
    private readonly RectCoordinates aimCoords = new RectCoordinates(-1, -1);
    private readonly RectCoordinates stoneRemovalHoverCoords = new RectCoordinates(-1, -1);
    private PlayerFlag aimChessPreviewPlayerFlag;

    public void Refresh(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState, bool blockInput)
    {
        aimCoords.SetValue(-1, -1);
        stoneRemovalHoverCoords.SetValue(-1, -1);
        if (!blockInput && mainScene is OgsDuelScene ogsScene) {
            OgsDuelSystem ogsDuelSystem = ogsScene.GetSystem<OgsDuelSystem>();
            if (ogsDuelSystem != null && ogsDuelSystem.IsInStoneRemovalPhase()) {
                SetAimChessPreviewActive(false);
                RefreshStoneRemovalHover(mainScene, ogsDuelSystem);
                return;
            }
        }

        SetStoneRemovalHoverMarkerActive(false);
        if (blockInput || IsPointerOverUI() || !inputState.CanSubmitMove) {
            SetAimChessPreviewActive(false);
            return;
        }

        RefreshAimChessPreview(mainScene, compDuel, inputState.localInputPlayerFlag);
    }

    public bool TryGetMoveCoords(DuelInputAuthorityState inputState, out RectCoordinates coords)
    {
        coords = null;
        if (!inputState.CanSubmitMove) {
            return false;
        }

        if (aimCoords.x < 0 || aimCoords.z < 0) {
            return false;
        }

        coords = aimCoords.Clone();
        return true;
    }

    public bool TryGetStoneRemovalCoords(out RectCoordinates coords)
    {
        coords = null;
        if (stoneRemovalHoverCoords.x < 0 || stoneRemovalHoverCoords.z < 0) {
            return false;
        }

        coords = stoneRemovalHoverCoords.Clone();
        return true;
    }

    public void Dispose()
    {
        if (aimChessPreview != null) {
            GameObject.DestroyImmediate(aimChessPreview);
            aimChessPreview = null;
        }
        if (stoneRemovalHoverMarker != null) {
            GameObject.DestroyImmediate(stoneRemovalHoverMarker);
            stoneRemovalHoverMarker = null;
        }
    }

    private void RefreshAimChessPreview(SceneBase mainScene, SceneComponentDuel compDuel, PlayerFlag playerFlag)
    {
        if (mainScene == null || compDuel == null) {
            SetAimChessPreviewActive(false);
            return;
        }

        Ray mouseRay = Global.Instance.uiManager.uiCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(mouseRay.origin, mouseRay.direction, out RaycastHit hitInfo, 500)) {
            SetAimChessPreviewActive(false);
            return;
        }

        SceneComponentChessBoard compChessBoard = mainScene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard?.chessBoardGrid == null) {
            SetAimChessPreviewActive(false);
            return;
        }

        Transform gridTransform = compChessBoard.chessBoardGrid.transform;
        Vector3 localHitPoint = gridTransform.InverseTransformPoint(hitInfo.point);
        float cellSideLength = ChessBoardConfig.rectCellSideLength;
        float boardSideLength = compChessBoard.chessBoardGrid.gridSize * cellSideLength;
        if (localHitPoint.x < 0f || localHitPoint.x > boardSideLength || localHitPoint.z < 0f || localHitPoint.z > boardSideLength) {
            SetAimChessPreviewActive(false);
            return;
        }

        int nearestCellX = Mathf.RoundToInt(localHitPoint.x / cellSideLength - 0.5f);
        int nearestCellZ = compChessBoard.chessBoardGrid.gridSize - 1 - Mathf.RoundToInt(localHitPoint.z / cellSideLength - 0.5f);

        int maxCellIndex = Mathf.Max(compChessBoard.chessBoardGrid.gridSize - 1, 0);
        nearestCellX = Mathf.Clamp(nearestCellX, 0, maxCellIndex);
        nearestCellZ = Mathf.Clamp(nearestCellZ, 0, maxCellIndex);

        RectCoordinates nearestCoords = new RectCoordinates(nearestCellX, nearestCellZ);
        int posIndex = compChessBoard.GetPosIndexByCoords(nearestCoords);
        if (posIndex < 0 || !DuelMoveRule.CheckMoveLegal(compChessBoard, playerFlag, nearestCoords)) {
            SetAimChessPreviewActive(false);
            return;
        }

        EnsureAimChessPreview(playerFlag);
        if (aimChessPreview == null) {
            return;
        }

        Vector3 nearestCellCenterLocalPos = compChessBoard.chessBoardGrid.GetCellCenterLocalPosition(nearestCellX, nearestCellZ);
        aimChessPreview.transform.position = gridTransform.TransformPoint(nearestCellCenterLocalPos);
        aimCoords.SetValue(nearestCoords.x, nearestCoords.z);
        SetAimChessPreviewActive(true);
    }

    private void RefreshStoneRemovalHover(SceneBase mainScene, OgsDuelSystem ogsDuelSystem)
    {
        if (mainScene == null || ogsDuelSystem == null || IsPointerOverUI()) {
            SetStoneRemovalHoverMarkerActive(false);
            return;
        }

        if (!TryGetBoardHitCoords(mainScene, out SceneComponentChessBoard compChessBoard, out RectCoordinates coords, out Vector3 localPosition)) {
            SetStoneRemovalHoverMarkerActive(false);
            return;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        bool hasStone = posIndex >= 0 && compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString());
        if (!hasStone || ogsDuelSystem.IsRemovedStone(coords)) {
            SetStoneRemovalHoverMarkerActive(false);
            return;
        }

        EnsureStoneRemovalHoverMarker();
        if (stoneRemovalHoverMarker == null) {
            return;
        }

        Transform gridTransform = compChessBoard.chessBoardGrid.transform;
        localPosition.y += StoneRemovalHoverMarkerYOffset;
        stoneRemovalHoverMarker.transform.position = gridTransform.TransformPoint(localPosition);
        stoneRemovalHoverMarker.transform.rotation = gridTransform.rotation;
        stoneRemovalHoverCoords.SetValue(coords.x, coords.z);
        SetStoneRemovalHoverMarkerActive(true);
    }

    private bool TryGetBoardHitCoords(
        SceneBase mainScene,
        out SceneComponentChessBoard compChessBoard,
        out RectCoordinates coords,
        out Vector3 nearestCellCenterLocalPos)
    {
        compChessBoard = null;
        coords = null;
        nearestCellCenterLocalPos = Vector3.zero;
        Ray mouseRay = Global.Instance.uiManager.uiCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(mouseRay.origin, mouseRay.direction, out RaycastHit hitInfo, 500)) {
            return false;
        }

        compChessBoard = mainScene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard?.chessBoardGrid == null) {
            return false;
        }

        Transform gridTransform = compChessBoard.chessBoardGrid.transform;
        Vector3 localHitPoint = gridTransform.InverseTransformPoint(hitInfo.point);
        float cellSideLength = ChessBoardConfig.rectCellSideLength;
        float boardSideLength = compChessBoard.chessBoardGrid.gridSize * cellSideLength;
        if (localHitPoint.x < 0f || localHitPoint.x > boardSideLength || localHitPoint.z < 0f || localHitPoint.z > boardSideLength) {
            return false;
        }

        int nearestCellX = Mathf.RoundToInt(localHitPoint.x / cellSideLength - 0.5f);
        int nearestCellZ = compChessBoard.chessBoardGrid.gridSize - 1 - Mathf.RoundToInt(localHitPoint.z / cellSideLength - 0.5f);
        int maxCellIndex = Mathf.Max(compChessBoard.chessBoardGrid.gridSize - 1, 0);
        nearestCellX = Mathf.Clamp(nearestCellX, 0, maxCellIndex);
        nearestCellZ = Mathf.Clamp(nearestCellZ, 0, maxCellIndex);
        coords = new RectCoordinates(nearestCellX, nearestCellZ);
        nearestCellCenterLocalPos = compChessBoard.chessBoardGrid.GetCellCenterLocalPosition(nearestCellX, nearestCellZ);
        return true;
    }

    private void EnsureAimChessPreview(PlayerFlag playerFlag)
    {
        if (aimChessPreview != null && aimChessPreviewPlayerFlag == playerFlag) {
            return;
        }

        if (aimChessPreview != null) {
            GameObject.DestroyImmediate(aimChessPreview);
            aimChessPreview = null;
        }

        string gamePrefabTypeId = DuelUtils.GetPreviewGamePrefabTypeIdWithPlayerFlag(playerFlag);
        GamePrefabDataType gamePrefabCfg = GamePrefabDataType.GetConfigData(gamePrefabTypeId);
        if (gamePrefabCfg == null) {
            return;
        }

        aimChessPreview = Global.Instance.resourceManager.LoadGamePrefab(gamePrefabCfg.resPath);
        if (aimChessPreview == null) {
            return;
        }

        aimChessPreviewPlayerFlag = playerFlag;
        SetAimChessPreviewActive(false);
        foreach (Collider collider in aimChessPreview.GetComponentsInChildren<Collider>()) {
            collider.enabled = false;
        }
    }

    private void SetAimChessPreviewActive(bool isActive)
    {
        if (aimChessPreview != null) {
            aimChessPreview.SetActive(isActive);
        }
    }

    private void EnsureStoneRemovalHoverMarker()
    {
        if (stoneRemovalHoverMarker != null) {
            return;
        }

        stoneRemovalHoverMarker = new GameObject("OgsStoneRemovalHoverCross");
        MeshFilter meshFilter = stoneRemovalHoverMarker.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateCrossMesh(ChessBoardConfig.rectCellSideLength * StoneRemovalHoverMarkerSizeFactor);
        MeshRenderer meshRenderer = stoneRemovalHoverMarker.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = CreateCrossMaterial();
        meshRenderer.receiveShadows = false;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetStoneRemovalHoverMarkerActive(false);
    }

    private Mesh CreateCrossMesh(float size)
    {
        float halfLength = size * 0.5f;
        float halfWidth = size * 0.08f;
        Mesh mesh = new Mesh();
        mesh.name = "OgsStoneRemovalHoverCrossMesh";
        Vector3[] vertices = {
            new Vector3(-halfLength, 0f, -halfWidth),
            new Vector3(halfLength, 0f, -halfWidth),
            new Vector3(halfLength, 0f, halfWidth),
            new Vector3(-halfLength, 0f, halfWidth),
            new Vector3(-halfWidth, 0f, -halfLength),
            new Vector3(halfWidth, 0f, -halfLength),
            new Vector3(halfWidth, 0f, halfLength),
            new Vector3(-halfWidth, 0f, halfLength),
        };
        int[] triangles = {
            0, 2, 1, 0, 3, 2,
            4, 6, 5, 4, 7, 6,
        };
        Quaternion rotation = Quaternion.Euler(0f, 45f, 0f);
        for (int i = 0; i < vertices.Length; i++) {
            vertices[i] = rotation * vertices[i];
        }
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    private Material CreateCrossMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        Material material = new Material(shader);
        Color color = new Color(0.95f, 0.12f, 0.08f, 0.9f);
        material.color = color;
        if (material.HasProperty("_BaseColor")) {
            material.SetColor("_BaseColor", color);
        }
        return material;
    }

    private void SetStoneRemovalHoverMarkerActive(bool isActive)
    {
        if (stoneRemovalHoverMarker != null) {
            stoneRemovalHoverMarker.SetActive(isActive);
        }
    }

    private bool IsPointerOverUI()
    {
        return UIUtils.IsPointerOverUI();
    }
}
