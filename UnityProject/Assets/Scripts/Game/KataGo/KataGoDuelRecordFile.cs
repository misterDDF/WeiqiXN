using System;
using System.IO;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public static class KataGoDuelRecordFile
{
    public const string Rules = "chinese";
    public const float Komi = 7.5f;
    public const int DefaultMaxVisits = 16;

    public static JObject BuildRecordJson(DuelScene duelScene, string requestId = "duel-record")
    {
        JObject recordJson = KataGoPositionJsonBuilder.BuildAnalysisJsonWithMoveHistory(
            duelScene,
            requestId,
            DefaultMaxVisits
        );

        recordJson["includeOwnership"] = true;
        recordJson["includePolicy"] = false;
        return recordJson;
    }

    public static bool Save(DuelScene duelScene, string filePath)
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

            JObject recordJson = BuildRecordJson(duelScene);
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

        return float.TryParse(recordJson["komi"].ToString(), out komi);
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
