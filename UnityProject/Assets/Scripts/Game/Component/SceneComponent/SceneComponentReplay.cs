using System.Collections.Generic;
using XNClient.ChessBoard;

public class SceneComponentReplay : SceneComponentBase
{
    public readonly List<ReplayMoveState> replayMoves = new List<ReplayMoveState>();
    public readonly List<ReplayMoveState> replayInitialStones = new List<ReplayMoveState>();
    public readonly List<ReplayMoveState> tryMoves = new List<ReplayMoveState>();
    public int replayBoardSize;
    public int replayCursorMoveIndex;
    public int tryBaseCursorMoveIndex;
    public bool isReplayLoaded;
    public bool isTryMode;
    public string replayStatus;

    public SceneComponentReplay(SceneBase scene) : base(scene)
    {
    }
}

public class ReplayMoveState
{
    public PlayerFlag playerFlag;
    public RectCoordinates coords;
    public bool isPass;
    public string pointText;
}
