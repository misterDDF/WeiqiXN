# WeiqiXN UnityProject 架构说明

## Design Goals

**设计目标**

- 让核心游戏逻辑尽量脱离 Unity `MonoBehaviour` 生命周期。
- 让 Unity 场景和预制体负责表现与固定引用，让项目层系统负责状态流转和规则处理。
- 保持本地对局作为后续联机功能的回归基线。
- 在现有配置层已经支持的范围内，让棋盘尺寸、预制体选择和页面入口保持数据驱动。
- 让后续联机对局能够复用同一套确定性的落子校验路径。
- 允许 AI 控制区域作为可替换的本地分析适配器接入；当前原型阶段可让终局结算临时复用同一份 `ownership` 口径，但要把这件事明确记录为过渡方案。
- 当前数子结算只依赖 KataGo `ownership`；KataGo 不可用或无结果时不产生数子结果、不进入终局。后续补齐死子确认后，再评估是否把正式数子收回到本地规则和明确的计分流程。

## Module Boundaries

**模块边界**

- `ClientMain` 是进程入口和 PlayerLoop 桥接层，负责初始化全局服务，并在 Unity 对应更新阶段后调用项目层更新。
- `Global` 和 `GlobalModule` 提供跨场景服务，包括 UI、资源加载、事件、定时器、场景加载、存档、红点和日志接入。
- `MainMenuScene`、`DuelScene` 等场景类负责项目层场景组合：创建场景组件、添加系统、打开对应 UI。
- 场景组件负责保存场景状态和固定引用。`SceneComponentChessBoard` 拥有棋盘配置、运行时棋盘缓存、`RectGrid` 和对局虚拟相机引用；`SceneComponentDuel` 拥有对局玩家 guid、对局状态机、电脑对局配置、连续虚手数、终局结果字段和运行时 KataGo 标准 `moves` 手顺。
- 系统负责行为。`ChessBoardSystem` 负责棋盘初始化、落子结果应用、提子、棋子实体放置和相机设置；`DuelMoveRule` 负责构建落子命令结果，输出 accepted/rejected、拒绝原因、上一局面、下一局面和待移除位置；真实落子和悔棋回放都只应用 accepted result。`DuelSystem` 负责本地玩家创建、电脑对局状态初始化、状态机更新、虚手、悔棋、数子结算请求和终局触发：当前“请求数子”和双方连续虚手只使用 KataGo `ownership` 统计结果作为结算口径，失败时不产生数子结果；`DuelSaveSystem` 负责对局保存触发并通过 `OnDuelSaveResult` 暴露保存成功/失败；`DuelOwnershipSystem` 负责响应形势请求并绘制 overlay；`DuelOwnershipQueryService` 负责集中向 KataGo 请求 `ownership`、按同一阈值统计双方控制点数、构建数子结果并复用缓存；`DuelAiBudgetService` 负责 AI 棋盘尺寸预算参数解析、probe/full budget 决策和预算日志；`DuelAiAnalyzeService` 负责电脑对局 AI 的 KataGo 分析请求构造、probe/full 请求调度和结果元信息返回；`DuelAiMoveSelector` 负责解析 `moveInfos`/`policy`、按本地规则筛选合法候选点并按难度配置加权选点；`DuelAiSystem` 只负责 AI 回合监听、取消检查和提交正常落子或虚手事件。`SceneComponentDuel` 保存一份运行时 ownership 结果缓存，供形势展示和数子结算在局面未变化时复用；合法落子或虚手会使该缓存失效。
- 实体表示运行时游戏对象。`Chess` 是带 Unity `GameObject` 的棋子实体；`Player` 是回合归属实体，并通过组件保存对局信息。
- 事件连接 UI、系统和实体。UI 发出 `OnAddChessToBoard`、`OnSaveDuelScene` 等系统事件；系统发出 `OnAfterAddChessToBoard`、`OnDuelMoveRejected` 等领域结果事件，UI 只展示结果，不重复承担棋规判断。
- `DuelPage` 只负责页面生命周期、事件注册和按钮命令转发；`DuelPageBoardInputController` 负责棋盘鼠标命中、合法预览棋子生命周期和点击落子坐标输出；`DuelPageHudView` 负责玩家信息、形势结果、数子确认文案、终局面板、动作提示和设置按钮状态；`DuelPageInteractionState` 集中计算人类回合输入、悔棋、认输、AI 身份和读秒显示可用性。
- `DuelFSM` 表示本地回合生命周期，回合开始、输入、超时和结束由状态机管理，而不是由 UI 直接切换。
- `Assets/Config/DataJson` 是当前棋盘、场景、UI 页面、预制体和 TMP sprite 的数据来源。
- 日志开关集中在 `LoggerConfig`。事件分发、FSM 参数/transition、AI 观察日志和 AI 分析细节日志默认关闭；错误、警告、关键启动、AI 回合开始和最终落子/虚手决策仍保持常规输出。需要排查时可临时打开 `ENABLE_EVENT_VERBOSE_LOG`、`ENABLE_FSM_VERBOSE_LOG`、`ENABLE_DUEL_AI_VERBOSE_LOG` 或 `ENABLE_DUEL_AI_DETAIL_LOG`。
- 资源加载通过 `ResourceManager` 和配置 id 抽象；编辑器环境使用 AssetDatabase，非编辑器环境使用 AssetBundle。
- 存档通过 `SavableObj`、`SavableField` 和可保存集合持久化场景与用户状态；对局保存会额外输出 KataGo analysis JSON 记录文件，供 ownership analysis 和后续复盘/读档设计使用。读档/继续对局暂不作为当前正式功能。
- KataGo 接入作为 Windows Unity Editor 和 Windows PC 包共用的本地子进程适配器存在：Unity 侧负责启动 `katago analysis`、通过 Win32 pipe 交换 stdin/stdout JSON、解析第一版形势按钮所需的 `ownership` 和电脑对局所需的 `moveInfos`，并把启动失败、超时、缺少模型或配置文件等情况转成可诊断状态。KataGo 运行源目录位于仓库根 `KataGo/`，Windows PC 构建成功后复制到包体根目录 `<BuildRoot>/KataGo/`，不进入 Unity `Assets` 导入体系。Windows 下优先尝试 OpenCL 引擎，初始化或 smoke test 失败时在适配器内部 fallback 到 Eigen AVX2 CPU 引擎；若启动时检测到游戏根目录不可写，则弹窗提示、跳过 OpenCL，并用 CPU 引擎的 `analysis_nowrite.cfg` 关闭 KataGo 文件写入。形势展示由 UI 发事件、系统请求分析、棋盘表现层绘制 overlay；电脑对局由 AI 分析服务读取 KataGo 结果，再由 AI 回合系统发出领域落子事件，不让 KataGo 适配器直接修改棋盘规则状态。
- 游戏侧手顺格式应直接使用 KataGo 标准 `moves` 数组项，不再维护额外字符串棋谱格式或转换层。`DuelMoveHistory` 是当前手顺访问边界，负责创建、追加、克隆、截断、尾部虚手统计和输出 KataGo `moves`；合法落子记录标准点位，虚手记录 `pass`；KataGo analysis JSON 生成优先输出 `moves`，是形势按钮、记录文件和后续复盘分析的统一路径；当前盘面 `initialStones` 快照入口只作为调试或无手顺场景使用。

## Coordinate Contract

**坐标契约**

- 本地棋盘逻辑坐标以 KataGo 布局为权威：`RectCoordinates.x` 从左到右递增，`RectCoordinates.z` 从棋盘上边向下递增，棋盘缓存索引和 KataGo `ownership` 都使用 `z * boardSize + x` 行序。Unity 本地空间中的显示位置由棋盘表现层从该逻辑坐标计算，不在 KataGo JSON 边界维护第二套转换语义。

## Key Tradeoffs

**关键取舍**

- 自定义 PlayerLoop 桥接让核心逻辑集中在模块层，但维护者需要理解非 Mono 系统也会逐帧更新。
- UI 通过事件与系统通信，避免页面直接修改棋盘内部状态，但事件定义会成为玩法合同的一部分。
- 棋盘运行状态使用字典缓存，便于规则校验和实体同步；保存侧输出 KataGo 标准记录文件，避免后续复盘或读档设计再引入第二套棋谱格式。
- 当前落子校验通过 `DuelMoveRule.BuildMoveResult()` 在临时棋盘缓存上模拟，结束后恢复原棋盘引用，再由外层系统应用 accepted result。这个结构降低了真实落子、AI 检查和悔棋回放的分叉风险；代价是规则结果仍依赖 `SceneComponentChessBoard` 和 `SavableObjectDict<ChessInfo>`，后续若要服务端或非 Unity 进程复用，还需要继续抽出纯棋盘状态模型。
- 电脑对局复用本地双玩家、FSM 和落子事件，能降低对现有本地对局基线的影响；代价是 AI 可用性取决于本地 KataGo 子进程、模型和难度配置，后续客户端打包仍需要单独处理资源分发和平台支持。
- 当前 FSM 适合本地热座对局。联机对局需要明确权威方和同步边界，不能让两个客户端各自自由认定最终棋盘。
- 数据驱动棋盘尺寸可以避免为不同棋盘复制场景，但规则与 UI 必须持续使用一致的配置 id。
- KataGo 本地子进程可以避免形势判断依赖网络服务，但会引入平台二进制、模型文件、首次初始化耗时、目录写权限和资源分发问题；当前策略是把 KataGo 放在 Unity `Assets` 外并在 Windows 构建后复制到包体根目录，目录不可写时自动降级为 CPU no-write 模式。
- 记录文件采用可直接提交给 KataGo analysis engine 的 JSON 结构，会把保存侧棋谱输出和 ownership 请求骨架统一到同一份标准格式；代价是后续若恢复读档/继续对局为正式功能，必须重新明确记录文件、场景存档和棋盘尺寸一致性策略。

## Guardrails

## 2026-05-20 Runtime Asset Addendum

- Code-loaded assets that must ship in player builds but may not be referenced by scenes or prefabs must be declared in `ConfigExporter/xlsx/runtime_asset.xlsx`.
- `AssetBundleGenerator.PackRuntimeAssetTable` consumes the exported `runtime_asset.json`, validates asset path and type, and applies the configured AssetBundle label before bundle build.
- Runtime code should load declared assets through generated config data and `ResourceManager`; do not add new runtime `Shader.Find` or ad hoc string asset lookups for shipped assets.
- Assembly boundaries remain respected: `XNClient.ChessBoard` owns board rendering primitives, while `ChessBoardSystem` in the main game assembly resolves table/config/global resources and injects materials into `RectGrid`.

**架构护栏**

- 不要把权威玩法状态修改直接写进 UI 页面类；UI 应该发出命令或事件，并展示状态。
- 不要绕过 `DuelMoveRule` 的落子结果模型来实现联机、AI 或回放。现有合法落子路径必须继续作为本地基线，或被明确提取为共享规则服务。
- 不要让网络代码直接耦合 Unity 预制体实例化；网络命令应描述领域动作，而不是描述预制体操作。
- 除非在线架构明确选择并记录客户端锁步，否则不要让远端客户端独立决定最终棋盘状态。
- 当前原型阶段“请求数子”和双方连续虚手复用 KataGo `ownership` 作为结算口径，以和形势展示保持一致；KataGo 不可用或无结果时不产生数子结果，也没有本地结算回退。后续补齐死子确认后，再评估是否把正式数子收回到本地规则和明确的计分流程。第一版也不要把 `rootInfo.scoreLead`、胜率或最佳选点纳入形势按钮或终局产品输出。
- 不要让 KataGo 适配器直接修改 `SceneComponentChessBoard` 或落子状态；它只能读取局面快照并返回分析结果供表现层或调试层展示。
- 电脑对局不能绕过本地落子规则。KataGo 返回的候选点必须先通过本地规则入口校验，再由系统发出正常 `OnAddChessToBoard` 事件。
- 不要新增非 KataGo 标准的内部棋谱格式；正常对局、保存、ownership 请求和后续复盘/读档设计都应围绕同一个 `moves` 表达。
- 不要把 `Library/`、`Temp/`、`Logs/`、IDE 生成文件、导入包内部文件或构建输出当成架构权威。
- 在 [ROADMAP.md](ROADMAP.md) 未移动阶段前，不要把当前本地对局范围扩展到匹配、重连、观战或完整线上终局裁定；当前结算临时只依赖 KataGo `ownership`，正式死子确认和线上裁定口径需要在后续阶段重新明确。
