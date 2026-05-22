using XNClient.ChessBoard;

public class DuelAuthoritySystem : SystemBase
{
    public override string systemName => GetSystemName<DuelAuthoritySystem>();

    public DuelAuthoritySystem(DuelScene scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnSubmitDuelMove>(OnSubmitDuelMove);
        scene.RegisterSystemEvent<OnSubmitDuelResign>(OnSubmitDuelResign);
    }

    private void OnSubmitDuelMove(OnSubmitDuelMove evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null || evt.coords == null) {
            return;
        }

        SubmitMove(compDuel, evt.coords);
    }

    private bool SubmitMove(SceneComponentDuel compDuel, RectCoordinates coords)
    {
        if (compDuel == null || coords == null) {
            return false;
        }

        if (compDuel.isLanDuel.value) {
            return SubmitLanMove(compDuel, coords);
        }

        return SubmitLocalHostMove(coords);
    }

    private bool SubmitLocalHostMove(RectCoordinates coords)
    {
        ChessBoardSystem chessBoardSystem = scene.GetSystem<ChessBoardSystem>();
        return chessBoardSystem != null && chessBoardSystem.TryApplyLocalDuelMove(coords, out _);
    }

    private bool SubmitLanMove(SceneComponentDuel compDuel, RectCoordinates coords)
    {
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(scene, compDuel);
        if (!inputState.CanSubmitMove || Global.Instance.lanRoomService == null) {
            return false;
        }

        return Global.Instance.lanRoomService.SubmitLocalMove(
            inputState.localInputPlayerFlag,
            coords,
            compDuel.lanBoardVersion.value);
    }

    private void OnSubmitDuelResign(OnSubmitDuelResign evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null) {
            return;
        }

        if (compDuel.isLanDuel.value) {
            SubmitLanResign(compDuel);
            return;
        }

        scene.EmitSystemEvent(new OnConfirmDuelResign());
    }

    private bool SubmitLanResign(SceneComponentDuel compDuel)
    {
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(scene, compDuel);
        if (!inputState.CanSubmitMove || Global.Instance.lanRoomService == null) {
            return false;
        }

        return Global.Instance.lanRoomService.SubmitLocalResign(inputState.localInputPlayerFlag);
    }
}
