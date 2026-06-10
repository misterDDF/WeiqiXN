# 已知 Bug

## 用途

本文档记录当前已知且尚未修复的 bug，方便后续会话继续跟踪。

- 只记录可复现或已经明确观察到的当前 bug。
- bug 修复并验证通过后，移除对应条目。
- 如果修复改变了当前行为，需要在同一轮修改中同步更新 `SPECIFICATION.md` 和受影响的模块文档。

## 未移除 Bug

- 2026-06-10: OGS 对局在移动端切后台期间如果对手落子，切回前台后可能无法更新到最新对手落子。已落地修复候选：移动端前后台状态纳入 `Global.IsApplicationInBackground`，`OgsDuelSystem` 在前台恢复时强制重建 OGS realtime websocket 并重新 `game/connect` 等待 `gamedata` 补同步；前台每 10 秒执行连接健康检查，socket 关闭/缺失或 120 秒无 realtime payload 时自动重连。仍待真机验证：后台期间对手落子后切回前台能通过服务端 `gamedata` 更新棋盘。
- 2026-06-10: 引入专用 `UICamera` 后，Play 模式中 UI/场景渲染仍待最终确认。已落地修复候选：`UICamera` 改为独立高 depth Base camera，不再依赖 URP overlay stack，并使用 `CameraClearFlags.Nothing` 避免清掉场景相机颜色；`UILogicBase.OnUnityResourceLoaded()` 会递归设置已加载 UI 页面/Widget 实例到 `UI` layer；场景相机继续排除 `UI` layer。仍待基础 Play 验证 loading、主菜单、棋盘和对局页面都恢复可见；验证通过后移除此条。
- Android 天玑 9000（V2183A / mt6983 / Mali-G710 MC10）首次启动时可能在 KataGo OpenCL autotune 中卡住并退出：2026-06-09 真机日志显示 `com.DefaultCompany.WeiqiXN` 进程 20451 在 12:04:15 进入 `OpenCLTuner::loadOrAutoTune`，KataGo 日志记录 `No existing tuning parameters found` / `Performing autotuning` / `Dummy tuning thread starting` 后不再继续；系统在 12:05:45 记录 `am_proc_died` 和 `proc died without state saved`，未出现 `AndroidRuntime`、`Fatal signal`、`am_crash` 或 dropbox tombstone。该问题只在首次启动缺少本地 OpenCL tuning cache 时出现，后续应继续观察是否能够稳定生成并复用 `KataGoData/opencltuning` 下的缓存文件，必要时再按实际现象收敛启动策略。
