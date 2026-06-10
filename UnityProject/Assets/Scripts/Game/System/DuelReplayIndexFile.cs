using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
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

            JObject indexJson = ParseIndexJson(File.ReadAllText(GameSaveConfig.ReplayIndexPath));
            if (!(indexJson["items"] is JArray itemTokens)) {
                return true;
            }

            foreach (JObject itemJson in itemTokens.OfType<JObject>()) {
                items.Add(DuelReplayIndexItem.FromJson(itemJson));
            }

            items.Sort(CompareByLastUpdatedDesc);
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

        string gameId = ReadTokenString(saveInfoJson["gameId"]);
        if (string.IsNullOrEmpty(gameId)) {
            return false;
        }

        try {
            JObject indexJson = LoadOrCreate();
            JArray items = GetItems(indexJson);
            JObject item = items
                .OfType<JObject>()
                .FirstOrDefault(existing => string.Equals(ReadTokenString(existing["gameId"]), gameId, StringComparison.Ordinal));
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
                .FirstOrDefault(existing => string.Equals(ReadTokenString(existing["gameId"]), gameId, StringComparison.Ordinal));
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

        JObject indexJson = ParseIndexJson(File.ReadAllText(indexPath));
        GetItems(indexJson);
        return indexJson;
    }

    private static JObject ParseIndexJson(string json)
    {
        using (StringReader stringReader = new StringReader(json))
        using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
        {
            jsonReader.DateParseHandling = DateParseHandling.None;
            return JObject.Load(jsonReader);
        }
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
        string gameId = ReadTokenString(saveInfoJson["gameId"]);
        item["gameId"] = gameId;
        item["createdAtUtc"] = ReadTokenString(saveInfoJson["createdAtUtc"]);
        item["lastUpdatedAtUtc"] = ReadTokenString(saveInfoJson["lastUpdatedAtUtc"]);
        item["archivedAtUtc"] = ReadTokenString(saveInfoJson["archivedAtUtc"]);
        item["moveCount"] = saveInfoJson["moveCount"]?.Value<int>() ?? 0;
        item["boardSize"] = saveInfoJson["board"]?["boardSize"]?.Value<int>() ?? 0;
        item["resultType"] = ReadTokenString(saveInfoJson["resultType"]);
        item["winnerFlag"] = ReadTokenString(saveInfoJson["winnerFlag"]);
        item["finalScore"] = ReadTokenString(saveInfoJson["finalScore"]);
        item["sourceType"] = ReadTokenString(saveInfoJson["sourceType"]);
        item["blackPlayerName"] = ReadTokenString(saveInfoJson["players"]?["black"]?["displayName"]);
        item["whitePlayerName"] = ReadTokenString(saveInfoJson["players"]?["white"]?["displayName"]);
        item["isCompleted"] = saveInfoJson["isCompleted"]?.Value<bool>() ?? false;
        item["isArchived"] = saveInfoJson["isArchived"]?.Value<bool>() ?? false;
        item["saveInfoPath"] = $"replay/{gameId}/SaveInfo.json";
    }

    private static void SortItems(JArray items)
    {
        JToken[] sortedItems = items
            .OfType<JObject>()
            .OrderByDescending(item => ReadUtcTicks(item["lastUpdatedAtUtc"]))
            .ThenByDescending(item => ReadTokenString(item["gameId"]), StringComparer.Ordinal)
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

    internal static string ReadIndexTokenString(JToken token)
    {
        return ReadTokenString(token);
    }

    internal static long ReadIndexUtcTicks(JToken token)
    {
        return ReadUtcTicks(token);
    }

    private static int CompareByLastUpdatedDesc(DuelReplayIndexItem a, DuelReplayIndexItem b)
    {
        long leftTicks = a != null ? a.lastUpdatedAtUtcTicks : long.MinValue;
        long rightTicks = b != null ? b.lastUpdatedAtUtcTicks : long.MinValue;
        int timeCompare = rightTicks.CompareTo(leftTicks);
        if (timeCompare != 0) {
            return timeCompare;
        }

        return string.CompareOrdinal(b?.gameId ?? string.Empty, a?.gameId ?? string.Empty);
    }

    private static string ReadTokenString(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return string.Empty;
        }

        if (token.Type == JTokenType.Date) {
            DateTime dateTime = token.Value<DateTime>();
            if (dateTime.Kind == DateTimeKind.Unspecified) {
                dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            }

            return dateTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        return token.Type == JTokenType.String
            ? token.Value<string>() ?? string.Empty
            : token.ToString();
    }

    private static long ReadUtcTicks(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return long.MinValue;
        }

        if (token.Type == JTokenType.Date) {
            DateTime dateTime = token.Value<DateTime>();
            if (dateTime.Kind == DateTimeKind.Unspecified) {
                dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            }

            return dateTime.ToUniversalTime().Ticks;
        }

        string timeText = ReadTokenString(token);
        if (DateTimeOffset.TryParse(
            timeText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset dateTimeOffset)) {
            return dateTimeOffset.UtcDateTime.Ticks;
        }

        return long.MinValue;
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
    public long lastUpdatedAtUtcTicks;

    public static DuelReplayIndexItem FromJson(JObject json)
    {
        string lastUpdatedAtUtc = DuelReplayIndexFile.ReadIndexTokenString(json?["lastUpdatedAtUtc"]);
        return new DuelReplayIndexItem
        {
            gameId = DuelReplayIndexFile.ReadIndexTokenString(json?["gameId"]),
            createdAtUtc = DuelReplayIndexFile.ReadIndexTokenString(json?["createdAtUtc"]),
            lastUpdatedAtUtc = lastUpdatedAtUtc,
            archivedAtUtc = DuelReplayIndexFile.ReadIndexTokenString(json?["archivedAtUtc"]),
            moveCount = json?["moveCount"]?.Value<int>() ?? 0,
            boardSize = json?["boardSize"]?.Value<int>() ?? 0,
            resultType = DuelReplayIndexFile.ReadIndexTokenString(json?["resultType"]),
            winnerFlag = DuelReplayIndexFile.ReadIndexTokenString(json?["winnerFlag"]),
            finalScore = DuelReplayIndexFile.ReadIndexTokenString(json?["finalScore"]),
            sourceType = DuelReplayIndexFile.ReadIndexTokenString(json?["sourceType"]),
            blackPlayerName = DuelReplayIndexFile.ReadIndexTokenString(json?["blackPlayerName"]),
            whitePlayerName = DuelReplayIndexFile.ReadIndexTokenString(json?["whitePlayerName"]),
            isCompleted = json?["isCompleted"]?.Value<bool>() ?? false,
            isArchived = json?["isArchived"]?.Value<bool>() ?? false,
            saveInfoPath = DuelReplayIndexFile.ReadIndexTokenString(json?["saveInfoPath"]),
            lastUpdatedAtUtcTicks = DuelReplayIndexFile.ReadIndexUtcTicks(json?["lastUpdatedAtUtc"]),
        };
    }
}
