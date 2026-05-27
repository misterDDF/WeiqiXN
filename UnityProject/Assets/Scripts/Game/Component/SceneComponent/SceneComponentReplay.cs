using System.Collections.Generic;
using XNClient.ChessBoard;

public class SceneComponentReplay : SceneComponentBase
{
    public readonly List<ReplayMoveState> replayMoves = new List<ReplayMoveState>();
    public readonly List<ReplayMoveState> replayInitialStones = new List<ReplayMoveState>();
    public readonly List<ReplayMoveState> tryMoves = new List<ReplayMoveState>();
    public readonly Dictionary<int, List<ReplayAiVariationMove>> aiRecommendationVariations = new Dictionary<int, List<ReplayAiVariationMove>>();
    public int replayBoardSize;
    public float replayKomi = KataGoDuelRecordFile.Komi;
    public int replayCursorMoveIndex;
    public int tryBaseCursorMoveIndex;
    public int tryCursorMoveIndex;
    public bool isReplayLoaded;
    public bool isTryMode;
    public bool isAiAnalyzing;
    public bool hasAiAnalysisRender;
    public int aiAnalysisVersion;
    public float lastAiAnalysisRequestTime;
    public string aiAnalysisStatus;
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
    public DuelMoveResult moveResult;
    public SavableObjectDict<ChessInfo> previousLastChessInfoDict;
}

public class ReplayAiVariationMove
{
    public PlayerFlag playerFlag;
    public RectCoordinates coords;
}
