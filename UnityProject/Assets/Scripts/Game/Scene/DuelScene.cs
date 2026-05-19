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

        AddSystem(new DuelSaveSystem(this));
        AddSystem(new ChessBoardSystem(this));
        AddSystem(new DuelOwnershipSystem(this));
        AddSystem(new DuelSystem(this));

        Global.Instance.uiManager.ShowPage<DuelPage>();
    }
}
