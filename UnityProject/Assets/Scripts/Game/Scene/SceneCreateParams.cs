public class SceneCreateParams
{
    public static SceneCreateParams Default => new SceneCreateParams();

    public string saveFilePath;

    public DuelSceneCreateParamas duelSceneCreateParamas;
}

public class DuelSceneCreateParamas
{
    public string boardCfgId;
    public string holdTimeCfgId;
    public string byoyomiCountCfgId;
    public string byoyomiTimeCfgId;
    public bool isAiDuel;
    public string aiDifficultyCfgId;
    public bool isLanDuel;
    public LanRoomRole lanRole;
}
