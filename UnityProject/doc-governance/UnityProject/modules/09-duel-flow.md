# 对局流程模块

## 主要文件

- `Assets/Scripts/Game/System/DuelSystem.cs`
- `Assets/Scripts/Game/System/DuelAuthoritySystem.cs`
- `Assets/Scripts/Game/System/DuelAiSystem.cs`
- `Assets/Scripts/Game/System/DuelAiAnalyzeService.cs`
- `Assets/Scripts/Game/System/DuelAiBudgetService.cs`
- `Assets/Scripts/Game/System/DuelAiMoveSelector.cs`
- `Assets/Scripts/Game/System/DuelAudioSystem.cs`
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

- 新对局会创建 Player1 和 Player2。分先和让先对局把当前玩家设为 Player1 / 黑方；让子对局会先按配置摆放黑方让子，再把当前玩家设为 Player2 / 白方。
- 电脑对局仍创建 Player1 和 Player2，人类可选择执黑、执白或猜先，AI 控制另一方，并把 `isAiDuel`、`aiDifficultyCfgId`、`aiPlayerGuid`、本端座位和双方显示名保存到 `SceneComponentDuel`；人类座位显示本地用户名，AI 座位显示 AI 文案。
- 当前正式流程只覆盖新开本地对局和电脑对局；读档/继续对局暂不作为正式功能。
- FSM 状态包含 `GameStart`、`TurnStart`、`TurnInput`、`WaitAction`、`TurnEnd`、`GameEnd`。
- 当前实际主循环是 `GameStart -> TurnStart -> TurnInput -> TurnEnd -> TurnStart`。
- `TurnInput` 进入时按当前玩家的持有时间或读秒状态刷新剩余时间。
- 回合倒计时归零时触发 `TURN_TIMEOUT`，进入 `TurnEnd`。
- 成功落子后 `OnAfterAddChessToBoard` 触发 `TURN_INPUT_FINISH`，进入 `TurnEnd`；发生提子时棋盘视图移除棋子后触发携带提子数量的 `OnAfterCaptureChessFromBoard`。`DuelAudioSystem` 监听这两个事件播放落子/提子音效，不参与棋规、手顺或 FSM 推进；提子音效按本次提子数量区分单提和多提，短音效按单通道独占播放并带最小间隔，提子音效优先于同一步落子音效，避免大量落子或提子时叠加爆音。
- `TurnEnd` 切换当前玩家，然后触发下一轮 `TurnStart`。
- `DuelInputAuthority` 是当前本端人类输入权限的集中读取入口；它只读取 `SceneComponentDuel.localInputPlayerFlag`，不在 UI 中派生 LAN 座位。`DuelInputAuthoritySystem` 负责刷新该字段：本地热座跟随当前回合玩家，电脑对局在 AI 回合不给本端人类输入权，LAN 对局由 host 广播 `InputAuthority` 后双方应用。
- `DuelAuthoritySystem` 是正常落子、虚手、数子、悔棋和认输的统一提交入口：页面和 AI 提交 `OnSubmitDuelMove` / `OnSubmitDuelPass` 等命令，本地/电脑对局直接转入本进程权威应用，LAN 对局按 `DuelInputAuthority` 给出的本端输入方提交到 `LanRoomService`，由房间服务决定本端 host 入队还是远端 TCP 发送。本地/电脑正常落子和 LAN host 接受的正常落子已共用 `ChessBoardSystem` 内的 host 权威落子入口，统一检查当前行棋方、可用棋盘状态和 `DuelMoveRule` 结果；LAN host 在该入口上额外校验提交携带的棋盘版本。虚手和认输已共用 `DuelSystem` 的 host 回合命令校验与状态应用入口，LAN host 在虚手路径上额外校验提交版本并广播 accepted 消息；数子和悔棋的 LAN 第一阶段请求校验复用同一组 host 回合状态与版本校验，确认式后续流程保持独立。
- `DuelSystem` 会在新对局初始化本端玩家座位和黑白显示名；本地热座默认把用户资料映射到 Player1，电脑对局把用户资料映射到人类座位，LAN 对局按 host/client 资料和 host 座位映射显示名。收到 `OnLanPlayerProfileChanged` 后会刷新对应座位显示名。
- `DuelAiSystem` 在电脑对局的 AI 回合读取 `duel_ai_difficulty` 配置，只负责 AI 回合触发、取消检查和提交 `OnSubmitDuelMove` 或 `OnSubmitDuelPass`。`DuelAiAnalyzeService` 负责构造并发送 KataGo AI 分析请求；`DuelAiBudgetService` 负责按当前棋盘路数解析 `realtimeMaxVisits9/13/19`、`candidateLimit9/13/19` 和 `maxScoreLoss9/13/19`，并决定动态预算下使用 probe 结果或升级完整预算；`DuelAiMoveSelector` 负责解析 `moveInfos`/`humanPolicy`/`policy`、处理 KataGo 建议 `pass`、筛选本地规则允许的候选点并按难度配置加权选点。启用 `useHumanPolicy` 且本地 KataGo 已加载 Human SL companion model 时，AI 请求会发送 `humanSLProfile` 并请求 policy 输出；选点侧按 `humanPolicyWeight` 概率从 `humanPolicy` 采样，否则回退到普通 `moveInfos`/`policy`。`humanPolicy` 不自行触发提前虚手，虚手仍以搜索结果的 top `moveInfo` 或普通 policy fallback 为准。分析结果不可用时不擅自结束对局；没有可用候选点时，仅在难度配置允许提前虚手或棋盘已满时发出兜底虚手。
- `DuelMoveRule` 提供共享落子规则入口和结果模型，`ChessBoardSystem` 用 accepted `DuelMoveResult` 执行真实落子，`DuelAiMoveSelector` 用同一规则入口检查候选点合法性；AI 检查候选点不能保留模拟产生的棋盘状态。Local/Computer 与 LAN host 共享同一本进程 host 权威落子入口，OGS 等外部服务器对局后续只应复用提交入口、输入权和棋盘表现，不应把本地 host core 作为最终裁定方。
- `DuelPage` 的预览棋子只在 `DuelInputAuthority` 授予本端输入权且 `DuelMoveRule.CheckMoveLegal()` 通过时显示；非法位置、AI 回合和 LAN 对端回合都不创建预览棋子，也不额外显示“无法落子”文案。真实落子被规则拒绝时，`ChessBoardSystem` 仍发出 `OnDuelMoveRejected` 作为系统边界事件。
- 悔棋回放通过 `DuelMoveRule.BuildMoveResult()` 生成 accepted result，再用同一应用口径更新棋盘规则缓存；回放结束后由棋子表现缓存按最终 `chessInfoDict` 同步显示，避免真实落子与悔棋路径分叉，也避免整盘 prefab 销毁重建造成闪动。
- `DuelMoveHistory` 是当前手顺访问边界，集中处理 KataGo `moves` 的创建、追加、克隆、截断、尾部虚手统计和输出；保存、AI 查询和悔棋仍保持 KataGo 标准 `moves` 结构。让子棋的预置黑子写入 KataGo `initialStones`，不计入 `moves` 手顺；形势展示和 ownership 数子改用当前盘面快照生成 `initialStones`，不把历史手顺传入该类请求。
- `DuelPage` 右下角“形式”按钮会发出 `OnRequestDuelOwnership`，并在分析或显示期间切换为“关闭”；再次点击会发出 `OnRequestClearDuelOwnership`。`DuelOwnershipSystem` 通过 `DuelOwnershipQueryService` 按当前棋盘快照生成 KataGo ownership 请求，收到结果后绘制棋盘 overlay，并通过 `OnDuelOwnershipResult` 让 UI 显示双方目数。白方目数按当前 `duel_handicap` 配置加 `komi`；分先显示贴目后，让先显示让先，让子显示让子。该流程不推进 FSM，也不改变正式对局结果。
- `DuelPage.prefab` 会在形势按钮旁提供“虚手”入口；`DuelSystem` 在回合输入状态收到虚手后记录 KataGo `pass`，第一手虚手推进到下一回合，双方连续虚手会立即按 KataGo `ownership` 结算结果进入 `GameEnd`，不弹二次确认；如果 ownership 数子失败，会回滚第二手虚手记录并保持当前对局。
- `DuelPage.prefab` 设置面板会提供“请求数子”和“认输”入口；请求数子会先弹出通用确认面板显示“数子中...”，确认按钮不可点击。`DuelSystem` 通过 `DuelOwnershipQueryService` 按当前棋盘快照请求 KataGo `ownership`，复用形势按钮的 `0.45` ownership 阈值和贴目口径自动计算黑白分数、胜者、目差和来源字段；KataGo 不可用或无结果时不产生数子结果，弹窗显示失败且不允许确认。结果通过 `OnDuelScoreResult` 更新同一个确认面板，确认后进入 `GameEnd`，取消则保持当前对局。LAN 请求数子提交后，发起方先显示不可手动关闭的等待对端确认弹窗；host 转发 `ScoreConfirmRequest` 给对端并等待 `ScoreConfirmResponse`，同意后广播 `ScoreRequestAccepted` 并执行 host 侧数子。host 计算出的 `ScoreResult` 只是候选结果，双方都会弹出结果确认窗口；双方都回传 `ScoreResultConfirmResponse` 且都接受后，host 广播 `ScoreResultAccepted` 并进入 `GameEnd`。任一方拒绝请求、拒绝结果、请求失效或 KataGo 计算失败都会广播带原因的 `ScoreFailed`，关闭等待/结果弹窗并继续对局。认输按钮只在回合输入且本端有输入权时显示，点击后先弹出通用二次确认，确认后提交 `OnSubmitDuelResign`；本地/电脑对局直接进入认输终局，LAN 对局由 host 接受 `SubmitResign` 后广播 `ResignAccepted`，双方进入同一认输终局。
- `DuelPage.prefab` 设置面板会提供“悔棋”入口；本地双人模式每次回退最后 1 手，电脑对局模式每次回到上次人类可行棋局面：当前为人类行棋时回退 2 手，当前为 AI 行棋时回退 1 手。点击悔棋会先弹出二次确认；成功悔棋后会清理当前 AI 推荐点和 ownership 覆盖层。LAN 悔棋提交后，发起方会显示不可手动关闭的等待对端确认弹窗。LAN 悔棋先由 host 转发 `TakeBackConfirmRequest` 给对端并等待 `TakeBackConfirmResponse`，host 按 `actionId` 使用原始请求的 `boardVersion`、`requesterFlag` 和 `removeCount` 执行或拒绝；同意后广播 `TakeBackAccepted`，双方以 `SceneComponentDuel.kataGoMoves` 的剩余手顺为权威来源重建棋盘、KataGo 手顺、当前行棋方和派生终局/ownership 状态，并在最终局面确定后同步棋子表现缓存；拒绝则广播携带请求方座位的 `TakeBackRejected`，只由发起方显示拒绝/失败提示。当前版本不回滚历史计时快照。
- `SceneComponentDuel` 维护运行时 ownership 结果缓存；形势展示和请求数子在局面未变化时复用缓存，合法落子或虚手会清除缓存。
- `DuelPage` 在 AI 回合或 LAN 对端回合不接受人类棋盘落子；AI 回合也不接受人类虚手或认输输入，避免人与 AI 同时驱动同一个回合。虚手、数子、悔棋和认输按钮会随 `DuelInputAuthority` 的输入权或对局状态切换可用性；LAN 数子和悔棋请求到达时会弹出对端确认窗口，数子结果到达后还会再弹双方结果确认窗口。
- `DuelPage` 黑白双方信息面板会显示玩家显示名、人类/AI 身份、当前行棋状态和主时间；开启读秒时显示剩余读秒次数和读秒时间，未开启读秒时隐藏读秒信息。请求形势后会先显示“计算中”，收到 ownership 结果后更新目数。
- `DuelPage.prefab` 维护动作提示 HUD；`DuelPage` 在成功落子、虚手、双方连续虚手进入数子和连续虚手数子失败时短暂显示提示，落子提示使用 KataGo 棋盘坐标，AI 行棋会带 AI 标记。
- `DuelPage.prefab` 右侧中部维护结算结果面板，进入 `GameEnd` 后显示对应显示名的胜出和结束原因；数子或连续虚手显示领先目数，超时显示对应显示名超时判负，认输显示对应显示名认输。进入 `GameEnd` 时对局相机会用 1.5 秒抬高到距棋盘中心 1.35 倍的位置，让棋盘在结算结果出现时渐渐缩小；悔棋从终局回到 `TurnInput` 时相机恢复正常对局位置。非平局终局会按 `SceneComponentDuel.localPlayerFlag` 记录本地用户胜场或负场，悔棋从终局回到对局时会允许后续终局重新记录。
- `DuelSetupPopup` 默认选择无限持有时间；本地对局隐藏开局座位选择但保留让子下拉框，电脑对局和 LAN 创建房间显示 `猜先` / `执黑` / `执白`。`猜先` 强制分先并禁用让子选择，执黑或执白时可按棋盘尺寸选择让先或让 2 子到最大让子数。OGS 创建对局也复用该弹窗，使用独立的 OGS mode state 隐藏 AI 难度和开局座位选择，由 OGS automatch 分配黑白，并按 `chess_board`、`duel_hold_time`、`duel_byoyomi_count`、`duel_byoyomi_time` 和 `duel_handicap` 表中的 `ogsEnabled` 字段筛选可用选项；当前 OGS 默认配置为 9 路、10 分钟、5 次 30 秒读秒，`infinite` 持有时间不在 OGS 模式启用。弹窗关闭时会按本地对局、电脑对局、LAN 创建房间和 OGS 创建对局四种模式分别保存当前选择到用户存档，下一次打开同模式弹窗时恢复最近一次配置，并对当前配置表中已不存在或当前模式不可用的 id 回退默认值。

## 设计观察

FSM 让本地对局流程清晰可扩展。`WaitAction` 和 `GameEnd` 已有状态占位，但当前主流程还没有实质使用。

## 风险和缺口

- `WaitAction` 未接入主路径。
- `GameEnd` 已可由超时、确认数子、双方连续虚手或认输进入，并保存 `winnerGuid`、终局原因和终局分数；当前数子只复用 KataGo `ownership` 口径，仍缺少死子确认流程和线上裁定模型。
- 终局后仍缺少复盘、重新开始或返回主菜单的专门结果操作入口。
- 电脑对局依赖本地 KataGo analysis 进程和模型；KataGo 不可用时 AI 无法行棋，但本地规则和人工对局基线不应被替换。KataGo 分析超时后适配器会停止当前进程，下一次分析请求会尝试自动重启。
- 联机时当前玩家、倒计时、落子确认、虚手、数子和悔棋都由 host 权威状态驱动，客户端只提交命令或确认响应。当前仍缺少断线重连后的完整状态恢复。

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
- `DuelPage` shows black-player time information in the upper-left panel and white-player time information in the upper-right panel, while scoring, take-back, resign, and exit actions live in an in-duel settings panel opened from the lower-right settings button.
- `DuelPage` 会在成功落子、虚手、双方连续虚手进入数子和连续虚手数子失败时短暂显示动作提示；落子提示使用围棋坐标，AI 行棋会带 AI 标记。
- `GameEnd` is now reachable through timeout loss, scoring, consecutive pass, and resign; scoring currently depends on KataGo `ownership`, while dead-stone confirmation, review flows, and online adjudication remain out of scope.
- Computer duel uses the same board, time-control, handicap, FSM, save, scoring, pass, and resign flow as local duel. The turn-owner difference is that `DuelAiSystem` drives the opponent seat of the selected local player from KataGo candidates or weighted Human SL `humanPolicy` sampling analyzed by `DuelAiAnalyzeService` and selected by `DuelAiMoveSelector` according to `duel_ai_difficulty`.
- AI turn logs keep the turn start and final move/pass decision in normal output. Board-size budget details, probe result summaries, `use_probe` / `upgrade_full` budget decisions, request JSON summaries, candidate summaries, and observer noise are gated behind `LoggerConfig.ENABLE_DUEL_AI_DETAIL_LOG` or `LoggerConfig.ENABLE_DUEL_AI_VERBOSE_LOG`.

## 2026-06-02 OGS Duel Addendum

- `OgsDuelScene` reuses the Unity `Duel` scene resource and `DuelPage`, but does not install `DuelAuthoritySystem`, `DuelInputAuthoritySystem`, `DuelSystem`, LAN systems, or local AI systems. It does install `DuelOwnershipSystem` so the shape/ownership overlay remains available, and installs `DuelReplayArchiveSystem` only to write server-accepted OGS state into the local replay archive.
- `OgsDuelSystem` creates the runtime players, opens a persistent authenticated OGS realtime game session, applies `gamedata` and `game/{id}/move` messages into local board presentation, and submits local move/pass requests through OGS realtime `game/move`. OGS player-seat resolution reads black/white ids from direct player objects, nested `user` / `player` objects, numeric player tokens, and top-level black/white id fields so loaded active games keep the local seat aligned with server state.
- OGS accepted state is the only source of truth for board mutation in `OgsDuelScene`. Local clicks and pass button presses only submit requests; they do not directly apply stones, passes, turn changes, or board versions.
- OGS handicap handling follows the server payload shape. Explicit initial-state stones are applied before accepted moves, are not appended into `SceneComponentDuel.kataGoMoves`, are saved into replay record `initialStones`, and make white the default first accepted move/input side after setup unless server current-player fields provide an explicit side; `free_handicap_placement` games treat the first handicap-count accepted moves as same-color opening placements before normal alternation. OGS move arrays and packed strings map directly to local `RectCoordinates(x, z=y)`, and local OGS submissions encode the same local `z` value as OGS y.
- OGS clock state is read from `gamedata.clock` and realtime `game/{id}/clock`, then written into the same `ComponentDuelInfo` fields used by the duel HUD. Official OGS start-clock mode is handled as a separate display branch: when `clock.start_mode` is present, the current first-move player shows remaining time from `expiration` / `expiration_delta`, normal `black_time` / `white_time` thinking time is not decremented for that player, and byoyomi labels are hidden until OGS exits start mode. `DuelStateTurnInput` skips local countdown and timeout adjudication for `OgsDuelScene`; OGS remains the remote authority for timeout and clock state.
- OGS terminal state reuses `DuelPage`'s existing `GameEnd` result panel. `OgsDuelSystem` maps server-provided terminal fields such as `outcome`, `winner`, `black_lost` / `white_lost`, `result`, `reason`, and termination-style fields into `SceneComponentDuel.gameEndReason`, `winnerGuid`, `resignLoserGuid`, `timeoutLoserGuid`, and score-margin fields. Resignation and timeout therefore display through the same side result bar text as local/LAN endings, while unrecognized OGS terminal reasons fall back to score display instead of local adjudication.
- The OGS main-menu entry checks for an existing active OGS game before opening setup. If an active game is found, Unity loads it directly; otherwise `DuelSetupPopup.OpenForOgs` collects board, handicap, main time, byoyomi count, and byoyomi period before starting OGS realtime automatch; OGS automatch assigns colors, so the OGS setup state does not expose player-side selection. During automatch the cancelable waiting popup displays `寻找对局中...`; canceling the popup cancels the local wait and sends `automatch/cancel` if the realtime match request has already been submitted. After `automatch/start`, Unity sends `game/connect`, waits for server `gamedata`, enters `OgsDuelScene`, and keeps OGS as the authority. Automatch maps board size to OGS size, finite main time to a speed bucket, byoyomi-enabled settings to OGS `byoyomi`, byoyomi `off` to OGS `fischer`, and handicap to the OGS handicap preference.
- `ChessBoardSystem.TryApplyAcceptedRemoteMove` exists for OGS and other remote-authority adapters to reuse the local accepted-move board presentation and capture logic after the remote authority has accepted a move. Local/LAN host adjudication continues to use the existing host-authority path.
- `DuelPage` branches input for `OgsDuelScene`: board clicks emit `OnSubmitOgsDuelMove`, pass emits `OnSubmitOgsDuelPass`, resign emits `OnSubmitOgsDuelResign`, and non-bot OGS takeback emits `OnSubmitOgsDuelTakeBack`. Peer OGS takeback requests in non-bot games emit `OnOgsDuelTakeBackConfirmRequest` so the page can show an accept/reject confirmation before `OgsDuelSystem` sends the server response. OGS bot games disable the takeback entry and ignore `undo_requested` messages without surfacing a peer-confirm popup. This non-bot takeback path has been user-verified with a human OGS game created on the OGS web client and loaded into Unity. OGS `paused` / `suspended` phase locks local OGS command submission without changing black/white seat mapping. Shape/ownership remains available; local score request and AI move analysis/recommendation are disabled or hidden.
- OGS stone-removal phase reuses the existing bottom HUD buttons instead of adding an automatic scoring entry: the ownership button becomes confirm-dead-stones / waiting-for-opponent, and the pass button becomes reject/continue. Board clicks in this phase submit same-color connected strings to OGS `removed_stones/set`; hover draws a temporary red cross only over unremoved stones, while confirmed server-removed stones are shown by switching the existing stone renderer to the matching preview-stone translucent material. Server `removed_stones` broadcasts remain the source of truth. The phase automatically requests ownership preview using a board snapshot that excludes server-removed stones, and clears that preview when OGS leaves stone removal or finishes. OGS final result still comes only from server finished/score payloads.
