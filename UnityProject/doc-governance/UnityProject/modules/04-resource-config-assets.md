# 资源、配置与构建资源模块

## 主要文件

- `Assets/Scripts/GlobalModule/ResourceManager/ResourceManager.cs`
- `Assets/Scripts/GlobalModule/ResourceManager/ResourceLoader/AssetDatabaseLoader.cs`
- `Assets/Scripts/GlobalModule/ResourceManager/ResourceLoader/AssetBundleLoader.cs`
- `Assets/Scripts/GlobalModule/ResourceManager/AssetRequest/*.cs`
- `Assets/Scripts/GlobalModule/ResourceManager/ResourceUtils.cs`
- `Assets/Config/DataJson/*.json`
- `Assets/Config/DataType/**/*.cs`
- `Assets/Scripts/Editor/Build/AssetBundleGenerator.cs`

## 职责

资源模块负责根据逻辑路径加载 Unity 资源。配置模块通过 JSON 和对应 data type 为场景、UI、棋盘、预制体和 TMP sprite 提供数据入口。

## 当前进度

- 编辑器下资源通过 `AssetDatabaseLoader` 读取。
- 非编辑器下会预加载 AssetBundle，并用 `AssetBundleLoader` 按路径读取资源。
- `ResourceManager` 支持同步 prefab 加载、异步 prefab 加载、异步资源请求、加载绑定者取消。
- `game_prefab.json` 已配置 EventSystem、IngameDebugConsole、落子 VFX、黑白棋子预制体。
- `chess_board.json` 已配置 `9x9`、`13x13`、`19x19`。
- `duel_ai_difficulty.json` 已配置电脑对局难度选项、KataGo human policy profile、基础访问次数、基础候选点数量、失误率、采样温度、基础最大亏损目数、访问权重、提前虚手开关、思考延迟，以及 9 路、13 路、19 路分别使用的实时访问上限、候选点数量、最大亏损目数覆盖值和动态预算阈值。
- `scene.json` 已配置 MainMenu 和 Duel。
- `ui_page.json` 已配置 LoadingPage、MainMenuPage、SavingPopup、UserInfoPopup、DuelSetupPopup、DuelPage。
- `message.json` 已配置运行时 UI 展示文案，包括通用弹窗按钮、Loading 状态、局域网房间状态、对局 HUD 和操作反馈文案；代码侧通过 `MessageText` 消费。

## 设计观察

配置驱动的资源和场景入口已经满足本地对局扩展需求。后续联机阶段可以复用配置层增加房间、匹配、联网对局页面和网络状态提示。

## 风险和缺口

- `ResourceManager.LoadAsset<TAsset>` 当前内部调用 `ResourceUtils.GetAssetFullPath<GameObject>(assetPath)`，泛型类型没有传下去，非 prefab 资源扩展时需要检查。
- AssetBundle 预加载依赖 `GlobalConfig.PATH_ASSET_BUNDLE` 目录存在。
- 配置 data type 是静态缓存字典，热更新或运行时重载配置需要额外设计。

## 后续建议

- 在新增联网 UI 前，先补齐 `ui_page.json`、prefab 配置和生成的 Binder。
- 为资源路径配置加一次完整校验工具，避免运行时才发现 prefab 缺失。
- 如果联机需要热更新资源，单独设计资源版本和配置版本，不要隐式复用当前本地配置缓存。
## 2026-05-15 Current Addendum

- Time-control configs are exported from `ConfigExporter/xlsx/duel_hold_time.xlsx`, `duel_byoyomi_count.xlsx`, and `duel_byoyomi_time.xlsx`.
- Runtime JSON lives under `Assets/Config/DataJson/duel_hold_time/`, `duel_byoyomi_count/`, and `duel_byoyomi_time/`.
- Generated data types live under `Assets/Config/DataType/duel_hold_time/`, `duel_byoyomi_count/`, and `duel_byoyomi_time/`.
- Computer-duel difficulty configs are exported from `ConfigExporter/xlsx/duel_ai_difficulty.xlsx`.
- Runtime AI difficulty JSON lives under `Assets/Config/DataJson/duel_ai_difficulty/`; its generated data type lives under `Assets/Config/DataType/duel_ai_difficulty/`.
- Board-size-specific AI parameters are exported as `realtimeMaxVisits9/13/19`, `candidateLimit9/13/19`, and `maxScoreLoss9/13/19`. `maxVisits` remains the theoretical upper bound for the difficulty, while the realtime visit budget is resolved per board size at runtime.
- Dynamic-budget AI parameters are exported as `dynamicBudgetEnabled`, `probeMaxVisits9/13/19`, `openingProbeMoveLimit9/13/19`, `closeScoreLeadThreshold9/13/19`, `closeWinrateThreshold9/13/19`, `simpleCandidateGapThreshold9/13/19`, `confidentBestMoveGapThreshold9/13/19`, `forceFullBudgetMoveLimit9/13/19`, and `probeMinMoveInfoCount`. Disabled difficulties keep the single-request behavior.
- Runtime config JSON is UTF-8. When validating config JSON that contains Chinese text in Windows PowerShell, use `Get-Content -Raw -Encoding UTF8 ... | ConvertFrom-Json`; omitting `-Encoding UTF8` can corrupt Chinese text during PowerShell reads and surface as a misleading JSON parse error. Unity-side config loading is not affected when the file itself is valid UTF-8 JSON.
- Runtime-only asset configs are exported from `ConfigExporter/xlsx/runtime_asset.xlsx`. The exported JSON lives under `Assets/Config/DataJson/runtime_asset/`, and the generated data type lives under `Assets/Config/DataType/runtime_asset/`.
- The runtime asset table is consumed by `AssetBundleGenerator.PackRuntimeAssetTable`, which validates each listed asset and sets the configured AssetBundle label before building bundles.
- The current runtime asset entries are the shared black and white chess-board materials used by ownership overlay and latest-move marker rendering.
- LAN room runtime parameter configs are exported from `ConfigExporter/xlsx/lan_room_config.xlsx`. The exported JSON lives under `Assets/Config/DataJson/lan_room_config/`, and the generated data type lives under `Assets/Config/DataType/lan_room_config/`. The table contains ports, timeout, max player count, broadcast interval, and buffer sizes; LAN protocol strings are code-owned by `LanRoomProtocol`.
- Runtime UI message configs are exported from `ConfigExporter/xlsx/message.xlsx`. The exported JSON lives under `Assets/Config/DataJson/message/`, and the generated data type lives under `Assets/Config/DataType/message/`. The table contains player-facing runtime text; protocol strings, logs, editor menu text, and prefab-owned fixed initial text are not part of this table.
