# -*- coding: utf-8 -*-
"""
rig_render.py —— 用切好的部件 + 骨骼角度渲染逐帧动画（2D 骨骼绑定第二步）

角色像素 100% 来自定妆图，不做任何生成/重绘 → 帧间绝对稳定，不会闪烁。

角度约定（关键，别搞反）：
  正值 = 该肢体末端朝「角色面向的方向（右/前方）」摆动
  thigh  正值 = 大腿前摆
  shin   负值 = 膝盖弯曲（小腿向后折）
  arm_u  正值 = 上臂前摆
  arm_f  正值 = 前臂相对上臂向前弯（跑步的屈肘）

用法：
  python rig_render.py --parts 10_rig/parts --outdir 10_rig/frames --n 12
"""
import argparse
import math
import os

import numpy as np
from PIL import Image

# ---------------- 骨骼树 ----------------
NODES = {
    "hip":   dict(part=None,        parent=None,     pivot=(648, 585)),
    "torso": dict(part="torso",     parent="hip",    pivot=(648, 585)),
    "head":  dict(part="head",      parent="torso",  pivot=(620, 352)),
    "arm_u": dict(part="arm_upper", parent="torso",  pivot=(570, 398)),
    "arm_f": dict(part="arm_fore",  parent="arm_u",  pivot=(574, 486)),
    "thigh": dict(part="thigh",     parent="hip",    pivot=(648, 585)),
    "shin":  dict(part="shin",      parent="thigh",  pivot=(645, 668)),
}

# 12 帧膝弯表（对应 cos 相位：i=0 大腿最前、i=6 大腿最后）
# 支撑相 i=0→6 从触地微弯 → 吸收 → 蹬离伸直；摆动相 i=6→11 大幅屈膝再前伸
KNEE = [10, 20, 34, 45, 32, 18, 12, 55, 105, 95, 55, 22]
TILT = -5.0      # 躯干前倾（负值 = 上半身向前/右倾）
SHADE_FAR = 0.86


def solve(name, angles):
    """返回该节点的 (世界 pivot, 绝对角度)。"""
    n = NODES[name]
    a = angles.get(name, 0.0)
    if n["parent"] is None:
        return n["pivot"], a
    pw, pa = solve(n["parent"], angles)
    pn = NODES[n["parent"]]["pivot"]
    dx, dy = n["pivot"][0] - pn[0], n["pivot"][1] - pn[1]
    r = math.radians(pa)
    return (pw[0] + dx * math.cos(r) + dy * math.sin(r),
            pw[1] - dx * math.sin(r) + dy * math.cos(r)), pa + a


def paste_rigid(canvas, part, pivot, world_pivot, angle, shade=1.0, dx=0):
    pg = Image.fromarray(part)
    if shade != 1.0:
        arr = np.array(pg).astype(np.float32)
        arr[..., :3] *= shade
        pg = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    rot = pg.rotate(-angle, center=(float(pivot[0]), float(pivot[1])),
                    resample=Image.BICUBIC, expand=False)
    canvas.alpha_composite(rot, (int(round(world_pivot[0] - pivot[0] + dx)),
                                 int(round(world_pivot[1] - pivot[1]))))


def build_frame(parts, W, H, i, n, o):
    """o: dict(amp, knee_scale, arm, elbow, far_dx_leg, far_dx_arm)"""
    cv = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    th = 2 * math.pi * i / n
    thf = th + math.pi

    k = len(KNEE)
    near = dict(thigh=o["amp"] * math.cos(th),
                knee=KNEE[int(round(i * k / n)) % k] * o["knee_scale"])
    far = dict(thigh=o["amp"] * math.cos(thf),
               knee=KNEE[int(round((i + n / 2) * k / n)) % k] * o["knee_scale"])
    sh_near = -o["arm"] * math.cos(th)
    sh_far = -o["arm"] * math.cos(thf)

    def legs(p, base):
        ang = dict(base, thigh=p["thigh"], shin=-p["knee"])
        return ang

    # ---- 远侧腿 ----
    fa = dict(torso=TILT, **legs(far, {}))
    for nm in ("shin", "thigh"):
        wp, ab = solve(nm, fa)
        paste_rigid(cv, parts[NODES[nm]["part"]], NODES[nm]["pivot"], wp, ab,
                    SHADE_FAR, o["far_dx_leg"])
    # ---- 近侧腿 ----
    na = dict(torso=TILT, **legs(near, {}))
    for nm in ("shin", "thigh"):
        wp, ab = solve(nm, na)
        paste_rigid(cv, parts[NODES[nm]["part"]], NODES[nm]["pivot"], wp, ab)

    # ---- 躯干 ----
    body = dict(torso=TILT, head=-TILT)
    wp, ab = solve("torso", body)
    paste_rigid(cv, parts["torso"], NODES["torso"]["pivot"], wp, ab)

    # ---- 远侧手臂（画在躯干之下会被完全遮住，这里放在躯干之后、
    #      近臂之前，只让它在摆到身体轮廓外时露出手）----
    faa = dict(torso=TILT, arm_u=sh_far, arm_f=sh_far + o["elbow"])
    for nm in ("arm_u", "arm_f"):
        wp, ab = solve(nm, faa)
        paste_rigid(cv, parts[NODES[nm]["part"]], NODES[nm]["pivot"], wp, ab,
                    SHADE_FAR, o["far_dx_arm"])

    # ---- 头 ----
    wp, ab = solve("head", body)
    paste_rigid(cv, parts["head"], NODES["head"]["pivot"], wp, ab)

    # ---- 近侧手臂 ----
    naa = dict(torso=TILT, arm_u=sh_near, arm_f=sh_near + o["elbow"])
    for nm in ("arm_u", "arm_f"):
        wp, ab = solve(nm, naa)
        paste_rigid(cv, parts[NODES[nm]["part"]], NODES[nm]["pivot"], wp, ab)
    return cv


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--parts", default="10_rig/parts")
    ap.add_argument("--outdir", default="10_rig/frames")
    ap.add_argument("--n", type=int, default=12)
    ap.add_argument("--only", default="")
    ap.add_argument("--amp", type=float, default=40.0, help="大腿摆幅°")
    ap.add_argument("--knee", type=float, default=0.78, help="膝弯系数")
    ap.add_argument("--arm", type=float, default=14.0, help="上臂摆幅°")
    ap.add_argument("--elbow", type=float, default=25.0, help="肘弯°")
    ap.add_argument("--far-dx-leg", type=float, default=-8)
    ap.add_argument("--far-dx-arm", type=float, default=-5)
    ap.add_argument("--raw", action="store_true")
    ap.add_argument("--size", default="1200x833")
    ap.add_argument("--scale", type=float, default=1.05)
    ap.add_argument("--base-y", type=int, default=825)
    args = ap.parse_args()

    parts = {}
    for f in os.listdir(args.parts):
        if f.endswith(".png"):
            parts[f[:-4]] = np.array(Image.open(os.path.join(args.parts, f)).convert("RGBA"))
    H, W = parts["torso"].shape[:2]
    print(f"[parts] 画布 {W}x{H}：{sorted(parts)}")

    o = dict(amp=args.amp, knee_scale=args.knee, arm=args.arm, elbow=args.elbow,
             far_dx_leg=args.far_dx_leg, far_dx_arm=args.far_dx_arm)
    os.makedirs(args.outdir, exist_ok=True)
    idxs = [int(x) for x in args.only.split(",")] if args.only else list(range(args.n))
    tw, th_ = (int(v) for v in args.size.split("x"))

    for i in idxs:
        cv = build_frame(parts, W, H, i, args.n, o)
        if not args.raw:
            sw, sh = int(round(W * args.scale)), int(round(H * args.scale))
            z = cv.resize((sw, sh), Image.LANCZOS)
            cv = Image.new("RGBA", (tw, th_), (0, 0, 0, 0))
            cx = (436 + 762) / 2 * args.scale
            cv.alpha_composite(z, (int(round(tw / 2 - cx)),
                                   int(round(args.base_y - 790 * args.scale))))
        p = os.path.join(args.outdir, f"run_{i:02d}.png")
        cv.save(p)
        a = np.array(cv)
        ys, xs = np.where(a[..., 3] > 8)
        print(f"[frame] run_{i:02d} {cv.size} bbox y[{ys.min()},{ys.max()}] "
              f"x[{xs.min()},{xs.max()}]")


if __name__ == "__main__":
    main()
