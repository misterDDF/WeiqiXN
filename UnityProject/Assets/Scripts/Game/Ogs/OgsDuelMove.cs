using XNClient.ChessBoard;

public sealed class OgsDuelMove
{
    public PlayerFlag playerFlag;
    public RectCoordinates coords;
    public bool isPass;
    public int moveNumber;

    public OgsDuelMove(PlayerFlag playerFlag, RectCoordinates coords, bool isPass, int moveNumber)
    {
        this.playerFlag = playerFlag;
        this.coords = coords;
        this.isPass = isPass;
        this.moveNumber = moveNumber;
    }
}

public sealed class OgsDuelInitialStone
{
    public PlayerFlag playerFlag;
    public RectCoordinates coords;

    public OgsDuelInitialStone(PlayerFlag playerFlag, RectCoordinates coords)
    {
        this.playerFlag = playerFlag;
        this.coords = coords;
    }
}
