# WeiqiXN UnityProject 规格说明

## Scope

**范围**

- 本文档记录 Unity 围棋项目当前已经实现的系统行为。
- 本文档覆盖启动流程、本地对局流程、棋盘配置、落子处理、提子校验、场景与 UI 入口、存档行为和当前依赖。
- 本文档不定义联机计划、架构理由或阶段路线；这些内容分别归属 [ARCHITECTURE.md](ARCHITECTURE.md) 和 [ROADMAP.md](ROADMAP.md)。

## Current Behavior

### 2026-05-15 Current Addendum

- 2026-05-21：KataGo 运行资源正式迁移到仓库根目录 `KataGo/`，该目录位于 `UnityProject/Assets` 外，避免 Unity 把 KataGo `.dll` 导入为 Editor 插件。Windows Unity Editor 通过仓库根 `KataGo/` 解析引擎、配置和模型路径；Windows PC 构建成功后由构建脚本复制到包体根目录 `<BuildRoot>/KataGo/`。Windows 构建入口会在打包前检查 CPU fallback 所需的 `eigenavx2` 引擎目录、模型目录、`katago.exe`、`analysis_example.cfg`、`analysis_nowrite.cfg` 和 `kata1-b18c384nbt-s9996604416-d4316597426.bin.gz` 模型是否齐全，缺失时直接中止构建；如果 `opencl` 引擎目录已随包提供，则同样检查其 `katago.exe` 和 `analysis_example.cfg`。KataGo 运行时可能在引擎目录生成 `analysis_logs` 和 `KataGoData/opencltuning`，这些目录属于本地诊断或调优缓存，不纳入版本库。
- 2026-05-19：启动流程在资源加载完成后先显示 `LoadingPage` 并调用 `KataGoBootstrap.Start()` 预热本地 AI，退出时调用 `KataGoBootstrap.Stop()`；平台差异由 `KataGoBootstrap` 内部处理。Windows Unity Editor 和 Windows PC 包当前会通过同一套 Win32 pipe 子进程适配器优先后台启动 `KataGo/engines/win-x64/opencl/katago.exe analysis`，加载 `kata1-b18c384nbt-s9996604416-d4316597426.bin.gz`，并依次发送 9 路、13 路、19 路 smoke query 验证 `ownership` 能返回；OpenCL 引擎缺失、启动失败、初始化失败或 smoke test 失败时会自动改用 `KataGo/engines/win-x64/eigenavx2/katago.exe analysis`。启动时会实际写入临时文件检查游戏根目录权限；如果根目录不可写，会通过 `ConfirmPopup` 提示模式提示用户、跳过 OpenCL 预热，并使用 CPU 引擎的 `analysis_nowrite.cfg` 关闭 KataGo 文件写入。AI 预热进度会显示在 `LoadingPage`，按引擎候选和 9 路、13 路、19 路 smoke test 的阶段估算推进；缓存命中导致预热很快完成时也会保留最短 Loading 展示时间；非支持平台会记录跳过原因。`DuelPage` 右下角常驻“形式”按钮，点击后发出 `OnRequestDuelOwnership`，按钮文案切为“关闭”；再次点击会发出 `OnRequestClearDuelOwnership`、清除形势绘制和结果面板，并把按钮文案切回“形式”。`DuelOwnershipSystem` 请求 KataGo `ownership` 后会在棋盘交叉点绘制黑白小方块控制区，同时在按钮上方的结果面板显示黑方目数和白方目数；白方目数会加上当前让子配置的 `komi`，分先显示“（贴目后）”，让先显示“（让先）”，让子显示“（让子）”。`ownership` 绝对值不超过 `0.35` 的交叉点视为未明确控制，不计入双方目数，也不绘制 overlay；同色相邻控制点之间会用对应黑白颜色的细线连接，线宽略粗于棋盘线；绘制层位于棋子模型上方，避免被棋子遮挡。每次新分析请求和下一手合法落子都会清除旧图层与旧结果面板。第一版形势按钮只关心 `ownership` 控制区域，不展示、不缓存也不以 `rootInfo.scoreLead`、胜率或最佳选点作为产品信息。KataGo 分析超时会停止当前子进程，后续分析请求会尝试重启当前已选引擎；重启失败时会继续尝试后续 fallback 引擎。该流程当前不参与正式数子或落子校验。
- 2026-05-19：合法落子成功后会向 `SceneComponentDuel.kataGoMoves` 追加 KataGo 标准手顺数组项，例如 `["B","Q16"]`、`["W","D4"]`。本地 `RectCoordinates` 直接采用 KataGo 棋盘布局作为逻辑坐标契约：`x` 从左到右递增，`z` 从棋盘上边向下递增；9 路左上角为本地 `(0,0)` 并写为 `A9`，左下角为本地 `(0,8)` 并写为 `A1`。KataGo `ownership` 数组按同一行序直接回绘，不再在 KataGo 边界做额外坐标兼容转换。`DuelSaveSystem` 保存对局时先写入 `GameSaveConfig.GetDuelRecordSavePath(0)` 指向的 KataGo analysis JSON 记录文件，再保存场景状态；记录文件包含 `boardXSize`、`boardYSize`、`rules`、`komi`、`initialStones`、`moves`、`includeOwnership: true` 和 `includePolicy: false`，可直接作为第一版 ownership 分析请求骨架；分先对局的 `initialStones` 为空，让子棋会写入预置黑子。当前正式流程暂不包含读档/继续对局入口。`KataGoPositionJsonBuilder.BuildOwnershipAnalysisJson` 是第一版形势按钮默认入口，优先使用当前对局的 `moves` 生成 ownership 请求；`BuildAnalysisJsonWithCurrentBoard` 只保留为调试或无手顺场景的快照入口。
- 2026-05-20：`DuelPage.prefab` 通过 Binder 显式维护“虚手”“请求数子”和“认输”按钮；业务代码只绑定点击监听，不在运行时创建这些固定 UI 控件。请求数子会先打开通用 `ConfirmPopup`，内容显示“数子中...”，确认按钮不可点击；`DuelSystem` 按当前对局 `moves` 请求 KataGo `ownership`，复用与“形势”按钮相同的 `0.35` ownership 阈值和贴目口径生成黑白目数、胜者和目差，结果返回后更新同一个弹窗内容并启用确认按钮，玩家确认后写入 `SceneComponentDuel.finalBlackScore`、`finalWhiteScore`、`finalScoreMargin`、`winnerGuid` 和 `gameEndReason`，再进入 `GameEnd`。如果 KataGo `ownership` 请求失败或返回为空，则弹窗显示失败且确认按钮保持不可用，不进入终局。`SceneComponentDuel` 会缓存最近一次 ownership 数子结果和 ownership 数组；没有新落子或虚手时，“形势”和“请求数子”会复用缓存，不重复请求 KataGo。虚手会写入 KataGo 标准 `moves` 项 `["B","pass"]` 或 `["W","pass"]`，第一手虚手只推进回合并清除旧形势图层，双方连续虚手会立即按同一 ownership 结算流程结束对局，不弹出确认；如果连续虚手后的 ownership 数子失败，会回滚第二手虚手记录并保持当前对局。合法落子或虚手会清除 ownership 缓存。
- `DuelSetupPopup` now passes board, hold-time, byoyomi-count, byoyomi-time, handicap, and player-side config into `DuelSceneCreateParamas`; when the prefab is still on the old three-board-button layout, selecting a board starts a game with default time and even-game settings.
- Hold-time options are table-driven by `Assets/Config/DataJson/duel_hold_time/duel_hold_time.json`: `2m`, `5m`, `10m`, `20m`, and `infinite`.
- Byoyomi count options are table-driven by `Assets/Config/DataJson/duel_byoyomi_count/duel_byoyomi_count.json`: `off`, `1`, `3`, and `5`. `off` means no byoyomi after hold time runs out.
- Byoyomi period options are table-driven by `Assets/Config/DataJson/duel_byoyomi_time/duel_byoyomi_time.json`: `10s`, `20s`, `30s`, and `60s`.
- Handicap options are table-driven by `Assets/Config/DataJson/duel_handicap/duel_handicap.json`: 9x9 supports even game plus 2-5 handicap stones, while 13x13 and 19x19 support even game plus 2-9 handicap stones.
- `DuelSetupPopup` defaults hold time to `infinite`. Local duel hides the player-side dropdown and keeps the handicap dropdown enabled with even game selected by default. Computer duel and LAN room setup show `猜先` / `执黑` / `执白`; `猜先` forces even game and disables handicap, while `执黑` / `执白` enable handicap selection. Handicap options include `分先`, `让先`, and board-size-specific `让N子` entries. Player1 is always black and Player2 is always white; choosing `执白` maps the local or LAN host seat to Player2 rather than changing Go's normal black-first seat contract.
- When `DuelSetupPopup` selects infinite hold time, byoyomi is forced to `off` and byoyomi count/period buttons are disabled.
- `DuelSystem` initializes both local players from the selected time-control configs and stores current time-control config ids on `SceneComponentDuel`.
- `DuelSystem` applies configured handicap stones as initial black stones before the FSM starts. Even games start with black / Player1 to move; handicap games start with white / Player2 to move after the black handicap stones have been placed.
- `DuelStateTurnInput` counts down the current player's hold time first. If byoyomi is enabled, the player enters byoyomi after hold time reaches zero; each byoyomi period timeout consumes one byoyomi count, and exhausting the count records `timeoutLoserGuid` / `winnerGuid` and enters `GameEnd`.
- `DuelPage` displays black-player time information in the upper-left panel and white-player time information in the upper-right panel. Each panel shows hold-time countdown, byoyomi remaining count, and byoyomi period time; only the current turn player's time values are decremented by the duel FSM.
- `DuelPage` 维护短暂动作提示 HUD。成功落子会显示行棋方和围棋坐标，虚手会显示行棋方且在 AI 行棋时带 AI 标记，双方连续虚手会显示正在数子的提示，连续虚手数子失败会显示已回到对局。
- `DuelPage` moves save and exit actions into an in-duel settings panel opened by the lower-right settings button; direct board click input ignores clicks that are already over UI controls.
- `MainMenuPage` provides separate local duel, computer duel, and LAN multiplayer entries. Local duel and computer duel open `DuelSetupPopup`; only the computer duel entry enables the AI difficulty dropdown. The LAN multiplayer entry opens `LanRoomPopup`; creating a room first reuses `DuelSetupPopup` to choose board, time-control, handicap, and host-side config, then `LanRoomPopup` writes those config ids into `LanRoomService` before creating the LAN host room. UDP room broadcasts include room name, host address, player count, host player name, board, hold-time, byoyomi-count, byoyomi-period, handicap, actual host player flag, and visible host-side selection; when the creator leaves the default side option unchanged, the discovered room list displays `猜先` even though the actual host player flag has already been randomized for game start. `LanRoomPopup` exchanges minimal ready state and sends host-owned start config before game start. Closing the LAN popup before entering a duel leaves the current LAN session and releases search, room, and connection sockets. After `StartConfig`, both peers enter `DuelScene` with `isLanDuel`, `lanRole`, and host player flag set and use the host-provided board/time-control/handicap config ids. LAN move input uses a minimal host-authoritative `SubmitMove` / `MoveAccepted` / `MoveRejected` / `BoardSnapshot` command path: submitted and accepted moves carry a board version, host rejects stale-version moves, and accepted moves are followed by an authoritative board snapshot for client correction. `BoardSnapshot` carries the host-authoritative KataGo-standard `moves`; clients replace local `kataGoMoves` from the snapshot and clear stale ownership cache/overlay when applying it. LAN input permission is applied from host-broadcast `InputAuthority`; UI preview, buttons, and submission checks read `SceneComponentDuel.localInputPlayerFlag` instead of deriving LAN ownership in page code. LAN pass uses `SubmitPass` / `PassAccepted` and clears stale ownership display when accepted. LAN resign uses host-authoritative `SubmitResign` / `ResignAccepted`: host accepts only the current turn player resigning, then both peers enter resign game end with the same loser. LAN scoring and takeback are confirmation-based: host forwards `ScoreConfirmRequest` or `TakeBackConfirmRequest` to the opponent, waits for `ScoreConfirmResponse` or `TakeBackConfirmResponse`, then performs `ScoreResult` / `ScoreFailed` or `TakeBackAccepted` / `TakeBackRejected`; accepted takeback is followed by an authoritative `BoardSnapshot` for board and move-history correction. LAN time control is host-authoritative for the current prototype: host broadcasts `TimeState`, client uses it for display, and host broadcasts `PlayerTimeout` for timeout game end. LAN exit uses `LeaveRoom`: the leaving side notifies the peer, stops UDP discovery/broadcast and TCP session/listener sockets, clears LAN message queues and returns to the main menu; the peer shows a leave prompt and returns to the main menu. Recovery and reconnect synchronization are not implemented yet.
- 2026-05-22：LAN 悔棋发起方点击悔棋后先使用 `ConfirmPopup` 二次确认；确认提交后会打开同一弹窗样式的阻塞等待模式，该模式只显示标题和内容，不显示确认/取消按钮，必须等对端同意、拒绝或提交失败后由代码关闭。host 转发 `TakeBackConfirmRequest` 前会按 `actionId` 暂存原始请求，确认回复回来后使用原始 `boardVersion` 和 `removeCount` 执行回退，不再用当前盘面临时重建悔棋请求。`TakeBackRejected` 携带请求方座位，只有发起方显示拒绝/失败结果并关闭等待弹窗；接受分支仍由 host 执行回退后广播 `TakeBackAccepted` 并补发权威 `BoardSnapshot`。
- 2026-05-22：LAN 请求数子分为请求确认、host 计算、结果双确认和最终结算四段。发起方提交 `SubmitScore` 后显示不可手动关闭的等待对端确认弹窗；对端收到 `ScoreConfirmRequest` 后可同意或继续对局。对端同意后双方进入数子中状态，host 使用 KataGo `ownership` 计算候选结果并广播 `ScoreResult`；双方收到候选结果后都弹出数子结果确认窗口。双方都通过 `ScoreResultConfirmResponse` 接受后，host 广播 `ScoreResultAccepted`，双端再进入 `GameEnd`；任一方不接受结果则广播 `ScoreFailed(ResultRejected)`，双端弹出“有一方不接受结果”并继续对局。对端拒绝请求或 KataGo 数子失败也会关闭等待界面并恢复对局。
- 2026-05-22：用户资料保存本地 `userId`、`userName`、胜场和负场；新用户默认名为“人类”，`userId` 当前由本地随机 6 位数字字符串生成，只作为本地存档标识，不作为跨设备账号。`UserInfoPopup` 展示姓名、较小字号的 ID、胜负场和修改入口；修改姓名时复用 `ConfirmPopup` 的输入框模式，确认后保存用户数据并刷新当前资料页。`DuelSystem` 在电脑对局或 LAN 对局的非平局 `GameEnd` 中按本端玩家座位记录一次胜/负，本地双人对局不记录胜负场；电脑对局的人类座位显示本地用户名，LAN 对局双方座位显示同步到的玩家姓名。
- 2026-05-22：LAN 协议新增 `PlayerProfile` 消息，用于同步玩家资料。当前载荷是 base64 包裹的 JSON，结构只包含 `name` 字段；接收侧按 host/client 角色保存资料并映射到黑白座位，后续可在同一 JSON 对象中扩展字段。LAN 开局参数会携带 host/client 已知资料，开局后资料更新也会通过 `OnLanPlayerProfileChanged` 刷新对局显示名。
- LAN room runtime parameters are table-driven by `Assets/Config/DataJson/lan_room_config/lan_room_config.json`: UDP broadcast port, TCP listen port, connection timeout, max player count, broadcast interval, handshake buffer size, and session read buffer size. LAN protocol message names are not table-driven; they are derived from `LanRoomProtocol` enum names and dispatched to `OnXxx` receiver methods by naming convention.
- AI difficulty options are table-driven by `Assets/Config/DataJson/duel_ai_difficulty/duel_ai_difficulty.json`. `DuelSetupPopup` displays the config `name` values in a fixed rank order and passes the selected config id through `DuelSceneCreateParamas`. The difficulty table includes base selection parameters, board-size-specific realtime overrides for 9x9, 13x13, and 19x19 boards, and optional dynamic-budget thresholds.
- Computer duel creates the same two local `Player` entities as local duel, stores `isAiDuel`, `aiDifficultyCfgId`, and `aiPlayerGuid` on `SceneComponentDuel`, and assigns the AI to the opponent seat of the selected local player side.
- `DuelAiSystem` is installed in `DuelScene`. During an AI turn it asks KataGo analysis for move candidates using the current KataGo-standard `moves`; if KataGo's top `moveInfos` candidate is `pass`, or the `policy` fallback ranks pass above every legal board point, the AI emits `OnRequestDuelPass`; otherwise it filters candidates through the same local move legality rule path, then emits `OnSubmitDuelMove` so the normal authority entry applies the move. Human board click, pass, and resign inputs are ignored while the current turn belongs to the AI player. Realtime AI move requests resolve `maxVisits`, candidate count, and maximum score-loss threshold from the selected board size; request visits use `min(maxVisits, realtimeMaxVisitsN)`. Difficulties with dynamic budget enabled first send a low-visit probe request, then either use the probe result for opening/simple/confident positions or upgrade to the full realtime budget for complex, incomplete, or late-game positions.

**当前行为**

- Unity 入口由 `ClientMain` 初始化；它启动 `XNLogger` 和 `Global`，并把自定义 `Update`、`FixedUpdate`、`LateUpdate` 回调插入 Unity PlayerLoop。
- `Global` 按顺序创建全局模块：事件、资源、定时器、存档、红点、局域网房间服务、UI、场景。
- 开发构建和 Unity 编辑器环境会加载 IngameDebugConsole 预制体。
- 启动后，场景管理器进入主菜单场景。
- 项目场景位于 `Assets/Scenes/`，当前包含主菜单、主场景和对局场景。
- `MainMenuScene` 加载后打开 `MainMenuPage`；主菜单的个人信息入口打开 `UserInfoPopup`，可查看本地姓名、ID、胜场和负场，并通过确认弹窗输入模式修改姓名。
- `MainMenuPage` 可以打开本地对局、电脑对局或局域网联机入口；本地对局和电脑对局使用 `DuelSetupPopup` 选择对局参数，局域网联机入口打开 `LanRoomPopup`。
- `LanRoomPopup` 当前提供“创建房间”和“搜索房间”两条 UI 路径；创建房间会通过 `LanRoomService` 打开 TCP 监听并每秒 UDP 广播房间信息，广播内容包含房主玩家名和创建房间时选择的棋盘、时间、读秒、让子、实际 host 座位和可见 host 座位选择项；搜索房间会监听 UDP 广播并在发现到的房间列表中展示这些信息，默认座位选项显示为“猜先”，不显示已经随机出的实际座位。点击房间会发起 TCP 加入握手。关闭 LAN 弹窗会离开当前 LAN 会话，停止搜索、房间监听、UDP 广播和已建立的 TCP 连接。连接成功后双方会交换最小准备状态和 `PlayerProfile` 玩家资料，host 看到双方准备后会发送 `StartConfig`；收到开局配置的一端会进入 `DuelScene`，并在 `SceneComponentDuel` 中记录 `isLanDuel`、`lanRole`、`lanHostPlayerFlag`、`lanBoardVersion` 和双方显示名。LAN 对局中 host 可配置为 Player1 / 黑方或 Player2 / 白方，另一方自动归给 client；host 通过 `InputAuthority` 告知双方当前可输入方，客户端 UI 只按该状态显示预览和动作按钮。client 的落子、虚手、认输、数子请求和悔棋请求都提交到 host，host 校验当前版本和当前行棋方。合法落子会递增 host 权威棋盘版本，并广播包含棋盘尺寸、下一手玩家、最后一步、棋子列表和 host 权威 KataGo 标准手顺的 `BoardSnapshot`，client 用该快照纠正本地棋盘与 `kataGoMoves`；应用权威快照时会清除旧形势显示和 ownership 缓存。虚手由 host 接受后广播 `PassAccepted`；LAN 虚手接受会清除旧形势显示；双方连续虚手后按 host 侧 ownership 数子进入终局。认输由 host 接受后广播 `ResignAccepted`。数子和悔棋需要对端确认：host 转发确认请求，收到对端同意后才广播结果或悔棋接受，拒绝则广播失败；悔棋接受后 host 会补发权威 `BoardSnapshot` 用于手顺和棋盘纠偏。host 每秒广播当前行棋方 `TimeState`，client 不自行扣时或裁定超时，只按 host 状态刷新计时显示；host 超时后广播 `PlayerTimeout`，client 据此进入超时终局。LAN 对局退出会发送 `LeaveRoom`，并释放本端 TCP/UDP socket、清空 LAN 消息队列后回主菜单；对端收到离开消息后提示联机结束并回主菜单。当前尚未接入断线恢复或完整重连恢复。
- `DuelSetupPopup` 可以用 `9x9`、`13x13`、`19x19` 三个棋盘配置进入对局场景，并配置持有时间、读秒、让子和开局座位；本地对局隐藏开局座位选择但保留让子选择，从电脑对局和 LAN 创建房间入口打开时显示 `猜先` / `执黑` / `执白`。让子配置由 `duel_handicap` 提供，分先贴目为 `7.5`，让先和让子贴目为 `0.5`。
- 棋盘尺寸和对局虚拟相机 y 偏移配置在 `Assets/Config/DataJson/chess_board/chess_board.json`。
- 场景、UI 页面、运行时 UI 文案、预制体和 TMP sprite 配置放在 `Assets/Config/DataJson/`，对应的数据读取类放在 `Assets/Config/DataType/`。
- `DuelScene` 创建 `SceneComponentChessBoard` 和 `SceneComponentDuel`，从 `DuelSceneFixedRef` 绑定固定场景引用，安装 `DuelSaveSystem`、`ChessBoardSystem`、`DuelOwnershipSystem`、`DuelAuthoritySystem`、`DuelSystem`、`LanDuelSystem`、`DuelAiSystem`，然后打开 `DuelPage`。
- `SceneComponentChessBoard` 保存当前棋盘配置 id、运行时按棋盘位置索引缓存的棋子信息、用于简单重复局面对比的上一局面快照、`RectGrid` 引用、对局虚拟相机引用和运行时棋子表现缓存。棋盘规则状态以 `chessInfoDict` 为权威，棋子 prefab 由表现缓存按位置显示、隐藏和复用；LAN 快照纠偏、悔棋回放和读档恢复在最终规则状态确定后同步表现缓存，不再通过整盘销毁重建棋子 prefab 更新画面。
- `SceneComponentDuel` 保存双方玩家 guid、当前回合玩家 guid、本端玩家座位、双方显示名、时间配置、让子配置、电脑对局配置、局域网对局标记、局域网角色、局域网 host 座位、局域网棋盘版本、超时/胜者 guid、连续虚手数、终局原因、最终数子分数和运行时 KataGo 标准 `moves` 手顺。
- `RectGrid` 及其相关棋盘类使用 `RectCoordinates` 生成和寻址矩形棋盘；`RectCoordinates` 的逻辑行列语义与 KataGo 坐标保持一致。
- `ChessBoardSystem` 根据所选棋盘尺寸初始化网格，并把对局虚拟相机调整为轻透视俯视以覆盖棋盘；相机使用较窄 FOV 和小倾角，在保留棋盘读盘清晰度的同时提供少量真实桌面透视。
- `DuelSystem` 在新对局中创建两个本地玩家，按让子配置预置黑棋，并启动对局状态机；分先和让先对局从玩家 1 / 黑方开始，让子对局摆子后从玩家 2 / 白方开始。
- 电脑对局仍复用本地双玩家和本地回合 FSM，人类可选择执黑、执白或猜先，AI 控制另一方；人类座位显示本地用户名，AI 座位显示 AI 文案，AI 难度配置随场景状态保存。
- `DuelFSM` 当前定义本地回合循环：`GameStart -> TurnStart -> TurnInput -> TurnEnd -> TurnStart`，回合输入可以通过落子完成或超时进入回合结束。
- `DuelStateTurnInput` 按当前玩家的持有时间或读秒状态每秒递减一次；无限时间不会启动回合倒计时。
- `DuelStateTurnEnd` 在玩家 1 和玩家 2 之间切换 `curTurnPlayerGuid`。
- `DuelPage` 显示黑方和白方的显示名、身份、持有时间、读秒次数和读秒时间；显示名来自 `SceneComponentDuel.player1DisplayName` / `player2DisplayName`，本地/电脑对局使用本地用户名，LAN 对局使用同步到的玩家姓名。它通过 `DuelInputAuthority` 读取本端当前是否拥有输入权，再根据鼠标位置计算最近棋盘坐标，只有本端有输入权且该点通过本地落子规则时才显示落点 VFX。非 UI 区域左键只在已有合法预览坐标且本端仍有输入权时触发 `OnSubmitDuelMove`，由 `DuelAuthoritySystem` 请求权威执行，本地/电脑对局转入本进程权威落子，LAN 对局提交到 `LanRoomService` 后由本端 host 入队或远端 TCP 发送。右下角设置按钮打开对局设置面板，右下角形式按钮触发 `OnRequestDuelOwnership`，形式按钮旁的虚手按钮触发 `OnSubmitDuelPass`，形式按钮上方的结果面板显示 ownership 统计出的双方目数，设置面板中的保存按钮触发 `OnSaveDuelScene`，请求数子按钮触发 `OnSubmitDuelScore`；LAN 请求数子提交后会显示不可手动关闭的等待对端确认弹窗，对端同意后等待弹窗更新为数子中，收到候选数子结果后再弹双方结果确认窗口，拒绝或失败会弹出原因并继续对局。悔棋按钮会先弹出二次确认再触发 `OnSubmitDuelTakeBack`，LAN 悔棋提交后会显示不可手动关闭的等待对端确认弹窗，认输按钮仅在当前对局处于回合输入且本端有输入权时显示，点击后通过二次确认触发 `OnSubmitDuelResign`。LAN 数子和悔棋确认请求到达时，页面会弹出确认窗口。退出按钮回到主菜单；LAN 对局退出会先离开当前 LAN 会话并通知对端，对端离开时页面显示提示后回主菜单。进入 `GameEnd` 后，页面右侧中部的结算结果面板会显示对应显示名的胜出和结束原因。
- `DuelPage` 会通过短暂 HUD 提示显示成功落子、虚手、双方连续虚手进入数子和连续虚手数子失败回到对局；落子提示使用棋盘坐标，电脑对局中由 AI 触发的落子或虚手会显示 AI 标记。
- 电脑对局的 AI 回合中，`DuelInputAuthority` 不授予本端输入权，因此 `DuelPage` 不显示预览棋子，也不接受人类棋盘落子、虚手或认输输入；AI 由 `DuelAiSystem` 请求 KataGo 候选点，KataGo 明确建议 `pass` 时走现有虚手事件，否则通过本地落子规则筛选后提交正常落子命令。启用动态预算的难度会先请求低访问次数 probe，再依据当前手数、`rootInfo.scoreLead`、`rootInfo.winrate`、首选和次选 `scoreLoss` 差距等配表阈值决定是否升级完整预算。
- 落子只有在目标坐标位于棋盘内、目标位置为空、当前回合玩家存在时才会继续处理。
- 落子校验会先缓存当前棋盘状态，再移除无气的对方连通棋串，随后拒绝自杀、拒绝单子无气、拒绝与上一局面完全一致的棋盘状态。
- 合法落子接受后，被提掉的棋子实体会被销毁。
- 合法落子接受后，系统会更新 `lastChessInfoDict`，创建新的 `Chess` 实体，并发出 `OnAfterAddChessToBoard`。
- `OnAfterAddChessToBoard` 会推动对局状态机从回合输入进入回合结束。
- `OnRequestDuelPass` 在回合输入状态下记录当前玩家虚手并推动回合结束；双方连续虚手会按 KataGo `ownership` 统计结果结算，然后直接进入 `GameEnd`；如果 ownership 数子失败，会回滚第二手虚手记录并保持当前对局。
- `OnRequestDuelScore` 会先显示“数子中...”确认弹窗且禁用确认按钮，再按 KataGo `ownership` 统计结果更新确认内容；若 KataGo 不可用或无结果，则显示失败且不进入终局。玩家确认后进入 `GameEnd`，取消则继续当前对局。
- `OnSubmitDuelResign` 是认输提交入口；本地/电脑对局会转成 `OnConfirmDuelResign` 并把当前行棋方记录为认输方，LAN 对局会提交到 host，收到 `ResignAccepted` 后双方按 host 接受的认输方进入 `GameEnd`。
- `GameEnd` 结果面板按终局原因显示结果：数子和连续虚手显示领先目数，超时显示对应显示名超时判负，认输显示对应显示名认输；电脑对局或 LAN 对局的非平局终局会按本端玩家座位记录一次胜场或负场，本地双人对局不记录胜负场。
- `DuelSaveSystem` 响应 `OnSaveDuelScene`，先通过 `KataGoDuelRecordFile.Save` 保存棋盘记录文件，再通过 `DuelSaveInfoFile.Save` 保存槽位摘要信息，成功后再通过 `GameSaveManager.SaveDataAsync` 把当前场景状态保存到 `GameSaveConfig.GetDuelSceneSavePath(0)`；任一步失败或全部成功后都会发出 `OnDuelSaveResult`。
- 保存使用 `SavableObj`、`SavableField`、可保存集合和 JSON 文件；Unity Editor 下存档根目录是仓库根目录的 `save/`，PC Standalone 下存档根目录是游戏包体根目录的 `save/`，其他非 Editor 平台默认使用 `Application.persistentDataPath`；对局槽位目录为 `save/0/`、`save/1/` 等。`SaveInfo.json` 记录存档时间、槽位、棋盘配置、时间配置、让子配置和当前手数。读档/继续对局暂不作为当前正式功能。
- 当前包依赖包括 Unity 内置模块、URP、Cinemachine、TextMesh Pro、UGUI、Newtonsoft JSON、AssetBundle Browser 和开发工具包。
- `Packages/manifest.json` 中没有网络或多人相关依赖。
- 按 socket、WebSocket、Netcode、Mirror、Photon、UnityTransport、HTTP client、TCP、UDP、multiplayer、联机等关键词扫描 `Assets` 后，没有发现项目网络实现。

## Fixed Rules

**固定规则**

- 本地对局是当前行为基线。
- 棋盘坐标使用矩形网格坐标，字段为 `x` 和 `z`。
- 棋盘位置索引公式为 `z * gridSize + x`。
- 玩家标记通过 `DuelUtils.GetGamePrefabTypeIdWithPlayerFlag` 映射到黑白棋子预制体。
- 对局记录文件中的 KataGo 标准 `moves` 是保存侧的棋谱输出；场景存档不再保存 `SceneComponentChessBoard.chessInfoDict`。
- `chessInfoDict` 和 `lastChessInfoDict` 跳过保存检查，只作为运行时棋盘缓存和局面对比状态。
- 非法落子不显示预览棋子，页面不额外弹出“无法落子”提示；真实落子被规则拒绝时系统仍保留 `OnDuelMoveRejected` 边界事件。
- 死子确认、复盘、匹配、重连和完整网络对局同步当前未实现；局域网联机入口已有房间创建、UDP 广播发现、房间列表展示、TCP 加入握手、最小准备状态交换、host 开局配置、进入带 LAN 标记的 `DuelScene`、host 权威落子、输入权下发、棋盘版本、落子后权威快照纠偏、host 权威计时状态广播、超时通知、虚手、认输、确认式数子、确认式悔棋和 `LeaveRoom` 主动离开原型，但尚未接入断线恢复或完整线上裁定模型。当前阶段“请求数子”和连续虚手终局只依赖 KataGo `ownership` 结算，KataGo 不可用或无结果时不产生数子结果。

## Validation and Maintenance

## 2026-05-20 Runtime Asset Addendum

- Runtime-only assets loaded by code and not guaranteed to be referenced by scenes or prefabs are declared in `ConfigExporter/xlsx/runtime_asset.xlsx`.
- The exported runtime asset config is `Assets/Config/DataJson/runtime_asset/runtime_asset.json`; the generated reader is `Assets/Config/DataType/runtime_asset/RuntimeAssetDataType.cs`.
- The current runtime asset table contains the shared black and white chess-board materials used by ownership overlay and latest-move marker rendering.
- `ChessBoardSystem` loads these materials through `RuntimeAssetDataType` and `ResourceManager`, then injects them into `RectGrid`. `RectGrid` does not call `Shader.Find` or read global config directly.
- Latest-move marker draw failures are logged and isolated so a visual material issue does not block `AppendKataGoMove` or `OnAfterAddChessToBoard`.

**验证与维护**

- 当前行为验证应覆盖：从配置的启动场景进入运行、从主菜单打开本地对局和电脑对局、从主菜单打开局域网联机弹窗并切换创建/搜索房间 UI、分别启动 `9x9`、`13x13`、`19x19` 棋盘、电脑对局难度下拉框读表、正常落子、尝试已有棋子位置、尝试棋盘外坐标、尝试自杀、尝试简单重复局面、保存对局。
- 棋规、回合、场景、UI、资源、存档或依赖的当前行为发生变化时，需要更新本文档。
- 网络依赖或网络实现一旦成为当前行为，也需要更新本文档。
