# 当前进度检查

## 扫描范围

- `Assets/Scripts/Game`
- `Assets/Scripts/Global`
- `Assets/Scripts/GlobalModule`
- `Assets/Scripts/Editor`
- `Assets/Config/DataJson`
- `Assets/Config/DataType`
- `Packages/manifest.json`

## 总体判断

项目已经超过原型空壳阶段，具备本地热座对局的核心闭环：启动、主菜单、对局场景、棋盘选择、棋盘生成、落子、提子、回合切换、回合计时、保存入口和恢复钩子都已经存在。

项目还没有进入联机实现阶段。当前未发现网络依赖、网络传输层、房间/匹配、联机协议、同步模型、服务端权威模型或断线恢复逻辑。

## 已完成度较高的部分

- 启动与全局模块生命周期：`ClientMain` 和 `Global` 已形成统一入口。
- 场景切换：`SceneManager`、`SceneBase`、`MainMenuScene`、`DuelScene` 已形成主场景进入、加载、退出流程。
- UI 框架：`UIManager`、`UIContext`、`UIPage`、`UIWidget`、UI Binder 代码生成已成型。
- 资源加载：编辑器下走 `AssetDatabaseLoader`，非编辑器下预加载 AssetBundle 并走 `AssetBundleLoader`。
- 配置读取：场景、UI、棋盘、预制体、TMP sprite 都有 JSON 配置和对应 data type。
- 本地对局：两名本地玩家、回合 FSM、30 秒回合计时、点击落子、回合结束切换。
- 棋盘与规则：支持 `9x9`、`13x13`、`19x19`，有提子、自杀禁手和简单防重复局面判断。
- 存档：基于 `SavableObj` 的 JSON 保存/读取链路已存在。
- 编辑器工具：UI 代码生成、AssetBundle 生成、TMP Sprite 工具、Inspector 辅助工具存在。

## 半完成或需要补齐的部分

- 非法落子目前只有逻辑回退，`ChessBoardSystem` 中多个 `TODO show message` 还没有用户反馈。
- 鼠标输入在 `DuelPage` 中直接读取，标注了 `TODO input manager`。
- `SceneBase` 加载时有 `TODO update load progress`，LoadingPage 尚未展示真实进度。
- 对局结束规则仍不完整：已有虚手、请求数子、连续虚手终局、认输和基础终局结果 UI 原型，但仍没有死子确认和线上裁定模型。
- 保存已有触发入口，但用户可见的读取入口不明确。
- 本地回归验证还没有自动化测试。
- 联机前需要明确规则最小集，否则同步协议会被 pass/resign/scoring 的缺口反复打断。

## 明确未开始的部分

- 网络传输层。
- 房间、匹配、邀请、准备、重连。
- 联机移动命令协议。
- 服务端权威或客户端锁步模型。
- 联机状态同步、快照、重放、观战。
- 账号、排行榜、战绩。

## 下一步建议

1. 先把本地对局基线收口：非法提示、pass/resign 或明确暂不做、终局边界、保存读取入口、手动验证清单。
2. 把落子合法性从 `ChessBoardSystem` 中整理成可复用的规则入口，至少让联机层能提交“落子命令”并得到确定结果。
3. 决定联机架构：服务端权威优先，还是先做点对点/房间内锁步原型。
4. 在接入任何联网 SDK 前，先写最小协议：加入房间、开始对局、提交落子、确认落子、同步棋盘、超时、退出。
