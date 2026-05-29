using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.ChessBoard;
using XNClient.Logger;

public class DuelAiRecommendationSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelAiRecommendationSystem>();

    private readonly string kataGoRequestOwnerKey = $"DuelAiRecommendationSystem:{Guid.NewGuid():N}";
    private CancellationTokenSource sceneCancellationTokenSource = new CancellationTokenSource();
    private CancellationTokenSource aiAnalysisCancellationTokenSource;
    private int requestVersion;
    private float lastRequestTime;
    private bool isAiAnalyzing;
    private bool hasAiAnalysisRender;

    public DuelAiRecommendationSystem(DuelScene scene) : base(scene)
    {
    }

    public bool IsAiAnalyzing => isAiAnalyzing;
    public bool HasAiAnalysisRender => hasAiAnalysisRender;
    public bool IsAiAnalysisEnabled => KataGoAiAnalysisConfigService.IsAiAnalysisEnabled;

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnRequestDuelPass>(OnRequestDuelPass);
        scene.RegisterSystemEvent<OnDuelTakeBackResult>(OnDuelTakeBackResult);
        scene.RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
    }

    public override void OnDestroy()
    {
        ClearAiAnalysisRender();
        KataGoBootstrap.CancelQueuedAnalyzeRequests(kataGoRequestOwnerKey);
        sceneCancellationTokenSource.Cancel();
        sceneCancellationTokenSource.Dispose();
        sceneCancellationTokenSource = null;
        base.OnDestroy();
    }

    public async void RequestAiAnalysis()
    {
        await RequestAiAnalysisAsync();
    }

    public void ClearAiAnalysisRender()
    {
        CancelAiAnalysisRequest();
        requestVersion += 1;
        isAiAnalyzing = false;
        hasAiAnalysisRender = false;
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        compChessBoard?.chessBoardGrid?.ClearAiRecommendationMarkers();
        compChessBoard?.chessBoardGrid?.ClearOwnership();
    }

    private async Task RequestAiAnalysisAsync()
    {
        if (!CanAnalyze()) {
            return;
        }

        if (!KataGoAiAnalysisConfigService.IsAiAnalysisEnabled) {
            return;
        }

        if (isAiAnalyzing) {
            return;
        }

        int cooldownMs = KataGoAiAnalysisConfigService.AnalysisCooldownMs;
        if (cooldownMs > 0 && Time.realtimeSinceStartup - lastRequestTime < cooldownMs / 1000f) {
            return;
        }

        DuelScene duelScene = scene as DuelScene;
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (duelScene == null || compChessBoard?.chessBoardGrid == null) {
            return;
        }

        ClearAiAnalysisRender();
        CancellationTokenSource requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(sceneCancellationTokenSource.Token);
        aiAnalysisCancellationTokenSource = requestCancellationTokenSource;
        isAiAnalyzing = true;
        lastRequestTime = Time.realtimeSinceStartup;
        int currentRequestVersion = ++requestVersion;

        try {
            List<KataGoAiAnalysisTier> tiers = KataGoAiAnalysisConfigService.BuildAiAnalysisTiers(compChessBoard.chessBoardGrid.gridSize);
            for (int i = 0; i < tiers.Count; i++) {
                KataGoAiAnalysisTier tier = tiers[i];
                string requestId = $"duel-ai-recommend-tier{tier.tier}-{DateTime.UtcNow.Ticks}";
                JObject query = KataGoPositionJsonBuilder.BuildDuelAiAnalysisJson(
                    duelScene,
                    requestId,
                    tier.maxVisits,
                    tier.includeOwnership,
                    KataGoAiAnalysisConfigService.IncludePolicy);
                KataGoAnalyzeOptions options = CreateAnalyzeOptions($"duel-ai-recommend-tier{tier.tier}");
                options.priority = tier.priority;

                JObject result = await KataGoBootstrap.AnalyzeAsync(query, options, requestCancellationTokenSource.Token);
                if (!CanApplyResult(currentRequestVersion, requestCancellationTokenSource)) {
                    return;
                }

                bool hasOwnershipRender = KataGoAiAnalysisRenderService.DrawOwnership(compChessBoard, result);
                List<RectGridAiRecommendationMarker> markers = KataGoAiAnalysisRenderService.BuildRecommendationMarkers(
                    scene,
                    result,
                    ResolveCurrentPlayerFlag(),
                    null);
                bool hasRecommendationRender = markers.Count > 0;
                if (hasRecommendationRender) {
                    compChessBoard.chessBoardGrid.DrawAiRecommendationMarkers(markers);
                }

                hasAiAnalysisRender = hasAiAnalysisRender || hasOwnershipRender || hasRecommendationRender;
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) {
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel AI recommendation analysis failed.", ("error", ex.Message));
        }
        finally {
            if (requestVersion == currentRequestVersion) {
                isAiAnalyzing = false;
            }

            if (aiAnalysisCancellationTokenSource == requestCancellationTokenSource) {
                aiAnalysisCancellationTokenSource = null;
            }

            requestCancellationTokenSource.Dispose();
        }
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        ClearAiAnalysisRender();
    }

    private void OnRequestDuelPass(OnRequestDuelPass evt)
    {
        ClearAiAnalysisRender();
    }

    private void OnDuelTakeBackResult(OnDuelTakeBackResult evt)
    {
        if (evt == null || !evt.success) {
            return;
        }

        ClearAiAnalysisRender();
    }

    private void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        if (evt == null || evt.curStateName != DuelStateDefine.STATE_TURN_INPUT) {
            ClearAiAnalysisRender();
        }
    }

    private bool CanAnalyze()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || compDuel.isLanDuel.value || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return false;
        }

        return compDuel.duelFSM.curState != null && compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_TURN_INPUT;
    }

    private bool CanApplyResult(int currentRequestVersion, CancellationTokenSource requestCancellationTokenSource)
    {
        return requestVersion == currentRequestVersion &&
            !requestCancellationTokenSource.IsCancellationRequested &&
            CanAnalyze();
    }

    private PlayerFlag ResolveCurrentPlayerFlag()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return 0;
        }

        if (compDuel.curTurnPlayerGuid.value == compDuel.player1Guid.value) {
            return PlayerFlag.Player1;
        }

        if (compDuel.curTurnPlayerGuid.value == compDuel.player2Guid.value) {
            return PlayerFlag.Player2;
        }

        return 0;
    }

    private KataGoAnalyzeOptions CreateAnalyzeOptions(string requestKind)
    {
        KataGoAnalyzeOptions options = KataGoBootstrap.CreateRetryUntilCanceledAnalyzeOptions(requestKind);
        options.ownerKey = kataGoRequestOwnerKey;
        return options;
    }

    private void CancelAiAnalysisRequest()
    {
        if (aiAnalysisCancellationTokenSource == null) {
            return;
        }

        aiAnalysisCancellationTokenSource.Cancel();
        aiAnalysisCancellationTokenSource = null;
    }
}
