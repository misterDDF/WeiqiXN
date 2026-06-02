using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public sealed class OgsRealtimeGameSession : IDisposable
{
    private readonly ClientWebSocket websocket;
    private readonly int gameId;
    private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    private readonly ConcurrentQueue<OgsRealtimeGameMessage> messageQueue = new ConcurrentQueue<OgsRealtimeGameMessage>();
    private Task receiveTask;
    private bool isDisposed;

    public int GameId => gameId;
    public bool IsOpen => websocket != null && websocket.State == WebSocketState.Open && !isDisposed;

    public OgsRealtimeGameSession(ClientWebSocket websocket, int gameId)
    {
        this.websocket = websocket;
        this.gameId = gameId;
    }

    public void StartReceiveLoop()
    {
        if (receiveTask != null) {
            return;
        }

        receiveTask = ReceiveLoopAsync();
    }

    public bool TryDequeueMessage(out OgsRealtimeGameMessage message)
    {
        return messageQueue.TryDequeue(out message);
    }

    public async Task SendMoveAsync(string packedMove, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrEmpty(packedMove)) {
            throw new ArgumentException("OGS move is empty.", nameof(packedMove));
        }

        var payload = new JArray
        {
            "game/move",
            new JObject
            {
                ["game_id"] = gameId,
                ["move"] = packedMove,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task SendResignAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        var payload = new JArray
        {
            "game/resign",
            new JObject
            {
                ["game_id"] = gameId,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task SendUndoRequestAsync(int moveNumber, CancellationToken cancellationToken = default(CancellationToken))
    {
        var payload = new JArray
        {
            "game/undo/request",
            new JObject
            {
                ["game_id"] = gameId,
                ["move_number"] = moveNumber,
                ["undo_move_count"] = 1,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task SendUndoAcceptAsync(int moveNumber, CancellationToken cancellationToken = default(CancellationToken))
    {
        var payload = new JArray
        {
            "game/undo/accept",
            new JObject
            {
                ["game_id"] = gameId,
                ["move_number"] = moveNumber,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task SendUndoCancelAsync(int moveNumber, CancellationToken cancellationToken = default(CancellationToken))
    {
        var payload = new JArray
        {
            "game/undo/cancel",
            new JObject
            {
                ["game_id"] = gameId,
                ["move_number"] = moveNumber,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        if (isDisposed) {
            return;
        }

        try {
            if (websocket.State == WebSocketState.Open) {
                var payload = new JArray
                {
                    "game/disconnect",
                    new JObject
                    {
                        ["game_id"] = gameId,
                    },
                };
                await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", cancellationToken);
            }
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS realtime disconnect failed.", ("gameId", gameId.ToString()), ("err", ex.Message));
        }
    }

    public void Dispose()
    {
        if (isDisposed) {
            return;
        }

        isDisposed = true;
        cancellationTokenSource.Cancel();
        websocket.Dispose();
        cancellationTokenSource.Dispose();
    }

    private async Task SendPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        if (!IsOpen) {
            throw new InvalidOperationException($"OGS realtime socket is not open: {websocket.State}");
        }

        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationTokenSource.Token)) {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await websocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                linked.Token);
        }
        LogVerboseRealtimePayload("OGS realtime sent.", payload);
    }

    private async Task ReceiveLoopAsync()
    {
        try {
            while (!cancellationTokenSource.IsCancellationRequested &&
                (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.CloseReceived)) {
                string rawMessage = await ReceiveMessageAsync(cancellationTokenSource.Token);
                if (string.IsNullOrEmpty(rawMessage)) {
                    continue;
                }

                LogVerboseRealtimePayload("OGS realtime received.", rawMessage);
                EnqueueParsedMessage(rawMessage);
            }
        }
        catch (OperationCanceledException) {
        }
        catch (Exception ex) {
            XNLogger.LogError("OGS realtime receive loop failed.", ("gameId", gameId.ToString()), ("err", ex.Message));
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.Closed, string.Empty, null, ex.Message));
        }
        finally {
            if (!isDisposed) {
                messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.Closed, string.Empty, null, websocket.State.ToString()));
            }
        }
    }

    private async Task<string> ReceiveMessageAsync(CancellationToken cancellationToken)
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

    private void EnqueueParsedMessage(string rawMessage)
    {
        JArray envelope;
        try {
            envelope = JArray.Parse(rawMessage);
        }
        catch (Exception ex) {
            XNLogger.LogWarn("OGS realtime message parse failed.", ("gameId", gameId.ToString()), ("err", ex.Message));
            return;
        }

        if (envelope.Count < 2) {
            return;
        }

        string channel = envelope[0]?.ToString() ?? string.Empty;
        JToken payload = envelope[1];
        if (channel == $"game/{gameId}/gamedata" || channel == $"game/{gameId}/data") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.GameData, channel, payload));
        } else if (channel == $"game/{gameId}/move") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.Move, channel, payload));
        } else if (channel == $"game/{gameId}/clock") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.Clock, channel, payload));
        } else if (channel == $"game/{gameId}/phase") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.Phase, channel, payload));
        } else if (channel == $"game/{gameId}/undo_accepted") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.UndoAccepted, channel, payload));
        } else if (channel == $"game/{gameId}/undo_canceled") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.UndoCanceled, channel, payload));
        } else if (channel == $"game/{gameId}/undo_requested") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.UndoRequested, channel, payload));
        } else if (channel == $"game/{gameId}/error") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.Error, channel, payload, payload?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty));
        } else if (channel.StartsWith($"game/{gameId}/", StringComparison.Ordinal)) {
            XNLogger.LogInfo(
                "OGS realtime unhandled game message.",
                ("gameId", gameId.ToString()),
                ("channel", channel),
                ("payload", TrimForLog(payload?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty)));
        }
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        return value.Length <= 300 ? value : value.Substring(0, 300);
    }

    private void LogVerboseRealtimePayload(string message, string payload)
    {
        if (!LoggerConfig.ENABLE_OGS_VERBOSE_LOG) {
            return;
        }

        XNLogger.LogInfo(
            message,
            ("gameId", gameId.ToString()),
            ("payload", payload ?? string.Empty));
    }
}
