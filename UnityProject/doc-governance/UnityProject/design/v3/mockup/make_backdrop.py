# 用横屏截图合成“棋盘靠左”的底图：桌面按顶行横向明暗 × 左右空白列纵向明暗重建，棋盘连阴影平移后羽化贴回。
import numpy as np
from PIL import Image
import concept_d as d

src = np.asarray(Image.open(d.LOOK + 'board19_landscape.png').convert('RGB')).astype(np.float32)
H, W, _ = src.shape
row = np.concatenate([src[:, :360], src[:, 1250:]], axis=1).mean(axis=1)          # 每行颜色
col = src[:24].mean(axis=0)                                                        # 每列颜色（顶端全是桌面）
col_l = col.mean(axis=1, keepdims=True)
desk = row[:, None, :] * (col_l / col_l.mean())[None, :, :]

shift = -350
x0, x1, y0, y1 = 366, 1240, 16, 896
crop = src[y0:y1, x0:x1]
mask = np.ones(crop.shape[:2], np.float32)
feather = 22
ramp = np.linspace(0, 1, feather)
mask[:, :feather] *= ramp[None, :]
mask[:, -feather:] *= ramp[::-1][None, :]
mask[:feather, :] *= ramp[:, None]
mask[-feather:, :] *= ramp[::-1][:, None]
out = desk.copy()
nx0 = x0 + shift
region = out[y0:y1, nx0:nx0 + (x1 - x0)]
out[y0:y1, nx0:nx0 + (x1 - x0)] = region * (1 - mask[..., None]) + crop * mask[..., None]
Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(d.LOOK + 'board19_landscape_left.png')
print('ok')
