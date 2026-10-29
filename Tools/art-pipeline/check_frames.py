# -*- coding: utf-8 -*-
"""逐帧尺寸比对（管线通用校验器）。

对 Frames/<Clip>/ 下的每帧测量：画布尺寸、人物（主体连通域）身高、脚底线 / 底留白、
水平中心偏移、内容左右触边，然后分两类判定：

  1) 结构问题（FAIL，默认即失败）：条内画布尺寸不一致、帧目录为空、空帧过多；
  2) 质量提示（WARN）：帧间身高极差、脚底线极差、与参考动作身高/底留白差异、
     底座水平偏移极差、内容触边。这些都可能是「动作本来就该变」，只提示不判死；
     要求严格质检时加 `--strict`，身高/脚底超差即算 FAIL。

  参考动作默认 Player_Idle：攻击帧比待机矮、帧间大小跳动，看一条 WARN 就知道。

用途：快速定位「攻击帧比待机矮」「帧间人物大小跳动」「人物被画格裁掉」这类问题。

用法：
  python check_frames.py                                  # 全部 Player 动作，参考 Player_Idle
  python check_frames.py Player_Attack03                  # 只查一条
  python check_frames.py Enemy03_Die --ref Enemy03_Idle    # 指定参考
  python check_frames.py --dir <帧目录> [--ref-dir <参考帧目录>]   # 任意帧目录（脱离 SPECS）
  python check_frames.py Player_Attack01 --tol-ref 2 --tol-foot 2

身高取「主体连通域」（包围盒最高者）而非整帧 bbox：帧内分离的光束 / 水花 / 爆炸会把
bbox 撑高，按 bbox 比对会把角色误判成矮子。

退出码：0 = 无 FAIL（WARN 不影响）；1 = 有 FAIL。
"""
import sys, os, glob, argparse
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from PIL import Image

try:
    from scipy import ndimage
    HAVE_SCIPY = True
except ImportError:
    HAVE_SCIPY = False

# 脚本位于 <工程根>\Tools\art-pipeline\，向上两级定位工程根（含 Assets 的目录），免配置路径
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Art', 'Art')
ALPHA_MIN = 8
DEFAULT_REF = 'Player_Idle'
BOT_GAP = 7          # 与 process_sheets 一致的脚底留白
BASE_BAND = 74       # 脚底以上取多高的「底座带」来算水平中心


def subdir_for(name):
    if name.startswith('Player'):   return 'Player'
    if name.startswith('Enemy03'):  return os.path.join('Enemies', 'Enemy03')
    if name.startswith('Tower01'):  return os.path.join('Towers', 'Tower01')
    if name.startswith('Boss01'):   return os.path.join('Boss', 'Boss01')
    raise ValueError(name)


def frames_dir(clip):
    return os.path.join(ROOT, subdir_for(clip), 'Frames', clip)


def subject_mask(mask):
    """取主体连通域：包围盒最高者（同高取面积大者），避免分离特效抢占主体。"""
    if not HAVE_SCIPY:
        return mask
    lbl, n = ndimage.label(mask)
    if n <= 1:
        return mask
    areas = ndimage.sum(mask, lbl, range(1, n + 1))
    sls = ndimage.find_objects(lbl)
    best = max(range(n), key=lambda i: (sls[i][0].stop - sls[i][0].start, areas[i]))
    return lbl == best + 1


def measure(path):
    im = Image.open(path).convert('RGBA')
    W, H = im.size
    a = np.asarray(im)[..., 3]
    m = a > ALPHA_MIN
    if not m.any():
        return dict(name=os.path.basename(path), W=W, H=H, empty=True)
    subj = subject_mask(m)
    ys, xs = np.where(subj)
    x0, x1, y0, y1 = int(xs.min()), int(xs.max()), int(ys.min()), int(ys.max())
    # 水平中心取「底座带」而非整个包围盒：投掷/挥砍帧的手臂、光束会把 bbox 拉宽，
    # 用 bbox 中心算出的偏移是动作幅度，不是人物漂移。底座带与管线锚点口径一致。
    top = max(y0, y1 - BOT_GAP - BASE_BAND + 1)
    bx = np.where(subj[top:max(y0 + 1, y1 - BOT_GAP + 1), :].any(axis=0))[0]
    cx = (bx.min() + bx.max() + 1) / 2 if len(bx) else (x0 + x1) / 2
    fy, fx = np.where(m)          # 全部内容（含分离特效），只用于触边判断
    return dict(name=os.path.basename(path), W=W, H=H, empty=False,
                h=y1 - y0 + 1, w=x1 - x0 + 1, foot=y1,
                gap=H - 1 - y1, cx=cx, dx=cx - (W - 1) / 2,
                left=int(fx.min()), right=int(W - 1 - fx.max()))


def scan(dirpath):
    files = sorted(glob.glob(os.path.join(dirpath, '*.png')))
    if not files:
        raise SystemExit(f'[FAIL] 目录内没有 png 帧：{dirpath}')
    return [measure(p) for p in files]


def med(v):
    v = sorted(v)
    return v[len(v) // 2] if v else 0


def check(dirpath, label, ref=None, tol_spread=8.0, tol_foot=3, tol_ref=5.0, tol_gap=3,
          tol_x=12, strict=False):
    """返回 (fail 列表, warn 列表, 统计信息)。"""
    rows = scan(dirpath)
    live = [r for r in rows if not r['empty']]
    fails, warns = [], []
    if not live:
        return ['全部帧为空'], [], {}
    sizes = {(r['W'], r['H']) for r in live}
    if len(sizes) > 1:
        fails.append(f'画布尺寸不一致：{sorted(sizes)}')
    if len(live) < len(rows):
        msg = f'{len(rows) - len(live)} 帧为空（{len(rows)} 帧中）'
        (fails if strict else warns).append(msg)
    hs = [r['h'] for r in live]
    gaps = [r['gap'] for r in live]
    height_spread = (max(hs) - min(hs)) / med(hs) * 100 if med(hs) else 0
    foot_spread = max(r['foot'] for r in live) - min(r['foot'] for r in live)
    if height_spread > tol_spread:
        msg = f'帧间身高极差 {height_spread:.1f}% > {tol_spread}%（{min(hs)}–{max(hs)}）'
        (fails if strict else warns).append(msg)
    if foot_spread > tol_foot:
        msg = f'脚底线极差 {foot_spread}px > {tol_foot}px'
        (fails if strict else warns).append(msg)
    edge = [r['name'] for r in live if r['left'] == 0 or r['right'] == 0]
    if edge:
        warns.append(f'内容触边（左/右留白=0）：{", ".join(edge)} —— 确认是画格自适应而非裁切')
    cx_span = max(r['dx'] for r in live) - min(r['dx'] for r in live)
    if cx_span > tol_x:
        warns.append(f'底座水平偏移极差 {cx_span:.1f}px > {tol_x}px —— 确认是动作幅度还是人物漂移')
    stats = dict(n=len(rows), h_med=med(hs), h_min=min(hs), h_max=max(hs),
                 gap_med=med(gaps), foot_span=foot_spread, cx_span=cx_span)
    if ref:
        rh, rg = ref['h_med'], ref['gap_med']
        diff = (stats['h_med'] - rh) / rh * 100 if rh else 0
        stats['ref_h'] = rh
        stats['ref_diff'] = diff
        if abs(diff) > tol_ref:
            warns.append(f'身高中位 {stats["h_med"]}px 与参考 {rh}px 相差 {diff:+.1f}% > {tol_ref}%')
        if abs(stats['gap_med'] - rg) > tol_gap:
            warns.append(f'底留白中位 {stats["gap_med"]}px 与参考 {rg}px 相差 {abs(stats["gap_med"] - rg)}px > {tol_gap}px')
    return fails, warns, dict(stats, rows=rows)


def report(label, res):
    fails, warns, st = res
    print(f'\n===== {label} =====')
    if not st:
        print('  [FAIL] ' + '；'.join(fails))
        return True
    ref = f"  参考身高={st['ref_h']}({st['ref_diff']:+.1f}%)" if 'ref_h' in st else ''
    print(f"  帧数={st['n']}  身高={st['h_min']}–{st['h_max']}（中位 {st['h_med']}）  "
          f"底留白中位={st['gap_med']}  脚底线极差={st['foot_span']}px  "
          f"水平偏移极差={st['cx_span']:.1f}px{ref}")
    for r in st['rows']:
        if r['empty']:
            print(f"  {r['name']}: 空帧 {r['W']}x{r['H']}")
            continue
        print(f"  {r['name']}: {r['W']}x{r['H']}  h={r['h']} w={r['w']}  foot={r['foot']} "
              f"gap={r['gap']}  dx={r['dx']:+.1f}  L={r['left']} R={r['right']}")
    for f in fails:
        print('  [FAIL] ' + f)
    for w in warns:
        print('  [WARN] ' + w)
    if not fails and not warns:
        print('  [PASS] 逐帧尺寸一致')
    elif not fails:
        print('  [PASS] 条内一致（有提示项）')
    return bool(fails)


def main():
    ap = argparse.ArgumentParser(description='逐帧尺寸比对（身高/脚底/画布/触边）')
    ap.add_argument('clips', nargs='*', help='要校验的 Clip 名（默认全部 Player 动作）')
    ap.add_argument('--ref', default=None, help=f'参考 Clip 名（默认 {DEFAULT_REF}，Player 之外的角色需显式指定）')
    ap.add_argument('--dir', default=None, help='直接指定帧目录（跳过 Clip 名，用于任意素材）')
    ap.add_argument('--ref-dir', default=None, help='参考帧目录（配合 --dir）')
    ap.add_argument('--tol-spread', type=float, default=8.0, help='帧间身高极差上限 %%（默认 8）')
    ap.add_argument('--strict', action='store_true', help='身高/脚底/空帧超差也判 FAIL（严格质检）')
    ap.add_argument('--tol-foot', type=int, default=3, help='脚底线极差上限 px（默认 3）')
    ap.add_argument('--tol-ref', type=float, default=5.0, help='与参考身高差异上限 %%（默认 5）')
    ap.add_argument('--tol-gap', type=int, default=3, help='与参考底留白差异上限 px（默认 3）')
    ap.add_argument('--tol-x', type=int, default=12, help='底座水平偏移极差上限 px（默认 12）')
    a = ap.parse_args()
    kw = dict(tol_spread=a.tol_spread, tol_foot=a.tol_foot, tol_ref=a.tol_ref,
              tol_gap=a.tol_gap, tol_x=a.tol_x, strict=a.strict)

    if a.dir:
        if not os.path.isdir(a.dir):
            raise SystemExit(f'[FAIL] 目录不存在：{a.dir}')
        ref = None
        if a.ref_dir:
            rows = [r for r in scan(a.ref_dir) if not r['empty']]
            ref = dict(h_med=med([r['h'] for r in rows]), gap_med=med([r['gap'] for r in rows]))
        bad = report(os.path.basename(os.path.normpath(a.dir)), check(a.dir, '', ref, **kw))
        sys.exit(1 if bad else 0)

    clips = a.clips or [os.path.basename(d) for d in sorted(glob.glob(os.path.join(ROOT, 'Player', 'Frames', '*')))
                        if os.path.isdir(d)]
    ref_name = a.ref
    if ref_name is None and all(c.startswith('Player') for c in clips):
        ref_name = DEFAULT_REF
    ref = None
    if ref_name:
        rdir = frames_dir(ref_name)
        if os.path.isdir(rdir):
            rows = [r for r in scan(rdir) if not r['empty']]
            ref = dict(h_med=med([r['h'] for r in rows]), gap_med=med([r['gap'] for r in rows]))
        else:
            print(f'[WARN] 参考动作不存在，跳过比对：{rdir}')
    bad = False
    for c in clips:
        d = frames_dir(c)
        if not os.path.isdir(d):
            print(f'\n===== {c} =====\n  [FAIL] 帧目录不存在：{d}')
            bad = True
            continue
        bad |= report(c, check(d, c, ref, **kw))
    print('\n结果：' + ('存在 FAIL' if bad else 'PASS'))
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
