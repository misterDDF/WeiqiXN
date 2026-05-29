# 棋盘与规则模块

## 主要文件

- `Assets/Scripts/Game/ChessBoard/ChessBoardConfig.cs`
- `Assets/Scripts/Game/ChessBoard/RectGrid.cs`
- `Assets/Scripts/Game/ChessBoard/RectGridChunk.cs`
- `Assets/Scripts/Game/ChessBoard/RectCell.cs`
- `Assets/Scripts/Game/ChessBoard/RectCoordinates.cs`
- `Assets/Scripts/Game/Component/SceneComponent/ChessStoneViewCache.cs`
- `Assets/Scripts/Game/ChessBoard/RectMesh.cs`
- `Assets/Scripts/Game/ChessBoard/ChessBoardUtils.cs`
- `Assets/Scripts/Game/System/ChessBoardSystem.cs`
- `Assets/Scripts/Game/Component/SceneComponent/SceneComponentChessBoard.cs`
- `Assets/Config/DataJson/chess_board/chess_board.json`

## 职责

棋盘模块负责棋盘生成、棋盘坐标、棋子状态存储、落子合法性、提子、自杀禁手和棋子表现同步。

## 当前进度

- 支持 `9x9`、`13x13`、`19x19` 三种棋盘。
- `RectCoordinates` 直接采用 KataGo 棋盘布局：`x` 从左到右递增，`z` 从棋盘上边向下递增；棋盘缓存索引使用 `z * boardSize + x`。
- `RectGrid` 按配置尺寸生成棋盘并计算边界。
- `RectGridChunk` 负责分块 mesh 和棋盘视觉结构。
- `RectGrid` 会在棋盘外边框区域生成围棋常用坐标标签，列标跳过 `I`，行标按从上到下递减显示；标签使用加粗平铺文本并只作为纯表现层提示，不写入规则状态。外边框和坐标标签由 `RectGrid.SetBoardCoordinateFrameVisible(bool)` 统一切换，便于后续设置项控制。
- `RectGrid` 可以绘制和清除 ownership overlay：形势分析结果会按 KataGo `ownership` 行序直接在棋盘交叉点显示黑白小方块，低于当前阈值的中立或未明确控制点不绘制；同色相邻控制点之间会用对应颜色细线连接；overlay 位于棋子模型上方，只作为 AI 预测控制区域的表现层，不写入棋盘规则状态。
- `ChessStoneViewCache` / `ChessStoneView` 负责棋子上方标记：对局最新手三角和复盘手数数字都绑定到当前可见棋子，黑棋使用白色标记、白棋使用黑色标记；棋子隐藏、提掉或复用时自动清理，落子动画到达棋面后显示，不等待后续抖动完全结束。标记只作为表现层，不写入棋盘规则状态。
- `ReplaySystem` 使用棋子级手数数字：普通复盘只标当前最新非虚手主线手数，试下模式标仍留在棋盘上的每一步试下分支编号。手数数字与最新手三角通过单一 `StoneMarkerIntent` 互斥。
- `SceneComponentChessBoard` 负责棋盘配置 id、运行时当前棋子信息、上一局面棋子信息、棋盘引用和虚拟相机引用；棋子字典不再作为持久化棋盘权威。
- `ChessStoneViewCache` 负责棋子 prefab 表现缓存。规则状态仍以 `SceneComponentChessBoard.chessInfoDict` 为权威；普通落子、提子、LAN 快照纠偏、读档恢复和悔棋重建只把最终棋盘状态同步给表现缓存，由缓存按棋盘位置显示、隐藏或复用黑白棋子 prefab，避免整盘销毁重建造成闪动。
- `ChessBoardSystem.Init()` 根据棋盘配置初始化网格，设置对局虚拟相机为轻透视俯视：相机仍看向棋盘中心并自动按棋盘尺寸和屏幕宽高比完整取景，但使用较窄 FOV 和小倾角保留少量真实桌面透视。`ReplayScene` 在非竖屏视图下额外使用水平偏移为桌面 HUD 留空间；竖屏判断复用 UI 的 `height > width` 口径，竖屏复盘保持棋盘居中取景。对局画面的后处理由 Duel 场景显式维护：主相机开启 URP post-processing，全局 `DuelLookVolume` 引用 `Assets/Scenes/Duel/Profiles/DuelLookProfile.asset`，Profile 包含 ACES tonemapping、轻微色彩校正、低强度 Bloom 和 Vignette；读档/继续对局暂不作为当前正式功能。
- `OnSubmitDuelMove` 是当前页面和 AI 的正常落子提交入口；`DuelAuthoritySystem` 在本地/电脑对局中转入本进程权威落子应用，在 LAN 对局中先读取 `DuelInputAuthority` 的本端输入权限，再提交到 `LanRoomService`，由房间服务决定本端 host 入队还是远端 TCP 发送。`OnAddChessToBoard` 仍保留为本地落子应用的兼容入口，不再作为 UI/AI 首选入口。
- `DuelMoveRule` 提供领域落子命令和结果模型：`DuelMoveCommand` 描述落子方、坐标和棋子 guid；`DuelMoveResult` 描述是否接受、拒绝原因、上一局面、下一局面和待移除位置；`DuelMoveRejectReason` 记录非法原因。
- `DuelMoveRule.BuildMoveResult()` 先在临时棋盘缓存上模拟落子并生成结果，结束后恢复原棋盘引用；AI 候选检查和非法性判断不会保留模拟状态。`TryBuildMoveResult()` 作为兼容入口保留，但后续新调用优先使用结果对象本身。
- `DuelMoveRule.ApplyMoveResult()` 是当前统一应用口径；真实落子和悔棋回放都只应用 accepted result。
- 真实落子被拒绝时，`ChessBoardSystem` 会发出 `OnDuelMoveRejected`，携带落子方、坐标和 `DuelMoveRejectReason`；当前 `DuelPage` 不显示额外非法落子文案，而是在预览阶段只对本端有输入权且合法的点显示预览棋子。
- 已检查目标位置是否在棋盘内、是否为空、当前玩家是否存在。
- 已通过 BFS 检查相邻对方棋串是否无气并提子。
- 已检查提子后是否导致自杀。
- 已检查单独落子是否有气。
- 已用 `lastChessInfoDict` 和当前棋盘状态比较来阻止完全重复局面。
- KataGo 编辑器验证不参与当前落子合法性；AI 分析应读取标准 `moves`，形势展示和当前 ownership 数子结算应读取当前盘面快照。
- 合法落子成功后会记录一条 KataGo 标准 `moves` 数组项，例如 `["B","Q16"]`；虚手会记录 `["B","pass"]` 或 `["W","pass"]`。保存对局时该手顺写入单独的 KataGo analysis JSON 记录文件，`pass` 项不改变棋盘状态。
- 让子配置由 `duel_handicap` 提供：分先贴目 `7.5`，让先贴目 `0.5` 且不预摆黑子，让子贴目 `0.5` 并按配置预摆黑子。让先仍由黑方先行，只有真正预摆让子的配置才由白方下第一手。

## 设计观察

当前规则逻辑已经能支撑本地基础围棋对局。联机阶段最重要的是把这个逻辑变成可复用的权威校验入口，而不是在网络层复制一份规则。

## 风险和缺口

- 非法落子的主要可见反馈是“不显示预览棋子”；`OnDuelMoveRejected` 只作为系统边界事件保留，当前页面不额外弹出无法落子提示。
- 简单防重复局面只能说明“当前实现会阻止前后局面完全一致”，还没有完整定义复杂劫争规则。
- 已有虚手、请求数子、双方连续虚手终局、认输和基础终局结果 UI 原型；仍没有死子确认和线上裁定模型。
- 当前“请求数子”和双方连续虚手只依赖当前盘面快照生成的 KataGo `ownership` 统计结果；本地棋盘状态数子算法不再作为回退路径。统计时白方加上当前让子配置的 `komi`，该口径仍未覆盖死子确认或完整线上裁定模型。
- 当前规则结果已经和棋子 prefab 生命周期分离，但 `DuelMoveRule` 仍直接依赖 `SceneComponentChessBoard` 和 `SavableObjectDict<ChessInfo>`；后续若要服务端或非 Unity 进程复用，还需要进一步抽出纯棋盘状态模型。

## 后续建议

- 继续收敛旧 `TryApplyMove` / `TryBuildMoveResult` 兼容调用点，逐步让外层系统只依赖 `BuildMoveResult()` 返回的 `DuelMoveResult`。
- 扩展非法原因枚举：当前已有棋盘无效、命令无效、越界、已有棋子、自杀、重复局面；后续可补非当前玩家、对局未在输入状态等流程原因。
- 后续补齐非当前玩家、对局状态不允许落子等流程级拒绝原因；除非产品明确需要，否则 UI 不额外弹出无法落子提示。
- 为 KataGo 分析提供只读 JSON 构造：AI、保存和复盘分析路径使用运行时 `moves`；让子棋预置黑子通过配置生成 `initialStones`，不写入 `moves`；形势展示和 ownership 数子路径把当前棋盘快照转换为 `initialStones` 并置空 `moves`，不在转换过程中修改棋盘状态。
- 继续把虚手、认输、数子和悔棋收敛到与正常落子一致的命令/结果边界。
