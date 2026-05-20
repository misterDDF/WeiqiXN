# 对局流程模块

## 主要文件

- `Assets/Scripts/Game/System/DuelSystem.cs`
- `Assets/Scripts/Game/System/DuelAiSystem.cs`
- `Assets/Scripts/Game/System/DuelMoveRule.cs`
- `Assets/Scripts/Game/FSM/DuelFSM/DuelFSM.cs`
- `Assets/Scripts/Game/FSM/DuelFSM/DuelFSMConst.cs`
- `Assets/Scripts/Game/FSM/DuelFSM/DuelState*.cs`
- `Assets/Scripts/Game/Component/SceneComponent/SceneComponentDuel.cs`
- `Assets/Scripts/Game/Component/EntityComponent/ComponentDuelInfo.cs`
- `Assets/Scripts/Game/UI/Logic/Page/DuelPage.cs`
- `Assets/Scripts/Game/UI/Logic/Page/DuelSetupPopup.cs`

## 职责

对局流程模块负责本地两名玩家的创建、电脑对局状态初始化、当前玩家记录、回合状态流转、回合计时、落子后推进状态和对局 UI 入口。

## 当前进度

- 新对局会创建 Player1 和 Player2，并把当前玩家设为 Player1。
- 电脑对局仍创建 Player1 和 Player2，默认由 Player2 / 白方作为 AI 玩家，并把 `isAiDuel`、`aiDifficultyCfgId`、`aiPlayerGuid` 保存到 `SceneComponentDuel`。
- 读档对局会按保存的玩家 guid 恢复两个玩家，并激活 `TurnInput` 状态。
- FSM 状态包含 `GameStart`、`TurnStart`、`TurnInput`、`WaitAction`、`TurnEnd`、`GameEnd`。
- 当前实际主循环是 `GameStart -> TurnStart -> TurnInput -> TurnEnd -> TurnStart`。
- `TurnInput` 进入时按当前玩家的持有时间或读秒状态刷新剩余时间。
- 回合倒计时归零时触发 `TURN_TIMEOUT`，进入 `TurnEnd`。
- 成功落子后 `OnAfterAddChessToBoard` 触发 `TURN_INPUT_FINISH`，进入 `TurnEnd`。
- `TurnEnd` 切换当前玩家，然后触发下一轮 `TurnStart`。
- `DuelAiSystem` 在电脑对局的 AI 回合读取 `duel_ai_difficulty` 配置，请求 KataGo `moveInfos`，筛选本地规则允许的候选点后发出正常 `OnAddChessToBoard`。实时 AI 落子请求会按当前棋盘路数解析 `realtimeMaxVisits9/13/19`、`candidateLimit9/13/19` 和 `maxScoreLoss9/13/19`；实际 KataGo 完整预算访问次数为 `min(maxVisits, realtimeMaxVisitsN)`，候选筛选使用对应路数的候选数量和亏损阈值。启用 `dynamicBudgetEnabled` 的难度会先用 `probeMaxVisits9/13/19` 发送低预算 probe 请求；开局手数内、局势接近且候选差距小，或首选明显领先时直接使用 probe 结果，probe 缺失、候选不足、缺少 `rootInfo`、复杂局面或到达强制完整预算手数时升级完整预算。难度差异仍通过候选数量、失误率、温度、亏损阈值和访问权重等配置参与选点。没有可用候选点时，仅在难度配置允许提前虚手或棋盘已满时发出虚手。
- `DuelMoveRule` 提供共享落子规则入口，`ChessBoardSystem` 用它执行真实落子，`DuelAiSystem` 用它检查候选点合法性；AI 检查候选点不能保留模拟产生的棋盘状态。
- `DuelPage` 右下角“形式”按钮会发出 `OnRequestDuelOwnership`，并在分析或显示期间切换为“关闭”；再次点击会发出 `OnRequestClearDuelOwnership`。`DuelOwnershipSystem` 根据当前对局生成 KataGo ownership 请求，收到结果后绘制棋盘 overlay，并通过 `OnDuelOwnershipResult` 让 UI 显示双方目数。该流程不推进 FSM，也不改变正式对局结果。
- `DuelPage.prefab` 会在形势按钮旁提供“虚手”入口；`DuelSystem` 在回合输入状态收到虚手后记录 KataGo `pass`，第一手虚手推进到下一回合，双方连续虚手会立即按本地原型数子结果进入 `GameEnd`，不弹二次确认。
- `DuelPage.prefab` 设置面板会提供“请求数子”和“认输”入口；`DuelSystem` 自动计算黑白分数、胜者和目差，通过 `OnDuelScoreResult` 交给页面弹出通用二次确认，确认后进入 `GameEnd`，取消则保持当前对局。认输按钮只在回合输入且当前行棋玩家有效时显示，点击后先弹出通用二次确认，确认后当前行棋方判负并进入 `GameEnd`。
- `DuelPage` 在 AI 回合不接受人类棋盘落子、虚手或认输输入，避免人与 AI 同时驱动同一个回合。
- `DuelPage.prefab` 右侧中部维护结算结果面板，进入 `GameEnd` 后显示黑/白方胜出和结束原因；数子或连续虚手显示领先目数，超时显示黑/白方超时判负，认输显示黑/白方认输。

## 设计观察

FSM 让本地对局流程清晰可扩展。`WaitAction` 和 `GameEnd` 已有状态占位，但当前主流程还没有实质使用。

## 风险和缺口

- `WaitAction` 未接入主路径。
- `GameEnd` 已可由超时、确认数子、双方连续虚手或认输进入，并保存 `winnerGuid`、终局原因和原型数子结果；仍缺少死子确认流程和线上裁定模型。
- 玩家显示仍偏调试形态。
- 电脑对局依赖本地 KataGo analysis 进程和模型；KataGo 不可用时 AI 无法行棋，但本地规则和人工对局基线不应被替换。KataGo 分析超时后适配器会停止当前进程，下一次分析请求会尝试自动重启。
- 联机时当前玩家、倒计时、落子确认都必须由权威状态驱动，不能只依赖本地 FSM 触发。

## 后续建议

- 补齐死子确认、线上裁定和 `GameEnd` 后续复盘流程。
- 为对局状态变化补充明确 UI 事件。
- 联机阶段将 FSM 区分为“权威状态”和“客户端表现状态”，避免客户端抢先进入不可回滚状态。
## 2026-05-15 Current Addendum

- New local games initialize board, hold-time, byoyomi-count, and byoyomi-time from `DuelSceneCreateParamas`.
- Hold time is configured by `duel_hold_time`; byoyomi count is configured by `duel_byoyomi_count`; byoyomi period seconds are configured by `duel_byoyomi_time`.
- `DuelSetupPopup` forces byoyomi count to `off` and disables byoyomi controls when infinite hold time is selected.
- `TurnInput` now counts down the current player's remaining hold time. After hold time reaches zero, byoyomi starts only when the selected byoyomi count is greater than zero.
- Every byoyomi period timeout consumes one remaining byoyomi count. When the count is exhausted, `SceneComponentDuel.timeoutLoserGuid` and `winnerGuid` are recorded and the FSM enters `GameEnd`.
- `DuelPage` shows black-player time information in the upper-left panel and white-player time information in the upper-right panel, while save and exit actions live in an in-duel settings panel opened from the lower-right settings button.
- `GameEnd` is now reachable through timeout loss, scoring, consecutive pass, and resign; dead-stone confirmation, review flows, and online adjudication remain out of scope.
- Computer duel uses the same board, time-control, FSM, save, scoring, pass, and resign flow as local duel. The only turn-owner difference is that `DuelAiSystem` drives Player2 / white turns from KataGo candidates selected by `duel_ai_difficulty`.
- AI turn logs include the board size, configured and requested visit count, configured and requested candidate limit, configured and requested score-loss threshold, probe result summary, and `use_probe` / `upgrade_full` budget decision so board-size-specific runtime budgets can be checked from Unity Console logs.
