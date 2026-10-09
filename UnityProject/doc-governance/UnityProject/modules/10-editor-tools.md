# 编辑器工具模块

## 主要文件

- UI 生成与预览：`Assets/Scripts/Editor/UI/UICodeGenerator.cs`、`CSCodeGenerator.cs`、`UIPagePrefabPreviewPlatformMenu.cs`；`Assets/Scripts/Editor/Inspector/UIBinderEditor_Inspector.cs`、`UIBinderBase_Inspector.cs`
- UI 主题：`Assets/Scripts/Editor/UI/UIThemePreviewCaptureTool.cs`、`UIThemeFontAssetTool.cs`、`UIThemeTextureTool.cs`、`UIThemeMigrationTool.cs`、`MainMenuBoardBackdropTool.cs`
- 棋盘与场景美术：`Assets/Scripts/Editor/Chess/ChessStoneAssetPolishTool.cs`、`ChessStonePreviewAssetPolishTool.cs`、`ChessBoardOverlayAssetPolishTool.cs`；`Assets/Scripts/Editor/Scene/DuelSceneLookAssetTool.cs`、`DuelLookPreviewCaptureTool.cs`
- 通用：`Assets/Scripts/Editor/CustomEditorMenuPaths.cs`、`EditorUtils.cs`、`KataGoOpenClWarmupCleaner.cs`
- 复盘音效验证：`Assets/Scripts/Editor/ReplayAudioValidationTool.cs`
- 构建与资源：`Assets/Scripts/Editor/Build/AssetBundleGenerator.cs`、`BuildConfig.cs`；`Assets/Scripts/Editor/TMPSprite/SpriteAtlasToTMPSpriteTool.cs`；`Assets/Scripts/Editor/Inspector/TextureArrayWizard.cs`
- KataGo 运行时：`Assets/Scripts/Global/GameConfig.cs`；`Assets/Scripts/Game/KataGo/` 下的 `KataGoBootstrap`、`KataGoRuntimeEnvironment`、`KataGoRuntimePreparer`、`Win32NativeKataGoEngine`、`AndroidNativeKataGoEngine`、`KataGoDuelRecordFile`、`KataGoPositionJsonBuilder`

## 职责

编辑器工具模块负责提升内容生产效率：UI Binder 代码生成、UI 逻辑脚手架、美术配置与离屏截图、AssetBundle 生成、TMP sprite 转换和贴图辅助工具。KataGo 运行时接入和打包校验目前也记在本文。

## 当前进度

通用：

- 自维护菜单统一挂在顶部菜单 `自定义功能` 下，迁移既有菜单时保留去掉 `Assets/` 前缀后的层级（例如 `自定义功能/打包/打PC包`）。
- `自定义功能/KataGo/清除opencl预热文件` 清除 OpenCL 预热缓存，结束后弹窗报告结果。
- 编辑器自动化和编译验证的规则见 [AGENTS.md](../AGENTS.md)「编译验证」「编辑器操作策略」。

UI 生成与预览：

- `UICodeGenerator` 按 `UIBinderEditor` 节点导出 Binder 脚本，并刷新 AssetDatabase。逻辑文件不存在时，生成基础的 `UIPageWithBinder<T>` / `UIWidgetWithBinder<T>` 类。
- Page prefab 右键菜单 `切换预览平台/PC端`（1600×900）和 `切换预览平台/移动端`（720×1280）。Project 面板和 Prefab Mode 的 Hierarchy 都可触发，只对 `Assets/UI/Prefab/Page/*.prefab` 中带 `CanvasScaler` 的 prefab 生效。执行时会：
  - 保存 prefab 的 CanvasScaler 预览尺寸；
  - 切换 Game 窗口分辨率；
  - 写入不入库的 `UserSettings/UIRuntimeCanvasResolution.json`，让 Editor Play 使用同一基准。

对局画面美术：

- 配置由幂等菜单维护，参数写在工具常量里。执行顺序：
  1. `自定义功能/棋盘/应用棋盘覆盖层材质`
  2. `应用棋子美术配置`
  3. `应用预览棋子透明配置`：预览材质从正式材质复制，所以必须在上一步之后执行。
  4. `自定义功能/场景/应用对局场景画面配置`
- `自定义功能/场景/生成对局画面预览截图` 不进 Play，离屏渲染九张图到 `Temp/WeiqiXN/LookPreview/`：
  - 19 路横屏、9 路横屏、19 路竖屏、19 路近景；
  - 用合成数据画的形势和推荐点（全景与近景）、13 路手数、三位数手数近景；
  - 落子关键帧动效近景 `board19_motion_closeup`。
- `Temp/` 在编辑器重启时清空，要保留的图需要另存。

UI 主题（V3，参数以工具代码常量为准）：

- `自定义功能/UI/生成主题样张截图`：在预览场景里临时搭 World Space Canvas，输出两张 1600×900 样张到 `Temp/WeiqiXN/ThemePreview/`：
  - `theme_landscape.png`：同一套内容分别放在纸卡上和桌面上，展示字号阶梯、文字色、字重对比和 `UIPalette` 全部令牌。
  - `theme_controls.png`：按钮各状态、输入框、下拉、勾选、滑条、滚动条、列表和全部图标。
  - Regular 文字只用图集里已有的字符，缺字时跳过并警告，避免给 10MB 的 Regular 资产追加字形。
  - 离屏渲染共用 `EditorUtils.RenderCameraToPng`。
- `自定义功能/UI/生成主题贴图`：用有符号距离场生成 `Assets/UI/Textures/Theme/` 下的白色 + alpha 贴图。
  - 按 4 倍精度绘制（PPU 400；柔影 2 倍、PPU 200），开 mipmap、Trilinear、不压缩，打 `ui_main_texture` 标签。
  - 1px 边缘的明度由 `Hairline` 与 `Paper` 算出。
  - 字节和导入参数不变时不重写，可重复执行。
- `自定义功能/UI/生成主题字体资产`：为思源黑体 CN Medium 和思源宋体 CN SemiBold 生成动态 TMP 字体资产。
  - 参数与 Regular 相同：采样 90、padding 9、SDFAA、1024 多图集。
  - 同时校正 fallback 到 Regular，并给 OTF 和字体资产打 `font` 标签。
  - 已有资产只校正设置、不重建。
- `自定义功能/UI/生成页面预览截图`：把每个 Page prefab 临时改为 World Space，按运行时参考分辨率 1:1 铺满，输出 `page_<名称>.png`（1600×900）和 `page_<名称>_portrait.png`（720×1280）。
  - 有 `Landscape` / `Portrait` 状态的 `StateRoot` 会切到对应状态。
  - 截的是 prefab 默认状态，运行时才填的内容不会出现。
  - 工具不保存页面。
- `自定义功能/UI/安装主菜单真实棋盘背景`：从对局场景复制棋盘模板到主菜单场景，引用正式黑白棋子预制体，并同步灯光与环境色；页面旧图片背景保持停用。
- `自定义功能/UI/生成主菜单棋盘预览截图`：在编辑态生成横竖屏真实棋盘与完整主菜单合成图到 `Temp/WeiqiXN/MainMenuPreview/`，不保存临时棋子或页面实例。
- `自定义功能/UI/应用主题迁移`：按 `UIThemeMigrationTool` 的页面规则表，把节点路径映射到样式角色，只写外观，不按旧颜色全局替换。
  - 写入范围：
    - Image 的 sprite、type 和颜色；
    - TMP 的字体与默认材质、字号（自动字号以样式字号为上限）、颜色，并去掉 Bold；
    - Selectable 的 ColorTint 色块，底板 `Image.color` 置白；按钮另挂 `UIButtonFeedback`，输入框不挂；
    - 移除被套样式节点上的 `Shadow` / `Outline`。
  - 保存前把全部 `RectTransform` 和根 `Canvas.renderMode` 恢复成资产里的值。
  - 跳过嵌套 prefab 实例里的节点并警告；贴图或字体缺失时整次不执行；可重复执行。
  - 首批 `LoadingPage` / `ConfirmPopup` 的平涂规则只用于旧结构；检测到 V3.3 新内容结构时跳过，避免覆盖新版深色底板与透明层级。
- `自定义功能/UI/重建 DuelPage V3.3`：按 14 号文档方案 E 幂等重建 `DuelPage.prefab` 与 `DuelMoveConfirmPopup.prefab` 的 `PanelRoot` 子树、`sr_platform` 横竖状态、固定控件和 Binder 引用。
  - 保存前把两个 Page 的 Canvas 根显式恢复为零尺寸 `Screen Space - Camera` 结构，布局只写在 `PanelRoot` 内。
  - 菜单不改场景和业务配置；运行时模式、玩家、时钟、手数、形势、提示和按钮可用性仍由 `DuelPageHudView` 刷新。
- `自定义功能/UI/重建 MainMenuPage V3.3`：幂等重建 `MainMenuPage.prefab` 的 `PanelRoot`、`sr_platform` 横竖状态、宣纸导航、用户胶囊和 Binder 引用；真实棋盘由主菜单场景独立渲染，不改变入口事件。
- `自定义功能/UI/重排剩余页面 V3.3`：按已确认补缺样图维护九个剩余 Page、三个列表 Widget 和 DuelPage 竖屏终局。保留 Page 根 Canvas 序列化结构；固定控件与状态通过 Binder 导出工具维护，首次生成新字段编译后再执行一次以回填引用。批量处理期间锁定程序集重载。
- `自定义功能/UI/更新对局设置竖屏抽屉`：只维护 `DuelSetupPopup.prefab` 的竖屏底部纸面抽屉和组件引用，保留 Canvas、PanelRoot 与既有横屏状态。全量重排剩余页面也复用此布局。
- `自定义功能/UI/生成对局设置预览`：输出六种业务模式的横屏及四种竖屏比例合成截图，检查控件边界、文字高度、模式收缩与横竖屏旋转恢复；竖屏摘要和按钮文案调用真实页面刷新路径，报告写到 `Temp/WeiqiXN/ThemePreview/setup_drawer_validation.txt`。仅修改预览实例，不主动进入 Play。
- `自定义功能/UI/更新复盘图表视角布局`：在 `ReplayPage.prefab` 图表子树内维护标题栏黑白视角 Toggle、摘要、图例、深色游标和横竖屏布局，并维护竖屏顶部操作与试下选子组的位置，通过既有 Binder 导出工具维护引用；保存前恢复 Canvas 根结构，可重复执行。全量重排剩余页面也复用此布局。
- `自定义功能/UI/验证复盘图表视角`：以编辑态合成数据检查正负目差与黑白胜率摘要、均势与缺失值、两条曲线的镜像位置、横竖屏 mesh、原始数据/游标保持不变和竖屏试下选子组与棋盘/功能操作行无重叠，输出 `Temp/WeiqiXN/ThemePreview/replay_chart_validation.txt`。
- `自定义功能/UI/验证剩余页面 V3.3`：检查上述资源的 Binder、编辑器节点引用、状态目标类型与 Canvas 组件，输出 `Temp/WeiqiXN/ThemePreview/remaining_validation.txt`。根 RectTransform 序列化值需另查 prefab diff，避免受预览驱动值误导。
- `自定义功能/Editor/验证复盘落子音效`：在编辑态构造临时复盘局面，覆盖主线/试下单步前进、手动落子、AI 变化、后退/跳转/虚手/失败静音和着盘延迟、单提多提优先级；输出 `Temp/WeiqiXN/replay_audio_validation.txt`，验证结束销毁临时对象并恢复音频静态状态，不修改实际场景。
- `自定义功能/UI/生成剩余页面状态预览`：输出设置模式、账号、列表内容/错误、好友申请、确认按钮组合、输入、数子、复盘与终局的横竖屏合成数据截图；仅修改临时实例，Regular 缺字在预览实例切换 Medium，不清空文案。
- `自定义功能/场景/生成剩余页面棋盘合成预览`：编辑态生成真实棋盘与复盘/终局 UI 的横竖屏组合图到 `Temp/WeiqiXN/LookPreview/`，复盘包含黑方与白方视角变体，并额外输出竖屏试下和自由布局；要求当前场景无未保存改动，完成后重新打开原场景丢弃临时对象。
- UI 重建菜单按工具模板重写 prefab，可能覆盖随后在 Inspector 保存的自适应配置。重建主菜单、对局页、对局设置或复盘布局后，需按 03 号文档复核 CanvasScaler 的 Expand 模式、主菜单标题宽度、落子确认按钮底边和复盘 Portrait 底部锚点；通过多比例验证后再保存，不能把旧模板默认值当作当前布局事实。

KataGo 运行时（行为细节以 [SPECIFICATION.md](../SPECIFICATION.md) 为准）：

- 启动与后端：
  - Loading 阶段调用 `KataGoBootstrap.Start()`，退出时调用 `Stop()`，读取根目录 `game-config.json` 按平台选后端。
  - Windows Editor 用仓库根 `KataGo/`，PC 包用 `<BuildRoot>/KataGo/`。
  - `exe` 后端经 Win32 pipe 优先启动 OpenCL，失败时回退 Eigen AVX2。`native` 后端加载 bridge DLL。
  - 两种后端都跑 9/13/19 路 smoke query，日志输出 `ownershipLength`。
  - 根目录不可写时提示，并改用 no-write 配置。
- 调度与 Android：
  - 并发由 `katago.analysis.maxConcurrentNativeRequests` 控制，请求按优先级出队，可按 owner 清理未派发的请求。
  - Android OpenCL 用 tuning 标记识别上次调优失败，天玑类 SoC 直接跳过 OpenCL。
- Human SL 是第二个模型，由 `katago.model.humanSlFileName` 配置。
- 查询与记录：
  - `AnalyzeOwnershipAsync` 返回 `ownership`，超时会停掉后端，下次请求时自动重启。
  - `KataGoPositionJsonBuilder`：形势和数子按当前快照生成，AI 选点和记录文件用完整手顺。
  - `KataGoDuelRecordFile` 只用保存侧。

构建：

- `AssetBundleGenerator` 在 PC 与 WebGL 共用的构建路径里执行 `PackRuntimeAssetTable`，见 04 号文档。
- 构建前只清理当前平台的输出目录：Windows 清 `../Build/PC`，Android 清 `../Build/Android`，WebGL 清 `../WebGL`。
- Windows 打包：按 `katago.backend.windowsPlayer` 校验并复制运行文件，并复制根目录 `game-config.json`。
  - exe 包带完整 `KataGo/`。
  - native 包只带配置的 bridge、config 和两个模型，不完整的 `native-opencl` 跳过并警告。
  - 复制时排除日志、`KataGoData`、tuning 缓存、`Library`、`Temp`、`weiqixn_bridge_resolved_config.cfg` 和 `.meta`。
- Android 打包：
  - 校验 bridge 插件、配置和两个模型，模型写成 `StreamingAssets/KataGo/models` 下的 `.bytes`。
  - `OpenCLNativeLibrary.androidlib` 只声明可选的 `libOpenCL.so`，必须保留独立 package。
  - `AndroidOpenClLibraryGradlePostprocessor` 在 Gradle 工程生成后修正该 library 的 namespace 并关闭 BuildConfig，避免和 launcher 生成同名 `BuildConfig`，导致 release dex 合并失败。

## 设计观察

UI 工具链承担了重复代码生成工作，但要求 prefab 上的绑定信息和生成脚本保持同步。

## 风险和缺口

- Binder 文件是生成产物，重新生成会覆盖手改内容；业务逻辑写在 `Logic/Page` 或 `Logic/Widget`。
- UI 逻辑文件只在不存在时生成，之后需要手工维护。
- KataGo 文件缺失、启动失败、超时和协议解析失败都必须有明确日志。

## 后续建议

- 在 UI 模块补一份“新增页面流程”；新增页面前先走一遍 UI 生成流程，避免手工维护绑定字段。
