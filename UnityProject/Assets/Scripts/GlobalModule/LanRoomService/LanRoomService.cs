using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
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
    private UdpClient hostDiscoveryClient;
    private Thread broadcastThread;
    private Thread hostDiscoveryThread;
    private Thread acceptThread;
    private volatile bool isHosting;

    private UdpClient discoveryClient;
    private Thread discoveryThread;
    private Thread discoveryProbeThread;
    private volatile bool isSearching;

    private readonly object sessionLock = new object();
    private readonly Queue<LanDuelMoveMessage> pendingSubmittedMoves = new Queue<LanDuelMoveMessage>();
    private readonly Queue<LanPlayerProfileMessage> pendingPlayerProfiles = new Queue<LanPlayerProfileMessage>();
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
    private readonly Queue<OnLanRoomPeerLeft> pendingPeerLeftEvents = new Queue<OnLanRoomPeerLeft>();
    private TcpClient connectedClient;
    private Thread sessionReadThread;
    private Thread reconnectThread;
    private volatile bool isReconnectProbing;
    private LanRoomRole currentRole = LanRoomRole.None;
    private bool hostReady;
    private bool clientReady;
    private bool gameStarted;
    private bool isReconnectWaiting;
    private bool pendingReconnectRestoredUiEvent;
    private bool pendingReconnectRestoredDuelEvent;
    private int lastReconnectWaitingSecond = -1;
    private DateTime lastPeerMessageUtc = DateTime.MinValue;
    private DateTime lastHeartbeatSentUtc = DateTime.MinValue;
    private DateTime reconnectStartedUtc = DateTime.MinValue;
    private int nextMoveId = 1;
    private string lanBoardCfgId = "9x9";
    private string lanHoldTimeCfgId = "infinite";
    private string lanByoyomiCountCfgId = "off";
    private string lanByoyomiTimeCfgId = "30s";
    private string lanHandicapCfgId = "9x9_0";
    private PlayerFlag lanHostPlayerFlag = PlayerFlag.Player1;
    private string lanHostPlayerSideCfgId = "black";
    private string lanSessionId;
    private string lanResumeToken;
    private string lastRoomId;
    private string lastHostAddress;
    private int lastHostTcpPort;
    private UserProfileData hostPlayerProfile = UserProfileData.CreateFallback("Host");
    private UserProfileData clientPlayerProfile = UserProfileData.CreateFallback("Client");
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
    public string LanHostPlayerSideCfgId => lanHostPlayerSideCfgId;
    public UserProfileData HostPlayerProfile => hostPlayerProfile;
    public UserProfileData ClientPlayerProfile => clientPlayerProfile;
    public bool IsReconnectWaiting
    {
        get
        {
            lock (sessionLock) {
                return isReconnectWaiting;
            }
        }
    }
    public int ReconnectWaitingSeconds
    {
        get
        {
            lock (sessionLock) {
                return GetReconnectWaitingSeconds(DateTime.UtcNow);
            }
        }
    }
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

    public override void Update()
    {
        base.Update();

        UpdateHeartbeatAndReconnect();

        while (TryDequeuePeerLeftEvent(out OnLanRoomPeerLeft evt)) {
            Global.Instance.eventManager.EmitSystemEvent(evt);
        }

        if (TryConsumeReconnectRestoredEvent()) {
            Global.Instance.eventManager.EmitSystemEvent(new OnLanRoomReconnected());
        }
    }

    public void SetStartConfig(
        string boardCfgId,
        string holdTimeCfgId,
        string byoyomiCountCfgId,
        string byoyomiTimeCfgId,
        string handicapCfgId,
        PlayerFlag hostPlayerFlag,
        string hostPlayerSideCfgId = "black")
    {
        lanBoardCfgId = string.IsNullOrEmpty(boardCfgId) ? "9x9" : boardCfgId;
        lanHoldTimeCfgId = string.IsNullOrEmpty(holdTimeCfgId) ? "infinite" : holdTimeCfgId;
        lanByoyomiCountCfgId = string.IsNullOrEmpty(byoyomiCountCfgId) ? "off" : byoyomiCountCfgId;
        lanByoyomiTimeCfgId = string.IsNullOrEmpty(byoyomiTimeCfgId) ? "30s" : byoyomiTimeCfgId;
        lanHandicapCfgId = DuelHandicapPlacement.GetValidCfgId(handicapCfgId, lanBoardCfgId);
        lanHostPlayerFlag = DuelUtils.GetValidPlayerFlag(hostPlayerFlag);
        lanHostPlayerSideCfgId = GetValidHostPlayerSideCfgId(hostPlayerSideCfgId, lanHostPlayerFlag);
    }

    public void SyncLocalPlayerProfile()
    {
        UserProfileData profile = User.Instance.compUserInfo.BuildProfileData();
        LanRoomRole role;
        lock (sessionLock) {
            role = currentRole;
            if (role == LanRoomRole.Host) {
                hostPlayerProfile = profile;
            } else if (role == LanRoomRole.Client) {
                clientPlayerProfile = profile;
            } else {
                return;
            }
        }

        SendRoomMessage(SerializePlayerProfileMessage(role, profile));
    }

    public bool CreateRoom(string roomName)
    {
        StopRoom();
        hostedRoomId = Guid.NewGuid().ToString("N");
        hostedRoomName = string.IsNullOrEmpty(roomName) ? MessageText.Get("lan_room_default_name") : roomName;
        hostedTcpPort = LanRoomConfig.TcpListenPort;
        lanSessionId = Guid.NewGuid().ToString("N");
        lanResumeToken = Guid.NewGuid().ToString("N");
        lastRoomId = hostedRoomId;
        lastHostAddress = null;
        lastHostTcpPort = hostedTcpPort;
        lock (sessionLock) {
            currentRole = LanRoomRole.Host;
            hostPlayerProfile = User.Instance.compUserInfo.BuildProfileData();
            clientPlayerProfile = UserProfileData.CreateFallback("Client");
            hostReady = false;
            clientReady = false;
            gameStarted = false;
            isReconnectWaiting = false;
            pendingReconnectRestoredUiEvent = false;
            pendingReconnectRestoredDuelEvent = false;
            lastReconnectWaitingSecond = -1;
            nextMoveId = 1;
            lastPeerMessageUtc = DateTime.MinValue;
            lastHeartbeatSentUtc = DateTime.MinValue;
            reconnectStartedUtc = DateTime.MinValue;
            ClearDuelMessageQueues();
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

            hostDiscoveryClient = CreateBoundUdpClient(LanRoomConfig.UdpBroadcastPort);
            hostDiscoveryThread = new Thread(ReceiveDiscoveryRequestLoop)
            {
                IsBackground = true,
                Name = "LanRoomHostDiscovery"
            };
            hostDiscoveryThread.Start();

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
        StopReconnectProbe();
        CloseClient(connectedClient);
        connectedClient = null;
        lock (sessionLock) {
            currentRole = LanRoomRole.None;
            hostPlayerProfile = UserProfileData.CreateFallback("Host");
            clientPlayerProfile = UserProfileData.CreateFallback("Client");
            hostReady = false;
            clientReady = false;
            gameStarted = false;
            isReconnectWaiting = false;
            pendingReconnectRestoredUiEvent = false;
            pendingReconnectRestoredDuelEvent = false;
            lastReconnectWaitingSecond = -1;
            nextMoveId = 1;
            lastPeerMessageUtc = DateTime.MinValue;
            lastHeartbeatSentUtc = DateTime.MinValue;
            reconnectStartedUtc = DateTime.MinValue;
            ClearDuelMessageQueues();
        }

        try {
            hostListener?.Stop();
        }
        catch (Exception e) {
            XNLogger.LogWarn("Stop LAN host listener failed.", ("error", e.Message));
        }
        hostListener = null;

        CloseUdpClient(broadcastClient);
        CloseUdpClient(hostDiscoveryClient);
        broadcastClient = null;
        hostDiscoveryClient = null;
        JoinThread(acceptThread);
        JoinThread(broadcastThread);
        JoinThread(hostDiscoveryThread);
        JoinThread(sessionReadThread);
        acceptThread = null;
        broadcastThread = null;
        hostDiscoveryThread = null;
        sessionReadThread = null;
        hostedRoomId = null;
        hostedRoomName = null;
        lanSessionId = null;
        lanResumeToken = null;
        lastRoomId = null;
        lastHostAddress = null;
        lastHostTcpPort = 0;
        lastStatus = MessageText.Get("lan_room_service_stopped");
    }

    public void LeaveCurrentSession(LanRoomLeaveReason reason, bool notifyPeer = true)
    {
        if (notifyPeer && connectedClient != null) {
            SendLeaveRoomMessage(reason);
        }

        LanRoomResumeTicketStore.Clear();
        StopReconnectProbe();
        StopSearchRooms();
        StopRoom();
    }

    public bool StartSearchRooms()
    {
        LanRoomSessionState state = SessionState;
        if (state.role != LanRoomRole.None) {
            lastStatus = state.GetDisplayText();
            return false;
        }

        StopSearchRooms();
        lock (roomLock) {
            discoveredRooms.Clear();
        }

        try {
            discoveryClient = CreateBoundUdpClient(LanRoomConfig.UdpBroadcastPort);
            isSearching = true;
            discoveryThread = new Thread(ReceiveDiscoveryLoop)
            {
                IsBackground = true,
                Name = "LanRoomDiscovery"
            };
            discoveryThread.Start();

            discoveryProbeThread = new Thread(SendDiscoveryProbeLoop)
            {
                IsBackground = true,
                Name = "LanRoomDiscoveryProbe"
            };
            discoveryProbeThread.Start();

            lastStatus = MessageText.Get("lan_room_searching");
            return true;
        }
        catch (Exception e) {
            lastStatus = MessageText.Format("lan_room_search_failed", e.Message);
            XNLogger.LogError("Start LAN room search failed.", ("error", e.ToString()));
            StopSearchRooms();
            return false;
        }
    }

    public void StopSearchRooms()
    {
        isSearching = false;
        CloseUdpClient(discoveryClient);
        discoveryClient = null;
        JoinThread(discoveryThread);
        JoinThread(discoveryProbeThread);
        discoveryThread = null;
        discoveryProbeThread = null;
    }

    public List<LanRoomInfo> GetDiscoveredRooms()
    {
        lock (roomLock) {
            return new List<LanRoomInfo>(discoveredRooms.Values);
        }
    }

    public bool ConnectToRoom(LanRoomInfo room)
    {
        LanRoomSessionState state = SessionState;
        TcpClient activeClient = connectedClient;
        if (state.role == LanRoomRole.Client && activeClient != null) {
            if (lastRoomId == room.roomId) {
                lastStatus = MessageText.Format("lan_room_connected", room.name);
                return true;
            }

            lastStatus = state.GetDisplayText();
            return false;
        }

        if (state.role == LanRoomRole.Host || (state.role != LanRoomRole.None && activeClient != null)) {
            lastStatus = state.GetDisplayText();
            return false;
        }

        StopReconnectProbe();
        CloseClient(activeClient);
        if (ReferenceEquals(connectedClient, activeClient)) {
            connectedClient = null;
        }
        lastHostAddress = room.hostAddress;
        lastHostTcpPort = room.tcpPort;
        lastRoomId = room.roomId;

        if (room.canResumeGame && TryLoadResumeTicketForRoom(room, out LanRoomResumeTicket ticket)) {
            lanSessionId = ticket.sessionId;
            lanResumeToken = ticket.resumeToken;
            ApplyResumeTicketConfig(ticket, room);
            if (TryResumeConnection(room.hostAddress, room.tcpPort, true)) {
                return true;
            }

            return false;
        }

        if (room.canResumeGame) {
            lastStatus = MessageText.Get("lan_room_connect_rejected");
            return false;
        }

        lanSessionId = null;
        lanResumeToken = null;

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
                hostPlayerProfile = UserProfileData.CreateFallback("Host");
                clientPlayerProfile = User.Instance.compUserInfo.BuildProfileData();
                hostReady = false;
                clientReady = false;
                gameStarted = false;
                isReconnectWaiting = false;
                pendingReconnectRestoredUiEvent = false;
                pendingReconnectRestoredDuelEvent = false;
                lastReconnectWaitingSecond = -1;
                nextMoveId = 1;
                lastPeerMessageUtc = DateTime.UtcNow;
                lastHeartbeatSentUtc = DateTime.MinValue;
                reconnectStartedUtc = DateTime.MinValue;
                ClearDuelMessageQueues();
            }
            StartSessionReader(client);
            SyncLocalPlayerProfile();
            SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.Ready)}|CLIENT|0");
            LanRoomResumeTicketStore.Clear();
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

    public void SetLocalReady(bool ready, bool updateStatus = true)
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

        if (updateStatus) {
            lastStatus = ready ? MessageText.Get("lan_room_ready") : MessageText.Get("lan_room_ready_cancelled");
        }
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
        SaveCurrentResumeTicket();
        BroadcastRoomState();
        lastStatus = MessageText.Get("lan_room_start_command_sent");
        return true;
    }

    public bool SubmitLocalMove(PlayerFlag playerFlag, RectCoordinates coords, int boardVersion)
    {
        if (coords == null) {
            return false;
        }
        if (IsReconnectWaiting) {
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
        if (IsReconnectWaiting) {
            return false;
        }

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
        if (IsReconnectWaiting) {
            return false;
        }

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
        if (IsReconnectWaiting) {
            return false;
        }

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
        if (IsReconnectWaiting) {
            return false;
        }

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
        if (ShouldSendConfirmRequestToPeer(request.requesterFlag)) {
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
        if (ShouldSendConfirmRequestToPeer(request.requesterFlag)) {
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

    public bool TryDequeuePlayerProfile(out LanPlayerProfileMessage profile)
    {
        lock (sessionLock) {
            if (pendingPlayerProfiles.Count > 0) {
                profile = pendingPlayerProfiles.Dequeue();
                return true;
            }
        }

        profile = default;
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
        IPEndPoint[] broadcastEndPoints = BuildDiscoveryBroadcastEndPoints();
        while (isHosting) {
            try {
                string localAddress = GetLocalAddress();
                string payload = BuildDiscoveryPayload(localAddress);
                byte[] data = Encoding.UTF8.GetBytes(payload);
                foreach (IPEndPoint endPoint in broadcastEndPoints) {
                    broadcastClient?.Send(data, data.Length, endPoint);
                }
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

    private void ReceiveDiscoveryRequestLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (isHosting) {
            try {
                byte[] data = hostDiscoveryClient.Receive(ref remoteEndPoint);
                string payload = Encoding.UTF8.GetString(data).Trim();
                if (!string.Equals(payload, LanRoomProtocolName.DiscoveryRequest, StringComparison.Ordinal)) {
                    continue;
                }

                SendDiscoveryResponse(remoteEndPoint);
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (SocketException e) {
                if (isHosting) {
                    XNLogger.LogWarn("Receive LAN room discovery request failed.", ("error", e.Message));
                }
            }
            catch (Exception e) {
                XNLogger.LogWarn("Receive LAN room discovery request failed.", ("error", e.Message));
            }
        }
    }

    private void SendDiscoveryResponse(IPEndPoint remoteEndPoint)
    {
        if (remoteEndPoint == null || remoteEndPoint.Address == null || IPAddress.Any.Equals(remoteEndPoint.Address)) {
            return;
        }

        try {
            string localAddress = GetLocalAddress();
            string payload = BuildDiscoveryPayload(localAddress);
            byte[] data = Encoding.UTF8.GetBytes(payload);
            hostDiscoveryClient?.Send(data, data.Length, remoteEndPoint);
        }
        catch (ObjectDisposedException) {
        }
        catch (Exception e) {
            XNLogger.LogWarn(
                "Send LAN room discovery response failed.",
                ("remote", remoteEndPoint.ToString()),
                ("error", e.Message));
        }
    }

    private void ReceiveDiscoveryLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        while (isSearching) {
            try {
                byte[] data = discoveryClient.Receive(ref remoteEndPoint);
                string payload = Encoding.UTF8.GetString(data);
                AddDiscoveredRoom(payload, remoteEndPoint.Address.ToString());
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
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[LanRoomConfig.HandshakeBufferSize];
                int readLength = stream.Read(buffer, 0, buffer.Length);
                string request = Encoding.UTF8.GetString(buffer, 0, readLength).Trim();

                if (IsResumeRequest(request)) {
                    HandleResumeRequest(client, stream, request);
                    continue;
                }

                lock (sessionLock) {
                    if (isReconnectWaiting || gameStarted) {
                        SendAndClose(client, $"{LanRoomProtocolName.HostFull}\n");
                        continue;
                    }
                }

                if (connectedClient != null) {
                    SendAndClose(client, $"{LanRoomProtocolName.HostFull}\n");
                    continue;
                }

                if (!request.StartsWith($"{LanRoomProtocolName.ClientHello}|{hostedRoomId}", StringComparison.Ordinal)) {
                    SendAndClose(client, $"{LanRoomProtocolName.HostReject}\n");
                    continue;
                }

                byte[] acceptBytes = Encoding.UTF8.GetBytes($"{LanRoomProtocolName.HostAccept}|{hostedRoomId}\n");
                stream.Write(acceptBytes, 0, acceptBytes.Length);
                connectedClient = client;
                lock (sessionLock) {
                    currentRole = LanRoomRole.Host;
                    hostPlayerProfile = User.Instance.compUserInfo.BuildProfileData();
                    clientPlayerProfile = UserProfileData.CreateFallback("Client");
                    clientReady = false;
                    gameStarted = false;
                    isReconnectWaiting = false;
                    pendingReconnectRestoredUiEvent = false;
                    pendingReconnectRestoredDuelEvent = false;
                    lastReconnectWaitingSecond = -1;
                    nextMoveId = 1;
                    lastPeerMessageUtc = DateTime.UtcNow;
                    lastHeartbeatSentUtc = DateTime.MinValue;
                    reconnectStartedUtc = DateTime.MinValue;
                    ClearDuelMessageQueues();
                }
                StartSessionReader(client);
                SyncLocalPlayerProfile();
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

    private void SendDiscoveryProbeLoop()
    {
        IPEndPoint[] probeEndPoints = BuildDiscoveryBroadcastEndPoints();
        byte[] data = Encoding.UTF8.GetBytes(LanRoomProtocolName.DiscoveryRequest);
        while (isSearching) {
            try {
                foreach (IPEndPoint endPoint in probeEndPoints) {
                    discoveryClient?.Send(data, data.Length, endPoint);
                }
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (Exception e) {
                XNLogger.LogWarn("Send LAN room discovery probe failed.", ("error", e.Message));
            }

            Thread.Sleep(LanRoomConfig.BroadcastIntervalMilliseconds);
        }
    }

    private void AddDiscoveredRoom(string payload, string remoteAddress)
    {
        if (TryParseRoom(payload, remoteAddress, out LanRoomInfo room) &&
            !IsHostedRoom(room)) {
            lock (roomLock) {
                discoveredRooms[room.roomId] = room;
            }
        }
    }

    private bool IsHostedRoom(LanRoomInfo room)
    {
        return !string.IsNullOrEmpty(hostedRoomId) &&
            string.Equals(room.roomId, hostedRoomId, StringComparison.Ordinal);
    }

    private bool TryParseRoom(string payload, string remoteAddress, out LanRoomInfo room)
    {
        room = default;
        string[] parts = payload.Split('|');
        if ((parts.Length != 7 && parts.Length != 14 && parts.Length != 15 && parts.Length != 16) ||
            parts[0] != LanRoomProtocolName.DiscoveryPrefix) {
            return false;
        }

        if (!int.TryParse(parts[4], out int tcpPort) ||
            !int.TryParse(parts[5], out int playerCount) ||
            !int.TryParse(parts[6], out int maxPlayerCount)) {
            return false;
        }

        // The UDP source address is the address that actually reached this client.
        // Android-reported local addresses can point at loopback or non-Wi-Fi interfaces.
        string hostAddress = string.IsNullOrEmpty(remoteAddress) ? parts[3] : remoteAddress;
        if (parts.Length == 7) {
            room = new LanRoomInfo(parts[1], parts[2], hostAddress, tcpPort, playerCount, maxPlayerCount);
            return true;
        }

        if (!int.TryParse(parts[13], out int hostPlayerFlagValue)) {
            return false;
        }

        string hostPlayerName = DecodeText(parts[7]);
        room = new LanRoomInfo(
            parts[1],
            parts[2],
            hostAddress,
            tcpPort,
            playerCount,
            maxPlayerCount,
            hostPlayerName,
            parts[8],
            parts[9],
            parts[10],
            parts[11],
            parts[12],
            (PlayerFlag)hostPlayerFlagValue,
            parts.Length >= 15 ? parts[14] : string.Empty,
            parts.Length >= 16 && parts[15] == "1");
        return true;
    }

    private string BuildDiscoveryPayload(string localAddress)
    {
        string hostPlayerName;
        bool canResumeGame;
        lock (sessionLock) {
            UserProfileData profile = hostPlayerProfile ?? UserProfileData.CreateFallback(GetDefaultProfileName(LanRoomRole.Host));
            profile.Normalize(GetDefaultProfileName(LanRoomRole.Host));
            hostPlayerName = profile.name;
            canResumeGame = gameStarted && isReconnectWaiting;
        }

        return $"{LanRoomProtocolName.DiscoveryPrefix}|{hostedRoomId}|{hostedRoomName}|{localAddress}|{hostedTcpPort}|{GetHostedPlayerCount()}|{LanRoomConfig.MaxPlayerCount}|{EncodeText(hostPlayerName)}|{lanBoardCfgId}|{lanHoldTimeCfgId}|{lanByoyomiCountCfgId}|{lanByoyomiTimeCfgId}|{lanHandicapCfgId}|{(int)lanHostPlayerFlag}|{lanHostPlayerSideCfgId}|{BoolToInt(canResumeGame)}";
    }

    private int GetHostedPlayerCount()
    {
        return connectedClient != null ? 2 : 1;
    }

    private UdpClient CreateBoundUdpClient(int port)
    {
        UdpClient client = new UdpClient(AddressFamily.InterNetwork);
        client.EnableBroadcast = true;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        return client;
    }

    private IPEndPoint[] BuildDiscoveryBroadcastEndPoints()
    {
        HashSet<string> addresses = new HashSet<string>();
        List<IPEndPoint> endPoints = new List<IPEndPoint>();

        AddDiscoveryEndPoint(IPAddress.Broadcast, addresses, endPoints);
        AddNetworkInterfaceBroadcastEndPoints(addresses, endPoints);
        foreach (IPAddress address in GetLocalIPv4Addresses()) {
            if (TryGetClassCBroadcastAddress(address, out IPAddress broadcastAddress)) {
                AddDiscoveryEndPoint(broadcastAddress, addresses, endPoints);
            }
        }

        AddDiscoveryEndPoint(IPAddress.Parse("192.168.43.255"), addresses, endPoints);
        AddDiscoveryEndPoint(IPAddress.Parse("192.168.49.255"), addresses, endPoints);
        AddDiscoveryEndPoint(IPAddress.Parse("192.168.1.255"), addresses, endPoints);
        return endPoints.ToArray();
    }

    private void AddDiscoveryEndPoint(IPAddress address, HashSet<string> addresses, List<IPEndPoint> endPoints)
    {
        string text = address.ToString();
        if (!addresses.Add(text)) {
            return;
        }

        endPoints.Add(new IPEndPoint(address, LanRoomConfig.UdpBroadcastPort));
    }

    private void AddNetworkInterfaceBroadcastEndPoints(HashSet<string> addresses, List<IPEndPoint> endPoints)
    {
        try {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()) {
                if (networkInterface.OperationalStatus != OperationalStatus.Up) {
                    continue;
                }

                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation unicastAddress in properties.UnicastAddresses) {
                    IPAddress address = unicastAddress.Address;
                    IPAddress mask = unicastAddress.IPv4Mask;
                    if (address == null ||
                        mask == null ||
                        address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(address)) {
                        continue;
                    }

                    if (TryGetBroadcastAddress(address, mask, out IPAddress broadcastAddress)) {
                        AddDiscoveryEndPoint(broadcastAddress, addresses, endPoints);
                    }
                }
            }
        }
        catch (Exception e) {
            XNLogger.LogWarn("Get LAN broadcast addresses from interfaces failed.", ("error", e.Message));
        }
    }

    private List<IPAddress> GetLocalIPv4Addresses()
    {
        List<IPAddress> addresses = new List<IPAddress>();
        try {
            IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress address in hostEntry.AddressList) {
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)) {
                    addresses.Add(address);
                }
            }
        }
        catch (Exception e) {
            XNLogger.LogWarn("Get local IPv4 addresses failed.", ("error", e.Message));
        }

        return addresses;
    }

    private bool TryGetBroadcastAddress(IPAddress address, IPAddress subnetMask, out IPAddress broadcastAddress)
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte[] maskBytes = subnetMask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4) {
            broadcastAddress = null;
            return false;
        }

        byte[] broadcastBytes = new byte[4];
        for (int i = 0; i < broadcastBytes.Length; i++) {
            broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
        }

        broadcastAddress = new IPAddress(broadcastBytes);
        return true;
    }

    private bool TryGetClassCBroadcastAddress(IPAddress address, out IPAddress broadcastAddress)
    {
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4) {
            broadcastAddress = null;
            return false;
        }

        bytes[3] = 255;
        broadcastAddress = new IPAddress(bytes);
        return true;
    }

    private string GetLocalAddress()
    {
        List<IPAddress> addresses = GetLocalIPv4Addresses();
        if (addresses.Count > 0) {
            return addresses[0].ToString();
        }

        return IPAddress.Loopback.ToString();
    }

    private void StartSessionReader(TcpClient client)
    {
        MarkPeerMessageReceived();
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

        MarkPeerMessageReceived();
        EnsureProtocolHandlers();
        if (protocolHandlers != null && protocolHandlers.TryGetValue(protocolMessage.protocol, out Action<LanRoomProtocolMessage> handler)) {
            handler(protocolMessage);
            return;
        }

        XNLogger.LogWarn("Unknown LAN room protocol.", ("protocol", protocolMessage.protocol));
    }

    private void HandleLeaveRoomMessage(LanRoomProtocolMessage message)
    {
        LanRoomRole peerRole = LanRoomRole.None;
        LanRoomLeaveReason reason = LanRoomLeaveReason.ExitDuel;
        if (message.ArgCount > 0 && Enum.TryParse(message.GetArg(0), out LanRoomRole parsedRole)) {
            peerRole = parsedRole;
        }
        if (message.ArgCount > 1 && Enum.TryParse(message.GetArg(1), out LanRoomLeaveReason parsedReason)) {
            reason = parsedReason;
        }

        EnqueuePeerLeftEvent(peerRole, reason);
        LanRoomResumeTicketStore.Clear();
        StopRoom();
        lastStatus = MessageText.Get("lan_room_peer_left");
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

    private void HandlePlayerProfileMessage(LanRoomProtocolMessage message)
    {
        if (message.ArgCount != 2 || !int.TryParse(message.GetArg(0), out int roleValue)) {
            return;
        }

        LanRoomRole role = (LanRoomRole)roleValue;
        UserProfileData profile = UserProfileData.FromJson(DecodeText(message.GetArg(1)), GetDefaultProfileName(role));
        lock (sessionLock) {
            if (role == LanRoomRole.Host) {
                hostPlayerProfile = profile;
            } else if (role == LanRoomRole.Client) {
                clientPlayerProfile = profile;
            } else {
                return;
            }
        }

        EnqueuePlayerProfile(new LanPlayerProfileMessage(role, profile));
        if (SessionState.role == LanRoomRole.Host) {
            SendRoomMessage(SerializePlayerProfileMessage(LanRoomRole.Host, hostPlayerProfile));
        }
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
        if (message.ArgCount != 4 && message.ArgCount != 6 && message.ArgCount != 7 && message.ArgCount != 9) {
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
        lanHostPlayerSideCfgId = message.ArgCount >= 7
            ? GetValidHostPlayerSideCfgId(message.GetArg(6), lanHostPlayerFlag)
            : GetValidHostPlayerSideCfgId(string.Empty, lanHostPlayerFlag);
        if (message.ArgCount >= 9) {
            lanSessionId = message.GetArg(7);
            lanResumeToken = message.GetArg(8);
        }
        if (string.IsNullOrEmpty(lastRoomId) && LanRoomResumeTicketStore.TryLoad(out LanRoomResumeTicket ticket)) {
            lastRoomId = ticket.roomId;
        }
        lock (sessionLock) {
            gameStarted = true;
            lastPeerMessageUtc = DateTime.UtcNow;
        }
        SaveCurrentResumeTicket();
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

    private void EnqueuePlayerProfile(LanPlayerProfileMessage profile)
    {
        lock (sessionLock) {
            pendingPlayerProfiles.Enqueue(profile);
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

    private void ClearDuelMessageQueues()
    {
        pendingSubmittedMoves.Clear();
        pendingPlayerProfiles.Clear();
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

    private void ClearTransientActionQueuesForReconnect()
    {
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

    private bool TryDequeuePeerLeftEvent(out OnLanRoomPeerLeft evt)
    {
        lock (sessionLock) {
            if (pendingPeerLeftEvents.Count > 0) {
                evt = pendingPeerLeftEvents.Dequeue();
                return true;
            }
        }

        evt = null;
        return false;
    }

    private void EnqueuePeerLeftEvent(LanRoomRole peerRole, LanRoomLeaveReason reason)
    {
        lock (sessionLock) {
            pendingPeerLeftEvents.Enqueue(new OnLanRoomPeerLeft(peerRole, reason));
        }
    }

    public bool TryConsumeReconnectRestoredForDuel()
    {
        lock (sessionLock) {
            if (!pendingReconnectRestoredDuelEvent || isReconnectWaiting) {
                return false;
            }

            pendingReconnectRestoredDuelEvent = false;
            return true;
        }
    }

    private bool TryConsumeReconnectRestoredEvent()
    {
        lock (sessionLock) {
            if (!pendingReconnectRestoredUiEvent || isReconnectWaiting) {
                return false;
            }

            pendingReconnectRestoredUiEvent = false;
        }

        return true;
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
        EnsureResumeSessionKeys();
        return $"{LanRoomProtocolName.ToWireName(LanRoomProtocol.StartConfig)}|{lanBoardCfgId}|{lanHoldTimeCfgId}|{lanByoyomiCountCfgId}|{lanByoyomiTimeCfgId}|{lanHandicapCfgId}|{(int)lanHostPlayerFlag}|{lanHostPlayerSideCfgId}|{lanSessionId}|{lanResumeToken}";
    }

    private string SerializePlayerProfileMessage(LanRoomRole role, UserProfileData profile)
    {
        UserProfileData safeProfile = profile ?? UserProfileData.CreateFallback(GetDefaultProfileName(role));
        safeProfile.Normalize(GetDefaultProfileName(role));
        return $"{LanRoomProtocolName.ToWireName(LanRoomProtocol.PlayerProfile)}|{(int)role}|{EncodeText(safeProfile.ToJson())}";
    }

    private string SerializeTimeStateMessage(LanDuelTimeStateMessage timeState)
    {
        return $"{LanRoomProtocolName.ToWireName(LanRoomProtocol.TimeState)}|{(int)timeState.playerFlag}|{timeState.holdLeftSeconds}|{timeState.byoyomiLeftCount}|{timeState.byoyomiLeftSeconds}|{BoolToInt(timeState.isInByoyomi)}|{timeState.turnLeftTimes}|{timeState.hostTimestampMilliseconds}";
    }

    private void SendLeaveRoomMessage(LanRoomLeaveReason reason)
    {
        SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.LeaveRoom)}|{currentRole}|{reason}", false);
    }

    private void UpdateHeartbeatAndReconnect()
    {
        DateTime now = DateTime.UtcNow;
        TcpClient client;
        LanRoomRole role;
        bool waiting;
        bool started;
        lock (sessionLock) {
            client = connectedClient;
            role = currentRole;
            waiting = isReconnectWaiting;
            started = gameStarted;
        }

        if (waiting) {
            EmitReconnectWaitingTick(now);
            if (role == LanRoomRole.Client) {
                EnsureReconnectProbeStarted();
            }
            return;
        }

        if (client == null || role == LanRoomRole.None || !started) {
            return;
        }

        if ((now - lastHeartbeatSentUtc).TotalMilliseconds >= LanRoomConfig.HeartbeatIntervalMilliseconds) {
            lastHeartbeatSentUtc = now;
            SendRoomMessage($"{LanRoomProtocolName.ToWireName(LanRoomProtocol.Heartbeat)}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        }

        if (lastPeerMessageUtc != DateTime.MinValue &&
            (now - lastPeerMessageUtc).TotalMilliseconds >= LanRoomConfig.HeartbeatTimeoutMilliseconds) {
            EnterReconnectWaiting(client, "heartbeat timeout");
        }
    }

    private void EmitReconnectWaitingTick(DateTime now)
    {
        int elapsedSeconds;
        lock (sessionLock) {
            elapsedSeconds = GetReconnectWaitingSeconds(now);
            if (elapsedSeconds == lastReconnectWaitingSecond) {
                return;
            }
            lastReconnectWaitingSecond = elapsedSeconds;
        }

        Global.Instance.eventManager.EmitSystemEvent(new OnLanRoomReconnectWaiting(elapsedSeconds));
    }

    private int GetReconnectWaitingSeconds(DateTime now)
    {
        if (!isReconnectWaiting || reconnectStartedUtc == DateTime.MinValue) {
            return 0;
        }

        return Math.Max(1, (int)(now - reconnectStartedUtc).TotalSeconds);
    }

    private void MarkPeerMessageReceived()
    {
        lock (sessionLock) {
            lastPeerMessageUtc = DateTime.UtcNow;
        }
    }

    private void EnsureResumeSessionKeys()
    {
        if (string.IsNullOrEmpty(lanSessionId)) {
            lanSessionId = Guid.NewGuid().ToString("N");
        }
        if (string.IsNullOrEmpty(lanResumeToken)) {
            lanResumeToken = Guid.NewGuid().ToString("N");
        }
    }

    private void EnterReconnectWaiting(TcpClient client, string reason)
    {
        if (!IsCurrentClient(client)) {
            return;
        }

        CloseClient(client);
        connectedClient = null;
        bool shouldStartProbe = false;
        lock (sessionLock) {
            if (!gameStarted || currentRole == LanRoomRole.None) {
                ClearDuelMessageQueues();
                lastStatus = MessageText.Get("lan_room_disconnected");
                return;
            }
            if (isReconnectWaiting) {
                return;
            }

            isReconnectWaiting = true;
            reconnectStartedUtc = DateTime.UtcNow;
            lastReconnectWaitingSecond = -1;
            clientReady = false;
            lastPeerMessageUtc = DateTime.MinValue;
            lastHeartbeatSentUtc = DateTime.MinValue;
            ClearTransientActionQueuesForReconnect();
            shouldStartProbe = currentRole == LanRoomRole.Client;
        }

        lastStatus = MessageText.Get("lan_room_reconnect_waiting_status");
        XNLogger.LogWarn("LAN room session entered reconnect waiting.", ("reason", reason ?? string.Empty));
        if (shouldStartProbe) {
            EnsureReconnectProbeStarted();
        }
    }

    private bool IsResumeRequest(string request)
    {
        return !string.IsNullOrEmpty(request) &&
            request.StartsWith($"{LanRoomProtocolName.ResumeHello}|", StringComparison.Ordinal);
    }

    private void HandleResumeRequest(TcpClient client, NetworkStream stream, string request)
    {
        string[] parts = request.Split('|');
        if (parts.Length < 3) {
            SendAndClose(client, $"{LanRoomProtocolName.ResumeReject}|BAD_REQUEST\n");
            return;
        }

        bool accepted;
        lock (sessionLock) {
            accepted = currentRole == LanRoomRole.Host
                && gameStarted
                && isReconnectWaiting
                && parts[1] == lanSessionId
                && parts[2] == lanResumeToken;
        }

        if (!accepted) {
            SendAndClose(client, $"{LanRoomProtocolName.ResumeReject}|INVALID_SESSION\n");
            return;
        }

        try {
            lock (sessionLock) {
                if (!isReconnectWaiting || currentRole != LanRoomRole.Host) {
                    SendAndClose(client, $"{LanRoomProtocolName.ResumeReject}|SESSION_ACTIVE\n");
                    return;
                }
            }

            byte[] acceptBytes = Encoding.UTF8.GetBytes($"{LanRoomProtocolName.ResumeAccept}|{lanSessionId}\n");
            stream.Write(acceptBytes, 0, acceptBytes.Length);
            CloseClient(connectedClient);
            connectedClient = client;
            lock (sessionLock) {
                currentRole = LanRoomRole.Host;
                clientReady = true;
                isReconnectWaiting = false;
                hostReady = true;
                pendingReconnectRestoredUiEvent = true;
                pendingReconnectRestoredDuelEvent = true;
                lastReconnectWaitingSecond = -1;
                lastPeerMessageUtc = DateTime.UtcNow;
                lastHeartbeatSentUtc = DateTime.MinValue;
                reconnectStartedUtc = DateTime.MinValue;
            }

            StartSessionReader(client);
            SendRoomMessage(SerializeStartConfigMessage());
            BroadcastRoomState();
            SyncLocalPlayerProfile();
            lastStatus = MessageText.Get("lan_room_reconnect_restored");
            XNLogger.LogInfo("LAN room client resumed session.", ("sessionId", lanSessionId ?? string.Empty));
        }
        catch (Exception e) {
            XNLogger.LogWarn("Accept LAN room resume failed.", ("error", e.Message));
            CloseClient(client);
        }
    }

    private void EnsureReconnectProbeStarted()
    {
        if (isReconnectProbing || string.IsNullOrEmpty(lastHostAddress) || lastHostTcpPort <= 0) {
            return;
        }

        lock (sessionLock) {
            if (isReconnectProbing || !isReconnectWaiting || currentRole != LanRoomRole.Client) {
                return;
            }

            isReconnectProbing = true;
        }

        reconnectThread = new Thread(ReconnectProbeLoop)
        {
            IsBackground = true,
            Name = "LanRoomReconnect"
        };
        reconnectThread.Start();
    }

    private void ReconnectProbeLoop()
    {
        while (isReconnectProbing) {
            bool shouldContinue;
            lock (sessionLock) {
                shouldContinue = isReconnectWaiting && currentRole == LanRoomRole.Client;
            }
            if (!shouldContinue) {
                break;
            }

            if (TryResumeConnection()) {
                break;
            }

            Thread.Sleep(Math.Max(200, LanRoomConfig.ReconnectProbeIntervalMilliseconds));
        }

        isReconnectProbing = false;
    }

    private bool TryResumeConnection()
    {
        return TryResumeConnection(lastHostAddress, lastHostTcpPort, false);
    }

    private bool TryResumeConnection(string hostAddress, int tcpPort, bool allowColdStart)
    {
        string sessionId;
        string resumeToken;
        lock (sessionLock) {
            sessionId = lanSessionId;
            resumeToken = lanResumeToken;
        }

        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(resumeToken) || string.IsNullOrEmpty(hostAddress) || tcpPort <= 0) {
            return false;
        }

        TcpClient client = new TcpClient();
        try {
            if (!ConnectWithTimeout(client, hostAddress, tcpPort)) {
                client.Close();
                return false;
            }

            client.SendTimeout = LanRoomConfig.ConnectTimeoutMilliseconds;
            client.ReceiveTimeout = LanRoomConfig.ConnectTimeoutMilliseconds;
            NetworkStream stream = client.GetStream();
            byte[] helloBytes = Encoding.UTF8.GetBytes($"{LanRoomProtocolName.ResumeHello}|{sessionId}|{resumeToken}\n");
            stream.Write(helloBytes, 0, helloBytes.Length);

            byte[] buffer = new byte[LanRoomConfig.HandshakeBufferSize];
            int readLength = stream.Read(buffer, 0, buffer.Length);
            string response = Encoding.UTF8.GetString(buffer, 0, readLength).Trim();
            if (!response.StartsWith($"{LanRoomProtocolName.ResumeAccept}|{sessionId}", StringComparison.Ordinal)) {
                client.Close();
                return false;
            }

            lock (sessionLock) {
                if (!allowColdStart && (!isReconnectProbing || !isReconnectWaiting || currentRole != LanRoomRole.Client)) {
                    client.Close();
                    return false;
                }
            }

            CompleteClientResume(client, hostAddress, tcpPort, allowColdStart);
            return true;
        }
        catch (Exception e) {
            XNLogger.LogWarn("LAN room resume probe failed.", ("error", e.Message));
            CloseClient(client);
            return false;
        }
    }

    private void StopReconnectProbe()
    {
        isReconnectProbing = false;
        JoinThread(reconnectThread);
        reconnectThread = null;
    }

    private void CompleteClientResume(TcpClient client, string hostAddress, int tcpPort, bool restoredFromTicket)
    {
        client.ReceiveTimeout = 0;
        CloseClient(connectedClient);
        connectedClient = client;
        lastHostAddress = hostAddress;
        lastHostTcpPort = tcpPort;
        if (string.IsNullOrEmpty(lastRoomId)) {
            lastRoomId = LoadTicketRoomIdFallback();
        }
        lock (sessionLock) {
            currentRole = LanRoomRole.Client;
            hostReady = true;
            clientReady = true;
            gameStarted = true;
            isReconnectWaiting = false;
            pendingReconnectRestoredUiEvent = true;
            pendingReconnectRestoredDuelEvent = true;
            lastReconnectWaitingSecond = -1;
            lastPeerMessageUtc = DateTime.UtcNow;
            lastHeartbeatSentUtc = DateTime.MinValue;
            reconnectStartedUtc = DateTime.MinValue;
            if (restoredFromTicket) {
                ClearDuelMessageQueues();
            }
        }

        StartSessionReader(client);
        SyncLocalPlayerProfile();
        SaveCurrentResumeTicket();
        lastStatus = MessageText.Get("lan_room_reconnect_restored");
        XNLogger.LogInfo(
            "LAN room client resumed connection.",
            ("host", hostAddress ?? string.Empty),
            ("restoredFromTicket", restoredFromTicket.ToString()));
    }

    private bool TryLoadResumeTicketForRoom(LanRoomInfo room, out LanRoomResumeTicket ticket)
    {
        if (!LanRoomResumeTicketStore.TryLoad(out ticket)) {
            return false;
        }

        return ticket.roomId == room.roomId;
    }

    private void ApplyResumeTicketConfig(LanRoomResumeTicket ticket, LanRoomInfo room)
    {
        lanBoardCfgId = string.IsNullOrEmpty(ticket.boardCfgId) ? room.boardCfgId : ticket.boardCfgId;
        lanHoldTimeCfgId = string.IsNullOrEmpty(ticket.holdTimeCfgId) ? room.holdTimeCfgId : ticket.holdTimeCfgId;
        lanByoyomiCountCfgId = string.IsNullOrEmpty(ticket.byoyomiCountCfgId) ? room.byoyomiCountCfgId : ticket.byoyomiCountCfgId;
        lanByoyomiTimeCfgId = string.IsNullOrEmpty(ticket.byoyomiTimeCfgId) ? room.byoyomiTimeCfgId : ticket.byoyomiTimeCfgId;
        string handicapCfgId = string.IsNullOrEmpty(ticket.handicapCfgId) ? room.handicapCfgId : ticket.handicapCfgId;
        lanHandicapCfgId = DuelHandicapPlacement.GetValidCfgId(handicapCfgId, lanBoardCfgId);
        lanHostPlayerFlag = DuelUtils.GetValidPlayerFlag((PlayerFlag)(ticket.hostPlayerFlag != 0 ? ticket.hostPlayerFlag : (int)room.hostPlayerFlag));
        lanHostPlayerSideCfgId = GetValidHostPlayerSideCfgId(
            string.IsNullOrEmpty(ticket.hostPlayerSideCfgId) ? room.hostPlayerSideCfgId : ticket.hostPlayerSideCfgId,
            lanHostPlayerFlag);
    }

    private void SaveCurrentResumeTicket()
    {
        LanRoomRole role;
        bool started;
        string sessionId;
        string resumeToken;
        lock (sessionLock) {
            role = currentRole;
            started = gameStarted;
            sessionId = lanSessionId;
            resumeToken = lanResumeToken;
        }

        if (!started || role == LanRoomRole.None || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(resumeToken)) {
            return;
        }

        string roomId = role == LanRoomRole.Host ? hostedRoomId : lastRoomId;
        if (string.IsNullOrEmpty(roomId)) {
            roomId = LoadTicketRoomIdFallback();
        }
        if (string.IsNullOrEmpty(roomId)) {
            return;
        }

        LanRoomResumeTicketStore.Save(new LanRoomResumeTicket
        {
            roomId = roomId,
            sessionId = sessionId,
            resumeToken = resumeToken,
            hostAddress = role == LanRoomRole.Host ? GetLocalAddress() : lastHostAddress,
            tcpPort = role == LanRoomRole.Host ? hostedTcpPort : lastHostTcpPort,
            boardCfgId = lanBoardCfgId,
            holdTimeCfgId = lanHoldTimeCfgId,
            byoyomiCountCfgId = lanByoyomiCountCfgId,
            byoyomiTimeCfgId = lanByoyomiTimeCfgId,
            handicapCfgId = lanHandicapCfgId,
            hostPlayerFlag = (int)lanHostPlayerFlag,
            hostPlayerSideCfgId = lanHostPlayerSideCfgId,
        });
    }

    private string LoadTicketRoomIdFallback()
    {
        if (LanRoomResumeTicketStore.TryLoad(out LanRoomResumeTicket ticket)) {
            return ticket.roomId;
        }

        return null;
    }

    private void SendRoomMessage(string message)
    {
        SendRoomMessage(message, true);
    }

    private void SendRoomMessage(string message, bool disconnectOnFailure)
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
            if (disconnectOnFailure) {
                OnSessionDisconnected(client);
            }
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

        LanRoomRole role;
        bool started;
        lock (sessionLock) {
            role = currentRole;
            started = gameStarted;
        }
        if (started && role != LanRoomRole.None) {
            EnterReconnectWaiting(client, "session disconnected");
            return;
        }

        CloseClient(connectedClient);
        connectedClient = null;
        lock (sessionLock) {
            if (currentRole == LanRoomRole.Client) {
                currentRole = LanRoomRole.None;
                hostPlayerProfile = UserProfileData.CreateFallback("Host");
                clientPlayerProfile = UserProfileData.CreateFallback("Client");
                hostReady = false;
            }
            clientReady = false;
            gameStarted = false;
            ClearDuelMessageQueues();
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

    private string GetDefaultProfileName(LanRoomRole role)
    {
        if (role == LanRoomRole.Host) {
            return "Host";
        }

        if (role == LanRoomRole.Client) {
            return "Client";
        }

        return "Player";
    }

    private string GetValidHostPlayerSideCfgId(string hostPlayerSideCfgId, PlayerFlag fallbackPlayerFlag)
    {
        if (hostPlayerSideCfgId == "guess" || hostPlayerSideCfgId == "black" || hostPlayerSideCfgId == "white") {
            return hostPlayerSideCfgId;
        }

        return fallbackPlayerFlag == PlayerFlag.Player2 ? "white" : "black";
    }

    private bool ShouldSendConfirmRequestToPeer(PlayerFlag requesterFlag)
    {
        lock (sessionLock) {
            return currentRole == LanRoomRole.Host
                && requesterFlag != 0
                && requesterFlag == lanHostPlayerFlag;
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

    private void JoinThread(Thread thread)
    {
        if (thread == null || !thread.IsAlive || ReferenceEquals(Thread.CurrentThread, thread)) {
            return;
        }

        try {
            if (!thread.Join(100)) {
                XNLogger.LogWarn("Wait LAN room thread exit timed out.", ("thread", thread.Name ?? string.Empty));
            }
        }
        catch (Exception e) {
            XNLogger.LogWarn("Wait LAN room thread exit failed.", ("error", e.Message));
        }
    }
}
