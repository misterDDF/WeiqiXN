using System;
using XNClient.ChessBoard;
using XNClient.Logger;

public interface ISystemEventHandler
{
    string eventType { get; }
    IEventReceiver receiver { get; }
    public void Execute(SystemEventBase systemEvent);
}

public class SystemEventHandler<TEvent> : ISystemEventHandler where TEvent : SystemEventBase
{
    public IEventReceiver receiver { get; private set; }
    public Action<TEvent> callback;
    public string eventType => SystemEventBase.GetEventType<TEvent>();

    public SystemEventHandler(IEventReceiver receiver, Action<TEvent> callback)
    {
        this.receiver = receiver;
        this.callback = callback;
    }

    public void Execute(SystemEventBase systemEvent)
    {
        if (systemEvent is TEvent tEvent) {
            callback?.Invoke(tEvent);
        } else {
            XNLogger.LogError("Type not matched, execute system event failed.", ("dstEvent", SystemEventBase.GetEventType<TEvent>()), ("srcEvent", systemEvent.GetEventType()));
        }
    }
}

public abstract class SystemEventBase
{
    public static string GetEventType<TEvent>() where TEvent : SystemEventBase
    {
        return typeof(TEvent).Name;
    }

    public abstract string GetEventType();
}

public class OnActiveSceneChanged : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnActiveSceneChanged>();
}

public class OnExitMainScene : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnExitMainScene>();

    public SceneBase scene;
    public OnExitMainScene(SceneBase scene)
    {
        this.scene = scene;
    }
}

public class OnSaveDuelScene : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSaveDuelScene>();
}

public class OnRequestDuelOwnership : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnRequestDuelOwnership>();
}

public class OnRequestClearDuelOwnership : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnRequestClearDuelOwnership>();
}

public class DuelScoreResult
{
    public float blackScore;
    public float whiteScore;
    public float komi;
    public float margin;
    public PlayerFlag winnerFlag;
    public string scoreSource;
}

public class OnRequestDuelScore : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnRequestDuelScore>();
}

public class OnConfirmDuelScore : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnConfirmDuelScore>();

    public DuelScoreResult scoreResult;
    public OnConfirmDuelScore(DuelScoreResult scoreResult)
    {
        this.scoreResult = scoreResult;
    }
}

public class OnConfirmDuelResign : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnConfirmDuelResign>();
}

public class OnRequestDuelPass : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnRequestDuelPass>();
}

public class OnRequestDuelTakeBack : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnRequestDuelTakeBack>();
}

public class OnDuelTakeBackResult : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnDuelTakeBackResult>();

    public bool success;
    public string message;
    public int removedMoveCount;

    public OnDuelTakeBackResult(bool success, string message, int removedMoveCount = 0)
    {
        this.success = success;
        this.message = message;
        this.removedMoveCount = removedMoveCount;
    }
}

public class OnDuelPassAccepted : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnDuelPassAccepted>();

    public string playerGuid;
    public PlayerFlag playerFlag;
    public bool isAiPlayer;
    public int consecutivePassCount;

    public OnDuelPassAccepted(string playerGuid, PlayerFlag playerFlag, bool isAiPlayer, int consecutivePassCount)
    {
        this.playerGuid = playerGuid;
        this.playerFlag = playerFlag;
        this.isAiPlayer = isAiPlayer;
        this.consecutivePassCount = consecutivePassCount;
    }
}

public class OnDuelScoreResult : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnDuelScoreResult>();

    public DuelScoreResult scoreResult;
    public bool requireConfirm;
    public OnDuelScoreResult(DuelScoreResult scoreResult, bool requireConfirm)
    {
        this.scoreResult = scoreResult;
        this.requireConfirm = requireConfirm;
    }
}

public class OnDuelScoreFailed : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnDuelScoreFailed>();

    public bool requireConfirm;
    public OnDuelScoreFailed(bool requireConfirm)
    {
        this.requireConfirm = requireConfirm;
    }
}

public class OnDuelOwnershipResult : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnDuelOwnershipResult>();

    public float blackPoints;
    public float whitePoints;
    public float komi;
    public OnDuelOwnershipResult(float blackPoints, float whitePoints, float komi)
    {
        this.blackPoints = blackPoints;
        this.whitePoints = whitePoints;
        this.komi = komi;
    }
}

public class OnClearDuelOwnership : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnClearDuelOwnership>();
}

public class OnDuelStateChanged : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnDuelStateChanged>();

    public string curStateName;
    public OnDuelStateChanged(string curStateName)
    {
        this.curStateName = curStateName;
    }
}

public class OnAddChessToBoard : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnAddChessToBoard>();

    public RectCoordinates coords;
    public OnAddChessToBoard(RectCoordinates coords)
    {
        this.coords = coords;
    }
}

public class OnAfterAddChessToBoard : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnAfterAddChessToBoard>();

    public PlayerFlag playerFlag;
    public RectCoordinates coords;
    public OnAfterAddChessToBoard(PlayerFlag playerFlag, RectCoordinates coords)
    {
        this.playerFlag = playerFlag;
        this.coords = coords;
    }
}

public class OnDuelMoveRejected : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnDuelMoveRejected>();

    public PlayerFlag playerFlag;
    public RectCoordinates coords;
    public DuelMoveRejectReason rejectReason;
    public OnDuelMoveRejected(PlayerFlag playerFlag, RectCoordinates coords, DuelMoveRejectReason rejectReason)
    {
        this.playerFlag = playerFlag;
        this.coords = coords;
        this.rejectReason = rejectReason;
    }
}
