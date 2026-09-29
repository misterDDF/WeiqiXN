# 形势开关各状态对比：横屏裁右侧列，竖屏裁顶部状态行。
from PIL import Image, ImageDraw, ImageFont
import concept_d as d

LABEL_FONT = ImageFont.truetype(d.FONTS['medium'], 22)
BG = (16, 14, 13)


def labeled(name, frames, box, labels, horizontal, gap=16, band=44):
    crops = [Image.open(f).crop(box) for f in frames]
    cw, ch = crops[0].size
    if horizontal:
        out = Image.new('RGB', (len(crops) * cw + (len(crops) + 1) * gap, ch + gap * 2 + band), BG)
        spots = [(gap + i * (cw + gap), gap + band) for i in range(len(crops))]
    else:
        out = Image.new('RGB', (cw + gap * 2, len(crops) * (ch + band) + (len(crops) + 1) * gap), BG)
        spots = [(gap, gap + band + i * (ch + band + gap)) for i in range(len(crops))]
    draw = ImageDraw.Draw(out)
    for crop, (x, y), label in zip(crops, spots, labels):
        out.paste(crop, (x, y))
        draw.text((x + 4, y - 12), label, font=LABEL_FONT, fill=(214, 206, 192), anchor='ls')
    path = d.OUT + name + '.png'
    out.save(path)
    print(path, out.size)


if __name__ == '__main__':
    land = [
        d.duel_landscape('rest', 'off', False, '_off_idle'),
        d.duel_landscape('rest', 'off', True, '_off_notice'),
        d.duel_landscape('rest', 'calc', False, '_calc'),
        d.duel_landscape('rest', 'on', False, '_on'),
    ]
    labeled('D_shape_landscape', land, (1200, 0, 1600, 900),
            ['① 形势关 · 无提示', '② 形势关 · 落子提示', '③ 形势开 · 计算中', '④ 形势开 · 有结果'], True)
    port = [
        d.duel_portrait('rest', 'idle', '_idle'),
        d.duel_portrait('rest', 'notice', '_notice'),
        d.duel_portrait('rest', 'calc', '_calc'),
        d.duel_portrait('rest', 'on', '_on'),
    ]
    labeled('D_shape_portrait', port, (0, 40, 720, 290),
            ['① 形势关 · 平时显示对局信息', '② 落子提示（2.2 秒后退回）', '③ 形势开 · 计算中', '④ 形势开 · 结果常驻这一行'], False)
    print(land[0])
