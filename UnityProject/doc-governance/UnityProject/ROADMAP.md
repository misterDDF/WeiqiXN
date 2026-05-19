# WeiqiXN UnityProject 路线图

<!-- governance-profile:start -->

## Current Stage

**当前阶段**

- 阶段 1：本地对局基础与文档基线。
- 当前阶段的重点是收口本地对局已有能力，补齐进入联机阶段前必须明确的规则缺口，并保持文档与实际代码一致。

## Active Goals

**活跃目标**

- 在 Unity 编辑器模式下先跑通本地 KataGo ownership 链路：启动本地 `katago analysis` 子进程，向 stdin 发送测试棋局 JSON，并读取 `ownership` 和失败状态。
- 将 AI 控制区域与正式数子拆开：第一版形势按钮只使用 KataGo `ownership` 绘制双方控制范围，不展示胜率、目差或最佳选点，也不作为正式终局数子的权威结果。
- 为本地对局建立可重复的手动验证流程。
- 为非法落子增加用户可见反馈。
- 明确本地终局流程的最小规则：停一手、认输、数目，或先接受一个更小的原型规则集。
- 做出联机架构决策：传输或框架选择、权威模型、房间或会话模型、落子协议、重连策略、持久化预期。
- 稳定或提取落子校验入口，让本地对局和联机对局共用同一套规则路径。

## Progress

### 2026-05-19 Current Addendum

- KataGo 接入目标先收敛为编辑器模式验证，不要求立即进入正式客户端打包流程。
- 编辑器验证的最小闭环是：本地配置 KataGo 可执行文件、模型和 analysis 配置；Unity 通过子进程启动 analysis engine；用当前或固定测试棋局发起 JSON 请求；解析 `ownership`；在日志或临时调试 UI 中呈现成功、超时、启动失败和缺少资源文件等状态。
- 当前已接入 Play 模式启动 smoke test：优先使用 `eigenavx2` 引擎，后台加载模型并验证 `ownershipLength` 日志；OpenCL 优先与 fallback 策略仍是后续打包前事项。
- 当前已新增 KataGo 局面 JSON 生成基础：合法落子会保存 KataGo 可读手顺；生成器同时提供完整手顺 `moves` JSON 和当前盘面 `initialStones` JSON，第一版形势按钮优先使用完整手顺入口。
- 正式数子仍应由本地规则算法和死子确认流程承担；KataGo 在当前阶段只提供 AI 控制区域，不改变棋规权威。
- 目标客户端离线打包、OpenCL/Eigen fallback、模型资源分发、常驻形势按钮和完整数子 UI 暂不作为本次编辑器验证的完成条件。

### 2026-05-15 Current Addendum

- Local game time control is now partly table-driven: hold-time, byoyomi-count, and byoyomi-period options are exported from Excel into config JSON and generated C# data types.
- Runtime logic supports hold-time countdown, byoyomi countdown, byoyomi count consumption, and timeout loss into `GameEnd`.
- Remaining UI work: the code and binder fields are prepared for the expanded setup popup, but `DuelSetupPopup.prefab` still needs Unity Editor/MCP-side creation and binding of the new time-control buttons.

**进度状态**

- 基线状态：Unity 项目结构、启动循环、主菜单场景、对局场景、棋盘尺寸选择、矩形棋盘生成、本地双人回合循环、30 秒回合计时、鼠标悬停落点 VFX、点击落子、提子校验、自杀拒绝、简单重复局面拒绝、保存触发和读取恢复钩子已经存在。
- 本地打磨缺口：非法落子提示、停一手、认输、计分或终局规则、比调试 guid 更友好的当前玩家显示、自动化玩法测试、明确的读档入口。
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
- 当前 KataGo 目标不接网络 AI 服务，不要求移动端、WebGL 或跨平台发布跑通，也不把 AI 估值写成正式数子结论。

<!-- governance-profile:end -->
