using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class ReplaySystem : SystemBase
{
    public override string systemName => GetSystemName<ReplaySystem>();

    private SceneComponentReplay compReplay;
    private SceneComponentChessBoard compChessBoard;
    private SceneComponentDuel compDuel;
    private string recordFilePath = string.Empty;

    public ReplaySystem(SceneBase scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();

        compReplay = scene.GetComponent<SceneComponentReplay>();
        compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        compDuel = scene.GetComponent<SceneComponentDuel>();
        LoadReplayRecord();
    }

    public bool IsReplayLoaded => compReplay != null && compReplay.isReplayLoaded;
    public bool IsTryMode => compReplay != null && compReplay.isTryMode;
    public int ReplayCursorMoveIndex => compReplay != null ? compReplay.replayCursorMoveIndex : 0;
    public int ReplayMoveCount => compReplay != null ? compReplay.replayMoves.Count : 0;
    public int TryMoveCount => compReplay != null ? compReplay.tryMoves.Count : 0;
    public int TryCursorMoveIndex => compReplay != null ? compReplay.tryCursorMoveIndex : 0;
    public int ReplayBoardSize => compReplay != null ? compReplay.replayBoardSize : 0;
    public PlayerFlag CurrentTryPlayerFlag => compReplay != null ? ResolveNextTryPlayerFlag() : 0;
    public string ReplayStatus => BuildReplayStatusText();

    public void RestoreDefaultBoard()
    {
        if (!IsReplayLoaded) {
            return;
        }

        ApplyReplayCursor(compReplay.replayMoves.Count);
        if (scene.GetSystem<ChessBoardSystem>() == null) {
            XNLogger.LogError("Replay scene restore failed.", ("recordFilePath", recordFilePath));
        }
    }

    public void GoFirst()
    {
        if (IsTryMode) {
            ApplyTryCursor(0);
            return;
        }

        ApplyReplayCursor(0);
    }

    public void GoPrev()
    {
        if (IsTryMode) {
            ApplyTryCursor(compReplay.tryCursorMoveIndex - 1);
            return;
        }

        ApplyReplayCursor(compReplay.replayCursorMoveIndex - 1);
    }

    public void GoNext()
    {
        if (IsTryMode) {
            ApplyTryCursor(compReplay.tryCursorMoveIndex + 1);
            return;
        }

        ApplyReplayCursor(compReplay.replayCursorMoveIndex + 1);
    }

    public void GoLast()
    {
        if (IsTryMode) {
            ApplyTryCursor(compReplay.tryMoves.Count);
            return;
        }

        ApplyReplayCursor(compReplay.replayMoves.Count);
    }

    public bool ToggleTryMode()
    {
        return IsTryMode ? ExitTryMode() : EnterTryMode();
    }

    public bool EnterTryMode()
    {
        if (!IsReplayLoaded || IsTryMode) {
            return false;
        }

        compReplay.tryBaseCursorMoveIndex = compReplay.replayCursorMoveIndex;
        compReplay.tryCursorMoveIndex = 0;
        compReplay.tryMoves.Clear();
        compReplay.isTryMode = true;
        compReplay.replayStatus = string.Empty;
        return true;
    }

    public bool ExitTryMode()
    {
        if (!IsTryMode) {
            return false;
        }

        compReplay.isTryMode = false;
        compReplay.tryMoves.Clear();
        compReplay.tryCursorMoveIndex = 0;
        ApplyReplayCursor(compReplay.tryBaseCursorMoveIndex);
        compReplay.replayStatus = string.Empty;
        return true;
    }

    public bool TryApplyTryMove(RectCoordinates coords)
    {
        if (!IsReplayLoaded || !IsTryMode || coords == null || compChessBoard == null || compDuel == null) {
            return false;
        }

        PlayerFlag playerFlag = ResolveNextTryPlayerFlag();
        if (playerFlag == 0) {
            compReplay.replayStatus = "试下行棋方无效";
            return false;
        }

        ReplayMoveState move = CreateReplayMoveState(playerFlag, coords.Clone(), false);
        if (!TryBuildAndApplyTryMove(move)) {
            compReplay.replayStatus = "试下落子失败";
            return false;
        }

        if (compReplay.tryCursorMoveIndex < compReplay.tryMoves.Count) {
            compReplay.tryMoves.RemoveRange(compReplay.tryCursorMoveIndex, compReplay.tryMoves.Count - compReplay.tryCursorMoveIndex);
        }

        compReplay.tryMoves.Add(move);
        compReplay.tryCursorMoveIndex = compReplay.tryMoves.Count;
        SyncTryBoardMarkers();
        compReplay.replayStatus = string.Empty;
        return true;
    }

    public string BuildSummaryText()
    {
        if (!IsReplayLoaded) {
            return string.IsNullOrEmpty(compReplay?.replayStatus) ? "未加载复盘记录" : compReplay.replayStatus;
        }

        string boardText = compReplay.replayBoardSize > 0 ? $"{compReplay.replayBoardSize} 路" : "未知棋盘";
        if (IsTryMode) {
            return $"{boardText} · 主线 {compReplay.replayMoves.Count} 手 · 试下 {compReplay.tryCursorMoveIndex}/{compReplay.tryMoves.Count} 手";
        }

        return $"{boardText} · {compReplay.replayMoves.Count} 手 · 复盘场景";
    }

    public string BuildCursorText()
    {
        if (!IsReplayLoaded) {
            return "0 / 0";
        }

        if (IsTryMode) {
            return $"{compReplay.tryBaseCursorMoveIndex}+{compReplay.tryCursorMoveIndex} / {compReplay.replayMoves.Count}";
        }

        return $"{compReplay.replayCursorMoveIndex} / {compReplay.replayMoves.Count}";
    }

    public string BuildMoveDetailText()
    {
        if (!IsReplayLoaded) {
            return string.IsNullOrEmpty(compReplay?.replayStatus) ? "未加载复盘" : compReplay.replayStatus;
        }

        if (IsTryMode) {
            if (compReplay.tryCursorMoveIndex <= 0) {
                return $"试下模式：从第 {compReplay.tryBaseCursorMoveIndex} 手开始";
            }

            ReplayMoveState tryMove = compReplay.tryMoves[compReplay.tryCursorMoveIndex - 1];
            string tryPlayerText = GetPlayerText(tryMove.playerFlag);
            string tryMoveText = tryMove.isPass ? "虚手" : tryMove.pointText;
            return $"试下第 {compReplay.tryCursorMoveIndex} 手：{tryPlayerText} {tryMoveText}";
        }

        if (compReplay.replayCursorMoveIndex <= 0) {
            return compReplay.replayInitialStones.Count > 0
                ? $"初始局面，含 {compReplay.replayInitialStones.Count} 颗让子"
                : "初始局面";
        }

        ReplayMoveState latestMove = compReplay.replayMoves[compReplay.replayCursorMoveIndex - 1];
        string playerText = GetPlayerText(latestMove.playerFlag);
        string moveText = latestMove.isPass ? "虚手" : latestMove.pointText;
        return $"第 {compReplay.replayCursorMoveIndex} 手：{playerText} {moveText}";
    }

    public string BuildActionHint()
    {
        if (!IsReplayLoaded) {
            return string.IsNullOrEmpty(compReplay?.replayStatus) ? "复盘尚未加载" : compReplay.replayStatus;
        }

        if (IsTryMode) {
            string playerText = GetPlayerText(ResolveNextTryPlayerFlag());
            return $"试下模式不会写回原始复盘归档。当前轮到{playerText}方试下。";
        }

        return "复盘场景已切换到棋盘级渲染，当前页面只保留控制层。";
    }

    private void LoadReplayRecord()
    {
        if (compReplay == null || compChessBoard == null || compDuel == null) {
            if (compReplay != null) {
                compReplay.replayStatus = "复盘场景组件缺失";
            }
            return;
        }

        string gameId = scene.sceneCreateParams != null ? scene.sceneCreateParams.replayGameId : string.Empty;
        if (string.IsNullOrEmpty(gameId)) {
            XNLogger.LogError("Replay scene load failed, replay game id is empty.");
            compReplay.replayStatus = "复盘记录无效";
            return;
        }

        recordFilePath = GameSaveConfig.GetReplayDuelRecordPath(gameId);
        if (KataGoDuelRecordFile.TryLoad(recordFilePath, out JObject recordJson) &&
            KataGoDuelRecordFile.TryGetBoardSize(recordJson, out int boardSize)) {
            compReplay.replayBoardSize = boardSize;
            compChessBoard.boardCfgId.value = $"{boardSize}x{boardSize}";
            if (TryLoadReplayRecord(recordJson)) {
                compReplay.isReplayLoaded = true;
            }
        } else {
            compReplay.replayStatus = "复盘记录读取失败";
        }
    }

    private bool TryLoadReplayRecord(JObject recordJson)
    {
        compReplay.replayMoves.Clear();
        compReplay.replayInitialStones.Clear();
        compReplay.replayCursorMoveIndex = 0;
        compReplay.replayStatus = string.Empty;

        if (KataGoDuelRecordFile.TryGetInitialStones(recordJson, out JArray initialStoneArray)) {
            foreach (JToken stoneToken in initialStoneArray) {
                if (!TryParseReplayMove(stoneToken, out ReplayMoveState stone) || stone.isPass) {
                    compReplay.replayStatus = "复盘让子记录无效";
                    return false;
                }

                compReplay.replayInitialStones.Add(stone);
            }
        }

        if (!KataGoDuelRecordFile.TryGetMoves(recordJson, out JArray moveArray)) {
            compReplay.replayStatus = "复盘手顺缺失";
            return false;
        }

        foreach (JToken moveToken in moveArray) {
            if (!TryParseReplayMove(moveToken, out ReplayMoveState move)) {
                compReplay.replayStatus = "复盘手顺包含无效落点";
                return false;
            }

            compReplay.replayMoves.Add(move);
        }

        return true;
    }

    private void ApplyReplayCursor(int targetCursorMoveIndex)
    {
        if (!IsReplayLoaded || compChessBoard == null || compDuel == null || compChessBoard.chessBoardGrid == null) {
            return;
        }

        int safeCursor = Mathf.Clamp(targetCursorMoveIndex, 0, compReplay.replayMoves.Count);
        compReplay.replayCursorMoveIndex = safeCursor;
        compReplay.replayStatus = string.Empty;

        compChessBoard.chessInfoDict.Clear();
        compChessBoard.lastChessInfoDict.Clear();
        compChessBoard.GetStoneViewCache().ClearStoneMarkers();
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();
        compChessBoard.chessBoardGrid.ClearMoveNumberMarkers();
        compDuel.ResetKataGoMoves();
        ApplyReplayInitialStones();

        ReplayMoveState latestMove = null;
        int latestMoveNumber = 0;
        for (int i = 0; i < safeCursor; i++) {
            ReplayMoveState move = compReplay.replayMoves[i];
            if (move.isPass) {
                compDuel.AppendKataGoPass(move.playerFlag);
                continue;
            }

            if (!ApplyReplayMove(move)) {
                compReplay.replayStatus = "复盘手顺回放失败";
                break;
            }

            latestMove = move;
            latestMoveNumber = i + 1;
        }

        SyncBoardViews(latestMove, latestMoveNumber);
    }

    private void ApplyTryCursor(int targetTryCursorMoveIndex)
    {
        if (!IsReplayLoaded || !IsTryMode || compChessBoard == null || compDuel == null || compChessBoard.chessBoardGrid == null) {
            return;
        }

        int safeCursor = Mathf.Clamp(targetTryCursorMoveIndex, 0, compReplay.tryMoves.Count);
        if (safeCursor == compReplay.tryCursorMoveIndex) {
            return;
        }

        while (compReplay.tryCursorMoveIndex < safeCursor) {
            if (!ApplyTryStepForward(compReplay.tryCursorMoveIndex)) {
                break;
            }
        }

        while (compReplay.tryCursorMoveIndex > safeCursor) {
            if (!ApplyTryStepBackward(compReplay.tryCursorMoveIndex - 1)) {
                break;
            }
        }

        SyncTryBoardMarkers();
        compReplay.replayStatus = string.Empty;
    }

    private void ApplyReplayInitialStones()
    {
        foreach (ReplayMoveState stone in compReplay.replayInitialStones) {
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
        compDuel.AppendKataGoMove(move.playerFlag, move.coords, compReplay.replayBoardSize);
        return true;
    }

    private bool TryBuildAndApplyTryMove(ReplayMoveState move)
    {
        if (move == null || move.coords == null) {
            return false;
        }

        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        DuelMoveResult moveResult = DuelMoveRule.BuildMoveResult(
            compChessBoard,
            new DuelMoveCommand(move.playerFlag, move.coords, chessGuid)
        );
        if (!moveResult.accepted) {
            return false;
        }

        move.previousLastChessInfoDict = CloneChessInfoDict(compChessBoard.lastChessInfoDict);
        move.moveResult = moveResult;
        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        compDuel.AppendKataGoMove(move.playerFlag, move.coords, compReplay.replayBoardSize);
        ApplyTryMoveStoneViews(moveResult, true);
        return true;
    }

    private bool ApplyTryStepForward(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= compReplay.tryMoves.Count) {
            return false;
        }

        ReplayMoveState move = compReplay.tryMoves[stepIndex];
        if (move == null || move.moveResult == null || !move.moveResult.accepted) {
            return false;
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, move.moveResult);
        compDuel.AppendKataGoMove(move.playerFlag, move.coords, compReplay.replayBoardSize);
        ApplyTryMoveStoneViews(move.moveResult, true);
        compReplay.tryCursorMoveIndex = stepIndex + 1;
        return true;
    }

    private bool ApplyTryStepBackward(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= compReplay.tryMoves.Count) {
            return false;
        }

        ReplayMoveState move = compReplay.tryMoves[stepIndex];
        DuelMoveResult moveResult = move?.moveResult;
        if (moveResult == null || moveResult.previousChessInfoDict == null) {
            return false;
        }

        compChessBoard.chessInfoDict = CloneChessInfoDict(moveResult.previousChessInfoDict);
        compChessBoard.lastChessInfoDict = CloneChessInfoDict(move.previousLastChessInfoDict);
        compDuel.RemoveLastKataGoMove();
        RevertTryMoveStoneViews(moveResult);
        compReplay.tryCursorMoveIndex = stepIndex;
        return true;
    }

    private void ApplyTryMoveStoneViews(DuelMoveResult moveResult, bool animatePlacedStone)
    {
        if (moveResult == null || compChessBoard == null) {
            return;
        }

        ChessStoneViewCache stoneViewCache = compChessBoard.GetStoneViewCache();
        foreach (int removePosIndex in moveResult.pendingRemovePosIndexes) {
            RectCoordinates removeCoords = compChessBoard.GetCoordsByPosIndex(removePosIndex);
            stoneViewCache.HideStone(removeCoords);
        }

        if (moveResult.coords != null) {
            stoneViewCache.ShowStone(moveResult.coords, moveResult.playerFlag, animatePlacedStone);
        }
    }

    private void RevertTryMoveStoneViews(DuelMoveResult moveResult)
    {
        if (moveResult == null || compChessBoard == null) {
            return;
        }

        ChessStoneViewCache stoneViewCache = compChessBoard.GetStoneViewCache();
        if (moveResult.coords != null) {
            stoneViewCache.HideStone(moveResult.coords);
        }

        foreach (int restorePosIndex in moveResult.pendingRemovePosIndexes) {
            string posKey = restorePosIndex.ToString();
            if (moveResult.previousChessInfoDict == null || !moveResult.previousChessInfoDict.TryGetValue(posKey, out ChessInfo chessInfo) || chessInfo == null) {
                continue;
            }

            RectCoordinates restoreCoords = compChessBoard.GetCoordsByPosIndex(restorePosIndex);
            stoneViewCache.ShowStone(restoreCoords, (PlayerFlag)chessInfo.chessFlag.value, false);
        }
    }

    private SavableObjectDict<ChessInfo> CloneChessInfoDict(SavableObjectDict<ChessInfo> source)
    {
        SavableObjectDict<ChessInfo> cloned = new SavableObjectDict<ChessInfo>();
        if (source == null) {
            return cloned;
        }

        foreach (var kvp in source) {
            if (kvp.Value == null) {
                continue;
            }

            ChessInfo chessInfo = new ChessInfo();
            chessInfo.chessGuid.value = kvp.Value.chessGuid.value;
            chessInfo.chessFlag.value = kvp.Value.chessFlag.value;
            cloned.SetValue(kvp.Key, chessInfo);
        }

        return cloned;
    }

    private void SyncBoardViews(ReplayMoveState latestMove, int latestMoveNumber)
    {
        if (compChessBoard == null || compChessBoard.chessBoardGrid == null) {
            return;
        }

        compChessBoard.GetStoneViewCache().SyncFromChessInfoDict();
        compChessBoard.GetStoneViewCache().ClearStoneMarkers();
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();
        compChessBoard.chessBoardGrid.ClearMoveNumberMarkers();

        if (IsTryMode) {
            ApplyTryMoveNumberMarkers();
        } else if (latestMove != null && latestMove.coords != null && latestMoveNumber > 0) {
            ApplyMoveNumberMarker(latestMove, latestMoveNumber);
        }
    }

    private void ApplyMoveNumberMarker(ReplayMoveState move, int moveNumber)
    {
        if (move == null || move.coords == null || moveNumber <= 0 || !IsStoneStillOnBoard(move)) {
            return;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(move.coords);
        if (posIndex < 0) {
            return;
        }

        Dictionary<int, StoneMarkerIntent> markers = new Dictionary<int, StoneMarkerIntent>
        {
            [posIndex] = StoneMarkerIntent.MoveNumber(moveNumber, move.playerFlag == PlayerFlag.Player1)
        };
        compChessBoard.GetStoneViewCache().ApplyStoneMarkers(markers);
    }

    private void ApplyTryMoveNumberMarkers()
    {
        if (compChessBoard == null || compReplay.tryCursorMoveIndex <= 0) {
            return;
        }

        Dictionary<int, StoneMarkerIntent> markers = new Dictionary<int, StoneMarkerIntent>();
        for (int i = 0; i < compReplay.tryCursorMoveIndex; i++) {
            ReplayMoveState move = compReplay.tryMoves[i];
            if (move == null || move.isPass || move.coords == null || !IsStoneStillOnBoard(move)) {
                continue;
            }

            int posIndex = compChessBoard.GetPosIndexByCoords(move.coords);
            if (posIndex >= 0) {
                markers[posIndex] = StoneMarkerIntent.MoveNumber(i + 1, move.playerFlag == PlayerFlag.Player1);
            }
        }

        compChessBoard.GetStoneViewCache().ApplyStoneMarkers(markers);
    }

    private void SyncTryBoardMarkers()
    {
        if (compChessBoard == null || compChessBoard.chessBoardGrid == null) {
            return;
        }

        compChessBoard.GetStoneViewCache().ClearStoneMarkers();
        compChessBoard.chessBoardGrid.ClearLatestMoveMarker();
        compChessBoard.chessBoardGrid.ClearMoveNumberMarkers();
        ApplyTryMoveNumberMarkers();
    }

    private bool IsStoneStillOnBoard(ReplayMoveState move)
    {
        if (compChessBoard == null || move == null || move.coords == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(move.coords);
        return posIndex >= 0 &&
            compChessBoard.chessInfoDict.TryGetValue(posIndex.ToString(), out ChessInfo chessInfo) &&
            chessInfo != null &&
            chessInfo.chessFlag.value == (int)move.playerFlag;
    }

    private bool TryParseReplayMove(JToken moveToken, out ReplayMoveState move)
    {
        move = null;
        if (!KataGoDuelRecordFile.TryParseMove(moveToken, out PlayerFlag playerFlag, out RectCoordinates coords, out bool isPass, compReplay.replayBoardSize)) {
            return false;
        }

        move = CreateReplayMoveState(playerFlag, coords?.Clone(), isPass);
        return true;
    }

    private ReplayMoveState CreateReplayMoveState(PlayerFlag playerFlag, RectCoordinates coords, bool isPass)
    {
        return new ReplayMoveState
        {
            playerFlag = playerFlag,
            coords = coords?.Clone(),
            isPass = isPass,
            pointText = isPass ? "pass" : KataGoPositionJsonBuilder.ToKataGoPoint(coords, compReplay.replayBoardSize),
        };
    }

    private PlayerFlag ResolveNextTryPlayerFlag()
    {
        if (compReplay == null) {
            return 0;
        }

        if (IsTryMode && compReplay.tryMoves.Count > 0) {
            int lastVisibleTryMoveIndex = Mathf.Min(compReplay.tryCursorMoveIndex, compReplay.tryMoves.Count) - 1;
            if (lastVisibleTryMoveIndex >= 0) {
                return compReplay.tryMoves[lastVisibleTryMoveIndex].playerFlag.GetOpponentPlayerFlag();
            }
        }

        int baseCursorMoveIndex = IsTryMode ? compReplay.tryBaseCursorMoveIndex : compReplay.replayCursorMoveIndex;
        if (baseCursorMoveIndex >= 0 && baseCursorMoveIndex < compReplay.replayMoves.Count) {
            return compReplay.replayMoves[baseCursorMoveIndex].playerFlag;
        }

        if (baseCursorMoveIndex > 0 && baseCursorMoveIndex <= compReplay.replayMoves.Count) {
            return compReplay.replayMoves[baseCursorMoveIndex - 1].playerFlag.GetOpponentPlayerFlag();
        }

        return compReplay.replayInitialStones.Count > 0 ? PlayerFlag.Player2 : PlayerFlag.Player1;
    }

    private string BuildReplayStatusText()
    {
        if (!IsReplayLoaded) {
            return compReplay?.replayStatus ?? string.Empty;
        }

        if (IsTryMode) {
            return $"试下模式 · 轮到{GetPlayerText(ResolveNextTryPlayerFlag())}方";
        }

        return compReplay.replayStatus ?? string.Empty;
    }

    private string GetPlayerText(PlayerFlag playerFlag)
    {
        return playerFlag == PlayerFlag.Player1 ? "黑" : "白";
    }
}
