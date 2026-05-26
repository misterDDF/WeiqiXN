# 启动与全局服务模块

## 主要文件

- `Assets/Scripts/ClientMain.cs`
- `Assets/Scripts/Global/Global.cs`
- `Assets/Scripts/Global/GlobalConfig.cs`
- `Assets/Scripts/GlobalModule/ModuleBase.cs`
- `Assets/Scripts/Global/Logger/XNLogger.cs`

## 职责

启动模块负责把 Unity 生命周期接入项目自定义逻辑层。`ClientMain` 通过 `RuntimeInitializeOnLoadMethod` 在首个场景加载前启动项目，并在 `SubsystemRegistration` 阶段注册自定义 PlayerLoop。

`Global` 是跨场景服务容器，负责创建和维护全局模块：

- `EventManager`
- `ResourceManager`
- `TimerManager`
- `GameSaveManager`
- `ReddotManager`
- `UIManager`
- `SceneManager`

## 当前进度

- 已有单例入口。
- 已接管 `Update`、`FixedUpdate`、`LateUpdate` 的集中派发。
- 已在开发环境加载 IngameDebugConsole。
- 已在启动后进入主菜单场景。
- 已在退出时逆序销毁模块并清理用户数据。
- Android 启动期和对局期会在前台临时保持屏幕常亮，退出加载、场景切换、失焦、暂停或退出时恢复系统睡眠设置。

## 设计观察

当前结构把逻辑更新集中到模块层，减少散落在 `MonoBehaviour` 上的业务逻辑。这个方向适合后续做联机，因为网络同步、定时器、事件和状态机可以统一挂在逻辑层，而不是分散在表现对象上。

## 风险和缺口

- `ModuleBase` 构造函数直接调用 `Init()`，子类字段初始化和虚方法调用顺序需要保持谨慎。
- `Global` 模块顺序已经成为隐式依赖，例如 UI 初始化依赖资源加载和事件系统。
- 联机模块加入时，应明确放在 `ResourceManager`、`TimerManager`、`SceneManager` 之间的哪一层，避免全局服务互相硬依赖。

## 后续建议

- 在接入网络前新增 `NetworkManager` 设计文档和初始化顺序说明。
- 对 `Global.Start()` 的模块顺序增加注释或文档约束。
- 若后续出现更多全局服务，考虑把模块注册顺序拆成显式列表，降低隐式依赖风险。
