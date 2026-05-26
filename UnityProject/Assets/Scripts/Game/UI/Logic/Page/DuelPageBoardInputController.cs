using UnityEngine;
using XNClient.ChessBoard;

public class DuelPageBoardInputController
{
    private GameObject aimChessPreview;
    private readonly RectCoordinates aimCoords = new RectCoordinates(-1, -1);
    private PlayerFlag aimChessPreviewPlayerFlag;

    public void Refresh(SceneBase mainScene, SceneComponentDuel compDuel, DuelInputAuthorityState inputState, bool blockInput)
    {
        aimCoords.SetValue(-1, -1);
        if (blockInput || !inputState.CanSubmitMove) {
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

    public void Dispose()
    {
        if (aimChessPreview != null) {
            GameObject.DestroyImmediate(aimChessPreview);
            aimChessPreview = null;
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
}
