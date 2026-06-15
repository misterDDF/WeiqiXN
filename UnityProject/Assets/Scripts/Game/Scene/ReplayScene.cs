using UnityEngine;
using XNClient.Logger;

public class ReplayScene : SceneBase
{
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

    public override void OnSceneLoaded()
    {
        Global.ReleaseKeepAwake(Global.KeepAwakeReason.Startup);
        Global.RequestKeepAwake(Global.KeepAwakeReason.Duel);

        BindFixedRefs();

        AddSystem(new ReplaySystem(this));
        AddSystem(new ChessBoardSystem(this));
        ReplaySystem replaySystem = GetSystem<ReplaySystem>();
        replaySystem?.RestoreDefaultBoard();

        if (!isMainScene) {
            return;
        }

        Global.Instance.uiManager.TryClosePage<LoadingPage>();
        Global.Instance.uiManager.ShowPage<ReplayPage>();
        StartReplayChartBackgroundBuild(replaySystem);
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

    private void StartReplayChartBackgroundBuild(ReplaySystem replaySystem)
    {
        if (replaySystem == null || !replaySystem.IsReplayLoaded || replaySystem.IsChartHidden) {
            return;
        }

        try {
            replaySystem.StartInitialChartBuild();
        }
        catch (System.Exception ex) {
            XNLogger.LogError("Replay scene chart background start failed.", ("error", ex.Message));
        }
    }
}
