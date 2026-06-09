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

    public static JObject BuildAnalysisJsonWithMoveHistory(SceneBase scene, string requestId, int maxVisits = DefaultMaxVisits)
    {
        JObject query = BuildBaseAnalysisJson(scene, requestId, maxVisits);
        query["initialStones"] = BuildConfiguredInitialStonesArray(scene);
        query["moves"] = BuildMovesArray(scene);
        return query;
    }

    public static JObject BuildOwnershipAnalysisJson(SceneBase scene, string requestId, int maxVisits = DefaultMaxVisits)
    {
        return BuildAnalysisJsonWithCurrentBoard(scene, requestId, maxVisits);
    }

    public static JObject BuildAiMoveAnalysisJson(DuelScene duelScene, string requestId, DuelAiDifficultyDataType difficultyData, int maxVisits)
    {
        JObject query = BuildAnalysisJsonWithMoveHistory(duelScene, requestId, Math.Max(maxVisits, 1));
        query["analyzeTurns"] = new JArray((query["moves"] as JArray)?.Count ?? 0);
        query["includeOwnership"] = false;

        if (difficultyData != null) {
            bool useHumanSlProfile = difficultyData.useHumanPolicy
                && !string.IsNullOrEmpty(difficultyData.humanSLProfile)
                && KataGoBootstrap.CanUseHumanSlProfile();
            query["includePolicy"] = difficultyData.includePolicy || useHumanSlProfile;
            if (useHumanSlProfile) {
                query["overrideSettings"] = new JObject
                {
                    ["humanSLProfile"] = difficultyData.humanSLProfile,
                    ["ignorePreRootHistory"] = false,
                };
            }
        }

        return query;
    }

    public static JObject BuildReplayAiAnalysisJson(
        ReplayScene replayScene,
        string requestId,
        int maxVisits,
        bool includeOwnership,
        bool includePolicy)
    {
        JObject query = BuildBaseAnalysisJson(replayScene, requestId, Math.Max(maxVisits, 1));
        SceneComponentReplay compReplay = replayScene.GetComponent<SceneComponentReplay>();
        if (compReplay != null) {
            query["komi"] = compReplay.replayKomi;
        }

        query["initialStones"] = BuildReplayInitialStonesArray(replayScene);
        query["moves"] = BuildMovesArray(replayScene);
        query["analyzeTurns"] = new JArray((query["moves"] as JArray)?.Count ?? 0);
        query["includeOwnership"] = includeOwnership;
        query["includePolicy"] = includePolicy;
        return query;
    }

    public static JObject BuildDuelAiAnalysisJson(
        DuelScene duelScene,
        string requestId,
        int maxVisits,
        bool includeOwnership,
        bool includePolicy)
    {
        JObject query = BuildAnalysisJsonWithMoveHistory(duelScene, requestId, Math.Max(maxVisits, 1));
        query["analyzeTurns"] = new JArray((query["moves"] as JArray)?.Count ?? 0);
        query["includeOwnership"] = includeOwnership;
        query["includePolicy"] = includePolicy;
        return query;
    }

    public static JObject BuildAnalysisJsonWithCurrentBoard(SceneBase scene, string requestId, int maxVisits = DefaultMaxVisits)
    {
        JObject query = BuildBaseAnalysisJson(scene, requestId, maxVisits);
        query["initialStones"] = BuildInitialStonesArray(scene);
        query["moves"] = new JArray();
        return query;
    }

    public static JObject BuildOwnershipAnalysisJsonWithCurrentBoardSnapshot(
        SceneBase scene,
        string requestId,
        IEnumerable<int> excludedStonePosIndexes,
        int maxVisits = DefaultMaxVisits)
    {
        JObject query = BuildBaseAnalysisJson(scene, requestId, maxVisits);
        query["initialStones"] = BuildInitialStonesArray(scene, excludedStonePosIndexes);
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

    private static JObject BuildBaseAnalysisJson(SceneBase scene, string requestId, int maxVisits)
    {
        int boardSize = GetBoardSize(scene);
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        float komi = ResolveAnalysisKomi(scene, compDuel);

        JObject query = new JObject
        {
            ["id"] = requestId,
            ["rules"] = DefaultRules,
            ["komi"] = komi,
            ["boardXSize"] = boardSize,
            ["boardYSize"] = boardSize,
            ["maxVisits"] = maxVisits,
            ["includeOwnership"] = true,
            ["includePolicy"] = false,
        };

        return query;
    }

    private static float ResolveAnalysisKomi(SceneBase scene, SceneComponentDuel compDuel)
    {
        SceneComponentOgsDuel compOgsDuel = scene.GetComponent<SceneComponentOgsDuel>();
        if (compOgsDuel != null && compOgsDuel.hasKomi) {
            return compOgsDuel.komi;
        }

        return compDuel != null
            ? DuelHandicapPlacement.GetKomi(compDuel.handicapCfgId.value)
            : DefaultKomi;
    }

    private static JArray BuildMovesArray(SceneBase scene)
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return DuelMoveHistory.CreateEmpty();
        }

        return DuelMoveHistory.BuildKataGoMovesArray(compDuel.kataGoMoves);
    }

    private static JArray BuildConfiguredInitialStonesArray(SceneBase scene)
    {
        SceneComponentOgsDuel compOgsDuel = scene.GetComponent<SceneComponentOgsDuel>();
        if (compOgsDuel != null) {
            return compOgsDuel.kataGoInitialStones != null
                ? new JArray(compOgsDuel.kataGoInitialStones)
                : new JArray();
        }

        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !DuelHandicapPlacement.HasHandicap(compDuel.handicapCfgId.value)) {
            return new JArray();
        }

        return DuelHandicapPlacement.BuildInitialStonesArray(compDuel.handicapCfgId.value, GetBoardSize(scene));
    }

    private static JArray BuildReplayInitialStonesArray(ReplayScene replayScene)
    {
        JArray initialStones = new JArray();
        SceneComponentReplay compReplay = replayScene.GetComponent<SceneComponentReplay>();
        if (compReplay == null) {
            return initialStones;
        }

        foreach (ReplayMoveState stone in compReplay.replayInitialStones) {
            if (stone == null || stone.coords == null || stone.isPass) {
                continue;
            }

            string color = ToKataGoColor(stone.playerFlag);
            if (string.IsNullOrEmpty(color)) {
                continue;
            }

            initialStones.Add(new JArray(color, ToKataGoPoint(stone.coords, compReplay.replayBoardSize)));
        }

        return initialStones;
    }

    private static JArray BuildInitialStonesArray(SceneBase scene)
    {
        return BuildInitialStonesArray(scene, null);
    }

    private static JArray BuildInitialStonesArray(SceneBase scene, IEnumerable<int> excludedStonePosIndexes)
    {
        JArray initialStones = new JArray();
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard == null) {
            return initialStones;
        }

        int boardSize = GetBoardSize(scene);
        HashSet<int> excluded = excludedStonePosIndexes != null
            ? new HashSet<int>(excludedStonePosIndexes)
            : null;
        List<int> sortedPosIndexes = new List<int>();
        foreach (string posKey in compChessBoard.chessInfoDict.Keys) {
            if (int.TryParse(posKey, out int posIndex)) {
                sortedPosIndexes.Add(posIndex);
            }
        }
        sortedPosIndexes.Sort();

        foreach (int posIndex in sortedPosIndexes) {
            if (excluded != null && excluded.Contains(posIndex)) {
                continue;
            }

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

    private static int GetBoardSize(SceneBase scene)
    {
        SceneComponentChessBoard compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
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
