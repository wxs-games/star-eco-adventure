# -*- coding: utf-8 -*-
"""自动流水线：识别一张 raw 属于「一图单动作」还是「一图多动作」-> 自动选模式 -> 处理 -> 自检。

三种来源的判定顺序（先准后猜）：
  1) 文件名含 '+'  ：「Player_Attack01+Attack02_raw.png」= 一图多动作，按行带均分给每个动作
                     （生成端 --layout multi 产出的就是这种，一行=一个动作）；
  2) 名字在 process_sheets.SPECS 注册表里：一动作一图，直接按注册参数处理（最可靠，不问识图）；
  3) 都没命中：调 detect_layout.py 的识图层判断——1 段=单动作（参数从同角色参考条目派生），
               多段=多动作（按识图给的帧区间切分）。
     识图层是这个模型的分歧点：它会把「一套连招里的第二段」也算成一个动作，
     所以已登记的动作一律以注册表为准，不让识图去改。

用法：
  python auto_pipeline.py --scan Player                 # 只看判定，不动任何文件
  python auto_pipeline.py Player                       # 处理该角色全部 raw
  python auto_pipeline.py Player --only Attack02 Idle
  python auto_pipeline.py Player --only Attack01+Attack02 --vision   # 顺带识图复核
"""
import sys, os, json, argparse
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import process_sheets as ps
import detect_layout as dl
import slice_actions as sa
import subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
LAYOUT_DIR = os.path.join(HERE, '_layout')
SPEC_BY_NAME = {s['name']: s for s in ps.SPECS}


def char_root(char):
    if char == 'Player':  return os.path.join(ps.ROOT, 'Player')
    if char == 'Boss01':  return os.path.join(ps.ROOT, 'Boss', 'Boss01')
    if char.startswith('Enemy'): return os.path.join(ps.ROOT, 'Enemies', char)
    if char.startswith('Tower'): return os.path.join(ps.ROOT, 'Towers', char)
    if char.startswith('NPC'):   return os.path.join(ps.ROOT, 'NPC', char)
    raise SystemExit('[ERR] 未知角色目录: ' + char)


def list_raws(char):
    d = os.path.join(char_root(char), 'Sheets', 'raw')
    if not os.path.isdir(d):
        raise SystemExit('[ERR] 没有 raw 目录: ' + d)
    out = []
    for f in sorted(os.listdir(d)):
        if f.endswith('_raw.png'):
            out.append((f[:-len('_raw.png')], os.path.join(d, f)))
    return out


def band_layout(src, base, cols, rows, k):
    """把 k 个动作按行带均分；行数除不尽就报错要求手写 layout。"""
    if rows % k:
        raise SystemExit(f'[ERR] {base}: 网格 {cols}x{rows} 不能均分给 {k} 个动作，'
                         f'请用 --layout-json 指定每个动作占的行带')
    per = rows // k
    prefix = base.split('_')[0] + '_'          # Player_Attack01+Attack02 -> 第二个名字自动补前缀
    names = [n if n.startswith(prefix) else prefix + n for n in base.split('+')]
    acts = [dict(name=names[i], row0=i * per + 1, row1=(i + 1) * per, col0=1, col1=cols)
            for i in range(k)]
    return dict(prefix=base.split('_')[0], out=os.path.join(os.path.basename(os.path.dirname(os.path.dirname(src))),
                                                           'raw'), grid=dict(cols=cols, rows=rows),
                actions=acts)


def derive_spec(name, cols, rows, n, ref_name=None):
    """未登记的动作：从同角色参考条目派生一套参数，保证画格/身高/锚点与全角色一致。"""
    ref = SPEC_BY_NAME.get(ref_name) if ref_name else None
    if ref is None:
        same = [s for s in ps.SPECS if s['name'].startswith(name.split('_')[0])]
        if not same:
            raise SystemExit('[ERR] 找不到可继承的参考条目: ' + name)
        # 优先挑帧数相同、其次帧数最多的
        ref = sorted(same, key=lambda s: (s['n'] != n, -s['n']))[0]
    spec = dict(ref)
    spec.update(name=name, cols=cols, rows=rows, n=n)
    spec.pop('ref_first', None)
    print(f'  [派生] {name} 参数继承自 {ref["name"]}  cell={spec["cell"]} target={spec["target"]}')
    return spec


def plan_for(char, base, src, use_vision):
    """返回 (mode, payload, note)"""
    if '+' in base:
        cols, rows, _, _ = dl.infer_grid(dl.load_mask(src)[1])
        names = base.split('+')
        n = cols * rows
        if n % len(names):
            raise SystemExit(f'[ERR] {base}: {cols}x{rows}={n} 帧不能整除 {len(names)} 个动作')
        note = f'文件名含 +，按 {len(names)} 个动作、各 {n // len(names)} 帧处理（{cols}列x{rows}行）'
        return 'multi', dict(cols=cols, rows=rows, names=names, per=n // len(names)), note

    if base in SPEC_BY_NAME:
        s = SPEC_BY_NAME[base]
        return 'single', dict(spec=s), f'已登记（cols={s["cols"]} rows={s["rows"]} n={s["n"]}）'

    r = dl.detect(src, use_vision=use_vision)
    if r['mode'] == 'multi':
        note = f'未登记 + 识图判定 {len(r["actions"])} 个动作：{[(a["name"], a["frames"]) for a in r["actions"]]}'
        return 'multi-vision', dict(segments=r['actions'], grid=r['grid']), note
    note = f'未登记 + {"识图" if r["method"]=="vision" else "几何"}判定单动作({r["frames"]}帧)'
    return 'single-new', dict(grid=r['grid'], frames=r['frames']), note


def run_one(char, base, src, use_vision, dry):
    mode, payload, note = plan_for(char, base, src, use_vision)
    print(f'\n=== {base}  ->  {mode} ===')
    print('  判定: ' + note)

    if mode == 'single':
        if dry:
            print('  [dry] process_sheets ' + payload['spec']['name'])
            return [payload['spec']['name']]
        ps.process(payload['spec'])
        return [payload['spec']['name']]

    if mode == 'single-new':
        cols, rows = payload['grid']
        spec = derive_spec(base, cols, rows, payload['frames'])
        if dry:
            print(f'  [dry] 派生参数后处理 {base}')
            return [base]
        ps.process(spec)
        return [base]

    # ---- 多动作：切分 ----
    if mode == 'multi':
        cols, rows, names, per = payload['cols'], payload['rows'], payload['names'], payload['per']
        lay = band_layout(src, base, cols, rows, len(names))
    else:  # multi-vision：按识图给的帧区间转成行带（要求区间对齐整行）
        cols, rows = payload['grid']
        names, acts = [], []
        for seg in payload['segments']:
            r0 = (seg['from_'] - 1) // cols + 1
            r1 = (seg['to'] - 1) // cols + 1
            nm = f"{base}_A{seg['index']}"
            names.append(nm)
            acts.append(dict(name=nm, row0=r0, row1=r1, col0=1, col1=cols))
        lay = dict(prefix=base, out='', grid=dict(cols=cols, rows=rows), actions=acts)
    os.makedirs(LAYOUT_DIR, exist_ok=True)
    lp = os.path.join(LAYOUT_DIR, base.replace('+', '_') + '_layout.json')
    json.dump(lay, open(lp, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    print('  layout: ' + lp)
    if dry:
        for a in lay['actions']:
            print(f'  [dry] 切出 {a["name"]}  行{a["row0"]}-{a["row1"]}')
        return names

    raw_dir = os.path.dirname(src)
    subprocess.check_call([sys.executable, os.path.join(HERE, 'slice_actions.py'),
                           '--src', src, '--layout', lp, '--out', raw_dir])
    done = []
    for a in lay['actions']:
        nm = a['name']
        spec = SPEC_BY_NAME.get(nm) or derive_spec(nm, cols, a['row1'] - a['row0'] + 1,
                                                   cols * (a['row1'] - a['row0'] + 1))
        ps.process(spec)
        done.append(nm)
    return done


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument('char')
    ap.add_argument('--only', nargs='*', default=None)
    ap.add_argument('--scan', action='store_true', help='只判定不处理')
    ap.add_argument('--vision', action='store_true', help='对未登记/多动作图跑识图复核')
    a = ap.parse_args(argv)

    raws = list_raws(a.char)
    if a.only:
        raws = [r for r in raws if any(o in r[0] for o in a.only)]
    if not raws:
        raise SystemExit('[ERR] 没有匹配的 raw')

    done = []
    for base, src in raws:
        try:
            done += run_one(a.char, base, src, a.vision, a.scan)
        except Exception as e:
            print(f'  [FAIL] {base}: {e}')
            raise

    if not a.scan and done:
        print('\n---- 自检 ----')
        subprocess.call([sys.executable, os.path.join(HERE, 'verify_raw_grid.py'), *done])
    print(f'\n处理 {len(done)} 条：' + ', '.join(done))


if __name__ == '__main__':
    main(sys.argv[1:])
