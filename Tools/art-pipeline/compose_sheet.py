# -*- coding: utf-8 -*-
"""
新流程入口：单帧序列 -> 均匀网格 raw（喂给既有 process_sheets.py）

为什么需要它
------------
老管线（模式一/模式二）的输入是「一张已经排好帧的网格图」：
    Sheets/raw/{name}_raw.png  --process_sheets-->  Frames 单帧 + Sheets 精灵表
但现在出图范式改成「逐帧独立生成」（整网格文生图无法控制每帧姿态），
产出是 N 张各不相同的单帧，必须先把它们摆回均匀网格，老管线才吃得下。

本脚本只做「摆格子」这一件事，不做任何图像处理：
    抠像 / 去白边 / 连通域清理 / 身高归一 / 基线对齐 / 画格宽锁死
    全部仍由 process_sheets.py 负责，一行不改。

输出与 process_sheets.py 的约定严格对齐
---------------------------------------
画布必须是 cols*cell_w x rows*cell_h，且每帧居中贴在自己那一格里、不跨格。
process_sheets.py 按 cols/rows 均匀切格，切完自己再做居中与基线对齐，
所以这里贴图位置只需要保证「不越格」，居中贴是为了让切格后的居中量最小、
避免帧太靠边被连通域清理误判。

用法
----
  # 最省事：给一个目录，自动排序、自动算网格
  python compose_sheet.py --frames ../Frames/Player_Run --name Player_Run

  # 指定列数（比如想让成品别太宽）
  python compose_sheet.py --frames ../Frames/Player_Run --name Player_Run --cols 4

  # 拼完直接跑管线（复用 auto_pipeline 的登记/派生逻辑）
  python compose_sheet.py --frames ../Frames/Player_Run --name Player_Run --run-pipeline

  # 只看会拼成什么样，不落盘
  python compose_sheet.py --frames ../Frames/Player_Run --name Player_Run --dry
"""
import argparse
import glob
import math
import os
import re
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
# process_sheets.py 的根：<工程根>/Assets/Art/Art
ART_ROOT = os.path.join(HERE, "..", "..", "Assets", "Art", "Art")

DEFAULT_MAX_SIDE = 4096   # Unity 纹理上限的保守值；超出会给告警（可用 --max-side 改）


# --------------------------------------------------------------------------- 输入

def natural_key(path):
    """让 _2 排在 _10 前面，而不是字典序把 _10 排到 _2 前。"""
    base = os.path.basename(path)
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", base)]


def collect_frames(frames_arg):
    """frames_arg 可以是目录、glob 或逗号分隔的多个路径。"""
    paths = []
    for part in [p.strip() for p in frames_arg.split(",") if p.strip()]:
        if os.path.isdir(part):
            paths.extend(glob.glob(os.path.join(part, "*.png")))
        elif any(c in part for c in "*?["):
            paths.extend(glob.glob(part))
        elif os.path.isfile(part):
            paths.append(part)
    # 排除 process_sheets 生成的备份，避免把 .orig.png 当成一帧
    paths = [p for p in paths if not p.endswith(".orig.png")]
    paths = sorted(set(paths), key=natural_key)
    return paths


# --------------------------------------------------------------------------- 网格

def auto_grid(n, cw, ch, max_side):
    """选 cols/rows：空格最少 -> 横向优先(cols>=rows) -> 不超限 -> 接近正方。

    为什么横向优先：项目既有精灵表全是横排（Attack01 4列x2行、Attack03 3列x3行），
    横版动作游戏的动画条也习惯从左到右读。单纯追求「最方」会把 8 帧排成 2列x4行，
    与既有 22 张 sheet 的习惯不一致，反而增加理解成本。

    max_side 在这里只是"软"约束（排在横向优先之后），因为 raw 是中间产物，
    process_sheets 会按自己算出的画格重新排版，超限只做告警。

    返回 (cols, rows, over)  over=True 表示超了 max_side。
    """
    best = None
    for cols in range(1, n + 1):
        rows = math.ceil(n / cols)
        w, h = cols * cw, rows * ch
        waste = cols * rows - n                     # 空格数
        ratio = abs(w - h) / max(w, h)              # 0=正方
        over = max(w, h) > max_side
        # 排序优先级：少空格 > 横排(0)优于竖排(1) > 不超限 > 接近正方 > 面积小
        key = (waste, 0 if cols >= rows else 1, over, round(ratio, 3), w * h)
        if best is None or key < best[0]:
            best = (key, cols, rows, over)
    _, cols, rows, over = best
    return cols, rows, over


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="把 N 张单帧摆成均匀网格 raw，供 process_sheets.py 使用")
    ap.add_argument("--frames", required=True,
                    help="输入单帧：目录 / glob / 逗号分隔的多个路径")
    ap.add_argument("--name", required=True, help="动作名，如 Player_Run")
    ap.add_argument("--out", default=None,
                    help="输出网格路径（默认 <ART_ROOT>/<角色>/Sheets/raw/{name}_raw.png）")
    ap.add_argument("--cols", type=int, default=0, help="列数（默认自动推导）")
    ap.add_argument("--rows", type=int, default=0, help="行数（默认自动推导；一般不用给）")
    ap.add_argument("--cell", default="", help="单格尺寸 WxH（默认取所有帧的最大宽高）")
    ap.add_argument("--max-side", type=int, default=DEFAULT_MAX_SIDE,
                    help=f"画布单边上限，超出只告警不阻断（默认 {DEFAULT_MAX_SIDE}）")
    ap.add_argument("--bg", default="alpha",
                    choices=["alpha", "white", "black"],
                    help="空格填充：alpha=全透明(默认) / white / black")
    ap.add_argument("--dry", action="store_true", help="只打印方案，不落盘")
    ap.add_argument("--run-pipeline", action="store_true",
                    help="拼完立刻调用 process_sheets.py（复用既有抠像/缩放/对齐）")
    a = ap.parse_args(argv)

    files = collect_frames(a.frames)
    if not files:
        raise SystemExit(f"[ERR] 没找到任何帧：{a.frames}")
    n = len(files)
    print(f"[输入] {n} 帧")
    for i, f in enumerate(files[:3]):
        print(f"   [{i:02d}] {os.path.basename(f)}")
    if n > 3:
        print(f"   ... 共 {n} 张")

    # --- 单格尺寸
    sizes = []
    for f in files:
        try:
            with Image.open(f) as im:
                sizes.append(im.size)
        except Exception as e:
            raise SystemExit(f"[ERR] 打不开 {f}: {e}")

    if a.cell:
        m = re.fullmatch(r"(\d+)[xX*](\d+)", a.cell.strip())
        if not m:
            raise SystemExit(f"[ERR] --cell 格式应为 WxH，收到 {a.cell}")
        cw, ch = int(m.group(1)), int(m.group(2))
    else:
        cw = max(w for w, _ in sizes)
        ch = max(h for _, h in sizes)

    # --- 网格
    if a.cols and a.rows:
        cols, rows, over = a.cols, a.rows, (a.cols * cw > a.max_side or a.rows * ch > a.max_side)
        if cols * rows < n:
            raise SystemExit(f"[ERR] {cols}x{rows}={cols*rows} 格装不下 {n} 帧")
    elif a.cols:
        cols = a.cols
        rows = math.ceil(n / cols)
        over = (cols * cw > a.max_side or rows * ch > a.max_side)
    else:
        cols, rows, over = auto_grid(n, cw, ch, a.max_side)

    W, H = cols * cw, rows * ch

    print(f"\n[方案] 单帧 {cw}x{ch}  ->  {cols}列 x {rows}行 = {cols*rows} 格"
          f"（{cols*rows-n} 空格）  ->  画布 {W}x{H}")
    if over:
        print(f"[warn] 画布 {W}x{H} 超过单边上限 {a.max_side}：")
        print(f"       如果这张 raw 只是中间产物（最终切成单帧），无所谓；")
        print(f"       如果要当 Sprite Sheet 单张导入 Unity，需调大 maxTextureSize 或缩小单帧。")

    out = a.out or os.path.join(
        ART_ROOT, a.name.split("_")[0], "Sheets", "raw", f"{a.name}_raw.png")

    if a.dry:
        print(f"\n[dry] 未落盘。将输出到：{out}")
        _print_spec_hint(a.name, cols, rows, n, cw, ch)
        return 0

    # --- 拼版
    if a.bg == "alpha":
        fill = (0, 0, 0, 0)
    elif a.bg == "white":
        fill = (255, 255, 255, 255)
    else:
        fill = (0, 0, 0, 255)

    canvas = Image.new("RGBA", (W, H), fill)
    placed = 0
    for i, f in enumerate(files):
        cx, cy = (i % cols) * cw, (i // cols) * ch
        with Image.open(f) as im:
            fr = im.convert("RGBA")
            fw, fh = fr.size
            if fw > cw or fh > ch:
                s = min(cw / fw, ch / fh)
                fw2, fh2 = max(1, round(fw * s)), max(1, round(fh * s))
                fr = fr.resize((fw2, fh2), Image.LANCZOS)
                fw, fh = fw2, fh2
            # 居中贴：切格后 process_sheets 的居中量最小，最不容易踩到连通域清理
            ox = cx + (cw - fw) // 2
            oy = cy + (ch - fh) // 2
            # 必须用 paste 而不是 alpha_composite：
            # alpha_composite 会把 alpha=0 像素的 RGB 清成 (0,0,0)，虽然肉眼无差，
            # 但 process_sheets 的 BLACK_THR=60 近黑判据会把它们当成「黑底」，
            # 可能误判走错抠图分支。paste 是直接覆盖矩形，原始 RGB 完整保留。
            canvas.paste(fr, (ox, oy))
        placed += 1

    os.makedirs(os.path.dirname(out), exist_ok=True)
    backup = out + ".pre-compose.png" if os.path.exists(out) else None
    if backup:
        # 旧 raw 是美术资产来源，覆盖前留一份，避免不可逆
        import shutil
        shutil.copy2(out, backup)
        print(f"[warn] 已覆盖原有 raw，旧文件备份为 {os.path.basename(backup)}")

    canvas.save(out)
    print(f"\n[ok ] 已写入 {out}  ({W}x{H}, {placed} 帧)")
    _print_spec_hint(a.name, cols, rows, n, cw, ch)

    if a.run_pipeline:
        print("\n[pipe] 调用 process_sheets.py（复用既有抠像/缩放/对齐）...")
        cmd = [sys.executable, os.path.join(HERE, "process_sheets.py"),
               "--only", a.name]
        rc = subprocess.call(cmd)
        if rc != 0:
            print(f"[warn] process_sheets.py 返回 {rc}。如果报「未登记动作」，"
                  f"把上面那行 SPECS 加进 process_sheets.py 即可。")
        return rc

    return 0


def _print_spec_hint(name, cols, rows, n, cw, ch):
    """打印可直接粘进 process_sheets.py SPECS 的一行。

    除 cols/rows/n 外的参数全部继承同角色既有条目（与 auto_pipeline.derive_spec 一致），
    所以这里给的是「按 Player 通用口径」的模板。
    """
    cell = f"({cw},{ch})"
    print("\n[提示] 若该动作尚未在 SPECS 登记，需要加一行"
          "（其余参数继承同角色已有条目：画格宽锁死/身高 760/脚底锚点/去投影/去白边）：")
    print(f"    dict(name='{name}', cols={cols}, rows={rows}, n={n:<2d}, cell={cell}, "
          f"target=760, largest=True, stray_prune=True, anchor='base', "
          f"fixed_canvas=True, shadow_trim=True, anchor_final=True, "
          f"white_unmix=True, white_holes=True),")


if __name__ == "__main__":
    sys.exit(main())
