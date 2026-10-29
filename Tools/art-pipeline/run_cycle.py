#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""run_cycle.py —— 批量出「局部重绘」循环动画帧。

上部躯干永远取自定妆图（像素级），只有 mask 内的腿由 AI 逐帧重画，
每帧给不同的 seed + 跑步相位描述，从而拿到 12 个互不相同的腿部姿态。
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import inpaint_limbs as il  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

# 12 帧跑步循环（面向右，侧视只能看到近侧腿，所以以 near leg 为主语）
PHASES = [
    "near leg extended far forward, heel about to strike the ground, far leg extended back",
    "near foot planted on the ground directly under the hip, far leg lifting behind",
    "lowest point, near knee bent deeply absorbing the landing, far knee driving forward",
    "near leg straightening and pushing off, body rising, far leg swinging forward",
    "airborne, both feet off the ground, near leg trailing behind, far knee high in front",
    "far foot striking the ground in front, near leg lifted up behind",
    "far leg planted directly under the hip, near knee driving forward",
    "lowest point, far knee bent, near knee high up in front",
    "far leg straightening and pushing off, near leg swinging forward",
    "airborne, both feet off the ground, near leg reaching forward, far leg trailing back",
    "near foot striking the ground in front, far leg extended back",
    "near leg planted under the body, body sinking, far knee starting to drive forward",
]

POS_TMPL = (
    "a cute anime boy side view facing right, running fast, {phase}, "
    "wearing dark teal blue-green cargo trousers with side pockets, the trousers are deep "
    "blue-green almost petrol blue, brown leather work boots, "
    "clean cel shading, flat colors, soft simple shading, thin or no outline, "
    "pure white background, full body"
)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", required=True, help="姿势底图（提供腿的起点与画布）")
    ap.add_argument("--ref", required=True, help="定妆图")
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--n", type=int, default=12)
    ap.add_argument("--denoise", type=float, default=0.88)
    ap.add_argument("--seed0", type=int, default=771001)
    ap.add_argument("--seed-step", type=int, default=137)
    ap.add_argument("--wipe-x0", type=int, default=650)
    ap.add_argument("--wipe-y0", type=int, default=500)
    ap.add_argument("--dy", default="", help="每帧垂直偏移，逗号分隔（正=下移）")
    ap.add_argument("--tilt", type=float, default=-8.0, help="躯干前倾角度")
    ap.add_argument("--keep-bd", type=int, default=640,
                    help="定妆图贴图截断线；必须落在 mask 区内，否则旋转会留下斜切白缝")
    ap.add_argument("--mask-y0", type=int, default=574)
    a = ap.parse_args()

    os.makedirs(a.outdir, exist_ok=True)
    dys = [int(v) for v in a.dy.split(",")] if a.dy else [0] * a.n

    comp = os.path.join(a.outdir, "_comp.png")
    comp_p, mask_p = il.compose(a.base, a.ref, comp, a.keep_bd, 380, 820, a.mask_y0, 10,
                                a.wipe_x0, a.wipe_y0, None, a.tilt, 600)

    for i in range(a.n):
        phase = PHASES[i % len(PHASES)]
        pos = POS_TMPL.format(phase=phase)
        seed = a.seed0 + i * a.seed_step
        gen = os.path.join(a.outdir, f"_g{i:02d}.png")
        fin = os.path.join(a.outdir, f"f{i:02d}.png")
        print(f"\n[{i:02d}] {phase}")
        il.run(il.DEFAULT_HOST, None, comp_p, mask_p, gen, a.denoise, seed,
               30, 7.0, pos, il.INPAINT_NEG, 900)
        il.blend(comp_p, mask_p, gen, fin)


if __name__ == "__main__":
    main()
