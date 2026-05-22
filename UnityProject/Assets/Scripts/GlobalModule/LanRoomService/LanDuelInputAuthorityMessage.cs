using XNClient.ChessBoard;

public readonly struct LanDuelInputAuthorityMessage
{
    public readonly PlayerFlag hostInputPlayerFlag;
    public readonly PlayerFlag clientInputPlayerFlag;

    public LanDuelInputAuthorityMessage(PlayerFlag hostInputPlayerFlag, PlayerFlag clientInputPlayerFlag)
    {
        this.hostInputPlayerFlag = hostInputPlayerFlag;
        this.clientInputPlayerFlag = clientInputPlayerFlag;
    }
}
