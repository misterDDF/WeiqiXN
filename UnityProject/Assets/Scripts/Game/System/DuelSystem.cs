using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public class DuelSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelSystem>();
    private const string DEFAULT_HOLD_TIME_CFG_ID = "5m";
    private const string DEFAULT_BYOYOMI_COUNT_CFG_ID = "off";
    private const string DEFAULT_BYOYOMI_TIME_CFG_ID = "30s";
    private const string DEFAULT_AI_DIFFICULTY_CFG_ID = "k20_k15";
    private const string SCORE_SOURCE_OWNERSHIP = "katago_ownership";

    public DuelSystem(DuelScene scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnRequestDuelScore>(OnRequestDuelScore);
        scene.RegisterSystemEvent<OnConfirmDuelScore>(OnConfirmDuelScore);
        scene.RegisterSystemEvent<OnConfirmDuelResign>(OnConfirmDuelResign);
        scene.RegisterSystemEvent<OnRequestDuelPass>(OnRequestDuelPass);
        scene.RegisterSystemEvent<OnRequestDuelTakeBack>(OnRequestDuelTakeBack);

        // 非读档进来的需要手动初始化
        if (scene.sceneCreateParams.saveFilePath == null) {
            var compDuel = scene.GetComponent<SceneComponentDuel>();
            if (compDuel != null) {
                InitTimeControlConfig(compDuel);

                string player1Guid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Player>());
                Player player1 = EntityUtils.CreatePlayer(scene, player1Guid, PlayerFlag.Player1);
                compDuel.player1Guid.value = player1Guid;
                string player2Guid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Player>());
                Player player2 = EntityUtils.CreatePlayer(scene, player2Guid, PlayerFlag.Player2);
                compDuel.player2Guid.value = player2Guid;
                compDuel.curTurnPlayerGuid.value = player1Guid;
                InitAiDuelConfig(compDuel);
                InitPlayerTimeControl(compDuel, player1);
                InitPlayerTimeControl(compDuel, player2);

                compDuel.duelFSM.Activate();
            }
        } else {
            var compDuel = scene.GetComponent<SceneComponentDuel>();
            if (compDuel != null) {
                EnsureTimeControlConfig(compDuel);
                EnsureAiDuelConfig(compDuel);

                Player player1 = EntityUtils.CreatePlayer(scene, compDuel.player1Guid.value, PlayerFlag.Player1);
                Player player2 = EntityUtils.CreatePlayer(scene, compDuel.player2Guid.value, PlayerFlag.Player2);

                compDuel.duelFSM.Activate(DuelStateDefine.STATE_TURN_INPUT);
            }
        }
    }

    private void InitTimeControlConfig(SceneComponentDuel compDuel)
    {
        var duelParams = scene.sceneCreateParams.duelSceneCreateParamas;
        compDuel.holdTimeCfgId.value = GetValidHoldTimeCfgId(duelParams?.holdTimeCfgId);
        compDuel.byoyomiCountCfgId.value = GetValidByoyomiCountCfgId(duelParams?.byoyomiCountCfgId);
        compDuel.byoyomiTimeCfgId.value = GetValidByoyomiTimeCfgId(duelParams?.byoyomiTimeCfgId);
    }

    private void InitAiDuelConfig(SceneComponentDuel compDuel)
    {
        var duelParams = scene.sceneCreateParams.duelSceneCreateParamas;
        compDuel.isAiDuel.value = duelParams != null && duelParams.isAiDuel;
        compDuel.aiDifficultyCfgId.value = compDuel.isAiDuel.value
            ? GetValidAiDifficultyCfgId(duelParams?.aiDifficultyCfgId)
            : string.Empty;
        compDuel.aiPlayerGuid.value = compDuel.isAiDuel.value ? compDuel.player2Guid.value : string.Empty;
    }

    private void EnsureTimeControlConfig(SceneComponentDuel compDuel)
    {
        compDuel.holdTimeCfgId.value = GetValidHoldTimeCfgId(compDuel.holdTimeCfgId.value);
        compDuel.byoyomiCountCfgId.value = GetValidByoyomiCountCfgId(compDuel.byoyomiCountCfgId.value);
        compDuel.byoyomiTimeCfgId.value = GetValidByoyomiTimeCfgId(compDuel.byoyomiTimeCfgId.value);
    }

    private void EnsureAiDuelConfig(SceneComponentDuel compDuel)
    {
        if (!compDuel.isAiDuel.value) {
            compDuel.aiDifficultyCfgId.value = string.Empty;
            compDuel.aiPlayerGuid.value = string.Empty;
            return;
        }

        compDuel.aiDifficultyCfgId.value = GetValidAiDifficultyCfgId(compDuel.aiDifficultyCfgId.value);
        if (string.IsNullOrEmpty(compDuel.aiPlayerGuid.value)) {
            compDuel.aiPlayerGuid.value = compDuel.player2Guid.value;
        }
    }

    private string GetValidHoldTimeCfgId(string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && DuelHoldTimeDataType.GetConfigData(cfgId) != null) {
            return cfgId;
        }
        return DEFAULT_HOLD_TIME_CFG_ID;
    }

    private string GetValidByoyomiCountCfgId(string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && DuelByoyomiCountDataType.GetConfigData(cfgId) != null) {
            return cfgId;
        }
        return DEFAULT_BYOYOMI_COUNT_CFG_ID;
    }

    private string GetValidByoyomiTimeCfgId(string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && DuelByoyomiTimeDataType.GetConfigData(cfgId) != null) {
            return cfgId;
        }
        return DEFAULT_BYOYOMI_TIME_CFG_ID;
    }

    private string GetValidAiDifficultyCfgId(string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && DuelAiDifficultyDataType.GetConfigData(cfgId) != null) {
            return cfgId;
        }
        return DEFAULT_AI_DIFFICULTY_CFG_ID;
    }

    private void InitPlayerTimeControl(SceneComponentDuel compDuel, Player player)
    {
        if (player == null) {
            return;
        }

        var compDuelInfo = player.GetComponent<ComponentDuelInfo>();
        var holdTimeData = DuelHoldTimeDataType.GetConfigData(compDuel.holdTimeCfgId.value);
        var byoyomiCountData = DuelByoyomiCountDataType.GetConfigData(compDuel.byoyomiCountCfgId.value);
        var byoyomiTimeData = DuelByoyomiTimeDataType.GetConfigData(compDuel.byoyomiTimeCfgId.value);
        if (compDuelInfo == null || holdTimeData == null || byoyomiCountData == null || byoyomiTimeData == null) {
            return;
        }

        compDuelInfo.isInfiniteTime.value = holdTimeData.isInfinite;
        compDuelInfo.holdLeftSeconds.value = holdTimeData.isInfinite ? -1 : holdTimeData.holdSeconds;
        compDuelInfo.byoyomiLeftCount.value = byoyomiCountData.count;
        compDuelInfo.byoyomiLeftSeconds.value = byoyomiTimeData.seconds;
        compDuelInfo.isInByoyomi.value = false;
        compDuelInfo.turnLeftTimes.value = holdTimeData.isInfinite ? -1 : holdTimeData.holdSeconds;
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.duelFSM.isActivated) {
            compDuel.duelFSM.Update();
        }
    }

    public void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.duelFSM.isActivated) {
            compDuel.consecutivePassCount.value = 0;
            compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
        }
    }

    private async void OnRequestDuelScore(OnRequestDuelScore evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

        if (compDuel.isScoring) {
            XNLogger.LogWarn("Duel score request skipped, score calculation is already running.");
            return;
        }

        DuelScoreResult scoreResult = null;
        try {
            compDuel.isScoring = true;
            scoreResult = await CalculateOwnershipScoreResultAsync("duel-score");
        }
        finally {
            compDuel.isScoring = false;
        }

        if (scoreResult == null) {
            scene.EmitSystemEvent(new OnDuelScoreFailed(true));
            return;
        }

        scene.EmitSystemEvent(new OnDuelScoreResult(scoreResult, true));
    }

    private void OnConfirmDuelScore(OnConfirmDuelScore evt)
    {
        EndGameByScore(evt.scoreResult, DuelGameEndReason.Score);
    }

    private void OnConfirmDuelResign(OnConfirmDuelResign evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_GAME_END) {
            return;
        }

        string loserGuid = compDuel.curTurnPlayerGuid.value;
        if (string.IsNullOrEmpty(loserGuid)) {
            return;
        }

        string winnerGuid = loserGuid == compDuel.player1Guid.value
            ? compDuel.player2Guid.value
            : compDuel.player1Guid.value;

        compDuel.resignLoserGuid.value = loserGuid;
        compDuel.winnerGuid.value = winnerGuid;
        compDuel.gameEndReason.value = DuelGameEndReason.Resign;
        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

    private void OnRequestDuelTakeBack(OnRequestDuelTakeBack evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            EmitTakeBackResult(false, "当前无法悔棋");
            return;
        }

        if (compDuel.isScoring) {
            EmitTakeBackResult(false, "数子中，暂不能悔棋");
            return;
        }

        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);
        int removeCount = GetTakeBackMoveCount(compDuel);
        if (removeCount <= 0 || moveCount < removeCount) {
            EmitTakeBackResult(false, "无可悔棋的手数");
            return;
        }

        JArray originalMoves = DuelMoveHistory.Clone(compDuel.kataGoMoves, moveCount);
        JArray remainMoves = DuelMoveHistory.TakeAfterRemovingLast(compDuel.kataGoMoves, removeCount);

        if (!RebuildBoardFromMoves(compChessBoard, compDuel, remainMoves)) {
            XNLogger.LogError("Duel take back failed, board rebuild failed.");
            RebuildBoardFromMoves(compChessBoard, compDuel, originalMoves);
            EmitTakeBackResult(false, "悔棋失败");
            return;
        }

        compDuel.curTurnPlayerGuid.value = GetNextTurnPlayerGuid(compDuel, remainMoves.Count);
        compDuel.timeoutLoserGuid.value = string.Empty;
        compDuel.resignLoserGuid.value = string.Empty;
        compDuel.winnerGuid.value = string.Empty;
        compDuel.gameEndReason.value = string.Empty;
        compDuel.finalBlackScore.value = 0f;
        compDuel.finalWhiteScore.value = 0f;
        compDuel.finalScoreMargin.value = 0f;
        compDuel.consecutivePassCount.value = DuelMoveHistory.CountTrailingPasses(compDuel.kataGoMoves);
        compDuel.ClearOwnershipScoreCache();
        compChessBoard.chessBoardGrid?.ClearOwnership();

        compDuel.duelFSM.SwitchState(DuelStateDefine.STATE_TURN_INPUT);
        EmitTakeBackResult(true, removeCount >= 2 ? "已回退两手棋" : "已悔棋", removeCount);
    }

    private async void OnRequestDuelPass(OnRequestDuelPass evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return;
        }

        if (compDuel.isScoring) {
            XNLogger.LogWarn("Duel pass skipped, score calculation is already running.");
            return;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            return;
        }

        PlayerFlag curPlayerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        compDuel.AppendKataGoPass(curPlayerFlag);
        compDuel.consecutivePassCount.value += 1;
        scene.EmitSystemEvent(new OnDuelPassAccepted(
            curPlayer.guid,
            curPlayerFlag,
            compDuel.isAiDuel.value && curPlayer.guid == compDuel.aiPlayerGuid.value,
            compDuel.consecutivePassCount.value
        ));

        if (compDuel.consecutivePassCount.value >= 2) {
            DuelScoreResult scoreResult = null;
            try {
                compDuel.isScoring = true;
                scoreResult = await CalculateOwnershipScoreResultAsync("duel-consecutive-pass-score");
            }
            finally {
                compDuel.isScoring = false;
            }

            if (scoreResult == null) {
                compDuel.RemoveLastKataGoMove();
                compDuel.consecutivePassCount.value = 1;
                scene.EmitSystemEvent(new OnDuelScoreFailed(false));
                return;
            }

            scene.EmitSystemEvent(new OnDuelScoreResult(scoreResult, false));
            EndGameByScore(scoreResult, DuelGameEndReason.ConsecutivePass);
            return;
        }

        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
    }

    private int GetTakeBackMoveCount(SceneComponentDuel compDuel)
    {
        if (compDuel == null) {
            return 0;
        }

        if (!compDuel.isAiDuel.value) {
            return 1;
        }

        string humanPlayerGuid = compDuel.player1Guid.value == compDuel.aiPlayerGuid.value
            ? compDuel.player2Guid.value
            : compDuel.player1Guid.value;
        return compDuel.curTurnPlayerGuid.value == humanPlayerGuid ? 2 : 1;
    }

    private bool RebuildBoardFromMoves(SceneComponentChessBoard compChessBoard, SceneComponentDuel compDuel, JArray moves)
    {
        if (compChessBoard == null || compDuel == null || moves == null) {
            return false;
        }

        ClearChessEntities();
        compChessBoard.chessInfoDict.Clear();
        compChessBoard.lastChessInfoDict.Clear();
        compChessBoard.chessBoardGrid?.ClearLatestMoveMarker();
        compDuel.ResetKataGoMoves();

        int boardSize = compChessBoard.chessBoardGrid != null ? compChessBoard.chessBoardGrid.gridSize : 19;
        RectCoordinates latestMoveCoords = null;
        PlayerFlag latestMovePlayerFlag = 0;
        foreach (JToken move in moves) {
            if (!KataGoDuelRecordFile.TryParseMove(move, out PlayerFlag playerFlag, out RectCoordinates coords, out bool isPass, boardSize)) {
                return false;
            }

            if (isPass) {
                compDuel.AppendKataGoPass(playerFlag);
                continue;
            }

            if (!ReplayMove(compChessBoard, compDuel, playerFlag, coords, boardSize)) {
                return false;
            }

            latestMoveCoords = coords.Clone();
            latestMovePlayerFlag = playerFlag;
        }

        if (latestMoveCoords != null) {
            compChessBoard.chessBoardGrid?.DrawLatestMoveMarker(latestMoveCoords.x, latestMoveCoords.z, latestMovePlayerFlag == PlayerFlag.Player1);
        }

        return true;
    }

    private bool ReplayMove(SceneComponentChessBoard compChessBoard, SceneComponentDuel compDuel, PlayerFlag playerFlag, RectCoordinates coords, int boardSize)
    {
        string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        DuelMoveResult moveResult = DuelMoveRule.BuildMoveResult(
            compChessBoard,
            new DuelMoveCommand(playerFlag, coords, chessGuid)
        );
        if (!moveResult.accepted) {
            return false;
        }

        foreach (int removePosIndex in moveResult.pendingRemovePosIndexes) {
            if (moveResult.previousChessInfoDict.TryGetValue(removePosIndex.ToString(), out ChessInfo chessInfo)) {
                Chess chess = scene.GetEntity<Chess>(chessInfo.chessGuid.value);
                chess?.Destroy();
            }
        }

        DuelMoveRule.ApplyMoveResult(compChessBoard, moveResult);
        EntityUtils.CreateChess(scene, chessGuid, playerFlag, coords);
        compDuel.AppendKataGoMove(playerFlag, coords, boardSize);
        return true;
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

    private string GetNextTurnPlayerGuid(SceneComponentDuel compDuel, int moveCount)
    {
        return moveCount % 2 == 0 ? compDuel.player1Guid.value : compDuel.player2Guid.value;
    }

    private void EmitTakeBackResult(bool success, string message, int removedMoveCount = 0)
    {
        scene.EmitSystemEvent(new OnDuelTakeBackResult(success, message, removedMoveCount));
    }

    private async System.Threading.Tasks.Task<DuelScoreResult> CalculateOwnershipScoreResultAsync(string requestIdPrefix)
    {
        DuelScene duelScene = scene as DuelScene;
        if (duelScene == null) {
            XNLogger.LogError("Duel ownership score failed, scene is not DuelScene.");
            return null;
        }

        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.hasOwnershipScoreCache) {
            DuelOwnershipSystem.OwnershipScore cachedScore = compDuel.GetCachedOwnershipScore();
            return BuildScoreResult(cachedScore.blackPoints, cachedScore.whitePoints, cachedScore.komi, SCORE_SOURCE_OWNERSHIP);
        }

        try {
            string requestId = $"{requestIdPrefix}-{DateTime.UtcNow.Ticks}";
            JObject query = KataGoPositionJsonBuilder.BuildOwnershipAnalysisJson(duelScene, requestId);
            JArray ownership = await KataGoBootstrap.AnalyzeOwnershipAsync(query);
            if (ownership == null) {
                XNLogger.LogWarn("Duel ownership score failed, ownership result is empty.", ("requestId", requestId));
                return null;
            }

            DuelOwnershipSystem.OwnershipScore score = DuelOwnershipSystem.CalculateOwnershipScore(ownership, query);
            compDuel?.CacheOwnershipScore(score, ownership);
            return BuildScoreResult(score.blackPoints, score.whitePoints, score.komi, SCORE_SOURCE_OWNERSHIP);
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel ownership score failed.", ("err", ex.Message));
            return null;
        }
    }

    private DuelScoreResult BuildScoreResult(float blackScore, float whiteScore, float komi, string scoreSource)
    {
        float margin = Math.Abs(blackScore - whiteScore);
        PlayerFlag winnerFlag = 0;
        if (blackScore > whiteScore) {
            winnerFlag = PlayerFlag.Player1;
        } else if (whiteScore > blackScore) {
            winnerFlag = PlayerFlag.Player2;
        }

        return new DuelScoreResult
        {
            blackScore = blackScore,
            whiteScore = whiteScore,
            komi = komi,
            margin = margin,
            winnerFlag = winnerFlag,
            scoreSource = scoreSource,
        };
    }

    private void EndGameByScore(DuelScoreResult scoreResult, string reason)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || scoreResult == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        compDuel.finalBlackScore.value = scoreResult.blackScore;
        compDuel.finalWhiteScore.value = scoreResult.whiteScore;
        compDuel.finalScoreMargin.value = scoreResult.margin;
        compDuel.gameEndReason.value = reason;

        if (scoreResult.winnerFlag == PlayerFlag.Player1) {
            compDuel.winnerGuid.value = compDuel.player1Guid.value;
        } else if (scoreResult.winnerFlag == PlayerFlag.Player2) {
            compDuel.winnerGuid.value = compDuel.player2Guid.value;
        } else {
            compDuel.winnerGuid.value = string.Empty;
        }

        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

}
