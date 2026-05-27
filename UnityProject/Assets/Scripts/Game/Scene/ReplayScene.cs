using System.Threading.Tasks;
using UnityEngine;
using XNClient.Logger;

public class ReplayScene : SceneBase
{
    private const float UnitySceneProgressEnd = 0.6f;

    public SceneComponentChessBoard compChessBoard;
    public SceneComponentDuel compDuel;
    public SceneComponentReplay compReplay;

    public ReplayScene(SceneDataType configData, SceneCreateParams sceneCreateParams) : base(configData, sceneCreateParams)
    {
        compChessBoard = new SceneComponentChessBoard(this);
        AddComponent(compChessBoard);
        compDuel = new SceneComponentDuel(this);
        AddComponent(compDuel);
        compReplay = new SceneComponentReplay(this);
        AddComponent(compReplay);
    }

    protected override void ConfigureLoadingProgressBeforeSceneLoad()
    {
        LoadingPage.SetProgressRange(0f, UnitySceneProgressEnd);
    }

    public override async void OnSceneLoaded()
    {
        Global.ReleaseKeepAwake(Global.KeepAwakeReason.Startup);
        Global.RequestKeepAwake(Global.KeepAwakeReason.Duel);

        BindFixedRefs();

        AddSystem(new ReplaySystem(this));
        AddSystem(new ChessBoardSystem(this));
        ReplaySystem replaySystem = GetSystem<ReplaySystem>();
        replaySystem?.RestoreDefaultBoard();
        await BuildReplayChartDuringLoading(replaySystem);

        if (!isMainScene) {
            return;
        }

        Global.Instance.uiManager.TryClosePage<LoadingPage>();
        Global.Instance.uiManager.ShowPage<ReplayPage>();
    }

    public override void OnSceneExit()
    {
        Global.Instance.uiManager.TryClosePage<ReplayPage>();
        Global.ReleaseKeepAwake(Global.KeepAwakeReason.Duel);
        base.OnSceneExit();
    }

    private void BindFixedRefs()
    {
        foreach (var rootObj in unityScene.GetRootGameObjects()) {
            DuelSceneFixedRef fixedRef = rootObj.GetComponent<DuelSceneFixedRef>();
            if (fixedRef == null) {
                continue;
            }

            compChessBoard.chessBoardGrid = fixedRef.chessBoardGrid;
            compChessBoard.duelVCam = fixedRef.duelVCam;
            break;
        }
    }

    private async Task BuildReplayChartDuringLoading(ReplaySystem replaySystem)
    {
        if (replaySystem == null || !replaySystem.IsReplayLoaded) {
            return;
        }

        try {
            if (!LoadingPage.hasActivePage) {
                Global.Instance.uiManager.ShowPage<LoadingPage>();
            }

            LoadingPage.SetProgressRange(UnitySceneProgressEnd, 1f);
            await replaySystem.BuildChartDuringLoadingAsync();
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Replay scene chart loading failed.", ("error", ex.Message));
        }
        finally {
            LoadingPage.ResetProgressRange();
            replaySystem.StartChartBackgroundBuild();
        }
    }
}
