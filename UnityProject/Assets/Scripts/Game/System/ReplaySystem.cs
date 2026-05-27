using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class ReplaySystem : SystemBase
{
    public override string systemName => GetSystemName<ReplaySystem>();

    private const string ConfigAiAnalysisEnabled = "aiAnalysisEnabled";
    private const string ConfigAiMaxVisits9 = "aiMaxVisits9";
    private const string ConfigAiMaxVisits13 = "aiMaxVisits13";
    private const string ConfigAiMaxVisits19 = "aiMaxVisits19";
    private const string ConfigAiDisplayCandidateLimit = "aiDisplayCandidateLimit";
    private const string ConfigAiRequestCandidateLimit = "aiRequestCandidateLimit";
    private const string ConfigAiIncludePolicy = "aiIncludePolicy";
    private const string ConfigAiShowCurrentPlayerWinrate = "aiShowCurrentPlayerWinrate";
    private const string ConfigAiWinrateMinDisplay = "aiWinrateMinDisplay";
    private const string ConfigAiWinrateMaxDisplay = "aiWinrateMaxDisplay";
    private const string ConfigAiAnalysisCooldownMs = "aiAnalysisCooldownMs";

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
    public bool IsAiAnalyzing => compReplay != null && compReplay.isAiAnalyzing;
    public bool HasAiAnalysisRender => compReplay != null && compReplay.hasAiAnalysisRender;
    public bool IsAiAnalysisEnabled => GetReplayConfigBool(ConfigAiAnalysisEnabled, true);
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
        ClearAiRecommendationMarkers();
        if (IsTryMode) {
            ApplyTryCursor(0);
            return;
        }

        ApplyReplayCursor(0);
    }

    public void GoPrev()
    {
        ClearAiRecommendationMarkers();
        if (IsTryMode) {
            ApplyTryCursor(compReplay.tryCursorMoveIndex - 1);
            return;
        }

        ApplyReplayCursor(compReplay.replayCursorMoveIndex - 1);
    }

    public void GoNext()
    {
        ClearAiRecommendationMarkers();
        if (IsTryMode) {
            ApplyTryCursor(compReplay.tryCursorMoveIndex + 1);
            return;
        }

        ApplyReplayCursor(compReplay.replayCursorMoveIndex + 1);
    }

    public void GoLast()
    {
        ClearAiRecommendationMarkers();
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
        return EnterTryMode(true);
    }

    private bool EnterTryMode(bool clearAiRecommendation)
    {
        if (!IsReplayLoaded || IsTryMode) {
            return false;
        }

        if (clearAiRecommendation) {
            ClearAiRecommendationMarkers();
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

        ClearAiRecommendationMarkers();
        compReplay.isTryMode = false;
        compReplay.tryMoves.Clear();
        compReplay.tryCursorMoveIndex = 0;
        ApplyReplayCursor(compReplay.tryBaseCursorMoveIndex);
        compReplay.replayStatus = string.Empty;
        return true;
    }

    public bool TryApplyBoardMove(RectCoordinates coords)
    {
        if (!IsReplayLoaded || coords == null) {
            return false;
        }

        if (!IsTryMode && !EnterTryMode(false)) {
            return false;
        }

        return TryApplyTryMove(coords);
    }

    public bool TryApplyTryMove(RectCoordinates coords)
    {
        if (!IsReplayLoaded || !IsTryMode || coords == null || compChessBoard == null || compDuel == null) {
            return false;
        }

        List<ReplayAiVariationMove> aiVariation = GetAiRecommendationVariation(coords);
        ClearAiRecommendationMarkers();
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
        int appliedVariationCount = ApplyAiRecommendationVariation(aiVariation);
        SyncTryBoardMarkers();
        compReplay.replayStatus = appliedVariationCount > 0 ? $"已展开AI推荐变化 {appliedVariationCount} 手" : string.Empty;
        return true;
    }

    public async void RequestAiAnalysis()
    {
        await RequestAiAnalysisAsync();
    }

    public void ClearAiAnalysisRender()
    {
        ClearAiRecommendationMarkers();
    }

    public async Task RequestAiAnalysisAsync()
    {
        if (!IsReplayLoaded || compReplay == null || compChessBoard?.chessBoardGrid == null || compDuel == null) {
            return;
        }

        if (!IsAiAnalysisEnabled) {
            compReplay.aiAnalysisStatus = "AI分析未启用";
            return;
        }

        if (compReplay.isAiAnalyzing) {
            return;
        }

        int cooldownMs = Mathf.Max(GetReplayConfigInt(ConfigAiAnalysisCooldownMs, 500), 0);
        if (cooldownMs > 0 && Time.realtimeSinceStartup - compReplay.lastAiAnalysisRequestTime < cooldownMs / 1000f) {
            return;
        }

        ClearAiRecommendationMarkers();
        compReplay.isAiAnalyzing = true;
        compReplay.aiAnalysisStatus = "AI分析中";
        compReplay.lastAiAnalysisRequestTime = Time.realtimeSinceStartup;
        int requestVersion = ++compReplay.aiAnalysisVersion;

        try {
            ReplayScene replayScene = scene as ReplayScene;
            if (replayScene == null) {
                compReplay.aiAnalysisStatus = "AI分析失败";
                return;
            }

            string requestId = $"replay-ai-{System.DateTime.UtcNow.Ticks}";
            JObject query = KataGoPositionJsonBuilder.BuildReplayAiAnalysisJson(
                replayScene,
                requestId,
                ResolveAiMaxVisits(compReplay.replayBoardSize),
                true,
                GetReplayConfigBool(ConfigAiIncludePolicy, false));

            JObject result = await KataGoBootstrap.AnalyzeAsync(query);
            if (compReplay == null || requestVersion != compReplay.aiAnalysisVersion) {
                return;
            }

            bool hasOwnershipRender = DrawAiAnalysisOwnership(result);
            List<RectGridAiRecommendationMarker> markers = BuildAiRecommendationMarkers(result);
            bool hasRecommendationRender = false;
            if (markers.Count == 0) {
                compReplay.aiAnalysisStatus = "AI暂无推荐点";
                compReplay.hasAiAnalysisRender = hasOwnershipRender;
                return;
            }

            compChessBoard.chessBoardGrid.DrawAiRecommendationMarkers(markers);
            hasRecommendationRender = true;
            compReplay.hasAiAnalysisRender = hasOwnershipRender || hasRecommendationRender;
            compReplay.aiAnalysisStatus = $"AI推荐 {markers.Count} 点";
        }
        catch (System.Exception ex) {
            if (compReplay != null && requestVersion == compReplay.aiAnalysisVersion) {
                compReplay.aiAnalysisStatus = "AI分析失败";
            }

            XNLogger.LogError("Replay AI analysis failed.", ("error", ex.Message));
        }
        finally {
            if (compReplay != null && requestVersion == compReplay.aiAnalysisVersion) {
                compReplay.isAiAnalyzing = false;
            }
        }
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

        if (!string.IsNullOrEmpty(compReplay.aiAnalysisStatus)) {
            return compReplay.aiAnalysisStatus;
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
            compReplay.replayKomi = KataGoDuelRecordFile.TryGetKomi(recordJson, out float komi)
                ? komi
                : KataGoDuelRecordFile.Komi;
            compChessBoard.boardCfgId.value = $"{boardSize}x{boardSize}";
            if (TryLoadReplayRecord(recordJson)) {
                compReplay.isReplayLoaded = true;
            }
        } else {
            compReplay.replayStatus = "复盘记录读取失败";
        }
    }

    private List<RectGridAiRecommendationMarker> BuildAiRecommendationMarkers(JObject result)
    {
        List<RectGridAiRecommendationMarker> markers = new List<RectGridAiRecommendationMarker>();
        JArray moveInfos = result?["moveInfos"] as JArray;
        if (moveInfos == null || moveInfos.Count == 0 || compChessBoard?.chessBoardGrid == null) {
            return markers;
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        int displayLimit = Mathf.Clamp(GetReplayConfigInt(ConfigAiDisplayCandidateLimit, 5), 1, 20);
        int requestLimit = Mathf.Max(GetReplayConfigInt(ConfigAiRequestCandidateLimit, 12), displayLimit);
        bool showCurrentPlayerWinrate = GetReplayConfigBool(ConfigAiShowCurrentPlayerWinrate, true);
        int winrateMinDisplay = Mathf.Clamp(GetReplayConfigInt(ConfigAiWinrateMinDisplay, 1), 1, 100);
        int winrateMaxDisplay = Mathf.Clamp(GetReplayConfigInt(ConfigAiWinrateMaxDisplay, 100), winrateMinDisplay, 100);
        PlayerFlag currentPlayerFlag = ResolveNextTryPlayerFlag();
        List<JToken> sortedMoveInfos = BuildSortedMoveInfos(moveInfos);
        int parsedCount = 0;

        foreach (JToken token in sortedMoveInfos) {
            if (markers.Count >= displayLimit || parsedCount >= requestLimit) {
                break;
            }

            parsedCount += 1;
            string move = token?["move"]?.ToString();
            if (string.Equals(move, KataGoPositionJsonBuilder.PassPoint, System.StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(move, boardSize, out RectCoordinates coords) ||
                !TryParseFloat(token?["winrate"], out float winrate)) {
                continue;
            }

            if (!IsLegalAiRecommendation(coords, currentPlayerFlag)) {
                continue;
            }

            if (showCurrentPlayerWinrate && currentPlayerFlag == PlayerFlag.Player2) {
                winrate = 1f - winrate;
            }

            int winratePercent = Mathf.RoundToInt(Mathf.Clamp01(winrate) * 100f);
            winratePercent = Mathf.Clamp(winratePercent, winrateMinDisplay, winrateMaxDisplay);
            markers.Add(new RectGridAiRecommendationMarker(coords.x, coords.z, winratePercent, markers.Count + 1));

            int posIndex = compChessBoard.GetPosIndexByCoords(coords);
            List<ReplayAiVariationMove> variation = BuildAiRecommendationVariation(token, coords, currentPlayerFlag, boardSize);
            if (posIndex >= 0 && variation.Count > 0) {
                compReplay.aiRecommendationVariations[posIndex] = variation;
            }
        }

        return markers;
    }

    private List<ReplayAiVariationMove> BuildAiRecommendationVariation(JToken moveInfo, RectCoordinates recommendationCoords, PlayerFlag firstPlayerFlag, int boardSize)
    {
        List<ReplayAiVariationMove> variation = new List<ReplayAiVariationMove>();
        JArray pv = moveInfo?["pv"] as JArray;
        if (pv == null || pv.Count <= 1 || recommendationCoords == null || firstPlayerFlag == 0) {
            return variation;
        }

        PlayerFlag playerFlag = firstPlayerFlag;
        for (int i = 0; i < pv.Count; i++) {
            string point = pv[i]?.ToString();
            if (string.Equals(point, KataGoPositionJsonBuilder.PassPoint, System.StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(point, boardSize, out RectCoordinates coords)) {
                break;
            }

            if (i == 0) {
                if (!IsSameCoords(coords, recommendationCoords)) {
                    variation.Clear();
                    return variation;
                }

                playerFlag = playerFlag.GetOpponentPlayerFlag();
                continue;
            }

            variation.Add(new ReplayAiVariationMove
            {
                playerFlag = playerFlag,
                coords = coords.Clone(),
            });
            playerFlag = playerFlag.GetOpponentPlayerFlag();
        }

        return variation;
    }

    private List<ReplayAiVariationMove> GetAiRecommendationVariation(RectCoordinates coords)
    {
        if (coords == null || compReplay == null || compChessBoard == null || !compReplay.hasAiAnalysisRender) {
            return null;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || !compReplay.aiRecommendationVariations.TryGetValue(posIndex, out List<ReplayAiVariationMove> variation)) {
            return null;
        }

        return variation;
    }

    private int ApplyAiRecommendationVariation(List<ReplayAiVariationMove> variation)
    {
        if (variation == null || variation.Count == 0) {
            return 0;
        }

        int appliedCount = 0;
        foreach (ReplayAiVariationMove variationMove in variation) {
            PlayerFlag expectedPlayerFlag = ResolveNextTryPlayerFlag();
            if (variationMove == null || variationMove.coords == null || variationMove.playerFlag != expectedPlayerFlag) {
                break;
            }

            ReplayMoveState move = CreateReplayMoveState(expectedPlayerFlag, variationMove.coords.Clone(), false);
            if (!TryBuildAndApplyTryMove(move)) {
                break;
            }

            compReplay.tryMoves.Add(move);
            compReplay.tryCursorMoveIndex = compReplay.tryMoves.Count;
            appliedCount += 1;
        }

        return appliedCount;
    }

    private bool IsSameCoords(RectCoordinates left, RectCoordinates right)
    {
        return left != null && right != null && left.x == right.x && left.z == right.z;
    }

    private List<JToken> BuildSortedMoveInfos(JArray moveInfos)
    {
        List<JToken> sorted = new List<JToken>();
        foreach (JToken token in moveInfos) {
            sorted.Add(token);
        }

        sorted.Sort((left, right) => GetMoveInfoOrder(left).CompareTo(GetMoveInfoOrder(right)));
        return sorted;
    }

    private int GetMoveInfoOrder(JToken token)
    {
        return int.TryParse(token?["order"]?.ToString(), out int order) ? order : int.MaxValue;
    }

    private bool IsLegalAiRecommendation(RectCoordinates coords, PlayerFlag playerFlag)
    {
        if (coords == null || playerFlag == 0 || compChessBoard == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            return false;
        }

        return DuelMoveRule.CheckMoveLegal(compChessBoard, playerFlag, coords);
    }

    private int ResolveAiMaxVisits(int boardSize)
    {
        if (boardSize <= 9) {
            return Mathf.Max(GetReplayConfigInt(ConfigAiMaxVisits9, 800), 1);
        }

        if (boardSize <= 13) {
            return Mathf.Max(GetReplayConfigInt(ConfigAiMaxVisits13, 512), 1);
        }

        return Mathf.Max(GetReplayConfigInt(ConfigAiMaxVisits19, 320), 1);
    }

    private int GetReplayConfigInt(string id, int defaultValue)
    {
        ReplayConfigDataType data = ReplayConfigDataType.GetConfigData(id);
        return data != null && data.valueType == "int" ? data.intValue : defaultValue;
    }

    private bool GetReplayConfigBool(string id, bool defaultValue)
    {
        ReplayConfigDataType data = ReplayConfigDataType.GetConfigData(id);
        return data != null && data.valueType == "boolean" ? data.boolValue : defaultValue;
    }

    private bool TryParseFloat(JToken token, out float value)
    {
        return float.TryParse(token?.ToString(), out value);
    }

    private bool DrawAiAnalysisOwnership(JObject result)
    {
        JArray ownership = result?["ownership"] as JArray;
        if (ownership == null || compChessBoard?.chessBoardGrid == null) {
            return false;
        }

        if (!HasVisibleOwnership(ownership, compChessBoard.chessBoardGrid.gridSize, DuelOwnershipQueryService.OwnershipThreshold)) {
            return false;
        }

        compChessBoard.chessBoardGrid.DrawOwnership(ownership, DuelOwnershipQueryService.OwnershipThreshold);
        return true;
    }

    private bool HasVisibleOwnership(JArray ownership, int boardSize, float ownershipThreshold)
    {
        int expectedCount = boardSize * boardSize;
        if (ownership == null || ownership.Count < expectedCount) {
            return false;
        }

        for (int i = 0; i < expectedCount; i++) {
            if (float.TryParse(ownership[i]?.ToString(), out float ownershipValue) &&
                Mathf.Abs(ownershipValue) > ownershipThreshold) {
                return true;
            }
        }

        return false;
    }

    private void ClearAiRecommendationMarkers()
    {
        if (compReplay != null) {
            compReplay.aiAnalysisVersion += 1;
            compReplay.isAiAnalyzing = false;
            compReplay.hasAiAnalysisRender = false;
            compReplay.aiAnalysisStatus = string.Empty;
            compReplay.aiRecommendationVariations.Clear();
        }

        compChessBoard?.chessBoardGrid?.ClearAiRecommendationMarkers();
        compChessBoard?.chessBoardGrid?.ClearOwnership();
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
