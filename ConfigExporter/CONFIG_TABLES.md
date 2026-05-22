# 配置表说明

本文档简略说明当前配置表的大概功能。源表位于 `xlsx/`，导出后的运行时 JSON 位于 `DataJson/`，对应 C# 读取类位于 `DataType/`。

| 配置表 | 主要功能 |
| --- | --- |
| `chess_board` | 定义棋盘规格和棋盘相关表现参数，当前包含 `9x9`、`13x13`、`19x19`。 |
| `duel_hold_time` | 定义对局保留时间选项，例如 2 分钟、5 分钟、无限时间等。 |
| `duel_byoyomi_count` | 定义读秒次数选项，包含关闭读秒和若干读秒次数。 |
| `duel_byoyomi_time` | 定义单次读秒时长选项。 |
| `duel_handicap` | 定义不同棋盘规格下的分先、让先、让子选项，包含贴目和预摆星位坐标。 |
| `duel_ai_difficulty` | 定义电脑对局难度、KataGo 分析参数、不同棋盘尺寸的实时预算和动态预算阈值。 |
| `game_prefab` | 定义运行时可按配置 id 加载的游戏预制体路径。 |
| `runtime_asset` | 定义代码运行时直接加载、但不一定被场景或 prefab 引用的资源，供运行时加载和 AssetBundle 打包使用。 |
| `scene` | 定义项目场景入口和场景类型到 Unity scene 资源的映射。 |
| `tmp_sprite` | 定义 TextMesh Pro sprite 字符和 sprite asset 相关配置。 |
| `ui_page` | 定义 UI 页面、弹窗、prefab 路径和 UI 上下文信息。 |
| `lan_room_config` | 定义局域网房间运行参数，包括 UDP 广播端口、TCP 监听端口、连接超时、房间人数上限、广播间隔和读写缓冲区大小；协议字符串由代码枚举约定生成，不放入配表。 |
| `message` | 定义运行时 UI 展示文案，包括通用弹窗按钮、Loading 状态、局域网房间状态、对局 HUD 和操作反馈文案。 |

新增或修改配置表时，应先修改 `xlsx/` 下的源表，再通过 `main.py` 导出 JSON 和 C# 数据类型，不直接手改 `DataJson/` 或 `DataType/` 下的生成物。
