#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""inpaint_limbs.py —— 局部重绘：把定妆图的躯干像素级保留，只让 AI 重画四肢。

为什么需要它（相对 comfy_gen.py 的全局 img2img）：
    全局 img2img 的 denoise 是"整张图一个旋钮"。加到 0.55 以下姿态不动，
    加到 0.80 以上脸和衣服就全被重画成模型心里的"通用卡通男孩"。
    局部重绘把旋钮的作用范围限制在 mask 内，mask 外的像素最后用原图强制贴回，
    因此头/帽/马甲/白T/水壶可以做到 100% 来自定妆图。

三步（可分别执行）：
    1. --compose  合成底图 + 生成 mask，纯本地，不出网
    2. --run      提交 ComfyUI 做 inpaint（SetLatentNoiseMask，mask 外不动）
    3. --blend    把 mask 外像素强制还原成合成底图（消除 VAE 往返回损）

用法：
    python inpaint_limbs.py --compose --base 帧.png --ref 定妆.png --out raw.png
    python inpaint_limbs.py --run --raw raw.png --mask m.png --out gen.png --denoise 0.65
    python inpaint_limbs.py --blend --raw raw.png --mask m.png --gen gen.png --out final.png
"""
import argparse
import io
import json
import os
import sys
import time
import urllib.parse
import urllib.request

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from comfy_gen import DEFAULT_HOST, _OPENER, get_json, post_json, upload_image  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")


# --------------------------------------------------------------------------- 合成

def bg_mask_whiteness(img_rgb, thr=238):
    """返回"角色前景"布尔图：与画面四边连通的近白像素判为背景。

    定妆图内部有白色 T 恤，不能用"是否白"直接判前景，
    否则 T 恤会被当成背景挖空。
    """
    a = np.asarray(img_rgb).astype(np.int16)
    near_white = a.min(axis=2) >= thr
    h, w = near_white.shape
    # 从四边向内做 BFS 扩散，只标记与边界连通的白色
    from collections import deque
    bg = np.zeros((h, w), bool)
    dq = deque()
    for x in range(w):
        for y in (0, h - 1):
            if near_white[y, x] and not bg[y, x]:
                bg[y, x] = True
                dq.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if near_white[y, x] and not bg[y, x]:
                bg[y, x] = True
                dq.append((y, x))
    while dq:
        y, x = dq.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and near_white[ny, nx] and not bg[ny, nx]:
                bg[ny, nx] = True
                dq.append((ny, nx))
    return ~bg


def compose(base_p, ref_p, out_p, keep_bd, mask_x0, mask_x1, mask_y0, feather,
            wipe_x0=None, wipe_y0=None, arm=None, tilt=0.0, hip_x=600):
    base = Image.open(base_p).convert("RGB")
    ref = Image.open(ref_p).convert("RGB")
    if base.size != ref.size:
        raise SystemExit(f"尺寸不一致：base {base.size} vs ref {ref.size}")
    W, H = base.size

    # 跑步时躯干应前倾：绕髋部把定妆图整体转一点，避免"直立走路感"
    if abs(tilt) > 1e-6:
        ref = ref.rotate(tilt, center=(hip_x, keep_bd), resample=Image.BICUBIC,
                         expand=False, fillcolor=(255, 255, 255))
        print(f"[compose] 躯干前倾 {tilt}°（绕髋部 x={hip_x} y={keep_bd}）")

    ref_a = np.asarray(ref).astype(np.float32)
    fg = bg_mask_whiteness(Image.fromarray(ref_a.astype(np.uint8)))
    # 软 alpha：离白越远越不透明，保留定妆图自己的抗锯齿边缘
    soft = np.clip((255.0 - ref_a.min(axis=2)) / 28.0, 0.0, 1.0)
    alpha = np.where(fg, np.maximum(soft, 0.85), 0.0)[..., None]

    # 画布：躯干区以上全部刷白（清掉底图残留的旧上身/书包/速度线）
    canvas = np.asarray(base).astype(np.float32).copy()
    canvas[:keep_bd, :, :] = 255.0
    # 贴定妆图躯干（只贴 keep_bd 以上）
    top = ref_a[:keep_bd]
    al = alpha[:keep_bd]
    canvas[:keep_bd] = top * al + canvas[:keep_bd] * (1.0 - al)
    canvas_img = Image.fromarray(np.clip(canvas, 0, 255).astype(np.uint8))
    canvas_img.save(out_p)

    # mask：白色=重绘。腿区主矩形 + 右侧扫除条（清掉底图残留的水花/速度线），再羽化
    mk = np.zeros((H, W), np.uint8)
    mk[mask_y0:H, mask_x0:mask_x1] = 255
    if wipe_x0 is not None and wipe_y0 is not None:
        mk[wipe_y0:H, wipe_x0:] = 255
    if arm:
        ax0, ax1, ay0, ay1 = arm
        mk[ay0:ay1, ax0:ax1] = 255
    mk_img = Image.fromarray(mk, "L").filter(ImageFilter.GaussianBlur(feather))
    mask_p = os.path.splitext(out_p)[0] + "_mask.png"
    Image.fromarray(np.asarray(mk_img)).save(mask_p)
    white = 100.0 * (np.asarray(mk_img) > 127).mean()
    print(f"[compose] 底图 -> {out_p}")
    print(f"[compose] mask -> {mask_p}  重绘面积 {white:.1f}%  羽化 {feather}px")
    print(f"[compose] 躯干保留区 y<{keep_bd}，腿区 x[{mask_x0},{mask_x1}) y[{mask_y0},{H})"
          + (f" + 扫除条 x>={wipe_x0} y>={wipe_y0}" if wipe_x0 is not None else "")
          + (f" + 手臂区 {arm}" if arm else ""))
    # mask 叠色预览，便于人工确认覆盖范围
    pv = np.asarray(canvas_img).astype(np.float32).copy()
    mkf = np.asarray(mk_img).astype(np.float32)[..., None] / 255.0
    red = np.zeros_like(pv)
    red[..., 0] = 255.0
    pv = pv * (1 - mkf * 0.55) + red * (mkf * 0.55)
    pv_p = os.path.splitext(out_p)[0] + "_maskview.png"
    Image.fromarray(np.clip(pv, 0, 255).astype(np.uint8)).save(pv_p)
    print(f"[compose] mask 预览 -> {pv_p}")
    return out_p, mask_p


# --------------------------------------------------------------------------- 出图

INPAINT_POS = (
    "a cute anime boy side view facing right, running, mid-stride, legs wide apart, "
    "wearing dark teal blue-green cargo trousers with side pockets, the trousers are deep "
    "blue-green almost petrol blue, brown leather work boots, "
    "clean cel shading, flat colors, soft simple shading, thin or no outline, "
    "pure white background, full body, feet on ground"
)
INPAINT_NEG = (
    "olive green pants, grass green pants, light green trousers, gray pants, khaki pants, "
    "sneakers, running shoes, white socks, "
    "thick black outline, heavy ink lines, text, watermark, signature, blurry, "
    "motion lines, speed lines, splash, water drops, shadow on ground, cropped, extra limbs, deformed"
)


def build_inpaint_wf(ckpt, pos, neg, seed, steps, cfg, sampler, scheduler, denoise,
                     image_name, mask_name, prefix):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": ckpt}},
        "2": {"class_type": "CLIPTextEncode", "inputs": {"text": pos, "clip": ["1", 1]}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": neg, "clip": ["1", 1]}},
        "8": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "9": {"class_type": "LoadImage", "inputs": {"image": mask_name}},
        "10": {"class_type": "ImageToMask", "inputs": {"image": ["9", 0], "channel": "red"}},
        "11": {"class_type": "VAEEncode", "inputs": {"pixels": ["8", 0], "vae": ["1", 2]}},
        "12": {"class_type": "SetLatentNoiseMask", "inputs": {"samples": ["11", 0], "mask": ["10", 0]}},
        "5": {"class_type": "KSampler", "inputs": {
            "model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0],
            "latent_image": ["12", 0],
            "seed": seed, "steps": steps, "cfg": cfg,
            "sampler_name": sampler, "scheduler": scheduler, "denoise": denoise,
        }},
        "6": {"class_type": "VAEDecode", "inputs": {"samples": ["5", 0], "vae": ["1", 2]}},
        "7": {"class_type": "SaveImage", "inputs": {"images": ["6", 0], "filename_prefix": prefix}},
    }


def first_ckpt(host):
    info = get_json(host, "/object_info/CheckpointLoaderSimple", timeout=120)
    lst = info["CheckpointLoaderSimple"]["input"]["required"]["ckpt_name"][0]
    return lst[0] if lst else None


def run(host, ckpt, raw_p, mask_p, out_p, denoise, seed, steps, cfg, pos, neg, timeout):
    if not ckpt:
        ckpt = first_ckpt(host)
    img_name = upload_image(host, raw_p)
    mask_name = upload_image(host, mask_p)
    prefix = "inpaint_" + os.path.splitext(os.path.basename(out_p))[0]
    wf = {"prompt": build_inpaint_wf(ckpt, pos, neg, seed, steps, cfg,
                                     "dpmpp_2m", "karras", denoise,
                                     img_name, mask_name, prefix)}
    print(f"[run] 底模 {ckpt}  denoise={denoise}  seed={seed}  {raw_p}")
    resp = post_json(host, "/prompt", wf, timeout=120)
    pid = resp.get("prompt_id")
    if not pid:
        raise SystemExit(f"提交失败：{resp}")
    t0 = time.time()
    while time.time() - t0 < timeout:
        hist = get_json(host, f"/history/{pid}", timeout=60)
        node = hist.get(pid)
        if node and node.get("outputs"):
            outs = node["outputs"]
            for k, v in outs.items():
                for im in v.get("images", []):
                    q = urllib.parse.urlencode({"filename": im["filename"],
                                                "subfolder": im.get("subfolder", ""),
                                                "type": im.get("type", "output")})
                    with _OPENER.open(f"http://{host}/view?{q}", timeout=300) as r:
                        blob = r.read()
                    Image.open(io.BytesIO(blob)).save(out_p)
                    print(f"[run] -> {out_p}  ({time.time()-t0:.1f}s)")
                    return out_p
        if node and node.get("status", {}).get("status_str") == "error":
            raise SystemExit(f"ComfyUI 执行失败：{json.dumps(node.get('status'), ensure_ascii=False)[:600]}")
        time.sleep(1.5)
    raise SystemExit("超时")


def blend(raw_p, mask_p, gen_p, out_p, thr=127):
    """mask 外强制用合成底图的原始像素，杜绝 VAE 往返带来的整体色偏/细节丢失。"""
    raw = np.asarray(Image.open(raw_p).convert("RGB")).astype(np.uint8)
    gen = np.asarray(Image.open(gen_p).convert("RGB")).astype(np.uint8)
    mk = np.asarray(Image.open(mask_p).convert("L"))
    if gen.shape != raw.shape:
        gen = np.asarray(Image.fromarray(gen).resize((raw.shape[1], raw.shape[0]), Image.LANCZOS))
    # 羽化带做线性混合，硬区直接取
    a = (mk.astype(np.float32) / 255.0)[..., None]
    out = gen.astype(np.float32) * a + raw.astype(np.float32) * (1.0 - a)
    print(f"[blend] mask 外保留原像素 {(mk <= thr).mean()*100:.1f}%")
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(out_p)
    return out_p


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", help="姿势底图（姿态正确、外形不对的帧）")
    ap.add_argument("--ref", help="定妆图（外形唯一真源）")
    ap.add_argument("--keep-bd", type=int, default=582, help="躯干保留区下界 y")
    ap.add_argument("--mask-x0", type=int, default=380)
    ap.add_argument("--mask-x1", type=int, default=820)
    ap.add_argument("--mask-y0", type=int, default=574)
    ap.add_argument("--feather", type=int, default=10)
    ap.add_argument("--wipe-x0", type=int, default=None, help="额外扫除条左界 x（清残影用）")
    ap.add_argument("--wipe-y0", type=int, default=None, help="额外扫除条上界 y")
    ap.add_argument("--arm", help="手臂重绘矩形 x0,x1,y0,y1")
    ap.add_argument("--tilt", type=float, default=0.0, help="躯干前倾角度（负值=面右角色前倾）")
    ap.add_argument("--hip-x", type=int, default=600, help="髋部中心 x，倾斜旋转点")
    ap.add_argument("--raw", help="合成底图路径（--run/--blend 的输入）")
    ap.add_argument("--mask", help="mask 路径")
    ap.add_argument("--gen", help="ComfyUI 原始出图")
    ap.add_argument("--out")
    ap.add_argument("--denoise", type=float, default=0.65)
    ap.add_argument("--seed", type=int, default=20261027)
    ap.add_argument("--steps", type=int, default=30)
    ap.add_argument("--cfg", type=float, default=7.0)
    ap.add_argument("--prompt", default=INPAINT_POS)
    ap.add_argument("--negative", default=INPAINT_NEG)
    ap.add_argument("--host", default=DEFAULT_HOST)
    ap.add_argument("--ckpt", default=os.environ.get("COMFY_CKPT"))
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--compose", action="store_true")
    ap.add_argument("--run", action="store_true")
    ap.add_argument("--blend", action="store_true")
    a = ap.parse_args(argv)

    if a.compose:
        if not (a.base and a.ref and a.out):
            raise SystemExit("--compose 需要 --base --ref --out")
        arm = tuple(int(v) for v in a.arm.split(",")) if a.arm else None
        compose(a.base, a.ref, a.out, a.keep_bd, a.mask_x0, a.mask_x1, a.mask_y0, a.feather,
                a.wipe_x0, a.wipe_y0, arm, a.tilt, a.hip_x)
    if a.run:
        if not (a.raw and a.mask and a.out):
            raise SystemExit("--run 需要 --raw --mask --out")
        run(a.host, a.ckpt, a.raw, a.mask, a.out, a.denoise, a.seed, a.steps, a.cfg,
            a.prompt, a.negative, a.timeout)
    if a.blend:
        if not (a.raw and a.mask and a.gen and a.out):
            raise SystemExit("--blend 需要 --raw --mask --gen --out")
        blend(a.raw, a.mask, a.gen, a.out)


if __name__ == "__main__":
    main()
