using System.Collections.Generic;
using XNClient.Logger;

public class FSMTransition
{
    public FSMStateBase srcState;
    public FSMStateBase dstState;
    public FSMBase fsm => srcState.fsm;

    protected List<FSMTransConditionBase> conditionList = new List<FSMTransConditionBase>();

    public FSMTransition(FSMStateBase srcState, FSMStateBase dstState)
    {
        this.srcState = srcState;
        this.dstState = dstState;
    }

    public bool CheckActivateTransition()
    {
        bool allConditionPass = true;
        foreach (FSMTransConditionBase condition in conditionList) {
            if (!condition.CheckConditionPass()) {
                allConditionPass = false;
                break;
            }
        }

        return allConditionPass;
    }

    public void ActivateTransition()
    {
        srcState.OnExitState();
        dstState.OnEnterState();
        if (LoggerConfig.ENABLE_FSM_VERBOSE_LOG) {
            XNLogger.LogInfo("FSM activate transition.", ("srcStateName", srcState.stateName), ("dstStateName", dstState.stateName));
        }
    }

    public void AddIntCondition(string paramName, FSMIntConditionOption opt, int conditionVal)
    {
        FSMTransConditionInt condition = new FSMTransConditionInt(this, paramName, opt, conditionVal);
        conditionList.Add(condition);
    }

    public void AddFloatCondition(string paramName, FSMFloatConditionOption opt, float conditionVal)
    {
        FSMTransConditionFloat condition = new FSMTransConditionFloat(this, paramName, opt, conditionVal);
        conditionList.Add(condition);
    }

    public void AddBoolCondition(string paramName, FSMBoolConditionOption opt)
    {
        FSMTransConditionBool condition = new FSMTransConditionBool(this, paramName, opt);
        conditionList.Add(condition);
    }

    public void AddTriggerCondition(string paramName)
    {
        FSMTransConditionTrigger conditoin = new FSMTransConditionTrigger(this, paramName);
        conditionList.Add(conditoin);
    }
}
