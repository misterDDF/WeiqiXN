# 事件与定时器模块

## 主要文件

- `Assets/Scripts/GlobalModule/EventManager/EventManager.cs`
- `Assets/Scripts/GlobalModule/EventManager/SystemEventDefine.cs`
- `Assets/Scripts/GlobalModule/EventManager/EntityEventDefine.cs`
- `Assets/Scripts/Game/Base/IEventReceiver.cs`
- `Assets/Scripts/GlobalModule/TimerManager/TimerManager.cs`
- `Assets/Scripts/GlobalModule/TimerManager/TimerBase.cs`
- `Assets/Scripts/GlobalModule/TimerManager/*Timer.cs`
- `Assets/Scripts/Game/Base/ITimerAttacher.cs`

## 职责

事件模块负责系统事件和实体事件的注册、派发和注销。定时器模块负责秒级和帧级的 timeout/interval 定时器，并把定时器绑定到拥有者生命周期。

## 当前进度

- 系统事件和实体事件都按事件类型字符串组织 handler 列表。
- 事件接收者会记录已注册 handler，销毁时可统一注销。
- 场景、模块、UI 逻辑等对象都可以作为事件接收者。
- `TimerManager` 支持 `SecondTimeout`、`SecondInterval`、`FrameTimeout`、`FrameInterval`。
- `DuelStateTurnInput` 使用 `SecondIntervalTimer` 实现 30 秒倒计时。

## 设计观察

事件与定时器已经支撑了本地对局的主要流程。对联机而言，事件可继续作为本地模块之间的解耦机制，但网络消息不应无边界地映射为任意系统事件。

## 风险和缺口

- 事件派发过程中 handler 列表如果被修改，未来可能需要复制列表或延迟变更。
- 事件类型字符串由类型名派生，重命名会影响日志和潜在调试工具。
- 定时器 id 使用 `Time.realtimeSinceStartup` 和递增索引，适合运行时唯一性，不适合作为可保存状态。
- 联机回合计时需要权威时间源，不能直接依赖本地 `SecondIntervalTimer`。

## 后续建议

- 为非法落子、对局结束、超时等增加明确系统事件。
- 联机层只把网络消息转换为受控的领域命令，不要直接开放任意事件派发。
- 在线对局倒计时应从服务器时间或同步时间派生，本地定时器只做显示和预测。
## 2026-05-15 Current Addendum

- `DuelStateTurnInput` still uses `SecondIntervalTimer`, but the countdown source is now the selected hold-time and byoyomi configuration instead of a fixed 30-second turn.
- Infinite hold time does not create a turn countdown timer for the current player.
- Byoyomi periods reset when a player enters a new input turn while already in byoyomi; period timeout consumes one byoyomi count, and count exhaustion enters `GameEnd` through timeout loss.
