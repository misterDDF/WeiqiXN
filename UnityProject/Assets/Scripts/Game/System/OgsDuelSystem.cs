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

        while (realtimeSession != null && realtimeSession.TryDequeueMessage(out OgsRealtimeGameMessage message)) {
            HandleRealtimeMessage(message);
        }
    }

    public override void OnDestroy()
    {
        cancellationTokenSource?.Cancel();
        if (realtimeSession != null) {
            _ = realtimeSession.DisconnectAsync();
            realtimeSession.Dispose();
            realtimeSession = null;
        }
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
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
        if (compOgsDuel == null || compOgsDuel.gameId <= 0) {
            SetError("OGS game id is empty.");
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        compOgsDuel.isConnecting = true;
        try {
            realtimeSession = await Global.Instance.ogsConnectionService.CreateRealtimeGameSessionAsync(
                compOgsDuel.gameId,
                OgsConnectionConfig.DefaultWebSocketUrl,
                cancellationTokenSource.Token);
            compOgsDuel.isConnected = true;
            compOgsDuel.lastError = string.Empty;
        }
        catch (Exception ex) {
            SetError(ex.Message);
            XNLogger.LogError("OGS duel realtime connect failed.", ("gameId", compOgsDuel.gameId.ToString()), ("err", ex.Message));
        }
        finally {
            if (compOgsDuel != null) {
                compOgsDuel.isConnecting = false;
            }
        }
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
            case OgsRealtimeGameMessageType.Error:
                SetError(message.message);
                break;
            case OgsRealtimeGameMessageType.Closed:
                compOgsDuel.isConnected = false;
                if (!string.IsNullOrEmpty(message.message)) {
                    compOgsDuel.lastError = message.message;
                }
                SyncInputAuthority();
                break;
        }
    }

    public bool CanSubmitResign()
    {
        return CanSubmitOgsCommand();
    }

    public bool CanSubmitTakeBack()
    {
        return CanSubmitOgsCommand()
            && !compOgsDuel.isBotGame
            && !hasPendingUndoRequest
            && compOgsDuel.acceptedMoveCount > 0
            && !IsStoneRemovalPhase();
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
        if (!RebuildBoardFromMoves(acceptedInitialStones, acceptedMoves)) {
            SetError("OGS game data board state could not be applied.");
            return;
        }
        compOgsDuel.acceptedMoveCount = moves.Count;
        compOgsDuel.isSubmitting = false;
        if (TryBuildOgsScoreResult(gameData, out DuelScoreResult scoreResult)) {
            EndGameByOgsScore(scoreResult);
        } else if (IsFinishedPhase()) {
            EndGameByOgsFinishedFallback();
        } else {
            SyncTurnAndInputFromMoveCount(moves.Count);
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

    private PlayerFlag ResolveOgsFirstMovePlayerFlag(JObject gameData, int handicapCount, int initialStoneCount)
    {
        PlayerFlag initialPlayerFlag = ResolveOgsInitialPlayerFlag(gameData);
        if (ResolveOgsOpeningSameColorMoveCount(gameData, initialStoneCount) > 0) {
            return initialPlayerFlag;
        }

        return initialStoneCount > 0 || handicapCount > 1
            ? initialPlayerFlag.GetOpponentPlayerFlag()
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
        if (compOgsDuel == null) {
            return;
        }

        compOgsDuel.phase = phase ?? string.Empty;
        if (IsFinishedPhase()) {
            compDuel.localInputPlayerFlag.value = 0;
            compDuel.gameEndReason.value = DuelGameEndReason.Score;
            compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
        } else {
            SyncInputAuthority();
        }
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

    private async Task SubmitResignAsync()
    {
        if (realtimeSession == null || !realtimeSession.IsOpen || compOgsDuel == null) {
            SetError("OGS realtime session is not connected.");
            return;
        }

        try {
            compOgsDuel.isSubmitting = true;
            SyncInputAuthority();
            await realtimeSession.SendResignAsync(cancellationTokenSource.Token);
            XNLogger.LogInfo("OGS resign submitted.", ("gameId", compOgsDuel.gameId.ToString()));
        }
        catch (Exception ex) {
            compOgsDuel.isSubmitting = false;
            SetError(ex.Message);
            XNLogger.LogError("OGS resign submit failed.", ("gameId", compOgsDuel.gameId.ToString()), ("err", ex.Message));
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
        if (compDuel == null || compOgsDuel == null || !compOgsDuel.isConnected || compOgsDuel.isSubmitting || IsFinishedPhase() || IsPausedPhase()) {
            return false;
        }

        return compDuel.localPlayerFlag.value != 0;
    }

    private void SyncTurnAndInputFromMoveCount(int moveCount)
    {
        if (compDuel == null) {
            return;
        }

        PlayerFlag currentTurnPlayerFlag = ResolveCurrentTurnPlayerFlag(moveCount);
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

        JObject score = gameData["score"] as JObject;
        bool hasFinishedScore = IsFinishedPhase() || HasNonEmptyField(gameData, "end_time") || HasNonEmptyField(gameData, "outcome");
        if (!hasFinishedScore) {
            return false;
        }

        float blackScore = ReadFirstFloat(score?["black"] as JObject, 0f, "total");
        float whiteScore = ReadFirstFloat(score?["white"] as JObject, 0f, "total");
        float komi = ReadFirstFloat(gameData, 0f, "komi");
        PlayerFlag winnerFlag = ResolveWinnerFlag(gameData, blackScore, whiteScore);
        float margin = Math.Abs(blackScore - whiteScore);
        if (margin <= 0f) {
            margin = ParseOutcomeMargin(ReadFirstString(gameData, "outcome"));
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

    private void EndGameByOgsScore(DuelScoreResult scoreResult)
    {
        if (compDuel == null || scoreResult == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        compDuel.finalBlackScore.value = scoreResult.blackScore;
        compDuel.finalWhiteScore.value = scoreResult.whiteScore;
        compDuel.finalScoreMargin.value = scoreResult.margin;
        compDuel.gameEndReason.value = DuelGameEndReason.Score;
        compDuel.localInputPlayerFlag.value = 0;
        compOgsDuel.isSubmitting = false;

        if (scoreResult.winnerFlag == PlayerFlag.Player1) {
            compDuel.winnerGuid.value = compDuel.player1Guid.value;
        } else if (scoreResult.winnerFlag == PlayerFlag.Player2) {
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
            ("winnerFlag", scoreResult.winnerFlag.ToString()));
    }

    private void EndGameByOgsFinishedFallback()
    {
        var scoreResult = new DuelScoreResult
        {
            blackScore = 0f,
            whiteScore = 0f,
            komi = 0f,
            margin = 0f,
            winnerFlag = 0,
            scoreSource = "ogs",
        };
        EndGameByOgsScore(scoreResult);
    }

    private PlayerFlag ResolveWinnerFlag(JObject gameData, float blackScore, float whiteScore)
    {
        int winnerId = ReadFirstInt(gameData, 0, "winner", "winner_id");
        int blackId = ReadFirstInt(gameData, compOgsDuel.blackOgsUserId, "black_player_id", "black");
        int whiteId = ReadFirstInt(gameData, compOgsDuel.whiteOgsUserId, "white_player_id", "white");
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
}
