using System;
using System.Threading.Tasks;
using XNClient.Logger;

public class DuelAiSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelAiSystem>();

    private int requestVersion;
    private bool isThinking;

    public DuelAiSystem(DuelScene scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnRequestDuelPass>(OnRequestDuelPass);
        XNLogger.LogInfo("Duel AI system initialized.");
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        XNLogger.LogInfo(
            "Duel AI observed board move.",
            ("playerFlag", evt.playerFlag.ToString()),
            ("coords", evt.coords?.ToString() ?? "null"),
            ("requestVersion", requestVersion.ToString()));
    }

    private void OnRequestDuelPass(OnRequestDuelPass evt)
    {
        XNLogger.LogInfo("Duel AI observed pass request.", ("requestVersion", requestVersion.ToString()));
    }

    private void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        requestVersion += 1;
        XNLogger.LogInfo(
            "Duel AI observed duel state changed.",
            ("state", evt.curStateName),
            ("requestVersion", requestVersion.ToString()),
            ("turnInfo", BuildTurnInfoLog()));
        if (evt.curStateName == DuelStateDefine.STATE_TURN_INPUT) {
            TryStartAiTurn();
        }
    }

    private async void TryStartAiTurn()
    {
        if (isThinking) {
            XNLogger.LogInfo("Duel AI turn start skipped, already thinking.", ("requestVersion", requestVersion.ToString()));
            return;
        }

        if (!IsAiTurn(out string skipReason)) {
            XNLogger.LogInfo(
                "Duel AI turn start skipped.",
                ("reason", skipReason),
                ("turnInfo", BuildTurnInfoLog()));
            return;
        }

        int currentRequestVersion = requestVersion;
        isThinking = true;
        try {
            SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
            DuelAiDifficultyDataType difficultyData = DuelAiDifficultyDataType.GetConfigData(compDuel.aiDifficultyCfgId.value);
            if (difficultyData == null) {
                XNLogger.LogError("Duel AI move failed, difficulty config not found.", ("cfgId", compDuel.aiDifficultyCfgId.value));
                return;
            }

            DuelAiRuntimeParams runtimeParams = DuelAiBudgetService.ResolveRuntimeParams(difficultyData, GetBoardSize(), GetMoveCount());
            XNLogger.LogInfo(
                "Duel AI turn started.",
                ("cfgId", compDuel.aiDifficultyCfgId.value),
                ("cfgName", difficultyData.name ?? string.Empty),
                ("thinkDelayMs", difficultyData.thinkDelayMs.ToString()),
                ("boardSize", runtimeParams.boardSize.ToString()),
                ("configuredMaxVisits", runtimeParams.configuredMaxVisits.ToString()),
                ("requestMaxVisits", runtimeParams.requestMaxVisits.ToString()),
                ("configuredCandidateLimit", runtimeParams.configuredCandidateLimit.ToString()),
                ("requestCandidateLimit", runtimeParams.requestCandidateLimit.ToString()),
                ("configuredMaxScoreLoss", runtimeParams.configuredMaxScoreLoss.ToString()),
                ("requestMaxScoreLoss", runtimeParams.requestMaxScoreLoss.ToString()),
                ("requestVersion", currentRequestVersion.ToString()),
                ("turnInfo", BuildTurnInfoLog()));

            if (difficultyData.thinkDelayMs > 0) {
                await Task.Delay(difficultyData.thinkDelayMs);
            }

            if (currentRequestVersion != requestVersion || !IsAiTurn(out skipReason)) {
                XNLogger.LogWarn(
                    "Duel AI move canceled before analyze.",
                    ("reason", currentRequestVersion != requestVersion ? "request version changed" : skipReason),
                    ("startVersion", currentRequestVersion.ToString()),
                    ("currentVersion", requestVersion.ToString()),
                    ("turnInfo", BuildTurnInfoLog()));
                return;
            }

            DuelAiAnalyzeResult analyzeResult = await DuelAiAnalyzeService.AnalyzeAiMoveAsync((DuelScene)scene, difficultyData, runtimeParams);
            if (currentRequestVersion != requestVersion || !IsAiTurn(out skipReason)) {
                XNLogger.LogWarn(
                    "Duel AI move canceled after analyze.",
                    ("reason", currentRequestVersion != requestVersion ? "request version changed" : skipReason),
                    ("startVersion", currentRequestVersion.ToString()),
                    ("currentVersion", requestVersion.ToString()),
                    ("turnInfo", BuildTurnInfoLog()));
                return;
            }

            DuelAiTurnDecision decision = DuelAiMoveSelector.SelectTurnDecision(scene, analyzeResult.result, difficultyData, runtimeParams);
            if (decision.type == DuelAiTurnDecisionType.Move && decision.coords != null) {
                XNLogger.LogInfo(
                    "Duel AI move selected.",
                    ("coords", decision.coords.ToString()),
                    ("reason", decision.reason ?? string.Empty),
                    ("requestId", analyzeResult.requestId ?? string.Empty),
                    ("budgetMode", analyzeResult.budgetMode ?? string.Empty),
                    ("budgetDecision", analyzeResult.decisionReason ?? string.Empty));
                scene.EmitSystemEvent(new OnAddChessToBoard(decision.coords));
                return;
            }

            if (decision.type == DuelAiTurnDecisionType.Pass) {
                XNLogger.LogInfo(
                    "Duel AI requests pass.",
                    ("reason", decision.reason ?? string.Empty),
                    ("requestId", analyzeResult.requestId ?? string.Empty),
                    ("budgetMode", analyzeResult.budgetMode ?? string.Empty),
                    ("budgetDecision", analyzeResult.decisionReason ?? string.Empty));
                scene.EmitSystemEvent(new OnRequestDuelPass());
                return;
            }

            bool isBoardFull = IsBoardFull();
            if (difficultyData.allowPassBeforeEndgame || isBoardFull) {
                XNLogger.LogWarn(
                    "Duel AI requests pass.",
                    ("allowPassBeforeEndgame", difficultyData.allowPassBeforeEndgame.ToString()),
                    ("isBoardFull", isBoardFull.ToString()),
                    ("requestId", analyzeResult.requestId ?? string.Empty),
                    ("reason", decision.reason ?? string.Empty));
                scene.EmitSystemEvent(new OnRequestDuelPass());
                return;
            }

            XNLogger.LogWarn(
                "Duel AI move skipped, no playable decision found.",
                ("requestId", analyzeResult.requestId ?? string.Empty),
                ("reason", decision.reason ?? string.Empty));
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel AI move failed.", ("err", ex.Message), ("stack", ex.StackTrace ?? string.Empty));
        }
        finally {
            isThinking = false;
            XNLogger.LogInfo("Duel AI thinking finished.", ("requestVersion", requestVersion.ToString()), ("turnInfo", BuildTurnInfoLog()));
        }
    }

    private bool IsAiTurn(out string reason)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            reason = "duel component missing";
            return false;
        }

        if (!compDuel.isAiDuel.value) {
            reason = "not ai duel";
            return false;
        }

        if (compDuel.duelFSM == null) {
            reason = "duel fsm missing";
            return false;
        }

        if (!compDuel.duelFSM.isActivated) {
            reason = "duel fsm not activated";
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            reason = compDuel.duelFSM.curState == null ? "duel fsm state missing" : $"state is {compDuel.duelFSM.curState.stateName}";
            return false;
        }

        if (string.IsNullOrEmpty(compDuel.aiPlayerGuid.value)) {
            reason = "ai player guid empty";
            return false;
        }

        if (compDuel.curTurnPlayerGuid.value != compDuel.aiPlayerGuid.value) {
            reason = "current turn is human";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private int GetMoveCount()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        return DuelMoveHistory.Count(compDuel?.kataGoMoves);
    }

    private int GetBoardSize()
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard?.chessBoardGrid != null) {
            return compChessBoard.chessBoardGrid.gridSize;
        }

        if (compChessBoard != null && !string.IsNullOrEmpty(compChessBoard.boardCfgId.value)) {
            ChessBoardDataType chessBoardData = ChessBoardDataType.GetConfigData(compChessBoard.boardCfgId.value);
            if (chessBoardData != null) {
                return chessBoardData.boardSize;
            }
        }

        return 19;
    }

    private bool IsBoardFull()
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard == null) {
            return false;
        }

        return compChessBoard.chessInfoDict.Count >= compChessBoard.GetGridMaxSize();
    }

    private string BuildTurnInfoLog()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return "duel=null";
        }

        string stateName = compDuel.duelFSM?.curState?.stateName ?? "null";
        return $"isAiDuel={compDuel.isAiDuel.value},state={stateName},activated={compDuel.duelFSM?.isActivated.ToString() ?? "null"},cur={compDuel.curTurnPlayerGuid.value},ai={compDuel.aiPlayerGuid.value},p1={compDuel.player1Guid.value},p2={compDuel.player2Guid.value},cfg={compDuel.aiDifficultyCfgId.value}";
    }
}
