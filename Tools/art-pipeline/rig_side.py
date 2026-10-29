"""侧视图 cutout 骨骼 v2

v1 的产物「腿像被硬掰开」，根因有两条，这里逐条修掉：

  1. 两条腿共用同一个髋点 (800,1220) —— 那个点其实落在**裆部正中间**。
     实测近腿真实髋 x≈843、远腿 x≈650，差近 200px。绕裆部中点转，
     两条腿就是「从胯中间张开」，这正是掰腿感的来源。
     v2：两腿各有独立的髋 / 膝 / 踝。

  2. 膝角用 max(0, sin(...))，半个周期恒为 0 → 膝盖完全伸直，
     髋到脚是一条直筒。v2：用 12 个关键帧定义跑步相位，
     膝角全程屈曲（最大 ~100°），支撑期屈膝、蹬离期伸展。

  3. 每腿拆三节（大腿 / 小腿 / 靴），靴子可独立控制脚掌角度。

  4. 远腿使用原图真实像素（实测两条腿都完整、靴尖都朝右），v1 是近腿副本。

  5. 关节补洞为「同心于内层转轴的实心圆盘」，保留圆内原有的腿像素细节，
     只把超轮廓的部分用该部位主色补满 —— 任意旋转角度都不露缝。

用法：
  python rig_side.py cut    --src cut/v3_side.png --outdir rig2
  python rig_side.py render --parts rig2 --outdir frames --n 12
  python rig_side.py render --parts rig2 --outdir rest --rest      # 零旋转自检
"""
import argparse
import json
import math
import os

import numpy as np
from PIL import Image
from scipy import ndimage

# ---------------- 原图（1440x1920）关节坐标 ----------------
# 逐行测中轴线得出：近腿 846→912 斜向右下，远腿 641→530 斜向左下
LEGS = {
    "n": dict(hip=(843, 1298), knee=(886, 1492), ankle=(912, 1614), sole=1808),
    "f": dict(hip=(650, 1298), knee=(573, 1492), ankle=(528, 1614), sole=1826),
}

# ---- 远腿整体右移（关键）----
# 原图是 3/4 视角：远腿整条画在近腿左侧 193px（髋 650 vs 843）。
# 这是**位置**偏移，旋转补不回来 —— 同一个摆角下，远脚恒比近脚靠左半个步幅，
# 结果是步幅一大一小、交叉帧两只靴子叠在一起。
# 试过把远腿像素整体右移来对齐骨盆：会把一大块裤料顶到骨盆左侧外露成方形硬边，
# 而且 3/4 视角下远腿本来就该比近腿显得步子小（透视），强行拉平反而假。
# 结论：位移保持 0，只靠下面的「静止朝向归零」修相位差。
FAR_LEG_SHIFT = 0

# ---- 髋间距压缩（辅助）----
# 移完还剩一点错位，按比例再收一点，保留立体感但不让大腿从骨盆两侧支出去。
HIP_COMPRESS = 1.0
SHOULDER = (893, 812)
ELBOW = (995, 1040)

# 远侧手臂（原图左侧那条，压在水壶前面）：肩点、切分上界
# 实测皮肤连通域 x[332,613] y[863,1255]，是完整手臂（袖口以上为白色短袖）
FAR_SHOULDER = (608, 884)
FAR_ARM_TOP_Y = 828

ARM_TOP_Y = 752
CUT_KNEE = 1492        # 大腿 / 小腿分界
CUT_ANKLE = 1614       # 小腿 / 靴分界（靴筒上沿）
LEG_TOP_Y = 1290       # 腿像素起点（其上归躯干）

# 躯干下沿 V 形：顶点在两髋中点，两侧上抬
BODY_AXIS = (746, 1298)
V_MID = 1282           # 两侧水平段高度
V_DEPTH = 16           # 中点再向下凸出的量
V_HALF = 165           # 抛物线半宽

# 工作关节 = 原图关节 + 该腿的像素位移
LEG_SHIFT = {"n": 0, "f": FAR_LEG_SHIFT}
JOINTS = {
    tag: {k: (v[0] + LEG_SHIFT[tag], v[1]) if isinstance(v, tuple) else v
          for k, v in L.items()}
    for tag, L in LEGS.items()
}

# 实际用于转动的髋点（像素原地不动，只把转轴往骨盆中心收）
PIVOT_HIP = {
    tag: (BODY_AXIS[0] + (J["hip"][0] - BODY_AXIS[0]) * HIP_COMPRESS, J["hip"][1])
    for tag, J in JOINTS.items()
}

# 关节补洞圆盘半径（原图像素）
R_TOP = 80             # thigh 上端圆弧半径 = body 挖孔半径（同心于髋，旋转不变形）
# 髋盘只用来填「身体下沿 V 形缺口」，半径不能大 —— 大了会在骨盆正中糊出一个亮色圆球
R_HIP, R_KNEE, R_ANKLE = 95, 82, 54
HIP_TONE = (1.07, 0.90)  # 髋盘径向渐变（与原图裤子明暗节奏一致）
R_ARM_U, R_ARM_F = 42, 46

# 输出画布
CANVAS = (1200, 833)
SCALE = 0.42
SRC_ANCHOR = BODY_AXIS
OUT_ANCHOR = (600, 545)
# 站立姿态最低的脚（远腿靴底 1826）落在画布上的行号 —— 所有帧把最低脚压到这条线
GROUND_Y = int(round(OUT_ANCHOR[1] + (LEGS["f"]["sole"] - SRC_ANCHOR[1]) * SCALE))


def masks(rgb):
    R, G, B = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    return {
        "skin":  (R > 230) & (G > 175) & (B > 150) & (R >= G) & (G >= B - 8),
        "white": (mx > 226) & (mx - mn < 30),
        "green": (G > 115) & (G > R + 25) & (G > B + 45),
        "teal":  (B > 80) & (B > R + 42) & (G > R + 42) & (R < 115),
        "brown": (R > 100) & (R < 220) & (G > 60) & (G < 152) & (B < 118)
                 & (R > G + 24) & (G > B + 4),
        "blue":  (B > 150) & (B > R + 40) & (B > G + 12),
    }


def biggest(m, k=1):
    lab, n = ndimage.label(m, structure=np.ones((3, 3)))
    if n == 0:
        return m, 0
    sz = ndimage.sum(m, lab, range(1, n + 1))
    out = np.zeros_like(m)
    for j in np.argsort(sz)[::-1][:k]:
        out |= lab == j + 1
    return out, n


def dominant_color(rgb, mask):
    c = rgb[mask]
    if len(c) == 0:
        return np.array([100, 100, 100], np.float32)
    return np.median(c, axis=0).astype(np.float32)


def add_disc(part, cx, cy, R, color, tone=(1.07, 0.90)):
    """在 part 上补一个以 (cx,cy) 为圆心的**完整**圆盘。

    圆内原本不透明的像素保留（保住裤褶、口袋、靴子缝线等细节），
    只把超出原轮廓的部分用该部位主色的径向渐变补满。
    因为圆心就是内层转轴，旋转时这个圆形状不变 —— 任意角度都不露缝。
    """
    H, W = part.shape[:2]
    yy, xx = np.ogrid[:H, :W]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    disk = d <= R
    fill = disk & (part[..., 3] < 200)
    if not fill.any():
        return 0
    t = np.clip(d[fill] / R, 0.0, 1.0)
    k = tone[0] + (tone[1] - tone[0]) * t
    part[fill, :3] = np.clip(
        np.asarray(color, np.float32)[None, :] * k[:, None], 0, 255).astype(np.uint8)
    part[fill, 3] = 255
    return int(fill.sum())


def to_part(rgb, mask, alpha_val=255):
    out = np.zeros((*mask.shape, 4), np.uint8)
    out[..., :3] = rgb.astype(np.uint8)
    out[..., 3] = np.where(mask, alpha_val, 0).astype(np.uint8)
    return out


def save_part(part, path, canvas=CANVAS, scale=SCALE,
              src_anchor=SRC_ANCHOR, out_anchor=OUT_ANCHOR):
    im = Image.fromarray(part, "RGBA")
    inv = 1.0 / scale
    coeffs = (inv, 0, src_anchor[0] - out_anchor[0] * inv,
              0, inv, src_anchor[1] - out_anchor[1] * inv)
    im = im.transform(canvas, Image.AFFINE, coeffs, resample=Image.BICUBIC)
    im.save(path)
    return im


# ======================= 切分 =======================
def cmd_cut(a):
    os.makedirs(a.outdir, exist_ok=True)
    src = Image.open(a.src).convert("RGBA")
    arr = np.array(src)
    rgb = arr[..., :3].astype(np.int16)
    fg = arr[..., 3] > 100
    H, W = fg.shape
    Y, X = np.mgrid[0:H, 0:W]
    m = masks(rgb)

    # ---------- 近侧手臂 ----------
    core = (fg & m["skin"] & (X > 866) & (Y > 880)) | \
           (fg & m["white"] & (X > 870) & (X < 960) & (Y > ARM_TOP_Y) & (Y < 950))
    arm = ndimage.binary_dilation(core, iterations=9) & fg & (X > 860) & \
        ~(m["green"] | m["teal"] | m["blue"])
    arm, _ = biggest(arm, 1)
    arm_u = arm & (Y < ELBOW[1])
    arm_f = arm & (Y >= ELBOW[1])

    # ---------- 远侧手臂 ----------
    # 原图左侧那条完整手臂（含白色短袖），压在水壶前面。
    # 不切出来它就会跟着躯干僵住 —— 渲染时一只手臂摆动、另一只钉死，读作「浮空的手」。
    # 上界取到袖口以上一点点（切边落在绿背心上），小幅摆动最多露出背心色，不会露洞。
    core_far = (fg & m["skin"] & (X < 670) & (Y > FAR_ARM_TOP_Y) & (Y < 1280)) | \
               (fg & m["white"] & (X < 670) & (Y > FAR_ARM_TOP_Y) & (Y < 960))
    farm = ndimage.binary_dilation(core_far, iterations=7) & fg & (X < 680) & \
        (Y > FAR_ARM_TOP_Y - 3) & (Y < 1285) & ~(m["green"] | m["teal"] | m["blue"])
    farm, _ = biggest(farm, 1)

    # ---------- 髋铰链 ----------
    # 躯干挖一个半径 R_TOP 的圆孔，大腿上端补成**同圆心同半径**的圆弧。
    # 圆弧绕自身圆心旋转形状不变，所以腿摆到任何角度都严丝合缝，不会露出躯干直边。
    # （试过反过来把 socket 留在躯干里：腿一摆就把髋孔附近的像素带走，socket 里没人画 → 露白月牙，已弃用）
    hip_zone = {}
    for tag, L in LEGS.items():
        hx, hy = PIVOT_HIP[tag]
        hip_zone[tag] = (X - hx) ** 2 + (Y - hy) ** 2 <= R_TOP ** 2
    hip_any = hip_zone["n"] | hip_zone["f"]

    # ---------- 两腿连通域（各自独立）----------
    legex = (m["teal"] | m["brown"]) & (Y >= LEG_TOP_Y) & (X > 380)
    legex = ndimage.binary_closing(legex, np.ones((5, 5)))
    lab, n = ndimage.label(legex, structure=np.ones((3, 3)))
    sz = ndimage.sum(legex, lab, range(1, n + 1))
    cx = [np.where(lab == j + 1)[1].mean() if (lab == j + 1).any() else 0
          for j in range(n)]
    order = [j for j in np.argsort(sz)[::-1] if sz[j] > 3000][:2]
    order.sort(key=lambda j: cx[j])
    far_leg = lab == order[0] + 1
    near_leg = lab == order[1] + 1
    near_leg = ndimage.binary_dilation(near_leg, iterations=6) & fg & (Y >= LEG_TOP_Y - 6)
    far_leg = ndimage.binary_dilation(far_leg, iterations=6) & fg & (Y >= LEG_TOP_Y - 6)
    near_leg, _ = biggest(near_leg, 1)
    far_leg, _ = biggest(far_leg, 1)

    # 远腿整体右移到骨盆下方 —— 掩膜和像素一起滚，部件仍是原图像素
    far_leg_orig = far_leg
    far_leg = np.roll(far_leg_orig, FAR_LEG_SHIFT, axis=1)
    rgb_far = np.roll(rgb, FAR_LEG_SHIFT, axis=1)
    fg_shift = np.roll(fg, FAR_LEG_SHIFT, axis=1)

    # ---------- 躯干 ----------
    # 原位移的远腿区域要从躯干里剔掉（腿已经挪走了，那里应该露出背景），
    # 但躯干也不能白拿移位后的那份像素（那是远腿的）。
    body = fg & ~arm & ~farm & ~near_leg & ~far_leg_orig & ~far_leg

    # ---------- 皮带（单独一层，盖住髋帽上缘）----------
    belt = fg & (Y > 1088) & (Y < 1182) & (X > 556) & (X < 944) & (rgb.max(axis=2) < 130)
    belt = ndimage.binary_closing(belt, np.ones((7, 7)))
    belt, _ = biggest(belt, 1)
    body = body & ~belt

    # 躯干下沿 V 形（顶点在两髋中点）
    ybot = V_MID + V_DEPTH * np.clip(1.0 - ((X - BODY_AXIS[0]) / V_HALF) ** 2, 0.0, 1.0)
    body = body & (Y < ybot)
    # 挖髋孔（由大腿的同心圆弧来填）
    body = body & ~hip_any

    pants_col = dominant_color(rgb, m["teal"] & fg)
    boot_col = dominant_color(rgb, m["brown"] & fg & (Y > 1560))

    parts = {}
    report = []

    def emit(name, part, disc=None, tone=(1.07, 0.90)):
        if disc:
            filled = add_disc(part, disc[0], disc[1], disc[2], disc[3], tone=tone)
        else:
            filled = 0
        parts[name] = part[..., 3] > 0
        save_part(part, os.path.join(a.outdir, name + ".png"))
        report.append(f"  {name:9s} {int((part[...,3]>0).sum()):7d}px  圆盘补 {filled:6d}px")

    # 远腿（先画，压暗由渲染负责）
    for tag, legmask, L, src_rgb in [("f", far_leg, JOINTS["f"], rgb_far),
                                     ("n", near_leg, JOINTS["n"], rgb)]:
        hy = L["hip"][1]
        thigh = legmask & (Y < CUT_KNEE) & ((Y >= hy) | hip_zone[tag])
        shin = legmask & (Y >= CUT_KNEE) & (Y < CUT_ANKLE)
        boot = legmask & (Y >= CUT_ANKLE)
        emit(f"thigh_{tag}", to_part(src_rgb, thigh),
             disc=(PIVOT_HIP[tag][0], PIVOT_HIP[tag][1], R_HIP, pants_col),
             tone=HIP_TONE)
        # 小腿带膝帽（裤色）和踝帽（靴色）——圆心分别是膝、踝
        shin_part = to_part(src_rgb, shin)
        add_disc(shin_part, L["knee"][0], L["knee"][1], R_KNEE, pants_col)
        emit(f"shin_{tag}", shin_part,
             disc=(L["ankle"][0], L["ankle"][1], R_ANKLE, boot_col))
        emit(f"boot_{tag}", to_part(src_rgb, boot))

    # 近侧手臂
    emit("arm_u", to_part(rgb, arm_u),
         disc=(SHOULDER[0], SHOULDER[1], R_ARM_U, dominant_color(rgb, m["white"] & fg)))
    emit("arm_f", to_part(rgb, arm_f),
         disc=(ELBOW[0], ELBOW[1], R_ARM_F, dominant_color(rgb, m["skin"] & fg)))

    # 远侧手臂（整条一件，绕肩小幅摆动；上端切边压在背心上）
    emit("farm", to_part(rgb, farm))

    # 躯干 + 皮带
    emit("body", to_part(rgb, body))
    emit("belt", to_part(rgb, belt))

    legs_meta = {tag: {**{k: v for k, v in J.items()}, "hip": list(PIVOT_HIP[tag])}
                 for tag, J in JOINTS.items()}
    meta = {"legs": legs_meta, "shoulder": SHOULDER, "elbow": ELBOW,
            "far_shoulder": FAR_SHOULDER,
            "hip_compress": HIP_COMPRESS,
            "far_leg_shift": FAR_LEG_SHIFT,
            "scale": SCALE, "canvas": CANVAS,
            "src_anchor": SRC_ANCHOR, "out_anchor": OUT_ANCHOR,
            "body_axis": BODY_AXIS,
            "colors": {"pants": pants_col.tolist(), "boot": boot_col.tolist()}}
    with open(os.path.join(a.outdir, "rig.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    # 切分着色图
    colors = {"thigh_n": (150, 90, 255), "shin_n": (255, 140, 0),
              "boot_n": (255, 235, 0), "thigh_f": (110, 60, 200),
              "shin_f": (200, 100, 0), "boot_f": (200, 190, 0),
              "arm_u": (255, 220, 0), "arm_f": (0, 255, 120),
              "farm": (255, 0, 200),
              "body": (255, 60, 60), "belt": (0, 220, 255)}
    ov = np.array(src.convert("RGB")).astype(float)
    out = ov.copy()
    for name, mask in parts.items():
        out[mask] = ov[mask] * 0.4 + np.array(colors[name]) * 0.6
    Image.fromarray(out.astype(np.uint8)).save(os.path.join(a.outdir, "_parts_color.png"))

    print("[cut] 部件")
    print("\n".join(report))
    print(f"  裤主色 {pants_col.astype(int)}  靴主色 {boot_col.astype(int)}")
    print(f"[ok] -> {a.outdir}")


# ======================= 渲染 =======================
# 12 个跑步相位关键帧（与 12 帧一一对应，无需插值）
# 列：thigh 前摆(+前) / knee 屈曲(+向后折) / **脚掌相对水平的目标角度**(+上勾、-下压)
# 第三列是世界角度，不是相对小腿 —— 脚掌必须平放在地上，不能跟着腿一起斜。
KEY = [
    (24.0,  16.0,    0.0),   # 0 触地    脚掌平放
    (18.0,  32.0,    0.0),   # 1 承重    膝屈缓冲、身体最低
    (4.0,   26.0,    0.0),   # 2 支撑中  脚掌贴地
    (-14.0, 14.0,   -8.0),   # 3 支撑末  脚跟抬起
    (-32.0,  8.0,  -35.0),   # 4 蹬离    腿蹬直、脚尖下压
    (-30.0, 45.0,  -20.0),   # 5 离地    小腿开始回收（腾空）
    (-8.0,  95.0,   -5.0),   # 6 屈膝最大 脚跟提到臀部下方
    (12.0, 105.0,   10.0),   # 7 摆动中  膝最屈、脚尖上勾
    (30.0,  85.0,   12.0),   # 8 前摆    大腿高抬
    (42.0,  48.0,    8.0),   # 9 前摆中  小腿向前甩
    (34.0,  24.0,    2.0),   # 10 前伸   膝屈过大脚会被拉回、和另一条腿叠在一起
    (34.0,  14.0,    0.0),   # 11 准备触地 脚掌拉平
]
# 两腿相位差半个周期时，这两帧双脚都在空中 —— 身体要按抛物线上浮，不能硬拽回地面
# 远腿单独一张表：它的髋在画面左侧（比近髋靠后 80px，画布），同角度只能摆出一半幅度。
# 后蹬放大 → 跨步时后脚更靠后；前伸时多屈膝 → 那个半周期读作「腾空收腿」而不是「站直」。
FAR_GAIN = 1.00         # （旧）仅后蹬方向放大，已由 FAR_SWING 取代
FAR_SWING = 1.35        # 远腿整体摆幅放大
FAR_KNEE_ADD = 0.0      # 静止朝向归零后不再需要额外屈膝
AIR_FRAMES = {5, 11}
AIR_LIFT = 9


def _load(parts, name):
    return Image.open(os.path.join(parts, name + ".png")).convert("RGBA")


def _rot(img, pivot, deg):
    """deg>0 = 视觉逆时针。"""
    if abs(deg) < 0.01:
        return img
    return img.rotate(deg, center=pivot, resample=Image.BICUBIC)


def _shade(img, k):
    if k >= 0.999:
        return img
    arr = np.array(img)
    arr[..., :3] = np.clip(arr[..., :3].astype(np.float32) * k, 0, 255).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def cmd_render(a):
    meta = json.load(open(os.path.join(a.parts, "rig.json"), encoding="utf-8"))
    cv = tuple(meta["canvas"])
    s = meta["scale"]
    ax, ay = meta["src_anchor"]
    ox, oy = meta["out_anchor"]

    def C(pt):
        return (ox + (pt[0] - ax) * s, oy + (pt[1] - ay) * s)

    P = {n: _load(a.parts, n) for n in
         ["body", "belt", "arm_u", "arm_f", "farm",
          "thigh_n", "shin_n", "boot_n", "thigh_f", "shin_f", "boot_f"]}
    JN = {k: C(v) for k, v in meta["legs"]["n"].items() if k != "sole"}
    JF = {k: C(v) for k, v in meta["legs"]["f"].items() if k != "sole"}
    sh, el = C(meta["shoulder"]), C(meta["elbow"])
    fsh = C(meta.get("far_shoulder", FAR_SHOULDER))
    bax = C(meta["body_axis"])

    os.makedirs(a.outdir, exist_ok=True)
    n = a.n
    idxs = [0] if a.rest else (range(n) if not a.only else [int(t) for t in a.only.split(",")])

    def _far_thigh(t):
        return t * FAR_SWING

    # ---- 静止朝向归零（对称性的关键）----
    # 原图是 3/4 视角，两条腿的「静止朝向」本身就相反：
    #   近腿 髋843→踝912  → 前倾 14.0°
    #   远腿 髋650→踝528  → 后倾 23.9°
    # 相差约 38°。同一张角度表套上去，远腿永远比近腿「慢」38°，
    # 步幅就会一大一小（实测 F00=187 / F06=380），读起来就是瘸。
    # 所以每条腿先绕自己的髋反向校正各自的静止倾角，让 th=0 对两条腿都表示「竖直」。
    LEAN = {}
    for _tag, _J in (("n", JN), ("f", JF)):
        _dx = _J["ankle"][0] - _J["hip"][0]
        _dy = _J["ankle"][1] - _J["hip"][1]
        LEAN[_tag] = -math.degrees(math.atan2(_dx, _dy))

    def angles(i):
        """近腿、远腿的 (thigh, knee, 脚掌相对小腿角)。靴子角已换算到链条内。"""
        if a.rest:
            return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)
        tn, kn, an = KEY[(i + 6) % 12]
        tf, kf, af = KEY[i % 12]
        kk = a.key_scale
        tn, kn = tn * kk, kn * kk
        tf, kf = tf * kk, kf * kk
        tf = _far_thigh(tf)
        if tf > 0:
            kf += FAR_KNEE_ADD
        tn += LEAN["n"]                     # 归零到竖直
        tf += LEAN["f"]
        # 靴子绝对倾角 = th - kn + an_rel  →  an_rel = an_target - th + kn
        return (tn, kn, an - tn + kn), (tf, kf, af - tf + kf)

    def foot_low(img):
        aa = np.array(img)[..., 3]
        rr = np.where((aa > 8).any(axis=1))[0]
        return int(rr.max()) if len(rr) else GROUND_Y

    def leg_layers(tag, J, th, kn, an):
        hip, knee, ankle = J["hip"], J["knee"], J["ankle"]
        b = _rot(P[f"boot_{tag}"], ankle, an)
        b = _rot(b, knee, -kn)
        b = _rot(b, hip, th)
        sh = _rot(P[f"shin_{tag}"], knee, -kn)
        sh = _rot(sh, hip, th)
        th_ = _rot(P[f"thigh_{tag}"], hip, th)
        # 返回绘制顺序：大腿在下、小腿盖住膝、靴盖住踝
        return th_, sh, b

    # 第一遍：只算两只脚各自落在哪一行
    lows = {}
    for i in range(n):
        (tn, kn, an), (tf, kf, af) = angles(i)
        _, _, bn = leg_layers("n", JN, tn, kn, an)
        _, _, bf = leg_layers("f", JF, tf, kf, af)
        lows[i] = (foot_low(bn), foot_low(bf))

    # 第二遍：支撑帧用最低脚钉地，腾空帧按抛物线抬升（同时不允许穿地）
    dy_map = {}
    for i in range(n):
        if a.rest:
            dy_map[i] = GROUND_Y - max(lows[i])
        elif (i % 12) not in AIR_FRAMES:
            dy_map[i] = GROUND_Y - max(lows[i])
    if not a.rest:
        for i in range(n):
            if (i % 12) in AIR_FRAMES:
                # 以本帧「最低脚刚好触地」为基准，再整体抬高 AIR_LIFT
                dy_map[i] = GROUND_Y - max(lows[i]) - AIR_LIFT

    for i in idxs:
        (th_n, kn_n, an_n), (th_f, kn_f, an_f) = angles(i)
        tilt = 0.0 if a.rest else -a.tilt          # 负 = 顺时针 = 躯干前倾
        arm_n = 0.0 if a.rest else -a.arm_gain * (th_n - LEAN["n"])
        _sw = th_f - LEAN["f"]
        if _sw > 0:            # 腿前摆 → 远臂该向后；但向后会戳出轮廓，压小
            _sw *= 0.35
        arm_f = 0.0 if a.rest else -a.far_arm * _sw
        elb = 0.0 if a.rest else a.elbow

        def leg_layers(tag, J, th, kn, an):
            hip, knee, ankle = J["hip"], J["knee"], J["ankle"]
            # 靴：先绕踝，再随小腿（绕膝），再随大腿（绕髋）
            b = _rot(P[f"boot_{tag}"], ankle, an)
            b = _rot(b, knee, -kn)
            b = _rot(b, hip, th)
            # 小腿：绕膝后随大腿
            s_ = _rot(P[f"shin_{tag}"], knee, -kn)
            s_ = _rot(s_, hip, th)
            # 大腿：绕髋
            t_ = _rot(P[f"thigh_{tag}"], hip, th)
            return t_, s_, b

        out = Image.new("RGBA", cv, (0, 0, 0, 0))

        # 侧视里「向前伸的那条腿」必须盖住另一条，否则前腿会被后腿吃掉。
        # 所以图层顺序不能固定，要按谁摆得更前决定：后腿 → 躯干 → 前腿。
        LEGP = {"f": (JF, th_f, kn_f, an_f, 1.0),
                "n": (JN, th_n, kn_n, an_n, 1.0)}
        back, front = ("n", "f") if th_f > th_n else ("f", "n")

        J, th, kn, an, _ = LEGP[back]
        t_, s_, b_ = leg_layers(back, J, th, kn, an)
        for layer in (t_, s_, b_):
            out.alpha_composite(_shade(layer, a.shade))    # 后腿压暗

        # --- 躯干 ---
        out.alpha_composite(_rot(P["body"], bax, tilt))

        # --- 远侧手臂（在原图里压在躯干/水壶前面，所以合成在躯干之后）---
        fa = _rot(P["farm"], fsh, arm_f)
        fa = _rot(fa, bax, tilt)
        out.alpha_composite(_shade(fa, a.shade))

        J, th, kn, an, _ = LEGP[front]
        t_, s_, b_ = leg_layers(front, J, th, kn, an)
        for layer in (t_, s_, b_):
            out.alpha_composite(layer)

        # --- 皮带（盖住髋帽上缘）---
        out.alpha_composite(_rot(P["belt"], bax, tilt))

        # --- 近侧手臂 ---
        au = _rot(P["arm_u"], sh, arm_n)
        au = _rot(au, bax, tilt)
        af = _rot(P["arm_f"], el, elb)
        af = _rot(af, sh, arm_n)
        af = _rot(af, bax, tilt)
        out.alpha_composite(au)
        out.alpha_composite(af)

        # ---- 地面约束：只把**当前支撑脚**压回 GROUND_Y ----

        dy = dy_map[i]
        if dy:
            out = out.transform(cv, Image.AFFINE, (1, 0, 0, 0, 1, -dy),
                                resample=Image.BICUBIC)

        name = f"run_{i:02d}.png" if not a.rest else "rest.png"
        out.save(os.path.join(a.outdir, name))
        rr, _cc = np.where(np.array(out)[..., 3] > 8)
        air = "腾空" if (not a.rest and (i % 12) in AIR_FRAMES) else "支撑"
        print(f"  frame {i:02d}  {air}  base={rr.max():3d}/{GROUND_Y}  dy={dy:+3d}  top={rr.min():3d}")
    print(f"[render] {len(list(idxs))} 帧 -> {a.outdir}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("cut")
    c.add_argument("--src", default="cut/v3_side.png")
    c.add_argument("--outdir", default="rig2")
    c.set_defaults(func=cmd_cut)
    r = sub.add_parser("render")
    r.add_argument("--parts", default="rig2")
    r.add_argument("--outdir", default="frames")
    r.add_argument("--n", type=int, default=12)
    r.add_argument("--only", default="")
    r.add_argument("--rest", action="store_true")
    r.add_argument("--tilt", type=float, default=8.0)
    r.add_argument("--elbow", type=float, default=58.0)
    r.add_argument("--shade", type=float, default=0.86)
    r.add_argument("--arm-gain", type=float, default=0.80)
    r.add_argument("--key-scale", type=float, default=1.0,
                   help="腿角整体缩放：1.0=大摆幅跑步，0.55=小摆幅自然跑")
    r.add_argument("--far-arm", type=float, default=0.28,
                   help="远侧手臂摆幅系数（相对远腿摆角，正数=反向摆）")
    r.set_defaults(func=cmd_render)
    a = ap.parse_args()
    a.func(a)
