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
        base.OnSceneLoaded();
        Global.RequestKeepAwake(Global.KeepAwakeReason.Duel);

        BindFixedRefs();

        AddSystem(new ReplaySystem(this));
        AddSystem(new ChessBoardSystem(this));
        GetSystem<ReplaySystem>()?.RestoreDefaultBoard();

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
}
