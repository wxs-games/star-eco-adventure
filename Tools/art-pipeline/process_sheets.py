# -*- coding: utf-8 -*-
"""
精灵图集流水线（alpha-first，全程透明底）：
  入口归一化（透明底直通 / 白底·黑底 -> 先抠图 -> 统一 RGBA master）
  -> 切格 -> 连通域清理（去碎影 / 可只留最大域）
  -> 条内身高中位数统一缩放 -> 脚底基线对齐（底留白 7px）+ 水平居中
  -> 输出 Frames 单帧 + Sheets 等大透明精灵表

说明：全程以 alpha 为准，不再经过白底中间图。非透明底来源在入口先抠成 alpha；
      阈值抠图的边缘质量天然低于原生 alpha，故只对这类来源额外做 1px 收缩去白晕。
"""
import sys, os, glob, re
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from PIL import Image, ImageFilter
from collections import deque

try:
    from scipy import ndimage
    HAVE_SCIPY = True
except ImportError:
    HAVE_SCIPY = False

# 脚本位于 <工程根>\Tools\art-pipeline\，向上两级定位工程根（含 Assets 的目录），免配置路径
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Art', 'Art')
BOT_GAP = 7          # 与既有素材一致的脚底留白
BASE_BAND = 74       # 底座锚点带高度（脚底留白之上），见 base_anchor_x
WHITE_THR = 232      # 近白阈值
BLACK_THR = 60       # 近黑阈值
KEEP_RATIO = 0.12    # 小于最大连通域该比例的碎影丢弃
ALPHA_MIN = 8        # 低于该 alpha 视为完全透明
SHADOW_LUM = 150     # 地面投影亮度下限（投影是浅灰→近白的渐变，取 150 把最淡的尾巴也清掉；
                     # 履带高光 160~175 与投影重叠，靠「主体剪影填孔」保护，不会被误删）
SHADOW_SAT = 0.05    # 投影彩度上限（投影是中性灰，饱和度≈0）
SHADOW_ROWS = 56     # 只在内容底部该行数范围内找投影
POCKET_ROWS = 50     # 近白连通域底边进入脚底该行数范围 -> 判为漏抠的背景白（肩部烟柱底边远高于此）
POCKET_LUM = 222     # 轮拱白板的单通道下限（比 WHITE_THR 松，才能把整块白连同边缘一起框进连通域）
POCKET_SAT = 0.06    # 轮拱白板的彩度上限（背景白中性；发光核心偏暖、彩度更高，靠这条跳过）
POCKET_AREA = 600    # 小于该面积的近白小高光不算白板
POCKET_TOP = 0.5     # 白板顶边落在内容高度该比例以下才清理（上半段是烟雾/核心）
HOLE_MINCH = 240.0    # 「封闭背景白」判据：块内「三通道最小值」的中位数 >= 240。
                      # 这是唯一可靠的判据 —— 实测背景白三通道 248~255，文生图画上去的白烟/
                      # 高光三通道最高只到 ~232（Enrage 第 4 帧两团白烟 medMin=226/228），
                      # 两者不重叠。早期用的「均值>=244 + 240占比>=60%」会把抗锯齿糊住的
                      # 中间值混进来，判不准
HOLE_AREA_FINAL = 8   # 成品像素最小面积。只留作 raw 阶段之后的兜底（raw 已清掉主体），
                      # 这里只抓缩放重新插值出来的残余小岛
FEATHER_ROWS = 56    # 顶部羽化行数：生成图把烟柱画到格子上沿就被截断，靠渐隐把硬切口藏掉

# ---- 18 条规格 ----
SPECS = [
    # name, cols, rows, n, cell(w,h), target_h
    # ---- Player（2026-10-26 依新定妆图 Player_Lockup_A.png 重生成全部动作）----
    # 与 Boss01 同口径：cell 692x833 / target 760 / fixed_canvas 画格宽锁死 / anchor='base' 脚底
    # 锚点 / shadow_trim 去脚底灰影 / white_unmix+white_holes 去白边与内部白洞 —— 唯一目的就是
    # 「任何动作切换时体型与画布零跳变」（旧素材 7 种画布宽、身高 267~794 的病灶由此修掉）。
    # norm='frame'：源图把同一站姿画成不同大小，逐帧等高归一（仅直立姿态的动作可用）。
    # 其余用默认 ref='max'：全条统一比例、最高帧=760（翻滚/蹲伏/落地/死亡姿态高度本就变化，
    # 逐帧等高会把蹲伏帧放大成巨人）。
    dict(name='Player_Idle',        cols=5, rows=2, n=10, cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Idle02',      cols=4, rows=2, n=8 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Attack01',    cols=4, rows=2, n=8 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Attack02',    cols=4, rows=2, n=8 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Attack03',    cols=3, rows=3, n=9 , cell=(1200,833), target=760, ref_h=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_HeavyAttack', cols=4, rows=3, n=12 , cell=(1200,833), target=760, ref_h=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Victory',     cols=4, rows=2, n=8 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Interact',    cols=3, rows=2, n=6 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Hurt',        cols=3, rows=1, n=3 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Turn',        cols=1, rows=1, n=1 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_LedgeGrab',   cols=3, rows=1, n=3 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_WallSlide',   cols=3, rows=1, n=3 , cell=(1200,833), target=760, largest=True, stray_prune=True, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_WallJump',    cols=3, rows=1, n=3 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Run',         cols=4, rows=2, n=8 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Dash',        cols=3, rows=2, n=6 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Roll',        cols=4, rows=2, n=8 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Crouch',      cols=2, rows=1, n=2 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    # Die 由 .workbuddy/tmp/gen_die_joint.py（4 段骨骼 LBS）直接产出成品帧，不走本管线；
    # 这里登记参数只为让 compose_sheet / measure_base 等旁路工具对齐画格数（5x2 = 10 帧）。
    dict(name='Player_Die',         cols=5, rows=2, n=10, cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Jump',        cols=3, rows=1, n=3 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Land',        cols=3, rows=1, n=3 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_Fall',        cols=2, rows=2, n=4 , cell=(1200,833), target=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Player_AttackAir',   cols=3, rows=2, n=6 , cell=(1200,833), target=760, ref_h=760, largest=True, stray_prune=True, anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Enemy03_Spawn',     cols=2, rows=2, n=4,  cell=(993,1124), target=900),
    # 注：Tower01 的 Idle/Aim/Disable 均不在本表 —— 三套的塔体要求与定妆图 Tower01_Fire_00
    #     逐像素一致，文生图无法保证，改由 make_tower_sets_from_fire.py 以 Fire_00 为底图派生
    #     （塔体零改动 + 背景区叠加效果）。若在此重新登记，跑全量管线会用 raw 覆盖掉派生结果。
    dict(name='Tower01_Fire',      cols=3, rows=2, n=6,  cell=(942,979), target=890, anchor='base'),
    # ---- Boss01（2026-10-26 依新定妆图 Boss01_Lockup_A.png 重生成全部动作）----
    # 统一口径：cell 831x798 / target 760 / norm='frame' 逐帧等高 / anchor='base' 履带底座锚点 /
    # fixed_canvas 强制画格宽一致 —— 唯一目的就是「任何动作切换时体型与画布零跳变」。
    # 旧素材的病灶：Idle 760 / Special 788 / Enrage 780 / Die 部分 737·707·676，且 Die_16~23
    # 画布是 1116x784（其余 831x798），切换时会忽大忽小；本批全部压平到 760。
    # parts=None：帧内含与本体分离的烟雾/毒雾，跳过「每格恰好 1 个主体」校验。
    # shadow_trim：生成图自带脚底浅灰投影，游戏里呈白色横条，按主体剪影外清理。
    # feather_top：仅烟雾类动作启用，烟柱顶到格子上沿时做渐隐，藏掉硬切口。
    dict(name='Boss01_Idle',    cols=3, rows=2, n=6,  cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Boss01_Move',    cols=5, rows=2, n=10, cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Boss01_AttackA', cols=5, rows=2, n=10, cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    dict(name='Boss01_AttackB', cols=4, rows=3, n=12, cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True, feather_top=True),
    dict(name='Boss01_Charge',  cols=4, rows=2, n=8,  cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True, feather_top=True),
    dict(name='Boss01_Special', cols=5, rows=3, n=14, cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True, feather_top=True),
    dict(name='Boss01_Enrage',  cols=3, rows=2, n=6,  cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True, feather_top=True),
    dict(name='Boss01_Hurt',    cols=3, rows=1, n=3,  cell=(1216,798), target=760, parts=None, norm='frame', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True),
    # Die：首帧站立 -> 倒地。不用 norm='frame'（逐帧等高会把倒地帧放大成巨人），
    # 改用 ref='first' 全条统一比例：站立帧与 Idle 同高 760，倒地自然变矮。
    dict(name='Boss01_Die',     cols=4, rows=4, n=16, cell=(1216,798), target=760, parts=None, ref='first', anchor='base', fixed_canvas=True, shadow_trim=True, anchor_final=True, white_unmix=True, white_holes=True, feather_top=True),
    # NPC：沿用现有 npc_ava.png 的导入规格（PPU 485、身高 922）——同角色同 PPU，
    # 场景里现成的 scale 1.16 / 世界高 2.20 单位不需要改，动画帧可直接替换静态图。
    # anchor='base'：NPC 站着讲话，手上举台账/抬手时 bbox 会被横向拉宽，
    # 按 bbox 居中会让身体左右漂移（实测 67.5px），改按脚底底座带定位。
    dict(name='NPC_Ava_JinlingAssault', cols=4, rows=2, n=8, cell=(692,955), target=922, anchor='base'),
    dict(name='NPC_Ava_Idle',      cols=4, rows=2, n=8, cell=(692,955), target=922, anchor='base'),
]

def subdir_for(name):
    if name.startswith('Player'):  return 'Player'
    if name.startswith('Enemy03'): return os.path.join('Enemies','Enemy03')
    if name.startswith('Tower01'): return os.path.join('Towers','Tower01')
    if name.startswith('Boss01'):  return os.path.join('Boss','Boss01')
    if name.startswith('NPC'):     # NPC_<角色>_<动作> -> NPC/NPC_<角色>
        return os.path.join('NPC', '_'.join(name.split('_')[:2]))
    raise ValueError(name)

def find_raw(name):
    d = os.path.join(ROOT, subdir_for(name), 'Sheets', 'raw')
    hits = [p for p in glob.glob(os.path.join(d, name + '_raw.*')) if not p.endswith('.meta')]
    if not hits: raise FileNotFoundError(name + '_raw.*')
    return hits[0]

# ===== 入口归一化：任意来源 -> 统一 RGBA master =====

def _bg_from_border(rgb):
    """用图像外框像素的中位亮度判定背景类型：亮=白底，暗=黑底。"""
    b = np.concatenate([rgb[0, :], rgb[-1, :], rgb[:, 0], rgb[:, -1]])
    return 'white' if np.median(b.mean(axis=1)) >= 128 else 'black'

def _border_connected(mask):
    """返回与图像边缘连通的区域（scipy 缺失时用 BFS 兜底）。"""
    h, w = mask.shape
    if HAVE_SCIPY:
        lbl, _ = ndimage.label(mask)
        border = set(lbl[0, :]) | set(lbl[-1, :]) | set(lbl[:, 0]) | set(lbl[:, -1])
        border.discard(0)
        return np.isin(lbl, list(border)) if border else np.zeros((h, w), bool)
    seen = np.zeros((h, w), bool); dq = deque()
    for x in range(w):
        for y in (0, h - 1):
            if mask[y, x] and not seen[y, x]: seen[y, x] = True; dq.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if mask[y, x] and not seen[y, x]: seen[y, x] = True; dq.append((y, x))
    while dq:
        y, x = dq.popleft()
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True; dq.append((ny, nx))
    return seen

def matte_background(rgb, bg):
    """非透明底 -> alpha：只去掉与边缘连通的背景色，保留内部同色区（白 T 恤 / 眼白 / 暗部细节）。"""
    target = (rgb >= WHITE_THR).all(axis=2) if bg == 'white' else (rgb <= BLACK_THR).all(axis=2)
    return ((~_border_connected(target)) * 255).astype(np.uint8)

RAW_HOLE_WHITE = 240   # 封闭白块判据：三通道都 >=240。raw 实测背景 248~255，文生图画上去的
                       # 白烟最高只到 ~232，两者不重叠，所以按这个值清不会伤到美术
RAW_HOLE_AREA = 6      # raw 像素最小面积。必须设得很小：肢体缝隙里的白底常常只有 8~59px 的细条
                       # （Boss01_Move 第 1 帧腋下那条就是 31px），放大 1.7 倍后变成 85px 的白块，
                       # 肉眼很明显。实测 9 条 Boss01 素材里 8~59px 的封闭白块共 221 个，
                       # 中位通道全都是 240~255（背景色），没有一个是画上去的白烟，所以可以放心清掉。

def kill_enclosed_bg_holes(rgb, alpha, white_thr=RAW_HOLE_WHITE, min_area=RAW_HOLE_AREA):
    """清「被肢体包住」的背景白。

    matte_background 只去「与图片边缘连通」的背景色。肢体缝隙里（腋下、手臂与躯干之间、
    烟柱与塔身之间）被整片包围的白底不是边缘连通的，会原样留下来 —— 抠完图就是一块白斑
    （Boss01_Move 第 1 帧腋下 799px、Boss01_Enrage 第 1 帧 6882px 都是这个原因）。

    判据只有一条：**封闭 + 三通道都 >= white_thr**。
    早期版本在「成品尺寸」上用「面积>=600 + 均值>=244 + 240占比>=60%」判，
    既漏掉小于 600px 的白斑（用户看到的腋下白底就是 100px），又因成品缩放后
    抗锯齿把背景白和画的烟糊在一起而判不准。

    必须在 unmix_white_edge 之前调用：洞要先打开，边缘反混合才能把洞口那一圈
    抗锯齿白边一起解掉；否则洞开了、边上仍留一圈白。
    """
    if not HAVE_SCIPY:
        return alpha
    m = alpha > ALPHA_MIN
    tgt = m & (rgb >= white_thr).all(axis=2)
    lbl, n = ndimage.label(tgt)
    if n == 0:
        return alpha
    border = set(lbl[0, :]) | set(lbl[-1, :]) | set(lbl[:, 0]) | set(lbl[:, -1])
    border.discard(0)
    fringe = m & (rgb >= WHITE_THR).all(axis=2)     # 洞边抗锯齿过渡带（232~239）
    out = alpha.copy()
    killed = 0
    for i, sl in enumerate(ndimage.find_objects(lbl), start=1):
        if sl is None or i in border:
            continue
        comp = (lbl == i)
        if int(comp.sum()) < min_area:
            continue
        kill = comp | (ndimage.binary_dilation(comp) & fringe)
        out[kill] = 0
        killed += int(kill.sum())
    if killed:
        print(f'  [hole-raw] kill enclosed bg-white {killed} px')
    return out

def unmix_white_edge(rgb, alpha, ring=2, core_lum=240.0):
    """白边反混合：去掉白底抠图留下的白色描边。

    源图是 RGB（无 alpha）。边界一圈抗锯齿像素本来就是「机体色 × 白底」按 alpha 混合出来的
    浅色像素，阈值抠图把它们整圈当成不透明保留下来 → 输出边缘发白
    （实测 Boss01 输出帧半透明边缘像素里 12%~16% 的 RGB>225）。

    按 C观察 = a·C机体 + (1-a)·255 反解：
      1) 取最外 ring 圈像素；用距其最近的「芯」（更深层的机体像素）亮度作 C机体参考；
      2) a = (255 - 观察亮度) / (255 - 芯亮度)，即该像素真实的覆盖率；
      3) 颜色反混合回 C机体，alpha 用 a（顺带给剪影一圈真抗锯齿，不再靠缩小 alpha 硬切）。
    芯色接近纯白（烟雾/发光，>core_lum）时不反解——白烟对白底本就看不出边，
    强行反解会把烟削掉，这类像素原样保留。
    """
    if not HAVE_SCIPY:
        return rgb, alpha
    m = alpha > ALPHA_MIN
    if not m.any():
        return rgb, alpha
    depth = ndimage.distance_transform_edt(m)
    core = depth > ring
    if not core.any():
        return rgb, alpha
    idx = ndimage.distance_transform_edt(~core, return_distances=False, return_indices=True)
    f = rgb.astype(np.float32)
    lum = f.mean(axis=2)
    lum_core = lum[idx[0], idx[1]]
    edge = m & (depth <= ring) & (lum_core < core_lum)
    if not edge.any():
        return rgb, alpha
    cov = np.clip((255.0 - lum) / np.maximum(255.0 - lum_core, 1.0), 0.0, 1.0)
    cov = np.maximum(cov, 0.06)
    out = np.clip((f - (1.0 - cov[..., None]) * 255.0) / cov[..., None], 0, 255)
    f[edge] = out[edge]
    a = alpha.astype(np.float32)
    a[edge] = cov[edge] * 255.0
    return f.astype(np.uint8), np.clip(a, 0, 255).astype(np.uint8)

def bleed_colors(rgb, alpha):
    """颜色外溢（alpha bleeding）：用最近的不透明像素颜色填满透明区，
    避免下游 LANCZOS 缩放时透明区的黑/白 RGB 渗进边缘产生晕圈。"""
    if not HAVE_SCIPY:
        return rgb
    empty = alpha <= ALPHA_MIN
    if not empty.any() or empty.all():
        return rgb
    idx = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
    return rgb[idx[0], idx[1]]

def clean_mask(mask, largest_only):
    """连通域清理：去碎影；largest_only=True 时只保留最大域（去除悬空配件/水印）。"""
    if not HAVE_SCIPY:
        return mask
    lbl, n = ndimage.label(mask)
    if n <= 1:
        return mask
    areas = ndimage.sum(mask, lbl, range(1, n + 1))
    keep = np.zeros(n + 1, bool)
    if largest_only:
        keep[int(np.argmax(areas)) + 1] = True
    else:
        amax = areas.max()
        for i, a in enumerate(areas, start=1):
            if a >= amax * KEEP_RATIO: keep[i] = True
    return keep[lbl]

def load_master(path, unmix=False, kill_holes=False):
    """入口归一化：任意来源 -> (RGBA ndarray, 是否来自阈值抠图)。
    透明底直通；白底/黑底先抠图变透明。返回前统一做颜色外溢，保证缩放安全。
    unmix=True 时额外做白边反混合（见 unmix_white_edge），消除阈值抠图的白色描边。"""
    im = Image.open(path)
    has_alpha = im.mode in ('RGBA', 'LA', 'PA') or 'transparency' in im.info
    if has_alpha:
        rgba = np.asarray(im.convert('RGBA'))
        a = rgba[..., 3]
        if (a <= ALPHA_MIN).mean() > 0.02:      # 确有透明区域 -> 直通
            return np.dstack([bleed_colors(rgba[..., :3], a), a]), False
    rgb = np.asarray(im.convert('RGB'))
    bg = _bg_from_border(rgb)
    a = matte_background(rgb, bg)
    print(f'  [entry] 非透明底（{bg}） -> 抠图成 alpha')
    if kill_holes and bg == 'white':
        a = kill_enclosed_bg_holes(rgb, a)
    if unmix and bg == 'white':
        rgb, a = unmix_white_edge(rgb, a)
        rb = (rgb[a > ALPHA_MIN] >= 225).all(axis=1)
        print(f'  [unmix] 白边反混合完成，剩余近白不透明像素 {rb.mean()*100:.2f}%')
    return np.dstack([bleed_colors(rgb, a), a]), True

def trim_ground_shadow(alpha, rgb):
    """去掉文生图自带的脚底浅灰投影，以及被履带围住而漏抠的白底。

    1) 投影：只处理内容底部 SHADOW_ROWS 行内、近白且低饱和、且落在主体剪影之外的像素。
       剪影用「非近白像素填孔」求得（binary_fill_holes）：履带内部的高光被剪影包住，
       不会被误删；投影在剪影外（履带两侧、两条履带之间）的部分才会被清掉。
    2) 漏底：两条履带之间的背景白被履带+投影围住，入口 matte 只删与外框连通的白色，
       于是整块白被留在 alpha 里（游戏中是裆部一大块白）。这里删掉「底边贴近脚下」的
       近白连通域；肩部排烟口的白色烟柱底边离脚底很远，不会被误删。
    3) 轮拱白板：履带内侧的轮拱是闭合区域，白底同样被围在里面（整条白板贯穿下半身）。
       规则：面积够大、且顶边落在内容高度下半段的近白连通域一律清掉。上半段的白色
       烟雾 / 发光核心保留——核心顶边在内容 0.34~0.44 处，轮拱白板顶边都在 0.52 以下。"""
    if not HAVE_SCIPY:
        return alpha
    m = alpha > ALPHA_MIN
    if not m.any():
        return alpha
    ys = np.nonzero(m.any(axis=1))[0]
    y1 = int(ys.max())
    f = rgb.astype(np.float32)
    mx, mn = f.max(axis=2), f.min(axis=2)
    sat = (mx - mn) / np.maximum(mx, 1)
    light = m & (f.mean(axis=2) >= SHADOW_LUM) & (sat <= SHADOW_SAT)
    body = ndimage.binary_fill_holes(m & ~light)
    kill = light & ~body
    kill[:max(int(ys.min()), y1 - SHADOW_ROWS + 1)] = False

    white = m & (rgb >= WHITE_THR).all(axis=2)
    lbl, n = ndimage.label(white)
    for i, sl in enumerate(ndimage.find_objects(lbl), start=1):
        if sl is not None and sl[0].stop - 1 >= y1 - POCKET_ROWS:
            kill |= (lbl == i)

    # 轮拱白板：下半段的近白大块（顶边 = 内容顶 + POCKET_TOP×内容高）
    pocket = m & (mn >= POCKET_LUM) & (sat <= POCKET_SAT)
    lbl2, _ = ndimage.label(pocket)
    y0 = int(ys.min())
    h = y1 - y0 + 1
    lim = y0 + POCKET_TOP * h
    for i, sl in enumerate(ndimage.find_objects(lbl2), start=1):
        if sl is None or sl[0].start < lim:
            continue
        if int((lbl2[sl] == i).sum()) < POCKET_AREA:
            continue
        comp = (lbl2 == i)
        # 连 1px 白边一起清（白底与机体之间的抗锯齿过渡带），只吃亮的，不吃机体
        grow = ndimage.binary_dilation(comp) & ~comp & (f.mean(axis=2) >= 200) & (sat <= 0.12)
        kill |= comp | grow

    if not kill.any():
        return alpha
    out = alpha.copy()
    out[kill] = 0
    print(f'  [shadow] 清理投影/漏底 {int(kill.sum())} px')
    return out

def kill_bg_holes(img, min_area=HOLE_AREA_FINAL, minch_thr=HOLE_MINCH):
    """成品尺寸上的兜底：清「被机体包住」的残余背景白。

    主力清理在 raw 阶段（kill_enclosed_bg_holes），这一步只对付缩放重采样又插值出来的
    小岛 —— 为什么会有：raw 上一条只有 31px 的白缝，放大 1.7 倍后就是 85px 的白块；
    反过来 raw 上 <240 的抗锯齿过渡带，插值后也会重新冒到 240 以上。

    判据：**封闭 + 三通道最小值中位数 >= minch_thr**。
    封闭性用「先 3x3 闭运算 2 次、再 fill_holes」判 —— 1~2px 宽的抗锯齿细缝会把内部
    白洞和外部背景连起来（Boss01_Enrage 第 1 帧 6882px 那块就是只差 1px），不桥接就会漏。
    闭运算只在判据里用，实际删除的像素仍然是 non-white 之外的那些白像素，不会啃进机体。
    """
    arr = np.asarray(img).copy()
    a = arr[..., 3]
    rgb = arr[..., :3].astype(np.float32)
    m = a > ALPHA_MIN
    if not m.any() or not HAVE_SCIPY:
        return img
    tgt = m & (rgb >= minch_thr).all(axis=2)
    if not tgt.any():
        return img
    body = ndimage.binary_closing(m & ~tgt, structure=np.ones((3, 3), bool), iterations=2)
    enclosed = ndimage.binary_fill_holes(body) & tgt
    if not enclosed.any():
        return img
    lbl, n = ndimage.label(enclosed)
    kill = np.zeros_like(m)
    for i, sl in enumerate(ndimage.find_objects(lbl), start=1):
        if sl is None:
            continue
        comp = (lbl == i)
        if int(comp.sum()) < min_area:
            continue
        v = rgb[comp]
        if float(np.median(v.min(axis=1))) < minch_thr:
            continue
        kill |= comp
    if not kill.any():
        return img
    # 洞边那一圈 232~239 的抗锯齿过渡带一并吃掉，否则洞开了边上还留白圈
    fringe = m & (rgb >= WHITE_THR).all(axis=2)
    kill |= ndimage.binary_dilation(kill) & fringe
    arr[..., 3] = np.where(kill, 0, a)
    print(f'  [hole] 清残余封闭背景白 {int(kill.sum())} px')
    return Image.fromarray(arr, 'RGBA')


def feather_top(img, rows=FEATHER_ROWS):
    """顶部羽化：文生图把烟柱画到格子上沿就截断了（硬平口），缩放补不回丢失的像素，
    这里把内容顶部 rows 行按 alpha 线性渐隐，让切口读作烟自然消散。
    只按行位置做，不动颜色；上半段是烟柱的帧都能用（机体顶边远低于此）。

    注意：这是一次性渐隐，重复跑会继续变淡；已羽化的图顶部 alpha 极低，这里直接跳过。"""
    arr = np.asarray(img).copy()
    a = arr[..., 3].astype(np.float32)
    m = a > ALPHA_MIN
    if not m.any():
        return img
    y0 = int(np.nonzero(m.any(axis=1))[0].min())
    if a[y0].max() < 24:        # 顶部已经很淡：判为已羽化
        return img
    n = min(rows, a.shape[0] - y0)
    if n <= 1:
        return img
    a[y0:y0 + n] *= (np.arange(1, n + 1, dtype=np.float32) / n)[:, None]
    arr[..., 3] = np.clip(a, 0, 255).astype(np.uint8)
    return Image.fromarray(arr, 'RGBA')

def content_rgba(cell_rgb, alpha):
    bbox = Image.fromarray(alpha).getbbox()
    rgba = np.dstack([cell_rgb, alpha])
    return Image.fromarray(rgba, 'RGBA').crop(bbox), bbox

def tile_parts(alpha, largest_only):
    """返回某格清理后的主体数；(0, True) 表示空帧。"""
    m = alpha > ALPHA_MIN
    if not m.any():
        return 0
    if largest_only:      # 只留最大域，必然是 1
        return 1
    if not HAVE_SCIPY:
        return 1
    lbl, nn = ndimage.label(m)
    areas = ndimage.sum(m, lbl, range(1, nn + 1))
    return int((areas >= areas.max() * KEEP_RATIO).sum())

def check_tiles(master, cols, rows, n, name, largest_only, parts):
    """网格校验：每格清理后应恰好含 parts 个主体，否则报错中止。

    分量数偏多几乎总是 raw 的真实网格与声明的 cols×rows 不符：按声明切格会跨行，
    一格叠进两个半截身体（Tower01_Fire 出过的坏图就是这样静默通过的）；
    分量数为 0 则说明该格被切空。发现不符直接 raise，避免再产出坏 sheet。
    parts=None 表示该条目不校验（帧内含分离特效时，角色+特效会形成多个连通域，
    数量逐帧不同，无法用固定值约束）。"""
    if parts is None:
        return
    H, W = master.shape[:2]
    cellw, cellh = W // cols, H // rows
    bad = []
    for i in range(n):
        r, c = divmod(i, cols)
        tile = master[r*cellh:(r+1)*cellh, c*cellw:(c+1)*cellw]
        cnt = tile_parts(tile[..., 3], largest_only)
        if cnt != parts:
            bad.append(f'f{i}={cnt}')
    if bad:
        raise RuntimeError(
            f'{name}: 网格校验未通过 —— 每格期望 {parts} 个主体，实得 [{" ".join(bad)}]；'
            f'raw={W}x{H} 按 {cols}x{rows} 切格得 {cellw}x{cellh}。'
            f'请核对 raw 的真实网格与声明是否一致（每帧须完整落在自己的格内）。')

def prune_stray(img, ratio=0.03):
    """清掉与主体分离的小碎块（AI 出图常在脚边留下一小块浅色/白色碎屑）。

    只删「面积 < 最大连通域 ratio 倍」的独立连通域，主体与正常分离部件（如飘带末端断开的
    1~2px）不受影响；ratio=0.03 时主体必定保留。
    """
    a = np.array(img)[..., 3]
    m = a > ALPHA_MIN
    if not m.any():
        return img
    if HAVE_SCIPY:
        lab, n = ndimage.label(m)
        if n <= 1:
            return img
        sizes = ndimage.sum(m, lab, range(1, n + 1))
        keep = np.zeros(n + 1, dtype=bool)
        keep[1:] = sizes >= sizes.max() * ratio
        out = np.array(img)
        out[..., 3] = np.where(keep[lab], out[..., 3], 0).astype(np.uint8)
        im2 = Image.fromarray(out, 'RGBA')
        bb = im2.getbbox()
        return im2.crop(bb) if bb else im2
    # 无 scipy 时退化为 4 邻域 BFS
    h, w = m.shape
    seen = np.zeros_like(m, dtype=bool)
    comps = []
    for y in range(h):
        for x in range(w):
            if not m[y, x] or seen[y, x]:
                continue
            q = deque([(y, x)]); seen[y, x] = True; pts = []
            while q:
                cy, cx = q.popleft(); pts.append((cy, cx))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = cy + dy, cx + dx
                    if 0 <= ny < h and 0 <= nx < w and m[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True; q.append((ny, nx))
            comps.append(pts)
    comps.sort(key=len, reverse=True)
    if len(comps) <= 1:
        return img
    out = np.array(img)
    for pts in comps[1:]:
        if len(pts) < len(comps[0]) * ratio:
            ys = [p[0] for p in pts]; xs = [p[1] for p in pts]
            out[ys, xs, 3] = 0
    im2 = Image.fromarray(out, 'RGBA')
    bb = im2.getbbox()
    return im2.crop(bb) if bb else im2


def subject_mask(mask):
    """取「主体」连通域：包围盒最高者（同高取面积大者）。
    分离的横向特效（光束/爆炸/水花）面积可能很大但高度低，若按面积取最大域会被特效
    抢占，导致身高归一与底座锚点被特效带偏；按高度取可稳定命中角色本体。"""
    if not HAVE_SCIPY:
        return mask
    lbl, n = ndimage.label(mask)
    if n <= 1:
        return mask
    areas = ndimage.sum(mask, lbl, range(1, n + 1))
    sls = ndimage.find_objects(lbl)
    best = max(range(n), key=lambda i: (sls[i][0].stop - sls[i][0].start, areas[i]))
    return lbl == best + 1

def subject_height(img):
    a = np.asarray(img)[..., 3]
    m = a > ALPHA_MIN
    if not m.any():
        return img.height
    ys = np.where(subject_mask(m).any(axis=1))[0]
    return int(ys.max() - ys.min() + 1) if len(ys) else img.height

def base_anchor_x(alpha, bbox):
    """主体「底座带」的水平中心，在内容 bbox 内的相对 x。
    固定式单位（炮塔）的炮管朝向逐帧变化时，按整帧内容 bbox 居中会把塔身带偏
    （Tower01_Fire 实测底座中心极差 91px）；改用底座锚点后塔身不再左右滑动。
    只统计主体连通域：帧内分离的地面特效（水花）若落在底座带内会把锚点带偏。"""
    m = subject_mask(alpha > ALPHA_MIN)
    ys = np.where(m.any(axis=1))[0]
    y1 = ys.max()
    top = max(ys.min(), y1 - BOT_GAP - BASE_BAND + 1)
    bot = max(ys.min() + 1, y1 - BOT_GAP + 1)
    xs = np.where(m[top:bot, :].any(axis=0))[0]
    if len(xs) == 0:            # 兜底：内容过扁时退回整帧中心
        xs = np.where((alpha > ALPHA_MIN).any(axis=0))[0]
    return (xs.min() + xs.max() + 1) / 2 - bbox[0]

def base_anchor_x_final(img):
    """在「最终像素尺寸」的内容图上量底座带中心 —— 与 check_frames 完全同口径。
    返回自内容左边缘算起的 x（最终像素，非整数）。"""
    a = np.asarray(img)[..., 3]
    m = subject_mask(a > ALPHA_MIN)
    ys = np.where(m.any(axis=1))[0]
    if len(ys) == 0:
        return img.width / 2
    y1 = ys.max()
    top = max(ys.min(), y1 - BOT_GAP - BASE_BAND + 1)
    bot = max(ys.min() + 1, y1 - BOT_GAP + 1)
    xs = np.where(m[top:bot, :].any(axis=0))[0]
    if len(xs) == 0:
        xs = np.where((a > ALPHA_MIN).any(axis=0))[0]
    return (xs.min() + xs.max() + 1) / 2

def process(spec):
    name = spec['name']; cols=spec['cols']; rows=spec['rows']; n=spec['n']
    cw, ch = spec['cell']; target = spec['target']
    # 防呆：部分动作的帧数后来扩过（Run/Jump/Fall/Land/Dash 由 3~8 帧扩到 12 帧），
    # 但 SPECS 与 raw 网格还是旧帧数。此时跑本管线会用旧网格覆盖成品，把帧数截回去
    # （Frame 目录里多出来的帧文件留着不动，但 clip 引用的还是旧数量，表现是动作变短）。
    # 不阻断执行，只报警 —— 真要重做这些动作，得先重摆 raw 网格并把 cols/rows/n 一起改。
    _existing = glob.glob(os.path.join(ROOT, subdir_for(name), 'Frames', name, '*.png'))
    if len(_existing) > n:
        print(f'  [WARN] {name}: 目录已有 {len(_existing)} 帧，但 SPECS 只处理 {n} 帧；'
              f'本次会覆盖并截断，若该动作已扩帧请先更新 raw 网格与 cols/rows/n')
    raw_p = find_raw(name)
    if not raw_p.endswith('.png'):
        png = raw_p.rsplit('.', 1)[0] + '.png'
        Image.open(raw_p).save(png); os.remove(raw_p); raw_p = png
    master, matted = load_master(raw_p, unmix=bool(spec.get('white_unmix')),
                                kill_holes=bool(spec.get('white_holes')))
    H, W = master.shape[:2]
    cellw, cellh = W//cols, H//rows
    check_tiles(master, cols, rows, n, name, spec.get('largest', False), spec.get('parts', 1))
    frames, anchors = [], []
    for i in range(n):
        r, c = divmod(i, cols)
        tile = master[r*cellh:(r+1)*cellh, c*cellw:(c+1)*cellw]
        a = tile[..., 3]
        gate = clean_mask(a > ALPHA_MIN, spec.get('largest', False))
        alpha = np.where(gate, a, 0).astype(np.uint8)
        if spec.get('shadow_trim'):
            alpha = trim_ground_shadow(alpha, tile[..., :3])
        f, bbox = content_rgba(tile[..., :3], alpha)
        if f is not None and spec.get('stray_prune'):
            f = prune_stray(f)
        frames.append(f)
        anchors.append(base_anchor_x(alpha, bbox) if f is not None else None)
    # 坏帧替换: replace={目标帧: 来源帧}, 如末帧与水印重叠被切时用首帧替补
    for dst, src in (spec.get('replace') or {}).items():
        frames[dst] = frames[src]; anchors[dst] = anchors[src]
    ok = [(f, ax) for f, ax in zip(frames, anchors) if f is not None]
    if len(ok) < max(1, n-2):
        raise RuntimeError(f'{name}: 有效帧过少 {len(ok)}/{n}')
    # ref: 基准身高取条内最高帧（max）。若按中位数，最高帧缩放后会超出画格被裁，
    # 死亡这类「站立帧少、躺平帧多」的双峰分布尤其明显（max亦即站立帧高度）
    ref_mode = spec.get('ref', 'max')
    heights = sorted(f.height for f, _ in ok)
    if ref_mode == 'first':
        # 「站立 -> 倒地」类动作（Boss01_Die）不能用条内最高帧做基准：倒地帧的烟柱比站立帧
        # 还高，会把站立帧反缩成矮子，与待机身高对不上。改用首帧（站立帧）的主体身高做基准，
        # 整条统一比例缩放 —— 站立帧与 Idle 同高 760，倒地过程自然变矮，
        # 不会出现「切到死亡时人物突然变大/变小」。
        first = next((f for f in frames if f is not None), None)
        ref_h = subject_height(first) if first is not None else max(heights)
    elif ref_mode == 'max':
        ref_h = max(heights)
    else:
        ref_h = heights[len(heights)//2]
    # ref_h 固定基准：源帧已在外部按「刚性标尺」（角色帽子）归一到与 Idle 同尺度，
    # 此时不能再用条内最高帧当基准 —— 生成漂移会让最高帧偏大（HeavyAttack 实测 790 vs 站姿 759），
    # 整条会被缩得比 Idle 矮 4%，切状态时体型跳变。给死 760 让 scale≈1，保留归一结果。
    if spec.get('ref_h'):
        ref_h = float(spec['ref_h'])
    # norm='frame'：逐帧身高归一。源素材各帧人物大小不一致时，统一比例缩放会把差异原样带进游戏
    # （Attack01 实测最矮帧比最高帧低 19%，Attack03 两行素材差近 2 倍）。
    # 身高基准取每帧「主体连通域」（角色本体）高度，而不是整帧 bbox —— 帧内分离的
    # 光束/爆炸/水花会把 bbox 撑高，若用 bbox 会把角色反缩成矮子。
    if spec.get('norm') == 'frame':
        subj = [subject_height(f) if f is not None else 0 for f, _ in ok]
        ref_h = max(subj) if ref_mode == 'max' else sorted(subj)[len(subj)//2]
        for i, (f, ax) in enumerate(zip(frames, anchors)):
            if f is None:
                continue
            s = ref_h / subj[i]
            if abs(s - 1) < 1e-6:
                continue
            frames[i] = f.resize((max(1, round(f.width * s)), max(1, round(f.height * s))), Image.LANCZOS)
            if ax is not None:
                anchors[i] = ax * s
    scale = target / ref_h
    anchor = spec.get('anchor')
    # ---- 最终尺寸预缩放（anchor_final）----
    # 旧做法在 raw 尺寸上量「底座带」再乘 scale 换算，等于把 raw 的 74px 带映射成最终的
    # 约 116px 带 —— 与 check_frames 量到的 74px 根本不是同一块身体，锚点逐帧错位
    # （Boss01_AttackB 实测底座中心极差 153px，且与画格宽无关，加宽画格完全无效）。
    # 这里先把每帧缩到最终像素尺寸，再在最终尺寸上量锚点，与校验口径逐像素一致。
    final_mode = bool(spec.get('anchor_final')) and anchor == 'base'
    prep = []
    for f in frames:
        if f is None:
            prep.append(None); continue
        if not final_mode:
            prep.append(f); continue
        nw, nh = max(1, round(f.width * scale)), max(1, round(f.height * scale))
        fr = f.resize((nw, nh), Image.LANCZOS)
        if matted:      # 阈值抠图来源: 额外 1px 收缩+轻羽化去白晕（原生 alpha 来源不做）
            a = fr.split()[-1].filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.6))
            fr.putalpha(a)
        if spec.get('white_holes'):
            fr = kill_bg_holes(fr)      # 成品尺寸上清背景白洞（锚点必须在清理后的剪影上量）
        if spec.get('stray_prune'):
            # 成品尺寸上再清一次：MinFilter 收缩会把 raw 阶段相连的细丝切断，
            # 缩放后变成独立的小碎点，这一步按 0.5% 阈值把它们彻底清掉
            fr = prune_stray(fr, 0.005)
        prep.append(fr)
    if final_mode:
        anchors = [base_anchor_x_final(fr) if fr is not None else None for fr in prep]
    # sized: [(最终用图, 底座锚点(自内容左边缘量、最终像素), 最终宽)]
    sized = []
    for i, f in enumerate(prep):
        if f is None:
            sized.append(None); continue
        if final_mode:
            sized.append((f, anchors[i], f.width))
        else:
            sized.append((f, (anchors[i] or 0.0) * scale, max(1, round(f.width * scale))))
    if anchor == 'base':
        # 画格宽取「底座到内容左右两端」的最大值×2：伸得最远的那帧也不被裁
        half = 0.0
        for it in sized:
            if it is None:
                continue
            _, ax, w = it
            half = max(half, ax, w - ax)
        cw_out = max(cw, int(np.ceil(half * 2)))
    else:
        # 画格宽度以条内最宽帧为基准：固定 692 会把挥砍等横向大动作左右裁掉
        cw_out = max(cw, max(it[2] for it in sized if it is not None))
    # fixed_canvas：强制画格宽=spec.cell 宽，保证与其他动作套的画布完全一致。
    # 前提是 cell 宽 ≥ 上面算出的自适应宽，否则人物本体也会被裁——这里显式报警，
    # 免得「画格统一」被误当成「可以随便压窄」。
    if spec.get('fixed_canvas'):
        if cw_out > cw:
            print(f'  [WARN] {name}: 自适应需要画格宽 {cw_out} > 固定 {cw}，本体将被裁；请加大 cell 宽')
        cw_out = cw
    sub = os.path.join(ROOT, subdir_for(name))
    fdir = os.path.join(sub, 'Frames', name); os.makedirs(fdir, exist_ok=True)
    sheet = Image.new('RGBA', (cw_out*cols, ch*rows), (0,0,0,0))
    for i, it in enumerate(sized):
        out = Image.new('RGBA', (cw_out, ch), (0,0,0,0))
        if it is not None:
            f, ax, nw = it
            if not final_mode:
                nh = max(1, round(f.height * scale))
                f = f.resize((nw, nh), Image.LANCZOS)
                if matted:
                    a = f.split()[-1].filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.6))
                    f.putalpha(a)
                if spec.get('white_holes'):
                    f = kill_bg_holes(f)
            # 溢出防裁：宽/高超格时 clamp 到 0，让溢出端从右/下裁掉，保住 bbox 左上（人物主体）
            # 底座锚点模式按底座中心定位（塔身不随炮管朝向滑动），否则按内容 bbox 居中
            x = max(0, round(cw_out/2 - ax)) if anchor == 'base' else max(0, (cw_out - nw)//2)
            y = max(0, ch - BOT_GAP - f.height)
            out.paste(f, (x, y))        # 目标全透明, 直贴保留 straight alpha, 避免 mask 混合压暗边缘
        if spec.get('feather_top'):
            out = feather_top(out)
        out.save(os.path.join(fdir, f'{name}_{i:02d}.png'))
        sheet.paste(out, ((i%cols)*cw_out, (i//cols)*ch))
    sp = os.path.join(sub, 'Sheets', f'{name}_sheet.png')
    sheet.save(sp)
    src_kind = '阈值抠图' if matted else '原生 alpha'
    wide = f'  画格宽={cw_out}' if cw_out != cw else ''
    anc = '  锚点=底座' if anchor == 'base' else ''
    print(f'[OK] {name}: {n}帧  来源={src_kind}  raw格={cellw}x{cellh}{wide}{anc}  '
          f'基准身高[{ref_mode}]={ref_h}  scale={scale:.3f}  -> {os.path.relpath(sp, ROOT)}')

if __name__ == '__main__':
    only = sys.argv[1:] if len(sys.argv) > 1 else None
    for s in SPECS:
        if only and s['name'] not in only: continue
        try:
            process(s)
        except Exception as e:
            print(f'[FAIL] {s["name"]}: {e}')
