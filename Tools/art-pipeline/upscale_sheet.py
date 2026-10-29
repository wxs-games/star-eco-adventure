"""把精灵表逐帧**无损放大**（Real-ESRGAN anime 4x），再按工程规格排版。

目标不是「重新画」，而是「同一张图变清晰」：
  - RGB 走超分模型（动漫线稿专用），线条与平涂色块保持原样
  - alpha 单独双三次放大后收紧边缘，避免出现半透明毛边
  - 透明区的 RGB 先用最近邻不透明像素「外扩填色」，否则放大后边缘会发黑
  - 全部帧用**同一套变换**（同一缩放、同一落点），所以帧间相对位移（跑步的上下起伏、
    前后脚错位）被原样保留，不会被逐帧居中抹平

用法:
  python upscale_sheet.py --src <sheet.png> --cols 5 --rows 3 \\
      --canvas 1200x833 --baseline 825 --height 755 --outdir out [--scale 4] [--device cpu]
"""
import argparse
import os

import numpy as np
import torch
from PIL import Image
from torch import nn
from torch.nn import functional as F

# ---------------------------------------------------------------- Real-ESRGAN 结构
# 与官方 basicsr 的 RRDBNet 完全一致，只取用到的部分，避免装 basicsr 带来的版本冲突。


class ResidualDenseBlock(nn.Module):
    def __init__(self, num_feat=64, num_grow_ch=32):
        super().__init__()
        g = num_grow_ch
        self.conv1 = nn.Conv2d(num_feat, g, 3, 1, 1)
        self.conv2 = nn.Conv2d(num_feat + g, g, 3, 1, 1)
        self.conv3 = nn.Conv2d(num_feat + 2 * g, g, 3, 1, 1)
        self.conv4 = nn.Conv2d(num_feat + 3 * g, g, 3, 1, 1)
        self.conv5 = nn.Conv2d(num_feat + 4 * g, num_feat, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        x1 = self.lrelu(self.conv1(x))
        x2 = self.lrelu(self.conv2(torch.cat((x, x1), 1)))
        x3 = self.lrelu(self.conv3(torch.cat((x, x1, x2), 1)))
        x4 = self.lrelu(self.conv4(torch.cat((x, x1, x2, x3), 1)))
        x5 = self.conv5(torch.cat((x, x1, x2, x3, x4), 1))
        return x5 * 0.2 + x


class RRDB(nn.Module):
    def __init__(self, num_feat, num_grow_ch=32):
        super().__init__()
        self.rdb1 = ResidualDenseBlock(num_feat, num_grow_ch)
        self.rdb2 = ResidualDenseBlock(num_feat, num_grow_ch)
        self.rdb3 = ResidualDenseBlock(num_feat, num_grow_ch)

    def forward(self, x):
        out = self.rdb3(self.rdb2(self.rdb1(x)))
        return out * 0.2 + x


class RRDBNet(nn.Module):
    def __init__(self, num_in_ch=3, num_out_ch=3, num_feat=64, num_block=6, num_grow_ch=32):
        super().__init__()
        self.conv_first = nn.Conv2d(num_in_ch, num_feat, 3, 1, 1)
        self.body = nn.Sequential(*[RRDB(num_feat, num_grow_ch) for _ in range(num_block)])
        self.conv_body = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_up1 = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_up2 = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_hr = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_last = nn.Conv2d(num_feat, num_out_ch, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        feat = self.conv_first(x)
        feat = feat + self.conv_body(self.body(feat))
        feat = self.lrelu(self.conv_up1(F.interpolate(feat, scale_factor=2, mode="nearest")))
        feat = self.lrelu(self.conv_up2(F.interpolate(feat, scale_factor=2, mode="nearest")))
        return self.conv_last(self.lrelu(self.conv_hr(feat)))


def load_model(path, device):
    net = RRDBNet(num_block=6).to(device).eval()
    sd = torch.load(path, map_location="cpu", weights_only=False)
    key = "params_ema" if "params_ema" in sd else ("params" if "params" in sd else None)
    if key:
        sd = sd[key]
    net.load_state_dict(sd, strict=True)
    return net


# ---------------------------------------------------------------- 图像处理


def bleed_rgb(rgb, alpha):
    """把不透明像素的颜色向透明区外扩，避免超分在边缘处混进黑色/杂色。"""
    from scipy import ndimage

    opaque = alpha > 8
    if not opaque.any():
        return rgb
    # 最近的不透明像素索引
    ind = ndimage.distance_transform_edt(~opaque, return_distances=False, return_indices=True)
    filled = rgb[ind[0], ind[1]]
    return filled


def upscale_rgb(net, rgb_u8, device, pad=16):
    """rgb_u8: HxWx3 uint8 → 4x 结果 uint8"""
    h, w, _ = rgb_u8.shape
    x = rgb_u8.astype(np.float32) / 255.0
    x = np.transpose(x, (2, 0, 1))[None]  # 1,3,H,W
    # 反射填充，压掉边缘伪影
    xp = np.pad(x, ((0, 0), (0, 0), (pad, pad), (pad, pad)), mode="reflect")
    t = torch.from_numpy(xp).to(device)
    with torch.no_grad():
        out = net(t)
    out = out.clamp_(0, 1)[0].permute(1, 2, 0).cpu().numpy()
    out = (out * 255.0 + 0.5).astype(np.uint8)
    return out[pad * 4:(pad + h) * 4, pad * 4:(pad + w) * 4]


def upscale_alpha(alpha_u8, size):
    """alpha 用双三次放大，再用软阈值收紧边缘（保留 1px 过渡，避免锯齿）。"""
    im = Image.fromarray(alpha_u8, "L").resize(size, Image.LANCZOS)
    a = np.asarray(im).astype(np.float32) / 255.0
    lo, hi = 0.18, 0.72
    t = np.clip((a - lo) / (hi - lo), 0.0, 1.0)
    a = t * t * (3 - 2 * t)  # smoothstep
    return (a * 255.0 + 0.5).astype(np.uint8)


def cell_grid(img, cols, rows):
    W, H = img.size
    cw, ch = W // cols, H // rows
    for r in range(rows):
        for c in range(cols):
            yield r * cols + c, img.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--cols", type=int, default=5)
    ap.add_argument("--rows", type=int, default=3)
    ap.add_argument("--canvas", default="1200x833")
    ap.add_argument("--baseline", type=int, default=825, help="脚底基线 y")
    ap.add_argument("--height", type=int, default=755, help="角色目标身高(px)")
    ap.add_argument("--center-x", type=int, default=600)
    ap.add_argument("--weights", default=os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                     "weights", "RealESRGAN_x4plus_anime_6B.pth"))
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--device", default="cpu")
    ap.add_argument("--max-frames", type=int, default=0)
    ap.add_argument("--align", choices=("shared", "per-frame", "top"), default="shared",
                    help="shared=全帧共用一个落点（保留帧间起伏，适合跑步）；"
                         "per-frame=每帧脚底锁基线、水平居中（工程既有惯例，跳跃/攻击/受击都用这个）；"
                         "top=每帧头顶锁固定高度、水平居中（空中循环用：下落/滑墙，腿收起时不会把整个人拉低）")
    ap.add_argument("--top", type=int, default=0,
                    help="--align top 时的头顶基线 y；0 表示按 baseline-height 推算")
    a = ap.parse_args()

    CW, CH = [int(v) for v in a.canvas.lower().split("x")]
    os.makedirs(a.outdir, exist_ok=True)

    src = Image.open(a.src).convert("RGBA")
    net = load_model(a.weights, a.device)

    cells = list(cell_grid(src, a.cols, a.rows))
    # 只保留有内容的格
    keep = []
    for idx, c in cells:
        if (np.asarray(c)[..., 3] > 8).any():
            keep.append((idx, c))
    if a.max_frames:
        keep = keep[:a.max_frames]
    print(f"[in ] 源 {src.size}  {a.cols}x{a.rows} 格 → 有效 {len(keep)} 帧")

    # ---- 第一遍：逐格超分，记录各自内容框 ----
    up = []
    for idx, cell in keep:
        arr = np.array(cell)
        alpha = arr[..., 3]
        if not (alpha > 8).any():
            continue
        rgb = bleed_rgb(arr[..., :3], alpha)
        big_rgb = upscale_rgb(net, rgb, a.device)
        big_a = upscale_alpha(alpha, (big_rgb.shape[1], big_rgb.shape[0]))
        out = np.dstack([big_rgb, big_a])
        ys, xs = np.where(big_a > 8)
        up.append(dict(idx=idx, img=Image.fromarray(out, "RGBA"),
                       bbox=(int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)))
        print(f"  frame {idx:02d}  {arr.shape[1]}x{arr.shape[0]} → {big_rgb.shape[1]}x{big_rgb.shape[0]}"
              f"   内容 {xs.min()},{ys.min()} - {xs.max()},{ys.max()}")

    # ---- 统一变换：一个缩放系数 + 一个落点，全部帧共用（保住相对位移）----
    hs = [b["bbox"][3] - b["bbox"][1] for b in up]
    src_h = max(hs)
    scale = a.height / src_h
    # 以「最低的脚」（内容框底边最大者）贴基线
    ref = max(up, key=lambda b: b["bbox"][3])
    ref_bottom = ref["bbox"][3]
    cx = float(np.median([(b["bbox"][0] + b["bbox"][2]) / 2 for b in up]))
    print(f"[fit] 源身高 {src_h}px → 目标 {a.height}px  统一缩放 {scale:.4f}")
    print(f"[fit] 基准帧#{ref['idx']:02d} 底边 {ref_bottom} → 基线 {a.baseline} ; 内容中位 x {cx:.1f} → {a.center_x}")

    # 关键：落点对所有帧**完全相同**（不是逐帧贴基线），否则每帧脚底都被按到 825，
    # 跑步本来该有的腾空起伏就被抹平了，只剩下原地踏步。
    px = int(round(a.center_x - cx * scale))
    py = int(round(a.baseline - ref_bottom * scale))
    print(f"[fit] 统一落点 ({px}, {py}) —— 所有帧共用，帧间上下起伏原样保留")

    files = []
    top_y = a.top if a.top > 0 else (a.baseline - a.height)
    for b in up:
        img = b["img"]
        nw, nh = int(round(img.width * scale)), int(round(img.height * scale))
        r = img.resize((nw, nh), Image.LANCZOS)
        if a.align == "per-frame":
            # 每帧独立对齐：脚底压到基线、水平居中（工程既有惯例）
            fx = ((b["bbox"][0] + b["bbox"][2]) / 2.0) * scale
            fy = b["bbox"][3] * scale
            fx_px = int(round(a.center_x - fx))
            fy_px = int(round(a.baseline - fy))
        elif a.align == "top":
            # 空中循环：每帧头顶压到同一高度、水平居中。
            # 锁脚底会让"收腿"的帧把整个人往下拉，播起来像被压扁；锁头顶则腿收脚升，身体稳住。
            fx = ((b["bbox"][0] + b["bbox"][2]) / 2.0) * scale
            fy = b["bbox"][1] * scale
            fx_px = int(round(a.center_x - fx))
            fy_px = int(round(top_y - fy))
        else:
            fx_px, fy_px = px, py
        canvas = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
        canvas.alpha_composite(r, (fx_px, fy_px))
        name = "frame_%02d.png" % b["idx"]
        canvas.save(os.path.join(a.outdir, name))
        files.append(name)
        ys, xs = np.where(np.asarray(canvas)[..., 3] > 8)
        print(f"  out {name}  内容 x[{xs.min()},{xs.max()}] y[{ys.min()},{ys.max()}]  {xs.max()-xs.min()}x{ys.max()-ys.min()}")

    print(f"[done] {len(files)} 帧 → {a.outdir}")


if __name__ == "__main__":
    main()
