# -*- coding: utf-8 -*-
"""Attack01 不规则图集 -> 连通域自动分帧 -> 重拼规则 4x2 透明底网格 -> raw\\Player_Attack01_raw.png

用途：用户直接交付的 Player_Attack01_sheet.png（1536x1024、8 帧、人物与光效跨格排布）
不是规则网格，硬按格线切会把人物切断，故先按连通域取出 8 个角色，再按 (行, 列) 排序重排。

alpha-first：源图已是透明底 RGBA，全程以 alpha 为准，不做白底合成。
"""
from PIL import Image
from pathlib import Path
import numpy as np
from scipy import ndimage

# 脚本位于 <工程根>\Tools\art-pipeline\，向上两级定位工程根（含 Assets 的目录），免配置路径
PROJ = Path(__file__).resolve().parents[2]
PLAYER = PROJ / "Assets" / "Art" / "Art" / "Player"
SRC = PLAYER / "Sheets" / "Player_Attack01_sheet.png"
DST = PLAYER / "Sheets" / "raw" / "Player_Attack01_raw.png"
N, COLS, ROWS = 8, 4, 2
ALPHA_MIN = 8
PAD = 48          # 每帧留白，保证主流程切格时内容不贴格线
MIN_SIDE = 40     # 过滤碎屑

im = Image.open(SRC).convert("RGBA")
W, H = im.size
arr = np.asarray(im)
alpha = arr[..., 3]
print(f"src={W}x{H} mode=RGBA")
assert (alpha <= ALPHA_MIN).mean() > 0.02, "源图不是透明底，请先在入口抠图"

fg = alpha > ALPHA_MIN
lbl, n = ndimage.label(fg)
if n < N:
    raise RuntimeError(f"只找到 {n} 个连通域, 少于 {N}")

sizes = ndimage.sum(fg, lbl, range(1, n + 1))
order = np.argsort(sizes)[::-1]          # 取最大的 8 个域（角色主体；悬空小光效不入选）
boxes = []
for idx in order:
    ys, xs = np.where(lbl == idx + 1)
    bw, bh = xs.max() - xs.min() + 1, ys.max() - ys.min() + 1
    if bw < MIN_SIDE or bh < MIN_SIDE:
        continue
    boxes.append((int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)))
    if len(boxes) == N:
        break
if len(boxes) != N:
    raise RuntimeError(f"有效主体只有 {len(boxes)} 个, 少于 {N}")

def cy(b):
    return (b[1] + b[3]) / 2

row1 = sorted([b for b in boxes if cy(b) < H / 2], key=lambda b: b[0])
row2 = sorted([b for b in boxes if cy(b) >= H / 2], key=lambda b: b[0])
print(f"row1={len(row1)} row2={len(row2)}")
if len(row1) != COLS or len(row2) != COLS:
    raise RuntimeError(f"行内帧数不是 {COLS}/{COLS}: {len(row1)}/{len(row2)}")
seq = row1 + row2

# 颜色外溢：透明区 RGB 用最近不透明像素填充，避免下游 LANCZOS 缩放渗色出晕圈
empty = ~fg
if empty.any():
    idx = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
    rgb = arr[..., :3][idx[0], idx[1]]
else:
    rgb = arr[..., :3]
master = np.dstack([rgb, np.where(fg, alpha, 0).astype(np.uint8)])

max_h = max(b[3] - b[1] for b in seq)
CW, CH = max(b[2] - b[0] for b in seq) + PAD * 2, max_h + PAD * 2
sheet = Image.new("RGBA", (CW * COLS, CH * ROWS), (0, 0, 0, 0))
for i, b in enumerate(seq):
    img = Image.fromarray(master[b[1]:b[3], b[0]:b[2]], "RGBA")
    r, c = divmod(i, COLS)
    px = c * CW + (CW - img.width) // 2
    py = r * CH + (CH - img.height) // 2
    sheet.paste(img, (px, py))          # 直贴保留 straight alpha，不做 mask 混合
    print(f"frame{i}: bbox={b} size={img.size}")
sheet.save(DST)
print(f"OK -> {DST} ({sheet.size[0]}x{sheet.size[1]}, cell {CW}x{CH}, mode={sheet.mode})")
