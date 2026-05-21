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

存档模块负责把 `SavableObj` 树保存为 JSON，并保留从 JSON 恢复对象状态的基础能力。当前正式对局流程只把保存入口纳入阶段范围，对局读取/继续对局入口暂不作为正式功能推进。

## 当前进度

- 支持同步保存、异步保存和底层读取能力；当前阶段正式入口只覆盖保存。
- `GameSaveConfig.SaveRootPath` 统一定义存档根目录：Unity Editor 使用仓库根目录下的 `save/`，PC Standalone 使用游戏包体根目录下的 `save/`，其他非 Editor 平台使用 `Application.persistentDataPath`。
- 异步保存时显示 `SavingPopup` 并使用 `savingLock` 防止并发保存；保存完成会返回成功/失败结果，异常路径通过 `finally` 释放保存锁并关闭弹窗。
- `SavableObj.SaveObj()` 通过反射保存 public `SavableField<T>` 和子 `SavableObj`。
- 当前基础类型支持 `int`、`float`、`bool`、`string`。
- 可用 `SkipSavableCheckAttribute` 跳过不应保存的字段。
- `SceneBase` 继承 `SavableObj`，因此场景组件和对局数据可挂在场景保存树上。
- `DuelSaveSystem` 已接入 `OnSaveDuelScene`；对局槽位结构为 `save/{slot}/`，场景状态保存为 `DuelScene.json`，棋盘记录保存为 `DuelRecord.json`，槽位摘要保存为 `SaveInfo.json`。记录文件、槽位摘要或场景状态保存失败时会发出 `OnDuelSaveResult`，避免最终场景保存失败被静默忽略。
- `SaveInfo.json` 记录 `saveSlotIndex`、`savedAtUtc`、`moveCount`、棋盘配置和时间配置，用于后续菜单或存档列表读取摘要；当前阶段暂不把继续对局入口作为正式功能。

## 设计观察

存档系统对本地对局很实用，也为联机断线恢复提供了可复用思路。但联机恢复需要明确“本地存档”和“权威对局状态”的关系。

## 风险和缺口

- 当前保存类型较窄，不支持枚举、列表以外复杂类型或版本迁移。
- 反射保存依赖 public 字段和类型名，重命名类型/字段会影响兼容性。
- 对局读取/继续对局入口暂不作为正式功能，后续需要产品入口时再恢复为阶段目标。
- 联机状态不能直接以本地客户端存档为权威。

## 后续建议

- 增加存档版本字段和迁移策略。
- 为对局保存写最小回归检查；读取回归等正式读档入口确定后再补。
- 联机阶段区分本地缓存、服务器快照、断线恢复快照。
