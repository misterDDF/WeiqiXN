using Newtonsoft.Json.Linq;

public enum OgsRealtimeGameMessageType
{
    GameData,
    Move,
    Clock,
    Phase,
    UndoAccepted,
    UndoCanceled,
    UndoRequested,
    Error,
    Closed,
}

public sealed class OgsRealtimeGameMessage
{
    public readonly OgsRealtimeGameMessageType messageType;
    public readonly string channel;
    public readonly JToken payload;
    public readonly string message;

    public OgsRealtimeGameMessage(OgsRealtimeGameMessageType messageType, string channel, JToken payload, string message = "")
    {
        this.messageType = messageType;
        this.channel = channel ?? string.Empty;
        this.payload = payload;
        this.message = message ?? string.Empty;
    }
}
