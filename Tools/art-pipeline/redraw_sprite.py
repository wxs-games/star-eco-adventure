"""把平涂精灵表按「矢量式重绘」放大 —— 不是像素插值，而是重新描边重新填色。

为什么不用超分模型：
  超分是「根据低分辨率像素猜高分辨率像素」，边缘必然是插值出来的过渡灰，
  线条发虚、平涂区带噪。对**本来就很小的平涂动画原画**，肉眼一看就是「被拉大的图」。

这里的做法（三步，全部基于原图的真实信息，不凭空生成任何内容）：
  1. 归一化色板：把所有帧的不透明像素归并成 K 个主色。
     原图里的 #EF7420 / #EF7620 / #EC7621 这类差异只是 JPEG 式噪点，
     归并后得到真正干净的平涂色。
  2. 源分辨率归色：每个源像素吸附到色板 → 噪点消失，色块边界变干净。
  3. 目标分辨率重绘：把归色后的图按目标尺寸重采样，得到一个连续的颜色场，
     再在 **2 倍超采样** 的网格上重新吸附色板 → 色块边界落在亚像素精度上，
     最后降采样得到抗锯齿的硬边平涂。
     等价于「按比例重新描一遍轮廓、重新填一遍色」，所以任意尺寸都不会糊。

排版沿用 upscale_sheet.py 的规则：**全部帧共用一个缩放系数和一个落点**，
帧间相对位移（跑步的上下起伏、前后脚错位）原样保留。

用法:
  python redraw_sprite.py --src <sheet.png> --cols 5 --rows 3 \
      --canvas 2400x1666 --baseline 1650 --height 1510 --outdir out \
      [--colors 24] [--ss 2] [--no-snap-source]
"""
import argparse
import os

import numpy as np
from PIL import Image

# ---------------------------------------------------------------- 色彩工具


def to_lab(rgb_u8):
    """(h,w,3) uint8 RGB → (h,w,3) float32 CIELAB（cv2 不可用时退回近似）。"""
    try:
        import cv2

        small = np.ascontiguousarray(rgb_u8[..., :3])
        lab = cv2.cvtColor(small, cv2.COLOR_RGB2LAB).astype(np.float32)
        # OpenCV Lab: L 0..255, a/b 0..255(偏移 128) —— 缩放到常规量级，让三轴权重接近
        lab[..., 0] *= 100.0 / 255.0
        lab[..., 1] -= 128.0
        lab[..., 2] -= 128.0
        return lab
    except Exception:
        f = rgb_u8[..., :3].astype(np.float32) / 255.0
        lin = np.where(f <= 0.04045, f / 12.92, ((f + 0.055) / 1.055) ** 2.4)
        m = np.array([[0.4124, 0.3576, 0.1805],
                      [0.2126, 0.7152, 0.0722],
                      [0.0193, 0.1192, 0.9505]], np.float32)
        xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883], np.float32)
        e = 216.0 / 24389.0
        k = 24389.0 / 27.0
        f2 = np.where(xyz > e, np.cbrt(xyz), (k * xyz + 16.0) / 116.0)
        L = 116.0 * f2[..., 1] - 16.0
        return np.stack([L, 500.0 * (f2[..., 0] - f2[..., 1]),
                         200.0 * (f2[..., 1] - f2[..., 2])], -1).astype(np.float32)


def build_palette(rgb_pixels, n_colors):
    """对一堆 RGB 像素做中位切分量化，返回 (K,3) uint8 色板。

    先量化再取每簇的中位色，比直接用量化器给出的代表色更贴近平涂的真实色值。
    """
    px = rgb_pixels.reshape(-1, 3)
    if len(px) == 0:
        return np.zeros((1, 3), np.uint8)
    strip = Image.fromarray(px[None, :, :], "RGB")
    q = strip.quantize(colors=n_colors, method=Image.MEDIANCUT, dither=Image.NONE)
    idx = np.asarray(q).reshape(-1)
    pal = q.getpalette()[: n_colors * 3]
    pal = np.array(pal, np.uint8).reshape(-1, 3)
    used = np.unique(idx)
    out = []
    for u in used:
        member = px[idx == u]
        if len(member) == 0:
            continue
        out.append(np.median(member, axis=0))
    if not out:
        return pal[:1]
    return np.clip(np.round(np.array(out)), 0, 255).astype(np.uint8)


def snap_to_palette(rgb_u8, pal_rgb, pal_lab, chunk=200000):
    """把每个像素吸附到最近的色板颜色，返回 (h,w) 索引与 (h,w,3) 结果。"""
    h, w = rgb_u8.shape[:2]
    lab = to_lab(rgb_u8).reshape(-1, 3)
    n = lab.shape[0]
    idx = np.empty(n, np.int16)
    pal_sq = (pal_lab ** 2).sum(1)
    for s in range(0, n, chunk):
        seg = lab[s:s + chunk]
        # |a|^2 - 2·a·p + |p|^2
        d = pal_sq[None, :] - 2.0 * (seg @ pal_lab.T) + (seg ** 2).sum(1)[:, None]
        idx[s:s + chunk] = np.argmin(d, axis=1).astype(np.int16)
    return idx.reshape(h, w), pal_rgb[idx.reshape(h, w)]


# ---------------------------------------------------------------- 图像工具


def bleed_rgb(rgb, alpha):
    """透明区的 RGB 用最近的不透明像素填满，避免重采样时边缘发黑/发灰。"""
    from scipy import ndimage

    opaque = alpha > 8
    if not opaque.any():
        return rgb
    ind = ndimage.distance_transform_edt(~opaque, return_distances=False, return_indices=True)
    return rgb[ind[0], ind[1]]


def up_alpha(alpha_u8, size):
    """alpha 单独放大并收紧，得到干净的边缘（中间保留 1px 过渡用于抗锯齿）。"""
    a = np.asarray(Image.fromarray(alpha_u8, "L").resize(size, Image.LANCZOS)).astype(np.float32) / 255.0
    lo, hi = 0.20, 0.70
    t = np.clip((a - lo) / (hi - lo), 0.0, 1.0)
    a = t * t * (3 - 2 * t)
    return (a * 255.0 + 0.5).astype(np.uint8)


def resize_premul(rgb_u8, alpha_u8, size, resample=Image.LANCZOS):
    """带 alpha 的缩放：预乘后再插值，避免透明边缘混进黑边。"""
    a = alpha_u8.astype(np.float32) / 255.0
    pm = rgb_u8.astype(np.float32) * a[..., None]
    chans = []
    for c in range(3):
        im = Image.fromarray(np.clip(pm[..., c], 0, 255).astype(np.uint8), "L").resize(size, resample)
        chans.append(np.asarray(im).astype(np.float32))
    a_im = Image.fromarray(alpha_u8, "L").resize(size, resample)
    a_out = np.asarray(a_im).astype(np.float32) / 255.0
    rgb = np.stack(chans, -1) / np.maximum(a_out, 1e-6)[..., None]
    return np.clip(rgb, 0, 255).astype(np.uint8)


def cell_grid(img, cols, rows):
    cw, ch = img.width // cols, img.height // rows
    for r in range(rows):
        for c in range(cols):
            yield r * cols + c, img.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))


# ---------------------------------------------------------------- 主流程


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--cols", type=int, default=5)
    ap.add_argument("--rows", type=int, default=3)
    ap.add_argument("--canvas", default="2400x1666")
    ap.add_argument("--baseline", type=int, default=1650)
    ap.add_argument("--height", type=int, default=1510, help="目标角色身高(px)")
    ap.add_argument("--center-x", type=int, default=0, help="0 = 画布中线")
    ap.add_argument("--colors", type=int, default=24, help="色板颜色数")
    ap.add_argument("--ss", type=int, default=2, help="超采样倍率（用于抗锯齿）")
    ap.add_argument("--no-snap-source", action="store_true",
                    help="跳过源分辨率归色（默认会先归色去噪）")
    ap.add_argument("--outdir", required=True)
    a = ap.parse_args()

    CW, CH = [int(v) for v in a.canvas.lower().split("x")]
    cx_target = a.center_x or CW // 2
    os.makedirs(a.outdir, exist_ok=True)

    src = Image.open(a.src).convert("RGBA")
    cells = []
    for idx, c in cell_grid(src, a.cols, a.rows):
        arr = np.array(c)
        if (arr[..., 3] > 8).any():
            cells.append((idx, arr))
    print(f"[in ] 源 {src.size} {a.cols}x{a.rows} → 有效 {len(cells)} 帧")

    # ---- 1. 建色板（全部帧共用一套，保证帧间颜色一致）----
    samples = [c[1][c[1][..., 3] > 200][:, :3] for c in cells]
    allpx = np.concatenate([s for s in samples if len(s)]) if samples else np.zeros((1, 3), np.uint8)
    pal_rgb = build_palette(allpx, a.colors)
    pal_lab = to_lab(pal_rgb[None, :, :])[0]
    print(f"[pal] {a.colors} 色 → 实际 {len(pal_rgb)} 个")

    # ---- 2. 逐帧：源归色 → 目标重绘 ----
    frames = []
    for idx, arr in cells:
        alpha = arr[..., 3]
        ys, xs = np.where(alpha > 8)
        x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
        pad = 3
        x0, y0 = max(0, x0 - pad), max(0, y0 - pad)
        x1, y1 = min(arr.shape[1], x1 + pad), min(arr.shape[0], y1 + pad)
        rgb = bleed_rgb(arr[y0:y1, x0:x1, :3], alpha[y0:y1, x0:x1])
        al = alpha[y0:y1, x0:x1]

        if not a.no_snap_source:
            # 源分辨率先吸附一次：把压缩噪点压平，色块边界变干净
            _, rgb = snap_to_palette(rgb, pal_rgb, pal_lab)

        frames.append(dict(idx=idx, rgb=rgb, alpha=al,
                           bbox=(int(x0), int(y0), int(x1), int(y1))))

    # ---- 3. 统一缩放与落点（全部帧共用，保住帧间相对位移）----
    src_h = max(f["bbox"][3] - f["bbox"][1] for f in frames)
    scale = a.height / src_h
    ref = max(frames, key=lambda f: f["bbox"][3])
    ref_bottom = ref["bbox"][3]
    centers = [((f["bbox"][0] + f["bbox"][2]) / 2) for f in frames]
    src_cx = float(np.median(centers))
    tx = cx_target - src_cx * scale
    ty = a.baseline - ref_bottom * scale
    print(f"[fit] 源身高 {src_h} → {a.height}px  缩放 {scale:.4f}")
    print(f"[fit] 基准帧#{ref['idx']:02d} 底边 {ref_bottom} → 基线 {a.baseline}")
    print(f"[fit] 统一落点 ({tx:.1f}, {ty:.1f})  超采样 x{a.ss}")

    made = []
    for f in frames:
        x0, y0, x1, y1 = f["bbox"]
        w_t = max(1, int(round((x1 - x0) * scale)))
        h_t = max(1, int(round((y1 - y0) * scale)))
        ws = (w_t * a.ss, h_t * a.ss)

        # 重采样到超采样尺寸 → 连续颜色场 → 重新吸附色板（这是「重绘」的关键一步）
        big_rgb = resize_premul(f["rgb"], f["alpha"], ws)
        _, big_rgb = snap_to_palette(big_rgb, pal_rgb, pal_lab)
        big_a = up_alpha(f["alpha"], ws)

        # 降采样：超采样色块 → 抗锯齿硬边
        fin_rgb = resize_premul(big_rgb, big_a, (w_t, h_t))
        fin_a = up_alpha(f["alpha"], (w_t, h_t))
        canvas = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
        canvas.alpha_composite(Image.fromarray(np.dstack([fin_rgb, fin_a]), "RGBA"),
                               (int(round(x0 * scale + tx)), int(round(y0 * scale + ty))))
        name = "frame_%02d.png" % f["idx"]
        canvas.save(os.path.join(a.outdir, name))
        made.append(name)
        ay, ax = np.where(np.asarray(canvas)[..., 3] > 8)
        print(f"  out {name}  内容 x[{ax.min()},{ax.max()}] y[{ay.min()},{ay.max()}]")

    print(f"[done] {len(made)} 帧 → {a.outdir}")


if __name__ == "__main__":
    main()
