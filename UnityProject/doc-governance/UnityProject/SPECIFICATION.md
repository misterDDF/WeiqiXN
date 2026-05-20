# WeiqiXN UnityProject 规格说明

## Scope

**范围**

- 本文档记录 Unity 围棋项目当前已经实现的系统行为。
- 本文档覆盖启动流程、本地对局流程、棋盘配置、落子处理、提子校验、场景与 UI 入口、存档行为和当前依赖。
- 本文档不定义联机计划、架构理由或阶段路线；这些内容分别归属 [ARCHITECTURE.md](ARCHITECTURE.md) 和 [ROADMAP.md](ROADMAP.md)。

## Current Behavior

### 2026-05-15 Current Addendum

- 2026-05-20：KataGo 运行资源正式迁移到 `Assets/StreamingAssets/KataGo/`，Windows Unity Editor 和 Windows PC 包统一通过 `Application.streamingAssetsPath/KataGo` 解析引擎、配置和模型路径；PC 包体中的对应目录为 `<GameName>_Data/StreamingAssets/KataGo/`。Windows 构建入口会在打包前检查 `eigenavx2` 引擎目录、模型目录、`katago.exe`、`analysis_example.cfg` 和 `kata1-b18c384nbt-s9996604416-d4316597426.bin.gz` 模型是否齐全，缺失时直接中止构建。KataGo 运行时可能在引擎目录生成 `analysis_logs`，该目录属于诊断日志，不纳入版本库。
- 2026-05-19：启动流程会统一调用 `KataGoBootstrap.Start()`，退出时调用 `KataGoBootstrap.Stop()`；平台差异由 `KataGoBootstrap` 内部处理。Windows Unity Editor 和 Windows PC 包当前会通过同一套 Win32 pipe 子进程适配器后台启动 `Assets/StreamingAssets/KataGo/engines/win-x64/eigenavx2/katago.exe analysis`，加载 `kata1-b18c384nbt-s9996604416-d4316597426.bin.gz`，并发送固定 19 路 smoke query 验证 `ownership` 能返回；非支持平台会记录跳过原因。`DuelPage` 右下角常驻“形式”按钮，点击后发出 `OnRequestDuelOwnership`，按钮文案切为“关闭”；再次点击会发出 `OnRequestClearDuelOwnership`、清除形势绘制和结果面板，并把按钮文案切回“形式”。`DuelOwnershipSystem` 请求 KataGo `ownership` 后会在棋盘交叉点绘制黑白小方块控制区，同时在按钮上方的结果面板显示黑方目数和白方目数；白方目数会加上当前 query 的 `komi` 并显示“（贴目后）”。`ownership` 绝对值不超过当前阈值的交叉点视为未明确控制，不计入双方目数，也不绘制 overlay；同色相邻控制点之间会用对应黑白颜色的细线连接，线宽略粗于棋盘线；绘制层位于棋子模型上方，避免被棋子遮挡。每次新分析请求和下一手合法落子都会清除旧图层与旧结果面板。第一版形势按钮只关心 `ownership` 控制区域，不展示、不缓存也不以 `rootInfo.scoreLead`、胜率或最佳选点作为产品信息。KataGo 分析超时会停止当前子进程，后续分析请求会尝试使用已解析路径自动重启 KataGo。该流程当前不参与正式数子或落子校验。
- 2026-05-19：合法落子成功后会向 `SceneComponentDuel.kataGoMoves` 追加 KataGo 标准手顺数组项，例如 `["B","Q16"]`、`["W","D4"]`。本地 `RectCoordinates` 直接采用 KataGo 棋盘布局作为逻辑坐标契约：`x` 从左到右递增，`z` 从棋盘上边向下递增；9 路左上角为本地 `(0,0)` 并写为 `A9`，左下角为本地 `(0,8)` 并写为 `A1`。KataGo `ownership` 数组按同一行序直接回绘，不再在 KataGo 边界做额外坐标兼容转换。`DuelSaveSystem` 保存对局时先写入 `GameSaveConfig.GetDuelRecordSavePath(0)` 指向的 KataGo analysis JSON 记录文件，再保存场景状态；记录文件包含 `boardXSize`、`boardYSize`、`rules`、`komi`、`initialStones: []`、`moves`、`includeOwnership: true` 和 `includePolicy: false`，可直接作为第一版 ownership 分析请求骨架。读取对局时，棋盘状态由记录文件回放恢复，`SceneComponentChessBoard.chessInfoDict` 只作为运行时棋盘缓存，不再作为存档恢复权威。`KataGoPositionJsonBuilder.BuildOwnershipAnalysisJson` 是第一版形势按钮默认入口，优先使用当前对局的 `moves` 生成 ownership 请求；`BuildAnalysisJsonWithCurrentBoard` 只保留为调试或无手顺场景的快照入口。
- 2026-05-20：`DuelPage.prefab` 通过 Binder 显式维护“虚手”“请求数子”和“认输”按钮；业务代码只绑定点击监听，不在运行时创建这些固定 UI 控件。请求数子会先打开通用 `ConfirmPopup`，内容显示“数子中...”，确认按钮不可点击；`DuelSystem` 按当前对局 `moves` 请求 KataGo `ownership`，复用与“形势”按钮相同的阈值和贴目口径生成黑白目数、胜者和目差，结果返回后更新同一个弹窗内容并启用确认按钮，玩家确认后写入 `SceneComponentDuel.finalBlackScore`、`finalWhiteScore`、`finalScoreMargin`、`winnerGuid` 和 `gameEndReason`，再进入 `GameEnd`。如果 KataGo `ownership` 请求失败或返回为空，则弹窗显示失败且确认按钮保持不可用，不进入终局。`SceneComponentDuel` 会缓存最近一次 ownership 数子结果和 ownership 数组；没有新落子或虚手时，“形势”和“请求数子”会复用缓存，不重复请求 KataGo。虚手会写入 KataGo 标准 `moves` 项 `["B","pass"]` 或 `["W","pass"]`，第一手虚手只推进回合并清除旧形势图层，双方连续虚手会立即按同一 ownership 结算流程结束对局，不弹出确认；如果连续虚手后的 ownership 数子失败，会回滚第二手虚手记录并保持当前对局。合法落子或虚手会清除 ownership 缓存。
- `DuelSetupPopup` now passes board, hold-time, byoyomi-count, and byoyomi-time config ids into `DuelSceneCreateParamas`; when the prefab is still on the old three-board-button layout, selecting a board starts a game with default time settings.
- Hold-time options are table-driven by `Assets/Config/DataJson/duel_hold_time/duel_hold_time.json`: `2m`, `5m`, `10m`, `20m`, and `infinite`.
- Byoyomi count options are table-driven by `Assets/Config/DataJson/duel_byoyomi_count/duel_byoyomi_count.json`: `off`, `1`, `3`, and `5`. `off` means no byoyomi after hold time runs out.
- Byoyomi period options are table-driven by `Assets/Config/DataJson/duel_byoyomi_time/duel_byoyomi_time.json`: `10s`, `20s`, `30s`, and `60s`.
- When `DuelSetupPopup` selects infinite hold time, byoyomi is forced to `off` and byoyomi count/period buttons are disabled.
- `DuelSystem` initializes both local players from the selected time-control configs and stores current time-control config ids on `SceneComponentDuel`.
- `DuelStateTurnInput` counts down the current player's hold time first. If byoyomi is enabled, the player enters byoyomi after hold time reaches zero; each byoyomi period timeout consumes one byoyomi count, and exhausting the count records `timeoutLoserGuid` / `winnerGuid` and enters `GameEnd`.
- `DuelPage` displays black-player time information in the upper-left panel and white-player time information in the upper-right panel. Each panel shows hold-time countdown, byoyomi remaining count, and byoyomi period time; only the current turn player's time values are decremented by the duel FSM.
- `DuelPage` moves save and exit actions into an in-duel settings panel opened by the lower-right settings button; direct board click input ignores clicks that are already over UI controls.
- `MainMenuPage` provides separate local duel and computer duel entries. Both entries open `DuelSetupPopup`; only the computer duel entry enables the AI difficulty dropdown.
- AI difficulty options are table-driven by `Assets/Config/DataJson/duel_ai_difficulty/duel_ai_difficulty.json`. `DuelSetupPopup` displays the config `name` values in a fixed rank order and passes the selected config id through `DuelSceneCreateParamas`. The difficulty table includes base selection parameters, board-size-specific realtime overrides for 9x9, 13x13, and 19x19 boards, and optional dynamic-budget thresholds.
- Computer duel creates the same two local `Player` entities as local duel, stores `isAiDuel`, `aiDifficultyCfgId`, and `aiPlayerGuid` on `SceneComponentDuel`, and assigns the AI to Player2 / white by default.
- `DuelAiSystem` is installed in `DuelScene`. During an AI turn it asks KataGo analysis for move candidates using the current KataGo-standard `moves`, filters candidates through the same local move legality rule path, then emits the normal `OnAddChessToBoard` event. Human board click, pass, and resign inputs are ignored while the current turn belongs to the AI player. Realtime AI move requests resolve `maxVisits`, candidate count, and maximum score-loss threshold from the selected board size; request visits use `min(maxVisits, realtimeMaxVisitsN)`. Difficulties with dynamic budget enabled first send a low-visit probe request, then either use the probe result for opening/simple/confident positions or upgrade to the full realtime budget for complex, incomplete, or late-game positions.

**当前行为**

- Unity 入口由 `ClientMain` 初始化；它启动 `XNLogger` 和 `Global`，并把自定义 `Update`、`FixedUpdate`、`LateUpdate` 回调插入 Unity PlayerLoop。
- `Global` 按顺序创建全局模块：事件、资源、定时器、存档、红点、UI、场景。
- 开发构建和 Unity 编辑器环境会加载 IngameDebugConsole 预制体。
- 启动后，场景管理器进入主菜单场景。
- 项目场景位于 `Assets/Scenes/`，当前包含主菜单、主场景和对局场景。
- `MainMenuScene` 加载后打开 `MainMenuPage`。
- `MainMenuPage` 可以打开本地对局或电脑对局；两者都使用 `DuelSetupPopup` 选择对局参数。
- `DuelSetupPopup` 可以用 `9x9`、`13x13`、`19x19` 三个棋盘配置进入对局场景；从电脑对局入口打开时还会显示电脑难度下拉框。
- 棋盘尺寸和对局虚拟相机 y 偏移配置在 `Assets/Config/DataJson/chess_board/chess_board.json`。
- 场景、UI 页面、预制体和 TMP sprite 配置放在 `Assets/Config/DataJson/`，对应的数据读取类放在 `Assets/Config/DataType/`。
- `DuelScene` 创建 `SceneComponentChessBoard` 和 `SceneComponentDuel`，从 `DuelSceneFixedRef` 绑定固定场景引用，安装 `DuelSaveSystem`、`ChessBoardSystem`、`DuelOwnershipSystem`、`DuelSystem`、`DuelAiSystem`，然后打开 `DuelPage`。
- `SceneComponentChessBoard` 保存当前棋盘配置 id、运行时按棋盘位置索引缓存的棋子信息、用于简单重复局面对比的上一局面快照、`RectGrid` 引用和对局虚拟相机引用。
- `SceneComponentDuel` 保存双方玩家 guid、当前回合玩家 guid、时间配置、电脑对局配置、超时/胜者 guid、连续虚手数、终局原因、最终数子分数和运行时 KataGo 标准 `moves` 手顺。
- `RectGrid` 及其相关棋盘类使用 `RectCoordinates` 生成和寻址矩形棋盘；`RectCoordinates` 的逻辑行列语义与 KataGo 坐标保持一致。
- `ChessBoardSystem` 根据所选棋盘尺寸初始化网格，调整对局相机以覆盖棋盘，并在读取存档时通过 KataGo 记录文件回放恢复运行时棋盘缓存和棋子实体。
- `DuelSystem` 在新对局中创建两个本地玩家并启动对局状态机；读取存档时，它会按存档中的玩家 guid 重新创建玩家，并激活回合输入状态。
- 电脑对局仍复用本地双玩家和本地回合 FSM，默认由玩家 1 执黑先行、AI 控制玩家 2 / 白方；AI 难度配置随场景状态保存和读档恢复。
- `DuelFSM` 当前定义本地回合循环：`GameStart -> TurnStart -> TurnInput -> TurnEnd -> TurnStart`，回合输入可以通过落子完成或超时进入回合结束。
- `DuelStateTurnInput` 按当前玩家的持有时间或读秒状态每秒递减一次；无限时间不会启动回合倒计时。
- `DuelStateTurnEnd` 在玩家 1 和玩家 2 之间切换 `curTurnPlayerGuid`。
- `DuelPage` 显示黑方和白方的持有时间、读秒次数和读秒时间；它根据鼠标位置计算最近棋盘坐标，显示落点 VFX，非 UI 区域左键触发 `OnAddChessToBoard`，右下角设置按钮打开对局设置面板，右下角形式按钮触发 `OnRequestDuelOwnership`，形式按钮旁的虚手按钮触发 `OnRequestDuelPass`，形式按钮上方的结果面板显示 ownership 统计出的双方目数，设置面板中的保存按钮触发 `OnSaveDuelScene`，请求数子按钮触发 `OnRequestDuelScore`，认输按钮仅在当前对局处于回合输入且当前行棋玩家有效时显示，点击后通过二次确认触发 `OnConfirmDuelResign`，退出按钮回到主菜单。进入 `GameEnd` 后，页面右侧中部的结算结果面板会显示黑/白方胜出和结束原因。
- 电脑对局的 AI 回合中，`DuelPage` 不接受人类棋盘落子、虚手或认输输入；AI 由 `DuelAiSystem` 请求 KataGo 候选点并通过本地落子规则筛选后走正常落子事件。启用动态预算的难度会先请求低访问次数 probe，再依据当前手数、`rootInfo.scoreLead`、`rootInfo.winrate`、首选和次选 `scoreLoss` 差距等配表阈值决定是否升级完整预算。
- 落子只有在目标坐标位于棋盘内、目标位置为空、当前回合玩家存在时才会继续处理。
- 落子校验会先缓存当前棋盘状态，再移除无气的对方连通棋串，随后拒绝自杀、拒绝单子无气、拒绝与上一局面完全一致的棋盘状态。
- 合法落子接受后，被提掉的棋子实体会被销毁。
- 合法落子接受后，系统会更新 `lastChessInfoDict`，创建新的 `Chess` 实体，并发出 `OnAfterAddChessToBoard`。
- `OnAfterAddChessToBoard` 会推动对局状态机从回合输入进入回合结束。
- `OnRequestDuelPass` 在回合输入状态下记录当前玩家虚手并推动回合结束；双方连续虚手会按 KataGo `ownership` 统计结果结算，然后直接进入 `GameEnd`；如果 ownership 数子失败，会回滚第二手虚手记录并保持当前对局。
- `OnRequestDuelScore` 会先显示“数子中...”确认弹窗且禁用确认按钮，再按 KataGo `ownership` 统计结果更新确认内容；若 KataGo 不可用或无结果，则显示失败且不进入终局。玩家确认后进入 `GameEnd`，取消则继续当前对局。
- `OnConfirmDuelResign` 会把当前行棋方记录为认输方，另一方记录为胜者，并进入 `GameEnd`。
- `GameEnd` 结果面板按终局原因显示结果：数子和连续虚手显示领先目数，超时显示黑/白方超时判负，认输显示黑/白方认输。
- `DuelSaveSystem` 响应 `OnSaveDuelScene`，先通过 `KataGoDuelRecordFile.Save` 保存棋盘记录文件，再通过 `DuelSaveInfoFile.Save` 保存槽位摘要信息，成功后再通过 `GameSaveManager.SaveDataAsync` 把当前场景状态保存到 `GameSaveConfig.GetDuelSceneSavePath(0)`。
- 保存与读取使用 `SavableObj`、`SavableField`、可保存集合和 JSON 文件；Unity Editor 下存档根目录是仓库根目录的 `save/`，PC Standalone 下存档根目录是游戏包体根目录的 `save/`，其他非 Editor 平台默认使用 `Application.persistentDataPath`；对局槽位目录为 `save/0/`、`save/1/` 等，继续对局需要槽位内的 `DuelScene.json`、`DuelRecord.json` 和 `SaveInfo.json` 同时存在。`SaveInfo.json` 记录存档时间、槽位、棋盘配置、时间配置和当前手数。
- 当前包依赖包括 Unity 内置模块、URP、Cinemachine、TextMesh Pro、UGUI、Newtonsoft JSON、AssetBundle Browser 和开发工具包。
- `Packages/manifest.json` 中没有网络或多人相关依赖。
- 按 socket、WebSocket、Netcode、Mirror、Photon、UnityTransport、HTTP client、TCP、UDP、multiplayer、联机等关键词扫描 `Assets` 后，没有发现项目网络实现。

## Fixed Rules

**固定规则**

- 本地对局是当前行为基线。
- 棋盘坐标使用矩形网格坐标，字段为 `x` 和 `z`。
- 棋盘位置索引公式为 `z * gridSize + x`。
- 玩家标记通过 `DuelUtils.GetGamePrefabTypeIdWithPlayerFlag` 映射到黑白棋子预制体。
- 对局棋盘恢复权威是槽位目录中 `DuelRecord.json` 的 KataGo 标准 `moves`，场景存档不再保存 `SceneComponentChessBoard.chessInfoDict`。
- `chessInfoDict` 和 `lastChessInfoDict` 跳过保存检查，只作为运行时棋盘缓存和局面对比状态；读档时由记录文件回放重建。
- 非法落子当前只做逻辑回退，没有用户可见提示。
- 死子确认、复盘、匹配、房间、重连和网络同步当前未实现；数子、虚手、认输和基础终局结果 UI 已有本地原型实现，但尚未覆盖死子确认或完整线上裁定模型。当前阶段“请求数子”和连续虚手终局只依赖 KataGo `ownership` 结算，KataGo 不可用或无结果时不产生数子结果。

## Validation and Maintenance

## 2026-05-20 Runtime Asset Addendum

- Runtime-only assets loaded by code and not guaranteed to be referenced by scenes or prefabs are declared in `ConfigExporter/xlsx/runtime_asset.xlsx`.
- The exported runtime asset config is `Assets/Config/DataJson/runtime_asset/runtime_asset.json`; the generated reader is `Assets/Config/DataType/runtime_asset/RuntimeAssetDataType.cs`.
- The current runtime asset table contains the shared black and white chess-board materials used by ownership overlay and latest-move marker rendering.
- `ChessBoardSystem` loads these materials through `RuntimeAssetDataType` and `ResourceManager`, then injects them into `RectGrid`. `RectGrid` does not call `Shader.Find` or read global config directly.
- Latest-move marker draw failures are logged and isolated so a visual material issue does not block `AppendKataGoMove` or `OnAfterAddChessToBoard`.

**验证与维护**

- 当前行为验证应覆盖：从配置的启动场景进入运行、从主菜单打开本地对局和电脑对局、分别启动 `9x9`、`13x13`、`19x19` 棋盘、电脑对局难度下拉框读表、正常落子、尝试已有棋子位置、尝试棋盘外坐标、尝试自杀、尝试简单重复局面、保存对局、读取保存状态。
- 棋规、回合、场景、UI、资源、存档或依赖的当前行为发生变化时，需要更新本文档。
- 网络依赖或网络实现一旦成为当前行为，也需要更新本文档。
