# -*- coding: utf-8 -*-
"""多动作合图 -> 按动作切分 -> 每动作独立 raw（交给 process_sheets.py 直接使用）

背景：现有管线是「一个 raw 图 = 一个动作」（process_sheets.py 的 SPECS 一条对一张
<名称>_raw.png）。本脚本补上缺的那一步：把一张包含多个动作的大图按动作区域裁开，
写成 <动作名>_raw.png，之后 process_sheets / check_frames / Unity 装配全部零改动。

用法：
  python slice_actions.py --src 大图.png --layout layout.json
  python slice_actions.py --src 大图.png --layout layout.json --dry-run
  python slice_actions.py --src 大图.png --layout layout.json --out D:\\tmp\\slice

layout.json 示例（1 基、闭区间；每个动作占连续的 行块 x 列块）：
{
  "prefix": "Player",
  "out": "Player/Sheets/raw",
  "grid": {"cols": 4, "rows": 4},
  "actions": [
    {"name": "Player_Attack01", "row0": 1, "row1": 2, "col0": 1, "col1": 4},
    {"name": "Player_Attack02", "row0": 3, "row1": 4, "col0": 1, "col1": 4}
  ]
}

约定：
- 纯裁剪，不缩放、不居中、不抠图 —— 像素原样搬过去。归一化（条内基准身高、脚底基线、
  画格固定宽）仍由 process_sheets.py 按每个动作独立计算，所以合图不会让动作之间互相牵连。
- 切出的每块尺寸 = 动作占用列数 x 行数 个整格，因此帧仍完整落在自己格里，不会被切断。
"""
import sys, os, json, argparse
sys.stdout.reconfigure(encoding='utf-8')
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, '..', '..'))
ART = os.path.join(PROJECT, 'Assets', 'Art', 'Art')


def parse_args(argv):
    p = argparse.ArgumentParser()
    p.add_argument('--src', required=True, help='多动作大图（png，建议透明底）')
    p.add_argument('--layout', required=True, help='layout.json')
    p.add_argument('--out', default=None, help='输出目录；默认取 layout.out（相对 Assets/Art/Art）')
    p.add_argument('--dry-run', action='store_true', help='只打印切分方案，不写文件')
    return p.parse_args(argv)


def main(argv):
    a = parse_args(argv)
    with open(a.layout, encoding='utf-8-sig') as f:
        lay = json.load(f)

    grid = lay['grid']
    cols, rows = int(grid['cols']), int(grid['rows'])
    out_dir = a.out or os.path.join(ART, lay['out'])

    im = Image.open(a.src)
    if im.mode != 'RGBA':
        im = im.convert('RGBA')
    W, H = im.size
    cw, ch = W / cols, H / rows
    print(f'大图 {os.path.basename(a.src)}  {W}x{H}   网格 {cols}x{rows} -> 每格 {cw:.1f}x{ch:.1f}')
    if abs(cw - round(cw)) > 1e-6 or abs(ch - round(ch)) > 1e-6:
        print(f'[warn] 大图尺寸不能被网格整除，边缘会丢 <1px，建议大图按整数倍生成')

    # ---- 区域校验：越界 / 重叠 ----
    cells = {}
    acts = lay['actions']
    for act in acts:
        name = act['name']
        r0, r1 = int(act['row0']), int(act['row1'])
        c0, c1 = int(act['col0']), int(act['col1'])
        if not (1 <= r0 <= r1 <= rows and 1 <= c0 <= c1 <= cols):
            raise SystemExit(f'[ERR] {name}: 区域越界  rows {r0}-{r1} / cols {c0}-{c1}（网格 {rows}x{cols}）')
        for r in range(r0, r1 + 1):
            for c in range(c0, c1 + 1):
                if (r, c) in cells:
                    raise SystemExit(f'[ERR] {name} 与 {cells[(r, c)]} 在 第{r}行第{c}列 重叠')
                cells[(r, c)] = name

    empty = [f'r{r}c{c}' for r in range(1, rows + 1) for c in range(1, cols + 1) if (r, c) not in cells]
    if empty:
        print(f'[warn] 有 {len(empty)} 个格子没有分配给任何动作：{", ".join(empty[:12])}'
              f'{" ..." if len(empty) > 12 else ""}')
        print('       文生图在空格里会自由发挥画出多余角色，建议让每个动作占满整行/整列')

    # ---- 切分 ----
    if not a.dry_run:
        os.makedirs(out_dir, exist_ok=True)
    for act in acts:
        name = act['name']
        r0, r1 = int(act['row0']), int(act['row1'])
        c0, c1 = int(act['col0']), int(act['col1'])
        box = (int(round((c0 - 1) * cw)), int(round((r0 - 1) * ch)),
               int(round(c1 * cw)), int(round(r1 * ch)))
        n = (r1 - r0 + 1) * (c1 - c0 + 1)
        sub = im.crop(box)
        dst = os.path.join(out_dir, f'{name}_raw.png')
        if a.dry_run:
            print(f'[dry] {name:24s} 行{r0}-{r1} 列{c0}-{c1}  {n}格  -> {sub.size[0]}x{sub.size[1]}  {dst}')
        else:
            sub.save(dst)
            print(f'[ok ] {name:24s} 行{r0}-{r1} 列{c0}-{c1}  {n}格  -> {sub.size[0]}x{sub.size[1]}  {dst}')
    print(f'\n共 {len(acts)} 个动作 -> {out_dir}')
    print('下一步：python process_sheets.py <动作名...>  然后 verify_raw_grid.py / check_frames.py 照旧')


if __name__ == '__main__':
    main(sys.argv[1:])
