public readonly struct LanRoomInfo
{
    public readonly string roomId;
    public readonly string name;
    public readonly string hostAddress;
    public readonly int tcpPort;
    public readonly int playerCount;
    public readonly int maxPlayerCount;
    public readonly string hostPlayerName;
    public readonly string boardCfgId;
    public readonly string holdTimeCfgId;
    public readonly string byoyomiCountCfgId;
    public readonly string byoyomiTimeCfgId;
    public readonly string handicapCfgId;
    public readonly PlayerFlag hostPlayerFlag;
    public readonly string hostPlayerSideCfgId;

    public LanRoomInfo(string roomId, string name, string hostAddress, int tcpPort, int playerCount, int maxPlayerCount)
        : this(
            roomId,
            name,
            hostAddress,
            tcpPort,
            playerCount,
            maxPlayerCount,
            string.Empty,
            "9x9",
            "infinite",
            "off",
            "30s",
            "9x9_0",
            PlayerFlag.Player1,
            "black")
    {
    }

    public LanRoomInfo(
        string roomId,
        string name,
        string hostAddress,
        int tcpPort,
        int playerCount,
        int maxPlayerCount,
        string hostPlayerName,
        string boardCfgId,
        string holdTimeCfgId,
        string byoyomiCountCfgId,
        string byoyomiTimeCfgId,
        string handicapCfgId,
        PlayerFlag hostPlayerFlag,
        string hostPlayerSideCfgId)
    {
        this.roomId = roomId;
        this.name = name;
        this.hostAddress = hostAddress;
        this.tcpPort = tcpPort;
        this.playerCount = playerCount;
        this.maxPlayerCount = maxPlayerCount;
        this.hostPlayerName = string.IsNullOrWhiteSpace(hostPlayerName) ? "Host" : hostPlayerName.Trim();
        this.boardCfgId = string.IsNullOrEmpty(boardCfgId) ? "9x9" : boardCfgId;
        this.holdTimeCfgId = string.IsNullOrEmpty(holdTimeCfgId) ? "infinite" : holdTimeCfgId;
        this.byoyomiCountCfgId = string.IsNullOrEmpty(byoyomiCountCfgId) ? "off" : byoyomiCountCfgId;
        this.byoyomiTimeCfgId = string.IsNullOrEmpty(byoyomiTimeCfgId) ? "30s" : byoyomiTimeCfgId;
        this.handicapCfgId = DuelHandicapPlacement.GetValidCfgId(handicapCfgId, this.boardCfgId);
        this.hostPlayerFlag = DuelUtils.GetValidPlayerFlag(hostPlayerFlag);
        this.hostPlayerSideCfgId = GetValidHostPlayerSideCfgId(hostPlayerSideCfgId, this.hostPlayerFlag);
    }

    public string GetDisplayText()
    {
        return $"{name}  {playerCount}/{maxPlayerCount}\n房主：{hostPlayerName}（{GetHostPlayerFlagText()}）  {GetDuelConfigText()}\n{hostAddress}:{tcpPort}";
    }

    private string GetDuelConfigText()
    {
        return $"{GetBoardDisplayText()}  {GetTimeControlDisplayText()}  {GetHandicapDisplayText()}";
    }

    private string GetBoardDisplayText()
    {
        ChessBoardDataType data = ChessBoardDataType.GetConfigData(boardCfgId);
        return data != null ? $"{data.boardSize}路" : boardCfgId;
    }

    private string GetTimeControlDisplayText()
    {
        DuelHoldTimeDataType holdTimeData = DuelHoldTimeDataType.GetConfigData(holdTimeCfgId);
        string holdTimeText = holdTimeData != null ? holdTimeData.displayName : holdTimeCfgId;
        if (holdTimeCfgId == "infinite" || byoyomiCountCfgId == "off") {
            return holdTimeText;
        }

        DuelByoyomiCountDataType byoyomiCountData = DuelByoyomiCountDataType.GetConfigData(byoyomiCountCfgId);
        DuelByoyomiTimeDataType byoyomiTimeData = DuelByoyomiTimeDataType.GetConfigData(byoyomiTimeCfgId);
        string byoyomiCountText = byoyomiCountData != null ? byoyomiCountData.displayName : byoyomiCountCfgId;
        string byoyomiTimeText = byoyomiTimeData != null ? byoyomiTimeData.displayName : byoyomiTimeCfgId;
        return $"{holdTimeText}+{byoyomiCountText}x{byoyomiTimeText}";
    }

    private string GetHandicapDisplayText()
    {
        DuelHandicapDataType data = DuelHandicapDataType.GetConfigData(handicapCfgId);
        return data != null ? data.displayName : handicapCfgId;
    }

    private string GetHostPlayerFlagText()
    {
        switch (hostPlayerSideCfgId) {
            case "guess":
                return "猜先";
            case "white":
                return "执白";
            case "black":
            default:
                return "执黑";
        }
    }

    private static string GetValidHostPlayerSideCfgId(string hostPlayerSideCfgId, PlayerFlag fallbackPlayerFlag)
    {
        if (hostPlayerSideCfgId == "guess" || hostPlayerSideCfgId == "black" || hostPlayerSideCfgId == "white") {
            return hostPlayerSideCfgId;
        }

        return fallbackPlayerFlag == PlayerFlag.Player2 ? "white" : "black";
    }
}
