# 已知 Bug

## 用途

本文档记录当前已知且尚未修复的 bug，方便后续会话继续跟踪。

- 只记录可复现或已经明确观察到的当前 bug。
- bug 修复并验证通过后，移除对应条目。
- 如果修复改变了当前行为，需要在同一轮修改中同步更新 `SPECIFICATION.md` 和受影响的模块文档。

## 未移除 Bug

- OGS 载入已有对局时可能把本端玩家座位或让子局首手颜色判断错误；已观察到 `free_handicap_placement=true` 的让二子对局中，服务器把前两手黑方自由摆子放在 `moves` 内。当前坐标规则已确认 OGS y 直接映射本地 `RectCoordinates.z`，不是反向转换；已补充多形态玩家 id 解析、OGS verbose payload 日志、free handicap 同色开局手顺解析和 OGS/local `z=y` 直接坐标映射，待用已有 OGS 对局载入路径复测确认。
- OGS 对局界面的服务器时间、官方起始钟和终局原因显示已接入 `gamedata` / realtime clock / server terminal payload 到本地 HUD 与侧边结算栏；仍待用真实 OGS 对局复测确认起始钟、双方时间刷新、数子/认输/超时终局原因映射和胜负方显示。
