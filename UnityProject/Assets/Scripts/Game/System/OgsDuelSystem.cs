using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XNClient.ChessBoard;
using XNClient.Logger;

public class OgsDuelSystem : SystemBase
{
    private const float RealtimeReconnectCooldownSeconds = 1f;
    private const float RealtimeHealthCheckIntervalSeconds = 10f;
    private const float RealtimeNoMessageReconnectSeconds = 120f;

    public override string systemName => GetSystemName<OgsDuelSystem>();

    private OgsRealtimeGameSession realtimeSession;
    private CancellationTokenSource cancellationTokenSource;
    private SceneComponentDuel compDuel;
    private SceneComponentChessBoard compChessBoard;
    private SceneComponentOgsDuel compOgsDuel;
    private ChessBoardSystem chessBoardSystem;
    private readonly List<OgsDuelInitialStone> acceptedInitialStones = new List<OgsDuelInitialStone>();
    private readonly List<OgsDuelMove> acceptedMoves = new List<OgsDuelMove>();
    private bool hasPendingUndoRequest;
    private int pendingUndoMoveNumber;
    private int pendingUndoMoveCount;
    private int pendingUndoRequesterOgsUserId;
    private bool hasOgsClock;
    private bool hasOgsStartClock;
    private bool hasOgsStoneRemovalClock;
    private float ogsClockBaseLocalTime;
    private float ogsStoneRemovalClockBaseLocalTime;
    private PlayerFlag ogsClockCurrentPlayerFlag;
    private PlayerFlag ogsStartClockPlayerFlag;
    private int ogsStartClockBaseLeftSeconds;
    private int ogsStoneRemovalClockBaseLeftSeconds;
    private OgsClockPlayerTime ogsBlackClock = OgsClockPlayerTime.Empty;
    private OgsClockPlayerTime ogsWhiteClock = OgsClockPlayerTime.Empty;
    private bool wasApplicationInBackground;
    private float lastRealtimeMessageLocalTime = -1f;
    private float lastRealtimeHealthCheckLocalTime = -1f;
    private float lastRealtimeReconnectRequestLocalTime = -1f;

    public OgsDuelSystem(SceneBase scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();

        compDuel = scene.GetComponent<SceneComponentDuel>();
        compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        compOgsDuel = scene.GetComponent<SceneComponentOgsDuel>();
        chessBoardSystem = scene.GetSystem<ChessBoardSystem>();

        scene.RegisterSystemEvent<OnSubmitOgsDuelMove>(OnSubmitOgsDuelMove);
        scene.RegisterSystemEvent<OnSubmitOgsDuelPass>(OnSubmitOgsDuelPass);
        scene.RegisterSystemEvent<OnSubmitOgsDuelResign>(OnSubmitOgsDuelResign);
        scene.RegisterSystemEvent<OnSubmitOgsDuelTakeBack>(OnSubmitOgsDuelTakeBack);
        scene.RegisterSystemEvent<OnSubmitOgsDuelTakeBackConfirm>(OnSubmitOgsDuelTakeBackConfirm);
        scene.RegisterSystemEvent<OnSubmitOgsRemovedStoneToggle>(OnSubmitOgsRemovedStoneToggle);
        scene.RegisterSystemEvent<OnSubmitOgsRemovedStonesAccept>(OnSubmitOgsRemovedStonesAccept);
        scene.RegisterSystemEvent<OnSubmitOgsRemovedStonesReject>(OnSubmitOgsRemovedStonesReject);

        wasApplicationInBackground = Global.IsApplicationInBackground;
        InitFromSceneParams();
        InitPlayers();
        ConnectAsync();
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        if (compDuel != null && compDuel.duelFSM.isActivated) {
            compDuel.duelFSM.Update();
        }

        UpdateRealtimeConnectionHealth();
        while (realtimeSession != null && realtimeSession.TryDequeueMessage(out OgsRealtimeGameMessage message)) {
            lastRealtimeMessageLocalTime = UnityEngine.Time.unscaledTime;
            HandleRealtimeMessage(message);
        }
        RefreshOgsClockDisplay();
    }

    public override void OnDestroy()
    {
        DisposeRealtimeSession();
        base.OnDestroy();
    }

    public DuelInputAuthorityState GetInputState()
    {
        if (compDuel == null || compOgsDuel == null || !CanSubmitLocalMove()) {
            return default;
        }

        return new DuelInputAuthorityState((PlayerFlag)compDuel.localInputPlayerFlag.value);
    }

    private void InitFromSceneParams()
    {
        OgsDuelSceneCreateParams ogsParams = scene.sceneCreateParams?.ogsDuelSceneCreateParams;
        if (compOgsDuel == null || ogsParams == null) {
            return;
        }

        compOgsDuel.gameId = ogsParams.gameId;
        compOgsDuel.boardSize = ogsParams.boardSize > 0 ? ogsParams.boardSize : OgsConnectionConfig.DefaultBotGameBoardSize;
        compOgsDuel.botId = ogsParams.botId;
        compOgsDuel.botName = ogsParams.botName ?? string.Empty;
        compOgsDuel.isBotGame = ogsParams.isBotGame;
        compOgsDuel.challengeId = ogsParams.challengeId;
        compOgsDuel.challengeUuid = ogsParams.challengeUuid ?? string.Empty;
        compOgsDuel.firstMovePlayerFlag = PlayerFlag.Player1;
        compOgsDuel.ogsHandicapCount = 0;
        compOgsDuel.komi = 7.5f;
        compOgsDuel.hasKomi = false;
        compOgsDuel.initialStoneCount = 0;
        compOgsDuel.openingSameColorMoveCount = 0;

        OgsSession session = Global.Instance.ogsConnectionService?.Session;
        if (session != null && int.TryParse(session.userId, out int userId)) {
            compOgsDuel.localOgsUserId = userId;
        }
    }

    private void InitPlayers()
    {
        if (compDuel == null) {
            return;
        }

        string player1Guid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Player>());
        Player player1 = EntityUtils.CreatePlayer(scene, player1Guid, PlayerFlag.Player1);
        compDuel.player1Guid.value = player1Guid;

        string player2Guid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Player>());
        Player player2 = EntityUtils.CreatePlayer(scene, player2Guid, PlayerFlag.Player2);
        compDuel.player2Guid.value = player2Guid;

        compDuel.curTurnPlayerGuid.value = player1Guid;
        compDuel.localPlayerFlag.value = 0;
        compDuel.localInputPlayerFlag.value = 0;
        compDuel.isAiDuel.value = false;
        compDuel.isLanDuel.value = false;
        compDuel.holdTimeCfgId.value = "infinite";
        compDuel.byoyomiCountCfgId.value = "off";
        compDuel.byoyomiTimeCfgId.value = "30s";
        compDuel.handicapCfgId.value = string.Empty;
        compDuel.player1DisplayName.value = "Black";
        compDuel.player2DisplayName.value = "White";
        compDuel.ResetKataGoMoves();
        InitInfiniteTimeControl(player1);
        InitInfiniteTimeControl(player2);
        compDuel.duelFSM.Activate(DuelStateDefine.STATE_TURN_INPUT);
    }

    private void InitInfiniteTimeControl(Player player)
    {
        ComponentDuelInfo duelInfo = player?.GetComponent<ComponentDuelInfo>();
        if (duelInfo == null) {
            return;
        }

        duelInfo.isInfiniteTime.value = true;
        duelInfo.holdLeftSeconds.value = -1;
        duelInfo.byoyomiLeftCount.value = 0;
        duelInfo.byoyomiLeftSeconds.value = 0;
        duelInfo.turnLeftTimes.value = -1;
    }

    private async void ConnectAsync()
    {
        await ReconnectRealtimeAsync("initial connect", true, false);
    }

    private async Task ReconnectRealtimeAsync(string reason, bool force, bool showWaitingPopup)
    {
        if (compOgsDuel == null || compOgsDuel.gameId <= 0) {
            SetError("OGS game id is empty.");
            return;
        }
        if (compOgsDuel.isConnecting) {
            return;
        }
        if (!force && realtimeSession != null && realtimeSession.IsOpen) {
            return;
        }

        float now = UnityEngine.Time.unscaledTime;
        if (lastRealtimeReconnectRequestLocalTime >= 0f &&
            now - lastRealtimeReconnectRequestLocalTime < RealtimeReconnectCooldownSeconds) {
            return;
        }
        lastRealtimeReconnectRequestLocalTime = now;

        if (showWaitingPopup) {
            scene.EmitSystemEvent(new OnOgsDuelReconnectWaiting(reason));
        }
        DisposeRealtimeSession();
        var connectCancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource = connectCancellationTokenSource;
        compOgsDuel.isConnecting = true;
        compOgsDuel.isConnected = false;
        compOgsDuel.isSubmitting = false;
        compOgsDuel.isSubmittingRemovedStones = false;
        SyncInputAuthority();
        try {
            OgsRealtimeGameSession newSession = await Global.Instance.ogsConnectionService.CreateRealtimeGameSessionAsync(
                compOgsDuel.gameId,
                OgsConnectionConfig.DefaultWebSocketUrl,
                connectCancellationTokenSource.Token);
            if (connectCancellationTokenSource.IsCancellationRequested || cancellationTokenSource != connectCancellationTokenSource) {
                newSession.Dispose();
                return;
            }

            realtimeSession = newSession;
            compOgsDuel.isConnected = true;
            compOgsDuel.lastError = string.Empty;
            lastRealtimeMessageLocalTime = UnityEngine.Time.unscaledTime;
            XNLogger.LogInfo(
                "OGS duel realtime connected.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("reason", reason ?? string.Empty));
            if (showWaitingPopup) {
                scene.EmitSystemEvent(new OnOgsDuelReconnected());
            }
        }
        catch (OperationCanceledException) {
        }
        catch (Exception ex) {
            SetError(ex.Message);
            XNLogger.LogError(
                "OGS duel realtime connect failed.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("reason", reason ?? string.Empty),
                ("err", ex.Message));
        }
        finally {
            if (compOgsDuel != null && cancellationTokenSource == connectCancellationTokenSource) {
                compOgsDuel.isConnecting = false;
                SyncInputAuthority();
            }
        }
    }

    private void UpdateRealtimeConnectionHealth()
    {
        bool isInBackground = Global.IsApplicationInBackground;
        if (wasApplicationInBackground && !isInBackground) {
            RequestRealtimeReconnect("foreground resume", true);
        }
        wasApplicationInBackground = isInBackground;

        if (isInBackground || compOgsDuel == null || compOgsDuel.gameId <= 0 || compOgsDuel.isConnecting) {
            return;
        }

        float now = UnityEngine.Time.unscaledTime;
        if (lastRealtimeHealthCheckLocalTime >= 0f &&
            now - lastRealtimeHealthCheckLocalTime < RealtimeHealthCheckIntervalSeconds) {
            return;
        }
        lastRealtimeHealthCheckLocalTime = now;

        if (realtimeSession == null || !realtimeSession.IsOpen) {
            RequestRealtimeReconnect("realtime socket closed", true);
            return;
        }

        if (lastRealtimeMessageLocalTime >= 0f &&
            now - lastRealtimeMessageLocalTime >= RealtimeNoMessageReconnectSeconds) {
            RequestRealtimeReconnect("realtime message timeout", true);
        }
    }

    private void RequestRealtimeReconnect(string reason, bool force)
    {
        if (Global.IsApplicationInBackground || compOgsDuel == null || compOgsDuel.isConnecting) {
            return;
        }

        _ = ReconnectRealtimeAsync(reason, force, true);
    }

    private void DisposeRealtimeSession()
    {
        CancellationTokenSource oldCancellationTokenSource = cancellationTokenSource;
        OgsRealtimeGameSession oldSession = realtimeSession;
        cancellationTokenSource = null;
        realtimeSession = null;

        oldCancellationTokenSource?.Cancel();
        if (oldSession != null) {
            _ = oldSession.DisconnectAsync();
            oldSession.Dispose();
        }
        oldCancellationTokenSource?.Dispose();
    }

    private void HandleRealtimeMessage(OgsRealtimeGameMessage message)
    {
        if (message == null || compOgsDuel == null) {
            return;
        }

        switch (message.messageType) {
            case OgsRealtimeGameMessageType.GameData:
                ApplyGameData(message.payload as JObject);
                break;
            case OgsRealtimeGameMessageType.Move:
                ApplyIncrementalMove(message.payload as JObject);
                break;
            case OgsRealtimeGameMessageType.Clock:
                ApplyClock(message.payload as JObject);
                break;
            case OgsRealtimeGameMessageType.Phase:
                ApplyPhase(message.payload?.ToString());
                break;
            case OgsRealtimeGameMessageType.UndoAccepted:
                ApplyUndoAccepted(message.payload);
                break;
            case OgsRealtimeGameMessageType.UndoCanceled:
                ApplyUndoCanceled(message.payload);
                break;
            case OgsRealtimeGameMessageType.UndoRequested:
                ApplyUndoRequested(message.payload);
                break;
            case OgsRealtimeGameMessageType.RemovedStones:
                ApplyRemovedStones(message.payload);
                break;
            case OgsRealtimeGameMessageType.RemovedStonesAccepted:
                ApplyRemovedStonesAccepted(message.payload);
                break;
            case OgsRealtimeGameMessageType.Error:
                SetError(message.message);
                break;
            case OgsRealtimeGameMessageType.Closed:
                compOgsDuel.isConnected = false;
                if (!string.IsNullOrEmpty(message.message)) {
                    compOgsDuel.lastError = message.message;
                }
                SyncInputAuthority();
                RequestRealtimeReconnect("realtime closed message", true);
                break;
        }
    }

    public bool CanSubmitResign()
    {
        return CanSubmitOgsCommand();
    }

    public async Task<bool> SubmitInterruptResignAsync()
    {
        if (compDuel == null || compOgsDuel == null || compDuel.localPlayerFlag.value == 0 || IsFinishedPhase()) {
            return false;
        }

        return await SubmitResignAsync();
    }

    public bool CanSubmitTakeBack()
    {
        return CanSubmitOgsCommand()
            && !compOgsDuel.isBotGame
            && !hasPendingUndoRequest
            && compOgsDuel.acceptedMoveCount > 0
            && !IsStoneRemovalPhase();
    }

    public bool IsInStoneRemovalPhase()
    {
        return IsStoneRemovalPhase();
    }

    public bool CanSubmitStoneRemovalCommand()
    {
        return CanSubmitBaseOgsCommand()
            && IsStoneRemovalPhase()
            && !compOgsDuel.isSubmittingRemovedStones
            && compDuel.localPlayerFlag.value != 0;
    }

    public bool IsRemovedStone(RectCoordinates coords)
    {
        if (compChessBoard == null || coords == null || compOgsDuel == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        return posIndex >= 0 && compOgsDuel.removedStonePosIndexes.Contains(posIndex);
    }

    public int GetStoneRemovalCountdownSeconds()
    {
        if (!IsStoneRemovalPhase() || !hasOgsStoneRemovalClock) {
            return -1;
        }

        float elapsedSeconds = Math.Max(0f, UnityEngine.Time.unscaledTime - ogsStoneRemovalClockBaseLocalTime);
        return CeilRemainingSeconds(ogsStoneRemovalClockBaseLeftSeconds - elapsedSeconds);
    }

    private void ApplyGameData(JObject gameData)
    {
        if (gameData == null || compOgsDuel == null || compDuel == null || compChessBoard == null || chessBoardSystem == null) {
            return;
        }

        compOgsDuel.lastGameData = gameData.DeepClone();
        int width = ReadFirstInt(gameData, compOgsDuel.boardSize, "width", "board_width", "size");
        int height = ReadFirstInt(gameData, width, "height", "board_height", "size");
        if (width > 0 && width == height) {
            compOgsDuel.boardSize = width;
        }

        ApplyPlayerData(gameData);
        string phase = ReadFirstString(gameData, "phase", "state", "game_state");
        if (!string.IsNullOrEmpty(phase)) {
            compOgsDuel.phase = phase;
        }
        ApplyClock(gameData["clock"] as JObject);
        ApplyStoneRemovalData(gameData);
        ApplyOgsKomi(gameData);
        if (!TryResolveOgsInitialStones(gameData, out List<OgsDuelInitialStone> initialStones)) {
            SetError("OGS game data initial stones could not be parsed.");
            return;
        }

        compOgsDuel.firstMovePlayerFlag = ResolveOgsFirstMovePlayerFlag(gameData, compOgsDuel.ogsHandicapCount, initialStones.Count);
        compOgsDuel.openingSameColorMoveCount = ResolveOgsOpeningSameColorMoveCount(gameData, initialStones.Count);
        compOgsDuel.initialStoneCount = initialStones.Count;
        if (!OgsPackedMoveCodec.TryParseMoves(
            gameData["moves"],
            compOgsDuel.boardSize,
            compOgsDuel.firstMovePlayerFlag,
            compOgsDuel.openingSameColorMoveCount,
            out List<OgsDuelMove> moves)) {
            SetError("OGS game data moves could not be parsed.");
            return;
        }

        acceptedInitialStones.Clear();
        acceptedInitialStones.AddRange(initialStones);
        acceptedMoves.Clear();
        acceptedMoves.AddRange(moves);
        compOgsDuel.kataGoInitialStones = BuildKataGoInitialStones(initialStones);
        if (!RebuildBoardFromMoves(acceptedInitialStones, acceptedMoves)) {
            SetError("OGS game data board state could not be applied.");
            return;
        }
        RefreshStoneRemovalVisuals();
        if (IsStoneRemovalPhase()) {
            RequestStoneRemovalOwnershipPreview();
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
        }
        compOgsDuel.acceptedMoveCount = moves.Count;
        compOgsDuel.isSubmitting = false;
        if (TryBuildOgsScoreResult(gameData, out DuelScoreResult scoreResult)) {
            EndGameByOgsScore(scoreResult, gameData);
        } else if (IsFinishedPhase()) {
            EndGameByOgsFinishedFallback(gameData);
        } else {
            SyncTurnAndInputFromMoveCount(moves.Count, ResolveOgsCurrentPlayerFlag(gameData));
        }
        XNLogger.LogInfo(
            "OGS game data applied.",
            ("gameId", compOgsDuel.gameId.ToString()),
            ("moves", moves.Count.ToString()),
            ("phase", compOgsDuel.phase ?? string.Empty),
            ("handicap", compOgsDuel.ogsHandicapCount.ToString()),
            ("initialStones", compOgsDuel.initialStoneCount.ToString()),
            ("firstMovePlayerFlag", compOgsDuel.firstMovePlayerFlag.ToString()),
            ("openingSameColorMoveCount", compOgsDuel.openingSameColorMoveCount.ToString()),
            ("localOgsUserId", compOgsDuel.localOgsUserId.ToString()),
            ("blackOgsUserId", compOgsDuel.blackOgsUserId.ToString()),
            ("whiteOgsUserId", compOgsDuel.whiteOgsUserId.ToString()),
            ("localPlayerFlag", compDuel.localPlayerFlag.value.ToString()));
    }

    private void ApplyIncrementalMove(JObject moveData)
    {
        if (moveData == null || compOgsDuel == null || compDuel == null || chessBoardSystem == null) {
            return;
        }
        ApplyClock(moveData["clock"] as JObject);

        int serverMoveNumber = ReadFirstInt(moveData, compOgsDuel.acceptedMoveCount + 1, "move_number", "moveNumber");
        int acceptedMoveNumber = NormalizeOgsMoveNumber(serverMoveNumber);
        if (acceptedMoveNumber <= compOgsDuel.acceptedMoveCount) {
            compOgsDuel.isSubmitting = false;
            SyncInputAuthority();
            return;
        }

        if (acceptedMoveNumber != compOgsDuel.acceptedMoveCount + 1) {
            XNLogger.LogWarn(
                "OGS move skipped until next gamedata because move number is not contiguous.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("localMoveCount", compOgsDuel.acceptedMoveCount.ToString()),
                ("serverMoveNumber", serverMoveNumber.ToString()),
                ("acceptedMoveNumber", acceptedMoveNumber.ToString()),
                ("initialStones", compOgsDuel.initialStoneCount.ToString()));
            return;
        }

        if (!OgsPackedMoveCodec.TryParseIncrementalMove(
            moveData["move"],
            compOgsDuel.boardSize,
            acceptedMoveNumber,
            compOgsDuel.firstMovePlayerFlag,
            compOgsDuel.openingSameColorMoveCount,
            out OgsDuelMove move)) {
            SetError("OGS incremental move could not be parsed.");
            return;
        }

        if (!ApplyAcceptedMove(move, true)) {
            SetError("OGS incremental move could not be applied.");
            return;
        }

        compOgsDuel.acceptedMoveCount = acceptedMoveNumber;
        acceptedMoves.Add(move);
        compOgsDuel.isSubmitting = false;
        SyncTurnAndInputFromMoveCount(compOgsDuel.acceptedMoveCount);
    }

    private bool TryResolveOgsInitialStones(JObject gameData, out List<OgsDuelInitialStone> initialStones)
    {
        initialStones = new List<OgsDuelInitialStone>();
        if (gameData == null || compOgsDuel == null) {
            return true;
        }

        compOgsDuel.ogsHandicapCount = Math.Max(0, ReadFirstInt(gameData, 0, "handicap", "handicap_stones", "handicapStones"));
        JToken initialStateToken = ReadFirstToken(
            gameData,
            "initial_state",
            "initialState",
            "initial_stones",
            "initialStones",
            "initial_board",
            "initialBoard");
        if (initialStateToken != null &&
            !OgsPackedMoveCodec.TryParseInitialStones(initialStateToken, compOgsDuel.boardSize, out initialStones)) {
            return false;
        }

        return true;
    }

    private void ApplyOgsKomi(JObject gameData)
    {
        if (gameData == null || compOgsDuel == null) {
            return;
        }

        JToken komiToken = ReadFirstToken(gameData, "komi");
        if (komiToken == null) {
            JObject payload = GetOgsTerminalPayload(gameData);
            if (payload != gameData) {
                komiToken = ReadFirstToken(payload, "komi");
            }
        }

        if (komiToken != null && float.TryParse(komiToken.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float komi)) {
            compOgsDuel.komi = komi;
            compOgsDuel.hasKomi = true;
        }
    }

    private PlayerFlag ResolveOgsFirstMovePlayerFlag(JObject gameData, int handicapCount, int initialStoneCount)
    {
        PlayerFlag initialPlayerFlag = ResolveOgsInitialPlayerFlag(gameData);
        if (ResolveOgsOpeningSameColorMoveCount(gameData, initialStoneCount) > 0) {
            return initialPlayerFlag;
        }

        return initialStoneCount > 0 || handicapCount > 1
            ? PlayerFlag.Player2
            : initialPlayerFlag;
    }

    private int ResolveOgsOpeningSameColorMoveCount(JObject gameData, int initialStoneCount)
    {
        if (gameData == null || compOgsDuel == null || initialStoneCount > 0) {
            return 0;
        }

        bool freeHandicapPlacement = ReadFirstBool(gameData, false, "free_handicap_placement", "freeHandicapPlacement");
        return freeHandicapPlacement && compOgsDuel.ogsHandicapCount > 1
            ? compOgsDuel.ogsHandicapCount
            : 0;
    }

    private PlayerFlag ResolveOgsInitialPlayerFlag(JObject gameData)
    {
        string initialPlayer = ReadFirstString(gameData, "initial_player", "initialPlayer");
        return TryResolvePlayerFlagFromText(initialPlayer, out PlayerFlag playerFlag)
            ? playerFlag
            : PlayerFlag.Player1;
    }

    private int NormalizeOgsMoveNumber(int serverMoveNumber)
    {
        if (compOgsDuel == null || compOgsDuel.initialStoneCount <= 0) {
            return serverMoveNumber;
        }

        int expectedAcceptedMoveNumber = compOgsDuel.acceptedMoveCount + 1;
        int withoutInitialStones = serverMoveNumber - compOgsDuel.initialStoneCount;
        return withoutInitialStones == expectedAcceptedMoveNumber
            ? expectedAcceptedMoveNumber
            : serverMoveNumber;
    }

    private void ApplyPhase(string phase)
    {
        if (compOgsDuel == null || compDuel == null) {
            return;
        }

        compOgsDuel.phase = phase ?? string.Empty;
        if (IsFinishedPhase()) {
            ClearStoneRemovalState(false);
            JObject gameData = compOgsDuel.lastGameData as JObject;
            if (TryBuildOgsScoreResult(gameData, out DuelScoreResult scoreResult)) {
                EndGameByOgsScore(scoreResult, gameData);
            } else {
                EndGameByOgsFinishedFallback(gameData);
            }
        } else {
            if (!IsStoneRemovalPhase()) {
                ClearStoneRemovalState(true);
            } else {
                RefreshStoneRemovalVisuals();
                RequestStoneRemovalOwnershipPreview();
                scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
            }
            SyncInputAuthority();
        }
    }

    private void ApplyClock(JObject clock)
    {
        if (clock == null || compDuel == null) {
            return;
        }

        ogsBlackClock = ReadOgsClockPlayerTime(clock["black_time"] as JObject);
        ogsWhiteClock = ReadOgsClockPlayerTime(clock["white_time"] as JObject);
        ogsClockCurrentPlayerFlag = ResolveClockCurrentPlayerFlag(clock);
        hasOgsStartClock = ReadFirstBool(clock, false, "start_mode", "startMode") &&
            TryReadOgsStartClockLeftSeconds(clock, out ogsStartClockBaseLeftSeconds);
        hasOgsStoneRemovalClock = IsStoneRemovalPhase() &&
            TryReadOgsStoneRemovalClockLeftSeconds(clock, out ogsStoneRemovalClockBaseLeftSeconds);
        ogsStartClockPlayerFlag = ResolveOgsStartClockPlayerFlag();
        ogsClockBaseLocalTime = UnityEngine.Time.unscaledTime;
        ogsStoneRemovalClockBaseLocalTime = UnityEngine.Time.unscaledTime;
        hasOgsClock = true;

        bool hasByoyomi = !hasOgsStartClock && (ogsBlackClock.HasByoyomi || ogsWhiteClock.HasByoyomi);
        compDuel.byoyomiCountCfgId.value = hasByoyomi ? "1" : "off";
        RefreshOgsClockDisplay();
    }

    private void RefreshOgsClockDisplay()
    {
        if (!hasOgsClock || compDuel == null) {
            return;
        }

        float elapsedSeconds = IsFinishedPhase() || IsPausedPhase()
            ? 0f
            : Math.Max(0f, UnityEngine.Time.unscaledTime - ogsClockBaseLocalTime);
        bool useStartClock = hasOgsStartClock && ogsStartClockPlayerFlag != 0;
        ApplyOgsClockToPlayer(PlayerFlag.Player1, ogsBlackClock, !useStartClock && ogsClockCurrentPlayerFlag == PlayerFlag.Player1, elapsedSeconds);
        ApplyOgsClockToPlayer(PlayerFlag.Player2, ogsWhiteClock, !useStartClock && ogsClockCurrentPlayerFlag == PlayerFlag.Player2, elapsedSeconds);
        if (useStartClock) {
            ApplyOgsStartClockToPlayer(ogsStartClockPlayerFlag, CeilRemainingSeconds(ogsStartClockBaseLeftSeconds - elapsedSeconds));
        }
    }

    private void ApplyOgsClockToPlayer(PlayerFlag playerFlag, OgsClockPlayerTime baseTime, bool isCurrentPlayer, float elapsedSeconds)
    {
        Player player = GetPlayerByFlag(playerFlag);
        ComponentDuelInfo duelInfo = player?.GetComponent<ComponentDuelInfo>();
        if (duelInfo == null) {
            return;
        }

        int holdLeftSeconds = baseTime.holdLeftSeconds;
        int byoyomiLeftSeconds = baseTime.byoyomiLeftSeconds;
        int byoyomiLeftCount = baseTime.byoyomiLeftCount;
        bool isInByoyomi = baseTime.isInByoyomi;

        if (isCurrentPlayer && elapsedSeconds > 0f) {
            if (isInByoyomi) {
                byoyomiLeftSeconds = CeilRemainingSeconds(baseTime.byoyomiLeftSeconds - elapsedSeconds);
            } else {
                holdLeftSeconds = CeilRemainingSeconds(baseTime.holdLeftSeconds - elapsedSeconds);
                if (holdLeftSeconds <= 0 && byoyomiLeftCount > 0 && baseTime.byoyomiLeftSeconds > 0) {
                    float byoyomiElapsedSeconds = elapsedSeconds - baseTime.holdLeftSeconds;
                    isInByoyomi = byoyomiElapsedSeconds > 0f;
                    byoyomiLeftSeconds = isInByoyomi
                        ? CeilRemainingSeconds(baseTime.byoyomiLeftSeconds - byoyomiElapsedSeconds)
                        : baseTime.byoyomiLeftSeconds;
                }
            }
        }

        duelInfo.isInfiniteTime.value = false;
        duelInfo.holdLeftSeconds.value = Math.Max(0, holdLeftSeconds);
        duelInfo.byoyomiLeftCount.value = Math.Max(0, byoyomiLeftCount);
        duelInfo.byoyomiLeftSeconds.value = Math.Max(0, byoyomiLeftSeconds);
        duelInfo.isInByoyomi.value = isInByoyomi;
        duelInfo.turnLeftTimes.value = isInByoyomi
            ? duelInfo.byoyomiLeftSeconds.value
            : duelInfo.holdLeftSeconds.value;
    }

    private void ApplyOgsStartClockToPlayer(PlayerFlag playerFlag, int leftSeconds)
    {
        Player player = GetPlayerByFlag(playerFlag);
        ComponentDuelInfo duelInfo = player?.GetComponent<ComponentDuelInfo>();
        if (duelInfo == null) {
            return;
        }

        int safeLeftSeconds = Math.Max(0, leftSeconds);
        duelInfo.isInfiniteTime.value = false;
        duelInfo.holdLeftSeconds.value = safeLeftSeconds;
        duelInfo.byoyomiLeftCount.value = 0;
        duelInfo.byoyomiLeftSeconds.value = 0;
        duelInfo.isInByoyomi.value = false;
        duelInfo.turnLeftTimes.value = safeLeftSeconds;
    }

    private PlayerFlag ResolveClockCurrentPlayerFlag(JObject clock)
    {
        if (TryResolveOgsCurrentPlayerFlag(clock, true, out PlayerFlag playerFlag)) {
            return playerFlag;
        }

        Player currentTurnPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        return currentTurnPlayer != null
            ? (PlayerFlag)currentTurnPlayer.playerFlag.value
            : 0;
    }

    private PlayerFlag ResolveOgsCurrentPlayerFlag(JObject gameData)
    {
        if (TryResolveOgsCurrentPlayerFlag(gameData, false, out PlayerFlag playerFlag)) {
            return playerFlag;
        }

        if (TryResolveOgsCurrentPlayerFlag(gameData?["clock"] as JObject, true, out playerFlag)) {
            return playerFlag;
        }

        return 0;
    }

    private bool TryResolveOgsCurrentPlayerFlag(JObject json, bool includeClockPlayerFields, out PlayerFlag playerFlag)
    {
        playerFlag = 0;
        string currentPlayer = ReadFirstString(
            json,
            "current_player",
            "currentPlayer",
            "current_player_id",
            "currentPlayerId",
            "player_to_move",
            "playerToMove",
            "to_move",
            "toMove",
            "next_player",
            "nextPlayer",
            "next_player_id",
            "nextPlayerId");
        if (string.IsNullOrWhiteSpace(currentPlayer) && includeClockPlayerFields) {
            currentPlayer = ReadFirstString(json, "player_id", "playerId");
        }
        if (TryResolvePlayerFlagFromText(currentPlayer, out playerFlag)) {
            return true;
        }

        if (!int.TryParse(currentPlayer, out int currentPlayerId) || compOgsDuel == null) {
            return false;
        }

        if (currentPlayerId == compOgsDuel.blackOgsUserId) {
            playerFlag = PlayerFlag.Player1;
            return true;
        }
        if (currentPlayerId == compOgsDuel.whiteOgsUserId) {
            playerFlag = PlayerFlag.Player2;
            return true;
        }

        return false;
    }

    private PlayerFlag ResolveOgsStartClockPlayerFlag()
    {
        if (ogsClockCurrentPlayerFlag == PlayerFlag.Player1 || ogsClockCurrentPlayerFlag == PlayerFlag.Player2) {
            return ogsClockCurrentPlayerFlag;
        }
        if (compOgsDuel != null && (compOgsDuel.firstMovePlayerFlag == PlayerFlag.Player1 || compOgsDuel.firstMovePlayerFlag == PlayerFlag.Player2)) {
            return compOgsDuel.firstMovePlayerFlag;
        }
        return PlayerFlag.Player1;
    }

    private static OgsClockPlayerTime ReadOgsClockPlayerTime(JObject playerClock)
    {
        if (playerClock == null) {
            return OgsClockPlayerTime.Empty;
        }

        double thinkingTime = ReadOgsClockDurationSeconds(playerClock);
        if (thinkingTime <= 0d) {
            thinkingTime = ReadFirstDouble(playerClock, 0d, "thinking_time", "thinkingTime", "main_time", "mainTime", "time");
        }
        int byoyomiLeftCount = Math.Max(0, ReadFirstInt(playerClock, 0, "periods", "periods_left", "periodsLeft"));
        int byoyomiLeftSeconds = CeilRemainingSeconds(ReadFirstDouble(
            playerClock,
            0d,
            "period_time",
            "periodTime",
            "byoyomi_time",
            "byoyomiTime",
            "current_period_time",
            "currentPeriodTime"));
        bool isInByoyomi = ReadFirstBool(playerClock, false, "is_in_byoyomi", "isInByoyomi", "in_byoyomi", "inByoyomi") ||
            (thinkingTime <= 0d && (byoyomiLeftCount > 0 || byoyomiLeftSeconds > 0));
        int holdLeftSeconds = isInByoyomi ? 0 : CeilRemainingSeconds(thinkingTime);

        return new OgsClockPlayerTime(
            holdLeftSeconds,
            byoyomiLeftCount,
            byoyomiLeftSeconds,
            isInByoyomi);
    }

    private static bool TryReadOgsStartClockLeftSeconds(JObject clock, out int leftSeconds)
    {
        leftSeconds = 0;
        if (clock == null) {
            return false;
        }

        double deltaSeconds = ReadOgsClockDurationSeconds(clock);
        if (deltaSeconds > 0d) {
            leftSeconds = CeilRemainingSeconds(deltaSeconds);
            return true;
        }

        double expirationMillis = ReadOgsUnixMillis(clock, "expiration", "expires_at", "expiresAt");
        if (expirationMillis <= 0d) {
            return false;
        }

        double nowMillis = ReadOgsUnixMillis(clock, "now", "server_time", "serverTime", "current_time", "currentTime");
        if (nowMillis <= 0d) {
            nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        leftSeconds = CeilRemainingSeconds((expirationMillis - nowMillis) / 1000d);
        return true;
    }

    private static bool TryReadOgsStoneRemovalClockLeftSeconds(JObject clock, out int leftSeconds)
    {
        leftSeconds = 0;
        if (clock == null) {
            return false;
        }

        double directSeconds = ReadOgsClockMillisecondsAsSeconds(
            clock,
            "stone_removal_time_left",
            "stoneRemovalTimeLeft");
        if (directSeconds >= 0d) {
            leftSeconds = CeilRemainingSeconds(directSeconds);
            return true;
        }

        double expirationMillis = ReadOgsUnixMillis(
            clock,
            "stone_removal_expiration",
            "stoneRemovalExpiration",
            "stone_removal_expires_at",
            "stoneRemovalExpiresAt");
        if (expirationMillis <= 0d && IsOgsStoneRemovalClockPause(clock)) {
            expirationMillis = ReadOgsUnixMillis(clock, "expiration", "expires_at", "expiresAt");
        }
        if (expirationMillis <= 0d) {
            return false;
        }

        double nowMillis = ReadOgsUnixMillis(clock, "now", "server_time", "serverTime", "current_time", "currentTime");
        if (nowMillis <= 0d) {
            nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        leftSeconds = CeilRemainingSeconds((expirationMillis - nowMillis) / 1000d);
        return true;
    }

    private static bool IsOgsStoneRemovalClockPause(JObject clock)
    {
        if (clock == null) {
            return false;
        }

        if (HasOgsTruthyField(clock["pause_state"] as JObject, "stone_removal", "stoneRemoval", "stone-removal")) {
            return true;
        }

        JObject pause = clock["pause"] as JObject;
        if (HasOgsTruthyField(pause?["pause_control"] as JObject, "stone-removal", "stone_removal", "stoneRemoval")) {
            return true;
        }

        return HasOgsTruthyField(clock["pause_control"] as JObject, "stone-removal", "stone_removal", "stoneRemoval");
    }

    private static bool HasOgsTruthyField(JObject json, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return false;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token == null || token.Type == JTokenType.Null) {
                continue;
            }

            if (token.Type == JTokenType.Boolean) {
                return token.Value<bool>();
            }

            string value = token.ToString();
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (bool.TryParse(value, out bool boolValue)) {
                return boolValue;
            }

            return true;
        }

        return false;
    }

    private static double ReadOgsUnixMillis(JObject json, params string[] fieldNames)
    {
        double value = ReadFirstDouble(json, 0d, fieldNames);
        if (value <= 0d) {
            return 0d;
        }
        return value < 100000000000d ? value * 1000d : value;
    }

    private static double ReadOgsClockDurationSeconds(JObject json)
    {
        double expirationDeltaMillis = ReadFirstDouble(json, 0d, "expiration_delta", "expirationDelta");
        if (expirationDeltaMillis > 0d) {
            return expirationDeltaMillis / 1000d;
        }

        return ReadFirstDouble(json, 0d, "time_left", "timeLeft");
    }

    private static double ReadOgsClockDurationSeconds(JObject json, params string[] fieldNames)
    {
        double value = ReadFirstDouble(json, -1d, fieldNames);
        if (value < 0d) {
            return -1d;
        }

        return value > 10000d ? value / 1000d : value;
    }

    private static double ReadOgsClockMillisecondsAsSeconds(JObject json, params string[] fieldNames)
    {
        double value = ReadFirstDouble(json, -1d, fieldNames);
        return value < 0d ? -1d : value / 1000d;
    }

    private static int CeilRemainingSeconds(double seconds)
    {
        return Math.Max(0, (int)Math.Ceiling(seconds));
    }

    private void ApplyStoneRemovalData(JObject gameData)
    {
        if (gameData == null || compOgsDuel == null || !IsStoneRemovalPhase()) {
            return;
        }

        JToken removedToken = ReadFirstToken(
            gameData,
            "removed_stones",
            "removedStones",
            "removed",
            "stones");
        if (removedToken == null) {
            return;
        }

        if (TryReadRemovedStoneSet(removedToken, out HashSet<int> removedSet, out string stonesText)) {
            SetRemovedStoneSet(removedSet, stonesText);
        }
    }

    private void ApplyRemovedStones(JToken payload)
    {
        if (compOgsDuel == null || compChessBoard == null) {
            return;
        }

        bool isFullReplacement = false;
        bool removed = true;
        JToken stonesToken = payload;
        bool strictSekiMode = compOgsDuel.strictSekiMode;
        if (payload is JObject obj) {
            strictSekiMode = ReadFirstBool(obj, strictSekiMode, "strict_seki_mode", "strictSekiMode");
            if (TryReadFullRemovedStoneToken(obj, out JToken fullToken)) {
                isFullReplacement = true;
                stonesToken = fullToken;
            } else {
                stonesToken = ReadFirstToken(obj, "stones", "stone", "removed", "removed_stones", "removedStones") ?? payload;
                removed = ReadFirstBool(obj, true, "removed", "is_removed", "isRemoved");
            }
        }

        if (!TryReadRemovedStoneSet(stonesToken, out HashSet<int> changedSet, out string stonesText)) {
            XNLogger.LogWarn(
                "OGS removed stones payload could not be parsed.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("payload", payload?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty));
            return;
        }

        compOgsDuel.strictSekiMode = strictSekiMode;
        if (isFullReplacement || payload is JArray || payload?.Type == JTokenType.String) {
            SetRemovedStoneSet(changedSet, stonesText);
        } else {
            foreach (int posIndex in changedSet) {
                if (removed) {
                    compOgsDuel.removedStonePosIndexes.Add(posIndex);
                } else {
                    compOgsDuel.removedStonePosIndexes.Remove(posIndex);
                }
            }
            compOgsDuel.removedStones = BuildRemovedStoneString(compOgsDuel.removedStonePosIndexes);
            ResetRemovedStoneAcceptancesIfNeeded();
        }

        RefreshStoneRemovalVisuals();
        RequestStoneRemovalOwnershipPreview();
        scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
    }

    private void ApplyRemovedStonesAccepted(JToken payload)
    {
        if (compOgsDuel == null) {
            return;
        }

        string stones = ReadRemovedStonesText(payload);
        bool strictSekiMode = payload is JObject obj
            ? ReadFirstBool(obj, compOgsDuel.strictSekiMode, "strict_seki_mode", "strictSekiMode")
            : compOgsDuel.strictSekiMode;
        int playerId = ReadOgsPlayerId(payload);
        bool isLocal = playerId > 0 && playerId == compOgsDuel.localOgsUserId;
        bool isOpponent = playerId > 0 && playerId != compOgsDuel.localOgsUserId;
        if (playerId <= 0) {
            isLocal = true;
        }

        compOgsDuel.strictSekiMode = strictSekiMode;
        string normalizedStones = NormalizeRemovedStonesText(stones);
        if (isLocal) {
            compOgsDuel.localRemovedStonesAccepted = true;
            compOgsDuel.localAcceptedRemovedStones = normalizedStones;
        }
        if (isOpponent) {
            compOgsDuel.opponentRemovedStonesAccepted = true;
            compOgsDuel.opponentAcceptedRemovedStones = normalizedStones;
        }

        scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
    }

    private void SetRemovedStoneSet(HashSet<int> removedSet, string stonesText)
    {
        if (compOgsDuel == null) {
            return;
        }

        compOgsDuel.removedStonePosIndexes.Clear();
        if (removedSet != null) {
            foreach (int posIndex in removedSet) {
                compOgsDuel.removedStonePosIndexes.Add(posIndex);
            }
        }
        compOgsDuel.removedStones = string.IsNullOrWhiteSpace(stonesText)
            ? BuildRemovedStoneString(compOgsDuel.removedStonePosIndexes)
            : NormalizeRemovedStonesText(stonesText);
        ResetRemovedStoneAcceptancesIfNeeded();
    }

    private void ClearStoneRemovalState(bool emitChanged)
    {
        if (compOgsDuel == null) {
            return;
        }

        bool hadState = compOgsDuel.removedStonePosIndexes.Count > 0 ||
            compOgsDuel.localRemovedStonesAccepted ||
            compOgsDuel.opponentRemovedStonesAccepted;
        compOgsDuel.removedStonePosIndexes.Clear();
        compOgsDuel.removedStones = string.Empty;
        compOgsDuel.localRemovedStonesAccepted = false;
        compOgsDuel.opponentRemovedStonesAccepted = false;
        compOgsDuel.localAcceptedRemovedStones = string.Empty;
        compOgsDuel.opponentAcceptedRemovedStones = string.Empty;
        compOgsDuel.isSubmittingRemovedStones = false;
        hasOgsStoneRemovalClock = false;
        RefreshStoneRemovalVisuals();
        scene.EmitSystemEvent(new OnRequestClearDuelOwnership());
        if (emitChanged && hadState) {
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
        }
    }

    private void RefreshStoneRemovalVisuals()
    {
        compChessBoard?.GetStoneViewCache().ApplyRemovedStoneVisuals(compOgsDuel?.removedStonePosIndexes);
    }

    private void RequestStoneRemovalOwnershipPreview()
    {
        if (!IsStoneRemovalPhase()) {
            return;
        }

        scene.EmitSystemEvent(new OnRequestDuelOwnership(compOgsDuel?.removedStonePosIndexes, false));
    }

    private void ResetRemovedStoneAcceptancesIfNeeded()
    {
        if (compOgsDuel == null) {
            return;
        }

        string current = NormalizeRemovedStonesText(compOgsDuel.removedStones);
        if (compOgsDuel.localRemovedStonesAccepted &&
            !string.Equals(NormalizeRemovedStonesText(compOgsDuel.localAcceptedRemovedStones), current, StringComparison.Ordinal)) {
            compOgsDuel.localRemovedStonesAccepted = false;
            compOgsDuel.localAcceptedRemovedStones = string.Empty;
        }
        if (compOgsDuel.opponentRemovedStonesAccepted &&
            !string.Equals(NormalizeRemovedStonesText(compOgsDuel.opponentAcceptedRemovedStones), current, StringComparison.Ordinal)) {
            compOgsDuel.opponentRemovedStonesAccepted = false;
            compOgsDuel.opponentAcceptedRemovedStones = string.Empty;
        }
    }

    private bool TryCollectStoneGroup(RectCoordinates startCoords, out HashSet<int> group)
    {
        group = new HashSet<int>();
        if (compChessBoard == null || startCoords == null) {
            return false;
        }

        int startPosIndex = compChessBoard.GetPosIndexByCoords(startCoords);
        if (startPosIndex < 0 ||
            !compChessBoard.chessInfoDict.TryGetValue(startPosIndex.ToString(), out ChessInfo startInfo) ||
            startInfo == null) {
            return false;
        }

        PlayerFlag playerFlag = (PlayerFlag)startInfo.chessFlag.value;
        if (playerFlag != PlayerFlag.Player1 && playerFlag != PlayerFlag.Player2) {
            return false;
        }

        Queue<int> pending = new Queue<int>();
        pending.Enqueue(startPosIndex);
        group.Add(startPosIndex);
        while (pending.Count > 0) {
            int posIndex = pending.Dequeue();
            RectCoordinates coords = compChessBoard.GetCoordsByPosIndex(posIndex);
            AddGroupNeighbor(coords.x - 1, coords.z, playerFlag, group, pending);
            AddGroupNeighbor(coords.x + 1, coords.z, playerFlag, group, pending);
            AddGroupNeighbor(coords.x, coords.z - 1, playerFlag, group, pending);
            AddGroupNeighbor(coords.x, coords.z + 1, playerFlag, group, pending);
        }
        return group.Count > 0;
    }

    private void AddGroupNeighbor(int x, int z, PlayerFlag playerFlag, HashSet<int> group, Queue<int> pending)
    {
        RectCoordinates coords = new RectCoordinates(x, z);
        int posIndex = compChessBoard.GetPosIndexByCoords(coords);
        if (posIndex < 0 || group.Contains(posIndex)) {
            return;
        }

        if (!compChessBoard.chessInfoDict.TryGetValue(posIndex.ToString(), out ChessInfo chessInfo) ||
            chessInfo == null ||
            (PlayerFlag)chessInfo.chessFlag.value != playerFlag) {
            return;
        }

        group.Add(posIndex);
        pending.Enqueue(posIndex);
    }

    private bool TryReadRemovedStoneSet(JToken token, out HashSet<int> removedSet, out string stonesText)
    {
        removedSet = new HashSet<int>();
        stonesText = ReadRemovedStonesText(token);
        if (string.IsNullOrEmpty(stonesText)) {
            return true;
        }

        if (!OgsPackedMoveCodec.TryParseStoneString(stonesText, compOgsDuel.boardSize, out List<RectCoordinates> coordsList)) {
            return false;
        }

        foreach (RectCoordinates coords in coordsList) {
            int posIndex = compChessBoard.GetPosIndexByCoords(coords);
            if (posIndex >= 0) {
                removedSet.Add(posIndex);
            }
        }
        return true;
    }

    private string ReadRemovedStonesText(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return string.Empty;
        }

        if (token.Type == JTokenType.String || token.Type == JTokenType.Integer) {
            return token.ToString();
        }

        if (token is JObject obj) {
            return ReadFirstString(obj, "stones", "stone", "removed_stones", "removedStones", "removed");
        }

        if (token is JArray array) {
            List<string> stones = new List<string>();
            foreach (JToken item in array) {
                string value = ReadRemovedStonesText(item);
                if (!string.IsNullOrWhiteSpace(value)) {
                    stones.Add(value);
                }
            }
            return string.Concat(stones);
        }

        return string.Empty;
    }

    private static bool TryReadFullRemovedStoneToken(JObject obj, out JToken token)
    {
        token = null;
        if (obj == null) {
            return false;
        }

        foreach (string fieldName in new[] { "removed_stones", "removedStones", "all_removed_stones", "allRemovedStones" }) {
            if (obj.TryGetValue(fieldName, out token)) {
                return true;
            }
        }
        return false;
    }

    private int ReadOgsPlayerId(JToken token)
    {
        if (token is JObject obj) {
            int id = ReadFirstInt(obj, 0, "player_id", "playerId", "user_id", "userId", "player", "user");
            if (id > 0) {
                return id;
            }
            id = ReadFirstInt(obj["player"] as JObject, 0, "id", "user_id", "player_id");
            if (id > 0) {
                return id;
            }
            return ReadFirstInt(obj["user"] as JObject, 0, "id", "user_id", "player_id");
        }
        return 0;
    }

    private string BuildRemovedStoneString(IEnumerable<int> posIndexes)
    {
        if (compChessBoard == null || posIndexes == null) {
            return string.Empty;
        }

        List<RectCoordinates> coordsList = new List<RectCoordinates>();
        foreach (int posIndex in posIndexes) {
            coordsList.Add(compChessBoard.GetCoordsByPosIndex(posIndex));
        }
        return NormalizeRemovedStonesText(OgsPackedMoveCodec.EncodeStoneString(coordsList));
    }

    private string NormalizeRemovedStonesText(string stones)
    {
        if (string.IsNullOrWhiteSpace(stones)) {
            return string.Empty;
        }

        return OgsPackedMoveCodec.TryParseStoneString(stones, compOgsDuel?.boardSize ?? 19, out List<RectCoordinates> coordsList)
            ? OgsPackedMoveCodec.EncodeStoneString(coordsList)
            : stones.Trim();
    }

    private void ApplyUndoAccepted(JToken payload)
    {
        if (compOgsDuel == null || compDuel == null || chessBoardSystem == null) {
            return;
        }

        int undoMoveCount = ReadUndoMoveCount(payload, pendingUndoMoveCount > 0 ? pendingUndoMoveCount : 1);
        int targetMoveCount = Math.Max(0, Math.Min(compOgsDuel.acceptedMoveCount - undoMoveCount, acceptedMoves.Count));
        if (acceptedMoves.Count > targetMoveCount) {
            acceptedMoves.RemoveRange(targetMoveCount, acceptedMoves.Count - targetMoveCount);
        }

        hasPendingUndoRequest = false;
        pendingUndoMoveNumber = 0;
        pendingUndoMoveCount = 0;
        pendingUndoRequesterOgsUserId = 0;
        if (!RebuildBoardFromMoves(acceptedInitialStones, acceptedMoves)) {
            SetError("OGS undo board state could not be applied.");
            return;
        }
        compOgsDuel.acceptedMoveCount = targetMoveCount;
        compOgsDuel.isSubmitting = false;
        SyncTurnAndInputFromMoveCount(targetMoveCount);
        XNLogger.LogInfo("OGS undo accepted.", ("gameId", compOgsDuel.gameId.ToString()), ("moveCount", targetMoveCount.ToString()));
    }

    private void ApplyUndoCanceled(JToken payload)
    {
        if (compOgsDuel != null) {
            compOgsDuel.isSubmitting = false;
        }
        hasPendingUndoRequest = false;
        pendingUndoMoveNumber = 0;
        pendingUndoMoveCount = 0;
        pendingUndoRequesterOgsUserId = 0;
        SyncInputAuthority();
        XNLogger.LogInfo("OGS undo canceled.", ("gameId", compOgsDuel?.gameId.ToString() ?? string.Empty), ("payload", payload?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty));
    }

    private void ApplyUndoRequested(JToken payload)
    {
        if (compOgsDuel == null) {
            return;
        }

        int requestedMoveNumber = ReadMoveNumber(payload, compOgsDuel.acceptedMoveCount);
        int requestedUndoMoveCount = ReadUndoMoveCount(payload, 1);
        int requesterOgsUserId = ReadRequesterUserId(payload);
        bool isOwnRequestEcho = requesterOgsUserId == compOgsDuel.localOgsUserId ||
            (hasPendingUndoRequest &&
                pendingUndoRequesterOgsUserId == compOgsDuel.localOgsUserId &&
                requestedMoveNumber == pendingUndoMoveNumber);

        pendingUndoMoveNumber = requestedMoveNumber;
        pendingUndoMoveCount = requestedUndoMoveCount;
        pendingUndoRequesterOgsUserId = isOwnRequestEcho ? compOgsDuel.localOgsUserId : requesterOgsUserId;
        hasPendingUndoRequest = true;
        compOgsDuel.isSubmitting = false;
        SyncInputAuthority();
        if (compOgsDuel.isBotGame) {
            XNLogger.LogInfo(
                "OGS bot game undo request ignored.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("moveNumber", pendingUndoMoveNumber.ToString()),
                ("undoMoveCount", pendingUndoMoveCount.ToString()),
                ("requesterOgsUserId", requesterOgsUserId.ToString()),
                ("payload", payload?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty));
            hasPendingUndoRequest = false;
            pendingUndoMoveNumber = 0;
            pendingUndoMoveCount = 0;
            pendingUndoRequesterOgsUserId = 0;
            SyncInputAuthority();
            return;
        }
        if (isOwnRequestEcho) {
            XNLogger.LogInfo(
                "OGS own undo request echoed.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("moveNumber", pendingUndoMoveNumber.ToString()),
                ("undoMoveCount", pendingUndoMoveCount.ToString()),
                ("payload", payload?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty));
            return;
        }

        scene.EmitSystemEvent(new OnOgsDuelTakeBackConfirmRequest(pendingUndoMoveNumber));
        XNLogger.LogInfo(
            "OGS undo requested by peer.",
            ("gameId", compOgsDuel.gameId.ToString()),
            ("moveNumber", pendingUndoMoveNumber.ToString()),
            ("undoMoveCount", pendingUndoMoveCount.ToString()),
            ("requesterOgsUserId", requesterOgsUserId.ToString()),
            ("payload", payload?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty));
    }

    private bool RebuildBoardFromMoves(List<OgsDuelInitialStone> initialStones, List<OgsDuelMove> moves)
    {
        chessBoardSystem.ClearBoardRuntimeState();
        if (initialStones != null) {
            foreach (OgsDuelInitialStone stone in initialStones) {
                if (!ApplyOgsInitialStone(stone)) {
                    XNLogger.LogWarn(
                        "OGS rebuild skipped invalid initial stone.",
                        ("gameId", compOgsDuel.gameId.ToString()),
                        ("playerFlag", stone?.playerFlag.ToString() ?? string.Empty),
                        ("coords", stone?.coords?.ToString() ?? string.Empty));
                    return false;
                }
            }
        }

        RectCoordinates latestMoveCoords = null;
        PlayerFlag latestMovePlayerFlag = 0;
        foreach (OgsDuelMove move in moves) {
            if (!ApplyAcceptedMove(move, false)) {
                XNLogger.LogWarn("OGS rebuild skipped invalid move.", ("gameId", compOgsDuel.gameId.ToString()), ("moveNumber", move.moveNumber.ToString()));
                continue;
            }

            if (!move.isPass) {
                latestMoveCoords = move.coords?.Clone();
                latestMovePlayerFlag = move.playerFlag;
            }
        }

        compChessBoard.GetStoneViewCache().SyncFromChessInfoDict();
        if (latestMoveCoords != null) {
            compChessBoard.GetStoneViewCache().ApplyLatestMoveMarker(latestMoveCoords, latestMovePlayerFlag);
        }
        compChessBoard.lastChessInfoDict = compChessBoard.CreateCacheChessInfoDict();
        compDuel.consecutivePassCount.value = CountTrailingPasses(moves);
        scene.EmitSystemEvent(new OnClearDuelOwnership());
        return true;
    }

    private bool ApplyOgsInitialStone(OgsDuelInitialStone stone)
    {
        if (stone == null || stone.coords == null || stone.playerFlag == 0 || compChessBoard == null) {
            return false;
        }

        int posIndex = compChessBoard.GetPosIndexByCoords(stone.coords);
        if (posIndex < 0 || compChessBoard.chessInfoDict.ContainsKey(posIndex.ToString())) {
            return false;
        }

        ChessInfo chessInfo = new ChessInfo();
        chessInfo.chessGuid.value = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Chess>());
        chessInfo.chessFlag.value = (int)stone.playerFlag;
        compChessBoard.chessInfoDict.SetValue(posIndex.ToString(), chessInfo);
        return true;
    }

    private JArray BuildKataGoInitialStones(List<OgsDuelInitialStone> initialStones)
    {
        JArray stones = new JArray();
        if (initialStones == null || compOgsDuel == null) {
            return stones;
        }

        foreach (OgsDuelInitialStone stone in initialStones) {
            if (stone == null || stone.coords == null || stone.playerFlag == 0) {
                continue;
            }

            string color = KataGoPositionJsonBuilder.ToKataGoColor(stone.playerFlag);
            if (string.IsNullOrEmpty(color)) {
                continue;
            }

            stones.Add(new JArray(color, KataGoPositionJsonBuilder.ToKataGoPoint(stone.coords, compOgsDuel.boardSize)));
        }
        return stones;
    }

    private bool ApplyAcceptedMove(OgsDuelMove move, bool emitAcceptedEvent)
    {
        if (move == null || move.playerFlag == 0) {
            return false;
        }

        if (move.isPass) {
            compDuel.AppendKataGoPass(move.playerFlag);
            compDuel.consecutivePassCount.value += 1;
            compDuel.ClearOwnershipScoreCache();
            scene.EmitSystemEvent(new OnClearDuelOwnership());
            if (emitAcceptedEvent) {
                Player player = GetPlayerByFlag(move.playerFlag);
                scene.EmitSystemEvent(new OnDuelPassAccepted(
                    player?.guid ?? string.Empty,
                    move.playerFlag,
                    false,
                    compDuel.consecutivePassCount.value));
            }
            return true;
        }

        bool applied = chessBoardSystem.TryApplyAcceptedRemoteMove(
            move.playerFlag,
            move.coords,
            emitAcceptedEvent,
            out DuelMoveRejectReason rejectReason);
        if (!applied) {
            XNLogger.LogWarn(
                "OGS accepted move failed local rule application.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("moveNumber", move.moveNumber.ToString()),
                ("rejectReason", rejectReason.ToString()));
        }
        return applied;
    }

    private async void OnSubmitOgsDuelMove(OnSubmitOgsDuelMove evt)
    {
        if (evt == null || evt.coords == null || !CanSubmitLocalMove()) {
            return;
        }

        await SubmitMoveAsync(OgsPackedMoveCodec.Encode(evt.coords, compOgsDuel.boardSize));
    }

    private async void OnSubmitOgsDuelPass(OnSubmitOgsDuelPass evt)
    {
        if (!CanSubmitLocalMove()) {
            return;
        }

        await SubmitMoveAsync(OgsPackedMoveCodec.PassMove);
    }

    private async void OnSubmitOgsDuelResign(OnSubmitOgsDuelResign evt)
    {
        if (!CanSubmitResign()) {
            return;
        }

        await SubmitResignAsync();
    }

    private async void OnSubmitOgsDuelTakeBack(OnSubmitOgsDuelTakeBack evt)
    {
        if (!CanSubmitTakeBack()) {
            return;
        }

        await SubmitUndoRequestAsync();
    }

    private async void OnSubmitOgsDuelTakeBackConfirm(OnSubmitOgsDuelTakeBackConfirm evt)
    {
        if (evt == null || !hasPendingUndoRequest || compOgsDuel == null || compOgsDuel.isBotGame || !CanSubmitOgsCommand()) {
            return;
        }

        await SubmitUndoConfirmAsync(evt.accepted);
    }

    private async void OnSubmitOgsRemovedStoneToggle(OnSubmitOgsRemovedStoneToggle evt)
    {
        if (evt == null || evt.coords == null || !CanSubmitStoneRemovalCommand()) {
            return;
        }

        if (!TryCollectStoneGroup(evt.coords, out HashSet<int> group)) {
            return;
        }

        bool shouldRemove = !compOgsDuel.removedStonePosIndexes.Contains(compChessBoard.GetPosIndexByCoords(evt.coords));
        string stones = BuildRemovedStoneString(group);
        if (string.IsNullOrEmpty(stones)) {
            return;
        }

        await SubmitRemovedStonesSetAsync(stones, shouldRemove);
    }

    private async void OnSubmitOgsRemovedStonesAccept(OnSubmitOgsRemovedStonesAccept evt)
    {
        if (!CanSubmitStoneRemovalCommand()) {
            return;
        }

        await SubmitRemovedStonesAcceptAsync();
    }

    private async void OnSubmitOgsRemovedStonesReject(OnSubmitOgsRemovedStonesReject evt)
    {
        if (!CanSubmitStoneRemovalCommand()) {
            return;
        }

        await SubmitRemovedStonesRejectAsync();
    }

    private async Task SubmitMoveAsync(string packedMove)
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return;
        }

        try {
            compOgsDuel.isSubmitting = true;
            SyncInputAuthority();
            await realtimeSession.SendMoveAsync(packedMove, cancellationTokenSource.Token);
            XNLogger.LogInfo("OGS move submitted.", ("gameId", compOgsDuel.gameId.ToString()), ("move", packedMove));
        }
        catch (Exception ex) {
            compOgsDuel.isSubmitting = false;
            SetError(ex.Message);
            XNLogger.LogError("OGS move submit failed.", ("gameId", compOgsDuel.gameId.ToString()), ("move", packedMove), ("err", ex.Message));
        }
        finally {
            SyncInputAuthority();
        }
    }

    private async Task<bool> SubmitResignAsync()
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return false;
        }

        try {
            compOgsDuel.isSubmitting = true;
            SyncInputAuthority();
            await realtimeSession.SendResignAsync(cancellationTokenSource.Token);
            XNLogger.LogInfo("OGS resign submitted.", ("gameId", compOgsDuel.gameId.ToString()));
            return true;
        }
        catch (Exception ex) {
            compOgsDuel.isSubmitting = false;
            SetError(ex.Message);
            XNLogger.LogError("OGS resign submit failed.", ("gameId", compOgsDuel.gameId.ToString()), ("err", ex.Message));
            return false;
        }
        finally {
            SyncInputAuthority();
        }
    }

    private async Task SubmitUndoRequestAsync()
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return;
        }

        try {
            compOgsDuel.isSubmitting = true;
            hasPendingUndoRequest = true;
            pendingUndoMoveNumber = compOgsDuel.acceptedMoveCount;
            pendingUndoMoveCount = 1;
            pendingUndoRequesterOgsUserId = compOgsDuel.localOgsUserId;
            SyncInputAuthority();
            await realtimeSession.SendUndoRequestAsync(compOgsDuel.acceptedMoveCount, cancellationTokenSource.Token);
            compOgsDuel.isSubmitting = false;
            XNLogger.LogInfo("OGS undo requested.", ("gameId", compOgsDuel.gameId.ToString()), ("moveCount", compOgsDuel.acceptedMoveCount.ToString()));
        }
        catch (Exception ex) {
            compOgsDuel.isSubmitting = false;
            hasPendingUndoRequest = false;
            pendingUndoMoveNumber = 0;
            pendingUndoMoveCount = 0;
            pendingUndoRequesterOgsUserId = 0;
            SetError(ex.Message);
            XNLogger.LogError("OGS undo request failed.", ("gameId", compOgsDuel.gameId.ToString()), ("err", ex.Message));
        }
        finally {
            SyncInputAuthority();
        }
    }

    private async Task SubmitRemovedStonesSetAsync(string stones, bool removed)
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return;
        }

        try {
            compOgsDuel.isSubmittingRemovedStones = true;
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
            await realtimeSession.SendRemovedStonesSetAsync(stones, removed, compOgsDuel.strictSekiMode, cancellationTokenSource.Token);
            XNLogger.LogInfo(
                "OGS removed stones set submitted.",
                ("gameId", compOgsDuel.gameId.ToString()),
                ("stones", stones),
                ("removed", removed.ToString()));
        }
        catch (Exception ex) {
            SetError(ex.Message);
            XNLogger.LogError("OGS removed stones set failed.", ("gameId", compOgsDuel.gameId.ToString()), ("err", ex.Message));
        }
        finally {
            compOgsDuel.isSubmittingRemovedStones = false;
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
            SyncInputAuthority();
        }
    }

    private async Task SubmitRemovedStonesAcceptAsync()
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return;
        }

        try {
            compOgsDuel.isSubmittingRemovedStones = true;
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
            string stones = NormalizeRemovedStonesText(compOgsDuel.removedStones);
            await realtimeSession.SendRemovedStonesAcceptAsync(stones, compOgsDuel.strictSekiMode, cancellationTokenSource.Token);
            compOgsDuel.localRemovedStonesAccepted = true;
            compOgsDuel.localAcceptedRemovedStones = stones;
            XNLogger.LogInfo("OGS removed stones accepted.", ("gameId", compOgsDuel.gameId.ToString()), ("stones", stones));
        }
        catch (Exception ex) {
            SetError(ex.Message);
            XNLogger.LogError("OGS removed stones accept failed.", ("gameId", compOgsDuel.gameId.ToString()), ("err", ex.Message));
        }
        finally {
            compOgsDuel.isSubmittingRemovedStones = false;
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
            SyncInputAuthority();
        }
    }

    private async Task SubmitRemovedStonesRejectAsync()
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return;
        }

        try {
            compOgsDuel.isSubmittingRemovedStones = true;
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
            await realtimeSession.SendRemovedStonesRejectAsync(cancellationTokenSource.Token);
            XNLogger.LogInfo("OGS removed stones rejected.", ("gameId", compOgsDuel.gameId.ToString()));
        }
        catch (Exception ex) {
            SetError(ex.Message);
            XNLogger.LogError("OGS removed stones reject failed.", ("gameId", compOgsDuel.gameId.ToString()), ("err", ex.Message));
        }
        finally {
            compOgsDuel.isSubmittingRemovedStones = false;
            scene.EmitSystemEvent(new OnOgsStoneRemovalStateChanged());
            SyncInputAuthority();
        }
    }

    private async Task SubmitUndoConfirmAsync(bool accepted)
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return;
        }

        try {
            compOgsDuel.isSubmitting = true;
            SyncInputAuthority();
            if (accepted) {
                await realtimeSession.SendUndoAcceptAsync(pendingUndoMoveNumber, cancellationTokenSource.Token);
                compOgsDuel.isSubmitting = false;
            } else {
                await realtimeSession.SendUndoCancelAsync(pendingUndoMoveNumber, cancellationTokenSource.Token);
                compOgsDuel.isSubmitting = false;
                hasPendingUndoRequest = false;
                pendingUndoMoveNumber = 0;
                pendingUndoMoveCount = 0;
                pendingUndoRequesterOgsUserId = 0;
            }
            XNLogger.LogInfo("OGS undo confirm submitted.", ("gameId", compOgsDuel.gameId.ToString()), ("accepted", accepted.ToString()));
        }
        catch (Exception ex) {
            compOgsDuel.isSubmitting = false;
            if (!accepted) {
                hasPendingUndoRequest = false;
                pendingUndoMoveNumber = 0;
                pendingUndoMoveCount = 0;
                pendingUndoRequesterOgsUserId = 0;
            }
            SetError(ex.Message);
            XNLogger.LogError("OGS undo confirm failed.", ("gameId", compOgsDuel.gameId.ToString()), ("accepted", accepted.ToString()), ("err", ex.Message));
        }
        finally {
            SyncInputAuthority();
        }
    }

    private bool CanSubmitLocalMove()
    {
        if (!CanSubmitOgsCommand() || IsStoneRemovalPhase()) {
            return false;
        }

        PlayerFlag localPlayerFlag = (PlayerFlag)compDuel.localPlayerFlag.value;
        if (localPlayerFlag == 0) {
            return false;
        }

        Player currentPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        return currentPlayer != null && (PlayerFlag)currentPlayer.playerFlag.value == localPlayerFlag;
    }

    private bool CanSubmitOgsCommand()
    {
        if (!CanSubmitBaseOgsCommand()) {
            return false;
        }

        return compDuel.localPlayerFlag.value != 0;
    }

    private bool CanSubmitBaseOgsCommand()
    {
        return compDuel != null
            && compOgsDuel != null
            && compOgsDuel.isConnected
            && !compOgsDuel.isSubmitting
            && !IsFinishedPhase()
            && !IsPausedPhase();
    }

    private void SyncTurnAndInputFromMoveCount(int moveCount)
    {
        SyncTurnAndInputFromMoveCount(moveCount, 0);
    }

    private void SyncTurnAndInputFromMoveCount(int moveCount, PlayerFlag currentTurnPlayerFlag)
    {
        if (compDuel == null) {
            return;
        }

        if (currentTurnPlayerFlag != PlayerFlag.Player1 && currentTurnPlayerFlag != PlayerFlag.Player2) {
            currentTurnPlayerFlag = ResolveCurrentTurnPlayerFlag(moveCount);
        }
        compDuel.curTurnPlayerGuid.value = currentTurnPlayerFlag == PlayerFlag.Player1
            ? compDuel.player1Guid.value
            : compDuel.player2Guid.value;

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            compDuel.duelFSM.SwitchState(DuelStateDefine.STATE_TURN_INPUT);
        }
        SyncInputAuthority();
        scene.EmitSystemEvent(new OnDuelStateChanged(DuelStateDefine.STATE_TURN_INPUT));
    }

    private PlayerFlag ResolveCurrentTurnPlayerFlag(int moveCount)
    {
        PlayerFlag firstMovePlayerFlag = compOgsDuel != null
            ? DuelUtils.GetValidPlayerFlag(compOgsDuel.firstMovePlayerFlag)
            : PlayerFlag.Player1;
        int openingSameColorMoveCount = compOgsDuel != null ? Math.Max(0, compOgsDuel.openingSameColorMoveCount) : 0;
        if (openingSameColorMoveCount > 0) {
            if (moveCount < openingSameColorMoveCount) {
                return firstMovePlayerFlag;
            }

            int postOpeningMoveCount = moveCount - openingSameColorMoveCount;
            PlayerFlag postOpeningFirstPlayerFlag = firstMovePlayerFlag.GetOpponentPlayerFlag();
            return postOpeningMoveCount % 2 == 0
                ? postOpeningFirstPlayerFlag
                : firstMovePlayerFlag;
        }

        return moveCount % 2 == 0 ? firstMovePlayerFlag : firstMovePlayerFlag.GetOpponentPlayerFlag();
    }

    private void SyncInputAuthority()
    {
        if (compDuel == null) {
            return;
        }

        compDuel.localInputPlayerFlag.value = CanSubmitLocalMove()
            ? compDuel.localPlayerFlag.value
            : 0;
    }

    private void ApplyPlayerData(JObject gameData)
    {
        JObject players = gameData["players"] as JObject;
        JToken blackPlayerToken = players?["black"] ?? gameData["black_player"] ?? gameData["black"];
        JToken whitePlayerToken = players?["white"] ?? gameData["white_player"] ?? gameData["white"];
        JObject blackPlayer = ReadPlayerObject(blackPlayerToken);
        JObject whitePlayer = ReadPlayerObject(whitePlayerToken);

        string blackName = ReadPlayerName(blackPlayer);
        string whiteName = ReadPlayerName(whitePlayer);
        int blackId = ReadPlayerId(blackPlayerToken, gameData, "black_player_id", "black_id", "black");
        int whiteId = ReadPlayerId(whitePlayerToken, gameData, "white_player_id", "white_id", "white");

        if (blackId > 0) {
            compOgsDuel.blackOgsUserId = blackId;
        }
        if (whiteId > 0) {
            compOgsDuel.whiteOgsUserId = whiteId;
        }
        if (IsBotPlayer(blackPlayer) || IsBotPlayer(whitePlayer)) {
            compOgsDuel.isBotGame = true;
        }

        if (!string.IsNullOrWhiteSpace(blackName)) {
            compDuel.player1DisplayName.value = blackName;
        }
        if (!string.IsNullOrWhiteSpace(whiteName)) {
            compDuel.player2DisplayName.value = whiteName;
        }

        if (compOgsDuel.localOgsUserId > 0) {
            if (compOgsDuel.localOgsUserId == compOgsDuel.blackOgsUserId) {
                compDuel.localPlayerFlag.value = (int)PlayerFlag.Player1;
            } else if (compOgsDuel.localOgsUserId == compOgsDuel.whiteOgsUserId) {
                compDuel.localPlayerFlag.value = (int)PlayerFlag.Player2;
            } else {
                XNLogger.LogWarn(
                    "OGS local player seat could not be resolved from game data.",
                    ("gameId", compOgsDuel.gameId.ToString()),
                    ("localOgsUserId", compOgsDuel.localOgsUserId.ToString()),
                    ("blackOgsUserId", compOgsDuel.blackOgsUserId.ToString()),
                    ("whiteOgsUserId", compOgsDuel.whiteOgsUserId.ToString()),
                    ("phase", compOgsDuel.phase ?? string.Empty));
            }
        }
    }

    private bool IsFinishedPhase()
    {
        string phase = compOgsDuel?.phase ?? string.Empty;
        return string.Equals(phase, "finished", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "finished_game", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsStoneRemovalPhase()
    {
        string phase = compOgsDuel?.phase ?? string.Empty;
        return string.Equals(phase, "stone removal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "stone_removal", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPausedPhase()
    {
        string phase = compOgsDuel?.phase ?? string.Empty;
        return string.Equals(phase, "paused", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "pause", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "suspended", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryBuildOgsScoreResult(JObject gameData, out DuelScoreResult scoreResult)
    {
        scoreResult = null;
        if (gameData == null || compOgsDuel == null) {
            return false;
        }

        JObject payload = GetOgsTerminalPayload(gameData);
        JObject score = payload?["score"] as JObject ?? gameData["score"] as JObject;
        bool hasFinishedScore = IsFinishedPhase() ||
            HasNonEmptyField(gameData, "end_time") ||
            HasNonEmptyField(payload, "end_time") ||
            HasNonEmptyField(gameData, "outcome") ||
            HasNonEmptyField(payload, "outcome");
        if (!hasFinishedScore) {
            return false;
        }

        float blackScore = ReadFirstFloat(score?["black"] as JObject, 0f, "total");
        float whiteScore = ReadFirstFloat(score?["white"] as JObject, 0f, "total");
        float komi = ReadFirstFloat(payload, ReadFirstFloat(gameData, 0f, "komi"), "komi");
        PlayerFlag winnerFlag = ResolveWinnerFlag(gameData, blackScore, whiteScore);
        float margin = Math.Abs(blackScore - whiteScore);
        if (margin <= 0f) {
            margin = ParseOutcomeMargin(ReadFirstOgsTerminalText(gameData));
        }

        scoreResult = new DuelScoreResult
        {
            blackScore = blackScore,
            whiteScore = whiteScore,
            komi = komi,
            margin = margin,
            winnerFlag = winnerFlag,
            scoreSource = "ogs",
        };
        return true;
    }

    private void EndGameByOgsScore(DuelScoreResult scoreResult, JObject gameData)
    {
        if (compDuel == null || scoreResult == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        OgsGameEndResolution endResolution = ResolveOgsGameEndReason(gameData, scoreResult.winnerFlag);
        compDuel.finalBlackScore.value = scoreResult.blackScore;
        compDuel.finalWhiteScore.value = scoreResult.whiteScore;
        compDuel.finalScoreMargin.value = scoreResult.margin;
        ApplyOgsGameEndResolution(endResolution);
        compDuel.localInputPlayerFlag.value = 0;
        compOgsDuel.isSubmitting = false;

        PlayerFlag winnerFlag = scoreResult.winnerFlag != 0 || endResolution.loserFlag == 0
            ? scoreResult.winnerFlag
            : endResolution.loserFlag.GetOpponentPlayerFlag();
        if (winnerFlag == PlayerFlag.Player1) {
            compDuel.winnerGuid.value = compDuel.player1Guid.value;
        } else if (winnerFlag == PlayerFlag.Player2) {
            compDuel.winnerGuid.value = compDuel.player2Guid.value;
        } else {
            compDuel.winnerGuid.value = string.Empty;
        }

        scene.EmitSystemEvent(new OnDuelScoreResult(scoreResult, false));
        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
        scene.EmitSystemEvent(new OnDuelStateChanged(DuelStateDefine.STATE_GAME_END));
        XNLogger.LogInfo(
            "OGS game ended by server score.",
            ("gameId", compOgsDuel.gameId.ToString()),
            ("blackScore", scoreResult.blackScore.ToString(CultureInfo.InvariantCulture)),
            ("whiteScore", scoreResult.whiteScore.ToString(CultureInfo.InvariantCulture)),
            ("margin", scoreResult.margin.ToString(CultureInfo.InvariantCulture)),
            ("winnerFlag", winnerFlag.ToString()),
            ("reason", endResolution.reason),
            ("loserFlag", endResolution.loserFlag.ToString()),
            ("reasonSource", endResolution.source));
    }

    private void EndGameByOgsFinishedFallback(JObject gameData)
    {
        PlayerFlag winnerFlag = ResolveWinnerFlag(gameData, 0f, 0f);
        var scoreResult = new DuelScoreResult
        {
            blackScore = 0f,
            whiteScore = 0f,
            komi = 0f,
            margin = 0f,
            winnerFlag = winnerFlag,
            scoreSource = "ogs",
        };
        EndGameByOgsScore(scoreResult, gameData);
    }

    private OgsGameEndResolution ResolveOgsGameEndReason(JObject gameData, PlayerFlag winnerFlag)
    {
        PlayerFlag loserFlag = ResolveOgsLoserFlag(gameData, winnerFlag);
        if (TryFindOgsTerminalText(gameData, IsOgsResignText, out string resignSource)) {
            return new OgsGameEndResolution(DuelGameEndReason.Resign, loserFlag, resignSource);
        }
        if (TryFindOgsTerminalText(gameData, IsOgsTimeoutText, out string timeoutSource)) {
            return new OgsGameEndResolution(DuelGameEndReason.Timeout, loserFlag, timeoutSource);
        }
        if (TryFindOgsTerminalText(gameData, IsOgsScoreText, out string scoreSource) || HasOgsScorePayload(gameData)) {
            return new OgsGameEndResolution(DuelGameEndReason.Score, 0, scoreSource);
        }

        string terminalSource = ReadFirstOgsTerminalText(gameData);
        if (winnerFlag != 0) {
            XNLogger.LogWarn(
                "OGS game end reason was not recognized, defaulting to score display.",
                ("gameId", compOgsDuel?.gameId.ToString() ?? string.Empty),
                ("winnerFlag", winnerFlag.ToString()),
                ("loserFlag", loserFlag.ToString()),
                ("terminalSource", terminalSource));
        }
        return new OgsGameEndResolution(DuelGameEndReason.Score, 0, terminalSource);
    }

    private void ApplyOgsGameEndResolution(OgsGameEndResolution endResolution)
    {
        compDuel.gameEndReason.value = endResolution.reason;
        compDuel.timeoutLoserGuid.value = string.Empty;
        compDuel.resignLoserGuid.value = string.Empty;

        string loserGuid = GetPlayerGuidByFlag(endResolution.loserFlag);
        if (endResolution.reason == DuelGameEndReason.Timeout) {
            compDuel.timeoutLoserGuid.value = loserGuid;
        } else if (endResolution.reason == DuelGameEndReason.Resign) {
            compDuel.resignLoserGuid.value = loserGuid;
        }
    }

    private PlayerFlag ResolveWinnerFlag(JObject gameData, float blackScore, float whiteScore)
    {
        JObject payload = GetOgsTerminalPayload(gameData);
        int winnerId = ReadFirstInt(gameData, 0, "winner", "winner_id", "winnerId");
        if (winnerId <= 0) {
            winnerId = ReadFirstInt(payload, 0, "winner", "winner_id", "winnerId");
        }
        int blackId = ReadFirstInt(payload, compOgsDuel.blackOgsUserId, "black_player_id", "black_id", "black");
        int whiteId = ReadFirstInt(payload, compOgsDuel.whiteOgsUserId, "white_player_id", "white_id", "white");
        if (winnerId > 0) {
            if (winnerId == blackId || winnerId == compOgsDuel.blackOgsUserId) {
                return PlayerFlag.Player1;
            }
            if (winnerId == whiteId || winnerId == compOgsDuel.whiteOgsUserId) {
                return PlayerFlag.Player2;
            }
        }
        if (blackScore > whiteScore) {
            return PlayerFlag.Player1;
        }
        if (whiteScore > blackScore) {
            return PlayerFlag.Player2;
        }
        return 0;
    }

    private PlayerFlag ResolveOgsLoserFlag(JObject gameData, PlayerFlag winnerFlag)
    {
        if (TryReadOgsLostPlayerFlag(gameData, out PlayerFlag lostFlag)) {
            return lostFlag;
        }

        int loserId = ReadFirstOgsInt(gameData, 0, "loser", "loser_id", "loserId", "losing_player", "losing_player_id");
        if (loserId > 0) {
            if (loserId == compOgsDuel.blackOgsUserId) {
                return PlayerFlag.Player1;
            }
            if (loserId == compOgsDuel.whiteOgsUserId) {
                return PlayerFlag.Player2;
            }
        }

        return winnerFlag == PlayerFlag.Player1 || winnerFlag == PlayerFlag.Player2
            ? winnerFlag.GetOpponentPlayerFlag()
            : 0;
    }

    private bool TryReadOgsLostPlayerFlag(JObject gameData, out PlayerFlag lostFlag)
    {
        lostFlag = 0;
        bool blackLost = ReadFirstOgsBool(gameData, false, "black_lost", "blackLost");
        bool whiteLost = ReadFirstOgsBool(gameData, false, "white_lost", "whiteLost");
        if (blackLost && !whiteLost) {
            lostFlag = PlayerFlag.Player1;
            return true;
        }
        if (whiteLost && !blackLost) {
            lostFlag = PlayerFlag.Player2;
            return true;
        }
        return false;
    }

    private static float ParseOutcomeMargin(string outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome)) {
            return 0f;
        }

        string[] parts = outcome.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 0) {
            return 0f;
        }
        return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float margin)
            ? Math.Abs(margin)
            : 0f;
    }

    private int CountTrailingPasses(List<OgsDuelMove> moves)
    {
        int count = 0;
        if (moves == null) {
            return count;
        }

        for (int i = moves.Count - 1; i >= 0; i--) {
            if (!moves[i].isPass) {
                break;
            }
            count += 1;
        }
        return count;
    }

    private Player GetPlayerByFlag(PlayerFlag playerFlag)
    {
        if (playerFlag == PlayerFlag.Player1) {
            return scene.GetEntity<Player>(compDuel.player1Guid.value);
        }
        if (playerFlag == PlayerFlag.Player2) {
            return scene.GetEntity<Player>(compDuel.player2Guid.value);
        }
        return null;
    }

    private string GetPlayerGuidByFlag(PlayerFlag playerFlag)
    {
        if (compDuel == null) {
            return string.Empty;
        }
        if (playerFlag == PlayerFlag.Player1) {
            return compDuel.player1Guid.value;
        }
        if (playerFlag == PlayerFlag.Player2) {
            return compDuel.player2Guid.value;
        }
        return string.Empty;
    }

    private void SetError(string message)
    {
        if (compOgsDuel != null) {
            compOgsDuel.lastError = message ?? string.Empty;
            compOgsDuel.isConnected = false;
            compOgsDuel.isSubmitting = false;
        }
        if (compDuel != null) {
            compDuel.localInputPlayerFlag.value = 0;
        }
    }

    private static string ReadPlayerName(JObject playerJson)
    {
        string value = ReadFirstString(playerJson, "username", "name", "professional_name", "id");
        if (!string.IsNullOrWhiteSpace(value)) {
            return value;
        }

        value = ReadFirstString(playerJson?["user"] as JObject, "username", "name", "professional_name", "id");
        if (!string.IsNullOrWhiteSpace(value)) {
            return value;
        }

        return ReadFirstString(playerJson?["player"] as JObject, "username", "name", "professional_name", "id");
    }

    private static bool IsBotPlayer(JObject playerJson)
    {
        string uiClass = ReadFirstString(playerJson, "ui_class", "class");
        if (string.IsNullOrWhiteSpace(uiClass)) {
            uiClass = ReadFirstString(playerJson?["user"] as JObject, "ui_class", "class");
        }
        if (string.IsNullOrWhiteSpace(uiClass)) {
            uiClass = ReadFirstString(playerJson?["player"] as JObject, "ui_class", "class");
        }
        return uiClass.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static JObject ReadPlayerObject(JToken token)
    {
        return token as JObject;
    }

    private static int ReadPlayerId(JToken playerToken, JObject gameData, params string[] topLevelFieldNames)
    {
        int id = ReadFirstInt(playerToken, 0);
        if (id > 0) {
            return id;
        }

        if (playerToken is JObject playerObj) {
            id = ReadFirstInt(playerObj, 0, "id", "user_id", "player_id", "pk", "uid");
            if (id > 0) {
                return id;
            }

            id = ReadFirstInt(playerObj["user"] as JObject, 0, "id", "user_id", "player_id", "pk", "uid");
            if (id > 0) {
                return id;
            }

            id = ReadFirstInt(playerObj["player"] as JObject, 0, "id", "user_id", "player_id", "pk", "uid");
            if (id > 0) {
                return id;
            }
        }

        return ReadFirstInt(gameData, 0, topLevelFieldNames);
    }

    private static string ReadFirstString(JObject json, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return string.Empty;
        }

        foreach (string fieldName in fieldNames) {
            string value = json[fieldName]?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool TryFindOgsTerminalText(JObject gameData, Func<string, bool> matcher, out string matchedValue)
    {
        matchedValue = string.Empty;
        if (matcher == null) {
            return false;
        }

        if (TryFindTerminalText(gameData, matcher, out matchedValue)) {
            return true;
        }

        JObject payload = GetOgsTerminalPayload(gameData);
        return payload != gameData && TryFindTerminalText(payload, matcher, out matchedValue);
    }

    private static bool TryFindTerminalText(JObject json, Func<string, bool> matcher, out string matchedValue)
    {
        matchedValue = string.Empty;
        if (json == null) {
            return false;
        }

        foreach (string fieldName in OgsTerminalTextFieldNames) {
            string value = json[fieldName]?.ToString();
            if (!string.IsNullOrWhiteSpace(value) && matcher(value)) {
                matchedValue = value;
                return true;
            }
        }
        return false;
    }

    private static string ReadFirstOgsTerminalText(JObject gameData)
    {
        string value = ReadFirstTerminalText(gameData);
        if (!string.IsNullOrWhiteSpace(value)) {
            return value;
        }

        JObject payload = GetOgsTerminalPayload(gameData);
        return payload != gameData ? ReadFirstTerminalText(payload) : string.Empty;
    }

    private static string ReadFirstTerminalText(JObject json)
    {
        if (json == null) {
            return string.Empty;
        }

        foreach (string fieldName in OgsTerminalTextFieldNames) {
            string value = json[fieldName]?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }
        return string.Empty;
    }

    private static bool IsOgsResignText(string value)
    {
        string normalized = NormalizeOgsTerminalText(value);
        return normalized.Contains("resign") ||
            normalized.Contains("+r");
    }

    private static bool IsOgsTimeoutText(string value)
    {
        string normalized = NormalizeOgsTerminalText(value);
        return normalized == "time" ||
            normalized.Contains("timeout") ||
            normalized.Contains("time out") ||
            normalized.Contains("timed out") ||
            normalized.Contains("time loss") ||
            normalized.Contains("out of time") ||
            normalized.Contains("on time") ||
            normalized.Contains("+t");
    }

    private static bool IsOgsScoreText(string value)
    {
        string normalized = NormalizeOgsTerminalText(value);
        return normalized.Contains("point") ||
            normalized.Contains("score") ||
            normalized.Contains("jigo") ||
            normalized.Contains("draw") ||
            normalized.Contains("+0") ||
            normalized.Contains("+1") ||
            normalized.Contains("+2") ||
            normalized.Contains("+3") ||
            normalized.Contains("+4") ||
            normalized.Contains("+5") ||
            normalized.Contains("+6") ||
            normalized.Contains("+7") ||
            normalized.Contains("+8") ||
            normalized.Contains("+9");
    }

    private static string NormalizeOgsTerminalText(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace("_", " ").Replace("-", " ");
    }

    private static bool HasOgsScorePayload(JObject gameData)
    {
        JObject payload = GetOgsTerminalPayload(gameData);
        JObject score = payload?["score"] as JObject ?? gameData?["score"] as JObject;
        return score?["black"] != null || score?["white"] != null;
    }

    private static int ReadFirstOgsInt(JObject gameData, int defaultValue, params string[] fieldNames)
    {
        int value = ReadFirstInt(gameData, defaultValue, fieldNames);
        if (value != defaultValue) {
            return value;
        }

        JObject payload = GetOgsTerminalPayload(gameData);
        return payload != gameData ? ReadFirstInt(payload, defaultValue, fieldNames) : defaultValue;
    }

    private static bool ReadFirstOgsBool(JObject gameData, bool defaultValue, params string[] fieldNames)
    {
        bool value = ReadFirstBool(gameData, defaultValue, fieldNames);
        if (value != defaultValue) {
            return value;
        }

        JObject payload = GetOgsTerminalPayload(gameData);
        return payload != gameData ? ReadFirstBool(payload, defaultValue, fieldNames) : defaultValue;
    }

    private static JObject GetOgsTerminalPayload(JObject gameData)
    {
        return gameData?["gamedata"] as JObject ?? gameData;
    }

    private static JToken ReadFirstToken(JObject json, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return null;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token != null && token.Type != JTokenType.Null) {
                return token;
            }
        }

        return null;
    }

    private static bool TryResolvePlayerFlagFromText(string value, out PlayerFlag playerFlag)
    {
        playerFlag = 0;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        if (string.Equals(value, "black", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "b", StringComparison.OrdinalIgnoreCase)) {
            playerFlag = PlayerFlag.Player1;
            return true;
        }
        if (string.Equals(value, "white", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "w", StringComparison.OrdinalIgnoreCase)) {
            playerFlag = PlayerFlag.Player2;
            return true;
        }

        return false;
    }

    private static int ReadFirstInt(JObject json, int defaultValue, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return defaultValue;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token != null && int.TryParse(token.ToString(), out int value)) {
                return value;
            }
        }

        return defaultValue;
    }

    private static bool ReadFirstBool(JObject json, bool defaultValue, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return defaultValue;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token == null || token.Type == JTokenType.Null) {
                continue;
            }

            if (token.Type == JTokenType.Boolean) {
                return token.Value<bool>();
            }

            if (bool.TryParse(token.ToString(), out bool value)) {
                return value;
            }
        }

        return defaultValue;
    }

    private static int ReadFirstInt(JToken token, int defaultValue)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return defaultValue;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.String) {
            return int.TryParse(token.ToString(), out int value) ? value : defaultValue;
        }

        return defaultValue;
    }

    private static int ReadMoveNumber(JToken token, int defaultValue)
    {
        if (token == null || token.Type == JTokenType.Null) {
            return defaultValue;
        }

        if (int.TryParse(token.ToString(), out int directValue)) {
            return directValue;
        }

        if (token is JObject obj) {
            return ReadFirstInt(obj, defaultValue, "move_number", "moveNumber", "move");
        }

        return defaultValue;
    }

    private static int ReadUndoMoveCount(JToken token, int defaultValue)
    {
        if (token is JObject obj) {
            return Math.Max(1, ReadFirstInt(obj, defaultValue, "undo_move_count", "undoMoveCount"));
        }

        return Math.Max(1, defaultValue);
    }

    private static int ReadRequesterUserId(JToken token)
    {
        if (token is JObject obj) {
            return ReadFirstInt(obj, 0, "player_id", "requester_id", "requester", "user_id", "user");
        }

        return 0;
    }

    private static bool HasNonEmptyField(JObject json, string fieldName)
    {
        JToken token = json?[fieldName];
        return token != null && token.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(token.ToString());
    }

    private static float ReadFirstFloat(JObject json, float defaultValue, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return defaultValue;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token != null && float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) {
                return value;
            }
        }

        return defaultValue;
    }

    private static double ReadFirstDouble(JObject json, double defaultValue, params string[] fieldNames)
    {
        if (json == null || fieldNames == null) {
            return defaultValue;
        }

        foreach (string fieldName in fieldNames) {
            JToken token = json[fieldName];
            if (token != null && double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) {
                return value;
            }
        }

        return defaultValue;
    }

    private static readonly string[] OgsTerminalTextFieldNames = {
        "ended_reason",
        "endedReason",
        "termination",
        "termination_reason",
        "terminationReason",
        "result",
        "reason",
        "win_reason",
        "winReason",
        "outcome"
    };

    private readonly struct OgsGameEndResolution
    {
        public readonly string reason;
        public readonly PlayerFlag loserFlag;
        public readonly string source;

        public OgsGameEndResolution(string reason, PlayerFlag loserFlag, string source)
        {
            this.reason = string.IsNullOrWhiteSpace(reason) ? DuelGameEndReason.Score : reason;
            this.loserFlag = loserFlag;
            this.source = source ?? string.Empty;
        }
    }

    private readonly struct OgsClockPlayerTime
    {
        public static readonly OgsClockPlayerTime Empty = new OgsClockPlayerTime(0, 0, 0, false);

        public readonly int holdLeftSeconds;
        public readonly int byoyomiLeftCount;
        public readonly int byoyomiLeftSeconds;
        public readonly bool isInByoyomi;

        public OgsClockPlayerTime(int holdLeftSeconds, int byoyomiLeftCount, int byoyomiLeftSeconds, bool isInByoyomi)
        {
            this.holdLeftSeconds = Math.Max(0, holdLeftSeconds);
            this.byoyomiLeftCount = Math.Max(0, byoyomiLeftCount);
            this.byoyomiLeftSeconds = Math.Max(0, byoyomiLeftSeconds);
            this.isInByoyomi = isInByoyomi;
        }

        public bool HasByoyomi => byoyomiLeftCount > 0 || byoyomiLeftSeconds > 0;
    }
}
