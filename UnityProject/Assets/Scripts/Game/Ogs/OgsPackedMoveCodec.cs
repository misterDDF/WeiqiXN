using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;

public static class OgsPackedMoveCodec
{
    private const string CoordinateSequence = "abcdefghijklmnopqrstuvwxyz";
    public const string PassMove = "..";

    public static string Encode(RectCoordinates coords)
    {
        return Encode(coords, 0);
    }

    public static string Encode(RectCoordinates coords, int boardSize)
    {
        if (coords == null) {
            return PassMove;
        }

        return EncodeCoordinate(coords.x) + EncodeCoordinate(coords.z);
    }

    public static bool TryParseMoves(JToken movesToken, int boardSize, out List<OgsDuelMove> moves)
    {
        return TryParseMoves(movesToken, boardSize, PlayerFlag.Player1, out moves);
    }

    public static bool TryParseMoves(JToken movesToken, int boardSize, PlayerFlag firstMovePlayerFlag, out List<OgsDuelMove> moves)
    {
        return TryParseMoves(movesToken, boardSize, firstMovePlayerFlag, 0, out moves);
    }

    public static bool TryParseMoves(JToken movesToken, int boardSize, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount, out List<OgsDuelMove> moves)
    {
        moves = new List<OgsDuelMove>();
        firstMovePlayerFlag = DuelUtils.GetValidPlayerFlag(firstMovePlayerFlag);
        openingSameColorMoveCount = Math.Max(0, openingSameColorMoveCount);
        if (movesToken == null || movesToken.Type == JTokenType.Null) {
            return true;
        }

        if (movesToken.Type == JTokenType.String) {
            return TryParsePackedMoveString(movesToken.ToString(), boardSize, firstMovePlayerFlag, openingSameColorMoveCount, moves);
        }

        if (movesToken is JArray moveArray) {
            for (int i = 0; i < moveArray.Count; i++) {
                if (!TryParseMoveToken(moveArray[i], boardSize, i + 1, firstMovePlayerFlag, openingSameColorMoveCount, out OgsDuelMove move)) {
                    return false;
                }
                moves.Add(move);
            }
            return true;
        }

        if (TryParseMoveToken(movesToken, boardSize, 1, firstMovePlayerFlag, openingSameColorMoveCount, out OgsDuelMove singleMove)) {
            moves.Add(singleMove);
            return true;
        }

        return false;
    }

    public static bool TryParseIncrementalMove(JToken moveToken, int boardSize, int moveNumber, out OgsDuelMove move)
    {
        return TryParseIncrementalMove(moveToken, boardSize, moveNumber, PlayerFlag.Player1, out move);
    }

    public static bool TryParseIncrementalMove(JToken moveToken, int boardSize, int moveNumber, PlayerFlag firstMovePlayerFlag, out OgsDuelMove move)
    {
        return TryParseIncrementalMove(moveToken, boardSize, moveNumber, firstMovePlayerFlag, 0, out move);
    }

    public static bool TryParseIncrementalMove(JToken moveToken, int boardSize, int moveNumber, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount, out OgsDuelMove move)
    {
        return TryParseMoveToken(moveToken, boardSize, moveNumber, DuelUtils.GetValidPlayerFlag(firstMovePlayerFlag), Math.Max(0, openingSameColorMoveCount), out move);
    }

    public static bool TryParseInitialStones(JToken initialStateToken, int boardSize, out List<OgsDuelInitialStone> stones)
    {
        stones = new List<OgsDuelInitialStone>();
        if (initialStateToken == null || initialStateToken.Type == JTokenType.Null) {
            return true;
        }

        return TryParseInitialStoneCollection(initialStateToken, boardSize, 0, stones);
    }

    private static bool TryParsePackedMoveString(string packedMoves, int boardSize, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount, List<OgsDuelMove> moves)
    {
        if (string.IsNullOrEmpty(packedMoves)) {
            return true;
        }

        int index = 0;
        while (index < packedMoves.Length) {
            if (char.IsLetter(packedMoves[index]) && index + 1 < packedMoves.Length && char.IsDigit(packedMoves[index + 1])) {
                return TryParsePrettyMoveString(packedMoves.Substring(index), boardSize, firstMovePlayerFlag, openingSameColorMoveCount, moves);
            }

            if (index + 1 >= packedMoves.Length) {
                return false;
            }

            string packedMove = packedMoves.Substring(index, 2);
            int moveNumber = moves.Count + 1;
            if (!TryParsePackedPair(packedMove, boardSize, moveNumber, ResolvePlayerFlagForMoveNumber(moveNumber, firstMovePlayerFlag, openingSameColorMoveCount), out OgsDuelMove move)) {
                return false;
            }
            moves.Add(move);
            index += 2;
        }

        return true;
    }

    private static bool TryParsePrettyMoveString(string prettyMoves, int boardSize, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount, List<OgsDuelMove> moves)
    {
        string[] parts = prettyMoves.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts) {
            if (!TryParsePrettyMove(part, boardSize, moves.Count + 1, firstMovePlayerFlag, openingSameColorMoveCount, out OgsDuelMove move)) {
                return false;
            }
            moves.Add(move);
        }
        return true;
    }

    private static bool TryParseMoveToken(JToken moveToken, int boardSize, int moveNumber, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount, out OgsDuelMove move)
    {
        move = null;
        if (moveToken == null || moveToken.Type == JTokenType.Null) {
            return false;
        }

        PlayerFlag playerFlag = ResolvePlayerFlagFromMoveToken(moveToken, moveNumber, firstMovePlayerFlag, openingSameColorMoveCount);
        if (moveToken.Type == JTokenType.String) {
            string text = moveToken.ToString();
            if (text.Length == 2 && !char.IsDigit(text[1])) {
                return TryParsePackedPair(text, boardSize, moveNumber, playerFlag, out move);
            }
            return TryParsePrettyMove(text, boardSize, moveNumber, firstMovePlayerFlag, openingSameColorMoveCount, out move);
        }

        if (moveToken is JArray array) {
            if (array.Count < 2) {
                return false;
            }

            int x = ReadInt(array[0], -1);
            int y = ReadInt(array[1], -1);
            move = BuildMove(x, y, boardSize, moveNumber, playerFlag);
            return move != null;
        }

        if (moveToken is JObject obj) {
            int x = ReadFirstInt(obj, -1, "x", "i");
            int y = ReadFirstInt(obj, -1, "y", "j");
            if (x < 0 && y < 0) {
                string packed = ReadFirstString(obj, "move", "coords", "coordinate");
                if (!string.IsNullOrEmpty(packed)) {
                    if (packed.Length == 2 && !char.IsDigit(packed[1])) {
                        return TryParsePackedPair(packed, boardSize, moveNumber, playerFlag, out move);
                    }

                    return TryParsePrettyMoveWithPlayerFlag(packed, boardSize, moveNumber, playerFlag, out move);
                }
            }

            move = BuildMove(x, y, boardSize, moveNumber, playerFlag);
            return move != null;
        }

        return false;
    }

    private static bool TryParseInitialStoneCollection(JToken token, int boardSize, PlayerFlag defaultPlayerFlag, List<OgsDuelInitialStone> stones)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return true;
        }

        if (token is JObject obj) {
            bool hasColorFields = false;
            hasColorFields |= TryParseInitialStonesForColorField(obj, boardSize, PlayerFlag.Player1, stones, "black", "Black", "b", "B");
            hasColorFields |= TryParseInitialStonesForColorField(obj, boardSize, PlayerFlag.Player2, stones, "white", "White", "w", "W");
            if (hasColorFields) {
                return true;
            }

            foreach (string nestedField in new[] { "initial_state", "initialState", "initial_stones", "initialStones", "stones" }) {
                if (obj.TryGetValue(nestedField, out JToken nestedToken)) {
                    return TryParseInitialStoneCollection(nestedToken, boardSize, defaultPlayerFlag, stones);
                }
            }

            if (TryParseInitialStoneToken(obj, boardSize, defaultPlayerFlag, out OgsDuelInitialStone objStone)) {
                stones.Add(objStone);
                return true;
            }

            return false;
        }

        if (token is JArray array) {
            if (TryParseInitialStoneToken(array, boardSize, defaultPlayerFlag, out OgsDuelInitialStone arrayStone)) {
                stones.Add(arrayStone);
                return true;
            }

            foreach (JToken item in array) {
                if (!TryParseInitialStoneCollection(item, boardSize, defaultPlayerFlag, stones)) {
                    return false;
                }
            }
            return true;
        }

        if (token.Type == JTokenType.String) {
            return TryParseInitialStoneString(token.ToString(), boardSize, defaultPlayerFlag, stones);
        }

        return false;
    }

    private static bool TryParseInitialStonesForColorField(JObject obj, int boardSize, PlayerFlag playerFlag, List<OgsDuelInitialStone> stones, params string[] fieldNames)
    {
        foreach (string fieldName in fieldNames) {
            if (obj.TryGetValue(fieldName, out JToken token)) {
                return TryParseInitialStoneCollection(token, boardSize, playerFlag, stones);
            }
        }

        return false;
    }

    private static bool TryParseInitialStoneString(string text, int boardSize, PlayerFlag defaultPlayerFlag, List<OgsDuelInitialStone> stones)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            return true;
        }

        if (defaultPlayerFlag == 0) {
            return TryParseInitialStoneToken(new JValue(text), boardSize, defaultPlayerFlag, out OgsDuelInitialStone stone) &&
                AddInitialStoneIfValid(stone, stones);
        }

        string trimmed = text.Trim();
        bool looksPacked = trimmed.Length % 2 == 0 && trimmed.IndexOfAny(new[] { ' ', ',', ';' }) < 0;
        if (looksPacked) {
            for (int index = 0; index < trimmed.Length; index += 2) {
                if (!TryParseInitialStonePoint(trimmed.Substring(index, 2), boardSize, out RectCoordinates coords)) {
                    return false;
                }
                stones.Add(new OgsDuelInitialStone(defaultPlayerFlag, coords));
            }
            return true;
        }

        string[] parts = trimmed.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts) {
            if (!TryParseInitialStonePoint(part, boardSize, out RectCoordinates coords)) {
                return false;
            }
            stones.Add(new OgsDuelInitialStone(defaultPlayerFlag, coords));
        }
        return true;
    }

    private static bool TryParseInitialStoneToken(JToken token, int boardSize, PlayerFlag defaultPlayerFlag, out OgsDuelInitialStone stone)
    {
        stone = null;
        if (token == null || token.Type == JTokenType.Null) {
            return false;
        }

        if (token.Type == JTokenType.String) {
            string text = token.ToString();
            PlayerFlag playerFlag = defaultPlayerFlag;
            string point = text;
            string[] parts = text.Split(new[] { ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && TryResolvePlayerFlagFromText(parts[0], out PlayerFlag textFlag)) {
                playerFlag = textFlag;
                point = parts[1];
            }

            if (playerFlag == 0 || !TryParseInitialStonePoint(point, boardSize, out RectCoordinates coords)) {
                return false;
            }

            stone = new OgsDuelInitialStone(playerFlag, coords);
            return true;
        }

        if (token is JArray array) {
            if (array.Count < 2) {
                return false;
            }

            if (TryResolvePlayerFlagFromText(array[0]?.ToString(), out PlayerFlag textFlag)) {
                if (!TryParseInitialStonePoint(array[1]?.ToString(), boardSize, out RectCoordinates coords)) {
                    return false;
                }
                stone = new OgsDuelInitialStone(textFlag, coords);
                return true;
            }

            int x = ReadInt(array[0], -1);
            int y = ReadInt(array[1], -1);
            PlayerFlag playerFlag = defaultPlayerFlag;
            if (array.Count >= 3) {
                if (!TryResolvePlayerFlagFromText(array[2]?.ToString(), out playerFlag)) {
                    playerFlag = ResolvePlayerFlagFromNumber(ReadInt(array[2], 0));
                }
            }
            stone = BuildInitialStone(x, y, boardSize, playerFlag);
            return stone != null;
        }

        if (token is JObject obj) {
            PlayerFlag playerFlag = defaultPlayerFlag;
            if (TryResolvePlayerFlagFromText(ReadFirstString(obj, "color", "player", "stone_color"), out PlayerFlag textFlag)) {
                playerFlag = textFlag;
            } else {
                PlayerFlag numericFlag = ResolvePlayerFlagFromNumber(ReadFirstInt(obj, 0, "color", "player"));
                if (numericFlag != 0) {
                    playerFlag = numericFlag;
                }
            }

            string point = ReadFirstString(obj, "point", "move", "coords", "coordinate");
            if (!string.IsNullOrWhiteSpace(point)) {
                if (playerFlag == 0 || !TryParseInitialStonePoint(point, boardSize, out RectCoordinates coords)) {
                    return false;
                }
                stone = new OgsDuelInitialStone(playerFlag, coords);
                return true;
            }

            int x = ReadFirstInt(obj, -1, "x", "i");
            int y = ReadFirstInt(obj, -1, "y", "j");
            stone = BuildInitialStone(x, y, boardSize, playerFlag);
            return stone != null;
        }

        return false;
    }

    private static bool TryParseInitialStonePoint(string point, int boardSize, out RectCoordinates coords)
    {
        coords = null;
        if (string.IsNullOrWhiteSpace(point) || point == PassMove) {
            return false;
        }

        string trimmed = point.Trim();
        if (trimmed.Length == 2 && !char.IsDigit(trimmed[1])) {
            int x = DecodeCoordinate(trimmed[0]);
            int y = DecodeCoordinate(trimmed[1]);
            if (x >= 0 && y >= 0 && x < boardSize && y < boardSize) {
                coords = new RectCoordinates(x, y);
                return true;
            }
            return false;
        }

        return KataGoPositionJsonBuilder.TryParseKataGoPoint(trimmed, boardSize, out coords);
    }

    private static OgsDuelInitialStone BuildInitialStone(int x, int y, int boardSize, PlayerFlag playerFlag)
    {
        if (playerFlag == 0 || x < 0 || y < 0 || x >= boardSize || y >= boardSize) {
            return null;
        }

        return new OgsDuelInitialStone(playerFlag, new RectCoordinates(x, y));
    }

    private static bool AddInitialStoneIfValid(OgsDuelInitialStone stone, List<OgsDuelInitialStone> stones)
    {
        if (stone == null) {
            return false;
        }

        stones.Add(stone);
        return true;
    }

    private static bool TryParsePackedPair(string packedMove, int boardSize, int moveNumber, PlayerFlag playerFlag, out OgsDuelMove move)
    {
        move = null;
        if (packedMove == PassMove) {
            move = new OgsDuelMove(playerFlag, null, true, moveNumber);
            return true;
        }

        if (string.IsNullOrEmpty(packedMove) || packedMove.Length != 2) {
            return false;
        }

        int x = DecodeCoordinate(packedMove[0]);
        int y = DecodeCoordinate(packedMove[1]);
        move = BuildMove(x, y, boardSize, moveNumber, playerFlag);
        return move != null;
    }

    private static bool TryParsePrettyMove(string text, int boardSize, int moveNumber, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount, out OgsDuelMove move)
    {
        if (string.Equals(text, "pass", StringComparison.OrdinalIgnoreCase) || text == PassMove) {
            return TryParsePrettyMoveWithPlayerFlag(text, boardSize, moveNumber, ResolvePlayerFlagForMoveNumber(moveNumber, firstMovePlayerFlag, openingSameColorMoveCount), out move);
        }

        return TryParsePrettyMoveWithPlayerFlag(text, boardSize, moveNumber, ResolvePlayerFlagForMoveNumber(moveNumber, firstMovePlayerFlag, openingSameColorMoveCount), out move);
    }

    private static bool TryParsePrettyMoveWithPlayerFlag(string text, int boardSize, int moveNumber, PlayerFlag playerFlag, out OgsDuelMove move)
    {
        move = null;
        if (playerFlag == 0) {
            return false;
        }

        if (string.Equals(text, "pass", StringComparison.OrdinalIgnoreCase) || text == PassMove) {
            move = new OgsDuelMove(playerFlag, null, true, moveNumber);
            return true;
        }

        if (!KataGoPositionJsonBuilder.TryParseKataGoPoint(text, boardSize, out RectCoordinates coords)) {
            return false;
        }

        move = new OgsDuelMove(playerFlag, coords, false, moveNumber);
        return true;
    }

    private static OgsDuelMove BuildMove(int x, int y, int boardSize, int moveNumber, PlayerFlag playerFlag)
    {
        if (x < 0 && y < 0) {
            return new OgsDuelMove(playerFlag, null, true, moveNumber);
        }

        if (x < 0 || y < 0 || x >= boardSize || y >= boardSize) {
            return null;
        }

        return new OgsDuelMove(playerFlag, new RectCoordinates(x, y), false, moveNumber);
    }

    private static string EncodeCoordinate(int coordinate)
    {
        if (coordinate < 0) {
            return ".";
        }

        if (coordinate >= CoordinateSequence.Length) {
            throw new ArgumentOutOfRangeException(nameof(coordinate), coordinate, "OGS coordinate is out of range.");
        }

        return CoordinateSequence[coordinate].ToString();
    }

    private static int DecodeCoordinate(char coordinate)
    {
        if (coordinate == '.') {
            return -1;
        }

        return CoordinateSequence.IndexOf(char.ToLowerInvariant(coordinate));
    }

    private static PlayerFlag ResolvePlayerFlagFromMoveToken(JToken moveToken, int moveNumber, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount)
    {
        if (moveToken is JArray array && array.Count >= 4) {
            PlayerFlag flag = ResolvePlayerFlagFromNumber(ReadInt(array[3], 0));
            if (flag != 0) {
                return flag;
            }
        }

        if (moveToken is JObject obj) {
            string color = ReadFirstString(obj, "color", "player", "move_color");
            if (TryResolvePlayerFlagFromText(color, out PlayerFlag textFlag)) {
                return textFlag;
            }

            PlayerFlag numericFlag = ResolvePlayerFlagFromNumber(ReadFirstInt(obj, 0, "color", "player"));
            if (numericFlag != 0) {
                return numericFlag;
            }
        }

        return ResolvePlayerFlagForMoveNumber(moveNumber, firstMovePlayerFlag, openingSameColorMoveCount);
    }

    private static PlayerFlag ResolvePlayerFlagForMoveNumber(int moveNumber, PlayerFlag firstMovePlayerFlag, int openingSameColorMoveCount)
    {
        firstMovePlayerFlag = DuelUtils.GetValidPlayerFlag(firstMovePlayerFlag);
        openingSameColorMoveCount = Math.Max(0, openingSameColorMoveCount);
        if (openingSameColorMoveCount > 0) {
            if (moveNumber <= openingSameColorMoveCount) {
                return firstMovePlayerFlag;
            }

            int postOpeningMoveNumber = moveNumber - openingSameColorMoveCount;
            PlayerFlag postOpeningFirstPlayerFlag = firstMovePlayerFlag.GetOpponentPlayerFlag();
            return postOpeningMoveNumber % 2 == 1
                ? postOpeningFirstPlayerFlag
                : firstMovePlayerFlag;
        }

        return moveNumber % 2 == 1 ? firstMovePlayerFlag : firstMovePlayerFlag.GetOpponentPlayerFlag();
    }

    private static PlayerFlag ResolvePlayerFlagFromNumber(int value)
    {
        if (value == 1) {
            return PlayerFlag.Player1;
        }
        if (value == 2) {
            return PlayerFlag.Player2;
        }
        return 0;
    }

    private static bool TryResolvePlayerFlagFromText(string value, out PlayerFlag playerFlag)
    {
        playerFlag = 0;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        if (string.Equals(value, "black", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "b", StringComparison.OrdinalIgnoreCase)) {
            playerFlag = PlayerFlag.Player1;
            return true;
        }
        if (string.Equals(value, "white", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "w", StringComparison.OrdinalIgnoreCase)) {
            playerFlag = PlayerFlag.Player2;
            return true;
        }

        return false;
    }

    private static int ReadInt(JToken token, int defaultValue)
    {
        return token != null && int.TryParse(token.ToString(), out int value) ? value : defaultValue;
    }

    private static int ReadFirstInt(JObject obj, int defaultValue, params string[] fieldNames)
    {
        if (obj == null || fieldNames == null) {
            return defaultValue;
        }

        foreach (string fieldName in fieldNames) {
            if (obj.TryGetValue(fieldName, out JToken token) && int.TryParse(token.ToString(), out int value)) {
                return value;
            }
        }

        return defaultValue;
    }

    private static string ReadFirstString(JObject obj, params string[] fieldNames)
    {
        if (obj == null || fieldNames == null) {
            return string.Empty;
        }

        foreach (string fieldName in fieldNames) {
            if (obj.TryGetValue(fieldName, out JToken token)) {
                string value = token?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    return value;
                }
            }
        }

        return string.Empty;
    }
}
