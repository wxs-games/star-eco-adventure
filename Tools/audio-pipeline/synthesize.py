#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""StarEco 音频素材程序化合成管线（M10）
==========================================

用 numpy 合成全部游戏音频（22 个 SFX + 4 首 BGM），输出 WAV（16-bit PCM）。
零外部素材依赖、固定随机种子、完全可复现——与 Tools/art-pipeline 同气质。

运行：
    python synthesize.py                # 生成全部素材
    python synthesize.py --sfx Jump     # 只生成指定 SFX（调试用）

输出（相对仓库根）：
    Assets/Audio/Sfx/<SfxId>.wav
    Assets/Resources/Audio/Bgm/<Name>.wav

依赖：numpy（pip install numpy）。采样率 44100，SFX 单声道、BGM 立体声。
"""
import argparse
import math
import os
import struct
import sys
import wave

import numpy as np

SR = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))  # Tools/audio-pipeline/ -> 仓库根
SFX_DIR = os.path.join(REPO_ROOT, "Assets", "Audio", "Sfx")
BGM_DIR = os.path.join(REPO_ROOT, "Assets", "Resources", "Audio", "Bgm")


# ---------------------------------------------------------------- 基础工具

def midi(n):
    """MIDI 音高 -> 频率（A4=69=440Hz）。"""
    return 440.0 * (2.0 ** ((n - 69) / 12.0))


def write_wav(path, data, stereo=False):
    """data: float64 [-1,1] 数组（立体声传 (N,2)）。16-bit PCM。"""
    data = np.clip(data, -1.0, 1.0)
    pcm = (data * 32767.0).astype("<i2")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(2 if stereo else 1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    dur = len(data) / SR
    rms = float(np.sqrt(np.mean(data ** 2))) if len(data) else 0.0
    print("  {:<28} {:>5.2f}s  rms={:.3f}".format(os.path.basename(path), dur, rms))
    return dur, rms


def t_of(dur):
    return np.arange(int(dur * SR)) / SR


def square(freq, dur, duty=0.5):
    """方波（带占空比，模拟芯片音色）。"""
    t = t_of(dur)
    return 2.0 * (((freq * t) % 1.0) < duty) - 1.0


def tri(freq, dur):
    """三角波。"""
    t = t_of(dur)
    return 2.0 * np.abs(2.0 * ((freq * t) % 1.0) - 1.0) - 1.0


def sine(freq, dur):
    t = t_of(dur)
    return np.sin(2 * np.pi * freq * t)


def noise(dur):
    rng = np.random.default_rng(20261018)
    return rng.standard_normal(int(dur * SR)) * 2.0 - 1.0


def sweep(wave_fn, f0, f1, dur):
    """频率随时间线性扫。wave_fn: square/tri/sine。"""
    n = int(dur * SR)
    t = np.arange(n) / SR
    freqs = f0 + (f1 - f0) * (t / dur)
    phase = np.cumsum(freqs) / SR
    if wave_fn is square:
        return 2.0 * ((phase % 1.0) < 0.5) - 1.0
    if wave_fn is tri:
        return 2.0 * np.abs(2.0 * (phase % 1.0) - 1.0) - 1.0
    return np.sin(2 * np.pi * phase)


def env_ar(n, attack, release):
    """起落包络：attack 线性上升，release 指数衰减到 0。"""
    e = np.ones(n)
    na = max(1, int(attack * SR))
    nr = max(1, int(release * SR))
    e[:na] = np.linspace(0, 1, na)
    e[-nr:] = np.linspace(1, 0, nr) ** 2
    return e


def env_exp(n, tau):
    """指数衰减包络（打击感）。"""
    t = np.arange(n) / SR
    return np.exp(-t / tau)


def lowpass(x, cutoff):
    """一阶低通（简化 RC）。"""
    a = math.exp(-2 * math.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = a * acc + (1 - a) * v
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def norm(x, peak=0.89):
    """峰值归一 + 软限幅。"""
    m = np.max(np.abs(x)) if len(x) else 0.0
    if m > 1e-9:
        x = x / m * peak
    return np.tanh(x * 1.2) * 0.95


def mix(*parts):
    """等长对齐混音（长度取最长，短者零填充）。"""
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def fit(n):
    return int(n)


def fft_filter(x, low=None, high=None):
    """FFT 带通/低通/高通：用平滑边缘，避免一阶 RC 的低频残留与相位失真。"""
    n = len(x)
    if n == 0:
        return x
    spec = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    mask = np.ones_like(freqs)
    if low is not None and low > 0:
        mask *= 1.0 / (1.0 + np.exp(-(freqs - low) / max(low * 0.18, 1.0)))
    if high is not None and high > 0:
        mask *= 1.0 / (1.0 + np.exp((freqs - high) / max(high * 0.18, 1.0)))
    return np.fft.irfft(spec * mask, n)


def soft_clip(x, drive=1.4):
    """软限幅：给瞬态一点谐波，听起来更“实”。"""
    m = np.max(np.abs(x)) if len(x) else 0.0
    if m > 1e-9:
        x = x / m
    return np.tanh(x * drive) / np.tanh(drive)


def add_at(buf, start, sig):
    """把 sig 叠加到 buf 的 start 秒处，超出末尾的部分静默截断。"""
    i0 = int(start * SR)
    if i0 >= len(buf) or i0 < 0:
        return
    i1 = min(len(buf), i0 + len(sig))
    if i1 > i0:
        buf[i0:i1] += sig[: i1 - i0]


def add_wrap(buf, start, sig):
    """循环缓冲区叠加：尾部溢出的音符绕回开头，保证 30s 循环接缝无断裂。"""
    n = len(buf)
    if n == 0 or len(sig) == 0:
        return
    i0 = int(start * SR) % n
    i1 = i0 + len(sig)
    if i1 <= n:
        buf[i0:i1] += sig
    else:
        k = n - i0
        buf[i0:] += sig[:k]
        buf[: i1 - n] += sig[k:]


def pluck(freq, dur, tau=0.55, bright=0.55):
    """钢琴/竖琴式拨弦：基频 + 少量谐波，指数衰减，起音极短。"""
    t = t_of(dur)
    sig = np.zeros_like(t)
    partials = [(1.0, 1.0), (2.0, 0.42 * bright), (3.0, 0.22 * bright), (4.0, 0.10 * bright)]
    for mult, amp in partials:
        sig += amp * np.sin(2 * np.pi * freq * mult * t + 0.15 * mult)
    sig *= np.exp(-t / tau)
    attack = max(1, int(0.008 * SR))
    sig[:attack] *= np.linspace(0.0, 1.0, attack)
    return sig


def pad_tone(freqs, dur, amp=0.18):
    """弦乐/合成器铺底：慢起音、慢释放，微失谐制造厚度。"""
    t = t_of(dur)
    sig = np.zeros_like(t)
    for i, f in enumerate(freqs):
        detune = 1.0 + (i - len(freqs) * 0.5) * 0.0015
        sig += np.sin(2 * np.pi * f * detune * t + i * 0.7)
        sig += 0.28 * np.sin(2 * np.pi * f * detune * 2.0 * t + i * 1.3)
    sig /= max(1, len(freqs))
    attack = max(1, int(0.55 * SR))
    release = max(1, int(1.1 * SR))
    env = np.ones_like(t)
    env[:attack] = np.linspace(0.0, 1.0, attack) ** 1.4
    env[-release:] *= np.linspace(1.0, 0.0, release) ** 1.2
    return sig * env * amp


def comb_reverb(x, mix=0.34):
    """块向量化的反馈梳状混响，尾巴自然、无逐样本 Python 循环。"""
    out = x.copy()
    taps = [(0.0297, 0.36), (0.0371, 0.30), (0.0411, 0.26), (0.0437, 0.22)]
    wet = np.zeros_like(x)
    for delay_s, g in taps:
        d = max(1, int(delay_s * SR))
        y = x.copy()
        for start in range(d, len(y), d):
            end = min(start + d, len(y))
            k = end - start
            y[start:end] += g * y[start - d:start - d + k]
        wet += fft_filter(y, None, 5200)
    wet /= len(taps)
    return (1.0 - mix) * out + mix * wet


# ---------------------------------------------------------------- SFX 定义
# 每个函数返回 float64 单声道数组。命名即 SfxId。

def sfx_uiclick():
    a = square(880, 0.04) * env_ar(fit(0.04 * SR), 0.002, 0.035)
    b = square(660, 0.05) * env_ar(fit(0.05 * SR), 0.002, 0.045)
    return mix(a, np.concatenate([np.zeros(fit(0.04 * SR)), b]))


def sfx_jump():
    s = sweep(square, 300, 700, 0.12)
    return s * env_ar(len(s), 0.01, 0.11)


def sfx_land():
    n = lowpass(noise(0.09), 900)
    return n * env_ar(len(n), 0.002, 0.085)


def sfx_footstep():
    """常规脚步（foley 三件套）：鞋跟落地闷击 + 鞋底拍地 + 砂砾摩擦，短促无音调。"""
    dur = 0.12
    n = fit(dur * SR)
    t = np.arange(n) / SR
    src = noise(dur)

    # 1) 鞋跟落地：低频“咚”，短促下坠，提供重量（不作为主音色，只垫底）
    thump = sweep(sine, 96, 58, dur) * np.exp(-t / 0.022) * 0.75

    # 2) 鞋底拍地：中频噪声瞬态，2ms 起音、14ms 衰减，是“踩下去”的主体
    slap = fft_filter(src, 900, 4500) * np.exp(-t / 0.014)
    slap *= np.clip(t / 0.002, 0.0, 1.0)

    # 3) 鞋底与砂砾摩擦：高频细碎颗粒，极短、音量很低，负责“质感”
    grit = fft_filter(src, 3200, 10000) * np.exp(-t / 0.005) * 0.22

    return soft_clip(mix(thump, slap * 0.9, grit), drive=1.3)


def sfx_grassrustle():
    """走过草丛：叶片摩擦的两段沙沙声 + 极轻踩地闷响，像脚陷进草里。"""
    dur = 0.2
    n = fit(dur * SR)
    t = np.arange(n) / SR

    # 两簇错开的带通噪声：先擦过外层叶片，再扫到内层
    leaf_a = fft_filter(noise(dur), 2300, 9000) * np.exp(-((t - 0.028) / 0.045) ** 2)
    leaf_b = fft_filter(noise(dur), 1300, 5600) * np.exp(-((t - 0.095) / 0.05) ** 2) * 0.72
    step = sweep(sine, 96, 62, dur) * np.exp(-t / 0.018) * 0.4

    return soft_clip(mix(leaf_a, leaf_b, step), drive=1.15)


def sfx_attack():
    """挥夹刀风：空气噪声 + 高频金属冷光 + 低频带体，三层错开几毫秒。"""
    dur = 0.24
    t = t_of(dur)
    n = noise(dur)

    # 1) 空气刀风：900~7000Hz，能量集中在起手后 40~80ms
    air = fft_filter(n, 900, 7000)
    air *= np.exp(-((t - 0.055) / 0.052) ** 2)

    # 2) 金属冷光：两个高频正弦快速衰减，给“叮”的锐度
    ring = (sine(3150, dur) * 0.55 + sine(4720, dur) * 0.28) * np.exp(-t / 0.028)
    ring *= np.exp(-((t - 0.012) / 0.035) ** 2)

    # 3) 低频带体：短促下滑，补足厚度，不喧宾夺主
    thump = sweep(sine, 190, 72, 0.13) * env_exp(fit(0.13 * SR), 0.045)
    thump = np.concatenate([np.zeros(fit(0.008 * SR)), thump])

    return soft_clip(mix(air * 0.9, ring * 0.8, thump * 0.55), drive=1.2)


def sfx_hurt():
    t = t_of(0.25)
    vib = 1.0 + 0.06 * np.sin(2 * np.pi * 22 * t)
    ph = np.cumsum(200 * vib) / SR
    s = 2.0 * ((ph % 1.0) < 0.5) - 1.0
    return s * env_ar(len(s), 0.005, 0.24)


def sfx_death():
    s = sweep(square, 400, 60, 0.6)
    return s * env_ar(len(s), 0.01, 0.58)


def sfx_enemyhit():
    """命中有机体：高频脆响 + 中频闷击 + 低频冲击，三层同时起音。"""
    dur = 0.2
    t = t_of(dur)
    n = noise(dur)

    crack = fft_filter(n, 1400, 6500) * np.exp(-t / 0.016)
    flesh = fft_filter(n, 260, 1500) * np.exp(-t / 0.055)
    body = sweep(sine, 145, 52, 0.17) * env_exp(fit(0.17 * SR), 0.055)
    body = np.concatenate([np.zeros(fit(0.004 * SR)), body])

    return soft_clip(mix(crack * 0.95, flesh * 0.8, body * 0.85), drive=1.7)


def sfx_breakplant():
    """水草/藤蔓折断：纤维连续崩断 + 叶片摩擦，多段随机脆裂。"""
    dur = 0.42
    rng = np.random.default_rng(20261021)
    out = np.zeros(fit(dur * SR))

    for i, start in enumerate([0.0, 0.045, 0.082, 0.118, 0.16]):
        seg_dur = 0.05 + rng.uniform(0.0, 0.025)
        seg = noise(seg_dur)
        seg = fft_filter(seg, 700 + i * 120, 4200 + i * 300)
        seg *= np.exp(-np.arange(len(seg)) / SR / 0.018)
        add_at(out, start, seg * (1.0 - i * 0.14))

    rustle = fft_filter(noise(0.34), 1800, 9000) * np.exp(-t_of(0.34) / 0.11) * 0.42
    add_at(out, 0.02, rustle)

    sap = sweep(sine, 260, 90, 0.22) * env_exp(fit(0.22 * SR), 0.06) * 0.5
    add_at(out, 0.0, sap)
    return soft_clip(out, drive=1.25)


def sfx_breakstone():
    """石头碎裂：低频崩裂 + 颗粒碎屑 + 高频石屑飞散。"""
    dur = 0.5
    rng = np.random.default_rng(20261021)
    out = np.zeros(fit(dur * SR))

    thump = sweep(sine, 120, 42, 0.28) * env_exp(fit(0.28 * SR), 0.09)
    add_at(out, 0.0, thump * 0.9)

    crack = fft_filter(noise(0.3), 900, 5200)
    crack *= np.exp(-t_of(0.3) / 0.045)
    add_at(out, 0.0, crack * 0.9)

    # 颗粒碎屑：一串短促高频噪声点，模拟石块互相碰撞
    for i in range(9):
        start = 0.03 + rng.uniform(0.0, 0.3)
        d = 0.012 + rng.uniform(0.0, 0.012)
        tick = fft_filter(noise(d), 1600, 8000)
        tick *= np.exp(-np.arange(len(tick)) / SR / 0.006)
        add_at(out, start, tick * (0.5 - i * 0.035))

    return soft_clip(out, drive=1.5)


def sfx_bosshit():
    s = sweep(square, 150, 90, 0.15)
    n = lowpass(noise(0.1), 500)
    return mix(s * env_ar(len(s), 0.003, 0.14), n * env_ar(len(n), 0.002, 0.09))


def sfx_pickup():
    a = square(midi(76), 0.05) * env_ar(fit(0.05 * SR), 0.002, 0.045)
    b = square(midi(81), 0.10) * env_ar(fit(0.10 * SR), 0.002, 0.095)
    return mix(a, np.concatenate([np.zeros(fit(0.05 * SR)), b]))


def _arp(notes, step, tail=0.0, wave_fn=square):
    """琶音：notes 为 MIDI 列表，每音 step 秒。"""
    parts = []
    for i, m in enumerate(notes):
        d = step + (tail if i == len(notes) - 1 else 0.0)
        s = wave_fn(midi(m), d) * env_ar(fit(d * SR), 0.004, d * 0.9)
        parts.append(np.concatenate([np.zeros(fit(i * step * SR)), s]))
    return mix(*parts)


def sfx_purifycomplete():
    return _arp([72, 76, 79, 84], 0.1)


def sfx_spraystart():
    n = highpass(noise(0.22), 1200)
    ramp = np.linspace(0, 1, len(n)) ** 0.6
    return n * ramp * env_ar(len(n), 0.001, 0.06)


def sfx_heavyrelease():
    n = lowpass(noise(0.5), 300) * env_ar(fit(0.5 * SR), 0.01, 0.48)
    s = sweep(square, 130, 45, 0.5) * env_ar(fit(0.5 * SR), 0.005, 0.48)
    return mix(n, s)


def sfx_rescue():
    s = sweep(square, 520, 780, 0.2) * env_ar(fit(0.2 * SR), 0.005, 0.19)
    tail = square(780, 0.12) * 0.5 * env_ar(fit(0.12 * SR), 0.005, 0.11)
    vib = 1.0 + 0.04 * np.sin(2 * np.pi * 18 * t_of(0.12))
    tail = tail * vib
    return mix(s, np.concatenate([np.zeros(fit(0.2 * SR)), tail]))


def sfx_puzzlesolved():
    a = _arp([72, 76, 79, 84], 0.12)
    tail = square(midi(72), 0.35) * 0.4 * env_ar(fit(0.35 * SR), 0.01, 0.34)
    return mix(a, np.concatenate([np.zeros(fit(0.48 * SR)), tail]))


def sfx_checkpoint():
    a = square(midi(74), 0.09) * env_ar(fit(0.09 * SR), 0.003, 0.085)
    b = square(midi(79), 0.14) * env_ar(fit(0.14 * SR), 0.003, 0.135)
    return mix(a, np.concatenate([np.zeros(fit(0.09 * SR)), b]))


def sfx_splash():
    n = highpass(lowpass(noise(0.4), 3200), 350)
    return n * env_ar(len(n), 0.005, 0.39)


def sfx_levelcomplete():
    return _arp([72, 76, 79, 84, 88], 0.18, tail=0.4)


def sfx_levelfail():
    a = _arp([67, 64, 60], 0.22, wave_fn=tri)
    b = tri(midi(48), 0.5) * 0.6 * env_ar(fit(0.5 * SR), 0.01, 0.48)
    return mix(a, np.concatenate([np.zeros(fit(0.66 * SR)), b]))


SFX = {
    "UIClick": sfx_uiclick,
    "Jump": sfx_jump,
    "Land": sfx_land,
    "Attack": sfx_attack,
    "Hurt": sfx_hurt,
    "Death": sfx_death,
    "EnemyHit": sfx_enemyhit,
    "BossHit": sfx_bosshit,
    "Pickup": sfx_pickup,
    "PurifyComplete": sfx_purifycomplete,
    "SprayStart": sfx_spraystart,
    "HeavyRelease": sfx_heavyrelease,
    "Rescue": sfx_rescue,
    "PuzzleSolved": sfx_puzzlesolved,
    "Checkpoint": sfx_checkpoint,
    "Splash": sfx_splash,
    "LevelComplete": sfx_levelcomplete,
    "LevelFail": sfx_levelfail,
    "BreakPlant": sfx_breakplant,
    "BreakStone": sfx_breakstone,
    "Footstep": sfx_footstep,
    "GrassRustle": sfx_grassrustle,
}


# ---------------------------------------------------------------- BGM 定义
# 每首 30s 无缝循环：所有音符量化到 8 分音符网格，BPM 取 8 的倍数保证整_bar 收尾。

BGM_TOTAL = 30.0

THEMES = {
    # 名称: (bpm, 4 小节和弦进行[midi 根音], 音阶, 亮度参数)
    "Title":    dict(bpm=96,  prog=[60, 67, 69, 65], scale=[60, 62, 64, 65, 67, 69, 71, 72], lead_oct=12, hat=0.10),
    "Level01":  dict(bpm=88,  prog=[57, 53, 60, 55], scale=[57, 59, 60, 62, 64, 65, 67, 69], lead_oct=12, hat=0.06),
    "Level02":  dict(bpm=64, prog=[57, 53, 60, 55], scale=[57, 59, 60, 62, 64, 65, 67, 69], lead_oct=12, hat=0.0, style="lyrical"),
    "Level03":  dict(bpm=128, prog=[52, 48, 55, 50], scale=[52, 54, 55, 57, 59, 60, 62, 64], lead_oct=12, hat=0.16),
}

# 每小节的 8 分音符 lead 琶音模式（索引 -> 和弦内音级 0..3，-1 = 休止）
LEAD_PATTERNS = [
    [0, 2, 1, 2, 0, 2, 1, 3],
    [0, 1, 2, 3, 2, 1, 0, 2],
    [3, 2, 1, 0, 1, 2, 3, 2],
    [0, 3, 1, 2, 0, 3, 1, 2],
]


def _chord_tones(root):
    """大三/小三和弦由根音推（按调内简化和弦：根+三度+五度+八度）。"""
    third = 3 if root % 12 in (0, 5, 7) else 4  # C/G/D 大三，其余按小处理和弦色彩
    return [root, root + third, root + 7, root + 12]


def render_bgm(name):
    cfg = THEMES[name]
    bpm = cfg["bpm"]
    beat = 60.0 / bpm
    eighth = beat / 2.0
    bar_dur = beat * 4
    n_bars = int(round(BGM_TOTAL / bar_dur))
    total = n_bars * bar_dur
    assert abs(total - BGM_TOTAL) < 1e-6, "循环长度必须整除"
    n = int(total * SR)
    left = np.zeros(n)
    right = np.zeros(n)
    rng = np.random.default_rng(20261018)

    def place(buf, start, sig, pan=0.5):
        i0 = int(start * SR)
        i1 = min(n, i0 + len(sig))
        if i1 <= i0:
            return
        buf[i0:i1] += sig[: i1 - i0]

    for bar in range(n_bars):
        root = cfg["prog"][bar % len(cfg["prog"])]
        tones = _chord_tones(root)
        bar_t = bar * bar_dur
        # Bass：三角波，每拍根音（第 1/3 拍加重）
        for b in range(4):
            d = beat * 0.9
            s = tri(midi(root - 12 + (12 if b == 3 else 0)), d) * (0.5 if b % 2 == 0 else 0.35)
            s *= env_ar(len(s), 0.01, d * 0.8)
            place(left, bar_t + b * beat, s)
            place(right, bar_t + b * beat, s)
        # Lead：8 分音符琶音（方波，轻微衰减）
        pat = LEAD_PATTERNS[bar % len(LEAD_PATTERNS)]
        for i, deg in enumerate(pat):
            if deg < 0:
                continue
            d = eighth * 0.85
            m = tones[deg] + cfg["lead_oct"]
            s = square(midi(m), d) * 0.30
            s *= env_ar(len(s), 0.005, d * 0.85)
            place(left, bar_t + i * eighth, s, pan=0.42)
            place(right, bar_t + i * eighth, s, pan=0.58)
        # 鼓：底鼓 1/3 拍，军鼓 2/4 拍，闭合 hi-hat 每个 8 分
        k = sweep(sine, 130, 48, 0.10) * env_ar(fit(0.10 * SR), 0.001, 0.095)
        place(left, bar_t, k); place(right, bar_t, k)
        place(left, bar_t + 2 * beat, k); place(right, bar_t + 2 * beat, k)
        for i in range(8):
            d = 0.035
            h = highpass(rng.standard_normal(fit(d * SR)) * 2 - 1, 6000) * cfg["hat"] * 0.5
            h *= env_ar(len(h), 0.001, d * 0.9)
            amp = 1.0 if i % 2 == 0 else 0.6
            place(left, bar_t + i * eighth, h * amp)
            place(right, bar_t + i * eighth, h * amp)

    # 简易胶水：整体一阶低通 + 归一
    glue = 4200 if name != "Level03" else 5200
    left = norm(lowpass(left, glue), peak=0.82)
    right = norm(lowpass(right, glue), peak=0.82)
    return np.stack([left, right], axis=1)


# ---------------------------------------------------------------- 抒情 BGM（第二关）
# 空洞骑士式氛围：慢速钢琴/竖琴主旋律 + 弦乐铺底 + 低音提琴，无鼓点，
# 全部音符走循环缓冲区叠加，30s 接缝处尾音自然绕回开头。

LYRICAL_CHORDS = [
    [57, 60, 64, 69],   # Am
    [53, 57, 60, 65],   # F
    [48, 52, 55, 60],   # C
    [55, 59, 62, 67],   # G
    [57, 60, 64, 69],   # Am
    [53, 57, 60, 65],   # F
    [50, 53, 57, 62],   # Dm
    [52, 56, 59, 64],   # E
]

# 每小节旋律：(起始拍, 时值拍, MIDI 音高)
LYRICAL_MELODY = [
    [(0.0, 1.5, 76), (1.5, 0.5, 74), (2.0, 1.0, 72), (3.0, 1.0, 71)],
    [(0.0, 2.0, 69), (2.0, 1.0, 72), (3.0, 1.0, 76)],
    [(0.0, 1.5, 79), (1.5, 0.5, 76), (2.0, 2.0, 74)],
    [(0.0, 1.0, 72), (1.0, 1.0, 71), (2.0, 2.0, 74)],
    [(0.0, 1.0, 76), (1.0, 1.0, 79), (2.0, 2.0, 81)],
    [(0.0, 1.5, 77), (1.5, 0.5, 76), (2.0, 1.0, 74), (3.0, 1.0, 72)],
    [(0.0, 2.0, 74), (2.0, 1.0, 71), (3.0, 1.0, 69)],
    [(0.0, 1.5, 71), (1.5, 0.5, 67), (2.0, 2.0, 64)],
]


def render_bgm_lyrical(name):
    """抒情编曲：8 小节 × 3.75s = 30s 整，钢琴旋律 + 弦乐垫 + 低音，无鼓。"""
    cfg = THEMES[name]
    bpm = cfg["bpm"]
    beat = 60.0 / bpm
    bar_dur = beat * 4.0
    n_bars = int(round(BGM_TOTAL / bar_dur))
    total = n_bars * bar_dur
    assert abs(total - BGM_TOTAL) < 1e-6, "循环长度必须整除"

    n = int(total * SR)
    left = np.zeros(n)
    right = np.zeros(n)

    for bar in range(n_bars):
        bar_t = bar * bar_dur
        chord = LYRICAL_CHORDS[bar % len(LYRICAL_CHORDS)]

        # 弦乐铺底：整小节慢起慢收，左右轻微错相制造宽度
        pad = pad_tone([midi(m) for m in chord], bar_dur * 1.08, amp=0.12)
        add_wrap(left, bar_t, pad * 0.95)
        add_wrap(right, bar_t + 0.012, pad * 0.95)

        # 低音：每小节第 1、3 拍，柔和拨弦
        for b in (0, 2):
            bass = pluck(midi(chord[0] - 12), beat * 2.0, tau=1.05, bright=0.10) * 0.42
            add_wrap(left, bar_t + b * beat, bass)
            add_wrap(right, bar_t + b * beat, bass * 0.92)

        # 主旋律：钢琴拨弦，偏右一点点
        for offset, length, note in LYRICAL_MELODY[bar % len(LYRICAL_MELODY)]:
            sig = pluck(midi(note), max(0.18, length * beat * 1.35), tau=0.95, bright=0.72) * 0.5
            add_wrap(left, bar_t + offset * beat, sig * 0.82)
            add_wrap(right, bar_t + offset * beat, sig)

        # 装饰琶音：只在第 3/6/8 小节出现，避免过密
        if bar in (2, 5, 7):
            for i in range(4):
                note = chord[i] + 12
                sig = pluck(midi(note), beat * 0.55, tau=0.38, bright=0.45) * 0.16
                t0 = bar_t + (1.5 + i * 0.5) * beat
                add_wrap(left, t0, sig * 0.75)
                add_wrap(right, t0, sig)

    # 混响 + 柔和低通，去掉合成器毛刺
    left = fft_filter(comb_reverb(left, mix=0.36), None, 4200)
    right = fft_filter(comb_reverb(right, mix=0.36), None, 4200)
    left = norm(left, peak=0.78)
    right = norm(right, peak=0.78)
    return np.stack([left, right], axis=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sfx", default=None, help="只生成指定 SFX（如 Jump）")
    ap.add_argument("--bgm", default=None, help="只生成指定 BGM（如 Title）")
    args = ap.parse_args()

    print("== StarEco 音频合成 ==  sr={}  seed=20261018".format(SR))
    if args.sfx:
        if args.sfx not in SFX:
            print("未知 SFX：{}，可选：{}".format(args.sfx, ", ".join(SFX)))
            sys.exit(1)
        data = norm(SFX[args.sfx]())
        write_wav(os.path.join(SFX_DIR, args.sfx + ".wav"), data)
        return

    if args.bgm:
        if args.bgm not in THEMES:
            print("未知 BGM：{}，可选：{}".format(args.bgm, ", ".join(THEMES)))
            sys.exit(1)
        data = render_bgm_lyrical(args.bgm) if THEMES[args.bgm].get("style") == "lyrical" else render_bgm(args.bgm)
        write_wav(os.path.join(BGM_DIR, args.bgm + ".wav"), data, stereo=True)
        return

    print("[SFX] -> {}".format(os.path.relpath(SFX_DIR, REPO_ROOT)))
    for name, fn in SFX.items():
        write_wav(os.path.join(SFX_DIR, name + ".wav"), norm(fn()))
    print("[BGM] -> {}".format(os.path.relpath(BGM_DIR, REPO_ROOT)))
    for name in THEMES:
        data = render_bgm_lyrical(name) if THEMES[name].get("style") == "lyrical" else render_bgm(name)
        write_wav(os.path.join(BGM_DIR, name + ".wav"), data, stereo=True)
    print("完成：{} 个 SFX + {} 首 BGM".format(len(SFX), len(THEMES)))


if __name__ == "__main__":
    main()
