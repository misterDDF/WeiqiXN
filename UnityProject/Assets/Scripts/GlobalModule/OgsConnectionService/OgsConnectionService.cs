using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XNClient.Logger;

public sealed class OgsConnectionService : ModuleBase
{
    private object sessionLock;
    private OgsSession session;
    private string apiBaseUrl;
    private bool sessionLoaded;

    public OgsConnectionService()
    {
        EnsureInitialized();
    }

    public OgsSession Session
    {
        get
        {
            EnsureInitialized();
            lock (sessionLock) {
                return CloneSession(session);
            }
        }
    }

    public bool HasSession
    {
        get
        {
            EnsureInitialized();
            lock (sessionLock) {
                return session.HasAccessToken || session.CanRefresh;
            }
        }
    }

    public bool HasWriteSession
    {
        get
        {
            EnsureInitialized();
            lock (sessionLock) {
                return (session.HasAccessToken || session.CanRefresh) && ContainsScope(session.scope, "write");
            }
        }
    }

    public override void Init()
    {
    }

    private void EnsureInitialized()
    {
        if (sessionLock == null) {
            sessionLock = new object();
        }
        if (session == null) {
            session = new OgsSession();
        }
        if (string.IsNullOrEmpty(apiBaseUrl)) {
            apiBaseUrl = OgsConnectionConfig.DefaultApiBaseUrl;
        }
        if (sessionLoaded) {
            return;
        }

        sessionLoaded = true;
        OgsSessionStore.TryLoad(session);
        if (session.HasAccessToken || session.CanRefresh) {
            XNLogger.LogInfo(
                "OGS session loaded.",
                ("userId", session.userId ?? string.Empty),
                ("username", session.username ?? string.Empty),
                ("hasRefreshToken", session.CanRefresh.ToString()));
        }
    }

    public void SetApiBaseUrl(string baseUrl)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            apiBaseUrl = OgsConnectionConfig.DefaultApiBaseUrl;
            return;
        }

        apiBaseUrl = baseUrl.Trim().TrimEnd('/');
    }

    public OgsAuthorizationRequest CreateAuthorizationRequest(
        string clientId,
        string redirectUri = null,
        string scope = null)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(clientId)) {
            throw new ArgumentException("OGS client id is required.", nameof(clientId));
        }

        string safeRedirectUri = string.IsNullOrWhiteSpace(redirectUri)
            ? OgsConnectionConfig.DefaultRedirectUri
            : redirectUri.Trim();
        string safeScope = string.IsNullOrWhiteSpace(scope)
            ? OgsConnectionConfig.DefaultScope
            : scope.Trim();
        string verifier = CreatePkceVerifier();
        string challenge = CreatePkceChallenge(verifier);
        string state = Guid.NewGuid().ToString("N");

        string url = $"{apiBaseUrl}{OgsConnectionConfig.AuthorizationPath}" +
            $"?response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId.Trim())}" +
            $"&redirect_uri={Uri.EscapeDataString(safeRedirectUri)}" +
            $"&scope={Uri.EscapeDataString(safeScope)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            $"&code_challenge_method=S256";

        return new OgsAuthorizationRequest(url, verifier, state, safeRedirectUri);
    }

    public async Task<OgsConnectionResult> LoginWithAuthorizationCodeAsync(
        string clientId,
        string authorizationCode,
        string codeVerifier,
        string redirectUri = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(clientId)) {
            return new OgsConnectionResult(false, "OGS client id is empty.");
        }
        if (string.IsNullOrWhiteSpace(authorizationCode)) {
            return new OgsConnectionResult(false, "OGS authorization code is empty.");
        }
        if (string.IsNullOrWhiteSpace(codeVerifier)) {
            return new OgsConnectionResult(false, "OGS PKCE verifier is empty.");
        }

        try {
            JObject tokenJson = await PostFormAsync(
                $"{apiBaseUrl}{OgsConnectionConfig.TokenPath}",
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = clientId.Trim(),
                    ["code"] = authorizationCode.Trim(),
                    ["code_verifier"] = codeVerifier.Trim(),
                    ["redirect_uri"] = string.IsNullOrWhiteSpace(redirectUri) ? OgsConnectionConfig.DefaultRedirectUri : redirectUri.Trim(),
                },
                null,
                cancellationToken);

            ApplyTokenJson(tokenJson);
            OgsConnectionResult profileResult = await RefreshCurrentUserAsync(cancellationToken);
            if (!profileResult.success) {
                return profileResult;
            }

            OgsSessionStore.Save(session);
            XNLogger.LogInfo("OGS login succeeded.", ("userId", session.userId ?? string.Empty), ("username", session.username ?? string.Empty));
            return new OgsConnectionResult(true, "OGS login succeeded.");
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS login failed.", ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
        }
    }

    public async Task<OgsConnectionResult> LoginWithBrowserCallbackAsync(
        string clientId = null,
        string redirectUri = null,
        string scope = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        clientId = string.IsNullOrWhiteSpace(clientId) ? OgsConnectionConfig.DefaultClientId : clientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId)) {
            return new OgsConnectionResult(false, "OGS client id is empty.");
        }

        string safeRedirectUri = string.IsNullOrWhiteSpace(redirectUri)
            ? OgsConnectionConfig.DefaultRedirectUri
            : redirectUri.Trim();
        if (!CanUseLocalhostCallback(safeRedirectUri)) {
            return new OgsConnectionResult(false, "OGS browser login currently requires a desktop localhost callback. Mobile login needs a deep-link callback implementation.");
        }

        try {
            OgsAuthorizationRequest request = CreateAuthorizationRequest(clientId, safeRedirectUri, scope);
            Task<OgsCallbackResult> callbackTask = WaitForCallbackAsync(safeRedirectUri, request.state, cancellationToken);
            Application.OpenURL(request.authorizationUrl);
            XNLogger.LogInfo("OGS authorization opened in browser.", ("redirectUri", safeRedirectUri));

            OgsCallbackResult callback = await callbackTask;
            if (!callback.success) {
                return new OgsConnectionResult(false, callback.message);
            }

            return await LoginWithAuthorizationCodeAsync(
                clientId,
                callback.code,
                request.codeVerifier,
                request.redirectUri,
                cancellationToken);
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS browser callback login failed.", ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
        }
    }

    public async Task<OgsConnectionResult> RefreshTokenAsync(string clientId, CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        string refreshToken;
        lock (sessionLock) {
            refreshToken = session.refreshToken;
        }
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrEmpty(refreshToken)) {
            return new OgsConnectionResult(false, "OGS refresh token or client id is empty.");
        }

        try {
            JObject tokenJson = await PostFormAsync(
                $"{apiBaseUrl}{OgsConnectionConfig.TokenPath}",
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = clientId.Trim(),
                    ["refresh_token"] = refreshToken,
                },
                null,
                cancellationToken);

            ApplyTokenJson(tokenJson, refreshToken);
            OgsSessionStore.Save(session);
            XNLogger.LogInfo("OGS token refresh succeeded.", ("userId", session.userId ?? string.Empty));
            return new OgsConnectionResult(true, "OGS token refresh succeeded.");
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS token refresh failed.", ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
        }
    }

    public async Task<OgsConnectionResult> RefreshCurrentUserAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }
        if (string.IsNullOrEmpty(accessToken)) {
            return new OgsConnectionResult(false, "OGS access token is empty.");
        }

        try {
            string userId = string.Empty;
            string username = string.Empty;

            JObject meJson = null;
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username)) {
                meJson = await TryGetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.MePath}", accessToken, "me", cancellationToken);
                if (meJson == null) {
                    meJson = await TryGetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.MePathWithoutTrailingSlash}", accessToken, "me-no-slash", cancellationToken);
                }
                ReadCurrentUserFields(meJson, ref userId, ref username);
            }

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username)) {
                JObject uiConfigJson = await TryGetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.UiConfigPath}", accessToken, "ui-config", cancellationToken);
                ReadCurrentUserFields(uiConfigJson, ref userId, ref username);
                ReadCurrentUserFields(uiConfigJson?["user"] as JObject, ref userId, ref username);
                ReadCurrentUserFields(uiConfigJson?["user_info"] as JObject, ref userId, ref username);
                ReadCurrentUserFields(uiConfigJson?["config"]?["user"] as JObject, ref userId, ref username);
            }

            lock (sessionLock) {
                session.userId = userId ?? string.Empty;
                session.username = username ?? string.Empty;
            }
            OgsSessionStore.Save(session);

            if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(username)) {
                return new OgsConnectionResult(false, "OGS current user response did not include a user id or username.");
            }

            XNLogger.LogInfo("OGS current user refreshed.", ("userId", userId ?? string.Empty), ("username", username ?? string.Empty));
            return new OgsConnectionResult(true, "OGS current user refreshed.");
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS current user request failed.", ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
        }
    }

    private async Task<JObject> TryGetJsonAsync(string url, string accessToken, string probeName, CancellationToken cancellationToken)
    {
        try {
            return await GetJsonAsync(url, accessToken, cancellationToken);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS current user probe failed.", ("probe", probeName ?? string.Empty), ("err", ex.Message));
            return null;
        }
    }

    public void Logout()
    {
        EnsureInitialized();
        lock (sessionLock) {
            session.Clear();
        }
        OgsSessionStore.Clear();
        XNLogger.LogInfo("OGS session cleared.");
    }

    public async Task<OgsRealtimeSmokeResult> TestRealtimeAuthenticationAsync(
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }
        if (string.IsNullOrEmpty(accessToken)) {
            return new OgsRealtimeSmokeResult(false, "OGS access token is empty.");
        }
        if (string.IsNullOrWhiteSpace(websocketUrl)) {
            return new OgsRealtimeSmokeResult(false, "OGS websocket URL is empty.");
        }

        try {
            string userJwt = await RequestRealtimeUserJwtAsync(accessToken, cancellationToken);
            if (string.IsNullOrEmpty(userJwt)) {
                return new OgsRealtimeSmokeResult(false, "OGS ui config did not include user_jwt.");
            }

            using (var websocket = new ClientWebSocket()) {
                await websocket.ConnectAsync(new Uri(websocketUrl.Trim()), cancellationToken);
                string authPayload = BuildRealtimeAuthenticatePayload(userJwt);
                byte[] authBytes = Encoding.UTF8.GetBytes(authPayload);
                await websocket.SendAsync(
                    new ArraySegment<byte>(authBytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);

                string firstMessage = await TryReceiveRealtimeMessage(websocket, cancellationToken);
                bool stillOpen = websocket.State == WebSocketState.Open || websocket.State == WebSocketState.CloseReceived;
                if (!stillOpen) {
                    return new OgsRealtimeSmokeResult(false, $"OGS realtime socket closed after authenticate: {websocket.State}", firstMessage);
                }

                XNLogger.LogInfo(
                    "OGS realtime authentication smoke completed.",
                    ("websocketState", websocket.State.ToString()),
                    ("hasFirstMessage", (!string.IsNullOrEmpty(firstMessage)).ToString()));
                return new OgsRealtimeSmokeResult(true, "OGS realtime socket connected and authenticate payload sent.", firstMessage);
            }
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS realtime authentication smoke failed.", ("err", ex.Message));
            return new OgsRealtimeSmokeResult(false, ex.Message);
        }
    }

    public async Task<OgsGameStateSmokeResult> TestReadonlyGameStateAsync(
        int gameId,
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (gameId <= 0) {
            return new OgsGameStateSmokeResult(false, "OGS game id must be positive.", gameId);
        }
        websocketUrl = string.IsNullOrWhiteSpace(websocketUrl) ? OgsConnectionConfig.DefaultWebSocketUrl : websocketUrl.Trim();

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }
        if (string.IsNullOrEmpty(accessToken)) {
            return new OgsGameStateSmokeResult(false, "OGS access token is empty.", gameId);
        }
        if (string.IsNullOrWhiteSpace(websocketUrl)) {
            return new OgsGameStateSmokeResult(false, "OGS websocket URL is empty.", gameId);
        }

        try {
            string userJwt = await RequestRealtimeUserJwtAsync(accessToken, cancellationToken);
            if (string.IsNullOrEmpty(userJwt)) {
                return new OgsGameStateSmokeResult(false, "OGS ui config did not include user_jwt.", gameId);
            }

            using (var websocket = new ClientWebSocket()) {
                await websocket.ConnectAsync(new Uri(websocketUrl.Trim()), cancellationToken);
                await SendRealtimePayloadAsync(websocket, BuildRealtimeAuthenticatePayload(userJwt), cancellationToken);
                await SendRealtimePayloadAsync(websocket, BuildGameConnectPayload(gameId), cancellationToken);

                OgsGameStateSmokeResult result = await WaitForGameDataAsync(websocket, gameId, cancellationToken);
                XNLogger.LogInfo(
                    "OGS readonly game state smoke completed.",
                    ("success", result.success.ToString()),
                    ("gameId", gameId.ToString()),
                    ("board", $"{result.boardWidth}x{result.boardHeight}"),
                    ("moveCount", result.moveCount.ToString()));
                return result;
            }
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS readonly game state smoke failed.", ("gameId", gameId.ToString()), ("err", ex.Message));
            return new OgsGameStateSmokeResult(false, ex.Message, gameId);
        }
    }

    public async Task<OgsBotGameStartResult> StartDefaultBotGameAsync(
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        return await StartBotGameAsync(OgsBotGameCreateParams.Default, websocketUrl, cancellationToken);
    }

    public async Task<OgsBotGameStartResult> StartBotGameAsync(
        OgsBotGameCreateParams createParams,
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        createParams = NormalizeBotGameCreateParams(createParams);
        websocketUrl = string.IsNullOrWhiteSpace(websocketUrl) ? OgsConnectionConfig.DefaultWebSocketUrl : websocketUrl.Trim();
        if (string.IsNullOrWhiteSpace(websocketUrl)) {
            return new OgsBotGameStartResult(false, "OGS websocket URL is empty.");
        }

        OgsConnectionResult accessResult = await EnsureUsableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return new OgsBotGameStartResult(false, accessResult.message);
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        try {
            string userJwt = await RequestRealtimeUserJwtAsync(accessToken, cancellationToken);
            if (string.IsNullOrEmpty(userJwt)) {
                return new OgsBotGameStartResult(false, "OGS ui config did not include user_jwt.");
            }

            using (var websocket = new ClientWebSocket()) {
                await websocket.ConnectAsync(new Uri(websocketUrl.Trim()), cancellationToken);
                await SendRealtimePayloadAsync(websocket, BuildRealtimeAuthenticatePayload(userJwt), cancellationToken);

                JObject activeBots = await WaitForActiveBotsAsync(websocket, cancellationToken);
                if (activeBots == null || activeBots.Count <= 0) {
                    return new OgsBotGameStartResult(false, "OGS did not return any active bots.");
                }

                OgsBotSelection bot = SelectBotForBoard(activeBots, createParams.boardSize);
                if (bot.id <= 0) {
                    return new OgsBotGameStartResult(false, $"No active OGS bot accepted the requested {createParams.boardSize}x{createParams.boardSize} settings.");
                }

                JObject challengePayload = BuildBotChallengePayload(createParams);
                JObject challengeJson = await PostJsonAsync(
                    $"{apiBaseUrl}/api/v1/players/{bot.id}/challenge",
                    challengePayload,
                    accessToken,
                    cancellationToken);

                int gameId = ReadGameIdFromChallengeResponse(challengeJson);
                int challengeId = ReadFirstInt(challengeJson, "challenge", "challenge_id");
                string challengeUuid = ReadFirstString(challengeJson, "uuid", "challenge_uuid");
                if (gameId <= 0) {
                    return new OgsBotGameStartResult(
                        false,
                        "OGS bot challenge response did not include a game id.",
                        bot.id,
                        bot.name,
                        challengeId,
                        challengeUuid,
                        rawResponse: TrimForLog(challengeJson?.ToString(Newtonsoft.Json.Formatting.None)));
                }

                await SendRealtimePayloadAsync(websocket, BuildGameConnectPayload(gameId), cancellationToken);

                OgsGameStateSmokeResult gameState = await WaitForBotGameDataAsync(
                    websocket,
                    gameId,
                    challengeId,
                    OgsConnectionConfig.BotGameStateReceiveMilliseconds,
                    cancellationToken);
                if (!gameState.success) {
                    XNLogger.LogWarn(
                        "OGS bot game created, but game data was not received.",
                        ("gameId", gameId.ToString()),
                        ("botId", bot.id.ToString()),
                        ("botName", bot.name),
                        ("message", gameState.message),
                        ("lastMessage", gameState.rawMessage),
                        ("rawResponse", TrimForLog(challengeJson?.ToString(Newtonsoft.Json.Formatting.None))));
                    return new OgsBotGameStartResult(
                        false,
                        $"OGS bot game created, but game data was not received: {gameState.message}",
                        bot.id,
                        bot.name,
                        challengeId,
                        challengeUuid,
                        gameId,
                        gameState,
                        TrimForLog(challengeJson?.ToString(Newtonsoft.Json.Formatting.None)));
                }

                XNLogger.LogInfo(
                    "OGS bot game started.",
                    ("gameId", gameId.ToString()),
                    ("botId", bot.id.ToString()),
                    ("botName", bot.name),
                    ("requestedBoard", $"{createParams.boardSize}x{createParams.boardSize}"),
                    ("handicap", createParams.handicap.ToString()),
                    ("mainTime", createParams.mainTimeSeconds.ToString()),
                    ("byoyomiPeriods", createParams.byoyomiPeriods.ToString()),
                    ("byoyomiPeriod", createParams.byoyomiPeriodSeconds.ToString()),
                    ("board", $"{gameState.boardWidth}x{gameState.boardHeight}"));
                return new OgsBotGameStartResult(
                    true,
                    "OGS bot game created and game data received.",
                    bot.id,
                    bot.name,
                    challengeId,
                    challengeUuid,
                    gameId,
                    gameState,
                    TrimForLog(challengeJson?.ToString(Newtonsoft.Json.Formatting.None)),
                    true);
            }
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS bot game start failed.", ("err", ex.Message));
            return new OgsBotGameStartResult(false, ex.Message);
        }
    }

    public async Task<OgsBotGameStartResult> StartOrLoadDefaultBotGameAsync(
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        OgsBotGameStartResult activeGameResult = await LoadCurrentActiveGameAsync(websocketUrl, cancellationToken);
        if (activeGameResult != null) {
            return activeGameResult;
        }

        return await StartDefaultBotGameAsync(websocketUrl, cancellationToken);
    }

    public async Task<OgsBotGameStartResult> LoadCurrentActiveGameAsync(
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        websocketUrl = string.IsNullOrWhiteSpace(websocketUrl) ? OgsConnectionConfig.DefaultWebSocketUrl : websocketUrl.Trim();

        OgsConnectionResult accessResult = await EnsureUsableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return new OgsBotGameStartResult(false, accessResult.message);
        }

        string accessToken;
        string userId;
        lock (sessionLock) {
            accessToken = session.accessToken;
            userId = session.userId;
        }

        if (string.IsNullOrWhiteSpace(userId)) {
            OgsConnectionResult userResult = await RefreshCurrentUserAsync(cancellationToken);
            if (!userResult.success) {
                return new OgsBotGameStartResult(false, userResult.message);
            }
            lock (sessionLock) {
                userId = session.userId;
            }
        }

        if (string.IsNullOrWhiteSpace(userId)) {
            return null;
        }

        try {
            string url = $"{apiBaseUrl}/api/v1/players/{Uri.EscapeDataString(userId)}/games?ended__isnull=true";
            JObject gamesJson = await GetJsonAsync(url, accessToken, cancellationToken);
            OgsActiveGameSelection activeGame = SelectCurrentActiveGame(gamesJson, userId);
            if (activeGame.gameId <= 0) {
                return null;
            }

            OgsGameStateSmokeResult gameState = await TestReadonlyGameStateAsync(activeGame.gameId, websocketUrl, cancellationToken);
            if (!gameState.success) {
                return new OgsBotGameStartResult(
                    false,
                    $"OGS active game was found but could not be loaded: {gameState.message}",
                    activeGame.opponentId,
                    activeGame.opponentName,
                    gameId: activeGame.gameId,
                    gameState: gameState,
                    rawResponse: activeGame.rawResponse,
                    isBotGame: activeGame.opponentIsBot);
            }

            XNLogger.LogInfo(
                "OGS active game loaded.",
                ("gameId", activeGame.gameId.ToString()),
                ("opponentId", activeGame.opponentId.ToString()),
                ("opponentName", activeGame.opponentName),
                ("opponentIsBot", activeGame.opponentIsBot.ToString()));
            return new OgsBotGameStartResult(
                true,
                "OGS active game loaded.",
                activeGame.opponentId,
                activeGame.opponentName,
                gameId: activeGame.gameId,
                gameState: gameState,
                rawResponse: activeGame.rawResponse,
                isBotGame: activeGame.opponentIsBot);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS active game lookup failed.", ("err", ex.Message));
            return null;
        }
    }

    public async Task<OgsRealtimeGameSession> CreateRealtimeGameSessionAsync(
        int gameId,
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (gameId <= 0) {
            throw new ArgumentException("OGS game id must be positive.", nameof(gameId));
        }
        websocketUrl = string.IsNullOrWhiteSpace(websocketUrl) ? OgsConnectionConfig.DefaultWebSocketUrl : websocketUrl.Trim();
        if (string.IsNullOrWhiteSpace(websocketUrl)) {
            throw new ArgumentException("OGS websocket URL is empty.", nameof(websocketUrl));
        }

        OgsConnectionResult accessResult = await EnsureUsableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            throw new InvalidOperationException(accessResult.message);
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        string userJwt = await RequestRealtimeUserJwtAsync(accessToken, cancellationToken);
        if (string.IsNullOrEmpty(userJwt)) {
            throw new InvalidOperationException("OGS ui config did not include user_jwt.");
        }

        var websocket = new ClientWebSocket();
        try {
            await websocket.ConnectAsync(new Uri(websocketUrl.Trim()), cancellationToken);
            await SendRealtimePayloadAsync(websocket, BuildRealtimeAuthenticatePayload(userJwt), cancellationToken);
            await SendRealtimePayloadAsync(websocket, BuildGameConnectPayload(gameId), cancellationToken);
            var session = new OgsRealtimeGameSession(websocket, gameId);
            session.StartReceiveLoop();
            XNLogger.LogInfo("OGS realtime game session connected.", ("gameId", gameId.ToString()));
            return session;
        }
        catch {
            websocket.Dispose();
            throw;
        }
    }

    private async Task<OgsConnectionResult> EnsureUsableAccessTokenAsync(CancellationToken cancellationToken)
    {
        string accessToken;
        bool isExpired;
        bool canRefresh;
        string scope;
        lock (sessionLock) {
            accessToken = session.accessToken;
            isExpired = session.IsExpired;
            canRefresh = session.CanRefresh;
            scope = session.scope ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(scope) && !ContainsScope(scope, "write")) {
            return new OgsConnectionResult(false, "当前 OGS 授权缺少 write 权限，请重新登录 OGS 后再创建对局。");
        }

        if (!string.IsNullOrEmpty(accessToken) && !isExpired) {
            return new OgsConnectionResult(true, "OGS access token is available.");
        }

        if (canRefresh) {
            return await RefreshTokenAsync(OgsConnectionConfig.DefaultClientId, cancellationToken);
        }

        return new OgsConnectionResult(false, "请先登录 OGS。");
    }

    private async Task<string> RequestRealtimeUserJwtAsync(string accessToken, CancellationToken cancellationToken)
    {
        JObject configJson = await GetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.UiConfigPath}", accessToken, cancellationToken);
        return ReadFirstString(configJson, "user_jwt", "jwt");
    }

    private static bool CanUseLocalhostCallback(string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri)) {
            return false;
        }
        if (!redirectUri.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) &&
            !redirectUri.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        return false;
#else
        return true;
#endif
    }

    private static async Task<OgsCallbackResult> WaitForCallbackAsync(
        string redirectUri,
        string expectedState,
        CancellationToken cancellationToken)
    {
        string prefix = BuildHttpListenerPrefix(redirectUri);
        using (var listener = new HttpListener()) {
            try {
                listener.Prefixes.Add(prefix);
                listener.Start();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                    timeout.CancelAfter(120000);
                    HttpListenerContext context = await WaitForContextAsync(listener, timeout.Token);
                    if (!IsExpectedCallbackPath(context.Request.Url, redirectUri)) {
                        WriteCallbackResponse(context, false);
                        return new OgsCallbackResult(false, $"OGS callback path mismatch: {context.Request.Url?.AbsolutePath ?? string.Empty}");
                    }

                    string code = context.Request.QueryString["code"] ?? string.Empty;
                    string state = context.Request.QueryString["state"] ?? string.Empty;
                    string error = context.Request.QueryString["error"] ?? string.Empty;
                    WriteCallbackResponse(context, string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code));

                    if (!string.IsNullOrEmpty(error)) {
                        return new OgsCallbackResult(false, $"OGS authorization failed: {error}");
                    }
                    if (string.IsNullOrEmpty(code)) {
                        return new OgsCallbackResult(false, "OGS callback did not include a code.");
                    }
                    if (!string.IsNullOrEmpty(expectedState) && state != expectedState) {
                        return new OgsCallbackResult(false, "OGS callback state mismatch.");
                    }

                    return new OgsCallbackResult(true, "OGS callback received.", code);
                }
            }
            catch (OperationCanceledException) {
                return new OgsCallbackResult(false, "Timed out waiting for OGS callback.");
            }
            catch (Exception ex) {
                return new OgsCallbackResult(false, $"Start OGS callback listener failed: {ex.Message}");
            }
            finally {
                if (listener.IsListening) {
                    listener.Stop();
                }
            }
        }
    }

    private static Task<HttpListenerContext> WaitForContextAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        return Task.Run(() => {
            using (cancellationToken.Register(() => {
                try {
                    listener.Stop();
                }
                catch {
                }
            })) {
                return listener.GetContext();
            }
        }, cancellationToken);
    }

    private static string BuildHttpListenerPrefix(string redirectUri)
    {
        var uri = new Uri(redirectUri);
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}/";
    }

    private static bool IsExpectedCallbackPath(Uri callbackUri, string redirectUri)
    {
        if (callbackUri == null) {
            return false;
        }

        var expectedUri = new Uri(redirectUri);
        string actualPath = NormalizeCallbackPath(callbackUri.AbsolutePath);
        string expectedPath = NormalizeCallbackPath(expectedUri.AbsolutePath);
        return string.Equals(actualPath, expectedPath, StringComparison.Ordinal);
    }

    private static string NormalizeCallbackPath(string path)
    {
        if (string.IsNullOrEmpty(path)) {
            return "/";
        }

        string normalized = path.TrimEnd('/');
        return string.IsNullOrEmpty(normalized) ? "/" : normalized;
    }

    private static void WriteCallbackResponse(HttpListenerContext context, bool success)
    {
        string body = success
            ? "OGS login code received. You can return to WeiqiXN."
            : "OGS login failed. You can return to WeiqiXN.";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    private async Task<JObject> GetJsonAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using (HttpClient client = CreateHttpClient())
        using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using (HttpResponseMessage response = await SendOgsRequestAsync(client, request, "GET", cancellationToken)) {
                string body = await response.Content.ReadAsStringAsync();
                LogVerboseHttpResponse("GET", url, response, body);
                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"OGS GET failed: {(int)response.StatusCode} {response.ReasonPhrase} {TrimForLog(body)}");
                }
                return JObject.Parse(body);
            }
        }
    }

    private async Task<JObject> PostJsonAsync(
        string url,
        JObject json,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using (HttpClient client = CreateHttpClient())
        using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
            request.Content = new StringContent(
                (json ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
            if (!string.IsNullOrEmpty(accessToken)) {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            }

            using (HttpResponseMessage response = await SendOgsRequestAsync(client, request, "POST", cancellationToken)) {
                string body = await response.Content.ReadAsStringAsync();
                LogVerboseHttpResponse("POST", url, response, body);
                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"OGS POST failed: {(int)response.StatusCode} {response.ReasonPhrase} {TrimForLog(body)}");
                }
                return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
            }
        }
    }

    private async Task<JObject> PostFormAsync(
        string url,
        Dictionary<string, string> form,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using (HttpClient client = CreateHttpClient())
        using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
            request.Content = new FormUrlEncodedContent(form);
            if (!string.IsNullOrEmpty(accessToken)) {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            }

            using (HttpResponseMessage response = await SendOgsRequestAsync(client, request, "POST", cancellationToken)) {
                string body = await response.Content.ReadAsStringAsync();
                LogVerboseHttpResponse("POST", url, response, body);
                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"OGS POST failed: {(int)response.StatusCode} {response.ReasonPhrase} {TrimForLog(body)}");
                }
                return JObject.Parse(body);
            }
        }
    }

    private HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(OgsConnectionConfig.RequestTimeoutMilliseconds),
        };
        return client;
    }

    private static async Task<HttpResponseMessage> SendOgsRequestAsync(
        HttpClient client,
        HttpRequestMessage request,
        string method,
        CancellationToken cancellationToken)
    {
        try {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(
                $"OGS {method} timed out: {request.RequestUri} ({OgsConnectionConfig.RequestTimeoutMilliseconds} ms). {DescribeException(ex)}",
                ex);
        }
        catch (Exception ex) {
            throw new InvalidOperationException(
                $"OGS {method} send failed: {request.RequestUri}. {DescribeException(ex)}",
                ex);
        }
    }

    private static string BuildRealtimeAuthenticatePayload(string userJwt)
    {
        var payload = new JArray
        {
            "authenticate",
            new JObject
            {
                ["jwt"] = userJwt,
            },
        };
        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string BuildGameConnectPayload(int gameId)
    {
        var payload = new JArray
        {
            "game/connect",
            new JObject
            {
                ["game_id"] = gameId,
                ["chat"] = false,
            },
        };
        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string BuildChallengeKeepalivePayload(int challengeId, int gameId)
    {
        var payload = new JArray
        {
            "challenge/keepalive",
            new JObject
            {
                ["challenge_id"] = challengeId,
                ["game_id"] = gameId,
            },
        };
        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static OgsBotGameCreateParams NormalizeBotGameCreateParams(OgsBotGameCreateParams createParams)
    {
        createParams = createParams ?? OgsBotGameCreateParams.Default;
        int boardSize = createParams.boardSize > 0 ? createParams.boardSize : OgsConnectionConfig.DefaultBotGameBoardSize;
        int mainTimeSeconds = createParams.mainTimeSeconds;
        int byoyomiPeriods = Math.Max(0, createParams.byoyomiPeriods);
        int byoyomiPeriodSeconds = Math.Max(0, createParams.byoyomiPeriodSeconds);
        int handicap = Math.Max(0, createParams.handicap);
        string challengerColor = NormalizeChallengerColor(createParams.challengerColor);
        string gameName = string.IsNullOrWhiteSpace(createParams.gameName)
            ? OgsConnectionConfig.DefaultBotGameName
            : createParams.gameName.Trim();
        return new OgsBotGameCreateParams(
            boardSize,
            mainTimeSeconds,
            byoyomiPeriods,
            byoyomiPeriodSeconds,
            handicap,
            createParams.komi,
            challengerColor,
            gameName);
    }

    private static JObject BuildBotChallengePayload(OgsBotGameCreateParams createParams)
    {
        createParams = NormalizeBotGameCreateParams(createParams);
        JObject game = new JObject
        {
            ["name"] = createParams.gameName,
            ["rules"] = OgsConnectionConfig.DefaultBotGameRules,
            ["ranked"] = false,
            ["width"] = createParams.boardSize,
            ["height"] = createParams.boardSize,
            ["handicap"] = createParams.handicap,
            ["komi_auto"] = "custom",
            ["komi"] = createParams.komi,
            ["disable_analysis"] = false,
            ["initial_state"] = JValue.CreateNull(),
            ["private"] = false,
            ["rengo"] = false,
            ["rengo_casual_mode"] = true,
            ["pause_on_weekends"] = false,
        };

        ApplyTimeControlPayload(game, createParams);

        return new JObject
        {
            ["initialized"] = false,
            ["min_ranking"] = -1000,
            ["max_ranking"] = 1000,
            ["challenger_color"] = createParams.challengerColor,
            ["rengo_auto_start"] = 0,
            ["game"] = game,
            ["aga_ranked"] = false,
        };
    }

    private static void ApplyTimeControlPayload(JObject game, OgsBotGameCreateParams createParams)
    {
        if (game == null) {
            return;
        }

        if (createParams.mainTimeSeconds <= 0) {
            game["time_control"] = "none";
            game["time_control_parameters"] = new JObject
            {
                ["system"] = "none",
                ["time_control"] = "none",
            };
            return;
        }

        if (createParams.byoyomiPeriods > 0 && createParams.byoyomiPeriodSeconds > 0) {
            game["time_control"] = "byoyomi";
            game["time_control_parameters"] = new JObject
            {
                ["main_time"] = createParams.mainTimeSeconds,
                ["period_time"] = createParams.byoyomiPeriodSeconds,
                ["periods"] = createParams.byoyomiPeriods,
                ["periods_min"] = 1,
                ["periods_max"] = 300,
                ["pause_on_weekends"] = false,
                ["speed"] = "live",
                ["system"] = "byoyomi",
                ["time_control"] = "byoyomi",
            };
            return;
        }

        game["time_control"] = "absolute";
        game["time_control_parameters"] = new JObject
        {
            ["total_time"] = createParams.mainTimeSeconds,
            ["pause_on_weekends"] = false,
            ["speed"] = "live",
            ["system"] = "absolute",
            ["time_control"] = "absolute",
        };
    }

    private static string NormalizeChallengerColor(string challengerColor)
    {
        if (string.Equals(challengerColor, "black", StringComparison.OrdinalIgnoreCase)) {
            return "black";
        }
        if (string.Equals(challengerColor, "white", StringComparison.OrdinalIgnoreCase)) {
            return "white";
        }
        return "automatic";
    }

    private static async Task SendRealtimePayloadAsync(ClientWebSocket websocket, string payload, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await websocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
        LogVerboseRealtimePayload("OGS transient realtime sent.", payload);
    }

    private static async Task<string> TryReceiveRealtimeMessage(ClientWebSocket websocket, CancellationToken cancellationToken)
    {
        using (var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            receiveCancellation.CancelAfter(OgsConnectionConfig.WebSocketSmokeReceiveMilliseconds);
            byte[] buffer = new byte[4096];
            var messageBuilder = new StringBuilder();
            try {
                WebSocketReceiveResult result;
                do {
                    result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), receiveCancellation.Token);
                    if (result.MessageType == WebSocketMessageType.Close) {
                        string closeMessage = messageBuilder.ToString();
                        LogVerboseRealtimePayload("OGS transient realtime received.", closeMessage);
                        return closeMessage;
                    }
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException) {
                return string.Empty;
            }

            string message = messageBuilder.ToString();
            LogVerboseRealtimePayload("OGS transient realtime received.", message);
            return message;
        }
    }

    private static async Task<OgsGameStateSmokeResult> WaitForGameDataAsync(ClientWebSocket websocket, int gameId, CancellationToken cancellationToken)
    {
        return await WaitForGameDataAsync(
            websocket,
            gameId,
            OgsConnectionConfig.GameStateSmokeReceiveMilliseconds,
            cancellationToken);
    }

    private static async Task<OgsGameStateSmokeResult> WaitForGameDataAsync(
        ClientWebSocket websocket,
        int gameId,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        string lastObservedMessage = string.Empty;
        using (var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            receiveCancellation.CancelAfter(timeoutMilliseconds);
            try {
                while (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.CloseReceived) {
                    string message = await ReceiveRealtimeMessageAsync(websocket, receiveCancellation.Token);
                    if (string.IsNullOrEmpty(message)) {
                        continue;
                    }

                    OgsGameStateSmokeResult result = TryParseGameStateSmokeMessage(message, gameId);
                    if (result != null) {
                        return result;
                    }

                    lastObservedMessage = DescribeRealtimeMessageForLog(message);
                }
            }
            catch (OperationCanceledException) {
                string detail = string.IsNullOrEmpty(lastObservedMessage)
                    ? string.Empty
                    : $" Last OGS realtime message: {lastObservedMessage}";
                return new OgsGameStateSmokeResult(false, $"Timed out waiting for OGS game data.{detail}", gameId, rawMessage: lastObservedMessage);
            }
        }

        string closeDetail = string.IsNullOrEmpty(lastObservedMessage)
            ? string.Empty
            : $" Last OGS realtime message: {lastObservedMessage}";
        return new OgsGameStateSmokeResult(false, $"OGS websocket closed before game data: {websocket.State}.{closeDetail}", gameId, rawMessage: lastObservedMessage);
    }

    private static async Task<OgsGameStateSmokeResult> WaitForBotGameDataAsync(
        ClientWebSocket websocket,
        int gameId,
        int challengeId,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using (var keepaliveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            Task keepaliveTask = challengeId > 0
                ? SendChallengeKeepaliveLoopAsync(websocket, challengeId, gameId, keepaliveCancellation.Token)
                : Task.CompletedTask;
            try {
                return await WaitForGameDataAsync(websocket, gameId, timeoutMilliseconds, cancellationToken);
            }
            finally {
                keepaliveCancellation.Cancel();
                try {
                    await keepaliveTask;
                }
                catch (OperationCanceledException) {
                }
                catch (Exception ex) {
                    XNLogger.LogWarn("OGS challenge keepalive loop failed.", ("gameId", gameId.ToString()), ("challengeId", challengeId.ToString()), ("err", ex.Message));
                }
            }
        }
    }

    private static async Task SendChallengeKeepaliveLoopAsync(
        ClientWebSocket websocket,
        int challengeId,
        int gameId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && websocket.State == WebSocketState.Open) {
            await SendRealtimePayloadAsync(websocket, BuildChallengeKeepalivePayload(challengeId, gameId), cancellationToken);
            await Task.Delay(1000, cancellationToken);
        }
    }

    private static async Task<JObject> WaitForActiveBotsAsync(ClientWebSocket websocket, CancellationToken cancellationToken)
    {
        using (var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            receiveCancellation.CancelAfter(OgsConnectionConfig.ActiveBotsReceiveMilliseconds);
            try {
                while (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.CloseReceived) {
                    string message = await ReceiveRealtimeMessageAsync(websocket, receiveCancellation.Token);
                    JObject activeBots = TryParseActiveBotsMessage(message);
                    if (activeBots != null) {
                        return activeBots;
                    }
                }
            }
            catch (OperationCanceledException) {
                return null;
            }
        }

        return null;
    }

    private static async Task<string> ReceiveRealtimeMessageAsync(ClientWebSocket websocket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        var messageBuilder = new StringBuilder();
        WebSocketReceiveResult result;
        do {
            result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) {
                string closeMessage = messageBuilder.ToString();
                LogVerboseRealtimePayload("OGS transient realtime received.", closeMessage);
                return closeMessage;
            }
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        string message = messageBuilder.ToString();
        LogVerboseRealtimePayload("OGS transient realtime received.", message);
        return message;
    }

    private static OgsGameStateSmokeResult TryParseGameStateSmokeMessage(string message, int gameId)
    {
        JArray envelope = TryParseArray(message);
        if (envelope == null || envelope.Count < 2) {
            return null;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        if (channel == $"game/{gameId}/error") {
            return new OgsGameStateSmokeResult(false, $"OGS game connect error: {TrimForLog(envelope[1]?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty)}", gameId, rawMessage: TrimForLog(message));
        }
        if (channel == $"game/{gameId}/rejected") {
            return new OgsGameStateSmokeResult(false, $"OGS game offer was rejected: {TrimForLog(envelope[1]?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty)}", gameId, rawMessage: TrimForLog(message));
        }
        if (TryParseGameOfferRejectedMessage(envelope, gameId, out string rejectionMessage)) {
            return new OgsGameStateSmokeResult(false, rejectionMessage, gameId, rawMessage: TrimForLog(message));
        }
        if (channel != $"game/{gameId}/gamedata") {
            return null;
        }

        JObject gameData = envelope[1] as JObject;
        if (gameData == null) {
            return new OgsGameStateSmokeResult(false, "OGS game data payload is not an object.", gameId, rawMessage: TrimForLog(message));
        }

        int width = ReadFirstInt(gameData, "width", "board_width", "size");
        int height = ReadFirstInt(gameData, "height", "board_height", "size");
        JArray moves = gameData["moves"] as JArray;
        int moveCount = moves?.Count ?? ReadFirstInt(gameData, "move_number", "moveNumber", "turn_number");
        string phase = ReadFirstString(gameData, "phase", "state", "game_state");
        string blackPlayer = ReadPlayerName(gameData["players"]?["black"] as JObject);
        string whitePlayer = ReadPlayerName(gameData["players"]?["white"] as JObject);

        if (string.IsNullOrEmpty(blackPlayer)) {
            blackPlayer = ReadPlayerName(gameData["black_player"] as JObject);
        }
        if (string.IsNullOrEmpty(whitePlayer)) {
            whitePlayer = ReadPlayerName(gameData["white_player"] as JObject);
        }

        return new OgsGameStateSmokeResult(
            true,
            "OGS game data received.",
            gameId,
            width,
            height,
            moveCount,
            blackPlayer,
            whitePlayer,
            phase,
            TrimForLog(message));
    }

    private static JObject TryParseActiveBotsMessage(string message)
    {
        JArray envelope = TryParseArray(message);
        if (envelope == null || envelope.Count < 2) {
            return null;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        if (channel != "active-bots") {
            return null;
        }

        return envelope[1] as JObject;
    }

    private static bool TryParseGameOfferRejectedMessage(JArray envelope, int gameId, out string message)
    {
        message = string.Empty;
        if (envelope == null || envelope.Count < 2) {
            return false;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        if (!channel.Contains("notification")) {
            return false;
        }

        JObject payload = envelope[1] as JObject;
        if (payload == null) {
            return false;
        }

        string type = ReadFirstString(payload, "type", "notification_type");
        if (!string.Equals(type, "gameOfferRejected", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        int rejectedGameId = ReadFirstInt(payload, "game_id", "gameId");
        if (rejectedGameId > 0 && rejectedGameId != gameId) {
            return false;
        }

        string serverMessage = ReadFirstString(payload, "message", "text", "reason");
        JObject details = payload["rejection_details"] as JObject;
        if (details != null && string.IsNullOrEmpty(serverMessage)) {
            serverMessage = details.ToString(Newtonsoft.Json.Formatting.None);
        }

        message = string.IsNullOrEmpty(serverMessage)
            ? "OGS game offer was rejected."
            : $"OGS game offer was rejected: {TrimForLog(serverMessage)}";
        return true;
    }

    private static OgsBotSelection SelectBotForBoard(JObject activeBots, int boardSize)
    {
        if (activeBots == null) {
            return default(OgsBotSelection);
        }

        foreach (JProperty property in activeBots.Properties()) {
            JObject botJson = property.Value as JObject;
            if (botJson == null) {
                continue;
            }

            int botId = ReadFirstInt(botJson, "id", "user_id");
            if (botId <= 0 && !int.TryParse(property.Name, out botId)) {
                continue;
            }

            string botName = ReadFirstString(botJson, "username", "name");
            var candidate = new OgsBotSelection(botId, botName);
            if (CanBotPlayBoard(botJson["config"] as JObject, boardSize)) {
                return candidate;
            }
        }

        return default(OgsBotSelection);
    }

    private static bool CanBotPlayBoard(JObject config, int boardSize)
    {
        if (config == null) {
            return true;
        }

        JToken sizes = config["allowed_board_sizes"];
        boardSize = boardSize > 0 ? boardSize : OgsConnectionConfig.DefaultBotGameBoardSize;
        if (sizes == null || sizes.Type == JTokenType.Null) {
            return true;
        }
        if (sizes.Type == JTokenType.String) {
            string value = sizes.ToString();
            return value == "all" || value == "square";
        }
        if (sizes.Type == JTokenType.Integer) {
            return sizes.ToObject<int>() == boardSize;
        }
        if (sizes is JArray sizeArray) {
            if (sizeArray.Count == 1 && sizeArray[0]?.ToObject<int>() == 0) {
                return true;
            }
            foreach (JToken size in sizeArray) {
                if (size.Type == JTokenType.Integer && size.ToObject<int>() == boardSize) {
                    return true;
                }
            }
        }

        return false;
    }

    private static int ReadGameIdFromChallengeResponse(JObject json)
    {
        if (json == null) {
            return 0;
        }

        int gameId = ReadFirstInt(json, "game", "game_id");
        if (gameId > 0) {
            return gameId;
        }

        return ReadFirstInt(json["game"] as JObject, "id", "game_id");
    }

    private static OgsActiveGameSelection SelectCurrentActiveGame(JObject gamesJson, string userId)
    {
        if (gamesJson == null) {
            return default(OgsActiveGameSelection);
        }

        JArray results = gamesJson["results"] as JArray;
        if (results == null || results.Count <= 0) {
            return default(OgsActiveGameSelection);
        }

        int.TryParse(userId, out int localUserId);
        int defaultBoardSize = OgsConnectionConfig.DefaultBotGameBoardSize;
        OgsActiveGameSelection best = default(OgsActiveGameSelection);
        int bestScore = -1;
        foreach (JToken token in results) {
            JObject gameJson = token as JObject;
            if (gameJson == null || HasNonNullField(gameJson, "ended")) {
                continue;
            }

            int gameId = ReadFirstInt(gameJson, "id", "game_id");
            int width = ReadFirstInt(gameJson, "width", "board_width", "size");
            int height = ReadFirstInt(gameJson, "height", "board_height", "size");
            if (gameId <= 0 || width <= 0 || width != height) {
                continue;
            }

            JObject playersJson = gameJson["players"] as JObject;
            JToken blackPlayerToken = playersJson?["black"] ?? gameJson["black_player"] ?? gameJson["black"];
            JToken whitePlayerToken = playersJson?["white"] ?? gameJson["white_player"] ?? gameJson["white"];
            JObject blackPlayer = ReadPlayerObject(blackPlayerToken);
            JObject whitePlayer = ReadPlayerObject(whitePlayerToken);
            int blackId = ReadPlayerId(blackPlayerToken, gameJson, "black", "black_id", "black_player_id");
            int whiteId = ReadPlayerId(whitePlayerToken, gameJson, "white", "white_id", "white_player_id");
            if (localUserId > 0 && blackId != localUserId && whiteId != localUserId) {
                continue;
            }

            JObject opponentPlayer = blackId == localUserId ? whitePlayer : blackPlayer;
            int opponentId = blackId == localUserId ? whiteId : blackId;
            string opponentName = ReadPlayerName(opponentPlayer);
            bool opponentIsBot = IsBotPlayer(opponentPlayer);
            int score = 1;
            if (width == defaultBoardSize) {
                score += 10;
            }
            if (!opponentIsBot) {
                score += 100;
            }

            if (score > bestScore) {
                bestScore = score;
                best = new OgsActiveGameSelection(
                    gameId,
                    opponentId,
                    opponentName,
                    width,
                    height,
                    opponentIsBot,
                    TrimForLog(gameJson.ToString(Newtonsoft.Json.Formatting.None)));
            }
        }

        return best;
    }

    private static bool HasNonNullField(JObject json, string fieldName)
    {
        JToken token = json?[fieldName];
        return token != null && token.Type != JTokenType.Null && !string.IsNullOrEmpty(token.ToString());
    }

    private static bool IsBotPlayer(JObject playerJson)
    {
        string uiClass = ReadFirstString(playerJson, "ui_class", "class");
        if (string.IsNullOrWhiteSpace(uiClass)) {
            uiClass = ReadFirstString(playerJson?["user"] as JObject, "ui_class", "class");
        }
        if (string.IsNullOrWhiteSpace(uiClass)) {
            uiClass = ReadFirstString(playerJson?["player"] as JObject, "ui_class", "class");
        }
        return uiClass.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsScope(string scope, string expectedScope)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(expectedScope)) {
            return false;
        }

        string[] parts = scope.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts) {
            if (string.Equals(part.Trim(), expectedScope, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static JArray TryParseArray(string json)
    {
        try {
            return JArray.Parse(json);
        }
        catch {
            return null;
        }
    }

    private void ApplyTokenJson(JObject tokenJson, string fallbackRefreshToken = "")
    {
        if (tokenJson == null) {
            throw new InvalidOperationException("OGS token response is empty.");
        }

        string accessToken = tokenJson["access_token"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(accessToken)) {
            throw new InvalidOperationException("OGS token response does not include access_token.");
        }

        int expiresIn = tokenJson["expires_in"]?.ToObject<int>() ?? 0;
        lock (sessionLock) {
            session.accessToken = accessToken;
            session.refreshToken = tokenJson["refresh_token"]?.ToString() ?? fallbackRefreshToken ?? string.Empty;
            session.tokenType = tokenJson["token_type"]?.ToString() ?? "Bearer";
            session.scope = tokenJson["scope"]?.ToString() ?? string.Empty;
            session.expiresAtUtc = expiresIn > 0 ? DateTime.UtcNow.AddSeconds(expiresIn) : DateTime.MinValue;
        }
    }

    private static OgsSession CloneSession(OgsSession source)
    {
        return new OgsSession
        {
            accessToken = source.accessToken,
            refreshToken = source.refreshToken,
            tokenType = source.tokenType,
            scope = source.scope,
            expiresAtUtc = source.expiresAtUtc,
            userId = source.userId,
            username = source.username,
        };
    }

    private static string CreatePkceVerifier()
    {
        byte[] bytes = new byte[32];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) {
            rng.GetBytes(bytes);
        }
        return Base64UrlEncode(bytes);
    }

    private static string CreatePkceChallenge(string verifier)
    {
        using (SHA256 sha256 = SHA256.Create()) {
            return Base64UrlEncode(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ReadFirstString(JObject json, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return string.Empty;
        }

        foreach (string fieldName in fieldNames) {
            string value = json[fieldName]?.ToString();
            if (!string.IsNullOrEmpty(value)) {
                return value;
            }
        }

        return string.Empty;
    }

    private static int ReadFirstInt(JObject json, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return 0;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token != null && int.TryParse(token.ToString(), out int value)) {
                return value;
            }
        }

        return 0;
    }

    private static string ReadPlayerName(JObject playerJson)
    {
        if (playerJson == null) {
            return string.Empty;
        }

        string value = ReadFirstString(playerJson, "username", "name", "professional_name", "id");
        if (!string.IsNullOrWhiteSpace(value)) {
            return value;
        }

        value = ReadFirstString(playerJson["user"] as JObject, "username", "name", "professional_name", "id");
        if (!string.IsNullOrWhiteSpace(value)) {
            return value;
        }

        return ReadFirstString(playerJson["player"] as JObject, "username", "name", "professional_name", "id");
    }

    private static JObject ReadPlayerObject(JToken token)
    {
        return token as JObject;
    }

    private static int ReadPlayerId(JToken playerToken, JObject gameJson, params string[] topLevelFieldNames)
    {
        int id = ReadFirstInt(playerToken, 0);
        if (id > 0) {
            return id;
        }

        if (playerToken is JObject playerObj) {
            id = ReadFirstInt(playerObj, "id", "user_id", "player_id", "pk", "uid");
            if (id > 0) {
                return id;
            }

            id = ReadFirstInt(playerObj["user"] as JObject, "id", "user_id", "player_id", "pk", "uid");
            if (id > 0) {
                return id;
            }

            id = ReadFirstInt(playerObj["player"] as JObject, "id", "user_id", "player_id", "pk", "uid");
            if (id > 0) {
                return id;
            }
        }

        return ReadFirstInt(gameJson, topLevelFieldNames);
    }

    private static int ReadFirstInt(JToken token, int defaultValue)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return defaultValue;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.String) {
            return int.TryParse(token.ToString(), out int value) ? value : defaultValue;
        }

        return defaultValue;
    }

    private static void ReadCurrentUserFields(JObject json, ref string userId, ref string username)
    {
        if (json == null) {
            return;
        }

        if (string.IsNullOrEmpty(userId)) {
            userId = ReadFirstString(json, "sub", "id", "user_id", "pk", "uid");
        }
        if (string.IsNullOrEmpty(username)) {
            username = ReadFirstString(json, "preferred_username", "username", "name", "display_name");
        }
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        return value.Length <= 300 ? value : value.Substring(0, 300);
    }

    private static void LogVerboseHttpResponse(string method, string url, HttpResponseMessage response, string body)
    {
        if (!LoggerConfig.ENABLE_OGS_VERBOSE_LOG) {
            return;
        }

        XNLogger.LogInfo(
            "OGS HTTP response.",
            ("method", method ?? string.Empty),
            ("url", url ?? string.Empty),
            ("status", response != null ? ((int)response.StatusCode).ToString() : string.Empty),
            ("reason", response?.ReasonPhrase ?? string.Empty),
            ("body", RedactSensitiveOgsLogPayload(body)));
    }

    private static void LogVerboseRealtimePayload(string message, string payload)
    {
        if (!LoggerConfig.ENABLE_OGS_VERBOSE_LOG) {
            return;
        }

        XNLogger.LogInfo(
            message,
            ("payload", RedactSensitiveOgsLogPayload(payload)));
    }

    private static string RedactSensitiveOgsLogPayload(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        try {
            JToken token = JToken.Parse(value);
            RedactSensitiveOgsLogFields(token);
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch {
            return value;
        }
    }

    private static void RedactSensitiveOgsLogFields(JToken token)
    {
        if (token is JObject obj) {
            foreach (JProperty property in obj.Properties()) {
                if (IsSensitiveOgsLogField(property.Name)) {
                    property.Value = "[redacted]";
                } else {
                    RedactSensitiveOgsLogFields(property.Value);
                }
            }
            return;
        }

        if (token is JArray array) {
            foreach (JToken item in array) {
                RedactSensitiveOgsLogFields(item);
            }
        }
    }

    private static bool IsSensitiveOgsLogField(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) {
            return false;
        }

        string lower = fieldName.Trim().ToLowerInvariant();
        return lower.Contains("token") ||
            lower.Contains("jwt") ||
            lower.Contains("authorization") ||
            lower.Contains("password") ||
            lower == "code_verifier" ||
            lower == "code_challenge";
    }

    private static string DescribeRealtimeMessageForLog(string message)
    {
        JArray envelope = TryParseArray(message);
        if (envelope == null || envelope.Count <= 0) {
            return TrimForLog(message);
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        string payload = envelope.Count > 1
            ? envelope[1]?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty
            : string.Empty;
        return TrimForLog($"{channel} {payload}");
    }

    private static string DescribeException(Exception ex)
    {
        if (ex == null) {
            return string.Empty;
        }

        var builder = new StringBuilder();
        Exception current = ex;
        int depth = 0;
        while (current != null && depth < 4) {
            if (builder.Length > 0) {
                builder.Append(" Inner: ");
            }
            builder.Append(current.GetType().Name);
            if (!string.IsNullOrEmpty(current.Message)) {
                builder.Append(": ");
                builder.Append(current.Message);
            }

            current = current.InnerException;
            depth += 1;
        }

        return builder.ToString();
    }

    private struct OgsBotSelection
    {
        public readonly int id;
        public readonly string name;

        public OgsBotSelection(int id, string name)
        {
            this.id = id;
            this.name = name ?? string.Empty;
        }
    }

    private struct OgsActiveGameSelection
    {
        public readonly int gameId;
        public readonly int opponentId;
        public readonly string opponentName;
        public readonly int boardWidth;
        public readonly int boardHeight;
        public readonly bool opponentIsBot;
        public readonly string rawResponse;

        public OgsActiveGameSelection(
            int gameId,
            int opponentId,
            string opponentName,
            int boardWidth,
            int boardHeight,
            bool opponentIsBot,
            string rawResponse)
        {
            this.gameId = gameId;
            this.opponentId = opponentId;
            this.opponentName = opponentName ?? string.Empty;
            this.boardWidth = boardWidth;
            this.boardHeight = boardHeight;
            this.opponentIsBot = opponentIsBot;
            this.rawResponse = rawResponse ?? string.Empty;
        }
    }
}
