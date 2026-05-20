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

            XNLogger.LogInfo(
                "Duel AI turn started.",
                ("cfgId", compDuel.aiDifficultyCfgId.value),
                ("cfgName", difficultyData.name ?? string.Empty),
                ("thinkDelayMs", difficultyData.thinkDelayMs.ToString()),
                ("maxVisits", difficultyData.maxVisits.ToString()),
                ("candidateLimit", difficultyData.candidateLimit.ToString()),
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

            string requestId = $"duel-ai-move-{DateTime.UtcNow.Ticks}";
            JObject query = KataGoPositionJsonBuilder.BuildAiMoveAnalysisJson((DuelScene)scene, requestId, difficultyData);
            XNLogger.LogInfo(
                "Duel AI analyze requested.",
                ("requestId", requestId),
                ("moveCount", ((query["moves"] as JArray)?.Count ?? 0).ToString()),
                ("analyzeTurns", query["analyzeTurns"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "null"),
                ("configuredMaxVisits", difficultyData.maxVisits.ToString()),
                ("requestMaxVisits", query["maxVisits"]?.ToString() ?? "null"),
                ("includePolicy", query["includePolicy"]?.ToString() ?? "null"),
                ("humanProfileRequested", difficultyData.useHumanPolicy.ToString()),
                ("humanProfileEnabled", KataGoBootstrap.CanUseHumanSlProfile().ToString()),
                ("humanSLProfile", difficultyData.humanSLProfile ?? "none"),
                ("humanProfileSent", (query["overrideSettings"]?["humanSLProfile"] != null).ToString()));
            JObject result = await KataGoBootstrap.AnalyzeAsync(query);
            if (currentRequestVersion != requestVersion || !IsAiTurn(out skipReason)) {
                XNLogger.LogWarn(
                    "Duel AI move canceled after analyze.",
                    ("reason", currentRequestVersion != requestVersion ? "request version changed" : skipReason),
                    ("startVersion", currentRequestVersion.ToString()),
                    ("currentVersion", requestVersion.ToString()),
                    ("turnInfo", BuildTurnInfoLog()));
                return;
            }

            RectCoordinates coords = SelectMove(result, difficultyData);
            if (coords != null) {
                XNLogger.LogInfo("Duel AI move selected.", ("coords", coords.ToString()), ("requestId", requestId));
                scene.EmitSystemEvent(new OnAddChessToBoard(coords));
                return;
            }

            if (difficultyData.allowPassBeforeEndgame || IsBoardFull()) {
                XNLogger.LogWarn(
                    "Duel AI requests pass.",
                    ("allowPassBeforeEndgame", difficultyData.allowPassBeforeEndgame.ToString()),
                    ("isBoardFull", IsBoardFull().ToString()),
                    ("requestId", requestId));
                scene.EmitSystemEvent(new OnRequestDuelPass());
                return;
            }

            XNLogger.LogWarn("Duel AI move skipped, no playable candidate found.", ("requestId", requestId));
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

    private RectCoordinates SelectMove(JObject result, DuelAiDifficultyDataType difficultyData)
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
            RectCoordinates policyCoords = SelectMoveFromPolicy(result, difficultyData);
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
        int candidateLimit = difficultyData.candidateLimit > 0 ? difficultyData.candidateLimit : moveInfos.Count;
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

        List<AiMoveCandidate> filteredCandidates = BuildFilteredCandidates(candidates, difficultyData);
        AiMoveCandidate pickedCandidate = PickCandidate(filteredCandidates.Count > 0 ? filteredCandidates : candidates, difficultyData);
        XNLogger.LogInfo(
            "Duel AI move candidates ready.",
            ("moveInfoCount", moveInfos.Count.ToString()),
            ("candidateLimit", candidateLimit.ToString()),
            ("legalCount", candidates.Count.ToString()),
            ("filteredCount", filteredCandidates.Count.ToString()),
            ("pickedCoords", pickedCandidate.coords?.ToString() ?? "null"),
            ("pickedScoreLoss", pickedCandidate.scoreLoss.ToString()),
            ("pickedVisits", pickedCandidate.visits.ToString()));
        return pickedCandidate.coords;
    }

    private RectCoordinates SelectMoveFromPolicy(JObject result, DuelAiDifficultyDataType difficultyData)
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

        int candidateLimit = difficultyData.candidateLimit > 0 ? difficultyData.candidateLimit : pointCount;
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
            ("candidateLimit", candidateLimit.ToString()),
            ("legalCount", candidates.Count.ToString()),
            ("pickedCoords", pickedCandidate.coords?.ToString() ?? "null"),
            ("pickedWeight", pickedCandidate.visits.ToString()));
        return pickedCandidate.coords;
    }

    private List<AiMoveCandidate> BuildFilteredCandidates(List<AiMoveCandidate> candidates, DuelAiDifficultyDataType difficultyData)
    {
        List<AiMoveCandidate> filteredCandidates = new List<AiMoveCandidate>();
        float maxScoreLoss = Mathf.Max(difficultyData.maxScoreLoss, 0f);
        foreach (AiMoveCandidate candidate in candidates) {
            if (candidate.scoreLoss <= maxScoreLoss) {
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

    private int ParseInt(JToken token)
    {
        if (int.TryParse(token?.ToString(), out int value)) {
            return value;
        }

        return 0;
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
}
