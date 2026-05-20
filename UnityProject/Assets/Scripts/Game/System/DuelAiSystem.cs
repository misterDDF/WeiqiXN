using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;
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

            AiRuntimeParams runtimeParams = ResolveRuntimeParams(difficultyData);
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

            AiAnalyzeResult analyzeResult = await AnalyzeAiMoveAsync(difficultyData, runtimeParams);
            JObject result = analyzeResult.result;
            if (currentRequestVersion != requestVersion || !IsAiTurn(out skipReason)) {
                XNLogger.LogWarn(
                    "Duel AI move canceled after analyze.",
                    ("reason", currentRequestVersion != requestVersion ? "request version changed" : skipReason),
                    ("startVersion", currentRequestVersion.ToString()),
                    ("currentVersion", requestVersion.ToString()),
                    ("turnInfo", BuildTurnInfoLog()));
                return;
            }

            RectCoordinates coords = SelectMove(result, difficultyData, runtimeParams);
            if (coords != null) {
                XNLogger.LogInfo(
                    "Duel AI move selected.",
                    ("coords", coords.ToString()),
                    ("requestId", analyzeResult.requestId ?? string.Empty),
                    ("budgetMode", analyzeResult.budgetMode ?? string.Empty),
                    ("budgetDecision", analyzeResult.decisionReason ?? string.Empty));
                scene.EmitSystemEvent(new OnAddChessToBoard(coords));
                return;
            }

            if (difficultyData.allowPassBeforeEndgame || IsBoardFull()) {
                XNLogger.LogWarn(
                    "Duel AI requests pass.",
                    ("allowPassBeforeEndgame", difficultyData.allowPassBeforeEndgame.ToString()),
                    ("isBoardFull", IsBoardFull().ToString()),
                    ("requestId", analyzeResult.requestId ?? string.Empty));
                scene.EmitSystemEvent(new OnRequestDuelPass());
                return;
            }

            XNLogger.LogWarn("Duel AI move skipped, no playable candidate found.", ("requestId", analyzeResult.requestId ?? string.Empty));
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

    private async Task<AiAnalyzeResult> AnalyzeAiMoveAsync(DuelAiDifficultyDataType difficultyData, AiRuntimeParams runtimeParams)
    {
        int fullMaxVisits = runtimeParams.requestMaxVisits;
        if (!runtimeParams.dynamicBudgetEnabled || runtimeParams.probeMaxVisits >= fullMaxVisits) {
            string reason = runtimeParams.dynamicBudgetEnabled ? "probe_budget_not_lower" : "dynamic_disabled";
            JObject fullResult = await RequestAiAnalyzeAsync(difficultyData, runtimeParams, fullMaxVisits, "full", reason);
            return new AiAnalyzeResult
            {
                result = fullResult,
                requestId = GetResultRequestId(fullResult),
                budgetMode = "full",
                decisionReason = reason,
            };
        }

        JObject probeResult = await RequestAiAnalyzeAsync(difficultyData, runtimeParams, runtimeParams.probeMaxVisits, "probe", "dynamic_probe");
        AiBudgetDecision decision = DecideBudgetAfterProbe(probeResult, runtimeParams);
        LogBudgetDecision(decision, runtimeParams);

        if (!decision.useFullBudget) {
            return new AiAnalyzeResult
            {
                result = probeResult,
                requestId = GetResultRequestId(probeResult),
                budgetMode = "probe",
                decisionReason = decision.reason,
            };
        }

        JObject fullResultAfterProbe = await RequestAiAnalyzeAsync(difficultyData, runtimeParams, fullMaxVisits, "full", decision.reason);
        return new AiAnalyzeResult
        {
            result = fullResultAfterProbe,
            requestId = GetResultRequestId(fullResultAfterProbe),
            budgetMode = "full",
            decisionReason = decision.reason,
        };
    }

    private async Task<JObject> RequestAiAnalyzeAsync(DuelAiDifficultyDataType difficultyData, AiRuntimeParams runtimeParams, int maxVisits, string budgetMode, string reason)
    {
        string requestId = $"duel-ai-{budgetMode}-{DateTime.UtcNow.Ticks}";
        JObject query = KataGoPositionJsonBuilder.BuildAiMoveAnalysisJson((DuelScene)scene, requestId, difficultyData, maxVisits);
        XNLogger.LogInfo(
            budgetMode == "probe" ? "Duel AI probe analyze requested." : "Duel AI analyze requested.",
            ("requestId", requestId),
            ("reason", reason ?? string.Empty),
            ("moveCount", ((query["moves"] as JArray)?.Count ?? 0).ToString()),
            ("analyzeTurns", query["analyzeTurns"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "null"),
            ("boardSize", runtimeParams.boardSize.ToString()),
            ("budgetMode", budgetMode),
            ("configuredMaxVisits", runtimeParams.configuredMaxVisits.ToString()),
            ("requestMaxVisits", maxVisits.ToString()),
            ("fullMaxVisits", runtimeParams.requestMaxVisits.ToString()),
            ("probeMaxVisits", runtimeParams.probeMaxVisits.ToString()),
            ("configuredCandidateLimit", runtimeParams.configuredCandidateLimit.ToString()),
            ("requestCandidateLimit", runtimeParams.requestCandidateLimit.ToString()),
            ("configuredMaxScoreLoss", runtimeParams.configuredMaxScoreLoss.ToString()),
            ("requestMaxScoreLoss", runtimeParams.requestMaxScoreLoss.ToString()),
            ("includePolicy", query["includePolicy"]?.ToString() ?? "null"),
            ("humanProfileRequested", difficultyData.useHumanPolicy.ToString()),
            ("humanProfileEnabled", KataGoBootstrap.CanUseHumanSlProfile().ToString()),
            ("humanSLProfile", difficultyData.humanSLProfile ?? "none"),
            ("humanProfileSent", (query["overrideSettings"]?["humanSLProfile"] != null).ToString()));

        JObject result = await KataGoBootstrap.AnalyzeAsync(query);
        if (budgetMode == "probe") {
            LogProbeResult(result, runtimeParams);
        }

        return result;
    }

    private AiBudgetDecision DecideBudgetAfterProbe(JObject result, AiRuntimeParams runtimeParams)
    {
        AiProbeStats stats = BuildProbeStats(result);
        if (result == null) {
            return BuildBudgetDecision(true, "missing_probe_result", stats, runtimeParams);
        }

        if (!string.IsNullOrEmpty(stats.error)) {
            return BuildBudgetDecision(true, "probe_error", stats, runtimeParams);
        }

        if (stats.moveInfoCount < runtimeParams.probeMinMoveInfoCount) {
            return BuildBudgetDecision(true, "probe_candidates_insufficient", stats, runtimeParams);
        }

        if (runtimeParams.moveCount <= runtimeParams.openingProbeMoveLimit) {
            return BuildBudgetDecision(false, "opening", stats, runtimeParams);
        }

        if (!stats.hasRootInfo) {
            return BuildBudgetDecision(true, "missing_root_info", stats, runtimeParams);
        }

        if (stats.hasCandidateGap && stats.candidateGap >= runtimeParams.confidentBestMoveGapThreshold) {
            return BuildBudgetDecision(false, "confident_best", stats, runtimeParams);
        }

        if (runtimeParams.forceFullBudgetMoveLimit > 0 && runtimeParams.moveCount >= runtimeParams.forceFullBudgetMoveLimit) {
            return BuildBudgetDecision(true, "late_game", stats, runtimeParams);
        }

        if (stats.hasScoreLead
            && stats.hasWinrate
            && stats.hasCandidateGap
            && stats.scoreLeadAbs <= runtimeParams.closeScoreLeadThreshold
            && stats.winrateDistance <= runtimeParams.closeWinrateThreshold
            && stats.candidateGap <= runtimeParams.simpleCandidateGapThreshold) {
            return BuildBudgetDecision(false, "close_position", stats, runtimeParams);
        }

        return BuildBudgetDecision(true, "complex_position", stats, runtimeParams);
    }

    private AiProbeStats BuildProbeStats(JObject result)
    {
        AiProbeStats stats = new AiProbeStats();
        if (result == null) {
            return stats;
        }

        stats.requestId = result["id"]?.ToString() ?? string.Empty;
        stats.warning = result["warning"]?.ToString() ?? string.Empty;
        stats.error = result["error"]?.ToString() ?? string.Empty;

        JArray moveInfos = result["moveInfos"] as JArray;
        stats.moveInfoCount = moveInfos?.Count ?? 0;
        if (moveInfos != null && moveInfos.Count > 0) {
            stats.topMove = moveInfos[0]?["move"]?.ToString() ?? string.Empty;
            stats.topScoreLoss = ParseFloat(moveInfos[0]?["scoreLoss"]);
            if (moveInfos.Count > 1) {
                stats.secondMove = moveInfos[1]?["move"]?.ToString() ?? string.Empty;
                stats.secondScoreLoss = ParseFloat(moveInfos[1]?["scoreLoss"]);
                stats.candidateGap = Mathf.Abs(stats.secondScoreLoss - stats.topScoreLoss);
                stats.hasCandidateGap = true;
            }
        }

        JObject rootInfo = result["rootInfo"] as JObject;
        stats.hasRootInfo = rootInfo != null;
        if (rootInfo != null) {
            stats.rootVisits = ParseInt(rootInfo["visits"]);
            if (TryParseFloat(rootInfo["scoreLead"], out stats.scoreLead)) {
                stats.scoreLeadAbs = Mathf.Abs(stats.scoreLead);
                stats.hasScoreLead = true;
            }

            if (TryParseFloat(rootInfo["winrate"], out stats.winrate)) {
                stats.winrateDistance = Mathf.Abs(stats.winrate - 0.5f);
                stats.hasWinrate = true;
            }
        }

        return stats;
    }

    private AiBudgetDecision BuildBudgetDecision(bool useFullBudget, string reason, AiProbeStats stats, AiRuntimeParams runtimeParams)
    {
        stats.moveCount = runtimeParams.moveCount;
        return new AiBudgetDecision
        {
            useFullBudget = useFullBudget,
            reason = reason,
            stats = stats,
            runtimeParams = runtimeParams,
        };
    }

    private void LogProbeResult(JObject result, AiRuntimeParams runtimeParams)
    {
        AiProbeStats stats = BuildProbeStats(result);
        XNLogger.LogInfo(
            "Duel AI probe analyze result.",
            ("requestId", stats.requestId),
            ("boardSize", runtimeParams.boardSize.ToString()),
            ("moveCount", runtimeParams.moveCount.ToString()),
            ("moveInfoCount", stats.moveInfoCount.ToString()),
            ("rootVisits", stats.rootVisits.ToString()),
            ("scoreLead", stats.hasScoreLead ? stats.scoreLead.ToString() : "null"),
            ("winrate", stats.hasWinrate ? stats.winrate.ToString() : "null"),
            ("topMove", stats.topMove ?? string.Empty),
            ("topScoreLoss", stats.topScoreLoss.ToString()),
            ("secondMove", stats.secondMove ?? string.Empty),
            ("secondScoreLoss", stats.secondScoreLoss.ToString()),
            ("candidateGap", stats.hasCandidateGap ? stats.candidateGap.ToString() : "null"),
            ("warning", stats.warning ?? string.Empty),
            ("error", stats.error ?? string.Empty));
    }

    private void LogBudgetDecision(AiBudgetDecision decision, AiRuntimeParams runtimeParams)
    {
        AiProbeStats stats = decision.stats;
        XNLogger.LogInfo(
            "Duel AI budget decision.",
            ("decision", decision.useFullBudget ? "upgrade_full" : "use_probe"),
            ("reason", decision.reason ?? string.Empty),
            ("moveCount", stats.moveCount.ToString()),
            ("boardSize", runtimeParams.boardSize.ToString()),
            ("scoreLeadAbs", stats.hasScoreLead ? stats.scoreLeadAbs.ToString() : "null"),
            ("winrateDistance", stats.hasWinrate ? stats.winrateDistance.ToString() : "null"),
            ("candidateGap", stats.hasCandidateGap ? stats.candidateGap.ToString() : "null"),
            ("probeMaxVisits", runtimeParams.probeMaxVisits.ToString()),
            ("fullMaxVisits", runtimeParams.requestMaxVisits.ToString()),
            ("openingProbeMoveLimit", runtimeParams.openingProbeMoveLimit.ToString()),
            ("forceFullBudgetMoveLimit", runtimeParams.forceFullBudgetMoveLimit.ToString()));
    }

    private AiRuntimeParams ResolveRuntimeParams(DuelAiDifficultyDataType difficultyData)
    {
        int boardSize = GetBoardSize();
        int configuredMaxVisits = Mathf.Max(difficultyData.maxVisits, 1);
        int configuredCandidateLimit = Mathf.Max(difficultyData.candidateLimit, 0);
        float configuredMaxScoreLoss = Mathf.Max(difficultyData.maxScoreLoss, 0f);

        int realtimeMaxVisits = GetBoardSizeIntValue(boardSize, difficultyData.realtimeMaxVisits9, difficultyData.realtimeMaxVisits13, difficultyData.realtimeMaxVisits19, configuredMaxVisits);
        int candidateLimit = GetBoardSizeIntValue(boardSize, difficultyData.candidateLimit9, difficultyData.candidateLimit13, difficultyData.candidateLimit19, configuredCandidateLimit);
        float maxScoreLoss = GetBoardSizeFloatValue(boardSize, difficultyData.maxScoreLoss9, difficultyData.maxScoreLoss13, difficultyData.maxScoreLoss19, configuredMaxScoreLoss);
        int requestMaxVisits = Mathf.Clamp(realtimeMaxVisits, 1, configuredMaxVisits);
        int probeMaxVisits = GetBoardSizeIntValue(boardSize, difficultyData.probeMaxVisits9, difficultyData.probeMaxVisits13, difficultyData.probeMaxVisits19, requestMaxVisits);

        return new AiRuntimeParams
        {
            boardSize = boardSize,
            moveCount = GetMoveCount(),
            configuredMaxVisits = configuredMaxVisits,
            requestMaxVisits = requestMaxVisits,
            configuredCandidateLimit = configuredCandidateLimit,
            requestCandidateLimit = Mathf.Max(candidateLimit, 0),
            configuredMaxScoreLoss = configuredMaxScoreLoss,
            requestMaxScoreLoss = Mathf.Max(maxScoreLoss, 0f),
            dynamicBudgetEnabled = difficultyData.dynamicBudgetEnabled,
            probeMaxVisits = Mathf.Clamp(probeMaxVisits, 1, requestMaxVisits),
            openingProbeMoveLimit = Mathf.Max(GetBoardSizeIntValue(boardSize, difficultyData.openingProbeMoveLimit9, difficultyData.openingProbeMoveLimit13, difficultyData.openingProbeMoveLimit19, 0), 0),
            closeScoreLeadThreshold = Mathf.Max(GetBoardSizeFloatValue(boardSize, difficultyData.closeScoreLeadThreshold9, difficultyData.closeScoreLeadThreshold13, difficultyData.closeScoreLeadThreshold19, 0f), 0f),
            closeWinrateThreshold = Mathf.Max(GetBoardSizeFloatValue(boardSize, difficultyData.closeWinrateThreshold9, difficultyData.closeWinrateThreshold13, difficultyData.closeWinrateThreshold19, 0f), 0f),
            simpleCandidateGapThreshold = Mathf.Max(GetBoardSizeFloatValue(boardSize, difficultyData.simpleCandidateGapThreshold9, difficultyData.simpleCandidateGapThreshold13, difficultyData.simpleCandidateGapThreshold19, 0f), 0f),
            confidentBestMoveGapThreshold = Mathf.Max(GetBoardSizeFloatValue(boardSize, difficultyData.confidentBestMoveGapThreshold9, difficultyData.confidentBestMoveGapThreshold13, difficultyData.confidentBestMoveGapThreshold19, 0f), 0f),
            forceFullBudgetMoveLimit = Mathf.Max(GetBoardSizeIntValue(boardSize, difficultyData.forceFullBudgetMoveLimit9, difficultyData.forceFullBudgetMoveLimit13, difficultyData.forceFullBudgetMoveLimit19, 0), 0),
            probeMinMoveInfoCount = Mathf.Max(difficultyData.probeMinMoveInfoCount, 1),
        };
    }

    private int GetMoveCount()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        return compDuel?.kataGoMoves?.Count ?? 0;
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

    private int GetBoardSizeIntValue(int boardSize, int board9, int board13, int board19, int fallback)
    {
        int value;
        switch (boardSize) {
            case 9:
                value = board9;
                break;
            case 13:
                value = board13;
                break;
            case 19:
                value = board19;
                break;
            default:
                value = fallback;
                break;
        }

        return value > 0 ? value : fallback;
    }

    private float GetBoardSizeFloatValue(int boardSize, float board9, float board13, float board19, float fallback)
    {
        float value;
        switch (boardSize) {
            case 9:
                value = board9;
                break;
            case 13:
                value = board13;
                break;
            case 19:
                value = board19;
                break;
            default:
                value = fallback;
                break;
        }

        return value > 0f ? value : fallback;
    }

    private RectCoordinates SelectMove(JObject result, DuelAiDifficultyDataType difficultyData, AiRuntimeParams runtimeParams)
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (result == null || compChessBoard?.chessBoardGrid == null || difficultyData == null) {
            XNLogger.LogError(
                "Duel AI move select failed, required data missing.",
                ("hasResult", (result != null).ToString()),
                ("hasBoardGrid", (compChessBoard?.chessBoardGrid != null).ToString()),
                ("hasDifficulty", (difficultyData != null).ToString()));
            return null;
        }

        JArray moveInfos = result["moveInfos"] as JArray;
        if (moveInfos == null || moveInfos.Count == 0) {
            RectCoordinates policyCoords = SelectMoveFromPolicy(result, difficultyData, runtimeParams);
            if (policyCoords != null) {
                return policyCoords;
            }

            XNLogger.LogError(
                "Duel AI move failed, KataGo moveInfos missing.",
                ("resultKeys", BuildResultKeysLog(result)),
                ("moveInfoTokenType", result["moveInfos"]?.Type.ToString() ?? "null"),
                ("moveInfoCount", moveInfos?.Count.ToString() ?? "null"),
                ("policyCount", ((result["policy"] as JArray)?.Count ?? 0).ToString()),
                ("hasOwnership", (result["ownership"] != null).ToString()),
                ("hasRootInfo", (result["rootInfo"] != null).ToString()),
                ("warning", result["warning"]?.ToString() ?? string.Empty),
                ("error", result["error"]?.ToString() ?? string.Empty));
            return null;
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        List<AiMoveCandidate> candidates = new List<AiMoveCandidate>();
        int candidateLimit = runtimeParams.requestCandidateLimit > 0 ? runtimeParams.requestCandidateLimit : moveInfos.Count;
        int parsedCount = 0;
        int passCount = 0;
        int parseFailedCount = 0;
        int illegalCount = 0;

        foreach (JToken token in moveInfos) {
            if (candidates.Count >= candidateLimit) {
                break;
            }

            string move = token?["move"]?.ToString();
            if (string.Equals(move, KataGoPositionJsonBuilder.PassPoint, StringComparison.OrdinalIgnoreCase)) {
                passCount += 1;
                continue;
            }

            if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(move, boardSize, out RectCoordinates coords)) {
                parseFailedCount += 1;
                continue;
            }

            parsedCount += 1;
            if (!IsLegalMove(coords)) {
                illegalCount += 1;
                continue;
            }

            candidates.Add(new AiMoveCandidate
            {
                coords = coords,
                scoreLoss = ParseFloat(token?["scoreLoss"]),
                visits = Mathf.Max(ParseInt(token?["visits"]), 1),
                order = candidates.Count,
            });
        }

        if (candidates.Count == 0) {
            XNLogger.LogWarn(
                "Duel AI move select found no legal candidates.",
                ("moveInfoCount", moveInfos.Count.ToString()),
                ("candidateLimit", candidateLimit.ToString()),
                ("parsedCount", parsedCount.ToString()),
                ("passCount", passCount.ToString()),
                ("parseFailedCount", parseFailedCount.ToString()),
                ("illegalCount", illegalCount.ToString()));
            return null;
        }

        List<AiMoveCandidate> filteredCandidates = BuildFilteredCandidates(candidates, runtimeParams);
        AiMoveCandidate pickedCandidate = PickCandidate(filteredCandidates.Count > 0 ? filteredCandidates : candidates, difficultyData);
        XNLogger.LogInfo(
            "Duel AI move candidates ready.",
            ("moveInfoCount", moveInfos.Count.ToString()),
            ("boardSize", runtimeParams.boardSize.ToString()),
            ("candidateLimit", candidateLimit.ToString()),
            ("maxScoreLoss", runtimeParams.requestMaxScoreLoss.ToString()),
            ("legalCount", candidates.Count.ToString()),
            ("filteredCount", filteredCandidates.Count.ToString()),
            ("pickedCoords", pickedCandidate.coords?.ToString() ?? "null"),
            ("pickedScoreLoss", pickedCandidate.scoreLoss.ToString()),
            ("pickedVisits", pickedCandidate.visits.ToString()));
        return pickedCandidate.coords;
    }

    private RectCoordinates SelectMoveFromPolicy(JObject result, DuelAiDifficultyDataType difficultyData, AiRuntimeParams runtimeParams)
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        JArray policy = result?["policy"] as JArray;
        if (compChessBoard?.chessBoardGrid == null || policy == null || policy.Count == 0) {
            return null;
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        int pointCount = boardSize * boardSize;
        if (policy.Count < pointCount) {
            XNLogger.LogWarn(
                "Duel AI policy fallback skipped, policy length invalid.",
                ("policyCount", policy.Count.ToString()),
                ("pointCount", pointCount.ToString()));
            return null;
        }

        int candidateLimit = runtimeParams.requestCandidateLimit > 0 ? runtimeParams.requestCandidateLimit : pointCount;
        List<PolicyCandidate> policyCandidates = new List<PolicyCandidate>();
        for (int i = 0; i < pointCount; i++) {
            policyCandidates.Add(new PolicyCandidate
            {
                posIndex = i,
                probability = Mathf.Max(ParseFloat(policy[i]), 0f),
            });
        }

        policyCandidates.Sort((left, right) => right.probability.CompareTo(left.probability));

        List<AiMoveCandidate> candidates = new List<AiMoveCandidate>();
        int illegalCount = 0;
        int zeroProbabilityCount = 0;
        foreach (PolicyCandidate policyCandidate in policyCandidates) {
            if (candidates.Count >= candidateLimit) {
                break;
            }

            if (policyCandidate.probability <= 0f) {
                zeroProbabilityCount += 1;
                continue;
            }

            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(policyCandidate.posIndex);
            if (!IsLegalMove(coords)) {
                illegalCount += 1;
                continue;
            }

            candidates.Add(new AiMoveCandidate
            {
                coords = coords,
                scoreLoss = 0f,
                visits = Mathf.Max(Mathf.RoundToInt(policyCandidate.probability * 10000f), 1),
                order = candidates.Count,
            });
        }

        if (candidates.Count == 0) {
            XNLogger.LogWarn(
                "Duel AI policy fallback found no legal candidates.",
                ("policyCount", policy.Count.ToString()),
                ("candidateLimit", candidateLimit.ToString()),
                ("illegalCount", illegalCount.ToString()),
                ("zeroProbabilityCount", zeroProbabilityCount.ToString()));
            return null;
        }

        AiMoveCandidate pickedCandidate = PickCandidate(candidates, difficultyData);
        XNLogger.LogInfo(
            "Duel AI policy fallback selected move.",
            ("policyCount", policy.Count.ToString()),
            ("boardSize", runtimeParams.boardSize.ToString()),
            ("candidateLimit", candidateLimit.ToString()),
            ("legalCount", candidates.Count.ToString()),
            ("pickedCoords", pickedCandidate.coords?.ToString() ?? "null"),
            ("pickedWeight", pickedCandidate.visits.ToString()));
        return pickedCandidate.coords;
    }

    private List<AiMoveCandidate> BuildFilteredCandidates(List<AiMoveCandidate> candidates, AiRuntimeParams runtimeParams)
    {
        List<AiMoveCandidate> filteredCandidates = new List<AiMoveCandidate>();
        foreach (AiMoveCandidate candidate in candidates) {
            if (candidate.scoreLoss <= runtimeParams.requestMaxScoreLoss) {
                filteredCandidates.Add(candidate);
            }
        }

        return filteredCandidates;
    }

    private AiMoveCandidate PickCandidate(List<AiMoveCandidate> candidates, DuelAiDifficultyDataType difficultyData)
    {
        if (candidates.Count <= 1) {
            return candidates[0];
        }

        if (UnityEngine.Random.value < Mathf.Clamp01(difficultyData.mistakeRate)) {
            int index = UnityEngine.Random.Range(0, candidates.Count);
            return candidates[index];
        }

        float temperature = Mathf.Max(difficultyData.temperature, 0.01f);
        float visitPower = Mathf.Max(difficultyData.visitPower, 0.01f);
        float totalWeight = 0f;
        float[] weights = new float[candidates.Count];

        for (int i = 0; i < candidates.Count; i++) {
            AiMoveCandidate candidate = candidates[i];
            float visitWeight = Mathf.Pow(candidate.visits, visitPower);
            float scoreWeight = Mathf.Exp(-candidate.scoreLoss / temperature);
            float orderWeight = 1f / (candidate.order + 1f);
            weights[i] = Mathf.Max(visitWeight * scoreWeight * orderWeight, 0.0001f);
            totalWeight += weights[i];
        }

        float target = UnityEngine.Random.value * totalWeight;
        float accumulated = 0f;
        for (int i = 0; i < candidates.Count; i++) {
            accumulated += weights[i];
            if (target <= accumulated) {
                return candidates[i];
            }
        }

        return candidates[0];
    }

    private bool IsLegalMove(RectCoordinates coords)
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compChessBoard == null || compDuel == null || coords == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString()) || curPlayer == null) {
            return false;
        }

        PlayerFlag playerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        return DuelMoveRule.CheckMoveLegal(compChessBoard, playerFlag, coords);
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

    private string BuildResultKeysLog(JObject result)
    {
        if (result == null) {
            return string.Empty;
        }

        List<string> keys = new List<string>();
        foreach (JProperty property in result.Properties()) {
            keys.Add(property.Name);
        }

        return string.Join(",", keys);
    }

    private string GetResultRequestId(JObject result)
    {
        return result?["id"]?.ToString() ?? string.Empty;
    }

    private int ParseInt(JToken token)
    {
        if (int.TryParse(token?.ToString(), out int value)) {
            return value;
        }

        return 0;
    }

    private bool TryParseFloat(JToken token, out float value)
    {
        return float.TryParse(token?.ToString(), out value);
    }

    private float ParseFloat(JToken token)
    {
        if (float.TryParse(token?.ToString(), out float value)) {
            return value;
        }

        return 0f;
    }

    private struct AiMoveCandidate
    {
        public RectCoordinates coords;
        public float scoreLoss;
        public int visits;
        public int order;
    }

    private struct PolicyCandidate
    {
        public int posIndex;
        public float probability;
    }

    private struct AiAnalyzeResult
    {
        public JObject result;
        public string requestId;
        public string budgetMode;
        public string decisionReason;
    }

    private struct AiProbeStats
    {
        public string requestId;
        public int moveCount;
        public int moveInfoCount;
        public bool hasRootInfo;
        public int rootVisits;
        public bool hasScoreLead;
        public float scoreLead;
        public float scoreLeadAbs;
        public bool hasWinrate;
        public float winrate;
        public float winrateDistance;
        public string topMove;
        public float topScoreLoss;
        public string secondMove;
        public float secondScoreLoss;
        public bool hasCandidateGap;
        public float candidateGap;
        public string warning;
        public string error;
    }

    private struct AiBudgetDecision
    {
        public bool useFullBudget;
        public string reason;
        public AiProbeStats stats;
        public AiRuntimeParams runtimeParams;
    }

    private struct AiRuntimeParams
    {
        public int boardSize;
        public int moveCount;
        public int configuredMaxVisits;
        public int requestMaxVisits;
        public int configuredCandidateLimit;
        public int requestCandidateLimit;
        public float configuredMaxScoreLoss;
        public float requestMaxScoreLoss;
        public bool dynamicBudgetEnabled;
        public int probeMaxVisits;
        public int openingProbeMoveLimit;
        public float closeScoreLeadThreshold;
        public float closeWinrateThreshold;
        public float simpleCandidateGapThreshold;
        public float confidentBestMoveGapThreshold;
        public int forceFullBudgetMoveLimit;
        public int probeMinMoveInfoCount;
    }
}
