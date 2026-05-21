public readonly struct LanRoomSessionState
{
    public readonly LanRoomRole role;
    public readonly bool isConnected;
    public readonly bool hostReady;
    public readonly bool clientReady;
    public readonly bool gameStarted;

    public bool CanStartGame => isConnected && hostReady && clientReady && !gameStarted;

    public LanRoomSessionState(
        LanRoomRole role,
        bool isConnected,
        bool hostReady,
        bool clientReady,
        bool gameStarted)
    {
        this.role = role;
        this.isConnected = isConnected;
        this.hostReady = hostReady;
        this.clientReady = clientReady;
        this.gameStarted = gameStarted;
    }

    public string GetDisplayText()
    {
        if (role == LanRoomRole.None) {
            return "尚未进入房间。";
        }

        string roleText = role == LanRoomRole.Host ? "主机" : "客户端";
        string hostText = hostReady ? "主机已准备" : "主机未准备";
        string clientText = clientReady ? "客机已准备" : "客机未准备";
        string startText = gameStarted ? "已开局" : "未开局";
        return $"{roleText}  {hostText}  {clientText}  {startText}";
    }
}
