# V3 方案稿 D：宣纸半透明（信息）+ 无框排版（操作）融合，横竖屏各一套布局。
# 真实字体、令牌色值，叠在真实棋盘截图上；2 倍超采样后缩回参考分辨率。方向稿，不代表最终像素。
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = 'F:/WorkSpace/WeiqiXN/UnityProject/'
LOOK = ROOT + 'Temp/WeiqiXN/LookPreview/'
OUT = ROOT + 'Temp/WeiqiXN/ThemePreview/concept/'
S = 2
FONTS = {
    'regular': ROOT + 'Assets/UI/Font/SourceHanSansSC-Regular.otf',
    'medium': ROOT + 'Assets/UI/Font/SourceHanSansCN-Medium.otf',
    'serif': ROOT + 'Assets/UI/Font/SourceHanSerifCN-SemiBold.otf',
}
DESK = (38, 34, 31)
PAPER = (243, 239, 230)
INK = (31, 28, 25)
INK2 = (92, 86, 78)
INK3 = (140, 132, 122)
ACCENT = (168, 67, 47)
WHITE = (255, 255, 255)
_font_cache = {}


def font(kind, size):
    key = (kind, size)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(FONTS[kind], int(round(size * S)))
    return _font_cache[key]


def a(color, alpha):
    return color[:3] + (int(round(alpha * 255)),)


class Canvas:
    def __init__(self, w, h, backdrop=None):
        self.w, self.h = w, h
        if backdrop:
            self.img = Image.open(backdrop).convert('RGBA').resize((w * S, h * S), Image.LANCZOS)
        else:
            self.img = Image.new('RGBA', (w * S, h * S), DESK + (255,))

    def _layer(self):
        return Image.new('RGBA', self.img.size, (0, 0, 0, 0))

    def _comp(self, layer):
        self.img = Image.alpha_composite(self.img, layer)

    def paste(self, path, x, y, w, h):
        image = Image.open(path).convert('RGBA').resize((int(w * S), int(h * S)), Image.LANCZOS)
        layer = self._layer()
        layer.paste(image, (int(x * S), int(y * S)), image)
        self._comp(layer)

    def rect(self, x, y, w, h, fill=None, radius=0, outline=None, shadow=None):
        box = [x * S, y * S, (x + w) * S - 1, (y + h) * S - 1]
        if shadow:
            alpha, blur, dy = shadow
            layer = self._layer()
            ImageDraw.Draw(layer).rounded_rectangle([box[0], box[1] + dy * S, box[2], box[3] + dy * S], radius * S, fill=(0, 0, 0, int(alpha * 255)))
            self._comp(layer.filter(ImageFilter.GaussianBlur(blur * S)))
        if fill:
            layer = self._layer()
            ImageDraw.Draw(layer).rounded_rectangle(box, radius * S, fill=fill)
            self._comp(layer)
        if outline:
            layer = self._layer()
            ImageDraw.Draw(layer).rounded_rectangle(box, radius * S, outline=outline, width=S)
            self._comp(layer)

    def circle(self, cx, cy, r, fill=None, outline=None, width=1.0):
        layer = self._layer()
        ImageDraw.Draw(layer).ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=fill, outline=outline, width=max(1, int(round(width * S))))
        self._comp(layer)

    def line(self, x0, y0, x1, y1, color, width=1.0):
        layer = self._layer()
        ImageDraw.Draw(layer).line([x0 * S, y0 * S, x1 * S, y1 * S], fill=color, width=max(1, int(round(width * S))))
        self._comp(layer)

    def measure(self, text, kind, size, spacing=0.0):
        f = font(kind, size)
        return sum(f.getlength(ch) for ch in text) / S + spacing * max(0, len(text) - 1)

    # y 为基线；align 为 left / right / center。
    def text(self, x, y, text, kind, size, color, spacing=0.0, align='left'):
        f = font(kind, size)
        width = self.measure(text, kind, size, spacing)
        if align == 'right':
            x -= width
        elif align == 'center':
            x -= width / 2
        layer = self._layer()
        d = ImageDraw.Draw(layer)
        cx = x * S
        for ch in text:
            d.text((cx, y * S), ch, font=f, fill=color, anchor='ls')
            cx += f.getlength(ch) + spacing * S
        self._comp(layer)
        return width

    def stone(self, cx, cy, r, white, alpha=1.0):
        size = int(r * 2 * S) + 8
        layer = Image.new('RGBA', (size, size), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        outer, inner = ((188, 183, 174), (252, 250, 246)) if white else ((14, 13, 12), (78, 76, 73))
        steps = 32
        for i in range(steps):
            t = i / (steps - 1)
            rr = r * S * (1 - t * 0.8)
            ox = oy = -r * S * 0.32 * t
            c = tuple(int(outer[k] + (inner[k] - outer[k]) * t ** 1.6) for k in range(3)) + (255,)
            cxl, cyl = size / 2 + ox, size / 2 + oy
            d.ellipse([cxl - rr, cyl - rr, cxl + rr, cyl + rr], fill=c)
        if alpha < 1.0:
            layer.putalpha(layer.getchannel('A').point(lambda v: int(v * alpha)))
        full = self._layer()
        full.alpha_composite(layer, (int(cx * S - size / 2), int(cy * S - size / 2)))
        self._comp(full)

    def icon(self, kind, cx, cy, color, width=1.6):
        layer = self._layer()
        d = ImageDraw.Draw(layer)
        w = max(1, int(round(width * S)))

        def p(x, y):
            return (cx + x) * S, (cy + y) * S

        if kind == 'analysis':
            d.ellipse([*p(-8, -8), *p(4, 4)], outline=color, width=w)
            d.line([*p(2.5, 2.5), *p(8, 8)], fill=color, width=w)
        elif kind == 'ownership':
            for i, (ox, oy) in enumerate(((-7, -7), (1, -7), (-7, 1), (1, 1))):
                if i in (0, 3):
                    d.rectangle([*p(ox, oy), *p(ox + 6, oy + 6)], fill=color)
                else:
                    d.rectangle([*p(ox, oy), *p(ox + 6, oy + 6)], outline=color, width=w)
        elif kind == 'pass':
            d.ellipse([*p(-7.5, -7.5), *p(7.5, 7.5)], outline=color, width=w)
            d.line([*p(-4, 4), *p(4, -4)], fill=color, width=w)
        elif kind == 'menu':
            for oy in (-5, 0, 5):
                d.line([*p(-7, oy), *p(7, oy)], fill=color, width=w)
        elif kind == 'close':
            d.line([*p(-6, -6), *p(6, 6)], fill=color, width=w)
            d.line([*p(-6, 6), *p(6, -6)], fill=color, width=w)
        elif kind in ('up', 'down', 'left', 'right', 'next'):
            pts = {
                'up': ((-6, 3), (0, -3), (6, 3)),
                'down': ((-6, -3), (0, 3), (6, -3)),
                'left': ((3, -6), (-3, 0), (3, 6)),
                'right': ((-3, -6), (3, 0), (-3, 6)),
                'next': ((-2.5, -5), (2.5, 0), (-2.5, 5)),
            }[kind]
            d.line([p(*pt) for pt in pts], fill=color, width=w, joint='curve')
        elif kind == 'user':
            d.ellipse([*p(-4, -8), *p(4, 0)], outline=color, width=w)
            d.arc([*p(-8, 2), *p(8, 16)], 180, 360, fill=color, width=w)
        self._comp(layer)

    def save(self, name):
        os.makedirs(OUT, exist_ok=True)
        path = OUT + name + '.png'
        self.img.resize((self.w, self.h), Image.LANCZOS).convert('RGB').save(path)
        return path


# 样例数据（电脑对局，人类执黑）。文案取自现有功能清单；“第 N 手”“贴目”表头与坐标读数是新增显示，数据现成。
BLACK = dict(white=False, label='黑方', name='棋手', sub='本机', time='18:42', byo='剩余读秒 3 次 · 读秒 00:30', active=True)
WHITE_P = dict(white=True, label='白方', name='AI', sub='电脑 · KataGo', time='21:07', byo='剩余读秒 3 次 · 读秒 00:30', active=False)
MODE = '电脑对局'
MOVE = '第 57 手'
BOARD = '十九路'
KOMI = '贴 6.5 目'
SCRIM = a((14, 12, 10), 0.45)
PANEL = a(PAPER, 0.9)
SHEET = a(PAPER, 0.95)
EDGE = a(WHITE, 0.35)
RULE_INK = a(INK, 0.12)
RULE_PAPER = a(PAPER, 0.14)


def player_label(c, x, y, player, size=13, ended=False):
    c.stone(x + 8, y - 5, 8, player['white'])
    w = c.text(x + 24, y, player['label'], 'medium', size, INK3, spacing=4)
    if player['active'] and not ended:
        c.text(x + 24 + w + 12, y, '行棋中', 'medium', size, ACCENT, spacing=2)


# ---------- 对局页 横屏 ----------
# 左：计分纸（宣纸半透明），上下边与棋盘对齐；对手在上、本方在下，与座位一致。
# 右上：对局动态（形势结果、落子提示、终局结果）；右下：无框操作列表，底边与棋盘对齐。
def scoresheet(c, ended=False):
    x, y, w, h = 40, 42, 312, 816
    c.rect(x, y, w, h, PANEL, 6, EDGE, shadow=(0.35, 24, 8))
    left, right = x + 32, x + w - 32
    c.text(left, 90, MODE, 'medium', 13, INK3, spacing=4)
    c.text(right, 90, BOARD, 'medium', 13, INK3, spacing=4, align='right')
    c.text(left, 146, MOVE, 'serif', 40, a(INK, 0.95))
    c.text(left, 176, KOMI, 'regular', 14, INK2)
    c.line(left, 206, right, 206, RULE_INK)

    def block(y0, player):
        live = player['active'] and not ended
        if live:
            c.rect(x, y0 + 18, 3, 222, ACCENT)
        player_label(c, left, y0 + 44, player, ended=ended)
        c.text(left, y0 + 88, player['name'], 'serif', 28, a(INK, 0.95))
        c.text(left, y0 + 112, player['sub'], 'regular', 14, INK2)
        c.text(left, y0 + 146, '主时间', 'regular', 13, INK3)
        c.text(left - 3, y0 + 206, player['time'], 'serif', 60, a(INK, 0.95 if live else 0.3))
        c.text(left, y0 + 234, player['byo'], 'regular', 14, INK2)

    block(206, WHITE_P)
    c.line(left, 598, right, 598, RULE_INK)
    block(598, BLACK)


# shape：形势开关状态，off / calc（计算中）/ on（有结果）。形势与 AI 分析互斥，开一个会关掉另一个。
def rail_rows(ended, shape='on'):
    return [
        ('pass', '虚手', ''),
        ('ownership', '形势', '已开启' if shape != 'off' else '显示归属与目数'),
        ('analysis', '分析', 'AI 推荐点'),
        ('menu', '对局菜单', '数子 · 悔棋 · 认输 · 退出'),
    ]


def action_rail(c, ended=False, shape='on'):
    rows = rail_rows(ended, shape)
    x0, x1, row = 1240, 1560, 60
    top = 858 - row * len(rows)
    for i, (kind, label, hint) in enumerate(rows):
        y = top + row * i
        disabled = ended and kind in ('pass', 'analysis')
        tone = 0.32 if disabled else 0.92
        c.line(x0, y, x1, y, RULE_PAPER)
        c.icon(kind, x0 + 12, y + 30, a(PAPER, tone * 0.87))
        c.text(x0 + 40, y + 37, label, 'medium', 18, a(PAPER, tone), spacing=2)
        if hint:
            on = hint == '已开启'
            hx = c.text(x1, y + 36, hint, 'regular', 13, a(PAPER, 0.7 if on else 0.4 * tone / 0.92), align='right')
            if on:
                c.circle(x1 - hx - 10, y + 31, 3, ACCENT)
    c.line(x0, 858, x1, 858, RULE_PAPER)


# 动态区自上而下堆叠：形势块只在形势开启时出现；落子提示 2.2 秒后淡出，形势关闭时顶到最上面。
def live_panel(c, ended=False, shape='on', notice=True):
    x0, x1 = 1240, 1560
    if ended:
        # 终局：动态区换成结果卡，顶边与棋盘对齐。
        c.rect(x0, 42, x1 - x0, 300, PANEL, 6, EDGE, shadow=(0.35, 24, 8))
        left, right = x0 + 32, x1 - 32
        c.text(left, 90, '终局', 'medium', 13, INK3, spacing=4)
        seal(c, right - 34, 70, 34, '胜')
        c.text(left, 158, '棋手 胜出', 'serif', 40, a(INK, 0.95))
        c.text(left, 192, '领先 3.5 目 · 双方连续虚手后数子', 'regular', 14, INK2)
        c.line(left, 222, right, 222, RULE_INK)
        c.rect(left, 250, right - left, 52, a(INK, 0.92), 4)
        c.text((left + right) / 2, 283, '退出对局', 'medium', 18, PAPER, spacing=4, align='center')
        return
    notice_y = 92
    if shape != 'off':
        c.text(x0, 90, '形势', 'medium', 13, a(PAPER, 0.45), spacing=4)
        if shape == 'calc':
            c.text(x0, 146, '计算中…', 'serif', 36, a(PAPER, 0.45))
            c.text(x0, 176, '贴目 计算中…', 'regular', 14, a(PAPER, 0.35))
        else:
            c.text(x0, 146, '黑领先 3.5 目', 'serif', 36, a(PAPER, 0.95))
            c.text(x0, 176, '贴目 6.5', 'regular', 14, a(PAPER, 0.5))
        c.line(x0, 206, x1, 206, RULE_PAPER)
        notice_y = 247
    if notice:
        c.stone(x0 + 8, notice_y - 6, 8, True)
        c.text(x0 + 26, notice_y, '白 落子 Q16', 'medium', 16, a(PAPER, 0.85), spacing=1)
        c.text(x1, notice_y, '刚刚', 'regular', 13, a(PAPER, 0.35), align='right')


MENU = [
    ('请求数子', '按当前局面计算结果'),
    ('悔棋', '回退到你的上一手'),
    ('认输', '确认 棋手 认输？认输后对局立即结束。'),
    ('退出对局', '确认后返回主菜单'),
]


def menu_rows(c, left, right, y, confirm_index, row_h, confirm_h, button_w=132, full_buttons=False):
    for i, (title, desc) in enumerate(MENU):
        expanded = i == confirm_index
        h = confirm_h if expanded else row_h
        if expanded:
            c.rect(left - 16, y, right - left + 32, h, a(ACCENT, 0.07), 4)
        else:
            c.line(left, y, right, y, RULE_INK)
        title_color = ACCENT if (expanded or title == '认输') else a(INK, 0.95)
        c.text(left, y + 40, title, 'medium', 20, title_color, spacing=1)
        c.text(left, y + 66, desc, 'regular', 14, INK2)
        if expanded:
            by = y + h - 24 - 44
            if full_buttons:
                half = (right - left - 12) / 2
                c.rect(left, by, half, 48, None, 4, a(INK, 0.25))
                c.text(left + half / 2, by + 31, '继续对局', 'medium', 17, a(INK, 0.9), spacing=2, align='center')
                c.rect(left + half + 12, by, half, 48, ACCENT, 4)
                c.text(left + half + 12 + half / 2, by + 31, '确认认输', 'medium', 17, PAPER, spacing=2, align='center')
            else:
                c.rect(right - button_w, by, button_w, 44, ACCENT, 4)
                c.text(right - button_w / 2, by + 29, '确认认输', 'medium', 17, PAPER, spacing=2, align='center')
                c.text(right - button_w - 28, by + 29, '继续对局', 'medium', 17, a(INK, 0.75), spacing=2, align='right')
        else:
            c.icon('next', right - 4, y + row_h / 2, a(INK, 0.35))
        y += h
    c.line(left, y, right, y, RULE_INK)
    return y


def side_sheet(c):
    c.rect(0, 0, c.w, c.h, SCRIM)
    x, w = 1140, 460
    c.rect(x, -20, w + 20, c.h + 40, SHEET, 0, None, shadow=(0.45, 30, 0))
    left, right = x + 48, 1600 - 48
    c.text(left, 96, f'{MODE} · {MOVE}', 'medium', 13, INK3, spacing=4)
    c.icon('close', right - 6, 90, a(INK, 0.6))
    c.text(left, 150, '对局菜单', 'serif', 34, a(INK, 0.95))
    menu_rows(c, left, right, 196, 2, 96, 176)
    c.text(left, 858, 'Esc 或点击左侧空白处关闭', 'regular', 13, INK3)


# 通用确认框（横屏）：居中宣纸页，眉题 + 宋体标题 + 正文，按钮右对齐；主按钮墨色，危险操作朱色。
def dialog_landscape(c):
    c.rect(0, 0, c.w, c.h, SCRIM)
    x, y, w, h = 500, 230, 600, 440
    c.rect(x, y, w, h, SHEET, 8, a(WHITE, 0.5), shadow=(0.5, 36, 12))
    left, right = x + 48, x + w - 48
    c.text(left, y + 64, f'{MODE} · 数子', 'medium', 13, INK3, spacing=4)
    c.text(left, y + 114, '确认数子结果', 'serif', 32, a(INK, 0.95))
    rows = [('黑方', '184 目'), ('白方', '177 目（含贴目 6.5）'), ('结果', '黑方胜 0.5 目')]
    ty = y + 150
    for i, (k, v) in enumerate(rows):
        c.line(left, ty, right, ty, RULE_INK)
        last = i == len(rows) - 1
        c.text(left, ty + 36, k, 'regular', 16, INK2)
        c.text(right, ty + 36, v, 'serif' if last else 'regular', 20 if last else 17, ACCENT if last else a(INK, 0.9), align='right')
        ty += 54
    c.line(left, ty, right, ty, RULE_INK)
    by = y + h - 48 - 48
    c.rect(right - 148, by, 148, 48, a(INK, 0.92), 4)
    c.text(right - 74, by + 31, '确认结果', 'medium', 17, PAPER, spacing=3, align='center')
    c.text(right - 148 - 32, by + 31, '继续对局', 'medium', 17, a(INK, 0.75), spacing=3, align='right')


def duel_landscape(mode, shape='on', notice=True, suffix=''):
    c = Canvas(1600, 900, LOOK + 'board19_landscape.png')
    ended = mode == 'end'
    scoresheet(c, ended)
    live_panel(c, ended, shape, notice)
    action_rail(c, ended, shape)
    if mode == 'menu':
        side_sheet(c)
    if mode == 'dialog':
        dialog_landscape(c)
    return c.save('duel_landscape_' + mode + suffix)


# ---------- 对局页 竖屏 ----------
# 棋盘外框约 x 14–706、y 295–985。顶部状态行（平时是对局信息，落子提示与形势结果临时替换它），
# 对手卡在棋盘上方，本方卡在下方，操作栏贴底。
PB = dict(x0=50, x1=670, y0=330, y1=948)


def board_point(col, row):
    letters = 'ABCDEFGHJKLMNOPQRST'
    i = letters.index(col)
    step_x = (PB['x1'] - PB['x0']) / 18
    step_y = (PB['y1'] - PB['y0']) / 18
    return PB['x0'] + i * step_x, PB['y0'] + (19 - row) * step_y


def portrait_card(c, y, player):
    x, w, h = 14, 692, 136
    c.rect(x, y, w, h, PANEL, 6, EDGE, shadow=(0.3, 18, 6))
    if player['active']:
        c.rect(x, y + 16, 3, h - 32, ACCENT)
    left, right = x + 26, x + w - 26
    player_label(c, left, y + 42, player, 12)
    c.text(left, y + 86, player['name'], 'serif', 26, a(INK, 0.95))
    c.text(left, y + 112, player['sub'], 'regular', 13, INK2)
    c.text(right, y + 86, player['time'], 'serif', 52, a(INK, 0.95 if player['active'] else 0.3), align='right')
    c.text(right, y + 112, player['byo'], 'regular', 13, INK2, align='right')


# status：idle（对局信息）/ notice（落子提示，2.2 秒后退回）/ calc / on（形势开启时常驻这一行）。
# 优先级：提示 > 形势 > 对局信息。
def portrait_header(c, status):
    if status == 'notice':
        c.stone(32, 90, 8, True)
        c.text(50, 96, '白 落子 Q16', 'medium', 15, a(PAPER, 0.88), spacing=1)
    elif status in ('calc', 'on'):
        w = c.text(24, 96, '形势', 'medium', 12, a(PAPER, 0.5), spacing=3)
        x = 24 + w + 14
        if status == 'calc':
            c.text(x, 97, '计算中…', 'serif', 20, a(PAPER, 0.5))
        else:
            w = c.text(x, 97, '黑领先 3.5 目', 'serif', 20, a(PAPER, 0.95))
            c.text(x + w + 12, 96, '贴目 6.5', 'regular', 13, a(PAPER, 0.5))
    else:
        c.text(24, 96, f'{MODE} · {BOARD} · {KOMI}', 'medium', 12, a(PAPER, 0.5), spacing=3)
    c.text(696, 98, MOVE, 'serif', 22, a(PAPER, 0.92), align='right')


def portrait_bar(c, shape_on=True):
    labels = [('pass', '虚手'), ('ownership', '形势'), ('analysis', '分析'), ('menu', '菜单')]
    col = 720 / len(labels)
    for i, (kind, label) in enumerate(labels):
        cx = col * i + col / 2
        c.icon(kind, cx, 1194, a(PAPER, 0.85))
        c.text(cx, 1234, label, 'medium', 15, a(PAPER, 0.9), spacing=2, align='center')
        if kind == 'ownership' and shape_on:
            c.circle(cx + 16, 1180, 3, ACCENT)
        if i:
            c.line(col * i, 1182, col * i, 1238, RULE_PAPER)


def bottom_sheet(c, top):
    c.rect(0, 0, 720, 1280, SCRIM)
    c.rect(0, top, 720, 1280 - top + 40, SHEET, 18, None, shadow=(0.45, 30, 0))
    c.rect(340, top + 12, 40, 4, a(INK, 0.18), 2)


def duel_portrait(mode, status=None, suffix=''):
    c = Canvas(720, 1280, LOOK + 'board19_portrait.png')
    status = status or ('notice' if mode == 'rest' else 'idle')
    portrait_header(c, status)
    portrait_card(c, 138, WHITE_P)
    portrait_card(c, 1004, BLACK)
    if mode == 'move':
        cx, cy = board_point('K', 14)
        c.line(cx, PB['y0'], cx, PB['y1'], a(ACCENT, 0.45), 1)
        c.line(PB['x0'], cy, PB['x1'], cy, a(ACCENT, 0.45), 1)
        c.stone(cx, cy, 15.5, False, 0.6)
        # 落子确认替换操作栏：左侧四向微调，中间坐标，右侧落子 / 取消。棋盘仍可点按、拖动改位置。
        pad = (92, 1206)
        for kind, dx, dy in (('up', 0, -40), ('down', 0, 40), ('left', -40, 0), ('right', 40, 0)):
            c.circle(pad[0] + dx, pad[1] + dy, 19, a(PAPER, 0.06), a(PAPER, 0.3), 1)
            c.icon(kind, pad[0] + dx, pad[1] + dy, a(PAPER, 0.85))
        c.text(196, 1188, '落子位置', 'medium', 12, a(PAPER, 0.5), spacing=3)
        c.text(196, 1234, 'K 14', 'serif', 36, a(PAPER, 0.95))
        c.text(452, 1216, '取消', 'medium', 17, a(PAPER, 0.75), spacing=3, align='center')
        c.rect(520, 1180, 176, 52, a(PAPER, 0.96), 4)
        c.text(608, 1214, '确认落子', 'medium', 18, INK, spacing=3, align='center')
    else:
        portrait_bar(c, status != 'idle' or mode != 'rest')
    if mode == 'menu':
        top = 630
        bottom_sheet(c, top)
        left, right = 40, 680
        c.text(left, top + 70, f'{MODE} · {MOVE}', 'medium', 12, INK3, spacing=4)
        c.icon('close', right - 6, top + 64, a(INK, 0.6))
        c.text(left, top + 118, '对局菜单', 'serif', 30, a(INK, 0.95))
        menu_rows(c, left, right, top + 150, 2, 88, 190, full_buttons=True)
    if mode == 'dialog':
        top = 900
        bottom_sheet(c, top)
        left, right = 40, 680
        c.text(left, top + 70, '局域网对战 · 对方请求', 'medium', 12, INK3, spacing=4)
        c.text(left, top + 122, '对方请求悔棋', 'serif', 30, a(INK, 0.95))
        c.text(left, top + 166, '是否同意回退上一手？', 'regular', 17, INK2)
        by, half = top + 222, (right - left - 12) / 2
        c.rect(left, by, half, 56, None, 4, a(INK, 0.25))
        c.text(left + half / 2, by + 36, '拒绝', 'medium', 18, a(INK, 0.9), spacing=4, align='center')
        c.rect(left + half + 12, by, half, 56, a(INK, 0.92), 4)
        c.text(left + half + 12 + half / 2, by + 36, '同意悔棋', 'medium', 18, PAPER, spacing=4, align='center')
    return c.save('duel_portrait_' + mode + suffix)


def seal(c, x, y, size, char):
    c.rect(x, y, size, size, ACCENT, 3)
    c.text(x + size / 2, y + size * 0.78, char, 'serif', size * 0.72, a(PAPER, 0.96), align='center')


# ---------- 主菜单 ----------
# 横屏：左侧宣纸半透明面板压在棋盘静物上（棋盘边缘从面板下透出来），编号目录式菜单；右上用户胶囊。
# 竖屏：上半棋盘静物，下半宣纸底板承载标题与菜单。
MENU_ITEMS = [
    ('01', '新游戏', '本地双人对弈'),
    ('02', '电脑对局', '与 KataGo 对弈'),
    ('03', '局域网对战', '同一网络内联机'),
    ('04', 'OGS 对战', 'Online Go Server'),
]
DESK_CAPTURE = (45, 40, 33)


def user_chip(c, right, y):
    w, h = 214, 56
    x = right - w
    c.rect(x, y, w, h, a(PAPER, 0.92), h / 2, EDGE, shadow=(0.3, 14, 4))
    c.circle(x + 28, y + 28, 18, a(INK, 0.07))
    c.icon('user', x + 28, y + 26, a(INK, 0.7), 1.4)
    c.circle(x + 42, y + 14, 5, ACCENT, a(PAPER, 1.0), 1.5)
    c.text(x + 58, y + 25, '棋手', 'medium', 16, a(INK, 0.95))
    c.text(x + 58, y + 44, 'OGS 已登录 · 1 条好友申请', 'regular', 11, INK2)


def menu_list(c, left, right, y, row, hover, title_size=26):
    for i, (num, title, caption) in enumerate(MENU_ITEMS):
        top = y + row * i
        if i == hover:
            c.rect(left - 20, top + 1, right - left + 40, row - 1, a(INK, 0.05), 2)
            c.rect(left - 20, top + 1, 3, row - 1, ACCENT)
        else:
            pass
        c.line(left, top, right, top, RULE_INK)
        base = top + row / 2 + title_size * 0.36
        c.text(left, base - 4, num, 'medium', 12, INK3, spacing=2)
        c.text(left + 44, base, title, 'serif', title_size, ACCENT if i == hover else a(INK, 0.95))
        if i == hover:
            c.icon('next', right - 4, top + row / 2, ACCENT)
            c.text(right - 24, base - 3, caption, 'regular', 14, INK2, align='right')
        else:
            c.text(right, base - 3, caption, 'regular', 14, INK3, align='right')
    end = y + row * len(MENU_ITEMS)
    c.line(left, end, right, end, RULE_INK)
    return end


def main_landscape():
    c = Canvas(1600, 900)
    c.rect(0, 0, 1600, 900, DESK_CAPTURE + (255,))
    c.paste(LOOK + 'board19_closeup.png', 502, 0, 1600, 900)
    c.rect(0, 0, 620, 900, a(PAPER, 0.9), 0, None, shadow=(0.4, 30, 0))
    left, right = 96, 524
    c.text(left, 132, '围棋 · 对弈与复盘', 'medium', 13, INK3, spacing=6)
    w = c.text(left - 4, 250, '弈·悟', 'serif', 96, a(INK, 0.95), spacing=10)
    seal(c, left + w + 18, 196, 34, '弈')
    end = menu_list(c, left, right, 350, 80, 1)
    c.text(left, end + 70, '退出', 'medium', 16, INK2, spacing=4)
    c.text(right, 844, 'v1.0', 'regular', 12, INK3, align='right')
    user_chip(c, 1560, 40)
    return c.save('main_landscape')


def main_portrait():
    c = Canvas(720, 1280)
    c.rect(0, 0, 720, 1280, DESK_CAPTURE + (255,))
    c.paste(LOOK + 'board19_closeup.png', -520, -330, 1600, 900)
    top = 560
    c.rect(0, top, 720, 760, a(PAPER, 0.95), 18, None, shadow=(0.4, 30, 0))
    left, right = 48, 672
    c.text(left, top + 70, '围棋 · 对弈与复盘', 'medium', 12, INK3, spacing=6)
    w = c.text(left - 4, top + 160, '弈·悟', 'serif', 76, a(INK, 0.95), spacing=8)
    seal(c, left + w + 14, top + 116, 28, '弈')
    end = menu_list(c, left, right, top + 206, 84, -1, 26)
    c.text(left, end + 64, '退出', 'medium', 16, INK2, spacing=4)
    c.text(right, end + 64, 'v1.0', 'regular', 12, INK3, align='right')
    user_chip(c, 696, 52)
    return c.save('main_portrait')


def sheet(name, paths, cols, scale, gap=16):
    frames = [Image.open(p) for p in paths]
    fw, fh = int(frames[0].width * scale), int(frames[0].height * scale)
    rows = (len(frames) + cols - 1) // cols
    out = Image.new('RGB', (cols * fw + (cols + 1) * gap, rows * fh + (rows + 1) * gap), (16, 14, 13))
    for i, f in enumerate(frames):
        r, col = divmod(i, cols)
        out.paste(f.resize((fw, fh), Image.LANCZOS), (gap + col * (fw + gap), gap + r * (fh + gap)))
    path = OUT + name + '.png'
    out.save(path)
    print(path, out.size)


if __name__ == '__main__':
    landscape = [duel_landscape(m) for m in ('rest', 'menu', 'dialog', 'end')]
    portrait = [duel_portrait(m) for m in ('rest', 'menu', 'move', 'dialog')]
    main_l, main_p = main_landscape(), main_portrait()
    sheet('D_duel_landscape', landscape, 2, 0.75)
    sheet('D_portrait', portrait + [main_p], 5, 0.6)
    print(main_l)
