# 存档模块

## 主要文件

- `Assets/Scripts/GlobalModule/GameSaveManager/GameSaveManager.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableObj.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableField.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableObjectDict.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/SavableSimple*.cs`
- `Assets/Scripts/GlobalModule/GameSaveManager/GameSaveConfig.cs`
- `Assets/Scripts/Game/System/DuelSaveInfoFile.cs`
- `Assets/Scripts/Game/System/DuelReplayArchiveSystem.cs`
- `Assets/Scripts/Game/System/DuelReplayIndexFile.cs`

## 职责

存档模块负责把 `SavableObj` 树保存为 JSON，并保留从 JSON 恢复对象状态的基础能力。当前正式对局流程只保留自动复盘归档写入路径；对局读取/继续对局入口暂不作为正式功能推进。

## 当前进度

- 支持同步保存和底层读取能力；当前阶段正式入口只覆盖保存。
- `GameSaveConfig.SaveRootPath` 统一定义存档根目录：Unity Editor 使用仓库根目录下的 `save/`，PC Standalone 使用游戏包体根目录下的 `save/`，其他非 Editor 平台使用 `Application.persistentDataPath`。
- `SavableObj.SaveObj()` 通过反射保存 public `SavableField<T>` 和子 `SavableObj`。
- 当前基础类型支持 `int`、`float`、`bool`、`string`。
- 可用 `SkipSavableCheckAttribute` 跳过不应保存的字段。
- `SceneBase` 继承 `SavableObj`，因此场景组件和对局数据可挂在场景保存树上。
- `DuelReplayArchiveSystem` 在成功落子、成功虚手、成功悔棋、数子失败回滚和终局后自动覆盖写入 `save/replay/{gameId}/DuelScene.json`、`DuelRecord.json` 和 `SaveInfo.json`。第一手有效手顺才生成 `gameId`，同一局后续变化只覆盖同一目录。
- `DuelReplayIndexFile` 维护 `save/replay/ReplayIndex.json`；索引只收录手数大于 10 的可见复盘归档，并按 `lastUpdatedAtUtc` 倒序保存轻量摘要。索引摘要冗余黑方和白方显示名，供最近对局列表直接展示双方信息；10 手及以内不会写入复盘目录或索引，已达到归档门槛后又悔棋回 10 手及以内时会移除索引项并删除对应归档目录。
- `SaveInfo.json` 记录 `saveSlotIndex`、`savedAtUtc`、`gameId`、创建/更新时间、归档时间、`moveCount`、是否完成、是否归档、来源类型、黑白双方显示名、胜者座位、终局结果、棋盘配置、时间配置和让子配置，用于后续菜单或复盘列表读取摘要；当前阶段暂不把继续对局入口作为正式功能。

## 设计观察

存档系统对本地对局很实用，也为联机断线恢复提供了可复用思路。当前正式入口只保留复盘归档；如果后续恢复运行中检查点或崩溃恢复，需要重新明确它与复盘归档、短局过滤和权威对局状态的关系。

## 风险和缺口

- 当前保存类型较窄，不支持枚举、列表以外复杂类型或版本迁移。
- 反射保存依赖 public 字段和类型名，重命名类型/字段会影响兼容性。
- 对局读取/继续对局入口暂不作为正式功能，后续需要产品入口时再恢复为阶段目标。
- 联机状态不能直接以本地客户端存档为权威。

## 后续建议

- 增加存档版本字段和迁移策略。
- 为对局保存写最小回归检查；读取回归等正式读档入口确定后再补。
- 联机阶段区分本地缓存、服务器快照、断线恢复快照。
