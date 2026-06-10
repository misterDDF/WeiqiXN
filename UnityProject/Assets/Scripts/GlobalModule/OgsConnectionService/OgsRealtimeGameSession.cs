using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public sealed class OgsRealtimeGameSession : IDisposable
{
    private readonly OgsRealtimeConnection connection;
    private readonly int gameId;
    private readonly ConcurrentQueue<OgsRealtimeGameMessage> messageQueue = new ConcurrentQueue<OgsRealtimeGameMessage>();
    private bool isDisposed;

    public int GameId => gameId;
    public bool IsOpen => connection != null && connection.IsConnected && !isDisposed;

    internal OgsRealtimeGameSession(OgsRealtimeConnection connection, int gameId)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.gameId = gameId;
    }

    public void StartReceiveLoop()
    {
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

    public async Task SendRemovedStonesSetAsync(string stones, bool removed, bool strictSekiMode, CancellationToken cancellationToken = default(CancellationToken))
    {
        var payload = new JArray
        {
            "game/removed_stones/set",
            new JObject
            {
                ["game_id"] = gameId,
                ["stones"] = stones ?? string.Empty,
                ["removed"] = removed,
                ["strict_seki_mode"] = strictSekiMode,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task SendRemovedStonesAcceptAsync(string stones, bool strictSekiMode, CancellationToken cancellationToken = default(CancellationToken))
    {
        var payload = new JArray
        {
            "game/removed_stones/accept",
            new JObject
            {
                ["game_id"] = gameId,
                ["stones"] = stones ?? string.Empty,
                ["strict_seki_mode"] = strictSekiMode,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task SendRemovedStonesRejectAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        var payload = new JArray
        {
            "game/removed_stones/reject",
            new JObject
            {
                ["game_id"] = gameId,
            },
        };
        await SendPayloadAsync(payload.ToString(Newtonsoft.Json.Formatting.None), cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        if (isDisposed) {
            return;
        }

        await connection.SendGameDisconnectAsync(gameId, cancellationToken);
    }

    public void Dispose()
    {
        if (isDisposed) {
            return;
        }

        isDisposed = true;
        connection.UnregisterGameSession(gameId, this);
    }

    internal void EnqueueParsedMessage(string channel, JToken payload)
    {
        if (isDisposed) {
            return;
        }

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
        } else if (channel == $"game/{gameId}/removed_stones") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.RemovedStones, channel, payload));
        } else if (channel == $"game/{gameId}/removed_stones_accepted") {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.RemovedStonesAccepted, channel, payload));
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

    internal void NotifyClosed(string message)
    {
        if (!isDisposed) {
            messageQueue.Enqueue(new OgsRealtimeGameMessage(OgsRealtimeGameMessageType.Closed, string.Empty, null, message ?? string.Empty));
        }
    }

    private async Task SendPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        if (!IsOpen) {
            throw new InvalidOperationException("OGS realtime game session is not open.");
        }

        await connection.SendPayloadAsync(payload, cancellationToken);
        LogVerboseRealtimePayload("OGS realtime sent.", payload);
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
