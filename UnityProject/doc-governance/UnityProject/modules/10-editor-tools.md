# 编辑器工具模块

## 主要文件

- `Assets/Scripts/Editor/UI/UICodeGenerator.cs`
- `Assets/Scripts/Editor/UI/CSCodeGenerator.cs`
- `Assets/Scripts/Editor/Inspector/UIBinderEditor_Inspector.cs`
- `Assets/Scripts/Editor/Inspector/UIBinderBase_Inspector.cs`
- `Assets/Scripts/Editor/Build/AssetBundleGenerator.cs`
- `Assets/Scripts/Editor/Build/BuildConfig.cs`
- `Assets/Scripts/Editor/TMPSprite/SpriteAtlasToTMPSpriteTool.cs`
- `Assets/Scripts/Editor/Inspector/TextureArrayWizard.cs`
- `Assets/Scripts/Game/KataGo/KataGoBootstrap.cs`
- `Assets/Scripts/Game/KataGo/KataGoDuelRecordFile.cs`
- `Assets/Scripts/Game/KataGo/KataGoPositionJsonBuilder.cs`

## 职责

编辑器工具模块负责提升内容生产效率，包括 UI Binder 代码生成、UI 逻辑脚手架、AssetBundle 生成、TMP sprite 转换和材质/贴图辅助工具。

## 当前进度

- `UICodeGenerator` 会根据 `UIBinderEditor` 节点导出 Binder 脚本。
- 如果 UI 逻辑文件不存在，生成器会创建基础 `UIPageWithBinder<T>` 或 `UIWidgetWithBinder<T>` 逻辑类。
- 生成脚本会自动刷新 AssetDatabase。
- 项目已有多份生成的 `*PageUI.cs` 和页面逻辑类。
- AssetBundle、TMP sprite、TextureArray 等编辑器工具文件已经存在。
- KataGo 流程当前目标是编辑器模式跑通本地子进程调用和 JSON 解析，不要求进入正式构建资源管线。
- `ClientMain` 按普通流程调用 `KataGoBootstrap.Start()` / `Stop()`；`KataGoBootstrap` 内部按平台解析引擎路径。Windows Unity Editor 会后台启动 Eigen AVX2 版 KataGo，加载本地模型，发送固定 smoke query，并在日志中输出第一版所需的 `ownershipLength`。
- `KataGoBootstrap.AnalyzeOwnershipAsync` 负责把当前对局 query 写入 KataGo stdin，读取匹配 request id 的最终结果，并只返回第一版形势按钮需要的 `ownership` 数组；超时、进程未运行和协议缺失会写入日志。分析超时会停止当前 KataGo 子进程，后续分析请求会先检查进程状态并尝试按已解析路径自动重启，避免一次超时后持续报 `process is not running`。双方目数面板由游戏侧根据同一个 `ownership` 阈值统计，低于阈值的中立或未明确控制点不计入双方，白方显示值会额外加上 query 中的 `komi`。
- `KataGoPositionJsonBuilder` 当前提供 `BuildOwnershipAnalysisJson` 默认入口，以及完整手顺和当前盘面快照两个显式 JSON 生成入口；正常对局直接维护 KataGo 标准 `moves`，合法落子记录点位、虚手记录 `pass`，第一版形势按钮使用默认入口优先走 `moves`，快照入口只用于调试或无手顺场景。电脑对局实时落子请求的访问次数由 `DuelAiSystem` 按难度配置和棋盘路数解析后传入 JSON 生成入口，JSON Builder 不再维护本地固定上限。本地 `RectCoordinates` 已按 KataGo 棋盘布局定义，点位输出不再维护额外坐标兼容转换层。
- `KataGoDuelRecordFile` 负责保存和读取对局棋盘记录文件。记录文件采用可直接提交给 KataGo analysis engine 的 JSON 结构，场景读档通过其中的 `moves` 回放恢复棋盘；`pass` 项会恢复到手顺但不会改变棋盘缓存。

## 设计观察

UI 工具链已经承担了重复代码生成工作，这对后续快速增加联机页面有帮助。现有自动生成 Binder 的方式要求 prefab 上的绑定信息和生成脚本保持同步。

## 风险和缺口

- 自动生成文件可能覆盖手写 Binder 修改，Binder 文件应视为生成产物。
- UI 逻辑文件只在不存在时生成，后续逻辑需要手工维护。
- 编辑器工具没有在文档中形成使用流程，新成员容易误改生成文件。
- KataGo 二进制、模型和配置路径在编辑器验证阶段位于 `ExternalTools/KataGo/`；缺失、启动失败、超时和协议解析失败都需要有明确日志。

## 后续建议

- 在 UI 模块补一份“新增页面流程”。
- 明确 `*UI.cs` Binder 文件为生成文件，业务逻辑写在 `Logic/Page` 或 `Logic/Widget`。
- 新增 KataGo 编辑器工具时，优先提供一次性 smoke test：选择或读取本地 `katago`、模型和 config 路径，发送固定 19 路测试局面，请求 `includeOwnership`，并输出 ownership 数组长度和错误原因；第一版不要把 `scoreLead`、胜率或最佳选点接到形势按钮。
- 联机页面新增前先跑一遍 UI 生成流程，避免手工维护绑定字段。
