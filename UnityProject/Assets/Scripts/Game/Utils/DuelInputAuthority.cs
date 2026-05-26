public readonly struct DuelInputAuthorityState
{
    public readonly PlayerFlag localInputPlayerFlag;

    public DuelInputAuthorityState(PlayerFlag localInputPlayerFlag)
    {
        this.localInputPlayerFlag = localInputPlayerFlag;
    }

    public bool CanSubmitMove => localInputPlayerFlag != 0;
}

public static class DuelInputAuthority
{
    public static DuelInputAuthorityState GetLocalState(SceneBase scene, SceneComponentDuel compDuel)
    {
        if (scene == null || compDuel == null || !CanAcceptTurnInputState(compDuel)) {
            return default;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            return default;
        }

        PlayerFlag localInputPlayerFlag = (PlayerFlag)compDuel.localInputPlayerFlag.value;
        if (localInputPlayerFlag == 0) {
            return default;
        }

        PlayerFlag curPlayerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        return localInputPlayerFlag == curPlayerFlag
            ? new DuelInputAuthorityState(localInputPlayerFlag)
            : default;
    }

    private static bool CanAcceptTurnInputState(SceneComponentDuel compDuel)
    {
        return compDuel.duelFSM != null
            && compDuel.duelFSM.isActivated
            && !compDuel.isScoring
            && compDuel.duelFSM.curState != null
            && compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_TURN_INPUT
            && !string.IsNullOrEmpty(compDuel.curTurnPlayerGuid.value);
    }

}

public static class DuelLanTakeBackRule
{
    public static bool TryGetRequiredRemoveCount(SceneBase scene, SceneComponentDuel compDuel, PlayerFlag requesterFlag, out int removeCount)
    {
        removeCount = 0;
        if (!CanEvaluate(scene, compDuel, requesterFlag)) {
            return false;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            return false;
        }

        PlayerFlag curTurnPlayerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        removeCount = curTurnPlayerFlag == requesterFlag ? 2 : 1;
        return removeCount > 0;
    }

    public static bool CanTakeBack(SceneBase scene, SceneComponentDuel compDuel, PlayerFlag requesterFlag)
    {
        return TryGetRequiredRemoveCount(scene, compDuel, requesterFlag, out int requiredRemoveCount)
            && DuelMoveHistory.Count(compDuel.kataGoMoves) >= requiredRemoveCount;
    }

    private static bool CanEvaluate(SceneBase scene, SceneComponentDuel compDuel, PlayerFlag requesterFlag)
    {
        if (scene == null || compDuel == null || !compDuel.isLanDuel.value || requesterFlag == 0) {
            return false;
        }

        if (compDuel.duelFSM == null || !compDuel.duelFSM.isActivated || compDuel.isScoring) {
            return false;
        }

        if (compDuel.duelFSM.curState == null || compDuel.duelFSM.curState.stateName != DuelStateDefine.STATE_TURN_INPUT) {
            return false;
        }

        if (string.IsNullOrEmpty(compDuel.curTurnPlayerGuid.value)) {
            return false;
        }

        return requesterFlag == PlayerFlag.Player1 || requesterFlag == PlayerFlag.Player2;
    }
}
