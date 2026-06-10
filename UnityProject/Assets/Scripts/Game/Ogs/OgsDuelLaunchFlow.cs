public static class OgsDuelLaunchFlow
{
    public static OgsAutomatchCreateParams BuildAutomatchCreateParams(DuelSceneCreateParamas duelParams)
    {
        OgsDuelSetupValues values = ReadDuelSetupValues(duelParams);
        return new OgsAutomatchCreateParams(
            values.boardSize,
            values.mainTimeSeconds,
            values.byoyomiPeriods,
            values.byoyomiPeriodSeconds,
            0,
            values.ogsAutomatchSpeed,
            values.ogsAutomatchSystem);
    }

    public static OgsFriendChallengeCreateParams BuildFriendChallengeCreateParams(
        string friendUserId,
        DuelSceneCreateParamas duelParams,
        string gameName)
    {
        OgsDuelSetupValues values = ReadDuelSetupValues(duelParams);
        return new OgsFriendChallengeCreateParams(
            friendUserId,
            values.boardSize,
            values.mainTimeSeconds,
            values.byoyomiPeriods,
            values.byoyomiPeriodSeconds,
            values.handicap,
            values.komi,
            ResolveChallengerColor(duelParams?.playerSideCfgId),
            gameName);
    }

    public static void EnterOgsDuelScene(OgsBotGameStartResult result, DuelSceneCreateParamas duelParams = null)
    {
        if (result == null || result.gameId <= 0) {
            return;
        }

        OgsGameStateSmokeResult gameState = result.gameState;
        int boardSize = gameState != null && gameState.boardWidth > 0 && gameState.boardWidth == gameState.boardHeight
            ? gameState.boardWidth
            : OgsConnectionConfig.DefaultBotGameBoardSize;
        string boardCfgId = $"{boardSize}x{boardSize}";
        SceneCreateParams sceneCreateParams = new SceneCreateParams
        {
            duelSceneCreateParamas = duelParams ?? new DuelSceneCreateParamas
            {
                boardCfgId = boardCfgId,
                holdTimeCfgId = "infinite",
                byoyomiCountCfgId = "off",
                byoyomiTimeCfgId = "30s",
            },
            ogsDuelSceneCreateParams = new OgsDuelSceneCreateParams
            {
                gameId = result.gameId,
                boardSize = boardSize,
                botId = result.botId,
                botName = result.botName,
                isBotGame = result.isBotGame,
                challengeId = result.challengeId,
                challengeUuid = result.challengeUuid,
            },
        };
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.OGS_DUEL_SCENE_TYPE_ID, sceneCreateParams);
    }

    private static OgsDuelSetupValues ReadDuelSetupValues(DuelSceneCreateParamas duelParams)
    {
        ChessBoardDataType boardData = ChessBoardDataType.GetConfigData(duelParams?.boardCfgId);
        DuelHoldTimeDataType holdTimeData = DuelHoldTimeDataType.GetConfigData(duelParams?.holdTimeCfgId);
        DuelByoyomiCountDataType byoyomiCountData = DuelByoyomiCountDataType.GetConfigData(duelParams?.byoyomiCountCfgId);
        DuelByoyomiTimeDataType byoyomiTimeData = DuelByoyomiTimeDataType.GetConfigData(duelParams?.byoyomiTimeCfgId);
        DuelHandicapDataType handicapData = DuelHandicapDataType.GetConfigData(duelParams?.handicapCfgId);
        OgsAutomatchTimeOptionDataType automatchOption = ResolveOgsAutomatchTimeOption(
            duelParams?.boardCfgId,
            duelParams?.holdTimeCfgId,
            duelParams?.byoyomiCountCfgId,
            duelParams?.byoyomiTimeCfgId);

        return new OgsDuelSetupValues
        {
            boardSize = boardData != null && boardData.boardSize > 0
                ? boardData.boardSize
                : OgsConnectionConfig.DefaultBotGameBoardSize,
            mainTimeSeconds = holdTimeData != null ? holdTimeData.holdSeconds : 600,
            byoyomiPeriods = byoyomiCountData != null ? byoyomiCountData.count : 0,
            byoyomiPeriodSeconds = byoyomiTimeData != null ? byoyomiTimeData.seconds : 30,
            handicap = handicapData != null ? handicapData.handicapCount : 0,
            komi = handicapData != null ? handicapData.komi : 7.5f,
            ogsAutomatchSpeed = automatchOption?.speed ?? string.Empty,
            ogsAutomatchSystem = automatchOption?.system ?? string.Empty,
        };
    }

    private static OgsAutomatchTimeOptionDataType ResolveOgsAutomatchTimeOption(
        string boardCfgId,
        string holdTimeCfgId,
        string byoyomiCountCfgId,
        string byoyomiTimeCfgId)
    {
        OgsAutomatchTimeOptionDataType.GetConfigData(string.Empty);
        if (OgsAutomatchTimeOptionDataType.OgsAutomatchTimeOptionDict == null) {
            return null;
        }

        foreach (OgsAutomatchTimeOptionDataType option in OgsAutomatchTimeOptionDataType.OgsAutomatchTimeOptionDict.Values) {
            if (option == null || !option.enabled) {
                continue;
            }
            if (option.boardCfgId == boardCfgId
                && option.holdTimeCfgId == holdTimeCfgId
                && option.byoyomiCountCfgId == byoyomiCountCfgId
                && option.byoyomiTimeCfgId == byoyomiTimeCfgId) {
                return option;
            }
        }

        return null;
    }

    private static string ResolveChallengerColor(string playerSideCfgId)
    {
        switch (playerSideCfgId) {
            case "black":
                return "black";
            case "white":
                return "white";
            case "guess":
            default:
                return "automatic";
        }
    }

    private struct OgsDuelSetupValues
    {
        public int boardSize;
        public int mainTimeSeconds;
        public int byoyomiPeriods;
        public int byoyomiPeriodSeconds;
        public int handicap;
        public float komi;
        public string ogsAutomatchSpeed;
        public string ogsAutomatchSystem;
    }
}
