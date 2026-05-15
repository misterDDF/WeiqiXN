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
- `scene.json` 已配置 MainMenu 和 Duel。
- `ui_page.json` 已配置 LoadingPage、MainMenuPage、SavingPopup、UserInfoPopup、DuelSetupPopup、DuelPage。

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
