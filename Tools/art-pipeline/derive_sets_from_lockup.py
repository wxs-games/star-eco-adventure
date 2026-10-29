# -*- coding: utf-8 -*-
"""定妆图派生：以「某一帧」为像素基准，派生同一角色的其它动作套。

## 什么时候必须用这个方法
当定妆图被指定成「某个动作的某一帧」（例如「炮塔射击的第一帧就是定妆图」），
其余动作的同一部件必须与它**像素级完全一致** —— 文生图做不到，
`process_sheets.py` 的生成式管线同样做不到（每帧都会重画）。
这时不要再让模型生成，改为以该帧为底图，**逐帧只在背景区叠加效果**。

## 脚本强制的硬约束（不靠人眼检查）
- 输出画布 = 底图画布尺寸，角色在格内的像素坐标与底图逐点相同 → 多套动作**零位移**，
  动画套之间切换时角色不会跳位、不会突然变大变小；
- 只在 `alpha <= 8` 的背景区叠加；角色像素（`alpha > 8`）除「整体变灰」这类
  语义效果外逐点不变；
- 输出沿用 `<Frames>/<名>/<名>_NN.png` 的原名与原帧数，`.meta`/guid 不变
  → 已绑定的 `.anim` 引用不会失效。

## 怎么改成你自己的角色（只动这三处）
1. **CONFIG**：`CHAR_DIR` / `BASE_REL` / `CELL`（画布尺寸必须与底图一致）；
2. **SETS**：要派生哪几套、每套的 `cols`、用哪个 `effect`、每帧参数 `plan`；
3. **`*_frame()` 效果函数**里的几何锚点 —— 示例里的 `FAN_C` / `FAN_R` / `K_BARREL`
   是针对示例角色（双烟囱炮塔）实测出来的，换图要重新量。

## 用法
    python derive_sets_from_lockup.py --dry-run              # 只报告，不写盘
    python derive_sets_from_lockup.py                        # 按 CONFIG 写回工程
    python derive_sets_from_lockup.py --out-dir <临时目录>     # 写到别处，便于和原素材比对
"""
import sys, os, argparse
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from PIL import Image
from scipy import ndimage

# ============================== CONFIG ==============================
# 工程根 -> 角色目录（脚本位于 <工程根>\Tools\art-pipeline\，向上两级即工程根）
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
CHAR_DIR = os.path.join(ROOT, 'Assets', 'Art', 'Art', 'Towers', 'Tower01')   # ← 换角色改这里
BASE_REL = os.path.join('Frames', 'Tower01_Fire', 'Tower01_Fire_00.png')      # ← 定妆帧（像素基准）
CELL = (1039, 979)          # 画布尺寸，必须与定妆帧一致
ALPHA_MIN = 8

# --- 示例角色的配色与几何锚点（换图请重新量）---
GREEN = np.array([124.0, 236.0, 72.0])       # 蓄能绿
WARM = np.array([255.0, 96.0, 48.0])         # 警戒灯暖光
ARC_RGB = np.array([232.0, 232.0, 232.0])    # 风影弧
SMOKE_RGB = np.array([168.0, 168.0, 168.0])  # 停机烟
FAN_C = (695.0, 705.0)      # 风扇轮中心（底图像素坐标）
FAN_R = 95.0                # 风扇轮半径
K_BARREL = 1.6              # 炮口绿气沿炮管方向的拉伸系数

# --- 要派生的动作套 ---
# effect: 'lamp_pulse' 暖光脉动 | 'muzzle_charge' 炮口蓄能 | 'gray_out' 去饱和停机
# plan:   每一帧的效果参数，含义见对应 *_frame() 函数
SETS = [
    # 塔体不动 + 罐顶警戒灯辉光脉动 + 风扇轮外侧风影弧（循环 4 帧）
    dict(name='Tower01_Idle', cols=2, effect='lamp_pulse',
         plan=[(0.0, None), (0.50, 18.0), (0.85, 36.0), (0.50, 0.0)]),
    # 塔体不动 + 炮口绿气蓄能，由弱到强，末帧最强（攻击前摇）
    dict(name='Tower01_Aim', cols=2, effect='muzzle_charge',
         plan=[(16, 0.0), (26, 0.62), (38, 0.80), (52, 0.96)]),
    # 塔体几何不动 + 逐帧去饱和变灰 + 顶部冒烟；末帧即「全灰停机」终态
    # 帧数必须与实际 .anim 键数一致（示例 Disable.anim 只有 5 个键）
    dict(name='Tower01_Disable', cols=3, effect='gray_out',
         plan=[(0.00, 1.00, 0.00), (0.10, 1.00, 0.22), (0.32, 0.99, 0.50),
               (0.65, 0.97, 0.78), (1.00, 0.92, 1.00)]),
]
# ====================================================================

FRAMES = os.path.join(CHAR_DIR, 'Frames')
SHEETS = os.path.join(CHAR_DIR, 'Sheets')
BASE_PNG = os.path.join(CHAR_DIR, BASE_REL)


# ---------- 基础工具 ----------

def load_base():
    im = Image.open(BASE_PNG).convert('RGBA')
    a = np.asarray(im).astype(np.float32)
    assert (a.shape[1], a.shape[0]) == CELL, f'底图尺寸 {a.shape[1]}x{a.shape[0]} != {CELL}'
    return a


def overlay(base, cov, rgb, fg=None):
    """把覆盖度 cov 叠加到 base 的背景区，返回新 RGBA（uint8 数组）。
    cov 在塔体像素（fg）上强制归零 —— 保证塔体逐点不变。"""
    if fg is None:
        fg = base[..., 3] > ALPHA_MIN
    a = base[..., 3]
    cov = np.clip(cov, 0.0, 1.0).copy()
    cov[fg] = 0.0
    out_a = np.maximum(a, cov * 255.0)
    w = (cov * 255.0) / np.maximum(out_a, 1.0)
    out_rgb = base[..., :3] * (1.0 - w[..., None]) + rgb * w[..., None]
    return np.clip(np.dstack([out_rgb, out_a]), 0, 255).astype(np.uint8)


def radial_field(shape, cx, cy, ):
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]]
    return np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2), xx - cx, yy - cy


def red_blobs(a):
    """红色部件（罐顶警戒灯 / 塔身红色圆灯）的 (cy, cx, 等效半径)，按 y 从小到大排序。"""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    m = (r > 150) & (g < 120) & (b < 120) & (r - np.maximum(g, b) > 55)
    lbl, n = ndimage.label(m)
    out = []
    for k in range(1, n + 1):
        ys, xs = np.where(lbl == k)
        if len(ys) < 150:
            continue
        out.append((ys.mean(), xs.mean(), float(np.sqrt(len(ys) / np.pi))))
    out.sort()
    return out


def muzzle_point(alpha):
    """炮口：最左 10 列不透明像素质心。"""
    ys, xs = np.where(alpha > ALPHA_MIN)
    x0 = xs.min()
    sel = xs <= x0 + 10
    return ys[sel].mean(), xs[sel].mean()


def barrel_dir(fg, my, mx):
    """炮管轴向 = 炮口 - 炮口附近塔体质心。"""
    yy, xx = np.mgrid[0:fg.shape[0], 0:fg.shape[1]]
    near = fg & ((yy - my) ** 2 + (xx - mx) ** 2 <= 80 ** 2)
    cy, cx = yy[near].mean(), xx[near].mean()
    v = np.array([mx - cx, my - cy], float)
    n = float(np.hypot(*v))
    return v / n if n > 1e-6 else np.array([-1.0, 0.0])


# ---------- 三套的逐帧效果 ----------

def idle_frame(base, cap, halo_s, arc_deg):
    H, W = base.shape[:2]
    fg = base[..., 3] > ALPHA_MIN
    d, _, _ = radial_field((H, W), cap[1], cap[0])
    r = max(46.0, 2.0 * cap[2])
    out = overlay(base, (np.clip(1.0 - d / r, 0.0, 1.0) ** 1.6) * halo_s, WARM, fg)
    if arc_deg is not None:
        df, dx, dy = radial_field((H, W), *FAN_C)
        ang = np.degrees(np.arctan2(dy, dx))
        # 风影弧紧贴风扇轮缘：带内半圈压在轮盘上（被塔体遮掉），外半圈才露出，
        # 因此露出的弧必然与轮缘相邻 -> 不会变成游离背景的孤立灰斑。
        band = np.clip(1.0 - np.abs(df - (FAN_R + 4.0)) / 16.0, 0.0, 1.0)
        wnd = np.zeros_like(df)
        for base_ang in (-40.0, 0.0, 40.0):                          # 三道风影弧
            delta = (ang - (base_ang + arc_deg) + 180.0) % 360.0 - 180.0
            wnd = np.maximum(wnd, np.clip(1.0 - np.abs(delta) / 24.0, 0.0, 1.0))
        out = overlay(out.astype(np.float32), band * wnd * 0.45, ARC_RGB, fg)
    return out


def aim_frame(base, r, strength):
    H, W = base.shape[:2]
    alpha = base[..., 3]
    fg = alpha > ALPHA_MIN
    my, mx = muzzle_point(alpha)
    ux, uy = barrel_dir(fg, my, mx)
    _, dx, dy = radial_field((H, W), mx, my)
    along = dx * ux + dy * uy
    perp = -dx * uy + dy * ux
    d = np.sqrt((along / K_BARREL) ** 2 + perp ** 2)
    cov = (np.clip(1.0 - d / r, 0.0, 1.0) ** 1.5) * strength
    return overlay(base, cov, GREEN)


def disable_frame(base, t, dim, smoke):
    """t: 去饱和程度 0..1；dim: 亮度系数；smoke: (中心x, 中心y, 半径, 强度) 列表"""
    a = base[..., 3]
    rgb = base[..., :3]
    gray = (rgb @ np.array([0.299, 0.587, 0.114], np.float32))[..., None]
    out_rgb = (rgb * (1.0 - t) + gray * t) * dim
    out = np.clip(np.dstack([out_rgb, a]), 0, 255).astype(np.uint8)
    if smoke:
        H, W = a.shape
        cov = np.zeros((H, W), np.float32)
        for (sx, sy, sr, ss) in smoke:
            d, _, _ = radial_field((H, W), sx, sy)
            cov = np.maximum(cov, np.clip(1.0 - d / sr, 0.0, 1.0) ** 1.2 * ss)
        out = overlay(out.astype(np.float32), cov, SMOKE_RGB)
    return out


def smoke_plume(sx, sy, t):
    """按帧进度 t(0..1) 生成一股上升烟柱的烟团；t=0 时无烟。"""
    if t <= 0:
        return []
    puffs = []
    for k, (rise, rad, ss) in enumerate(((18.0, 26.0, 0.50), (48.0, 34.0, 0.42),
                                         (84.0, 42.0, 0.34), (126.0, 48.0, 0.24))):
        puffs.append((sx + (k % 2) * 20.0 - 6.0,
                      sy - rise * (0.35 + 0.65 * t),
                      rad * (0.5 + 0.5 * t),
                      ss * t))
    return puffs


# ---------- 输出 ----------

def write_set(name, imgs, cols, dry=False):
    d = os.path.join(FRAMES, name)
    if not dry:
        os.makedirs(d, exist_ok=True)
        for i, im in enumerate(imgs):
            im.save(os.path.join(d, f'{name}_{i:02d}.png'))
    rows = (len(imgs) + cols - 1) // cols
    cw, ch = CELL
    sheet = Image.new('RGBA', (cw * cols, ch * rows), (0, 0, 0, 0))
    for i, im in enumerate(imgs):
        sheet.paste(im, ((i % cols) * cw, (i // cols) * ch))
    if dry:
        print(f'  [DRY] {name}: {len(imgs)} 帧  {cols}x{rows}  sheet={sheet.size}')
        return
    sp = os.path.join(SHEETS, f'{name}_sheet.png')
    os.makedirs(SHEETS, exist_ok=True)
    sheet.save(sp)
    praw = os.path.join(SHEETS, 'raw', f'{name}_raw.png')
    os.makedirs(os.path.dirname(praw), exist_ok=True)
    sheet.save(praw)
    try:
        rel = os.path.relpath(sp, CHAR_DIR)
    except ValueError:            # --out-dir 跨盘时 relpath 无解，退回绝对路径
        rel = sp
    print(f'  [OK] {name}: {len(imgs)} 帧  {cols}x{rows}  sheet={sheet.size}  -> {rel}')


def build_frames(base, s, ctx):
    eff, out = s['effect'], []
    for p in s['plan']:
        if eff == 'lamp_pulse':
            out.append(idle_frame(base, ctx['cap'], p[0], p[1]))
        elif eff == 'muzzle_charge':
            out.append(aim_frame(base, p[0], p[1]))
        elif eff == 'gray_out':
            out.append(disable_frame(base, p[0], p[1], smoke_plume(ctx['sx'], ctx['sy'], p[2])))
        else:
            raise SystemExit(f'未知 effect: {eff}')
    return [Image.fromarray(x, 'RGBA') for x in out]


def main(argv=None):
    global CHAR_DIR, FRAMES, SHEETS, BASE_PNG
    ap = argparse.ArgumentParser(description='定妆图派生：以某一帧为像素基准派生其它动作套')
    ap.add_argument('--char-dir', help='角色目录（默认 CONFIG.CHAR_DIR）')
    ap.add_argument('--base', help='定妆帧 png（默认 <char-dir>/BASE_REL）')
    ap.add_argument('--out-dir', help='输出根目录（默认 = char-dir），给临时目录便于比对')
    ap.add_argument('--only', nargs='*', help='只派生指定的套（默认 SETS 全部）')
    ap.add_argument('--dry-run', action='store_true', help='只报告，不写盘')
    a = ap.parse_args(argv)

    if a.char_dir:
        CHAR_DIR = os.path.abspath(a.char_dir)
        FRAMES = os.path.join(CHAR_DIR, 'Frames')
        SHEETS = os.path.join(CHAR_DIR, 'Sheets')
        BASE_PNG = os.path.join(CHAR_DIR, BASE_REL)
    if a.base:
        BASE_PNG = os.path.abspath(a.base)
    if a.out_dir:
        OUT = os.path.abspath(a.out_dir)
        FRAMES = os.path.join(OUT, 'Frames')
        SHEETS = os.path.join(OUT, 'Sheets')

    if not os.path.exists(BASE_PNG):
        raise SystemExit(f'找不到定妆帧：{BASE_PNG}')
    base = load_base()
    blobs = red_blobs(base)
    if not blobs:
        raise SystemExit('定妆帧里没找到红色部件：示例的「警戒灯/信号灯」定位依赖红色连通域，'
                         '换角色请改 red_blobs() 的判据，或把 cap 直接写死在 CONFIG 里')
    print('  红色部件（y 升序）:', [(round(b[0]), round(b[1]), b[2]) for b in blobs])
    cap = blobs[0]                     # 最靠上的红件 = 罐顶警戒灯
    print(f'  警戒灯 = ({cap[1]:.0f},{cap[0]:.0f})  风扇 = {FAN_C}')
    ys, xs = np.where(base[..., 3] > ALPHA_MIN)
    ctx = dict(cap=cap, sx=float((xs.min() + xs.max()) / 2 + 52), sy=float(ys.min() + 112))

    names = set(a.only) if a.only else None
    for i, s in enumerate(SETS, 1):
        if names and s['name'] not in names:
            continue
        print(f"\n[{i}/{len(SETS)}] {s['name']}  effect={s['effect']}  {len(s['plan'])} 帧")
        write_set(s['name'], build_frames(base, s, ctx), s['cols'], a.dry_run)


if __name__ == '__main__':
    main()
