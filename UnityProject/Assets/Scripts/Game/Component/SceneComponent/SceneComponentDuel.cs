public class SceneComponentDuel : SceneComponentBase
{
    public SavableField<string> player1Guid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> player2Guid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> curTurnPlayerGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> holdTimeCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> byoyomiCountCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> byoyomiTimeCfgId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> timeoutLoserGuid = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> winnerGuid = SavableFieldFactory.CreateStringField(string.Empty);

    public DuelFSM duelFSM;

    public SceneComponentDuel(SceneBase scene) : base(scene)
    {
        duelFSM = new DuelFSM(scene);
    }
}
