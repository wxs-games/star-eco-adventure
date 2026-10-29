"""把 AI 生成的「白底网格精灵表」预处理成可切图的透明网格表。

解决三件事：
  1. 白底 -> 真透明：反预乘去白边，边缘色用最近实心像素回填（否则放大后发黑/发白）
  2. 格子边框：每格按 inset 内缩后再取，抹掉模型画的表格线
  3. 水印/杂点：按连通域面积过滤，只保留主体（水印文字是若干小块，面积远小于角色）

输出一张 cols*rows 的透明网格表，cell 尺寸统一，交给 upscale_sheet.py 切图放大。

用法:
  python prepare_grid_sheet.py --src in.png --cols 4 --rows 3 --out clean.png [--inset 8] [--min-area 600]
"""
import argparse

import numpy as np
from PIL import Image, ImageFilter
from scipy.ndimage import distance_transform_edt, label


def white_to_alpha(rgb):
    """白底转透明：先估计 alpha，再反预乘去白边。

    背景判定用「从格子边界泛洪」：只有与边缘连通的浅色低饱和区域才算背景。
    这样模型底图里那些淡灰纸纹会被整体清掉，而角色内部的浅色（高光等）
    因为被深色轮廓包住、不与边缘连通，不会被误伤成透明洞。
    """
    f = rgb.astype(np.float32)
    lum_min = f.min(axis=2)
    sat = f.max(axis=2) - lum_min

    candidate = (sat <= 12) & (lum_min >= 205)
    lab, n = label(candidate)
    border = np.zeros(lab.shape, bool)
    border[0, :] = border[-1, :] = True
    border[:, 0] = border[:, -1] = True
    bg_ids = np.unique(lab[border & candidate])
    bg_ids = bg_ids[bg_ids > 0]
    bg = np.isin(lab, bg_ids)

    a = np.clip((250.0 - lum_min) / 26.0, 0.0, 1.0)
    a[bg] = 0.0
    solid = a >= 0.5

    a_safe = np.clip(a, 0.5, 1.0)[:, :, None]
    true = np.clip((f - (1.0 - a)[:, :, None] * 255.0) / a_safe, 0, 255)

    # 非实心区（含完全透明与半透明边）用最近实心像素的真实色回填
    if solid.any():
        _, idx = distance_transform_edt(~solid, return_indices=True)
        filled = true[idx[0], idx[1]]
        true[~solid] = filled[~solid]

    alpha8 = (a * 255.0).round().astype(np.uint8)
    alpha8 = np.array(Image.fromarray(alpha8, "L").filter(ImageFilter.GaussianBlur(0.5)))
    return true, alpha8


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--cols", type=int, required=True)
    ap.add_argument("--rows", type=int, required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--inset", type=int, default=8, help="每格四周内缩像素，用于抹掉表格线")
    ap.add_argument("--min-area", type=int, default=600, help="连通域最小面积，小于此值视为水印/杂点")
    ap.add_argument("--no-filter", action="store_true", help="关闭连通域过滤（调试用）")
    a = ap.parse_args()

    sheet = np.array(Image.open(a.src).convert("RGB"))
    H, W = sheet.shape[:2]
    cw, ch = W // a.cols, H // a.rows
    print(f"[in ] 源图 {W}x{H}  网格 {a.cols}x{a.rows}  单格 {cw}x{ch}  inset={a.inset}")

    ow, oh = cw - 2 * a.inset, ch - 2 * a.inset
    out = Image.new("RGBA", (ow * a.cols, oh * a.rows), (0, 0, 0, 0))
    dropped_total = 0

    for r in range(a.rows):
        for c in range(a.cols):
            idx = r * a.cols + c
            cell = sheet[r * ch + a.inset:(r + 1) * ch - a.inset,
                         c * cw + a.inset:(c + 1) * cw - a.inset]
            rgb, alpha = white_to_alpha(cell)

            # 连通域过滤：只留主体
            if not a.no_filter:
                mask = alpha > 24
                lab, n = label(mask)
                if n > 0:
                    area = np.bincount(lab.ravel())
                    area[0] = 0
                    keep = area >= a.min_area
                    if keep.any():
                        keep_ids = np.where(keep)[0]
                        keep_map = np.isin(lab, keep_ids)
                        dropped = int(((lab > 0) & ~keep_map).sum())
                        dropped_total += dropped
                        alpha = np.where(keep_map, alpha, 0).astype(np.uint8)
                        rgb = np.where(keep_map[:, :, None], rgb, 0)
                    print(f"  cell {idx:02d} (r{r}c{c}) 连通域={n} 保留={int(keep.sum())} 丢弃像素={dropped}")
                else:
                    print(f"  cell {idx:02d} (r{r}c{c}) 空")

            out.paste(Image.fromarray(np.dstack([rgb, alpha]).round().astype(np.uint8), "RGBA"),
                      (c * ow, r * oh))

    out.save(a.out)
    print(f"[out] {out.size}  -> {a.out}   共丢弃小连通域像素 {dropped_total}")


if __name__ == "__main__":
    main()
