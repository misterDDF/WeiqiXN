using System;
using XNClient.Logger;

public class DuelOwnershipSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelOwnershipSystem>();

    private bool isAnalyzing;
    private int requestVersion;

    public DuelOwnershipSystem(DuelScene scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnRequestDuelOwnership>(OnRequestDuelOwnership);
        scene.RegisterSystemEvent<OnRequestClearDuelOwnership>(OnRequestClearDuelOwnership);
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

    private void OnRequestDuelPass(OnRequestDuelPass evt)
    {
        ClearOwnershipScoreCache();
        ClearOwnershipAndNotify();
    }

    private async void OnRequestDuelOwnership(OnRequestDuelOwnership evt)
    {
        if (isAnalyzing) {
            XNLogger.LogWarn("Duel ownership analyze skipped, request is already running.");
            return;
        }

        DuelScene duelScene = scene as DuelScene;
        if (duelScene == null) {
            XNLogger.LogError("Duel ownership analyze failed, scene is not DuelScene.");
            return;
        }

        isAnalyzing = true;
        try {
            requestVersion += 1;
            int currentRequestVersion = requestVersion;
            ClearOwnershipOverlay();

            DuelOwnershipQueryResult queryResult = await DuelOwnershipQueryService.QueryOwnershipAsync(duelScene, "duel-ownership", true);
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
        }
    }

    private void ClearOwnershipAndNotify()
    {
        requestVersion += 1;
        ClearOwnershipOverlay();
        scene.EmitSystemEvent(new OnClearDuelOwnership());
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
