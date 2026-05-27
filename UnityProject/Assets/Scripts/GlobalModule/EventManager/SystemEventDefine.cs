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

public class OnSubmitDuelResign : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitDuelResign>();
}

public class OnSubmitDuelPass : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitDuelPass>();
}

public class OnSubmitDuelScore : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitDuelScore>();
}

public class OnSubmitLanDuelScoreConfirm : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitLanDuelScoreConfirm>();

    public LanDuelScoreRequestMessage request;
    public bool accepted;

    public OnSubmitLanDuelScoreConfirm(LanDuelScoreRequestMessage request, bool accepted)
    {
        this.request = request;
        this.accepted = accepted;
    }
}

public class OnSubmitLanDuelScoreResultConfirm : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitLanDuelScoreResultConfirm>();

    public LanDuelScoreResultMessage result;
    public bool accepted;

    public OnSubmitLanDuelScoreResultConfirm(LanDuelScoreResultMessage result, bool accepted)
    {
        this.result = result;
        this.accepted = accepted;
    }
}

public class OnSubmitDuelTakeBack : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitDuelTakeBack>();
}

public class OnSubmitLanDuelTakeBackConfirm : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitLanDuelTakeBackConfirm>();

    public LanDuelTakeBackRequestMessage request;
    public bool accepted;

    public OnSubmitLanDuelTakeBackConfirm(LanDuelTakeBackRequestMessage request, bool accepted)
    {
        this.request = request;
        this.accepted = accepted;
    }
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
    public string message;
    public OnDuelScoreFailed(bool requireConfirm, string message = null)
    {
        this.requireConfirm = requireConfirm;
        this.message = message;
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

public class OnSubmitDuelMove : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnSubmitDuelMove>();

    public RectCoordinates coords;
    public OnSubmitDuelMove(RectCoordinates coords)
    {
        this.coords = coords;
    }
}

public class OnApplyLanDuelMove : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelMove>();

    public LanDuelMoveMessage move;
    public OnApplyLanDuelMove(LanDuelMoveMessage move)
    {
        this.move = move;
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

public class OnApplyLanDuelTimeState : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelTimeState>();

    public LanDuelTimeStateMessage timeState;
    public OnApplyLanDuelTimeState(LanDuelTimeStateMessage timeState)
    {
        this.timeState = timeState;
    }
}

public class OnApplyLanDuelTimeout : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelTimeout>();

    public PlayerFlag loserFlag;
    public OnApplyLanDuelTimeout(PlayerFlag loserFlag)
    {
        this.loserFlag = loserFlag;
    }
}

public class OnApplyLanDuelResign : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelResign>();

    public PlayerFlag loserFlag;
    public OnApplyLanDuelResign(PlayerFlag loserFlag)
    {
        this.loserFlag = loserFlag;
    }
}

public class OnApplyLanDuelPass : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelPass>();

    public LanDuelPassMessage pass;
    public OnApplyLanDuelPass(LanDuelPassMessage pass)
    {
        this.pass = pass;
    }
}

public class OnApplyLanDuelScoreRequest : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelScoreRequest>();

    public LanDuelScoreRequestMessage request;
    public OnApplyLanDuelScoreRequest(LanDuelScoreRequestMessage request)
    {
        this.request = request;
    }
}

public class OnLanDuelScoreConfirmRequest : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnLanDuelScoreConfirmRequest>();

    public LanDuelScoreRequestMessage request;
    public OnLanDuelScoreConfirmRequest(LanDuelScoreRequestMessage request)
    {
        this.request = request;
    }
}

public class OnLanDuelScoreResultConfirmRequest : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnLanDuelScoreResultConfirmRequest>();

    public LanDuelScoreResultMessage result;
    public OnLanDuelScoreResultConfirmRequest(LanDuelScoreResultMessage result)
    {
        this.result = result;
    }
}

public class OnLanDuelTakeBackConfirmRequest : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnLanDuelTakeBackConfirmRequest>();

    public LanDuelTakeBackRequestMessage request;
    public OnLanDuelTakeBackConfirmRequest(LanDuelTakeBackRequestMessage request)
    {
        this.request = request;
    }
}

public class OnApplyLanDuelTakeBack : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelTakeBack>();

    public LanDuelTakeBackRequestMessage request;
    public OnApplyLanDuelTakeBack(LanDuelTakeBackRequestMessage request)
    {
        this.request = request;
    }
}

public class OnLanDuelTakeBackRejected : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnLanDuelTakeBackRejected>();
}

public class OnLanRoomPeerLeft : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnLanRoomPeerLeft>();

    public LanRoomRole peerRole;
    public LanRoomLeaveReason reason;

    public OnLanRoomPeerLeft(LanRoomRole peerRole, LanRoomLeaveReason reason)
    {
        this.peerRole = peerRole;
        this.reason = reason;
    }
}

public class OnLanPlayerProfileChanged : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnLanPlayerProfileChanged>();

    public PlayerFlag playerFlag;
    public UserProfileData profile;

    public OnLanPlayerProfileChanged(PlayerFlag playerFlag, UserProfileData profile)
    {
        this.playerFlag = playerFlag;
        this.profile = profile;
    }
}

public class OnApplyLanDuelScoreResult : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelScoreResult>();

    public LanDuelScoreResultMessage result;
    public OnApplyLanDuelScoreResult(LanDuelScoreResultMessage result)
    {
        this.result = result;
    }
}

public class OnApplyLanDuelScoreFailed : SystemEventBase
{
    public override string GetEventType() => GetEventType<OnApplyLanDuelScoreFailed>();

    public LanDuelScoreFailedMessage failure;
    public OnApplyLanDuelScoreFailed()
    {
        failure = default;
    }

    public OnApplyLanDuelScoreFailed(LanDuelScoreFailedMessage failure)
    {
        this.failure = failure;
    }
}
