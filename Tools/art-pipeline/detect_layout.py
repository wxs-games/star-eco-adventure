# -*- coding: utf-8 -*-
"""布局识别：判断一张 raw 精灵表是「一图单动作(多帧)」「一图多动作」还是「一图单帧」。
   给 auto_pipeline.py 用来自动选择处理模式。

两层判定：
  1) 几何层（确定性，不花钱）：按 alpha 投影推断网格 cols x rows、每格是否有主体、
     以及相邻帧剪影差异曲线（哪两帧之间姿态突变最大）。
  2) 语义层（识图模型）：把图交给多模态模型，直接问「这张图里有几个不同的动作、
     每个动作第几帧到第几帧」。
     —— 语义层是主判定。原因：相邻动作的「收尾帧 / 起手帧」姿势往往几乎一样，
        纯几何分不出边界，必须靠识图。

用法：
  python detect_layout.py --src <raw.png>                       # 人读输出
  python detect_layout.py --src <raw.png> --json                # 机器读 JSON
  python detect_layout.py --src <raw.png> --cols 4 --rows 2     # 已知网格时跳过推断
  python detect_layout.py --src <raw.png> --no-vision           # 只跑几何层
"""
import sys, os, json, base64, argparse, re
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from PIL import Image

ALPHA_MIN = 16          # alpha 低于此值算透明
GAP_RATIO = 0.006       # 判定「空沟」的最小宽度占比
VIS_MAX_SIDE = 1600     # 送识图前把长边压到该值，省 token 又不影响数格子


# ---------------- 几何层 ----------------
def load_mask(path):
    """返回 (size, 主体掩码, 底色类型)。
    原生透明底直接用 alpha；整张不透明的（生图常见的白底）按「非近白」抠出主体，
    否则掩码会全 True，几何层就完全失效。"""
    im = Image.open(path).convert('RGBA')
    a = np.array(im.getchannel('A'))
    if (a > ALPHA_MIN).mean() > 0.98:
        rgb = np.array(im.convert('RGB'), dtype=np.int16)
        mask = rgb.min(axis=2) < 235          # 三通道都近乎纯白才算背景
        bg = 'white'
    else:
        mask = a > ALPHA_MIN
        bg = 'alpha'
    return im.size, mask, bg


def _runs(flags):
    """返回全是 True 的连续区间 [(start, end_exclusive), ...]"""
    out, s = [], None
    for i, v in enumerate(flags):
        if v and s is None:
            s = i
        elif not v and s is not None:
            out.append((s, i)); s = None
    if s is not None:
        out.append((s, len(flags)))
    return out


def infer_grid(mask):
    """按 alpha 投影找内部空沟，推断 cols / rows。"""
    H, W = mask.shape
    col_empty = mask.sum(axis=0) == 0
    row_empty = mask.sum(axis=1) == 0
    min_gap_c = max(2, int(W * GAP_RATIO))
    min_gap_r = max(2, int(H * GAP_RATIO))
    gaps_c = [(s, e) for s, e in _runs(col_empty) if e - s >= min_gap_c]
    gaps_r = [(s, e) for s, e in _runs(row_empty) if e - s >= min_gap_r]
    # 丢掉最外侧的两条（那是画布外边距，不是分隔）
    inner_c = gaps_c[1:-1] if len(gaps_c) >= 2 else []
    inner_r = gaps_r[1:-1] if len(gaps_r) >= 2 else []
    return len(inner_c) + 1, len(inner_r) + 1, inner_c, inner_r


def cell_boxes(W, H, cols, rows):
    cw, ch = W / cols, H / rows
    return [(int(round((c) * cw)), int(round((r) * ch)),
             int(round((c + 1) * cw)), int(round((r + 1) * ch)))
            for r in range(rows) for c in range(cols)]


def cell_metrics(mask, cols, rows, thumb=64):
    H, W = mask.shape
    boxes = cell_boxes(W, H, cols, rows)
    occ, heights, widths, thumbs = [], [], [], []
    for (x0, y0, x1, y1) in boxes:
        sub = mask[y0:y1, x0:x1]
        n = int(sub.sum())
        occ.append(n)
        if n > 0:
            ys = np.where(sub.any(axis=1))[0]
            xs = np.where(sub.any(axis=0))[0]
            heights.append(int(ys[-1] - ys[0] + 1))
            widths.append(int(xs[-1] - xs[0] + 1))
            t = Image.fromarray((sub * 255).astype(np.uint8)).resize((thumb, thumb), Image.BILINEAR)
            thumbs.append((np.array(t, dtype=np.float32) / 255.0, ys[0], xs[0]))
        else:
            heights.append(0); widths.append(0); thumbs.append(None)
    deltas = []
    for i in range(1, len(thumbs)):
        a, b = thumbs[i - 1], thumbs[i]
        if a is None or b is None:
            deltas.append(None); continue
        # 对齐到脚底/左沿后再比，避免单纯「位置偏移」被当成姿态变化
        ta = Image.fromarray((a[0] * 255).astype(np.uint8))
        tb = Image.fromarray((b[0] * 255).astype(np.uint8))
        da = np.abs(np.array(ta, np.float32) - np.array(tb, np.float32)).mean() / 255.0
        deltas.append(round(float(da), 4))
    return dict(boxes=boxes, occ=occ, heights=heights, widths=widths, deltas=deltas)


# ---------------- 语义层（识图） ----------------
def _gateway():
    p = os.path.join(os.environ.get('USERPROFILE', ''), '.codex', 'config.toml')
    txt = open(p, encoding='utf-8').read()
    base = re.search(r'base_url\s*=\s*"([^"]+)"', txt)
    key = re.search(r'experimental_bearer_token\s*=\s*"([^"]+)"', txt)
    if not base or not key:
        return None, None
    return base.group(1).rstrip('/'), key.group(1)


def _img_data_url(path):
    im = Image.open(path).convert('RGB')
    w, h = im.size
    if max(w, h) > VIS_MAX_SIDE:
        s = VIS_MAX_SIDE / max(w, h)
        im = im.resize((int(w * s), int(h * s)), Image.LANCZOS)
    import io
    buf = io.BytesIO()
    im.save(buf, format='PNG')
    return 'data:image/png;base64,' + base64.b64encode(buf.getvalue()).decode()


VISION_PROMPT = """这是一张游戏角色的精灵表（sprite sheet），网格是 {cols} 列 x {rows} 行，共 {n} 帧，
帧号按「从左到右、从上到下」从 1 开始数。

请判断：这张图里一共有几个【不同的动作】？

判断标准：
- 如果整张图是同一个动作的连续动作分解（比如整个跑步循环、整个攻击挥砍），那就是 1 个动作。
- 如果图里前半部分是走路/待机、后半部分是另一种攻击或另一套循环，那是多个动作。
- 相邻两帧姿势差异小、动作连贯 = 同一个动作；出现明显「换个动作重来」的跳变 = 动作边界。

只输出一行 JSON，不要任何解释文字，格式严格如下：
{{"actions":[{{"name":"动作名(中文或英文皆可)","from":1,"to":8}}],"note":"一句话理由"}}

要求：
- actions 覆盖 1..{n} 的全部帧，区间首尾相接、不重叠、不遗漏；
- 只有一个动作时也照常输出该动作的完整区间。"""


def ask_vision(path, cols, rows, n):
    base, key = _gateway()
    if not base or not key:
        return None, '缺少 gateway 配置（config.toml 里没有 base_url / experimental_bearer_token）'
    body = {
        'model': 'deepseek-v4.1-flash-1017',
        'messages': [{'role': 'user', 'content': [
            {'type': 'text', 'text': VISION_PROMPT.format(cols=cols, rows=rows, n=n)},
            {'type': 'image_url', 'image_url': {'url': _img_data_url(path)}},
        ]}],
        'max_tokens': 12000,  # 思考过程也算 completion，给小了只出 reasoning、content 为空
    }
    try:
        import urllib.request
        req = urllib.request.Request(base + '/chat/completions',
                                     data=json.dumps(body).encode(),
                                     headers={'Authorization': 'Bearer ' + key,
                                              'Content-Type': 'application/json'})
        with urllib.request.urlopen(req, timeout=180) as r:
            j = json.loads(r.read().decode())
        msg = (j.get('choices') or [{}])[0].get('message', {}) or {}
        txt = msg.get('content') or msg.get('reasoning_content') or ''
        m = re.search(r'\{.*\}', txt, re.S)
        if not m:
            return None, '识图输出里没有 JSON：' + txt[:160]
        return json.loads(m.group(0)), None
    except Exception as e:
        return None, f'识图调用失败：{e}'


# ---------------- 汇总 ----------------
def detect(src, cols=None, rows=None, use_vision=True):
    (W, H), mask, bg = load_mask(src)
    g_cols, g_rows, _, _ = infer_grid(mask)
    if cols is None or rows is None:
        cols, rows = g_cols, g_rows
        grid_src = 'inferred'
    else:
        grid_src = 'declared'
    n = cols * rows
    met = cell_metrics(mask, cols, rows)

    notes = []
    if grid_src == 'inferred' and (g_cols, g_rows) != (cols, rows):
        notes.append(f'推断网格 {g_cols}x{g_rows} 与使用网格 {cols}x{rows} 不一致')
    empty_cells = [i + 1 for i, v in enumerate(met['occ']) if v == 0]
    if empty_cells:
        notes.append(f'空格子 {len(empty_cells)} 个：{empty_cells[:10]}')
    big_jump = [i + 1 for i, d in enumerate(met['deltas']) if d is not None and d >= 0.35]
    if big_jump:
        notes.append(f'第 {big_jump} 帧前出现大幅姿态跳变（可能是动作边界）')

    res = dict(src=src, size=[W, H], bg=bg, grid=[cols, rows], grid_source=grid_src,
               frames=n, empty_cells=empty_cells, deltas=met['deltas'],
               heights=met['heights'], notes=notes)

    vis, err = (None, '未启用')
    if use_vision:
        vis, err = ask_vision(src, cols, rows, n)
    if vis and vis.get('actions'):
        acts = vis['actions']
        segs, cur = [], 1
        for i, ac in enumerate(acts):
            f, t = int(ac.get('from', cur)), int(ac.get('to', n))
            segs.append(dict(index=i + 1, name=str(ac.get('name', f'动作{i+1}')),
                             from_=f, to=t, frames=t - f + 1))
            cur = t + 1
        res['actions'] = segs
        res['note_vision'] = vis.get('note', '')
        res['mode'] = ('single_frame' if n == 1 else 'single') if len(segs) == 1 else 'multi'
        res['confidence'] = 0.9 if not notes else 0.75
        res['method'] = 'vision'
    else:
        # 回退：几何跳变分段
        bounds = [i + 2 for i, d in enumerate(met['deltas']) if d is not None and d >= 0.35]
        cuts = [1] + [b for b in bounds if 1 < b <= n] + [n + 1]
        segs = [dict(index=i + 1, name=f'动作{i+1}', from_=cuts[i], to=cuts[i + 1] - 1,
                     frames=cuts[i + 1] - cuts[i]) for i in range(len(cuts) - 1)]
        res['actions'] = segs
        res['note_vision'] = '识图不可用（' + str(err) + '），已回退几何判据'
        res['mode'] = ('single_frame' if n == 1 else 'single') if len(segs) == 1 else 'multi'
        res['confidence'] = 0.5
        res['method'] = 'geometry'
    return res


def main(argv):
    p = argparse.ArgumentParser()
    p.add_argument('--src', required=True)
    p.add_argument('--cols', type=int)
    p.add_argument('--rows', type=int)
    p.add_argument('--no-vision', action='store_true')
    p.add_argument('--json', action='store_true')
    a = p.parse_args(argv)
    r = detect(a.src, a.cols, a.rows, not a.no_vision)
    if a.json:
        print(json.dumps(r, ensure_ascii=False, indent=2))
        return
    print(f'文件   : {os.path.basename(r["src"])}  {r["size"][0]}x{r["size"][1]}')
    print(f'网格   : {r["grid"][0]}列 x {r["grid"][1]}行 = {r["frames"]} 帧（{r["grid_source"]}）')
    print(f'判定   : mode={r["mode"]}  置信度={r["confidence"]}  依据={r["method"]}')
    print(f'动作   : ' + ' | '.join(f'{s["name"]} 第{s["from_"]}-{s["to"]}帧({s["frames"]}帧)' for s in r['actions']))
    if r.get('note_vision'):
        print(f'识图说明: {r["note_vision"]}')
    if r['notes']:
        print('几何提示: ' + '；'.join(r['notes']))
    print('相邻帧剪影差异: ' + ' '.join('-' if d is None else f'{d:.2f}' for d in r['deltas']))


if __name__ == '__main__':
    main(sys.argv[1:])
