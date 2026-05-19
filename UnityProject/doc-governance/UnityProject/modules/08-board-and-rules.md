# 棋盘与规则模块

## 主要文件

- `Assets/Scripts/Game/ChessBoard/ChessBoardConfig.cs`
- `Assets/Scripts/Game/ChessBoard/RectGrid.cs`
- `Assets/Scripts/Game/ChessBoard/RectGridChunk.cs`
- `Assets/Scripts/Game/ChessBoard/RectCell.cs`
- `Assets/Scripts/Game/ChessBoard/RectCoordinates.cs`
- `Assets/Scripts/Game/ChessBoard/RectMesh.cs`
- `Assets/Scripts/Game/ChessBoard/ChessBoardUtils.cs`
- `Assets/Scripts/Game/System/ChessBoardSystem.cs`
- `Assets/Scripts/Game/Component/SceneComponent/SceneComponentChessBoard.cs`
- `Assets/Config/DataJson/chess_board/chess_board.json`

## 职责

棋盘模块负责棋盘生成、棋盘坐标、棋子状态存储、落子合法性、提子、自杀禁手和棋子实体创建/销毁。

## 当前进度

- 支持 `9x9`、`13x13`、`19x19` 三种棋盘。
- `RectCoordinates` 直接采用 KataGo 棋盘布局：`x` 从左到右递增，`z` 从棋盘上边向下递增；棋盘缓存索引使用 `z * boardSize + x`。
- `RectGrid` 按配置尺寸生成棋盘并计算边界。
- `RectGridChunk` 负责分块 mesh 和棋盘视觉结构。
- `RectGrid` 可以绘制和清除 ownership overlay：形势分析结果会按 KataGo `ownership` 行序直接在棋盘交叉点显示黑白小方块，作为 AI 预测控制区域的表现层，不写入棋盘规则状态。
- `SceneComponentChessBoard` 负责棋盘配置 id、运行时当前棋子信息、上一局面棋子信息、棋盘引用和虚拟相机引用；棋子字典不再作为持久化棋盘权威。
- `ChessBoardSystem.Init()` 根据棋盘配置初始化网格，设置相机俯视棋盘，并在读档时通过 KataGo 记录文件回放恢复运行时棋盘缓存和棋子实体。
- `OnAddChessToBoard` 是当前落子主入口。
- 已检查目标位置是否在棋盘内、是否为空、当前玩家是否存在。
- 已通过 BFS 检查相邻对方棋串是否无气并提子。
- 已检查提子后是否导致自杀。
- 已检查单独落子是否有气。
- 已用 `lastChessInfoDict` 和当前棋盘状态比较来阻止完全重复局面。
- KataGo 编辑器验证尚未进入当前规则实现；它应读取标准 `moves` 或调试快照做 AI 形势分析，而不是参与落子合法性或正式计分。
- 合法落子成功后会记录一条 KataGo 标准 `moves` 数组项，例如 `["B","Q16"]`；保存对局时该手顺写入单独的 KataGo analysis JSON 记录文件，读档时棋盘由该记录文件回放恢复。

## 设计观察

当前规则逻辑已经能支撑本地基础围棋对局。联机阶段最重要的是把这个逻辑变成可复用的权威校验入口，而不是在网络层复制一份规则。

## 风险和缺口

- 非法落子目前没有 UI 消息。
- 简单防重复局面只能说明“当前实现会阻止前后局面完全一致”，还没有完整定义复杂劫争规则。
- 没有 pass、resign、终局、数目、死子确认。
- KataGo `ownership` 只能表示 AI 预测的控制区域，不能替代死子确认、数目或数子规则。
- 规则逻辑和棋子实体创建/销毁混在同一个系统方法中，后续服务端校验复用会困难。
- `visited` 是 `ChessBoardSystem` 成员字段，当前单线程本地流程可用；如果未来异步或并发验证，需要改成局部状态。

## 后续建议

- 提取 `TryApplyMove` 风格的规则方法，输入当前棋盘状态和落子命令，输出合法性、提子列表、新棋盘状态和错误原因。
- 增加非法原因枚举：越界、已有棋子、自杀、打劫、非当前玩家、对局未在输入状态。
- 在 UI 层订阅非法原因并显示提示。
- 为 KataGo 分析提供只读 JSON 构造：正常路径直接使用运行时 `moves`；当前棋盘快照转换为 `initialStones` 只用于调试或无手顺场景，不在转换过程中修改棋盘状态。
- 联机前优先让本地和网络共用同一条落子校验路径。
