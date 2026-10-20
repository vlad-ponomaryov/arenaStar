"""Bront sound set: original synthesis (numpy + scipy), no samples (Docs/Briefs/A-Roster3D.md, "Sounds").

Same roles/format as synth_theo.py / synth_elixa.py: 16-bit mono WAV 44.1 kHz in Assets/_Project/Audio/Fighters/Bront/:
bront_step_01..04 (sneaker steps), bront_attack_01..03 (lightning from the copper rod: crackle-zap + buzz + a smaller
second zap for the chain jump), bront_hit (electric snap + sizzle), bront_ult ("Storm Cloud": the cloud Rumbly grumbles
like a kitten, thunder roll, crackles), bront_hurt_01..02, bront_vo_effort/hurt/win/lose (wordless voice, loud boy of 15:
f0 ~ 140-230 Hz). Prints checks: duration, peak dBFS, edge level. Deterministic (fixed seed).
Usage: python Tools/Audio/synth_bront.py
"""
import os

import numpy as np
from scipy import signal
from scipy.io import wavfile

SEED = 20261032
SR = 44100
PEAK_DBFS = -1.0
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Fighters", "Bront")
rng = np.random.default_rng(SEED)

FORMANT_K = 1.06  # teen boy, voice already low
STEP = [(0.085, 230.0, 2200.0), (0.095, 210.0, 2500.0), (0.090, 250.0, 2000.0), (0.100, 200.0, 2300.0)]  # dur, thump, tap
ATTACK = [(0.40, 1.00), (0.42, 0.93), (0.38, 1.08)]  # dur, pitch factor
VOWELS = {"a": (850, 1220, 2810), "e": (600, 2300, 2900), "i": (350, 2700, 3300), "o": (550, 900, 2800),
          "u": (380, 950, 2600), "eh": (650, 1900, 2800)}
VOICE = {  # name: (duration, f0 points (t, Hz), vowel, breath, level)
    "vo_effort": (0.30, [(0, 180), (0.07, 205), (0.30, 160)], "a", 0.22, 1.0),
    "vo_hurt": (0.36, [(0, 220), (0.05, 230), (0.36, 150)], "eh", 0.28, 1.0),
    "vo_win": (0.95, [(0, 150), (0.2, 190), (0.45, 210), (0.7, 230), (0.95, 200)], "a", 0.12, 1.0),
    "vo_lose": (0.90, [(0, 190), (0.35, 165), (0.45, 175), (0.90, 125)], "o", 0.20, 1.0),
}
HURT = [(0.24, 220.0), (0.27, 200.0)]


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
    wavfile.write(os.path.join(OUT_DIR, f"bront_{name}.wav"), SR, (x * 32767).astype(np.int16))
    pk = 20 * np.log10(np.abs(x).max())
    edge = max(abs(x[0]), abs(x[-1]))
    print(f"  bront_{name}.wav  {n / SR:.3f}s  peak {pk:.1f} dBFS  edge {edge:.4f}")
    return x


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
        F2 = F * FORMANT_K
        out += bp(src, F2 * 0.85, F2 * 1.15) * (1.0, 0.6, 0.3)[i]
    a = np.clip(t / 0.03, 0, 1) * np.clip((d - t) / 0.10, 0, 1)
    return out * a * level


def step(d, fth, ftap):
    t = t_of(d)
    th = np.sin(sweep(fth, fth * 0.5, t, 0.5)) * env(t, 0.002, 0.022)
    tap = bp(noise(len(t)), ftap * 0.7, ftap * 1.3) * env(t, 0.0005, 0.010) * 0.45
    return lp(th + tap, 4200)


def crackle(t, t0, dur, rate, level):
    """Random sharp electric ticks between t0 and t0 + dur."""
    x = np.zeros_like(t)
    n = int(dur * rate)
    for tt0 in t0 + rng.uniform(0, dur, n):
        i = int(tt0 * SR)
        m = min(len(t) - i, int(0.004 * SR))
        if m > 0:
            x[i:i + m] += rng.standard_normal(m) * np.exp(-np.arange(m) / (0.0007 * SR)) * rng.uniform(0.4, 1.0)
    return bp(x, 1500, 9000) * level


def zap(t, t0, d, k, level):
    """Lightning zap: buzzy sawtooth (jittered pitch) under a fast envelope + crackle."""
    tt = np.clip(t - t0, 0, None)
    on = ((t >= t0) & (t < t0 + d)).astype(float)
    f = 120 * k * (1 + 0.25 * rng.standard_normal(len(t)).cumsum() / np.sqrt(np.arange(1, len(t) + 1)))
    ph = 2 * np.pi * np.cumsum(np.abs(f)) / SR
    saw = signal.sawtooth(ph) + 0.5 * signal.sawtooth(ph * 2.01)
    e = on * np.clip(tt / 0.003, 0, 1) * np.exp(-tt / (d * 0.45))
    snap = on * bp(noise(len(t)), 2000, 8000) * np.exp(-tt / 0.008)
    return (lp(saw, 3500) * e * 0.6 + snap * 0.7) * level + crackle(t, t0, d * 0.8, 120, 0.5 * level)


def attack(d, k):
    t = t_of(d)
    charge = np.sin(sweep(600 * k, 1800 * k, t, 1.0)) * np.clip(t / 0.05, 0, 1) * (t < 0.07) * 0.2
    return charge + zap(t, 0.05, 0.22, k, 1.0) + zap(t, 0.20, 0.15, k * 1.3, 0.55)  # main bolt + chain jump


def hit():
    t = t_of(0.30)
    snap = bp(noise(len(t)), 1500, 9000) * env(t, 0.0005, 0.012)
    thud = np.sin(sweep(200, 80, t, 0.5)) * env(t, 0.001, 0.035) * 0.7
    sizzle = bp(noise(len(t)), 3000, 8000) * env(t, 0.01, 0.10) * 0.25 * (1 + np.sin(2 * np.pi * 60 * t))
    return snap + thud + sizzle + crackle(t, 0.0, 0.2, 80, 0.4)


def ult():
    d = 1.6
    t = t_of(d)
    # Rumbly grumbles like a kitten: low purr-ish tone with fast amplitude flutter
    g_e = np.clip(t / 0.05, 0, 1) * np.clip((0.55 - t) / 0.1, 0, 1)
    grumble = np.sin(sweep(150, 200, t, 1.0)) * (0.5 + 0.5 * np.sin(2 * np.pi * 26 * t)) * g_e * 0.5
    grumble = lp(grumble + 0.3 * grumble ** 2, 900)
    # thunder: big low noise roll with a sharp start at 0.45 s
    tt = np.clip(t - 0.45, 0, None)
    on = (t >= 0.45).astype(float)
    roll = lp(noise(len(t)), 260, 4) * on * env(tt, 0.01, 0.45) * (1 + 0.6 * np.sin(2 * np.pi * 7 * t)) * 2.5
    crack = bp(noise(len(t)), 800, 7000) * on * np.exp(-tt / 0.03) * 0.8
    return grumble + roll + crack + crackle(t, 0.5, 1.0, 60, 0.35)


def hurt(d, f):
    t = t_of(d)
    v = voice(d, [(0, f), (0.05, f * 1.05), (d, f * 0.72)], "eh", 0.32, 1.0)
    thud = np.sin(sweep(150, 65, t, 0.5)) * env(t, 0.001, 0.04) * 0.6
    return v / (np.abs(v).max() + 1e-9) + thud


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print("Bront sound set ->", OUT_DIR)
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
