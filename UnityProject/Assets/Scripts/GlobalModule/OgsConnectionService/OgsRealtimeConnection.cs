using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

internal sealed class OgsRealtimeConnection : IDisposable
{
    private const int ReconnectDelayMilliseconds = 3000;

    private readonly Func<CancellationToken, Task<string>> userJwtFactory;
    private readonly Func<string> websocketUrlFactory;
    private readonly object syncRoot = new object();
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    private readonly HashSet<int> monitoredUserIds = new HashSet<int>();
    private readonly Dictionary<int, bool> userStates = new Dictionary<int, bool>();
    private readonly Dictionary<int, OgsRealtimeGameSession> gameSessions = new Dictionary<int, OgsRealtimeGameSession>();

    private CancellationTokenSource cancellationTokenSource;
    private Task runTask;
    private ClientWebSocket websocket;
    private bool isStarted;
    private bool isConnected;
    private bool isDisposed;

    public OgsRealtimeConnection(
        Func<CancellationToken, Task<string>> userJwtFactory,
        Func<string> websocketUrlFactory)
    {
        this.userJwtFactory = userJwtFactory ?? throw new ArgumentNullException(nameof(userJwtFactory));
        this.websocketUrlFactory = websocketUrlFactory ?? throw new ArgumentNullException(nameof(websocketUrlFactory));
    }

    public bool IsStarted
    {
        get
        {
            lock (syncRoot) {
                return isStarted && !isDisposed;
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (syncRoot) {
                return isConnected && !isDisposed;
            }
        }
    }

    public void Start()
    {
        lock (syncRoot) {
            if (isDisposed || isStarted) {
                return;
            }

            isStarted = true;
            cancellationTokenSource = new CancellationTokenSource();
            runTask = RunLoopAsync(cancellationTokenSource.Token);
        }
    }

    public void Stop()
    {
        CancellationTokenSource tokenSource;
        ClientWebSocket socket;
        lock (syncRoot) {
            if (!isStarted) {
                return;
            }

            isStarted = false;
            isConnected = false;
            tokenSource = cancellationTokenSource;
            cancellationTokenSource = null;
            socket = websocket;
            websocket = null;
        }

        tokenSource?.Cancel();
        _ = CloseSocketAsync(socket);
        tokenSource?.Dispose();
    }

    public void Dispose()
    {
        lock (syncRoot) {
            if (isDisposed) {
                return;
            }
            isDisposed = true;
        }

        Stop();
    }

    public void MonitorUsers(List<int> userIds)
    {
        if (userIds == null || userIds.Count <= 0) {
            return;
        }

        bool shouldSend;
        lock (syncRoot) {
            for (int i = 0; i < userIds.Count; i++) {
                int userId = userIds[i];
                if (userId <= 0) {
                    continue;
                }
                monitoredUserIds.Add(userId);
                if (!userStates.ContainsKey(userId)) {
                    userStates[userId] = false;
                }
            }
            shouldSend = isStarted && isConnected && websocket != null;
        }

        if (shouldSend) {
            _ = SendMonitorSnapshotAsync();
        }
    }

    public JObject GetUserStatesSnapshot(List<int> userIds)
    {
        var result = new JObject();
        if (userIds == null) {
            return result;
        }

        lock (syncRoot) {
            for (int i = 0; i < userIds.Count; i++) {
                int userId = userIds[i];
                if (userId <= 0) {
                    continue;
                }
                bool online = userStates.TryGetValue(userId, out bool value) && value;
                result[userId.ToString()] = online;
            }
        }

        return result;
    }

    public void ClearUserStates()
    {
        lock (syncRoot) {
            monitoredUserIds.Clear();
            userStates.Clear();
        }
    }

    public async Task<OgsRealtimeGameSession> CreateGameSessionAsync(int gameId, CancellationToken cancellationToken)
    {
        if (gameId <= 0) {
            throw new ArgumentException("OGS game id must be positive.", nameof(gameId));
        }

        Start();
        var session = new OgsRealtimeGameSession(this, gameId);
        lock (syncRoot) {
            if (gameSessions.TryGetValue(gameId, out OgsRealtimeGameSession existing)) {
                existing.Dispose();
            }
            gameSessions[gameId] = session;
        }

        try {
            await WaitUntilConnectedAsync(cancellationToken);
            await SendGameConnectAsync(gameId, cancellationToken);
            return session;
        }
        catch {
            UnregisterGameSession(gameId, session);
            session.Dispose();
            throw;
        }
    }

    internal async Task SendPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        ClientWebSocket socket;
        lock (syncRoot) {
            socket = websocket;
        }

        await SendPayloadAsync(socket, payload, cancellationToken);
    }

    internal async Task SendGameDisconnectAsync(int gameId, CancellationToken cancellationToken)
    {
        if (gameId <= 0) {
            return;
        }

        try {
            await SendPayloadAsync(BuildGameDisconnectPayload(gameId), cancellationToken);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS realtime global game disconnect send failed.", ("gameId", gameId.ToString()), ("err", ex.Message));
        }
    }

    internal void UnregisterGameSession(int gameId, OgsRealtimeGameSession session)
    {
        lock (syncRoot) {
            if (gameSessions.TryGetValue(gameId, out OgsRealtimeGameSession existing) && existing == session) {
                gameSessions.Remove(gameId);
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            ClientWebSocket socket = null;
            try {
                string websocketUrl = websocketUrlFactory();
                if (string.IsNullOrWhiteSpace(websocketUrl)) {
                    throw new InvalidOperationException("OGS websocket URL is empty.");
                }

                string userJwt = await userJwtFactory(cancellationToken);
                if (string.IsNullOrWhiteSpace(userJwt)) {
                    throw new InvalidOperationException("OGS ui config did not include user_jwt.");
                }

                socket = new ClientWebSocket();
                lock (syncRoot) {
                    if (!isStarted || cancellationToken.IsCancellationRequested) {
                        socket.Dispose();
                        return;
                    }
                    websocket = socket;
                }

                await socket.ConnectAsync(new Uri(websocketUrl.Trim()), cancellationToken);
                await SendPayloadAsync(socket, BuildAuthenticatePayload(userJwt), cancellationToken);
                SetConnected(socket, true);
                await SendMonitorSnapshotAsync(cancellationToken);
                await SendGameConnectSnapshotAsync(cancellationToken);
                XNLogger.LogInfo("OGS realtime global connection established.");
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) {
                XNLogger.LogWarn("OGS realtime global connection failed.", ("err", ex.Message));
            }
            finally {
                SetConnected(socket, false);
                NotifyGameSessionsClosed("global realtime disconnected");
                await CloseSocketAsync(socket);
            }

            try {
                await Task.Delay(ReconnectDelayMilliseconds, cancellationToken);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
            socket != null &&
            (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)) {
            string message = await ReceiveMessageAsync(socket, cancellationToken);
            if (string.IsNullOrEmpty(message)) {
                continue;
            }

            LogVerboseRealtimePayload("OGS realtime global received.", message);
            HandleMessage(message);
        }
    }

    private void HandleMessage(string message)
    {
        JArray envelope;
        try {
            envelope = JArray.Parse(message);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS realtime global message parse failed.", ("err", ex.Message));
            return;
        }

        if (envelope.Count < 2) {
            return;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        if (channel == "user/state" && envelope[1] is JObject states) {
            ApplyUserStates(states);
            return;
        }

        if (TryReadGameId(channel, out int gameId)) {
            OgsRealtimeGameSession session = null;
            lock (syncRoot) {
                gameSessions.TryGetValue(gameId, out session);
            }
            session?.EnqueueParsedMessage(channel, envelope[1]);
        }
    }

    private void ApplyUserStates(JObject states)
    {
        if (states == null) {
            return;
        }

        lock (syncRoot) {
            foreach (JProperty property in states.Properties()) {
                if (!int.TryParse(property.Name, out int userId) || userId <= 0) {
                    continue;
                }
                if (TryReadOnlineState(property.Value, out bool online)) {
                    userStates[userId] = online;
                }
            }
        }
    }

    private async Task SendMonitorSnapshotAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        ClientWebSocket socket;
        List<int> ids;
        lock (syncRoot) {
            if (!isStarted || !isConnected || websocket == null || monitoredUserIds.Count <= 0) {
                return;
            }

            socket = websocket;
            ids = new List<int>(monitoredUserIds);
        }

        try {
            await SendPayloadAsync(socket, BuildUserMonitorPayload(ids), cancellationToken);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS realtime global user monitor send failed.", ("err", ex.Message));
        }
    }

    private async Task SendGameConnectSnapshotAsync(CancellationToken cancellationToken)
    {
        List<int> gameIds;
        lock (syncRoot) {
            if (!isStarted || !isConnected || websocket == null || gameSessions.Count <= 0) {
                return;
            }

            gameIds = new List<int>(gameSessions.Keys);
        }

        for (int i = 0; i < gameIds.Count; i++) {
            try {
                await SendGameConnectAsync(gameIds[i], cancellationToken);
            }
            catch (Exception ex) {
                XNLogger.LogWarn("OGS realtime global game reconnect send failed.", ("gameId", gameIds[i].ToString()), ("err", ex.Message));
            }
        }
    }

    private async Task SendGameConnectAsync(int gameId, CancellationToken cancellationToken)
    {
        await SendPayloadAsync(BuildGameConnectPayload(gameId), cancellationToken);
    }

    private async Task WaitUntilConnectedAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(OgsConnectionConfig.RequestTimeoutMilliseconds);
        while (!cancellationToken.IsCancellationRequested) {
            if (IsConnected) {
                return;
            }
            if (DateTime.UtcNow >= deadline) {
                throw new TimeoutException("Timed out waiting for OGS realtime global connection.");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private void NotifyGameSessionsClosed(string reason)
    {
        List<OgsRealtimeGameSession> sessions;
        lock (syncRoot) {
            if (gameSessions.Count <= 0) {
                return;
            }

            sessions = new List<OgsRealtimeGameSession>(gameSessions.Values);
        }

        for (int i = 0; i < sessions.Count; i++) {
            sessions[i]?.NotifyClosed(reason);
        }
    }

    private void SetConnected(ClientWebSocket socket, bool connected)
    {
        lock (syncRoot) {
            if (socket != null && websocket != socket) {
                return;
            }
            isConnected = connected;
            if (!connected && socket == websocket) {
                websocket = null;
            }
        }
    }

    private async Task SendPayloadAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        if (socket == null || socket.State != WebSocketState.Open) {
            throw new InvalidOperationException($"OGS realtime global socket is not open: {socket?.State.ToString() ?? "null"}");
        }

        await sendLock.WaitAsync(cancellationToken);
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }
        finally {
            sendLock.Release();
        }
        LogVerboseRealtimePayload("OGS realtime global sent.", payload);
    }

    private static async Task<string> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        var messageBuilder = new StringBuilder();
        WebSocketReceiveResult result;
        do {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) {
                return messageBuilder.ToString();
            }
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        return messageBuilder.ToString();
    }

    private static async Task CloseSocketAsync(ClientWebSocket socket)
    {
        if (socket == null) {
            return;
        }

        try {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived) {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
            }
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS realtime global socket close failed.", ("err", ex.Message));
        }
        finally {
            socket.Dispose();
        }
    }

    private static string BuildAuthenticatePayload(string userJwt)
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

    private static string BuildUserMonitorPayload(List<int> userIds)
    {
        var payloadUserIds = new JArray();
        if (userIds != null) {
            userIds.Sort();
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

    private static string BuildGameDisconnectPayload(int gameId)
    {
        var payload = new JArray
        {
            "game/disconnect",
            new JObject
            {
                ["game_id"] = gameId,
            },
        };
        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static bool TryReadGameId(string channel, out int gameId)
    {
        gameId = 0;
        if (string.IsNullOrWhiteSpace(channel) || !channel.StartsWith("game/", StringComparison.Ordinal)) {
            return false;
        }

        int start = "game/".Length;
        int slashIndex = channel.IndexOf('/', start);
        if (slashIndex <= start) {
            return false;
        }

        return int.TryParse(channel.Substring(start, slashIndex - start), out gameId) && gameId > 0;
    }

    private static bool TryReadOnlineState(JToken token, out bool online)
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

    private static void LogVerboseRealtimePayload(string message, string payload)
    {
        if (!LoggerConfig.ENABLE_OGS_VERBOSE_LOG) {
            return;
        }

        XNLogger.LogInfo(message, ("payload", payload ?? string.Empty));
    }
}
