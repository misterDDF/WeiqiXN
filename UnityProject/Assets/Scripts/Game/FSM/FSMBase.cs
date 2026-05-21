using System.Collections.Generic;
using XNClient.Logger;

public abstract class FSMBase
{
    public FSMStateBase curState;
    public bool isActivated;
    protected FSMStateBase defaultState;
    protected Dictionary<string, FSMStateBase> stateDict = new Dictionary<string, FSMStateBase>();

    public Dictionary<string, int> intParamDict = new Dictionary<string, int>();
    public Dictionary<string, float> floatParamDict = new Dictionary<string, float>();
    public Dictionary<string, bool> boolParamDict = new Dictionary<string, bool>();
    public Dictionary<string, bool> triggerParamDict = new Dictionary<string, bool>();

    public void Activate(string defaultStateName = "")
    {
        if (!string.IsNullOrEmpty(defaultStateName)) {
            if (TryGetState(defaultStateName, out var state)) {
                curState = state;
            } else {
                curState = defaultState;
            }
        } else {
            curState = defaultState;
        }
        curState.OnEnterState();
        isActivated = true;
    }

    public bool SwitchState(string stateName)
    {
        if (!TryGetState(stateName, out var state)) {
            XNLogger.LogError("FSM switch state failed, state not found.", ("stateName", stateName));
            return false;
        }

        if (curState != null) {
            curState.OnExitState();
        }

        state.OnEnterState();
        isActivated = true;
        return true;
    }

    public virtual void Update()
    {
        if (curState != null) {
            curState.OnUpdateState();
        }
    }

    public void RegisterState(FSMStateBase state)
    {
        if (stateDict.ContainsKey(state.stateName)) {
            XNLogger.LogError("Duplicated state name, register state for fsm failed.", ("stateName", state.stateName));
            return;
        }
        stateDict.Add(state.stateName, state);
    }

    public bool TryGetState(string stateName, out FSMStateBase state)
    {
        if (stateDict.TryGetValue(stateName, out state)) {
            return true;
        }
        return false;
    }

    public void SetParameterInt(string paramName, int paramVal)
    {
        if (intParamDict.ContainsKey(paramName)) {
            intParamDict[paramName] = paramVal;
            if (curState != null) {
                curState.TryActivateTransitions();
            }
            if (LoggerConfig.ENABLE_FSM_VERBOSE_LOG) {
                XNLogger.LogInfo("FSM set int parameter.", ("paramName", paramName), ("paramVal", paramVal.ToString()));
            }
        }
    }

    public void SetParameterFloat(string paramName, float paramVal)
    {
        if (floatParamDict.ContainsKey(paramName)) {
            floatParamDict[paramName] = paramVal;
            if (curState != null) {
                curState.TryActivateTransitions();
            }
            if (LoggerConfig.ENABLE_FSM_VERBOSE_LOG) {
                XNLogger.LogInfo("FSM set float parameter.", ("paramName", paramName), ("paramVal", paramVal.ToString()));
            }
        }
    }

    public void SetParameterBool(string paramName, bool paramVal)
    {
        if (boolParamDict.ContainsKey(paramName)) {
            boolParamDict[paramName] = paramVal;
            if (curState != null) {
                curState.TryActivateTransitions();
            }
            if (LoggerConfig.ENABLE_FSM_VERBOSE_LOG) {
                XNLogger.LogInfo("FSM set bool parameter.", ("paramName", paramName), ("paramVal", paramVal.ToString()));
            }
        }
    }

    public void SetParamterTrigger(string paramName)
    {
        if (triggerParamDict.ContainsKey(paramName)) {
            triggerParamDict[paramName] = true;
            if (curState != null) {
                curState.TryActivateTransitions();
            }
            triggerParamDict[paramName] = false;
            if (LoggerConfig.ENABLE_FSM_VERBOSE_LOG) {
                XNLogger.LogInfo("FSM set trigger parameter.", ("paramName", paramName));
            }
        }
    }
}
