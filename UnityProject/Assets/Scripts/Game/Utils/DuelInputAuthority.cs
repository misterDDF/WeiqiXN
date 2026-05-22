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
