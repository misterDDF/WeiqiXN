public readonly struct LanRoomInfo
{
    public readonly string roomId;
    public readonly string name;
    public readonly string hostAddress;
    public readonly int tcpPort;
    public readonly int playerCount;
    public readonly int maxPlayerCount;

    public LanRoomInfo(string roomId, string name, string hostAddress, int tcpPort, int playerCount, int maxPlayerCount)
    {
        this.roomId = roomId;
        this.name = name;
        this.hostAddress = hostAddress;
        this.tcpPort = tcpPort;
        this.playerCount = playerCount;
        this.maxPlayerCount = maxPlayerCount;
    }

    public string GetDisplayText()
    {
        return $"{name}  {hostAddress}:{tcpPort}  {playerCount}/{maxPlayerCount}";
    }
}
