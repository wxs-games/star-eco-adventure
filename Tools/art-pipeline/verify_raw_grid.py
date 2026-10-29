# -*- coding: utf-8 -*-
"""verify_raw_grid.py —— 校验网格 raw 图「每格人物是否贴边/越界」。

用途：AI 生图常把人物画到格子分割线上，切格时就会把人切掉一块。
      本脚本按 cols x rows 均分切格，逐格算前景包围盒到四边的距离，
      距离 <= 2px 判为「贴边」（会导致切图缺肉）。

用法：
    python verify_raw_grid.py                 # 自动扫 Player 全部 raw
    python verify_raw_grid.py Player_Boss01   # 指定若干条（按文件名匹配）
退出码 1 表示存在贴边帧。
"""
import glob, os, re, sys
import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Art', 'Art')

# 网格规格：name -> (cols, rows)。与 gen_sheet.mjs 的 JOBS 保持一致。
GRID = {
    'Player_Idle': (5, 2), 'Player_Idle02': (4, 2), 'Player_Run': (4, 2), 'Player_Dash': (3, 2),
    'Player_Roll': (4, 2), 'Player_Attack01': (4, 2), 'Player_Attack02': (4, 2),
    'Player_Attack03': (3, 3), 'Player_AttackAir': (3, 2), 'Player_HeavyAttack': (4, 3),
    'Player_Die': (4, 2), 'Player_Fall': (2, 2), 'Player_Jump': (3, 1), 'Player_Land': (3, 1),
    'Player_Hurt': (3, 1), 'Player_Interact': (3, 2), 'Player_LedgeGrab': (3, 1),
    'Player_Turn': (1, 1), 'Player_Victory': (4, 2), 'Player_WallJump': (3, 1),
    'Player_WallSlide': (3, 1), 'Player_Crouch': (2, 1),
}
TOL = 2


def check(name, cols, rows, path):
    a = np.array(Image.open(path).convert('RGB')).astype(int)
    H, W, _ = a.shape
    cw, ch = W // cols, H // rows
    mn = a.min(axis=2)
    sat = a.max(axis=2) - mn
    fg = (mn < 245) | (sat > 12)
    bad = []
    for r in range(rows):
        for c in range(cols):
            m = fg[r*ch:(r+1)*ch, c*cw:(c+1)*cw]
            ys, xs = np.where(m)
            if not len(ys):
                bad.append((r, c, 'EMPTY'))
                continue
            d = [int(xs.min()), int(cw - 1 - xs.max()), int(ys.min()), int(ch - 1 - ys.max())]
            if min(d) <= TOL:
                bad.append((r, c, 'L%d R%d T%d B%d' % tuple(d)))
    return cw, ch, bad


if __name__ == '__main__':
    pats = sys.argv[1:]
    files = sorted(glob.glob(os.path.join(ROOT, '**', 'Sheets', 'raw', '*_raw.png'), recursive=True))
    fail = 0
    for p in files:
        name = os.path.basename(p)[:-len('_raw.png')]
        if name not in GRID:
            continue
        if pats and not any(x in name for x in pats):
            continue
        cols, rows = GRID[name]
        cw, ch, bad = check(name, cols, rows, p)
        tag = 'OK' if not bad else 'TOUCH'
        print('%-4s %-22s cell=%dx%d  贴边帧=%d/%d %s' % (tag, name, cw, ch, len(bad), cols*rows,
                                                          '' if not bad else bad[:4]))
        fail += 1 if bad else 0
    print('\n贴边条数=%d' % fail)
    sys.exit(1 if fail else 0)
