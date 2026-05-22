using XNClient.ChessBoard;

public readonly struct LanDuelTakeBackRequestMessage
{
    public readonly int actionId;
    public readonly int boardVersion;
    public readonly PlayerFlag requesterFlag;
    public readonly int removeCount;

    public LanDuelTakeBackRequestMessage(int actionId, int boardVersion, PlayerFlag requesterFlag, int removeCount)
    {
        this.actionId = actionId;
        this.boardVersion = boardVersion;
        this.requesterFlag = requesterFlag;
        this.removeCount = removeCount;
    }
}

public readonly struct LanDuelTakeBackConfirmMessage
{
    public readonly int actionId;
    public readonly PlayerFlag requesterFlag;
    public readonly PlayerFlag confirmerFlag;
    public readonly bool accepted;

    public LanDuelTakeBackConfirmMessage(int actionId, PlayerFlag requesterFlag, PlayerFlag confirmerFlag, bool accepted)
    {
        this.actionId = actionId;
        this.requesterFlag = requesterFlag;
        this.confirmerFlag = confirmerFlag;
        this.accepted = accepted;
    }
}

public readonly struct LanDuelTakeBackRejectedMessage
{
    public readonly int actionId;
    public readonly PlayerFlag requesterFlag;

    public LanDuelTakeBackRejectedMessage(int actionId, PlayerFlag requesterFlag)
    {
        this.actionId = actionId;
        this.requesterFlag = requesterFlag;
    }
}
