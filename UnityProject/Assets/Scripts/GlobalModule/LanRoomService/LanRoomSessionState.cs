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
            return MessageText.Get("lan_room_not_joined");
        }

        string roleText = role == LanRoomRole.Host
            ? MessageText.Get("lan_room_role_host")
            : MessageText.Get("lan_room_role_client");
        string hostText = hostReady
            ? MessageText.Get("lan_room_host_ready")
            : MessageText.Get("lan_room_host_not_ready");
        string clientText = clientReady
            ? MessageText.Get("lan_room_client_ready")
            : MessageText.Get("lan_room_client_not_ready");
        string startText = gameStarted
            ? MessageText.Get("lan_room_game_started")
            : MessageText.Get("lan_room_game_not_started");
        return MessageText.Format("lan_room_session_state", roleText, hostText, clientText, startText);
    }
}
