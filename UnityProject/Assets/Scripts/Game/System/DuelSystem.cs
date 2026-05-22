using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public class DuelSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelSystem>();
    private const string DEFAULT_HOLD_TIME_CFG_ID = "infinite";
    private const string DEFAULT_BYOYOMI_COUNT_CFG_ID = "off";
    private const string DEFAULT_BYOYOMI_TIME_CFG_ID = "30s";
    private const string DEFAULT_AI_DIFFICULTY_CFG_ID = "k20_k15";

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
        scene.RegisterSystemEvent<OnApplyLanDuelTimeState>(OnApplyLanDuelTimeState);
        scene.RegisterSystemEvent<OnApplyLanDuelTimeout>(OnApplyLanDuelTimeout);
        scene.RegisterSystemEvent<OnApplyLanDuelResign>(OnApplyLanDuelResign);
        scene.RegisterSystemEvent<OnApplyLanDuelPass>(OnApplyLanDuelPass);
        scene.RegisterSystemEvent<OnApplyLanDuelScoreRequest>(OnApplyLanDuelScoreRequest);
        scene.RegisterSystemEvent<OnApplyLanDuelScoreResult>(OnApplyLanDuelScoreResult);
        scene.RegisterSystemEvent<OnApplyLanDuelScoreFailed>(OnApplyLanDuelScoreFailed);
        scene.RegisterSystemEvent<OnApplyLanDuelTakeBack>(OnApplyLanDuelTakeBack);
        scene.RegisterSystemEvent<OnLanDuelTakeBackRejected>(OnLanDuelTakeBackRejected);

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
                InitLanDuelConfig(compDuel);
                InitPlayerTimeControl(compDuel, player1);
                InitPlayerTimeControl(compDuel, player2);
                ApplyInitialHandicapStones(compDuel);
                compDuel.curTurnPlayerGuid.value = GetNextTurnPlayerGuid(compDuel, 0);

                compDuel.duelFSM.Activate();
                scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
            }
        } else {
            var compDuel = scene.GetComponent<SceneComponentDuel>();
            if (compDuel != null) {
                EnsureTimeControlConfig(compDuel);
                EnsureHandicapConfig(compDuel);
                EnsureAiDuelConfig(compDuel);
                EnsureLanDuelConfig(compDuel);

                Player player1 = EntityUtils.CreatePlayer(scene, compDuel.player1Guid.value, PlayerFlag.Player1);
                Player player2 = EntityUtils.CreatePlayer(scene, compDuel.player2Guid.value, PlayerFlag.Player2);

                compDuel.duelFSM.Activate(DuelStateDefine.STATE_TURN_INPUT);
                scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
            }
        }
    }

    private void InitTimeControlConfig(SceneComponentDuel compDuel)
    {
        var duelParams = scene.sceneCreateParams.duelSceneCreateParamas;
        bool useHostConfig = duelParams != null && duelParams.isLanDuel && duelParams.isLanRoomHostConfig;
        compDuel.holdTimeCfgId.value = useHostConfig ? duelParams.holdTimeCfgId : GetValidHoldTimeCfgId(duelParams?.holdTimeCfgId);
        compDuel.byoyomiCountCfgId.value = useHostConfig ? duelParams.byoyomiCountCfgId : GetValidByoyomiCountCfgId(duelParams?.byoyomiCountCfgId);
        compDuel.byoyomiTimeCfgId.value = useHostConfig ? duelParams.byoyomiTimeCfgId : GetValidByoyomiTimeCfgId(duelParams?.byoyomiTimeCfgId);
        compDuel.handicapCfgId.value = useHostConfig ? duelParams.handicapCfgId : GetValidHandicapCfgId(duelParams?.handicapCfgId);
    }

    private void InitAiDuelConfig(SceneComponentDuel compDuel)
    {
        var duelParams = scene.sceneCreateParams.duelSceneCreateParamas;
        compDuel.isAiDuel.value = duelParams != null && duelParams.isAiDuel;
        compDuel.aiDifficultyCfgId.value = compDuel.isAiDuel.value
            ? GetValidAiDifficultyCfgId(duelParams?.aiDifficultyCfgId)
            : string.Empty;
        PlayerFlag localPlayerFlag = DuelUtils.GetValidPlayerFlag(duelParams != null ? duelParams.localPlayerFlag : 0);
        PlayerFlag aiPlayerFlag = localPlayerFlag.GetOpponentPlayerFlag();
        compDuel.aiPlayerGuid.value = compDuel.isAiDuel.value ? GetPlayerGuidByFlag(compDuel, aiPlayerFlag) : string.Empty;
    }

    private void InitLanDuelConfig(SceneComponentDuel compDuel)
    {
        var duelParams = scene.sceneCreateParams.duelSceneCreateParamas;
        compDuel.isLanDuel.value = duelParams != null && duelParams.isLanDuel;
        compDuel.lanRole.value = compDuel.isLanDuel.value ? (int)duelParams.lanRole : (int)LanRoomRole.None;
        compDuel.lanHostPlayerFlag.value = (int)DuelUtils.GetValidPlayerFlag(
            duelParams != null ? duelParams.lanHostPlayerFlag : 0);
    }

    private void EnsureTimeControlConfig(SceneComponentDuel compDuel)
    {
        compDuel.holdTimeCfgId.value = GetValidHoldTimeCfgId(compDuel.holdTimeCfgId.value);
        compDuel.byoyomiCountCfgId.value = GetValidByoyomiCountCfgId(compDuel.byoyomiCountCfgId.value);
        compDuel.byoyomiTimeCfgId.value = GetValidByoyomiTimeCfgId(compDuel.byoyomiTimeCfgId.value);
    }

    private void EnsureHandicapConfig(SceneComponentDuel compDuel)
    {
        compDuel.handicapCfgId.value = GetValidHandicapCfgId(compDuel.handicapCfgId.value);
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

    private void EnsureLanDuelConfig(SceneComponentDuel compDuel)
    {
        if (!compDuel.isLanDuel.value) {
            compDuel.lanRole.value = (int)LanRoomRole.None;
            compDuel.lanHostPlayerFlag.value = (int)PlayerFlag.Player1;
            compDuel.lanBoardVersion.value = 0;
            return;
        }

        if (compDuel.lanRole.value != (int)LanRoomRole.Host && compDuel.lanRole.value != (int)LanRoomRole.Client) {
            compDuel.lanRole.value = (int)LanRoomRole.None;
        }
        compDuel.lanHostPlayerFlag.value = (int)DuelUtils.GetValidPlayerFlag((PlayerFlag)compDuel.lanHostPlayerFlag.value);
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

    private string GetValidHandicapCfgId(string cfgId)
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        string boardCfgId = compChessBoard?.boardCfgId.value;
        return DuelHandicapPlacement.GetValidCfgId(cfgId, boardCfgId);
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

    private void ApplyInitialHandicapStones(SceneComponentDuel compDuel)
    {
        if (compDuel == null || !DuelHandicapPlacement.HasHandicap(compDuel.handicapCfgId.value)) {
            return;
        }

        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        DuelHandicapPlacement.ApplyInitialStones(scene, compChessBoard, compDuel.handicapCfgId.value);
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
            if (compDuel.isLanDuel.value && compDuel.lanRole.value == (int)LanRoomRole.Client) {
                return;
            }

            compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
        }
    }

    private void OnApplyLanDuelTimeState(OnApplyLanDuelTimeState evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || evt == null) {
            return;
        }

        Player player = GetPlayerByFlag(compDuel, evt.timeState.playerFlag);
        ComponentDuelInfo compDuelInfo = player?.GetComponent<ComponentDuelInfo>();
        if (compDuelInfo == null || compDuelInfo.isInfiniteTime.value) {
            return;
        }

        compDuelInfo.holdLeftSeconds.value = evt.timeState.holdLeftSeconds;
        compDuelInfo.byoyomiLeftCount.value = evt.timeState.byoyomiLeftCount;
        compDuelInfo.byoyomiLeftSeconds.value = evt.timeState.byoyomiLeftSeconds;
        compDuelInfo.isInByoyomi.value = evt.timeState.isInByoyomi;
        compDuelInfo.turnLeftTimes.value = evt.timeState.turnLeftTimes;
    }

    private void OnApplyLanDuelTimeout(OnApplyLanDuelTimeout evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || evt == null) {
            return;
        }

        Player loser = GetPlayerByFlag(compDuel, evt.loserFlag);
        if (loser == null) {
            return;
        }

        compDuel.timeoutLoserGuid.value = loser.guid;
        compDuel.gameEndReason.value = DuelGameEndReason.Timeout;
        compDuel.winnerGuid.value = loser.guid == compDuel.player1Guid.value
            ? compDuel.player2Guid.value
            : compDuel.player1Guid.value;
        compDuel.localInputPlayerFlag.value = 0;
        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

    private void OnApplyLanDuelResign(OnApplyLanDuelResign evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || evt == null) {
            return;
        }

        Player loser = GetPlayerByFlag(compDuel, evt.loserFlag);
        if (loser == null) {
            return;
        }

        EndGameByResign(compDuel, loser.guid);
    }

    private void OnApplyLanDuelPass(OnApplyLanDuelPass evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || evt == null) {
            return;
        }

        ApplyAcceptedPass(compDuel, evt.pass);
    }

    private void OnApplyLanDuelScoreRequest(OnApplyLanDuelScoreRequest evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || evt == null) {
            return;
        }

        compDuel.isScoring = true;
        compDuel.localInputPlayerFlag.value = 0;
    }

    private void OnApplyLanDuelScoreResult(OnApplyLanDuelScoreResult evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || evt == null) {
            return;
        }

        compDuel.isScoring = false;
        EndGameByScore(BuildScoreResult(evt.result), DuelGameEndReason.Score);
    }

    private void OnApplyLanDuelScoreFailed(OnApplyLanDuelScoreFailed evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value) {
            return;
        }

        compDuel.isScoring = false;
        scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
        LanDuelScoreFailureReason reason = evt != null ? evt.failure.reason : LanDuelScoreFailureReason.Unknown;
        scene.EmitSystemEvent(new OnDuelScoreFailed(false, BuildLanScoreFailureMessage(reason)));
    }

    private void OnApplyLanDuelTakeBack(OnApplyLanDuelTakeBack evt)
    {
        if (evt == null || !ApplyLanDuelTakeBack(evt.request)) {
            EmitTakeBackResult(false, MessageText.Get("duel_take_back_failed"));
        }
    }

    private void OnLanDuelTakeBackRejected(OnLanDuelTakeBackRejected evt)
    {
        EmitTakeBackResult(false, MessageText.Get("duel_take_back_opponent_rejected"));
        scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
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

        DuelScoreResult scoreResult = await QueryScoreResult(compDuel, "duel-score");
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

        EndGameByResign(compDuel, loserGuid);
    }

    private void OnRequestDuelTakeBack(OnRequestDuelTakeBack evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            EmitTakeBackResult(false, MessageText.Get("duel_take_back_unavailable"));
            return;
        }

        if (compDuel.isScoring) {
            EmitTakeBackResult(false, MessageText.Get("duel_take_back_scoring_blocked"));
            return;
        }

        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);
        int removeCount = GetTakeBackMoveCount(compDuel);
        if (removeCount <= 0 || moveCount < removeCount) {
            EmitTakeBackResult(false, MessageText.Get("duel_take_back_no_moves"));
            return;
        }

        JArray originalMoves = DuelMoveHistory.Clone(compDuel.kataGoMoves, moveCount);
        JArray remainMoves = DuelMoveHistory.TakeAfterRemovingLast(compDuel.kataGoMoves, removeCount);

        if (!RebuildBoardFromMoves(compChessBoard, compDuel, remainMoves)) {
            XNLogger.LogError("Duel take back failed, board rebuild failed.");
            RebuildBoardFromMoves(compChessBoard, compDuel, originalMoves);
            EmitTakeBackResult(false, MessageText.Get("duel_take_back_failed"));
            return;
        }

        compDuel.curTurnPlayerGuid.value = GetNextTurnPlayerGuid(compDuel, remainMoves.Count);
        compDuel.timeoutLoserGuid.value = string.Empty;
        compDuel.resignLoserGuid.value = string.Empty;
        compDuel.winnerGuid.value = string.Empty;
        compDuel.localInputPlayerFlag.value = 0;
        compDuel.gameEndReason.value = string.Empty;
        compDuel.finalBlackScore.value = 0f;
        compDuel.finalWhiteScore.value = 0f;
        compDuel.finalScoreMargin.value = 0f;
        compDuel.consecutivePassCount.value = DuelMoveHistory.CountTrailingPasses(compDuel.kataGoMoves);
        compDuel.ClearOwnershipScoreCache();
        compChessBoard.chessBoardGrid?.ClearOwnership();

        compDuel.duelFSM.SwitchState(DuelStateDefine.STATE_TURN_INPUT);
        EmitTakeBackResult(true, BuildTakeBackSuccessMessage(removeCount), removeCount);
    }

    public bool CanAcceptLanDuelTakeBack(SceneComponentDuel compDuel, LanDuelTakeBackRequestMessage request)
    {
        if (compDuel == null || request.removeCount <= 0 || request.boardVersion != compDuel.lanBoardVersion.value) {
            return false;
        }

        if (compDuel.isScoring || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return false;
        }

        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);
        return moveCount >= request.removeCount;
    }

    public bool ApplyLanDuelTakeBack(LanDuelTakeBackRequestMessage request)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel != null && compDuel.lanBoardVersion.value == request.boardVersion + 1) {
            EmitTakeBackResult(true, BuildTakeBackSuccessMessage(request.removeCount), request.removeCount);
            return true;
        }

        if (!CanAcceptLanDuelTakeBack(compDuel, request) || compChessBoard == null) {
            return false;
        }

        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);
        JArray remainMoves = DuelMoveHistory.TakeAfterRemovingLast(compDuel.kataGoMoves, request.removeCount);
        if (!RebuildBoardFromMoves(compChessBoard, compDuel, remainMoves)) {
            return false;
        }

        compDuel.curTurnPlayerGuid.value = GetNextTurnPlayerGuid(compDuel, remainMoves.Count);
        compDuel.timeoutLoserGuid.value = string.Empty;
        compDuel.resignLoserGuid.value = string.Empty;
        compDuel.winnerGuid.value = string.Empty;
        compDuel.localInputPlayerFlag.value = 0;
        compDuel.gameEndReason.value = string.Empty;
        compDuel.finalBlackScore.value = 0f;
        compDuel.finalWhiteScore.value = 0f;
        compDuel.finalScoreMargin.value = 0f;
        compDuel.consecutivePassCount.value = DuelMoveHistory.CountTrailingPasses(compDuel.kataGoMoves);
        compDuel.lanBoardVersion.value = request.boardVersion + 1;
        compDuel.ClearOwnershipScoreCache();
        compChessBoard.chessBoardGrid?.ClearOwnership();

        compDuel.duelFSM.SwitchState(DuelStateDefine.STATE_TURN_INPUT);
        EmitTakeBackResult(true, BuildTakeBackSuccessMessage(request.removeCount), request.removeCount);
        return moveCount > DuelMoveHistory.Count(compDuel.kataGoMoves);
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
        ApplyPassToState(compDuel, curPlayer, curPlayerFlag, compDuel.consecutivePassCount.value + 1);

        if (compDuel.consecutivePassCount.value >= 2) {
            DuelScoreResult scoreResult = null;
            try {
                compDuel.isScoring = true;
                scoreResult = await DuelOwnershipQueryService.QueryScoreResultAsync(scene as DuelScene, "duel-consecutive-pass-score");
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

    public bool CanAcceptLanDuelPass(SceneComponentDuel compDuel, LanDuelPassMessage pass)
    {
        return CanAcceptTurnCommand(compDuel, pass.playerFlag)
            && pass.boardVersion == compDuel.lanBoardVersion.value;
    }

    public bool CanAcceptLanDuelScore(SceneComponentDuel compDuel, LanDuelScoreRequestMessage request)
    {
        return CanAcceptTurnCommand(compDuel, request.requesterFlag)
            && request.boardVersion == compDuel.lanBoardVersion.value;
    }

    public LanDuelPassMessage AcceptLanDuelPass(LanDuelPassMessage pass)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return pass;
        }

        int nextConsecutivePassCount = compDuel.consecutivePassCount.value + 1;
        LanDuelPassMessage acceptedPass = new LanDuelPassMessage(
            pass.actionId,
            compDuel.lanBoardVersion.value,
            pass.playerFlag,
            nextConsecutivePassCount);
        ApplyAcceptedPass(compDuel, acceptedPass);
        return acceptedPass;
    }

    public void AcceptLanDuelScoreRequest(LanDuelScoreRequestMessage request)
    {
        scene.EmitSystemEvent(new OnApplyLanDuelScoreRequest(request));
        QueryAndBroadcastLanScore(request);
    }

    private bool CanAcceptTurnCommand(SceneComponentDuel compDuel, PlayerFlag playerFlag)
    {
        if (compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated || compDuel.isScoring) {
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        return curPlayer != null && (PlayerFlag)curPlayer.playerFlag.value == playerFlag;
    }

    private void ApplyAcceptedPass(SceneComponentDuel compDuel, LanDuelPassMessage pass)
    {
        if (compDuel == null || pass.playerFlag == 0) {
            return;
        }

        if (compDuel.duelFSM?.curState == null || compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_GAME_END) {
            return;
        }

        Player player = GetPlayerByFlag(compDuel, pass.playerFlag);
        if (player == null) {
            return;
        }

        ApplyPassToState(compDuel, player, pass.playerFlag, pass.consecutivePassCount);
        compDuel.lanBoardVersion.value = pass.boardVersion;
        if (compDuel.consecutivePassCount.value >= 2) {
            compDuel.localInputPlayerFlag.value = 0;
            return;
        }

        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
    }

    private void ApplyPassToState(SceneComponentDuel compDuel, Player player, PlayerFlag playerFlag, int consecutivePassCount)
    {
        compDuel.AppendKataGoPass(playerFlag);
        compDuel.consecutivePassCount.value = consecutivePassCount;
        compDuel.ClearOwnershipScoreCache();
        scene.EmitSystemEvent(new OnClearDuelOwnership());
        scene.EmitSystemEvent(new OnDuelPassAccepted(
            player.guid,
            playerFlag,
            compDuel.isAiDuel.value && player.guid == compDuel.aiPlayerGuid.value,
            compDuel.consecutivePassCount.value
        ));
    }

    private async void QueryAndBroadcastLanScore(LanDuelScoreRequestMessage request)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || compDuel.lanRole.value != (int)LanRoomRole.Host) {
            return;
        }

        DuelScoreResult scoreResult = await QueryScoreResult(compDuel, "lan-duel-score");
        if (scoreResult == null) {
            Global.Instance.lanRoomService?.BroadcastScoreFailed(
                request.actionId,
                request.requesterFlag,
                LanDuelScoreFailureReason.CalculationFailed);
            return;
        }

        Global.Instance.lanRoomService?.BroadcastScoreResult(BuildLanScoreResultMessage(request, scoreResult));
    }

    private async System.Threading.Tasks.Task<DuelScoreResult> QueryScoreResult(SceneComponentDuel compDuel, string requestTag)
    {
        DuelScoreResult scoreResult = null;
        try {
            compDuel.isScoring = true;
            compDuel.localInputPlayerFlag.value = 0;
            scoreResult = await DuelOwnershipQueryService.QueryScoreResultAsync(scene as DuelScene, requestTag);
        }
        finally {
            compDuel.isScoring = false;
        }

        return scoreResult;
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
        ApplyInitialHandicapStones(compDuel);

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
        bool startsFromWhite = DuelHandicapPlacement.HasHandicap(compDuel.handicapCfgId.value);
        if (startsFromWhite) {
            return moveCount % 2 == 0 ? compDuel.player2Guid.value : compDuel.player1Guid.value;
        }

        return moveCount % 2 == 0 ? compDuel.player1Guid.value : compDuel.player2Guid.value;
    }

    private Player GetPlayerByFlag(SceneComponentDuel compDuel, PlayerFlag playerFlag)
    {
        if (compDuel == null) {
            return null;
        }

        if (playerFlag == PlayerFlag.Player1) {
            return scene.GetEntity<Player>(compDuel.player1Guid.value);
        }
        if (playerFlag == PlayerFlag.Player2) {
            return scene.GetEntity<Player>(compDuel.player2Guid.value);
        }

        return null;
    }

    private string GetPlayerGuidByFlag(SceneComponentDuel compDuel, PlayerFlag playerFlag)
    {
        if (compDuel == null) {
            return string.Empty;
        }

        if (playerFlag == PlayerFlag.Player1) {
            return compDuel.player1Guid.value;
        }
        if (playerFlag == PlayerFlag.Player2) {
            return compDuel.player2Guid.value;
        }

        return string.Empty;
    }

    private void EmitTakeBackResult(bool success, string message, int removedMoveCount = 0)
    {
        scene.EmitSystemEvent(new OnDuelTakeBackResult(success, message, removedMoveCount));
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
        compDuel.localInputPlayerFlag.value = 0;

        if (scoreResult.winnerFlag == PlayerFlag.Player1) {
            compDuel.winnerGuid.value = compDuel.player1Guid.value;
        } else if (scoreResult.winnerFlag == PlayerFlag.Player2) {
            compDuel.winnerGuid.value = compDuel.player2Guid.value;
        } else {
            compDuel.winnerGuid.value = string.Empty;
        }

        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

    private void EndGameByResign(SceneComponentDuel compDuel, string loserGuid)
    {
        if (compDuel == null || string.IsNullOrEmpty(loserGuid)) {
            return;
        }

        string winnerGuid = loserGuid == compDuel.player1Guid.value
            ? compDuel.player2Guid.value
            : compDuel.player1Guid.value;

        compDuel.resignLoserGuid.value = loserGuid;
        compDuel.winnerGuid.value = winnerGuid;
        compDuel.gameEndReason.value = DuelGameEndReason.Resign;
        compDuel.localInputPlayerFlag.value = 0;
        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

    private LanDuelScoreResultMessage BuildLanScoreResultMessage(LanDuelScoreRequestMessage request, DuelScoreResult scoreResult)
    {
        return new LanDuelScoreResultMessage(
            request.actionId,
            request.requesterFlag,
            scoreResult.blackScore,
            scoreResult.whiteScore,
            scoreResult.komi,
            scoreResult.margin,
            scoreResult.winnerFlag,
            scoreResult.scoreSource);
    }

    private DuelScoreResult BuildScoreResult(LanDuelScoreResultMessage result)
    {
        return new DuelScoreResult
        {
            blackScore = result.blackScore,
            whiteScore = result.whiteScore,
            komi = result.komi,
            margin = result.margin,
            winnerFlag = result.winnerFlag,
            scoreSource = result.scoreSource,
        };
    }

    private string BuildLanScoreFailureMessage(LanDuelScoreFailureReason reason)
    {
        if (reason == LanDuelScoreFailureReason.RequestRejected) {
            return MessageText.Get("duel_score_opponent_rejected");
        }
        if (reason == LanDuelScoreFailureReason.ResultRejected) {
            return MessageText.Get("duel_score_result_rejected");
        }
        if (reason == LanDuelScoreFailureReason.InvalidRequest) {
            return MessageText.Get("duel_score_request_unavailable");
        }

        return MessageText.Get("duel_score_failed_back_to_game");
    }

    private string BuildTakeBackSuccessMessage(int removeCount)
    {
        return removeCount >= 2
            ? MessageText.Get("duel_take_back_two_moves_success")
            : MessageText.Get("duel_take_back_success");
    }

}
