using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class ChessBoardSystem : SystemBase
{
    public override string systemName => GetSystemName<ChessBoardSystem>();
    public ChessBoardDataType chessBoardData;
    private const string BlackMaterialConfigId = "chess_board_black_material";
    private const string WhiteMaterialConfigId = "chess_board_white_material";

    public ChessBoardSystem(SceneBase scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterEntityEvent<Chess, OnEntityCreated>(OnChessCreated);
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

    public void OnChessCreated(Chess chess, OnEntityCreated evt)
    {
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard != null) {
            int posIndex = compChessBoard.GetPosIndexByCoords(chess.coords);
            if (posIndex >= 0) {
                if (compChessBoard.chessInfoDict.TryGetValue(posIndex.ToString(), out var chessInfo) &&
                    chessInfo.chessGuid.value == chess.guid &&
                    chessInfo.chessFlag.value == (int)chess.playerFlag
                ) {
                    Transform gridTransform = compChessBoard.chessBoardGrid.transform;
                    Vector3 localChessPos = compChessBoard.chessBoardGrid.GetCellCenterLocalPosition(chess.coords.x, chess.coords.z);
                    chess.transform.position = gridTransform.TransformPoint(localChessPos);
                } else {
                    chess.Destroy();
                }
            }
        }
    }

    public void OnAddChessToBoard(OnAddChessToBoard evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null) {
            return;
        }

        if (compDuel.isLanDuel.value) {
            return;
        }

        if (compDuel.isScoring) {
            return;
        }

        var curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            return;
        }

        PlayerFlag playerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        DuelMoveResult moveResult = DuelMoveRule.BuildMoveResult(
            compChessBoard,
            new DuelMoveCommand(playerFlag, evt.coords, chessGuid)
        );
        if (!moveResult.accepted) {
            scene.EmitSystemEvent(new OnDuelMoveRejected(playerFlag, evt.coords?.Clone(), moveResult.rejectReason));
            return;
        }

        foreach (var removePosIndex in moveResult.pendingRemovePosIndexes) {
            if (moveResult.previousChessInfoDict.TryGetValue(removePosIndex.ToString(), out var chessInfo)) {
                var chess = scene.GetEntity<Chess>(chessInfo.chessGuid.value);
                if (chess != null) {
                    chess.Destroy();
                }
            }
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        EntityUtils.CreateChess(scene, chessGuid, playerFlag, evt.coords);
        DrawLatestMoveMarker(compChessBoard, playerFlag, evt.coords);
        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
        compDuel.AppendKataGoMove(playerFlag, evt.coords, boardSize);
        scene.EmitSystemEvent(new OnAfterAddChessToBoard(playerFlag, evt.coords.Clone()));
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

    private bool ApplyLanDuelMove(LanDuelMoveMessage move, bool isHostAuthority, out DuelMoveRejectReason rejectReason)
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
            scene.EmitSystemEvent(new OnDuelMoveRejected(move.playerFlag, move.coords.Clone(), rejectReason));
            return false;
        }

        if (isHostAuthority && move.boardVersion != compDuel.lanBoardVersion.value) {
            rejectReason = DuelMoveRejectReason.BoardVersionMismatch;
            scene.EmitSystemEvent(new OnDuelMoveRejected(move.playerFlag, move.coords.Clone(), rejectReason));
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
            scene.EmitSystemEvent(new OnDuelMoveRejected(move.playerFlag, move.coords.Clone(), moveResult.rejectReason));
            return false;
        }

        foreach (var removePosIndex in moveResult.pendingRemovePosIndexes) {
            if (moveResult.previousChessInfoDict.TryGetValue(removePosIndex.ToString(), out var chessInfo)) {
                var chess = scene.GetEntity<Chess>(chessInfo.chessGuid.value);
                if (chess != null) {
                    chess.Destroy();
                }
            }
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        EntityUtils.CreateChess(scene, chessGuid, move.playerFlag, move.coords);
        DrawLatestMoveMarker(compChessBoard, move.playerFlag, move.coords);
        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
        compDuel.AppendKataGoMove(move.playerFlag, move.coords, boardSize);
        if (move.boardVersion > 0) {
            compDuel.lanBoardVersion.value = move.boardVersion;
        } else {
            compDuel.lanBoardVersion.value += 1;
        }
        scene.EmitSystemEvent(new OnAfterAddChessToBoard(move.playerFlag, move.coords.Clone()));
        return true;
    }

    public bool TryBuildLanBoardSnapshot(LanDuelMoveMessage latestMove, out LanDuelBoardSnapshotMessage snapshot)
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

        PlayerFlag nextTurnPlayerFlag = latestMove.playerFlag.GetOpponentPlayerFlag();
        snapshot = new LanDuelBoardSnapshotMessage(
            compDuel.lanBoardVersion.value,
            compChessBoard.chessBoardGrid.gridSize,
            nextTurnPlayerFlag,
            latestMove.coords?.Clone(),
            latestMove.playerFlag,
            stones);
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

        ClearChessEntities();
        compChessBoard.chessInfoDict.Clear();
        compChessBoard.lastChessInfoDict.Clear();
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();

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
                EntityUtils.CreateChess(scene, chessGuid, stone.playerFlag, stone.coords);
            }
        }

        compChessBoard.lastChessInfoDict = compChessBoard.CreateCacheChessInfoDict();

        if (snapshot.latestMoveCoords != null && snapshot.latestMovePlayerFlag != 0) {
            DrawLatestMoveMarker(compChessBoard, snapshot.latestMovePlayerFlag, snapshot.latestMoveCoords);
        }

        compDuel.lanBoardVersion.value = snapshot.boardVersion;
        if (snapshot.boardVersion > previousBoardVersion &&
            snapshot.latestMoveCoords != null &&
            snapshot.latestMovePlayerFlag != 0) {
            compDuel.AppendKataGoMove(snapshot.latestMovePlayerFlag, snapshot.latestMoveCoords, compChessBoard.chessBoardGrid.gridSize);
            scene.EmitSystemEvent(new OnAfterAddChessToBoard(snapshot.latestMovePlayerFlag, snapshot.latestMoveCoords.Clone()));
            return;
        }

        if (snapshot.nextTurnPlayerFlag == PlayerFlag.Player1) {
            compDuel.curTurnPlayerGuid.value = compDuel.player1Guid.value;
        } else if (snapshot.nextTurnPlayerFlag == PlayerFlag.Player2) {
            compDuel.curTurnPlayerGuid.value = compDuel.player2Guid.value;
        }
    }

    private void ClearChessEntities()
    {
        string chessEntityType = EntityBase.GetEntityType<Chess>();
        if (!scene.entityTypeDict.TryGetValue(chessEntityType, out HashSet<EntityBase> chessEntities)) {
            return;
        }

        foreach (EntityBase chessEntity in chessEntities.ToList()) {
            chessEntity.Destroy();
        }
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
        rectGrid.SetBoardMaterials(blackMaterial, whiteMaterial);
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
        duelVCamTransform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;

        float extraYOffset = 0;
        if (chessBoardData != null) {
            extraYOffset = chessBoardData.vcamYOffset;
        }

        LensSettings lens = compChessBoard.duelVCam.m_Lens;
        float halfVerticalFovRad = lens.FieldOfView * 0.5f * Mathf.Deg2Rad;
        float halfHeightByBoard = Mathf.Max(gridBound.extents.z, aspect > 0f ? gridBound.extents.x / aspect : gridBound.extents.z);
        float halfHeightPadding = Mathf.Tan(halfVerticalFovRad) * extraYOffset;
        lens.OrthographicSize = halfHeightByBoard + Mathf.Max(halfHeightPadding, 0f);
        lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
        compChessBoard.duelVCam.m_Lens = lens;

        float cameraYOffset = Mathf.Max(gridBound.size.x, gridBound.size.z) + extraYOffset;
        duelVCamTransform.position = gridBound.center + Vector3.up * cameraYOffset;
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

        foreach (var kvp in compChessBoard.chessInfoDict) {
            if (!int.TryParse(kvp.Key, out int posIndex)) {
                continue;
            }

            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(posIndex);
            ChessInfo chessInfo = kvp.Value;
            if (coords.x >= 0 && coords.z >= 0 && chessInfo != null) {
                EntityUtils.CreateChess(scene, chessInfo.chessGuid.value, (PlayerFlag)chessInfo.chessFlag.value, coords);
            }
        }

        if (latestMoveCoords != null) {
            DrawLatestMoveMarker(compChessBoard, latestMovePlayerFlag, latestMoveCoords);
        }
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
}
