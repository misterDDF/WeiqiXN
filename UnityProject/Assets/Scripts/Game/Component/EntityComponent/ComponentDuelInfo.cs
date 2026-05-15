using XNClient.ChessBoard;

public class ComponentDuelInfo : EntityComponentBase
{
    public SavableField<int> turnLeftTimes = SavableFieldFactory.CreateIntField(0);
    public SavableField<int> holdLeftSeconds = SavableFieldFactory.CreateIntField(0);
    public SavableField<int> byoyomiLeftCount = SavableFieldFactory.CreateIntField(0);
    public SavableField<int> byoyomiLeftSeconds = SavableFieldFactory.CreateIntField(0);
    public SavableField<bool> isInByoyomi = SavableFieldFactory.CreateBoolField(false);
    public SavableField<bool> isInfiniteTime = SavableFieldFactory.CreateBoolField(false);
    public RectCoordinates lastChessCoord = new RectCoordinates(-1, -1);

    public ComponentDuelInfo(Player owner) : base(owner)
    {

    }
}
