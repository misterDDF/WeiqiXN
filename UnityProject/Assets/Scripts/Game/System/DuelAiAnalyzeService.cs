using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public static class DuelAiAnalyzeService
{
    public static async Task<DuelAiAnalyzeResult> AnalyzeAiMoveAsync(
        DuelScene duelScene,
        DuelAiDifficultyDataType difficultyData,
        DuelAiRuntimeParams runtimeParams,
        CancellationToken cancellationToken)
    {
        int fullMaxVisits = runtimeParams.requestMaxVisits;
        if (!runtimeParams.dynamicBudgetEnabled || runtimeParams.probeMaxVisits >= fullMaxVisits) {
            string reason = runtimeParams.dynamicBudgetEnabled ? "probe_budget_not_lower" : "dynamic_disabled";
            JObject fullResult = await RequestAiAnalyzeAsync(duelScene, difficultyData, runtimeParams, fullMaxVisits, "full", reason, cancellationToken);
            return new DuelAiAnalyzeResult
            {
                result = fullResult,
                requestId = GetResultRequestId(fullResult),
                budgetMode = "full",
                decisionReason = reason,
            };
        }

        JObject probeResult = await RequestAiAnalyzeAsync(duelScene, difficultyData, runtimeParams, runtimeParams.probeMaxVisits, "probe", "dynamic_probe", cancellationToken);
        DuelAiBudgetDecision decision = DuelAiBudgetService.DecideBudgetAfterProbe(probeResult, runtimeParams);
        DuelAiBudgetService.LogBudgetDecision(decision, runtimeParams);

        if (!decision.useFullBudget) {
            return new DuelAiAnalyzeResult
            {
                result = probeResult,
                requestId = GetResultRequestId(probeResult),
                budgetMode = "probe",
                decisionReason = decision.reason,
            };
        }

        JObject fullResultAfterProbe = await RequestAiAnalyzeAsync(duelScene, difficultyData, runtimeParams, fullMaxVisits, "full", decision.reason, cancellationToken);
        return new DuelAiAnalyzeResult
        {
            result = fullResultAfterProbe,
            requestId = GetResultRequestId(fullResultAfterProbe),
            budgetMode = "full",
            decisionReason = decision.reason,
        };
    }

    private static async Task<JObject> RequestAiAnalyzeAsync(
        DuelScene duelScene,
        DuelAiDifficultyDataType difficultyData,
        DuelAiRuntimeParams runtimeParams,
        int maxVisits,
        string budgetMode,
        string reason,
        CancellationToken cancellationToken)
    {
        string requestId = $"duel-ai-{budgetMode}-{DateTime.UtcNow.Ticks}";
        JObject query = KataGoPositionJsonBuilder.BuildAiMoveAnalysisJson(duelScene, requestId, difficultyData, maxVisits);
        if (LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG) {
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
                ("humanPolicyWeight", difficultyData.humanPolicyWeight.ToString()),
                ("humanProfileEnabled", KataGoBootstrap.CanUseHumanSlProfile().ToString()),
                ("humanSLProfile", difficultyData.humanSLProfile ?? "none"),
                ("humanProfileSent", (query["overrideSettings"]?["humanSLProfile"] != null).ToString()));
        }

        JObject result = await KataGoBootstrap.AnalyzeAsync(
            query,
            KataGoBootstrap.CreateRetryUntilCanceledAnalyzeOptions($"duel-ai-{budgetMode}"),
            cancellationToken);
        if (budgetMode == "probe") {
            DuelAiBudgetService.LogProbeResult(result, runtimeParams);
        }

        return result;
    }

    private static string GetResultRequestId(JObject result)
    {
        return result?["id"]?.ToString() ?? string.Empty;
    }
}

public struct DuelAiAnalyzeResult
{
    public JObject result;
    public string requestId;
    public string budgetMode;
    public string decisionReason;
}
