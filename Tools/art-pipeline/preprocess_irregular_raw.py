# -*- coding: utf-8 -*-
"""不规则图集 -> 连通域自动分帧 -> 规则网格透明底 raw（通用版，可复用任意项目/任意素材）。

解决的问题：用户/AIGC 交付的图集常常不是规整网格（人物跨格、行内帧数不等、旁边还有
飞桶/光束/爆炸/水花等分离特效），硬按格线切会把人物切断。本脚本按连通域取主体，再按
(行, 列) 排序重拼成 cols×rows 的等大透明网格，供 process_sheets.py 正常切格。

alpha-first：源图必须自带 alpha（透明底）；白底/黑底素材请先在 process_sheets.py 的
入口里抠图，或先转成透明底再跑本脚本。

用法示例：
  python preprocess_irregular_raw.py --src 素材.png --out ../../Assets/Art/Art/Player/Sheets/raw/Player_Attack03_raw.png \\
      --n 9 --cols 3 --rows 3 --min-h 120          # 只取角色本体，高于 120px 的连通域才算角色
  python preprocess_irregular_raw.py --src run.png --out .../Player_Run_raw.png --n 8 --cols 4 --rows 2
  python preprocess_irregular_raw.py ... --cell 576 704       # 指定画格（默认按最大帧 + 2×pad 自适应）
  python preprocess_irregular_raw.py ... --dry-run            # 只打印归属，不落盘

--min-h：把「高度不足」的连通域当分离特效排除（不并入角色帧）。默认 0 = 不排除，只按面积
筛掉碎点；连通域多于 n 时保留面积最大的 n 个。
"""
import sys, os, argparse
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from PIL import Image
from scipy import ndimage

ALPHA_MIN = 8


def load_rgba(path):
    im = Image.open(path)
    if im.mode not in ('RGBA', 'LA', 'PA') and 'transparency' not in im.info:
        raise SystemExit(f'[FAIL] 源图不是透明底：{path}（请先抠图转成透明底 RGBA）')
    im = im.convert('RGBA')
    a = np.asarray(im)[..., 3]
    if (a <= ALPHA_MIN).mean() <= 0.02:
        raise SystemExit(f'[FAIL] 源图几乎没有透明像素，疑似白底/黑底：{path}')
    return im


def bleed(rgb, alpha):
    """颜色外溢：透明区 RGB 用最近的不透明像素颜色填充，避免后续 LANCZOS 缩放渗色出晕圈。"""
    empty = alpha <= ALPHA_MIN
    if not empty.any():
        return rgb
    idx = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
    return rgb[idx[0], idx[1]]


def split_rows(boxes, rows):
    """按 y 中心的最大间隙把帧切成 rows 行；行内按 x 排序。"""
    if rows <= 1:
        return [sorted(boxes, key=lambda b: b[0])]
    ordered = sorted(boxes, key=lambda b: (b[1] + b[3]) / 2)
    cys = [(b[1] + b[3]) / 2 for b in ordered]
    gaps = sorted(((cys[i + 1] - cys[i]), i) for i in range(len(cys) - 1))[- (rows - 1):]
    cuts = sorted(i for _, i in gaps)
    out, start = [], 0
    for c in cuts + [len(ordered) - 1]:
        out.append(sorted(ordered[start:c + 1], key=lambda b: b[0]))
        start = c + 1
    return [r for r in out if r]


def main():
    ap = argparse.ArgumentParser(description='不规则图集 -> 连通域分帧 -> 规则网格透明 raw')
    ap.add_argument('--src', required=True, help='源图路径（透明底 RGBA）')
    ap.add_argument('--out', required=True, help='输出 raw 路径（建议 <工程根>/Assets/Art/Art/<角色>/Sheets/raw/<名>_raw.png）')
    ap.add_argument('--n', type=int, required=True, help='期望帧数（主体连通域个数）')
    ap.add_argument('--cols', type=int, required=True, help='输出网格列数')
    ap.add_argument('--rows', type=int, required=True, help='输出网格行数')
    ap.add_argument('--src-rows', type=int, default=None,
                    help='源图里素材实际排布的行数（默认与 --rows 相同）；源排 2 行、输出 3 行时要显式给 2')
    ap.add_argument('--pad', type=int, default=48, help='每帧四周留白 px（默认 48，保证切格不粘格线）')
    ap.add_argument('--area-min', type=int, default=500, help='连通域最小面积，低于视为碎点（默认 500）')
    ap.add_argument('--min-h', type=int, default=0, help='主体最小高度 px；低于该值判为分离特效排除（默认 0 不排除）')
    ap.add_argument('--cell', type=int, nargs=2, metavar=('W', 'H'), help='固定画格尺寸；缺省按最大帧 + 2×pad 自适应')
    ap.add_argument('--dry-run', action='store_true', help='只打印分帧结果，不写文件')
    a = ap.parse_args()

    im = load_rgba(a.src)
    W, H = im.size
    arr = np.asarray(im)
    alpha = arr[..., 3]
    fg = alpha > ALPHA_MIN
    print(f'src={a.src} {W}x{H}')

    lbl, cnt = ndimage.label(fg)
    sizes = ndimage.sum(fg, lbl, range(1, cnt + 1))
    objs = ndimage.find_objects(lbl)

    def bbox(i):
        sl = objs[i - 1]
        return (sl[1].start, sl[0].start, sl[1].stop, sl[0].stop)

    comps, fx = [], []
    for i in range(1, cnt + 1):
        if sizes[i - 1] < a.area_min:
            continue
        b = bbox(i)
        if a.min_h and (b[3] - b[1]) < a.min_h:
            fx.append((i, b))
        else:
            comps.append((i, b))
    print(f'连通域={cnt} 主体候选={len(comps)} 排除(特效/矮块)={len(fx)}')
    if len(comps) < a.n:
        raise SystemExit(f'[FAIL] 主体只有 {len(comps)} 个，少于 --n {a.n}；可调小 --min-h 或 --area-min')
    if len(comps) > a.n:
        keep = sorted(comps, key=lambda c: sizes[c[0] - 1], reverse=True)[:a.n]
        dropped = [c for c in comps if c not in keep]
        print(f'[WARN] 主体多于 {a.n} 个，按面积保留最大的 {a.n} 个；丢弃 bbox={[d[1] for d in dropped]}')
        comps = keep
    if fx:
        print(f'[INFO] 排除的分离连通域（不并入帧）：{[b for _, b in fx]}')

    src_rows = a.src_rows or a.rows
    rows = split_rows([b for _, b in comps], src_rows)
    counts = [len(r) for r in rows]
    print(f'源分行={counts}（按 y 最大间隙分割，共 {src_rows} 行）')
    seq = [b for r in rows for b in r]
    if a.cols * a.rows < len(seq):
        raise SystemExit(f'[FAIL] 画格 {a.cols}x{a.rows} 装不下 {len(seq)} 帧')
    if len(seq) != a.cols * a.rows:
        print(f'[INFO] 帧数 {len(seq)} < 画格 {a.cols}x{a.rows}，末行会有 {a.cols * a.rows - len(seq)} 个空位')
    if len(seq) < a.cols * a.rows - 2:
        print(f'[WARN] 空位超过 2 个，process_sheets 可能按有效帧不足报错，请核对 --cols/--rows')

    rgb = bleed(arr[..., :3], alpha)
    master = np.dstack([rgb, np.where(fg, alpha, 0).astype(np.uint8)])
    if a.cell:
        CW, CH = a.cell
        max_w = max(b[2] - b[0] for b in seq) + a.pad * 2
        max_h = max(b[3] - b[1] for b in seq) + a.pad * 2
        if CW < max_w or CH < max_h:
            raise SystemExit(f'[FAIL] --cell {CW}x{CH} 装不下最大帧 {max_w}x{max_h}（含留白）')
    else:
        CW = max(b[2] - b[0] for b in seq) + a.pad * 2
        CH = max(b[3] - b[1] for b in seq) + a.pad * 2

    sheet = Image.new('RGBA', (CW * a.cols, CH * a.rows), (0, 0, 0, 0))
    for k, b in enumerate(seq):
        img = Image.fromarray(master[b[1]:b[3], b[0]:b[2]], 'RGBA')
        r, c = divmod(k, a.cols)
        px = c * CW + (CW - img.width) // 2
        py = r * CH + (CH - img.height) // 2
        sheet.paste(img, (px, py))       # 直贴保留 straight alpha，不做 mask 混合
        print(f'frame{k}: bbox={b} size={img.size}')
    if a.dry_run:
        print(f'[DRY] 未落盘；将输出 {sheet.size[0]}x{sheet.size[1]}（cell {CW}x{CH}）')
        return
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    sheet.save(a.out)
    print(f'[OK] {a.out} ({sheet.size[0]}x{sheet.size[1]}, cell {CW}x{CH})')


if __name__ == '__main__':
    main()
