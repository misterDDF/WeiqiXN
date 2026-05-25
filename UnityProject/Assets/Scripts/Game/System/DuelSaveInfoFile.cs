using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public static class DuelSaveInfoFile
{
    public static bool Save(
        DuelScene duelScene,
        string filePath,
        int saveSlotIndex,
        bool isArchived = false,
        bool isCompleted = false,
        string archivedAtUtc = null)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Duel save info save is skipped on WebGL platform.", ("filePath", filePath));
        return false;
#else
        try {
            string dirPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dirPath)) {
                Directory.CreateDirectory(dirPath);
            }

            JObject saveInfoJson = BuildSaveInfoJson(duelScene, saveSlotIndex, isArchived, isCompleted, archivedAtUtc);
            File.WriteAllText(filePath, saveInfoJson.ToString());
            XNLogger.LogInfo("Duel save info save success.", ("filePath", filePath));
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel save info save failed.", ("filePath", filePath), ("err", ex.Message));
            return false;
        }
#endif
    }

    public static JObject BuildSaveInfoJson(
        DuelScene duelScene,
        int saveSlotIndex,
        bool isArchived = false,
        bool isCompleted = false,
        string archivedAtUtc = null)
    {
        SceneComponentChessBoard compChessBoard = duelScene.GetComponent<SceneComponentChessBoard>();
        SceneComponentDuel compDuel = duelScene.GetComponent<SceneComponentDuel>();

        string boardCfgId = compChessBoard?.boardCfgId.value ?? string.Empty;
        ChessBoardDataType chessBoardData = !string.IsNullOrEmpty(boardCfgId) ? ChessBoardDataType.GetConfigData(boardCfgId) : null;

        string holdTimeCfgId = compDuel?.holdTimeCfgId.value ?? string.Empty;
        string byoyomiCountCfgId = compDuel?.byoyomiCountCfgId.value ?? string.Empty;
        string byoyomiTimeCfgId = compDuel?.byoyomiTimeCfgId.value ?? string.Empty;
        string handicapCfgId = compDuel?.handicapCfgId.value ?? string.Empty;

        DuelHoldTimeDataType holdTimeData = !string.IsNullOrEmpty(holdTimeCfgId) ? DuelHoldTimeDataType.GetConfigData(holdTimeCfgId) : null;
        DuelByoyomiCountDataType byoyomiCountData = !string.IsNullOrEmpty(byoyomiCountCfgId) ? DuelByoyomiCountDataType.GetConfigData(byoyomiCountCfgId) : null;
        DuelByoyomiTimeDataType byoyomiTimeData = !string.IsNullOrEmpty(byoyomiTimeCfgId) ? DuelByoyomiTimeDataType.GetConfigData(byoyomiTimeCfgId) : null;
        DuelHandicapDataType handicapData = !string.IsNullOrEmpty(handicapCfgId) ? DuelHandicapDataType.GetConfigData(handicapCfgId) : null;
        string winnerFlag = GetWinnerFlag(compDuel);
        string resultType = compDuel?.gameEndReason.value ?? string.Empty;
        int moveCount = DuelMoveHistory.Count(compDuel?.kataGoMoves);
        bool completed = isCompleted || IsGameCompleted(compDuel);
        string lastUpdatedAtUtc = compDuel?.replayLastUpdatedAtUtc.value ?? string.Empty;
        if (string.IsNullOrEmpty(lastUpdatedAtUtc)) {
            lastUpdatedAtUtc = DateTime.UtcNow.ToString("o");
        }

        JObject saveInfoJson = new JObject
        {
            ["saveSlotIndex"] = saveSlotIndex,
            ["savedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["gameId"] = compDuel?.replayGameId.value ?? string.Empty,
            ["createdAtUtc"] = compDuel?.replayCreatedAtUtc.value ?? string.Empty,
            ["lastUpdatedAtUtc"] = lastUpdatedAtUtc,
            ["archivedAtUtc"] = archivedAtUtc ?? compDuel?.replayArchivedAtUtc.value ?? string.Empty,
            ["moveCount"] = moveCount,
            ["isCompleted"] = completed,
            ["isArchived"] = isArchived,
            ["sourceType"] = GetSourceType(compDuel),
            ["winnerFlag"] = winnerFlag,
            ["finalScore"] = BuildFinalScoreText(compDuel, winnerFlag),
            ["resultType"] = resultType,
            ["rules"] = KataGoDuelRecordFile.Rules,
            ["komi"] = GetKomi(handicapData),
            ["board"] = new JObject
            {
                ["cfgId"] = boardCfgId,
                ["boardSize"] = chessBoardData?.boardSize ?? 0,
            },
            ["timeControl"] = new JObject
            {
                ["holdTime"] = new JObject
                {
                    ["cfgId"] = holdTimeCfgId,
                    ["displayName"] = holdTimeData?.displayName ?? string.Empty,
                    ["holdSeconds"] = holdTimeData?.holdSeconds ?? 0,
                    ["isInfinite"] = holdTimeData?.isInfinite ?? false,
                },
                ["byoyomiCount"] = new JObject
                {
                    ["cfgId"] = byoyomiCountCfgId,
                    ["displayName"] = byoyomiCountData?.displayName ?? string.Empty,
                    ["count"] = byoyomiCountData?.count ?? 0,
                },
                ["byoyomiTime"] = new JObject
                {
                    ["cfgId"] = byoyomiTimeCfgId,
                    ["displayName"] = byoyomiTimeData?.displayName ?? string.Empty,
                    ["seconds"] = byoyomiTimeData?.seconds ?? 0,
                },
            },
            ["handicap"] = new JObject
            {
                ["cfgId"] = handicapCfgId,
                ["displayName"] = handicapData?.displayName ?? string.Empty,
                ["handicapCount"] = handicapData?.handicapCount ?? 0,
            },
        };

        return saveInfoJson;
    }

    private static bool IsGameCompleted(SceneComponentDuel compDuel)
    {
        return compDuel != null &&
            compDuel.duelFSM?.curState != null &&
            compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_GAME_END;
    }

    private static string GetSourceType(SceneComponentDuel compDuel)
    {
        if (compDuel == null) {
            return string.Empty;
        }

        if (compDuel.isLanDuel.value) {
            return "lan";
        }

        if (compDuel.isAiDuel.value) {
            return "ai";
        }

        return "local";
    }

    private static string GetWinnerFlag(SceneComponentDuel compDuel)
    {
        if (compDuel == null || string.IsNullOrEmpty(compDuel.winnerGuid.value)) {
            return string.Empty;
        }

        if (compDuel.winnerGuid.value == compDuel.player1Guid.value) {
            return PlayerFlag.Player1.ToString();
        }

        if (compDuel.winnerGuid.value == compDuel.player2Guid.value) {
            return PlayerFlag.Player2.ToString();
        }

        return string.Empty;
    }

    private static string BuildFinalScoreText(SceneComponentDuel compDuel, string winnerFlag)
    {
        if (compDuel == null || string.IsNullOrEmpty(winnerFlag)) {
            return string.Empty;
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Timeout) {
            return winnerFlag == PlayerFlag.Player1.ToString() ? "B+T" : "W+T";
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Resign) {
            return winnerFlag == PlayerFlag.Player1.ToString() ? "B+R" : "W+R";
        }

        if (compDuel.gameEndReason.value == DuelGameEndReason.Score ||
            compDuel.gameEndReason.value == DuelGameEndReason.ConsecutivePass) {
            string margin = Math.Abs(compDuel.finalScoreMargin.value).ToString("0.##", CultureInfo.InvariantCulture);
            return winnerFlag == PlayerFlag.Player1.ToString() ? $"B+{margin}" : $"W+{margin}";
        }

        return string.Empty;
    }

    private static float GetKomi(DuelHandicapDataType handicapData)
    {
        return handicapData != null ? handicapData.komi : KataGoDuelRecordFile.Komi;
    }
}
