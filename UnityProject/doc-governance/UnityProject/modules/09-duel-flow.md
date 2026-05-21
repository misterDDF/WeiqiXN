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
- `DuelAiSystem` 在电脑对局的 AI 回合读取 `duel_ai_difficulty` 配置，请求 KataGo `moveInfos`，并把 KataGo 明确建议的 `pass` 转为现有 `OnRequestDuelPass` 虚手事件：`moveInfos` 首选为 `pass` 时直接虚手；`moveInfos` 为空时，如果 `policy` 最后一项的 pass 权重大于所有本地合法棋盘点，也会虚手。未触发虚手时，系统筛选本地规则允许的候选点后发出正常 `OnAddChessToBoard`。实时 AI 落子请求会按当前棋盘路数解析 `realtimeMaxVisits9/13/19`、`candidateLimit9/13/19` 和 `maxScoreLoss9/13/19`；实际 KataGo 完整预算访问次数为 `min(maxVisits, realtimeMaxVisitsN)`，候选筛选使用对应路数的候选数量和亏损阈值。启用 `dynamicBudgetEnabled` 的难度会先用 `probeMaxVisits9/13/19` 发送低预算 probe 请求；开局手数内、局势接近且候选差距小，或首选明显领先时直接使用 probe 结果，probe 缺失、候选不足、缺少 `rootInfo`、复杂局面或到达强制完整预算手数时升级完整预算。难度差异仍通过候选数量、失误率、温度、亏损阈值和访问权重等配置参与选点。分析结果不可用时不擅自结束对局；没有可用候选点时，仅在难度配置允许提前虚手或棋盘已满时发出兜底虚手。
- `DuelMoveRule` 提供共享落子规则入口和结果模型，`ChessBoardSystem` 用 accepted `DuelMoveResult` 执行真实落子，`DuelAiSystem` 用同一规则入口检查候选点合法性；AI 检查候选点不能保留模拟产生的棋盘状态。
- `DuelPage` 的预览棋子只在 `DuelMoveRule.CheckMoveLegal()` 通过时显示；非法位置不创建预览棋子，也不额外显示“无法落子”文案。真实落子被规则拒绝时，`ChessBoardSystem` 仍发出 `OnDuelMoveRejected` 作为系统边界事件。
- 读档回放和悔棋回放也通过 `DuelMoveRule.BuildMoveResult()` 生成 accepted result，再用同一应用口径更新棋盘缓存和棋子实体，避免真实落子与回放路径分叉。
- `DuelMoveHistory` 是当前手顺访问边界，集中处理 KataGo `moves` 的创建、追加、克隆、截断、尾部虚手统计和输出；保存、读档、ownership、AI 查询和悔棋仍保持 KataGo 标准 `moves` 结构。
- `DuelPage` 右下角“形式”按钮会发出 `OnRequestDuelOwnership`，并在分析或显示期间切换为“关闭”；再次点击会发出 `OnRequestClearDuelOwnership`。`DuelOwnershipSystem` 通过 `DuelOwnershipQueryService` 根据当前对局生成 KataGo ownership 请求，收到结果后绘制棋盘 overlay，并通过 `OnDuelOwnershipResult` 让 UI 显示双方目数。该流程不推进 FSM，也不改变正式对局结果。
- `DuelPage.prefab` 会在形势按钮旁提供“虚手”入口；`DuelSystem` 在回合输入状态收到虚手后记录 KataGo `pass`，第一手虚手推进到下一回合，双方连续虚手会立即按 KataGo `ownership` 结算结果进入 `GameEnd`，不弹二次确认；如果 ownership 数子失败，会回滚第二手虚手记录并保持当前对局。
- `DuelPage.prefab` 设置面板会提供“请求数子”和“认输”入口；请求数子会先弹出通用确认面板显示“数子中...”，确认按钮不可点击。`DuelSystem` 通过 `DuelOwnershipQueryService` 请求 KataGo `ownership`，复用形势按钮的阈值和贴目口径自动计算黑白分数、胜者、目差和来源字段；KataGo 不可用或无结果时不产生数子结果，弹窗显示失败且不允许确认。结果通过 `OnDuelScoreResult` 更新同一个确认面板，确认后进入 `GameEnd`，取消则保持当前对局。认输按钮只在回合输入且当前行棋玩家有效时显示，点击后先弹出通用二次确认，确认后当前行棋方判负并进入 `GameEnd`。
- `DuelPage.prefab` 设置面板会提供“悔棋”入口；本地双人模式每次回退最后 1 手，电脑对局模式每次回到上次人类可行棋局面：当前为人类行棋时回退 2 手，当前为 AI 行棋时回退 1 手。悔棋以 `SceneComponentDuel.kataGoMoves` 的剩余手顺为权威来源重建棋盘、KataGo 手顺、当前行棋方和派生终局/ownership 状态；当前版本不回滚历史计时快照。
- `SceneComponentDuel` 维护运行时 ownership 结果缓存；形势展示和请求数子在局面未变化时复用缓存，合法落子或虚手会清除缓存。
- `DuelSaveSystem` 保存对局后会发出 `OnDuelSaveResult`，UI 只展示保存成功或失败结果；场景数据异步保存失败不再被 fire-and-forget 静默忽略。
- `DuelPage` 在 AI 回合不接受人类棋盘落子、虚手或认输输入，避免人与 AI 同时驱动同一个回合；虚手按钮会随当前是否可接受人类回合输入切换可点击状态，请求数子按钮会在正在数子或终局后禁用。
- `DuelPage` 黑白双方信息面板会显示人类/AI 身份、当前行棋状态和主时间；开启读秒时显示剩余读秒次数和读秒时间，未开启读秒时隐藏读秒信息。请求形势后会先显示“计算中”，收到 ownership 结果后更新目数。
- `DuelPage.prefab` 维护动作提示 HUD；`DuelPage` 在成功落子、虚手、双方连续虚手进入数子和连续虚手数子失败时短暂显示提示，落子提示使用 KataGo 棋盘坐标，AI 行棋会带 AI 标记。
- `DuelPage.prefab` 右侧中部维护结算结果面板，进入 `GameEnd` 后显示黑/白方胜出和结束原因；数子或连续虚手显示领先目数，超时显示黑/白方超时判负，认输显示黑/白方认输。

## 设计观察

FSM 让本地对局流程清晰可扩展。`WaitAction` 和 `GameEnd` 已有状态占位，但当前主流程还没有实质使用。

## 风险和缺口

- `WaitAction` 未接入主路径。
- `GameEnd` 已可由超时、确认数子、双方连续虚手或认输进入，并保存 `winnerGuid`、终局原因和终局分数；当前数子只复用 KataGo `ownership` 口径，仍缺少死子确认流程和线上裁定模型。
- 终局后仍缺少复盘、重新开始或返回主菜单的专门结果操作入口。
- 电脑对局依赖本地 KataGo analysis 进程和模型；KataGo 不可用时 AI 无法行棋，但本地规则和人工对局基线不应被替换。KataGo 分析超时后适配器会停止当前进程，下一次分析请求会尝试自动重启。
- 联机时当前玩家、倒计时、落子确认和悔棋都必须由权威状态驱动，不能只依赖本地 FSM 触发。联机悔棋规则暂记录为：发起方请求回到发起方上次落子前，对方需要在确认窗口同意后才能执行；拒绝时悔棋方收到提示“对方不同意悔棋”。当前尚未实现联机运行时流程。

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
- `DuelPage` 会在成功落子、虚手、双方连续虚手进入数子和连续虚手数子失败时短暂显示动作提示；落子提示使用围棋坐标，AI 行棋会带 AI 标记。
- `GameEnd` is now reachable through timeout loss, scoring, consecutive pass, and resign; scoring currently depends on KataGo `ownership`, while dead-stone confirmation, review flows, and online adjudication remain out of scope.
- Computer duel uses the same board, time-control, FSM, save, scoring, pass, and resign flow as local duel. The only turn-owner difference is that `DuelAiSystem` drives Player2 / white turns from KataGo candidates selected by `duel_ai_difficulty`.
- AI turn logs include the board size, configured and requested visit count, configured and requested candidate limit, configured and requested score-loss threshold, probe result summary, and `use_probe` / `upgrade_full` budget decision so board-size-specific runtime budgets can be checked from Unity Console logs.
