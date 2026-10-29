# -*- coding: utf-8 -*-
"""对「已生成的」精灵帧与精灵表批量补顶部羽化（一次性后处理，可重复执行）。

## 什么时候需要
文生图常把烟柱/蒸汽/光柱画到格子上沿就被截断，成品顶部是一条硬平口 —— 缩放补不回丢失的
像素，只能把切口做成渐隐。`process_sheets.py` 里可以给 spec 加 `feather_top=True` 在管线内
直接做；但如果素材已经导入 Unity、不想重跑整条管线，就用本脚本单独补一次。

## 可重复执行
`process_sheets.feather_top()` 内部会识别「已羽化」的图并直接跳过，所以重复跑无副作用。
（但不要拿本脚本对「本来就该是硬边」的动作执行：例如死亡动作后半段顶部是废墟轮廓，
渐隐会把它削掉。）

## 用法
    # 1) 按动作名（脚本自动在 <工程根>/Assets/Art 下找 **/Frames/<名>/ 与同名 sheet）
    python feather_smoke.py Boss01_Idle Boss01_Move

    # 2) 全部动作
    python feather_smoke.py all

    # 3) 直接给目录（frames 目录），只羽化帧
    python feather_smoke.py --frames <Frames/某动作>

    # 4) 直接给 sheet，手工指定网格
    python feather_smoke.py --sheet <Sheets/某动作_sheet.png> --cols 5 --rows 2

## 网格怎么来的
不再写死每套动作的 cols/rows：帧图的宽高就是画格宽高，`cols = 表宽 // 帧宽`、
`rows = 表高 // 帧高`，由文件自身推导，换任何动作都不用改脚本。
"""
import sys, os, glob, argparse
sys.stdout.reconfigure(encoding='utf-8')
from PIL import Image
from process_sheets import feather_top, ROOT

ROOT = os.path.abspath(ROOT)      # 去掉路径里的 '..'，否则 Pillow 写回时会 OSError(22)


def _pngs(d):
    return sorted(p for p in glob.glob(os.path.join(d, '*.png')))


def feather_frames(fdir, dry=False):
    files = _pngs(fdir)
    if not files:
        print(f'[SKIP] {fdir}: 没有 png')
        return 0
    for p in files:
        im = Image.open(p).convert('RGBA')
        out = feather_top(im)
        if not dry:
            out.save(p)
    return len(files)


def feather_sheet(sheet_path, cols=None, rows=None, dry=False):
    """按网格逐格羽化后重拼；cols/rows 不给就按帧图尺寸自动推导（需同目录有 Frames）。"""
    sheet = Image.open(sheet_path).convert('RGBA')
    if cols is None or rows is None:
        clip = os.path.basename(sheet_path).replace('_sheet.png', '')
        fdir = os.path.join(os.path.dirname(os.path.dirname(sheet_path)), 'Frames', clip)
        files = _pngs(fdir)
        if not files:
            print(f'[SKIP] {sheet_path}: 无法推导网格（同目录找不到 Frames/{clip}），请用 --cols/--rows 指定')
            return False
        f0 = Image.open(files[0])
        cols, rows = sheet.width // f0.width, sheet.height // f0.height
    cw, ch = sheet.width // cols, sheet.height // rows
    out = Image.new('RGBA', sheet.size, (0, 0, 0, 0))
    for r in range(rows):
        for c in range(cols):
            cell = sheet.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
            if cell.getbbox() is not None:
                cell = feather_top(cell)
            out.paste(cell, (c * cw, r * ch))
    if not dry:
        out.save(sheet_path)
    print(f'  sheet {os.path.basename(sheet_path)}: {cols}x{rows}')
    return True


def find_clip(name):
    """在 ROOT（= <工程根>/Assets/Art/Art）下找 Frames/<name> 与同名 Sheets/<name>_sheet.png。"""
    for fdir in glob.glob(os.path.join(ROOT, '**', 'Frames', name), recursive=True):
        if not os.path.isdir(fdir):
            continue
        sheet = os.path.join(os.path.dirname(os.path.dirname(fdir)), 'Sheets', name + '_sheet.png')
        return fdir, (sheet if os.path.exists(sheet) else None)
    return None, None


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True, description='批量补顶部羽化（帧 + 精灵表）')
    ap.add_argument('names', nargs='*', help='动作名（all = 全部）')
    ap.add_argument('--frames', help='直接指定 Frames/<动作> 目录，只羽化帧')
    ap.add_argument('--sheet', help='直接指定 <动作>_sheet.png，逐格羽化后重拼')
    ap.add_argument('--cols', type=int, help='配合 --sheet，手工指定列数（默认自动推导）')
    ap.add_argument('--rows', type=int, help='配合 --sheet，手工指定行数（默认自动推导）')
    ap.add_argument('--dry-run', action='store_true', help='只报告，不写盘')
    a = ap.parse_args(argv)

    if a.frames:
        n = feather_frames(a.frames, a.dry_run)
        print(f'[OK] {a.frames}: {n} 帧')
        return 0
    if a.sheet:
        ok = feather_sheet(a.sheet, a.cols, a.rows, a.dry_run)
        return 0 if ok else 1

    names = a.names
    if not names or names == ['all']:
        pat = os.path.join(ROOT, '**', 'Frames', '*')
        names = sorted({os.path.basename(d) for d in glob.glob(pat, recursive=True) if os.path.isdir(d)})

    fail = 0
    for name in names:
        fdir, sheet = find_clip(name)
        if not fdir:
            print(f'[SKIP] {name}: 找不到 Frames/{name}')
            fail += 1
            continue
        n = feather_frames(fdir, a.dry_run)
        if sheet:
            feather_sheet(sheet, None, None, a.dry_run)
        else:
            print(f'  [WARN] {name}: 没有 {name}_sheet.png，只处理了帧')
        print(f'[OK] {name}: {n} 帧' + ('' if sheet else '（无 sheet）'))
    return 1 if fail else 0


if __name__ == '__main__':
    sys.exit(main())
