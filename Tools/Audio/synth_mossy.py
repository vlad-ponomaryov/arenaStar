"""Mossy sound set (big old forest spirit, no mouth: hums like a bee hive; dandelion "Fluff Puff"; ult "Thicket"):
original synthesis (numpy + scipy), no samples.

Writes 16-bit mono WAV files to Assets/_Project/Audio/Fighters/Mossy/, a preview (all sounds in a row) and a
spectrogram sheet to Tools/out/audio/, and prints objective checks (duration, peak, edge clicks, centroid, voice f0).

Deterministic: fixed seed, all tunable parameters in dictionaries below.
Usage:  python Tools/Audio/synth_mossy.py
Brief:  Docs/Briefs/A-Roster3D.md (sounds), base: synth_wren.py / synth_theo.py
"""
import os
import sys

import numpy as np
from scipy import signal
from scipy.io import wavfile

# ---------------------------------------------------------------- parameters
SEED = 20261023
SR = 44100
PEAK_DBFS = -1.0
FADE_IN_MS = 3.0
FADE_OUT_MS = 5.0
PREVIEW_GAP_S = 0.4

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Fighters", "Mossy")
PREVIEW_DIR = os.path.join(ROOT, "Tools", "out", "audio")

# Thump kept above ~100 Hz so it is still audible on phone speakers.

# Steps: Mossy floats - no foot thump, a soft rustle of the coat (willow fronds and moss brushing the grass).
STEP = {
    "variants": [  # (duration s, rustle centre Hz, low brush Hz, level)
        (0.16, 2300.0, 520.0, 1.00),
        (0.17, 2000.0, 480.0, 0.95),
        (0.15, 2600.0, 560.0, 1.00),
        (0.18, 1900.0, 450.0, 0.90),
    ],
    "grain_rate": 140.0,       # leaf "ticks" per second inside the rustle
    "grain_level": 0.5,
    "brush_level": 0.55,
    "lowpass": 5000.0,
}

# Projectile hit: the dandelion puff bursts - soft cottony "pff" + seeds scattering.
HIT = {
    "duration": 0.32,
    "thud_hz": (240.0, 120.0),
    "thud_decay": 0.030,
    "puff_band": (500.0, 2600.0),
    "puff_level": 0.8,
    "puff_decay": 0.060,
    "seed_band": (3000.0, 7000.0),
    "seed_level": 0.18,
    "seed_decay": 0.12,
}

# Hurt: muffled thump into the moss + a twig snap.
HURT = {
    "variants": [  # (duration s, thump Hz, twig crack delay s)
        (0.20, 150.0, 0.030),
        (0.22, 135.0, 0.045),
    ],
    "thump_decay": 0.050,
    "moss_lowpass": 900.0,
    "moss_level": 0.45,
    "twig_band": (1800.0, 5000.0),
    "twig_level": 0.35,
}

# Vowel / nasal formants (Hz) for a big, low, closed-mouth hum (Mossy has no mouth: "Mmmm", "Hmm-m!").
VOWELS = {
    "m": (280.0, 1150.0, 2400.0, 3300.0),
    "n": (300.0, 1500.0, 2600.0, 3400.0),
    "u": (330.0, 800.0, 2300.0, 3200.0),
    "o": (450.0, 850.0, 2500.0, 3300.0),
}
FORMANT_BW = (80.0, 140.0, 220.0, 300.0)

VOICE = {
    "mossy_vo_effort": {  # "Hmm-m!" on ult: breathy onset, firm rise
        "duration": 0.55,
        "f0": [(0.0, 105.0), (0.12, 128.0), (0.40, 122.0), (0.55, 108.0)],
        "vowels": [(0.0, "u"), (0.15, "m"), (0.55, "m")],
        "amp": [(0.0, 0.0), (0.06, 0.0), (0.10, 1.0), (0.40, 0.8), (0.55, 0.0)],
        "breath_onset": 0.08,
        "breath_level": 0.12,
    },
    "mossy_vo_hurt": {  # short "hm!" dropping
        "duration": 0.32,
        "f0": [(0.0, 140.0), (0.08, 146.0), (0.32, 105.0)],
        "vowels": [(0.0, "o"), (0.10, "m"), (0.32, "m")],
        "amp": [(0.0, 0.0), (0.02, 1.0), (0.18, 0.7), (0.32, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.05,
    },
    "mossy_vo_win": {  # content rising-falling "mm-mmm~" (bee-hive purr)
        "duration": 1.00,
        "f0": [(0.0, 112.0), (0.25, 135.0), (0.45, 128.0), (0.75, 150.0), (1.00, 132.0)],
        "vowels": [(0.0, "m"), (0.5, "n"), (1.0, "m")],
        "amp": [(0.0, 0.0), (0.02, 0.8), (0.30, 1.0), (0.42, 0.6), (0.55, 1.0), (0.85, 0.8), (1.00, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.04,
    },
    "mossy_vo_lose": {  # slow sad sinking "mmmm..."
        "duration": 1.05,
        "f0": [(0.0, 128.0), (0.40, 112.0), (1.05, 88.0)],
        "vowels": [(0.0, "m"), (1.05, "u")],
        "amp": [(0.0, 0.0), (0.06, 0.9), (0.60, 0.7), (1.05, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.04,
    },
}
VOICE_COMMON = {
    "vibrato_hz": 4.5,
    "vibrato_cents": 14.0,
    "jitter_cents": 5.0,
    "tilt": 0.85,             # rich harmonics so the low hum still reads on phone speakers
    "max_harm_hz": 4500.0,
    "frame_s": 0.002,
    "buzz_hz": 180.0,         # bee-hive buzz: amplitude flutter at this rate
    "buzz_depth": 0.18,
}

# Attack "Fluff Puff": a soft blow "ffhu" + airy whoosh of the lobbed puff + a light cottony pop.
ATTACK = {
    "variants": [  # (duration s, blow centre Hz, whoosh centre Hz, pop Hz)
        (0.42, 900.0, 1500.0, 330.0),
        (0.44, 820.0, 1350.0, 300.0),
        (0.40, 980.0, 1650.0, 360.0),
    ],
    "blow_len": 0.12, "blow_level": 0.8,
    "whoosh_start": 0.08, "whoosh_len": 0.26, "whoosh_level": 0.55,
    "pop_at": 0.05, "pop_decay": 0.025, "pop_level": 0.6,
}
# Ult "Thicket": deep hum swell + rustling growth rising in pitch + wooden creaks + bushes "pop up" + robin chirps.
ULT_M = {
    "duration": 1.60,
    "hum_hz": (70.0, 98.0), "hum_level": 0.55,
    "grow_from": 0.10, "grow_to": 1.05, "grow_band": ((600.0, 1500.0), (1800.0, 5200.0)), "grow_level": 0.6,
    "creaks": [0.35, 0.62, 0.85], "creak_hz": 210.0,
    "burst_at": 1.05, "burst_hz": (140.0, 60.0), "burst_decay": 0.14,
    "chirps": [1.20, 1.31, 1.40], "chirp_hz": (3600.0, 4700.0), "chirp_len": 0.06, "chirp_level": 0.22,
}

CHECKS = {
    "attack_centroid_max_hz": 3000.0,
    "voice_f0_range": (85.0, 155.0),
    "voice_f0_tolerance": 10.0,
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
def norm_noise(x):
    return x / (np.std(x) + 1e-9)


def make_step(v):
    p = STEP
    dur, rc, bc, lvl = v
    t = t_axis(dur)
    env = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 1.5
    rustle = norm_noise(bandpass(noise(len(t)), rc * 0.6, rc * 1.8)) * 0.3
    ticks = (rng.random(len(t)) < p["grain_rate"] / SR).astype(float)
    ticks = bandpass(np.convolve(ticks, np.exp(-np.arange(int(0.004 * SR)) / (0.0008 * SR)))[:len(t)], 1500.0, 6000.0)
    ticks = norm_noise(ticks) * 0.3 * p["grain_level"]
    brush = norm_noise(bandpass(noise(len(t)), bc * 0.6, bc * 1.6)) * 0.3 * p["brush_level"]
    return lowpass(lvl * (rustle + ticks + brush) * env, p["lowpass"])


def make_attack(v):
    p = ATTACK
    dur, bc, wc, pf = v
    t = t_axis(dur)
    xb = np.clip(t / p["blow_len"], 0, 1)
    blow = norm_noise(bandpass(noise(len(t)), bc * 0.5, bc * 1.8)) * 0.3 * p["blow_level"] * np.sin(np.pi * xb) ** 1.2
    xw = np.clip((t - p["whoosh_start"]) / p["whoosh_len"], 0, 1)
    fc = wc * (1.3 - 0.5 * xw)                       # whoosh slides down as the puff flies away
    w = np.zeros_like(t)
    for k in range(4):                                 # piecewise band so the whoosh glides
        sl = (xw >= k / 4) & (xw <= (k + 1) / 4 + 1e-9)
        band = norm_noise(bandpass(noise(len(t)), float(np.mean(fc[sl])) * 0.6, float(np.mean(fc[sl])) * 1.6))
        w += band * sl
    w *= 0.3 * p["whoosh_level"] * np.sin(np.pi * xw) ** 2
    ph, _ = sweep_phase(pf, pf * 0.6, t, 0.5)
    pop = p["pop_level"] * np.sin(ph) * attack_decay(t, 0.002, p["pop_decay"], p["pop_at"])
    return blow + w + pop


def make_hit():
    p = HIT
    t = t_axis(p["duration"])
    ph, _ = sweep_phase(*p["thud_hz"], t, curve=0.4)
    thud = 0.7 * np.sin(ph) * attack_decay(t, 0.002, p["thud_decay"])
    puff = norm_noise(bandpass(noise(len(t)), *p["puff_band"])) * 0.3 * p["puff_level"] * attack_decay(t, 0.004, p["puff_decay"])
    seeds = norm_noise(bandpass(noise(len(t)), *p["seed_band"]))
    seeds *= 0.3 * p["seed_level"] * attack_decay(t, 0.02, p["seed_decay"], 0.01) * (0.6 + 0.4 * np.sin(2 * np.pi * 23 * t))
    return thud + puff + seeds


def make_hurt(v):
    p = HURT
    dur, hz, cd = v
    t = t_axis(dur)
    ph, _ = sweep_phase(hz, hz * 0.5, t, curve=0.4)
    thump = np.sin(ph) * attack_decay(t, 0.002, p["thump_decay"])
    moss = norm_noise(lowpass(noise(len(t)), p["moss_lowpass"], order=4)) * 0.3 * p["moss_level"] * attack_decay(t, 0.002, 0.05)
    twig = np.zeros_like(t)
    for k, d in enumerate((cd, cd + 0.009, cd + 0.016)):
        twig += (1.0 - 0.3 * k) * attack_decay(t, 0.0003, 0.004, d)
    twig = norm_noise(bandpass(noise(len(t)) * twig, *p["twig_band"])) * 0.3 * p["twig_level"] * (t >= cd)
    return thump + moss + twig


def formant_gain(freq, formants):
    g = np.ones_like(freq)
    for F, B in zip(formants, FORMANT_BW):
        g *= F * F / np.sqrt((F * F - freq * freq) ** 2 + (B * freq) ** 2)
    return g


def vowel_track(points, t):
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
    frames = np.arange(0, dur + c["frame_s"], c["frame_s"])
    ft = vowel_track(spec["vowels"], frames)
    f0_fr = np.interp(frames, t, f0)
    kmax = int(c["max_harm_hz"] / 85.0) + 1
    voiced = np.zeros_like(t)
    for k in range(1, kmax + 1):
        fk = k * f0_fr
        g = formant_gain(fk, ft.T)
        g = g / k ** c["tilt"] * (fk < c["max_harm_hz"])
        voiced += np.interp(t, frames, g) * np.sin(k * phase)
    amp = interp_points(spec["amp"], t)
    voiced *= amp * (1 - c["buzz_depth"] * (0.5 + 0.5 * np.sin(2 * np.pi * c["buzz_hz"] * t)))
    br = norm_noise(bandpass(noise(len(t)), 300.0, 2500.0))
    v_peak = np.max(np.abs(voiced)) + 1e-9
    breath_env = spec["breath_level"] * np.maximum(amp, 0.0)
    if spec["breath_onset"] > 0:
        h = spec["breath_onset"]
        h_env = np.clip(t / 0.015, 0, 1) * np.clip((h + 0.03 - t) / 0.03, 0, 1)
        breath_env = breath_env + 3.0 * spec["breath_level"] * h_env
    return voiced / v_peak + 0.3 * breath_env * br


def chirp(t, at, f0, f1, length):
    x = np.clip((t - at) / length, 0, 1)
    on = (t >= at) & (t <= at + length)
    f = f0 + (f1 - f0) * np.sin(np.pi * x)
    return np.sin(2 * np.pi * np.cumsum(f * on) / SR) * np.sin(np.pi * x) ** 2 * on


def make_ult_mossy():
    p = ULT_M
    t = t_axis(p["duration"])
    x = np.clip(t / p["burst_at"], 0, 1)
    fh = p["hum_hz"][0] + (p["hum_hz"][1] - p["hum_hz"][0]) * x
    hum = harmonic_tone(fh, [1.0, 0.6, 0.4, 0.25, 0.15, 0.1]) * (0.3 + 0.7 * x) * np.clip((p["duration"] - t) / 0.5, 0, 1)
    hum *= p["hum_level"] * (1 - 0.2 * (0.5 + 0.5 * np.sin(2 * np.pi * 150 * t)))
    xg = np.clip((t - p["grow_from"]) / (p["grow_to"] - p["grow_from"]), 0, 1)
    grow = np.zeros_like(t)
    (a0, a1), (b0, b1) = p["grow_band"]
    for k in range(5):                                # rising rustle in five overlapping bands
        lo, hi = a0 + (b0 - a0) * k / 4, a1 + (b1 - a1) * k / 4
        win = np.clip(1 - np.abs(xg * 4 - k), 0, 1) * (t >= p["grow_from"])
        grow += norm_noise(bandpass(noise(len(t)), lo, hi)) * win
    grow *= 0.3 * p["grow_level"] * (0.4 + 0.6 * xg) * (t <= p["burst_at"] + 0.08)
    cr = np.zeros_like(t)
    for at in p["creaks"]:
        xc = np.clip((t - at) / 0.12, 0, 1)
        pulses = (0.5 + 0.5 * np.sin(2 * np.pi * np.cumsum(np.full(len(t), 45.0)) / SR)) ** 6
        cr += bandpass(harmonic_tone(np.full(len(t), p["creak_hz"]) * (1 + 0.1 * xc), [1, 0.6, 0.4, 0.2]) * pulses,
                       150.0, 2000.0) * np.sin(np.pi * xc) * ((t >= at) & (t <= at + 0.12))
    tb = np.maximum(t - p["burst_at"], 0)
    after = (t >= p["burst_at"]).astype(float)
    fb = p["burst_hz"][0] * (p["burst_hz"][1] / p["burst_hz"][0]) ** np.clip(tb / 0.4, 0, 1) ** 0.5
    burst = np.sin(2 * np.pi * np.cumsum(fb * after) / SR) * attack_decay(t, 0.003, p["burst_decay"], p["burst_at"])
    burst += norm_noise(bandpass(noise(len(t)), 800.0, 4000.0)) * 0.3 * 0.6 * attack_decay(t, 0.002, 0.10, p["burst_at"])
    ch = sum(chirp(t, at, *p["chirp_hz"], p["chirp_len"]) for at in p["chirps"]) * p["chirp_level"]
    return hum + grow + 0.5 * cr + burst + ch


# ---------------------------------------------------------------- checks
def centroid(x):
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x)))) ** 2
    freqs = np.fft.rfftfreq(len(x), 1 / SR)
    return float(np.sum(freqs * spec) / np.sum(spec))


def f0_track(x, fmin=70.0, fmax=300.0):
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
    if name.startswith("mossy_attack") and centroid(x) > CHECKS["attack_centroid_max_hz"]:
        ok = False
    if name.startswith("mossy_vo"):
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
        sounds.append((f"mossy_step_{i:02d}", make_step(v)))
    for i, v in enumerate(ATTACK["variants"], 1):
        sounds.append((f"mossy_attack_{i:02d}", make_attack(v)))
    sounds.append(("mossy_hit", make_hit()))
    sounds.append(("mossy_ult", make_ult_mossy()))
    for i, v in enumerate(HURT["variants"], 1):
        sounds.append((f"mossy_hurt_{i:02d}", make_hurt(v)))
    for name in ("mossy_vo_effort", "mossy_vo_hurt", "mossy_vo_win", "mossy_vo_lose"):
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
    fig.suptitle("Mossy SFX - spectrograms (0-8 kHz)", fontsize=11)
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
    write_wav(os.path.join(PREVIEW_DIR, "mossy_preview.wav"), np.concatenate(preview))
    make_sheet(sounds, os.path.join(PREVIEW_DIR, "mossy_sheet.png"))
    print(f"SFX: {len(sounds)} files -> {OUT_DIR}; checks {'OK' if all_ok else 'FAILED'}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
