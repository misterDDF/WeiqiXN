# 已知 Bug

## 用途

本文档记录当前已知且尚未修复的 bug，方便后续会话继续跟踪。

- 只记录可复现或已经明确观察到的当前 bug。
- bug 修复并验证通过后，移除对应条目。
- 如果修复改变了当前行为，需要在同一轮修改中同步更新 `SPECIFICATION.md` 和受影响的模块文档。

## 未移除 Bug

- Unity Play 中 OGS 好友申请后台轮询可能遇到 UnityTLS 握手失败：已观察到 `GET https://online-go.com/api/v1/me/friends/invitations/` 报 `SecureChannelFailure` / `UNITYTLS_INTERNAL_ERROR` / `UNITYTLS_X509VERIFY_NOT_DONE`。当前已将好友申请数量后台刷新路径降级为 `Warn`，避免主菜单/好友列表红点轮询把网络/TLS 临时失败刷成 Console error；手动进入好友申请列表仍会显示读取失败状态。仍需确认该 TLS 失败是当前 Editor/系统证书链环境问题还是 OGS 接口访问稳定性问题，并在真实网络环境复测后决定是否需要改用 UnityWebRequest 或统一 HTTP 证书处理策略。
- OGS 对局连续虚手进入服务器 `stone removal` 数子阶段的死子确认流程仍待真实 OGS 双账号复测：当前已接入 `removed_stones` / `removed_stones_accepted` realtime 消息、确认死子 / 不接受提交入口、服务器权威死子半透明表现、hover 叉号预览和排除死子的临时 ownership 快照；已通过 Unity `Assets/Refresh`、脚本重编译和 Console error 检查。仍需用真实 OGS stone-removal 对局确认服务器 payload 形态、双方确认/拒绝后 phase 恢复或 finished 结果、按钮倒计时与对端同步表现无误后移除此条目。
- OGS 好友对战邀请接收侧可能不会弹出“OGS 对局邀请”确认框：当前邀请创建侧会等待对方接受，但接收侧提示依赖 incoming challenge 轮询；已观察到好友端疑似未收到弹窗。当前已补强邀请轮询触发与登录会话判断，修复 `OgsChallengeInviteCoordinator` 依赖未被全局调用的 `Init()` 导致轮询空引用中断的问题；经对照 OGS 官方前端源码，直接挑战列表应读取 `GET /api/v1/me/challenges/?page_size=30`，并按 `challenged` 当前账号过滤入站挑战，Unity 已从 `/me/challenges/invites/` 改为该接口且轮询间隔改为 2 秒。此前轮询范围仍只包含主菜单，导致普通对局、OGS 对局和复盘场景不会弹出邀请；当前已扩展这些场景的轮询，并在对局场景接受邀请时先提交本机认输、返回主菜单，再显示进入对局阻塞等待。接收侧拒绝已改为 `DELETE /api/v1/me/challenges/{id}/`，邀请方等待期间会把 challenge 消失或 realtime rejected 结果提示为“对方已拒绝邀请。”。已补充 `OGS incoming challenge invites refreshed.` 日志用于区分请求失败、返回 0 和解析为空；已通过 Unity `Assets/Refresh`、脚本重编译和 Console error 检查，仍待双账号真实 OGS 好友对战复测确认后移除此条目。
- 移动端点击 OGS 登录后的 OAuth 回跳仍待真机确认：Android/iOS 玩家包已改为让 OGS 回调 GitHub Pages HTTPS 中转页 `https://leo-zhang-git.github.io/weiqixn-oauth-redirect/ogs/callback/`，再由该静态页跳回 `weiqixn://ogs/callback` deep-link；仍待 GitHub Pages URL 可访问、OGS OAuth 应用后台登记该 HTTPS redirect URI，并在真机确认浏览器授权能回到应用。
- OGS `free_handicap_placement=true` 的让子局仍需专项复测：已观察到让二子对局中，服务器把前两手黑方自由摆子放在 `moves` 内。当前坐标规则已确认 OGS y 直接映射本地 `RectCoordinates.z`，不是反向转换；已补充多形态玩家 id 解析、OGS verbose payload 日志、free handicap 同色开局手顺解析和 OGS/local `z=y` 直接坐标映射。普通已有真人 OGS 对局载入与非 bot 悔棋路径已通过用户复测，后续只保留 free-handicap 形态待专项确认。
- `OgsFriendItemWidget.prefab` 在竖屏窄宽度下会让 `txt_rating` 与 `txt_status` 保持同排显示，导致段位/等级与在线状态文本重叠；当前已参考 `ReplayArchiveItemWidget` 将好友条目的昵称、OGS ID/国家、段位/等级、在线状态整理为上下四行文本列，并关闭自动换行改为截断显示，已通过 Unity `Assets/Refresh` 与 Console error 检查，仍待人工预览确认后移除此条目。
- `RecentReplayListPopup.prefab` 的复盘列表内容状态节点曾是默认 100x100 居中布局，导致三条预览用 `ReplayArchiveItemWidget` 被压在页面中央且底部被翻页栏遮挡；当前已将 `Content` 状态节点恢复为铺满 `sr_recent_replay_state` 的 stretch 布局，并通过 Unity 刷新和 Console error 检查，仍待人工预览确认后移除此条目。
- UserInfoPopup 这次 prefab 重构曾误改 `Assets/UI/Prefab/Page/UserInfoPopup.prefab` 的 Page Canvas 根节点并导致 prefab 预览生成 `Canvas (environment)`；当前已将根 Canvas 结构恢复到受保护序列化值并重新推进内容子树重构，仍待人工打开 Prefab Mode 确认预览中不再生成 `Canvas (environment)`。
- OGS 对局界面的服务器时间、官方起始钟和终局原因显示已接入 `gamedata` / realtime clock / server terminal payload 到本地 HUD 与侧边结算栏；仍待用真实 OGS 对局复测确认起始钟、双方时间刷新、数子/认输/超时终局原因映射和胜负方显示。
