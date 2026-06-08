public class OgsDuelScene : SceneBase
{
    public SceneComponentChessBoard compChessBoard;
    public SceneComponentDuel compDuel;
    public SceneComponentOgsDuel compOgsDuel;

    public OgsDuelScene(SceneDataType configData, SceneCreateParams sceneCreateParams) : base(configData, sceneCreateParams)
    {
        compChessBoard = new SceneComponentChessBoard(this);
        AddComponent(compChessBoard);
        compDuel = new SceneComponentDuel(this);
        AddComponent(compDuel);
        compOgsDuel = new SceneComponentOgsDuel(this);
        AddComponent(compOgsDuel);
    }

    public override void OnSceneLoaded()
    {
        base.OnSceneLoaded();
        Global.RequestKeepAwake(Global.KeepAwakeReason.Duel);
        GameAudio.PlayDuelBgm();

        BindFixedRefs();

        AddSystem(new ChessBoardSystem(this));
        AddSystem(new DuelAudioSystem(this));
        AddSystem(new DuelOwnershipSystem(this));
        AddSystem(new DuelGameEndCameraSystem(this));
        AddSystem(new OgsDuelSystem(this));
        AddSystem(new DuelReplayArchiveSystem(this));

        Global.Instance.uiManager.ShowPage<DuelPage>();
    }

    public override void OnSceneExit()
    {
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
