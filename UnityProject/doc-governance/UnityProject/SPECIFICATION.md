# WeiqiXN UnityProject 规格说明

## Scope

**范围**

- 本文档记录 Unity 围棋项目当前已经实现的系统行为。
- 本文档覆盖启动流程、本地对局流程、棋盘配置、落子处理、提子校验、场景与 UI 入口、存档行为和当前依赖。
- 本文档不定义联机计划、架构理由或阶段路线；这些内容分别归属 [ARCHITECTURE.md](ARCHITECTURE.md) 和 [ROADMAP.md](ROADMAP.md)。

## Current Behavior

### 2026-05-15 Current Addendum

- `DuelSetupPopup` now passes board, hold-time, byoyomi-count, and byoyomi-time config ids into `DuelSceneCreateParamas`; when the prefab is still on the old three-board-button layout, selecting a board starts a game with default time settings.
- Hold-time options are table-driven by `Assets/Config/DataJson/duel_hold_time/duel_hold_time.json`: `2m`, `5m`, `10m`, `20m`, and `infinite`.
- Byoyomi count options are table-driven by `Assets/Config/DataJson/duel_byoyomi_count/duel_byoyomi_count.json`: `off`, `1`, `3`, and `5`. `off` means no byoyomi after hold time runs out.
- Byoyomi period options are table-driven by `Assets/Config/DataJson/duel_byoyomi_time/duel_byoyomi_time.json`: `10s`, `20s`, `30s`, and `60s`.
- When `DuelSetupPopup` selects infinite hold time, byoyomi is forced to `off` and byoyomi count/period buttons are disabled.
- `DuelSystem` initializes both local players from the selected time-control configs and stores current time-control config ids on `SceneComponentDuel`.
- `DuelStateTurnInput` counts down the current player's hold time first. If byoyomi is enabled, the player enters byoyomi after hold time reaches zero; each byoyomi period timeout consumes one byoyomi count, and exhausting the count records `timeoutLoserGuid` / `winnerGuid` and enters `GameEnd`.
- `DuelPage` still displays the current player's `turnLeftTimes`; this value now reflects hold-time seconds, byoyomi period seconds, or `-1` for infinite time.

**当前行为**

- Unity 入口由 `ClientMain` 初始化；它启动 `XNLogger` 和 `Global`，并把自定义 `Update`、`FixedUpdate`、`LateUpdate` 回调插入 Unity PlayerLoop。
- `Global` 按顺序创建全局模块：事件、资源、定时器、存档、红点、UI、场景。
- 开发构建和 Unity 编辑器环境会加载 IngameDebugConsole 预制体。
- 启动后，场景管理器进入主菜单场景。
- 项目场景位于 `Assets/Scenes/`，当前包含主菜单、主场景和对局场景。
- `MainMenuScene` 加载后打开 `MainMenuPage`。
- `DuelSetupPopup` 可以用 `9x9`、`13x13`、`19x19` 三个棋盘配置进入对局场景。
- 棋盘尺寸和对局虚拟相机 y 偏移配置在 `Assets/Config/DataJson/chess_board/chess_board.json`。
- 场景、UI 页面、预制体和 TMP sprite 配置放在 `Assets/Config/DataJson/`，对应的数据读取类放在 `Assets/Config/DataType/`。
- `DuelScene` 创建 `SceneComponentChessBoard` 和 `SceneComponentDuel`，从 `DuelSceneFixedRef` 绑定固定场景引用，安装 `DuelSaveSystem`、`ChessBoardSystem`、`DuelSystem`，然后打开 `DuelPage`。
- `SceneComponentChessBoard` 保存当前棋盘配置 id、按棋盘位置索引存储的棋子信息、用于简单重复局面对比的上一局面快照、`RectGrid` 引用和对局虚拟相机引用。
- `RectGrid` 及其相关棋盘类使用 `RectCoordinates` 生成和寻址矩形棋盘。
- `ChessBoardSystem` 根据所选棋盘尺寸初始化网格，调整对局相机以覆盖棋盘，并在读取存档时恢复棋子实体。
- `DuelSystem` 在新对局中创建两个本地玩家并启动对局状态机；读取存档时，它会按存档中的玩家 guid 重新创建玩家，并激活回合输入状态。
- `DuelFSM` 当前定义本地回合循环：`GameStart -> TurnStart -> TurnInput -> TurnEnd -> TurnStart`，回合输入可以通过落子完成或超时进入回合结束。
- `DuelStateTurnInput` 为当前玩家设置 30 秒回合时间，并每秒递减一次。
- `DuelStateTurnEnd` 在玩家 1 和玩家 2 之间切换 `curTurnPlayerGuid`。
- `DuelPage` 显示当前状态、当前玩家和回合时间；它根据鼠标位置计算最近棋盘坐标，显示落点 VFX，左键触发 `OnAddChessToBoard`，保存时触发 `OnSaveDuelScene`，退出时回到主菜单。
- 落子只有在目标坐标位于棋盘内、目标位置为空、当前回合玩家存在时才会继续处理。
- 落子校验会先缓存当前棋盘状态，再移除无气的对方连通棋串，随后拒绝自杀、拒绝单子无气、拒绝与上一局面完全一致的棋盘状态。
- 合法落子接受后，被提掉的棋子实体会被销毁。
- 合法落子接受后，系统会更新 `lastChessInfoDict`，创建新的 `Chess` 实体，并发出 `OnAfterAddChessToBoard`。
- `OnAfterAddChessToBoard` 会推动对局状态机从回合输入进入回合结束。
- `DuelSaveSystem` 响应 `OnSaveDuelScene`，通过 `GameSaveManager.SaveDataAsync` 把当前场景保存到 `GameSaveConfig.GetDuelSceneSavePath(0)`。
- 保存与读取使用 `SavableObj`、`SavableField`、可保存集合和 JSON 文件。
- 当前包依赖包括 Unity 内置模块、URP、Cinemachine、TextMesh Pro、UGUI、Newtonsoft JSON、AssetBundle Browser 和开发工具包。
- `Packages/manifest.json` 中没有网络或多人相关依赖。
- 按 socket、WebSocket、Netcode、Mirror、Photon、UnityTransport、HTTP client、TCP、UDP、multiplayer、联机等关键词扫描 `Assets` 后，没有发现项目网络实现。

## Fixed Rules

**固定规则**

- 本地对局是当前行为基线。
- 棋盘坐标使用矩形网格坐标，字段为 `x` 和 `z`。
- 棋盘位置索引公式为 `z * gridSize + x`。
- 玩家标记通过 `DuelUtils.GetGamePrefabTypeIdWithPlayerFlag` 映射到黑白棋子预制体。
- 对局存档中的棋盘状态存放在 `SceneComponentChessBoard.chessInfoDict`。
- `lastChessInfoDict` 跳过保存检查，只作为运行时局面对比状态。
- 非法落子当前只做逻辑回退，没有用户可见提示。
- 胜负结算、停一手、认输、数目、死子确认、复盘、匹配、房间、重连和网络同步当前未实现。

## Validation and Maintenance

**验证与维护**

- 当前行为验证应覆盖：从配置的启动场景进入运行、从主菜单打开对局、分别启动 `9x9`、`13x13`、`19x19` 棋盘、正常落子、尝试已有棋子位置、尝试棋盘外坐标、尝试自杀、尝试简单重复局面、保存对局、读取保存状态。
- 棋规、回合、场景、UI、资源、存档或依赖的当前行为发生变化时，需要更新本文档。
- 网络依赖或网络实现一旦成为当前行为，也需要更新本文档。
