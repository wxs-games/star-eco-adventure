# -*- coding: utf-8 -*-
"""
rig_cut.py —— 把定妆图按关节拆成可独立旋转的部件（2D 骨骼绑定第一步）

不做任何重绘，角色像素原样切开。

两个关键技术点：
1. 切分靠「颜色 + 连通域」而不是矩形。因为侧视图里手垂在大腿旁，手臂与腿的
   矩形范围重叠，矩形切会把腿切成手臂。
2. 每个部件在关节处补一个「填充圆盘」：绕自身圆心旋转时圆盘形状不变，
   所以肢体大幅摆动时关节处不会露出透明洞。圆盘用该部位衣物的主色填充。

用法：
  python rig_cut.py --src 10_rig/char_alpha.png --outdir 10_rig/parts
"""
import argparse
import json
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

# ---------------- 关节坐标（1216x832 原图坐标系，目视标定） ----------------
J = dict(
    neck=(620, 352),
    shoulder=(570, 398),
    elbow=(574, 486),
    hip=(648, 585),
    knee=(645, 690),
)
HEAD_Y = 358       # 颈以下归躯干
TORSO_Y1 = 578     # 皮带下沿，以下归腿
LEG_X0 = 616       # 腿区左边界：只取「前侧那条腿」，避免整块粗腿旋转显笨
R_NECK, R_SHOULDER, R_ELBOW, R_HIP, R_KNEE = 30, 40, 26, 46, 34

COLORS = {
    "head": (255, 90, 90), "torso": (90, 160, 255),
    "arm_upper": (250, 215, 40), "arm_fore": (40, 210, 150),
    "thigh": (200, 110, 255), "shin": (255, 150, 50),
}


def load_clean(path):
    im = Image.open(path).convert("RGBA")
    a = np.array(im)
    fg = a[..., 3] > 8
    lab, n = ndimage.label(fg, structure=np.ones((3, 3)))
    info = []
    if n > 1:
        sz = ndimage.sum(fg, lab, range(1, n + 1))
        keep = int(np.argmax(sz)) + 1
        for k in range(1, n + 1):
            info.append((k, int(sz[k - 1]), k == keep))
        a[fg & (lab != keep), 3] = 0
        fg = a[..., 3] > 8
    return a, fg, info


def disk(shape, c, r):
    h, w = shape
    yy, xx = np.ogrid[:h, :w]
    return (xx - c[0]) ** 2 + (yy - c[1]) ** 2 <= r * r


def biggest(mask, grow=4, limit=None):
    """取最大连通域并膨胀（把描边吃进来）。"""
    lab, n = ndimage.label(mask, structure=np.ones((3, 3)))
    if n == 0:
        return np.zeros_like(mask)
    sz = ndimage.sum(mask, lab, range(1, n + 1))
    m = lab == (int(np.argmax(sz)) + 1)
    if grow:
        m = ndimage.binary_dilation(m, np.ones((3, 3)), iterations=grow)
    if limit is not None:
        m &= limit
    return m


def med_color(a, mask):
    c = a[mask][:, :3]
    return tuple(int(v) for v in np.median(c, axis=0)) if len(c) else (200, 200, 200)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="10_rig/char_alpha.png")
    ap.add_argument("--outdir", default="10_rig/parts")
    ap.add_argument("--preview", default=None)
    args = ap.parse_args()

    a, fg, info = load_clean(args.src)
    h, w = fg.shape
    Y = np.arange(h)[:, None].repeat(w, 1)
    X = np.arange(w)[None, :].repeat(h, 0)

    R, G, B = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    mx = a[..., :3].max(axis=2).astype(int)
    mn = a[..., :3].min(axis=2).astype(int)

    skin = fg & (R > 222) & (G > 175) & (G < 245) & (B > 145) & (B < 225) & (R > G + 10)
    white = fg & (mx > 195) & (mx - mn < 34)
    pants = fg & (B > G + 4) & (G > R + 4) & (R < 115) & (B > 45) & (B < 165)
    boot = fg & (R > 82) & (R < 195) & (G > 45) & (G < 150) & (B < 120) & (R > G + 12) & (G > B + 4)

    print(f"[load] 连通域 {len(info)} 个：" + ", ".join(
        f"#{k}({sz}px,{'保留' if kept else '剔除'})" for k, sz, kept in info))

    # ---- 腿：裤色|靴色（自动排除肤色手臂）----
    # 注意：裤子与靴子是两块互不相连的色块，不能取「最大连通域」，
    # 否则靴子会被丢掉。改成整体膨胀把描边吃进来，再用形状先验限定。
    legzone = (Y >= 556) & (X >= LEG_X0)
    legcore = (pants | boot) & legzone
    leg = ndimage.binary_dilation(legcore, np.ones((3, 3)), iterations=3) & fg

    # ---- 手臂：前臂=肤色连通域，上臂=白袖连通域 ----
    forezone = (Y > 455) & (Y < 640) & (X > 520) & (X < 640)
    arm_fore = biggest(skin & forezone, grow=5, limit=fg & forezone)

    upzone = (Y > 375) & (Y < 492) & (X > 528) & (X < 615)
    arm_upper = biggest(white & upzone, grow=5, limit=fg & upzone)

    # ---- 头 / 躯干 ----
    # 躯干刻意「不挖掉手臂」：手臂像素在躯干里再留一份当底衬。
    # 手臂摆开时底下露的是白袖/前臂而不是透明洞，代价只是残影（可当衣褶看）。
    head = fg & (Y < HEAD_Y)
    torso = fg & (Y >= HEAD_Y) & (Y < TORSO_Y1)

    # ---- 大腿 / 小腿 ----
    leg2 = leg & ~arm_fore & ~arm_upper
    thigh = leg2 & (Y < J["knee"][1])
    shin = leg2 & (Y >= J["knee"][1])

    parts = dict(head=head, torso=torso, arm_upper=arm_upper,
                 arm_fore=arm_fore, thigh=thigh, shin=shin)
    cover = np.zeros_like(fg)
    for m in parts.values():
        cover |= m
    lost = fg & ~cover
    if lost.sum():
        # 未归属像素：腰线以上补进躯干、以下补进腿。
        # 绝不能把下半身的零散像素塞进躯干，否则会跟着躯干一起倾，腿就断了。
        up, dn = lost & (Y < TORSO_Y1), lost & (Y >= TORSO_Y1)
        print(f"[warn] 未归属 {int(lost.sum())}px → torso {int(up.sum())} / 腿 {int(dn.sum())}")
        parts["torso"] |= up
        parts["thigh"] |= dn & (X >= LEG_X0 - 4)   # 只收前腿，后腿像素由合成的远腿负责

    # ---- 关节圆盘的填充色（取该部位衣物主色）----
    fill = {
        "head": med_color(a, skin & (Y > 335) & (Y < 356)),
        "torso": med_color(a, fg & (G > 100) & (G > R + 25) & (G > B + 25)),
        "arm_upper": med_color(a, fg & (G > 100) & (G > R + 25) & (G > B + 25)),
        "arm_fore": med_color(a, skin & forezone),
        "thigh": med_color(a, pants & (Y > 590) & (Y < 660)),
        "shin": med_color(a, pants & (Y > 590) & (Y < 660)),
    }
    print("[fill] 关节补色：" + "  ".join(f"{k}=#{c[0]:02x}{c[1]:02x}{c[2]:02x}" for k, c in fill.items()))

    # 每个部件：(圆盘中心, 半径)
    pdisk = {
        "head": (J["neck"], R_NECK),
        "torso": (None, 0),
        "arm_upper": (J["shoulder"], R_SHOULDER),
        "arm_fore": (J["elbow"], R_ELBOW),
        "thigh": ((648, 596), R_HIP),
        "shin": (J["knee"], R_KNEE),
    }

    os.makedirs(args.outdir, exist_ok=True)
    for name, m in parts.items():
        b = a.copy()
        b[~m, 3] = 0
        c, r = pdisk[name]
        if c is not None:
            d = disk((h, w), c, r)
            plug = d & ~m          # 圆盘里肢体没盖到的部分
            if plug.any():
                # 最近邻填充：用「部件自身最近的像素」补缺口，颜色与描边自然延续。
                # 比纯色平涂自然得多（纯色圆盘在裤子上会像贴了块膏药）。
                ind = ndimage.distance_transform_edt(
                    ~m, return_distances=False, return_indices=True)
                sy, sx = ind[0][plug], ind[1][plug]
                b[plug, :3] = a[sy, sx, :3]
                b[plug, 3] = 255
        Image.fromarray(b).save(os.path.join(args.outdir, f"{name}.png"))
        ys, xs = np.where(m)
        print(f"[part] {name:10s} {int(m.sum()):7d}px "
              f"y[{ys.min()},{ys.max()}] x[{xs.min()},{xs.max()}]")

    # ---- 着色预览（含圆盘）----
    prev = np.array(Image.open(args.src).convert("RGBA"))
    prev[..., 3] = (prev[..., 3] * 0.22).astype(np.uint8)
    for name, m in parts.items():
        c, r = pdisk[name]
        extra = disk((h, w), c, r) & ~m if c is not None else np.zeros_like(m)
        for i in range(3):
            prev[m, i] = (0.4 * prev[m, i] + 0.6 * COLORS[name][i]).astype(np.uint8)
        prev[m, 3] = 255
        prev[extra, 0], prev[extra, 1], prev[extra, 2] = COLORS[name]
        prev[extra, 3] = 150
    pim = Image.fromarray(prev)
    d = ImageDraw.Draw(pim)
    try:
        f = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)
    except Exception:
        f = ImageFont.load_default()
    for k, v in J.items():
        d.ellipse([v[0] - 4, v[1] - 4, v[0] + 4, v[1] + 4], outline=(255, 0, 0), width=2)
        d.text((v[0] + 8, v[1] - 8), k, fill=(190, 0, 0), font=f)
    out = args.preview or os.path.join(os.path.dirname(args.outdir), "_parts_color.png")
    pim.save(out)
    print(f"[preview] {out}")

    with open(os.path.join(args.outdir, "joints.json"), "w", encoding="utf-8") as fp:
        json.dump(J, fp, indent=2)
    print(f"[done] {args.outdir}")


if __name__ == "__main__":
    main()
