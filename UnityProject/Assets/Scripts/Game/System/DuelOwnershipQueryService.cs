using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public static class DuelOwnershipQueryService
{
    public const float OwnershipThreshold = 0.3f;
    public const string ScoreSourceOwnership = "katago_ownership";

    public static async Task<DuelOwnershipQueryResult> QueryOwnershipAsync(SceneBase scene, string requestIdPrefix, bool allowCachedResult)
    {
        return await QueryOwnershipAsync(scene, requestIdPrefix, allowCachedResult, null);
    }

    public static async Task<DuelOwnershipQueryResult> QueryOwnershipAsync(
        SceneBase scene,
        string requestIdPrefix,
        bool allowCachedResult,
        System.Collections.Generic.IEnumerable<int> excludedStonePosIndexes)
    {
        if (scene == null) {
            XNLogger.LogError("Duel ownership query failed, scene is empty.");
            return null;
        }

        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        bool hasExcludedStones = excludedStonePosIndexes != null;
        if (!hasExcludedStones && allowCachedResult && TryBuildCachedResult(compDuel, out DuelOwnershipQueryResult cachedResult)) {
            return cachedResult;
        }

        try {
            string requestId = $"{requestIdPrefix}-{DateTime.UtcNow.Ticks}";
            JObject query = hasExcludedStones
                ? KataGoPositionJsonBuilder.BuildOwnershipAnalysisJsonWithCurrentBoardSnapshot(scene, requestId, excludedStonePosIndexes)
                : KataGoPositionJsonBuilder.BuildOwnershipAnalysisJson(scene, requestId);
            JArray ownership = await KataGoBootstrap.AnalyzeOwnershipAsync(query);
            if (ownership == null) {
                XNLogger.LogWarn("Duel ownership query failed, ownership result is empty.", ("requestId", requestId));
                return null;
            }

            DuelOwnershipScore score = CalculateOwnershipScore(ownership, query);
            if (!hasExcludedStones) {
                compDuel?.CacheOwnershipScore(score, ownership);
            }
            return new DuelOwnershipQueryResult
            {
                ownership = ownership,
                score = score,
                scoreResult = BuildScoreResult(score),
            };
        }
        catch (Exception ex) {
            XNLogger.LogError("Duel ownership query failed.", ("err", ex.Message));
            return null;
        }
    }

    public static async Task<DuelScoreResult> QueryScoreResultAsync(DuelScene duelScene, string requestIdPrefix)
    {
        DuelOwnershipQueryResult queryResult = await QueryOwnershipAsync(duelScene, requestIdPrefix, true);
        return queryResult?.scoreResult;
    }

    public static DuelOwnershipScore CalculateOwnershipScore(JArray ownership, JObject query)
    {
        float blackPoints = 0f;
        float whitePoints = 0f;
        foreach (JToken ownershipToken in ownership) {
            if (!float.TryParse(ownershipToken?.ToString(), out float ownershipValue)) {
                continue;
            }

            if (ownershipValue > OwnershipThreshold) {
                blackPoints += 1f;
            } else if (ownershipValue < -OwnershipThreshold) {
                whitePoints += 1f;
            }
        }

        float komi = 0f;
        float.TryParse(query?["komi"]?.ToString(), out komi);
        whitePoints += komi;

        return new DuelOwnershipScore
        {
            blackPoints = blackPoints,
            whitePoints = whitePoints,
            komi = komi,
        };
    }

    public static DuelScoreResult BuildScoreResult(DuelOwnershipScore score)
    {
        float margin = Math.Abs(score.blackPoints - score.whitePoints);
        PlayerFlag winnerFlag = 0;
        if (score.blackPoints > score.whitePoints) {
            winnerFlag = PlayerFlag.Player1;
        } else if (score.whitePoints > score.blackPoints) {
            winnerFlag = PlayerFlag.Player2;
        }

        return new DuelScoreResult
        {
            blackScore = score.blackPoints,
            whiteScore = score.whitePoints,
            komi = score.komi,
            margin = margin,
            winnerFlag = winnerFlag,
            scoreSource = ScoreSourceOwnership,
        };
    }

    private static bool TryBuildCachedResult(SceneComponentDuel compDuel, out DuelOwnershipQueryResult queryResult)
    {
        queryResult = null;
        if (compDuel == null || !compDuel.hasOwnershipScoreCache || compDuel.cachedOwnership == null) {
            return false;
        }

        JArray ownership = new JArray(compDuel.cachedOwnership);
        DuelOwnershipScore score = compDuel.GetCachedOwnershipScore();
        queryResult = new DuelOwnershipQueryResult
        {
            ownership = ownership,
            score = score,
            scoreResult = BuildScoreResult(score),
        };
        return true;
    }
}

public class DuelOwnershipQueryResult
{
    public JArray ownership;
    public DuelOwnershipScore score;
    public DuelScoreResult scoreResult;
}
