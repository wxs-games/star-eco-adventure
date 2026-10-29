#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""sandbox_process.py —— 在沙箱里跑 process_sheets，绝不触碰工程 Assets。

原理：process_sheets.py 用 `os.path.dirname(__file__)/../../Assets/Art/Art` 相对定位 ROOT，
所以把副本放进 <sandbox>/Tools/art-pipeline/，ROOT 就自动指向 <sandbox>/Assets/Art/Art。
只需把 SPECS 换成单条目，即可只处理本次的新动作。
"""
import argparse
import os
import re
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "process_sheets.py")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sheet", required=True, help="已拼好的 raw 网格 png")
    ap.add_argument("--name", required=True, help="动作名，如 Player_Run12")
    ap.add_argument("--cols", type=int, required=True)
    ap.add_argument("--rows", type=int, required=True)
    ap.add_argument("--cell", required=True, help="单格 WxH")
    ap.add_argument("--target", type=int, default=760)
    ap.add_argument("--sandbox", required=True)
    ap.add_argument("--python", default=sys.executable)
    ap.add_argument("--white-clean", action="store_true",
                    help="开启白边/封闭白清理。输入本身已是原生 alpha（骨骼渲染）时不要开，"
                         "否则眼白、白 T 恤这类纯白会被当成背景白挖掉")
    a = ap.parse_args()

    cw, ch = a.cell.lower().split("x")
    wc = "True" if a.white_clean else "False"
    spec = (f"    dict(name='{a.name}', cols={a.cols}, rows={a.rows}, n={a.cols*a.rows}, "
            f"cell=({cw},{ch}), target={a.target}, largest=True, stray_prune=True, "
            f"anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, "
            f"white_unmix={wc}, white_holes={wc}),\n")

    tool_dir = os.path.join(a.sandbox, "Tools", "art-pipeline")
    raw_dir = os.path.join(a.sandbox, "Assets", "Art", "Art", "Player", "Sheets", "raw")
    os.makedirs(tool_dir, exist_ok=True)
    os.makedirs(raw_dir, exist_ok=True)

    with open(SRC, "r", encoding="utf-8") as f:
        src = f.read()
    new, cnt = re.subn(r"SPECS = \[.*?\n\]", "SPECS = [\n" + spec + "]", src, flags=re.S)
    if cnt != 1:
        raise SystemExit(f"SPECS 替换失败（匹配 {cnt} 次）")
    dst_py = os.path.join(tool_dir, "process_sheets.py")
    with open(dst_py, "w", encoding="utf-8") as f:
        f.write(new)

    shutil.copy2(a.sheet, os.path.join(raw_dir, f"{a.name}_raw.png"))
    print(f"[sandbox] 脚本 -> {dst_py}")
    print(f"[sandbox] raw  -> {os.path.join(raw_dir, a.name + '_raw.png')}")

    r = subprocess.run([a.python, dst_py], capture_output=True, text=True, encoding="utf-8")
    print(r.stdout.strip())
    if r.returncode != 0:
        print(r.stderr.strip()[-1500:])
        raise SystemExit(f"process_sheets 退出码 {r.returncode}")

    out = os.path.join(a.sandbox, "Assets", "Art", "Art", "Player")
    print(f"[sandbox] Frames -> {os.path.join(out, 'Frames', a.name)}")
    print(f"[sandbox] Sheet  -> {os.path.join(out, 'Sheets', a.name + '_sheet.png')}")


if __name__ == "__main__":
    main()
