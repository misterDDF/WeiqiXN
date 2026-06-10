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
    public const string FriendChallengeDeclinedMessage = "对方已拒绝邀请。";

    private const int ChallengeGameDataPollMilliseconds = 1500;
    private const int BrowserLoginCallbackTimeoutMilliseconds = 120000;
    private const string MobileOauthRedirectUri = "https://leo-zhang-git.github.io/weiqixn-oauth-redirect/ogs/callback/";
    private const string MobileDeepLinkCallbackUri = "weiqixn://ogs/callback";
    private const string FriendStatusOnlineText = "\u5728\u7ebf";
    private const string FriendStatusOfflineText = "\u79bb\u7ebf";

    private object sessionLock;
    private OgsSession session;
    private string apiBaseUrl;
    private bool sessionLoaded;
    private int friendInvitationCount;
    private OgsFriendDataRequestCache friendDataRequestCache;
    private OgsRealtimeConnection realtimeConnection;

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

    public override void Update()
    {
        EnsureInitialized();
        UpdateRealtimeConnectionLifecycle();
    }

    public override void OnDestroy()
    {
        StopRealtimeConnection();
        realtimeConnection?.Dispose();
        realtimeConnection = null;
        base.OnDestroy();
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
        if (friendDataRequestCache == null) {
            friendDataRequestCache = new OgsFriendDataRequestCache();
        }
        if (realtimeConnection == null) {
            realtimeConnection = new OgsRealtimeConnection(
                RequestRealtimeUserJwtForCurrentSessionAsync,
                () => OgsConnectionConfig.DefaultWebSocketUrl);
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
            friendDataRequestCache?.Clear();
            RestartRealtimeConnectionIfNeeded();
            return;
        }

        apiBaseUrl = baseUrl.Trim().TrimEnd('/');
        friendDataRequestCache?.Clear();
        RestartRealtimeConnectionIfNeeded();
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
            friendDataRequestCache?.Clear();
            StartRealtimeConnection();
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
            ? GetDefaultBrowserLoginRedirectUri()
            : redirectUri.Trim();
        bool useMobileCallback = CanUseMobileOauthRedirectUri(safeRedirectUri);
        if (!useMobileCallback && !CanUseLocalhostCallback(safeRedirectUri)) {
            return new OgsConnectionResult(false, $"OGS browser login redirect URI is not supported on this platform: {safeRedirectUri}");
        }

        try {
            OgsAuthorizationRequest request = CreateAuthorizationRequest(clientId, safeRedirectUri, scope);
            Task<OgsCallbackResult> callbackTask = useMobileCallback
                ? WaitForDeepLinkCallbackAsync(MobileDeepLinkCallbackUri, request.state, cancellationToken)
                : WaitForCallbackAsync(safeRedirectUri, request.state, cancellationToken);
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
            var currentUser = new OgsCurrentUserFields();

            JObject meJson = null;
            if (currentUser.NeedsIdentity) {
                meJson = await TryGetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.MePath}", accessToken, "me", cancellationToken);
                if (meJson == null) {
                    meJson = await TryGetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.MePathWithoutTrailingSlash}", accessToken, "me-no-slash", cancellationToken);
                }
                ReadCurrentUserFields(meJson, currentUser);
            }

            if (currentUser.NeedsAnyProfileField) {
                JObject uiConfigJson = await TryGetJsonAsync($"{apiBaseUrl}{OgsConnectionConfig.UiConfigPath}", accessToken, "ui-config", cancellationToken);
                ReadCurrentUserFields(uiConfigJson, currentUser);
                ReadCurrentUserFields(uiConfigJson?["user"] as JObject, currentUser);
                ReadCurrentUserFields(uiConfigJson?["user_info"] as JObject, currentUser);
                ReadCurrentUserFields(uiConfigJson?["config"]?["user"] as JObject, currentUser);
            }

            lock (sessionLock) {
                session.userId = currentUser.userId ?? string.Empty;
                session.username = currentUser.username ?? string.Empty;
                session.avatarUrl = NormalizeOgsUrl(currentUser.avatarUrl, apiBaseUrl);
                session.country = currentUser.country ?? string.Empty;
                session.registeredAt = currentUser.registeredAt ?? string.Empty;
                session.tags = currentUser.tags ?? string.Empty;
                session.about = currentUser.about ?? string.Empty;
                session.ratingOverall = currentUser.ratingOverall ?? string.Empty;
                session.ranking = currentUser.ranking ?? string.Empty;
                session.rating19 = currentUser.rating19 ?? string.Empty;
                session.rating13 = currentUser.rating13 ?? string.Empty;
                session.rating9 = currentUser.rating9 ?? string.Empty;
            }
            OgsSessionStore.Save(session);

            if (string.IsNullOrEmpty(currentUser.userId) && string.IsNullOrEmpty(currentUser.username)) {
                return new OgsConnectionResult(false, "OGS current user response did not include a user id or username.");
            }

            XNLogger.LogInfo("OGS current user refreshed.", ("userId", currentUser.userId ?? string.Empty), ("username", currentUser.username ?? string.Empty));
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

    public async Task<OgsFriendListResult> RequestFriendListAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        OgsConnectionResult accessResult = await EnsureReadableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return new OgsFriendListResult(false, accessResult.message);
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        try {
            page = Math.Max(1, page);
            pageSize = Mathf.Clamp(pageSize, 1, 100);
            JToken friendJson;
            string url = $"{apiBaseUrl}/api/v1/me/friends/?page={page}&page_size={pageSize}";
            try {
                friendJson = await friendDataRequestCache.GetJsonAsync(
                    $"friend-list:{apiBaseUrl}:page:{page}:size:{pageSize}",
                    token => GetJsonTokenAsync(url, accessToken, token),
                    cancellationToken);
            }
            catch (Exception ex) {
                XNLogger.LogWarn("OGS me/friends request failed, falling back to ui/friends.", ("err", ex.Message));
                string fallbackUrl = $"{apiBaseUrl}/api/v1/ui/friends";
                friendJson = await friendDataRequestCache.GetJsonAsync(
                    $"friend-list-ui-fallback:{apiBaseUrl}",
                    token => GetJsonTokenAsync(fallbackUrl, accessToken, token),
                    cancellationToken);
            }
            List<OgsFriendListItem> allFriends = ReadFriendListItems(friendJson, apiBaseUrl);
            await ApplyFriendOnlineStatusesAsync(allFriends, accessToken, cancellationToken);
            int totalCount = ReadFriendListTotalCount(friendJson, allFriends.Count);
            List<OgsFriendListItem> friends = IsPagedFriendListResponse(friendJson)
                ? allFriends
                : SliceFriendList(allFriends, page, pageSize);
            XNLogger.LogInfo(
                "OGS friend list refreshed.",
                ("page", page.ToString()),
                ("pageSize", pageSize.ToString()),
                ("count", friends.Count.ToString()),
                ("total", totalCount.ToString()));
            return new OgsFriendListResult(true, "OGS friend list refreshed.", friends, totalCount);
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS friend list request failed.", ("err", ex.Message));
            return new OgsFriendListResult(false, ex.Message);
        }
    }

    public async Task<OgsFriendProfileResult> RequestFriendProfileAsync(
        string friendUserId,
        OgsFriendListItem fallback = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        string safeUserId = string.IsNullOrWhiteSpace(friendUserId)
            ? fallback?.userId
            : friendUserId.Trim();
        if (string.IsNullOrWhiteSpace(safeUserId)) {
            return new OgsFriendProfileResult(false, "OGS friend user id is empty.", CloneFriendItem(fallback));
        }

        OgsConnectionResult accessResult = await EnsureReadableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return new OgsFriendProfileResult(false, accessResult.message, CloneFriendItem(fallback));
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        OgsFriendListItem merged = CloneFriendItem(fallback) ?? new OgsFriendListItem { userId = safeUserId };
        try {
            string escapedUserId = Uri.EscapeDataString(safeUserId);
            string profileUrl = $"{apiBaseUrl}/api/v1/players/{escapedUserId}";
            JToken profileJson = await friendDataRequestCache.GetJsonAsync(
                $"friend-profile:{apiBaseUrl}:{safeUserId}",
                token => GetJsonTokenAsync(profileUrl, accessToken, token),
                cancellationToken);
            MergeFriendItem(merged, ReadFriendListItem(profileJson, apiBaseUrl));

            string terminationProfileUrl = $"{apiBaseUrl}/termination-api/player/{escapedUserId}";
            try {
                JToken terminationProfileJson = await friendDataRequestCache.GetJsonAsync(
                    $"friend-profile-termination:{apiBaseUrl}:{safeUserId}",
                    token => GetJsonTokenAsync(terminationProfileUrl, accessToken, token),
                    cancellationToken);
                MergeFriendItem(merged, ReadFriendListItem(terminationProfileJson, apiBaseUrl));
            }
            catch (Exception ex) {
                XNLogger.LogWarn("OGS friend termination profile request failed, using REST profile.", ("friendUserId", safeUserId), ("err", ex.Message));
            }

            string fullProfileUrl = $"{apiBaseUrl}/api/v1/players/{escapedUserId}/full";
            try {
                JToken fullProfileJson = await friendDataRequestCache.GetJsonAsync(
                    $"friend-profile-full:{apiBaseUrl}:{safeUserId}",
                    token => GetJsonTokenAsync(fullProfileUrl, accessToken, token),
                    cancellationToken);
                MergeFriendItem(merged, ReadFriendListItem(fullProfileJson, apiBaseUrl));
            }
            catch (Exception ex) {
                XNLogger.LogWarn("OGS friend full profile request failed, using basic profile.", ("friendUserId", safeUserId), ("err", ex.Message));
            }

            await ApplyFriendOnlineStatusAsync(merged, accessToken, cancellationToken);
            return new OgsFriendProfileResult(true, "OGS friend profile refreshed.", merged);
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS friend profile request failed.", ("friendUserId", safeUserId), ("err", ex.Message));
            return new OgsFriendProfileResult(false, ex.Message, merged);
        }
    }

    public async Task<OgsConnectionResult> SendFriendRequestAsync(
        int playerId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (playerId <= 0) {
            return new OgsConnectionResult(false, "请输入有效的 OGS ID。");
        }

        OgsConnectionResult accessResult = await EnsureWritableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return accessResult;
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        try {
            var payload = new JObject
            {
                ["player_id"] = playerId,
            };
            await PostJsonAsync($"{apiBaseUrl}/api/v1/me/friends/", payload, accessToken, cancellationToken);
            friendDataRequestCache?.Clear();
            XNLogger.LogInfo("OGS friend request sent.", ("playerId", playerId.ToString()));
            return new OgsConnectionResult(true, "OGS friend request sent.");
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS friend request failed.", ("playerId", playerId.ToString()), ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
        }
    }

    public async Task<OgsConnectionResult> DeleteFriendAsync(
        string friendUserId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(friendUserId)) {
            return new OgsConnectionResult(false, "OGS friend user id is empty.");
        }

        OgsConnectionResult accessResult = await EnsureWritableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return accessResult;
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        try {
            JObject payload = new JObject
            {
                ["player_id"] = friendUserId.Trim(),
                ["delete"] = true,
            };
            await PostJsonTokenAsync($"{apiBaseUrl}/api/v1/me/friends/", payload, accessToken, cancellationToken);
            friendDataRequestCache?.Clear();
            XNLogger.LogInfo("OGS friend deleted.", ("friendUserId", friendUserId.Trim()));
            return new OgsConnectionResult(true, "OGS friend deleted.");
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS friend delete failed.", ("friendUserId", friendUserId.Trim()), ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
        }
    }

    public async Task<OgsFriendInvitationListResult> RequestFriendInvitationsAsync(
        CancellationToken cancellationToken = default(CancellationToken),
        bool logError = true)
    {
        EnsureInitialized();
        OgsConnectionResult accessResult = await EnsureReadableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return new OgsFriendInvitationListResult(false, accessResult.message);
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        try {
            JToken invitationJson = await GetJsonTokenAsync($"{apiBaseUrl}/api/v1/me/friends/invitations/", accessToken, cancellationToken);
            List<OgsFriendInvitationItem> invitations = ReadFriendInvitationItems(invitationJson, apiBaseUrl);
            XNLogger.LogInfo("OGS friend invitations refreshed.", ("count", invitations.Count.ToString()));
            EmitFriendInvitationCountChanged(invitations.Count);
            return new OgsFriendInvitationListResult(true, "OGS friend invitations refreshed.", invitations);
        }
        catch (Exception ex) {
            if (logError) {
                XNLogger.LogError("OGS friend invitations request failed.", ("err", ex.Message));
            } else {
                XNLogger.LogWarn("OGS friend invitations request failed.", ("err", ex.Message));
            }
            return new OgsFriendInvitationListResult(false, ex.Message);
        }
    }

    public int FriendInvitationCount => Mathf.Max(0, friendInvitationCount);

    public async Task<OgsFriendInvitationCountResult> RequestFriendInvitationCountAsync(
        CancellationToken cancellationToken = default(CancellationToken))
    {
        OgsFriendInvitationListResult result = await RequestFriendInvitationsAsync(cancellationToken, false);
        if (!result.success) {
            return new OgsFriendInvitationCountResult(false, result.message);
        }

        return new OgsFriendInvitationCountResult(true, result.message, result.invitations?.Count ?? 0);
    }

    public async Task<OgsConnectionResult> RespondFriendInvitationAsync(
        int fromUserId,
        bool accept,
        bool notifyRequestor = false,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (fromUserId <= 0) {
            return new OgsConnectionResult(false, "好友申请用户 ID 无效。");
        }

        OgsConnectionResult accessResult = await EnsureWritableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return accessResult;
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        try {
            var payload = new JObject
            {
                ["from_user"] = fromUserId,
            };
            if (!accept) {
                payload["delete"] = true;
                payload["notify_requestor"] = notifyRequestor;
            }

            await PostJsonAsync($"{apiBaseUrl}/api/v1/me/friends/invitations/", payload, accessToken, cancellationToken);
            friendDataRequestCache?.Clear();
            XNLogger.LogInfo(
                "OGS friend invitation responded.",
                ("fromUserId", fromUserId.ToString()),
                ("accept", accept.ToString()));
            return new OgsConnectionResult(true, accept ? "OGS friend invitation accepted." : "OGS friend invitation declined.");
        }
        catch (Exception ex) {
            XNLogger.LogError(
                "OGS friend invitation response failed.",
                ("fromUserId", fromUserId.ToString()),
                ("accept", accept.ToString()),
                ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
        }
    }

    public void Logout()
    {
        EnsureInitialized();
        StopRealtimeConnection();
        lock (sessionLock) {
            session.Clear();
        }
        friendDataRequestCache?.Clear();
        realtimeConnection?.ClearUserStates();
        OgsSessionStore.Clear();
        XNLogger.LogInfo("OGS session cleared.");
        EmitFriendInvitationCountChanged(0);
    }

    public void EmitFriendInvitationCountChanged(int count)
    {
        friendInvitationCount = Mathf.Max(0, count);
        Global.Instance?.eventManager?.EmitSystemEvent(new OnOgsFriendInvitationCountChanged(friendInvitationCount));
    }

    private void UpdateRealtimeConnectionLifecycle()
    {
        if (ShouldMaintainRealtimeConnection()) {
            StartRealtimeConnection();
        } else {
            StopRealtimeConnection();
        }
    }

    private bool ShouldMaintainRealtimeConnection()
    {
        if (Global.IsApplicationInBackground) {
            return false;
        }

        lock (sessionLock) {
            return session != null && (session.HasAccessToken || session.CanRefresh);
        }
    }

    private void StartRealtimeConnection()
    {
        if (realtimeConnection == null || !ShouldMaintainRealtimeConnection()) {
            return;
        }

        realtimeConnection.Start();
    }

    private void StopRealtimeConnection()
    {
        realtimeConnection?.Stop();
    }

    private void RestartRealtimeConnectionIfNeeded()
    {
        if (realtimeConnection == null || !realtimeConnection.IsStarted) {
            return;
        }

        realtimeConnection.Stop();
        StartRealtimeConnection();
    }

    private async Task<string> RequestRealtimeUserJwtForCurrentSessionAsync(CancellationToken cancellationToken)
    {
        OgsConnectionResult accessResult = await EnsureReadableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            throw new InvalidOperationException(accessResult.message);
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        return await RequestRealtimeUserJwtAsync(accessToken, cancellationToken);
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

    public async Task<OgsBotGameStartResult> CreateFriendChallengeAsync(
        OgsFriendChallengeCreateParams createParams,
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        createParams = NormalizeFriendChallengeCreateParams(createParams);
        if (string.IsNullOrWhiteSpace(createParams.friendUserId)) {
            return new OgsBotGameStartResult(false, "OGS friend user id is empty.");
        }

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

        int challengeId = 0;
        string challengeUuid = string.Empty;
        int gameId = 0;
        try {
            JObject challengePayload = BuildFriendChallengePayload(createParams);
            JObject challengeJson = await PostJsonAsync(
                $"{apiBaseUrl}/api/v1/players/{Uri.EscapeDataString(createParams.friendUserId)}/challenge",
                challengePayload,
                accessToken,
                cancellationToken);

            challengeId = ReadChallengeId(challengeJson);
            challengeUuid = ReadFirstString(challengeJson, "uuid", "challenge_uuid");
            gameId = ReadGameIdFromChallengeResponse(challengeJson);
            XNLogger.LogInfo(
                "OGS friend challenge created.",
                ("friendUserId", createParams.friendUserId),
                ("challengeId", challengeId.ToString()),
                ("gameId", gameId.ToString()));

            return await WaitForAcceptedChallengeGameDataAsync(
                challengeId,
                challengeUuid,
                gameId,
                accessToken,
                websocketUrl,
                challengeJson,
                "OGS friend challenge accepted and game data received.",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            if (challengeId > 0) {
                await CancelChallengeAsync(challengeId);
            }
            return new OgsBotGameStartResult(false, "OGS friend challenge canceled.", challengeId: challengeId, challengeUuid: challengeUuid, gameId: gameId, isBotGame: false);
        }
        catch (Exception ex) {
            XNLogger.LogError(
                "OGS friend challenge failed.",
                ("friendUserId", createParams.friendUserId),
                ("challengeId", challengeId.ToString()),
                ("gameId", gameId.ToString()),
                ("err", ex.Message));
            return new OgsBotGameStartResult(false, ex.Message, challengeId: challengeId, challengeUuid: challengeUuid, gameId: gameId, isBotGame: false);
        }
    }

    public async Task<OgsChallengeInviteListResult> RequestIncomingChallengeInvitesAsync(
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        OgsConnectionResult accessResult = await EnsureReadableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return new OgsChallengeInviteListResult(false, accessResult.message);
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
                return new OgsChallengeInviteListResult(false, userResult.message);
            }

            lock (sessionLock) {
                accessToken = session.accessToken;
                userId = session.userId;
            }
        }

        if (!int.TryParse(userId, out int localUserId) || localUserId <= 0) {
            return new OgsChallengeInviteListResult(false, "OGS current user id is empty.");
        }

        try {
            JToken invitesJson = await GetJsonTokenAsync($"{apiBaseUrl}/api/v1/me/challenges/?page_size=30", accessToken, cancellationToken);
            List<OgsChallengeInvite> invites = ReadChallengeInvites(invitesJson, localUserId);
            XNLogger.LogInfo(
                "OGS incoming challenge invites refreshed.",
                ("count", invites.Count.ToString()),
                ("response", DescribeChallengeInviteResponse(invitesJson)));
            return new OgsChallengeInviteListResult(true, "OGS incoming challenge invites refreshed.", invites);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS incoming challenge invite request failed.", ("err", ex.Message));
            return new OgsChallengeInviteListResult(false, ex.Message);
        }
    }

    public async Task<OgsBotGameStartResult> AcceptChallengeAsync(
        OgsChallengeInvite invite,
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (invite == null || invite.challengeId <= 0) {
            return new OgsBotGameStartResult(false, "OGS challenge id is empty.");
        }

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
            JToken acceptJson = await PostJsonTokenAsync(
                $"{apiBaseUrl}/api/v1/me/challenges/{invite.challengeId}/accept/",
                new JObject(),
                accessToken,
                cancellationToken);

            int gameId = ReadGameIdFromChallengeResponse(acceptJson as JObject);
            if (gameId <= 0) {
                gameId = invite.gameId;
            }
            return await WaitForAcceptedChallengeGameDataAsync(
                invite.challengeId,
                invite.challengeUuid,
                gameId,
                accessToken,
                websocketUrl,
                acceptJson,
                "OGS challenge accepted and game data received.",
                cancellationToken,
                invite.challengerId,
                invite.challengerName);
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS challenge accept failed.", ("challengeId", invite.challengeId.ToString()), ("err", ex.Message));
            return new OgsBotGameStartResult(false, ex.Message, invite.challengerId, invite.challengerName, invite.challengeId, invite.challengeUuid, isBotGame: false);
        }
    }

    public async Task<OgsConnectionResult> CancelChallengeAsync(
        int challengeId,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        if (challengeId <= 0) {
            return new OgsConnectionResult(false, "OGS challenge id is empty.");
        }

        OgsConnectionResult accessResult = await EnsureUsableAccessTokenAsync(cancellationToken);
        if (!accessResult.success) {
            return accessResult;
        }

        string accessToken;
        lock (sessionLock) {
            accessToken = session.accessToken;
        }

        try {
            await DeleteJsonTokenAsync($"{apiBaseUrl}/api/v1/me/challenges/{challengeId}/", accessToken, cancellationToken);
            XNLogger.LogInfo("OGS challenge canceled.", ("challengeId", challengeId.ToString()));
            return new OgsConnectionResult(true, "OGS challenge canceled.");
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS challenge cancel failed.", ("challengeId", challengeId.ToString()), ("err", ex.Message));
            return new OgsConnectionResult(false, ex.Message);
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

    public async Task<OgsBotGameStartResult> StartAutomatchGameAsync(
        OgsAutomatchCreateParams createParams,
        string websocketUrl = null,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        EnsureInitialized();
        createParams = NormalizeAutomatchCreateParams(createParams);
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

        string matchUuid = Guid.NewGuid().ToString("N");
        int gameId = 0;
        try {
            string userJwt = await RequestRealtimeUserJwtAsync(accessToken, cancellationToken);
            if (string.IsNullOrEmpty(userJwt)) {
                return new OgsBotGameStartResult(false, "OGS ui config did not include user_jwt.");
            }

            using (var websocket = new ClientWebSocket()) {
                await websocket.ConnectAsync(new Uri(websocketUrl.Trim()), cancellationToken);
                await SendRealtimePayloadAsync(websocket, BuildRealtimeAuthenticatePayload(userJwt), cancellationToken);
                await SendRealtimePayloadAsync(websocket, BuildAutomatchFindMatchPayload(createParams, matchUuid), cancellationToken);

                OgsAutomatchStartSelection match;
                try {
                    match = await WaitForAutomatchStartAsync(
                        websocket,
                        matchUuid,
                        OgsConnectionConfig.AutomatchReceiveMilliseconds,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    await TryCancelAutomatchAsync(websocket, matchUuid);
                    return new OgsBotGameStartResult(false, "OGS automatch canceled.", isBotGame: false);
                }

                if (match.gameId <= 0) {
                    await TryCancelAutomatchAsync(websocket, matchUuid);
                    return new OgsBotGameStartResult(false, match.message, rawResponse: match.rawMessage);
                }

                gameId = match.gameId;
                await SendRealtimePayloadAsync(websocket, BuildGameConnectPayload(gameId), cancellationToken);
                OgsGameStateSmokeResult gameState = await WaitForGameDataAsync(
                    websocket,
                    gameId,
                    OgsConnectionConfig.GameStateSmokeReceiveMilliseconds,
                    cancellationToken);
                if (!gameState.success) {
                    XNLogger.LogWarn(
                        "OGS automatch game found, but game data was not received.",
                        ("gameId", gameId.ToString()),
                        ("message", gameState.message),
                        ("lastMessage", gameState.rawMessage),
                        ("rawMatch", match.rawMessage));
                    return new OgsBotGameStartResult(
                        false,
                        $"OGS automatch game found, but game data was not received: {gameState.message}",
                        gameId: gameId,
                        gameState: gameState,
                        rawResponse: match.rawMessage,
                        isBotGame: false);
                }

                XNLogger.LogInfo(
                    "OGS automatch game started.",
                    ("gameId", gameId.ToString()),
                    ("requestedBoard", $"{createParams.boardSize}x{createParams.boardSize}"),
                    ("speed", ResolveAutomatchSpeed(createParams)),
                    ("system", ResolveAutomatchSystem(createParams)));
                return new OgsBotGameStartResult(
                    true,
                    "OGS automatch game found and game data received.",
                    gameId: gameId,
                    gameState: gameState,
                    rawResponse: match.rawMessage,
                    isBotGame: false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return new OgsBotGameStartResult(false, "OGS automatch canceled.", gameId: gameId, isBotGame: false);
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS automatch start failed.", ("gameId", gameId.ToString()), ("err", ex.Message));
            return new OgsBotGameStartResult(false, ex.Message, gameId: gameId, isBotGame: false);
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

        StartRealtimeConnection();
        OgsRealtimeGameSession gameSession = await realtimeConnection.CreateGameSessionAsync(gameId, cancellationToken);
        gameSession.StartReceiveLoop();
        XNLogger.LogInfo("OGS realtime game session subscribed.", ("gameId", gameId.ToString()));
        return gameSession;
    }

    private Task<OgsConnectionResult> EnsureUsableAccessTokenAsync(CancellationToken cancellationToken)
    {
        return EnsureWritableAccessTokenAsync(cancellationToken);
    }

    private async Task<OgsConnectionResult> EnsureWritableAccessTokenAsync(CancellationToken cancellationToken)
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
            return new OgsConnectionResult(false, "当前 OGS 授权缺少 write 权限，请重新登录 OGS。");
        }

        if (!string.IsNullOrEmpty(accessToken) && !isExpired) {
            return new OgsConnectionResult(true, "OGS access token is available.");
        }

        if (canRefresh) {
            return await RefreshTokenAsync(OgsConnectionConfig.DefaultClientId, cancellationToken);
        }

        return new OgsConnectionResult(false, "请先登录 OGS。");
    }

    private async Task<OgsConnectionResult> EnsureReadableAccessTokenAsync(CancellationToken cancellationToken)
    {
        string accessToken;
        bool isExpired;
        bool canRefresh;
        lock (sessionLock) {
            accessToken = session.accessToken;
            isExpired = session.IsExpired;
            canRefresh = session.CanRefresh;
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

    private static string GetDefaultBrowserLoginRedirectUri()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        return MobileOauthRedirectUri;
#else
        return OgsConnectionConfig.DefaultRedirectUri;
#endif
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

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        return false;
#else
        return true;
#endif
    }

    private static bool CanUseMobileOauthRedirectUri(string redirectUri)
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        return string.Equals(redirectUri?.Trim(), MobileOauthRedirectUri, StringComparison.Ordinal);
#else
        return false;
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
                    timeout.CancelAfter(BrowserLoginCallbackTimeoutMilliseconds);
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

    private static async Task<OgsCallbackResult> WaitForDeepLinkCallbackAsync(
        string redirectUri,
        string expectedState,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<OgsCallbackResult>();
        Action<string> deepLinkHandler = url => {
            OgsCallbackResult result = TryReadDeepLinkCallback(url, redirectUri, expectedState);
            if (result != null) {
                completion.TrySetResult(result);
            }
        };

        Application.deepLinkActivated += deepLinkHandler;
        try {
            OgsCallbackResult startupResult = TryReadDeepLinkCallback(Application.absoluteURL, redirectUri, expectedState, false);
            if (startupResult != null) {
                return startupResult;
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                timeout.CancelAfter(BrowserLoginCallbackTimeoutMilliseconds);
                using (timeout.Token.Register(() => completion.TrySetResult(new OgsCallbackResult(false, "Timed out waiting for OGS mobile callback.")))) {
                    return await completion.Task;
                }
            }
        }
        finally {
            Application.deepLinkActivated -= deepLinkHandler;
        }
    }

    private static OgsCallbackResult TryReadDeepLinkCallback(string url, string redirectUri, string expectedState, bool failOnStateMismatch = true)
    {
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri callbackUri)) {
            return null;
        }
        if (!IsExpectedDeepLinkCallbackUri(callbackUri, redirectUri)) {
            return null;
        }

        string code = ReadUriParameter(callbackUri, "code") ?? string.Empty;
        string state = ReadUriParameter(callbackUri, "state") ?? string.Empty;
        string error = ReadUriParameter(callbackUri, "error") ?? string.Empty;
        if (!string.IsNullOrEmpty(error)) {
            return new OgsCallbackResult(false, $"OGS authorization failed: {error}");
        }
        if (string.IsNullOrEmpty(code)) {
            return new OgsCallbackResult(false, "OGS mobile callback did not include a code.");
        }
        if (!string.IsNullOrEmpty(expectedState) && state != expectedState) {
            return failOnStateMismatch ? new OgsCallbackResult(false, "OGS callback state mismatch.") : null;
        }

        return new OgsCallbackResult(true, "OGS mobile callback received.", code);
    }

    private static bool IsExpectedDeepLinkCallbackUri(string callbackUri, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(callbackUri) || string.IsNullOrWhiteSpace(redirectUri)) {
            return false;
        }
        if (!Uri.TryCreate(callbackUri.Trim(), UriKind.Absolute, out Uri parsedCallbackUri)) {
            return false;
        }

        return IsExpectedDeepLinkCallbackUri(parsedCallbackUri, redirectUri);
    }

    private static bool IsExpectedDeepLinkCallbackUri(Uri callbackUri, string redirectUri)
    {
        if (callbackUri == null || string.IsNullOrWhiteSpace(redirectUri)) {
            return false;
        }
        if (!Uri.TryCreate(redirectUri.Trim(), UriKind.Absolute, out Uri expectedUri)) {
            return false;
        }

        string actualPath = NormalizeCallbackPath(callbackUri.AbsolutePath);
        string expectedPath = NormalizeCallbackPath(expectedUri.AbsolutePath);
        return string.Equals(callbackUri.Scheme, expectedUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(callbackUri.Host, expectedUri.Host, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actualPath, expectedPath, StringComparison.Ordinal);
    }

    private static string ReadUriParameter(Uri uri, string parameterName)
    {
        if (uri == null || string.IsNullOrEmpty(parameterName)) {
            return string.Empty;
        }

        string value = ReadParameterFromDelimitedString(uri.Query, parameterName);
        if (!string.IsNullOrEmpty(value)) {
            return value;
        }

        return ReadParameterFromDelimitedString(uri.Fragment, parameterName);
    }

    private static string ReadParameterFromDelimitedString(string delimitedString, string parameterName)
    {
        if (string.IsNullOrEmpty(delimitedString)) {
            return string.Empty;
        }

        string trimmed = delimitedString.TrimStart('?', '#');
        string[] pairs = trimmed.Split('&');
        foreach (string pair in pairs) {
            if (string.IsNullOrEmpty(pair)) {
                continue;
            }

            int equalsIndex = pair.IndexOf('=');
            string key = equalsIndex >= 0 ? pair.Substring(0, equalsIndex) : pair;
            string value = equalsIndex >= 0 ? pair.Substring(equalsIndex + 1) : string.Empty;
            key = Uri.UnescapeDataString(key.Replace("+", " "));
            if (string.Equals(key, parameterName, StringComparison.Ordinal)) {
                return Uri.UnescapeDataString(value.Replace("+", " "));
            }
        }

        return string.Empty;
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
        JToken token = await GetJsonTokenAsync(url, accessToken, cancellationToken);
        if (token is JObject obj) {
            return obj;
        }

        throw new InvalidOperationException($"OGS GET did not return a JSON object: {url}");
    }

    private async Task<JToken> GetJsonTokenAsync(string url, string accessToken, CancellationToken cancellationToken)
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
                return string.IsNullOrWhiteSpace(body) ? new JObject() : JToken.Parse(body);
            }
        }
    }

    private async Task<JObject> PostJsonAsync(
        string url,
        JObject json,
        string accessToken,
        CancellationToken cancellationToken)
    {
        JToken token = await PostJsonTokenAsync(url, json, accessToken, cancellationToken);
        return token as JObject ?? new JObject();
    }

    private async Task<JToken> PostJsonTokenAsync(
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
                return string.IsNullOrWhiteSpace(body) ? new JObject() : JToken.Parse(body);
            }
        }
    }

    private async Task<JToken> DeleteJsonTokenAsync(
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using (HttpClient client = CreateHttpClient())
        using (var request = new HttpRequestMessage(HttpMethod.Delete, url)) {
            if (!string.IsNullOrEmpty(accessToken)) {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            }

            using (HttpResponseMessage response = await SendOgsRequestAsync(client, request, "DELETE", cancellationToken)) {
                string body = await response.Content.ReadAsStringAsync();
                LogVerboseHttpResponse("DELETE", url, response, body);
                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"OGS DELETE failed: {(int)response.StatusCode} {response.ReasonPhrase} {TrimForLog(body)}");
                }
                return string.IsNullOrWhiteSpace(body) ? new JObject() : JToken.Parse(body);
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

    private static string BuildAutomatchFindMatchPayload(OgsAutomatchCreateParams createParams, string matchUuid)
    {
        createParams = NormalizeAutomatchCreateParams(createParams);
        var payload = new JArray
        {
            "automatch/find_match",
            new JObject
            {
                ["uuid"] = matchUuid,
                ["size_speed_options"] = new JArray
                {
                    new JObject
                    {
                        ["size"] = $"{createParams.boardSize}x{createParams.boardSize}",
                        ["speed"] = ResolveAutomatchSpeed(createParams),
                        ["system"] = ResolveAutomatchSystem(createParams),
                    },
                },
                ["timestamp"] = GetUnixMilliseconds(),
                ["lower_rank_diff"] = Math.Max(0, createParams.lowerRankDiff),
                ["upper_rank_diff"] = Math.Max(0, createParams.upperRankDiff),
                ["rules"] = new JObject
                {
                    ["condition"] = "required",
                    ["value"] = OgsConnectionConfig.DefaultBotGameRules,
                },
                ["handicap"] = new JObject
                {
                    ["condition"] = "preferred",
                    ["value"] = createParams.handicap > 0 ? "enabled" : "disabled",
                },
            },
        };
        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string BuildAutomatchCancelPayload(string matchUuid)
    {
        var payload = new JArray
        {
            "automatch/cancel",
            new JObject
            {
                ["uuid"] = matchUuid ?? string.Empty,
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

    private static OgsFriendChallengeCreateParams NormalizeFriendChallengeCreateParams(OgsFriendChallengeCreateParams createParams)
    {
        if (createParams == null) {
            return new OgsFriendChallengeCreateParams(
                string.Empty,
                OgsConnectionConfig.DefaultBotGameBoardSize,
                600,
                5,
                30,
                0,
                7.5f,
                "automatic",
                OgsConnectionConfig.DefaultBotGameName);
        }

        int boardSize = createParams.boardSize > 0 ? createParams.boardSize : OgsConnectionConfig.DefaultBotGameBoardSize;
        int mainTimeSeconds = createParams.mainTimeSeconds;
        int byoyomiPeriods = Math.Max(0, createParams.byoyomiPeriods);
        int byoyomiPeriodSeconds = Math.Max(0, createParams.byoyomiPeriodSeconds);
        int handicap = Math.Max(0, createParams.handicap);
        string challengerColor = NormalizeChallengerColor(createParams.challengerColor);
        string gameName = string.IsNullOrWhiteSpace(createParams.gameName)
            ? OgsConnectionConfig.DefaultBotGameName
            : createParams.gameName.Trim();
        return new OgsFriendChallengeCreateParams(
            createParams.friendUserId?.Trim(),
            boardSize,
            mainTimeSeconds,
            byoyomiPeriods,
            byoyomiPeriodSeconds,
            handicap,
            createParams.komi,
            challengerColor,
            gameName);
    }

    private static OgsAutomatchCreateParams NormalizeAutomatchCreateParams(OgsAutomatchCreateParams createParams)
    {
        createParams = createParams ?? OgsAutomatchCreateParams.Default;
        int boardSize = NormalizeAutomatchBoardSize(createParams.boardSize);
        int mainTimeSeconds = createParams.mainTimeSeconds > 0 ? createParams.mainTimeSeconds : OgsAutomatchCreateParams.Default.mainTimeSeconds;
        int byoyomiPeriods = Math.Max(0, createParams.byoyomiPeriods);
        int byoyomiPeriodSeconds = Math.Max(0, createParams.byoyomiPeriodSeconds);
        int handicap = Math.Max(0, createParams.handicap);
        string speed = NormalizeAutomatchSpeed(createParams.speed, mainTimeSeconds);
        string system = NormalizeAutomatchSystem(createParams.system, byoyomiPeriods, byoyomiPeriodSeconds);
        int lowerRankDiff = Math.Max(0, createParams.lowerRankDiff);
        int upperRankDiff = Math.Max(0, createParams.upperRankDiff);
        return new OgsAutomatchCreateParams(
            boardSize,
            mainTimeSeconds,
            byoyomiPeriods,
            byoyomiPeriodSeconds,
            handicap,
            speed,
            system,
            lowerRankDiff,
            upperRankDiff);
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

    private static JObject BuildFriendChallengePayload(OgsFriendChallengeCreateParams createParams)
    {
        createParams = NormalizeFriendChallengeCreateParams(createParams);
        var botEquivalentParams = new OgsBotGameCreateParams(
            createParams.boardSize,
            createParams.mainTimeSeconds,
            createParams.byoyomiPeriods,
            createParams.byoyomiPeriodSeconds,
            createParams.handicap,
            createParams.komi,
            createParams.challengerColor,
            createParams.gameName);
        return BuildBotChallengePayload(botEquivalentParams);
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

    private static int NormalizeAutomatchBoardSize(int boardSize)
    {
        if (boardSize == 9 || boardSize == 13 || boardSize == 19) {
            return boardSize;
        }

        int fallback = OgsConnectionConfig.DefaultBotGameBoardSize;
        return fallback == 9 || fallback == 13 || fallback == 19 ? fallback : 19;
    }

    private static string ResolveAutomatchSpeed(OgsAutomatchCreateParams createParams)
    {
        return createParams != null
            ? NormalizeAutomatchSpeed(createParams.speed, createParams.mainTimeSeconds)
            : OgsAutomatchCreateParams.Default.speed;
    }

    private static string NormalizeAutomatchSpeed(string speed, int mainTimeSeconds)
    {
        if (string.Equals(speed, "blitz", StringComparison.OrdinalIgnoreCase)) {
            return "blitz";
        }
        if (string.Equals(speed, "rapid", StringComparison.OrdinalIgnoreCase)) {
            return "rapid";
        }
        if (string.Equals(speed, "live", StringComparison.OrdinalIgnoreCase)) {
            return "live";
        }
        if (mainTimeSeconds > 0 && mainTimeSeconds <= 120) {
            return "blitz";
        }
        if (mainTimeSeconds > 0 && mainTimeSeconds <= 600) {
            return "rapid";
        }

        return "live";
    }

    private static string ResolveAutomatchSystem(OgsAutomatchCreateParams createParams)
    {
        return createParams != null
            ? NormalizeAutomatchSystem(createParams.system, createParams.byoyomiPeriods, createParams.byoyomiPeriodSeconds)
            : OgsAutomatchCreateParams.Default.system;
    }

    private static string NormalizeAutomatchSystem(string system, int byoyomiPeriods, int byoyomiPeriodSeconds)
    {
        if (string.Equals(system, "byoyomi", StringComparison.OrdinalIgnoreCase)) {
            return "byoyomi";
        }
        if (string.Equals(system, "fischer", StringComparison.OrdinalIgnoreCase)) {
            return "fischer";
        }
        if (byoyomiPeriods > 0 && byoyomiPeriodSeconds > 0) {
            return "byoyomi";
        }

        return "fischer";
    }

    private static long GetUnixMilliseconds()
    {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(DateTime.UtcNow - epoch).TotalMilliseconds;
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

    private static async Task<OgsAutomatchStartSelection> WaitForAutomatchStartAsync(
        ClientWebSocket websocket,
        string matchUuid,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using (var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            receiveCancellation.CancelAfter(Math.Max(1000, timeoutMilliseconds));
            try {
                while (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.CloseReceived) {
                    string message = await ReceiveRealtimeMessageAsync(websocket, receiveCancellation.Token);
                    OgsAutomatchStartSelection match = TryParseAutomatchStartMessage(message, matchUuid);
                    if (match.gameId > 0) {
                        return match;
                    }

                    string cancelMessage = TryParseAutomatchCancelMessage(message, matchUuid);
                    if (!string.IsNullOrEmpty(cancelMessage)) {
                        return new OgsAutomatchStartSelection(0, cancelMessage, TrimForLog(message));
                    }

                    string errorMessage = TryParseRealtimeErrorMessage(message);
                    if (!string.IsNullOrEmpty(errorMessage)) {
                        return new OgsAutomatchStartSelection(0, errorMessage, TrimForLog(message));
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                return new OgsAutomatchStartSelection(0, "OGS automatch timed out.", string.Empty);
            }
        }

        return new OgsAutomatchStartSelection(0, "OGS automatch websocket closed before a game was found.", string.Empty);
    }

    private static async Task TryCancelAutomatchAsync(ClientWebSocket websocket, string matchUuid)
    {
        if (websocket == null || websocket.State != WebSocketState.Open || string.IsNullOrWhiteSpace(matchUuid)) {
            return;
        }

        try {
            await SendRealtimePayloadAsync(websocket, BuildAutomatchCancelPayload(matchUuid), CancellationToken.None);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS automatch cancel failed.", ("uuid", matchUuid), ("err", ex.Message));
        }
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

    private static OgsAutomatchStartSelection TryParseAutomatchStartMessage(string message, string matchUuid)
    {
        JArray envelope = TryParseArray(message);
        if (envelope == null || envelope.Count < 2) {
            return default(OgsAutomatchStartSelection);
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        JObject payload = envelope[1] as JObject;
        if (payload == null) {
            return default(OgsAutomatchStartSelection);
        }

        if (channel == "automatch/start") {
            string payloadUuid = ReadFirstString(payload, "uuid");
            if (!string.IsNullOrWhiteSpace(matchUuid) &&
                !string.IsNullOrWhiteSpace(payloadUuid) &&
                !string.Equals(matchUuid, payloadUuid, StringComparison.OrdinalIgnoreCase)) {
                return default(OgsAutomatchStartSelection);
            }

            int gameId = ReadGameIdFromAutomatchPayload(payload);
            return gameId > 0
                ? new OgsAutomatchStartSelection(gameId, "OGS automatch game found.", TrimForLog(message))
                : new OgsAutomatchStartSelection(0, "OGS automatch start did not include a game id.", TrimForLog(message));
        }

        if (channel == "active_game") {
            int gameId = ReadGameIdFromAutomatchPayload(payload);
            if (gameId > 0) {
                return new OgsAutomatchStartSelection(gameId, "OGS active game found.", TrimForLog(message));
            }
        }

        return default(OgsAutomatchStartSelection);
    }

    private static string TryParseAutomatchCancelMessage(string message, string matchUuid)
    {
        JArray envelope = TryParseArray(message);
        if (envelope == null || envelope.Count < 2) {
            return string.Empty;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        if (channel != "automatch/cancel") {
            return string.Empty;
        }

        JObject payload = envelope[1] as JObject;
        string payloadUuid = ReadFirstString(payload, "uuid");
        if (!string.IsNullOrWhiteSpace(matchUuid) &&
            !string.IsNullOrWhiteSpace(payloadUuid) &&
            !string.Equals(matchUuid, payloadUuid, StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        string messageText = ReadFirstString(payload, "message", "reason", "error");
        return string.IsNullOrEmpty(messageText)
            ? "OGS automatch was canceled."
            : $"OGS automatch was canceled: {TrimForLog(messageText)}";
    }

    private static string TryParseRealtimeErrorMessage(string message)
    {
        JArray envelope = TryParseArray(message);
        if (envelope == null || envelope.Count < 2) {
            return string.Empty;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        if (!string.Equals(channel, "ERROR", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(channel, "error", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        return $"OGS realtime error: {TrimForLog(envelope[1]?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty)}";
    }

    private static int ReadGameIdFromAutomatchPayload(JObject payload)
    {
        if (payload == null) {
            return 0;
        }

        int gameId = ReadFirstInt(payload, "game_id", "gameId", "id");
        if (gameId > 0) {
            return gameId;
        }

        JObject game = payload["game"] as JObject;
        gameId = ReadFirstInt(game, "id", "game_id", "gameId");
        if (gameId > 0) {
            return gameId;
        }

        JObject body = payload["body"] as JObject;
        return ReadFirstInt(body, "game_id", "gameId", "id");
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

    private async Task<OgsBotGameStartResult> WaitForAcceptedChallengeGameDataAsync(
        int challengeId,
        string challengeUuid,
        int gameId,
        string accessToken,
        string websocketUrl,
        JToken rawResponseToken,
        string successMessage,
        CancellationToken cancellationToken,
        int opponentId = 0,
        string opponentName = "")
    {
        string rawResponse = TrimForLog(rawResponseToken?.ToString(Newtonsoft.Json.Formatting.None));
        if (challengeId <= 0 && gameId <= 0) {
            return new OgsBotGameStartResult(
                false,
                "OGS challenge did not return a challenge id or game id.",
                opponentId,
                opponentName,
                challengeId,
                challengeUuid,
                gameId,
                rawResponse: rawResponse,
                isBotGame: false);
        }

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            if (gameId <= 0) {
                OgsChallengeGameIdProbeResult probeResult = await TryReadAcceptedChallengeGameIdAsync(challengeId, accessToken, cancellationToken);
                if (probeResult.challengeUnavailable) {
                    return new OgsBotGameStartResult(
                        false,
                        FriendChallengeDeclinedMessage,
                        opponentId,
                        opponentName,
                        challengeId,
                        challengeUuid,
                        gameId,
                        rawResponse: rawResponse,
                        isBotGame: false);
                }

                gameId = probeResult.gameId;
            }

            if (gameId > 0) {
                OgsGameStateSmokeResult gameState = await TryReadChallengeGameDataAsync(gameId, websocketUrl, cancellationToken);
                if (gameState.success) {
                    XNLogger.LogInfo(
                        "OGS challenge game data received.",
                        ("challengeId", challengeId.ToString()),
                        ("gameId", gameId.ToString()));
                    return new OgsBotGameStartResult(
                        true,
                        successMessage,
                        opponentId,
                        opponentName,
                        challengeId,
                        challengeUuid,
                        gameId,
                        gameState,
                        rawResponse,
                        false);
                }

                if (!IsTransientChallengeGameDataWait(gameState)) {
                    string failureMessage = IsChallengeRejectedMessage(gameState.message)
                        ? FriendChallengeDeclinedMessage
                        : gameState.message;
                    return new OgsBotGameStartResult(
                        false,
                        failureMessage,
                        opponentId,
                        opponentName,
                        challengeId,
                        challengeUuid,
                        gameId,
                        gameState,
                        rawResponse,
                        false);
                }
            }

            await Task.Delay(ChallengeGameDataPollMilliseconds, cancellationToken);
        }
    }

    private async Task<OgsGameStateSmokeResult> TryReadChallengeGameDataAsync(
        int gameId,
        string websocketUrl,
        CancellationToken cancellationToken)
    {
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
                await websocket.ConnectAsync(new Uri(websocketUrl), cancellationToken);
                await SendRealtimePayloadAsync(websocket, BuildRealtimeAuthenticatePayload(userJwt), cancellationToken);
                await SendRealtimePayloadAsync(websocket, BuildGameConnectPayload(gameId), cancellationToken);
                return await WaitForGameDataAsync(
                    websocket,
                    gameId,
                    OgsConnectionConfig.GameStateSmokeReceiveMilliseconds,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS challenge game data probe failed.", ("gameId", gameId.ToString()), ("err", ex.Message));
            return new OgsGameStateSmokeResult(false, ex.Message, gameId);
        }
    }

    private static bool IsTransientChallengeGameDataWait(OgsGameStateSmokeResult gameState)
    {
        if (gameState == null) {
            return true;
        }

        string message = gameState.message ?? string.Empty;
        return message.IndexOf("Timed out waiting for OGS game data", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("OGS websocket closed before game data", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsChallengeRejectedMessage(string message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
            message.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task<OgsChallengeGameIdProbeResult> TryReadAcceptedChallengeGameIdAsync(
        int challengeId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try {
            JToken challengeJson = await GetJsonTokenAsync($"{apiBaseUrl}/api/v1/me/challenges/{challengeId}/", accessToken, cancellationToken);
            int gameId = ReadGameIdFromChallengeResponse(challengeJson as JObject);
            if (gameId > 0) {
                return OgsChallengeGameIdProbeResult.GameFound(gameId);
            }
        }
        catch (Exception ex) {
            if (IsOgsHttpStatus(ex, 404, 410)) {
                XNLogger.LogInfo("OGS challenge no longer exists.", ("challengeId", challengeId.ToString()));
                return OgsChallengeGameIdProbeResult.Unavailable(FriendChallengeDeclinedMessage);
            }

            XNLogger.LogWarn("OGS challenge game id probe failed.", ("challengeId", challengeId.ToString()), ("err", ex.Message));
        }

        return OgsChallengeGameIdProbeResult.Pending;
    }

    private static bool IsOgsHttpStatus(Exception ex, params int[] statusCodes)
    {
        if (ex == null || statusCodes == null || statusCodes.Length <= 0) {
            return false;
        }

        string message = ex.Message ?? string.Empty;
        foreach (int statusCode in statusCodes) {
            if (message.IndexOf($"OGS GET failed: {statusCode} ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf($"OGS DELETE failed: {statusCode} ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf($"OGS POST failed: {statusCode} ", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return IsOgsHttpStatus(ex.InnerException, statusCodes);
    }

    private static int ReadChallengeId(JObject json)
    {
        if (json == null) {
            return 0;
        }

        int challengeId = ReadFirstInt(json, "challenge", "challenge_id", "id");
        if (challengeId > 0) {
            return challengeId;
        }

        return ReadFirstInt(json["challenge"] as JObject, "id", "challenge_id");
    }

    private static List<OgsChallengeInvite> ReadChallengeInvites(JToken root, int localUserId)
    {
        var result = new List<OgsChallengeInvite>();
        JToken listToken = SelectChallengeInviteListToken(root);
        if (listToken is JArray array) {
            foreach (JToken token in array) {
                OgsChallengeInvite invite = ReadChallengeInvite(token, localUserId);
                if (invite != null && invite.challengeId > 0) {
                    result.Add(invite);
                }
            }
            return result;
        }

        if (listToken is JObject obj) {
            foreach (JProperty property in obj.Properties()) {
                OgsChallengeInvite invite = ReadChallengeInvite(property.Value, localUserId);
                if (invite != null && invite.challengeId > 0) {
                    result.Add(invite);
                }
            }
        }

        return result;
    }

    private static JToken SelectChallengeInviteListToken(JToken root)
    {
        if (root == null) {
            return null;
        }
        if (root is JArray) {
            return root;
        }
        if (root is JObject obj) {
            return obj["results"] ??
                obj["invites"] ??
                obj["challenges"] ??
                obj["items"] ??
                obj["data"] ??
                root;
        }

        return null;
    }

    private static string DescribeChallengeInviteResponse(JToken root)
    {
        if (root == null) {
            return "null";
        }

        if (root is JArray rootArray) {
            return $"array count={rootArray.Count}";
        }

        if (root is JObject rootObject) {
            JToken listToken = SelectChallengeInviteListToken(root);
            int listCount = -1;
            if (listToken is JArray listArray) {
                listCount = listArray.Count;
            } else if (listToken is JObject listObject) {
                listCount = listObject.Count;
            }

            return $"object keys={DescribeObjectKeys(rootObject)} listType={listToken?.Type.ToString() ?? "null"} listCount={listCount}";
        }

        return root.Type.ToString();
    }

    private static string DescribeObjectKeys(JObject obj)
    {
        if (obj == null) {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (JProperty property in obj.Properties()) {
            names.Add(property.Name);
            if (names.Count >= 8) {
                break;
            }
        }

        return string.Join(",", names);
    }

    private static OgsChallengeInvite ReadChallengeInvite(JToken token, int localUserId)
    {
        JObject wrapper = token as JObject;
        if (wrapper == null) {
            return null;
        }

        JObject challengeJson = wrapper["challenge"] as JObject ?? wrapper;
        int challengeId = ReadChallengeId(challengeJson);
        if (challengeId <= 0) {
            challengeId = ReadChallengeId(wrapper);
        }
        if (challengeId <= 0) {
            return null;
        }

        JObject challengerJson = ReadPlayerObject(
            challengeJson["challenger"] ??
            challengeJson["challenger_player"] ??
            challengeJson["creator"] ??
            challengeJson["user"]);
        int challengerId = ReadPlayerId(
            challengeJson["challenger"] ??
            challengeJson["challenger_player"] ??
            challengeJson["creator"] ??
            challengeJson["user"],
            challengeJson,
            "challenger_id",
            "user_id",
            "player_id");

        JToken challengedToken = challengeJson["challenged"] ??
            challengeJson["challenged_player"] ??
            challengeJson["recipient"] ??
            challengeJson["opponent"];
        int challengedId = ReadPlayerId(
            challengedToken,
            challengeJson,
            "challenged_id",
            "recipient_id",
            "opponent_id");
        if (localUserId > 0 && challengedId != localUserId) {
            return null;
        }

        JObject gameJson = challengeJson["game"] as JObject;
        int boardSize = ReadFirstInt(gameJson, "width", "board_width", "size");
        if (boardSize <= 0) {
            boardSize = ReadFirstInt(challengeJson, "width", "board_width", "size");
        }

        return new OgsChallengeInvite
        {
            challengeId = challengeId,
            challengeUuid = ReadFirstString(challengeJson, "uuid", "challenge_uuid"),
            gameId = ReadGameIdFromChallengeResponse(challengeJson),
            challengerId = challengerId,
            challengerName = ReadPlayerName(challengerJson),
            challengedId = challengedId,
            boardSize = boardSize,
            gameName = ReadFirstString(gameJson, "name", "game_name"),
            rawResponse = TrimForLog(wrapper.ToString(Newtonsoft.Json.Formatting.None)),
        };
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
            avatarUrl = source.avatarUrl,
            country = source.country,
            registeredAt = source.registeredAt,
            tags = source.tags,
            about = source.about,
            ratingOverall = source.ratingOverall,
            ranking = source.ranking,
            rating19 = source.rating19,
            rating13 = source.rating13,
            rating9 = source.rating9,
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

    private static bool ReadFirstBool(JObject json, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return false;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token == null || token.Type == JTokenType.Null) {
                continue;
            }
            if (token.Type == JTokenType.Boolean) {
                return token.Value<bool>();
            }
            if (bool.TryParse(token.ToString(), out bool value)) {
                return value;
            }
            if (int.TryParse(token.ToString(), out int intValue)) {
                return intValue != 0;
            }
        }

        return false;
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

    private static List<OgsFriendListItem> SliceFriendList(List<OgsFriendListItem> friends, int page, int pageSize)
    {
        var result = new List<OgsFriendListItem>();
        if (friends == null || friends.Count <= 0) {
            return result;
        }

        page = Math.Max(1, page);
        pageSize = Mathf.Clamp(pageSize, 1, 100);
        int startIndex = (page - 1) * pageSize;
        if (startIndex >= friends.Count) {
            return result;
        }

        int endIndex = Math.Min(friends.Count, startIndex + pageSize);
        for (int i = startIndex; i < endIndex; i++) {
            result.Add(friends[i]);
        }

        return result;
    }

    private static bool IsPagedFriendListResponse(JToken root)
    {
        return root is JObject obj && obj["count"] != null && obj["results"] != null;
    }

    private async Task ApplyFriendOnlineStatusesAsync(List<OgsFriendListItem> friends, string accessToken, CancellationToken cancellationToken)
    {
        if (friends == null || friends.Count <= 0) {
            return;
        }

        SetDefaultFriendOnlineStatuses(friends);
        if (string.IsNullOrEmpty(accessToken)) {
            return;
        }

        var userIds = new List<int>();
        for (int i = 0; i < friends.Count; i++) {
            if (int.TryParse(friends[i]?.userId, out int userId) && userId > 0 && !userIds.Contains(userId)) {
                userIds.Add(userId);
            }
        }
        if (userIds.Count <= 0) {
            return;
        }

        try {
            JObject states = await RequestFriendOnlineStateJsonAsync(userIds, accessToken, cancellationToken);
            for (int i = 0; i < friends.Count; i++) {
                ApplyFriendOnlineState(friends[i], states);
            }
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS friend online status request failed, keeping REST status text.", ("err", ex.Message));
        }
    }

    private async Task ApplyFriendOnlineStatusAsync(OgsFriendListItem friend, string accessToken, CancellationToken cancellationToken)
    {
        if (friend == null) {
            return;
        }

        SetDefaultFriendOnlineStatus(friend);
        if (string.IsNullOrEmpty(accessToken) || !int.TryParse(friend.userId, out int userId) || userId <= 0) {
            return;
        }

        try {
            JObject states = await RequestFriendOnlineStateJsonAsync(new List<int> { userId }, accessToken, cancellationToken);
            ApplyFriendOnlineState(friend, states);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS friend online status request failed, keeping profile status text.", ("friendUserId", friend.userId ?? string.Empty), ("err", ex.Message));
        }
    }

    private async Task<JObject> RequestFriendOnlineStateJsonAsync(List<int> userIds, string accessToken, CancellationToken cancellationToken)
    {
        if (userIds == null || userIds.Count <= 0) {
            return new JObject();
        }

        userIds.Sort();
        realtimeConnection?.MonitorUsers(userIds);
        string idsKey = BuildUserIdCacheKey(userIds);
        JToken stateJson = await friendDataRequestCache.GetJsonAsync(
            $"friend-online:{apiBaseUrl}:{idsKey}",
            async token => {
                string userJwt = await RequestRealtimeUserJwtAsync(accessToken, token);
                if (string.IsNullOrEmpty(userJwt)) {
                    throw new InvalidOperationException("OGS ui config did not include user_jwt.");
                }

                using (var websocket = new ClientWebSocket()) {
                    await websocket.ConnectAsync(new Uri(OgsConnectionConfig.DefaultWebSocketUrl), token);
                    await SendRealtimePayloadAsync(websocket, BuildRealtimeAuthenticatePayload(userJwt), token);
                    await SendRealtimePayloadAsync(websocket, BuildUserMonitorPayload(userIds), token);
                    JObject states = CreateDefaultOfflineUserStates(userIds);
                    MergeUserStateJson(states, await WaitForUserStateAsync(websocket, userIds, token));
                    return states;
                }
            },
            cancellationToken);

        return stateJson as JObject ?? new JObject();
    }

    private static async Task<JObject> WaitForUserStateAsync(ClientWebSocket websocket, List<int> requestedUserIds, CancellationToken cancellationToken)
    {
        var mergedStates = new JObject();
        using (var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            receiveCancellation.CancelAfter(OgsConnectionConfig.WebSocketSmokeReceiveMilliseconds);
            try {
                while (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.CloseReceived) {
                    string message = await ReceiveRealtimeMessageAsync(websocket, receiveCancellation.Token);
                    JObject states = TryParseUserStateMessage(message);
                    if (states != null) {
                        MergeUserStateJson(mergedStates, states);
                        if (ContainsRequestedUserState(states, requestedUserIds)) {
                            return mergedStates;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                return mergedStates;
            }
        }

        return mergedStates;
    }

    private static JObject TryParseUserStateMessage(string message)
    {
        JArray envelope = TryParseArray(message);
        if (envelope == null || envelope.Count < 2) {
            return null;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        return channel == "user/state" ? envelope[1] as JObject : null;
    }

    private static void ApplyFriendOnlineState(OgsFriendListItem friend, JObject states)
    {
        if (friend == null || states == null || string.IsNullOrWhiteSpace(friend.userId)) {
            return;
        }

        JToken token = states[friend.userId.Trim()];
        if (token == null || token.Type == JTokenType.Null) {
            return;
        }

        if (TryReadOnlineStateToken(token, out bool online)) {
            friend.statusText = online ? FriendStatusOnlineText : FriendStatusOfflineText;
        }
    }

    private static void SetDefaultFriendOnlineStatuses(List<OgsFriendListItem> friends)
    {
        if (friends == null) {
            return;
        }

        for (int i = 0; i < friends.Count; i++) {
            SetDefaultFriendOnlineStatus(friends[i]);
        }
    }

    private static void SetDefaultFriendOnlineStatus(OgsFriendListItem friend)
    {
        if (friend != null && string.IsNullOrWhiteSpace(friend.statusText)) {
            friend.statusText = FriendStatusOfflineText;
        }
    }

    private static JObject CreateDefaultOfflineUserStates(List<int> userIds)
    {
        var states = new JObject();
        if (userIds == null) {
            return states;
        }

        for (int i = 0; i < userIds.Count; i++) {
            if (userIds[i] > 0) {
                states[userIds[i].ToString()] = false;
            }
        }
        return states;
    }

    private static void MergeUserStateJson(JObject target, JObject source)
    {
        if (target == null || source == null) {
            return;
        }

        foreach (JProperty property in source.Properties()) {
            target[property.Name] = property.Value?.DeepClone() ?? JValue.CreateNull();
        }
    }

    private static bool ContainsRequestedUserState(JObject states, List<int> requestedUserIds)
    {
        if (states == null || requestedUserIds == null || requestedUserIds.Count <= 0) {
            return states != null && states.HasValues;
        }

        for (int i = 0; i < requestedUserIds.Count; i++) {
            if (requestedUserIds[i] > 0 && states[requestedUserIds[i].ToString()] != null) {
                return true;
            }
        }
        return false;
    }

    private static bool TryReadOnlineStateToken(JToken token, out bool online)
    {
        online = false;
        if (token == null || token.Type == JTokenType.Null) {
            return false;
        }

        if (token.Type == JTokenType.Boolean) {
            online = token.ToObject<bool>();
            return true;
        }
        if (token.Type == JTokenType.Integer) {
            online = token.ToObject<int>() != 0;
            return true;
        }
        if (token.Type == JTokenType.String) {
            string value = token.ToString();
            if (bool.TryParse(value, out online)) {
                return true;
            }
            if (int.TryParse(value, out int numericValue)) {
                online = numericValue != 0;
                return true;
            }
        }
        if (token is JObject obj) {
            return TryReadBoolean(obj, out online, "online", "is_online", "isOnline", "connected", "state");
        }

        return false;
    }

    private static string BuildUserMonitorPayload(List<int> userIds)
    {
        var payloadUserIds = new JArray();
        if (userIds != null) {
            for (int i = 0; i < userIds.Count; i++) {
                if (userIds[i] > 0) {
                    payloadUserIds.Add(userIds[i]);
                }
            }
        }

        var payload = new JArray
        {
            "user/monitor",
            new JObject
            {
                ["user_ids"] = payloadUserIds,
            },
        };
        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string BuildUserIdCacheKey(List<int> userIds)
    {
        if (userIds == null || userIds.Count <= 0) {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int i = 0; i < userIds.Count; i++) {
            if (userIds[i] <= 0) {
                continue;
            }
            if (builder.Length > 0) {
                builder.Append('.');
            }
            builder.Append(userIds[i]);
        }
        return builder.ToString();
    }

    private static OgsFriendListItem CloneFriendItem(OgsFriendListItem source)
    {
        if (source == null) {
            return null;
        }

        return new OgsFriendListItem
        {
            userId = source.userId,
            username = source.username,
            avatarUrl = source.avatarUrl,
            country = source.country,
            ratingText = source.ratingText,
            ratingOverall = source.ratingOverall,
            rankingText = source.rankingText,
            rating19 = source.rating19,
            rating13 = source.rating13,
            rating9 = source.rating9,
            statusText = source.statusText,
            registeredAt = source.registeredAt,
            about = source.about,
        };
    }

    private static void MergeFriendItem(OgsFriendListItem target, OgsFriendListItem source)
    {
        if (target == null || source == null) {
            return;
        }

        SetIfPresent(ref target.userId, source.userId);
        SetIfPresent(ref target.username, source.username);
        SetIfPresent(ref target.avatarUrl, source.avatarUrl);
        SetIfPresent(ref target.country, source.country);
        SetIfPresent(ref target.ratingText, source.ratingText);
        SetIfPresent(ref target.ratingOverall, source.ratingOverall);
        SetIfPresent(ref target.rankingText, source.rankingText);
        SetIfPresent(ref target.rating19, source.rating19);
        SetIfPresent(ref target.rating13, source.rating13);
        SetIfPresent(ref target.rating9, source.rating9);
        SetIfPresent(ref target.statusText, source.statusText);
        SetIfPresent(ref target.registeredAt, source.registeredAt);
        SetIfPresent(ref target.about, source.about);
    }

    private static void SetIfPresent(ref string target, string source)
    {
        if (!string.IsNullOrWhiteSpace(source)) {
            target = source.Trim();
        }
    }

    private static List<OgsFriendListItem> ReadFriendListItems(JToken root, string baseUrl)
    {
        var result = new List<OgsFriendListItem>();
        JToken listToken = SelectFriendListToken(root);
        if (listToken is JArray array) {
            foreach (JToken token in array) {
                OgsFriendListItem item = ReadFriendListItem(token, baseUrl);
                if (item != null) {
                    result.Add(item);
                }
            }
            return result;
        }

        if (listToken is JObject obj) {
            foreach (JProperty property in obj.Properties()) {
                OgsFriendListItem item = ReadFriendListItem(property.Value, baseUrl);
                if (item != null) {
                    result.Add(item);
                }
            }
        }

        return result;
    }

    private static int ReadFriendListTotalCount(JToken root, int fallbackCount)
    {
        if (root is JObject obj && obj["count"] != null && int.TryParse(obj["count"].ToString(), out int count)) {
            return Math.Max(0, count);
        }

        return Math.Max(0, fallbackCount);
    }

    private static JToken SelectFriendListToken(JToken root)
    {
        if (root == null) {
            return null;
        }
        if (root is JArray) {
            return root;
        }
        if (root is JObject obj) {
            return obj["results"] ??
                obj["friends"] ??
                obj["users"] ??
                obj["players"] ??
                obj["items"] ??
                obj["data"] ??
                root;
        }

        return null;
    }

    private static OgsFriendListItem ReadFriendListItem(JToken token, string baseUrl)
    {
        JObject wrapper = token as JObject;
        JObject userJson = SelectFriendUserJson(wrapper);
        if (userJson == null) {
            return null;
        }

        string userId = ReadFirstString(userJson, "id", "user_id", "player_id", "pk", "uid");
        string username = ReadPlayerName(userJson);
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(username)) {
            return null;
        }

        return new OgsFriendListItem
        {
            userId = userId,
            username = username,
            avatarUrl = NormalizeOgsUrl(ReadFirstUrlString(userJson, "icon", "icon-url", "icon_url", "avatar", "avatar_url", "picture", "image", "image_url"), baseUrl),
            country = ReadFriendCountry(userJson),
            ratingText = BuildFriendRatingText(userJson),
            ratingOverall = ReadRating(userJson["ratings"]?["overall"]) ??
                ReadRating(userJson["rating"]) ??
                ReadRating(userJson["ratings"]) ??
                string.Empty,
            rankingText = FormatNumericString(ReadFirstString(userJson, "ranking", "rank")),
            rating19 = ReadRating(userJson["ratings"]?["19x19"]) ?? ReadRating(userJson["ratings"]?["19"]) ?? string.Empty,
            rating13 = ReadRating(userJson["ratings"]?["13x13"]) ?? ReadRating(userJson["ratings"]?["13"]) ?? string.Empty,
            rating9 = ReadRating(userJson["ratings"]?["9x9"]) ?? ReadRating(userJson["ratings"]?["9"]) ?? string.Empty,
            statusText = BuildFriendStatusText(userJson, wrapper),
            registeredAt = ReadFirstString(userJson, "date_joined", "created", "created_at", "registered", "registered_at", "registration_date"),
            about = ReadFirstString(userJson, "about", "bio", "biography", "description"),
        };
    }

    private static List<OgsFriendInvitationItem> ReadFriendInvitationItems(JToken root, string baseUrl)
    {
        var result = new List<OgsFriendInvitationItem>();
        JToken listToken = SelectFriendListToken(root);
        if (listToken is JArray array) {
            foreach (JToken token in array) {
                OgsFriendInvitationItem item = ReadFriendInvitationItem(token, baseUrl);
                if (item != null) {
                    result.Add(item);
                }
            }
            return result;
        }

        if (listToken is JObject obj) {
            foreach (JProperty property in obj.Properties()) {
                OgsFriendInvitationItem item = ReadFriendInvitationItem(property.Value, baseUrl);
                if (item != null) {
                    result.Add(item);
                }
            }
        }

        return result;
    }

    private static OgsFriendInvitationItem ReadFriendInvitationItem(JToken token, string baseUrl)
    {
        JObject wrapper = token as JObject;
        if (wrapper == null) {
            return null;
        }

        OgsFriendListItem fromUser = ReadFriendListItem(wrapper["from_user"] ?? wrapper["user"] ?? wrapper["player"] ?? wrapper, baseUrl);
        if (fromUser == null) {
            return null;
        }

        return new OgsFriendInvitationItem
        {
            fromUser = fromUser,
            createdAt = ReadFirstString(wrapper, "created", "created_at", "timestamp"),
            accepted = ReadFirstBool(wrapper, "accepted"),
        };
    }

    private static JObject SelectFriendUserJson(JObject wrapper)
    {
        if (wrapper == null) {
            return null;
        }

        JObject userJson =
            wrapper["friend"] as JObject ??
            wrapper["user"] as JObject ??
            wrapper["player"] as JObject ??
            wrapper["profile"] as JObject ??
            wrapper["target"] as JObject;
        if (HasFriendIdentity(userJson)) {
            return userJson;
        }

        return wrapper;
    }

    private static bool HasFriendIdentity(JObject json)
    {
        if (json == null) {
            return false;
        }

        return HasNonNullField(json, "id") ||
            HasNonNullField(json, "user_id") ||
            HasNonNullField(json, "player_id") ||
            HasNonNullField(json, "username") ||
            HasNonNullField(json, "name");
    }

    private static string ReadFriendCountry(JObject userJson)
    {
        string value = ReadFirstString(userJson["country"] as JObject, "code", "name");
        return string.IsNullOrWhiteSpace(value)
            ? ReadFirstString(userJson, "country", "country_code", "location")
            : value;
    }

    private static string BuildFriendRatingText(JObject userJson)
    {
        string rating = ReadRating(userJson["ratings"]?["overall"]) ??
            ReadRating(userJson["rating"]) ??
            ReadRating(userJson["ratings"]) ??
            string.Empty;
        string ranking = FormatNumericString(ReadFirstString(userJson, "ranking", "rank"));

        if (!string.IsNullOrWhiteSpace(rating) && !string.IsNullOrWhiteSpace(ranking)) {
            return $"分数 {rating} / 排名 {ranking}";
        }
        if (!string.IsNullOrWhiteSpace(rating)) {
            return $"分数 {rating}";
        }
        if (!string.IsNullOrWhiteSpace(ranking)) {
            return $"排名 {ranking}";
        }

        return string.Empty;
    }

    private static string BuildFriendStatusText(JObject userJson, JObject wrapper)
    {
        string explicitStatus = ReadFirstString(userJson, "status", "online_status", "availability", "state");
        if (string.IsNullOrWhiteSpace(explicitStatus) && wrapper != null && wrapper != userJson) {
            explicitStatus = ReadFirstString(wrapper, "status", "online_status", "availability", "state");
        }
        if (!string.IsNullOrWhiteSpace(explicitStatus)) {
            return explicitStatus;
        }

        bool hasOnline = TryReadBoolean(userJson, out bool online, "online", "is_online", "isOnline", "connected");
        if (!hasOnline && wrapper != null && wrapper != userJson) {
            hasOnline = TryReadBoolean(wrapper, out online, "online", "is_online", "isOnline", "connected");
        }
        if (hasOnline) {
            return online ? "在线" : "离线";
        }

        string lastOnline = ReadFirstString(userJson, "last_online", "last_seen", "seen_at");
        if (string.IsNullOrWhiteSpace(lastOnline) && wrapper != null && wrapper != userJson) {
            lastOnline = ReadFirstString(wrapper, "last_online", "last_seen", "seen_at");
        }
        return string.IsNullOrWhiteSpace(lastOnline) ? string.Empty : $"上次在线 {lastOnline}";
    }

    private static bool TryReadBoolean(JObject json, out bool value, params string[] fieldNames)
    {
        value = false;
        if (json == null || fieldNames == null) {
            return false;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token == null || token.Type == JTokenType.Null) {
                continue;
            }
            if (token.Type == JTokenType.Boolean) {
                value = token.ToObject<bool>();
                return true;
            }
            if (bool.TryParse(token.ToString(), out value)) {
                return true;
            }
        }

        return false;
    }

    private static string ReadFirstUrlString(JObject json, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return string.Empty;
        }

        foreach (string fieldName in fieldNames) {
            string value = ReadUrlToken(json[fieldName]);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ReadUrlToken(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return string.Empty;
        }

        if (token.Type == JTokenType.String || token.Type == JTokenType.Uri) {
            return token.ToString();
        }

        if (token is JObject obj) {
            return ReadFirstUrlString(obj, "url", "href", "src", "source", "uri", "icon", "avatar", "image");
        }

        if (token is JArray array) {
            foreach (JToken item in array) {
                string value = ReadUrlToken(item);
                if (!string.IsNullOrWhiteSpace(value)) {
                    return value;
                }
            }
        }

        return string.Empty;
    }

    private static string NormalizeOgsUrl(string value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _)) {
            return trimmed;
        }

        string safeBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? OgsConnectionConfig.DefaultApiBaseUrl
            : baseUrl.Trim().TrimEnd('/');

        if (trimmed.StartsWith("//", StringComparison.Ordinal)) {
            return $"https:{trimmed}";
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal)) {
            return $"{safeBaseUrl}{trimmed}";
        }

        return $"{safeBaseUrl}/{trimmed}";
    }

    private static void ReadCurrentUserFields(JObject json, OgsCurrentUserFields fields)
    {
        if (json == null || fields == null) {
            return;
        }

        if (string.IsNullOrEmpty(fields.userId)) {
            fields.userId = ReadFirstString(json, "sub", "id", "user_id", "pk", "uid");
        }
        if (string.IsNullOrEmpty(fields.username)) {
            fields.username = ReadFirstString(json, "preferred_username", "username", "name", "display_name");
        }
        if (string.IsNullOrEmpty(fields.avatarUrl)) {
            fields.avatarUrl = ReadFirstUrlString(json, "icon", "icon_url", "avatar", "avatar_url", "picture", "image", "image_url");
        }
        if (string.IsNullOrEmpty(fields.country)) {
            fields.country = ReadFirstString(json["country"] as JObject, "code", "name");
            fields.country = string.IsNullOrEmpty(fields.country)
                ? ReadFirstString(json, "country_code", "location", "country")
                : fields.country;
        }
        if (string.IsNullOrEmpty(fields.registeredAt)) {
            fields.registeredAt = ReadFirstString(json, "date_joined", "created", "created_at", "registered", "registered_at", "registration_date");
        }
        if (string.IsNullOrEmpty(fields.about)) {
            fields.about = ReadFirstString(json, "about", "bio", "biography", "description");
        }
        if (string.IsNullOrEmpty(fields.tags)) {
            fields.tags = BuildUserTags(json);
        }
        if (string.IsNullOrEmpty(fields.ranking)) {
            fields.ranking = FormatNumericString(ReadFirstString(json, "ranking", "rank"));
        }
        if (string.IsNullOrEmpty(fields.ratingOverall)) {
            fields.ratingOverall = ReadRating(json["ratings"]?["overall"]) ??
                ReadRating(json["rating"]) ??
                ReadRating(json["ratings"]) ??
                string.Empty;
        }
        if (string.IsNullOrEmpty(fields.rating19)) {
            fields.rating19 = ReadRating(json["ratings"]?["19x19"]) ?? ReadRating(json["ratings"]?["19"]) ?? string.Empty;
        }
        if (string.IsNullOrEmpty(fields.rating13)) {
            fields.rating13 = ReadRating(json["ratings"]?["13x13"]) ?? ReadRating(json["ratings"]?["13"]) ?? string.Empty;
        }
        if (string.IsNullOrEmpty(fields.rating9)) {
            fields.rating9 = ReadRating(json["ratings"]?["9x9"]) ?? ReadRating(json["ratings"]?["9"]) ?? string.Empty;
        }
    }

    private static string BuildUserTags(JObject json)
    {
        List<string> tags = new List<string>();
        AddTag(tags, ReadFirstString(json, "ui_class", "class", "title"));
        AddFlagTag(tags, json, "is_moderator", "moderator");
        AddFlagTag(tags, json, "is_superuser", "admin");
        AddFlagTag(tags, json, "professional", "pro");
        AddFlagTag(tags, json, "is_professional", "pro");
        AddFlagTag(tags, json, "is_bot", "bot");

        JToken groups = json["groups"] ?? json["badges"] ?? json["tags"];
        if (groups is JArray array) {
            foreach (JToken token in array) {
                AddTag(tags, token?.ToString());
            }
        }

        return tags.Count == 0 ? string.Empty : string.Join(" / ", tags);
    }

    private static void AddFlagTag(List<string> tags, JObject json, string fieldName, string tag)
    {
        JToken token = json[fieldName];
        if (token != null && token.Type == JTokenType.Boolean && token.ToObject<bool>()) {
            AddTag(tags, tag);
        }
    }

    private static void AddTag(List<string> tags, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        string trimmed = value.Trim();
        if (!tags.Contains(trimmed)) {
            tags.Add(trimmed);
        }
    }

    private static string ReadRating(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return null;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.String) {
            return FormatNumericString(token.ToString());
        }

        if (token is JObject obj) {
            return FormatNumericString(ReadFirstString(obj, "rating", "elo", "glicko", "score", "value"));
        }

        return null;
    }

    private static string FormatNumericString(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (double.TryParse(trimmed, out double number)) {
            return Math.Abs(number - Math.Round(number)) < 0.01
                ? Math.Round(number).ToString("0")
                : number.ToString("0.0");
        }

        return trimmed;
    }

    private sealed class OgsCurrentUserFields
    {
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

        public bool NeedsIdentity => string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username);

        public bool NeedsAnyProfileField =>
            NeedsIdentity ||
            string.IsNullOrEmpty(avatarUrl) ||
            string.IsNullOrEmpty(country) ||
            string.IsNullOrEmpty(registeredAt) ||
            string.IsNullOrEmpty(tags) ||
            string.IsNullOrEmpty(about) ||
            string.IsNullOrEmpty(ratingOverall) ||
            string.IsNullOrEmpty(ranking) ||
            string.IsNullOrEmpty(rating19) ||
            string.IsNullOrEmpty(rating13) ||
            string.IsNullOrEmpty(rating9);
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

    private struct OgsAutomatchStartSelection
    {
        public readonly int gameId;
        public readonly string message;
        public readonly string rawMessage;

        public OgsAutomatchStartSelection(int gameId, string message, string rawMessage)
        {
            this.gameId = gameId;
            this.message = message ?? string.Empty;
            this.rawMessage = rawMessage ?? string.Empty;
        }
    }

    private sealed class OgsChallengeGameIdProbeResult
    {
        public static readonly OgsChallengeGameIdProbeResult Pending = new OgsChallengeGameIdProbeResult(0, false, string.Empty);

        public readonly int gameId;
        public readonly bool challengeUnavailable;
        public readonly string message;

        private OgsChallengeGameIdProbeResult(int gameId, bool challengeUnavailable, string message)
        {
            this.gameId = gameId;
            this.challengeUnavailable = challengeUnavailable;
            this.message = message ?? string.Empty;
        }

        public static OgsChallengeGameIdProbeResult GameFound(int gameId)
        {
            return new OgsChallengeGameIdProbeResult(gameId, false, string.Empty);
        }

        public static OgsChallengeGameIdProbeResult Unavailable(string message)
        {
            return new OgsChallengeGameIdProbeResult(0, true, message);
        }
    }
}
