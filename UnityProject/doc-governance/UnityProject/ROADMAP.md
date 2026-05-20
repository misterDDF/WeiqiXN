# WeiqiXN UnityProject 路线图

<!-- governance-profile:start -->

## Current Stage

**当前阶段**

- 阶段 1：本地对局基础与文档基线。
- 当前阶段的重点是收口本地对局已有能力，补齐进入联机阶段前必须明确的规则缺口，并保持文档与实际代码一致。

## Active Goals

**活跃目标**

- 在 Windows Unity Editor 和 Windows PC 包中继续验证本地 KataGo ownership 链路：启动本地 `katago analysis` 子进程，向 stdin 发送当前对局 JSON，读取 `ownership`、失败状态，并验证棋盘 overlay 表现。
- 将 AI 控制区域与当前结算口径保持一致：第一版形势按钮已接入 `ownership` 请求、棋盘黑白小方块 overlay 和按钮上方的双方目数面板；请求数子和双方连续虚手也复用 KataGo `ownership` 统计目数，不展示胜率、目差或最佳选点。
- 为本地对局建立可重复的手动验证流程。
- 为非法落子增加用户可见反馈。
- 明确本地终局流程的剩余规则：当前已有虚手、KataGo ownership 数子、认输和基础终局结果 UI，仍需补死子确认和线上裁定模型。
- 做出联机架构决策：传输或框架选择、权威模型、房间或会话模型、落子协议、重连策略、持久化预期。
- 稳定或提取落子校验入口，让本地对局和联机对局共用同一套规则路径。

## Progress

### 2026-05-19 Current Addendum

- KataGo 接入目标已覆盖 Windows Unity Editor 和 Windows PC 包；两者统一使用 `Assets/StreamingAssets/KataGo/` 作为运行资源源目录，PC 构建后由 Unity 原样复制到 `<GameName>_Data/StreamingAssets/KataGo/`。
- 最小闭环是：本地配置 KataGo 可执行文件、模型和 analysis 配置；Unity 通过子进程启动 analysis engine；用当前或固定测试棋局发起 JSON 请求；解析 `ownership`；通过对局页“形式”按钮在棋盘上呈现 ownership overlay，并在日志中呈现成功、超时、启动失败和缺少资源文件等状态。Windows 构建入口会在打包前校验当前运行所需的 `eigenavx2` 引擎文件和模型。
- 当前已接入 Play 模式和 Windows PC 包启动 smoke test：优先使用 `eigenavx2` 引擎，后台加载模型并验证 `ownershipLength` 日志；OpenCL 优先与 fallback 策略仍是后续事项。
- 当前已新增 KataGo 标准棋谱链路和第一版形势按钮链路：合法落子直接维护 KataGo `moves`，保存对局时生成可直接作为 ownership analysis 请求骨架的记录 JSON，读档时通过记录文件回放恢复棋盘；`DuelPage` 右下角“形式”按钮会请求当前对局 `ownership`，绘制棋盘 overlay，并在按钮上方显示黑方目数和白方贴目后目数；当前盘面 `initialStones` 入口仅保留为调试或无手顺场景。
- 当前已接入 KataGo ownership 数子、虚手终局、认输和基础终局结果 UI：设置面板请求数子时会先显示“数子中...”确认弹窗并禁用确认按钮，结果返回后更新同一弹窗；形势按钮旁的虚手按钮支持双方连续虚手后直接按 ownership 结算结束，设置面板认输按钮通过通用二次确认进入终局，右侧中部结算面板显示胜方和结束原因；虚手写入 KataGo 标准 `moves` 的 `pass` 项。
- 本地棋盘状态数子算法已从当前结算路径移除；当前阶段“请求数子”和双方连续虚手只依赖 KataGo `ownership`，没有新落子或虚手时复用 ownership 缓存。死子确认和完整线上裁定模型仍未实现，后续需要重新明确正式规则口径。
- Windows PC 离线包已纳入当前 KataGo 验证范围；移动端、WebGL、跨平台发布、OpenCL/Eigen fallback、外部模型分发和完整数子 UI 仍不是当前完成条件。

### 2026-05-15 Current Addendum

- Local game time control is now partly table-driven: hold-time, byoyomi-count, and byoyomi-period options are exported from Excel into config JSON and generated C# data types.
- Runtime logic supports hold-time countdown, byoyomi countdown, byoyomi count consumption, and timeout loss into `GameEnd`.
- Remaining UI work: the code and binder fields are prepared for the expanded setup popup, but `DuelSetupPopup.prefab` still needs Unity Editor/MCP-side creation and binding of the new time-control buttons.

**进度状态**

- 基线状态：Unity 项目结构、启动循环、主菜单场景、对局场景、棋盘尺寸选择、矩形棋盘生成、本地双人回合循环、30 秒回合计时、鼠标悬停落点 VFX、点击落子、提子校验、自杀拒绝、简单重复局面拒绝、保存触发和读取恢复钩子已经存在。
- 本地打磨缺口：非法落子提示、死子确认、线上裁定模型、比调试 guid 更友好的当前玩家显示、自动化玩法测试、明确的读档入口。
- 联机状态：当前没有网络依赖，也没有项目网络层。
- 阶段 2 入口条件：本地对局基线可以被手动验证，联机原型所依赖的规则缺口已经被明确接受或解决。
- 阶段 2 目标：联机基础。第一交付物是架构决策和最小落子命令协议，不是完整匹配产品。
- 阶段 2 风险：本地与网络路径重复实现规则、客户端权威导致作弊面、重连后存档或快照不一致、超时/停一手/认输处理不清晰。

## Explicit Non-goals

**明确非目标**

- 在落子同步模型确定前，不实现完整匹配系统。
- 当前阶段不加入观战、排行榜、复盘分享或账号系统。
- 不把联机当成单纯 UI 功能；联机必须有明确的权威模型和同步模型。
- 除非阶段目标明确要求替换，否则增加联机时不替换本地对局路径。
- 当前 KataGo 目标不接网络 AI 服务，不要求移动端、WebGL 或跨平台发布跑通；当前本地结算临时复用 `ownership`，但死子确认和完整线上裁定仍不是本阶段完成条件。

<!-- governance-profile:end -->
