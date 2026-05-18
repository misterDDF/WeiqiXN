# 对局流程模块

## 主要文件

- `Assets/Scripts/Game/System/DuelSystem.cs`
- `Assets/Scripts/Game/FSM/DuelFSM/DuelFSM.cs`
- `Assets/Scripts/Game/FSM/DuelFSM/DuelFSMConst.cs`
- `Assets/Scripts/Game/FSM/DuelFSM/DuelState*.cs`
- `Assets/Scripts/Game/Component/SceneComponent/SceneComponentDuel.cs`
- `Assets/Scripts/Game/Component/EntityComponent/ComponentDuelInfo.cs`
- `Assets/Scripts/Game/UI/Logic/Page/DuelPage.cs`
- `Assets/Scripts/Game/UI/Logic/Page/DuelSetupPopup.cs`

## 职责

对局流程模块负责本地两名玩家的创建、当前玩家记录、回合状态流转、回合计时、落子后推进状态和对局 UI 入口。

## 当前进度

- 新对局会创建 Player1 和 Player2，并把当前玩家设为 Player1。
- 读档对局会按保存的玩家 guid 恢复两个玩家，并激活 `TurnInput` 状态。
- FSM 状态包含 `GameStart`、`TurnStart`、`TurnInput`、`WaitAction`、`TurnEnd`、`GameEnd`。
- 当前实际主循环是 `GameStart -> TurnStart -> TurnInput -> TurnEnd -> TurnStart`。
- `TurnInput` 进入时按当前玩家的持有时间或读秒状态刷新剩余时间。
- 回合倒计时归零时触发 `TURN_TIMEOUT`，进入 `TurnEnd`。
- 成功落子后 `OnAfterAddChessToBoard` 触发 `TURN_INPUT_FINISH`，进入 `TurnEnd`。
- `TurnEnd` 切换当前玩家，然后触发下一轮 `TurnStart`。

## 设计观察

FSM 让本地对局流程清晰可扩展。`WaitAction` 和 `GameEnd` 已有状态占位，但当前主流程还没有实质使用。

## 风险和缺口

- 超时目前直接进入回合结束，没有记录超时原因或对局惩罚。
- `WaitAction` 未接入主路径。
- `GameEnd` 未接入胜负、终局或 UI。
- 玩家显示仍偏调试形态。
- 联机时当前玩家、倒计时、落子确认都必须由权威状态驱动，不能只依赖本地 FSM 触发。

## 后续建议

- 补齐 pass、resign、timeout 策略和 `GameEnd` 进入条件。
- 为对局状态变化补充明确 UI 事件。
- 联机阶段将 FSM 区分为“权威状态”和“客户端表现状态”，避免客户端抢先进入不可回滚状态。
## 2026-05-15 Current Addendum

- New local games initialize board, hold-time, byoyomi-count, and byoyomi-time from `DuelSceneCreateParamas`.
- Hold time is configured by `duel_hold_time`; byoyomi count is configured by `duel_byoyomi_count`; byoyomi period seconds are configured by `duel_byoyomi_time`.
- `DuelSetupPopup` forces byoyomi count to `off` and disables byoyomi controls when infinite hold time is selected.
- `TurnInput` now counts down the current player's remaining hold time. After hold time reaches zero, byoyomi starts only when the selected byoyomi count is greater than zero.
- Every byoyomi period timeout consumes one remaining byoyomi count. When the count is exhausted, `SceneComponentDuel.timeoutLoserGuid` and `winnerGuid` are recorded and the FSM enters `GameEnd`.
- `DuelPage` shows black-player time information in the upper-left panel and white-player time information in the upper-right panel, while save and exit actions live in an in-duel settings panel opened from the lower-right settings button.
- `GameEnd` is now reachable through timeout loss, but full endgame UI, pass, resign, scoring, and review flows remain out of scope.
