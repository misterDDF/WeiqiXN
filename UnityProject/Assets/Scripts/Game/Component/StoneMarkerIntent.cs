public enum StoneMarkerType
{
    None,
    LatestTriangle,
    MoveNumber,
}

public readonly struct StoneMarkerIntent
{
    public readonly StoneMarkerType markerType;
    public readonly int moveNumber;
    public readonly bool isBlackStone;

    public bool IsValid => markerType != StoneMarkerType.None;

    private StoneMarkerIntent(StoneMarkerType markerType, int moveNumber, bool isBlackStone)
    {
        this.markerType = markerType;
        this.moveNumber = moveNumber;
        this.isBlackStone = isBlackStone;
    }

    public static StoneMarkerIntent LatestTriangle(bool isBlackStone)
    {
        return new StoneMarkerIntent(StoneMarkerType.LatestTriangle, 0, isBlackStone);
    }

    public static StoneMarkerIntent MoveNumber(int moveNumber, bool isBlackStone)
    {
        return new StoneMarkerIntent(StoneMarkerType.MoveNumber, moveNumber, isBlackStone);
    }
}
