public class DuelFSM : FSMBase
{
    public SceneBase scene;

    public DuelFSM(SceneBase scene)
    {
        this.scene = scene;

        // State define
        DuelStateGameStart stateGameStart = new DuelStateGameStart(this);
        DuelStateTurnStart stateTurnStart = new DuelStateTurnStart(this);
        DuelStateTurnInput stateTurnInput = new DuelStateTurnInput(this);
        DuelStateWaitAction stateWaitAction = new DuelStateWaitAction(this);
        DuelStateTurnEnd stateTurnEnd = new DuelStateTurnEnd(this);
        DuelStateGameEnd stateGameEnd = new DuelStateGameEnd(this);

        // Parameters define
        triggerParamDict.Add(DuelParamDefine.TRIGGER_PARAM_GAME_START, false);
        triggerParamDict.Add(DuelParamDefine.TRIGGER_PARAM_WAIT_TURN_INPUT, false);
        triggerParamDict.Add(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH, false);
        triggerParamDict.Add(DuelParamDefine.TRIGGER_PARAM_TURN_TIMEOUT, false);
        triggerParamDict.Add(DuelParamDefine.TRIGGER_PARAM_TURN_START, false);
        triggerParamDict.Add(DuelParamDefine.TRIGGER_PARAM_GAME_END, false);

        // Transition define
        FSMTransition transGameStart = stateGameStart.AddTransition(stateTurnStart);
        transGameStart.AddTriggerCondition(DuelParamDefine.TRIGGER_PARAM_GAME_START);

        FSMTransition transWaitTurnInput = stateTurnStart.AddTransition(stateTurnInput);
        transWaitTurnInput.AddTriggerCondition(DuelParamDefine.TRIGGER_PARAM_WAIT_TURN_INPUT);

        FSMTransition transTurnInputFinish = stateTurnInput.AddTransition(stateTurnEnd);
        transTurnInputFinish.AddTriggerCondition(DuelParamDefine.TRIGGER_PARAM_TURN_INPUT_FINISH);
        FSMTransition transTurnTimeout = stateTurnInput.AddTransition(stateTurnEnd);
        transTurnTimeout.AddTriggerCondition(DuelParamDefine.TRIGGER_PARAM_TURN_TIMEOUT);
        FSMTransition transGameEnd = stateTurnInput.AddTransition(stateGameEnd);
        transGameEnd.AddTriggerCondition(DuelParamDefine.TRIGGER_PARAM_GAME_END);

        FSMTransition transTurnStart = stateTurnEnd.AddTransition(stateTurnStart);
        transTurnStart.AddTriggerCondition(DuelParamDefine.TRIGGER_PARAM_TURN_START);

        RegisterState(stateGameStart);
        RegisterState(stateTurnStart);
        RegisterState(stateTurnInput);
        RegisterState(stateWaitAction);
        RegisterState(stateTurnEnd);
        RegisterState(stateGameEnd);

        defaultState = stateGameStart;
    }
}
