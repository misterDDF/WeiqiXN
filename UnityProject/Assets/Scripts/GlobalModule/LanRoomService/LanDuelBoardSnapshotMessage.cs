using System.Collections.Generic;
using XNClient.ChessBoard;

public readonly struct LanDuelBoardSnapshotStone
{
    public readonly RectCoordinates coords;
    public readonly PlayerFlag playerFlag;

    public LanDuelBoardSnapshotStone(RectCoordinates coords, PlayerFlag playerFlag)
    {
        this.coords = coords;
        this.playerFlag = playerFlag;
    }
}

public readonly struct LanDuelBoardSnapshotMessage
{
    public readonly int boardVersion;
    public readonly int boardSize;
    public readonly PlayerFlag nextTurnPlayerFlag;
    public readonly RectCoordinates latestMoveCoords;
    public readonly PlayerFlag latestMovePlayerFlag;
    public readonly List<LanDuelBoardSnapshotStone> stones;

    public LanDuelBoardSnapshotMessage(
        int boardVersion,
        int boardSize,
        PlayerFlag nextTurnPlayerFlag,
        RectCoordinates latestMoveCoords,
        PlayerFlag latestMovePlayerFlag,
        List<LanDuelBoardSnapshotStone> stones)
    {
        this.boardVersion = boardVersion;
        this.boardSize = boardSize;
        this.nextTurnPlayerFlag = nextTurnPlayerFlag;
        this.latestMoveCoords = latestMoveCoords;
        this.latestMovePlayerFlag = latestMovePlayerFlag;
        this.stones = stones ?? new List<LanDuelBoardSnapshotStone>();
    }
}

public readonly struct LanDuelTimeStateMessage
{
    public readonly PlayerFlag playerFlag;
    public readonly int holdLeftSeconds;
    public readonly int byoyomiLeftCount;
    public readonly int byoyomiLeftSeconds;
    public readonly bool isInByoyomi;
    public readonly int turnLeftTimes;
    public readonly long hostTimestampMilliseconds;

    public LanDuelTimeStateMessage(
        PlayerFlag playerFlag,
        int holdLeftSeconds,
        int byoyomiLeftCount,
        int byoyomiLeftSeconds,
        bool isInByoyomi,
        int turnLeftTimes,
        long hostTimestampMilliseconds)
    {
        this.playerFlag = playerFlag;
        this.holdLeftSeconds = holdLeftSeconds;
        this.byoyomiLeftCount = byoyomiLeftCount;
        this.byoyomiLeftSeconds = byoyomiLeftSeconds;
        this.isInByoyomi = isInByoyomi;
        this.turnLeftTimes = turnLeftTimes;
        this.hostTimestampMilliseconds = hostTimestampMilliseconds;
    }
}
