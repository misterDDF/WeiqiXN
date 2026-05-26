using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using XNClient.Logger;
using XNClient.ChessBoard;

public class ReplayScene : SceneBase
{
    public SceneComponentChessBoard compChessBoard;
    public SceneComponentDuel compDuel;
    private readonly List<ReplayMoveState> replayMoves = new List<ReplayMoveState>();
    private readonly List<ReplayMoveState> replayInitialStones = new List<ReplayMoveState>();
    private int replayBoardSize;
    private int replayCursorMoveIndex;
    private bool isReplayLoaded;
    private string replayStatus;

    public ReplayScene(SceneDataType configData, SceneCreateParams sceneCreateParams) : base(configData, sceneCreateParams)
    {
        compChessBoard = new SceneComponentChessBoard(this);
        AddComponent(compChessBoard);
        compDuel = new SceneComponentDuel(this);
        AddComponent(compDuel);
    }

    public override void OnSceneLoaded()
    {
        base.OnSceneLoaded();
        Global.RequestKeepAwake(Global.KeepAwakeReason.Duel);

        foreach (var rootObj in unityScene.GetRootGameObjects()) {
            DuelSceneFixedRef fixedRef = rootObj.GetComponent<DuelSceneFixedRef>();
            if (fixedRef != null) {
                if (compChessBoard != null) {
                    compChessBoard.chessBoardGrid = fixedRef.chessBoardGrid;
                    compChessBoard.duelVCam = fixedRef.duelVCam;
                }
                break;
            }
        }

        string gameId = sceneCreateParams != null ? sceneCreateParams.replayGameId : string.Empty;
        string recordFilePath = string.Empty;
        if (string.IsNullOrEmpty(gameId)) {
            XNLogger.LogError("Replay scene load failed, replay game id is empty.");
            replayStatus = "复盘记录无效";
        } else {
            recordFilePath = GameSaveConfig.GetReplayDuelRecordPath(gameId);
            if (KataGoDuelRecordFile.TryLoad(recordFilePath, out JObject recordJson) &&
                KataGoDuelRecordFile.TryGetBoardSize(recordJson, out int boardSize)) {
                replayBoardSize = boardSize;
                compChessBoard.boardCfgId.value = $"{boardSize}x{boardSize}";
                if (TryLoadReplayRecord(recordJson)) {
                    isReplayLoaded = true;
                }
            } else {
                replayStatus = "复盘记录读取失败";
            }
        }

        AddSystem(new ChessBoardSystem(this));

        if (isReplayLoaded) {
            ApplyReplayCursor(replayMoves.Count);
            ChessBoardSystem chessBoardSystem = GetSystem<ChessBoardSystem>();
            if (chessBoardSystem == null) {
                XNLogger.LogError("Replay scene restore failed.", ("gameId", gameId), ("recordFilePath", recordFilePath));
            }
        }

        Global.Instance.uiManager.ShowPage<ReplayPage>();
    }

    public override void OnSceneExit()
    {
        Global.Instance.uiManager.TryClosePage<ReplayPage>();
        Global.ReleaseKeepAwake(Global.KeepAwakeReason.Duel);
        base.OnSceneExit();
    }

    public bool IsReplayLoaded => isReplayLoaded;
    public int ReplayCursorMoveIndex => replayCursorMoveIndex;
    public int ReplayMoveCount => replayMoves.Count;
    public int ReplayBoardSize => replayBoardSize;
    public string ReplayStatus => replayStatus ?? string.Empty;

    public void GoFirst()
    {
        ApplyReplayCursor(0);
    }

    public void GoPrev()
    {
        ApplyReplayCursor(replayCursorMoveIndex - 1);
    }

    public void GoNext()
    {
        ApplyReplayCursor(replayCursorMoveIndex + 1);
    }

    public void GoLast()
    {
        ApplyReplayCursor(replayMoves.Count);
    }

    public string BuildSummaryText()
    {
        if (!isReplayLoaded) {
            return string.IsNullOrEmpty(replayStatus) ? "未加载复盘记录" : replayStatus;
        }

        string boardText = replayBoardSize > 0 ? $"{replayBoardSize} 路" : "未知棋盘";
        return $"{boardText} · {replayMoves.Count} 手 · 复盘场景";
    }

    public string BuildCursorText()
    {
        if (!isReplayLoaded) {
            return "0 / 0";
        }

        return $"{replayCursorMoveIndex} / {replayMoves.Count}";
    }

    public string BuildMoveDetailText()
    {
        if (!isReplayLoaded) {
            return string.IsNullOrEmpty(replayStatus) ? "未加载复盘" : replayStatus;
        }

        if (replayCursorMoveIndex <= 0) {
            return replayInitialStones.Count > 0
                ? $"初始局面，含 {replayInitialStones.Count} 颗让子"
                : "初始局面";
        }

        ReplayMoveState latestMove = replayMoves[replayCursorMoveIndex - 1];
        string playerText = latestMove.playerFlag == PlayerFlag.Player1 ? "黑" : "白";
        string moveText = latestMove.isPass ? "虚手" : latestMove.pointText;
        return $"第 {replayCursorMoveIndex} 手：{playerText} {moveText}";
    }

    public string BuildActionHint()
    {
        if (!isReplayLoaded) {
            return string.IsNullOrEmpty(replayStatus) ? "复盘尚未加载" : replayStatus;
        }

        return "复盘场景已切换到棋盘级渲染，当前页面只保留控制层。";
    }

    private bool TryLoadReplayRecord(JObject recordJson)
    {
        replayMoves.Clear();
        replayInitialStones.Clear();
        replayCursorMoveIndex = 0;
        replayStatus = string.Empty;

        if (compChessBoard == null || compDuel == null) {
            replayStatus = "复盘场景组件缺失";
            return false;
        }

        if (KataGoDuelRecordFile.TryGetInitialStones(recordJson, out JArray initialStoneArray)) {
            foreach (JToken stoneToken in initialStoneArray) {
                if (!TryParseReplayMove(stoneToken, out ReplayMoveState stone) || stone.isPass) {
                    replayStatus = "复盘让子记录无效";
                    return false;
                }

                replayInitialStones.Add(stone);
            }
        }

        if (!KataGoDuelRecordFile.TryGetMoves(recordJson, out JArray moveArray)) {
            replayStatus = "复盘手顺缺失";
            return false;
        }

        foreach (JToken moveToken in moveArray) {
            if (!TryParseReplayMove(moveToken, out ReplayMoveState move)) {
                replayStatus = "复盘手顺包含无效落点";
                return false;
            }

            replayMoves.Add(move);
        }

        return true;
    }

    private void ApplyReplayCursor(int targetCursorMoveIndex)
    {
        if (!isReplayLoaded || compChessBoard == null || compDuel == null || compChessBoard.chessBoardGrid == null) {
            return;
        }

        int safeCursor = Mathf.Clamp(targetCursorMoveIndex, 0, replayMoves.Count);
        replayCursorMoveIndex = safeCursor;

        compChessBoard.chessInfoDict.Clear();
        compChessBoard.lastChessInfoDict.Clear();
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();
        compDuel.ResetKataGoMoves();
        ApplyReplayInitialStones();

        ReplayMoveState latestMove = null;
        for (int i = 0; i < safeCursor; i++) {
            ReplayMoveState move = replayMoves[i];
            if (move.isPass) {
                compDuel.AppendKataGoPass(move.playerFlag);
                continue;
            }

            if (!ApplyReplayMove(move)) {
                replayStatus = "复盘手顺回放失败";
                break;
            }

            latestMove = move;
        }

        compChessBoard.GetStoneViewCache().SyncFromChessInfoDict();
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();
        if (latestMove != null && latestMove.coords != null) {
            compChessBoard.chessBoardGrid.DrawLatestMoveMarker(latestMove.coords.x, latestMove.coords.z, latestMove.playerFlag == PlayerFlag.Player1);
        }
    }

    private void ApplyReplayInitialStones()
    {
        foreach (ReplayMoveState stone in replayInitialStones) {
            if (stone.coords == null) {
                continue;
            }

            int posIndex = compChessBoard.GetPosIndexByCoords(stone.coords);
            if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
                continue;
            }

            ChessInfo chessInfo = new ChessInfo();
            chessInfo.chessGuid.value = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
            chessInfo.chessFlag.value = (int)stone.playerFlag;
            compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), chessInfo);
        }
    }

    private bool ApplyReplayMove(ReplayMoveState move)
    {
        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        DuelMoveResult moveResult = DuelMoveRule.BuildMoveResult(
            compChessBoard,
            new DuelMoveCommand(move.playerFlag, move.coords, chessGuid)
        );
        if (!moveResult.accepted) {
            return false;
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        compDuel.AppendKataGoMove(move.playerFlag, move.coords, replayBoardSize);
        return true;
    }

    private bool TryParseReplayMove(JToken moveToken, out ReplayMoveState move)
    {
        move = null;
        if (!KataGoDuelRecordFile.TryParseMove(moveToken, out PlayerFlag playerFlag, out RectCoordinates coords, out bool isPass, replayBoardSize)) {
            return false;
        }

        move = new ReplayMoveState
        {
            playerFlag = playerFlag,
            coords = coords?.Clone(),
            isPass = isPass,
            pointText = isPass ? "pass" : KataGoPositionJsonBuilder.ToKataGoPoint(coords, replayBoardSize),
        };
        return true;
    }

    private class ReplayMoveState
    {
        public PlayerFlag playerFlag;
        public RectCoordinates coords;
        public bool isPass;
        public string pointText;
    }
}
