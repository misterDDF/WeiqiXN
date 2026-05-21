using XNClient.ChessBoard;

public readonly struct LanDuelMoveMessage
{
    public readonly int moveId;
    public readonly int boardVersion;
    public readonly PlayerFlag playerFlag;
    public readonly RectCoordinates coords;

    public LanDuelMoveMessage(int moveId, int boardVersion, PlayerFlag playerFlag, RectCoordinates coords)
    {
        this.moveId = moveId;
        this.boardVersion = boardVersion;
        this.playerFlag = playerFlag;
        this.coords = coords;
    }
}
