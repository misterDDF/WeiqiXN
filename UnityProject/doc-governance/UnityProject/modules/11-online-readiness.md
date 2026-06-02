# 联机功能准备度

## 2026-06-02 OGS Connection Foundation

- `OgsConnectionService` now exists as a global service for OGS OAuth2 PKCE login preparation and minimum authenticated REST connectivity.
- The service can create an authorization URL, exchange an authorization code with a PKCE verifier, refresh a token, clear a local session, query the current OGS user through `/api/v1/me/`, and request OGS UI config for the realtime `user_jwt`. Tokens are persisted in the save root as an OGS session file and are not written to diagnostic logs.
- An Editor-only OGS smoke tool under the custom menu provides the connection test surface. It can generate an authorization URL, run a localhost callback login smoke, run a saved-code login smoke, refresh the current user, send a realtime authentication smoke using the OGS UI-config JWT, and connect to a configured game id for read-only `gamedata` summary logging. It requires an OGS OAuth application `client_id` registered outside the project and a redirect URI matching that registration.
- The Editor smoke path has been verified against OGS with a real OAuth application: browser login, current-user REST probe, UI-config JWT retrieval, and realtime websocket authentication all succeeded. This is now the baseline before OGS minimum game-state connection work.
- The read-only OGS game-state smoke has also been user-verified: a configured game id returned `gamedata` through the authenticated websocket path. This proves the first server-authoritative game-state intake path before any project board mutation or move submission.
- This foundation intentionally does not alter local/LAN duel behavior, does not add OGS duel UI, does not submit OGS moves, and does not route OGS moves through the local/LAN host core. OGS realtime game integration remains a later adapter that should treat OGS server state as authoritative and then apply accepted state into the project board/presentation layer.

## 当前状态

联机功能已开始进入最小房间和最小对局命令阶段。当前已有 `LanRoomService` 作为局域网房间服务入口，支持 host 侧 TCP 监听、UDP 房间广播、UDP 发现请求监听与单播回复、client 侧 UDP 搜索和主动探测、TCP 加入握手、最小准备状态交换、玩家资料同步、host 开局配置、主动 `LeaveRoom` 离开、对局中 `Heartbeat` 心跳检测、等待重连、client 恢复握手、进入带 LAN 标记的 `DuelScene`，以及 `SubmitMove` / `MoveAccepted` / `MoveRejected` / `BoardSnapshot` 的最小命令搬运；开局配置包含棋盘、时间、让子、host 座位、恢复会话凭据和双方已知玩家资料。正常落子、虚手、数子、悔棋和认输已通过 `DuelAuthoritySystem` 收敛为统一提交入口。页面预览、点击提交和动作按钮权限通过 `DuelInputAuthority` 读取，LAN 下该权限来自 host 广播的 `InputAuthority`，等待重连时强制无输入权。host 侧 `LanDuelSystem` 通过 `ChessBoardSystem` 的本进程 host 权威落子入口进行版本与棋规校验；本地/电脑对局的正常落子也使用同一入口，保证 Local/Computer 与 LAN host 的正常落子裁定共享同一规则应用路径。host 合法落子后广播权威棋盘快照；当前 `BoardSnapshot` 携带棋盘尺寸、下一手玩家、最后一步、棋子列表和 host 权威 KataGo 标准手顺，client 应用快照时同步棋盘与 `kataGoMoves` 并清除旧形势缓存。`PlayerProfile` 使用 base64 JSON 同步 host/client 玩家资料，当前只包含 `name` 字段，但以对象结构保留后续扩展空间；资料到达后会映射到黑白座位并刷新对局显示名。虚手通过 `SubmitPass` / `PassAccepted` 同步；认输通过 `SubmitResign` / `ResignAccepted` 同步；数子和悔棋通过确认请求/确认回复协议让对端弹窗确认。LAN 数子在对端同意请求后只广播候选 `ScoreResult`，双方都通过 `ScoreResultConfirmResponse` 接受后，host 才广播 `ScoreResultAccepted` 进入终局；拒绝请求、拒绝结果、请求失效或计算失败都会通过带原因的 `ScoreFailed` 恢复对局。悔棋确认阶段由 host 按 `actionId` 暂存原始请求，确认回复使用原始 `boardVersion` / `removeCount`；拒绝消息带请求方座位，避免非发起方误报失败，悔棋接受后会补发权威快照。host 也负责广播当前行棋方 `TimeState` 并在超时后广播 `PlayerTimeout`，client 只使用 host 计时状态刷新显示。对局中断线会进入无限等待重连，host 暂停计时，client 按表驱动间隔向原 host 发起 `WEIQIXN_RESUME`；恢复成功后 host 补发 `BoardSnapshot`、`InputAuthority` 和 `TimeState`。任一端退出 LAN 房间或对局时会通过 `LanRoomService` 释放 TCP/UDP socket、清空 LAN 会话队列；对端收到 `LeaveRoom` 后提示并回主菜单。尚未实现正式传输抽象、匹配系统或完整同步系统。架构方向已收敛为 host 权威、单一 server core、客户端只发命令；第一版 server core 允许嵌在 host 进程中运行，后续再决定是否拆成独立进程。OGS 接入应在该结构继续收敛后作为外部 server 权威适配，不复用本地/LAN host core 做最终裁定。

## 已具备的基础

- 本地对局已经有可用的状态流：玩家、当前回合、棋盘状态、落子事件、落子后推进。
- 棋盘状态已经以数据结构保存，而不是只存在于 Unity GameObject 上。
- 落子合法性集中在 `ChessBoardSystem`，具备提取为共享规则服务的基础。
- 事件系统可以作为 UI、本地逻辑和未来网络层之间的内部通信机制。
- 存档系统可为快照、重连缓存或本地复盘提供参考。
- UI 框架和配置系统可以继续扩展房间、匹配、邀请、断线提示等页面。
- `LanRoomService` 已提供最小局域网房间发现、连接、准备状态、玩家资料同步、开局配置、主动离开和落子命令搬运骨架；UDP 房间广播和单播发现回复会携带房主玩家名、棋盘、时间、读秒、让子、实际 host 座位和可见 host 座位选择项，搜索端会监听广播/单播回复并主动发送 `WEIQIXN_DISCOVER` 探测请求，搜索列表展示这些创建房间时选定的信息，默认座位选项显示为“猜先”而不是随机后的实际座位；`LanRoomPopup` 创建房间前会复用 `DuelSetupPopup` 设置棋盘、时间、让子和 host 座位，并在开局状态到达后进入带 LAN 标记的 `DuelScene`，UI 不直接持有 socket。
- LAN 房间创建、加入和搜索状态保持互斥：已有 LAN 会话时 `LanRoomService` 不再启动搜索，房间发现列表会过滤本机正在广播的 `roomId`；`LanRoomPopup` 在未进入 LAN 对局前关闭时统一释放搜索、房间广播、TCP listener/client 和会话队列。
- `PlayerProfile` 已作为局域网玩家资料同步协议接入；当前资料对象只含 `name`，通过 base64 JSON 搬运，避免分隔符冲突并允许后续扩展头像、等级、战绩等字段。
- 正常落子提交已收敛到 `DuelAuthoritySystem`：本地/电脑对局直接请求本进程权威落子，LAN 对局按本端座位提交到 `LanRoomService`，host 本地玩家也不绕过命令队列直接改盘。本地/电脑正常落子与 LAN host 接受 `SubmitMove` 已共用 `ChessBoardSystem` 内的 host 权威落子入口。
- 本端人类输入权限已收敛到 `DuelInputAuthority`：页面预览、点击提交和 LAN 提交前置检查使用同一份权限状态；LAN 原型由 host 广播 `InputAuthority` 更新 `SceneComponentDuel.localInputPlayerFlag`。
- 虚手提交已接入 host 权威路径：本端通过 `OnSubmitDuelPass` 提交，本地/电脑虚手和 LAN host 接受 `SubmitPass` 已共用 `DuelSystem` 的 host 回合命令校验与 pass 状态应用入口；LAN host 额外校验棋盘版本后广播 `PassAccepted`。
- 数子和悔棋已接入确认式 host 权威路径：发起方提交到 host，host 转发确认请求给对端；第一阶段请求校验已复用 `DuelSystem` 的 host 回合状态与版本校验入口。数子在对端同意请求后由 host 计算候选结果，随后等待双方确认结果，双方都接受才广播 `ScoreResultAccepted` 结算；任一方拒绝请求或结果、请求失效、KataGo 计算失败都会广播带原因的 `ScoreFailed` 并继续对局。悔棋请求在 host 侧按 `actionId` 保存原始版本和回退手数，拒绝消息会带请求方座位。
- 认输提交已接入 host 权威路径：本端通过 `OnSubmitDuelResign` 提交，本地确认认输和 LAN host 接受 `SubmitResign` 已共用 `DuelSystem` 的 host 回合命令校验与认输终局应用入口；LAN host 接受当前行棋方认输后广播 `ResignAccepted`，双方通过 `OnApplyLanDuelResign` 进入同一认输终局。
- `LanDuelSystem` 已接入 `DuelScene`，host 消费带棋盘版本的 `SubmitMove` 并通过现有规则入口广播 `MoveAccepted` / `MoveRejected`，client 只应用 host 接受的落子。
- 合法落子会递增 `SceneComponentDuel.lanBoardVersion`，host 随后广播包含棋盘尺寸、下一手玩家、最后一步、棋子列表和 host 权威 KataGo 标准手顺的 `BoardSnapshot`，client 用快照纠正本地棋盘与 `kataGoMoves`。
- LAN 计时已按 host 权威收敛：host 广播 `TimeState`，client 不自行扣时或裁定超时；host 广播 `PlayerTimeout` 后 client 进入同样的超时终局。
- `lan_room_config` 已承载局域网房间端口、连接超时、人数上限、广播间隔、缓冲区大小、心跳间隔、心跳超时和 client 重连探测间隔；协议消息名由 `LanRoomProtocol` 枚举按命名约定生成，并通过 `OnXxx` 接收函数注册到协议回调表。
- LAN 房间搜索端解析 UDP 广播或单播发现回复时，加入连接地址以 UDP `remoteEndPoint.Address` 为准；房主 payload 中的 host address 只作为 UDP 来源地址为空时的兜底，避免 Android 房主自报地址不可靠导致房间可发现但无法加入。搜索端主动探测目标包含全局广播、系统网卡推导出的子网广播和常见 Android 热点广播地址，用于提高 Android 热点主机创建房间时的发现率。
- `LeaveRoom` 已作为当前 LAN 会话生命周期协议接入；退出房间弹窗或 LAN 对局会主动通知对端、释放 TCP client/TCP listener/UDP broadcast/UDP discovery 并清空 LAN 消息队列，对端收到后提示联机结束并回主菜单。对局中非主动断线先进入等待重连，不立即触发对方离开。
- client 收到 host 开局 `StartConfig` 后会把 `roomId`、恢复会话凭据、host 地址端口和开局配置保存到本地恢复票据；client 进程被强杀后重新进入 LAN 搜索，如果原 host 已因心跳超时进入等待重连并在房间广播中标记可恢复，点击原房间会优先使用本地票据发送 `WEIQIXN_RESUME`，恢复成功后进入 `DuelScene`，再由 host 补发权威快照纠正棋盘。正常主动离开或收到 `LeaveRoom` 会清除恢复票据，避免误恢复已放弃的对局。

## 主要缺口

- 没有正式网络协议和传输抽象；当前房间阶段直接使用 UDP 广播和 TCP 握手骨架。
- 还没有完整服务端或可独立运行的房主权威会话核心；当前只完成房间 host 监听、加入握手、准备状态、开局命令、正常落子、虚手、计时、超时、认输、确认式数子和确认式悔棋的内嵌 host 权威原型。
- 移动命令协议已有最小正常落子命令、版本确认、输入权下发、玩家资料同步、虚手、认输、数子确认和悔棋确认，尚未覆盖断线重连后的完整恢复。
- 棋盘状态快照只覆盖落子后的棋盘纠偏，计时只覆盖在线显示校准和超时终局通知，尚未覆盖断线重连、房间完整状态或终局恢复。
- 房间生命周期只覆盖创建、搜索、连接、开局和主动离开；当前玩家资料同步只覆盖座位显示名，座位只覆盖创建房间时 host 选择黑白或猜先，不包含房间内换座、观战、账号身份或跨设备账号。
- 对局中断线已定义为无限等待重连并由 host 补发权威快照、输入权和时间状态；完整终局恢复仍未定义。当前数子仍依赖 KataGo ownership，悔棋不回滚历史计时快照。
- 没有防作弊边界。

## 推荐联机路线

1. 先定义最小在线对局目标：当前阶段以好友房或房间码为主，不把匹配当成第一交付物。
2. 保持 host 权威和单一 server core：host 端也只通过同一套命令入口推进会话，不能因主机身份绕过协议；正常落子已按该方向收敛，后续动作继续沿用同一合同。
3. 提取规则服务：本地和网络都调用同一个落子校验入口。
4. 在接入 OGS 前先继续收敛结构：Local/Computer 与 LAN host 共用 host 权威核心；OGS 只作为外部 server 权威适配到统一提交入口、输入权和棋盘表现层。
5. 定义最小协议：`JoinRoom`、`Ready`、`StartConfig`、`PlayerProfile`、`InputAuthority`、`SubmitMove`、`MoveAccepted`、`MoveRejected`、`BoardSnapshot`、`SubmitPass`、`PassAccepted`、`SubmitResign`、`ResignAccepted`、`SubmitScore`、`ScoreConfirmRequest`、`ScoreConfirmResponse`、`ScoreResult`、`ScoreResultConfirmResponse`、`ScoreResultAccepted`、`ScoreFailed`、`SubmitTakeBack`、`TakeBackConfirmRequest`、`TakeBackConfirmResponse`、`TakeBackAccepted`、`TakeBackRejected`、`TimeState`、`PlayerTimeout`、`LeaveRoom`、`Heartbeat`、`ResumeHello` / `ResumeAccept` / `ResumeReject`。
6. 明确快照结构：棋盘尺寸、当前玩家、座位映射、棋子列表或位置字典、KataGo 标准手顺、回合计时、最后一步、局面版本号、房间状态。
7. 再接入具体网络 SDK、OGS realtime 或自建传输层，传输层只负责搬运命令和快照，不负责本地/LAN host 规则判断；OGS 路径以 OGS server state 为最终权威。

## 阶段验收标准

- 本地对局仍可运行。
- 在线对局能在两个客户端之间同步合法落子，且双方都经由同一权威会话核心处理。
- 非法落子只在权威端被拒绝，并能回传明确原因和对应版本号。
- 双端棋盘状态有版本号或 hash 校验。
- 断线后至少能安全退出或重新同步快照。
