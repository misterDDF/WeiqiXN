using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.Logger;

public static class DuelAiBudgetService
{
    public static DuelAiRuntimeParams ResolveRuntimeParams(DuelAiDifficultyDataType difficultyData, int boardSize, int moveCount)
    {
        int configuredMaxVisits = Mathf.Max(difficultyData.maxVisits, 1);
        int configuredCandidateLimit = Mathf.Max(difficultyData.candidateLimit, 0);
        float configuredMaxScoreLoss = Mathf.Max(difficultyData.maxScoreLoss, 0f);

        int realtimeMaxVisits = GetBoardSizeIntValue(boardSize, difficultyData.realtimeMaxVisits9, difficultyData.realtimeMaxVisits13, difficultyData.realtimeMaxVisits19, configuredMaxVisits);
        int candidateLimit = GetBoardSizeIntValue(boardSize, difficultyData.candidateLimit9, difficultyData.candidateLimit13, difficultyData.candidateLimit19, configuredCandidateLimit);
        float maxScoreLoss = GetBoardSizeFloatValue(boardSize, difficultyData.maxScoreLoss9, difficultyData.maxScoreLoss13, difficultyData.maxScoreLoss19, configuredMaxScoreLoss);
        int requestMaxVisits = Mathf.Clamp(realtimeMaxVisits, 1, configuredMaxVisits);
        int probeMaxVisits = GetBoardSizeIntValue(boardSize, difficultyData.probeMaxVisits9, difficultyData.probeMaxVisits13, difficultyData.probeMaxVisits19, requestMaxVisits);

        return new DuelAiRuntimeParams
        {
            boardSize = boardSize,
            moveCount = moveCount,
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

    public static DuelAiBudgetDecision DecideBudgetAfterProbe(JObject result, DuelAiRuntimeParams runtimeParams)
    {
        DuelAiProbeStats stats = BuildProbeStats(result);
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

    public static DuelAiProbeStats BuildProbeStats(JObject result)
    {
        DuelAiProbeStats stats = new DuelAiProbeStats();
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

    public static void LogProbeResult(JObject result, DuelAiRuntimeParams runtimeParams)
    {
        if (!LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG) {
            return;
        }

        DuelAiProbeStats stats = BuildProbeStats(result);
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

    public static void LogBudgetDecision(DuelAiBudgetDecision decision, DuelAiRuntimeParams runtimeParams)
    {
        if (!LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG) {
            return;
        }

        DuelAiProbeStats stats = decision.stats;
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

    private static DuelAiBudgetDecision BuildBudgetDecision(bool useFullBudget, string reason, DuelAiProbeStats stats, DuelAiRuntimeParams runtimeParams)
    {
        stats.moveCount = runtimeParams.moveCount;
        return new DuelAiBudgetDecision
        {
            useFullBudget = useFullBudget,
            reason = reason,
            stats = stats,
            runtimeParams = runtimeParams,
        };
    }

    private static int GetBoardSizeIntValue(int boardSize, int board9, int board13, int board19, int fallback)
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

    private static float GetBoardSizeFloatValue(int boardSize, float board9, float board13, float board19, float fallback)
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

    private static bool TryParseFloat(JToken token, out float value)
    {
        return float.TryParse(token?.ToString(), out value);
    }

    private static int ParseInt(JToken token)
    {
        if (int.TryParse(token?.ToString(), out int value)) {
            return value;
        }

        return 0;
    }

    private static float ParseFloat(JToken token)
    {
        if (float.TryParse(token?.ToString(), out float value)) {
            return value;
        }

        return 0f;
    }
}

public struct DuelAiRuntimeParams
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

public struct DuelAiProbeStats
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

public struct DuelAiBudgetDecision
{
    public bool useFullBudget;
    public string reason;
    public DuelAiProbeStats stats;
    public DuelAiRuntimeParams runtimeParams;
}
