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
