public class LanDuelSystem : SystemBase
{
    public override string systemName => GetSystemName<LanDuelSystem>();

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
    }
}
