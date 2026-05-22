using XNClient.ChessBoard;

public readonly struct LanDuelPassMessage
{
    public readonly int actionId;
    public readonly int boardVersion;
    public readonly PlayerFlag playerFlag;
    public readonly int consecutivePassCount;

    public LanDuelPassMessage(int actionId, int boardVersion, PlayerFlag playerFlag, int consecutivePassCount = 0)
    {
        this.actionId = actionId;
        this.boardVersion = boardVersion;
        this.playerFlag = playerFlag;
        this.consecutivePassCount = consecutivePassCount;
    }
}
