using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;

public class SceneComponentDuel : SceneComponentBase
{
    public SavableField<string> player1Guid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> player2Guid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> curTurnPlayerGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> holdTimeCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> byoyomiCountCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> byoyomiTimeCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<bool> isAiDuel = SavableFieldFactory.CreateBoolField(false);
    public SavableField<string> aiDifficultyCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> aiPlayerGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> timeoutLoserGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> resignLoserGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> winnerGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<int> consecutivePassCount = SavableFieldFactory.CreateIntField(0);
    public SavableField<string> gameEndReason = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<float> finalBlackScore = SavableFieldFactory.CreateFloatField(0f);
    public SavableField<float> finalWhiteScore = SavableFieldFactory.CreateFloatField(0f);
    public SavableField<float> finalScoreMargin = SavableFieldFactory.CreateFloatField(0f);
    public JArray kataGoMoves = new JArray();
    public bool isScoring;
    public bool hasOwnershipScoreCache;
    public float cachedOwnershipBlackPoints;
    public float cachedOwnershipWhitePoints;
    public float cachedOwnershipKomi;
    public JArray cachedOwnership;

    public DuelFSM duelFSM;

    public SceneComponentDuel(SceneBase scene) : base(scene)
    {
        duelFSM = new DuelFSM(scene);
    }

    public void ResetKataGoMoves()
    {
        kataGoMoves = DuelMoveHistory.CreateEmpty();
    }

    public void AppendKataGoMove(PlayerFlag playerFlag, RectCoordinates coords, int boardSize)
    {
        DuelMoveHistory.AppendMove(kataGoMoves, playerFlag, coords, boardSize);
    }

    public void AppendKataGoPass(PlayerFlag playerFlag)
    {
        DuelMoveHistory.AppendPass(kataGoMoves, playerFlag);
    }

    public void RemoveLastKataGoMove()
    {
        DuelMoveHistory.RemoveLast(kataGoMoves);
    }

    public void CacheOwnershipScore(DuelOwnershipSystem.OwnershipScore score, JArray ownership)
    {
        hasOwnershipScoreCache = true;
        cachedOwnershipBlackPoints = score.blackPoints;
        cachedOwnershipWhitePoints = score.whitePoints;
        cachedOwnershipKomi = score.komi;
        cachedOwnership = ownership != null ? new JArray(ownership) : null;
    }

    public DuelOwnershipSystem.OwnershipScore GetCachedOwnershipScore()
    {
        return new DuelOwnershipSystem.OwnershipScore
        {
            blackPoints = cachedOwnershipBlackPoints,
            whitePoints = cachedOwnershipWhitePoints,
            komi = cachedOwnershipKomi,
        };
    }

    public void ClearOwnershipScoreCache()
    {
        hasOwnershipScoreCache = false;
        cachedOwnershipBlackPoints = 0f;
        cachedOwnershipWhitePoints = 0f;
        cachedOwnershipKomi = 0f;
        cachedOwnership = null;
    }
}
