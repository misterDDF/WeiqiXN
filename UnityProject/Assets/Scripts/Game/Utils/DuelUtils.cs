public static class DuelUtils
{
    public static string GetGamePrefabTypeIdWithPlayerFlag(PlayerFlag playerFlag)
    {
        switch (playerFlag) {
            case PlayerFlag.Player1:
                return "ChessBlack";
            case PlayerFlag.Player2:
                return "ChessWhite";
        }
        return string.Empty;
    }

    public static string GetPreviewGamePrefabTypeIdWithPlayerFlag(PlayerFlag playerFlag)
    {
        switch (playerFlag) {
            case PlayerFlag.Player1:
                return "ChessBlackPreview";
            case PlayerFlag.Player2:
                return "ChessWhitePreview";
        }
        return string.Empty;
    }

    public static PlayerFlag GetOpponentPlayerFlag(this PlayerFlag playerFlag)
    {
        if (playerFlag == PlayerFlag.Player1) {
            return PlayerFlag.Player2;
        } else {
            return PlayerFlag.Player1;
        }
    }

    public static PlayerFlag GetValidPlayerFlag(PlayerFlag playerFlag, PlayerFlag defaultPlayerFlag = PlayerFlag.Player1)
    {
        return playerFlag == PlayerFlag.Player1 || playerFlag == PlayerFlag.Player2
            ? playerFlag
            : defaultPlayerFlag;
    }
}
