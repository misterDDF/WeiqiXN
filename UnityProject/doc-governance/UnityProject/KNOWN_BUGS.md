# 已知 Bug

## 用途

本文档记录当前已知且尚未修复的 bug，方便后续会话继续跟踪。

- 只记录可复现或已经明确观察到的当前 bug。
- bug 修复并验证通过后，移除对应条目。
- 如果修复改变了当前行为，需要在同一轮修改中同步更新 `SPECIFICATION.md` 和受影响的模块文档。

## 未移除 Bug

- 2026-06-10: OGS 好友列表/详情资料显示和全局 realtime 连接修复候选已落地并通过 Unity 脚本编译验证，但尚待真实 OGS 会话运行时确认：好友列表使用 `/api/v1/me/friends/` 作为 bearer-token 主路径，并仅在主路径失败时回退到 `/api/v1/ui/friends`；登录态 OGS 会话会维护服务层全局 authenticated realtime websocket，好友在线状态按 OGS 前端行为先默认为离线，再通过该连接和 best-effort `user/monitor` / `user/state` 覆盖；OGS 对局 realtime session 已改为全局连接上的 game subscription，由全局连接按 `game/{id}/...` channel 路由；好友详情打开时补拉 `players/{id}` / `players/{id}/full`；好友列表、详情和在线状态响应由服务层统一做 10 秒本地缓存。实网验证好友列表字段、注册时间、在线状态、登录态保持在线、OGS 对局重连和详情补全均正常后移除此条。
