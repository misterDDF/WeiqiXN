# WeiqiXN UnityProject 架构说明

## Design Goals

**设计目标**

- 让核心游戏逻辑尽量脱离 Unity `MonoBehaviour` 生命周期。
- 让 Unity 场景和预制体负责表现与固定引用，让项目层系统负责状态流转和规则处理。
- 保持本地对局作为后续联机功能的回归基线。
- 在现有配置层已经支持的范围内，让棋盘尺寸、预制体选择和页面入口保持数据驱动。
- 让后续联机对局能够复用同一套确定性的落子校验路径。
- 允许 AI 控制区域作为可替换的本地分析适配器接入；当前原型阶段可让终局结算临时复用同一份 `ownership` 口径，但要把这件事明确记录为过渡方案。
- 当前保留本地原型数子作为 KataGo `ownership` 不可用时的兜底；后续补齐死子确认后，再把正式数子收回到本地规则和明确的计分流程。

## Module Boundaries

**模块边界**

- `ClientMain` 是进程入口和 PlayerLoop 桥接层，负责初始化全局服务，并在 Unity 对应更新阶段后调用项目层更新。
- `Global` 和 `GlobalModule` 提供跨场景服务，包括 UI、资源加载、事件、定时器、场景加载、存档、红点和日志接入。
- `MainMenuScene`、`DuelScene` 等场景类负责项目层场景组合：创建场景组件、添加系统、打开对应 UI。
- 场景组件负责保存场景状态和固定引用。`SceneComponentChessBoard` 拥有棋盘配置、运行时棋盘缓存、`RectGrid` 和对局虚拟相机引用；`SceneComponentDuel` 拥有对局玩家 guid、对局状态机、电脑对局配置、连续虚手数、终局结果字段和运行时 KataGo 标准 `moves` 手顺。
- 系统负责行为。`ChessBoardSystem` 负责棋盘初始化、落子合法性、提子、棋子实体放置和相机设置；`DuelSystem` 负责本地玩家创建、电脑对局状态初始化、状态机更新、虚手、数子结算请求和终局触发：当前“请求数子”和双方连续虚手会优先请求 KataGo `ownership` 统计结果作为结算口径，失败时回退到本地原型数子；`DuelSaveSystem` 负责对局保存触发；`DuelOwnershipSystem` 负责响应形势请求、向 KataGo 请求 `ownership`、按同一阈值统计双方控制点数，并把 overlay 结果交给棋盘表现层、把目数结果通过事件交给 UI 展示；`DuelAiSystem` 负责在电脑对局的 AI 回合请求 KataGo 候选点、按本地规则筛选合法点，并通过正常落子事件进入现有棋盘和回合流程。
- 实体表示运行时游戏对象。`Chess` 是带 Unity `GameObject` 的棋子实体；`Player` 是回合归属实体，并通过组件保存对局信息。
- 事件连接 UI、系统和实体。UI 发出 `OnAddChessToBoard`、`OnSaveDuelScene` 等系统事件；系统发出 `OnAfterAddChessToBoard` 等领域结果事件。
- `DuelFSM` 表示本地回合生命周期，回合开始、输入、超时和结束由状态机管理，而不是由 UI 直接切换。
- `Assets/Config/DataJson` 是当前棋盘、场景、UI 页面、预制体和 TMP sprite 的数据来源。
- 资源加载通过 `ResourceManager` 和配置 id 抽象；编辑器环境使用 AssetDatabase，非编辑器环境使用 AssetBundle。
- 存档通过 `SavableObj`、`SavableField` 和可保存集合持久化场景与用户状态；对局棋盘恢复权威单独落在 KataGo analysis JSON 记录文件中，读档时由 `moves` 回放重建运行时棋盘缓存。
- KataGo 接入作为 Windows Unity Editor 和 Windows PC 包共用的本地子进程适配器存在：Unity 侧负责启动 `katago analysis`、通过 Win32 pipe 交换 stdin/stdout JSON、解析第一版形势按钮所需的 `ownership` 和电脑对局所需的 `moveInfos`，并把启动失败、超时、缺少模型或配置文件等情况转成可诊断状态。Windows 下优先尝试 OpenCL 引擎，初始化或 smoke test 失败时在适配器内部 fallback 到 Eigen AVX2 CPU 引擎，上层系统不感知具体引擎类型。形势展示由 UI 发事件、系统请求分析、棋盘表现层绘制 overlay；电脑对局由 `DuelAiSystem` 读取分析结果再发出领域落子事件，不让 KataGo 适配器直接修改棋盘规则状态。
- 游戏侧手顺格式应直接使用 KataGo 标准 `moves` 数组项，不再维护额外字符串棋谱格式或转换层。合法落子记录标准点位，虚手记录 `pass`；KataGo analysis JSON 生成优先输出 `moves`，是形势按钮、记录文件和后续复盘分析的统一路径；当前盘面 `initialStones` 快照入口只作为调试或无手顺场景使用。

## Coordinate Contract

**坐标契约**

- 本地棋盘逻辑坐标以 KataGo 布局为权威：`RectCoordinates.x` 从左到右递增，`RectCoordinates.z` 从棋盘上边向下递增，棋盘缓存索引和 KataGo `ownership` 都使用 `z * boardSize + x` 行序。Unity 本地空间中的显示位置由棋盘表现层从该逻辑坐标计算，不在 KataGo JSON 边界维护第二套转换语义。

## Key Tradeoffs

**关键取舍**

- 自定义 PlayerLoop 桥接让核心逻辑集中在模块层，但维护者需要理解非 Mono 系统也会逐帧更新。
- UI 通过事件与系统通信，避免页面直接修改棋盘内部状态，但事件定义会成为玩法合同的一部分。
- 棋盘运行状态使用字典缓存，便于规则校验和实体同步；持久化恢复以 KataGo 标准记录文件为权威，避免场景存档和棋谱出现两份棋盘来源。
- 当前落子校验会先修改缓存棋盘状态，再在非法时回滚。这让规则处理贴近棋盘状态，但联机复用时需要把领域命令校验入口进一步稳定下来。
- 电脑对局复用本地双玩家、FSM 和落子事件，能降低对现有本地对局基线的影响；代价是 AI 可用性取决于本地 KataGo 子进程、模型和难度配置，后续客户端打包仍需要单独处理资源分发和平台支持。
- 当前 FSM 适合本地热座对局。联机对局需要明确权威方和同步边界，不能让两个客户端各自自由认定最终棋盘。
- 数据驱动棋盘尺寸可以避免为不同棋盘复制场景，但规则与 UI 必须持续使用一致的配置 id。
- KataGo 本地子进程可以避免形势判断依赖网络服务，但会引入平台二进制、模型文件、首次初始化耗时和资源分发问题；这些问题应在编辑器验证跑通后再进入客户端打包决策。
- 记录文件采用可直接提交给 KataGo analysis engine 的 JSON 结构，会把存档恢复和 ownership 请求骨架耦合到同一份标准格式；代价是读档必须保证记录文件与场景存档同时存在并且棋盘尺寸一致。

## Guardrails

## 2026-05-20 Runtime Asset Addendum

- Code-loaded assets that must ship in player builds but may not be referenced by scenes or prefabs must be declared in `ConfigExporter/xlsx/runtime_asset.xlsx`.
- `AssetBundleGenerator.PackRuntimeAssetTable` consumes the exported `runtime_asset.json`, validates asset path and type, and applies the configured AssetBundle label before bundle build.
- Runtime code should load declared assets through generated config data and `ResourceManager`; do not add new runtime `Shader.Find` or ad hoc string asset lookups for shipped assets.
- Assembly boundaries remain respected: `XNClient.ChessBoard` owns board rendering primitives, while `ChessBoardSystem` in the main game assembly resolves table/config/global resources and injects materials into `RectGrid`.

**架构护栏**

- 不要把权威玩法状态修改直接写进 UI 页面类；UI 应该发出命令或事件，并展示状态。
- 不要绕过 `ChessBoardSystem` 的落子校验来实现联机。现有合法落子路径必须继续作为本地基线，或被明确提取为共享规则服务。
- 不要让网络代码直接耦合 Unity 预制体实例化；网络命令应描述领域动作，而不是描述预制体操作。
- 除非在线架构明确选择并记录客户端锁步，否则不要让远端客户端独立决定最终棋盘状态。
- 当前原型阶段允许“请求数子”和双方连续虚手复用 KataGo `ownership` 作为临时结算口径，以和形势展示保持一致；这不是长期架构终点。后续补齐死子确认后，正式数子仍应回到本地规则和明确的计分流程。第一版也不要把 `rootInfo.scoreLead`、胜率或最佳选点纳入形势按钮或终局产品输出。
- 不要让 KataGo 适配器直接修改 `SceneComponentChessBoard` 或落子状态；它只能读取局面快照并返回分析结果供表现层或调试层展示。
- 电脑对局不能绕过本地落子规则。KataGo 返回的候选点必须先通过本地规则入口校验，再由系统发出正常 `OnAddChessToBoard` 事件。
- 不要新增非 KataGo 标准的内部棋谱格式；正常对局、保存、读档和 ownership 请求都应围绕同一个 `moves` 表达。
- 不要把 `Library/`、`Temp/`、`Logs/`、IDE 生成文件、导入包内部文件或构建输出当成架构权威。
- 在 [ROADMAP.md](ROADMAP.md) 未移动阶段前，不要把当前本地对局范围扩展到匹配、重连、观战或完整线上终局裁定；本地数子应先保持为可回归的原型规则。
