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
        scene.RegisterSystemEvent<OnSubmitDuelPass>(OnSubmitDuelPass);
        scene.RegisterSystemEvent<OnSubmitDuelScore>(OnSubmitDuelScore);
        scene.RegisterSystemEvent<OnSubmitLanDuelScoreConfirm>(OnSubmitLanDuelScoreConfirm);
        scene.RegisterSystemEvent<OnSubmitDuelTakeBack>(OnSubmitDuelTakeBack);
        scene.RegisterSystemEvent<OnSubmitLanDuelTakeBackConfirm>(OnSubmitLanDuelTakeBackConfirm);
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

    private void OnSubmitDuelPass(OnSubmitDuelPass evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null) {
            return;
        }

        if (compDuel.isLanDuel.value) {
            SubmitLanPass(compDuel);
            return;
        }

        scene.EmitSystemEvent(new OnRequestDuelPass());
    }

    private bool SubmitLanPass(SceneComponentDuel compDuel)
    {
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(scene, compDuel);
        if (!inputState.CanSubmitMove || Global.Instance.lanRoomService == null) {
            return false;
        }

        return Global.Instance.lanRoomService.SubmitLocalPass(
            inputState.localInputPlayerFlag,
            compDuel.lanBoardVersion.value);
    }

    private void OnSubmitDuelScore(OnSubmitDuelScore evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null) {
            return;
        }

        if (compDuel.isLanDuel.value) {
            SubmitLanScore(compDuel);
            return;
        }

        scene.EmitSystemEvent(new OnRequestDuelScore());
    }

    private bool SubmitLanScore(SceneComponentDuel compDuel)
    {
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(scene, compDuel);
        if (!inputState.CanSubmitMove || Global.Instance.lanRoomService == null) {
            return false;
        }

        return Global.Instance.lanRoomService.SubmitLocalScore(
            inputState.localInputPlayerFlag,
            compDuel.lanBoardVersion.value);
    }

    private void OnSubmitLanDuelScoreConfirm(OnSubmitLanDuelScoreConfirm evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null || evt.request.requesterFlag == 0 || Global.Instance.lanRoomService == null) {
            return;
        }

        PlayerFlag confirmerFlag = ResolveLocalPlayerFlagForLanConfirm(compDuel);
        if (confirmerFlag == 0 || confirmerFlag == evt.request.requesterFlag) {
            return;
        }

        Global.Instance.lanRoomService.SubmitScoreConfirmResponse(evt.request, confirmerFlag, evt.accepted);
    }

    private PlayerFlag ResolveLocalPlayerFlagForLanConfirm(SceneComponentDuel compDuel)
    {
        LanRoomRole role = (LanRoomRole)compDuel.lanRole.value;
        if (role == LanRoomRole.Host) {
            return PlayerFlag.Player1;
        }
        if (role == LanRoomRole.Client) {
            return PlayerFlag.Player2;
        }

        return 0;
    }

    private void OnSubmitDuelTakeBack(OnSubmitDuelTakeBack evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null) {
            return;
        }

        if (compDuel.isLanDuel.value) {
            SubmitLanTakeBack(compDuel);
            return;
        }

        scene.EmitSystemEvent(new OnRequestDuelTakeBack());
    }

    private bool SubmitLanTakeBack(SceneComponentDuel compDuel)
    {
        if (Global.Instance.lanRoomService == null) {
            return false;
        }

        PlayerFlag requesterFlag = ResolveLocalPlayerFlagForLanConfirm(compDuel);
        if (requesterFlag == 0) {
            return false;
        }

        return Global.Instance.lanRoomService.SubmitLocalTakeBack(
            requesterFlag,
            compDuel.lanBoardVersion.value,
            1);
    }

    private void OnSubmitLanDuelTakeBackConfirm(OnSubmitLanDuelTakeBackConfirm evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null || evt.request.requesterFlag == 0 || Global.Instance.lanRoomService == null) {
            return;
        }

        PlayerFlag confirmerFlag = ResolveLocalPlayerFlagForLanConfirm(compDuel);
        if (confirmerFlag == 0 || confirmerFlag == evt.request.requesterFlag) {
            return;
        }

        Global.Instance.lanRoomService.SubmitTakeBackConfirmResponse(evt.request, confirmerFlag, evt.accepted);
    }
}
