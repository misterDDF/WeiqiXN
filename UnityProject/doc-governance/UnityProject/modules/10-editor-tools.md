# 编辑器工具模块

## 主要文件

- `Assets/Scripts/Editor/UI/UICodeGenerator.cs`
- `Assets/Scripts/Editor/UI/CSCodeGenerator.cs`
- `Assets/Scripts/Editor/UI/UIPagePrefabPreviewPlatformMenu.cs`
- `Assets/Scripts/Editor/Inspector/UIBinderEditor_Inspector.cs`
- `Assets/Scripts/Editor/Inspector/UIBinderBase_Inspector.cs`
- `Assets/Scripts/Editor/CustomEditorMenuPaths.cs`
- `Assets/Scripts/Editor/KataGoOpenClWarmupCleaner.cs`
- `Assets/Scripts/Editor/Build/AssetBundleGenerator.cs`
- `Assets/Scripts/Editor/Build/BuildConfig.cs`
- `Assets/Scripts/Editor/TMPSprite/SpriteAtlasToTMPSpriteTool.cs`
- `Assets/Scripts/Editor/Inspector/TextureArrayWizard.cs`
- `Assets/Scripts/Global/GameConfig.cs`
- `Assets/Scripts/Game/KataGo/KataGoBootstrap.cs`
- `Assets/Scripts/Game/KataGo/KataGoRuntimeEnvironment.cs`
- `Assets/Scripts/Game/KataGo/Win32NativeKataGoEngine.cs`
- `Assets/Scripts/Game/KataGo/KataGoDuelRecordFile.cs`
- `Assets/Scripts/Game/KataGo/KataGoPositionJsonBuilder.cs`

## 职责

编辑器工具模块负责提升内容生产效率，包括 UI Binder 代码生成、UI 逻辑脚手架、AssetBundle 生成、TMP sprite 转换和材质/贴图辅助工具。

## 当前进度

- `UICodeGenerator` 会根据 `UIBinderEditor` 节点导出 Binder 脚本。
- 如果 UI 逻辑文件不存在，生成器会创建基础 `UIPageWithBinder<T>` 或 `UIWidgetWithBinder<T>` 逻辑类。
- 生成脚本会自动刷新 AssetDatabase。
- Page prefab 右键菜单提供 `切换预览平台/PC端` 和 `切换预览平台/移动端`，可从 Project 面板的 Page prefab asset 触发，也可在 Page prefab 的 Prefab Mode 中从 Hierarchy 右键触发；入口仅对 `Assets/UI/Prefab/Page/*.prefab` 且带 `CanvasScaler` 的 prefab 生效。菜单直接保存 prefab 的 CanvasScaler 预览尺寸、同步切换 Editor Game 窗口固定分辨率，并写入不纳入版本库的 `UserSettings/UIRuntimeCanvasResolution.json`，让 Editor Play 模式使用同一运行时 UI 分辨率基准；PC 预览为 `1600x900`，移动端预览为 `720x1280`。
- 项目已有多份生成的 `*PageUI.cs` 和页面逻辑类。
- AssetBundle、TMP sprite、TextureArray 等编辑器工具文件已经存在。
- 项目自维护编辑器菜单统一挂在 Unity 顶部菜单 `自定义功能` 下；迁移既有菜单时保留去掉原 `Assets/` 前缀后的多层结构，例如 `Assets/打包/打PC包` 对应 `自定义功能/打包/打PC包`。KataGo OpenCL 预热缓存可通过 `自定义功能/KataGo/清除opencl预热文件` 清除，命令执行结束后会弹窗提示清除成功、未找到缓存或失败原因。
- 编辑器自动化优先通过 Unity MCP 执行。C# 编译验证优先使用 MCP `recompile_scripts` 并读取 Unity Console；不再把 `dotnet build UnityProject.sln` 作为常规验证路径。
- 场景对象、材质、资源导入等 MCP 已覆盖的编辑器操作应优先走 MCP。既有复杂 prefab asset 的层级编辑只有在 MCP 工具能完整覆盖时才直接用 MCP；否则应使用 Unity 编辑器脚本、编辑器菜单或人工 prefab 编辑，再通过 MCP 做导入和编译验证，避免手写复杂 prefab YAML。
- KataGo 流程当前目标是跑通 Windows Unity Editor 和 Windows PC 包内的本地 exe/native 后端调用和 JSON 解析。
- 启动流程在 Loading 阶段调用 `KataGoBootstrap.Start()`，退出时调用 `KataGoBootstrap.Stop()`；`KataGoBootstrap` 内部读取根目录 `game-config.json` 并按平台解析后端和引擎路径。Windows Unity Editor 通过仓库根 `KataGo/` 定位资源，Windows PC 包通过包体根目录 `<BuildRoot>/KataGo/` 定位资源。`exe` 后端通过 Win32 pipe 子进程适配器优先后台启动 OpenCL 版 KataGo，加载本地模型，依次发送 9 路、13 路、19 路 smoke query，并在日志中输出各棋盘路数的 `ownershipLength`；OpenCL 缺失、启动失败或任一 smoke test 失败时会自动 fallback 到 Eigen AVX2 CPU 版 KataGo。`native` 后端通过 `Win32NativeKataGoEngine` 加载 `native-eigen/katago_bridge.dll`，使用同一套 smoke query 验证 bridge。启动时会检查游戏根目录写权限；不可写时通过 `ConfirmPopup` 提示模式提示、跳过 OpenCL，并使用 no-write analysis 配置关闭 KataGo 文件写入。
- Native KataGo analysis concurrency is configured through the shared `katago.analysis.maxConcurrentNativeRequests` value in `game-config.json`. `KataGoBootstrap` uses the same request gate for Windows and Android native backends, then caps to `1` if the loaded bridge binary does not export concurrent-analyze support. The native bridge routes simultaneous `kg_analyze` calls by request id on the single active analysis engine, and the current OpenCL analysis config uses `numAnalysisThreads=2`, `numSearchThreadsPerAnalysisThread=4`, and `nnMaxBatchSize=16` for two in-flight positions without increasing total search threads above `8`. Windows native DLLs and Android eigen plugin are rebuilt for the concurrent bridge protocol; Android OpenCL remains capped to serialized mode until rebuilt with an Android OpenCL library input.
- `KataGoBootstrap.AnalyzeOwnershipAsync` 负责把当前对局 query 交给已选 KataGo 后端，读取匹配 request id 的最终结果，并只返回第一版形势按钮需要的 `ownership` 数组；超时、后端未运行和协议缺失会写入日志。分析超时会停止当前 KataGo 后端实例，后续分析请求会先检查状态并尝试按已解析路径自动重启，避免一次超时后持续失败。双方目数面板由游戏侧根据 `0.45` ownership 阈值统计，低于阈值的中立或未明确控制点不计入双方，白方显示值会额外加上 query 中的 `komi`。
- `KataGoPositionJsonBuilder` 当前提供 `BuildOwnershipAnalysisJson` 默认入口，以及完整手顺和当前盘面快照两个显式 JSON 生成入口；正常对局直接维护 KataGo 标准 `moves`，合法落子记录点位、虚手记录 `pass`。形势展示和 ownership 数子使用默认入口按当前棋盘快照生成 `initialStones` 并置空 `moves`；电脑对局实时落子、记录文件和后续复盘分析使用完整手顺入口。电脑对局实时落子请求的访问次数由 `DuelAiBudgetService` 按难度配置和棋盘路数解析，再由 `DuelAiAnalyzeService` 传入 JSON 生成入口；JSON Builder 不再维护本地固定上限。本地 `RectCoordinates` 已按 KataGo 棋盘布局定义，点位输出不再维护额外坐标兼容转换层。
- `KataGoDuelRecordFile` 负责保存和读取对局棋盘记录文件的底层能力。当前正式流程只使用保存侧；记录文件采用可直接提交给 KataGo analysis engine 的 JSON 结构，`pass` 项会保存在手顺中但不会改变棋盘缓存。

## 设计观察

UI 工具链已经承担了重复代码生成工作，这对后续快速增加联机页面有帮助。现有自动生成 Binder 的方式要求 prefab 上的绑定信息和生成脚本保持同步。

## 风险和缺口

- 自动生成文件可能覆盖手写 Binder 修改，Binder 文件应视为生成产物。
- UI 逻辑文件只在不存在时生成，后续逻辑需要手工维护。
- 编辑器工具没有在文档中形成使用流程，新成员容易误改生成文件。
- KataGo 二进制、模型和配置路径位于仓库根目录 `KataGo/`，该目录不进入 Unity `Assets` 导入体系；Windows PC 构建会按 `game-config.json` 的 `katago.backend.windowsPlayer` 校验和复制对应 runtime，并始终复制根目录 `game-config.json`。exe 校验 CPU fallback 所需的 `eigenavx2` 运行文件、`analysis_example.cfg`、`analysis_nowrite.cfg` 和模型，如果 `opencl` 引擎目录已随包提供，则同时校验其 `katago.exe` 和 `analysis_example.cfg`，复制时保留完整 `KataGo/` runtime 并排除 `analysis_logs`、`KataGoData` 和 `.meta`；native 校验并只复制 `native-eigen/katago_bridge.dll`、`analysis_nowrite.cfg` 和当前模型，因此 native 包不会携带 `opencl/`、`eigenavx2/` 或 `katago.exe`。缺失、启动失败、超时和协议解析失败都需要有明确日志。

## 后续建议

- 2026-05-25: KataGo Windows native packaging follows `game-config.json` native candidates. With CPU fallback enabled, `native-eigen/katago_bridge.dll` and `analysis_nowrite.cfg` are required; incomplete `native-opencl` produces build warnings and is skipped from the native runtime copy until `katago_bridge.dll` is available. Runtime fallback failures are logged with candidate name, DLL/config/model paths, write mode, and exception details.

## 2026-05-20 Runtime Asset Build Addendum

- `AssetBundleGenerator` includes `PackRuntimeAssetTable` in the shared AssetBundle build path used by both PC and WebGL build menu entries.
- `PackRuntimeAssetTable` reads `Assets/Config/DataJson/runtime_asset/runtime_asset.json`, validates each declared asset path and type, and applies the configured AssetBundle label before `BuildAssetBundles`.
- The source table is `ConfigExporter/xlsx/runtime_asset.xlsx`; generated JSON and data type files should not be hand-edited.
- Player build cleanup is scoped to the current platform output directory: Windows clears `../Build/PC`, Android clears `../Build/Android`, and WebGL clears `../WebGL`; Windows builds do not delete the shared `../Build` root or existing Android APK output.

- 在 UI 模块补一份“新增页面流程”。
- 明确 `*UI.cs` Binder 文件为生成文件，业务逻辑写在 `Logic/Page` 或 `Logic/Widget`。
- 新增 KataGo 编辑器工具时，优先提供一次性 smoke test：选择或读取本地 `katago`、模型和 config 路径，按 9 路、13 路、19 路请求 `includeOwnership`，并输出各棋盘路数 ownership 数组长度和错误原因；第一版不要把 `scoreLead`、胜率或最佳选点接到形势按钮。
- 联机页面新增前先跑一遍 UI 生成流程，避免手工维护绑定字段。
