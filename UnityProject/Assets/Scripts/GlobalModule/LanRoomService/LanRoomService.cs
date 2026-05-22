using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public partial class LanRoomService : ModuleBase
{
    private readonly object roomLock = new object();
    private readonly Dictionary<string, LanRoomInfo> discoveredRooms = new Dictionary<string, LanRoomInfo>();

    private string hostedRoomId;
    private string hostedRoomName;
    private int hostedTcpPort;
    private TcpListener hostListener;
    private UdpClient broadcastClient;
    private Thread broadcastThread;
    private Thread acceptThread;
    private volatile bool isHosting;

    private UdpClient discoveryClient;
    private Thread discoveryThread;
    private volatile bool isSearching;

    private readonly object sessionLock = new object();
    private readonly Queue<LanDuelMoveMessage> pendingSubmittedMoves = new Queue<LanDuelMoveMessage>();
    private readonly Queue<LanDuelMoveMessage> pendingAcceptedMoves = new Queue<LanDuelMoveMessage>();
    private readonly Queue<LanDuelMoveRejectMessage> pendingRejectedMoves = new Queue<LanDuelMoveRejectMessage>();
    private readonly Queue<LanDuelBoardSnapshotMessage> pendingBoardSnapshots = new Queue<LanDuelBoardSnapshotMessage>();
    private readonly Queue<LanDuelTimeStateMessage> pendingTimeStates = new Queue<LanDuelTimeStateMessage>();
    private readonly Queue<PlayerFlag> pendingTimeoutLosers = new Queue<PlayerFlag>();
    private readonly Queue<LanDuelResignMessage> pendingSubmittedResigns = new Queue<LanDuelResignMessage>();
    private readonly Queue<LanDuelResignMessage> pendingAcceptedResigns = new Queue<LanDuelResignMessage>();
    private readonly Queue<LanDuelInputAuthorityMessage> pendingInputAuthorities = new Queue<LanDuelInputAuthorityMessage>();
    private readonly Queue<LanDuelPassMessage> pendingSubmittedPasses = new Queue<LanDuelPassMessage>();
    private readonly Queue<LanDuelPassMessage> pendingAcceptedPasses = new Queue<LanDuelPassMessage>();
    private readonly Queue<LanDuelScoreRequestMessage> pendingSubmittedScores = new Queue<LanDuelScoreRequestMessage>();
    private readonly Queue<LanDuelScoreRequestMessage> pendingScoreConfirmRequests = new Queue<LanDuelScoreRequestMessage>();
    private readonly Queue<LanDuelScoreConfirmMessage> pendingScoreConfirmResponses = new Queue<LanDuelScoreConfirmMessage>();
    private readonly Queue<LanDuelScoreRequestMessage> pendingAcceptedScoreRequests = new Queue<LanDuelScoreRequestMessage>();
    private readonly Queue<LanDuelScoreResultMessage> pendingScoreResults = new Queue<LanDuelScoreResultMessage>();
    private readonly Queue<LanDuelScoreResultConfirmMessage> pendingScoreResultConfirmResponses = new Queue<LanDuelScoreResultConfirmMessage>();
    private readonly Queue<LanDuelScoreResultMessage> pendingAcceptedScoreResults = new Queue<LanDuelScoreResultMessage>();
    private readonly Queue<LanDuelScoreFailedMessage> pendingScoreFailures = new Queue<LanDuelScoreFailedMessage>();
    private readonly Queue<LanDuelTakeBackRequestMessage> pendingSubmittedTakeBacks = new Queue<LanDuelTakeBackRequestMessage>();
    private readonly Queue<LanDuelTakeBackRequestMessage> pendingTakeBackConfirmRequests = new Queue<LanDuelTakeBackRequestMessage>();
    private readonly Queue<LanDuelTakeBackConfirmMessage> pendingTakeBackConfirmResponses = new Queue<LanDuelTakeBackConfirmMessage>();
    private readonly Queue<LanDuelTakeBackRequestMessage> pendingAcceptedTakeBacks = new Queue<LanDuelTakeBackRequestMessage>();
    private readonly Queue<LanDuelTakeBackRejectedMessage> pendingRejectedTakeBacks = new Queue<LanDuelTakeBackRejectedMessage>();
    private TcpClient connectedClient;
    private Thread sessionReadThread;
    private LanRoomRole currentRole = LanRoomRole.None;
    private bool hostReady;
    private bool clientReady;
    private bool gameStarted;
    private int nextMoveId = 1;
    private string lanBoardCfgId = "9x9";
    private string lanHoldTimeCfgId = "infinite";
    private string lanByoyomiCountCfgId = "off";
    private string lanByoyomiTimeCfgId = "30s";
    private string lanHandicapCfgId = "9x9_0";
    private PlayerFlag lanHostPlayerFlag = PlayerFlag.Player1;
    private string lastStatus;

    public bool IsHosting => isHosting;
    public bool IsSearching => isSearching;
    public string LastStatus => string.IsNullOrEmpty(lastStatus) ? MessageText.Get("lan_room_service_stopped") : lastStatus;
    public string LanBoardCfgId => lanBoardCfgId;
    public string LanHoldTimeCfgId => lanHoldTimeCfgId;
    public string LanByoyomiCountCfgId => lanByoyomiCountCfgId;
    public string LanByoyomiTimeCfgId => lanByoyomiTimeCfgId;
    public string LanHandicapCfgId => lanHandicapCfgId;
    public PlayerFlag LanHostPlayerFlag => lanHostPlayerFlag;
    public LanRoomSessionState SessionState
    {
        get
        {
            lock (sessionLock) {
                return new LanRoomSessionState(currentRole, connectedClient != null, hostReady, clientReady, gameStarted);
            }
        }
    }

    public override void Init()
    {
    }

    public void SetStartConfig(
        string boardCfgId,
        string holdTimeCfgId,
        string byoyomiCountCfgId,
        string byoyomiTimeCfgId,
        string handicapCfgId,
        PlayerFlag hostPlayerFlag)
    {
        lanBoardCfgId = string.IsNullOrEmpty(boardCfgId) ? "9x9" : boardCfgId;
        lanHoldTimeCfgId = string.IsNullOrEmpty(holdTimeCfgId) ? "infinite" : holdTimeCfgId;
        lanByoyomiCountCfgId = string.IsNullOrEmpty(byoyomiCountCfgId) ? "off" : byoyomiCountCfgId;
        lanByoyomiTimeCfgId = string.IsNullOrEmpty(byoyomiTimeCfgId) ? "30s" : byoyomiTimeCfgId;
        lanHandicapCfgId = DuelHandicapPlacement.GetValidCfgId(handicapCfgId, lanBoardCfgId);
        lanHostPlayerFlag = DuelUtils.GetValidPlayerFlag(hostPlayerFlag);
    }

    public bool CreateRoom(string roomName)
    {
        StopRoom();
        hostedRoomId = Guid.NewGuid().ToString("N");
        hostedRoomName = string.IsNullOrEmpty(roomName) ? MessageText.Get("lan_room_default_name") : roomName;
        hostedTcpPort = LanRoomConfig.TcpListenPort;
        lock (sessionLock) {
            currentRole = LanRoomRole.Host;
            hostReady = false;
            clientReady = false;
            gameStarted = false;
            nextMoveId = 1;
            pendingSubmittedMoves.Clear();
            pendingAcceptedMoves.Clear();
            pendingRejectedMoves.Clear();
            pendingBoardSnapshots.Clear();
            pendingTimeStates.Clear();
            pendingTimeoutLosers.Clear();
            pendingSubmittedResigns.Clear();
            pendingAcceptedResigns.Clear();
            pendingInputAuthorities.Clear();
            pendingSubmittedPasses.Clear();
            pendingAcceptedPasses.Clear();
            pendingSubmittedScores.Clear();
            pendingScoreConfirmRequests.Clear();
            pendingScoreConfirmResponses.Clear();
            pendingAcceptedScoreRequests.Clear();
            ClearScoreResultQueues();
            ClearTakeBackQueues();
        }

        try {
            hostListener = new TcpListener(IPAddress.Any, hostedTcpPort);
            hostListener.Start();
            isHosting = true;

            acceptThread = new Thread(AcceptClientLoop)
            {
                IsBackground = true,
                Name = "LanRoomAccept"
            };
            acceptThread.Start();

            broadcastClient = new UdpClient();
            broadcastClient.EnableBroadcast = true;
            broadcastThread = new Thread(BroadcastRoomLoop)
            {
                IsBackground = true,
                Name = "LanRoomBroadcast"
            };
            broadcastThread.Start();

            lastStatus = MessageText.Format("lan_room_created_waiting", hostedRoomName);
            XNLogger.LogInfo("LAN room created.", ("roomId", hostedRoomId), ("tcpPort", hostedTcpPort.ToString()));
            return true;
        }
        catch (Exception e) {
            lastStatus = MessageText.Format("lan_room_create_failed", e.Message);
            XNLogger.LogError("Create LAN room failed.", ("error", e.ToString()));
            StopRoom();
            return false;
        }
    }

    public void StopRoom()
    {
        isHosting = false;
        CloseClient(connectedClient);
        connectedClient = null;
        lock (sessionLock) {
            currentRole = LanRoomRole.None;
            hostReady = false;
            clientReady = false;
            gameStarted = false;
            nextMoveId = 1;
            pendingSubmittedMoves.Clear();
            pendingAcceptedMoves.Clear();
            pendingRejectedMoves.Clear();
            pendingBoardSnapshots.Clear();
            pendingTimeStates.Clear();
            pendingTimeoutLosers.Clear();
            pendingSubmittedResigns.Clear();
            pendingAcceptedResigns.Clear();
            pendingInputAuthorities.Clear();
            pendingSubmittedPasses.Clear();
            pendingAcceptedPasses.Clear();
            pendingSubmittedScores.Clear();
            pendingScoreConfirmRequests.Clear();
            pendingScoreConfirmResponses.Clear();
            pendingAcceptedScoreRequests.Clear();
            ClearScoreResultQueues();
            ClearTakeBackQueues();
        }

        try {
            hostListener?.Stop();
        }
        catch (Exception e) {
            XNLogger.LogWarn("Stop LAN host listener failed.", ("error", e.Message));
        }
        hostListener = null;

        CloseUdpClient(broadcastClient);
        broadcastClient = null;
    }

    public void StartSearchRooms()
    {
        StopSearchRooms();
        lock (roomLock) {
            discoveredRooms.Clear();
        }

        try {
            discoveryClient = new UdpClient(AddressFamily.InterNetwork);
            discoveryClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            discoveryClient.Client.Bind(new IPEndPoint(IPAddress.Any, LanRoomConfig.UdpBroadcastPort));
            isSearching = true;
            discoveryThread = new Thread(ReceiveDiscoveryLoop)
            {
                IsBackground = true,
                Name = "LanRoomDiscovery"
            };
            discoveryThread.Start();

            lastStatus = MessageText.Get("lan_room_searching");
        }
        catch (Exception e) {
            lastStatus = MessageText.Format("lan_room_search_failed", e.Message);
            XNLogger.LogError("Start LAN room search failed.", ("error", e.ToString()));
            StopSearchRooms();
        }
    }

    public void StopSearchRooms()
    {
        isSearching = false;
        CloseUdpClient(discoveryClient);
        discoveryClient = null;
    }

    public List<LanRoomInfo> GetDiscoveredRooms()
    {
        lock (roomLock) {
            return new List<LanRoomInfo>(discoveredRooms.Values);
        }
    }

    public bool ConnectToRoom(LanRoomInfo room)
    {
        CloseClient(connectedClient);
        connectedClient = null;

        try {
            TcpClient client = new TcpClient();
            if (!ConnectWithTimeout(client, room.hostAddress, room.tcpPort)) {
                client.Close();
                lastStatus = MessageText.Get("lan_room_connect_timeout");
                return false;
            }

            client.SendTimeout = LanRoomConfig.ConnectTimeoutMilliseconds;
            client.ReceiveTimeout = LanRoomConfig.ConnectTimeoutMilliseconds;
            byte[] joinBytes = Encoding.UTF8.GetBytes($"{LanRoomProtocolName.ClientHello}|{room.roomId}\n");
            NetworkStream stream = client.GetStream();
            stream.Write(joinBytes, 0, joinBytes.Length);

            byte[] buffer = new byte[LanRoomConfig.HandshakeBufferSize];
            int readLength = stream.Read(buffer, 0, buffer.Length);
            string response = Encoding.UTF8.GetString(buffer, 0, readLength).Trim();
            if (!response.StartsWith(LanRoomProtocolName.HostAccept, StringComparison.Ordinal)) {
                client.Close();
                lastStatus = MessageText.Get("lan_room_connect_rejected");
                return false;
            }

            client.ReceiveTimeout = 0;
            connectedClient = client;
            lock (sessionLock) {
                currentRole = LanRoomRole.Client;
                hostReady = false;
                clientReady = false;
                gameStarted = false;
                nextMoveId = 1;
                pendingSubmittedMoves.Clear();
                pendingAcceptedMoves.Clear();
                pendingRejectedMoves.Clear();
                pendingBoardSnapshots.Clear();
                pendingTimeStates.Clear();
                pendingTimeoutLosers.Clear();
                pendingSubmittedResigns.Clear();
                pendingAcceptedResigns.Clear();
                pendingInputAuthorities.Clear();
                pendingSubmittedPasses.Clear();
                pendingAcceptedPasses.Clear();
                pendingSubmittedScores.Clear();
                pendingScoreConfirmRequests.Clear();
                pendingScoreConfirmResponses.Clear();
                pendingAcceptedScoreRequests.Clear();
                ClearScoreResultQueues();
                ClearTakeBackQueues();
            }
            StartSessionReader(client);
            SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.Ready)}|CLIENT|0");
            lastStatus = MessageText.Format("lan_room_connected", room.name);
            XNLogger.LogInfo("LAN room connected.", ("roomId", room.roomId), ("host", room.hostAddress));
            return true;
        }
        catch (Exception e) {
            lastStatus = MessageText.Format("lan_room_connect_failed", e.Message);
            XNLogger.LogError("Connect LAN room failed.", ("error", e.ToString()));
            return false;
        }
    }

    public void SetLocalReady(bool ready)
    {
        LanRoomRole role;
        lock (sessionLock) {
            role = currentRole;
            if (role == LanRoomRole.Host) {
                hostReady = ready;
            } else if (role == LanRoomRole.Client) {
                clientReady = ready;
            } else {
                lastStatus = MessageText.Get("lan_room_ready_not_joined");
                return;
            }
        }

        if (role == LanRoomRole.Host) {
            BroadcastRoomState();
        } else {
            SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.Ready)}|CLIENT|{BoolToInt(ready)}");
        }

        lastStatus = ready ? MessageText.Get("lan_room_ready") : MessageText.Get("lan_room_ready_cancelled");
    }

    public bool TryStartGame()
    {
        bool canStart;
        lock (sessionLock) {
            canStart = currentRole == LanRoomRole.Host && connectedClient != null && hostReady && clientReady && !gameStarted;
            if (canStart) {
                gameStarted = true;
            }
        }

        if (!canStart) {
            lastStatus = MessageText.Get("lan_room_start_requires_ready");
            return false;
        }

        SendRoomMessage(SerializeStartConfigMessage());
        BroadcastRoomState();
        lastStatus = MessageText.Get("lan_room_start_command_sent");
        return true;
    }

    public bool SubmitLocalMove(PlayerFlag playerFlag, RectCoordinates coords, int boardVersion)
    {
        if (coords == null) {
            return false;
        }

        LanRoomRole role;
        int moveId;
        lock (sessionLock) {
            role = currentRole;
            moveId = nextMoveId++;
        }

        LanDuelMoveMessage move = new LanDuelMoveMessage(moveId, boardVersion, playerFlag, coords.Clone());
        if (role == LanRoomRole.Host) {
            EnqueueSubmittedMove(move);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_submit_move_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.SubmitMove)}|{move.moveId}|{move.boardVersion}|{(int)move.playerFlag}|{move.coords.x}|{move.coords.z}");
        return true;
    }

    public bool SubmitLocalResign(PlayerFlag loserFlag)
    {
        LanRoomRole role;
        int actionId;
        lock (sessionLock) {
            role = currentRole;
            actionId = nextMoveId++;
        }

        LanDuelResignMessage resign = new LanDuelResignMessage(actionId, loserFlag);
        if (role == LanRoomRole.Host) {
            EnqueueSubmittedResign(resign);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_submit_resign_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.SubmitResign)}|{resign.actionId}|{(int)resign.loserFlag}");
        return true;
    }

    public bool SubmitLocalPass(PlayerFlag playerFlag, int boardVersion)
    {
        LanRoomRole role;
        int actionId;
        lock (sessionLock) {
            role = currentRole;
            actionId = nextMoveId++;
        }

        LanDuelPassMessage pass = new LanDuelPassMessage(actionId, boardVersion, playerFlag);
        if (role == LanRoomRole.Host) {
            EnqueueSubmittedPass(pass);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_submit_pass_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.SubmitPass)}|{pass.actionId}|{pass.boardVersion}|{(int)pass.playerFlag}");
        return true;
    }

    public bool SubmitLocalScore(PlayerFlag requesterFlag, int boardVersion)
    {
        LanRoomRole role;
        int actionId;
        lock (sessionLock) {
            role = currentRole;
            actionId = nextMoveId++;
        }

        LanDuelScoreRequestMessage request = new LanDuelScoreRequestMessage(actionId, boardVersion, requesterFlag);
        if (role == LanRoomRole.Host) {
            EnqueueSubmittedScore(request);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_submit_score_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.SubmitScore)}|{request.actionId}|{request.boardVersion}|{(int)request.requesterFlag}");
        return true;
    }

    public bool SubmitScoreConfirmResponse(LanDuelScoreRequestMessage request, PlayerFlag confirmerFlag, bool accepted)
    {
        LanRoomRole role;
        lock (sessionLock) {
            role = currentRole;
        }

        LanDuelScoreConfirmMessage response = new LanDuelScoreConfirmMessage(request.actionId, request.requesterFlag, confirmerFlag, accepted);
        if (role == LanRoomRole.Host) {
            EnqueueScoreConfirmResponse(response);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_score_confirm_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ScoreConfirmResponse)}|{response.actionId}|{(int)response.requesterFlag}|{(int)response.confirmerFlag}|{BoolToInt(response.accepted)}");
        return true;
    }

    public bool SubmitScoreResultConfirmResponse(LanDuelScoreResultMessage result, PlayerFlag confirmerFlag, bool accepted)
    {
        LanRoomRole role;
        lock (sessionLock) {
            role = currentRole;
        }

        LanDuelScoreResultConfirmMessage response = new LanDuelScoreResultConfirmMessage(result.actionId, result.requesterFlag, confirmerFlag, accepted);
        if (role == LanRoomRole.Host) {
            EnqueueScoreResultConfirmResponse(response);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_score_result_confirm_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ScoreResultConfirmResponse)}|{response.actionId}|{(int)response.requesterFlag}|{(int)response.confirmerFlag}|{BoolToInt(response.accepted)}");
        return true;
    }

    public bool SubmitLocalTakeBack(PlayerFlag requesterFlag, int boardVersion, int removeCount)
    {
        LanRoomRole role;
        int actionId;
        lock (sessionLock) {
            role = currentRole;
            actionId = nextMoveId++;
        }

        LanDuelTakeBackRequestMessage request = new LanDuelTakeBackRequestMessage(actionId, boardVersion, requesterFlag, removeCount);
        if (role == LanRoomRole.Host) {
            EnqueueSubmittedTakeBack(request);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_take_back_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.SubmitTakeBack)}|{request.actionId}|{request.boardVersion}|{(int)request.requesterFlag}|{request.removeCount}");
        return true;
    }

    public bool SubmitTakeBackConfirmResponse(LanDuelTakeBackRequestMessage request, PlayerFlag confirmerFlag, bool accepted)
    {
        LanRoomRole role;
        lock (sessionLock) {
            role = currentRole;
        }

        LanDuelTakeBackConfirmMessage response = new LanDuelTakeBackConfirmMessage(request.actionId, request.requesterFlag, confirmerFlag, accepted);
        if (role == LanRoomRole.Host) {
            EnqueueTakeBackConfirmResponse(response);
            return true;
        }

        if (role != LanRoomRole.Client || connectedClient == null) {
            lastStatus = MessageText.Get("lan_room_take_back_confirm_not_in_duel");
            return false;
        }

        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.TakeBackConfirmResponse)}|{response.actionId}|{(int)response.requesterFlag}|{(int)response.confirmerFlag}|{BoolToInt(response.accepted)}");
        return true;
    }

    public void BroadcastAcceptedMove(LanDuelMoveMessage move)
    {
        EnqueueAcceptedMove(move);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.MoveAccepted)}|{move.moveId}|{move.boardVersion}|{(int)move.playerFlag}|{move.coords.x}|{move.coords.z}");
    }

    public void BroadcastRejectedMove(LanDuelMoveRejectMessage move)
    {
        EnqueueRejectedMove(move);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.MoveRejected)}|{move.moveId}|{(int)move.playerFlag}|{move.coords.x}|{move.coords.z}|{(int)move.rejectReason}");
    }

    public void BroadcastBoardSnapshot(LanDuelBoardSnapshotMessage snapshot)
    {
        EnqueueBoardSnapshot(snapshot);
        SendRoomMessage(SerializeBoardSnapshot(snapshot));
    }

    public void BroadcastTimeState(LanDuelTimeStateMessage timeState)
    {
        EnqueueTimeState(timeState);
        SendRoomMessage(SerializeTimeStateMessage(timeState));
    }

    public void BroadcastPlayerTimeout(PlayerFlag loserFlag)
    {
        EnqueueTimeoutLoser(loserFlag);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.PlayerTimeout)}|{(int)loserFlag}");
    }

    public void BroadcastAcceptedResign(LanDuelResignMessage resign)
    {
        EnqueueAcceptedResign(resign);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ResignAccepted)}|{resign.actionId}|{(int)resign.loserFlag}");
    }

    public void BroadcastInputAuthority(PlayerFlag hostInputPlayerFlag, PlayerFlag clientInputPlayerFlag)
    {
        EnqueueInputAuthority(new LanDuelInputAuthorityMessage(hostInputPlayerFlag, clientInputPlayerFlag));
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.InputAuthority)}|{(int)hostInputPlayerFlag}|{(int)clientInputPlayerFlag}");
    }

    public void BroadcastAcceptedPass(LanDuelPassMessage pass)
    {
        EnqueueAcceptedPass(pass);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.PassAccepted)}|{pass.actionId}|{pass.boardVersion}|{(int)pass.playerFlag}|{pass.consecutivePassCount}");
    }

    public void BroadcastAcceptedScoreRequest(LanDuelScoreRequestMessage request)
    {
        EnqueueAcceptedScoreRequest(request);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ScoreRequestAccepted)}|{request.actionId}|{request.boardVersion}|{(int)request.requesterFlag}");
    }

    public void BroadcastScoreConfirmRequest(LanDuelScoreRequestMessage request)
    {
        if (request.requesterFlag == PlayerFlag.Player1) {
            SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ScoreConfirmRequest)}|{request.actionId}|{request.boardVersion}|{(int)request.requesterFlag}");
            return;
        }

        EnqueueScoreConfirmRequest(request);
    }

    public void BroadcastScoreResult(LanDuelScoreResultMessage result)
    {
        EnqueueScoreResult(result);
        string scoreSource = EncodeText(result.scoreSource);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ScoreResult)}|{result.actionId}|{(int)result.requesterFlag}|{result.blackScore}|{result.whiteScore}|{result.komi}|{result.margin}|{(int)result.winnerFlag}|{scoreSource}");
    }

    public void BroadcastAcceptedScoreResult(LanDuelScoreResultMessage result)
    {
        EnqueueAcceptedScoreResult(result);
        string scoreSource = EncodeText(result.scoreSource);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ScoreResultAccepted)}|{result.actionId}|{(int)result.requesterFlag}|{result.blackScore}|{result.whiteScore}|{result.komi}|{result.margin}|{(int)result.winnerFlag}|{scoreSource}");
    }

    public void BroadcastScoreFailed(
        int actionId,
        PlayerFlag requesterFlag = 0,
        LanDuelScoreFailureReason reason = LanDuelScoreFailureReason.CalculationFailed)
    {
        EnqueueScoreFailure(new LanDuelScoreFailedMessage(actionId, requesterFlag, reason));
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.ScoreFailed)}|{actionId}|{(int)requesterFlag}|{(int)reason}");
    }

    public void BroadcastTakeBackConfirmRequest(LanDuelTakeBackRequestMessage request)
    {
        if (request.requesterFlag == PlayerFlag.Player1) {
            SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.TakeBackConfirmRequest)}|{request.actionId}|{request.boardVersion}|{(int)request.requesterFlag}|{request.removeCount}");
            return;
        }

        EnqueueTakeBackConfirmRequest(request);
    }

    public void BroadcastAcceptedTakeBack(LanDuelTakeBackRequestMessage request)
    {
        EnqueueAcceptedTakeBack(request);
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.TakeBackAccepted)}|{request.actionId}|{request.boardVersion}|{(int)request.requesterFlag}|{request.removeCount}");
    }

    public void BroadcastRejectedTakeBack(int actionId, PlayerFlag requesterFlag)
    {
        EnqueueRejectedTakeBack(new LanDuelTakeBackRejectedMessage(actionId, requesterFlag));
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.TakeBackRejected)}|{actionId}|{(int)requesterFlag}");
    }

    public bool TryDequeueSubmittedMove(out LanDuelMoveMessage move)
    {
        lock (sessionLock) {
            if (pendingSubmittedMoves.Count > 0) {
                move = pendingSubmittedMoves.Dequeue();
                return true;
            }
        }

        move = default;
        return false;
    }

    public bool TryDequeueAcceptedMove(out LanDuelMoveMessage move)
    {
        lock (sessionLock) {
            if (pendingAcceptedMoves.Count > 0) {
                move = pendingAcceptedMoves.Dequeue();
                return true;
            }
        }

        move = default;
        return false;
    }

    public bool TryDequeueRejectedMove(out LanDuelMoveRejectMessage move)
    {
        lock (sessionLock) {
            if (pendingRejectedMoves.Count > 0) {
                move = pendingRejectedMoves.Dequeue();
                return true;
            }
        }

        move = default;
        return false;
    }

    public bool TryDequeueBoardSnapshot(out LanDuelBoardSnapshotMessage snapshot)
    {
        lock (sessionLock) {
            if (pendingBoardSnapshots.Count > 0) {
                snapshot = pendingBoardSnapshots.Dequeue();
                return true;
            }
        }

        snapshot = default;
        return false;
    }

    public bool TryDequeueTimeState(out LanDuelTimeStateMessage timeState)
    {
        lock (sessionLock) {
            if (pendingTimeStates.Count > 0) {
                timeState = pendingTimeStates.Dequeue();
                return true;
            }
        }

        timeState = default;
        return false;
    }

    public bool TryDequeueTimeoutLoser(out PlayerFlag loserFlag)
    {
        lock (sessionLock) {
            if (pendingTimeoutLosers.Count > 0) {
                loserFlag = pendingTimeoutLosers.Dequeue();
                return true;
            }
        }

        loserFlag = 0;
        return false;
    }

    public bool TryDequeueSubmittedResign(out LanDuelResignMessage resign)
    {
        lock (sessionLock) {
            if (pendingSubmittedResigns.Count > 0) {
                resign = pendingSubmittedResigns.Dequeue();
                return true;
            }
        }

        resign = default;
        return false;
    }

    public bool TryDequeueAcceptedResign(out LanDuelResignMessage resign)
    {
        lock (sessionLock) {
            if (pendingAcceptedResigns.Count > 0) {
                resign = pendingAcceptedResigns.Dequeue();
                return true;
            }
        }

        resign = default;
        return false;
    }

    public bool TryDequeueInputAuthority(out LanDuelInputAuthorityMessage authority)
    {
        lock (sessionLock) {
            if (pendingInputAuthorities.Count > 0) {
                authority = pendingInputAuthorities.Dequeue();
                return true;
            }
        }

        authority = default;
        return false;
    }

    public bool TryDequeueSubmittedPass(out LanDuelPassMessage pass)
    {
        lock (sessionLock) {
            if (pendingSubmittedPasses.Count > 0) {
                pass = pendingSubmittedPasses.Dequeue();
                return true;
            }
        }

        pass = default;
        return false;
    }

    public bool TryDequeueAcceptedPass(out LanDuelPassMessage pass)
    {
        lock (sessionLock) {
            if (pendingAcceptedPasses.Count > 0) {
                pass = pendingAcceptedPasses.Dequeue();
                return true;
            }
        }

        pass = default;
        return false;
    }

    public bool TryDequeueSubmittedScore(out LanDuelScoreRequestMessage request)
    {
        lock (sessionLock) {
            if (pendingSubmittedScores.Count > 0) {
                request = pendingSubmittedScores.Dequeue();
                return true;
            }
        }

        request = default;
        return false;
    }

    public bool TryDequeueScoreConfirmRequest(out LanDuelScoreRequestMessage request)
    {
        lock (sessionLock) {
            if (pendingScoreConfirmRequests.Count > 0) {
                request = pendingScoreConfirmRequests.Dequeue();
                return true;
            }
        }

        request = default;
        return false;
    }

    public bool TryDequeueScoreConfirmResponse(out LanDuelScoreConfirmMessage response)
    {
        lock (sessionLock) {
            if (pendingScoreConfirmResponses.Count > 0) {
                response = pendingScoreConfirmResponses.Dequeue();
                return true;
            }
        }

        response = default;
        return false;
    }

    public bool TryDequeueAcceptedScoreRequest(out LanDuelScoreRequestMessage request)
    {
        lock (sessionLock) {
            if (pendingAcceptedScoreRequests.Count > 0) {
                request = pendingAcceptedScoreRequests.Dequeue();
                return true;
            }
        }

        request = default;
        return false;
    }

    public bool TryDequeueScoreResult(out LanDuelScoreResultMessage result)
    {
        lock (sessionLock) {
            if (pendingScoreResults.Count > 0) {
                result = pendingScoreResults.Dequeue();
                return true;
            }
        }

        result = default;
        return false;
    }

    public bool TryDequeueScoreResultConfirmResponse(out LanDuelScoreResultConfirmMessage response)
    {
        lock (sessionLock) {
            if (pendingScoreResultConfirmResponses.Count > 0) {
                response = pendingScoreResultConfirmResponses.Dequeue();
                return true;
            }
        }

        response = default;
        return false;
    }

    public bool TryDequeueAcceptedScoreResult(out LanDuelScoreResultMessage result)
    {
        lock (sessionLock) {
            if (pendingAcceptedScoreResults.Count > 0) {
                result = pendingAcceptedScoreResults.Dequeue();
                return true;
            }
        }

        result = default;
        return false;
    }

    public bool TryDequeueScoreFailure(out LanDuelScoreFailedMessage failure)
    {
        lock (sessionLock) {
            if (pendingScoreFailures.Count > 0) {
                failure = pendingScoreFailures.Dequeue();
                return true;
            }
        }

        failure = default;
        return false;
    }

    public bool TryDequeueSubmittedTakeBack(out LanDuelTakeBackRequestMessage request)
    {
        lock (sessionLock) {
            if (pendingSubmittedTakeBacks.Count > 0) {
                request = pendingSubmittedTakeBacks.Dequeue();
                return true;
            }
        }

        request = default;
        return false;
    }

    public bool TryDequeueTakeBackConfirmRequest(out LanDuelTakeBackRequestMessage request)
    {
        lock (sessionLock) {
            if (pendingTakeBackConfirmRequests.Count > 0) {
                request = pendingTakeBackConfirmRequests.Dequeue();
                return true;
            }
        }

        request = default;
        return false;
    }

    public bool TryDequeueTakeBackConfirmResponse(out LanDuelTakeBackConfirmMessage response)
    {
        lock (sessionLock) {
            if (pendingTakeBackConfirmResponses.Count > 0) {
                response = pendingTakeBackConfirmResponses.Dequeue();
                return true;
            }
        }

        response = default;
        return false;
    }

    public bool TryDequeueAcceptedTakeBack(out LanDuelTakeBackRequestMessage request)
    {
        lock (sessionLock) {
            if (pendingAcceptedTakeBacks.Count > 0) {
                request = pendingAcceptedTakeBacks.Dequeue();
                return true;
            }
        }

        request = default;
        return false;
    }

    public bool TryDequeueRejectedTakeBack(out LanDuelTakeBackRejectedMessage rejected)
    {
        lock (sessionLock) {
            if (pendingRejectedTakeBacks.Count > 0) {
                rejected = pendingRejectedTakeBacks.Dequeue();
                return true;
            }
        }

        rejected = default;
        return false;
    }

    public override void OnDestroy()
    {
        StopSearchRooms();
        StopRoom();
        base.OnDestroy();
    }

    private void BroadcastRoomLoop()
    {
        IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, LanRoomConfig.UdpBroadcastPort);
        while (isHosting) {
            try {
                string localAddress = GetLocalAddress();
                string payload = $"{LanRoomProtocolName.DiscoveryPrefix}|{hostedRoomId}|{hostedRoomName}|{localAddress}|{hostedTcpPort}|{GetHostedPlayerCount()}|{LanRoomConfig.MaxPlayerCount}";
                byte[] data = Encoding.UTF8.GetBytes(payload);
                broadcastClient?.Send(data, data.Length, broadcastEndPoint);
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (Exception e) {
                XNLogger.LogWarn("Broadcast LAN room failed.", ("error", e.Message));
            }

            Thread.Sleep(LanRoomConfig.BroadcastIntervalMilliseconds);
        }
    }

    private void ReceiveDiscoveryLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (isSearching) {
            try {
                byte[] data = discoveryClient.Receive(ref remoteEndPoint);
                string payload = Encoding.UTF8.GetString(data);
                if (TryParseRoom(payload, remoteEndPoint.Address.ToString(), out LanRoomInfo room)) {
                    lock (roomLock) {
                        discoveredRooms[room.roomId] = room;
                    }
                }
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (SocketException e) {
                if (isSearching) {
                    XNLogger.LogWarn("Receive LAN room discovery failed.", ("error", e.Message));
                }
            }
            catch (Exception e) {
                XNLogger.LogWarn("Receive LAN room discovery failed.", ("error", e.Message));
            }
        }
    }

    private void AcceptClientLoop()
    {
        while (isHosting) {
            try {
                TcpClient client = hostListener.AcceptTcpClient();
                if (connectedClient != null) {
                    SendAndClose(client, $"{LanRoomProtocolName.HostFull}\n");
                    continue;
                }

                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[LanRoomConfig.HandshakeBufferSize];
                int readLength = stream.Read(buffer, 0, buffer.Length);
                string request = Encoding.UTF8.GetString(buffer, 0, readLength).Trim();
                if (!request.StartsWith($"{LanRoomProtocolName.ClientHello}|{hostedRoomId}", StringComparison.Ordinal)) {
                    SendAndClose(client, $"{LanRoomProtocolName.HostReject}\n");
                    continue;
                }

                byte[] acceptBytes = Encoding.UTF8.GetBytes($"{LanRoomProtocolName.HostAccept}|{hostedRoomId}\n");
                stream.Write(acceptBytes, 0, acceptBytes.Length);
                connectedClient = client;
                lock (sessionLock) {
                    currentRole = LanRoomRole.Host;
                    clientReady = false;
                    gameStarted = false;
                    nextMoveId = 1;
                    pendingSubmittedMoves.Clear();
                    pendingAcceptedMoves.Clear();
                    pendingRejectedMoves.Clear();
                    pendingBoardSnapshots.Clear();
                    pendingTimeStates.Clear();
                    pendingTimeoutLosers.Clear();
                    pendingSubmittedResigns.Clear();
                    pendingAcceptedResigns.Clear();
                    pendingInputAuthorities.Clear();
                    pendingSubmittedPasses.Clear();
                    pendingAcceptedPasses.Clear();
                    pendingSubmittedScores.Clear();
                    pendingScoreConfirmRequests.Clear();
                    pendingScoreConfirmResponses.Clear();
                    pendingAcceptedScoreRequests.Clear();
                    ClearScoreResultQueues();
                    ClearTakeBackQueues();
                }
                StartSessionReader(client);
                BroadcastRoomState();
                lastStatus = MessageText.Get("lan_room_player_joined_waiting_ready");
                XNLogger.LogInfo("LAN room client joined.", ("roomId", hostedRoomId));
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (SocketException e) {
                if (isHosting) {
                    XNLogger.LogWarn("Accept LAN room client failed.", ("error", e.Message));
                }
            }
            catch (Exception e) {
                XNLogger.LogWarn("Accept LAN room client failed.", ("error", e.Message));
            }
        }
    }

    private bool TryParseRoom(string payload, string fallbackAddress, out LanRoomInfo room)
    {
        room = default;
        string[] parts = payload.Split('|');
        if (parts.Length != 7 || parts[0] != LanRoomProtocolName.DiscoveryPrefix) {
            return false;
        }

        if (!int.TryParse(parts[4], out int tcpPort) ||
            !int.TryParse(parts[5], out int playerCount) ||
            !int.TryParse(parts[6], out int maxPlayerCount)) {
            return false;
        }

        string hostAddress = string.IsNullOrEmpty(parts[3]) ? fallbackAddress : parts[3];
        room = new LanRoomInfo(parts[1], parts[2], hostAddress, tcpPort, playerCount, maxPlayerCount);
        return true;
    }

    private int GetHostedPlayerCount()
    {
        return connectedClient != null ? 2 : 1;
    }

    private string GetLocalAddress()
    {
        try {
            IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress address in hostEntry.AddressList) {
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)) {
                    return address.ToString();
                }
            }
        }
        catch (Exception e) {
            XNLogger.LogWarn("Get local LAN address failed.", ("error", e.Message));
        }

        return IPAddress.Loopback.ToString();
    }

    private void StartSessionReader(TcpClient client)
    {
        sessionReadThread = new Thread(() => ReadSessionLoop(client))
        {
            IsBackground = true,
            Name = "LanRoomSession"
        };
        sessionReadThread.Start();
    }

    private void ReadSessionLoop(TcpClient client)
    {
        byte[] buffer = new byte[LanRoomConfig.SessionReadBufferSize];
        StringBuilder pendingText = new StringBuilder();
        while (IsCurrentClient(client)) {
            try {
                NetworkStream stream = client.GetStream();
                int readLength = stream.Read(buffer, 0, buffer.Length);
                if (readLength <= 0) {
                    OnSessionDisconnected(client);
                    return;
                }

                pendingText.Append(Encoding.UTF8.GetString(buffer, 0, readLength));
                ConsumeSessionMessages(pendingText);
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (Exception e) {
                if (IsCurrentClient(client)) {
                    XNLogger.LogWarn("Read LAN room session failed.", ("error", e.Message));
                    OnSessionDisconnected(client);
                }
                return;
            }
        }
    }

    private void ConsumeSessionMessages(StringBuilder pendingText)
    {
        while (true) {
            string text = pendingText.ToString();
            int newlineIndex = text.IndexOf('\n');
            if (newlineIndex < 0) {
                return;
            }

            string message = text.Substring(0, newlineIndex).Trim();
            pendingText.Remove(0, newlineIndex + 1);
            if (!string.IsNullOrEmpty(message)) {
                HandleSessionMessage(message);
            }
        }
    }

    private void HandleSessionMessage(string message)
    {
        if (!LanRoomProtocolMessage.TryParse(message, out LanRoomProtocolMessage protocolMessage)) {
            return;
        }

        EnsureProtocolHandlers();
        if (protocolHandlers != null && protocolHandlers.TryGetValue(protocolMessage.protocol, out Action<LanRoomProtocolMessage> handler)) {
            handler(protocolMessage);
            return;
        }

        XNLogger.LogWarn("Unknown LAN room protocol.", ("protocol", protocolMessage.protocol));
    }

    private void HandleReadyMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 2 || !TryParseBool(message.GetArg(1), out bool ready)) {
            return;
        }

        lock (sessionLock) {
            if (message.GetArg(0) == "HOST") {
                hostReady = ready;
            } else if (message.GetArg(0) == "CLIENT") {
                clientReady = ready;
            }
        }

        if (SessionState.role == LanRoomRole.Host) {
            BroadcastRoomState();
        }
        lastStatus = SessionState.GetDisplayText();
    }

    private void HandleStateMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 3 ||
            !TryParseBool(message.GetArg(0), out bool nextHostReady) ||
            !TryParseBool(message.GetArg(1), out bool nextClientReady) ||
            !TryParseBool(message.GetArg(2), out bool nextGameStarted)) {
            return;
        }

        lock (sessionLock) {
            hostReady = nextHostReady;
            clientReady = nextClientReady;
            gameStarted = nextGameStarted;
        }
        lastStatus = SessionState.GetDisplayText();
    }

    private void HandleStartConfigMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 4 && message.ArgCount != 6) {
            return;
        }

        lanBoardCfgId = message.GetArg(0);
        lanHoldTimeCfgId = message.GetArg(1);
        lanByoyomiCountCfgId = message.GetArg(2);
        lanByoyomiTimeCfgId = message.GetArg(3);
        lanHandicapCfgId = message.ArgCount >= 5
            ? DuelHandicapPlacement.GetValidCfgId(message.GetArg(4), lanBoardCfgId)
            : DuelHandicapPlacement.GetDefaultCfgId(lanBoardCfgId);
        lanHostPlayerFlag = message.ArgCount >= 6 && int.TryParse(message.GetArg(5), out int hostPlayerFlagValue)
            ? DuelUtils.GetValidPlayerFlag((PlayerFlag)hostPlayerFlagValue)
            : PlayerFlag.Player1;
        lock (sessionLock) {
            gameStarted = true;
        }
        lastStatus = MessageText.Get("lan_room_host_started_game");
    }

    private void BroadcastRoomState()
    {
        LanRoomSessionState state = SessionState;
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.State)}|{BoolToInt(state.hostReady)}|{BoolToInt(state.clientReady)}|{BoolToInt(state.gameStarted)}");
    }

    private void HandleMoveMessage(LanRoomProtocolMessage message, bool isSubmit)
    {
        if (message.ArgCount != 5 ||
            !int.TryParse(message.GetArg(0), out int moveId) ||
            !int.TryParse(message.GetArg(1), out int boardVersion) ||
            !int.TryParse(message.GetArg(2), out int playerFlagValue) ||
            !int.TryParse(message.GetArg(3), out int x) ||
            !int.TryParse(message.GetArg(4), out int z)) {
            return;
        }

        LanDuelMoveMessage move = new LanDuelMoveMessage(moveId, boardVersion, (PlayerFlag)playerFlagValue, new RectCoordinates(x, z));
        if (isSubmit) {
            EnqueueSubmittedMove(move);
        } else {
            EnqueueAcceptedMove(move);
        }
    }

    private void EnqueueSubmittedMove(LanDuelMoveMessage move)
    {
        lock (sessionLock) {
            pendingSubmittedMoves.Enqueue(move);
        }
    }

    private void EnqueueAcceptedMove(LanDuelMoveMessage move)
    {
        lock (sessionLock) {
            pendingAcceptedMoves.Enqueue(move);
        }
    }

    private void HandleMoveRejectedMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 5 ||
            !int.TryParse(message.GetArg(0), out int moveId) ||
            !int.TryParse(message.GetArg(1), out int playerFlagValue) ||
            !int.TryParse(message.GetArg(2), out int x) ||
            !int.TryParse(message.GetArg(3), out int z) ||
            !int.TryParse(message.GetArg(4), out int rejectReasonValue)) {
            return;
        }

        EnqueueRejectedMove(new LanDuelMoveRejectMessage(
            moveId,
            (PlayerFlag)playerFlagValue,
            new RectCoordinates(x, z),
            (DuelMoveRejectReason)rejectReasonValue));
    }

    private void HandleResignMessage(LanRoomProtocolMessage message, bool isSubmit)
    {
        if (message.ArgCount != 2 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int loserFlagValue)) {
            return;
        }

        LanDuelResignMessage resign = new LanDuelResignMessage(actionId, (PlayerFlag)loserFlagValue);
        if (isSubmit) {
            EnqueueSubmittedResign(resign);
        } else {
            EnqueueAcceptedResign(resign);
        }
    }

    private void HandleInputAuthorityMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 2 ||
            !int.TryParse(message.GetArg(0), out int hostInputPlayerFlagValue) ||
            !int.TryParse(message.GetArg(1), out int clientInputPlayerFlagValue)) {
            return;
        }

        EnqueueInputAuthority(new LanDuelInputAuthorityMessage(
            (PlayerFlag)hostInputPlayerFlagValue,
            (PlayerFlag)clientInputPlayerFlagValue));
    }

    private void HandlePassMessage(LanRoomProtocolMessage message, bool isSubmit)
    {
        if ((isSubmit && message.ArgCount != 3) ||
            (!isSubmit && message.ArgCount != 4) ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int boardVersion) ||
            !int.TryParse(message.GetArg(2), out int playerFlagValue)) {
            return;
        }

        int consecutivePassCount = 0;
        if (!isSubmit && !int.TryParse(message.GetArg(3), out consecutivePassCount)) {
            return;
        }

        LanDuelPassMessage pass = new LanDuelPassMessage(actionId, boardVersion, (PlayerFlag)playerFlagValue, consecutivePassCount);
        if (isSubmit) {
            EnqueueSubmittedPass(pass);
        } else {
            EnqueueAcceptedPass(pass);
        }
    }

    private void HandleScoreRequestMessage(LanRoomProtocolMessage message, bool isSubmit)
    {
        if (message.ArgCount != 3 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int boardVersion) ||
            !int.TryParse(message.GetArg(2), out int requesterFlagValue)) {
            return;
        }

        LanDuelScoreRequestMessage request = new LanDuelScoreRequestMessage(actionId, boardVersion, (PlayerFlag)requesterFlagValue);
        if (isSubmit) {
            EnqueueSubmittedScore(request);
        } else {
            EnqueueAcceptedScoreRequest(request);
        }
    }

    private void HandleScoreConfirmRequestMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 3 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int boardVersion) ||
            !int.TryParse(message.GetArg(2), out int requesterFlagValue)) {
            return;
        }

        EnqueueScoreConfirmRequest(new LanDuelScoreRequestMessage(actionId, boardVersion, (PlayerFlag)requesterFlagValue));
    }

    private void HandleScoreConfirmResponseMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 4 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int requesterFlagValue) ||
            !int.TryParse(message.GetArg(2), out int confirmerFlagValue) ||
            !TryParseBool(message.GetArg(3), out bool accepted)) {
            return;
        }

        EnqueueScoreConfirmResponse(new LanDuelScoreConfirmMessage(
            actionId,
            (PlayerFlag)requesterFlagValue,
            (PlayerFlag)confirmerFlagValue,
            accepted));
    }

    private void HandleScoreResultMessage(LanRoomProtocolMessage message)
    {
        if (TryParseScoreResultMessage(message, out LanDuelScoreResultMessage result)) {
            EnqueueScoreResult(result);
        }
    }

    private void HandleScoreResultConfirmResponseMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 4 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int requesterFlagValue) ||
            !int.TryParse(message.GetArg(2), out int confirmerFlagValue) ||
            !TryParseBool(message.GetArg(3), out bool accepted)) {
            return;
        }

        EnqueueScoreResultConfirmResponse(new LanDuelScoreResultConfirmMessage(
            actionId,
            (PlayerFlag)requesterFlagValue,
            (PlayerFlag)confirmerFlagValue,
            accepted));
    }

    private void HandleAcceptedScoreResultMessage(LanRoomProtocolMessage message)
    {
        if (TryParseScoreResultMessage(message, out LanDuelScoreResultMessage result)) {
            EnqueueAcceptedScoreResult(result);
        }
    }

    private bool TryParseScoreResultMessage(LanRoomProtocolMessage message, out LanDuelScoreResultMessage result)
    {
        result = default;
        if ((message.ArgCount != 7 && message.ArgCount != 8) ||
            !int.TryParse(message.GetArg(0), out int actionId)) {
            return false;
        }

        int offset = 0;
        PlayerFlag requesterFlag = 0;
        if (message.ArgCount == 8) {
            if (!int.TryParse(message.GetArg(1), out int requesterFlagValue)) {
                return false;
            }

            requesterFlag = (PlayerFlag)requesterFlagValue;
            offset = 1;
        }

        if (!float.TryParse(message.GetArg(1 + offset), out float blackScore) ||
            !float.TryParse(message.GetArg(2 + offset), out float whiteScore) ||
            !float.TryParse(message.GetArg(3 + offset), out float komi) ||
            !float.TryParse(message.GetArg(4 + offset), out float margin) ||
            !int.TryParse(message.GetArg(5 + offset), out int winnerFlagValue)) {
            return false;
        }

        result = new LanDuelScoreResultMessage(
            actionId,
            requesterFlag,
            blackScore,
            whiteScore,
            komi,
            margin,
            (PlayerFlag)winnerFlagValue,
            DecodeText(message.GetArg(6 + offset)));
        return true;
    }

    private void HandleScoreFailedMessage(LanRoomProtocolMessage message)
    {
        if ((message.ArgCount != 1 && message.ArgCount != 3) ||
            !int.TryParse(message.GetArg(0), out int actionId)) {
            return;
        }

        PlayerFlag requesterFlag = 0;
        LanDuelScoreFailureReason reason = LanDuelScoreFailureReason.Unknown;
        if (message.ArgCount == 3) {
            if (!int.TryParse(message.GetArg(1), out int requesterFlagValue) ||
                !int.TryParse(message.GetArg(2), out int reasonValue)) {
                return;
            }

            requesterFlag = (PlayerFlag)requesterFlagValue;
            reason = (LanDuelScoreFailureReason)reasonValue;
        }

        EnqueueScoreFailure(new LanDuelScoreFailedMessage(actionId, requesterFlag, reason));
    }

    private void HandleTakeBackRequestMessage(LanRoomProtocolMessage message, bool isSubmit)
    {
        if (message.ArgCount != 4 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int boardVersion) ||
            !int.TryParse(message.GetArg(2), out int requesterFlagValue) ||
            !int.TryParse(message.GetArg(3), out int removeCount)) {
            return;
        }

        LanDuelTakeBackRequestMessage request = new LanDuelTakeBackRequestMessage(actionId, boardVersion, (PlayerFlag)requesterFlagValue, removeCount);
        if (isSubmit) {
            EnqueueSubmittedTakeBack(request);
        } else {
            EnqueueTakeBackConfirmRequest(request);
        }
    }

    private void HandleTakeBackAcceptedMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 4 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int boardVersion) ||
            !int.TryParse(message.GetArg(2), out int requesterFlagValue) ||
            !int.TryParse(message.GetArg(3), out int removeCount)) {
            return;
        }

        EnqueueAcceptedTakeBack(new LanDuelTakeBackRequestMessage(actionId, boardVersion, (PlayerFlag)requesterFlagValue, removeCount));
    }

    private void HandleTakeBackConfirmResponseMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 4 ||
            !int.TryParse(message.GetArg(0), out int actionId) ||
            !int.TryParse(message.GetArg(1), out int requesterFlagValue) ||
            !int.TryParse(message.GetArg(2), out int confirmerFlagValue) ||
            !TryParseBool(message.GetArg(3), out bool accepted)) {
            return;
        }

        EnqueueTakeBackConfirmResponse(new LanDuelTakeBackConfirmMessage(
            actionId,
            (PlayerFlag)requesterFlagValue,
            (PlayerFlag)confirmerFlagValue,
            accepted));
    }

    private void HandleTakeBackRejectedMessage(LanRoomProtocolMessage message)
    {
        if ((message.ArgCount != 1 && message.ArgCount != 2) ||
            !int.TryParse(message.GetArg(0), out int actionId)) {
            return;
        }

        PlayerFlag requesterFlag = 0;
        if (message.ArgCount == 2 && int.TryParse(message.GetArg(1), out int requesterFlagValue)) {
            requesterFlag = (PlayerFlag)requesterFlagValue;
        }

        EnqueueRejectedTakeBack(new LanDuelTakeBackRejectedMessage(actionId, requesterFlag));
    }

    private void EnqueueRejectedMove(LanDuelMoveRejectMessage move)
    {
        lock (sessionLock) {
            pendingRejectedMoves.Enqueue(move);
        }
    }

    private void HandleBoardSnapshotMessage(LanRoomProtocolMessage message)
    {
        if ((message.ArgCount != 7 && message.ArgCount != 8) ||
            !int.TryParse(message.GetArg(0), out int boardVersion) ||
            !int.TryParse(message.GetArg(1), out int boardSize) ||
            !int.TryParse(message.GetArg(2), out int nextTurnPlayerFlagValue) ||
            !int.TryParse(message.GetArg(3), out int latestMoveX) ||
            !int.TryParse(message.GetArg(4), out int latestMoveZ) ||
            !int.TryParse(message.GetArg(5), out int latestMovePlayerFlagValue)) {
            return;
        }

        List<LanDuelBoardSnapshotStone> stones = new List<LanDuelBoardSnapshotStone>();
        string stonePayload = message.GetArg(6);
        if (!string.IsNullOrEmpty(stonePayload)) {
            string[] stoneTexts = stonePayload.Split(';');
            foreach (string stoneText in stoneTexts) {
                if (string.IsNullOrEmpty(stoneText)) {
                    continue;
                }

                string[] stoneParts = stoneText.Split(',');
                if (stoneParts.Length != 3 ||
                    !int.TryParse(stoneParts[0], out int x) ||
                    !int.TryParse(stoneParts[1], out int z) ||
                    !int.TryParse(stoneParts[2], out int playerFlagValue)) {
                    return;
                }

                stones.Add(new LanDuelBoardSnapshotStone(new RectCoordinates(x, z), (PlayerFlag)playerFlagValue));
            }
        }

        RectCoordinates latestMoveCoords = latestMoveX >= 0 && latestMoveZ >= 0
            ? new RectCoordinates(latestMoveX, latestMoveZ)
            : null;
        JArray kataGoMoves = ParseSnapshotMoves(message.ArgCount == 8 ? message.GetArg(7) : string.Empty);
        EnqueueBoardSnapshot(new LanDuelBoardSnapshotMessage(
            boardVersion,
            boardSize,
            (PlayerFlag)nextTurnPlayerFlagValue,
            latestMoveCoords,
            (PlayerFlag)latestMovePlayerFlagValue,
            stones,
            kataGoMoves,
            message.ArgCount == 8));
    }

    private void HandleTimeStateMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 7 ||
            !int.TryParse(message.GetArg(0), out int playerFlagValue) ||
            !int.TryParse(message.GetArg(1), out int holdLeftSeconds) ||
            !int.TryParse(message.GetArg(2), out int byoyomiLeftCount) ||
            !int.TryParse(message.GetArg(3), out int byoyomiLeftSeconds) ||
            !TryParseBool(message.GetArg(4), out bool isInByoyomi) ||
            !int.TryParse(message.GetArg(5), out int turnLeftTimes) ||
            !long.TryParse(message.GetArg(6), out long hostTimestampMilliseconds)) {
            return;
        }

        EnqueueTimeState(new LanDuelTimeStateMessage(
            (PlayerFlag)playerFlagValue,
            holdLeftSeconds,
            byoyomiLeftCount,
            byoyomiLeftSeconds,
            isInByoyomi,
            turnLeftTimes,
            hostTimestampMilliseconds));
    }

    private void HandlePlayerTimeoutMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 1 || !int.TryParse(message.GetArg(0), out int loserFlagValue)) {
            return;
        }

        EnqueueTimeoutLoser((PlayerFlag)loserFlagValue);
    }

    private void EnqueueBoardSnapshot(LanDuelBoardSnapshotMessage snapshot)
    {
        lock (sessionLock) {
            pendingBoardSnapshots.Enqueue(snapshot);
        }
    }

    private void EnqueueTimeState(LanDuelTimeStateMessage timeState)
    {
        lock (sessionLock) {
            pendingTimeStates.Enqueue(timeState);
        }
    }

    private void EnqueueTimeoutLoser(PlayerFlag loserFlag)
    {
        lock (sessionLock) {
            pendingTimeoutLosers.Enqueue(loserFlag);
        }
    }

    private void EnqueueSubmittedResign(LanDuelResignMessage resign)
    {
        lock (sessionLock) {
            pendingSubmittedResigns.Enqueue(resign);
        }
    }

    private void EnqueueAcceptedResign(LanDuelResignMessage resign)
    {
        lock (sessionLock) {
            pendingAcceptedResigns.Enqueue(resign);
        }
    }

    private void EnqueueInputAuthority(LanDuelInputAuthorityMessage authority)
    {
        lock (sessionLock) {
            pendingInputAuthorities.Enqueue(authority);
        }
    }

    private void EnqueueSubmittedPass(LanDuelPassMessage pass)
    {
        lock (sessionLock) {
            pendingSubmittedPasses.Enqueue(pass);
        }
    }

    private void EnqueueAcceptedPass(LanDuelPassMessage pass)
    {
        lock (sessionLock) {
            pendingAcceptedPasses.Enqueue(pass);
        }
    }

    private void EnqueueSubmittedScore(LanDuelScoreRequestMessage request)
    {
        lock (sessionLock) {
            pendingSubmittedScores.Enqueue(request);
        }
    }

    private void EnqueueScoreConfirmRequest(LanDuelScoreRequestMessage request)
    {
        lock (sessionLock) {
            pendingScoreConfirmRequests.Enqueue(request);
        }
    }

    private void EnqueueScoreConfirmResponse(LanDuelScoreConfirmMessage response)
    {
        lock (sessionLock) {
            pendingScoreConfirmResponses.Enqueue(response);
        }
    }

    private void EnqueueAcceptedScoreRequest(LanDuelScoreRequestMessage request)
    {
        lock (sessionLock) {
            pendingAcceptedScoreRequests.Enqueue(request);
        }
    }

    private void EnqueueScoreResult(LanDuelScoreResultMessage result)
    {
        lock (sessionLock) {
            pendingScoreResults.Enqueue(result);
        }
    }

    private void EnqueueScoreResultConfirmResponse(LanDuelScoreResultConfirmMessage response)
    {
        lock (sessionLock) {
            pendingScoreResultConfirmResponses.Enqueue(response);
        }
    }

    private void EnqueueAcceptedScoreResult(LanDuelScoreResultMessage result)
    {
        lock (sessionLock) {
            pendingAcceptedScoreResults.Enqueue(result);
        }
    }

    private void EnqueueScoreFailure(LanDuelScoreFailedMessage failure)
    {
        lock (sessionLock) {
            pendingScoreFailures.Enqueue(failure);
        }
    }

    private void EnqueueSubmittedTakeBack(LanDuelTakeBackRequestMessage request)
    {
        lock (sessionLock) {
            pendingSubmittedTakeBacks.Enqueue(request);
        }
    }

    private void EnqueueTakeBackConfirmRequest(LanDuelTakeBackRequestMessage request)
    {
        lock (sessionLock) {
            pendingTakeBackConfirmRequests.Enqueue(request);
        }
    }

    private void EnqueueTakeBackConfirmResponse(LanDuelTakeBackConfirmMessage response)
    {
        lock (sessionLock) {
            pendingTakeBackConfirmResponses.Enqueue(response);
        }
    }

    private void EnqueueAcceptedTakeBack(LanDuelTakeBackRequestMessage request)
    {
        lock (sessionLock) {
            pendingAcceptedTakeBacks.Enqueue(request);
        }
    }

    private void EnqueueRejectedTakeBack(LanDuelTakeBackRejectedMessage rejected)
    {
        lock (sessionLock) {
            pendingRejectedTakeBacks.Enqueue(rejected);
        }
    }

    private void ClearTakeBackQueues()
    {
        pendingSubmittedTakeBacks.Clear();
        pendingTakeBackConfirmRequests.Clear();
        pendingTakeBackConfirmResponses.Clear();
        pendingAcceptedTakeBacks.Clear();
        pendingRejectedTakeBacks.Clear();
    }

    private void ClearScoreResultQueues()
    {
        pendingScoreResults.Clear();
        pendingScoreResultConfirmResponses.Clear();
        pendingAcceptedScoreResults.Clear();
        pendingScoreFailures.Clear();
    }

    private string SerializeBoardSnapshot(LanDuelBoardSnapshotMessage snapshot)
    {
        StringBuilder stonesBuilder = new StringBuilder();
        if (snapshot.stones != null) {
            foreach (LanDuelBoardSnapshotStone stone in snapshot.stones) {
                if (stone.coords == null) {
                    continue;
                }

                if (stonesBuilder.Length > 0) {
                    stonesBuilder.Append(';');
                }
                stonesBuilder.Append(stone.coords.x);
                stonesBuilder.Append(',');
                stonesBuilder.Append(stone.coords.z);
                stonesBuilder.Append(',');
                stonesBuilder.Append((int)stone.playerFlag);
            }
        }

        int latestMoveX = snapshot.latestMoveCoords != null ? snapshot.latestMoveCoords.x : -1;
        int latestMoveZ = snapshot.latestMoveCoords != null ? snapshot.latestMoveCoords.z : -1;
        string movesPayload = EncodeText((snapshot.kataGoMoves ?? DuelMoveHistory.CreateEmpty()).ToString(Formatting.None));
        return $"{LanRoomProtocolName.ToWireName(LanRoomProtocol.BoardSnapshot)}|{snapshot.boardVersion}|{snapshot.boardSize}|{(int)snapshot.nextTurnPlayerFlag}|{latestMoveX}|{latestMoveZ}|{(int)snapshot.latestMovePlayerFlag}|{stonesBuilder}|{movesPayload}";
    }

    private JArray ParseSnapshotMoves(string encodedMoves)
    {
        if (string.IsNullOrEmpty(encodedMoves)) {
            return DuelMoveHistory.CreateEmpty();
        }

        try {
            string movesJson = DecodeText(encodedMoves);
            return string.IsNullOrEmpty(movesJson) ? DuelMoveHistory.CreateEmpty() : JArray.Parse(movesJson);
        }
        catch (Exception e) {
            XNLogger.LogWarn("Parse LAN board snapshot moves failed.", ("error", e.Message));
            return DuelMoveHistory.CreateEmpty();
        }
    }

    private string SerializeStartConfigMessage()
    {
        return $"{LanRoomProtocolName.ToWireName(LanRoomProtocol.StartConfig)}|{lanBoardCfgId}|{lanHoldTimeCfgId}|{lanByoyomiCountCfgId}|{lanByoyomiTimeCfgId}|{lanHandicapCfgId}|{(int)lanHostPlayerFlag}";
    }

    private string SerializeTimeStateMessage(LanDuelTimeStateMessage timeState)
    {
        return $"{LanRoomProtocolName.ToWireName(LanRoomProtocol.TimeState)}|{(int)timeState.playerFlag}|{timeState.holdLeftSeconds}|{timeState.byoyomiLeftCount}|{timeState.byoyomiLeftSeconds}|{BoolToInt(timeState.isInByoyomi)}|{timeState.turnLeftTimes}|{timeState.hostTimestampMilliseconds}";
    }

    private void SendRoomMessage(string message)
    {
        TcpClient client = connectedClient;
        if (client == null) {
            return;
        }

        try {
            byte[] data = Encoding.UTF8.GetBytes($"{message}\n");
            NetworkStream stream = client.GetStream();
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e) {
            XNLogger.LogWarn("Send LAN room session message failed.", ("error", e.Message));
            OnSessionDisconnected(client);
        }
    }

    private bool IsCurrentClient(TcpClient client)
    {
        return client != null && ReferenceEquals(client, connectedClient);
    }

    private void OnSessionDisconnected(TcpClient client)
    {
        if (!IsCurrentClient(client)) {
            return;
        }

        CloseClient(connectedClient);
        connectedClient = null;
        lock (sessionLock) {
            if (currentRole == LanRoomRole.Client) {
                currentRole = LanRoomRole.None;
                hostReady = false;
            }
            clientReady = false;
            gameStarted = false;
            pendingSubmittedMoves.Clear();
            pendingAcceptedMoves.Clear();
            pendingRejectedMoves.Clear();
            pendingBoardSnapshots.Clear();
            pendingTimeStates.Clear();
            pendingTimeoutLosers.Clear();
            pendingSubmittedResigns.Clear();
            pendingAcceptedResigns.Clear();
            pendingInputAuthorities.Clear();
            pendingSubmittedPasses.Clear();
            pendingAcceptedPasses.Clear();
            pendingSubmittedScores.Clear();
            pendingScoreConfirmRequests.Clear();
            pendingScoreConfirmResponses.Clear();
            pendingAcceptedScoreRequests.Clear();
            ClearScoreResultQueues();
            ClearTakeBackQueues();
        }
        lastStatus = MessageText.Get("lan_room_disconnected");
    }

    private int BoolToInt(bool value)
    {
        return value ? 1 : 0;
    }

    private bool TryParseBool(string value, out bool result)
    {
        if (value == "1") {
            result = true;
            return true;
        }
        if (value == "0") {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private string EncodeText(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private string DecodeText(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        try {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (Exception e) {
            XNLogger.LogWarn("Decode LAN room text failed.", ("error", e.Message));
            return string.Empty;
        }
    }

    private bool ConnectWithTimeout(TcpClient client, string hostAddress, int tcpPort)
    {
        IAsyncResult connectResult = client.BeginConnect(hostAddress, tcpPort, null, null);
        bool connected = connectResult.AsyncWaitHandle.WaitOne(LanRoomConfig.ConnectTimeoutMilliseconds);
        if (!connected) {
            return false;
        }

        client.EndConnect(connectResult);
        return true;
    }

    private void SendAndClose(TcpClient client, string message)
    {
        try {
            byte[] data = Encoding.UTF8.GetBytes(message);
            NetworkStream stream = client.GetStream();
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e) {
            XNLogger.LogWarn("Send LAN room response failed.", ("error", e.Message));
        }
        finally {
            CloseClient(client);
        }
    }

    private void CloseClient(TcpClient client)
    {
        try {
            client?.Close();
        }
        catch (Exception e) {
            XNLogger.LogWarn("Close LAN TCP client failed.", ("error", e.Message));
        }
    }

    private void CloseUdpClient(UdpClient client)
    {
        try {
            client?.Close();
        }
        catch (Exception e) {
            XNLogger.LogWarn("Close LAN UDP client failed.", ("error", e.Message));
        }
    }
}
