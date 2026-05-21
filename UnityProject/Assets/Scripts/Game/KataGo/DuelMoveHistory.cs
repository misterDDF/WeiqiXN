using System;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;

public static class DuelMoveHistory
{
    public static JArray CreateEmpty()
    {
        return new JArray();
    }

    public static int Count(JArray moves)
    {
        return moves?.Count ?? 0;
    }

    public static JArray Clone(JArray moves)
    {
        return Clone(moves, Count(moves));
    }

    public static JArray Clone(JArray moves, int count)
    {
        JArray clonedMoves = new JArray();
        if (moves == null) {
            return clonedMoves;
        }

        int safeCount = Math.Min(Math.Max(count, 0), moves.Count);
        for (int i = 0; i < safeCount; i++) {
            clonedMoves.Add(moves[i].DeepClone());
        }

        return clonedMoves;
    }

    public static JArray TakeAfterRemovingLast(JArray moves, int removeCount)
    {
        return Clone(moves, Count(moves) - Math.Max(removeCount, 0));
    }

    public static void AppendMove(JArray moves, PlayerFlag playerFlag, RectCoordinates coords, int boardSize)
    {
        if (moves == null) {
            return;
        }

        moves.Add(new JArray(
            KataGoPositionJsonBuilder.ToKataGoColor(playerFlag),
            KataGoPositionJsonBuilder.ToKataGoPoint(coords, boardSize)
        ));
    }

    public static void AppendPass(JArray moves, PlayerFlag playerFlag)
    {
        if (moves == null) {
            return;
        }

        moves.Add(new JArray(
            KataGoPositionJsonBuilder.ToKataGoColor(playerFlag),
            KataGoPositionJsonBuilder.PassPoint
        ));
    }

    public static void RemoveLast(JArray moves)
    {
        if (moves != null && moves.Count > 0) {
            moves.RemoveAt(moves.Count - 1);
        }
    }

    public static JArray BuildKataGoMovesArray(JArray moves)
    {
        JArray result = new JArray();
        if (moves == null) {
            return result;
        }

        foreach (JToken moveToken in moves) {
            JArray move = moveToken as JArray;
            if (move != null && move.Count >= 2) {
                result.Add(new JArray(move[0]?.ToString(), move[1]?.ToString()));
            }
        }

        return result;
    }

    public static int CountTrailingPasses(JArray moves)
    {
        if (moves == null) {
            return 0;
        }

        int count = 0;
        for (int i = moves.Count - 1; i >= 0; i--) {
            JArray move = moves[i] as JArray;
            if (move == null || move.Count < 2) {
                break;
            }

            if (!string.Equals(move[1]?.ToString(), KataGoPositionJsonBuilder.PassPoint, StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            count += 1;
        }

        return count;
    }
}
