using Cinemachine;
using XNClient.ChessBoard;

public class ChessInfo : SavableObj
{
    public SavableField<string> chessGuid = SavableFieldFactory.CreateStringField("");
    public SavableField<int> chessFlag = SavableFieldFactory.CreateIntField(0);
}

public class SceneComponentChessBoard : SceneComponentBase
{
    public SavableField<string> boardCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    [SkipSavableCheck]
    public SavableObjectDict<ChessInfo> chessInfoDict = new SavableObjectDict<ChessInfo>();
    [SkipSavableCheck]
    public SavableObjectDict<ChessInfo> lastChessInfoDict = new SavableObjectDict<ChessInfo>();

    public RectGrid chessBoardGrid;
    public CinemachineVirtualCamera duelVCam;
    [SkipSavableCheck]
    public ChessStoneViewCache stoneViewCache;

    public SceneComponentChessBoard(DuelScene scene) : base(scene)
    {

    }

    public ChessStoneViewCache GetStoneViewCache()
    {
        if (stoneViewCache == null) {
            stoneViewCache = new ChessStoneViewCache(scene, this);
        }

        return stoneViewCache;
    }

    public override void OnDestroy()
    {
        stoneViewCache?.Destroy();
        stoneViewCache = null;
        base.OnDestroy();
    }

    public int GetGridMaxSize()
    {
        if (chessBoardGrid != null) {
            return chessBoardGrid.gridSize * chessBoardGrid.gridSize;
        }

        return 0;
    }

    public int GetPosIndexByCoords(RectCoordinates coords)
    {
        if (coords == null || chessBoardGrid == null) {
            return -1;
        }

        int gridSize = chessBoardGrid.gridSize;
        if (coords.x < 0 || coords.x >= gridSize || coords.z < 0 || coords.z >= gridSize) {
            return -1;
        }

        int posIndex = coords.z * gridSize + coords.x;
        if (posIndex < 0 || posIndex >= gridSize * gridSize) {
            return -1;
        }

        return posIndex;
    }

    public RectCoordinates GetCoordsByPosIndex(int posIndex)
    {
        RectCoordinates coords = new RectCoordinates(-1, -1);

        int gridSize = chessBoardGrid.gridSize;
        coords.z = posIndex / gridSize;
        coords.x = posIndex - gridSize * coords.z;

        return coords;
    }

    // 双向检查落子前后局面有没有发生变化
    public bool CheckChessFlagChanged()
    {
        if (chessInfoDict == null || lastChessInfoDict == null) {
            return false;
        }

        foreach (var kvp in chessInfoDict) {
            string posKey = kvp.Key;
            ChessInfo chessInfo = kvp.Value;
            if (chessInfo == null) {
                continue;
            }

            if (!lastChessInfoDict.TryGetValue(posKey, out var lastChessInfo)) {
                if (chessInfo.chessFlag.value != 0) {
                    return true;
                }
                continue;
            }

            if (lastChessInfo == null || lastChessInfo.chessFlag.value != chessInfo.chessFlag.value) {
                return true;
            }
        }

        foreach (var kvp in lastChessInfoDict) {
            string posKey = kvp.Key;
            ChessInfo lastChessInfo = kvp.Value;
            if (lastChessInfo == null) {
                continue;
            }

            if (!chessInfoDict.TryGetValue(posKey, out var chessInfo)) {
                if (lastChessInfo.chessFlag.value != 0) {
                    return true;
                }
                continue;
            }

            if (chessInfo == null || chessInfo.chessFlag.value != lastChessInfo.chessFlag.value) {
                return true;
            }
        }

        return false;
    }

    // 深拷贝创建缓存infoDict
    public SavableObjectDict<ChessInfo> CreateCacheChessInfoDict()
    {
        SavableObjectDict<ChessInfo> cacheChessInfoDict = new SavableObjectDict<ChessInfo>();
        foreach (var kvp in chessInfoDict) {
            ChessInfo chessInfo = new ChessInfo();
            chessInfo.chessGuid.value = kvp.Value.chessGuid.value;
            chessInfo.chessFlag.value = kvp.Value.chessFlag.value;
            cacheChessInfoDict.SetValue(kvp.Key, chessInfo);
        }
        return cacheChessInfoDict;
    }
}
