using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using XNClient.ChessBoard;
using XNClient.Logger;

public class LanRoomService : ModuleBase
{
    private const int DiscoveryPort = 47861;
    private const int DefaultTcpPort = 47862;
    private const int ConnectTimeoutMilliseconds = 2000;
    private const int MaxPlayerCount = 2;
    private const string DiscoveryPrefix = "WEIQIXN_LAN_ROOM";
    private const string ClientHello = "WEIQIXN_JOIN";
    private const string HostAccept = "WEIQIXN_ACCEPT";
    private const string MsgReady = "READY";
    private const string MsgState = "STATE";
    private const string MsgStart = "START";
    private const string MsgSubmitMove = "SUBMIT_MOVE";
    private const string MsgMoveAccepted = "MOVE_ACCEPTED";
    private const string MsgMoveRejected = "MOVE_REJECTED";
    private const string MsgBoardSnapshot = "BOARD_SNAPSHOT";

    private readonly object roomLock = new object();
    private readonly Dictionary<string, LanRoomInfo> discoveredRooms = new Dictionary<string, LanRoomInfo>();

    private string hostedRoomId;
    private string hostedRoomName;
    private int hostedTcpPort = DefaultTcpPort;
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
    private TcpClient connectedClient;
    private Thread sessionReadThread;
    private LanRoomRole currentRole = LanRoomRole.None;
    private bool hostReady;
    private bool clientReady;
    private bool gameStarted;
    private int nextMoveId = 1;
    private string lastStatus = "局域网房间服务未启动。";

    public bool IsHosting => isHosting;
    public bool IsSearching => isSearching;
    public string LastStatus => lastStatus;
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

    public bool CreateRoom(string roomName)
    {
        StopRoom();
        hostedRoomId = Guid.NewGuid().ToString("N");
        hostedRoomName = string.IsNullOrEmpty(roomName) ? "局域网房间" : roomName;
        hostedTcpPort = DefaultTcpPort;
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

            lastStatus = $"已创建房间 {hostedRoomName}，等待玩家加入。";
            XNLogger.LogInfo("LAN room created.", ("roomId", hostedRoomId), ("tcpPort", hostedTcpPort.ToString()));
            return true;
        }
        catch (Exception e) {
            lastStatus = $"创建房间失败：{e.Message}";
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
            discoveryClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            isSearching = true;
            discoveryThread = new Thread(ReceiveDiscoveryLoop)
            {
                IsBackground = true,
                Name = "LanRoomDiscovery"
            };
            discoveryThread.Start();

            lastStatus = "正在搜索局域网房间。";
        }
        catch (Exception e) {
            lastStatus = $"搜索房间失败：{e.Message}";
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
                lastStatus = $"连接房间失败：连接超时。";
                return false;
            }

            client.SendTimeout = ConnectTimeoutMilliseconds;
            client.ReceiveTimeout = ConnectTimeoutMilliseconds;
            byte[] joinBytes = Encoding.UTF8.GetBytes($"{ClientHello}|{room.roomId}\n");
            NetworkStream stream = client.GetStream();
            stream.Write(joinBytes, 0, joinBytes.Length);

            byte[] buffer = new byte[128];
            int readLength = stream.Read(buffer, 0, buffer.Length);
            string response = Encoding.UTF8.GetString(buffer, 0, readLength).Trim();
            if (!response.StartsWith(HostAccept, StringComparison.Ordinal)) {
                client.Close();
                lastStatus = $"连接房间失败：主机拒绝连接。";
                return false;
            }

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
            }
            StartSessionReader(client);
            SendRoomMessage($"{MsgReady}|CLIENT|0");
            lastStatus = $"已连接房间 {room.name}。";
            XNLogger.LogInfo("LAN room connected.", ("roomId", room.roomId), ("host", room.hostAddress));
            return true;
        }
        catch (Exception e) {
            lastStatus = $"连接房间失败：{e.Message}";
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
                lastStatus = "尚未进入房间，无法准备。";
                return;
            }
        }

        if (role == LanRoomRole.Host) {
            BroadcastRoomState();
        } else {
            SendRoomMessage($"{MsgReady}|CLIENT|{BoolToInt(ready)}");
        }

        lastStatus = ready ? "已准备。" : "已取消准备。";
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
            lastStatus = "双方准备后才能由主机开始对局。";
            return false;
        }

        SendRoomMessage(MsgStart);
        BroadcastRoomState();
        lastStatus = "已发送开局命令。";
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
            lastStatus = "当前不在局域网对局中，无法提交落子。";
            return false;
        }

        SendRoomMessage($"{MsgSubmitMove}|{move.moveId}|{move.boardVersion}|{(int)move.playerFlag}|{move.coords.x}|{move.coords.z}");
        return true;
    }

    public void BroadcastAcceptedMove(LanDuelMoveMessage move)
    {
        EnqueueAcceptedMove(move);
        SendRoomMessage($"{MsgMoveAccepted}|{move.moveId}|{move.boardVersion}|{(int)move.playerFlag}|{move.coords.x}|{move.coords.z}");
    }

    public void BroadcastRejectedMove(LanDuelMoveRejectMessage move)
    {
        EnqueueRejectedMove(move);
        SendRoomMessage($"{MsgMoveRejected}|{move.moveId}|{(int)move.playerFlag}|{move.coords.x}|{move.coords.z}|{(int)move.rejectReason}");
    }

    public void BroadcastBoardSnapshot(LanDuelBoardSnapshotMessage snapshot)
    {
        EnqueueBoardSnapshot(snapshot);
        SendRoomMessage(SerializeBoardSnapshot(snapshot));
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

    public override void OnDestroy()
    {
        StopSearchRooms();
        StopRoom();
        base.OnDestroy();
    }

    private void BroadcastRoomLoop()
    {
        IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        while (isHosting) {
            try {
                string localAddress = GetLocalAddress();
                string payload = $"{DiscoveryPrefix}|{hostedRoomId}|{hostedRoomName}|{localAddress}|{hostedTcpPort}|{GetHostedPlayerCount()}|{MaxPlayerCount}";
                byte[] data = Encoding.UTF8.GetBytes(payload);
                broadcastClient?.Send(data, data.Length, broadcastEndPoint);
            }
            catch (ObjectDisposedException) {
                return;
            }
            catch (Exception e) {
                XNLogger.LogWarn("Broadcast LAN room failed.", ("error", e.Message));
            }

            Thread.Sleep(1000);
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
                    SendAndClose(client, "WEIQIXN_FULL\n");
                    continue;
                }

                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[128];
                int readLength = stream.Read(buffer, 0, buffer.Length);
                string request = Encoding.UTF8.GetString(buffer, 0, readLength).Trim();
                if (!request.StartsWith($"{ClientHello}|{hostedRoomId}", StringComparison.Ordinal)) {
                    SendAndClose(client, "WEIQIXN_REJECT\n");
                    continue;
                }

                byte[] acceptBytes = Encoding.UTF8.GetBytes($"{HostAccept}|{hostedRoomId}\n");
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
                }
                StartSessionReader(client);
                BroadcastRoomState();
                lastStatus = "已有玩家加入房间，等待双方准备。";
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
        if (parts.Length != 7 || parts[0] != DiscoveryPrefix) {
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
        byte[] buffer = new byte[512];
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
        string[] parts = message.Split('|');
        if (parts.Length == 0) {
            return;
        }

        switch (parts[0]) {
            case MsgReady:
                HandleReadyMessage(parts);
                break;
            case MsgState:
                HandleStateMessage(parts);
                break;
            case MsgStart:
                lock (sessionLock) {
                    gameStarted = true;
                }
                lastStatus = "主机已开始对局。";
                break;
            case MsgSubmitMove:
                HandleMoveMessage(parts, true);
                break;
            case MsgMoveAccepted:
                HandleMoveMessage(parts, false);
                break;
            case MsgMoveRejected:
                HandleMoveRejectedMessage(parts);
                break;
            case MsgBoardSnapshot:
                HandleBoardSnapshotMessage(parts);
                break;
        }
    }

    private void HandleReadyMessage(string[] parts)
    {
        if (parts.Length != 3 || !TryParseBool(parts[2], out bool ready)) {
            return;
        }

        lock (sessionLock) {
            if (parts[1] == "HOST") {
                hostReady = ready;
            } else if (parts[1] == "CLIENT") {
                clientReady = ready;
            }
        }

        if (SessionState.role == LanRoomRole.Host) {
            BroadcastRoomState();
        }
        lastStatus = SessionState.GetDisplayText();
    }

    private void HandleStateMessage(string[] parts)
    {
        if (parts.Length != 4 ||
            !TryParseBool(parts[1], out bool nextHostReady) ||
            !TryParseBool(parts[2], out bool nextClientReady) ||
            !TryParseBool(parts[3], out bool nextGameStarted)) {
            return;
        }

        lock (sessionLock) {
            hostReady = nextHostReady;
            clientReady = nextClientReady;
            gameStarted = nextGameStarted;
        }
        lastStatus = SessionState.GetDisplayText();
    }

    private void BroadcastRoomState()
    {
        LanRoomSessionState state = SessionState;
        SendRoomMessage($"{MsgState}|{BoolToInt(state.hostReady)}|{BoolToInt(state.clientReady)}|{BoolToInt(state.gameStarted)}");
    }

    private void HandleMoveMessage(string[] parts, bool isSubmit)
    {
        if (parts.Length != 6 ||
            !int.TryParse(parts[1], out int moveId) ||
            !int.TryParse(parts[2], out int boardVersion) ||
            !int.TryParse(parts[3], out int playerFlagValue) ||
            !int.TryParse(parts[4], out int x) ||
            !int.TryParse(parts[5], out int z)) {
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

    private void HandleMoveRejectedMessage(string[] parts)
    {
        if (parts.Length != 6 ||
            !int.TryParse(parts[1], out int moveId) ||
            !int.TryParse(parts[2], out int playerFlagValue) ||
            !int.TryParse(parts[3], out int x) ||
            !int.TryParse(parts[4], out int z) ||
            !int.TryParse(parts[5], out int rejectReasonValue)) {
            return;
        }

        EnqueueRejectedMove(new LanDuelMoveRejectMessage(
            moveId,
            (PlayerFlag)playerFlagValue,
            new RectCoordinates(x, z),
            (DuelMoveRejectReason)rejectReasonValue));
    }

    private void EnqueueRejectedMove(LanDuelMoveRejectMessage move)
    {
        lock (sessionLock) {
            pendingRejectedMoves.Enqueue(move);
        }
    }

    private void HandleBoardSnapshotMessage(string[] parts)
    {
        if (parts.Length != 8 ||
            !int.TryParse(parts[1], out int boardVersion) ||
            !int.TryParse(parts[2], out int boardSize) ||
            !int.TryParse(parts[3], out int nextTurnPlayerFlagValue) ||
            !int.TryParse(parts[4], out int latestMoveX) ||
            !int.TryParse(parts[5], out int latestMoveZ) ||
            !int.TryParse(parts[6], out int latestMovePlayerFlagValue)) {
            return;
        }

        List<LanDuelBoardSnapshotStone> stones = new List<LanDuelBoardSnapshotStone>();
        string stonePayload = parts[7];
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
        EnqueueBoardSnapshot(new LanDuelBoardSnapshotMessage(
            boardVersion,
            boardSize,
            (PlayerFlag)nextTurnPlayerFlagValue,
            latestMoveCoords,
            (PlayerFlag)latestMovePlayerFlagValue,
            stones));
    }

    private void EnqueueBoardSnapshot(LanDuelBoardSnapshotMessage snapshot)
    {
        lock (sessionLock) {
            pendingBoardSnapshots.Enqueue(snapshot);
        }
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
        return $"{MsgBoardSnapshot}|{snapshot.boardVersion}|{snapshot.boardSize}|{(int)snapshot.nextTurnPlayerFlag}|{latestMoveX}|{latestMoveZ}|{(int)snapshot.latestMovePlayerFlag}|{stonesBuilder}";
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
        }
        lastStatus = "房间连接已断开。";
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

    private bool ConnectWithTimeout(TcpClient client, string hostAddress, int tcpPort)
    {
        IAsyncResult connectResult = client.BeginConnect(hostAddress, tcpPort, null, null);
        bool connected = connectResult.AsyncWaitHandle.WaitOne(ConnectTimeoutMilliseconds);
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
