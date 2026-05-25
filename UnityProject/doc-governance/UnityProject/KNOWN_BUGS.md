# 已知 Bug

## 用途

本文档记录当前已知且尚未修复的 bug，方便后续会话继续跟踪。

- 只记录可复现或已经明确观察到的当前 bug。
- bug 修复并验证通过后，移除对应条目。
- 如果修复改变了当前行为，需要在同一轮修改中同步更新 `SPECIFICATION.md` 和受影响的模块文档。

## 未移除 Bug

### KataGo native OpenCL DLL 模式疑似落到 CPU/fallback 路径

- 状态：已修复实现，待运行复测后移除
- 记录日期：2026-05-25
- 涉及范围：KataGo Windows native 后端、OpenCL DLL 候选选择、DLL 加载、ownership 分析性能、启动调优流程
- 已观察行为：清理 OpenCL tuning 缓存后运行 `native` + `native-opencl` DLL 模式，启动调优步骤异常快，表现像没有真正执行 OpenCL 调优或已经落到 fallback；进入对局后 ownership 绘制缓慢。
- 对比现象：将 `game-config.json` 切回 `exe` 后，OpenCL exe 模式会重新执行正常的较慢调优；在 exe 模式 OpenCL 下 ownership 绘制速度明显更快。
- 初步判断：当前 `native` 模式可能没有实际使用 OpenCL DLL，或 Unity 进程中同名 `katago_bridge.dll` 的加载/候选切换导致配置显示与真实 native 后端不一致。
- 期望行为：`native-opencl` 候选必须按完整 DLL 路径加载对应 bridge，并在启动日志中明确输出候选名、实际 DLL 路径、配置路径、模型路径和 native bridge 编译后端；OpenCL 候选失败或被跳过时必须有可诊断日志，不能让界面或日志误判为 OpenCL 已可用。
- 移除条件：修复 native DLL 加载与诊断，完成 Unity 脚本编译，并通过清理 tuning 后运行 `native-opencl` 的启动日志和 ownership 性能复测确认真实使用 OpenCL 后移除此条记录。

### AI 对局猜先后 AI 执黑不会首手落子

- 状态：修复实现已落地，待运行复测后移除
- 记录日期：2026-05-22
- 涉及范围：AI 对局、猜先、对局场景初始化、对局 FSM 状态监听
- 已观察行为：当前 AI 对局模式选择猜先时，如果随机结果为 AI 执黑，进入对局后 AI 不会作为黑方首手自动落子。
- 可见表现：棋盘停在黑方回合，玩家没有本地输入权限，AI 也没有开始思考或提交落子。
- 期望行为：AI 对局中无论猜先结果为玩家执黑还是 AI 执黑，只要当前回合轮到 AI，AI 都应自动开始思考并通过统一落子提交入口行棋。
- 当前处理：定位到 `DuelSystem.Init()` 激活对局 FSM 时，`DuelAiSystem` 尚未注册状态变更事件；即使前移注册，FSM 首次进入输入状态事件发出时 `isActivated` 尚未置为 true，AI 回合判断仍会跳过。已将 AI 系统注册前移，并增加一次性初始回合检查，在 FSM 激活后如果当前已是 AI 的 `turnInput` 回合则立即启动 AI 行棋流程。
- 移除条件：修复 AI 首手触发时序，完成 Unity 脚本编译和最小可重复验证后移除此条记录。

### LAN 对局 13 路让 2 子后远端不显示新落子且会话读取失败

- 状态：修复实现已落地，待联机复测后移除
- 记录日期：2026-05-22
- 涉及范围：LAN 房间、联机对局场景切换、落子同步、TCP 会话读取
- 已观察行为：联机对局使用新增设置选择 13 路棋盘且让 2 子后，一端落子远端看不见新落子；同时 Console 出现 `Read LAN room session failed.`，错误信息为传输连接读取失败或连接方无响应。
- 可见表现：双端棋盘状态不同步，后续 LAN 消息可能中断。
- 期望行为：任一端提交合法落子后，应通过 LAN 协议同步到对端并在对端棋盘生成同一手棋；正常离开或断线应有可诊断但不过度误导的连接状态处理。
- 当前处理：定位到客户端连接握手后仍保留连接阶段 `ReceiveTimeout`，让子棋黑棋先摆子后白棋先手，若房主执黑则客户端等待自己操作期间可能因主机短时间无消息而误判读取超时并断开；已在握手通过后取消长期会话读取超时。
- 移除条件：修复 LAN 落子同步和相关连接读取失败原因，并完成 Unity 脚本编译及可重复的本地/联机验证后移除此条记录。

### 进入 LAN 对局时关闭 LoadingPage 报 Page not found

- 状态：修复实现已落地，待运行复测后移除
- 记录日期：2026-05-22
- 涉及范围：LAN 对局进入流程、LoadingPage 关闭流程、UI 页面栈
- 已观察行为：进入或运行联机对局时 Console 出现 `Page not found, close main page failed. #pageName: LoadingPage #contextType: Loading`。
- 可见表现：尝试关闭不存在或已关闭的 `LoadingPage`，可能与 LAN 双端进入场景时序有关。
- 期望行为：LoadingPage 关闭应具备幂等保护，或只在页面确实打开时关闭，不产生无效页面关闭 warning。
- 当前处理：定位到泛型 `UIManager.ClosePage<T>()` 关闭路径没有触发页面自身 `OnClose()`，会让 `LoadingPage.activePage` 保留过期引用；已让泛型关闭复用实例关闭路径，并在场景加载完成时使用无 warning 的尝试关闭。
- 移除条件：修复或收敛 LoadingPage 关闭时序，完成 Unity 脚本编译验证后移除此条记录。
