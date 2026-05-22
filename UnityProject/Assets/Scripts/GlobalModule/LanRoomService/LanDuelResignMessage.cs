using XNClient.ChessBoard;

public readonly struct LanDuelResignMessage
{
    public readonly int actionId;
    public readonly PlayerFlag loserFlag;

    public LanDuelResignMessage(int actionId, PlayerFlag loserFlag)
    {
        this.actionId = actionId;
        this.loserFlag = loserFlag;
    }
}
