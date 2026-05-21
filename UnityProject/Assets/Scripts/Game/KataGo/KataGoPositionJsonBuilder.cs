using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;

public static class KataGoPositionJsonBuilder
{
    public const string PassPoint = "pass";

    private const string GoColumns = "ABCDEFGHJKLMNOPQRST";
    private const string DefaultRules = "chinese";
    private const float DefaultKomi = 7.5f;
    private const int DefaultMaxVisits = 16;

    public static JObject BuildAnalysisJsonWithMoveHistory(DuelScene duelScene, string requestId, int maxVisits = DefaultMaxVisits)
    {
        JObject query = BuildBaseAnalysisJson(duelScene, requestId, maxVisits);
        query["initialStones"] = new JArray();
        query["moves"] = BuildMovesArray(duelScene);
        return query;
    }

    public static JObject BuildOwnershipAnalysisJson(DuelScene duelScene, string requestId, int maxVisits = DefaultMaxVisits)
    {
        SceneComponentDuel compDuel = duelScene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && DuelMoveHistory.Count(compDuel.kataGoMoves) > 0) {
            return BuildAnalysisJsonWithMoveHistory(duelScene, requestId, maxVisits);
        }

        return BuildAnalysisJsonWithCurrentBoard(duelScene, requestId, maxVisits);
    }

    public static JObject BuildAiMoveAnalysisJson(DuelScene duelScene, string requestId, DuelAiDifficultyDataType difficultyData, int maxVisits)
    {
        JObject query = BuildAnalysisJsonWithMoveHistory(duelScene, requestId, Math.Max(maxVisits, 1));
        query["analyzeTurns"] = new JArray((query["moves"] as JArray)?.Count ?? 0);
        query["includeOwnership"] = false;

        if (difficultyData != null) {
            query["includePolicy"] = difficultyData.includePolicy;
            if (difficultyData.useHumanPolicy
                && !string.IsNullOrEmpty(difficultyData.humanSLProfile)
                && KataGoBootstrap.CanUseHumanSlProfile()) {
                query["overrideSettings"] = new JObject
                {
                    ["humanSLProfile"] = difficultyData.humanSLProfile,
                };
            }
        }

        return query;
    }

    public static JObject BuildAnalysisJsonWithCurrentBoard(DuelScene duelScene, string requestId, int maxVisits = DefaultMaxVisits)
    {
        JObject query = BuildBaseAnalysisJson(duelScene, requestId, maxVisits);
        query["initialStones"] = BuildInitialStonesArray(duelScene);
        query["moves"] = new JArray();
        return query;
    }

    public static string ToKataGoColor(PlayerFlag playerFlag)
    {
        switch (playerFlag) {
            case PlayerFlag.Player1:
                return "B";
            case PlayerFlag.Player2:
                return "W";
            default:
                return string.Empty;
        }
    }

    public static string ToKataGoPoint(RectCoordinates coords, int boardSize)
    {
        if (coords == null) {
            throw new ArgumentNullException(nameof(coords));
        }

        if (boardSize <= 0 || boardSize > GoColumns.Length) {
            throw new ArgumentOutOfRangeException(nameof(boardSize), boardSize, "Unsupported board size for KataGo point conversion.");
        }

        if (coords.x < 0 || coords.x >= boardSize || coords.z < 0 || coords.z >= boardSize) {
            throw new ArgumentOutOfRangeException(nameof(coords), coords.ToString(), "Coordinates are outside of the board.");
        }

        return $"{GoColumns[coords.x]}{boardSize - coords.z}";
    }

    public static bool TryParseKataGoPoint(string point, int boardSize, out RectCoordinates coords)
    {
        coords = null;
        if (string.IsNullOrEmpty(point) || point.Length < 2 || boardSize <= 0 || boardSize > GoColumns.Length) {
            return false;
        }

        char column = char.ToUpperInvariant(point[0]);
        int x = GoColumns.IndexOf(column);
        if (x < 0 || x >= boardSize) {
            return false;
        }

        if (!int.TryParse(point.Substring(1), out int row)) {
            return false;
        }

        int z = boardSize - row;
        if (z < 0 || z >= boardSize) {
            return false;
        }

        coords = new RectCoordinates(x, z);
        return true;
    }

    private static JObject BuildBaseAnalysisJson(DuelScene duelScene, string requestId, int maxVisits)
    {
        int boardSize = GetBoardSize(duelScene);

        JObject query = new JObject
        {
            ["id"] = requestId,
            ["rules"] = DefaultRules,
            ["komi"] = DefaultKomi,
            ["boardXSize"] = boardSize,
            ["boardYSize"] = boardSize,
            ["maxVisits"] = maxVisits,
            ["includeOwnership"] = true,
            ["includePolicy"] = false,
        };

        return query;
    }

    private static JArray BuildMovesArray(DuelScene duelScene)
    {
        SceneComponentDuel compDuel = duelScene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return DuelMoveHistory.CreateEmpty();
        }

        return DuelMoveHistory.BuildKataGoMovesArray(compDuel.kataGoMoves);
    }

    private static JArray BuildInitialStonesArray(DuelScene duelScene)
    {
        JArray initialStones = new JArray();
        SceneComponentChessBoard compChessBoard = duelScene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard == null) {
            return initialStones;
        }

        int boardSize = GetBoardSize(duelScene);
        List<int> sortedPosIndexes = new List<int>();
        foreach (string posKey in compChessBoard.chessInfoDict.Keys) {
            if (int.TryParse(posKey, out int posIndex)) {
                sortedPosIndexes.Add(posIndex);
            }
        }
        sortedPosIndexes.Sort();

        foreach (int posIndex in sortedPosIndexes) {
            if (!compChessBoard.chessInfoDict.TryGetValue(posIndex.ToString(), out ChessInfo chessInfo) || chessInfo == null) {
                continue;
            }

            string color = ToKataGoColor((PlayerFlag)chessInfo.chessFlag.value);
            if (string.IsNullOrEmpty(color)) {
                continue;
            }

            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(posIndex);
            initialStones.Add(new JArray(color, ToKataGoPoint(coords, boardSize)));
        }

        return initialStones;
    }

    private static int GetBoardSize(DuelScene duelScene)
    {
        SceneComponentChessBoard compChessBoard = duelScene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard?.chessBoardGrid != null) {
            return compChessBoard.chessBoardGrid.gridSize;
        }

        if (compChessBoard != null && !string.IsNullOrEmpty(compChessBoard.boardCfgId.value)) {
            ChessBoardDataType chessBoardData = ChessBoardDataType.GetConfigData(compChessBoard.boardCfgId.value);
            if (chessBoardData != null) {
                return chessBoardData.boardSize;
            }
        }

        return 19;
    }
}
