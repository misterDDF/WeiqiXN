using System;
using System.Collections.Generic;
using System.Reflection;
using XNClient.Logger;

public partial class LanRoomService
{
    private Dictionary<string, Action<LanRoomProtocolMessage>> protocolHandlers;

    private void EnsureProtocolHandlers()
    {
        if (protocolHandlers != null) {
            return;
        }

        RegisterProtocolHandlers();
    }

    private void RegisterProtocolHandlers()
    {
        protocolHandlers = new Dictionary<string, Action<LanRoomProtocolMessage>>();
        foreach (LanRoomProtocol protocol in Enum.GetValues(typeof(LanRoomProtocol))) {
            string wireName = LanRoomProtocolName.ToWireName(protocol);
            string handlerName = LanRoomProtocolName.ToHandlerName(protocol);
            MethodInfo method = GetType().GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) {
                XNLogger.LogWarn(
                    "LAN room protocol handler not found.",
                    ("protocol", wireName),
                    ("handlerName", handlerName));
                continue;
            }

            try {
                protocolHandlers[wireName] = (Action<LanRoomProtocolMessage>)Delegate.CreateDelegate(typeof(Action<LanRoomProtocolMessage>), this, method);
            }
            catch (Exception e) {
                XNLogger.LogError(
                    "Register LAN room protocol handler failed.",
                    ("protocol", wireName),
                    ("handlerName", handlerName),
                    ("error", e.ToString()));
            }
        }
    }

    private void OnReady(LanRoomProtocolMessage message)
    {
        HandleReadyMessage(message);
    }

    private void OnState(LanRoomProtocolMessage message)
    {
        HandleStateMessage(message);
    }

    private void OnStart(LanRoomProtocolMessage message)
    {
        lock (sessionLock) {
            gameStarted = true;
        }
        lastStatus = MessageText.Get("lan_room_host_started_game");
    }

    private void OnStartConfig(LanRoomProtocolMessage message)
    {
        HandleStartConfigMessage(message);
    }

    private void OnSubmitMove(LanRoomProtocolMessage message)
    {
        HandleMoveMessage(message, true);
    }

    private void OnMoveAccepted(LanRoomProtocolMessage message)
    {
        HandleMoveMessage(message, false);
    }

    private void OnMoveRejected(LanRoomProtocolMessage message)
    {
        HandleMoveRejectedMessage(message);
    }

    private void OnBoardSnapshot(LanRoomProtocolMessage message)
    {
        HandleBoardSnapshotMessage(message);
    }

    private void OnTimeState(LanRoomProtocolMessage message)
    {
        HandleTimeStateMessage(message);
    }

    private void OnPlayerTimeout(LanRoomProtocolMessage message)
    {
        HandlePlayerTimeoutMessage(message);
    }

    private void OnSubmitResign(LanRoomProtocolMessage message)
    {
        HandleResignMessage(message, true);
    }

    private void OnResignAccepted(LanRoomProtocolMessage message)
    {
        HandleResignMessage(message, false);
    }

    private void OnInputAuthority(LanRoomProtocolMessage message)
    {
        HandleInputAuthorityMessage(message);
    }

    private void OnSubmitPass(LanRoomProtocolMessage message)
    {
        HandlePassMessage(message, true);
    }

    private void OnPassAccepted(LanRoomProtocolMessage message)
    {
        HandlePassMessage(message, false);
    }

    private void OnSubmitScore(LanRoomProtocolMessage message)
    {
        HandleScoreRequestMessage(message, true);
    }

    private void OnScoreConfirmRequest(LanRoomProtocolMessage message)
    {
        HandleScoreConfirmRequestMessage(message);
    }

    private void OnScoreConfirmResponse(LanRoomProtocolMessage message)
    {
        HandleScoreConfirmResponseMessage(message);
    }

    private void OnScoreRequestAccepted(LanRoomProtocolMessage message)
    {
        HandleScoreRequestMessage(message, false);
    }

    private void OnScoreResult(LanRoomProtocolMessage message)
    {
        HandleScoreResultMessage(message);
    }

    private void OnScoreResultConfirmResponse(LanRoomProtocolMessage message)
    {
        HandleScoreResultConfirmResponseMessage(message);
    }

    private void OnScoreResultAccepted(LanRoomProtocolMessage message)
    {
        HandleAcceptedScoreResultMessage(message);
    }

    private void OnScoreFailed(LanRoomProtocolMessage message)
    {
        HandleScoreFailedMessage(message);
    }

    private void OnSubmitTakeBack(LanRoomProtocolMessage message)
    {
        HandleTakeBackRequestMessage(message, true);
    }

    private void OnTakeBackConfirmRequest(LanRoomProtocolMessage message)
    {
        HandleTakeBackRequestMessage(message, false);
    }

    private void OnTakeBackConfirmResponse(LanRoomProtocolMessage message)
    {
        HandleTakeBackConfirmResponseMessage(message);
    }

    private void OnTakeBackAccepted(LanRoomProtocolMessage message)
    {
        HandleTakeBackAcceptedMessage(message);
    }

    private void OnTakeBackRejected(LanRoomProtocolMessage message)
    {
        HandleTakeBackRejectedMessage(message);
    }
}
