# 存档模块

## 主要文件

- `Assets/Scripts/GlobalModule/GameSaveManager/GameSaveManager.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableObj.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableField.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableObjectDict.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableSimple*.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/GameSaveConfig.cs`
- `Assets/Scripts/Game/System/DuelSaveSystem.cs`
- `Assets/Scripts/Game/System/DuelSaveInfoFile.cs`

## 职责

存档模块负责把 `SavableObj` 树保存为 JSON，并从 JSON 恢复对象状态。对局保存由 `DuelSaveSystem` 通过系统事件触发。

## 当前进度

- 支持同步保存、异步保存和读取。
- `GameSaveConfig.SaveRootPath` 统一定义存档根目录：Unity Editor 使用仓库根目录下的 `save/`，PC Standalone 使用游戏包体根目录下的 `save/`，其他非 Editor 平台使用 `Application.persistentDataPath`。
- 异步保存时显示 `SavingPopup` 并使用 `savingLock` 防止并发保存；保存完成会返回成功/失败结果，异常路径通过 `finally` 释放保存锁并关闭弹窗。
- `SavableObj.SaveObj()` 通过反射保存 public `SavableField<T>` 和子 `SavableObj`。
- 当前基础类型支持 `int`、`float`、`bool`、`string`。
- 可用 `SkipSavableCheckAttribute` 跳过不应保存的字段。
- `SceneBase` 继承 `SavableObj`，因此场景组件和对局数据可挂在场景保存树上。
- `DuelSaveSystem` 已接入 `OnSaveDuelScene`；对局槽位结构为 `save/{slot}/`，场景状态保存为 `DuelScene.json`，棋盘记录保存为 `DuelRecord.json`，槽位摘要保存为 `SaveInfo.json`。记录文件、槽位摘要或场景状态保存失败时会发出 `OnDuelSaveResult`，避免最终场景保存失败被静默忽略。
- `SaveInfo.json` 记录 `saveSlotIndex`、`savedAtUtc`、`moveCount`、棋盘配置和时间配置，用于菜单或存档列表读取摘要，不作为棋盘恢复权威；但它是槽位完整性文件，继续对局入口要求 `DuelScene.json`、`DuelRecord.json` 和 `SaveInfo.json` 同时存在。

## 设计观察

存档系统对本地对局很实用，也为联机断线恢复提供了可复用思路。但联机恢复需要明确“本地存档”和“权威对局状态”的关系。

## 风险和缺口

- 当前保存类型较窄，不支持枚举、列表以外复杂类型或版本迁移。
- 反射保存依赖 public 字段和类型名，重命名类型/字段会影响兼容性。
- 对局加载入口不清晰，保存有按钮，读取流程需要产品入口。
- 联机状态不能直接以本地客户端存档为权威。

## 后续建议

- 增加存档版本字段和迁移策略。
- 为对局存档写最小读写回归检查。
- 联机阶段区分本地缓存、服务器快照、断线恢复快照。
