using System;
using System.Collections.Generic;

public sealed class OgsAuthorizationRequest
{
    public readonly string authorizationUrl;
    public readonly string codeVerifier;
    public readonly string state;
    public readonly string redirectUri;

    public OgsAuthorizationRequest(string authorizationUrl, string codeVerifier, string state, string redirectUri)
    {
        this.authorizationUrl = authorizationUrl ?? string.Empty;
        this.codeVerifier = codeVerifier ?? string.Empty;
        this.state = state ?? string.Empty;
        this.redirectUri = redirectUri ?? string.Empty;
    }
}

public sealed class OgsSession
{
    public string accessToken;
    public string refreshToken;
    public string tokenType;
    public string scope;
    public DateTime expiresAtUtc;
    public string userId;
    public string username;
    public string avatarUrl;
    public string country;
    public string registeredAt;
    public string tags;
    public string about;
    public string ratingOverall;
    public string ranking;
    public string rating19;
    public string rating13;
    public string rating9;

    public bool HasAccessToken => !string.IsNullOrEmpty(accessToken);

    public bool IsExpired => HasAccessToken && expiresAtUtc != DateTime.MinValue && DateTime.UtcNow >= expiresAtUtc;

    public bool CanRefresh => !string.IsNullOrEmpty(refreshToken);

    public string DisplayName => string.IsNullOrEmpty(username) ? userId : username;

    public void Clear()
    {
        accessToken = string.Empty;
        refreshToken = string.Empty;
        tokenType = string.Empty;
        scope = string.Empty;
        expiresAtUtc = DateTime.MinValue;
        userId = string.Empty;
        username = string.Empty;
        avatarUrl = string.Empty;
        country = string.Empty;
        registeredAt = string.Empty;
        tags = string.Empty;
        about = string.Empty;
        ratingOverall = string.Empty;
        ranking = string.Empty;
        rating19 = string.Empty;
        rating13 = string.Empty;
        rating9 = string.Empty;
    }
}

public sealed class OgsConnectionResult
{
    public readonly bool success;
    public readonly string message;

    public OgsConnectionResult(bool success, string message)
    {
        this.success = success;
        this.message = message ?? string.Empty;
    }
}

public sealed class OgsFriendListResult
{
    public readonly bool success;
    public readonly string message;
    public readonly List<OgsFriendListItem> friends;
    public readonly int totalCount;

    public OgsFriendListResult(bool success, string message, List<OgsFriendListItem> friends = null, int totalCount = 0)
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.friends = friends ?? new List<OgsFriendListItem>();
        this.totalCount = totalCount;
    }
}

public sealed class OgsFriendListItem
{
    public string userId;
    public string username;
    public string avatarUrl;
    public string country;
    public string ratingText;
    public string ratingOverall;
    public string rankingText;
    public string rating19;
    public string rating13;
    public string rating9;
    public string statusText;
    public string registeredAt;
    public string about;
}

public sealed class OgsFriendProfileResult
{
    public readonly bool success;
    public readonly string message;
    public readonly OgsFriendListItem friend;

    public OgsFriendProfileResult(bool success, string message, OgsFriendListItem friend = null)
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.friend = friend;
    }
}

public sealed class OgsFriendInvitationListResult
{
    public readonly bool success;
    public readonly string message;
    public readonly List<OgsFriendInvitationItem> invitations;

    public OgsFriendInvitationListResult(bool success, string message, List<OgsFriendInvitationItem> invitations = null)
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.invitations = invitations ?? new List<OgsFriendInvitationItem>();
    }
}

public sealed class OgsFriendInvitationCountResult
{
    public readonly bool success;
    public readonly string message;
    public readonly int count;

    public OgsFriendInvitationCountResult(bool success, string message, int count = 0)
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.count = count;
    }
}

public sealed class OgsFriendInvitationItem
{
    public OgsFriendListItem fromUser;
    public string createdAt;
    public bool accepted;

    public string FromUserId => fromUser?.userId ?? string.Empty;

    public string FromUsername => fromUser?.username ?? string.Empty;
}

public sealed class OgsChallengeInviteListResult
{
    public readonly bool success;
    public readonly string message;
    public readonly List<OgsChallengeInvite> invites;

    public OgsChallengeInviteListResult(bool success, string message, List<OgsChallengeInvite> invites = null)
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.invites = invites ?? new List<OgsChallengeInvite>();
    }
}

public sealed class OgsChallengeInvite
{
    public int challengeId;
    public string challengeUuid;
    public int gameId;
    public int challengerId;
    public string challengerName;
    public int challengedId;
    public int boardSize;
    public string gameName;
    public string rawResponse;

    public string DisplayName => string.IsNullOrWhiteSpace(challengerName) ? $"OGS 玩家 {challengerId}" : challengerName;
}

public sealed class OgsCallbackResult
{
    public readonly bool success;
    public readonly string message;
    public readonly string code;

    public OgsCallbackResult(bool success, string message, string code = "")
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.code = code ?? string.Empty;
    }
}

public sealed class OgsRealtimeSmokeResult
{
    public readonly bool success;
    public readonly string message;
    public readonly string firstMessage;

    public OgsRealtimeSmokeResult(bool success, string message, string firstMessage = "")
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.firstMessage = firstMessage ?? string.Empty;
    }
}

public sealed class OgsGameStateSmokeResult
{
    public readonly bool success;
    public readonly string message;
    public readonly int gameId;
    public readonly int boardWidth;
    public readonly int boardHeight;
    public readonly int moveCount;
    public readonly string blackPlayer;
    public readonly string whitePlayer;
    public readonly string phase;
    public readonly string rawMessage;

    public OgsGameStateSmokeResult(
        bool success,
        string message,
        int gameId = 0,
        int boardWidth = 0,
        int boardHeight = 0,
        int moveCount = 0,
        string blackPlayer = "",
        string whitePlayer = "",
        string phase = "",
        string rawMessage = "")
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.gameId = gameId;
        this.boardWidth = boardWidth;
        this.boardHeight = boardHeight;
        this.moveCount = moveCount;
        this.blackPlayer = blackPlayer ?? string.Empty;
        this.whitePlayer = whitePlayer ?? string.Empty;
        this.phase = phase ?? string.Empty;
        this.rawMessage = rawMessage ?? string.Empty;
    }
}

public sealed class OgsBotGameStartResult
{
    public readonly bool success;
    public readonly string message;
    public readonly int botId;
    public readonly string botName;
    public readonly int challengeId;
    public readonly string challengeUuid;
    public readonly int gameId;
    public readonly OgsGameStateSmokeResult gameState;
    public readonly string rawResponse;
    public readonly bool isBotGame;

    public OgsBotGameStartResult(
        bool success,
        string message,
        int botId = 0,
        string botName = "",
        int challengeId = 0,
        string challengeUuid = "",
        int gameId = 0,
        OgsGameStateSmokeResult gameState = null,
        string rawResponse = "",
        bool isBotGame = false)
    {
        this.success = success;
        this.message = message ?? string.Empty;
        this.botId = botId;
        this.botName = botName ?? string.Empty;
        this.challengeId = challengeId;
        this.challengeUuid = challengeUuid ?? string.Empty;
        this.gameId = gameId;
        this.gameState = gameState;
        this.rawResponse = rawResponse ?? string.Empty;
        this.isBotGame = isBotGame;
    }
}

public sealed class OgsBotGameCreateParams
{
    public readonly int boardSize;
    public readonly int mainTimeSeconds;
    public readonly int byoyomiPeriods;
    public readonly int byoyomiPeriodSeconds;
    public readonly int handicap;
    public readonly float komi;
    public readonly string challengerColor;
    public readonly string gameName;

    public OgsBotGameCreateParams(
        int boardSize,
        int mainTimeSeconds,
        int byoyomiPeriods,
        int byoyomiPeriodSeconds,
        int handicap,
        float komi,
        string challengerColor,
        string gameName = "")
    {
        this.boardSize = boardSize;
        this.mainTimeSeconds = mainTimeSeconds;
        this.byoyomiPeriods = byoyomiPeriods;
        this.byoyomiPeriodSeconds = byoyomiPeriodSeconds;
        this.handicap = handicap;
        this.komi = komi;
        this.challengerColor = challengerColor ?? string.Empty;
        this.gameName = gameName ?? string.Empty;
    }

    public static OgsBotGameCreateParams Default => new OgsBotGameCreateParams(
        OgsConnectionConfig.DefaultBotGameBoardSize,
        600,
        5,
        30,
        0,
        7.5f,
        "automatic",
        OgsConnectionConfig.DefaultBotGameName);
}

public sealed class OgsFriendChallengeCreateParams
{
    public readonly string friendUserId;
    public readonly int boardSize;
    public readonly int mainTimeSeconds;
    public readonly int byoyomiPeriods;
    public readonly int byoyomiPeriodSeconds;
    public readonly int handicap;
    public readonly float komi;
    public readonly string challengerColor;
    public readonly string gameName;

    public OgsFriendChallengeCreateParams(
        string friendUserId,
        int boardSize,
        int mainTimeSeconds,
        int byoyomiPeriods,
        int byoyomiPeriodSeconds,
        int handicap,
        float komi,
        string challengerColor,
        string gameName = "")
    {
        this.friendUserId = friendUserId ?? string.Empty;
        this.boardSize = boardSize;
        this.mainTimeSeconds = mainTimeSeconds;
        this.byoyomiPeriods = byoyomiPeriods;
        this.byoyomiPeriodSeconds = byoyomiPeriodSeconds;
        this.handicap = handicap;
        this.komi = komi;
        this.challengerColor = challengerColor ?? string.Empty;
        this.gameName = gameName ?? string.Empty;
    }
}

public sealed class OgsAutomatchCreateParams
{
    public readonly int boardSize;
    public readonly int mainTimeSeconds;
    public readonly int byoyomiPeriods;
    public readonly int byoyomiPeriodSeconds;
    public readonly int handicap;
    public readonly string speed;
    public readonly string system;
    public readonly int lowerRankDiff;
    public readonly int upperRankDiff;

    public OgsAutomatchCreateParams(
        int boardSize,
        int mainTimeSeconds,
        int byoyomiPeriods,
        int byoyomiPeriodSeconds,
        int handicap,
        string speed = "",
        string system = "",
        int lowerRankDiff = 3,
        int upperRankDiff = 3)
    {
        this.boardSize = boardSize;
        this.mainTimeSeconds = mainTimeSeconds;
        this.byoyomiPeriods = byoyomiPeriods;
        this.byoyomiPeriodSeconds = byoyomiPeriodSeconds;
        this.handicap = handicap;
        this.speed = speed ?? string.Empty;
        this.system = system ?? string.Empty;
        this.lowerRankDiff = lowerRankDiff;
        this.upperRankDiff = upperRankDiff;
    }

    public static OgsAutomatchCreateParams Default => new OgsAutomatchCreateParams(
        OgsConnectionConfig.DefaultBotGameBoardSize,
        600,
        5,
        30,
        0,
        "rapid",
        "byoyomi");
}
