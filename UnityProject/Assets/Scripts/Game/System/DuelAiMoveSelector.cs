using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public static class DuelAiMoveSelector
{
    public static DuelAiTurnDecision SelectTurnDecision(SceneBase scene, JObject result, DuelAiDifficultyDataType difficultyData, DuelAiRuntimeParams runtimeParams)
    {
        SceneComponentChessBoard compChessBoard = scene?.GetComponent<SceneComponentChessBoard>();
        if (scene == null || result == null || compChessBoard?.chessBoardGrid == null || difficultyData == null) {
            XNLogger.LogError(
                "Duel AI move select failed, required data missing.",
                ("hasScene", (scene != null).ToString()),
                ("hasResult", (result != null).ToString()),
                ("hasBoardGrid", (compChessBoard?.chessBoardGrid != null).ToString()),
                ("hasDifficulty", (difficultyData != null).ToString()));
            return BuildFailedDecision("required_data_missing");
        }

        JArray moveInfos = result["moveInfos"] as JArray;
        string topMove = GetTopMove(moveInfos);
        if (IsPassMove(topMove)) {
            if (LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG) {
                XNLogger.LogInfo(
                    "Duel AI top move is pass.",
                    ("moveInfoCount", (moveInfos?.Count ?? 0).ToString()),
                    ("boardSize", runtimeParams.boardSize.ToString()));
            }
            return BuildPassDecision("moveinfos_top_pass");
        }

        if (ShouldTryHumanPolicy(difficultyData)) {
            DuelAiTurnDecision humanPolicyDecision = SelectTurnDecisionFromPolicy(
                scene,
                result,
                difficultyData,
                runtimeParams,
                "humanPolicy",
                "human_policy_candidate",
                false);
            if (humanPolicyDecision.type != DuelAiTurnDecisionType.Failed) {
                return humanPolicyDecision;
            }
        }

        if (moveInfos == null || moveInfos.Count == 0) {
            DuelAiTurnDecision policyDecision = SelectTurnDecisionFromPolicy(
                scene,
                result,
                difficultyData,
                runtimeParams,
                "policy",
                "policy_candidate",
                true);
            if (policyDecision.type != DuelAiTurnDecisionType.Failed) {
                return policyDecision;
            }

            XNLogger.LogError(
                "Duel AI move failed, KataGo moveInfos missing.",
                ("resultKeys", BuildResultKeysLog(result)),
                ("moveInfoTokenType", result["moveInfos"]?.Type.ToString() ?? "null"),
                ("moveInfoCount", moveInfos?.Count.ToString() ?? "null"),
                ("policyCount", ((result["policy"] as JArray)?.Count ?? 0).ToString()),
                ("humanPolicyCount", ((result["humanPolicy"] as JArray)?.Count ?? 0).ToString()),
                ("hasOwnership", (result["ownership"] != null).ToString()),
                ("hasRootInfo", (result["rootInfo"] != null).ToString()),
                ("warning", result["warning"]?.ToString() ?? string.Empty),
                ("error", result["error"]?.ToString() ?? string.Empty));
            return BuildFailedDecision("moveinfos_missing_policy_unusable");
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        List<DuelAiMoveCandidate> candidates = new List<DuelAiMoveCandidate>();
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
            if (IsPassMove(move)) {
                passCount += 1;
                continue;
            }

            if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(move, boardSize, out RectCoordinates coords)) {
                parseFailedCount += 1;
                continue;
            }

            parsedCount += 1;
            if (!IsLegalMove(scene, coords)) {
                illegalCount += 1;
                continue;
            }

            candidates.Add(new DuelAiMoveCandidate
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
            if (passCount > 0) {
                return BuildPassDecision("moveinfos_pass_no_legal_candidate");
            }

            return BuildFailedDecision("moveinfos_no_legal_candidate");
        }

        List<DuelAiMoveCandidate> filteredCandidates = BuildFilteredCandidates(candidates, runtimeParams);
        DuelAiMoveCandidate pickedCandidate = PickCandidate(filteredCandidates.Count > 0 ? filteredCandidates : candidates, difficultyData);
        if (LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG) {
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
        }
        return BuildMoveDecision(pickedCandidate.coords, "moveinfos_candidate");
    }

    private static DuelAiTurnDecision SelectTurnDecisionFromPolicy(
        SceneBase scene,
        JObject result,
        DuelAiDifficultyDataType difficultyData,
        DuelAiRuntimeParams runtimeParams,
        string policyKey,
        string decisionReason,
        bool allowPassSelection)
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        JArray policy = result?[policyKey] as JArray;
        if (compChessBoard?.chessBoardGrid == null || policy == null || policy.Count == 0) {
            return BuildFailedDecision($"{policyKey}_missing");
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        int pointCount = boardSize * boardSize;
        if (policy.Count < pointCount) {
            XNLogger.LogWarn(
                "Duel AI policy fallback skipped, policy length invalid.",
                ("policyKey", policyKey),
                ("policyCount", policy.Count.ToString()),
                ("pointCount", pointCount.ToString()));
            return BuildFailedDecision($"{policyKey}_length_invalid");
        }

        bool hasPassPolicy = policy.Count > pointCount;
        float passPolicy = hasPassPolicy ? Mathf.Max(ParseFloat(policy[pointCount]), 0f) : 0f;
        int candidateLimit = runtimeParams.requestCandidateLimit > 0 ? runtimeParams.requestCandidateLimit : pointCount;
        List<DuelAiPolicyCandidate> policyCandidates = new List<DuelAiPolicyCandidate>();
        for (int i = 0; i < pointCount; i++) {
            policyCandidates.Add(new DuelAiPolicyCandidate
            {
                posIndex = i,
                probability = Mathf.Max(ParseFloat(policy[i]), 0f),
            });
        }

        policyCandidates.Sort((left, right) => right.probability.CompareTo(left.probability));

        List<DuelAiMoveCandidate> candidates = new List<DuelAiMoveCandidate>();
        int illegalCount = 0;
        int zeroProbabilityCount = 0;
        float bestLegalPointPolicy = 0f;
        foreach (DuelAiPolicyCandidate policyCandidate in policyCandidates) {
            if (candidates.Count >= candidateLimit) {
                break;
            }

            if (policyCandidate.probability <= 0f) {
                zeroProbabilityCount += 1;
                continue;
            }

            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(policyCandidate.posIndex);
            if (!IsLegalMove(scene, coords)) {
                illegalCount += 1;
                continue;
            }

            bestLegalPointPolicy = Mathf.Max(bestLegalPointPolicy, policyCandidate.probability);
            candidates.Add(new DuelAiMoveCandidate
            {
                coords = coords,
                scoreLoss = 0f,
                visits = Mathf.Max(Mathf.RoundToInt(policyCandidate.probability * 10000f), 1),
                order = candidates.Count,
            });
        }

        if (allowPassSelection && hasPassPolicy && passPolicy > 0f && passPolicy > bestLegalPointPolicy) {
            if (LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG) {
                XNLogger.LogInfo(
                    "Duel AI policy fallback selected pass.",
                    ("policyKey", policyKey),
                    ("policyCount", policy.Count.ToString()),
                    ("boardSize", runtimeParams.boardSize.ToString()),
                    ("candidateLimit", candidateLimit.ToString()),
                    ("passPolicy", passPolicy.ToString()),
                    ("bestLegalPointPolicy", bestLegalPointPolicy.ToString()),
                    ("legalCount", candidates.Count.ToString()));
            }
            return BuildPassDecision($"{decisionReason}_pass_best");
        }

        if (candidates.Count == 0) {
            XNLogger.LogWarn(
                "Duel AI policy fallback found no legal candidates.",
                ("policyKey", policyKey),
                ("policyCount", policy.Count.ToString()),
                ("candidateLimit", candidateLimit.ToString()),
                ("hasPassPolicy", hasPassPolicy.ToString()),
                ("passPolicy", passPolicy.ToString()),
                ("illegalCount", illegalCount.ToString()),
                ("zeroProbabilityCount", zeroProbabilityCount.ToString()));
            if (allowPassSelection && hasPassPolicy && passPolicy > 0f) {
                return BuildPassDecision($"{decisionReason}_pass_no_legal_candidate");
            }

            return BuildFailedDecision($"{policyKey}_no_legal_candidate");
        }

        DuelAiMoveCandidate pickedCandidate = PickCandidate(candidates, difficultyData);
        if (LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG) {
            XNLogger.LogInfo(
                "Duel AI policy fallback selected move.",
                ("policyKey", policyKey),
                ("policyCount", policy.Count.ToString()),
                ("boardSize", runtimeParams.boardSize.ToString()),
                ("candidateLimit", candidateLimit.ToString()),
                ("allowPassSelection", allowPassSelection.ToString()),
                ("hasPassPolicy", hasPassPolicy.ToString()),
                ("passPolicy", passPolicy.ToString()),
                ("bestLegalPointPolicy", bestLegalPointPolicy.ToString()),
                ("legalCount", candidates.Count.ToString()),
                ("pickedCoords", pickedCandidate.coords?.ToString() ?? "null"),
                ("pickedWeight", pickedCandidate.visits.ToString()));
        }
        return BuildMoveDecision(pickedCandidate.coords, decisionReason);
    }

    private static bool ShouldTryHumanPolicy(DuelAiDifficultyDataType difficultyData)
    {
        if (!difficultyData.useHumanPolicy || !KataGoBootstrap.CanUseHumanSlProfile()) {
            return false;
        }

        float humanPolicyWeight = Mathf.Clamp01(difficultyData.humanPolicyWeight);
        return humanPolicyWeight > 0f && UnityEngine.Random.value < humanPolicyWeight;
    }

    private static List<DuelAiMoveCandidate> BuildFilteredCandidates(List<DuelAiMoveCandidate> candidates, DuelAiRuntimeParams runtimeParams)
    {
        List<DuelAiMoveCandidate> filteredCandidates = new List<DuelAiMoveCandidate>();
        foreach (DuelAiMoveCandidate candidate in candidates) {
            if (candidate.scoreLoss <= runtimeParams.requestMaxScoreLoss) {
                filteredCandidates.Add(candidate);
            }
        }

        return filteredCandidates;
    }

    private static DuelAiMoveCandidate PickCandidate(List<DuelAiMoveCandidate> candidates, DuelAiDifficultyDataType difficultyData)
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
            DuelAiMoveCandidate candidate = candidates[i];
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

    private static bool IsLegalMove(SceneBase scene, RectCoordinates coords)
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

    private static string BuildResultKeysLog(JObject result)
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

    private static string GetTopMove(JArray moveInfos)
    {
        if (moveInfos == null || moveInfos.Count == 0) {
            return string.Empty;
        }

        JToken bestOrderedMove = null;
        int bestOrder = int.MaxValue;
        foreach (JToken token in moveInfos) {
            if (TryParseInt(token?["order"], out int order) && order < bestOrder) {
                bestOrderedMove = token;
                bestOrder = order;
            }
        }

        JToken topMove = bestOrderedMove ?? moveInfos[0];
        return topMove?["move"]?.ToString() ?? string.Empty;
    }

    private static bool IsPassMove(string move)
    {
        return string.Equals(move, KataGoPositionJsonBuilder.PassPoint, StringComparison.OrdinalIgnoreCase);
    }

    private static DuelAiTurnDecision BuildMoveDecision(RectCoordinates coords, string reason)
    {
        if (coords == null) {
            return BuildFailedDecision("move_coords_missing");
        }

        return new DuelAiTurnDecision
        {
            type = DuelAiTurnDecisionType.Move,
            coords = coords,
            reason = reason,
        };
    }

    private static DuelAiTurnDecision BuildPassDecision(string reason)
    {
        return new DuelAiTurnDecision
        {
            type = DuelAiTurnDecisionType.Pass,
            reason = reason,
        };
    }

    private static DuelAiTurnDecision BuildFailedDecision(string reason)
    {
        return new DuelAiTurnDecision
        {
            type = DuelAiTurnDecisionType.Failed,
            reason = reason,
        };
    }

    private static bool TryParseInt(JToken token, out int value)
    {
        return int.TryParse(token?.ToString(), out value);
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

public enum DuelAiTurnDecisionType
{
    Failed,
    Move,
    Pass,
}

public struct DuelAiTurnDecision
{
    public DuelAiTurnDecisionType type;
    public RectCoordinates coords;
    public string reason;
}

public struct DuelAiMoveCandidate
{
    public RectCoordinates coords;
    public float scoreLoss;
    public int visits;
    public int order;
}

public struct DuelAiPolicyCandidate
{
    public int posIndex;
    public float probability;
}
