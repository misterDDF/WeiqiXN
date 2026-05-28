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
        scene.RegisterSystemEvent<OnSubmitLanDuelScoreResultConfirm>(OnSubmitLanDuelScoreResultConfirm);
        scene.RegisterSystemEvent<OnSubmitDuelTakeBack>(OnSubmitDuelTakeBack);
        scene.RegisterSystemEvent<OnSubmitLanDuelTakeBackConfirm>(OnSubmitLanDuelTakeBackConfirm);
    }

    private void OnSubmitDuelMove(OnSubmitDuelMove evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null || evt.coords == null) {
            return;
        }

        SubmitMove(compDuel, evt.coords, evt.playerFlag);
    }

    private bool SubmitMove(SceneComponentDuel compDuel, RectCoordinates coords, PlayerFlag playerFlag)
    {
        if (compDuel == null || coords == null || playerFlag == 0) {
            return false;
        }

        if (compDuel.isLanDuel.value) {
            return SubmitLanMove(compDuel, coords, playerFlag);
        }

        return SubmitLocalHostMove(coords, playerFlag);
    }

    private bool SubmitLocalHostMove(RectCoordinates coords, PlayerFlag playerFlag)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null ||
            compDuel.isScoring ||
            compDuel.duelFSM == null ||
            !compDuel.duelFSM.isActivated ||
            compDuel.duelFSM.curState == null ||
            compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel?.curTurnPlayerGuid.value);
        if (curPlayer == null || playerFlag != (PlayerFlag)curPlayer.playerFlag.value) {
            return false;
        }

        ChessBoardSystem chessBoardSystem = scene.GetSystem<ChessBoardSystem>();
        return chessBoardSystem != null && chessBoardSystem.TryApplyLocalDuelMove(coords, out _);
    }

    private bool SubmitLanMove(SceneComponentDuel compDuel, RectCoordinates coords, PlayerFlag playerFlag)
    {
        DuelInputAuthorityState inputState = DuelInputAuthority.GetLocalState(scene, compDuel);
        if (!inputState.CanSubmitMove || inputState.localInputPlayerFlag != playerFlag || Global.Instance.lanRoomService == null) {
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

        scene.EmitSystemEvent(new OnConfirmDuelResign(evt.loserGuid, evt.moveCount));
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
            if (!SubmitLanScore(compDuel)) {
                scene.EmitSystemEvent(new OnDuelScoreFailed(false, MessageText.Get("duel_score_request_unavailable")));
            }
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

    private void OnSubmitLanDuelScoreResultConfirm(OnSubmitLanDuelScoreResultConfirm evt)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || evt == null || evt.result.requesterFlag == 0 || Global.Instance.lanRoomService == null) {
            return;
        }

        PlayerFlag confirmerFlag = ResolveLocalPlayerFlagForLanConfirm(compDuel);
        if (confirmerFlag == 0) {
            return;
        }

        Global.Instance.lanRoomService.SubmitScoreResultConfirmResponse(evt.result, confirmerFlag, evt.accepted);
    }

    private PlayerFlag ResolveLocalPlayerFlagForLanConfirm(SceneComponentDuel compDuel)
    {
        LanRoomRole role = (LanRoomRole)compDuel.lanRole.value;
        PlayerFlag hostPlayerFlag = DuelUtils.GetValidPlayerFlag((PlayerFlag)compDuel.lanHostPlayerFlag.value);
        if (role == LanRoomRole.Host) {
            return hostPlayerFlag;
        }
        if (role == LanRoomRole.Client) {
            return hostPlayerFlag.GetOpponentPlayerFlag();
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
            if (!SubmitLanTakeBack(compDuel)) {
                scene.EmitSystemEvent(new OnDuelTakeBackResult(false, MessageText.Get("duel_take_back_unavailable")));
            }
            return;
        }

        scene.EmitSystemEvent(new OnRequestDuelTakeBack(evt.removeCount, evt.moveCount, evt.turnPlayerGuid));
    }

    private bool SubmitLanTakeBack(SceneComponentDuel compDuel)
    {
        if (Global.Instance.lanRoomService == null) {
            return false;
        }

        PlayerFlag requesterFlag = ResolveLocalPlayerFlagForLanConfirm(compDuel);
        if (requesterFlag == 0 || !DuelLanTakeBackRule.TryGetRequiredRemoveCount(scene, compDuel, requesterFlag, out int removeCount)) {
            return false;
        }

        if (DuelMoveHistory.Count(compDuel.kataGoMoves) < removeCount) {
            return false;
        }

        return Global.Instance.lanRoomService.SubmitLocalTakeBack(
            requesterFlag,
            compDuel.lanBoardVersion.value,
            removeCount);
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
