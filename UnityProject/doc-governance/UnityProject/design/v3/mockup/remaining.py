from functools import lru_cache
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


DESIGN = Path(__file__).resolve().parent.parent
PROJECT = DESIGN.parents[3]
OUTPUT = DESIGN / "remaining"
FONTS = PROJECT / "Assets/UI/Font"
LOOK = PROJECT / "Temp/WeiqiXN/LookPreview"

INK = (31, 28, 25)
SECONDARY = (92, 86, 78)
FAINT = (148, 140, 128)
PAPER = (243, 239, 230)
RAISED = (251, 249, 244)
SUNKEN = (232, 226, 213)
ACCENT = (168, 67, 47)
TABLE = (53, 48, 42)
GREEN = (79, 107, 58)
HAIRLINE = (210, 204, 193)


@lru_cache(maxsize=None)
def font(style, size):
    names = {
        "regular": "SourceHanSansSC-Regular.otf",
        "medium": "SourceHanSansCN-Medium.otf",
        "serif": "SourceHanSerifCN-SemiBold.otf",
    }
    return ImageFont.truetype(FONTS / names[style], size)


class Canvas:
    def __init__(self, portrait, backdrop):
        self.portrait = portrait
        self.width, self.height = (720, 1280) if portrait else (1600, 900)
        self.image = Image.open(backdrop).convert("RGB").resize((self.width, self.height), Image.Resampling.LANCZOS)
        self.draw = ImageDraw.Draw(self.image)

    def rect(self, left, top, width, height, color, radius=0, alpha=255, shadow=False):
        left, top, width, height = map(lambda value: int(round(value)), (left, top, width, height))
        if shadow:
            shadow_image = Image.new("RGBA", (width + 48, height + 48))
            ImageDraw.Draw(shadow_image).rounded_rectangle((24, 28, width + 23, height + 27), radius, fill=(0, 0, 0, 90))
            shadow_image = shadow_image.filter(ImageFilter.GaussianBlur(17))
            self.image.paste(shadow_image, (left - 24, top - 24), shadow_image)
        patch = Image.new("RGBA", (width, height))
        ImageDraw.Draw(patch).rounded_rectangle((0, 0, width - 1, height - 1), radius, fill=(*color, alpha))
        self.image.paste(patch, (left, top), patch)
        self.draw = ImageDraw.Draw(self.image)

    def line(self, left, top, right, bottom, color=HAIRLINE, width=1):
        self.draw.line((left, top, right, bottom), fill=color, width=width)

    def text(self, left, baseline, value, style="regular", size=18, color=INK, align="left", spacing=0):
        face = font(style, size)
        text_width = sum(face.getlength(character) for character in value) + max(0, len(value) - 1) * spacing
        if align == "right":
            left -= text_width
        elif align == "center":
            left -= text_width / 2
        for character in value:
            self.draw.text((int(left), int(baseline)), character, font=face, fill=color, anchor="ls")
            left += face.getlength(character) + spacing
        return text_width

    def circle(self, center_x, center_y, radius, color):
        self.draw.ellipse((center_x - radius, center_y - radius, center_x + radius, center_y + radius), fill=color)

    def paper(self, left, top, width, height, radius=6, alpha=245):
        self.rect(left, top, width, height, PAPER, radius, alpha, shadow=True)

    def header(self, left, top, width, kicker, title, subtitle=None, dark=False):
        main = PAPER if dark else INK
        muted = (179, 172, 161) if dark else FAINT
        self.text(left, top + 19, kicker, "medium", 13, muted, spacing=3)
        self.text(left, top + 69, title, "serif", 36 if not self.portrait else 34, main)
        if subtitle:
            self.text(left, top + 99, subtitle, "regular", 15, muted)
        self.line(left, top + 116, left + width, top + 116, (103, 96, 87) if dark else HAIRLINE)

    def button(self, left, top, width, height, label, kind="primary"):
        if kind == "primary":
            self.rect(left, top, width, height, INK, 4)
            color = PAPER
        elif kind == "danger":
            self.rect(left, top, width, height, ACCENT, 4)
            color = PAPER
        elif kind == "subtle":
            self.rect(left, top, width, height, SUNKEN, 4)
            color = INK
        else:
            self.rect(left, top, width, height, PAPER, 4)
            self.line(left, top, left + width, top, HAIRLINE)
            self.line(left, top + height, left + width, top + height, HAIRLINE)
            color = INK
        self.text(left + width / 2, top + height / 2 + 6, label, "medium", 17, color, align="center")

    def row(self, left, top, width, title, detail, right="", height=76, accent=False):
        if accent:
            self.rect(left, top + 7, 3, height - 14, ACCENT)
        self.text(left + 20, top + 31, title, "medium", 19, INK)
        self.text(left + 20, top + 56, detail, "regular", 13, SECONDARY)
        if right:
            self.text(left + width - 18, top + 42, right, "regular", 14, ACCENT if accent else FAINT, align="right")
        self.line(left, top + height, left + width, top + height)

    def field(self, left, top, width, label, value, enabled=True):
        self.text(left, top + 14, label, "medium", 13, FAINT, spacing=2)
        self.rect(left, top + 27, width, 48, SUNKEN, 4)
        self.text(left + 16, top + 58, value, "regular", 17, INK if enabled else FAINT)
        if enabled:
            self.line(left + width - 31, top + 48, left + width - 24, top + 55, SECONDARY, 2)
            self.line(left + width - 24, top + 55, left + width - 17, top + 48, SECONDARY, 2)

    def save(self, name):
        OUTPUT.mkdir(exist_ok=True)
        path = OUTPUT / f"{name}_{'portrait' if self.portrait else 'landscape'}.jpg"
        self.image.save(path, quality=94, subsampling=0)
        return path


def menu_canvas(portrait):
    canvas = Canvas(portrait, DESIGN / f"main_{'portrait' if portrait else 'landscape'}.jpg")
    canvas.rect(0, 0, canvas.width, canvas.height, (14, 12, 10), alpha=112)
    return canvas


def board_canvas(portrait):
    backdrop = LOOK / f"board19_{'portrait' if portrait else 'landscape_left'}.png"
    if not backdrop.exists():
        backdrop = DESIGN / f"duel_{'portrait' if portrait else 'landscape'}_rest.jpg"
    return Canvas(portrait, backdrop)


def frame(canvas, kicker, title, subtitle, top=None):
    if canvas.portrait:
        canvas.rect(0, 122 if top is None else top, 720, 1158 if top is None else 1280 - top, PAPER, 18)
        left, width, start = 42, 636, 168 if top is None else top + 46
    else:
        canvas.paper(230, 54, 1140, 792)
        left, width, start = 286, 1028, 102
    canvas.header(left, start, width, kicker, title, subtitle)
    canvas.text(left + width, start + 20, "×", "regular", 25, SECONDARY, align="right")
    return left, width, start + 140


def setup_landscape(canvas, ogs):
    canvas.paper(190, 43, 1220, 814)
    canvas.rect(190, 43, 398, 814, (24, 21, 18), 5, 247)
    canvas.text(226, 104, "OGS · 自动匹配" if ogs else "新游戏 · 电脑对局", "medium", 13, (168, 158, 145), spacing=3)
    canvas.text(226, 174, "落子之前", "serif", 40, PAPER)
    canvas.text(226, 209, "先定好这一局。", "regular", 16, (185, 177, 166))
    canvas.line(226, 231, 552, 231, (89, 80, 70))
    backdrop = LOOK / "board19_landscape_left.png"
    if not backdrop.exists():
        backdrop = DESIGN / "duel_landscape_rest.jpg"
    board = Image.open(backdrop).convert("RGB").crop((42, 42, 858, 858)).resize((322, 322), Image.Resampling.LANCZOS)
    canvas.image.paste(board, (226, 257))
    canvas.draw = ImageDraw.Draw(canvas.image)
    canvas.text(226, 640, "当前方案", "medium", 13, (168, 158, 145), spacing=3)
    canvas.text(226, 679, "十九路  ·  猜先  ·  分先", "serif", 22, PAPER)
    canvas.text(226, 709, "10 分钟 + 读秒" if ogs else "电脑水平 20K–18K", "regular", 14, (185, 177, 166))
    canvas.text(226, 808, "每种模式分别保存设置", "regular", 13, (142, 133, 122))

    left, width, top = 636, 718, 91
    canvas.header(left, top, width, "对局 · 参数", "对局设置", "选定棋盘与用时后开始对局")
    canvas.text(left + width, top + 20, "×", "regular", 25, SECONDARY, align="right")
    canvas.text(left, 252, "棋盘", "medium", 13, FAINT, spacing=3)
    for index, label in enumerate(("九路", "十三路", "十九路")):
        canvas.button(left + index * 245, 269, 228, 56, label, "primary" if index == 2 else "subtle")
    canvas.line(left, 353, left + width, 353)
    if ogs:
        canvas.field(left, 377, width, "匹配用时", "10 分钟 · 读秒 3 次")
        canvas.field(left, 474, 344, "执子", "猜先")
        canvas.field(left + 374, 474, 344, "让子", "分先", False)
        canvas.text(left, 633, "将按棋盘与用时寻找对手，分先局不开放让子。", "regular", 15, SECONDARY)
    else:
        canvas.field(left, 377, 344, "持有时间", "10 分钟")
        canvas.field(left + 374, 377, 344, "读秒次数", "3 次")
        canvas.field(left, 474, 344, "读秒时间", "30 秒")
        canvas.field(left + 374, 474, 344, "电脑水平", "20K–18K")
        canvas.field(left, 571, 344, "执子", "猜先")
        canvas.field(left + 374, 571, 344, "让子", "分先")
    canvas.line(left, 724, left + width, 724)
    canvas.text(left, 788, "这些选择仅影响本局", "regular", 14, FAINT)
    canvas.button(left + width - 218, 751, 218, 58, "开始匹配" if ogs else "开始对局")
    return canvas.save("setup_ogs" if ogs else "setup_ai")


def setup(portrait, ogs=False):
    canvas = menu_canvas(portrait)
    if not portrait:
        return setup_landscape(canvas, ogs)
    canvas.rect(0, 0, 720, 1280, PAPER)
    left, width, start = 42, 636, 38
    kicker = "OGS · 自动匹配" if ogs else "新游戏 · 电脑对局"
    canvas.header(left, start, width, kicker, "对局设置", "选定棋盘与用时后开始对局")
    canvas.text(left + width, start + 20, "×", "regular", 25, SECONDARY, align="right")
    section = start + 144
    canvas.text(left, section + 20, "棋盘", "medium", 14, FAINT, spacing=3)
    for index, label in enumerate(("九路", "十三路", "十九路")):
        canvas.button(left + index * 214, section + 34, 200, 58, label, "primary" if index == 2 else "subtle")
    canvas.field(left, section + 122, 636, "匹配用时" if ogs else "持有时间", "10 分钟 · 读秒 3 次" if ogs else "10 分钟")
    if not ogs:
        canvas.field(left, section + 219, 304, "读秒次数", "3 次")
        canvas.field(left + 332, section + 219, 304, "读秒时间", "30 秒")
    canvas.line(left, section + 330, left + width, section + 330)
    canvas.field(left, section + 362, 304, "执子", "猜先")
    canvas.field(left + 332, section + 362, 304, "让子", "分先", not ogs)
    if not ogs:
        canvas.field(left, section + 458, 636, "电脑水平", "20K–18K")
    canvas.rect(left, 808, width, 187, RAISED, 5)
    canvas.text(left + 24, 849, "当前方案", "serif", 23, INK)
    canvas.line(left + 24, 866, left + width - 24, 866)
    canvas.text(left + 24, 906, "十九路  ·  猜先  ·  分先", "medium", 17, INK)
    canvas.text(left + 24, 944, "OGS 自动匹配  ·  10 分钟 + 读秒" if ogs else "电脑对局  ·  10 分钟 + 读秒  ·  20K–18K", "regular", 15, SECONDARY)
    canvas.text(left, 1110, "设置将分别保存到该对局模式", "regular", 14, FAINT)
    canvas.button(left, 1154, width, 62, "开始匹配" if ogs else "开始对局")
    return canvas.save("setup_ogs" if ogs else "setup_ai")


def confirm(portrait):
    canvas = board_canvas(portrait)
    canvas.rect(0, 0, canvas.width, canvas.height, (14, 12, 10), alpha=124)
    if portrait:
        canvas.paper(0, 704, 720, 576, 18)
        left, width, top = 40, 640, 748
        canvas.header(left, top, width, "对局 · 请求", "对方请求悔棋", "白方希望撤回上一手")
        canvas.text(left, top + 166, "接受后，棋局将回到第 56 手。", "regular", 19, INK)
        canvas.text(left, top + 208, "此请求由对方发起，选择后立即生效。", "regular", 15, SECONDARY)
        canvas.button(left, 1160, 310, 62, "拒绝", "subtle")
        canvas.button(left + 330, 1160, 310, 62, "同意悔棋")
    else:
        canvas.paper(500, 226, 600, 448)
        left, width, top = 540, 520, 270
        canvas.header(left, top, width, "对局 · 请求", "对方请求悔棋", "白方希望撤回上一手")
        canvas.text(left, top + 166, "接受后，棋局将回到第 56 手。", "regular", 18, INK)
        canvas.text(left, top + 204, "此请求由对方发起，选择后立即生效。", "regular", 14, SECONDARY)
        canvas.button(758, 578, 126, 54, "拒绝", "subtle")
        canvas.button(898, 578, 162, 54, "同意悔棋")
    return canvas.save("confirm_request")


def loading(portrait):
    canvas = board_canvas(portrait)
    canvas.rect(0, 0, canvas.width, canvas.height, TABLE, alpha=180)
    if portrait:
        left, right, top = 48, 672, 886
        canvas.text(left, top, "弈·悟", "serif", 48, PAPER)
        canvas.text(left, top + 60, "正在准备棋盘", "serif", 30, PAPER)
        canvas.text(left, top + 96, "正在加载棋具与对局数据…", "regular", 16, (183, 177, 167))
        canvas.text(right, top + 96, "72%", "serif", 26, PAPER, align="right")
        canvas.rect(left, top + 130, right - left, 2, (116, 107, 96))
        canvas.rect(left, top + 130, (right - left) * 0.72, 2, PAPER)
        canvas.text(left, top + 166, "落子之间，留一点呼吸。", "regular", 14, (155, 147, 137))
    else:
        left, right, top = 133, 680, 620
        canvas.text(left, top - 260, "弈·悟", "serif", 56, PAPER)
        canvas.text(left, top - 68, "正在准备棋盘", "serif", 35, PAPER)
        canvas.text(left, top - 30, "正在加载棋具与对局数据…", "regular", 16, (183, 177, 167))
        canvas.text(right, top - 30, "72%", "serif", 28, PAPER, align="right")
        canvas.rect(left, top, right - left, 2, (116, 107, 96))
        canvas.rect(left, top, (right - left) * 0.72, 2, PAPER)
        canvas.text(left, top + 35, "落子之间，留一点呼吸。", "regular", 14, (155, 147, 137))
    return canvas.save("loading")


def chart(canvas, left, top, width, height):
    canvas.rect(left, top, width, height, RAISED, 4)
    for fraction in (0.25, 0.5, 0.75):
        level = top + int(height * fraction)
        canvas.line(left + 28, level, left + width - 20, level, HAIRLINE)
    points = [0.52, 0.48, 0.59, 0.55, 0.61, 0.58, 0.66, 0.63, 0.72, 0.69, 0.75, 0.73]
    coordinates = []
    for index, value in enumerate(points):
        coordinates.append((left + 30 + index * (width - 62) / (len(points) - 1), top + height - 19 - value * (height - 37)))
    canvas.draw.line(coordinates, fill=(63, 96, 91), width=3, joint="curve")
    canvas.circle(*coordinates[-1], 5, ACCENT)


def replay(portrait):
    canvas = board_canvas(portrait)
    if portrait:
        canvas.rect(0, 0, 720, 272, TABLE, alpha=240)
        canvas.text(32, 47, "复盘 · 本地对局", "medium", 13, (165, 158, 147), spacing=3)
        canvas.text(32, 99, "第 57 手", "serif", 36, PAPER)
        canvas.text(688, 93, "退出", "medium", 16, PAPER, align="right")
        canvas.text(32, 146, "黑棋 棋手  ·  白棋 KataGo", "regular", 16, (191, 185, 175))
        canvas.text(32, 183, "黑 Q16 · 形势：黑领先 3.5 目", "regular", 15, (191, 185, 175))
        canvas.line(32, 209, 688, 209, (90, 84, 77))
        canvas.text(32, 246, "试下", "medium", 16, PAPER)
        canvas.text(179, 246, "形势", "medium", 16, PAPER)
        canvas.text(326, 246, "AI 分析", "medium", 16, PAPER)
        canvas.text(511, 246, "导出 SGF", "medium", 16, PAPER)
        canvas.paper(0, 1010, 720, 270, 18)
        chart(canvas, 32, 1043, 656, 104)
        canvas.text(38, 1179, "|<", "medium", 19, INK)
        canvas.text(169, 1179, "<", "medium", 23, INK)
        canvas.text(360, 1179, "57 / 132", "serif", 23, INK, align="center")
        canvas.text(555, 1179, ">", "medium", 23, INK)
        canvas.text(681, 1179, ">|", "medium", 19, INK, align="right")
        canvas.text(36, 1224, "胜率  56%     ·     目差  +3.5", "regular", 15, SECONDARY)
    else:
        left, width = 906, 652
        canvas.text(left, 72, "复盘 · 本地对局", "medium", 13, (174, 166, 156), spacing=3)
        canvas.text(left, 133, "第 57 手", "serif", 42, PAPER)
        canvas.text(left + width, 72, "退出", "medium", 16, PAPER, align="right")
        canvas.text(left, 170, "黑棋 棋手   ·   白棋 KataGo   ·   十九路", "regular", 15, (183, 176, 167))
        canvas.text(left, 202, "黑 Q16 · 形势：黑领先 3.5 目", "regular", 15, (191, 185, 175))
        canvas.line(left, 229, left + width, 229, (95, 88, 80))
        canvas.text(left, 269, "胜率与目差", "medium", 17, PAPER)
        chart(canvas, left, 290, width, 188)
        canvas.text(left, 520, "|<", "medium", 22, PAPER)
        canvas.text(left + 138, 520, "<", "medium", 25, PAPER)
        canvas.text(left + width / 2, 520, "57 / 132", "serif", 27, PAPER, align="center")
        canvas.text(left + 509, 520, ">", "medium", 25, PAPER)
        canvas.text(left + width, 520, ">|", "medium", 22, PAPER, align="right")
        canvas.line(left, 549, left + width, 549, (95, 88, 80))
        for index, (label, description) in enumerate((("试下", "从当前手开始试下"), ("形势", "显示归属与目数"), ("AI 分析", "推荐落点"), ("导出 SGF", "保存棋谱文件"))):
            top = 557 + index * 71
            canvas.text(left, top + 26, label, "medium", 18, PAPER)
            canvas.text(left + width, top + 26, description, "regular", 13, (152, 145, 136), align="right")
            canvas.line(left, top + 48, left + width, top + 48, (95, 88, 80))
    return canvas.save("replay")


def user_info(portrait):
    canvas = menu_canvas(portrait)
    left, width, top = frame(canvas, "棋手 · 资料", "我的资料", "本地棋手与 OGS 账号")
    if portrait:
        canvas.rect(left, top, width, 212, RAISED, 5)
        canvas.circle(left + 57, top + 66, 28, SUNKEN)
        canvas.text(left + 111, top + 65, "棋手", "serif", 27)
        canvas.text(left + 111, top + 91, "ID  001284", "regular", 13, SECONDARY)
        canvas.text(left + width - 24, top + 62, "修改名称", "medium", 15, ACCENT, align="right")
        canvas.text(left + 30, top + 153, "12 胜", "serif", 30)
        canvas.text(left + 220, top + 153, "8 负", "serif", 30)
        canvas.text(left + 30, top + 187, "仅统计本机完成的对局", "regular", 13, FAINT)
        second = top + 236
        canvas.rect(left, second, width, 270, RAISED, 5)
        canvas.text(left + 28, second + 38, "OGS 账号", "serif", 25)
        canvas.text(left + width - 28, second + 38, "已登录", "medium", 13, GREEN, align="right")
        canvas.text(left + 28, second + 78, "sample_player · 3 段", "medium", 19)
        canvas.text(left + 28, second + 109, "OGS ID  238461", "regular", 14, SECONDARY)
        canvas.line(left + 28, second + 131, left + width - 28, second + 131)
        canvas.text(left + 28, second + 166, "十九路  3 段", "regular", 15, INK)
        canvas.text(left + 322, second + 166, "十三路  2 段", "regular", 15, INK)
        canvas.text(left + 28, second + 211, "好友列表", "medium", 17)
        canvas.text(left + width - 28, second + 211, "查看  →", "regular", 15, SECONDARY, align="right")
        canvas.text(left + 28, second + 248, "刷新资料     ·     退出登录", "regular", 13, FAINT)
        canvas.row(left, second + 293, width, "最近对局", "查看本机棋谱与导入记录", "查看  →", 86)
    else:
        card_width = 487
        canvas.rect(left, top, card_width, 383, RAISED, 5)
        canvas.circle(left + 58, top + 62, 27, SUNKEN)
        canvas.text(left + 108, top + 68, "棋手", "serif", 28)
        canvas.text(left + 108, top + 96, "ID  001284", "regular", 13, SECONDARY)
        canvas.text(left + card_width - 24, top + 68, "修改名称", "medium", 14, ACCENT, align="right")
        canvas.line(left + 28, top + 135, left + card_width - 28, top + 135)
        canvas.text(left + 30, top + 199, "12", "serif", 50)
        canvas.text(left + 30, top + 227, "胜", "regular", 14, FAINT)
        canvas.text(left + 253, top + 199, "8", "serif", 50)
        canvas.text(left + 253, top + 227, "负", "regular", 14, FAINT)
        canvas.text(left + 30, top + 335, "仅统计本机完成的对局", "regular", 13, FAINT)
        other = left + 511
        canvas.rect(other, top, 515, 383, RAISED, 5)
        canvas.text(other + 28, top + 51, "OGS 账号", "serif", 27)
        canvas.text(other + 487, top + 51, "已登录", "medium", 13, GREEN, align="right")
        canvas.text(other + 28, top + 99, "sample_player · 3 段", "medium", 20)
        canvas.text(other + 28, top + 127, "OGS ID  238461", "regular", 13, SECONDARY)
        canvas.line(other + 28, top + 157, other + 487, top + 157)
        canvas.text(other + 28, top + 197, "十九路  3 段      十三路  2 段", "regular", 15)
        canvas.row(other + 22, top + 222, 471, "好友列表", "查看好友和申请", "查看  →", 76)
        canvas.text(other + 28, top + 343, "刷新资料     ·     退出登录", "regular", 13, FAINT)
        canvas.row(left, top + 418, width, "最近对局", "查看本机棋谱与导入记录", "查看  →", 90)
    return canvas.save("user_info")


def lan_rooms(portrait):
    canvas = menu_canvas(portrait)
    left, width, top = frame(canvas, "局域网 · 同一网络", "寻找对局", "加入附近房间，或创建一间新房")
    if portrait:
        canvas.rect(left, top, width, 174, (24, 21, 18), 5)
        canvas.text(left + 25, top + 42, "开一间房", "serif", 25, PAPER)
        canvas.text(left + 25, top + 75, "邀请同一网络内的棋友加入", "regular", 15, (186, 177, 165))
        canvas.button(left + 25, top + 104, width - 50, 52, "设置并创建房间", "subtle")
        section = top + 225
        canvas.text(left, section, "附近房间", "serif", 25, INK)
        canvas.text(left + width, section, "重新搜索", "medium", 15, ACCENT, align="right")
        canvas.text(left, section + 34, "同一 Wi-Fi 下发现 2 间房", "regular", 14, FAINT)
        items = (("周末手谈", "192.168.1.23  ·  十九路  ·  分先", "1 / 2  加入 →"), ("对弈练习", "192.168.1.45  ·  九路  ·  无限时", "1 / 2  加入 →"))
        for index, (name, meta, action) in enumerate(items):
            canvas.row(left, section + 60 + index * 122, width, name, meta, action, 112, accent=index == 0)
        canvas.text(left, 1187, "仅显示当前局域网内可加入的房间", "regular", 14, FAINT)
    else:
        canvas.rect(left, top, 320, 516, (24, 21, 18), 5)
        canvas.text(left + 27, top + 50, "开一间房", "serif", 29, PAPER)
        canvas.text(left + 27, top + 88, "邀请同一网络内的棋友加入", "regular", 15, (186, 177, 165))
        canvas.line(left + 27, top + 118, left + 293, top + 118, (95, 86, 76))
        canvas.text(left + 27, top + 182, "选择棋盘、用时与执子", "regular", 16, PAPER)
        canvas.text(left + 27, top + 214, "创建后等待另一位棋手加入", "regular", 14, (167, 159, 148))
        canvas.button(left + 27, top + 431, 266, 58, "设置并创建房间", "subtle")
        section = left + 362
        canvas.text(section, top + 29, "附近房间", "serif", 27, INK)
        canvas.text(section + 666, top + 29, "重新搜索", "medium", 15, ACCENT, align="right")
        canvas.text(section, top + 65, "同一 Wi-Fi 下发现 2 间房", "regular", 14, FAINT)
        items = (("周末手谈", "192.168.1.23  ·  十九路  ·  分先", "1 / 2  加入 →"), ("对弈练习", "192.168.1.45  ·  九路  ·  无限时", "1 / 2  加入 →"))
        for index, (name, meta, action) in enumerate(items):
            canvas.row(section, top + 91 + index * 119, 666, name, meta, action, 109, accent=index == 0)
        canvas.text(section, top + 487, "仅显示当前局域网内可加入的房间", "regular", 13, FAINT)
    return canvas.save("lan_rooms")


def list_page(portrait, page_type, requests=False):
    canvas = menu_canvas(portrait)
    if page_type == "friends":
        kicker, title, subtitle = "OGS · 社交", "好友申请" if requests else "好友列表", "待处理的好友申请" if requests else "一起下棋的朋友"
    else:
        kicker, title, subtitle = "棋谱 · 本机归档", "最近对局", "已保存的棋局与导入棋谱"
    left, width, top = frame(canvas, kicker, title, subtitle)
    if page_type == "friends":
        canvas.button(left, top + 2, 140 if not portrait else 195, 47, "好友列表" if requests else "添加好友", "subtle")
        canvas.button(left + (155 if not portrait else 210), top + 2, 160 if not portrait else 195, 47, "好友列表" if requests else "好友申请  1", "primary" if requests else "subtle")
        canvas.text(left + width, top + (83 if portrait else 31), "刷新", "medium", 15, SECONDARY, align="right")
        items = (("山雨", "OGS · 3 段 · 在线", "同意   拒绝"),) if requests else (("山雨", "OGS · 3 段 · 在线", "查看  →"), ("静水", "OGS · 1 段 · 对局中", "查看  →"), ("一叶", "OGS · 2 级 · 离线", "查看  →"))
    else:
        canvas.button(left, top + 2, 164 if not portrait else 300, 49, "自由摆谱", "subtle")
        canvas.button(left + (180 if not portrait else 318), top + 2, 164 if not portrait else 300, 49, "导入 SGF", "subtle")
        canvas.text(left + width, top + (83 if portrait else 31), "刷新", "medium", 15, SECONDARY, align="right")
        items = (("棋手 对 KataGo", "十九路 · 132 手 · 今天 14:32", "黑胜 3.5 目 →"), ("周末手谈", "十三路 · 86 手 · 昨天 20:10", "白胜 2.5 目 →"), ("练习棋谱", "九路 · 47 手 · 9 月 26 日", "未结束 →"))
    row_top = top + (126 if portrait else 83)
    for index, (name, meta, action) in enumerate(items):
        canvas.row(left, row_top + index * (108 if portrait else 108), width, name, meta, action, 102, accent=index == 0)
    bottom = 1192 if portrait else 763
    canvas.text(left, bottom, "1 / 1", "regular", 15, FAINT)
    canvas.text(left + width, bottom, "上一页      下一页", "medium", 15, SECONDARY, align="right")
    name = {"friends": "friend_requests" if requests else "friend_list", "recent": "recent_replays"}[page_type]
    return canvas.save(name)


def friend_profile(portrait):
    canvas = menu_canvas(portrait)
    left, width, top = frame(canvas, "OGS · 好友资料", "山雨", "在线 · OGS 3 段")
    canvas.circle(left + 54, top + 54, 42, SUNKEN)
    canvas.text(left + 54, top + 63, "山", "serif", 24, INK, align="center")
    canvas.text(left + 122, top + 48, "山雨", "serif", 29)
    canvas.text(left + 122, top + 80, "OGS ID  238461  ·  在线", "regular", 15, SECONDARY)
    canvas.line(left, top + 116, left + width, top + 116)
    if portrait:
        canvas.text(left, top + 157, "综合段级", "regular", 15, FAINT)
        canvas.text(left + width, top + 157, "3 段", "serif", 21, INK, align="right")
        canvas.text(left, top + 211, "十九路", "regular", 15, FAINT)
        canvas.text(left + width, top + 211, "3 段", "serif", 21, INK, align="right")
        canvas.text(left, top + 265, "十三路 / 九路", "regular", 15, FAINT)
        canvas.text(left + width, top + 265, "2 段 / 1 段", "serif", 21, INK, align="right")
        canvas.line(left, top + 303, left + width, top + 303)
        canvas.text(left, top + 346, "地区  中国   ·   注册于 2024 年", "regular", 15, SECONDARY)
        canvas.text(left, top + 385, "简介  喜欢慢棋，也欢迎约棋。", "regular", 15, SECONDARY)
        canvas.text(left, top + 433, "资料来自 OGS，部分字段可能为空", "regular", 13, FAINT)
        canvas.button(left, 1117, width, 59, "邀请对局")
        canvas.text(left + width / 2, 1222, "删除好友", "regular", 15, ACCENT, align="center")
    else:
        for index, (label, value) in enumerate((("综合段级", "3 段"), ("十九路", "3 段"), ("十三路", "2 段"), ("九路", "1 段"))):
            column = left + index * 253
            canvas.text(column, top + 164, label, "regular", 14, FAINT)
            canvas.text(column, top + 212, value, "serif", 30)
        canvas.line(left, top + 244, left + width, top + 244)
        canvas.text(left, top + 291, "地区  中国     ·     注册于 2024 年", "regular", 15, SECONDARY)
        canvas.text(left, top + 328, "简介  喜欢慢棋，也欢迎约棋。", "regular", 15, SECONDARY)
        canvas.text(left, top + 369, "资料来自 OGS，部分字段可能为空", "regular", 13, FAINT)
        canvas.text(left, 773, "删除好友", "regular", 15, ACCENT)
        canvas.button(left + width - 202, 751, 202, 57, "邀请对局")
    return canvas.save("friend_profile")


def duel_end_portrait():
    canvas = board_canvas(True)
    canvas.rect(0, 0, 720, 283, TABLE, alpha=235)
    canvas.text(24, 91, "电脑对局 · 十九路 · 贴 6.5 目", "medium", 13, (182, 173, 163), spacing=2)
    canvas.text(696, 91, "第 57 手", "serif", 22, PAPER, align="right")
    canvas.rect(14, 138, 692, 136, PAPER, 6, 230)
    canvas.circle(54, 174, 9, (220, 218, 212))
    canvas.text(72, 181, "白方", "medium", 13, FAINT, spacing=2)
    canvas.text(39, 224, "AI", "serif", 26)
    canvas.text(39, 253, "电脑 · KataGo", "regular", 13, SECONDARY)
    canvas.text(680, 224, "21:07", "serif", 48, FAINT, align="right")
    canvas.rect(0, 987, 720, 293, TABLE, alpha=244)
    canvas.rect(14, 999, 692, 130, (18, 16, 14), 6, 235)
    canvas.rect(14, 1015, 3, 96, ACCENT)
    canvas.circle(54, 1039, 9, (25, 23, 21))
    canvas.text(72, 1046, "黑方", "medium", 13, (175, 165, 153), spacing=2)
    canvas.text(39, 1094, "棋手", "serif", 26, PAPER)
    canvas.text(680, 1094, "18:42", "serif", 48, PAPER, align="right")
    canvas.rect(0, 0, 720, 1280, (14, 12, 10), alpha=105)
    canvas.rect(0, 682, 720, 598, PAPER, 18)
    canvas.header(40, 727, 640, "终局 · 电脑对局", "棋手 胜出", "黑领先 3.5 目")
    canvas.rect(596, 747, 48, 48, ACCENT, 3)
    canvas.text(620, 780, "胜", "serif", 25, PAPER, align="center")
    canvas.text(40, 917, "双方连续虚手后数子", "regular", 18, INK)
    canvas.text(40, 958, "第 57 手  ·  十九路  ·  贴 6.5 目", "regular", 14, FAINT)
    canvas.button(40, 1156, 640, 62, "退出对局")
    return canvas.save("duel_end")


def overview(paths, portrait):
    labels = {
        "setup_ai": "电脑对局设置", "setup_ogs": "OGS 匹配设置", "confirm_request": "通用确认",
        "loading": "加载", "replay": "复盘", "user_info": "个人资料", "friend_list": "OGS 好友",
        "friend_requests": "好友申请", "friend_profile": "好友资料", "lan_rooms": "局域网房间",
        "recent_replays": "最近对局", "duel_end": "竖屏终局",
    }
    tile_width, tile_height = (304, 575) if portrait else (506, 320)
    columns = 4 if portrait else 3
    rows = (len(paths) + columns - 1) // columns
    image = Image.new("RGB", (columns * tile_width + 40, rows * tile_height + 60), PAPER)
    draw = ImageDraw.Draw(image)
    draw.text((24, 32), "弈·悟  /  V3 未覆盖界面样张  /  待确认", font=font("serif", 25), fill=INK, anchor="ls")
    for index, path in enumerate(paths):
        left = 20 + index % columns * tile_width
        top = 55 + index // columns * tile_height
        source = Image.open(path).convert("RGB")
        image_width = 272 if portrait else 466
        image_height = 485 if portrait else 262
        source.thumbnail((image_width, image_height), Image.Resampling.LANCZOS)
        image.paste(source, (left + (tile_width - source.width) // 2, top))
        draw.text((left + tile_width / 2, top + image_height + 29), labels[path.stem.rsplit("_", 1)[0]], font=font("medium", 17), fill=INK, anchor="ms")
    path = OUTPUT / ("overview_portrait.jpg" if portrait else "overview_landscape.jpg")
    image.save(path, quality=91)
    return path


def main():
    generated = []
    for portrait in (False, True):
        current = [
            setup(portrait), setup(portrait, True), confirm(portrait), loading(portrait),
            replay(portrait), user_info(portrait), list_page(portrait, "friends"),
            list_page(portrait, "friends", True), friend_profile(portrait),
            lan_rooms(portrait), list_page(portrait, "recent"),
        ]
        if portrait:
            current.append(duel_end_portrait())
        generated.extend(current)
        generated.append(overview(current, portrait))
    for path in generated:
        print(path)


if __name__ == "__main__":
    main()
