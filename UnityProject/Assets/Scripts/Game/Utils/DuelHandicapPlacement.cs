using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public static class DuelHandicapPlacement
{
    private const float DefaultEvenGameKomi = 7.5f;
    private const string SenCfgIdSuffix = "_sen";

    public static string GetDefaultCfgId(string boardCfgId)
    {
        return $"{GetValidBoardCfgId(boardCfgId)}_0";
    }

    public static string GetValidCfgId(string cfgId, string boardCfgId)
    {
        string validBoardCfgId = GetValidBoardCfgId(boardCfgId);
        if (!string.IsNullOrEmpty(cfgId)) {
            DuelHandicapDataType data = DuelHandicapDataType.GetConfigData(cfgId);
            if (data != null && data.boardCfgId == validBoardCfgId) {
                return cfgId;
            }
        }

        return GetDefaultCfgId(validBoardCfgId);
    }

    public static List<string> GetCfgIdsForBoard(string boardCfgId)
    {
        string validBoardCfgId = GetValidBoardCfgId(boardCfgId);
        DuelHandicapDataType.GetConfigData(GetDefaultCfgId(validBoardCfgId));

        List<string> cfgIds = new List<string>();
        if (DuelHandicapDataType.DuelHandicapDict != null) {
            foreach (KeyValuePair<string, DuelHandicapDataType> kvp in DuelHandicapDataType.DuelHandicapDict) {
                if (kvp.Value != null && kvp.Value.boardCfgId == validBoardCfgId) {
                    cfgIds.Add(kvp.Key);
                }
            }
        }

        cfgIds.Sort(CompareHandicapCfgId);
        if (cfgIds.Count == 0) {
            cfgIds.Add(GetDefaultCfgId(validBoardCfgId));
        }
        return cfgIds;
    }

    public static int GetHandicapCount(string cfgId)
    {
        DuelHandicapDataType data = DuelHandicapDataType.GetConfigData(cfgId);
        return data != null ? data.handicapCount : 0;
    }

    public static float GetKomi(string cfgId)
    {
        DuelHandicapDataType data = DuelHandicapDataType.GetConfigData(cfgId);
        return data != null ? data.komi : DefaultEvenGameKomi;
    }

    public static bool IsSen(string cfgId)
    {
        return !string.IsNullOrEmpty(cfgId) && cfgId.EndsWith(SenCfgIdSuffix);
    }

    public static bool HasHandicap(string cfgId)
    {
        return GetHandicapCount(cfgId) > 0;
    }

    public static JArray BuildInitialStonesArray(string cfgId, int boardSize)
    {
        JArray initialStones = new JArray();
        if (!TryBuildInitialStoneCoords(cfgId, boardSize, out List<RectCoordinates> coordsList)) {
            return initialStones;
        }

        foreach (RectCoordinates coords in coordsList) {
            initialStones.Add(new JArray(
                KataGoPositionJsonBuilder.ToKataGoColor(PlayerFlag.Player1),
                KataGoPositionJsonBuilder.ToKataGoPoint(coords, boardSize)));
        }
        return initialStones;
    }

    public static bool ApplyInitialStones(SceneBase scene, SceneComponentChessBoard compChessBoard, string cfgId, bool syncStoneViews = true)
    {
        if (scene == null || compChessBoard == null || compChessBoard.chessBoardGrid == null) {
            return false;
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        if (!TryBuildInitialStoneCoords(cfgId, boardSize, out List<RectCoordinates> coordsList)) {
            return false;
        }

        foreach (RectCoordinates coords in coordsList) {
            int posIndex = compChessBoard.GetPosIndexByCoords(coords);
            if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
                continue;
            }

            string chessGuid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
            ChessInfo chessInfo = new ChessInfo();
            chessInfo.chessGuid.value = chessGuid;
            chessInfo.chessFlag.value = (int)PlayerFlag.Player1;
            compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), chessInfo);
        }

        compChessBoard.lastChessInfoDict = compChessBoard.CreateCacheChessInfoDict();
        if (syncStoneViews) {
            compChessBoard.GetStoneViewCache().SyncFromChessInfoDict();
        }
        return true;
    }

    private static bool TryBuildInitialStoneCoords(string cfgId, int boardSize, out List<RectCoordinates> coordsList)
    {
        coordsList = new List<RectCoordinates>();
        DuelHandicapDataType data = DuelHandicapDataType.GetConfigData(cfgId);
        if (data == null || data.handicapCount <= 0 || data.stonePoints == null) {
            return true;
        }

        foreach (string point in data.stonePoints) {
            if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(point, boardSize, out RectCoordinates coords)) {
                XNLogger.LogError("Invalid handicap stone point.", ("cfgId", cfgId), ("point", point ?? string.Empty));
                coordsList.Clear();
                return false;
            }
            coordsList.Add(coords);
        }

        return true;
    }

    private static int CompareHandicapCfgId(string a, string b)
    {
        int orderCompare = GetHandicapOrder(a).CompareTo(GetHandicapOrder(b));
        if (orderCompare != 0) {
            return orderCompare;
        }

        return string.CompareOrdinal(a, b);
    }

    private static int GetHandicapOrder(string cfgId)
    {
        if (IsSen(cfgId)) {
            return 1;
        }

        return GetHandicapCount(cfgId) * 10;
    }

    private static string GetValidBoardCfgId(string boardCfgId)
    {
        return string.IsNullOrEmpty(boardCfgId) ? "9x9" : boardCfgId;
    }
}
