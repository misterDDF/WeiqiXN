public static class DuelPageInteractionState
{
    public static bool CanTakeBack(SceneComponentDuel compDuel)
    {
        if (compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated || compDuel.isScoring) {
            return false;
        }

        int moveCount = DuelMoveHistory.Count(compDuel.kataGoMoves);
        if (moveCount <= 0) {
            return false;
        }

        if (!compDuel.isAiDuel.value) {
            return true;
        }

        string humanPlayerGuid = compDuel.player1Guid.value == compDuel.aiPlayerGuid.value
            ? compDuel.player2Guid.value
            : compDuel.player1Guid.value;
        int requiredMoveCount = compDuel.curTurnPlayerGuid.value == humanPlayerGuid ? 2 : 1;
        return moveCount >= requiredMoveCount;
    }

    public static bool CanResign(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        if (mainScene == null || compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return false;
        }

        if (compDuel.isScoring) {
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        if (string.IsNullOrEmpty(compDuel.curTurnPlayerGuid.value)) {
            return false;
        }

        return CanAcceptHumanTurnInput(mainScene, compDuel)
            && mainScene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value) != null;
    }

    public static bool CanAcceptHumanTurnInput(SceneBase mainScene, SceneComponentDuel compDuel)
    {
        if (mainScene == null || compDuel == null || compDuel.duelFSM == null || !compDuel.duelFSM.isActivated) {
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        return !compDuel.isAiDuel.value
            || string.IsNullOrEmpty(compDuel.aiPlayerGuid.value)
            || compDuel.curTurnPlayerGuid.value != compDuel.aiPlayerGuid.value;
    }

    public static bool IsAiPlayer(SceneBase mainScene, PlayerFlag playerFlag)
    {
        SceneComponentDuel compDuel = mainScene?.GetComponent<SceneComponentDuel>();
        if (compDuel == null || !compDuel.isAiDuel.value || string.IsNullOrEmpty(compDuel.aiPlayerGuid.value)) {
            return false;
        }

        string playerGuid = playerFlag == PlayerFlag.Player1 ? compDuel.player1Guid.value : compDuel.player2Guid.value;
        return playerGuid == compDuel.aiPlayerGuid.value;
    }

    public static bool IsAiPlayer(Player player, SceneComponentDuel compDuel)
    {
        return player != null
            && compDuel != null
            && compDuel.isAiDuel.value
            && !string.IsNullOrEmpty(compDuel.aiPlayerGuid.value)
            && player.guid == compDuel.aiPlayerGuid.value;
    }

    public static bool IsByoyomiEnabled(SceneComponentDuel compDuel, ComponentDuelInfo compDuelInfo)
    {
        if (compDuelInfo != null && compDuelInfo.isInfiniteTime.value) {
            return false;
        }

        DuelByoyomiCountDataType byoyomiCountData = compDuel != null
            ? DuelByoyomiCountDataType.GetConfigData(compDuel.byoyomiCountCfgId.value)
            : null;
        if (byoyomiCountData != null) {
            return byoyomiCountData.count > 0;
        }

        return compDuelInfo != null && compDuelInfo.byoyomiLeftCount.value > 0;
    }
}
