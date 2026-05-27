using System.Collections.Generic;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public readonly struct ChessStoneViewState
{
    public readonly RectCoordinates coords;
    public readonly PlayerFlag playerFlag;

    public ChessStoneViewState(RectCoordinates coords, PlayerFlag playerFlag)
    {
        this.coords = coords;
        this.playerFlag = playerFlag;
    }
}

public class ChessStoneViewCache
{
    private readonly SceneBase scene;
    private readonly SceneComponentChessBoard compChessBoard;
    private readonly Dictionary<int, Dictionary<PlayerFlag, GameObject>> stoneViews = new Dictionary<int, Dictionary<PlayerFlag, GameObject>>();
    private readonly Dictionary<int, ChessStoneView> visibleStoneViews = new Dictionary<int, ChessStoneView>();
    private readonly Dictionary<int, PlayerFlag> visibleStoneFlags = new Dictionary<int, PlayerFlag>();
    private readonly Dictionary<int, StoneMarkerIntent> pendingStoneMarkers = new Dictionary<int, StoneMarkerIntent>();
    private readonly HashSet<string> loadingViewKeys = new HashSet<string>();
    private Material latestMoveMarkerOnBlackStoneMaterial;
    private Material latestMoveMarkerOnWhiteStoneMaterial;
    private bool isDestroyed;

    public ChessStoneViewCache(SceneBase scene, SceneComponentChessBoard compChessBoard)
    {
        this.scene = scene;
        this.compChessBoard = compChessBoard;
    }

    public void ShowStone(RectCoordinates coords, PlayerFlag playerFlag)
    {
        if (isDestroyed) {
            return;
        }

        int posIndex = GetValidPosIndex(coords, playerFlag);
        if (posIndex < 0) {
            return;
        }

        HideOtherStoneAt(posIndex, playerFlag);
        bool wasVisible = visibleStoneFlags.TryGetValue(posIndex, out PlayerFlag visibleFlag) && visibleFlag == playerFlag;
        visibleStoneFlags[posIndex] = playerFlag;

        if (TryGetStoneView(posIndex, playerFlag, out GameObject go)) {
            ApplyStoneTransform(go, coords);
            go.SetActive(true);
            ChessStoneView stoneView = EnsureStoneView(go);
            stoneView.Bind(posIndex, playerFlag, !wasVisible);
            visibleStoneViews[posIndex] = stoneView;
            ApplyPendingMarkerToView(posIndex, stoneView);
            return;
        }

        string viewKey = GetViewKey(posIndex, playerFlag);
        if (!loadingViewKeys.Contains(viewKey)) {
            LoadStoneView(posIndex, coords.Clone(), playerFlag, viewKey);
        }
    }

    public void HideStone(RectCoordinates coords)
    {
        if (isDestroyed || compChessBoard == null) {
            return;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0) {
            return;
        }

        HideStoneAt(posIndex);
    }

    public void HideAllStones()
    {
        if (isDestroyed) {
            return;
        }

        ClearStoneMarkers();
        visibleStoneFlags.Clear();
        visibleStoneViews.Clear();
        foreach (Dictionary<PlayerFlag, GameObject> viewsAtPos in stoneViews.Values) {
            foreach (GameObject go in viewsAtPos.Values) {
                if (go != null) {
                    EnsureStoneView(go).Unbind();
                    go.SetActive(false);
                }
            }
        }
    }

    public void SyncStones(IEnumerable<ChessStoneViewState> targetStones)
    {
        if (isDestroyed) {
            return;
        }

        Dictionary<int, PlayerFlag> targetFlags = new Dictionary<int, PlayerFlag>();
        if (targetStones != null) {
            foreach (ChessStoneViewState stone in targetStones) {
                int posIndex = GetValidPosIndex(stone.coords, stone.playerFlag);
                if (posIndex < 0 || targetFlags.ContainsKey(posIndex)) {
                    continue;
                }

                targetFlags[posIndex] = stone.playerFlag;
            }
        }

        List<int> currentVisibleIndexes = new List<int>(visibleStoneFlags.Keys);
        foreach (int posIndex in currentVisibleIndexes) {
            if (!targetFlags.ContainsKey(posIndex)) {
                HideStoneAt(posIndex);
            }
        }

        foreach (KeyValuePair<int, PlayerFlag> kvp in targetFlags) {
            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(kvp.Key);
            ShowStone(coords, kvp.Value);
        }
    }

    public void SyncFromChessInfoDict()
    {
        if (isDestroyed) {
            return;
        }

        List<ChessStoneViewState> targetStones = new List<ChessStoneViewState>();
        foreach (KeyValuePair<string, ChessInfo> kvp in compChessBoard.chessInfoDict) {
            if (!int.TryParse(kvp.Key, out int posIndex) || kvp.Value == null) {
                continue;
            }

            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(posIndex);
            PlayerFlag playerFlag = (PlayerFlag)kvp.Value.chessFlag.value;
            targetStones.Add(new ChessStoneViewState(coords, playerFlag));
        }

        SyncStones(targetStones);
    }

    public void SetLatestMoveMarkerMaterials(Material onBlackStoneMaterial, Material onWhiteStoneMaterial)
    {
        latestMoveMarkerOnBlackStoneMaterial = onBlackStoneMaterial;
        latestMoveMarkerOnWhiteStoneMaterial = onWhiteStoneMaterial;

        foreach (ChessStoneView stoneView in visibleStoneViews.Values) {
            if (stoneView != null) {
                stoneView.SetLatestMoveMarkerMaterials(latestMoveMarkerOnBlackStoneMaterial, latestMoveMarkerOnWhiteStoneMaterial);
            }
        }
    }

    public void ClearStoneMarkers()
    {
        pendingStoneMarkers.Clear();
        foreach (ChessStoneView stoneView in visibleStoneViews.Values) {
            if (stoneView != null) {
                stoneView.ClearMarker();
            }
        }
    }

    public void ApplyLatestMoveMarker(RectCoordinates coords, PlayerFlag playerFlag)
    {
        int posIndex = GetValidPosIndex(coords, playerFlag);
        if (posIndex < 0) {
            ClearStoneMarkers();
            return;
        }

        Dictionary<int, StoneMarkerIntent> markers = new Dictionary<int, StoneMarkerIntent>
        {
            [posIndex] = StoneMarkerIntent.LatestTriangle(playerFlag == PlayerFlag.Player1)
        };
        ApplyStoneMarkers(markers);
    }

    public void ApplyStoneMarkers(Dictionary<int, StoneMarkerIntent> markers)
    {
        ClearStoneMarkers();
        if (markers == null) {
            return;
        }

        foreach (KeyValuePair<int, StoneMarkerIntent> kvp in markers) {
            if (!kvp.Value.IsValid) {
                continue;
            }

            pendingStoneMarkers[kvp.Key] = kvp.Value;
            if (visibleStoneViews.TryGetValue(kvp.Key, out ChessStoneView stoneView) && stoneView != null) {
                stoneView.SetMarker(kvp.Value);
            }
        }
    }

    public void Destroy()
    {
        foreach (Dictionary<PlayerFlag, GameObject> viewsAtPos in stoneViews.Values) {
            foreach (GameObject go in viewsAtPos.Values) {
                if (go != null) {
                    GameObject.Destroy(go);
                }
            }
        }

        stoneViews.Clear();
        visibleStoneViews.Clear();
        visibleStoneFlags.Clear();
        pendingStoneMarkers.Clear();
        loadingViewKeys.Clear();
        isDestroyed = true;
    }

    private int GetValidPosIndex(RectCoordinates coords, PlayerFlag playerFlag)
    {
        if (compChessBoard == null || coords == null || !IsValidStoneFlag(playerFlag)) {
            return -1;
        }

        return compChessBoard.GetPosIndexByCoords(coords);
    }

    private static bool IsValidStoneFlag(PlayerFlag playerFlag)
    {
        return playerFlag == PlayerFlag.Player1 || playerFlag == PlayerFlag.Player2;
    }

    private bool TryGetStoneView(int posIndex, PlayerFlag playerFlag, out GameObject go)
    {
        go = null;
        return stoneViews.TryGetValue(posIndex, out Dictionary<PlayerFlag, GameObject> viewsAtPos) &&
            viewsAtPos.TryGetValue(playerFlag, out go) &&
            go != null;
    }

    private void SetStoneView(int posIndex, PlayerFlag playerFlag, GameObject go)
    {
        if (!stoneViews.TryGetValue(posIndex, out Dictionary<PlayerFlag, GameObject> viewsAtPos)) {
            viewsAtPos = new Dictionary<PlayerFlag, GameObject>();
            stoneViews[posIndex] = viewsAtPos;
        }

        viewsAtPos[playerFlag] = go;
    }

    private void HideOtherStoneAt(int posIndex, PlayerFlag visibleFlag)
    {
        if (!stoneViews.TryGetValue(posIndex, out Dictionary<PlayerFlag, GameObject> viewsAtPos)) {
            return;
        }

        foreach (KeyValuePair<PlayerFlag, GameObject> kvp in viewsAtPos) {
            if (kvp.Key != visibleFlag && kvp.Value != null) {
                EnsureStoneView(kvp.Value).Unbind();
                kvp.Value.SetActive(false);
            }
        }
    }

    private void HideStoneAt(int posIndex)
    {
        visibleStoneFlags.Remove(posIndex);
        visibleStoneViews.Remove(posIndex);
        if (!stoneViews.TryGetValue(posIndex, out Dictionary<PlayerFlag, GameObject> viewsAtPos)) {
            return;
        }

        foreach (GameObject go in viewsAtPos.Values) {
            if (go != null) {
                EnsureStoneView(go).Unbind();
                go.SetActive(false);
            }
        }
    }

    private static string GetViewKey(int posIndex, PlayerFlag playerFlag)
    {
        return $"{posIndex}_{(int)playerFlag}";
    }

    private void LoadStoneView(int posIndex, RectCoordinates coords, PlayerFlag playerFlag, string viewKey)
    {
        string gamePrefabTypeId = DuelUtils.GetGamePrefabTypeIdWithPlayerFlag(playerFlag);
        GamePrefabDataType gamePrefabCfg = GamePrefabDataType.GetConfigData(gamePrefabTypeId);
        if (gamePrefabCfg == null) {
            XNLogger.LogError("Chess stone prefab config not found.", ("playerFlag", playerFlag.ToString()));
            return;
        }

        loadingViewKeys.Add(viewKey);
        if (Global.Instance.resourceManager.LoadGamePrefabAsync(scene, gamePrefabCfg.resPath, (GameObject go) =>
        {
            loadingViewKeys.Remove(viewKey);
            if (go == null) {
                return;
            }

            if (isDestroyed) {
                GameObject.Destroy(go);
                return;
            }

            SetStoneView(posIndex, playerFlag, go);
            ApplyStoneTransform(go, coords);
            bool shouldShow = visibleStoneFlags.TryGetValue(posIndex, out PlayerFlag visibleFlag) && visibleFlag == playerFlag;
            go.SetActive(shouldShow);
            ChessStoneView stoneView = EnsureStoneView(go);
            stoneView.SetLatestMoveMarkerMaterials(latestMoveMarkerOnBlackStoneMaterial, latestMoveMarkerOnWhiteStoneMaterial);
            if (shouldShow) {
                stoneView.Bind(posIndex, playerFlag, true);
                visibleStoneViews[posIndex] = stoneView;
                ApplyPendingMarkerToView(posIndex, stoneView);
            } else {
                stoneView.Unbind();
            }
        }) == null) {
            loadingViewKeys.Remove(viewKey);
        }
    }

    private ChessStoneView EnsureStoneView(GameObject go)
    {
        ChessStoneView stoneView = go.GetComponent<ChessStoneView>();
        if (stoneView == null) {
            stoneView = go.AddComponent<ChessStoneView>();
        }

        stoneView.SetLatestMoveMarkerMaterials(latestMoveMarkerOnBlackStoneMaterial, latestMoveMarkerOnWhiteStoneMaterial);
        return stoneView;
    }

    private void ApplyPendingMarkerToView(int posIndex, ChessStoneView stoneView)
    {
        if (stoneView == null) {
            return;
        }

        if (pendingStoneMarkers.TryGetValue(posIndex, out StoneMarkerIntent marker)) {
            stoneView.SetMarker(marker);
        } else {
            stoneView.ClearMarker();
        }
    }

    private void ApplyStoneTransform(GameObject go, RectCoordinates coords)
    {
        if (go == null || compChessBoard?.chessBoardGrid == null || coords == null) {
            return;
        }

        Transform gridTransform = compChessBoard.chessBoardGrid.transform;
        Vector3 localChessPos = compChessBoard.chessBoardGrid.GetCellCenterLocalPosition(coords.x, coords.z);
        go.transform.position = gridTransform.TransformPoint(localChessPos);
    }
}
