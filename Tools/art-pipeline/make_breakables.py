"""可破坏物（道具/障碍）素材处理：洋红键控 + Voronoi 碎裂。

## 做什么
输入是**洋红底**（`#FF00FF`）的单主体原图（卡通描边），输出「整块」+「若干碎块」两套透明底
PNG，并写好 Unity 精灵导入用的 `.meta`。技术要点：

  1) 洋红键控 -> 透明底 RGBA master（只动 alpha，不做白底往返，边缘不会出白圈）
  2) despill 压掉边缘品红溢出（不然缩放后一圈粉边）
  3) trim 到 alpha 外接框，按**目标世界高度**缩放到最终像素尺寸（PPU 决定 1u = 多少 px）
  4) 在放大后的 master 上做 Voronoi 分块 —— 碎块尺寸即其在原图中的真实占比，
     所以游戏里让碎块按原位置飞散，视觉上正好拼回原物
  5) 写 `.meta`（PPU / Center pivot / 无压缩）保证导入参数一致

## 为什么用洋红底而不是白底
白底抠图后主体边缘会带一圈 232~255 的近白抗锯齿，铺到深色地面上是一道白边；
洋红与大多数美术用色差异极大，键控 + despill 能干净切开。

## 用法
    # 用文件内的 ITEMS 清单（默认指向本工程的路径，换成自己的即可）
    python make_breakables.py

    # 全部从命令行给（不改脚本）
    python make_breakables.py \
        --src-dir <洋红底原图目录> \
        --out-dir <Assets/.../Props> \
        --ppu 400 \
        --item "prop_name:raw_file.jpg:目标像素高:碎块数:随机种子"

`--item` 可重复；也可以直接改下面 `ITEMS` 列表。碎块数与种子决定分块形状，
同一 `(name, seed)` 结果稳定可复现。
"""
import hashlib
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

# ---- 默认路径（默认值指向本工程的示例布局，换工程用 CLI 覆盖即可）----
ART_GEN = os.path.abspath(os.path.join(ROOT, "..", "art_gen"))      # 洋红底原图目录
PROPS = os.path.join(ROOT, "Assets", "Art", "Art", "Environment", "Props")
DEBRIS = os.path.join(PROPS, "Debris")                              # 碎块输出子目录
PPU = 400                       # 精灵像素/单位；与工程里其它道具保持一致
SPRITE_ID = "5e97eb03825dee720800000000000000"   # 项目内精灵类型：与其它道具相同才可复用导入设置
NAME_STRIP = "breakable_"       # 输出碎块文件名去掉这个前缀（本工程命名习惯，可置空）

# ---- 道具清单：name / 洋红底原图文件名 / 目标像素高 / 碎块数 / 随机种子 ----
# 示例：两种可破坏物。换成自己的道具只需改这里。
ITEMS = [
    dict(name="breakable_reed", raw="breakable_reed_raw.jpg", target_h=540, pieces=6, seed=20261021),
    dict(name="breakable_rock", raw="breakable_rock_raw.jpg", target_h=380, pieces=6, seed=20261022),
]

META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: {ppu}
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: {sprite_id}
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName: 
  pSDRemoveMatte: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def key_magenta(path):
    """洋红底 -> 透明底 RGBA；返回 float RGB + alpha(0..1)。"""
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]

    # 背景特征：红蓝高、绿低。s 越大越接近洋红
    s = np.minimum(r, b) - g
    alpha = np.clip((118.0 - s) / (118.0 - 58.0), 0.0, 1.0)

    # despill：把残余品红从红/蓝通道里压掉，避免边缘紫边
    spill = np.clip(s, 0.0, None) * 0.8
    rgb[:, :, 0] = np.clip(r - spill, 0, 255)
    rgb[:, :, 2] = np.clip(b - spill, 0, 255)

    return rgb, alpha


def trim(rgb, alpha, pad=4):
    mask = alpha > 0.02
    ys, xs = np.where(mask)
    y0, y1 = ys.min(), ys.max() + 1
    x0, x1 = xs.min(), xs.max() + 1
    h, w = alpha.shape
    y0, y1 = max(0, y0 - pad), min(h, y1 + pad)
    x0, x1 = max(0, x0 - pad), min(w, x1 + pad)
    a = alpha[y0:y1, x0:x1]
    a[a < 0.02] = 0.0
    return rgb[y0:y1, x0:x1], a


def save_rgba(rgb, alpha, path):
    out = np.dstack([rgb, alpha * 255.0]).round().astype(np.uint8)
    Image.fromarray(out, "RGBA").save(path, optimize=True)


def write_meta(png_path, rel_key):
    guid = hashlib.md5(rel_key.encode("utf-8")).hexdigest()
    with open(png_path + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(META_TEMPLATE.format(guid=guid, ppu=PPU, sprite_id=SPRITE_ID))


def resize_to_height(rgb, alpha, target_h):
    h, w = alpha.shape
    scale = target_h / float(h)
    nw = max(1, int(round(w * scale)))
    rgba = np.dstack([rgb, alpha * 255.0]).astype(np.uint8)
    im = Image.fromarray(rgba, "RGBA").resize((nw, target_h), Image.LANCZOS)
    arr = np.asarray(im).astype(np.float32)
    return arr[:, :, :3], arr[:, :, 3] / 255.0


def voronoi_split(rgb, alpha, count, seed, min_ratio=0.015):
    """按最近种子点把不透明像素分给各碎块，返回碎块列表。"""
    rng = np.random.default_rng(seed)
    h, w = alpha.shape
    ys, xs = np.where(alpha > 0.15)
    if len(xs) == 0:
        return []

    pick = rng.choice(len(xs), count, replace=False)
    seeds = np.stack([xs[pick], ys[pick]], axis=1).astype(np.float32)

    # Lloyd 迭代 3 次，让碎块面积更均匀
    for _ in range(3):
        labels = np.zeros((h, w), dtype=np.int16) - 1
        best = np.full((h, w), np.inf, dtype=np.float32)
        for k in range(count):
            dx = np.arange(w)[None, :] - seeds[k, 0]
            dy = np.arange(h)[:, None] - seeds[k, 1]
            d = dx * dx + dy * dy
            better = d < best
            best[better] = d[better]
            labels[better] = k
        for k in range(count):
            sel = (labels == k) & (alpha > 0.15)
            if sel.sum() > 0:
                seeds[k, 0] = xs[sel[ys, xs]].mean()
                seeds[k, 1] = ys[sel[ys, xs]].mean()

    pieces = []
    total = len(xs)
    for k in range(count):
        cell = (labels == k) & (alpha > 0.15)
        area = int(cell.sum())
        if area < total * min_ratio:
            continue
        a = np.where(cell, alpha, 0.0)
        cys, cxs = np.where(a > 0.02)
        if len(cxs) == 0:
            continue
        y0, y1 = cys.min(), cys.max() + 1
        x0, x1 = cxs.min(), cxs.max() + 1
        pieces.append((rgb[y0:y1, x0:x1].copy(), a[y0:y1, x0:x1].copy(), area))

    pieces.sort(key=lambda p: -p[2])
    return pieces


def build(name, raw_file, target_h, piece_count, seed):
    rgb, alpha = key_magenta(os.path.join(ART_GEN, raw_file))
    rgb, alpha = trim(rgb, alpha)
    rgb, alpha = resize_to_height(rgb, alpha, target_h)

    whole = os.path.join(PROPS, name + ".png")
    save_rgba(rgb, alpha, whole)
    write_meta(whole, "props/" + name + ".png")

    h, w = alpha.shape
    print("%s: %dx%d px -> %.2f x %.2f u (PPU %d)" % (name, w, h, w / PPU, h / PPU, PPU))

    os.makedirs(DEBRIS, exist_ok=True)
    pieces = voronoi_split(rgb, alpha, piece_count, seed)
    for i, (prgb, palpha, area) in enumerate(pieces):
        piece = "%s_%02d.png" % (name[len(NAME_STRIP):] if NAME_STRIP and name.startswith(NAME_STRIP) else name, i)
        path = os.path.join(DEBRIS, piece)
        save_rgba(prgb, palpha, path)
        write_meta(path, "debris/" + piece)
        ph, pw = palpha.shape
        print("  debris %s: %dx%d px, %.2f x %.2f u" % (piece, pw, ph, pw / PPU, ph / PPU))

    return len(pieces)


def parse_item(spec):
    """--item "name:raw.jpg:540:6:20261021" -> dict"""
    parts = spec.split(":")
    if len(parts) != 5:
        raise SystemExit("--item 需要 5 段：name:raw_file:target_h:pieces:seed，收到 %r" % spec)
    name, raw, h, n, seed = parts
    return dict(name=name, raw=raw, target_h=int(h), pieces=int(n), seed=int(seed))


def main(argv=None):
    global ART_GEN, PROPS, DEBRIS, PPU, SPRITE_ID, NAME_STRIP
    import argparse
    ap = argparse.ArgumentParser(description="洋红键控 + Voronoi 碎裂：整块 + 碎块 + Unity .meta")
    ap.add_argument("--src-dir", help="洋红底原图目录（默认 <工程根>/../art_gen）")
    ap.add_argument("--out-dir", help="整块输出目录（默认 Assets/Art/Art/Environment/Props）")
    ap.add_argument("--debris-dir", help="碎块输出目录（默认 <out-dir>/Debris）")
    ap.add_argument("--ppu", type=int, help="像素/单位（默认 400）")
    ap.add_argument("--name-strip", help="碎块文件名去掉的前缀（默认 breakable_）")
    ap.add_argument("--item", action="append", metavar="NAME:RAW:H:PIECES:SEED",
                    help="追加/覆盖道具条目，可重复；给了就不再使用文件内 ITEMS")
    a = ap.parse_args(argv)

    if a.src_dir:     ART_GEN = os.path.abspath(a.src_dir)
    if a.out_dir:     PROPS = os.path.abspath(a.out_dir)
    if a.debris_dir:  DEBRIS = os.path.abspath(a.debris_dir)
    else:             DEBRIS = os.path.join(PROPS, "Debris")
    if a.ppu:         PPU = a.ppu
    if a.name_strip is not None: NAME_STRIP = a.name_strip

    items = [parse_item(x) for x in a.item] if a.item else ITEMS
    if not items:
        raise SystemExit("没有道具条目：请填 ITEMS 或用 --item")

    os.makedirs(PROPS, exist_ok=True)
    total = 0
    for it in items:
        total += build(it["name"], it["raw"], it["target_h"], it["pieces"], seed=it["seed"])
    print("done: %d 个道具, 共 %d 块碎片" % (len(items), total))
    return 0


if __name__ == "__main__":
    sys.exit(main())
