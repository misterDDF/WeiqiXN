using System;
using System.Collections.Generic;
using XNClient.ChessBoard;
using XNClient.Logger;

public class DuelSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelSystem>();
    private const string DEFAULT_HOLD_TIME_CFG_ID = "5m";
    private const string DEFAULT_BYOYOMI_COUNT_CFG_ID = "off";
    private const string DEFAULT_BYOYOMI_TIME_CFG_ID = "30s";
    private const float KOMI = KataGoDuelRecordFile.Komi;

    private static readonly int[] DirX = { 0, 0, 1, -1 };
    private static readonly int[] DirZ = { 1, -1, 0, 0 };

    public DuelSystem(DuelScene scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnRequestDuelScore>(OnRequestDuelScore);
        scene.RegisterSystemEvent<OnConfirmDuelScore>(OnConfirmDuelScore);
        scene.RegisterSystemEvent<OnConfirmDuelResign>(OnConfirmDuelResign);
        scene.RegisterSystemEvent<OnRequestDuelPass>(OnRequestDuelPass);

        // 非读档进来的需要手动初始化
        if (scene.sceneCreateParams.saveFilePath == null) {
            var compDuel = scene.GetComponent<SceneComponentDuel>();
            if (compDuel != null) {
                InitTimeControlConfig(compDuel);

                string player1Guid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Player>());
                Player player1 = EntityUtils.CreatePlayer(scene, player1Guid, PlayerFlag.Player1);
                compDuel.player1Guid.value = player1Guid;
                string player2Guid = EntityUtils.CreateGuidWithEntityType(EntityBase.GetEntityType<Player>());
                Player player2 = EntityUtils.CreatePlayer(scene, player2Guid, PlayerFlag.Player2);
                compDuel.player2Guid.value = player2Guid;
                compDuel.curTurnPlayerGuid.value = player1Guid;
                InitPlayerTimeControl(compDuel, player1);
                InitPlayerTimeControl(compDuel, player2);

                compDuel.duelFSM.Activate();
            }
        } else {
            var compDuel = scene.GetComponent<SceneComponentDuel>();
            if (compDuel != null) {
                EnsureTimeControlConfig(compDuel);

                Player player1 = EntityUtils.CreatePlayer(scene, compDuel.player1Guid.value, PlayerFlag.Player1);
                Player player2 = EntityUtils.CreatePlayer(scene, compDuel.player2Guid.value, PlayerFlag.Player2);

                compDuel.duelFSM.Activate(DuelStateDefine.STATE_TURN_INPUT);
            }
        }
    }

    private void InitTimeControlConfig(SceneComponentDuel compDuel)
    {
        var duelParams = scene.sceneCreateParams.duelSceneCreateParamas;
        compDuel.holdTimeCfgId.value = GetValidHoldTimeCfgId(duelParams?.holdTimeCfgId);
        compDuel.byoyomiCountCfgId.value = GetValidByoyomiCountCfgId(duelParams?.byoyomiCountCfgId);
        compDuel.byoyomiTimeCfgId.value = GetValidByoyomiTimeCfgId(duelParams?.byoyomiTimeCfgId);
    }

    private void EnsureTimeControlConfig(SceneComponentDuel compDuel)
    {
        compDuel.holdTimeCfgId.value = GetValidHoldTimeCfgId(compDuel.holdTimeCfgId.value);
        compDuel.byoyomiCountCfgId.value = GetValidByoyomiCountCfgId(compDuel.byoyomiCountCfgId.value);
        compDuel.byoyomiTimeCfgId.value = GetValidByoyomiTimeCfgId(compDuel.byoyomiTimeCfgId.value);
    }

    private string GetValidHoldTimeCfgId(string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && DuelHoldTimeDataType.GetConfigData(cfgId) != null) {
            return cfgId;
        }
        return DEFAULT_HOLD_TIME_CFG_ID;
    }

    private string GetValidByoyomiCountCfgId(string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && DuelByoyomiCountDataType.GetConfigData(cfgId) != null) {
            return cfgId;
        }
        return DEFAULT_BYOYOMI_COUNT_CFG_ID;
    }

    private string GetValidByoyomiTimeCfgId(string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && DuelByoyomiTimeDataType.GetConfigData(cfgId) != null) {
            return cfgId;
        }
        return DEFAULT_BYOYOMI_TIME_CFG_ID;
    }

    private void InitPlayerTimeControl(SceneComponentDuel compDuel, Player player)
    {
        if (player == null) {
            return;
        }

        var compDuelInfo = player.GetComponent<ComponentDuelInfo>();
        var holdTimeData = DuelHoldTimeDataType.GetConfigData(compDuel.holdTimeCfgId.value);
        var byoyomiCountData = DuelByoyomiCountDataType.GetConfigData(compDuel.byoyomiCountCfgId.value);
        var byoyomiTimeData = DuelByoyomiTimeDataType.GetConfigData(compDuel.byoyomiTimeCfgId.value);
        if (compDuelInfo == null || holdTimeData == null || byoyomiCountData == null || byoyomiTimeData == null) {
            return;
        }

        compDuelInfo.isInfiniteTime.value = holdTimeData.isInfinite;
        compDuelInfo.holdLeftSeconds.value = holdTimeData.isInfinite ? -1 : holdTimeData.holdSeconds;
        compDuelInfo.byoyomiLeftCount.value = byoyomiCountData.count;
        compDuelInfo.byoyomiLeftSeconds.value = byoyomiTimeData.seconds;
        compDuelInfo.isInByoyomi.value = false;
        compDuelInfo.turnLeftTimes.value = holdTimeData.isInfinite ? -1 : holdTimeData.holdSeconds;
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.duelFSM.isActivated) {
            compDuel.duelFSM.Update();
        }
    }

    public void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel != null && compDuel.duelFSM.isActivated) {
            compDuel.consecutivePassCount.value = 0;
            compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
        }
    }

    private void OnRequestDuelScore(OnRequestDuelScore evt)
    {
        DuelScoreResult scoreResult = CalculateScoreResult();
        if (scoreResult == null) {
            return;
        }

        scene.EmitSystemEvent(new OnDuelScoreResult(scoreResult, true));
    }

    private void OnConfirmDuelScore(OnConfirmDuelScore evt)
    {
        EndGameByScore(evt.scoreResult, DuelGameEndReason.Score);
    }

    private void OnConfirmDuelResign(OnConfirmDuelResign evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_GAME_END) {
            return;
        }

        string loserGuid = compDuel.curTurnPlayerGuid.value;
        if (string.IsNullOrEmpty(loserGuid)) {
            return;
        }

        string winnerGuid = loserGuid == compDuel.player1Guid.value
            ? compDuel.player2Guid.value
            : compDuel.player1Guid.value;

        compDuel.resignLoserGuid.value = loserGuid;
        compDuel.winnerGuid.value = winnerGuid;
        compDuel.gameEndReason.value = DuelGameEndReason.Resign;
        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

    private void OnRequestDuelPass(OnRequestDuelPass evt)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compDuel == null || compChessBoard == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            return;
        }

        compDuel.AppendKataGoPass((PlayerFlag)curPlayer.playerFlag.value);
        compDuel.consecutivePassCount.value += 1;
        if (compDuel.consecutivePassCount.value >= 2) {
            DuelScoreResult scoreResult = CalculateScoreResult();
            if (scoreResult == null) {
                return;
            }

            scene.EmitSystemEvent(new OnDuelScoreResult(scoreResult, false));
            EndGameByScore(scoreResult, DuelGameEndReason.ConsecutivePass);
            return;
        }

        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
    }

    private DuelScoreResult CalculateScoreResult()
    {
        var compChessBoard = scene.GetComponent<SceneComponentChessBoard>();
        if (compChessBoard?.chessBoardGrid == null) {
            XNLogger.LogError("Duel score failed, chess board grid is missing.");
            return null;
        }

        int boardSize = compChessBoard.chessBoardGrid.gridSize;
        int maxSize = compChessBoard.GetGridMaxSize();
        bool[] visited = new bool[maxSize];
        float blackScore = 0f;
        float whiteScore = KOMI;

        for (int posIndex = 0; posIndex < maxSize; posIndex++) {
            if (compChessBoard.chessInfoDict.TryGetValue(posIndex.ToString(), out ChessInfo chessInfo) && chessInfo != null) {
                if (chessInfo.chessFlag.value == (int)PlayerFlag.Player1) {
                    blackScore += 1f;
                } else if (chessInfo.chessFlag.value == (int)PlayerFlag.Player2) {
                    whiteScore += 1f;
                }
                continue;
            }

            if (visited[posIndex]) {
                continue;
            }

            EmptyRegion region = CollectEmptyRegion(compChessBoard, posIndex, boardSize, visited);
            if (region.adjacentBlack && !region.adjacentWhite) {
                blackScore += region.emptyCount;
            } else if (region.adjacentWhite && !region.adjacentBlack) {
                whiteScore += region.emptyCount;
            }
        }

        float margin = Math.Abs(blackScore - whiteScore);
        PlayerFlag winnerFlag = 0;
        if (blackScore > whiteScore) {
            winnerFlag = PlayerFlag.Player1;
        } else if (whiteScore > blackScore) {
            winnerFlag = PlayerFlag.Player2;
        }

        return new DuelScoreResult
        {
            blackScore = blackScore,
            whiteScore = whiteScore,
            komi = KOMI,
            margin = margin,
            winnerFlag = winnerFlag,
        };
    }

    private EmptyRegion CollectEmptyRegion(SceneComponentChessBoard compChessBoard, int startIndex, int boardSize, bool[] visited)
    {
        EmptyRegion region = new EmptyRegion();
        Queue<int> queue = new Queue<int>();

        queue.Enqueue(startIndex);
        visited[startIndex] = true;

        while (queue.Count > 0) {
            int curIndex = queue.Dequeue();
            region.emptyCount += 1;
            int curX = curIndex % boardSize;
            int curZ = curIndex / boardSize;

            for (int dir = 0; dir < Math.Min(DirX.Length, DirZ.Length); dir++) {
                int nx = curX + DirX[dir];
                int nz = curZ + DirZ[dir];
                int neighborIndex = compChessBoard.GetPosIndexByCoords(new RectCoordinates(nx, nz));
                if (neighborIndex < 0) {
                    continue;
                }

                if (compChessBoard.chessInfoDict.TryGetValue(neighborIndex.ToString(), out ChessInfo neighborChessInfo) && neighborChessInfo != null) {
                    if (neighborChessInfo.chessFlag.value == (int)PlayerFlag.Player1) {
                        region.adjacentBlack = true;
                    } else if (neighborChessInfo.chessFlag.value == (int)PlayerFlag.Player2) {
                        region.adjacentWhite = true;
                    }
                    continue;
                }

                if (visited[neighborIndex]) {
                    continue;
                }

                visited[neighborIndex] = true;
                queue.Enqueue(neighborIndex);
            }
        }

        return region;
    }

    private void EndGameByScore(DuelScoreResult scoreResult, string reason)
    {
        var compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null || scoreResult == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return;
        }

        compDuel.finalBlackScore.value = scoreResult.blackScore;
        compDuel.finalWhiteScore.value = scoreResult.whiteScore;
        compDuel.finalScoreMargin.value = scoreResult.margin;
        compDuel.gameEndReason.value = reason;

        if (scoreResult.winnerFlag == PlayerFlag.Player1) {
            compDuel.winnerGuid.value = compDuel.player1Guid.value;
        } else if (scoreResult.winnerFlag == PlayerFlag.Player2) {
            compDuel.winnerGuid.value = compDuel.player2Guid.value;
        } else {
            compDuel.winnerGuid.value = string.Empty;
        }

        compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

    private struct EmptyRegion
    {
        public int emptyCount;
        public bool adjacentBlack;
        public bool adjacentWhite;
    }
}
