using Newtonsoft.Json.Linq;
using System.Collections.Generic;

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
    public float komi = 7.5f;
    public bool hasKomi;
    public int initialStoneCount;
    public int openingSameColorMoveCount;
    public int acceptedMoveCount;
    public bool isConnecting;
    public bool isConnected;
    public bool isSubmitting;
    public string lastError = string.Empty;
    public JToken lastGameData;
    public JArray kataGoInitialStones = new JArray();
    public HashSet<int> removedStonePosIndexes = new HashSet<int>();
    public string removedStones = string.Empty;
    public bool strictSekiMode;
    public bool localRemovedStonesAccepted;
    public bool opponentRemovedStonesAccepted;
    public string localAcceptedRemovedStones = string.Empty;
    public string opponentAcceptedRemovedStones = string.Empty;
    public bool isSubmittingRemovedStones;

    public SceneComponentOgsDuel(SceneBase scene) : base(scene)
    {
    }
}
