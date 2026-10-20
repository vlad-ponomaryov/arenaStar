"""Nana sound set: original synthesis (numpy + scipy), no samples.

Writes 16-bit mono WAV files to Assets/_Project/Audio/Fighters/Nana/, a preview
(all sounds in a row) and a spectrogram sheet to Tools/out/audio/, and prints
objective checks (duration, peak, edge clicks, spectral centroid, voice f0).

Deterministic: fixed seed, all tunable parameters in dictionaries below.
Usage:  python Tools/Audio/synth_nana.py
Brief:  Docs/Briefs/A-Roster3D.md (sounds, wave 2). Copy of synth_lark.py retuned for Nana Chill
        (grandma, ~70, aura healer): soft slipper steps, thermos "warm tea splash" attack, wet "plip"
        hit, "warm spring" ult (bubbling pool + soft chime chord), calm elderly woman voice.
"""
import os
import sys

import numpy as np
from scipy import signal
from scipy.io import wavfile

# ---------------------------------------------------------------- parameters
SEED = 20261122
SR = 44100
PEAK_DBFS = -1.0
FADE_IN_MS = 3.0
FADE_OUT_MS = 5.0
PREVIEW_GAP_S = 0.4

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Fighters", "Nana")
PREVIEW_DIR = os.path.join(ROOT, "Tools", "out", "audio")

# Footsteps: rubber-sole "thump" + a little scuff noise.
# Thump kept above ~100 Hz so it is still audible on phone speakers.
STEP = {
    "variants": [  # (duration s, thump start Hz, thump end Hz, scuff level, scuff centre Hz)
        (0.090, 210.0, 115.0, 0.60, 1500.0),
        (0.095, 195.0, 110.0, 0.55, 1700.0),
        (0.100, 225.0, 120.0, 0.65, 1400.0),
        (0.105, 190.0, 105.0, 0.58, 1600.0),
    ],
    "thump_decay": 0.022,
    "scuff_decay": 0.018,
    "scuff_delay": 0.004,
    "lowpass": 2200.0,
}

# Blaster shot: low thump + soft harmonic glide (up, then down) + small hiss.
SHOT = {
    "variants": [  # (duration s, glide base Hz) -> here: splash duration, bubble base Hz
        (0.300, 420.0),
        (0.320, 380.0),
        (0.290, 460.0),
    ],
    "bubbles": 5,
    "bubble_spacing": 0.035,
    "bubble_rise": 1.8,
    "bubble_len": 0.045,
    "bubble_level": 0.35,
    "thump_hz": (160.0, 70.0),
    "thump_decay": 0.045,
    "thump_level": 0.6,
    "glide_up": 1.9,          # base -> base*up in glide_up_time
    "glide_up_time": 0.045,
    "glide_down": 1.5,        # then -> base*down at the end
    "harmonics": [1.0, 0.35, 0.12],
    "tone_level": 0.45,
    "tone_attack": 0.006,
    "tone_decay": 0.060,
    "tone_lowpass": 2000.0,
    "vibrato_hz": 28.0,
    "vibrato_depth": 0.015,
    "hiss_band": (500.0, 2600.0),
    "hiss_level": 0.55,
    "hiss_decay": 0.040,
}

# Projectile hit: dense soft "slap" + a short sparkle.
HIT = {
    "duration": 0.150,
    "thud_hz": (320.0, 140.0),
    "thud_decay": 0.030,
    "slap_band": (400.0, 2600.0),
    "slap_level": 0.55,
    "slap_decay": 0.018,
    "sparkle_hz": [900.0, 1250.0, 1600.0],
    "sparkle_delays": [0.010, 0.022, 0.034],
    "sparkle_level": 0.10,
    "sparkle_decay": 0.030,
}

# Ultimate: 0.3 s charge + big discharge.
ULT = {
    "duration": 1.40,
    "charge_time": 0.50,
    "charge_hz": (140.0, 520.0),
    "charge_harmonics": [1.0, 0.45, 0.22, 0.10],
    "charge_tremolo_hz": (18.0, 60.0),
    "charge_level": 0.55,
    "charge_noise_band": (600.0, 2400.0),
    "charge_noise_level": 0.10,
    "boom_hz": (95.0, 38.0),
    "boom_decay": 0.170,
    "boom_level": 0.45,
    "blast_hz": (700.0, 900.0),
    "blast_harmonics": [1.0, 0.6, 0.35, 0.2, 0.1],
    "blast_decay": 0.260,
    "blast_level": 0.55,
    "blast_lowpass": 2200.0,
    "rumble_lowpass": 900.0,
    "rumble_level": 0.35,
    "rumble_decay": 0.330,
}

# Hurt: dull thud + short armour squeak (no voice).
HURT = {
    "variants": [  # (duration s, thud start Hz, squeak start Hz, squeak end Hz)
        (0.160, 270.0, 1150.0, 1550.0),
        (0.180, 240.0, 1350.0, 1050.0),
    ],
    "thud_end_ratio": 0.5,
    "thud_decay": 0.040,
    "noise_lowpass": 1200.0,
    "noise_level": 0.6,
    "noise_decay": 0.030,
    "squeak_delay": 0.025,
    "squeak_len": 0.070,
    "squeak_level": 0.0,
    "squeak_vibrato_hz": 45.0,
    "squeak_vibrato_depth": 0.03,
}

# Elderly woman vowel formants (Hz): F1, F2, F3, F4.
VOWELS = {
    "a": (800.0, 1350.0, 2900.0, 3900.0),
    "e": (520.0, 2050.0, 2950.0, 4000.0),
    "i": (340.0, 2500.0, 3200.0, 4100.0),
    "o": (500.0, 900.0, 2800.0, 3800.0),
    "u": (380.0, 850.0, 2700.0, 3700.0),
    "m": (300.0, 1100.0, 2600.0, 3600.0),
}
FORMANT_BW = (110.0, 150.0, 240.0, 320.0)

# Wordless voice: f0 contour points (t s, Hz), vowel path (t s, vowel),
# amplitude points (t s, gain), breath onset ("h") length, breath level.
VOICE = {
    "nana_vo_effort": {  # calm exhale "hah" (Breathe.)
        "duration": 0.45,
        "f0": [(0.0, 230.0), (0.15, 238.0), (0.45, 200.0)],
        "vowels": [(0.0, "a"), (0.45, "o")],
        "amp": [(0.0, 0.0), (0.10, 0.0), (0.16, 1.0), (0.30, 0.7), (0.45, 0.0)],
        "breath_onset": 0.12,
        "breath_level": 0.16,
    },
    "nana_vo_hurt": {  # "oi"
        "duration": 0.36,
        "f0": [(0.0, 265.0), (0.10, 270.0), (0.36, 205.0)],
        "vowels": [(0.0, "o"), (0.16, "o"), (0.28, "i"), (0.36, "i")],
        "amp": [(0.0, 0.0), (0.02, 1.0), (0.20, 0.8), (0.36, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.08,
    },
    "nana_vo_win": {  # pleased "mm-hm!" rising then settling
        "duration": 0.80,
        "f0": [(0.0, 205.0), (0.25, 215.0), (0.32, 240.0), (0.60, 268.0), (0.80, 250.0)],
        "vowels": [(0.0, "m"), (0.24, "m"), (0.34, "a"), (0.80, "a")],
        "amp": [(0.0, 0.0), (0.03, 0.7), (0.22, 0.65), (0.27, 0.3), (0.35, 1.0), (0.65, 0.85), (0.80, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.08,
    },
    "nana_vo_lose": {  # gentle sigh "ohh..."
        "duration": 0.90,
        "f0": [(0.0, 240.0), (0.35, 220.0), (0.90, 182.0)],
        "vowels": [(0.0, "o"), (0.90, "u")],
        "amp": [(0.0, 0.0), (0.10, 0.85), (0.50, 0.65), (0.90, 0.0)],
        "breath_onset": 0.09,
        "breath_level": 0.15,
    },
}
VOICE_COMMON = {
    "vibrato_hz": 6.2,
    "vibrato_cents": 30.0,
    "jitter_cents": 10.0,
    "tilt": 1.15,              # harmonic amplitude ~ 1/k^tilt
    "max_harm_hz": 5000.0,
    "frame_s": 0.002,
}

CHECKS = {
    "shot_centroid_max_hz": 2500.0,
    "voice_f0_range": (175.0, 285.0),
    "voice_f0_tolerance": 15.0,
    "lead_silence_ms": 5.0,
    "edge_click_ratio": 0.02,   # |first|, |last| sample vs peak
}

rng = np.random.default_rng(SEED)


# ---------------------------------------------------------------- helpers
def t_axis(dur):
    return np.arange(int(round(dur * SR))) / SR


def exp_env(t, decay, delay=0.0):
    e = np.exp(-np.maximum(t - delay, 0.0) / decay)
    e[t < delay] = 0.0
    return e


def attack_decay(t, attack, decay, delay=0.0):
    tt = t - delay
    a = np.clip(tt / attack, 0.0, 1.0)
    d = np.exp(-np.maximum(tt - attack, 0.0) / decay)
    out = a * d
    out[tt < 0] = 0.0
    return out


def sweep_phase(f_start, f_end, t, curve=1.0):
    """Phase of an exponential-ish glide f_start -> f_end over len(t)."""
    x = (t - t[0]) / max(t[-1] - t[0], 1e-9)
    f = f_start * (f_end / f_start) ** (x ** curve)
    return 2 * np.pi * np.cumsum(f) / SR, f


def harmonic_tone(freq, harmonics):
    phase = 2 * np.pi * np.cumsum(freq) / SR
    out = np.zeros_like(freq)
    for k, a in enumerate(harmonics, start=1):
        alias_guard = (k * freq < SR * 0.45).astype(float)
        out += a * np.sin(k * phase) * alias_guard
    return out


def noise(n):
    return rng.standard_normal(n)


def bandpass(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="bandpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lowpass(x, fc, order=2):
    sos = signal.butter(order, fc, btype="lowpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def highpass(x, fc, order=2):
    sos = signal.butter(order, fc, btype="highpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def interp_points(points, t):
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return np.interp(t, xs, ys)


def finalize(x):
    x = highpass(x, 25.0)  # remove DC / sub-rumble
    peak = np.max(np.abs(x))
    thr = peak * 10 ** (-50 / 20)
    start = int(np.argmax(np.abs(x) > thr))
    start = max(0, start - int(0.001 * SR))
    x = x[start:].copy()
    nin = int(FADE_IN_MS * SR / 1000)
    nout = int(FADE_OUT_MS * SR / 1000)
    x[:nin] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, nin))
    x[-nout:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, nout))
    x[0] = 0.0
    x[-1] = 0.0
    x *= 10 ** (PEAK_DBFS / 20) / np.max(np.abs(x))
    return x


# ---------------------------------------------------------------- sounds
def make_step(v):
    dur, f0, f1, scuff_lvl, scuff_c = v
    t = t_axis(dur)
    ph, _ = sweep_phase(f0, f1, t, curve=0.5)
    thump = np.sin(ph) * attack_decay(t, 0.002, STEP["thump_decay"])
    n = bandpass(noise(len(t)), scuff_c * 0.5, scuff_c * 1.6)
    scuff = scuff_lvl * n * attack_decay(t, 0.003, STEP["scuff_decay"], STEP["scuff_delay"])
    return lowpass(thump + scuff, STEP["lowpass"])


def bubble(t, f0, rise, length, delay):
    """Single water bubble: short sine that glides up (Minnaert-like 'plip')."""
    tt = t - delay
    x = np.clip(tt / length, 0, 1)
    f = f0 * rise ** x
    ph = 2 * np.pi * np.cumsum(f * (tt >= 0)) / SR
    return np.sin(ph) * attack_decay(t, 0.002, length * 0.4, delay) * (tt <= length)


def make_shot(v):
    """Thermos throw: airy 'whoosh' of tea + low pour thump + a run of rising bubbles."""
    p = SHOT
    dur, base = v
    t = t_axis(dur)
    ph, _ = sweep_phase(*p["thump_hz"], t, curve=0.35)
    thump = 0.5 * p["thump_level"] * np.sin(ph) * attack_decay(t, 0.004, p["thump_decay"])
    sw = bandpass(noise(len(t)), *p["hiss_band"])
    sw *= p["hiss_level"] / (np.std(sw) + 1e-9) * 0.35 * attack_decay(t, 0.03, p["hiss_decay"] * 1.6)
    bub = np.zeros_like(t)
    for k in range(p["bubbles"]):
        f0 = base * (1 + 0.18 * rng.uniform(-1, 1)) * (1 + 0.12 * k)
        bub += bubble(t, f0, p["bubble_rise"], p["bubble_len"], 0.02 + k * p["bubble_spacing"] * rng.uniform(0.7, 1.3))
    bub = lowpass(bub, p["tone_lowpass"], order=2) * p["bubble_level"]
    return thump + sw + bub


def make_hit():
    p = HIT
    t = t_axis(p["duration"])
    ph, _ = sweep_phase(*p["thud_hz"], t, curve=0.4)
    thud = np.sin(ph) * attack_decay(t, 0.0015, p["thud_decay"])
    slap = bandpass(noise(len(t)), *p["slap_band"])
    slap *= p["slap_level"] / (np.std(slap) + 1e-9) * 0.35 * attack_decay(t, 0.001, p["slap_decay"])
    sparkle = np.zeros_like(t)
    for fz, d in zip(p["sparkle_hz"], p["sparkle_delays"]):
        sparkle += np.sin(2 * np.pi * fz * t) * attack_decay(t, 0.002, p["sparkle_decay"], d)
    return thud + slap + p["sparkle_level"] * sparkle


def make_ult():
    """Warm spring: soft rising major chord (sine pads) + a pool of bubbles + a low warm 'whoomp'."""
    p = ULT
    t = t_axis(p["duration"])
    ct = p["charge_time"]
    x = np.clip(t / ct, 0, 1)
    pad = np.zeros_like(t)
    for ratio in (1.0, 1.25, 1.5, 2.0):
        f = 196.0 * ratio * (1 + 0.004 * np.sin(2 * np.pi * 4.5 * t))
        pad += harmonic_tone(f, [1.0, 0.25, 0.08]) / ratio
    pad_env = (0.15 + 0.85 * x ** 1.2) * np.exp(-np.maximum(t - ct, 0) / 0.45)
    pad = lowpass(pad * pad_env, 1800.0) * 0.45
    boom = np.sin(2 * np.pi * np.cumsum(np.full_like(t, 90.0)) / SR) * attack_decay(t, 0.02, 0.25, ct) * 0.5
    bub = np.zeros_like(t)
    n = 26
    for k in range(n):
        d = ct * 0.4 + (p["duration"] - ct * 0.4 - 0.1) * rng.uniform(0, 1)
        bub += bubble(t, rng.uniform(380, 900), 1.7, rng.uniform(0.03, 0.06), d) * rng.uniform(0.4, 1.0)
    bub = lowpass(bub, 2200.0) * 0.25
    rumble = lowpass(noise(len(t)), 600.0, order=4)
    rumble *= 0.08 / (np.std(rumble) + 1e-9) * np.clip(t / ct, 0, 1) * np.exp(-np.maximum(t - ct, 0) / 0.4)
    return pad + boom + bub + rumble


def make_hurt(v):
    p = HURT
    dur, thud_hz, sq0, sq1 = v
    t = t_axis(dur)
    ph, _ = sweep_phase(thud_hz, thud_hz * p["thud_end_ratio"], t, curve=0.4)
    thud = np.sin(ph) * attack_decay(t, 0.002, p["thud_decay"])
    nz = lowpass(noise(len(t)), p["noise_lowpass"], order=4)
    nz *= p["noise_level"] / (np.std(nz) + 1e-9) * 0.3 * attack_decay(t, 0.001, p["noise_decay"])
    ts = t - p["squeak_delay"]
    x = np.clip(ts / p["squeak_len"], 0, 1)
    fs = sq0 * (sq1 / sq0) ** x
    fs = fs * (1 + p["squeak_vibrato_depth"] * np.sin(2 * np.pi * p["squeak_vibrato_hz"] * t))
    sq_env = np.sin(np.pi * x) ** 2 * ((ts >= 0) & (ts <= p["squeak_len"]))
    squeak = harmonic_tone(fs, [1.0, 0.3, 0.1]) * sq_env * p["squeak_level"]
    return thud + nz + squeak


def formant_gain(freq, formants):
    g = np.ones_like(freq)
    for F, B in zip(formants, FORMANT_BW):
        g *= F * F / np.sqrt((F * F - freq * freq) ** 2 + (B * freq) ** 2)
    return g


def vowel_track(points, t):
    """Formant frequencies over time (len(t) x 4), interpolated between vowels."""
    xs = [p[0] for p in points]
    out = np.zeros((len(t), 4))
    for i in range(4):
        out[:, i] = np.interp(t, xs, [VOWELS[p[1]][i] for p in points])
    return out


def make_voice(spec):
    c = VOICE_COMMON
    dur = spec["duration"]
    t = t_axis(dur)
    f0 = interp_points(spec["f0"], t)
    vib = c["vibrato_cents"] * np.sin(2 * np.pi * c["vibrato_hz"] * t + rng.uniform(0, 2 * np.pi))
    jit = lowpass(noise(len(t)), 30.0) * c["jitter_cents"] * 12.0
    f0 = f0 * 2 ** ((vib + jit) / 1200)
    phase = 2 * np.pi * np.cumsum(f0) / SR
    # formants / harmonic gains on a coarse frame grid, interpolated per sample
    frames = np.arange(0, dur + c["frame_s"], c["frame_s"])
    ft = vowel_track(spec["vowels"], frames)
    f0_fr = np.interp(frames, t, f0)
    kmax = int(c["max_harm_hz"] / 300.0) + 1
    voiced = np.zeros_like(t)
    norm = None
    for k in range(1, kmax + 1):
        fk = k * f0_fr
        g = formant_gain(fk, ft.T)
        g = g / k ** c["tilt"] * (fk < c["max_harm_hz"])
        voiced += np.interp(t, frames, g) * np.sin(k * phase)
    amp = interp_points(spec["amp"], t)
    voiced *= amp
    # breath: noise shaped by the current vowel's F2/F3 region, plus "h" onset
    br = bandpass(noise(len(t)), 1200.0, 4500.0)
    br /= np.std(br) + 1e-9
    v_peak = np.max(np.abs(voiced)) + 1e-9
    breath_env = spec["breath_level"] * np.maximum(amp, 0.0)
    if spec["breath_onset"] > 0:
        h = spec["breath_onset"]
        h_env = np.clip(t / 0.015, 0, 1) * np.clip((h + 0.03 - t) / 0.03, 0, 1)
        breath_env = breath_env + 3.0 * spec["breath_level"] * h_env
    return voiced / v_peak + 0.3 * breath_env * br


# ---------------------------------------------------------------- checks
def centroid(x):
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x)))) ** 2
    freqs = np.fft.rfftfreq(len(x), 1 / SR)
    return float(np.sum(freqs * spec) / np.sum(spec))


def f0_track(x, fmin=120.0, fmax=700.0):
    win = int(0.030 * SR)
    hop = int(0.010 * SR)
    peak = np.max(np.abs(x))
    res = []
    for s in range(0, len(x) - win, hop):
        fr = x[s:s + win]
        if np.max(np.abs(fr)) < 0.3 * peak:
            continue
        fr = fr - fr.mean()
        ac = np.correlate(fr, fr, "full")[win - 1:]
        lo, hi = int(SR / fmax), int(SR / fmin)
        lag = lo + int(np.argmax(ac[lo:hi]))
        if ac[lag] / (ac[0] + 1e-12) > 0.5:
            res.append(SR / lag)
    return np.array(res)


def check(name, x):
    peak = np.max(np.abs(x))
    peak_db = 20 * np.log10(peak)
    thr = peak * 10 ** (-40 / 20)
    lead_ms = int(np.argmax(np.abs(x) > thr)) / SR * 1000
    edge = max(abs(x[0]), abs(x[-1])) / peak
    head = np.max(np.abs(np.diff(x[: int(0.005 * SR)]))) / peak
    tail = np.max(np.abs(np.diff(x[-int(0.005 * SR):]))) / peak
    ok = lead_ms <= CHECKS["lead_silence_ms"] and edge <= CHECKS["edge_click_ratio"] and peak_db <= -0.99
    info = (f"{name:18s} {len(x) / SR * 1000:6.0f} ms  peak {peak_db:5.2f} dBFS  lead {lead_ms:4.1f} ms  "
            f"edge {edge:.3f}  dHead {head:.3f} dTail {tail:.3f}  centroid {centroid(x):6.0f} Hz")
    if name.startswith("nana_attack") and centroid(x) > CHECKS["shot_centroid_max_hz"]:
        ok = False
    if name.startswith("nana_vo"):
        tr = f0_track(x)
        lo, hi = CHECKS["voice_f0_range"]
        tol = CHECKS["voice_f0_tolerance"]
        info += f"  f0 med {np.median(tr):5.0f} min {tr.min():5.0f} max {tr.max():5.0f} (n={len(tr)})"
        if tr.min() < lo - tol or tr.max() > hi + tol:
            ok = False
    print(("OK   " if ok else "FAIL ") + info)
    return ok


# ---------------------------------------------------------------- main
def build_all():
    sounds = []
    for i, v in enumerate(STEP["variants"], 1):
        sounds.append((f"nana_step_{i:02d}", make_step(v)))
    for i, v in enumerate(SHOT["variants"], 1):
        sounds.append((f"nana_attack_{i:02d}", make_shot(v)))
    sounds.append(("nana_hit", make_hit()))
    sounds.append(("nana_ult", make_ult()))
    for i, v in enumerate(HURT["variants"], 1):
        sounds.append((f"nana_hurt_{i:02d}", make_hurt(v)))
    for name in ("nana_vo_effort", "nana_vo_hurt", "nana_vo_win", "nana_vo_lose"):
        sounds.append((name, make_voice(VOICE[name])))
    return [(n, finalize(x)) for n, x in sounds]


def write_wav(path, x):
    wavfile.write(path, SR, np.round(x * 32767).astype(np.int16))


def make_sheet(sounds, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    cols = 4
    rows = (len(sounds) + cols - 1) // cols
    fig, axes = plt.subplots(rows, cols, figsize=(cols * 4.0, rows * 2.6))
    for ax, (name, x) in zip(axes.flat, sounds):
        f, tt, S = signal.spectrogram(x, SR, nperseg=512, noverlap=448)
        ax.pcolormesh(tt * 1000, f / 1000, 10 * np.log10(S + 1e-12), shading="auto",
                      vmin=-110, vmax=-30, cmap="magma")
        ax.set_ylim(0, 8)
        ax.set_title(f"{name}  ({len(x) / SR * 1000:.0f} ms)", fontsize=9)
        ax.set_xlabel("ms", fontsize=7)
        ax.set_ylabel("kHz", fontsize=7)
        ax.tick_params(labelsize=7)
    for ax in list(axes.flat)[len(sounds):]:
        ax.axis("off")
    fig.suptitle("Nana SFX - spectrograms (0-8 kHz)", fontsize=11)
    fig.tight_layout()
    fig.savefig(path, dpi=90)
    plt.close(fig)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    sounds = build_all()
    all_ok = True
    gap = np.zeros(int(PREVIEW_GAP_S * SR))
    preview = []
    for name, x in sounds:
        write_wav(os.path.join(OUT_DIR, name + ".wav"), x)
        # re-read the 16-bit file so the checks see what ships
        _, xi = wavfile.read(os.path.join(OUT_DIR, name + ".wav"))
        all_ok &= check(name, xi.astype(float) / 32768.0)
        preview += [x, gap]
    write_wav(os.path.join(PREVIEW_DIR, "nana_preview.wav"), np.concatenate(preview))
    make_sheet(sounds, os.path.join(PREVIEW_DIR, "nana_sheet.png"))
    print(f"SFX: {len(sounds)} files -> {OUT_DIR}; checks {'OK' if all_ok else 'FAILED'}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
