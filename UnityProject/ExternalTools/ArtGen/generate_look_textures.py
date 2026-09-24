"""程序化生成对局场景的材质贴图（视觉美化 V1，见 doc-governance/UnityProject/modules/14-visual-polish-plan.md）。

用法：python generate_look_textures.py
输出直接写入 Unity 工程；固定随机种子，重复执行结果一致。
依赖：numpy、Pillow。
"""

import os

import numpy as np
from PIL import Image

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BOARD_TEXTURE_DIR = os.path.join(PROJECT_ROOT, "Assets", "Scenes", "Duel", "Materials", "Textures")
STONE_TEXTURE_DIR = os.path.join(PROJECT_ROOT, "Assets", "Models", "Chess", "Textures")


def periodic_blur(field, sigma_x, sigma_y):
    """FFT 高斯模糊，天然周期边界，可用于无缝平铺贴图。"""
    height, width = field.shape
    fy = np.fft.fftfreq(height)[:, None]
    fx = np.fft.fftfreq(width)[None, :]
    kernel = np.exp(-2.0 * (np.pi ** 2) * ((fx * sigma_x) ** 2 + (fy * sigma_y) ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(field) * kernel))


def normalized(field):
    field = field - field.mean()
    return field / (np.abs(field).max() + 1e-8)


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def srgb_hex(value):
    value = value.lstrip("#")
    return np.array([int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def save_rgb(path, rgb):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = (np.clip(rgb, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    Image.fromarray(data, "RGB").save(path, optimize=True)
    print("wrote", path)


def save_rgba(path, rgba):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = (np.clip(rgba, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    Image.fromarray(data, "RGBA").save(path, optimize=True)
    print("wrote", path)


def ring_phase(position, widths):
    """把连续坐标映射到年轮序号与年轮内相位（0=早材起点，1=晚材末端）。"""
    edges = np.concatenate([[0.0], np.cumsum(widths)])
    index = np.clip(np.searchsorted(edges, position, side="right") - 1, 0, len(widths) - 1)
    phase = (position - edges[index]) / widths[index]
    return index, np.clip(phase, 0.0, 1.0)


def generate_kaya_board(size=2048, seed=20260924):
    """柾目榧木：竖向平行细纹、蜜金色；整盘只铺一次，不需要无缝。

    2048 像素对应 19 路整盘，约 4.6 像素/毫米；真实柾目年轮间距 1~3 毫米，晚材是其中一道很窄的深线。
    """
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size].astype(np.float64)

    # 纹理整体微弯：低频位移让直纹有自然的收拢与发散，但不出现明显波浪。
    warp = normalized(periodic_blur(rng.standard_normal((size, size)), 260.0, 420.0)) * 6.0
    warp += normalized(periodic_blur(rng.standard_normal((size, size)), 40.0, 700.0)) * 1.2
    position = x + warp + 24.0

    widths = rng.gamma(shape=5.0, scale=1.4, size=700) + 3.5
    # 少量宽年轮，形成疏密变化。
    wide = rng.random(len(widths)) < 0.07
    widths[wide] *= rng.uniform(1.8, 3.2, size=wide.sum())
    ring_index, phase = ring_phase(position, widths)

    latewood_strength = rng.uniform(0.45, 1.0, size=len(widths))
    latewood_strength[rng.random(len(widths)) < 0.15] *= 0.4
    # 早材到晚材渐深，晚材末端骤然回到下一圈早材；像素级细线靠末端的陡峭过渡形成。
    latewood = smoothstep(0.45, 0.93, phase) ** 2.6 * (1.0 - smoothstep(0.93, 1.0, phase) * 0.55)
    latewood *= latewood_strength[ring_index]

    # 沿纹理方向拉长的纤维噪声与导管细点。
    fibre = normalized(periodic_blur(rng.standard_normal((size, size)), 0.6, 22.0))
    fibre_fine = normalized(periodic_blur(rng.standard_normal((size, size)), 0.5, 4.0))
    pores = periodic_blur((rng.random((size, size)) < 0.004).astype(np.float64), 0.55, 5.0)
    pores = np.clip(pores / (pores.max() + 1e-8) * 3.0, 0.0, 1.0)

    # 大尺度色差：沿纹理方向的明暗带与缓慢的冷暖变化。
    band = normalized(periodic_blur(rng.standard_normal((size, size)), 70.0, 1600.0))
    band_wide = normalized(periodic_blur(rng.standard_normal((size, size)), 260.0, 2400.0))
    mottle = normalized(periodic_blur(rng.standard_normal((size, size)), 420.0, 640.0))

    base = srgb_hex("#E8C995")
    late = srgb_hex("#C29A5E")
    deep = srgb_hex("#D9B37A")
    pale = srgb_hex("#EED6A8")

    rgb = base[None, None, :] * np.ones((size, size, 1))
    tone = np.clip(0.5 + 0.35 * band + 0.3 * band_wide, 0.0, 1.0)[..., None]
    rgb = rgb * (1.0 - tone * 0.45) + deep[None, None, :] * (tone * 0.45)
    pale_mix = np.clip(-mottle, 0.0, 1.0)[..., None] * 0.35
    rgb = rgb * (1.0 - pale_mix) + pale[None, None, :] * pale_mix
    late_mix = (0.62 * latewood)[..., None]
    rgb = rgb * (1.0 - late_mix) + late[None, None, :] * late_mix
    brightness = 1.0 + 0.02 * mottle + 0.022 * fibre + 0.012 * fibre_fine - 0.045 * pores
    rgb *= brightness[..., None]
    save_rgb(os.path.join(BOARD_TEXTURE_DIR, "KayaBoard.png"), rgb)


def generate_linen_table(size=1024, seed=924):
    """深色素麻布：平纹经纬线 + 粗节 + 低频色差，四方连续可平铺。"""
    rng = np.random.default_rng(seed)
    thread = 4
    count = size // thread
    y, x = np.mgrid[0:size, 0:size]
    column = x // thread
    row = y // thread

    def thread_values(axis_length):
        # 每根线的基础明暗 + 沿线方向的粗节（周期噪声，保证无缝）。
        base = rng.normal(0.0, 1.0, size=count)
        base = normalized(np.real(np.fft.ifft(np.fft.fft(base) * np.exp(-2.0 * (np.pi ** 2) * (np.fft.fftfreq(count) * 0.8) ** 2))))
        slub = rng.standard_normal((count, axis_length))
        slub = np.real(np.fft.ifft(np.fft.fft(slub, axis=1) * np.exp(-2.0 * (np.pi ** 2) * (np.fft.fftfreq(axis_length)[None, :] * 14.0) ** 2), axis=1))
        slub = slub / (np.abs(slub).max() + 1e-8)
        return base, slub

    warp_base, warp_slub = thread_values(size)
    weft_base, weft_slub = thread_values(size)

    warp_value = 0.035 * warp_base[column] + 0.06 * warp_slub[column, y]
    weft_value = 0.035 * weft_base[row] + 0.06 * weft_slub[row, x]

    # 线的横截面是圆的：中间亮、两侧暗。
    across_warp = (x % thread + 0.5) / thread
    across_weft = (y % thread + 0.5) / thread
    warp_profile = np.sin(np.pi * across_warp) ** 0.7
    weft_profile = np.sin(np.pi * across_weft) ** 0.7

    warp_on_top = ((column + row) % 2) == 0
    value = np.where(warp_on_top, warp_value + 0.07 * (warp_profile - 0.7), weft_value + 0.07 * (weft_profile - 0.7))

    mottle = normalized(periodic_blur(rng.standard_normal((size, size)), 90.0, 90.0))
    grain = normalized(periodic_blur(rng.standard_normal((size, size)), 0.8, 0.8))
    value += 0.045 * mottle + 0.02 * grain

    base = srgb_hex("#35302A")
    rgb = base[None, None, :] * (1.0 + value[..., None])
    save_rgb(os.path.join(BOARD_TEXTURE_DIR, "LinenTable.png"), rgb)


def generate_stone_detail(size=256, seed=1109):
    """棋子细节图：R=蛤碁石条纹（平行微弧、疏密不一），G=那智黑石微颗粒，B=白子云状色差。

    采样方式为棋子物体空间 xz 平面投影（[-0.5, 0.5] -> [0, 1]），shader 按每颗子的世界坐标随机旋转。
    """
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size].astype(np.float64) / size

    # 条纹：以远处一点为圆心的同心弧。
    center = np.array([0.5, -2.2])
    radius = np.sqrt((x - center[0]) ** 2 + (y - center[1]) ** 2) * size
    radius += normalized(periodic_blur(rng.standard_normal((size, size)), 24.0, 24.0)) * 2.5
    radius -= radius.min() - 2.0
    widths = rng.gamma(shape=4.0, scale=1.6, size=600) + 1.5
    stripe_index, stripe_phase = ring_phase(radius, widths)
    stripe_strength = rng.uniform(0.25, 1.0, size=len(widths))
    stripe_strength[rng.random(len(widths)) < 0.15] = 1.6
    stripes = np.exp(-((stripe_phase - 0.5) / 0.22) ** 2) * stripe_strength[stripe_index]
    stripes = np.clip(stripes / 1.6, 0.0, 1.0)

    grain = periodic_blur(rng.standard_normal((size, size)), 0.6, 0.6)
    grain = 0.5 + 0.5 * normalized(grain)

    cloud = periodic_blur(rng.standard_normal((size, size)), 18.0, 18.0)
    cloud = 0.5 + 0.5 * normalized(cloud)

    rgba = np.stack([stripes, grain, cloud, np.ones_like(stripes)], axis=-1)
    save_rgba(os.path.join(STONE_TEXTURE_DIR, "StoneDetail.png"), rgba)


if __name__ == "__main__":
    generate_kaya_board()
    generate_linen_table()
    generate_stone_detail()
