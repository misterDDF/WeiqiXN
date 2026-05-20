public class DuelStateTurnInput : DuelFSMState
{
    public override string stateName => DuelStateDefine.STATE_TURN_INPUT;
    private SecondIntervalTimer turnTimer;
    private bool hasTimedOut;

    public DuelStateTurnInput(DuelFSM fsm) : base(fsm)
    {

    }

    public override void OnEnterState()
    {
        base.OnEnterState();
        hasTimedOut = false;

        var compDuel = fsm.scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) return;

        Player curPlayer = fsm.scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer != null) {
            var compDuelInfo = curPlayer.GetComponent<ComponentDuelInfo>();
            if (compDuelInfo != null && compDuelInfo.isInByoyomi.value) {
                ResetByoyomiSeconds(compDuelInfo);
            }
            RefreshTurnLeftTimes(compDuelInfo);
            if (compDuelInfo != null && !compDuelInfo.isInfiniteTime.value) {
                turnTimer = fsm.scene.SetSecondInterval(1, OnTurnPassSecond);
            }
        }
    }

    public override void OnUpdateState()
    {
        base.OnUpdateState();

        var compDuel = fsm.scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) return;

        Player curPlayer = fsm.scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer != null) {
            var compDuelInfo = curPlayer.GetComponent<ComponentDuelInfo>();
            CheckTimeout(compDuel, compDuelInfo);
        }
    }

    public override void OnExitState()
    {
        base.OnExitState();

        if (turnTimer != null) {
            turnTimer.StopTimer();
        }
    }

    public void OnTurnPassSecond()
    {
        var compDuel = fsm.scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) return;

        Player curPlayer = fsm.scene.GetEntity<Player>(compDuel.curTurnPlayerGuid.value);
        if (curPlayer != null) {
            var compDuelInfo = curPlayer.GetComponent<ComponentDuelInfo>();
            if (compDuelInfo != null) {
                TickTimeControl(compDuelInfo);
                CheckTimeout(compDuel, compDuelInfo);
            }
        }
    }

    private void TickTimeControl(ComponentDuelInfo compDuelInfo)
    {
        if (compDuelInfo == null || compDuelInfo.isInfiniteTime.value || hasTimedOut) {
            return;
        }

        if (compDuelInfo.isInByoyomi.value) {
            compDuelInfo.byoyomiLeftSeconds.value -= 1;
        } else {
            compDuelInfo.holdLeftSeconds.value -= 1;
            if (compDuelInfo.holdLeftSeconds.value <= 0 && compDuelInfo.byoyomiLeftCount.value > 0) {
                compDuelInfo.holdLeftSeconds.value = 0;
                compDuelInfo.isInByoyomi.value = true;
                ResetByoyomiSeconds(compDuelInfo);
            }
        }

        RefreshTurnLeftTimes(compDuelInfo);
    }

    private void CheckTimeout(SceneComponentDuel compDuel, ComponentDuelInfo compDuelInfo)
    {
        if (compDuel == null || compDuelInfo == null || compDuelInfo.isInfiniteTime.value || hasTimedOut) {
            return;
        }

        if (!compDuelInfo.isInByoyomi.value && compDuelInfo.holdLeftSeconds.value <= 0 && compDuelInfo.byoyomiLeftCount.value <= 0) {
            TriggerTimeoutLose(compDuel);
            return;
        }

        if (compDuelInfo.isInByoyomi.value && compDuelInfo.byoyomiLeftSeconds.value <= 0) {
            compDuelInfo.byoyomiLeftCount.value -= 1;
            if (compDuelInfo.byoyomiLeftCount.value <= 0) {
                compDuelInfo.byoyomiLeftCount.value = 0;
                TriggerTimeoutLose(compDuel);
            } else {
                ResetByoyomiSeconds(compDuelInfo);
                RefreshTurnLeftTimes(compDuelInfo);
            }
        }
    }

    private void TriggerTimeoutLose(SceneComponentDuel compDuel)
    {
        hasTimedOut = true;
        compDuel.timeoutLoserGuid.value = compDuel.curTurnPlayerGuid.value;
        compDuel.gameEndReason.value = DuelGameEndReason.Timeout;
        compDuel.winnerGuid.value = compDuel.curTurnPlayerGuid.value == compDuel.player1Guid.value
            ? compDuel.player2Guid.value
            : compDuel.player1Guid.value;
        fsm.SetParamterTrigger(DuelParamDefine.TRIGGER_PARAM_GAME_END);
    }

    private void ResetByoyomiSeconds(ComponentDuelInfo compDuelInfo)
    {
        var compDuel = fsm.scene.GetComponent<SceneComponentDuel>();
        if (compDuel == null) {
            return;
        }

        var byoyomiTimeData = DuelByoyomiTimeDataType.GetConfigData(compDuel.byoyomiTimeCfgId.value);
        compDuelInfo.byoyomiLeftSeconds.value = byoyomiTimeData != null ? byoyomiTimeData.seconds : 0;
    }

    private void RefreshTurnLeftTimes(ComponentDuelInfo compDuelInfo)
    {
        if (compDuelInfo == null) {
            return;
        }

        if (compDuelInfo.isInfiniteTime.value) {
            compDuelInfo.turnLeftTimes.value = -1;
        } else if (compDuelInfo.isInByoyomi.value) {
            compDuelInfo.turnLeftTimes.value = compDuelInfo.byoyomiLeftSeconds.value;
        } else {
            compDuelInfo.turnLeftTimes.value = compDuelInfo.holdLeftSeconds.value;
        }
    }
}
