public class DuelScene : SceneBase
{
    public SceneComponentChessBoard compChessBoard;
    public SceneComponentDuel compDuel;

    public DuelScene(SceneDataType configData, SceneCreateParams sceneCreateParams) : base(configData, sceneCreateParams)
    {
        compChessBoard = new SceneComponentChessBoard(this);
        AddComponent(compChessBoard);
        compDuel = new SceneComponentDuel(this);
        AddComponent(compDuel);
    }

    public override void OnSceneLoaded()
    {
        base.OnSceneLoaded();
        Global.RequestKeepAwake(Global.KeepAwakeReason.Duel);

        foreach (var rootObj in unityScene.GetRootGameObjects()) {
            DuelSceneFixedRef fixedRef = rootObj.GetComponent<DuelSceneFixedRef>();
            if (fixedRef != null) {
                var compChessBoard = GetComponent<SceneComponentChessBoard>();
                if (compChessBoard != null) {
                    compChessBoard.chessBoardGrid = fixedRef.chessBoardGrid;
                    compChessBoard.duelVCam = fixedRef.duelVCam;
                }
                break;
            }
        }

        AddSystem(new ChessBoardSystem(this));
        AddSystem(new DuelGameEndCameraSystem(this));
        AddSystem(new DuelOwnershipSystem(this));
        AddSystem(new DuelAuthoritySystem(this));
        AddSystem(new DuelInputAuthoritySystem(this));
        AddSystem(new DuelAiRecommendationSystem(this));
        AddSystem(new DuelAiSystem(this));
        AddSystem(new DuelSystem(this));
        AddSystem(new LanDuelSystem(this));
        AddSystem(new DuelReplayArchiveSystem(this));

        Global.Instance.uiManager.ShowPage<DuelPage>();
    }

    public override void OnSceneExit()
    {
        Global.ReleaseKeepAwake(Global.KeepAwakeReason.Duel);
        base.OnSceneExit();
    }
}
