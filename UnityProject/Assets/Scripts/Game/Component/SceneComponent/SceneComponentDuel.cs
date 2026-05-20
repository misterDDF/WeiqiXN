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
    public SavableField<string> timeoutLoserGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> resignLoserGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> winnerGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<int> consecutivePassCount = SavableFieldFactory.CreateIntField(0);
    public SavableField<string> gameEndReason = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<float> finalBlackScore = SavableFieldFactory.CreateFloatField(0f);
    public SavableField<float> finalWhiteScore = SavableFieldFactory.CreateFloatField(0f);
    public SavableField<float> finalScoreMargin = SavableFieldFactory.CreateFloatField(0f);
    public JArray kataGoMoves = new JArray();

    public DuelFSM duelFSM;

    public SceneComponentDuel(SceneBase scene) : base(scene)
    {
        duelFSM = new DuelFSM(scene);
    }

    public void ResetKataGoMoves()
    {
        kataGoMoves = new JArray();
    }

    public void AppendKataGoMove(PlayerFlag playerFlag, RectCoordinates coords, int boardSize)
    {
        kataGoMoves.Add(new JArray(
            KataGoPositionJsonBuilder.ToKataGoColor(playerFlag),
            KataGoPositionJsonBuilder.ToKataGoPoint(coords, boardSize)
        ));
    }

    public void AppendKataGoPass(PlayerFlag playerFlag)
    {
        kataGoMoves.Add(new JArray(
            KataGoPositionJsonBuilder.ToKataGoColor(playerFlag),
            KataGoPositionJsonBuilder.PassPoint
        ));
    }
}
