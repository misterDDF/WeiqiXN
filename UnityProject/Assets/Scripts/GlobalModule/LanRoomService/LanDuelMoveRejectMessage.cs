using XNClient.ChessBoard;

public readonly struct LanDuelMoveRejectMessage
{
    public readonly int moveId;
    public readonly PlayerFlag playerFlag;
    public readonly RectCoordinates coords;
    public readonly DuelMoveRejectReason rejectReason;

    public LanDuelMoveRejectMessage(int moveId, PlayerFlag playerFlag, RectCoordinates coords, DuelMoveRejectReason rejectReason)
    {
        this.moveId = moveId;
        this.playerFlag = playerFlag;
        this.coords = coords;
        this.rejectReason = rejectReason;
    }
}
