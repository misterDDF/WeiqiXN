using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public static class DuelSgfReplayExporter
{
    private const string DefaultBlackName = "Black";
    private const string DefaultWhiteName = "White";
    private const string DefaultReplayFilePrefix = "replay";

    public static string BuildDefaultFileName(string gameId)
    {
        JObject saveInfoJson = TryLoadSaveInfo(gameId, out JObject loadedSaveInfo) ? loadedSaveInfo : null;
        string blackName = SanitizeFileNamePart(ResolvePlayerName(saveInfoJson, "black", DefaultBlackName));
        string whiteName = SanitizeFileNamePart(ResolvePlayerName(saveInfoJson, "white", DefaultWhiteName));
        string timestamp = ResolveFileTimestamp(saveInfoJson);
        string idPart = SanitizeFileNamePart(gameId);

        StringBuilder builder = new StringBuilder();
        builder.Append(string.IsNullOrEmpty(blackName) ? DefaultBlackName : blackName);
        builder.Append('-');
        builder.Append(string.IsNullOrEmpty(whiteName) ? DefaultWhiteName : whiteName);
        builder.Append('-');
        builder.Append(timestamp);
        if (!string.IsNullOrEmpty(idPart)) {
            builder.Append('-');
            builder.Append(idPart);
        }

        builder.Append(".sgf");
        return builder.ToString();
    }

    public static bool TryExport(SceneComponentReplay compReplay, string gameId, string sgfFilePath, out string message)
    {
        message = string.Empty;

#if UNITY_WEBGL && !UNITY_EDITOR
        message = "当前平台不支持导出棋谱。";
        return false;
#else
        if (compReplay == null || !compReplay.isReplayLoaded) {
            message = "复盘记录尚未加载。";
            return false;
        }

        if (compReplay.isFreeLayout) {
            message = "自由布局不支持导出棋谱。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sgfFilePath)) {
            message = "请选择有效的导出路径。";
            return false;
        }

        try {
            JObject saveInfoJson = TryLoadSaveInfo(gameId, out JObject loadedSaveInfo) ? loadedSaveInfo : null;
            string sgfText = BuildSgf(compReplay, saveInfoJson);
            string dirPath = Path.GetDirectoryName(sgfFilePath);
            if (!string.IsNullOrEmpty(dirPath)) {
                Directory.CreateDirectory(dirPath);
            }

            File.WriteAllText(sgfFilePath, sgfText, new UTF8Encoding(false));
            XNLogger.LogInfo("SGF replay export success.", ("filePath", sgfFilePath), ("gameId", gameId ?? string.Empty));
            message = "棋谱导出成功。";
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("SGF replay export failed.", ("filePath", sgfFilePath), ("gameId", gameId ?? string.Empty), ("err", ex.Message));
            message = $"棋谱导出失败：{ex.Message}";
            return false;
        }
#endif
    }

    private static bool TryLoadSaveInfo(string gameId, out JObject saveInfoJson)
    {
        saveInfoJson = null;
        if (string.IsNullOrWhiteSpace(gameId)) {
            return false;
        }

        string saveInfoPath = GameSaveConfig.GetReplayDuelSaveInfoPath(gameId);
        if (!File.Exists(saveInfoPath)) {
            return false;
        }

        try {
            saveInfoJson = JObject.Parse(File.ReadAllText(saveInfoPath));
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogWarn("Replay save info load failed for SGF export.", ("gameId", gameId), ("error", ex.Message));
            saveInfoJson = null;
            return false;
        }
    }

    private static string BuildSgf(SceneComponentReplay compReplay, JObject saveInfoJson)
    {
        StringBuilder builder = new StringBuilder(4096);
        builder.Append("(;");
        AppendProperty(builder, "GM", "1");
        AppendProperty(builder, "FF", "4");
        AppendProperty(builder, "CA", "UTF-8");
        AppendProperty(builder, "AP", "WeiqiXN");
        AppendProperty(builder, "SZ", compReplay.replayBoardSize.ToString(CultureInfo.InvariantCulture));
        AppendProperty(builder, "RU", ResolveSgfRules(compReplay.replayRules));
        AppendProperty(builder, "KM", compReplay.replayKomi.ToString("0.###", CultureInfo.InvariantCulture));
        AppendProperty(builder, "PB", ResolvePlayerName(saveInfoJson, "black", DefaultBlackName));
        AppendProperty(builder, "PW", ResolvePlayerName(saveInfoJson, "white", DefaultWhiteName));

        string result = saveInfoJson?["finalScore"]?.ToString();
        if (!string.IsNullOrWhiteSpace(result)) {
            AppendProperty(builder, "RE", result.Trim());
        }

        int handicapCount = compReplay.replayHandicapCount > 0
            ? compReplay.replayHandicapCount
            : compReplay.replayInitialStones.Count;
        if (handicapCount > 0) {
            AppendProperty(builder, "HA", handicapCount.ToString(CultureInfo.InvariantCulture));
        }

        AppendSetupStones(builder, "AB", compReplay, PlayerFlag.Player1);
        AppendSetupStones(builder, "AW", compReplay, PlayerFlag.Player2);

        foreach (ReplayMoveState move in compReplay.replayMoves) {
            if (move == null) {
                continue;
            }

            string color = move.playerFlag == PlayerFlag.Player1 ? "B" : "W";
            builder.Append(';');
            AppendProperty(builder, color, move.isPass ? string.Empty : ToSgfPoint(move.coords, compReplay.replayBoardSize));
        }

        builder.Append(')');
        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendSetupStones(StringBuilder builder, string propertyName, SceneComponentReplay compReplay, PlayerFlag playerFlag)
    {
        bool hasProperty = false;
        foreach (ReplayMoveState stone in compReplay.replayInitialStones) {
            if (stone == null || stone.isPass || stone.coords == null || stone.playerFlag != playerFlag) {
                continue;
            }

            if (!hasProperty) {
                builder.Append(propertyName);
                hasProperty = true;
            }

            builder.Append('[');
            builder.Append(EscapePropertyValue(ToSgfPoint(stone.coords, compReplay.replayBoardSize)));
            builder.Append(']');
        }
    }

    private static void AppendProperty(StringBuilder builder, string propertyName, string value)
    {
        builder.Append(propertyName);
        builder.Append('[');
        builder.Append(EscapePropertyValue(value));
        builder.Append(']');
    }

    private static string ResolveSgfRules(string rules)
    {
        switch (rules) {
            case "japanese":
                return "Japanese";
            case "aga":
                return "AGA";
            case "korean":
                return "Korean";
            case "new-zealand":
                return "New Zealand";
            case "tromp-taylor":
                return "Tromp-Taylor";
            case "chinese-ogs":
            case "stone-scoring":
            case "chinese":
            default:
                return "Chinese";
        }
    }

    private static string ResolvePlayerName(JObject saveInfoJson, string side, string fallback)
    {
        string displayName = saveInfoJson?["players"]?[side]?["displayName"]?.ToString();
        return string.IsNullOrWhiteSpace(displayName) ? fallback : displayName.Trim();
    }

    private static string ResolveFileTimestamp(JObject saveInfoJson)
    {
        string timestampText = FirstNonEmpty(
            saveInfoJson?["archivedAtUtc"]?.ToString(),
            saveInfoJson?["lastUpdatedAtUtc"]?.ToString(),
            saveInfoJson?["createdAtUtc"]?.ToString(),
            saveInfoJson?["savedAtUtc"]?.ToString());
        if (!string.IsNullOrWhiteSpace(timestampText) &&
            DateTimeOffset.TryParse(
                timestampText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed)) {
            return parsed.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        return DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null) {
            return string.Empty;
        }

        foreach (string value in values) {
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string SanitizeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim()) {
            if (Array.IndexOf(invalidChars, c) >= 0 || c == '-') {
                builder.Append('_');
                continue;
            }

            builder.Append(c);
        }

        string normalized = builder.ToString().Trim();
        return string.IsNullOrEmpty(normalized) ? DefaultReplayFilePrefix : normalized;
    }

    private static string ToSgfPoint(RectCoordinates coords, int boardSize)
    {
        if (coords == null || boardSize <= 0 || coords.x < 0 || coords.x >= boardSize || coords.z < 0 || coords.z >= boardSize) {
            throw new ArgumentException("Replay move contains invalid coordinates.");
        }

        return $"{(char)('a' + coords.x)}{(char)('a' + coords.z)}";
    }

    private static string EscapePropertyValue(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("]", "\\]")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", "\\\n");
    }
}
