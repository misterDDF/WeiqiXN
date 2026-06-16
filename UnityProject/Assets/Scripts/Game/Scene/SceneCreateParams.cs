public class SceneCreateParams
{
    public static SceneCreateParams Default => new SceneCreateParams();

    public string saveFilePath;
    public string replayGameId;
    public bool replayFreeLayout;
    public string replayFreeLayoutBoardCfgId;
    public bool replayHideChart;

    public DuelSceneCreateParamas duelSceneCreateParamas;
    public OgsDuelSceneCreateParams ogsDuelSceneCreateParams;
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
    public string playerSideCfgId;
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

public class OgsDuelSceneCreateParams
{
    public int gameId;
    public int boardSize;
    public int botId;
    public string botName;
    public bool isBotGame;
    public int challengeId;
    public string challengeUuid;
}
