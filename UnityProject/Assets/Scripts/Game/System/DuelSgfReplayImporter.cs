using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public static class DuelSgfReplayImporter
{
    private const int DefaultBoardSize = 19;
    private const int MinBoardSize = 2;
    private const int MaxBoardSize = 19;

    public static bool TryImport(string sgfFilePath, out string gameId, out string message)
    {
        gameId = string.Empty;
        message = string.Empty;

#if UNITY_WEBGL && !UNITY_EDITOR
        message = "当前平台不支持导入棋谱。";
        return false;
#else
        if (string.IsNullOrWhiteSpace(sgfFilePath) || !File.Exists(sgfFilePath)) {
            message = "请选择有效的 SGF 文件。";
            return false;
        }

        try {
            string sgfText = ReadSgfText(sgfFilePath);
            if (!TryParseMainLine(sgfText, out SgfGame sgfGame, out message)) {
                return false;
            }

            gameId = CreateGameId();
            string nowUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            JObject recordJson = BuildRecordJson(sgfGame, gameId);
            JObject saveInfoJson = BuildSaveInfoJson(sgfGame, gameId, nowUtc);
            Directory.CreateDirectory(GameSaveConfig.GetReplayGamePath(gameId));
            File.WriteAllText(GameSaveConfig.GetReplayDuelRecordPath(gameId), recordJson.ToString());
            File.WriteAllText(GameSaveConfig.GetReplayDuelSaveInfoPath(gameId), saveInfoJson.ToString());
            DuelReplayIndexFile.Upsert(saveInfoJson);
            XNLogger.LogInfo("SGF replay import success.", ("filePath", sgfFilePath), ("gameId", gameId));
            message = "棋谱导入成功。";
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("SGF replay import failed.", ("filePath", sgfFilePath), ("err", ex.Message));
            message = $"棋谱导入失败：{ex.Message}";
            return false;
        }
#endif
    }

    private static bool TryParseMainLine(string sgfText, out SgfGame sgfGame, out string message)
    {
        sgfGame = null;
        message = string.Empty;

        List<SgfNode> nodes = ParseRootMainLineNodes(sgfText);
        if (nodes.Count <= 0) {
            message = "SGF 文件没有可读取的主线。";
            return false;
        }

        SgfNode root = nodes[0];
        int boardSize = ParseBoardSize(root.GetFirst("SZ"));
        string rules = ParseRules(root.GetFirst("RU"));
        SgfGame parsed = new SgfGame
        {
            boardSize = boardSize,
            rules = rules,
            komi = ParseKomi(root.GetFirst("KM"), rules, IsFoxwqSgf(root)),
            handicapCount = ParseNonNegativeInt(root.GetFirst("HA")),
            blackName = FirstNonEmpty(root.GetFirst("PB"), "黑方"),
            whiteName = FirstNonEmpty(root.GetFirst("PW"), "白方"),
            result = root.GetFirst("RE") ?? string.Empty,
        };

        AppendSetupStones(parsed.initialStones, root.GetValues("AB"), "B", boardSize);
        AppendSetupStones(parsed.initialStones, root.GetValues("AW"), "W", boardSize);

        for (int i = 1; i < nodes.Count; i++) {
            SgfNode node = nodes[i];
            if (node.TryGetMove("B", out string blackMove) &&
                !TryAddMove(parsed.moves, "B", blackMove, boardSize, out message)) {
                return false;
            }

            if (node.TryGetMove("W", out string whiteMove) &&
                !TryAddMove(parsed.moves, "W", whiteMove, boardSize, out message)) {
                return false;
            }
        }

        sgfGame = parsed;
        return true;
    }

    private static string ReadSgfText(string sgfFilePath)
    {
        byte[] bytes = File.ReadAllBytes(sgfFilePath);
        string charset = ExtractCharset(bytes);
        Encoding declaredEncoding = ResolveDeclaredEncoding(charset);
        if (declaredEncoding != null) {
            return declaredEncoding.GetString(bytes);
        }

        if (TryDecodeUtf8(bytes, out string utf8Text)) {
            return utf8Text;
        }

        return ResolveChineseEncoding().GetString(bytes);
    }

    private static string ExtractCharset(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) {
            return string.Empty;
        }

        string asciiText = Encoding.ASCII.GetString(bytes);
        int start = asciiText.IndexOf("CA[", StringComparison.OrdinalIgnoreCase);
        if (start < 0) {
            return string.Empty;
        }

        start += 3;
        int end = asciiText.IndexOf(']', start);
        return end > start ? asciiText.Substring(start, end - start).Trim() : string.Empty;
    }

    private static Encoding ResolveDeclaredEncoding(string charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) {
            return null;
        }

        string normalized = charset.Trim().ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
        switch (normalized) {
            case "utf8":
            case "unicode11utf8":
                return new UTF8Encoding(false, true);
            case "gbk":
            case "gb2312":
            case "gb18030":
            case "cp936":
            case "ms936":
                return ResolveChineseEncoding();
            default:
                try {
                    return Encoding.GetEncoding(charset);
                }
                catch (Exception ex) {
                    XNLogger.LogWarn("Unsupported SGF charset, fallback will be used.", ("charset", charset), ("error", ex.Message));
                    return null;
                }
        }
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        text = string.Empty;
        try {
            text = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException) {
            return false;
        }
    }

    private static Encoding ResolveChineseEncoding()
    {
        string[] names = { "GB18030", "GBK", "GB2312", "936" };
        string lastError = string.Empty;
        foreach (string name in names) {
            try {
                return Encoding.GetEncoding(name);
            }
            catch (Exception ex) {
                lastError = ex.Message;
            }
        }

        XNLogger.LogWarn("Chinese SGF charset fallback to system default.", ("error", lastError));
        return Encoding.Default;
    }

    private static List<SgfNode> ParseRootMainLineNodes(string sgfText)
    {
        List<SgfNode> nodes = new List<SgfNode>();
        if (string.IsNullOrEmpty(sgfText)) {
            return nodes;
        }

        int index = 0;
        while (index < sgfText.Length && sgfText[index] != '(') {
            index += 1;
        }

        return index < sgfText.Length ? ParseGameTreeMainLine(sgfText, ref index) : nodes;
    }

    private static List<SgfNode> ParseGameTreeMainLine(string sgfText, ref int index)
    {
        List<SgfNode> nodes = new List<SgfNode>();
        if (index >= sgfText.Length || sgfText[index] != '(') {
            return nodes;
        }

        index += 1;
        while (index < sgfText.Length) {
            SkipWhitespace(sgfText, ref index);
            if (index >= sgfText.Length || sgfText[index] != ';') {
                break;
            }

            index += 1;
            nodes.Add(ParseNode(sgfText, ref index));
        }

        SkipWhitespace(sgfText, ref index);
        if (index < sgfText.Length && sgfText[index] == '(') {
            nodes.AddRange(ParseGameTreeMainLine(sgfText, ref index));
            SkipSiblingGameTrees(sgfText, ref index);
        }

        SkipWhitespace(sgfText, ref index);
        if (index < sgfText.Length && sgfText[index] == ')') {
            index += 1;
        }

        return nodes;
    }

    private static void SkipSiblingGameTrees(string sgfText, ref int index)
    {
        while (index < sgfText.Length) {
            SkipWhitespace(sgfText, ref index);
            if (index >= sgfText.Length || sgfText[index] != '(') {
                return;
            }

            SkipGameTree(sgfText, ref index);
        }
    }

    private static void SkipGameTree(string sgfText, ref int index)
    {
        int depth = 0;
        while (index < sgfText.Length) {
            char c = sgfText[index];
            if (c == '[') {
                SkipPropertyValue(sgfText, ref index);
                continue;
            }

            if (c == '(') {
                depth += 1;
                index += 1;
                continue;
            }

            if (c == ')') {
                depth -= 1;
                index += 1;
                if (depth <= 0) {
                    return;
                }

                continue;
            }

            index += 1;
        }
    }

    private static void SkipPropertyValue(string sgfText, ref int index)
    {
        index += 1;
        while (index < sgfText.Length) {
            char c = sgfText[index++];
            if (c == '\\' && index < sgfText.Length) {
                index += 1;
                continue;
            }

            if (c == ']') {
                return;
            }
        }
    }

    private static void SkipWhitespace(string sgfText, ref int index)
    {
        while (index < sgfText.Length && char.IsWhiteSpace(sgfText[index])) {
            index += 1;
        }
    }

    private static SgfNode ParseNode(string sgfText, ref int index)
    {
        SgfNode node = new SgfNode();
        while (index < sgfText.Length) {
            char c = sgfText[index];
            if (c == ';' || c == '(' || c == ')') {
                break;
            }

            if (!char.IsLetter(c)) {
                index += 1;
                continue;
            }

            int keyStart = index;
            while (index < sgfText.Length && char.IsLetter(sgfText[index])) {
                index += 1;
            }

            string key = sgfText.Substring(keyStart, index - keyStart).ToUpperInvariant();
            List<string> values = new List<string>();
            while (index < sgfText.Length && sgfText[index] == '[') {
                values.Add(ParsePropertyValue(sgfText, ref index));
            }

            if (values.Count > 0) {
                if (node.properties.TryGetValue(key, out List<string> existingValues)) {
                    existingValues.AddRange(values);
                } else {
                    node.properties[key] = values;
                }
            }
        }

        return node;
    }

    private static string ParsePropertyValue(string sgfText, ref int index)
    {
        index += 1;
        List<char> chars = new List<char>();
        while (index < sgfText.Length) {
            char c = sgfText[index++];
            if (c == '\\' && index < sgfText.Length) {
                chars.Add(sgfText[index++]);
                continue;
            }

            if (c == ']') {
                break;
            }

            chars.Add(c);
        }

        return new string(chars.ToArray()).Trim();
    }

    private static int ParseBoardSize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return DefaultBoardSize;
        }

        string sizeText = value.Trim();
        int colonIndex = sizeText.IndexOf(':');
        if (colonIndex >= 0) {
            sizeText = sizeText.Substring(0, colonIndex);
        }

        return int.TryParse(sizeText, out int boardSize)
            ? Math.Max(MinBoardSize, Math.Min(MaxBoardSize, boardSize))
            : DefaultBoardSize;
    }

    private static string ParseRules(string value)
    {
        if (KataGoDuelRecordFile.TryNormalizeRules(value, out string rules)) {
            return rules;
        }

        if (!string.IsNullOrWhiteSpace(value)) {
            XNLogger.LogWarn("Unsupported SGF rules, fallback will be used.", ("rules", value));
        }

        return KataGoDuelRecordFile.Rules;
    }

    private static float ParseKomi(string value, string rules, bool isFoxwq)
    {
        if (TryNormalizeFoxwqKomi(value, rules, isFoxwq, out float foxwqKomi)) {
            return foxwqKomi;
        }

        if (KataGoDuelRecordFile.TryNormalizeKomi(value, rules, out float komi)) {
            return komi;
        }

        if (!string.IsNullOrWhiteSpace(value)) {
            XNLogger.LogWarn(
                "Invalid SGF komi, fallback will be used.",
                ("rules", rules ?? string.Empty),
                ("komi", value),
                ("fallback", KataGoDuelRecordFile.Komi.ToString(CultureInfo.InvariantCulture)));
        }

        return KataGoDuelRecordFile.Komi;
    }

    private static bool TryNormalizeFoxwqKomi(string value, string rules, bool isFoxwq, out float komi)
    {
        komi = KataGoDuelRecordFile.Komi;
        if (!isFoxwq ||
            !IsChineseRules(rules) ||
            string.IsNullOrWhiteSpace(value) ||
            value.IndexOf('.') >= 0 ||
            value.IndexOf(',') >= 0 ||
            !int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int encodedKomi)) {
            return false;
        }

        if (encodedKomi != 0 && Math.Abs(encodedKomi) < 25) {
            return false;
        }

        if (encodedKomi % 25 != 0) {
            return false;
        }

        float normalized = encodedKomi / 50f;
        if (!KataGoDuelRecordFile.IsKataGoKomiValid(normalized)) {
            return false;
        }

        komi = normalized;
        return true;
    }

    private static bool IsFoxwqSgf(SgfNode root)
    {
        return root != null && root.HasValueContaining("AP", "foxwq");
    }

    private static bool IsChineseRules(string rules)
    {
        return string.Equals(rules, "chinese", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rules, "chinese-ogs", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseNonNegativeInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? Math.Max(result, 0)
            : 0;
    }

    private static void AppendSetupStones(JArray stones, IEnumerable<string> points, string color, int boardSize)
    {
        foreach (string point in points) {
            if (TryConvertSgfPoint(point, boardSize, out string kataGoPoint, out bool isPass) && !isPass) {
                stones.Add(new JArray(color, kataGoPoint));
            }
        }
    }

    private static bool TryAddMove(JArray moves, string color, string sgfPoint, int boardSize, out string message)
    {
        message = string.Empty;
        if (!TryConvertSgfPoint(sgfPoint, boardSize, out string kataGoPoint, out bool isPass)) {
            message = $"SGF 含有无效落点：{sgfPoint}";
            return false;
        }

        moves.Add(new JArray(color, isPass ? KataGoPositionJsonBuilder.PassPoint : kataGoPoint));
        return true;
    }

    private static bool TryConvertSgfPoint(string sgfPoint, int boardSize, out string kataGoPoint, out bool isPass)
    {
        kataGoPoint = string.Empty;
        isPass = false;

        if (string.IsNullOrEmpty(sgfPoint)) {
            isPass = true;
            return true;
        }

        string point = sgfPoint.Trim();
        if (point.Length < 2) {
            return false;
        }

        int x = char.ToLowerInvariant(point[0]) - 'a';
        int z = char.ToLowerInvariant(point[1]) - 'a';
        if (x >= boardSize && z >= boardSize) {
            isPass = true;
            return true;
        }

        if (x < 0 || x >= boardSize || z < 0 || z >= boardSize) {
            return false;
        }

        kataGoPoint = KataGoPositionJsonBuilder.ToKataGoPoint(new RectCoordinates(x, z), boardSize);
        return true;
    }

    private static JObject BuildRecordJson(SgfGame sgfGame, string gameId)
    {
        return new JObject
        {
            ["id"] = $"sgf-import-{gameId}",
            ["rules"] = sgfGame.rules,
            ["komi"] = sgfGame.komi,
            ["boardXSize"] = sgfGame.boardSize,
            ["boardYSize"] = sgfGame.boardSize,
            ["maxVisits"] = KataGoDuelRecordFile.DefaultMaxVisits,
            ["includeOwnership"] = true,
            ["includePolicy"] = false,
            ["initialStones"] = sgfGame.initialStones,
            ["handicapCount"] = ResolveHandicapCount(sgfGame),
            ["moves"] = sgfGame.moves,
        };
    }

    private static JObject BuildSaveInfoJson(SgfGame sgfGame, string gameId, string nowUtc)
    {
        string winnerFlag = ResolveWinnerFlag(sgfGame.result);
        return new JObject
        {
            ["saveSlotIndex"] = -1,
            ["savedAtUtc"] = nowUtc,
            ["gameId"] = gameId,
            ["createdAtUtc"] = nowUtc,
            ["lastUpdatedAtUtc"] = nowUtc,
            ["archivedAtUtc"] = nowUtc,
            ["moveCount"] = sgfGame.moves.Count,
            ["isCompleted"] = true,
            ["isArchived"] = true,
            ["sourceType"] = "sgf",
            ["winnerFlag"] = winnerFlag,
            ["finalScore"] = sgfGame.result,
            ["resultType"] = "sgf",
            ["rules"] = sgfGame.rules,
            ["komi"] = sgfGame.komi,
            ["players"] = new JObject
            {
                ["black"] = BuildPlayerInfoJson("Player1", "black", sgfGame.blackName),
                ["white"] = BuildPlayerInfoJson("Player2", "white", sgfGame.whiteName),
            },
            ["board"] = new JObject
            {
                ["cfgId"] = $"{sgfGame.boardSize}x{sgfGame.boardSize}",
                ["boardSize"] = sgfGame.boardSize,
            },
            ["timeControl"] = new JObject(),
            ["handicap"] = new JObject
            {
                ["handicapCount"] = ResolveHandicapCount(sgfGame),
                ["initialStoneCount"] = sgfGame.initialStones.Count,
            },
        };
    }

    private static JObject BuildPlayerInfoJson(string flag, string stone, string displayName)
    {
        return new JObject
        {
            ["flag"] = flag,
            ["stone"] = stone,
            ["guid"] = string.Empty,
            ["displayName"] = displayName,
        };
    }

    private static string ResolveWinnerFlag(string result)
    {
        if (string.IsNullOrWhiteSpace(result)) {
            return string.Empty;
        }

        string trimmed = result.Trim();
        if (trimmed.StartsWith("B+", StringComparison.OrdinalIgnoreCase)) {
            return "Player1";
        }

        if (trimmed.StartsWith("W+", StringComparison.OrdinalIgnoreCase)) {
            return "Player2";
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int ResolveHandicapCount(SgfGame sgfGame)
    {
        if (sgfGame == null) {
            return 0;
        }

        return sgfGame.handicapCount > 0
            ? sgfGame.handicapCount
            : Math.Max(sgfGame.initialStones.Count, 0);
    }

    private static string CreateGameId()
    {
        return $"sgf-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 45);
    }

    private sealed class SgfGame
    {
        public int boardSize;
        public string rules;
        public float komi;
        public int handicapCount;
        public string blackName;
        public string whiteName;
        public string result;
        public readonly JArray initialStones = new JArray();
        public readonly JArray moves = new JArray();
    }

    private sealed class SgfNode
    {
        public readonly Dictionary<string, List<string>> properties = new Dictionary<string, List<string>>();

        public string GetFirst(string key)
        {
            return properties.TryGetValue(key, out List<string> values) && values.Count > 0
                ? values[0]
                : string.Empty;
        }

        public IEnumerable<string> GetValues(string key)
        {
            return properties.TryGetValue(key, out List<string> values)
                ? values
                : Array.Empty<string>();
        }

        public bool TryGetMove(string key, out string value)
        {
            value = string.Empty;
            if (!properties.TryGetValue(key, out List<string> values) || values.Count <= 0) {
                return false;
            }

            value = values[0];
            return true;
        }

        public bool HasValueContaining(string key, string text)
        {
            if (string.IsNullOrEmpty(text) || !properties.TryGetValue(key, out List<string> values)) {
                return false;
            }

            foreach (string value in values) {
                if (value != null && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }

            return false;
        }
    }
}
