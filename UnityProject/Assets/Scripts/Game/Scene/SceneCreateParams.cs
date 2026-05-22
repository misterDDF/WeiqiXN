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
    public string handicapCfgId;
    public bool isAiDuel;
    public string aiDifficultyCfgId;
    public PlayerFlag localPlayerFlag;
    public UserProfileData localPlayerProfile;
    public UserProfileData hostPlayerProfile;
    public UserProfileData clientPlayerProfile;
    public bool isLanDuel;
    public LanRoomRole lanRole;
    public PlayerFlag lanHostPlayerFlag;
    public string lanHostPlayerSideCfgId;
    public bool isLanRoomHostConfig;
}
