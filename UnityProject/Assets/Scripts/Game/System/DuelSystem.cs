public class DuelSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelSystem>();
    private const string DEFAULT_HOLD_TIME_CFG_ID = "5m";
    private const string DEFAULT_BYOYOMI_COUNT_CFG_ID = "off";
    private const string DEFAULT_BYOYOMI_TIME_CFG_ID = "30s";

    public DuelSystem(DuelScene scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();

        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);

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
            compDuel.duelFSM.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
        }
    }
}
