# WeiqiXN UnityProject

## Project Positioning

**项目定位**

- WeiqiXN UnityProject 是一个基于 Unity 的围棋游戏项目。
- 当前项目以本地对局为基础能力：棋盘选择、棋盘生成、回合流转、鼠标落子、提子规则、自杀禁手、简单防重复局面、UI、资源加载和存档链路已经具备基础形态。
- 项目的后续产品方向是联机对局；当前代码库尚未实现网络层、房间、匹配、同步协议或断线恢复。

## Governance Profile

**治理画像**

- Governance Profile ID: stage-driven
- Governance Profile Name: 按阶段推进
- Governance Profile Summary: 阶段内连续推进，阶段结束后统一收口。

## Document Routing

**文档路由**

- 当前系统事实以 [SPECIFICATION.md](SPECIFICATION.md) 为准。
- 架构边界、设计理由和长期约束以 [ARCHITECTURE.md](ARCHITECTURE.md) 为准。
- 阶段计划、当前目标和范围控制以 [ROADMAP.md](ROADMAP.md) 为准。
- 当前已知未修复 bug 记录在 [KNOWN_BUGS.md](KNOWN_BUGS.md)；用户报告或执行过程中确认的新 bug 必须先登记到该文档，再开始逐个修复；修复并验证后移除对应条目，未能完成验证时保留条目并更新状态。
- 模块扫描说明放在 [modules/00-progress-check.md](modules/00-progress-check.md) 及同目录的模块文档中。
- 本文件只定义入口、执行约束、文档路由和维护规则，不承载详细系统事实。

## Reading Strategy

**读取策略**

- 默认先读本文件判断执行边界，不默认全量读取四件套和 `modules/`。
- 涉及当前系统行为时读取 [SPECIFICATION.md](SPECIFICATION.md)。
- 涉及架构边界、职责划分或长期约束时读取 [ARCHITECTURE.md](ARCHITECTURE.md)。
- 涉及阶段目标、范围控制或后续路线时读取 [ROADMAP.md](ROADMAP.md)。
- `modules/` 下文档只按任务涉及模块定向读取；跨模块改动才读取多个模块文档。

## Execution Boundaries

**执行边界**

- `AGENTS.md`、`SPECIFICATION.md`、`ARCHITECTURE.md`、`ROADMAP.md` 是四个核心权威文档。
- `modules/` 目录是支撑说明层，可以解释模块细节，但不能覆盖四个核心文档中的权威结论。
- 生成物、临时状态、样例、日志、Unity 构建产物、`Library/`、`Temp/`、`Logs/`、IDE 元数据不属于文档权威层。
- 当前治理方式是按阶段推进：阶段内可以连续整理或实现，阶段结束后统一收口。
- 联机功能必须视为阶段性架构变更。新增网络框架、协议形态、服务器权威模型或同步模型时，必须同步更新 [ARCHITECTURE.md](ARCHITECTURE.md)、[ROADMAP.md](ROADMAP.md) 和 [modules/11-online-readiness.md](modules/11-online-readiness.md)。
- 在准备联机功能时，本地对局仍然是回归基线，除非某个阶段明确声明要替换本地流程。
- 修改棋规、落子、提子、回合、存档、场景切换或 UI 输入时，需要同时判断逻辑行为和可见流程是否受到影响。

## Agent Coding Rules

**Agent 编码规范**

- 代码结构必须简洁明确，优先沿用当前 Unity 项目的场景、组件、系统、事件和资源加载边界。
- 不做过度设计，不为了未来可能出现的需求提前增加抽象层、适配层或包装层。
- 不做过度封装；只有当封装能减少真实复杂度、消除重复、稳定边界或提升可读性时才新增公共方法、类或模块。
- 同一逻辑出现超过 3 行重复代码时，必须进行必要的封装复用；封装后仍应保持调用关系直观。
- 修改必须聚焦当前任务范围，不做无关重构、无关格式化或无关资源整理。
- 修改 Unity C# 代码前必须先通过可用 Unity Editor/MCP 状态检查确认 Editor 不在 Play 模式；优先通过 MCP 执行 `自定义功能/Editor/Write Editor State`，再读取 `UnityProject/Temp/WeiqiXN/editor_state_probe.json`，以文件中的最新 `timeUtc` / `sequence`、`isPlaying`、`isPlayingOrWillChangePlaymode` 和 `isCompiling` 为准，不依赖 Console 日志。若正在 Play，或无法可靠判断 Play 状态且本轮需要改 C#，必须停止代码改动并等待用户关闭 Play 后再继续。不得在 Play 模式中直接编辑 C# 文件，避免触发 Domain Reload、脚本导入或 Editor 崩溃；除非用户明确要求，不由 agent 主动切换/停止 Play。C# 文件写入后应立即触发必要的 `Assets/Refresh` 和脚本重编译验证；若代码写入后先进入文档维护、等待、用户交互、长时间排查或其他明显间隔，再触发 Unity 导入/编译前必须重新执行文件探针，若用户已进入 Play，则停止 Unity 导入/编译验证并说明剩余风险。
- 不为小范围需求引入新的 Unity 包、插件、全局服务或编辑器工具；确实需要时必须说明现有能力为什么不足。
- 不把 UI、网络、KataGo、本地规则、存档或资源加载职责混入彼此边界；需要跨边界协作时优先使用已有事件、系统入口或文档已定义的模块职责。
- UI 类负责展示和输入转发，不直接承担棋规、存档、网络同步或 AI 分析决策。
- 固定 UI 控件必须由 prefab 或场景显式维护并通过现有 Binder 绑定；业务代码不得在运行时临时创建按钮、文本等固定交互控件。数据驱动列表、临时特效或对象池内容确需动态生成时，应使用 prefab 模板并保持生成边界清晰。
- System 类负责业务行为，Component 保存场景状态和固定引用，二者职责不要互相替代。
- Unity 运行时日志避免在逐帧路径、热循环或批量处理路径中无条件输出。
- 在 `Update`、输入处理、棋盘遍历、资源加载等热路径中避免不必要的分配、反射、查找和日志输出。
- 资源查找、组件查找和配置读取应优先缓存到已有生命周期边界内。
- 新功能不得破坏本地对局基线；涉及落子、提子、回合、存档、场景切换或 UI 输入时，必须说明回归影响。
- 联机、AI、编辑器工具等能力应复用或旁路接入现有稳定流程，不直接替换本地规则权威。
- 新增实现应服务当前阶段目标；超出 [ROADMAP.md](ROADMAP.md) 当前阶段范围的能力，需要先更新路线或明确记录为后续事项。

## Document Update Matrix

**文档更新矩阵**

- 当前行为发生变化 -> 更新 [SPECIFICATION.md](SPECIFICATION.md)。
- 架构边界、职责划分或设计约束发生变化 -> 更新 [ARCHITECTURE.md](ARCHITECTURE.md)。
- 阶段目标、待办、范围或联机路线发生变化 -> 更新 [ROADMAP.md](ROADMAP.md)。
- 某个模块的入口文件、完成度、风险或维护说明发生变化 -> 更新 `modules/` 下对应模块文档。
- 文档路由、执行约束或治理方式发生变化 -> 更新本文件。

## Documentation Sync Gate

**文档同步门禁**

- 修改代码后必须先判断改动是否改变当前行为、架构边界、阶段计划、入口约束或模块维护事实，再进入最终交付。
- 涉及 Unity C# 改动时，除已知 bug 登记等必须前置的记录外，应在 C# 文件写入后立即完成 `Assets/Refresh`、脚本重编译和 Console 检查，再进行较长的文档同步；如果代码写入后没有立即验证而先发生文档维护、等待或其他明显间隔，则触发 Unity 导入/编译前必须重新探针，避免文档维护期间用户进入 Play 后继续触发 Unity 导入或 Domain Reload。
- 修复 bug 前必须确认该问题已登记在 [KNOWN_BUGS.md](KNOWN_BUGS.md)；一轮任务包含多个 bug 时，先完整登记所有已观察问题，再按条修复，避免会话中断后丢失定位上下文。
- 只要触发文档更新矩阵中的任一条件，必须在同一轮改动中同步更新对应权威文档；不能把必要文档更新留到后续任务。
- 若判断不需要更新文档，交付说明中必须明确说明未触发文档更新的原因。
- 若当前验证发现实现与文档不一致，应优先修正实现或文档中的事实偏差，再继续编译、测试或交付。
- 不为普通实现细节、临时调试过程或未落地设想更新长期文档；文档只记录稳定事实、边界和约束。

## Compile Validation

**编译验证**

- 修改 Unity C# 代码、asmdef、Editor 脚本或会影响 Unity 脚本导入的资源后，必须触发 Unity Editor 脚本编译作为最小验证。
- 修改 Unity C# 代码、新增或修改 Editor 菜单函数、修改 asmdef、prefab、scene、material、asset 或其他会被 Unity 导入/序列化的资源后，必须先通过 Unity MCP 执行 `Assets/Refresh` 菜单项或等效 Editor 刷新入口，让 Unity Editor 实际感知改动；不能只停留在文件系统 diff。若改动涉及 C#、asmdef 或 Editor 菜单函数，刷新后还必须执行脚本编译验证并检查 Console。C# 写入后立即执行刷新和重编译时，改 C# 前的文件探针可覆盖这次连续验证；若代码写入和刷新/重编译之间出现文档维护、等待、用户交互、长时间排查或其他明显间隔，则必须重新写入并读取 Editor 状态探针，确认 `isPlaying=false`、`isPlayingOrWillChangePlaymode=false`、`isCompiling=false` 后才允许继续。
- 编译验证优先使用已连接的 Unity MCP 触发脚本重编译，并检查 Unity Console 编译错误；当前通用入口为 MCP `recompile_scripts`，可用时不再使用 `dotnet build UnityProject.sln` 作为常规验证。
- Unity MCP 触发重编译、Domain Reload 等编辑器操作时，执行、等待和读取日志应拆成多次调用。若 MCP 因 Domain Reload 短暂断开，应重试轻量检查。
- `dotnet build UnityProject.sln` 不能作为 Unity 编译的权威验证；除非 Unity Editor 或 MCP 不可用且需要辅助定位纯 C# 编译问题，否则不再执行。
- 若 Unity Editor 或 MCP 不可用，必须在交付说明中明确说明未执行 Unity 编译，并记录剩余风险。

## Editor Operation Policy

**编辑器操作策略**

- 能通过 Unity MCP 完成的编辑器操作优先使用 MCP，包括脚本重编译、Console 日志读取、场景对象查询与修改、材质和资源的常规编辑器操作。
- 涉及 Unity C# 文件改动的任务，在任何文件编辑前必须先完成 Play 模式状态检查；优先通过 MCP 执行 `自定义功能/Editor/Write Editor State`，再读取 `UnityProject/Temp/WeiqiXN/editor_state_probe.json`，以文件中的最新 `timeUtc` / `sequence`、`isPlaying`、`isPlayingOrWillChangePlaymode` 和 `isCompiling` 为准，不依赖 Console 日志。发现 Editor 正在 Play 时，不执行 `apply_patch`、脚本生成、格式化或其他会改写 C# 文件的操作，先告知用户需要退出 Play。C# 写入完成后应立即执行必要 Unity 导入/编译验证，再进入较长文档同步；若写入后未立即验证而发生文档维护、等待、用户交互或其他明显间隔，则在导入、重编译或任何可能触发 Domain Reload 的 Editor 操作前必须重新探针确认非 Play。
- prefab、场景和资源导入相关修改应优先走 Unity 编辑器能力；当现有 MCP 工具能覆盖目标 prefab 或资源操作时使用 MCP。
- 任何 C#、Editor 菜单、asmdef、prefab、scene、material 或 asset 改动完成后，交付前必须至少通过 Unity MCP 执行一次 `Assets/Refresh` 菜单项或等效 Editor 刷新入口；若涉及 C#、asmdef 或 Editor 菜单函数，还必须执行脚本编译验证并检查 Console 错误。C# 写入后立即连续执行刷新/重编译时，不需要在中间额外补探针；若改动完成和刷新/重编译之间存在明显间隔，则执行前必须重新读取 Editor 状态探针，确认非 Play、非即将 Play、非编译。
- 当前 MCP 未提供完整既有 prefab asset 层级编辑能力时，不直接手写复杂 prefab YAML；应改用 Unity 编辑器脚本、明确的编辑器菜单或人工维护 prefab，再通过 MCP 执行导入、编译和日志检查。
- 固定 UI 控件仍必须由 prefab 或场景显式维护并通过现有 Binder 绑定；MCP 只是优先编辑入口，不改变 UI 维护边界。
- `Assets/UI/Prefab/Page/*.prefab` 的 prefab 根节点必须作为 Unity Canvas 根节点维护，根节点上的 `RectTransform`、`Canvas`、`CanvasScaler` 和 `GraphicRaycaster` 一律视为受保护编辑器结构，不得为了界面铺满或横竖屏适配修改根 `RectTransform`。已验证可正常预览的 Page prefab 根节点通常是 Unity Canvas 编辑态零尺寸结构（如 `localScale=(0,0,0)`、`anchorMin=(0,0)`、`anchorMax=(0,0)`、`anchoredPosition=(0,0)`、`sizeDelta=(0,0)`、`pivot=(0,0)`）并使用 `Screen Space - Camera`；不要把根节点改成四向 Stretch 或 `Screen Space - Overlay` 来做页面布局，否则容易诱发 Unity Prefab 预览创建 `Canvas (environment)`。
- 页面或弹窗的全屏铺满布局必须放在 Canvas 根节点下的 `PanelRoot` 或既有等价内容根节点上维护；铺满内容根的 `RectTransform` 使用四向 Stretch（`anchorMin=(0,0)`、`anchorMax=(1,1)`、`anchoredPosition=(0,0)`、`sizeDelta=(0,0)`、`scale=(1,1,1)`）承载页面内容。新增 Page prefab 若缺少该节点，应先补 `PanelRoot`，再在其下维护主面板、布局容器和状态节点；既有 prefab 若还没有 `PanelRoot`，除非任务明确要求结构迁移，否则不要顺手重排整棵 UI 树。
- 修改既有 UI prefab 时，Canvas 根节点、Canvas、CanvasScaler、GraphicRaycaster、`PanelRoot`、主面板根节点、布局容器和棋盘承载节点默认视为受保护布局节点。除非任务明确要求调整这些节点，否则不得修改其 `RectTransform`、`Canvas`、`CanvasScaler`、`LayoutGroup` 或 `ContentSizeFitter` 配置。
- `MainMenuPage.prefab` 的主菜单按钮栈和其布局容器默认视为受保护布局节点，尤其是 `panel_buttons` 一类承载既有按钮排列的节点；除非任务明确要求调整主菜单布局，否则不得修改这些节点的 `RectTransform`、`LayoutGroup`、`ContentSizeFitter`、锚点、尺寸、间距或父子关系。
- UI prefab 修改完成后必须检查 prefab diff，确认改动只落在目标控件、目标子树和 Binder 引用上；若发现受保护布局节点出现非预期变化，必须先恢复这些变化再继续编译或交付。
- 使用 Unity 编辑器脚本或 MCP 辅助修改 prefab 时，脚本必须按稳定路径定位目标父节点和模板节点，不得对 Canvas 根节点执行缩放、锚点、父子关系或自动布局重排操作；需要铺满页面时只允许修改或创建根下 `PanelRoot`，除非当前任务就是修复 Canvas prefab 结构。
- 使用 `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` 或等效编辑器脚本修改 `Assets/UI/Prefab/Page/*.prefab` 前，必须先把 Canvas 根节点、`PanelRoot`、主面板根节点和任务相关受保护布局节点的关键序列化值纳入保护清单；保存前显式恢复这些值，尤其是 Page prefab 根 `RectTransform` 的 `localScale`、`pivot`、锚点、`anchoredPosition`、`sizeDelta` 和根 `Canvas.renderMode`。Unity 保存 prefab 时可能会归一化根 `RectTransform` 或写入预览态 Canvas 配置，即使脚本没有主动修改根节点，也必须主动防护。
- 编辑器脚本只新增静态控件或图例时，应只在目标父节点下创建目标子节点，不得调用会影响祖先布局的自动对齐、布局重建、父子重排或 Canvas 预览修正逻辑。脚本运行后必须用 prefab diff 定向确认：除新增目标节点、目标父节点 `m_Children` 和必要 Binder 引用外，Canvas 根节点、`PanelRoot`、主面板根节点、布局容器和棋盘承载节点没有 `RectTransform` / `Canvas` / `CanvasScaler` / `LayoutGroup` / `ContentSizeFitter` 变化。
- 完全静态的 UI 说明、图例、固定标签和固定按钮文案优先由 prefab 显式维护；只有运行时数据、状态进度、当前局面摘要或本地化动态文本才由页面逻辑刷新。不要为了避免改 prefab 而把静态图例混入运行时数据文本。

## Done Definition

**完成定义**

- 代码或文档修改已经落地。
- 必要检查已经完成；如无法执行，需要记录原因。
- 涉及 Unity C#、Editor 菜单、asmdef、prefab、scene、material 或 asset 变更时，已通过 Unity MCP 执行 `Assets/Refresh` 菜单项或等效 Editor 刷新入口；涉及 C#、asmdef 或 Editor 菜单函数时，已完成脚本编译验证并确认 Console 无相关错误。C# 写入后立即连续验证时，以改 C# 前的状态探针作为安全前置；若写入和验证之间存在明显间隔，则已在导入/编译前重新通过 Editor 状态文件探针确认非 Play、非即将 Play、非编译；无法执行时已说明原因和剩余风险。
- 已判断对本地对局基线的影响。
- 已判断是否触发四件套文档或模块文档更新。
- 被触发的权威文档已经同步更新。
- 未触发文档更新时，交付说明已说明原因。
