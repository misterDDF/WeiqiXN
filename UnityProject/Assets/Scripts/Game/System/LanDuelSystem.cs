public class LanDuelSystem : SystemBase
{
    public override string systemName => GetSystemName<LanDuelSystem>();
    private float nextTimeStateBroadcastTime;
    private const float TimeStateBroadcastIntervalSeconds = 1f;

    public LanDuelSystem(DuelScene scene) : base(scene)
    {
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isLanDuel.value || Global.Instance.lanRoomService == null) {
            return;
        }

        if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
            ProcessSubmittedMoves();
            ProcessSubmittedPasses(compDuel);
            ProcessSubmittedResigns(compDuel);
            ProcessSubmittedScores(compDuel);
            ProcessScoreConfirmResponses(compDuel);
            ProcessSubmittedTakeBacks(compDuel);
            ProcessTakeBackConfirmResponses(compDuel);
            BroadcastCurrentTimeState(compDuel);
        }

        ProcessSessionMessages();
    }

    private void ProcessSubmittedMoves()
    {
        ChessBoardSystem chessBoardSystem = scene.GetSystem<ChessBoardSystem>();
        if (chessBoardSystem == null) {
            return;
        }

        while (Global.Instance.lanRoomService.TryDequeueSubmittedMove(out LanDuelMoveMessage move)) {
            if (chessBoardSystem.TryAcceptLanDuelMove(move, out LanDuelMoveMessage acceptedMove, out DuelMoveRejectReason rejectReason)) {
                Global.Instance.lanRoomService.BroadcastAcceptedMove(acceptedMove);
                if (chessBoardSystem.TryBuildLanBoardSnapshot(acceptedMove, out LanDuelBoardSnapshotMessage snapshot)) {
                    Global.Instance.lanRoomService.BroadcastBoardSnapshot(snapshot);
                }
                scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
            } else {
                Global.Instance.lanRoomService.BroadcastRejectedMove(new LanDuelMoveRejectMessage(
                    move.moveId,
                    move.playerFlag,
                    move.coords,
                    rejectReason));
            }
        }
    }

    private void ProcessSessionMessages()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedMove(out LanDuelMoveMessage move)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelMove(move));
        }

        ChessBoardSystem chessBoardSystem = scene.GetSystem<ChessBoardSystem>();
        while (Global.Instance.lanRoomService.TryDequeueBoardSnapshot(out LanDuelBoardSnapshotMessage snapshot)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            chessBoardSystem?.ApplyLanBoardSnapshot(snapshot);
        }

        while (Global.Instance.lanRoomService.TryDequeueRejectedMove(out LanDuelMoveRejectMessage move)) {
            scene.EmitSystemEvent(new OnDuelMoveRejected(move.playerFlag, move.coords?.Clone(), move.rejectReason));
        }

        while (Global.Instance.lanRoomService.TryDequeueTimeState(out LanDuelTimeStateMessage timeState)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelTimeState(timeState));
        }

        while (Global.Instance.lanRoomService.TryDequeueTimeoutLoser(out PlayerFlag loserFlag)) {
            scene.EmitSystemEvent(new OnApplyLanDuelTimeout(loserFlag));
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedResign(out LanDuelResignMessage resign)) {
            scene.EmitSystemEvent(new OnApplyLanDuelResign(resign.loserFlag));
        }

        while (Global.Instance.lanRoomService.TryDequeueInputAuthority(out LanDuelInputAuthorityMessage authority)) {
            ApplyInputAuthority(compDuel, authority);
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedPass(out LanDuelPassMessage pass)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelPass(pass));
        }

        while (Global.Instance.lanRoomService.TryDequeueScoreConfirmRequest(out LanDuelScoreRequestMessage request)) {
            scene.EmitSystemEvent(new OnLanDuelScoreConfirmRequest(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedScoreRequest(out LanDuelScoreRequestMessage request)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelScoreRequest(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueScoreResult(out LanDuelScoreResultMessage result)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelScoreResult(result));
        }

        while (Global.Instance.lanRoomService.TryDequeueScoreFailure(out _)) {
            scene.EmitSystemEvent(new OnApplyLanDuelScoreFailed());
        }

        while (Global.Instance.lanRoomService.TryDequeueTakeBackConfirmRequest(out LanDuelTakeBackRequestMessage request)) {
            scene.EmitSystemEvent(new OnLanDuelTakeBackConfirmRequest(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedTakeBack(out LanDuelTakeBackRequestMessage request)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelTakeBack(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueRejectedTakeBack(out _)) {
            scene.EmitSystemEvent(new OnLanDuelTakeBackRejected());
        }
    }

    private void ApplyInputAuthority(SceneComponentDuel compDuel, LanDuelInputAuthorityMessage authority)
    {
        if (compDuel == null) {
            return;
        }

        compDuel.localInputPlayerFlag.value = compDuel.lanRole.value == (int)LanRoomRole.Host
            ? (int)authority.hostInputPlayerFlag
            : (int)authority.clientInputPlayerFlag;
    }

    private bool CanAcceptResign(SceneComponentDuel compDuel, PlayerFlag loserFlag)
    {
        if (compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        return curPlayer != null && (PlayerFlag)curPlayer.playerFlag.value == loserFlag;
    }

    private void ProcessSubmittedResigns(SceneComponentDuel compDuel)
    {
        while (Global.Instance.lanRoomService.TryDequeueSubmittedResign(out LanDuelResignMessage resign)) {
            if (!CanAcceptResign(compDuel, resign.loserFlag)) {
                continue;
            }

            Global.Instance.lanRoomService.BroadcastAcceptedResign(resign);
        }
    }

    private void ProcessSubmittedPasses(SceneComponentDuel compDuel)
    {
        DuelSystem duelSystem = scene.GetSystem<DuelSystem>();
        if (duelSystem == null) {
            return;
        }

        while (Global.Instance.lanRoomService.TryDequeueSubmittedPass(out LanDuelPassMessage pass)) {
            if (!duelSystem.CanAcceptLanDuelPass(compDuel, pass)) {
                continue;
            }

            LanDuelPassMessage acceptedPass = duelSystem.AcceptLanDuelPass(pass);
            Global.Instance.lanRoomService.BroadcastAcceptedPass(acceptedPass);
            scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
        }
    }

    private void ProcessSubmittedScores(SceneComponentDuel compDuel)
    {
        DuelSystem duelSystem = scene.GetSystem<DuelSystem>();
        if (duelSystem == null) {
            return;
        }

        while (Global.Instance.lanRoomService.TryDequeueSubmittedScore(out LanDuelScoreRequestMessage request)) {
            if (!duelSystem.CanAcceptLanDuelScore(compDuel, request)) {
                Global.Instance.lanRoomService.BroadcastScoreFailed(request.actionId);
                continue;
            }

            Global.Instance.lanRoomService.BroadcastScoreConfirmRequest(request);
        }
    }

    private void ProcessScoreConfirmResponses(SceneComponentDuel compDuel)
    {
        DuelSystem duelSystem = scene.GetSystem<DuelSystem>();
        if (duelSystem == null) {
            return;
        }

        while (Global.Instance.lanRoomService.TryDequeueScoreConfirmResponse(out LanDuelScoreConfirmMessage response)) {
            LanDuelScoreRequestMessage request = new LanDuelScoreRequestMessage(
                response.actionId,
                compDuel.lanBoardVersion.value,
                response.requesterFlag);
            if (!response.accepted || !IsValidScoreConfirm(compDuel, response)) {
                Global.Instance.lanRoomService.BroadcastScoreFailed(response.actionId);
                continue;
            }

            Global.Instance.lanRoomService.BroadcastAcceptedScoreRequest(request);
            duelSystem.AcceptLanDuelScoreRequest(request);
        }
    }

    private void ProcessSubmittedTakeBacks(SceneComponentDuel compDuel)
    {
        DuelSystem duelSystem = scene.GetSystem<DuelSystem>();
        if (duelSystem == null) {
            return;
        }

        while (Global.Instance.lanRoomService.TryDequeueSubmittedTakeBack(out LanDuelTakeBackRequestMessage request)) {
            if (!duelSystem.CanAcceptLanDuelTakeBack(compDuel, request)) {
                Global.Instance.lanRoomService.BroadcastRejectedTakeBack(request.actionId);
                continue;
            }

            Global.Instance.lanRoomService.BroadcastTakeBackConfirmRequest(request);
        }
    }

    private void ProcessTakeBackConfirmResponses(SceneComponentDuel compDuel)
    {
        DuelSystem duelSystem = scene.GetSystem<DuelSystem>();
        if (duelSystem == null) {
            return;
        }

        while (Global.Instance.lanRoomService.TryDequeueTakeBackConfirmResponse(out LanDuelTakeBackConfirmMessage response)) {
            LanDuelTakeBackRequestMessage request = new LanDuelTakeBackRequestMessage(
                response.actionId,
                compDuel.lanBoardVersion.value,
                response.requesterFlag,
                1);
            if (!response.accepted || !IsValidTakeBackConfirm(response) || !duelSystem.CanAcceptLanDuelTakeBack(compDuel, request)) {
                Global.Instance.lanRoomService.BroadcastRejectedTakeBack(response.actionId);
                continue;
            }

            if (!duelSystem.ApplyLanDuelTakeBack(request)) {
                Global.Instance.lanRoomService.BroadcastRejectedTakeBack(response.actionId);
                continue;
            }

            request = new LanDuelTakeBackRequestMessage(
                request.actionId,
                compDuel.lanBoardVersion.value,
                request.requesterFlag,
                request.removeCount);
            Global.Instance.lanRoomService.BroadcastAcceptedTakeBack(request);
            scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
        }
    }

    private bool IsValidTakeBackConfirm(LanDuelTakeBackConfirmMessage response)
    {
        if (response.confirmerFlag == 0 || response.confirmerFlag == response.requesterFlag) {
            return false;
        }

        return (response.requesterFlag == PlayerFlag.Player1 && response.confirmerFlag == PlayerFlag.Player2) ||
            (response.requesterFlag == PlayerFlag.Player2 && response.confirmerFlag == PlayerFlag.Player1);
    }

    private bool IsValidScoreConfirm(SceneComponentDuel compDuel, LanDuelScoreConfirmMessage response)
    {
        if (compDuel == null || response.confirmerFlag == 0 || response.confirmerFlag == response.requesterFlag) {
            return false;
        }

        return (response.requesterFlag == PlayerFlag.Player1 && response.confirmerFlag == PlayerFlag.Player2) ||
            (response.requesterFlag == PlayerFlag.Player2 && response.confirmerFlag == PlayerFlag.Player1);
    }

    private void BroadcastCurrentTimeState(SceneComponentDuel compDuel)
    {
        if (UnityEngine.Time.unscaledTime < nextTimeStateBroadcastTime) {
            return;
        }

        nextTimeStateBroadcastTime = UnityEngine.Time.unscaledTime + TimeStateBroadcastIntervalSeconds;
        LanDuelTimeStateMessage? timeState = BuildCurrentTimeState(compDuel);
        if (timeState.HasValue) {
            Global.Instance.lanRoomService.BroadcastTimeState(timeState.Value);
        }
    }

    private LanDuelTimeStateMessage? BuildCurrentTimeState(SceneComponentDuel compDuel)
    {
        if (compDuel == null || string.IsNullOrEmpty(compDuel.curTurnPlayerGuid.value)) {
            return null;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        ComponentDuelInfo compDuelInfo = curPlayer?.GetComponent<ComponentDuelInfo>();
        if (curPlayer == null || compDuelInfo == null || compDuelInfo.isInfiniteTime.value) {
            return null;
        }

        return new LanDuelTimeStateMessage(
            (PlayerFlag)curPlayer.playerFlag.value,
            compDuelInfo.holdLeftSeconds.value,
            compDuelInfo.byoyomiLeftCount.value,
            compDuelInfo.byoyomiLeftSeconds.value,
            compDuelInfo.isInByoyomi.value,
            compDuelInfo.turnLeftTimes.value,
            System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
