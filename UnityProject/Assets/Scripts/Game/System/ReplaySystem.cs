using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class ReplaySystem : SystemBase
{
    public override string systemName => GetSystemName<ReplaySystem>();

    private const string ConfigChartAnalysisEnabled = "chartAnalysisEnabled";
    private const string ConfigChartLoadingSampleRatio = "chartLoadingSampleRatio";
    private const string ConfigChartLoadingSampleMin = "chartLoadingSampleMin";
    private const string ConfigChartLowMaxVisits9 = "chartLowMaxVisits9";
    private const string ConfigChartLowMaxVisits13 = "chartLowMaxVisits13";
    private const string ConfigChartLowMaxVisits19 = "chartLowMaxVisits19";
    private const string ConfigChartHighMaxVisits9 = "chartHighMaxVisits9";
    private const string ConfigChartHighMaxVisits13 = "chartHighMaxVisits13";
    private const string ConfigChartHighMaxVisits19 = "chartHighMaxVisits19";
    private const string ConfigChartPcLowMaxVisits9 = "chartPcLowMaxVisits9";
    private const string ConfigChartPcLowMaxVisits13 = "chartPcLowMaxVisits13";
    private const string ConfigChartPcLowMaxVisits19 = "chartPcLowMaxVisits19";
    private const string ConfigChartPcHighMaxVisits9 = "chartPcHighMaxVisits9";
    private const string ConfigChartPcHighMaxVisits13 = "chartPcHighMaxVisits13";
    private const string ConfigChartPcHighMaxVisits19 = "chartPcHighMaxVisits19";
    private const string ConfigChartHighRefreshEnabled = "chartHighRefreshEnabled";
    private const string ConfigChartLowBatchTurnsLimit = "chartLowBatchTurnsLimit";
    private const string ConfigChartHighBatchTurnsLimit = "chartHighBatchTurnsLimit";
    private const string ChartTierLow = "chart_low";
    private const string ChartTierHigh = "chart_high";
    private const string ChartTierCurrent = "chart_current";
    private const string ChartSourceChart = "chart";
    private const string ChartSourceAi = "ai";
    private const int ReplayChartCurrentPriority = 75;
    private const int ReplayChartLoadingLowPriority = 65;
    private const int ReplayOwnershipPriority = 85;
    private const int ReplayChartBackgroundLowPriority = 30;
    private const int ReplayChartBackgroundHighPriority = 20;

    private SceneComponentReplay compReplay;
    private SceneComponentChessBoard compChessBoard;
    private SceneComponentDuel compDuel;
    private readonly string kataGoRequestOwnerKey = $"ReplaySystem:{Guid.NewGuid():N}";
    private string recordFilePath = string.Empty;
    private CancellationTokenSource sceneCancellationTokenSource = new CancellationTokenSource();
    private CancellationTokenSource aiAnalysisCancellationTokenSource;
    private CancellationTokenSource ownershipCancellationTokenSource;
    private CancellationTokenSource chartLoadingCancellationTokenSource;
    private CancellationTokenSource chartBackgroundCancellationTokenSource;
    private CancellationTokenSource cursorChartCancellationTokenSource;
    private bool chartBackgroundStoppedByFailure;
    private int chartCursorRequestVersion;
    private int activeCursorChartMoveIndex = -1;
    private bool hasOwnershipRender;
    private bool hasOwnershipScore;
    private DuelOwnershipScore ownershipScore;

    private struct ChartAnalysisBatchResult
    {
        public readonly int moveIndex;
        public readonly ReplayChartPoint point;

        public ChartAnalysisBatchResult(int moveIndex, ReplayChartPoint point)
        {
            this.moveIndex = moveIndex;
            this.point = point;
        }
    }

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

    public override void OnDestroy()
    {
        CancelAiAnalysisRequest();
        CancelOwnershipRequest();
        CancelChartLoadingRequest();
        CancelChartBackgroundRequest();
        CancelCursorChartRequest();
        KataGoBootstrap.CancelQueuedAnalyzeRequests(kataGoRequestOwnerKey);
        sceneCancellationTokenSource.Cancel();
        sceneCancellationTokenSource.Dispose();
        sceneCancellationTokenSource = null;
        base.OnDestroy();
    }

    public bool IsReplayLoaded => compReplay != null && compReplay.isReplayLoaded;
    public bool IsTryMode => compReplay != null && compReplay.isTryMode;
    public int ReplayCursorMoveIndex => compReplay != null ? compReplay.replayCursorMoveIndex : 0;
    public int ReplayMoveCount => compReplay != null ? compReplay.replayMoves.Count : 0;
    public int TryMoveCount => compReplay != null ? compReplay.tryMoves.Count : 0;
    public int TryCursorMoveIndex => compReplay != null ? compReplay.tryCursorMoveIndex : 0;
    public int ReplayBoardSize => compReplay != null ? compReplay.replayBoardSize : 0;
    public PlayerFlag CurrentTryPlayerFlag => compReplay != null ? ResolveNextTryPlayerFlag() : 0;
    public PlayerFlag TryPlayerFlagOverride => compReplay != null ? compReplay.tryPlayerFlagOverride : 0;
    public bool IsAiAnalyzing => compReplay != null && compReplay.isAiAnalyzing;
    public bool HasAiAnalysisRender => compReplay != null && compReplay.hasAiAnalysisRender;
    public bool IsOwnershipAnalyzing => ownershipCancellationTokenSource != null;
    public bool HasOwnershipRender => hasOwnershipRender;
    public bool HasOwnershipResult => hasOwnershipScore;
    public bool IsAiAnalysisEnabled => KataGoAiAnalysisConfigService.IsAiAnalysisEnabled;
    public bool IsChartReady => compReplay != null && compReplay.isChartReady;
    public bool IsFreeLayout => compReplay != null && compReplay.isFreeLayout;
    public bool IsChartHidden => compReplay != null && compReplay.hideChart;
    public IReadOnlyList<ReplayChartPoint> ChartPoints => compReplay != null ? compReplay.chartPoints : null;
    public string ReplayStatus => BuildReplayStatusText();
    public string ReplayGameId => scene.sceneCreateParams != null ? scene.sceneCreateParams.replayGameId : string.Empty;

    private static bool IsMobilePlayerBuild
    {
        get
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

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
            TryRequestCurrentCursorChartPoint();
            return;
        }

        ApplyReplayCursor(0);
        TryRequestCurrentCursorChartPoint();
    }

    public void GoPrev()
    {
        GoRelative(-1);
    }

    public void GoNext()
    {
        GoRelative(1);
    }

    public void GoRelative(int cursorDelta)
    {
        if (!IsReplayLoaded || compReplay == null || cursorDelta == 0) {
            return;
        }

        ClearAiRecommendationMarkers();
        if (IsTryMode) {
            ApplyTryCursor(compReplay.tryCursorMoveIndex + cursorDelta);
            TryRequestCurrentCursorChartPoint();
            return;
        }

        ApplyReplayCursor(compReplay.replayCursorMoveIndex + cursorDelta);
        TryRequestCurrentCursorChartPoint();
    }

    public void GoLast()
    {
        ClearAiRecommendationMarkers();
        if (IsTryMode) {
            ApplyTryCursor(compReplay.tryMoves.Count);
            TryRequestCurrentCursorChartPoint();
            return;
        }

        ApplyReplayCursor(compReplay.replayMoves.Count);
        TryRequestCurrentCursorChartPoint();
    }

    public void GoToReplayMove(int targetCursorMoveIndex)
    {
        if (!IsReplayLoaded) {
            return;
        }

        ClearAiRecommendationMarkers();
        if (IsTryMode) {
            ExitTryMode();
        }

        ApplyReplayCursor(targetCursorMoveIndex);
        TryRequestCurrentCursorChartPoint();
    }

    public string BuildScrubPreviewText(int targetCursorMoveIndex)
    {
        if (!IsReplayLoaded) {
            return "未加载复盘";
        }

        int safeCursor = Mathf.Clamp(targetCursorMoveIndex, 0, compReplay.replayMoves.Count);
        if (safeCursor <= 0) {
            return compReplay.replayInitialStones.Count > 0
                ? $"预览 初始局面 · {compReplay.replayInitialStones.Count} 颗让子"
                : "预览 初始局面";
        }

        ReplayMoveState move = compReplay.replayMoves[safeCursor - 1];
        string moveText = move.isPass ? "虚手" : move.pointText;
        return $"预览 第 {safeCursor} 手 · {GetPlayerText(move.playerFlag)} {moveText}";
    }

    public async void StartInitialChartBuild()
    {
        if (IsChartHidden) {
            return;
        }

        try {
            await BuildInitialChartAsync();
        }
        catch (System.Exception ex) {
            if (compReplay != null) {
                compReplay.chartStatus = "图表生成失败";
            }

            XNLogger.LogError("Replay initial chart analysis failed.", ("error", ex.Message));
        }
    }

    private async Task BuildInitialChartAsync()
    {
        if (!IsReplayLoaded || compReplay == null) {
            return;
        }

        CancelChartLoadingRequest();
        CancelChartBackgroundRequest();
        CancelCursorChartRequest();
        compReplay.chartPoints.Clear();
        chartBackgroundStoppedByFailure = false;
        compReplay.isChartReady = false;
        compReplay.isChartLoading = true;
        compReplay.isChartBackgroundBuilding = false;
        compReplay.isChartHighRefreshing = false;
        compReplay.chartStatus = string.Empty;
        int chartVersion = ++compReplay.chartAnalysisVersion;

        if (!GetReplayConfigBool(ConfigChartAnalysisEnabled, true)) {
            compReplay.chartStatus = "复盘图表未启用";
            compReplay.isChartReady = true;
            compReplay.isChartLoading = false;
            return;
        }

        int moveCount = compReplay.replayMoves.Count;
        if (moveCount <= 0) {
            compReplay.chartStatus = "复盘手顺为空";
            compReplay.isChartReady = true;
            compReplay.isChartLoading = false;
            return;
        }

        int sampleLimit = ResolveChartLoadingSampleCount(moveCount);
        if (sampleLimit <= 0) {
            compReplay.chartStatus = "图表后台生成中";
            compReplay.isChartLoading = false;
            StartChartBackgroundBuild();
            return;
        }

        List<int> sampleMoveIndexes = BuildChartLoadingSampleMoveIndexes(moveCount, sampleLimit);
        CancellationTokenSource requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(sceneCancellationTokenSource.Token);
        chartLoadingCancellationTokenSource = requestCancellationTokenSource;
        try {
            int completedSampleCount = 0;
            bool sampleFailed = false;
            await ProcessChartMoveIndexesAsync(
                sampleMoveIndexes,
                ResolveChartLowMaxVisits(compReplay.replayBoardSize),
                ChartTierLow,
                ChartSourceChart,
                "replay-chart-initial-low",
                ReplayChartLoadingLowPriority,
                ResolveChartLowBatchTurnsLimit(),
                chartVersion,
                requestCancellationTokenSource.Token,
                result =>
                {
                completedSampleCount += 1;
                compReplay.chartStatus = $"分析采样 {completedSampleCount}/{sampleMoveIndexes.Count}（第 {result.moveIndex} 手）";

                if (!IsValidChartPoint(result.point)) {
                    chartBackgroundStoppedByFailure = true;
                    compReplay.chartStatus = $"图表采样第 {result.moveIndex} 手生成失败";
                    sampleFailed = true;
                } else {
                    UpsertChartPoint(result.point);
                }
                });

            if (!sampleFailed) {
                compReplay.chartStatus = $"图表已生成采样 {compReplay.chartPoints.Count} 点";
            }
        }
        catch (OperationCanceledException) {
        }
        catch (System.Exception ex) {
            compReplay.chartStatus = "图表生成失败";
            compReplay.isChartReady = compReplay.chartPoints.Count > 0;
            XNLogger.LogError("Replay chart analysis failed.", ("error", ex.Message));
        }
        finally {
            if (chartLoadingCancellationTokenSource == requestCancellationTokenSource) {
                chartLoadingCancellationTokenSource = null;
            }

            compReplay.isChartLoading = false;
            bool wasCanceled = requestCancellationTokenSource.IsCancellationRequested;
            requestCancellationTokenSource.Dispose();
            if (compReplay != null && !wasCanceled) {
                StartChartBackgroundBuild();
            }
        }
    }

    public async void StartChartBackgroundBuild()
    {
        if (IsChartHidden) {
            return;
        }

        await BuildChartInBackgroundAsync();
    }

    private async Task BuildChartInBackgroundAsync()
    {
        if (!scene.isMainScene || !IsReplayLoaded || compReplay == null || compReplay.isChartBackgroundBuilding || compReplay.isChartHighRefreshing || compReplay.isChartReady) {
            return;
        }

        if (!GetReplayConfigBool(ConfigChartAnalysisEnabled, true)) {
            return;
        }

        int moveCount = compReplay.replayMoves.Count;
        if (moveCount <= 0) {
            compReplay.isChartReady = true;
            return;
        }

        compReplay.isChartBackgroundBuilding = true;
        compReplay.isChartHighRefreshing = false;
        CancellationTokenSource requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(sceneCancellationTokenSource.Token);
        chartBackgroundCancellationTokenSource = requestCancellationTokenSource;
        CancellationToken cancellationToken = requestCancellationTokenSource.Token;
        int chartVersion = compReplay.chartAnalysisVersion;
        try {
            List<int> backgroundMoveIndexes = new List<int>();
            for (int moveIndex = 0; moveIndex <= moveCount; moveIndex++) {
                if (GetChartPoint(moveIndex) == null) {
                    backgroundMoveIndexes.Add(moveIndex);
                }
            }

            int completedBackgroundCount = 0;
            compReplay.chartStatus = $"低精度曲线补全中（{backgroundMoveIndexes.Count} 点排队）";
            await ProcessChartMoveIndexesAsync(
                backgroundMoveIndexes,
                ResolveChartLowMaxVisits(compReplay.replayBoardSize),
                ChartTierLow,
                ChartSourceChart,
                "replay-chart-background-low",
                ReplayChartBackgroundLowPriority,
                ResolveChartLowBatchTurnsLimit(),
                chartVersion,
                cancellationToken,
                result =>
                {
                if (!IsValidChartPoint(result.point)) {
                    chartBackgroundStoppedByFailure = true;
                    compReplay.chartStatus = $"图表后台生成暂停，第 {result.moveIndex} 手待重试";
                    XNLogger.LogWarn("Replay chart background analysis stopped after failure.", ("moveIndex", result.moveIndex.ToString()));
                    requestCancellationTokenSource.Cancel();
                    return;
                }

                UpsertChartPoint(result.point);
                completedBackgroundCount += 1;
                compReplay.chartStatus = $"低精度曲线补全 {completedBackgroundCount}/{backgroundMoveIndexes.Count}（第 {result.moveIndex} 手）";
                });

            if (!chartBackgroundStoppedByFailure && IsChartHighPrecisionRequestEnabled() && GetReplayConfigBool(ConfigChartHighRefreshEnabled, true)) {
                compReplay.isChartHighRefreshing = true;
                List<int> highMoveIndexes = new List<int>();
                for (int moveIndex = 0; moveIndex <= moveCount; moveIndex++) {
                    highMoveIndexes.Add(moveIndex);
                }

                int completedHighCount = 0;
                compReplay.chartStatus = $"高精度曲线刷新中（{highMoveIndexes.Count} 点排队）";
                await ProcessChartMoveIndexesAsync(
                    highMoveIndexes,
                    ResolveChartHighMaxVisits(compReplay.replayBoardSize),
                    ChartTierHigh,
                    ChartSourceChart,
                    "replay-chart-background-high",
                    ReplayChartBackgroundHighPriority,
                    ResolveChartHighBatchTurnsLimit(),
                    chartVersion,
                    cancellationToken,
                    result =>
                    {
                    if (IsValidChartPoint(result.point)) {
                        UpsertChartPoint(result.point);
                    }

                    completedHighCount += 1;
                    compReplay.chartStatus = $"高精度曲线刷新 {completedHighCount}/{highMoveIndexes.Count}（第 {result.moveIndex} 手）";
                    });
            }

            compReplay.isChartReady = !chartBackgroundStoppedByFailure && compReplay.chartPoints.Count > 0;
            if (!chartBackgroundStoppedByFailure) {
                compReplay.chartStatus = $"图表已生成 {compReplay.chartPoints.Count} 点";
            }
        }
        catch (OperationCanceledException) {
        }
        catch (System.Exception ex) {
            compReplay.chartStatus = "图表后台生成失败";
            XNLogger.LogError("Replay chart background analysis failed.", ("error", ex.Message));
        }
        finally {
            if (chartBackgroundCancellationTokenSource == requestCancellationTokenSource) {
                chartBackgroundCancellationTokenSource = null;
            }

            compReplay.isChartBackgroundBuilding = false;
            compReplay.isChartHighRefreshing = false;
            requestCancellationTokenSource.Dispose();
            if (compReplay != null) {
                TryRequestCurrentCursorChartPoint();
            }
        }
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
        compReplay.tryPlayerFlagOverride = 0;
        compReplay.tryMoves.Clear();
        compReplay.isTryMode = true;
        compReplay.replayStatus = string.Empty;
        return true;
    }

    public bool ExitTryMode()
    {
        if (!IsTryMode || IsFreeLayout) {
            return false;
        }

        ClearAiRecommendationMarkers();
        compReplay.isTryMode = false;
        compReplay.tryMoves.Clear();
        compReplay.tryCursorMoveIndex = 0;
        compReplay.tryPlayerFlagOverride = 0;
        ApplyReplayCursor(compReplay.tryBaseCursorMoveIndex);
        TryRequestCurrentCursorChartPoint();
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
        int appliedVariationCount = compReplay.tryPlayerFlagOverride == 0 ? ApplyAiRecommendationVariation(aiVariation) : 0;
        SyncTryBoardMarkers();
        compReplay.replayStatus = appliedVariationCount > 0 ? $"已展开AI推荐变化 {appliedVariationCount} 手" : string.Empty;
        return true;
    }

    public void SetTryPlayerFlagOverride(PlayerFlag playerFlag)
    {
        if (!IsReplayLoaded || !IsTryMode || compReplay == null) {
            return;
        }

        compReplay.tryPlayerFlagOverride = IsValidTryPlayerFlag(playerFlag) ? playerFlag : 0;
        compReplay.replayStatus = string.Empty;
    }

    public async void RequestAiAnalysis()
    {
        await RequestAiAnalysisAsync();
    }

    public void ClearAiAnalysisRender()
    {
        ClearAiRecommendationMarkers();
    }

    public async void RequestOwnershipAnalysis()
    {
        await RequestOwnershipAnalysisAsync();
    }

    public void ClearOwnershipRender()
    {
        CancelOwnershipRequest();
        hasOwnershipRender = false;
        hasOwnershipScore = false;
        ownershipScore = default;
        compChessBoard?.chessBoardGrid?.ClearOwnership();
    }

    public bool TryGetOwnershipScore(out DuelOwnershipScore score)
    {
        score = ownershipScore;
        return hasOwnershipScore;
    }

    private async Task RequestOwnershipAnalysisAsync()
    {
        if (!IsReplayLoaded || compChessBoard?.chessBoardGrid == null) {
            return;
        }

        if (ownershipCancellationTokenSource != null) {
            return;
        }

        ClearOwnershipRender();
        CancellationTokenSource requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(sceneCancellationTokenSource.Token);
        ownershipCancellationTokenSource = requestCancellationTokenSource;
        try {
            string requestId = $"replay-ownership-{DateTime.UtcNow.Ticks}";
            JObject query = KataGoPositionJsonBuilder.BuildOwnershipAnalysisJson(scene, requestId);
            if (compReplay != null) {
                query["rules"] = compReplay.replayRules;
                query["komi"] = compReplay.replayKomi;
            }

            KataGoAnalyzeOptions options = CreateReplayRetryUntilCanceledAnalyzeOptions("replay-ownership");
            options.priority = ReplayOwnershipPriority;
            JObject result = await KataGoBootstrap.AnalyzeAsync(query, options, requestCancellationTokenSource.Token);
            if (requestCancellationTokenSource.IsCancellationRequested) {
                return;
            }

            JArray ownership = result?["ownership"] as JArray;
            if (ownership != null) {
                ownershipScore = DuelOwnershipQueryService.CalculateOwnershipScore(ownership, query);
                hasOwnershipScore = true;
            }

            hasOwnershipRender = KataGoAiAnalysisRenderService.DrawOwnership(compChessBoard, result);
        }
        catch (OperationCanceledException) {
        }
        catch (Exception ex) {
            XNLogger.LogError("Replay ownership analysis failed.", ("error", ex.Message));
        }
        finally {
            if (ownershipCancellationTokenSource == requestCancellationTokenSource) {
                ownershipCancellationTokenSource = null;
            }

            requestCancellationTokenSource.Dispose();
        }
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

        int cooldownMs = KataGoAiAnalysisConfigService.AnalysisCooldownMs;
        if (cooldownMs > 0 && Time.realtimeSinceStartup - compReplay.lastAiAnalysisRequestTime < cooldownMs / 1000f) {
            return;
        }

        ClearAiRecommendationMarkers();
        CancellationTokenSource requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(sceneCancellationTokenSource.Token);
        aiAnalysisCancellationTokenSource = requestCancellationTokenSource;
        compReplay.isAiAnalyzing = true;
        compReplay.aiAnalysisStatus = "AI分析中";
        compReplay.lastAiAnalysisRequestTime = Time.realtimeSinceStartup;
        int requestVersion = ++compReplay.aiAnalysisVersion;
        PrepareChartAnalysisForAiRequest();

        try {
            ReplayScene replayScene = scene as ReplayScene;
            if (replayScene == null) {
                compReplay.aiAnalysisStatus = "AI分析失败";
                return;
            }

            bool renderedAnyResult = false;
            List<KataGoAiAnalysisTier> tiers = KataGoAiAnalysisConfigService.BuildAiAnalysisTiers(compReplay.replayBoardSize);
            int requestMoveIndex = IsTryMode
                ? Mathf.Clamp(compReplay.tryBaseCursorMoveIndex + compReplay.tryCursorMoveIndex, 0, compReplay.replayMoves.Count)
                : Mathf.Clamp(compReplay.replayCursorMoveIndex, 0, compReplay.replayMoves.Count);
            for (int i = 0; i < tiers.Count; i++) {
                KataGoAiAnalysisTier tier = tiers[i];
                compReplay.aiAnalysisStatus = $"AI分析中 {tier.tier}/{tiers.Count}";
                string requestId = $"replay-ai-tier{tier.tier}-{System.DateTime.UtcNow.Ticks}";
                JObject query = KataGoPositionJsonBuilder.BuildReplayAiAnalysisJson(
                    replayScene,
                    requestId,
                    tier.maxVisits,
                    tier.includeOwnership,
                    KataGoAiAnalysisConfigService.IncludePolicy);
                KataGoAiAnalysisConfigService.ApplyAiAnalysisRequestSettings(query);

                KataGoAnalyzeOptions options = CreateReplayRetryUntilCanceledAnalyzeOptions($"replay-ai-tier{tier.tier}");
                options.priority = tier.priority;
                JObject result = await KataGoBootstrap.AnalyzeAsync(query, options, requestCancellationTokenSource.Token);
                if (compReplay == null || requestVersion != compReplay.aiAnalysisVersion || requestCancellationTokenSource.IsCancellationRequested) {
                    return;
                }

                TryUpsertChartPointFromAiResult(requestMoveIndex, result, tier, requestVersion);
                bool hasOwnershipRender = DrawAiAnalysisOwnership(result);
                List<RectGridAiRecommendationMarker> markers = BuildAiRecommendationMarkers(result);
                bool hasRecommendationRender = false;
                if (markers.Count > 0) {
                    compChessBoard.chessBoardGrid.DrawAiRecommendationMarkers(markers);
                    hasRecommendationRender = true;
                    renderedAnyResult = true;
                }

                compReplay.hasAiAnalysisRender = compReplay.hasAiAnalysisRender || hasOwnershipRender || hasRecommendationRender;
                compReplay.aiAnalysisStatus = markers.Count > 0
                    ? $"AI推荐 {markers.Count} 点（{tier.tier}/{tiers.Count}）"
                    : $"AI暂无推荐点（{tier.tier}/{tiers.Count}）";

                await Task.Yield();
            }

            if (!renderedAnyResult && compReplay != null && requestVersion == compReplay.aiAnalysisVersion) {
                compReplay.aiAnalysisStatus = "AI暂无推荐点";
            }
        }
        catch (OperationCanceledException) {
            if (compReplay != null && requestVersion == compReplay.aiAnalysisVersion) {
                compReplay.aiAnalysisStatus = string.Empty;
            }
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

            if (aiAnalysisCancellationTokenSource == requestCancellationTokenSource) {
                aiAnalysisCancellationTokenSource = null;
            }
            requestCancellationTokenSource.Dispose();
            ResumeChartAnalysisAfterAiRequest();
        }
    }

    public string BuildSummaryText()
    {
        if (!IsReplayLoaded) {
            return string.IsNullOrEmpty(compReplay?.replayStatus) ? "未加载复盘记录" : compReplay.replayStatus;
        }

        string boardText = compReplay.replayBoardSize > 0 ? $"{compReplay.replayBoardSize} 路" : "未知棋盘";
        if (IsFreeLayout) {
            return $"{boardText} · 自由布局";
        }

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

        if (!string.IsNullOrEmpty(compReplay.chartStatus)) {
            return compReplay.chartStatus;
        }

        if (IsTryMode) {
            string playerText = GetPlayerText(ResolveNextTryPlayerFlag());
            return $"试下模式不会写回原始复盘归档。当前轮到{playerText}方试下。";
        }

        return "复盘场景已切换到棋盘级渲染，当前页面只保留控制层。";
    }

    public string BuildChartProgressText()
    {
        if (!IsReplayLoaded || compReplay == null || IsChartHidden) {
            return string.Empty;
        }

        if (compReplay.isChartLoading || compReplay.isChartBackgroundBuilding) {
            return string.IsNullOrEmpty(compReplay.chartStatus) ? "图表生成中" : compReplay.chartStatus;
        }

        if (!compReplay.isChartReady && !string.IsNullOrEmpty(compReplay.chartStatus)) {
            return compReplay.chartStatus;
        }

        return string.Empty;
    }

    public string BuildChartSummaryText()
    {
        if (IsChartHidden) {
            return string.Empty;
        }

        if (!IsReplayLoaded) {
            return "图表未加载";
        }

        ReplayChartPoint point = GetChartPoint(compReplay.replayCursorMoveIndex);
        if (point == null && (!compReplay.isChartReady || compReplay.isChartBackgroundBuilding || compReplay.isChartLoading)) {
            return "当前手图表待生成";
        }

        if (point == null && compReplay.chartPoints.Count == 0) {
            return "暂无图表数据";
        }

        string winrateText = point != null && point.hasWinrate
            ? $"黑胜率 {Mathf.RoundToInt(Mathf.Clamp01(point.blackWinrate) * 100f)}%"
            : "黑胜率 --";
        string scoreText = point != null && point.hasScoreLead
            ? $"目差 {FormatScoreLead(point.scoreLead)}"
            : "目差 --";
        return $"{winrateText} · {scoreText}";
    }

    public string BuildSgfExportFileName()
    {
        return DuelSgfReplayExporter.BuildDefaultFileName(ReplayGameId);
    }

    public bool TryExportSgf(string sgfFilePath, out string message)
    {
        return DuelSgfReplayExporter.TryExport(compReplay, ReplayGameId, sgfFilePath, out message);
    }

    private void LoadReplayRecord()
    {
        if (compReplay == null || compChessBoard == null || compDuel == null) {
            if (compReplay != null) {
                compReplay.replayStatus = "复盘场景组件缺失";
            }
            return;
        }

        if (scene.sceneCreateParams != null && scene.sceneCreateParams.replayFreeLayout) {
            LoadFreeLayoutRecord();
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
            compReplay.replayRules = KataGoDuelRecordFile.TryGetRules(recordJson, out string rules)
                ? rules
                : KataGoDuelRecordFile.Rules;
            compReplay.replayKomi = KataGoDuelRecordFile.TryGetKomi(recordJson, out float komi)
                ? komi
                : KataGoDuelRecordFile.Komi;
            compReplay.replayHandicapCount = KataGoDuelRecordFile.TryGetHandicapCount(recordJson, out int handicapCount)
                ? handicapCount
                : 0;
            compChessBoard.boardCfgId.value = $"{boardSize}x{boardSize}";
            if (TryLoadReplayRecord(recordJson)) {
                compReplay.isReplayLoaded = true;
            }
        } else {
            compReplay.replayStatus = "复盘记录读取失败";
        }
    }

    private void LoadFreeLayoutRecord()
    {
        const int defaultBoardSize = 19;

        compReplay.replayMoves.Clear();
        compReplay.replayInitialStones.Clear();
        compReplay.tryMoves.Clear();
        compReplay.replayBoardSize = defaultBoardSize;
        compReplay.replayRules = KataGoDuelRecordFile.Rules;
        compReplay.replayKomi = KataGoDuelRecordFile.Komi;
        compReplay.replayHandicapCount = 0;
        compReplay.replayCursorMoveIndex = 0;
        compReplay.tryBaseCursorMoveIndex = 0;
        compReplay.tryCursorMoveIndex = 0;
        compReplay.tryPlayerFlagOverride = 0;
        compReplay.isReplayLoaded = true;
        compReplay.isFreeLayout = true;
        compReplay.hideChart = scene.sceneCreateParams == null || scene.sceneCreateParams.replayHideChart;
        compReplay.isTryMode = true;
        compReplay.isChartReady = true;
        compReplay.replayStatus = string.Empty;
        compChessBoard.boardCfgId.value = $"{defaultBoardSize}x{defaultBoardSize}";
    }

    private List<RectGridAiRecommendationMarker> BuildAiRecommendationMarkers(JObject result)
    {
        return KataGoAiAnalysisRenderService.BuildRecommendationMarkers(
            scene,
            result,
            ResolveNextTryPlayerFlag(),
            compReplay?.aiRecommendationVariations);
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

    private int ResolveChartLowMaxVisits(int boardSize)
    {
        if (KataGoAiAnalysisConfigService.UsesPcAnalysisProfile) {
            if (boardSize <= 9) {
                return Mathf.Max(GetReplayConfigInt(ConfigChartPcLowMaxVisits9, 120), 1);
            }

            if (boardSize <= 13) {
                return Mathf.Max(GetReplayConfigInt(ConfigChartPcLowMaxVisits13, 96), 1);
            }

            return Mathf.Max(GetReplayConfigInt(ConfigChartPcLowMaxVisits19, 72), 1);
        }

        if (boardSize <= 9) {
            return Mathf.Max(GetReplayConfigInt(ConfigChartLowMaxVisits9, 48), 1);
        }

        if (boardSize <= 13) {
            return Mathf.Max(GetReplayConfigInt(ConfigChartLowMaxVisits13, 32), 1);
        }

        return Mathf.Max(GetReplayConfigInt(ConfigChartLowMaxVisits19, 24), 1);
    }

    private int ResolveChartHighMaxVisits(int boardSize)
    {
        if (KataGoAiAnalysisConfigService.UsesPcAnalysisProfile) {
            if (boardSize <= 9) {
                return Mathf.Max(GetReplayConfigInt(ConfigChartPcHighMaxVisits9, 600), 1);
            }

            if (boardSize <= 13) {
                return Mathf.Max(GetReplayConfigInt(ConfigChartPcHighMaxVisits13, 480), 1);
            }

            return Mathf.Max(GetReplayConfigInt(ConfigChartPcHighMaxVisits19, 320), 1);
        }

        if (boardSize <= 9) {
            return Mathf.Max(GetReplayConfigInt(ConfigChartHighMaxVisits9, 192), 1);
        }

        if (boardSize <= 13) {
            return Mathf.Max(GetReplayConfigInt(ConfigChartHighMaxVisits13, 128), 1);
        }

        return Mathf.Max(GetReplayConfigInt(ConfigChartHighMaxVisits19, 96), 1);
    }

    private int ResolveChartLoadingSampleCount(int moveCount)
    {
        int totalCount = moveCount + 1;
        int ratio = Mathf.Clamp(GetReplayConfigInt(ConfigChartLoadingSampleRatio, 35), 0, 100);
        int minCount = Mathf.Max(GetReplayConfigInt(ConfigChartLoadingSampleMin, 3), 0);
        int ratioCount = Mathf.CeilToInt(totalCount * ratio / 100f);
        return Mathf.Clamp(Mathf.Max(ratioCount, minCount), 0, totalCount);
    }

    private int ResolveChartLowBatchTurnsLimit()
    {
        return Mathf.Clamp(GetReplayConfigInt(ConfigChartLowBatchTurnsLimit, 12), 1, 100);
    }

    private int ResolveChartHighBatchTurnsLimit()
    {
        return Mathf.Clamp(GetReplayConfigInt(ConfigChartHighBatchTurnsLimit, 6), 1, 100);
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
        return float.TryParse(token?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private int ParseInt(JToken token)
    {
        return int.TryParse(token?.ToString(), out int value) ? value : 0;
    }

    private KataGoAnalyzeOptions CreateReplaySingleAttemptAnalyzeOptions(string requestKind)
    {
        KataGoAnalyzeOptions options = KataGoBootstrap.CreateSingleAttemptAnalyzeOptions(requestKind);
        options.ownerKey = kataGoRequestOwnerKey;
        return options;
    }

    private KataGoAnalyzeOptions CreateReplayRetryUntilCanceledAnalyzeOptions(string requestKind)
    {
        KataGoAnalyzeOptions options = KataGoBootstrap.CreateRetryUntilCanceledAnalyzeOptions(requestKind);
        options.ownerKey = kataGoRequestOwnerKey;
        return options;
    }

    private JObject BuildChartAnalysisJson(int moveIndex, int maxVisits, string tier)
    {
        return BuildChartAnalysisJson(new List<int> { moveIndex }, maxVisits, tier);
    }

    private JObject BuildChartAnalysisJson(List<int> moveIndexes, int maxVisits, string tier)
    {
        List<int> safeMoveIndexes = NormalizeChartMoveIndexes(moveIndexes);
        int maxMoveIndex = safeMoveIndexes.Count > 0 ? safeMoveIndexes[safeMoveIndexes.Count - 1] : 0;
        JArray analyzeTurns = new JArray();
        foreach (int moveIndex in safeMoveIndexes) {
            analyzeTurns.Add(moveIndex);
        }

        JObject query = new JObject
        {
            ["id"] = $"replay-chart-{tier}-{maxMoveIndex}-{safeMoveIndexes.Count}-{System.DateTime.UtcNow.Ticks}",
            ["rules"] = compReplay.replayRules,
            ["komi"] = compReplay.replayKomi,
            ["boardXSize"] = compReplay.replayBoardSize,
            ["boardYSize"] = compReplay.replayBoardSize,
            ["maxVisits"] = Mathf.Max(maxVisits, 1),
            ["includeOwnership"] = false,
            ["includePolicy"] = false,
            ["initialStones"] = BuildReplayInitialStonesArray(),
            ["moves"] = BuildReplayMovesArray(maxMoveIndex),
            ["analyzeTurns"] = analyzeTurns,
        };
        return query;
    }

    private List<int> NormalizeChartMoveIndexes(List<int> moveIndexes)
    {
        List<int> safeMoveIndexes = new List<int>();
        if (moveIndexes != null) {
            foreach (int moveIndex in moveIndexes) {
                int safeMoveIndex = Mathf.Clamp(moveIndex, 0, compReplay.replayMoves.Count);
                if (!safeMoveIndexes.Contains(safeMoveIndex)) {
                    safeMoveIndexes.Add(safeMoveIndex);
                }
            }
        }

        safeMoveIndexes.Sort();
        return safeMoveIndexes;
    }

    private async Task<bool> AnalyzeAndUpsertChartPoint(
        int moveIndex,
        int maxVisits,
        string tier,
        string source,
        int chartVersion,
        KataGoAnalyzeOptions options,
        CancellationToken cancellationToken)
    {
        ReplayChartPoint point = await AnalyzeChartPoint(moveIndex, maxVisits, tier, source, chartVersion, options, cancellationToken);
        if (point == null || (!point.hasWinrate && !point.hasScoreLead)) {
            return false;
        }

        UpsertChartPoint(point);
        return true;
    }

    private async Task<ReplayChartPoint> AnalyzeChartPoint(
        int moveIndex,
        int maxVisits,
        string tier,
        string source,
        int chartVersion,
        KataGoAnalyzeOptions options,
        CancellationToken cancellationToken)
    {
        JObject query = BuildChartAnalysisJson(moveIndex, maxVisits, tier);
        JObject result = await KataGoBootstrap.AnalyzeAsync(query, options, cancellationToken);
        return ParseChartPoint(moveIndex, result, maxVisits, tier, source, chartVersion);
    }

    private bool IsValidChartPoint(ReplayChartPoint point)
    {
        return point != null && (point.hasWinrate || point.hasScoreLead);
    }

    private bool TryResolveChartResultMoveIndex(JObject result, List<int> expectedMoveIndexes, out int moveIndex)
    {
        moveIndex = -1;
        if (int.TryParse(result?["turnNumber"]?.ToString(), out int turnNumber)) {
            if (expectedMoveIndexes != null && expectedMoveIndexes.Contains(turnNumber)) {
                moveIndex = turnNumber;
                return true;
            }

            return false;
        }

        if (expectedMoveIndexes == null || expectedMoveIndexes.Count == 1) {
            moveIndex = expectedMoveIndexes != null && expectedMoveIndexes.Count == 1 ? expectedMoveIndexes[0] : 0;
            return true;
        }

        return false;
    }

    private async Task ProcessChartMoveIndexesAsync(
        List<int> moveIndexes,
        int maxVisits,
        string tier,
        string source,
        string requestKind,
        int priority,
        int batchLimit,
        int chartVersion,
        CancellationToken cancellationToken,
        Action<ChartAnalysisBatchResult> onResult)
    {
        if (moveIndexes == null || moveIndexes.Count == 0) {
            return;
        }

        int safeBatchLimit = Mathf.Max(batchLimit, 1);
        for (int start = 0; start < moveIndexes.Count; start += safeBatchLimit) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!scene.isMainScene || compReplay == null || compReplay.chartAnalysisVersion != chartVersion) {
                return;
            }

            int count = Mathf.Min(safeBatchLimit, moveIndexes.Count - start);
            List<int> batchMoveIndexes = NormalizeChartMoveIndexes(moveIndexes.GetRange(start, count));
            KataGoAnalyzeOptions options = CreateReplaySingleAttemptAnalyzeOptions(requestKind);
            options.priority = priority;
            JObject query = BuildChartAnalysisJson(batchMoveIndexes, maxVisits, tier);
            List<JObject> results = await KataGoBootstrap.AnalyzeTurnsAsync(query, batchMoveIndexes, options, cancellationToken);
            HashSet<int> completedMoveIndexes = new HashSet<int>();
            foreach (JObject result in results) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!scene.isMainScene || compReplay == null || compReplay.chartAnalysisVersion != chartVersion) {
                    return;
                }

                if (!TryResolveChartResultMoveIndex(result, batchMoveIndexes, out int resultMoveIndex)) {
                    continue;
                }

                completedMoveIndexes.Add(resultMoveIndex);
                ReplayChartPoint point = ParseChartPoint(resultMoveIndex, result, maxVisits, tier, source, chartVersion);
                onResult?.Invoke(new ChartAnalysisBatchResult(resultMoveIndex, point));
            }

            foreach (int moveIndex in batchMoveIndexes) {
                if (!completedMoveIndexes.Contains(moveIndex)) {
                    onResult?.Invoke(new ChartAnalysisBatchResult(moveIndex, null));
                }
            }

            await Task.Yield();
        }
    }

    private void UpsertChartPoint(ReplayChartPoint point)
    {
        if (point == null || compReplay == null) {
            return;
        }

        for (int i = 0; i < compReplay.chartPoints.Count; i++) {
            if (compReplay.chartPoints[i] != null && compReplay.chartPoints[i].moveIndex == point.moveIndex) {
                if (!ShouldReplaceChartPoint(compReplay.chartPoints[i], point)) {
                    return;
                }

                compReplay.chartPoints[i] = point;
                SortChartPoints();
                return;
            }
        }

        compReplay.chartPoints.Add(point);
        SortChartPoints();
    }

    private bool ShouldReplaceChartPoint(ReplayChartPoint current, ReplayChartPoint incoming)
    {
        if (current == null) {
            return true;
        }

        if (incoming == null) {
            return false;
        }

        if (incoming.analysisVersion != 0 && current.analysisVersion != 0 && incoming.analysisVersion != current.analysisVersion) {
            return incoming.analysisVersion > current.analysisVersion;
        }

        if (incoming.analysisVisits != current.analysisVisits) {
            return incoming.analysisVisits > current.analysisVisits;
        }

        return GetChartSourcePriority(incoming.analysisSource) >= GetChartSourcePriority(current.analysisSource);
    }

    private int GetChartSourcePriority(string source)
    {
        return string.Equals(source, ChartSourceAi, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private void SortChartPoints()
    {
        compReplay.chartPoints.Sort((left, right) =>
        {
            int leftIndex = left != null ? left.moveIndex : int.MaxValue;
            int rightIndex = right != null ? right.moveIndex : int.MaxValue;
            return leftIndex.CompareTo(rightIndex);
        });
    }

    private List<int> BuildChartLoadingSampleMoveIndexes(int moveCount, int sampleLimit)
    {
        List<int> indexes = new List<int>();
        int totalCount = moveCount + 1;
        if (sampleLimit >= totalCount) {
            for (int i = 0; i < totalCount; i++) {
                indexes.Add(i);
            }
            return indexes;
        }

        for (int i = 0; i < sampleLimit; i++) {
            int moveIndex = Mathf.RoundToInt((float)i * moveCount / Mathf.Max(sampleLimit - 1, 1));
            if (!indexes.Contains(moveIndex)) {
                indexes.Add(moveIndex);
            }
        }

        if (!indexes.Contains(moveCount)) {
            indexes[indexes.Count - 1] = moveCount;
        }

        indexes.Sort();
        return indexes;
    }

    private JArray BuildReplayInitialStonesArray()
    {
        JArray initialStones = new JArray();
        foreach (ReplayMoveState stone in compReplay.replayInitialStones) {
            if (stone == null || stone.coords == null || stone.isPass) {
                continue;
            }

            string color = KataGoPositionJsonBuilder.ToKataGoColor(stone.playerFlag);
            if (!string.IsNullOrEmpty(color)) {
                initialStones.Add(new JArray(color, KataGoPositionJsonBuilder.ToKataGoPoint(stone.coords, compReplay.replayBoardSize)));
            }
        }

        return initialStones;
    }

    private JArray BuildReplayMovesArray(int moveCount)
    {
        JArray moves = new JArray();
        int safeCount = Mathf.Clamp(moveCount, 0, compReplay.replayMoves.Count);
        for (int i = 0; i < safeCount; i++) {
            ReplayMoveState move = compReplay.replayMoves[i];
            if (move == null) {
                continue;
            }

            string color = KataGoPositionJsonBuilder.ToKataGoColor(move.playerFlag);
            if (!string.IsNullOrEmpty(color)) {
                moves.Add(new JArray(color, move.isPass ? KataGoPositionJsonBuilder.PassPoint : move.pointText));
            }
        }

        return moves;
    }

    private ReplayChartPoint ParseChartPoint(int moveIndex, JObject result, int requestedMaxVisits, string tier, string source, int chartVersion)
    {
        JToken rootInfo = result?["rootInfo"];
        int actualVisits = ParseInt(rootInfo?["visits"]);
        ReplayChartPoint point = new ReplayChartPoint
        {
            moveIndex = moveIndex,
            analysisVisits = actualVisits > 0 ? actualVisits : Mathf.Max(requestedMaxVisits, 1),
            analysisTier = tier ?? string.Empty,
            analysisSource = source ?? string.Empty,
            analysisVersion = chartVersion,
        };
        if (TryParseFloat(rootInfo?["winrate"], out float winrate)) {
            point.hasWinrate = true;
            point.blackWinrate = Mathf.Clamp01(winrate);
        }

        if (TryParseFloat(rootInfo?["scoreLead"], out float scoreLead)) {
            point.hasScoreLead = true;
            point.scoreLead = scoreLead;
        }

        return point;
    }

    private ReplayChartPoint GetChartPoint(int moveIndex)
    {
        if (compReplay == null || compReplay.chartPoints.Count == 0) {
            return null;
        }

        int safeMoveIndex = Mathf.Clamp(moveIndex, 0, compReplay.replayMoves.Count);
        foreach (ReplayChartPoint point in compReplay.chartPoints) {
            if (point != null && point.moveIndex == safeMoveIndex) {
                return point;
            }
        }

        return null;
    }

    private void TryRequestCurrentCursorChartPoint()
    {
        if (IsChartHidden) {
            CancelCursorChartRequest();
            return;
        }

        if (!scene.isMainScene || !IsReplayLoaded || compReplay == null || IsTryMode) {
            CancelCursorChartRequest();
            return;
        }

        if (!GetReplayConfigBool(ConfigChartAnalysisEnabled, true)) {
            return;
        }

        if (!IsChartHighPrecisionRequestEnabled()) {
            CancelCursorChartRequest();
            return;
        }

        if (compReplay.isChartLoading) {
            return;
        }

        int moveIndex = Mathf.Clamp(compReplay.replayCursorMoveIndex, 0, compReplay.replayMoves.Count);
        ReplayChartPoint currentPoint = GetChartPoint(moveIndex);
        if (currentPoint != null && currentPoint.analysisVisits >= ResolveChartHighMaxVisits(compReplay.replayBoardSize)) {
            if (activeCursorChartMoveIndex == moveIndex) {
                CancelCursorChartRequest();
            }
            return;
        }

        if (!chartBackgroundStoppedByFailure && compReplay.isChartBackgroundBuilding) {
            return;
        }

        if (activeCursorChartMoveIndex == moveIndex && cursorChartCancellationTokenSource != null) {
            return;
        }

        CancelCursorChartRequest();
        chartCursorRequestVersion += 1;
        activeCursorChartMoveIndex = moveIndex;
        CancellationTokenSource requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(sceneCancellationTokenSource.Token);
        cursorChartCancellationTokenSource = requestCancellationTokenSource;
        _ = AnalyzeCurrentCursorChartPointAsync(moveIndex, chartCursorRequestVersion, requestCancellationTokenSource);
    }

    private async Task AnalyzeCurrentCursorChartPointAsync(int moveIndex, int requestVersion, CancellationTokenSource requestCancellationTokenSource)
    {
        CancellationToken cancellationToken = requestCancellationTokenSource.Token;
        try {
            while (scene.isMainScene &&
                compReplay != null &&
                requestVersion == chartCursorRequestVersion &&
                compReplay.replayCursorMoveIndex == moveIndex &&
                !cancellationToken.IsCancellationRequested) {
                compReplay.chartStatus = $"正在补算第 {moveIndex} 手图表";
                KataGoAnalyzeOptions options = CreateReplayRetryUntilCanceledAnalyzeOptions("replay-chart-current");
                options.priority = ReplayChartCurrentPriority;
                bool success = await AnalyzeAndUpsertChartPoint(
                    moveIndex,
                    ResolveChartHighMaxVisits(compReplay.replayBoardSize),
                    ChartTierCurrent,
                    ChartSourceChart,
                    compReplay.chartAnalysisVersion,
                    options,
                    cancellationToken);
                if (success) {
                    chartBackgroundStoppedByFailure = false;
                    compReplay.chartStatus = $"当前手图表已生成";
                    return;
                }

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException) {
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Replay current cursor chart analysis failed.", ("moveIndex", moveIndex.ToString()), ("error", ex.Message));
        }
        finally {
            if (cursorChartCancellationTokenSource == requestCancellationTokenSource) {
                cursorChartCancellationTokenSource = null;
                activeCursorChartMoveIndex = -1;
            }

            requestCancellationTokenSource.Dispose();
        }
    }

    private string FormatScoreLead(float scoreLead)
    {
        if (Mathf.Abs(scoreLead) < 0.05f) {
            return "均势";
        }

        string sideText = scoreLead > 0f ? "黑+" : "白+";
        return $"{sideText}{Mathf.Abs(scoreLead):0.0}";
    }

    private bool DrawAiAnalysisOwnership(JObject result)
    {
        bool rendered = KataGoAiAnalysisRenderService.DrawOwnership(compChessBoard, result);
        hasOwnershipRender = hasOwnershipRender || rendered;
        return rendered;
    }

    private void TryUpsertChartPointFromAiResult(int moveIndex, JObject result, KataGoAiAnalysisTier tier, int requestVersion)
    {
        if (IsTryMode || compReplay == null || requestVersion != compReplay.aiAnalysisVersion || result == null) {
            return;
        }

        int safeMoveIndex = Mathf.Clamp(moveIndex, 0, compReplay.replayMoves.Count);
        ReplayChartPoint point = ParseChartPoint(
            safeMoveIndex,
            result,
            tier.maxVisits,
            $"ai_tier{tier.tier}",
            ChartSourceAi,
            compReplay.chartAnalysisVersion);
        if (IsValidChartPoint(point)) {
            UpsertChartPoint(point);
        }
    }

    private void ClearAiRecommendationMarkers()
    {
        if (compReplay != null) {
            CancelAiAnalysisRequest();
            CancelOwnershipRequest();
            compReplay.aiAnalysisVersion += 1;
            compReplay.isAiAnalyzing = false;
            compReplay.hasAiAnalysisRender = false;
            compReplay.aiAnalysisStatus = string.Empty;
            compReplay.aiRecommendationVariations.Clear();
        }

        hasOwnershipRender = false;
        hasOwnershipScore = false;
        ownershipScore = default;
        compChessBoard?.chessBoardGrid?.ClearAiRecommendationMarkers();
        compChessBoard?.chessBoardGrid?.ClearOwnership();
    }

    private void CancelAiAnalysisRequest()
    {
        if (aiAnalysisCancellationTokenSource == null) {
            return;
        }

        aiAnalysisCancellationTokenSource.Cancel();
        aiAnalysisCancellationTokenSource = null;
    }

    private void CancelOwnershipRequest()
    {
        if (ownershipCancellationTokenSource == null) {
            return;
        }

        ownershipCancellationTokenSource.Cancel();
        ownershipCancellationTokenSource = null;
    }

    private void PrepareChartAnalysisForAiRequest()
    {
        CancelCursorChartRequest();
    }

    private void ResumeChartAnalysisAfterAiRequest()
    {
        if (!scene.isMainScene || compReplay == null || compReplay.isChartLoading || IsTryMode) {
            return;
        }

        if (compReplay.isChartReady || !GetReplayConfigBool(ConfigChartAnalysisEnabled, true)) {
            return;
        }

        if (chartBackgroundStoppedByFailure) {
            TryRequestCurrentCursorChartPoint();
            return;
        }

        StartChartBackgroundBuild();
    }

    private bool IsChartHighPrecisionRequestEnabled()
    {
        return !IsMobilePlayerBuild;
    }

    private void CancelChartLoadingRequest()
    {
        if (chartLoadingCancellationTokenSource == null) {
            return;
        }

        chartLoadingCancellationTokenSource.Cancel();
        chartLoadingCancellationTokenSource = null;
    }

    private void CancelChartBackgroundRequest()
    {
        if (chartBackgroundCancellationTokenSource == null) {
            return;
        }

        chartBackgroundCancellationTokenSource.Cancel();
        chartBackgroundCancellationTokenSource = null;
    }

    private void CancelCursorChartRequest()
    {
        if (cursorChartCancellationTokenSource == null) {
            return;
        }

        cursorChartCancellationTokenSource.Cancel();
        cursorChartCancellationTokenSource = null;
        activeCursorChartMoveIndex = -1;
        chartCursorRequestVersion += 1;
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

        if (IsTryMode && IsValidTryPlayerFlag(compReplay.tryPlayerFlagOverride)) {
            return compReplay.tryPlayerFlagOverride;
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

    private static bool IsValidTryPlayerFlag(PlayerFlag playerFlag)
    {
        return playerFlag == PlayerFlag.Player1 || playerFlag == PlayerFlag.Player2;
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
