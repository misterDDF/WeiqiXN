# WeiqiXN UnityProject 路线图

<!-- governance-profile:start -->

## Current Stage

**当前阶段**

- 阶段 1：本地对局基础与文档基线。
- 当前阶段的重点是收口本地对局已有能力，补齐进入联机阶段前必须明确的规则缺口，并保持文档与实际代码一致。

## Active Goals

- 2026-05-25: Windows native KataGo bridge now follows the same OpenCL-first, CPU-fallback direction as the exe backend. Unity runtime fallback logging is in place; the remaining OpenCL bridge deliverable is a successful `native-opencl/katago_bridge.dll` build after installing or pointing CMake at an OpenCL SDK (`CL/cl.h` and `OpenCL.lib`).
- 2026-05-26: Android native KataGo has moved to the same OpenCL-first, eigen-fallback direction at the Unity candidate layer. Android now expects `libkatago_bridge_opencl.so`, `libkatago_bridge_eigen.so`, and a merged `uses-native-library libOpenCL.so` manifest declaration; OpenCL runtime resolution should use the device system public `libOpenCL.so` rather than a bundled same-named loader. OpenCL runtime success still depends on device vendor OpenCL availability and falls back to eigen when startup or smoke tests fail.

**活跃目标**

- 当前阶段的架构优化按 [modules/12-architecture-iteration-plan.md](modules/12-architecture-iteration-plan.md) 推进；该表只承载执行明细，阶段目标和范围仍以本文为准。
- 当前架构优化主线只收敛本地对局、AI、UI、结算、手顺和存档边界，为后续联机开发降低混乱度；本阶段不接网络 SDK、不实现传输层、房间、匹配或重连。
- 联机方案的默认方向是 host 权威、单一 server core、客户端只发命令；第一版 server core 可以嵌在 host 进程里，后续若有必要再拆分为独立进程，但命令合同和快照合同必须保持不变。接入 OGS 前，先把 Local/Computer 与 LAN host 的本进程 host 权威路径收敛，再把 OGS 作为外部 server 权威适配到同一提交和表现框架。
- 在 Windows Unity Editor 和 Windows PC 包中继续验证本地 KataGo ownership 链路：通过 `game-config.json` 选择 `exe` 或 `native` 后端，发送当前对局 JSON，读取 `ownership`、失败状态，并验证棋盘 overlay 表现。
- 将 AI 控制区域与当前结算口径保持一致：第一版形势按钮已接入 `ownership` 请求、棋盘黑白小方块 overlay 和按钮上方的双方目数面板；请求数子和双方连续虚手也复用 KataGo `ownership` 统计目数，不展示胜率、目差或最佳选点。
- 为本地对局建立可重复的手动验证流程。
- 非法落子反馈采用合法预览口径：不能落的位置不显示预览棋子，不额外弹出无法落子提示。
- 明确本地终局流程的剩余规则：当前已有虚手、KataGo ownership 数子、认输和基础终局结果 UI，仍需补死子确认和线上裁定模型。
- 做出联机架构决策：传输或框架选择、权威模型、房间或会话模型、落子协议、重连策略、持久化预期。当前已收敛为 host 权威、单一 server core、客户端命令驱动的方案；最小房间、正常落子、虚手、认输、确认式数子、确认式悔棋、主动离开、心跳等待重连、棋盘版本、落子后快照纠偏、恢复后补发权威快照、host 下发输入权、host 开局座位配置和 host 权威计时已进入原型。下一步先继续结构迭代，收敛 Local/Computer 与 LAN host 的 host 权威核心，再补齐完整终局恢复边界和进入 OGS 连接测试。
- 稳定对局命令入口：正常落子已通过 `OnSubmitDuelMove` 收敛到 `DuelAuthoritySystem`，虚手、数子、悔棋和认输也已接入同一提交边界；页面预览、点击提交、动作按钮和 LAN 提交前置检查已通过 `DuelInputAuthority` 共用本端人类输入权限口径。本地/电脑对局和 LAN 对局共用同一提交口径，LAN 输入权限来自 host 下发的 `InputAuthority`；本地/电脑正常落子与 LAN host 接受正常落子已共用本进程 host 权威落子入口，虚手和认输已共用 `DuelSystem` 的 host 回合命令校验与状态应用入口，数子和悔棋的 LAN 第一阶段请求校验已复用 host 回合状态与版本校验入口。

## Progress

### 2026-06-02 OGS Login Preparation Addendum

- Added the first OGS connection foundation: `OgsConnectionService` is registered as a global module and owns OAuth2 PKCE authorization URL creation, authorization-code token exchange, refresh-token exchange, logout, session persistence, and minimum authenticated REST probes for current user and realtime UI config.
- Added an Editor-only OGS smoke menu for setting a client id/redirect URI/scope/authorization code/websocket URL/game id, generating an authorization URL, running browser callback login, running saved-code login, refreshing the current user, sending a realtime authentication smoke with the OGS UI-config JWT, and connecting to a configured game id for read-only `gamedata` summary logging. This is a connection smoke path only; it does not add OGS game UI or change duel submission behavior.
- Added the first in-game OGS account card on `UserInfoPopup`: the OGS login button uses the same browser callback login service, refreshes the OGS session profile, displays OGS account fields through a dedicated card state, and keeps the authenticated OGS username separate from the local display name. The current runtime callback is still desktop/Editor localhost based; mobile deep-link login remains a follow-up. Recent-game and OGS friend lists are now planned as separate surfaces; the popup currently keeps only entry buttons and summaries.
- Editor smoke verification has passed with a real OGS OAuth application: browser callback login, `/api/v1/me/` current-user probe, OGS UI config `user_jwt` retrieval, and websocket realtime authentication all succeeded.
- User-confirmed Editor smoke verification has also passed for read-only OGS game-state connection: a configured game id returned `gamedata` through the authenticated websocket path.
- The first OGS 9x9 bot-game creation path is in place: default OAuth scope is `read write`, `OgsConnectionService` can read active bots from realtime, create a default 9x9 Japanese-rules bot challenge through `players/{botId}/challenge`, send `game/connect`, and wait for server `gamedata`; the Editor smoke menu and main menu OGS button both use this service path.
- `MainMenuPage` now hides the OGS game button until the saved OGS session includes `write` scope. Clicking it starts the default unranked OGS 9x9 bot challenge flow and enters the project `OgsDuelScene` only after server `gamedata` is received, avoiding an empty scene when OGS returns a challenge id that has not become a playable game.
- `OgsDuelScene` reuses the Unity `Duel` scene asset and `DuelPage`, but uses `OgsDuelSystem` as a server-authoritative adapter. The client submits move/pass requests to OGS realtime and applies board state only from OGS accepted `gamedata` / `game/{id}/move` messages.
- OGS duel UI keeps pass enabled only while the local OGS seat has input authority. Resign uses OGS realtime `game/resign`; non-bot takeback uses OGS realtime undo request/accept/cancel, and peer takeback requests in non-bot games must prompt the local user before responding. OGS bot games disable the takeback entry and ignore `undo_requested` messages without surfacing a peer-confirm popup. Shape/ownership display is available through the existing local ownership overlay, while AI move analysis/recommendation remains disabled to avoid cheating. Local score request is disabled until an OGS-backed scoring confirmation flow is implemented. OGS scoring after consecutive passes should be driven by server stone-removal/result state and reused score-result confirmation UI, not by the local/LAN request-score button.
- `ogs_config` now carries OGS runtime connection values in the same table-driven style as `lan_room_config`; the source table is maintained under `ConfigExporter/xlsx/` and generated JSON/C# outputs are consumed by Unity.
- Local, computer, and LAN duel authority paths remain unchanged. LAN/local continue to use the existing host-authority path, while OGS remains planned as an external server-authority adapter.

### 2026-05-25 KataGo Native Bridge Addendum

- 根目录 `game-config.json` 已成为 KataGo 后端选择入口；Windows Editor/Player 当前默认使用 `native` DLL bridge，可切换为 `exe` 回到旧子进程路径，Android/iOS 先预留 native 方向。
- Windows `native-eigen` bridge 已能编译为 `katago_bridge.dll`，运行资源放在 `KataGo/engines/win-x64/native-eigen/`，Unity 侧通过 P/Invoke 调用同一套 analysis JSON 合同。
- Android native bridge 已拆成 `katago_bridge_opencl` 和 `katago_bridge_eigen` 两个固定 P/Invoke 入口；Android 启动候选按 OpenCL 优先、eigen fallback 排列，OpenCL 缺失、加载失败、后端不匹配或 smoke test 失败时回退到 eigen。
- Windows PC 打包入口会读取 `game-config.json`，按 `windowsPlayer` 校验 exe 或 native 运行资源，并在构建后复制 `game-config.json` 和对应 KataGo runtime；native 包只带 `native-eigen/` 与模型，不带 `katago.exe`。
- 当前阶段先收口 Windows DLL bridge；Android `.so` 方案在 DLL 路径通过 Unity 运行验证后继续推进，不作为本轮完成条件。

### 2026-05-21 Architecture Iteration Closeout

- P0-P2 架构迭代主线已完成阶段收口：落子规则结果模型、手顺访问、ownership 查询/数子结果、保存结果反馈、AI 分析/预算/候选选择拆分、`DuelPage` 输入/展示拆分和高频日志收敛均已落地。
- 当前主线已进入局域网联机原型：已有 UDP 房间发现、TCP 加入握手、准备/开局、正常落子命令、统一对局命令提交入口、棋盘版本、落子后快照纠偏、host 下发输入权、host 权威计时、虚手、认输、确认式数子、确认式悔棋、`LeaveRoom` 主动离开、`Heartbeat` 心跳检测和对局中等待重连恢复；仍没有新增第三方网络 SDK、匹配或完整线上裁定实现。
- P2.5 复盘主线已从 UI-only 文本棋盘切到独立 `ReplayScene`：最近对局条目进入复盘专用场景，复用对局棋盘资源、棋盘系统和落子规则回放，`ReplayPage` 只保留控制层；试下模式已支持从当前主线局面进入、连续试下落子、退出后恢复主线且不写回归档。复盘 AI 推荐点展示、点击推荐点展开缓存 `pv`、loading 阶段生成胜率/目差折线图种子采样并在 HUD 后台补齐，以及图表拖动预览并跳转主线手数已落地。后续仍需补齐 AI 推荐列表和复盘验证清单。
- 下一步如果继续沿计划推进，应继续收口 P3 联机原型：在 host 权威、单一 server core 的前提下，补齐完整终局恢复和手动双端回归验证，重点复测 Android 切后台后恢复、热点发现和等待重连退出路径。
- P2 收口验证已通过 Unity Editor 脚本重编译和 Console 编译错误检查；自动化玩法回归仍未补齐，后续阶段仍需保留本地热座、电脑对局、ownership 数子、保存和悔棋的手动回归清单。

### 2026-05-19 Current Addendum

- KataGo 接入目标已覆盖 Windows Unity Editor 和 Windows PC 包；两者统一使用仓库根目录 `KataGo/` 作为运行资源源目录，PC 构建成功后由构建脚本按后端复制到包体根目录 `<BuildRoot>/KataGo/`，避免 Unity 导入 KataGo `.dll`。
- 最小闭环是：本地配置 KataGo 后端、模型和 analysis 配置；Unity 通过 exe 子进程或 native bridge 启动 analysis engine；用当前或固定测试棋局发起 JSON 请求；解析 `ownership`；通过对局页“形式”按钮在棋盘上呈现 ownership overlay，并在日志中呈现成功、超时、启动失败和缺少资源文件等状态。Windows 构建入口会按 `game-config.json` 校验 exe 或 native 运行资源。
- 当前已接入 Play 模式和 Windows PC 包启动 smoke test：启动 Loading 阶段会按 `game-config.json` 选择后端。exe 后端优先使用 `opencl` 引擎，后台加载模型并依次验证 9 路、13 路、19 路 `ownershipLength` 日志；OpenCL 缺失、启动失败或任一 smoke test 失败时会自动 fallback 到 `eigenavx2` 引擎。native 后端使用 `native-eigen/katago_bridge.dll` 并验证同一组 smoke query。启动时会检查游戏根目录写权限；不可写时会通过 `ConfirmPopup` 提示模式提示、跳过 OpenCL，并使用 no-write analysis 配置关闭 KataGo 文件写入。
- 当前已新增 KataGo 标准棋谱链路和第一版形势按钮链路：合法落子直接维护 KataGo `moves`，让子棋的预置黑子输出到 KataGo `initialStones`，保存对局时生成完整手顺 analysis 请求骨架；`DuelPage` 右下角“形式”按钮会按当前棋盘快照请求 `ownership`，绘制棋盘 overlay，并在按钮上方显示黑方目数和白方贴目后目数；ownership 数子复用同一当前盘面快照口径。读档/继续对局暂不作为当前正式功能。
- 当前已接入 KataGo ownership 数子、虚手终局、认输和基础终局结果 UI：设置面板请求数子时会先显示“数子中...”确认弹窗并禁用确认按钮，结果返回后更新同一弹窗；形势按钮旁的虚手按钮支持双方连续虚手后直接按 ownership 结算结束，设置面板认输按钮通过通用二次确认进入终局，右侧中部结算面板显示胜方和结束原因；虚手写入 KataGo 标准 `moves` 的 `pass` 项。
- 本地棋盘状态数子算法已从当前结算路径移除；当前阶段“请求数子”和双方连续虚手只依赖 KataGo `ownership`，没有新落子或虚手时复用 ownership 缓存。死子确认和完整线上裁定模型仍未实现，后续需要重新明确正式规则口径。
- Windows PC 离线包已纳入当前 KataGo 验证范围；移动端 `.so`、WebGL、跨平台发布、外部模型分发和完整数子 UI 仍不是当前完成条件。Windows OpenCL/Eigen fallback 仍服务于 exe 后端，native 后端先验证 Eigen/no-write bridge。

### 2026-05-15 Current Addendum

- Local game time control is now partly table-driven: hold-time, byoyomi-count, and byoyomi-period options are exported from Excel into config JSON and generated C# data types.
- Runtime logic supports hold-time countdown, byoyomi countdown, byoyomi count consumption, and timeout loss into `GameEnd`.
- Remaining UI work: the code and binder fields are prepared for the expanded setup popup, but `DuelSetupPopup.prefab` still needs Unity Editor/MCP-side creation and binding of the new time-control buttons.

**进度状态**

- 基线状态：Unity 项目结构、启动循环、主菜单场景、对局场景、棋盘尺寸选择、矩形棋盘生成、本地双人回合循环、30 秒回合计时、鼠标悬停落点 VFX、点击落子、提子校验、自杀拒绝、简单重复局面拒绝、保存触发和保存结果反馈已经存在。
- 架构迭代执行指针：本地规则、手顺/结算/保存、AI 大类、UI 大类和高频日志边界已经收敛；当前推进点是 P3 局域网联机原型。
- 本地打磨缺口：死子确认、线上裁定模型、比调试 guid 更友好的当前玩家显示、自动化玩法测试。读档/继续对局暂不作为正式功能。
- 联机状态：当前没有第三方网络依赖；已有基于 UDP/TCP 标准库的局域网房间、最小同步和主动离开释放会话原型。
- 阶段 2 入口条件：本地对局基线可以被手动验证，联机原型所依赖的规则缺口已经被明确接受或解决。
- 阶段 2 目标：联机基础。第一交付物是基于 host 权威、单一 server core 的局域网房间、最小落子命令协议、棋盘版本和快照纠偏，不是完整匹配产品。
- 阶段 2 风险：本地与网络路径重复实现规则、客户端权威导致作弊面、重连后存档或快照不一致、确认式动作在断线时缺少恢复策略。

## Explicit Non-goals

**明确非目标**

- 在落子同步模型确定前，不实现完整匹配系统。
- 当前阶段不加入观战、排行榜、复盘分享或账号系统。
- 不把联机当成单纯 UI 功能；联机必须有明确的权威模型和同步模型。
- 除非阶段目标明确要求替换，否则增加联机时不替换本地对局路径。
- 当前 KataGo 目标不接网络 AI 服务，不要求移动端、WebGL 或跨平台发布跑通；Android/iOS native 后端是 Windows DLL bridge 走通后的后续方向。当前本地结算临时复用 `ownership`，但死子确认和完整线上裁定仍不是本阶段完成条件。

### 2026-05-22 Replay Planning Addendum

- P2.5 复盘与历史归档将先完成本地闭环：最近对局列表、单局单归档、短局不过档、手顺浏览、折线图跳转、试下模式和 AI 推荐展示先做完，再继续推进 P3 联机原型。
- 该阶段继续沿用本地回归基线，不接入网络 SDK、传输层、房间、匹配或重连。
- 复盘只作为独立历史入口，不回写成新的本地对局规则主线。

<!-- governance-profile:end -->
