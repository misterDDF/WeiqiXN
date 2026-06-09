using UnityEngine;
using XNClient.ChessBoard;

public class DuelPageBoardInputController
{
    private const float StoneRemovalHoverMarkerYOffset = 1.74f;
    private const float StoneRemovalHoverMarkerSizeFactor = 0.48f;

    private GameObject aimChessPreview;
    private GameObject stoneRemovalHoverMarker;
    private readonly RectCoordinates aimCoords = new RectCoordinates(-1, -1);
    private readonly RectCoordinates pendingMoveCoords = new RectCoordinates(-1, -1);
    private readonly RectCoordinates stoneRemovalHoverCoords = new RectCoordinates(-1, -1);
    private PlayerFlag aimChessPreviewPlayerFlag;
    private bool isPendingMoveActive;

    public bool IsPendingMoveActive => isPendingMoveActive;

    public void Refresh(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState, bool blockInput)
    {
        stoneRemovalHoverCoords.SetValue(-1, -1);
        if (!blockInput && mainScene is OgsDuelScene ogsScene) {
            OgsDuelSystem ogsDuelSystem = ogsScene.GetSystem<OgsDuelSystem>();
            if (ogsDuelSystem != null && ogsDuelSystem.IsInStoneRemovalPhase()) {
                ClearPendingMove();
                SetAimChessPreviewActive(false);
                RefreshStoneRemovalHover(mainScene, ogsDuelSystem);
                return;
            }
        }

        SetStoneRemovalHoverMarkerActive(false);
        if (isPendingMoveActive) {
            if (blockInput || !inputState.CanSubmitMove || !IsPendingMoveStillLegal(mainScene, compDuel, inputState.localInputPlayerFlag)) {
                ClearPendingMove();
            }
            return;
        }

        aimCoords.SetValue(-1, -1);
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

    public bool TryBeginOrUpdatePendingMove(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState)
    {
        if (!inputState.CanSubmitMove) {
            return false;
        }

        if (!TryResolveLegalMoveAtPointer(mainScene, compDuel, inputState.localInputPlayerFlag, out RectCoordinates coords, out Vector3 worldPosition)) {
            return false;
        }

        SetPendingMove(inputState.localInputPlayerFlag, coords, worldPosition);
        return true;
    }

    public bool TryMovePendingMove(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState, int offsetX, int offsetZ)
    {
        if (!isPendingMoveActive || !inputState.CanSubmitMove) {
            return false;
        }

        SceneComponentChessBoard compChessBoard = mainScene?.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard?.chessBoardGrid == null) {
            return false;
        }

        int maxCellIndex = Mathf.Max(compChessBoard.chessBoardGrid.gridSize - 1, 0);
        int x = Mathf.Clamp(pendingMoveCoords.x + offsetX, 0, maxCellIndex);
        int z = Mathf.Clamp(pendingMoveCoords.z + offsetZ, 0, maxCellIndex);
        return TrySetPendingMoveCoords(compChessBoard, compDuel, inputState.localInputPlayerFlag, new RectCoordinates(x, z));
    }

    public bool TryGetPendingMoveCoords(DuelInputAuthorityState inputState, out RectCoordinates coords)
    {
        coords = null;
        if (!isPendingMoveActive || !inputState.CanSubmitMove) {
            return false;
        }

        if (pendingMoveCoords.x < 0 || pendingMoveCoords.z < 0) {
            return false;
        }

        coords = pendingMoveCoords.Clone();
        return true;
    }

    public void ClearPendingMove()
    {
        isPendingMoveActive = false;
        pendingMoveCoords.SetValue(-1, -1);
        aimCoords.SetValue(-1, -1);
        SetAimChessPreviewActive(false);
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
        if (!TryResolveLegalMoveAtPointer(mainScene, compDuel, playerFlag, out RectCoordinates nearestCoords, out Vector3 worldPosition)) {
            SetAimChessPreviewActive(false);
            return;
        }

        EnsureAimChessPreview(playerFlag);
        if (aimChessPreview == null) {
            return;
        }

        aimChessPreview.transform.position = worldPosition;
        aimCoords.SetValue(nearestCoords.x, nearestCoords.z);
        SetAimChessPreviewActive(true);
    }

    private bool TryResolveLegalMoveAtPointer(
        SceneBase mainScene,
        SceneComponentDuel compDuel,
        PlayerFlag playerFlag,
        out RectCoordinates coords,
        out Vector3 worldPosition)
    {
        coords = null;
        worldPosition = Vector3.zero;
        if (mainScene == null || compDuel == null) {
            return false;
        }

        if (!TryGetBoardHitCoords(mainScene, out SceneComponentChessBoard compChessBoard, out RectCoordinates nearestCoords, out Vector3 localPosition)) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(nearestCoords);
        if (posIndex < 0 || !DuelMoveRule.CheckMoveLegal(compChessBoard, playerFlag, nearestCoords)) {
            return false;
        }

        coords = nearestCoords;
        worldPosition = compChessBoard.chessBoardGrid.transform.TransformPoint(localPosition);
        return true;
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

    private void SetPendingMove(PlayerFlag playerFlag, RectCoordinates coords, Vector3 worldPosition)
    {
        EnsureAimChessPreview(playerFlag);
        if (aimChessPreview == null) {
            return;
        }

        pendingMoveCoords.SetValue(coords.x, coords.z);
        aimCoords.SetValue(coords.x, coords.z);
        aimChessPreview.transform.position = worldPosition;
        SetAimChessPreviewActive(true);
        isPendingMoveActive = true;
    }

    private bool TrySetPendingMoveCoords(SceneComponentChessBoard compChessBoard, SceneComponentDuel compDuel, PlayerFlag playerFlag, RectCoordinates coords)
    {
        if (compChessBoard?.chessBoardGrid == null || compDuel == null || coords == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || !DuelMoveRule.CheckMoveLegal(compChessBoard, playerFlag, coords)) {
            return false;
        }

        Vector3 localPosition = compChessBoard.chessBoardGrid.GetCellCenterLocalPosition(coords.x, coords.z);
        SetPendingMove(playerFlag, coords, compChessBoard.chessBoardGrid.transform.TransformPoint(localPosition));
        return true;
    }

    private bool IsPendingMoveStillLegal(SceneBase mainScene, SceneComponentDuel compDuel, PlayerFlag playerFlag)
    {
        SceneComponentChessBoard compChessBoard = mainScene?.GetComponent<SceneComponentChessBoard>();
        return TrySetPendingMoveCoords(compChessBoard, compDuel, playerFlag, pendingMoveCoords);
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
