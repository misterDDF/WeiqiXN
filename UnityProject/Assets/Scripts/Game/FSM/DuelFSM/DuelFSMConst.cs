public static class DuelStateDefine
{
    public const string STATE_GAME_START = "gameStart";
    public const string STATE_TURN_START = "turnStart";
    public const string STATE_TURN_INPUT = "turnInput";
    public const string STATE_WAIT_ACTION = "waitAction";
    public const string STATE_TURN_END = "turnEnd";
    public const string STATE_GAME_END = "gameEnd";
}

public static class DuelParamDefine
{
    public const string TRIGGER_PARAM_GAME_START = "trigger_gameStart";
    public const string TRIGGER_PARAM_WAIT_TURN_INPUT = "trigger_waitTurnInput";
    public const string TRIGGER_PARAM_TURN_INPUT_FINISH = "trigger_turnInputFinish";
    public const string TRIGGER_PARAM_TURN_TIMEOUT = "trigger_turnTimeout";
    public const string TRIGGER_PARAM_TURN_START = "trigger_turnStart";
    public const string TRIGGER_PARAM_GAME_END = "trigger_gameEnd";
}
