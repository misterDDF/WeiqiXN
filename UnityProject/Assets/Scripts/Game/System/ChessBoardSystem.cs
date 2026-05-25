using System.Collections.Generic;
using Cinemachine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class ChessBoardSystem : SystemBase
{
    public override string systemName => GetSystemName<ChessBoardSystem>();
    public ChessBoardDataType chessBoardData;
    private const string BlackMaterialConfigId = "chess_board_black_material";
    private const string WhiteMaterialConfigId = "chess_board_white_material";
    private const string LatestMoveOnBlackStoneMaterialConfigId = "chess_board_latest_move_on_black_stone_material";
    private const string LatestMoveOnWhiteStoneMaterialConfigId = "chess_board_latest_move_on_white_stone_material";
    private const float DuelPerspectiveFov = 30f;
    private const float DuelPerspectiveTiltFactor = 0.16f;
    private const float DuelPerspectiveFramePaddingFactor = 1.08f;

    public ChessBoardSystem(SceneBase scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterSystemEvent<OnAddChessToBoard>(OnAddChessToBoard);
        scene.RegisterSystemEvent<OnApplyLanDuelMove>(OnApplyLanDuelMove);

        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard == null) return;

        if (scene.sceneCreateParams.saveFilePath == null) {
            if (scene.sceneCreateParams.duelSceneCreateParamas != null) {
                compChessBoard.boardCfgId.value = scene.sceneCreateParams.duelSceneCreateParamas.boardCfgId;
            } else {
                XNLogger.LogError("Scene create params for duel scene is empty, init scene with default values.");
                compChessBoard.boardCfgId.value = "9x9";
            }
        }
        chessBoardData = ChessBoardDataType.GetConfigData(compChessBoard.boardCfgId.value);
        if (chessBoardData != null) {
            compChessBoard.chessBoardGrid.InitGrid(chessBoardData.boardSize);
            ApplyChessBoardMaterials(compChessBoard.chessBoardGrid);
            Bounds gridBounds = compChessBoard.chessBoardGrid.GetGridBounds();
            InitDuelVCam(gridBounds);
        } else {
            XNLogger.LogError("Chess board config not found!", ("chessBoardCfgId", compChessBoard.boardCfgId.value));
        }

        if (scene.sceneCreateParams.saveFilePath != null) {
            RestoreBoardFromKataGoRecord(compChessBoard);
        }
    }

    public void OnAddChessToBoard(OnAddChessToBoard evt)
    {
        if (evt == null) {
            return;
        }

        TryApplyLocalDuelMove(evt.coords, out _);
    }

    public bool TryApplyLocalDuelMove(RectCoordinates coords, out DuelMoveRejectReason rejectReason)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        rejectReason = DuelMoveRejectReason.None;
        if (compDuel == null || compChessBoard == null) {
            rejectReason = DuelMoveRejectReason.InvalidBoard;
            return false;
        }

        if (compDuel.isLanDuel.value) {
            rejectReason = DuelMoveRejectReason.InvalidCommand;
            return false;
        }

        if (compDuel.isScoring) {
            rejectReason = DuelMoveRejectReason.InvalidBoard;
            return false;
        }

        var curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            rejectReason = DuelMoveRejectReason.InvalidCommand;
            return false;
        }

        PlayerFlag playerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        DuelMoveResult moveResult = DuelMoveRule.BuildMoveResult(
            compChessBoard,
            new DuelMoveCommand(playerFlag, coords, chessGuid)
        );
        if (!moveResult.accepted) {
            rejectReason = moveResult.rejectReason;
            scene.EmitSystemEvent(new OnDuelMoveRejected(playerFlag, coords?.Clone(), moveResult.rejectReason));
            return false;
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        ApplyMoveStoneViews(compChessBoard, moveResult, playerFlag, coords);
        DrawLatestMoveMarker(compChessBoard, playerFlag, coords);
        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
        compDuel.AppendKataGoMove(playerFlag, coords, boardSize);
        scene.EmitSystemEvent(new OnAfterAddChessToBoard(playerFlag, coords.Clone()));
        return true;
    }

    public void OnApplyLanDuelMove(OnApplyLanDuelMove evt)
    {
        ApplyLanDuelMove(evt.move);
    }

    public bool TryAcceptLanDuelMove(LanDuelMoveMessage move, out LanDuelMoveMessage acceptedMove, out DuelMoveRejectReason rejectReason)
    {
        acceptedMove = move;
        if (!ApplyLanDuelMove(move, true, out rejectReason)) {
            return false;
        }

        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        int boardVersion = compDuel != null ? compDuel.lanBoardVersion.value : move.boardVersion;
        acceptedMove = new LanDuelMoveMessage(move.moveId, boardVersion, move.playerFlag, move.coords?.Clone());
        return true;
    }

    private bool ApplyLanDuelMove(LanDuelMoveMessage move)
    {
        return ApplyLanDuelMove(move, false, out _);
    }

    private bool ApplyLanDuelMove(
        LanDuelMoveMessage move,
        bool isHostAuthority,
        out DuelMoveRejectReason rejectReason,
        bool emitRejectEvent = true,
        bool emitAcceptedEvent = true)
    {
        rejectReason = DuelMoveRejectReason.None;
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null || move.coords == null || compDuel.isScoring) {
            rejectReason = DuelMoveRejectReason.InvalidBoard;
            return false;
        }

        var curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            rejectReason = DuelMoveRejectReason.InvalidCommand;
            return false;
        }

        PlayerFlag curPlayerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        if (curPlayerFlag != move.playerFlag) {
            rejectReason = DuelMoveRejectReason.NotPlayerTurn;
            if (emitRejectEvent) {
                scene.EmitSystemEvent(new OnDuelMoveRejected(move.playerFlag, move.coords.Clone(), rejectReason));
            }
            return false;
        }

        if (isHostAuthority && move.boardVersion != compDuel.lanBoardVersion.value) {
            rejectReason = DuelMoveRejectReason.BoardVersionMismatch;
            if (emitRejectEvent) {
                scene.EmitSystemEvent(new OnDuelMoveRejected(move.playerFlag, move.coords.Clone(), rejectReason));
            }
            return false;
        }

        if (!isHostAuthority && move.boardVersion > 0 && move.boardVersion != compDuel.lanBoardVersion.value + 1) {
            return true;
        }

        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        DuelMoveResult moveResult = DuelMoveRule.BuildMoveResult(
            compChessBoard,
            new DuelMoveCommand(move.playerFlag, move.coords, chessGuid)
        );
        if (!moveResult.accepted) {
            rejectReason = moveResult.rejectReason;
            if (emitRejectEvent) {
                scene.EmitSystemEvent(new OnDuelMoveRejected(move.playerFlag, move.coords.Clone(), moveResult.rejectReason));
            }
            return false;
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        ApplyMoveStoneViews(compChessBoard, moveResult, move.playerFlag, move.coords);
        DrawLatestMoveMarker(compChessBoard, move.playerFlag, move.coords);
        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
        compDuel.AppendKataGoMove(move.playerFlag, move.coords, boardSize);
        if (move.boardVersion > 0) {
            compDuel.lanBoardVersion.value = move.boardVersion;
        } else {
            compDuel.lanBoardVersion.value += 1;
        }
        if (emitAcceptedEvent) {
            scene.EmitSystemEvent(new OnAfterAddChessToBoard(move.playerFlag, move.coords.Clone()));
        }
        return true;
    }

    public bool TryBuildLanBoardSnapshot(LanDuelMoveMessage latestMove, out LanDuelBoardSnapshotMessage snapshot)
    {
        PlayerFlag nextTurnPlayerFlag = latestMove.playerFlag.GetOpponentPlayerFlag();
        return TryBuildLanBoardSnapshot(
            nextTurnPlayerFlag,
            latestMove.coords?.Clone(),
            latestMove.playerFlag,
            out snapshot);
    }

    public bool TryBuildLanBoardSnapshot(out LanDuelBoardSnapshotMessage snapshot)
    {
        snapshot = default;
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard?.chessBoardGrid == null) {
            return false;
        }

        TryResolveLatestMoveMarker(compDuel, compChessBoard.chessBoardGrid.gridSize, out RectCoordinates latestMoveCoords, out PlayerFlag latestMovePlayerFlag);
        return TryBuildLanBoardSnapshot(
            ResolveCurrentTurnPlayerFlag(compDuel),
            latestMoveCoords,
            latestMovePlayerFlag,
            out snapshot);
    }

    private bool TryBuildLanBoardSnapshot(
        PlayerFlag nextTurnPlayerFlag,
        RectCoordinates latestMoveCoords,
        PlayerFlag latestMovePlayerFlag,
        out LanDuelBoardSnapshotMessage snapshot)
    {
        snapshot = default;
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null || compChessBoard.chessBoardGrid == null) {
            return false;
        }

        List<LanDuelBoardSnapshotStone> stones = new List<LanDuelBoardSnapshotStone>();
        foreach (var kvp in compChessBoard.chessInfoDict) {
            if (!int.TryParse(kvp.Key, out int posIndex) || kvp.Value == null) {
                continue;
            }

            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(posIndex);
            if (coords.x < 0 || coords.z < 0) {
                continue;
            }

            stones.Add(new LanDuelBoardSnapshotStone(coords, (PlayerFlag)kvp.Value.chessFlag.value));
        }

        snapshot = new LanDuelBoardSnapshotMessage(
            compDuel.lanBoardVersion.value,
            compChessBoard.chessBoardGrid.gridSize,
            nextTurnPlayerFlag,
            latestMoveCoords?.Clone(),
            latestMovePlayerFlag,
            stones,
            DuelMoveHistory.Clone(compDuel.kataGoMoves));
        return true;
    }

    public void ApplyLanBoardSnapshot(LanDuelBoardSnapshotMessage snapshot)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null || compChessBoard.chessBoardGrid == null) {
            return;
        }

        int previousBoardVersion = compDuel.lanBoardVersion.value;
        if (snapshot.boardSize != compChessBoard.chessBoardGrid.gridSize) {
            XNLogger.LogWarn("LAN board snapshot skipped, board size mismatch.",
                ("snapshotBoardSize", snapshot.boardSize.ToString()),
                ("localBoardSize", compChessBoard.chessBoardGrid.gridSize.ToString()));
            return;
        }

        if (snapshot.boardVersion < previousBoardVersion) {
            XNLogger.LogWarn("LAN board snapshot skipped, snapshot version is older than local board.",
                ("snapshotBoardVersion", snapshot.boardVersion.ToString()),
                ("localBoardVersion", previousBoardVersion.ToString()));
            return;
        }

        if (snapshot.boardVersion == previousBoardVersion && IsLocalBoardSameAsSnapshot(compChessBoard, snapshot)) {
            ApplyLanBoardSnapshotMetadata(compDuel, compChessBoard, snapshot, previousBoardVersion, false, true);
            return;
        }

        if (snapshot.boardVersion == previousBoardVersion + 1 && TryApplySnapshotAsSingleMove(snapshot)) {
            if (IsLocalBoardSameAsSnapshot(compChessBoard, snapshot)) {
                ApplyLanBoardSnapshotMetadata(compDuel, compChessBoard, snapshot, previousBoardVersion, true, false);
                return;
            }
        }

        RebuildLanBoardFromSnapshot(compDuel, compChessBoard, snapshot, previousBoardVersion);
    }

    private bool TryApplySnapshotAsSingleMove(LanDuelBoardSnapshotMessage snapshot)
    {
        if (snapshot.latestMoveCoords == null || snapshot.latestMovePlayerFlag == 0) {
            return false;
        }

        LanDuelMoveMessage move = new LanDuelMoveMessage(
            0,
            snapshot.boardVersion,
            snapshot.latestMovePlayerFlag,
            snapshot.latestMoveCoords.Clone());
        return ApplyLanDuelMove(move, false, out _, false, false);
    }

    private bool IsLocalBoardSameAsSnapshot(SceneComponentChessBoard compChessBoard, LanDuelBoardSnapshotMessage snapshot)
    {
        if (compChessBoard == null || compChessBoard.chessInfoDict == null || compChessBoard.chessBoardGrid == null) {
            return false;
        }

        Dictionary<int, PlayerFlag> snapshotStoneFlags = new Dictionary<int, PlayerFlag>();
        if (snapshot.stones != null) {
            foreach (LanDuelBoardSnapshotStone stone in snapshot.stones) {
                if (stone.coords == null || stone.playerFlag == 0) {
                    return false;
                }

                int posIndex = compChessBoard.GetPosIndexByCoords(stone.coords);
                if (posIndex < 0 || snapshotStoneFlags.ContainsKey(posIndex)) {
                    return false;
                }

                snapshotStoneFlags[posIndex] = stone.playerFlag;
            }
        }

        int localStoneCount = 0;
        foreach (var kvp in compChessBoard.chessInfoDict) {
            if (!int.TryParse(kvp.Key, out int posIndex) || kvp.Value == null || kvp.Value.chessFlag.value == 0) {
                return false;
            }

            if (!snapshotStoneFlags.TryGetValue(posIndex, out PlayerFlag snapshotPlayerFlag) ||
                snapshotPlayerFlag != (PlayerFlag)kvp.Value.chessFlag.value) {
                return false;
            }

            localStoneCount += 1;
        }

        return localStoneCount == snapshotStoneFlags.Count;
    }

    private void RebuildLanBoardFromSnapshot(
        SceneComponentDuel compDuel,
        SceneComponentChessBoard compChessBoard,
        LanDuelBoardSnapshotMessage snapshot,
        int previousBoardVersion)
    {
        compChessBoard.chessInfoDict.Clear();
        compChessBoard.lastChessInfoDict.Clear();
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();
        ClearOwnershipState(compDuel, compChessBoard);

        if (snapshot.stones != null) {
            foreach (LanDuelBoardSnapshotStone stone in snapshot.stones) {
                if (stone.coords == null || compChessBoard.GetPosIndexByCoords(stone.coords) < 0 || stone.playerFlag == 0) {
                    continue;
                }

                string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
                ChessInfo chessInfo = new ChessInfo();
                chessInfo.chessGuid.value = chessGuid;
                chessInfo.chessFlag.value = (int)stone.playerFlag;
                compChessBoard.chessInfoDict.SetValue(compChessBoard.GetPosIndexByCoords(stone.coords).ToString(), chessInfo);
            }
        }

        compChessBoard.lastChessInfoDict = compChessBoard.CreateCacheChessInfoDict();
        compChessBoard.GetStoneViewCache().SyncFromChessInfoDict();
        ApplyLanBoardSnapshotMetadata(
            compDuel,
            compChessBoard,
            snapshot,
            previousBoardVersion,
            snapshot.boardVersion > previousBoardVersion,
            true);
    }

    private void ApplyLanBoardSnapshotMetadata(
        SceneComponentDuel compDuel,
        SceneComponentChessBoard compChessBoard,
        LanDuelBoardSnapshotMessage snapshot,
        int previousBoardVersion,
        bool emitAcceptedMoveEvent,
        bool appendFallbackMove)
    {
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();
        if (snapshot.latestMoveCoords != null && snapshot.latestMovePlayerFlag != 0) {
            DrawLatestMoveMarker(compChessBoard, snapshot.latestMovePlayerFlag, snapshot.latestMoveCoords);
        }

        compDuel.lanBoardVersion.value = snapshot.boardVersion;
        if (snapshot.hasKataGoMoves) {
            compDuel.kataGoMoves = DuelMoveHistory.Clone(snapshot.kataGoMoves);
        } else if (appendFallbackMove &&
            snapshot.boardVersion > previousBoardVersion &&
            snapshot.latestMoveCoords != null &&
            snapshot.latestMovePlayerFlag != 0) {
            compDuel.AppendKataGoMove(snapshot.latestMovePlayerFlag, snapshot.latestMoveCoords, compChessBoard.chessBoardGrid.gridSize);
        }
        if (snapshot.nextTurnPlayerFlag == PlayerFlag.Player1) {
            compDuel.curTurnPlayerGuid.value = compDuel.player1Guid.value;
        } else if (snapshot.nextTurnPlayerFlag == PlayerFlag.Player2) {
            compDuel.curTurnPlayerGuid.value = compDuel.player2Guid.value;
        }

        if (emitAcceptedMoveEvent &&
            snapshot.latestMoveCoords != null &&
            snapshot.latestMovePlayerFlag != 0) {
            scene.EmitSystemEvent(new OnAfterAddChessToBoard(snapshot.latestMovePlayerFlag, snapshot.latestMoveCoords.Clone()));
        }
    }

    private void ClearOwnershipState(SceneComponentDuel compDuel, SceneComponentChessBoard compChessBoard)
    {
        compDuel?.ClearOwnershipScoreCache();
        compChessBoard?.chessBoardGrid?.ClearOwnership();
        scene.EmitSystemEvent(new OnClearDuelOwnership());
    }

    private PlayerFlag ResolveCurrentTurnPlayerFlag(SceneComponentDuel compDuel)
    {
        if (compDuel == null || string.IsNullOrEmpty(compDuel.curTurnPlayerGuid.value)) {
            return 0;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        return curPlayer != null ? (PlayerFlag)curPlayer.playerFlag.value : 0;
    }

    private bool TryResolveLatestMoveMarker(SceneComponentDuel compDuel, int boardSize, out RectCoordinates coords, out PlayerFlag playerFlag)
    {
        coords = null;
        playerFlag = 0;
        if (compDuel?.kataGoMoves == null) {
            return false;
        }

        for (int i = compDuel.kataGoMoves.Count - 1; i >= 0; i--) {
            JToken move = compDuel.kataGoMoves[i];
            if (!KataGoDuelRecordFile.TryParseMove(move, out PlayerFlag parsedFlag, out RectCoordinates parsedCoords, out bool isPass, boardSize)) {
                continue;
            }

            if (isPass) {
                continue;
            }

            coords = parsedCoords?.Clone();
            playerFlag = parsedFlag;
            return coords != null && playerFlag != 0;
        }

        return false;
    }

    private void DrawLatestMoveMarker(SceneComponentChessBoard compChessBoard, PlayerFlag playerFlag, RectCoordinates coords)
    {
        if (compChessBoard?.chessBoardGrid == null || coords == null) {
            return;
        }

        try {
            compChessBoard.chessBoardGrid.DrawLatestMoveMarker(coords.x, coords.z, playerFlag == PlayerFlag.Player1);
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Latest move marker draw failed.", ("err", ex.Message));
        }
    }

    private void ApplyChessBoardMaterials(RectGrid rectGrid)
    {
        if (rectGrid == null) {
            return;
        }

        Material blackMaterial = LoadRuntimeMaterial(BlackMaterialConfigId);
        Material whiteMaterial = LoadRuntimeMaterial(WhiteMaterialConfigId);
        Material latestMoveOnBlackStoneMaterial = LoadRuntimeMaterial(LatestMoveOnBlackStoneMaterialConfigId);
        Material latestMoveOnWhiteStoneMaterial = LoadRuntimeMaterial(LatestMoveOnWhiteStoneMaterialConfigId);
        rectGrid.SetBoardMaterials(blackMaterial, whiteMaterial);
        rectGrid.SetLatestMoveMarkerMaterials(latestMoveOnBlackStoneMaterial, latestMoveOnWhiteStoneMaterial);
    }

    private Material LoadRuntimeMaterial(string configId)
    {
        RuntimeAssetDataType assetData = RuntimeAssetDataType.GetConfigData(configId);
        if (assetData == null) {
            XNLogger.LogError("Runtime material config not found.", ("configId", configId));
            return null;
        }
        if (assetData.assetType != typeof(Material).Name || string.IsNullOrEmpty(assetData.resPath)) {
            XNLogger.LogError(
                "Runtime material config invalid.",
                ("configId", configId),
                ("assetType", assetData.assetType),
                ("resPath", assetData.resPath));
            return null;
        }

        Material material = Global.Instance.resourceManager.LoadAsset<Material>(assetData.resPath);
        if (material == null) {
            XNLogger.LogError("Runtime material asset load failed.", ("configId", configId), ("resPath", assetData.resPath));
            return null;
        }

        return material;
    }

    private void InitDuelVCam(Bounds gridBound)
    {
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard.duelVCam == null) {
            XNLogger.LogError("Duel virtual camera not found, init camera failed.");
            return;
        }

        Transform duelVCamTransform = compChessBoard.duelVCam.transform;
        Vector3 viewDir = new Vector3(0f, -1f, -DuelPerspectiveTiltFactor).normalized;
        duelVCamTransform.rotation = Quaternion.LookRotation(viewDir, Vector3.forward);

        float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;

        float extraYOffset = 0;
        if (chessBoardData != null) {
            extraYOffset = chessBoardData.vcamYOffset;
        }

        LensSettings lens = compChessBoard.duelVCam.m_Lens;
        float halfHeightByBoard = Mathf.Max(gridBound.extents.z, aspect > 0f ? gridBound.extents.x / aspect : gridBound.extents.z);
        lens.FieldOfView = DuelPerspectiveFov;
        lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        compChessBoard.duelVCam.m_Lens = lens;

        float halfVerticalFovRad = lens.FieldOfView * 0.5f * Mathf.Deg2Rad;
        float boardDistance = halfHeightByBoard / Mathf.Tan(halfVerticalFovRad);
        float cameraDistance = boardDistance * DuelPerspectiveFramePaddingFactor + Mathf.Max(extraYOffset, 0f);
        duelVCamTransform.position = gridBound.center - viewDir * cameraDistance;
    }

    private void RestoreBoardFromKataGoRecord(SceneComponentChessBoard compChessBoard)
    {
        string recordFilePath = GameSaveConfig.GetDuelRecordSavePath(0);
        if (!KataGoDuelRecordFile.TryLoad(recordFilePath, out var recordJson) ||
            !KataGoDuelRecordFile.TryGetMoves(recordJson, out var moves)) {
            XNLogger.LogError("Restore board from KataGo record failed.", ("recordFilePath", recordFilePath));
            return;
        }

        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            XNLogger.LogError("Restore board from KataGo record failed, duel component not found.");
            return;
        }

        compChessBoard.chessInfoDict.Clear();
        compChessBoard.lastChessInfoDict.Clear();
        compDuel.ResetKataGoMoves();

        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
        if (!KataGoDuelRecordFile.TryGetBoardSize(recordJson, out int recordBoardSize)) {
            XNLogger.LogError("KataGo duel record board size invalid, restore skipped.", ("recordFilePath", recordFilePath));
            return;
        }

        if (recordBoardSize != boardSize) {
            XNLogger.LogError(
                "KataGo duel record board size mismatch, restore skipped.",
                ("recordBoardSize", recordBoardSize.ToString()),
                ("boardSize", boardSize.ToString()));
            return;
        }

        RectCoordinates latestMoveCoords = null;
        PlayerFlag latestMovePlayerFlag = 0;
        if (KataGoDuelRecordFile.TryGetInitialStones(recordJson, out JArray initialStones)) {
            foreach (JToken stone in initialStones) {
                if (!KataGoDuelRecordFile.TryParseMove(stone, out PlayerFlag playerFlag, out RectCoordinates coords, out bool isPass, boardSize) ||
                    isPass ||
                    !ApplyRecordInitialStone(compChessBoard, playerFlag, coords)) {
                    XNLogger.LogError("Invalid initial stone in KataGo duel record, restore stopped.", ("stone", stone.ToString()));
                    compChessBoard.chessInfoDict.Clear();
                    compChessBoard.lastChessInfoDict.Clear();
                    compDuel.ResetKataGoMoves();
                    return;
                }
            }
        }

        foreach (var move in moves) {
            if (!KataGoDuelRecordFile.TryParseMove(move, out PlayerFlag playerFlag, out RectCoordinates coords, out bool isPass, boardSize)) {
                XNLogger.LogError("Invalid move in KataGo duel record, restore stopped.", ("move", move.ToString()));
                compChessBoard.chessInfoDict.Clear();
                compChessBoard.lastChessInfoDict.Clear();
                compDuel.ResetKataGoMoves();
                return;
            }

            if (isPass) {
                compDuel.AppendKataGoPass(playerFlag);
                continue;
            }

            if (!ApplyRecordMove(compChessBoard, compDuel, playerFlag, coords, boardSize)) {
                XNLogger.LogError("Invalid move in KataGo duel record, restore stopped.", ("move", move.ToString()));
                compChessBoard.chessInfoDict.Clear();
                compChessBoard.lastChessInfoDict.Clear();
                compDuel.ResetKataGoMoves();
                return;
            }

            latestMoveCoords = coords.Clone();
            latestMovePlayerFlag = playerFlag;
        }

        compChessBoard.GetStoneViewCache().SyncFromChessInfoDict();

        if (latestMoveCoords != null) {
            DrawLatestMoveMarker(compChessBoard, latestMovePlayerFlag, latestMoveCoords);
        }
    }

    private bool ApplyRecordInitialStone(SceneComponentChessBoard compChessBoard, PlayerFlag playerFlag, RectCoordinates coords)
    {
        if (compChessBoard == null || coords == null || playerFlag == 0) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            return false;
        }

        ChessInfo chessInfo = new ChessInfo();
        chessInfo.chessGuid.value = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        chessInfo.chessFlag.value = (int)playerFlag;
        compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), chessInfo);
        return true;
    }

    private bool ApplyRecordMove(SceneComponentChessBoard compChessBoard, SceneComponentDuel compDuel, PlayerFlag playerFlag, RectCoordinates coords, int boardSize)
    {
        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        DuelMoveResult moveResult = DuelMoveRule.BuildMoveResult(
            compChessBoard,
            new DuelMoveCommand(playerFlag, coords, chessGuid)
        );
        if (!moveResult.accepted) {
            XNLogger.LogError("Invalid record move position, restore move skipped.", ("coords", coords?.ToString() ?? "null"));
            return false;
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        compDuel.AppendKataGoMove(playerFlag, coords, boardSize);
        return true;
    }

    private void ApplyMoveStoneViews(SceneComponentChessBoard compChessBoard, DuelMoveResult moveResult, PlayerFlag playerFlag, RectCoordinates coords)
    {
        if (compChessBoard == null || moveResult == null) {
            return;
        }

        ChessStoneViewCache stoneViewCache = compChessBoard.GetStoneViewCache();
        foreach (int removePosIndex in moveResult.pendingRemovePosIndexes) {
            RectCoordinates removeCoords = compChessBoard.GetCoordsByPosIndex(removePosIndex);
            stoneViewCache.HideStone(removeCoords);
        }

        stoneViewCache.ShowStone(coords, playerFlag);
    }
}
