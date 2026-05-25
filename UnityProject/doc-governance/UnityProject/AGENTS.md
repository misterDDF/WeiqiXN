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
- 修复 bug 前必须确认该问题已登记在 [KNOWN_BUGS.md](KNOWN_BUGS.md)；一轮任务包含多个 bug 时，先完整登记所有已观察问题，再按条修复，避免会话中断后丢失定位上下文。
- 只要触发文档更新矩阵中的任一条件，必须在同一轮改动中同步更新对应权威文档；不能把必要文档更新留到后续任务。
- 若判断不需要更新文档，交付说明中必须明确说明未触发文档更新的原因。
- 若当前验证发现实现与文档不一致，应优先修正实现或文档中的事实偏差，再继续编译、测试或交付。
- 不为普通实现细节、临时调试过程或未落地设想更新长期文档；文档只记录稳定事实、边界和约束。

## Compile Validation

**编译验证**

- 修改 Unity C# 代码、asmdef、Editor 脚本或会影响 Unity 脚本导入的资源后，必须触发 Unity Editor 脚本编译作为最小验证。
- 编译验证优先使用已连接的 Unity MCP 触发脚本重编译，并检查 Unity Console 编译错误；当前通用入口为 MCP `recompile_scripts`，可用时不再使用 `dotnet build UnityProject.sln` 作为常规验证。
- Unity MCP 触发重编译、Domain Reload 等编辑器操作时，执行、等待和读取日志应拆成多次调用。若 MCP 因 Domain Reload 短暂断开，应重试轻量检查。
- `dotnet build UnityProject.sln` 不能作为 Unity 编译的权威验证；除非 Unity Editor 或 MCP 不可用且需要辅助定位纯 C# 编译问题，否则不再执行。
- 若 Unity Editor 或 MCP 不可用，必须在交付说明中明确说明未执行 Unity 编译，并记录剩余风险。

## Editor Operation Policy

**编辑器操作策略**

- 能通过 Unity MCP 完成的编辑器操作优先使用 MCP，包括脚本重编译、Console 日志读取、场景对象查询与修改、材质和资源的常规编辑器操作。
- prefab、场景和资源导入相关修改应优先走 Unity 编辑器能力；当现有 MCP 工具能覆盖目标 prefab 或资源操作时使用 MCP。
- 当前 MCP 未提供完整既有 prefab asset 层级编辑能力时，不直接手写复杂 prefab YAML；应改用 Unity 编辑器脚本、明确的编辑器菜单或人工维护 prefab，再通过 MCP 执行导入、编译和日志检查。
- 固定 UI 控件仍必须由 prefab 或场景显式维护并通过现有 Binder 绑定；MCP 只是优先编辑入口，不改变 UI 维护边界。
- 修改既有 UI prefab 时，页面根节点、Canvas、CanvasScaler、GraphicRaycaster、主面板根节点、布局容器和棋盘承载节点默认视为受保护布局节点。除非任务明确要求调整这些节点，否则不得修改其 `RectTransform`、`Canvas`、`CanvasScaler`、`LayoutGroup` 或 `ContentSizeFitter` 配置。
- 所有 `Assets/UI/Prefab/Page/*.prefab` 的页面根节点必须使用全屏 Stretch 布局，根 `RectTransform` 固定为 `anchorMin=(0,0)`、`anchorMax=(1,1)`、`anchoredPosition=(0,0)`、`sizeDelta=(0,0)`、`scale=(1,1,1)`，以保证多分辨率和多平台自适应；除非任务明确是修复页面根节点适配，否则不得把页面根节点改回居中固定锚点或固定尺寸。
- `MainMenuPage.prefab` 的主菜单按钮栈和其布局容器默认视为受保护布局节点，尤其是 `panel_buttons` 一类承载既有按钮排列的节点；除非任务明确要求调整主菜单布局，否则不得修改这些节点的 `RectTransform`、`LayoutGroup`、`ContentSizeFitter`、锚点、尺寸、间距或父子关系。
- UI prefab 修改完成后必须检查 prefab diff，确认改动只落在目标控件、目标子树和 Binder 引用上；若发现受保护布局节点出现非预期变化，必须先恢复这些变化再继续编译或交付。
- 使用 Unity 编辑器脚本或 MCP 辅助修改 prefab 时，脚本必须按稳定路径定位目标父节点和模板节点，不得对页面根节点执行缩放、锚点、父子关系或自动布局重排操作，除非当前任务就是修复这些布局属性。

## Done Definition

**完成定义**

- 代码或文档修改已经落地。
- 必要检查已经完成；如无法执行，需要记录原因。
- 涉及 Unity C# 代码变更时，已触发 Unity Editor 编译并确认无编译错误；无法执行时已说明原因和剩余风险。
- 已判断对本地对局基线的影响。
- 已判断是否触发四件套文档或模块文档更新。
- 被触发的权威文档已经同步更新。
- 未触发文档更新时，交付说明已说明原因。
