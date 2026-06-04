public enum DuelSetupPreferenceMode
{
    Local = 0,
    Ai = 1,
    Lan = 2,
    Ogs = 3,
    OgsFriend = 4,
}

public class UserComponentDuelSetupPreference : UserComponentBase
{
    public DuelSetupModePreference localDuel = new DuelSetupModePreference();
    public DuelSetupModePreference aiDuel = new DuelSetupModePreference();
    public DuelSetupModePreference lanDuel = new DuelSetupModePreference();
    public DuelSetupModePreference ogsDuel = DuelSetupModePreference.CreateOgsDefault();
    public DuelSetupModePreference ogsFriendDuel = DuelSetupModePreference.CreateOgsDefault();

    public UserComponentDuelSetupPreference(User owner) : base(owner)
    {
    }

    public DuelSetupModePreference GetPreference(DuelSetupPreferenceMode mode)
    {
        switch (mode) {
            case DuelSetupPreferenceMode.Ai:
                return aiDuel;
            case DuelSetupPreferenceMode.Lan:
                return lanDuel;
            case DuelSetupPreferenceMode.Ogs:
                return ogsDuel;
            case DuelSetupPreferenceMode.OgsFriend:
                return ogsFriendDuel;
            case DuelSetupPreferenceMode.Local:
            default:
                return localDuel;
        }
    }
}

public class DuelSetupModePreference : SavableObj
{
    public SavableField<string> boardCfgId = SavableFieldFactory.CreateStringField("9x9");
    public SavableField<string> holdTimeCfgId = SavableFieldFactory.CreateStringField("infinite");
    public SavableField<string> byoyomiCountCfgId = SavableFieldFactory.CreateStringField("off");
    public SavableField<string> byoyomiTimeCfgId = SavableFieldFactory.CreateStringField("30s");
    public SavableField<string> playerSideCfgId = SavableFieldFactory.CreateStringField("guess");
    public SavableField<string> handicapCfgId = SavableFieldFactory.CreateStringField("9x9_0");
    public SavableField<string> aiDifficultyCfgId = SavableFieldFactory.CreateStringField("k20_k15");

    public static DuelSetupModePreference CreateOgsDefault()
    {
        DuelSetupModePreference preference = new DuelSetupModePreference();
        preference.Set("9x9", "10m", "5", "30s", "guess", "9x9_0", "k20_k15");
        return preference;
    }

    public void Set(
        string boardCfgId,
        string holdTimeCfgId,
        string byoyomiCountCfgId,
        string byoyomiTimeCfgId,
        string playerSideCfgId,
        string handicapCfgId,
        string aiDifficultyCfgId)
    {
        this.boardCfgId.value = boardCfgId;
        this.holdTimeCfgId.value = holdTimeCfgId;
        this.byoyomiCountCfgId.value = byoyomiCountCfgId;
        this.byoyomiTimeCfgId.value = byoyomiTimeCfgId;
        this.playerSideCfgId.value = playerSideCfgId;
        this.handicapCfgId.value = handicapCfgId;
        this.aiDifficultyCfgId.value = aiDifficultyCfgId;
    }
}
