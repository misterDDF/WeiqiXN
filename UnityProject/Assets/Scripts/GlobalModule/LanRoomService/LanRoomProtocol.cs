public enum LanRoomProtocol
{
    Ready,
    State,
    Start,
    StartConfig,
    SubmitMove,
    MoveAccepted,
    MoveRejected,
    BoardSnapshot,
    TimeState,
    PlayerTimeout,
    SubmitResign,
    ResignAccepted,
}

public static class LanRoomProtocolName
{
    public const string DiscoveryPrefix = "WEIQIXN_LAN_ROOM";
    public const string ClientHello = "WEIQIXN_JOIN";
    public const string HostAccept = "WEIQIXN_ACCEPT";
    public const string HostFull = "WEIQIXN_FULL";
    public const string HostReject = "WEIQIXN_REJECT";

    public static string ToWireName(LanRoomProtocol protocol)
    {
        return protocol.ToString();
    }

    public static string ToHandlerName(LanRoomProtocol protocol)
    {
        return "On" + protocol;
    }
}

public sealed class LanRoomProtocolMessage
{
    public LanRoomProtocolMessage(string protocol, string[] args)
    {
        this.protocol = protocol;
        this.args = args;
    }

    public string protocol { get; }
    public string[] args { get; }
    public int ArgCount => args.Length;

    public string GetArg(int index)
    {
        return args[index];
    }

    public static bool TryParse(string text, out LanRoomProtocolMessage message)
    {
        message = null;
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        string[] parts = text.Split('|');
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0])) {
            return false;
        }

        string[] args = new string[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++) {
            args[i - 1] = parts[i];
        }

        message = new LanRoomProtocolMessage(parts[0], args);
        return true;
    }
}
