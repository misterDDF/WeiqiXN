using XNClient.ChessBoard;

public readonly struct LanDuelScoreRequestMessage
{
    public readonly int actionId;
    public readonly int boardVersion;
    public readonly PlayerFlag requesterFlag;

    public LanDuelScoreRequestMessage(int actionId, int boardVersion, PlayerFlag requesterFlag)
    {
        this.actionId = actionId;
        this.boardVersion = boardVersion;
        this.requesterFlag = requesterFlag;
    }
}

public readonly struct LanDuelScoreConfirmMessage
{
    public readonly int actionId;
    public readonly PlayerFlag requesterFlag;
    public readonly PlayerFlag confirmerFlag;
    public readonly bool accepted;

    public LanDuelScoreConfirmMessage(int actionId, PlayerFlag requesterFlag, PlayerFlag confirmerFlag, bool accepted)
    {
        this.actionId = actionId;
        this.requesterFlag = requesterFlag;
        this.confirmerFlag = confirmerFlag;
        this.accepted = accepted;
    }
}

public readonly struct LanDuelScoreResultMessage
{
    public readonly int actionId;
    public readonly float blackScore;
    public readonly float whiteScore;
    public readonly float komi;
    public readonly float margin;
    public readonly PlayerFlag winnerFlag;
    public readonly string scoreSource;

    public LanDuelScoreResultMessage(
        int actionId,
        float blackScore,
        float whiteScore,
        float komi,
        float margin,
        PlayerFlag winnerFlag,
        string scoreSource)
    {
        this.actionId = actionId;
        this.blackScore = blackScore;
        this.whiteScore = whiteScore;
        this.komi = komi;
        this.margin = margin;
        this.winnerFlag = winnerFlag;
        this.scoreSource = scoreSource ?? string.Empty;
    }
}
