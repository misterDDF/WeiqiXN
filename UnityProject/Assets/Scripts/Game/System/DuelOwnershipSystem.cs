using System;
using XNClient.Logger;

public class DuelOwnershipSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelOwnershipSystem>();

    private bool isAnalyzing;
    private int requestVersion;
    private bool hasQueuedRequest;
    private System.Collections.Generic.HashSet<int> queuedExcludedStonePosIndexes;

    public DuelOwnershipSystem(SceneBase scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnRequestDuelOwnership>(OnRequestDuelOwnership);
        scene.RegisterSystemEvent<OnRequestClearDuelOwnership>(OnRequestClearDuelOwnership);
        scene.RegisterSystemEvent<OnClearDuelOwnership>(OnClearDuelOwnership);
        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnRequestDuelPass>(OnRequestDuelPass);
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        ClearOwnershipScoreCache();
        ClearOwnershipAndNotify();
    }

    private void OnRequestClearDuelOwnership(OnRequestClearDuelOwnership evt)
    {
        ClearOwnershipAndNotify();
    }

    private void OnClearDuelOwnership(OnClearDuelOwnership evt)
    {
        InvalidateOwnershipRequest();
    }

    private void OnRequestDuelPass(OnRequestDuelPass evt)
    {
        ClearOwnershipScoreCache();
        ClearOwnershipAndNotify();
    }

    private async void OnRequestDuelOwnership(OnRequestDuelOwnership evt)
    {
        if (isAnalyzing) {
            QueueOwnershipRequest(evt);
            return;
        }

        isAnalyzing = true;
        try {
            requestVersion += 1;
            int currentRequestVersion = requestVersion;
            ClearOwnershipOverlay();

            DuelOwnershipQueryResult queryResult = await DuelOwnershipQueryService.QueryOwnershipAsync(
                scene,
                "duel-ownership",
                evt == null || evt.excludedStonePosIndexes == null,
                evt?.excludedStonePosIndexes);
            if (currentRequestVersion != requestVersion) {
                return;
            }

            if (queryResult == null || queryResult.ownership == null) {
                ClearOwnershipAndNotify();
                return;
            }

            SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
            if (compChessBoard?.chessBoardGrid == null) {
                XNLogger.LogError("Duel ownership draw failed, chess board grid is missing.");
                return;
            }

            compChessBoard.chessBoardGrid.DrawOwnership(queryResult.ownership, DuelOwnershipQueryService.OwnershipThreshold);
            scene.EmitSystemEvent(new OnDuelOwnershipResult(
                queryResult.score.blackPoints,
                queryResult.score.whitePoints,
                queryResult.score.komi
            ));
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel ownership analyze failed.", ("err", ex.Message));
            ClearOwnershipAndNotify();
        }
        finally {
            isAnalyzing = false;
            RunQueuedOwnershipRequestIfNeeded();
        }
    }

    private void QueueOwnershipRequest(OnRequestDuelOwnership evt)
    {
        hasQueuedRequest = true;
        queuedExcludedStonePosIndexes = evt?.excludedStonePosIndexes != null
            ? new System.Collections.Generic.HashSet<int>(evt.excludedStonePosIndexes)
            : null;
    }

    private void RunQueuedOwnershipRequestIfNeeded()
    {
        if (!hasQueuedRequest) {
            return;
        }

        System.Collections.Generic.HashSet<int> excludedStonePosIndexes = queuedExcludedStonePosIndexes;
        hasQueuedRequest = false;
        queuedExcludedStonePosIndexes = null;
        OnRequestDuelOwnership(new OnRequestDuelOwnership(excludedStonePosIndexes));
    }

    private void ClearOwnershipAndNotify()
    {
        hasQueuedRequest = false;
        queuedExcludedStonePosIndexes = null;
        InvalidateOwnershipRequest();
        ClearOwnershipOverlay();
        scene.EmitSystemEvent(new OnClearDuelOwnership());
    }

    private void InvalidateOwnershipRequest()
    {
        requestVersion += 1;
    }

    private void ClearOwnershipOverlay()
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        compChessBoard?.chessBoardGrid?.ClearOwnership();
    }

    private void ClearOwnershipScoreCache()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        compDuel?.ClearOwnershipScoreCache();
    }
}
