# 已知 Bug

## 用途

本文档记录当前已知且尚未修复的 bug，方便后续会话继续跟踪。

- 只记录可复现或已经明确观察到的当前 bug。
- bug 修复并验证通过后，移除对应条目。
- 如果修复改变了当前行为，需要在同一轮修改中同步更新 `SPECIFICATION.md` 和受影响的模块文档。

## 未移除 Bug

- Android 天玑 9000（V2183A / mt6983 / Mali-G710 MC10）首次启动时可能在 KataGo OpenCL autotune 中卡住并退出：2026-06-09 真机日志显示 `com.DefaultCompany.WeiqiXN` 进程 20451 在 12:04:15 进入 `OpenCLTuner::loadOrAutoTune`，KataGo 日志记录 `No existing tuning parameters found` / `Performing autotuning` / `Dummy tuning thread starting` 后不再继续；系统在 12:05:45 记录 `am_proc_died` 和 `proc died without state saved`，未出现 `AndroidRuntime`、`Fatal signal`、`am_crash` 或 dropbox tombstone。该问题只在首次启动缺少本地 OpenCL tuning cache 时出现，后续应继续观察是否能够稳定生成并复用 `KataGoData/opencltuning` 下的缓存文件，必要时再按实际现象收敛启动策略。
