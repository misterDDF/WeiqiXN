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
        lastStatus = "主机已开始对局。";
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
}
