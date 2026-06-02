using System;

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
