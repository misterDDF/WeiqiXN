using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class ChessBoardSystem : SystemBase
{
    public override string systemName => GetSystemName<ChessBoardSystem>();
    public ChessBoardDataType chessBoardData;

    public ChessBoardSystem(SceneBase scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterEntityEvent<Chess, OnEntityCreated>(OnChessCreated);
        scene.RegisterSystemEvent<OnAddChessToBoard>(OnAddChessToBoard);

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

        var curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            return;
        }

        PlayerFlag playerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        if (!DuelMoveRule.TryApplyMove(
            compChessBoard,
            playerFlag,
            evt.coords,
            chessGuid,
            out SavableObjectDict<ChessInfo> cachedChessInfoDict,
            out List<int> pendingRemovePosIndexes
        )) {
            return;
        }

        foreach (var removePosIndex in pendingRemovePosIndexes) {
            if (cachedChessInfoDict.TryGetValue(removePosIndex.ToString(), out var chessInfo)) {
                var chess = scene.GetEntity<Chess>(chessInfo.chessGuid.value);
                if (chess != null) {
                    chess.Destroy();
                }
            }
        }

        compChessBoard.lastChessInfoDict = cachedChessInfoDict;
        EntityUtils.CreateChess(scene, chessGuid, playerFlag, evt.coords);
        DrawLatestMoveMarker(compChessBoard, playerFlag, evt.coords);
        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : chessBoardData?.boardSize ?? 19;
        compDuel.AppendKataGoMove(playerFlag, evt.coords, boardSize);
        scene.EmitSystemEvent(new OnAfterAddChessToBoard(playerFlag, evt.coords.Clone()));
    }

    private void DrawLatestMoveMarker(SceneComponentChessBoard compChessBoard, PlayerFlag playerFlag, RectCoordinates coords)
    {
        if (compChessBoard?.chessBoardGrid == null || coords == null) {
            return;
        }

        compChessBoard.chessBoardGrid.DrawLatestMoveMarker(coords.x, coords.z, playerFlag == PlayerFlag.Player1);
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
        if (!DuelMoveRule.TryApplyMove(compChessBoard, playerFlag, coords, chessGuid, out var cachedChessInfoDict, out _)) {
            XNLogger.LogError("Invalid record move position, restore move skipped.", ("coords", coords?.ToString() ?? "null"));
            return false;
        }

        compChessBoard.lastChessInfoDict = cachedChessInfoDict;
        compDuel.AppendKataGoMove(playerFlag, coords, boardSize);
        return true;
    }
}
