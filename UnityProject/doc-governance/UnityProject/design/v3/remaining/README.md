# V3 补缺界面样张（已确认并实装）

延续 `v3` 已确认的主菜单和对局方案：真实棋盘与棋子、深色桌面、半透明和纸、宋体标题、细线、留白，以及只用于重要状态的朱色。用户已确认本组视觉方向，页面与状态已实装；本目录保留设计参考图，不锁定最终像素。列表中的名字、棋局和进度是展示用数据。

交互和布局按新方案设计，不受旧 prefab 的控件位置限制。对局设置横屏改为左侧棋盘预览与实时摘要、右侧参数；竖屏把摘要放在主要操作之前。局域网页把“创建房间”和“加入附近房间”作为两条清楚的路径。落地时保留各模式的参数、状态、权限和结果功能，列表分页等表现方式可以随页面重做。

先看 [横屏总览](overview_landscape.jpg) 和 [竖屏总览](overview_portrait.jpg)，再按页面查看原尺寸样图：

| 页面 / 状态 | 横屏 | 竖屏 |
|---|---|---|
| 电脑对局设置 | [查看](setup_ai_landscape.jpg) | [查看](setup_ai_portrait.jpg) |
| OGS 自动匹配设置 | [查看](setup_ogs_landscape.jpg) | [查看](setup_ogs_portrait.jpg) |
| 通用确认（以对方请求悔棋为例） | [查看](confirm_request_landscape.jpg) | [查看](confirm_request_portrait.jpg) |
| 加载页 | [查看](loading_landscape.jpg) | [查看](loading_portrait.jpg) |
| 复盘页 | [查看](replay_landscape.jpg) | [查看](replay_portrait.jpg) |
| 我的资料 | [查看](user_info_landscape.jpg) | [查看](user_info_portrait.jpg) |
| OGS 好友列表 | [查看](friend_list_landscape.jpg) | [查看](friend_list_portrait.jpg) |
| OGS 好友申请 | [查看](friend_requests_landscape.jpg) | [查看](friend_requests_portrait.jpg) |
| OGS 好友资料 | [查看](friend_profile_landscape.jpg) | [查看](friend_profile_portrait.jpg) |
| 局域网房间 | [查看](lan_rooms_landscape.jpg) | [查看](lan_rooms_portrait.jpg) |
| 最近对局 | [查看](recent_replays_landscape.jpg) | [查看](recent_replays_portrait.jpg) |
| 对局竖屏终局 | 已有 `duel_landscape_end.jpg` | [查看](duel_end_portrait.jpg) |

好友、房间与最近对局的行样式同时作为 `OgsFriendItemWidget`、`LanRoomItemWidget`、`ReplayArchiveItemWidget` 的样张。通用确认的输入、提示、等待和数子结果沿用同一纸页或底部抽屉体系；数子结果已有 `duel_*_dialog.jpg` 参考。棋盘选路、用时、执子和让子在不同对局模式下继续遵守当前可用条件，图中只展示代表性组合。

实装状态与验收范围见 `../../../modules/14-visual-polish-plan.md`。当前 Unity 编辑态截图由“生成剩余页面状态预览”与“生成剩余页面棋盘合成预览”菜单输出到 `Temp/WeiqiXN/ThemePreview` 与 `Temp/WeiqiXN/LookPreview`，实际交互仍待 Play/真机验收。设计图生成脚本为 `../mockup/remaining.py`；有编辑器棋盘预览图时使用它，缺少预览图时回退到已有对局样张。
