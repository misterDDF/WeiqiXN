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
