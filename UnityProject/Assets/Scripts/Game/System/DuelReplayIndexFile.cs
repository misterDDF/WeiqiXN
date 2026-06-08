using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public static class DuelReplayIndexFile
{
    public const int MinArchivedMoveCount = 11;

    public static bool TryLoadItems(out List<DuelReplayIndexItem> items)
    {
        items = new List<DuelReplayIndexItem>();
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Duel replay index load is skipped on WebGL platform.");
        return false;
#else
        try {
            if (!File.Exists(GameSaveConfig.ReplayIndexPath)) {
                return true;
            }

            JObject indexJson = JObject.Parse(File.ReadAllText(GameSaveConfig.ReplayIndexPath));
            if (!(indexJson["items"] is JArray itemTokens)) {
                return true;
            }

            foreach (JObject itemJson in itemTokens.OfType<JObject>()) {
                items.Add(DuelReplayIndexItem.FromJson(itemJson));
            }

            items.Sort((a, b) => string.CompareOrdinal(b.lastUpdatedAtUtc, a.lastUpdatedAtUtc));
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel replay index load failed.", ("filePath", GameSaveConfig.ReplayIndexPath), ("err", ex.Message));
            return false;
        }
#endif
    }

    public static bool Upsert(JObject saveInfoJson)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Duel replay index save is skipped on WebGL platform.");
        return false;
#else
        if (saveInfoJson == null) {
            return false;
        }

        string gameId = saveInfoJson["gameId"]?.ToString();
        if (string.IsNullOrEmpty(gameId)) {
            return false;
        }

        try {
            JObject indexJson = LoadOrCreate();
            JArray items = GetItems(indexJson);
            JObject item = items
                .OfType<JObject>()
                .FirstOrDefault(existing => string.Equals(existing["gameId"]?.ToString(), gameId, StringComparison.Ordinal));
            if (item == null) {
                item = new JObject();
                items.Add(item);
            }

            FillItem(item, saveInfoJson);
            SortItems(items);
            Save(indexJson);
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel replay index upsert failed.", ("gameId", gameId), ("err", ex.Message));
            return false;
        }
#endif
    }

    public static bool Remove(string gameId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Duel replay index remove is skipped on WebGL platform.", ("gameId", gameId));
        return false;
#else
        if (string.IsNullOrEmpty(gameId)) {
            return false;
        }

        try {
            JObject indexJson = LoadOrCreate();
            JArray items = GetItems(indexJson);
            JToken target = items
                .OfType<JObject>()
                .FirstOrDefault(existing => string.Equals(existing["gameId"]?.ToString(), gameId, StringComparison.Ordinal));
            target?.Remove();
            Save(indexJson);
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel replay index remove failed.", ("gameId", gameId), ("err", ex.Message));
            return false;
        }
#endif
    }

    private static JObject LoadOrCreate()
    {
        string indexPath = GameSaveConfig.ReplayIndexPath;
        if (!File.Exists(indexPath)) {
            return new JObject { ["items"] = new JArray() };
        }

        JObject indexJson = JObject.Parse(File.ReadAllText(indexPath));
        GetItems(indexJson);
        return indexJson;
    }

    private static JArray GetItems(JObject indexJson)
    {
        if (indexJson["items"] is JArray items) {
            return items;
        }

        items = new JArray();
        indexJson["items"] = items;
        return items;
    }

    private static void FillItem(JObject item, JObject saveInfoJson)
    {
        string gameId = saveInfoJson["gameId"]?.ToString() ?? string.Empty;
        item["gameId"] = gameId;
        item["createdAtUtc"] = saveInfoJson["createdAtUtc"]?.ToString() ?? string.Empty;
        item["lastUpdatedAtUtc"] = saveInfoJson["lastUpdatedAtUtc"]?.ToString() ?? string.Empty;
        item["archivedAtUtc"] = saveInfoJson["archivedAtUtc"]?.ToString() ?? string.Empty;
        item["moveCount"] = saveInfoJson["moveCount"]?.Value<int>() ?? 0;
        item["boardSize"] = saveInfoJson["board"]?["boardSize"]?.Value<int>() ?? 0;
        item["resultType"] = saveInfoJson["resultType"]?.ToString() ?? string.Empty;
        item["winnerFlag"] = saveInfoJson["winnerFlag"]?.ToString() ?? string.Empty;
        item["finalScore"] = saveInfoJson["finalScore"]?.ToString() ?? string.Empty;
        item["sourceType"] = saveInfoJson["sourceType"]?.ToString() ?? string.Empty;
        item["blackPlayerName"] = saveInfoJson["players"]?["black"]?["displayName"]?.ToString() ?? string.Empty;
        item["whitePlayerName"] = saveInfoJson["players"]?["white"]?["displayName"]?.ToString() ?? string.Empty;
        item["isCompleted"] = saveInfoJson["isCompleted"]?.Value<bool>() ?? false;
        item["isArchived"] = saveInfoJson["isArchived"]?.Value<bool>() ?? false;
        item["saveInfoPath"] = $"replay/{gameId}/SaveInfo.json";
    }

    private static void SortItems(JArray items)
    {
        JToken[] sortedItems = items
            .OfType<JObject>()
            .OrderByDescending(item => item["lastUpdatedAtUtc"]?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .Cast<JToken>()
            .ToArray();

        items.Clear();
        foreach (JToken item in sortedItems) {
            items.Add(item);
        }
    }

    private static void Save(JObject indexJson)
    {
        Directory.CreateDirectory(GameSaveConfig.ReplayRootPath);
        File.WriteAllText(GameSaveConfig.ReplayIndexPath, indexJson.ToString());
    }
}

public class DuelReplayIndexItem
{
    public string gameId;
    public string createdAtUtc;
    public string lastUpdatedAtUtc;
    public string archivedAtUtc;
    public int moveCount;
    public int boardSize;
    public string resultType;
    public string winnerFlag;
    public string finalScore;
    public string sourceType;
    public string blackPlayerName;
    public string whitePlayerName;
    public bool isCompleted;
    public bool isArchived;
    public string saveInfoPath;

    public static DuelReplayIndexItem FromJson(JObject json)
    {
        return new DuelReplayIndexItem
        {
            gameId = json["gameId"]?.ToString() ?? string.Empty,
            createdAtUtc = json["createdAtUtc"]?.ToString() ?? string.Empty,
            lastUpdatedAtUtc = json["lastUpdatedAtUtc"]?.ToString() ?? string.Empty,
            archivedAtUtc = json["archivedAtUtc"]?.ToString() ?? string.Empty,
            moveCount = json["moveCount"]?.Value<int>() ?? 0,
            boardSize = json["boardSize"]?.Value<int>() ?? 0,
            resultType = json["resultType"]?.ToString() ?? string.Empty,
            winnerFlag = json["winnerFlag"]?.ToString() ?? string.Empty,
            finalScore = json["finalScore"]?.ToString() ?? string.Empty,
            sourceType = json["sourceType"]?.ToString() ?? string.Empty,
            blackPlayerName = json["blackPlayerName"]?.ToString() ?? string.Empty,
            whitePlayerName = json["whitePlayerName"]?.ToString() ?? string.Empty,
            isCompleted = json["isCompleted"]?.Value<bool>() ?? false,
            isArchived = json["isArchived"]?.Value<bool>() ?? false,
            saveInfoPath = json["saveInfoPath"]?.ToString() ?? string.Empty,
        };
    }
}
