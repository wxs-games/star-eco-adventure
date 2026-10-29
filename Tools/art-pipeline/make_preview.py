# -*- coding: utf-8 -*-
"""动作总览预览图（通用版）：把各动作的精灵表按固定帧高拼成一张对比图，深绿底，便于目检。

默认扫描 <工程根>/Assets/Art/Art 下所有 `Frames/<Clip>/`，自动取每个动作的画格与帧数；
也可用 --clips 指定只拼哪几条，用 --out 指定输出路径（默认 Assets/Art/_预览/contact_sheets.png）。

用法：
  python make_preview.py                                  # 全部动作
  python make_preview.py --clips Player_Attack01 Player_Attack03
  python make_preview.py --frame-h 80 --out ../../Assets/Art/_预览/contact_all.png
"""
import sys, os, glob, argparse
sys.stdout.reconfigure(encoding='utf-8')
from PIL import Image, ImageDraw

ART = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Art')
ROOT = os.path.join(ART, 'Art')
FG_H = 110      # 每帧在预览里的高度
PAD = 8
LABEL_W = 200
BG = (26, 51, 38)       # 深绿
FG = (235, 240, 235)


def find_clips():
    out = []
    for d in glob.glob(os.path.join(ROOT, '**', 'Frames', '*'), recursive=True):
        name = os.path.basename(d)
        if os.path.isdir(d) and os.path.exists(os.path.join(d, f'{name}_00.png')):
            out.append((name, d))
    return sorted(out)


def build(clips, out_path, fg_h=FG_H):
    rows = []
    for name, fdir in clips:
        sheet_p = os.path.join(os.path.dirname(os.path.dirname(fdir)), 'Sheets', f'{name}_sheet.png')
        if not os.path.exists(sheet_p):
            print(f'[WARN] 缺精灵表，跳过 {name}: {sheet_p}')
            continue
        f0 = Image.open(os.path.join(fdir, f'{name}_00.png')).convert('RGBA')
        cw, ch = f0.size
        sheet = Image.open(sheet_p).convert('RGBA')
        # 多行精灵表也要完整取到：列数×行数，按阅读顺序（左→右、上→下）展开
        ncol, nrow = sheet.width // cw, sheet.height // ch
        frames = [sheet.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
                  for r in range(nrow) for c in range(ncol)]
        scale = fg_h / ch
        frames = [f.resize((max(1, int(f.width * scale)), fg_h), Image.LANCZOS) for f in frames]
        rows.append((name, frames))
    if not rows:
        raise SystemExit('[FAIL] 没有可拼的动作（检查 Frames/<Clip>/<Clip>_00.png 与 Sheets/<Clip>_sheet.png）')

    row_h = fg_h + PAD * 2
    W = max(LABEL_W + sum(f.width + PAD for f in fs) + PAD for _, fs in rows) + PAD
    H = row_h * len(rows) + PAD
    img = Image.new('RGB', (W, H), BG)
    d = ImageDraw.Draw(img)
    for i, (name, frames) in enumerate(rows):
        y = PAD + i * row_h
        d.text((PAD + 4, y + row_h // 2 - 7), name, fill=FG)
        x = LABEL_W
        for f in frames:
            img.paste(f, (x, y), f)
            x += f.width + PAD
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    img.save(out_path)
    print(f'[OK] {out_path} {W}x{H}, {len(rows)} 条动作')


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description='动作总览预览图（深绿底）')
    ap.add_argument('--clips', nargs='*', help='只拼这些 Clip（默认扫描全部 Frames 目录）')
    ap.add_argument('--out', default=os.path.join(ART, '_预览', 'contact_sheets.png'), help='输出路径')
    ap.add_argument('--frame-h', type=int, default=FG_H, help='每帧预览高度（默认 110）')
    a = ap.parse_args()
    allc = find_clips()
    if a.clips:
        picked = [(n, d) for n, d in allc if n in a.clips]
        missing = set(a.clips) - {n for n, _ in picked}
        if missing:
            raise SystemExit(f'[FAIL] 找不到动作：{sorted(missing)}')
    else:
        picked = allc
    build(picked, a.out, a.frame_h)
