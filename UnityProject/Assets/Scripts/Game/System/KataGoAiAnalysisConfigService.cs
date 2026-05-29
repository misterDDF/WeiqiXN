using System.Collections.Generic;
using UnityEngine;

public struct KataGoAiAnalysisTier
{
    public readonly int tier;
    public readonly int maxVisits;
    public readonly bool includeOwnership;
    public readonly int priority;

    public KataGoAiAnalysisTier(int tier, int maxVisits, bool includeOwnership, int priority)
    {
        this.tier = tier;
        this.maxVisits = maxVisits;
        this.includeOwnership = includeOwnership;
        this.priority = priority;
    }
}

public static class KataGoAiAnalysisConfigService
{
    private const string ConfigAiAnalysisEnabled = "aiAnalysisEnabled";
    private const string ConfigAiTier1MaxVisits9 = "aiTier1MaxVisits9";
    private const string ConfigAiTier1MaxVisits13 = "aiTier1MaxVisits13";
    private const string ConfigAiTier1MaxVisits19 = "aiTier1MaxVisits19";
    private const string ConfigAiTier2MaxVisits9 = "aiTier2MaxVisits9";
    private const string ConfigAiTier2MaxVisits13 = "aiTier2MaxVisits13";
    private const string ConfigAiTier2MaxVisits19 = "aiTier2MaxVisits19";
    private const string ConfigAiTier3MaxVisits9 = "aiTier3MaxVisits9";
    private const string ConfigAiTier3MaxVisits13 = "aiTier3MaxVisits13";
    private const string ConfigAiTier3MaxVisits19 = "aiTier3MaxVisits19";
    private const string ConfigAiTier1IncludeOwnership = "aiTier1IncludeOwnership";
    private const string ConfigAiTier2IncludeOwnership = "aiTier2IncludeOwnership";
    private const string ConfigAiTier3IncludeOwnership = "aiTier3IncludeOwnership";
    private const string ConfigAiDisplayCandidateLimit = "aiDisplayCandidateLimit";
    private const string ConfigAiRequestCandidateLimit = "aiRequestCandidateLimit";
    private const string ConfigAiIncludePolicy = "aiIncludePolicy";
    private const string ConfigAiShowCurrentPlayerWinrate = "aiShowCurrentPlayerWinrate";
    private const string ConfigAiWinrateMinDisplay = "aiWinrateMinDisplay";
    private const string ConfigAiWinrateMaxDisplay = "aiWinrateMaxDisplay";
    private const string ConfigAiAnalysisCooldownMs = "aiAnalysisCooldownMs";

    private const int ReplayAiTier1Priority = 100;
    private const int ReplayAiTier2Priority = 60;
    private const int ReplayAiTier3Priority = 40;

    public static bool IsAiAnalysisEnabled => GetBool(ConfigAiAnalysisEnabled, true);
    public static bool IncludePolicy => GetBool(ConfigAiIncludePolicy, false);
    public static bool ShowCurrentPlayerWinrate => GetBool(ConfigAiShowCurrentPlayerWinrate, true);
    public static int AnalysisCooldownMs => Mathf.Max(GetInt(ConfigAiAnalysisCooldownMs, 500), 0);
    public static int DisplayCandidateLimit => Mathf.Clamp(GetInt(ConfigAiDisplayCandidateLimit, 5), 1, 20);
    public static int RequestCandidateLimit => Mathf.Max(GetInt(ConfigAiRequestCandidateLimit, 12), DisplayCandidateLimit);
    public static int WinrateMinDisplay => Mathf.Clamp(GetInt(ConfigAiWinrateMinDisplay, 1), 1, 100);
    public static int WinrateMaxDisplay => Mathf.Clamp(GetInt(ConfigAiWinrateMaxDisplay, 100), WinrateMinDisplay, 100);

    public static List<KataGoAiAnalysisTier> BuildAiAnalysisTiers(int boardSize)
    {
        return new List<KataGoAiAnalysisTier>
        {
            new KataGoAiAnalysisTier(1, ResolveAiTierMaxVisits(boardSize, ConfigAiTier1MaxVisits9, ConfigAiTier1MaxVisits13, ConfigAiTier1MaxVisits19, 64, 40, 25), GetBool(ConfigAiTier1IncludeOwnership, false), ReplayAiTier1Priority),
            new KataGoAiAnalysisTier(2, ResolveAiTierMaxVisits(boardSize, ConfigAiTier2MaxVisits9, ConfigAiTier2MaxVisits13, ConfigAiTier2MaxVisits19, 128, 80, 50), GetBool(ConfigAiTier2IncludeOwnership, true), ReplayAiTier2Priority),
            new KataGoAiAnalysisTier(3, ResolveAiTierMaxVisits(boardSize, ConfigAiTier3MaxVisits9, ConfigAiTier3MaxVisits13, ConfigAiTier3MaxVisits19, 1000, 768, 500), GetBool(ConfigAiTier3IncludeOwnership, true), ReplayAiTier3Priority),
        };
    }

    private static int ResolveAiTierMaxVisits(
        int boardSize,
        string config9,
        string config13,
        string config19,
        int default9,
        int default13,
        int default19)
    {
        if (boardSize <= 9) {
            return Mathf.Max(GetInt(config9, default9), 1);
        }

        if (boardSize <= 13) {
            return Mathf.Max(GetInt(config13, default13), 1);
        }

        return Mathf.Max(GetInt(config19, default19), 1);
    }

    private static int GetInt(string id, int defaultValue)
    {
        ReplayConfigDataType data = ReplayConfigDataType.GetConfigData(id);
        return data != null && data.valueType == "int" ? data.intValue : defaultValue;
    }

    private static bool GetBool(string id, bool defaultValue)
    {
        ReplayConfigDataType data = ReplayConfigDataType.GetConfigData(id);
        return data != null && data.valueType == "boolean" ? data.boolValue : defaultValue;
    }
}
