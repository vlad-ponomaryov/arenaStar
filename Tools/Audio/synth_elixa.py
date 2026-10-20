"""Elixa sound set: original synthesis (numpy + scipy), no samples (Docs/Briefs/A-Roster3D.md, "Sounds").

Same roles/format as synth_theo.py: 16-bit mono WAV 44.1 kHz in Assets/_Project/Audio/Fighters/Elixa/:
elixa_step_01..04 (soft shoe taps), elixa_attack_01..03 (glass flask throw: whoosh + glass clink + bubbly pop),
elixa_hit (glass splash + honey sparkle), elixa_ult (rising magic shimmer "dome"), elixa_hurt_01..02,
elixa_vo_effort/hurt/win/lose (wordless voice, girl of 15: f0 ~ 230-330 Hz, formant vowels).
Prints checks: duration, peak dBFS, edge level. Deterministic (fixed seed).
Usage: python Tools/Audio/synth_elixa.py
"""
import os

import numpy as np
from scipy import signal
from scipy.io import wavfile

SEED = 20261021
SR = 44100
PEAK_DBFS = -1.0
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Fighters", "Elixa")
rng = np.random.default_rng(SEED)

STEP = [(0.080, 300.0, 2600.0), (0.090, 280.0, 3000.0), (0.085, 320.0, 2400.0), (0.095, 270.0, 2800.0)]  # dur, thump Hz, tap Hz
ATTACK = [(0.30, 1.00), (0.32, 0.94), (0.29, 1.07)]  # dur, pitch factor
CLINK_HZ = [2650.0, 3720.0, 5180.0]  # glass partials (inharmonic)
VOWELS = {"a": (850, 1220, 2810), "e": (600, 2300, 2900), "i": (350, 2700, 3300), "o": (550, 900, 2800),
          "u": (380, 950, 2600), "eh": (650, 1900, 2800)}
VOICE = {  # name: (duration, f0 points (t, Hz), vowel, breath, level)
    "vo_effort": (0.32, [(0, 300), (0.08, 330), (0.32, 270)], "eh", 0.20, 1.0),
    "vo_hurt": (0.36, [(0, 340), (0.06, 350), (0.36, 240)], "a", 0.25, 1.0),
    "vo_win": (0.80, [(0, 260), (0.20, 300), (0.40, 290), (0.80, 360)], "a", 0.12, 1.0),
    "vo_lose": (0.85, [(0, 300), (0.35, 270), (0.45, 285), (0.85, 210)], "o", 0.18, 1.0),
}
HURT = [(0.22, 330.0), (0.25, 300.0)]


def t_of(d):
    return np.arange(int(d * SR)) / SR


def env(t, a, d):
    return (1 - np.exp(-t / max(a, 1e-4))) * np.exp(-t / d)


def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], "bandpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, "lowpass", fs=SR, output="sos"), x)


def noise(n):
    return rng.standard_normal(n)


def sweep(f0, f1, t, curve=1.0):
    d = t[-1] if len(t) else 1
    f = f0 + (f1 - f0) * (t / d) ** curve
    return 2 * np.pi * np.cumsum(f) / SR


def finish(x, name, gain_db=0.0):
    n = len(x)
    fi, fo = int(0.003 * SR), int(0.006 * SR)
    x = x.copy()
    x[:fi] *= np.linspace(0, 1, fi)
    x[-fo:] *= np.linspace(1, 0, fo)
    x = x / (np.abs(x).max() + 1e-9) * 10 ** ((PEAK_DBFS + gain_db) / 20)
    wavfile.write(os.path.join(OUT_DIR, f"elixa_{name}.wav"), SR, (x * 32767).astype(np.int16))
    pk = 20 * np.log10(np.abs(x).max())
    edge = max(abs(x[0]), abs(x[-1]))
    print(f"  elixa_{name}.wav  {n / SR:.3f}s  peak {pk:.1f} dBFS  edge {edge:.4f}")
    return x


def step(d, fth, ftap):
    t = t_of(d)
    th = np.sin(sweep(fth, fth * 0.5, t, 0.5)) * env(t, 0.002, 0.020)
    tap = bp(noise(len(t)), ftap * 0.7, ftap * 1.3) * env(t, 0.0005, 0.010) * 0.5
    return lp(th + tap, 4500)


def clink(t, k, level):
    x = np.zeros_like(t)
    for i, f in enumerate(CLINK_HZ):
        x += np.sin(2 * np.pi * f * k * t + i) * np.exp(-t / (0.06 / (i + 1))) / (i + 1)
    return x * level


def attack(d, k):
    t = t_of(d)
    whoosh = bp(noise(len(t)), 500 * k, 2500 * k) * np.sin(np.pi * np.clip(t / 0.16, 0, 1)) ** 2 * 0.5
    cl = clink(t, k, 0.7) * (1 - np.exp(-t / 0.001))
    # bubbly "glug": short rising blips
    bub = np.zeros_like(t)
    for j, t0 in enumerate((0.05, 0.09, 0.13)):
        tt = np.clip(t - t0, 0, None)
        on = (t >= t0).astype(float)
        bub += on * np.sin(sweep(500 * k * (1 + 0.2 * j), 1100 * k * (1 + 0.2 * j), tt, 0.7)) * np.exp(-tt / 0.025) * 0.35
    return whoosh + cl + bub


def hit():
    t = t_of(0.26)
    splash = bp(noise(len(t)), 900, 5000) * env(t, 0.001, 0.05) * 0.6
    thud = np.sin(sweep(220, 90, t, 0.5)) * env(t, 0.001, 0.03) * 0.8
    cl = clink(t, 1.15, 0.45)
    sp = np.zeros_like(t)
    for f, t0 in ((3300, 0.03), (4100, 0.06), (5000, 0.09)):
        tt = np.clip(t - t0, 0, None)
        sp += (t >= t0) * np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.03) * 0.15
    return splash + thud + cl + sp


def ult():
    d = 1.2
    t = t_of(d)
    x = np.zeros_like(t)
    notes = [392.0, 493.9, 587.3, 784.0, 987.8]  # G major arpeggio, rising "dome" shimmer
    for i, f in enumerate(notes):
        t0 = i * 0.09
        tt = np.clip(t - t0, 0, None)
        e = (t >= t0) * env(tt, 0.01, 0.45)
        x += e * (np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * 2 * f * tt) + 0.12 * np.sin(2 * np.pi * 3.01 * f * tt))
    pad = np.sin(sweep(196, 262, t, 1.0)) * np.sin(np.pi * t / d) * 0.5
    shimmer = bp(noise(len(t)), 4000, 9000) * np.sin(np.pi * t / d) ** 2 * 0.12 * (1 + np.sin(2 * np.pi * 9 * t))
    return x * 0.5 + pad + shimmer


def voice(d, pts, vow, breath, level):
    t = t_of(d)
    pts = np.array(pts, dtype=float)
    f0 = np.interp(t, pts[:, 0], pts[:, 1]) * 2 ** ((6 * np.sin(2 * np.pi * 5.5 * t)) / 1200)
    ph = 2 * np.pi * np.cumsum(f0) / SR
    src = np.zeros_like(t)
    for k in range(1, 30):
        src += np.sin(k * ph) / k * (k * f0 < SR / 2 - 2000)
    src += breath * noise(len(t))
    out = np.zeros_like(t)
    for i, F in enumerate(VOWELS[vow]):
        F2 = F * 1.18  # younger, smaller vocal tract
        out += bp(src, F2 * 0.85, F2 * 1.15) * (1.0, 0.6, 0.3)[i]
    a = np.clip(t / 0.03, 0, 1) * np.clip((d - t) / 0.10, 0, 1)
    return out * a * level


def hurt(d, f):
    t = t_of(d)
    v = voice(d, [(0, f), (0.05, f * 1.05), (d, f * 0.75)], "a", 0.35, 1.0)
    thud = np.sin(sweep(160, 70, t, 0.5)) * env(t, 0.001, 0.04) * 0.6
    return v / (np.abs(v).max() + 1e-9) + thud


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print("Elixa sound set ->", OUT_DIR)
    for i, v in enumerate(STEP, 1):
        finish(step(*v), f"step_{i:02d}", -6.0)
    for i, (d, k) in enumerate(ATTACK, 1):
        finish(attack(d, k), f"attack_{i:02d}")
    finish(hit(), "hit")
    finish(ult(), "ult")
    for i, (d, f) in enumerate(HURT, 1):
        finish(hurt(d, f), f"hurt_{i:02d}", -2.0)
    for name, (d, pts, vow, br, lvl) in VOICE.items():
        finish(voice(d, pts, vow, br, lvl), name, -2.0)
    print("SFX: 16 files OK")


if __name__ == "__main__":
    main()
