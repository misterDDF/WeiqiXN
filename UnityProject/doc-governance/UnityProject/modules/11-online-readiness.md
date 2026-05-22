# 联机功能准备度

## 当前状态

联机功能已开始进入最小房间和最小落子命令阶段。当前已有 `LanRoomService` 作为局域网房间服务入口，支持 host 侧 TCP 监听、UDP 房间广播、client 侧 UDP 搜索、TCP 加入握手、最小准备状态交换、host 开局配置、进入带 LAN 标记的 `DuelScene`，以及 `SubmitMove` / `MoveAccepted` / `MoveRejected` / `BoardSnapshot` 的最小命令搬运；host 侧 `LanDuelSystem` 通过现有规则入口进行权威落子校验，并在合法落子后广播权威棋盘快照。host 也负责广播当前行棋方 `TimeState` 并在超时后广播 `PlayerTimeout`，client 只使用 host 计时状态刷新显示。尚未实现正式传输抽象、座位选择、匹配系统、完整同步系统或断线恢复系统。架构方向已收敛为 host 权威、单一 server core、客户端只发命令；第一版 server core 允许嵌在 host 进程中运行，后续再决定是否拆成独立进程。

## 已具备的基础

- 本地对局已经有可用的状态流：玩家、当前回合、棋盘状态、落子事件、落子后推进。
- 棋盘状态已经以数据结构保存，而不是只存在于 Unity GameObject 上。
- 落子合法性集中在 `ChessBoardSystem`，具备提取为共享规则服务的基础。
- 事件系统可以作为 UI、本地逻辑和未来网络层之间的内部通信机制。
- 存档系统可为快照、重连缓存或本地复盘提供参考。
- UI 框架和配置系统可以继续扩展房间、匹配、邀请、断线提示等页面。
- `LanRoomService` 已提供最小局域网房间发现、连接、准备状态、开局配置和落子命令搬运骨架；`LanRoomPopup` 会在开局状态到达后进入带 LAN 标记的 `DuelScene`，UI 不直接持有 socket。
- `LanDuelSystem` 已接入 `DuelScene`，host 消费带棋盘版本的 `SubmitMove` 并通过现有规则入口广播 `MoveAccepted` / `MoveRejected`，client 只应用 host 接受的落子。
- 合法落子会递增 `SceneComponentDuel.lanBoardVersion`，host 随后广播包含棋盘尺寸、下一手玩家、最后一步和棋子列表的 `BoardSnapshot`，client 用快照纠正本地棋盘。
- LAN 计时已按 host 权威收敛：host 广播 `TimeState`，client 不自行扣时或裁定超时；host 广播 `PlayerTimeout` 后 client 进入同样的超时终局。
- `lan_room_config` 已承载局域网房间端口、连接超时、人数上限、广播间隔和缓冲区大小；协议消息名由 `LanRoomProtocol` 枚举按命名约定生成，并通过 `OnXxx` 接收函数注册到协议回调表。

## 主要缺口

- 没有正式网络协议和传输抽象；当前房间阶段直接使用 UDP 广播和 TCP 握手骨架。
- 没有完整服务端或房主权威会话核心；当前只完成房间 host 监听、加入握手、准备状态和开局命令骨架。
- 移动命令协议只有最小正常落子命令和版本确认，尚未覆盖虚手、认输或数子。
- 棋盘状态快照只覆盖落子后的棋盘纠偏，计时只覆盖在线显示校准和超时终局通知，尚未覆盖断线重连、房间完整状态或终局恢复。
- 没有玩家身份、座位选择和完整房间生命周期。
- 没有断线、重连、超时、投降、终局在网络环境下的定义。
- 没有防作弊边界。

## 推荐联机路线

1. 先定义最小在线对局目标：当前阶段以好友房或房间码为主，不把匹配当成第一交付物。
2. 保持 host 权威和单一 server core：host 端也只通过同一套命令入口推进会话，不能因主机身份绕过协议。
3. 提取规则服务：本地和网络都调用同一个落子校验入口。
4. 定义最小协议：`JoinRoom`、`Ready`、`StartConfig`、`SubmitMove`、`MoveAccepted`、`MoveRejected`、`BoardSnapshot`、`TimeState`、`PlayerTimeout`、`LeaveRoom`、`Heartbeat`。
5. 明确快照结构：棋盘尺寸、当前玩家、座位映射、棋子列表或位置字典、回合计时、最后一步、局面版本号、房间状态。
6. 再接入具体网络 SDK 或自建传输层，传输层只负责搬运命令和快照，不负责规则判断。

## 阶段验收标准

- 本地对局仍可运行。
- 在线对局能在两个客户端之间同步合法落子，且双方都经由同一权威会话核心处理。
- 非法落子只在权威端被拒绝，并能回传明确原因和对应版本号。
- 双端棋盘状态有版本号或 hash 校验。
- 断线后至少能安全退出或重新同步快照。
