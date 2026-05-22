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

        PlayerFlag curPlayerFlag = (PlayerFlag)curPlayer.playerFlag.value;
        if (compDuel.isLanDuel.value) {
            return CanLocalLanRoleControl(compDuel, curPlayerFlag)
                ? new DuelInputAuthorityState(curPlayerFlag)
                : default;
        }

        if (compDuel.isAiDuel.value && curPlayer.guid == compDuel.aiPlayerGuid.value) {
            return default;
        }

        return new DuelInputAuthorityState(curPlayerFlag);
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

    private static bool CanLocalLanRoleControl(SceneComponentDuel compDuel, PlayerFlag curPlayerFlag)
    {
        LanRoomRole role = (LanRoomRole)compDuel.lanRole.value;
        return (role == LanRoomRole.Host && curPlayerFlag == PlayerFlag.Player1) ||
            (role == LanRoomRole.Client && curPlayerFlag == PlayerFlag.Player2);
    }
}
