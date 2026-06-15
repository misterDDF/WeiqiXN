using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public static class KataGoDuelRecordFile
{
    public const string Rules = "chinese";
    public const float Komi = 7.5f;
    public const int DefaultMaxVisits = 16;
    private const float MinKataGoKomi = -150f;
    private const float MaxKataGoKomi = 150f;

    public static JObject BuildRecordJson(SceneBase scene, string requestId = "duel-record")
    {
        JObject recordJson = KataGoPositionJsonBuilder.BuildAnalysisJsonWithMoveHistory(
            scene,
            requestId,
            DefaultMaxVisits
        );

        recordJson["includeOwnership"] = true;
        recordJson["includePolicy"] = false;
        return recordJson;
    }

    public static bool Save(SceneBase scene, string filePath)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("KataGo duel record save is skipped on WebGL platform.", ("filePath", filePath));
        return false;
#else
        try {
            string dirPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dirPath)) {
                Directory.CreateDirectory(dirPath);
            }

            JObject recordJson = BuildRecordJson(scene);
            File.WriteAllText(filePath, recordJson.ToString());
            XNLogger.LogInfo("KataGo duel record save success.", ("filePath", filePath));
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("KataGo duel record save failed.", ("filePath", filePath), ("err", ex.Message));
            return false;
        }
#endif
    }

    public static bool TryLoad(string filePath, out JObject recordJson)
    {
        recordJson = null;
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("KataGo duel record load is skipped on WebGL platform.", ("filePath", filePath));
        return false;
#else
        if (!File.Exists(filePath)) {
            XNLogger.LogError("KataGo duel record file not found.", ("filePath", filePath));
            return false;
        }

        try {
            recordJson = JObject.Parse(File.ReadAllText(filePath));
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("KataGo duel record load failed.", ("filePath", filePath), ("err", ex.Message));
            return false;
        }
#endif
    }

    public static bool TryGetMoves(JObject recordJson, out JArray moves)
    {
        moves = recordJson?["moves"] as JArray;
        return moves != null;
    }

    public static bool TryGetInitialStones(JObject recordJson, out JArray initialStones)
    {
        initialStones = recordJson?["initialStones"] as JArray;
        return initialStones != null;
    }

    public static bool TryGetBoardSize(JObject recordJson, out int boardSize)
    {
        boardSize = 0;
        if (recordJson == null) {
            return false;
        }

        JToken boardXSizeToken = recordJson["boardXSize"];
        JToken boardYSizeToken = recordJson["boardYSize"];
        if (boardXSizeToken == null || boardYSizeToken == null) {
            return false;
        }

        if (!int.TryParse(boardXSizeToken.ToString(), out int boardXSize) ||
            !int.TryParse(boardYSizeToken.ToString(), out int boardYSize)) {
            return false;
        }

        if (boardXSize <= 0 || boardXSize != boardYSize) {
            return false;
        }

        boardSize = boardXSize;
        return true;
    }

    public static bool TryGetKomi(JObject recordJson, out float komi)
    {
        komi = Komi;
        if (recordJson == null || recordJson["komi"] == null) {
            return false;
        }

        string rules = TryGetRules(recordJson, out string recordRules) ? recordRules : Rules;
        return TryNormalizeKomi(recordJson["komi"]?.ToString(), rules, out komi);
    }

    public static bool TryGetRules(JObject recordJson, out string rules)
    {
        rules = Rules;
        return TryNormalizeRules(recordJson?["rules"]?.ToString(), out rules);
    }

    public static bool TryNormalizeRules(string rawRules, out string rules)
    {
        rules = Rules;
        if (string.IsNullOrWhiteSpace(rawRules)) {
            return false;
        }

        string normalized = rawRules.Trim().ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
        switch (normalized) {
            case "chinese":
            case "china":
            case "cn":
            case "zhongguo":
            case "zhongshi":
            case "中国":
            case "中式":
            case "中国规则":
                rules = "chinese";
                return true;
            case "japanese":
            case "japan":
            case "jp":
            case "riben":
            case "rishi":
            case "日本":
            case "日式":
            case "日本规则":
                rules = "japanese";
                return true;
            case "aga":
            case "american":
                rules = "aga";
                return true;
            case "korean":
            case "korea":
            case "kr":
            case "韩国":
            case "韩式":
                rules = "korean";
                return true;
            case "newzealand":
            case "nz":
                rules = "new-zealand";
                return true;
            case "tromptaylor":
            case "tt":
                rules = "tromp-taylor";
                return true;
            case "chineseogs":
                rules = "chinese-ogs";
                return true;
            case "stonescoring":
                rules = "stone-scoring";
                return true;
            default:
                return false;
        }
    }

    public static bool TryNormalizeKomi(string rawKomi, string rules, out float komi)
    {
        komi = Komi;
        if (string.IsNullOrWhiteSpace(rawKomi) ||
            !float.TryParse(rawKomi.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) {
            return false;
        }

        komi = NormalizeKomiValue(parsed, rules);
        return true;
    }

    public static float NormalizeKomiValue(float komi, string rules)
    {
        float normalized = komi;
        if (IsChineseRules(rules) && Math.Abs(normalized) > MaxKataGoKomi) {
            normalized = normalized / 50f;
        }

        if (IsChineseRules(rules) && !IsKataGoKomiValid(normalized)) {
            float chinesePointKomi = normalized * 2f;
            if (IsKataGoKomiValid(chinesePointKomi)) {
                normalized = chinesePointKomi;
            }
        }

        if (!IsKataGoKomiValid(normalized)) {
            XNLogger.LogWarn(
                "KataGo komi value invalid, fallback will be used.",
                ("rules", rules ?? string.Empty),
                ("komi", komi.ToString(CultureInfo.InvariantCulture)),
                ("normalizedKomi", normalized.ToString(CultureInfo.InvariantCulture)),
                ("fallback", Komi.ToString(CultureInfo.InvariantCulture)));
            return Komi;
        }

        return normalized;
    }

    public static bool IsKataGoKomiValid(float komi)
    {
        if (komi < MinKataGoKomi || komi > MaxKataGoKomi) {
            return false;
        }

        float doubled = komi * 2f;
        return Math.Abs(doubled - (float)Math.Round(doubled)) < 0.001f;
    }

    private static bool IsChineseRules(string rules)
    {
        return string.Equals(rules, "chinese", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rules, "chinese-ogs", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetHandicapCount(JObject recordJson, out int handicapCount)
    {
        handicapCount = 0;
        if (recordJson == null) {
            return false;
        }

        JToken token = recordJson["handicapCount"] ?? recordJson["handicap"]?["handicapCount"];
        if (token == null || !int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
            return false;
        }

        handicapCount = Math.Max(parsed, 0);
        return true;
    }

    public static bool TryParseMove(JToken moveToken, out PlayerFlag playerFlag, out RectCoordinates coords, out bool isPass, int boardSize)
    {
        playerFlag = 0;
        coords = null;
        isPass = false;

        JArray move = moveToken as JArray;
        if (move == null || move.Count < 2) {
            return false;
        }

        string color = move[0]?.ToString();
        string point = move[1]?.ToString();
        if (!TryParseColor(color, out playerFlag)) {
            return false;
        }

        if (string.Equals(point, KataGoPositionJsonBuilder.PassPoint, StringComparison.OrdinalIgnoreCase)) {
            isPass = true;
            return true;
        }

        if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(point, boardSize, out coords)) {
            return false;
        }

        return true;
    }

    private static bool TryParseColor(string color, out PlayerFlag playerFlag)
    {
        switch (color) {
            case "B":
                playerFlag = PlayerFlag.Player1;
                return true;
            case "W":
                playerFlag = PlayerFlag.Player2;
                return true;
            default:
                playerFlag = 0;
                return false;
        }
    }
}
