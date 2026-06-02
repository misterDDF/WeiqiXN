using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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
        string redirectUri = OgsConnectionConfig.DefaultRedirectUri,
        string scope = OgsConnectionConfig.DefaultScope)
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
        string redirectUri = OgsConnectionConfig.DefaultRedirectUri,
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
        string websocketUrl = OgsConnectionConfig.DefaultWebSocketUrl,
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
        string websocketUrl = OgsConnectionConfig.DefaultWebSocketUrl,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (gameId <= 0) {
            return new OgsGameStateSmokeResult(false, "OGS game id must be positive.", gameId);
        }

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

    private async Task<string> RequestRealtimeUserJwtAsync(string accessToken, CancellationToken cancellationToken)
    {
        JObject configJson = await GetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.UiConfigPath}", accessToken, cancellationToken);
        return ReadFirstString(configJson, "user_jwt", "jwt");
    }

    private async Task<JObject> GetJsonAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using (HttpClient client = CreateHttpClient())
        using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken)) {
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"OGS GET failed: {(int)response.StatusCode} {response.ReasonPhrase} {TrimForLog(body)}");
                }
                return JObject.Parse(body);
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

            using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken)) {
                string body = await response.Content.ReadAsStringAsync();
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

    private static async Task SendRealtimePayloadAsync(ClientWebSocket websocket, string payload, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await websocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
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
                        return messageBuilder.ToString();
                    }
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException) {
                return string.Empty;
            }

            return messageBuilder.ToString();
        }
    }

    private static async Task<OgsGameStateSmokeResult> WaitForGameDataAsync(ClientWebSocket websocket, int gameId, CancellationToken cancellationToken)
    {
        using (var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            receiveCancellation.CancelAfter(OgsConnectionConfig.GameStateSmokeReceiveMilliseconds);
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
                }
            }
            catch (OperationCanceledException) {
                return new OgsGameStateSmokeResult(false, "Timed out waiting for OGS game data.", gameId);
            }
        }

        return new OgsGameStateSmokeResult(false, $"OGS websocket closed before game data: {websocket.State}", gameId);
    }

    private static async Task<string> ReceiveRealtimeMessageAsync(ClientWebSocket websocket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        var messageBuilder = new StringBuilder();
        WebSocketReceiveResult result;
        do {
            result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) {
                return messageBuilder.ToString();
            }
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        return messageBuilder.ToString();
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

        return ReadFirstString(playerJson, "username", "name", "professional_name", "id");
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
}
