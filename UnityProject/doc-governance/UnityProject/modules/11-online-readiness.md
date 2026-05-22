# 联机功能准备度

## 当前状态

联机功能已开始进入最小房间和最小对局命令阶段。当前已有 `LanRoomService` 作为局域网房间服务入口，支持 host 侧 TCP 监听、UDP 房间广播、client 侧 UDP 搜索、TCP 加入握手、最小准备状态交换、host 开局配置、进入带 LAN 标记的 `DuelScene`，以及 `SubmitMove` / `MoveAccepted` / `MoveRejected` / `BoardSnapshot` 的最小命令搬运；正常落子、虚手、数子、悔棋和认输已通过 `DuelAuthoritySystem` 收敛为统一提交入口。页面预览、点击提交和动作按钮权限通过 `DuelInputAuthority` 读取，LAN 下该权限来自 host 广播的 `InputAuthority`。host 侧 `LanDuelSystem` 通过现有规则入口进行权威落子校验，并在合法落子后广播权威棋盘快照；当前 `BoardSnapshot` 携带棋盘尺寸、下一手玩家、最后一步、棋子列表和 host 权威 KataGo 标准手顺，client 应用快照时同步棋盘与 `kataGoMoves` 并清除旧形势缓存。虚手通过 `SubmitPass` / `PassAccepted` 同步；认输通过 `SubmitResign` / `ResignAccepted` 同步；数子和悔棋通过确认请求/确认回复协议让对端弹窗确认。LAN 数子在对端同意请求后只广播候选 `ScoreResult`，双方都通过 `ScoreResultConfirmResponse` 接受后，host 才广播 `ScoreResultAccepted` 进入终局；拒绝请求、拒绝结果、请求失效或计算失败都会通过带原因的 `ScoreFailed` 恢复对局。悔棋确认阶段由 host 按 `actionId` 暂存原始请求，确认回复使用原始 `boardVersion` / `removeCount`；拒绝消息带请求方座位，避免非发起方误报失败，悔棋接受后会补发权威快照。host 也负责广播当前行棋方 `TimeState` 并在超时后广播 `PlayerTimeout`，client 只使用 host 计时状态刷新显示。尚未实现正式传输抽象、座位选择、匹配系统、完整同步系统或断线恢复系统。架构方向已收敛为 host 权威、单一 server core、客户端只发命令；第一版 server core 允许嵌在 host 进程中运行，后续再决定是否拆成独立进程。

## 已具备的基础

- 本地对局已经有可用的状态流：玩家、当前回合、棋盘状态、落子事件、落子后推进。
- 棋盘状态已经以数据结构保存，而不是只存在于 Unity GameObject 上。
- 落子合法性集中在 `ChessBoardSystem`，具备提取为共享规则服务的基础。
- 事件系统可以作为 UI、本地逻辑和未来网络层之间的内部通信机制。
- 存档系统可为快照、重连缓存或本地复盘提供参考。
- UI 框架和配置系统可以继续扩展房间、匹配、邀请、断线提示等页面。
- `LanRoomService` 已提供最小局域网房间发现、连接、准备状态、开局配置和落子命令搬运骨架；`LanRoomPopup` 创建房间前会复用 `DuelSetupPopup` 设置棋盘和时间配置，并在开局状态到达后进入带 LAN 标记的 `DuelScene`，UI 不直接持有 socket。
- 正常落子提交已收敛到 `DuelAuthoritySystem`：本地/电脑对局直接请求本进程权威落子，LAN 对局按本端座位提交到 `LanRoomService`，host 本地玩家也不绕过命令队列直接改盘。
- 本端人类输入权限已收敛到 `DuelInputAuthority`：页面预览、点击提交和 LAN 提交前置检查使用同一份权限状态；LAN 原型由 host 广播 `InputAuthority` 更新 `SceneComponentDuel.localInputPlayerFlag`。
- 虚手提交已接入 host 权威路径：本端通过 `OnSubmitDuelPass` 提交，LAN host 校验当前行棋方和棋盘版本后广播 `PassAccepted`。
- 数子和悔棋已接入确认式 host 权威路径：发起方提交到 host，host 转发确认请求给对端。数子在对端同意请求后由 host 计算候选结果，随后等待双方确认结果，双方都接受才广播 `ScoreResultAccepted` 结算；任一方拒绝请求或结果、请求失效、KataGo 计算失败都会广播带原因的 `ScoreFailed` 并继续对局。悔棋请求在 host 侧按 `actionId` 保存原始版本和回退手数，拒绝消息会带请求方座位。
- 认输提交已接入 host 权威路径：本端通过 `OnSubmitDuelResign` 提交，LAN host 校验当前行棋方后广播 `ResignAccepted`，双方通过 `OnApplyLanDuelResign` 进入同一认输终局。
- `LanDuelSystem` 已接入 `DuelScene`，host 消费带棋盘版本的 `SubmitMove` 并通过现有规则入口广播 `MoveAccepted` / `MoveRejected`，client 只应用 host 接受的落子。
- 合法落子会递增 `SceneComponentDuel.lanBoardVersion`，host 随后广播包含棋盘尺寸、下一手玩家、最后一步、棋子列表和 host 权威 KataGo 标准手顺的 `BoardSnapshot`，client 用快照纠正本地棋盘与 `kataGoMoves`。
- LAN 计时已按 host 权威收敛：host 广播 `TimeState`，client 不自行扣时或裁定超时；host 广播 `PlayerTimeout` 后 client 进入同样的超时终局。
- `lan_room_config` 已承载局域网房间端口、连接超时、人数上限、广播间隔和缓冲区大小；协议消息名由 `LanRoomProtocol` 枚举按命名约定生成，并通过 `OnXxx` 接收函数注册到协议回调表。

## 主要缺口

- 没有正式网络协议和传输抽象；当前房间阶段直接使用 UDP 广播和 TCP 握手骨架。
- 还没有完整服务端或可独立运行的房主权威会话核心；当前只完成房间 host 监听、加入握手、准备状态、开局命令、正常落子、虚手、计时、超时、认输、确认式数子和确认式悔棋的内嵌 host 权威原型。
- 移动命令协议已有最小正常落子命令、版本确认、输入权下发、虚手、认输、数子确认和悔棋确认，尚未覆盖断线重连后的完整恢复。
- 棋盘状态快照只覆盖落子后的棋盘纠偏，计时只覆盖在线显示校准和超时终局通知，尚未覆盖断线重连、房间完整状态或终局恢复。
- 没有玩家身份、座位选择和完整房间生命周期。
- 没有断线、重连和完整终局恢复在网络环境下的定义；当前数子仍依赖 KataGo ownership，悔棋不回滚历史计时快照。
- 没有防作弊边界。

## 推荐联机路线

1. 先定义最小在线对局目标：当前阶段以好友房或房间码为主，不把匹配当成第一交付物。
2. 保持 host 权威和单一 server core：host 端也只通过同一套命令入口推进会话，不能因主机身份绕过协议；正常落子已按该方向收敛，后续动作继续沿用同一合同。
3. 提取规则服务：本地和网络都调用同一个落子校验入口。
4. 定义最小协议：`JoinRoom`、`Ready`、`StartConfig`、`InputAuthority`、`SubmitMove`、`MoveAccepted`、`MoveRejected`、`BoardSnapshot`、`SubmitPass`、`PassAccepted`、`SubmitResign`、`ResignAccepted`、`SubmitScore`、`ScoreConfirmRequest`、`ScoreConfirmResponse`、`ScoreResult`、`ScoreResultConfirmResponse`、`ScoreResultAccepted`、`ScoreFailed`、`SubmitTakeBack`、`TakeBackConfirmRequest`、`TakeBackConfirmResponse`、`TakeBackAccepted`、`TakeBackRejected`、`TimeState`、`PlayerTimeout`、`LeaveRoom`、`Heartbeat`。
5. 明确快照结构：棋盘尺寸、当前玩家、座位映射、棋子列表或位置字典、KataGo 标准手顺、回合计时、最后一步、局面版本号、房间状态。
6. 再接入具体网络 SDK 或自建传输层，传输层只负责搬运命令和快照，不负责规则判断。

## 阶段验收标准

- 本地对局仍可运行。
- 在线对局能在两个客户端之间同步合法落子，且双方都经由同一权威会话核心处理。
- 非法落子只在权威端被拒绝，并能回传明确原因和对应版本号。
- 双端棋盘状态有版本号或 hash 校验。
- 断线后至少能安全退出或重新同步快照。
