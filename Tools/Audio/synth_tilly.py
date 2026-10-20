"""Tilly sound set: original synthesis (numpy + scipy), no samples (Docs/Briefs/A-Roster3D.md, "Sounds").

Same roles/format as synth_theo.py / synth_elixa.py: 16-bit mono WAV 44.1 kHz in Assets/_Project/Audio/Fighters/Tilly/:
tilly_step_01..04 (light sneaker pats), tilly_attack_01..03 (balloon pump "pfft" + lobbed balloon "boop" pop),
tilly_hit (rubbery balloon pop + confetti sparkle), tilly_ult ("Fly away!": balloons squeak-inflate, rising whistle and
wind), tilly_hurt_01..02, tilly_vo_effort/hurt/win/lose (wordless voice, girl of 9: f0 ~ 320-480 Hz, small formants).
Prints checks: duration, peak dBFS, edge level. Deterministic (fixed seed).
Usage: python Tools/Audio/synth_tilly.py
"""
import os

import numpy as np
from scipy import signal
from scipy.io import wavfile

SEED = 20261031
SR = 44100
PEAK_DBFS = -1.0
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Fighters", "Tilly")
rng = np.random.default_rng(SEED)

FORMANT_K = 1.28  # small child: formants well above adult values
STEP = [(0.070, 360.0, 3200.0), (0.075, 340.0, 3600.0), (0.068, 380.0, 3000.0), (0.080, 330.0, 3400.0)]  # dur, thump, tap
ATTACK = [(0.36, 1.00), (0.38, 0.92), (0.35, 1.09)]  # dur, pitch factor
VOWELS = {"a": (850, 1220, 2810), "e": (600, 2300, 2900), "i": (350, 2700, 3300), "o": (550, 900, 2800),
          "u": (380, 950, 2600), "eh": (650, 1900, 2800)}
VOICE = {  # name: (duration, f0 points (t, Hz), vowel, breath, level)
    "vo_effort": (0.28, [(0, 400), (0.07, 440), (0.28, 370)], "eh", 0.18, 1.0),
    "vo_hurt": (0.34, [(0, 470), (0.05, 490), (0.34, 340)], "a", 0.22, 1.0),
    "vo_win": (0.85, [(0, 380), (0.15, 450), (0.30, 420), (0.55, 500), (0.85, 560)], "i", 0.10, 1.0),
    "vo_lose": (0.90, [(0, 430), (0.35, 380), (0.45, 400), (0.90, 300)], "o", 0.16, 1.0),
}
HURT = [(0.22, 460.0), (0.25, 420.0)]


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
    wavfile.write(os.path.join(OUT_DIR, f"tilly_{name}.wav"), SR, (x * 32767).astype(np.int16))
    pk = 20 * np.log10(np.abs(x).max())
    edge = max(abs(x[0]), abs(x[-1]))
    print(f"  tilly_{name}.wav  {n / SR:.3f}s  peak {pk:.1f} dBFS  edge {edge:.4f}")
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
    th = np.sin(sweep(fth, fth * 0.55, t, 0.5)) * env(t, 0.002, 0.016)
    tap = bp(noise(len(t)), ftap * 0.7, ftap * 1.3) * env(t, 0.0005, 0.008) * 0.45
    return lp(th + tap, 5000)


def pop(t, t0, k, level):
    """Rubber balloon pop: sharp broadband crack + short low 'boop' body."""
    tt = np.clip(t - t0, 0, None)
    on = (t >= t0).astype(float)
    crack = bp(noise(len(t)), 1200 * k, 7000 * k) * on * np.exp(-tt / 0.006)
    boop = on * np.sin(sweep(420 * k, 160 * k, tt, 0.6)) * np.exp(-tt / 0.045) * 0.9
    return (crack * 0.8 + boop) * level


def attack(d, k):
    t = t_of(d)
    # pump "pfft": short airy burst
    pf = bp(noise(len(t)), 700 * k, 3000 * k) * env(t, 0.004, 0.035) * 0.55
    # rubbery squeak as the little balloon leaves the nozzle (lob)
    sq_e = np.clip(t / 0.01, 0, 1) * np.exp(-t / 0.07) * (t < 0.16)
    sq = np.sin(sweep(700 * k, 1250 * k, t, 0.8) + 0.4 * np.sin(2 * np.pi * 38 * t)) * sq_e * 0.35
    return pf + sq + pop(t, d - 0.12, k, 0.85)


def hit():
    t = t_of(0.30)
    x = pop(t, 0.0, 1.1, 1.0)
    sp = np.zeros_like(t)
    for f, t0 in ((2900, 0.025), (3600, 0.05), (4400, 0.075), (5200, 0.10)):  # confetti twinkle
        tt = np.clip(t - t0, 0, None)
        sp += (t >= t0) * np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.03) * 0.14
    return x + sp


def ult():
    d = 1.3
    t = t_of(d)
    x = np.zeros_like(t)
    # three balloons inflating: rubbery squeaks rising in pitch
    for i, t0 in enumerate((0.0, 0.12, 0.24)):
        tt = np.clip(t - t0, 0, None)
        e = (t >= t0) * np.clip(tt / 0.02, 0, 1) * np.exp(-tt / 0.12) * (tt < 0.3)
        x += e * np.sin(sweep(500 * (1 + 0.25 * i), 900 * (1 + 0.25 * i), np.clip(tt, 0, 0.3), 1.0)
                        + 0.5 * np.sin(2 * np.pi * 45 * tt)) * 0.35
    # rising slide whistle "up we go"
    w_e = np.clip((t - 0.3) / 0.1, 0, 1) * np.clip((d - t) / 0.25, 0, 1)
    x += np.sin(sweep(520, 1560, t, 1.4) + 0.08 * np.sin(2 * np.pi * 6 * t)) * w_e * 0.45
    wind = bp(noise(len(t)), 500, 2200) * np.sin(np.pi * t / d) ** 2 * 0.35
    chime = np.zeros_like(t)
    for i, f in enumerate((1047.0, 1319.0, 1568.0, 2093.0)):  # C major sparkle on top
        t0 = 0.55 + i * 0.1
        tt = np.clip(t - t0, 0, None)
        chime += (t >= t0) * np.sin(2 * np.pi * f * tt) * env(tt, 0.004, 0.25) * 0.18
    return x + wind + chime


def hurt(d, f):
    t = t_of(d)
    v = voice(d, [(0, f), (0.05, f * 1.05), (d, f * 0.75)], "a", 0.3, 1.0)
    thud = np.sin(sweep(170, 80, t, 0.5)) * env(t, 0.001, 0.035) * 0.5
    return v / (np.abs(v).max() + 1e-9) + thud


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print("Tilly sound set ->", OUT_DIR)
    for i, v in enumerate(STEP, 1):
        finish(step(*v), f"step_{i:02d}", -7.0)
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
