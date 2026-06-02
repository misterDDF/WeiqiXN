# 已知 Bug

## 用途

本文档记录当前已知且尚未修复的 bug，方便后续会话继续跟踪。

- 只记录可复现或已经明确观察到的当前 bug。
- bug 修复并验证通过后，移除对应条目。
- 如果修复改变了当前行为，需要在同一轮修改中同步更新 `SPECIFICATION.md` 和受影响的模块文档。

## 未移除 Bug

- OGS 载入已有对局时可能把本端玩家座位或让子局首手颜色判断错误；已观察到 `free_handicap_placement=true` 的让二子对局中，服务器把前两手黑方自由摆子放在 `moves` 内。当前坐标规则已确认 OGS y 直接映射本地 `RectCoordinates.z`，不是反向转换；已补充多形态玩家 id 解析、OGS verbose payload 日志、free handicap 同色开局手顺解析和 OGS/local `z=y` 直接坐标映射，待用已有 OGS 对局载入路径复测确认。
- OGS 对局界面上的双方时间尚未按服务器时间正确刷新；当前日志显示 OGS realtime 仍有未接入的 game channel，时间状态需要从 OGS `gamedata` / realtime clock 类消息进入本地 `SceneComponentDuelInfo` 或等效 UI 刷新路径，待退出 Play 后修复并验证。
