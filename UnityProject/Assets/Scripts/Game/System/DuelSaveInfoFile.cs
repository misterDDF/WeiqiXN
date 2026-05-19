using System;
using System.IO;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public static class DuelSaveInfoFile
{
    public static bool Save(DuelScene duelScene, string filePath, int saveSlotIndex)
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

            JObject saveInfoJson = BuildSaveInfoJson(duelScene, saveSlotIndex);
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

    public static JObject BuildSaveInfoJson(DuelScene duelScene, int saveSlotIndex)
    {
        SceneComponentChessBoard compChessBoard = duelScene.GetComponent<SceneComponentChessBoard>();
        SceneComponentDuel compDuel = duelScene.GetComponent<SceneComponentDuel>();

        string boardCfgId = compChessBoard?.boardCfgId.value ?? string.Empty;
        ChessBoardDataType chessBoardData = !string.IsNullOrEmpty(boardCfgId) ? ChessBoardDataType.GetConfigData(boardCfgId) : null;

        string holdTimeCfgId = compDuel?.holdTimeCfgId.value ?? string.Empty;
        string byoyomiCountCfgId = compDuel?.byoyomiCountCfgId.value ?? string.Empty;
        string byoyomiTimeCfgId = compDuel?.byoyomiTimeCfgId.value ?? string.Empty;

        DuelHoldTimeDataType holdTimeData = !string.IsNullOrEmpty(holdTimeCfgId) ? DuelHoldTimeDataType.GetConfigData(holdTimeCfgId) : null;
        DuelByoyomiCountDataType byoyomiCountData = !string.IsNullOrEmpty(byoyomiCountCfgId) ? DuelByoyomiCountDataType.GetConfigData(byoyomiCountCfgId) : null;
        DuelByoyomiTimeDataType byoyomiTimeData = !string.IsNullOrEmpty(byoyomiTimeCfgId) ? DuelByoyomiTimeDataType.GetConfigData(byoyomiTimeCfgId) : null;

        JObject saveInfoJson = new JObject
        {
            ["saveSlotIndex"] = saveSlotIndex,
            ["savedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["moveCount"] = compDuel?.kataGoMoves?.Count ?? 0,
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
        };

        return saveInfoJson;
    }
}
