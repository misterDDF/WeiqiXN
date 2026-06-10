using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

public static class OgsRankDisplayFormatter
{
    private const double RatingToRankingScale = 23.15;
    private const double RatingToRankingBase = 525.0;

    public static string ReadOverallRankDisplay(JObject json, bool integerRank = false)
    {
        if (json == null) {
            return string.Empty;
        }

        string rating = ReadRatingRankDisplay(json["ratings"]?["overall"], integerRank) ??
            ReadRatingRankDisplay(json["rating"], integerRank) ??
            ReadRatingRankDisplay(json["ratings"], integerRank);
        if (!string.IsNullOrWhiteSpace(rating)) {
            return rating;
        }

        return ReadRankingDisplay(json, integerRank, "ranking", "rank");
    }

    public static string ReadRankingDisplay(JObject json, params string[] fieldNames)
    {
        return ReadRankingDisplay(json, false, fieldNames);
    }

    public static string ReadRankingDisplay(JObject json, bool integerRank, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return string.Empty;
        }

        foreach (string fieldName in fieldNames) {
            if (string.IsNullOrWhiteSpace(fieldName)) {
                continue;
            }
            if (TryReadDouble(json[fieldName], out double ranking)) {
                return FormatRankFromRanking(ranking, integerRank);
            }
        }

        return string.Empty;
    }

    public static string ReadRatingRankDisplay(JToken token, bool integerRank = false)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return null;
        }

        if (TryReadDouble(token, out double rating)) {
            return FormatRankFromRating(rating, integerRank);
        }

        if (token is JObject obj) {
            string ranking = ReadRankingDisplay(obj, integerRank, "ranking", "rank");
            if (!string.IsNullOrWhiteSpace(ranking)) {
                return ranking;
            }

            return ReadRatingRankDisplay(obj, integerRank, "rating", "elo", "glicko", "score", "value");
        }

        return null;
    }

    public static string ReadRatingRankDisplay(JObject json, bool integerRank, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return string.Empty;
        }

        foreach (string fieldName in fieldNames) {
            if (string.IsNullOrWhiteSpace(fieldName)) {
                continue;
            }
            string rankLabel = ReadRatingRankDisplay(json[fieldName], integerRank);
            if (!string.IsNullOrWhiteSpace(rankLabel)) {
                return rankLabel;
            }
        }

        return string.Empty;
    }

    public static string ReadBoardRankDisplay(JObject playerJson, int boardSize, bool allowOverallFallback, bool integerRank)
    {
        string rankLabel = ReadBoardRankDisplayFromObject(playerJson, boardSize, allowOverallFallback, integerRank);
        if (!string.IsNullOrWhiteSpace(rankLabel)) {
            return rankLabel;
        }

        rankLabel = ReadBoardRankDisplayFromObject(playerJson?["user"] as JObject, boardSize, allowOverallFallback, integerRank);
        if (!string.IsNullOrWhiteSpace(rankLabel)) {
            return rankLabel;
        }

        return ReadBoardRankDisplayFromObject(playerJson?["player"] as JObject, boardSize, allowOverallFallback, integerRank);
    }

    public static string FormatRankFromRating(double rating, bool integerRank = false)
    {
        if (rating <= 0.0 || double.IsNaN(rating) || double.IsInfinity(rating)) {
            return string.Empty;
        }

        double ranking = RatingToRankingScale * Math.Log(rating / RatingToRankingBase);
        return FormatRankFromRanking(ranking, integerRank);
    }

    public static string FormatRankFromRanking(double ranking, bool integerRank = false)
    {
        if (double.IsNaN(ranking) || double.IsInfinity(ranking)) {
            return string.Empty;
        }

        if (ranking < 30.0) {
            double kyu = Math.Max(1.0, Math.Min(30.0, 30.0 - ranking));
            return $"{FormatRankNumber(kyu, integerRank)}级";
        }

        double dan = Math.Max(1.0, Math.Min(9.0, ranking - 29.0));
        return $"{FormatRankNumber(dan, integerRank)}段";
    }

    public static bool TryParseDouble(string value, out double number)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) {
            return true;
        }

        return double.TryParse(value, out number);
    }

    private static string ReadBoardRankDisplayFromObject(JObject playerJson, int boardSize, bool allowOverallFallback, bool integerRank)
    {
        if (playerJson == null) {
            return string.Empty;
        }

        string boardKey = boardSize > 0
            ? $"{boardSize.ToString(CultureInfo.InvariantCulture)}x{boardSize.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
        string boardNumber = boardSize > 0 ? boardSize.ToString(CultureInfo.InvariantCulture) : string.Empty;
        JObject ratings = playerJson["ratings"] as JObject;

        string rankLabel = ReadRatingRankDisplay(ratings?[boardKey], integerRank) ??
            ReadRatingRankDisplay(ratings?[boardNumber], integerRank);
        if (!string.IsNullOrWhiteSpace(rankLabel)) {
            return rankLabel;
        }

        rankLabel = ReadRankingDisplay(
            playerJson,
            integerRank,
            $"ranking_{boardNumber}",
            $"rank_{boardNumber}",
            $"ranking{boardNumber}",
            $"rank{boardNumber}",
            $"{boardKey}_ranking",
            $"{boardKey}_rank");
        if (!string.IsNullOrWhiteSpace(rankLabel)) {
            return rankLabel;
        }

        rankLabel = ReadRatingRankDisplay(
            playerJson,
            integerRank,
            $"rating_{boardNumber}",
            $"rating{boardNumber}",
            $"{boardKey}_rating");
        if (!string.IsNullOrWhiteSpace(rankLabel)) {
            return rankLabel;
        }

        if (!allowOverallFallback) {
            return string.Empty;
        }

        rankLabel = ReadRatingRankDisplay(ratings?["overall"], integerRank);
        if (!string.IsNullOrWhiteSpace(rankLabel)) {
            return rankLabel;
        }

        rankLabel = ReadRankingDisplay(playerJson, integerRank, "ranking", "rank");
        if (!string.IsNullOrWhiteSpace(rankLabel)) {
            return rankLabel;
        }

        return ReadRatingRankDisplay(playerJson, integerRank, "rating", "elo", "glicko");
    }

    private static string FormatRankNumber(double value, bool integerRank)
    {
        if (integerRank) {
            return Math.Max(1, (int)Math.Truncate(value)).ToString(CultureInfo.InvariantCulture);
        }

        return Math.Abs(value - Math.Round(value)) < 0.05
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static bool TryReadDouble(JToken token, out double value)
    {
        value = 0.0;
        if (token == null || token.Type == JTokenType.Null) {
            return false;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) {
            value = token.ToObject<double>();
            return true;
        }

        return TryParseDouble(token.ToString(), out value);
    }
}
