using System.Collections.Generic;

public class LanDuelSystem : SystemBase
{
    public override string systemName => GetSystemName<LanDuelSystem>();
    private float nextTimeStateBroadcastTime;
    private const float TimeStateBroadcastIntervalSeconds = 1f;
    private readonly Dictionary<int, LanDuelScoreRequestMessage> pendingScoreRequests = new Dictionary<int, LanDuelScoreRequestMessage>();
    private readonly Dictionary<int, LanDuelScoreResultMessage> pendingScoreResults = new Dictionary<int, LanDuelScoreResultMessage>();
    private readonly Dictionary<int, HashSet<PlayerFlag>> pendingScoreResultAcceptances = new Dictionary<int, HashSet<PlayerFlag>>();
    private readonly Dictionary<int, LanDuelTakeBackRequestMessage> pendingTakeBackRequests = new Dictionary<int, LanDuelTakeBackRequestMessage>();

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

        ProcessReconnectRestored(compDuel);

        if (Global.Instance.lanRoomService.IsReconnectWaiting) {
            return;
        }

        if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
            ProcessSubmittedMoves();
            ProcessSubmittedPasses(compDuel);
            ProcessSubmittedResigns(compDuel);
            ProcessSubmittedScores(compDuel);
            ProcessScoreConfirmResponses(compDuel);
            ProcessScoreResultConfirmResponses(compDuel);
            ProcessSubmittedTakeBacks(compDuel);
            ProcessTakeBackConfirmResponses(compDuel);
            BroadcastCurrentTimeState(compDuel);
        }

        ProcessSessionMessages();
    }

    private void ProcessReconnectRestored(SceneComponentDuel compDuel)
    {
        if (!Global.Instance.lanRoomService.TryConsumeReconnectRestoredForDuel()) {
            return;
        }

        ProcessSessionMessages();
        if ((LanRoomRole)compDuel.lanRole.value != LanRoomRole.Host) {
            return;
        }

        ChessBoardSystem chessBoardSystem = scene.GetSystem<ChessBoardSystem>();
        if (chessBoardSystem != null && chessBoardSystem.TryBuildLanBoardSnapshot(out LanDuelBoardSnapshotMessage snapshot)) {
            Global.Instance.lanRoomService.BroadcastBoardSnapshot(snapshot);
        }

        scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
        LanDuelTimeStateMessage? timeState = BuildCurrentTimeState(compDuel);
        if (timeState.HasValue) {
            Global.Instance.lanRoomService.BroadcastTimeState(timeState.Value);
        }
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

        while (Global.Instance.lanRoomService.TryDequeuePlayerProfile(out LanPlayerProfileMessage profile)) {
            scene.EmitSystemEvent(new OnLanPlayerProfileChanged(ResolvePlayerFlagByRole(compDuel, profile.role), profile.profile));
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
            if (request.requesterFlag == ResolveLocalPlayerFlag(compDuel)) {
                continue;
            }

            scene.EmitSystemEvent(new OnLanDuelScoreConfirmRequest(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedScoreRequest(out LanDuelScoreRequestMessage request)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelScoreRequest(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueScoreResult(out LanDuelScoreResultMessage result)) {
            pendingScoreResults[result.actionId] = result;
            scene.EmitSystemEvent(new OnLanDuelScoreResultConfirmRequest(result));
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedScoreResult(out LanDuelScoreResultMessage result)) {
            ClearPendingScoreResult(result.actionId);
            scene.EmitSystemEvent(new OnApplyLanDuelScoreResult(result));
        }

        while (Global.Instance.lanRoomService.TryDequeueScoreFailure(out LanDuelScoreFailedMessage failure)) {
            PlayerFlag localPlayerFlag = ResolveLocalPlayerFlag(compDuel);
            if (failure.reason != LanDuelScoreFailureReason.ResultRejected &&
                failure.reason != LanDuelScoreFailureReason.CalculationFailed &&
                failure.requesterFlag != 0 &&
                failure.requesterFlag != localPlayerFlag) {
                continue;
            }

            ClearPendingScoreResult(failure.actionId);
            scene.EmitSystemEvent(new OnApplyLanDuelScoreFailed(failure));
        }

        while (Global.Instance.lanRoomService.TryDequeueTakeBackConfirmRequest(out LanDuelTakeBackRequestMessage request)) {
            if (request.requesterFlag == ResolveLocalPlayerFlag(compDuel)) {
                continue;
            }

            scene.EmitSystemEvent(new OnLanDuelTakeBackConfirmRequest(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueAcceptedTakeBack(out LanDuelTakeBackRequestMessage request)) {
            if ((LanRoomRole)compDuel.lanRole.value == LanRoomRole.Host) {
                continue;
            }
            scene.EmitSystemEvent(new OnApplyLanDuelTakeBack(request));
        }

        while (Global.Instance.lanRoomService.TryDequeueRejectedTakeBack(out LanDuelTakeBackRejectedMessage rejected)) {
            PlayerFlag localPlayerFlag = ResolveLocalPlayerFlag(compDuel);
            if (rejected.requesterFlag != 0 && rejected.requesterFlag != localPlayerFlag) {
                continue;
            }

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
                ClearPendingScoreResult(request.actionId);
                Global.Instance.lanRoomService.BroadcastScoreFailed(
                    request.actionId,
                    request.requesterFlag,
                    LanDuelScoreFailureReason.InvalidRequest);
                continue;
            }

            pendingScoreRequests[request.actionId] = request;
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
            if (!pendingScoreRequests.TryGetValue(response.actionId, out LanDuelScoreRequestMessage request)) {
                Global.Instance.lanRoomService.BroadcastScoreFailed(
                    response.actionId,
                    response.requesterFlag,
                    LanDuelScoreFailureReason.InvalidRequest);
                continue;
            }

            if (!response.accepted ||
                response.requesterFlag != request.requesterFlag ||
                !IsValidScoreConfirm(compDuel, response)) {
                ClearPendingScoreResult(response.actionId);
                Global.Instance.lanRoomService.BroadcastScoreFailed(
                    response.actionId,
                    request.requesterFlag,
                    LanDuelScoreFailureReason.RequestRejected);
                continue;
            }

            Global.Instance.lanRoomService.BroadcastAcceptedScoreRequest(request);
            duelSystem.AcceptLanDuelScoreRequest(request);
        }
    }

    private void ProcessScoreResultConfirmResponses(SceneComponentDuel compDuel)
    {
        while (Global.Instance.lanRoomService.TryDequeueScoreResultConfirmResponse(out LanDuelScoreResultConfirmMessage response)) {
            if (!pendingScoreRequests.TryGetValue(response.actionId, out LanDuelScoreRequestMessage request) ||
                !pendingScoreResults.TryGetValue(response.actionId, out LanDuelScoreResultMessage result) ||
                response.requesterFlag != request.requesterFlag ||
                !IsValidScoreResultConfirm(response)) {
                Global.Instance.lanRoomService.BroadcastScoreFailed(
                    response.actionId,
                    response.requesterFlag,
                    LanDuelScoreFailureReason.InvalidRequest);
                continue;
            }

            if (!response.accepted) {
                ClearPendingScoreResult(response.actionId);
                Global.Instance.lanRoomService.BroadcastScoreFailed(
                    response.actionId,
                    request.requesterFlag,
                    LanDuelScoreFailureReason.ResultRejected);
                scene.GetSystem<DuelInputAuthoritySystem>()?.RefreshLocalInputAuthority();
                continue;
            }

            if (!pendingScoreResultAcceptances.TryGetValue(response.actionId, out HashSet<PlayerFlag> acceptedFlags)) {
                acceptedFlags = new HashSet<PlayerFlag>();
                pendingScoreResultAcceptances[response.actionId] = acceptedFlags;
            }

            acceptedFlags.Add(response.confirmerFlag);
            if (acceptedFlags.Contains(PlayerFlag.Player1) && acceptedFlags.Contains(PlayerFlag.Player2)) {
                ClearPendingScoreResult(response.actionId);
                Global.Instance.lanRoomService.BroadcastAcceptedScoreResult(result);
            }
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
                pendingTakeBackRequests.Remove(request.actionId);
                Global.Instance.lanRoomService.BroadcastRejectedTakeBack(request.actionId, request.requesterFlag);
                continue;
            }

            pendingTakeBackRequests[request.actionId] = request;
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
            if (!pendingTakeBackRequests.TryGetValue(response.actionId, out LanDuelTakeBackRequestMessage request)) {
                Global.Instance.lanRoomService.BroadcastRejectedTakeBack(response.actionId, response.requesterFlag);
                continue;
            }

            pendingTakeBackRequests.Remove(response.actionId);
            if (!response.accepted ||
                response.requesterFlag != request.requesterFlag ||
                !IsValidTakeBackConfirm(response) ||
                !duelSystem.CanAcceptLanDuelTakeBack(compDuel, request)) {
                Global.Instance.lanRoomService.BroadcastRejectedTakeBack(response.actionId, request.requesterFlag);
                continue;
            }

            if (!duelSystem.ApplyLanDuelTakeBack(request)) {
                Global.Instance.lanRoomService.BroadcastRejectedTakeBack(response.actionId, request.requesterFlag);
                continue;
            }

            Global.Instance.lanRoomService.BroadcastAcceptedTakeBack(request);
            ChessBoardSystem chessBoardSystem = scene.GetSystem<ChessBoardSystem>();
            if (chessBoardSystem != null && chessBoardSystem.TryBuildLanBoardSnapshot(out LanDuelBoardSnapshotMessage snapshot)) {
                Global.Instance.lanRoomService.BroadcastBoardSnapshot(snapshot);
            }
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

    private PlayerFlag ResolveLocalPlayerFlag(SceneComponentDuel compDuel)
    {
        if (compDuel == null) {
            return 0;
        }

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

    private PlayerFlag ResolvePlayerFlagByRole(SceneComponentDuel compDuel, LanRoomRole role)
    {
        if (compDuel == null) {
            return 0;
        }

        PlayerFlag hostPlayerFlag = DuelUtils.GetValidPlayerFlag((PlayerFlag)compDuel.lanHostPlayerFlag.value);
        if (role == LanRoomRole.Host) {
            return hostPlayerFlag;
        }

        if (role == LanRoomRole.Client) {
            return hostPlayerFlag.GetOpponentPlayerFlag();
        }

        return 0;
    }

    private bool IsValidScoreConfirm(SceneComponentDuel compDuel, LanDuelScoreConfirmMessage response)
    {
        if (compDuel == null || response.confirmerFlag == 0 || response.confirmerFlag == response.requesterFlag) {
            return false;
        }

        return (response.requesterFlag == PlayerFlag.Player1 && response.confirmerFlag == PlayerFlag.Player2) ||
            (response.requesterFlag == PlayerFlag.Player2 && response.confirmerFlag == PlayerFlag.Player1);
    }

    private bool IsValidScoreResultConfirm(LanDuelScoreResultConfirmMessage response)
    {
        return response.confirmerFlag == PlayerFlag.Player1 || response.confirmerFlag == PlayerFlag.Player2;
    }

    private void ClearPendingScoreResult(int actionId)
    {
        pendingScoreRequests.Remove(actionId);
        pendingScoreResults.Remove(actionId);
        pendingScoreResultAcceptances.Remove(actionId);
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
