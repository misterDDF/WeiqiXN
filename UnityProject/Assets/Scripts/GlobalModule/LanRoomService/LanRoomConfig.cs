using System;
using XNClient.Logger;

public static class LanRoomConfig
{
    private const int DefaultUdpBroadcastPort = 47861;
    private const int DefaultTcpListenPort = 47862;
    private const int DefaultConnectTimeoutMilliseconds = 2000;
    private const int DefaultMaxPlayerCount = 2;
    private const int DefaultBroadcastIntervalMilliseconds = 1000;
    private const int DefaultHandshakeBufferSize = 128;
    private const int DefaultSessionReadBufferSize = 512;

    public static int UdpBroadcastPort => GetInt("udpBroadcastPort", DefaultUdpBroadcastPort);
    public static int TcpListenPort => GetInt("tcpListenPort", DefaultTcpListenPort);
    public static int ConnectTimeoutMilliseconds => GetInt("connectTimeoutMilliseconds", DefaultConnectTimeoutMilliseconds);
    public static int MaxPlayerCount => GetInt("maxPlayerCount", DefaultMaxPlayerCount);
    public static int BroadcastIntervalMilliseconds => GetInt("broadcastIntervalMilliseconds", DefaultBroadcastIntervalMilliseconds);
    public static int HandshakeBufferSize => GetInt("handshakeBufferSize", DefaultHandshakeBufferSize);
    public static int SessionReadBufferSize => GetInt("sessionReadBufferSize", DefaultSessionReadBufferSize);

    private static int GetInt(string key, int fallbackValue)
    {
        string value = GetRawValue(key);
        if (int.TryParse(value, out int result)) {
            return result;
        }

        XNLogger.LogWarn(
            "LAN room config int value invalid, fallback will be used.",
            ("key", key),
            ("value", value ?? string.Empty),
            ("fallback", fallbackValue.ToString()));
        return fallbackValue;
    }

    private static string GetRawValue(string key)
    {
        try {
            LanRoomConfigDataType data = LanRoomConfigDataType.GetConfigData(key);
            return data?.value;
        }
        catch (Exception e) {
            XNLogger.LogWarn("Read LAN room config failed.", ("key", key), ("error", e.Message));
            return null;
        }
    }
}
