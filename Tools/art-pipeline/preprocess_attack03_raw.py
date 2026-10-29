# -*- coding: utf-8 -*-
"""Attack03（第三段攻击·投掷蓝水桶）不规则图集预处理：
透明底 -> 连通域分帧（9 个角色，位于 5+4 两行）-> 重拼规则 3x3 透明底网格
-> raw\Player_Attack03_raw.png

alpha-first：源图自带 alpha，直接以 alpha 为准，不做白底合成。
身高归一等缩放交给 process_sheets.py 的 norm='frame'（以每帧最高连通域即角色为基准）。
图集中的飞桶/光束/爆炸/水花是分离连通域，且浮在角色头顶上方 40–60px：角色归一放大到
760px 后它们会超出 833 画格 71–236px 被裁断，故本脚本只取角色本体，特效后续作为独立 FX 制作。
"""
from PIL import Image
from pathlib import Path
import numpy as np
from scipy import ndimage

# 脚本位于 <工程根>\Tools\art-pipeline\，向上两级定位工程根，免配置路径
PROJ = Path(__file__).resolve().parents[2]
SRC = Path(r"C:\Users\WSX.LAPTOP-5HQ2JJQQ\Downloads\task-mvjy5yjx36k13.png")  # 外部原始素材
DST = PROJ / "Assets" / "Art" / "Art" / "Player" / "Sheets" / "raw" / "Player_Attack03_raw.png"
N, COLS, ROWS = 9, 3, 3
ALPHA_MIN = 8
AREA_MIN = 500      # 小于该面积视为碎点噪声
CHAR_MIN_H = 120    # 角色主体最小高度；低于该值且面积达标的连通域判为分离特效
PAD = 48

im = Image.open(SRC).convert("RGBA")
W, H = im.size
arr = np.asarray(im)
alpha = arr[..., 3]
print(f"src={W}x{H} mode=RGBA")
assert (alpha <= ALPHA_MIN).mean() > 0.02, "源图不是透明底，请先在入口抠图"

fg = alpha > ALPHA_MIN
lbl, n = ndimage.label(fg)
sizes = ndimage.sum(fg, lbl, range(1, n + 1))
objs = ndimage.find_objects(lbl)


def bbox_of(idx):
    sl = objs[idx - 1]
    return (sl[1].start, sl[0].start, sl[1].stop, sl[0].stop)


chars, fx = [], []
for i in range(1, n + 1):
    if sizes[i - 1] < AREA_MIN:
        continue
    x0, y0, x1, y1 = bbox_of(i)
    (chars if (y1 - y0) >= CHAR_MIN_H else fx).append((i, (x0, y0, x1, y1)))

print(f"chars={len(chars)} fx={len(fx)}")
assert len(chars) == N, f"角色连通域应为 {N} 个，实得 {len(chars)}"


def cy(b):
    return (b[1] + b[3]) / 2


# 按 (行, x) 排序：与素材的 5+4 两行排布一致
chars.sort(key=lambda c: cy(c[1]))
row1 = sorted([c for c in chars if cy(c[1]) < H / 2], key=lambda c: c[1][0])
row2 = sorted([c for c in chars if cy(c[1]) >= H / 2], key=lambda c: c[1][0])
seq = row1 + row2
print(f"row1={len(row1)} row2={len(row2)}")

# 分离特效归属：同排内，取「特效左边缘 - 角色右边缘」绝对值最小的角色
# 仅用于打印排查；本版不把特效并进角色帧（见文件头说明）
for fi, fb in fx:
    same = [k for k, (_, cb) in enumerate(seq) if (cy(cb) < H / 2) == (cy(fb) < H / 2)]
    k = min(same, key=lambda k: abs(fb[0] - seq[k][1][2]))
    print(f"fx area={int(sizes[fi-1])} bbox={fb} -> frame{k}（不并入）")

# 颜色外溢：透明区 RGB 用最近不透明像素颜色填充，避免下游缩放渗色出晕圈
empty = ~fg
if empty.any():
    idx = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
    rgb = arr[..., :3][idx[0], idx[1]]
else:
    rgb = arr[..., :3]
master = np.dstack([rgb, np.where(fg, alpha, 0).astype(np.uint8)])

# 每帧只取角色本体包围盒
boxes = []
for k, (ci, cb) in enumerate(seq):
    boxes.append(cb)
    print(f"frame{k}: char={cb}")

CW = max(b[2] - b[0] for b in boxes) + PAD * 2
CH = max(b[3] - b[1] for b in boxes) + PAD * 2
sheet = Image.new("RGBA", (CW * COLS, CH * ROWS), (0, 0, 0, 0))
for k, b in enumerate(boxes):
    img = Image.fromarray(master[b[1]:b[3], b[0]:b[2]], "RGBA")
    r, c = divmod(k, COLS)
    px = c * CW + (CW - img.width) // 2
    py = r * CH + (CH - img.height) // 2
    sheet.paste(img, (px, py))          # 直贴保留 straight alpha，不做 mask 混合
    print(f"frame{k}: size={img.size}")
sheet.save(DST)
print(f"OK -> {DST} ({sheet.size[0]}x{sheet.size[1]}, cell {CW}x{CH}, mode={sheet.mode})")
