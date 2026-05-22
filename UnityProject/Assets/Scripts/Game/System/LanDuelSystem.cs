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
