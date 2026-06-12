using UnityEngine;

public static class DuelOwnershipDisplayFormatter
{
    public static string BuildLeadText(float blackPoints, float whitePoints)
    {
        float margin = Mathf.Abs(blackPoints - whitePoints);
        if (Mathf.Approximately(margin, 0f)) {
            return MessageText.Get("duel_ownership_lead_even");
        }

        string leaderText = blackPoints > whitePoints
            ? MessageText.Get("duel_player_black_short")
            : MessageText.Get("duel_player_white_short");
        return MessageText.Format("duel_ownership_lead_points", leaderText, FormatPointCount(margin));
    }

    public static string BuildRuleInfoText(float komi, int handicapCount, bool isSen)
    {
        string suffix = string.Empty;
        if (handicapCount > 0) {
            suffix = MessageText.Format("duel_ownership_rule_handicap_suffix", ChineseNumberText.FormatInteger(handicapCount));
        } else if (isSen) {
            suffix = MessageText.Get("duel_ownership_rule_sen_suffix");
        }

        return MessageText.Format("duel_ownership_rule_info", FormatPointCount(komi), suffix);
    }

    public static string FormatPointCount(float pointCount)
    {
        return Mathf.Approximately(pointCount, Mathf.Round(pointCount))
            ? Mathf.RoundToInt(pointCount).ToString()
            : pointCount.ToString("0.0");
    }
}
