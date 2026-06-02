using Newtonsoft.Json.Linq;

public class SceneComponentOgsDuel : SceneComponentBase
{
    public int gameId;
    public int boardSize = OgsConnectionConfig.DefaultBotGameBoardSize;
    public int botId;
    public string botName = string.Empty;
    public bool isBotGame;
    public int challengeId;
    public string challengeUuid = string.Empty;
    public int localOgsUserId;
    public int blackOgsUserId;
    public int whiteOgsUserId;
    public string phase = string.Empty;
    public PlayerFlag firstMovePlayerFlag = PlayerFlag.Player1;
    public int ogsHandicapCount;
    public int initialStoneCount;
    public int openingSameColorMoveCount;
    public int acceptedMoveCount;
    public bool isConnecting;
    public bool isConnected;
    public bool isSubmitting;
    public string lastError = string.Empty;
    public JToken lastGameData;

    public SceneComponentOgsDuel(SceneBase scene) : base(scene)
    {
    }
}
