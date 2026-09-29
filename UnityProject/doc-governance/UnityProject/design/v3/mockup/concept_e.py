# V3 方案稿 E：横屏改为“棋盘靠左 + 右侧单栏”，右栏按竖屏的纵向顺序排（对手卡 / 对局信息 / 动态 / 操作 / 本方卡）。
# 玩家卡按棋色区分：黑方墨色卡、白方宣纸卡，横竖屏共用同一套卡片节点（StateRoot 只改位置和尺寸）。
import concept_d as d
from concept_d import Canvas, a, PAPER, INK, INK2, INK3, ACCENT, PANEL, EDGE, RULE_PAPER, RULE_INK, BLACK, WHITE_P, MODE, MOVE, BOARD, KOMI

BACK_LEFT = d.LOOK + 'board19_landscape_left.png'  # 由 make_backdrop.py 从对局截图合成
INK_CARD = a((18, 16, 14), 0.86)
INK_EDGE = a(PAPER, 0.10)
ACCENT_ON_INK = (204, 96, 72)


def card(c, x, y, w, h, player, pad=26, name_size=26, clock_size=52, ended=False):
    dark = not player['white']
    live = player['active'] and not ended
    c.rect(x, y, w, h, INK_CARD if dark else PANEL, 6, INK_EDGE if dark else EDGE, shadow=(0.3, 18, 6))
    fg = PAPER if dark else INK
    sub = a(PAPER, 0.55) if dark else INK2
    lab = a(PAPER, 0.5) if dark else INK3
    accent = ACCENT_ON_INK if dark else ACCENT
    if live:
        c.rect(x, y + 16, 3, h - 32, accent)
    left, right = x + pad, x + w - pad
    c.stone(left + 9, y + 36, 9, player['white'])
    if dark:
        c.circle(left + 9, y + 36, 9.5, None, a(PAPER, 0.22), 1)
    lw = c.text(left + 26, y + 41, player['label'], 'medium', 12, lab, spacing=4)
    if live:
        c.text(left + 26 + lw + 12, y + 41, '行棋中', 'medium', 12, accent, spacing=2)
    c.text(left, y + h - 50, player['name'], 'serif', name_size, a(fg, 0.95))
    c.text(left, y + h - 24, player['sub'], 'regular', 13, sub)
    c.text(right, y + h - 50, player['time'], 'serif', clock_size, a(fg, 0.95 if live else 0.3), align='right')
    c.text(right, y + h - 24, player['byo'], 'regular', 13, sub, align='right')


def rail(c, x0, x1, bottom, row, shape='off', ended=False):
    rows = d.rail_rows(ended, shape)
    top = bottom - row * len(rows)
    for i, (kind, label, hint) in enumerate(rows):
        y = top + row * i
        disabled = ended and kind in ('pass', 'analysis')
        tone = 0.32 if disabled else 0.92
        c.line(x0, y, x1, y, RULE_PAPER)
        c.icon(kind, x0 + 12, y + row / 2, a(PAPER, tone * 0.87))
        c.text(x0 + 40, y + row / 2 + 7, label, 'medium', 18, a(PAPER, tone), spacing=2)
        if hint:
            on = hint == '已开启'
            hx = c.text(x1, y + row / 2 + 6, hint, 'regular', 13, a(PAPER, 0.7 if on else 0.4 * tone / 0.92), align='right')
            if on:
                c.circle(x1 - hx - 10, y + row / 2 + 1, 3, ACCENT)
    c.line(x0, bottom, x1, bottom, RULE_PAPER)
    return top


def notice_line(c, x0, x1, y):
    c.stone(x0 + 8, y - 6, 8, True)
    c.text(x0 + 26, y, '白 落子 Q16', 'medium', 16, a(PAPER, 0.85), spacing=1)
    c.text(x1, y, '刚刚', 'regular', 13, a(PAPER, 0.35), align='right')


# 横屏对局菜单：抽屉正好盖住右栏（棋盘右缘 + 24 起到屏幕右缘），不切断玩家卡。
def side_sheet(c):
    c.rect(0, 0, c.w, c.h, d.SCRIM)
    x = 858 + 24
    c.rect(x, -20, c.w - x + 20, c.h + 40, d.SHEET, 0, None, shadow=(0.45, 30, 0))
    left, right = X0 + 24, X1 - 6
    c.text(left, 96, f'{MODE} · {MOVE}', 'medium', 13, INK3, spacing=4)
    c.icon('close', right - 6, 90, a(INK, 0.6))
    c.text(left, 150, '对局菜单', 'serif', 34, a(INK, 0.95))
    d.menu_rows(c, left, right, 196, 2, 96, 176)
    c.text(left, 858, 'Esc 或点击棋盘区域关闭', 'regular', 13, INK3)


# 横屏：棋盘外框 x 42–858（沿用复盘页的相机横移），右栏 x 906–1558，上下与棋盘对齐。
X0, X1 = 906, 1558
CARD_H = 150


def duel_e(mode, shape='off', notice=False):
    c = Canvas(1600, 900, BACK_LEFT)
    ended = mode == 'end'
    card(c, X0, 42, X1 - X0, CARD_H, WHITE_P, pad=30, name_size=28, clock_size=56, ended=ended)
    card(c, X0, 858 - CARD_H, X1 - X0, CARD_H, BLACK, pad=30, name_size=28, clock_size=56, ended=ended)
    rail_top = rail(c, X0, X1, 858 - CARD_H - 24, 54, shape, ended)
    y = 42 + CARD_H
    if ended:
        top, bottom = y + 24, rail_top - 24
        c.rect(X0, top, X1 - X0, bottom - top, PANEL, 6, EDGE, shadow=(0.35, 24, 8))
        left, right = X0 + 30, X1 - 30
        c.text(left, top + 44, f'终局 · {MODE}', 'medium', 13, INK3, spacing=4)
        d.seal(c, right - 34, top + 24, 34, '胜')
        c.text(left, top + 100, '棋手 胜出', 'serif', 36, a(INK, 0.95))
        c.text(left, top + 132, '领先 3.5 目 · 双方连续虚手后数子', 'regular', 14, INK2)
        bw = 176
        c.rect(right - bw, bottom - 30 - 48, bw, 48, a(INK, 0.92), 4)
        c.text(right - bw / 2, bottom - 30 - 17, '退出对局', 'medium', 17, PAPER, spacing=4, align='center')
        c.text(left, bottom - 30 - 17, '第 57 手 · 十九路 · 贴 6.5 目', 'regular', 13, INK3)
        return c.save('E_duel_end')
    # 对局信息：常驻，承接竖屏顶部状态行。
    c.text(X0, y + 58, f'{MODE} · {BOARD} · {KOMI}', 'medium', 13, a(PAPER, 0.5), spacing=4)
    c.text(X0 - 2, y + 112, MOVE, 'serif', 44, a(PAPER, 0.95))
    # 动态区：形势块（开启时）+ 落子提示（2.2 秒淡出），自上而下堆叠。
    dy = y + 112 + 58
    if shape != 'off':
        c.text(X0, dy, '形势', 'medium', 13, a(PAPER, 0.45), spacing=4)
        if shape == 'calc':
            c.text(X0 + 60, dy + 1, '计算中…', 'serif', 24, a(PAPER, 0.45))
        else:
            w = c.text(X0 + 60, dy + 2, '黑领先 3.5 目', 'serif', 26, a(PAPER, 0.95))
            c.text(X0 + 60 + w + 14, dy, '贴目 6.5', 'regular', 14, a(PAPER, 0.5))
        dy += 44
    if notice:
        notice_line(c, X0, X1, dy)
    if mode == 'menu':
        side_sheet(c)
        return c.save('E_duel_menu')
    if mode == 'dialog':
        d.dialog_landscape(c)
        return c.save('E_duel_dialog')
    name = {('off', False): 'rest', ('off', True): 'notice', ('on', True): 'shape'}.get((shape, notice), shape)
    return c.save('E_duel_' + name)


def portrait_e(mode, status=None):
    d.portrait_card = lambda c, y, player: card(c, 14, y, 692, 136, player)
    return d.duel_portrait(mode, status, '_E' + ('_' + status if status and status != 'idle' else ''))


if __name__ == '__main__':
    frames = [duel_e('rest'), duel_e('rest', 'on', True), duel_e('end'), duel_e('menu'), duel_e('dialog')]
    frames += [portrait_e('rest', 'idle'), portrait_e('rest', 'on')] + [portrait_e(m) for m in ('menu', 'move', 'dialog')]
    for f in frames:
        print(f)
