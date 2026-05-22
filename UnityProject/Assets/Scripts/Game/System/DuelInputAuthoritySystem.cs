public class DuelInputAuthoritySystem : SystemBase
{
    public override string systemName => GetSystemName<DuelInputAuthoritySystem>();

    public DuelInputAuthoritySystem(DuelScene scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnDuelStateChanged>(OnDuelStateChanged);
        RefreshLocalInputAuthority();
    }

    private void OnDuelStateChanged(OnDuelStateChanged evt)
    {
        RefreshLocalInputAuthority();
    }

    public void RefreshLocalInputAuthority()
    {
        SceneComponentDuel compDuel = scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

        if (compDuel.isLanDuel.value) {
            RefreshLanInputAuthority(compDuel);
            return;
        }

        compDuel.localInputPlayerFlag.value = (int)ResolveLocalInputPlayerFlag(compDuel);
    }

    private void RefreshLanInputAuthority(SceneComponentDuel compDuel)
    {
        if ((LanRoomRole)compDuel.lanRole.value != LanRoomRole.Host || Global.Instance.lanRoomService == null) {
            return;
        }

        PlayerFlag curTurnPlayerFlag = ResolveCurrentTurnPlayerFlag(compDuel);
        PlayerFlag hostPlayerFlag = DuelUtils.GetValidPlayerFlag((PlayerFlag)compDuel.lanHostPlayerFlag.value);
        PlayerFlag clientPlayerFlag = hostPlayerFlag.GetOpponentPlayerFlag();
        PlayerFlag hostInputFlag = curTurnPlayerFlag == hostPlayerFlag ? hostPlayerFlag : 0;
        PlayerFlag clientInputFlag = curTurnPlayerFlag == clientPlayerFlag ? clientPlayerFlag : 0;
        compDuel.localInputPlayerFlag.value = (int)hostInputFlag;
        Global.Instance.lanRoomService.BroadcastInputAuthority(hostInputFlag, clientInputFlag);
    }

    private PlayerFlag ResolveLocalInputPlayerFlag(SceneComponentDuel compDuel)
    {
        if (!CanAcceptTurnInputState(compDuel)) {
            return 0;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer == null) {
            return 0;
        }

        if (compDuel.isAiDuel.value && curPlayer.guid == compDuel.aiPlayerGuid.value) {
            return 0;
        }

        return (PlayerFlag)curPlayer.playerFlag.value;
    }

    private PlayerFlag ResolveCurrentTurnPlayerFlag(SceneComponentDuel compDuel)
    {
        if (!CanAcceptTurnInputState(compDuel)) {
            return 0;
        }

        Player curPlayer = scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        return curPlayer != null ? (PlayerFlag)curPlayer.playerFlag.value : 0;
    }

    private bool CanAcceptTurnInputState(SceneComponentDuel compDuel)
    {
        return compDuel.duelFSM != null
            && compDuel.duelFSM.isActivated
            && !compDuel.isScoring
            && compDuel.duelFSM.curState != null
            && compDuel.duelFSM.curState.stateName == DuelStateDefine.STATE_TURN_INPUT
            && !string.IsNullOrEmpty(compDuel.curTurnPlayerGuid.value);
    }
}
