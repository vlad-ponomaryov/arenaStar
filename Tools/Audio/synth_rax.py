"""Rax sound set: original synthesis (numpy + scipy), no samples.

Writes 16-bit mono WAV files to Assets/_Project/Audio/Fighters/Rax/, a preview
(all sounds in a row) and a spectrogram sheet to Tools/out/audio/, and prints
objective checks (duration, peak, edge clicks, spectral centroid, voice f0).

Deterministic: fixed seed, all tunable parameters in dictionaries below.
Usage:  python Tools/Audio/synth_rax.py
Brief:  Docs/Briefs/A-Roster3D.md (sounds, wave 2). Copy of synth_lark.py retuned for Rax
        (raccoon trickster): soft quick paw steps, acorn "flick" (finger snap + short whistle), woody
        "tok" hit, "Swap" ult (warble swoosh + shimmer + pop), cheeky chittering creature voice.
"""
import os
import sys

import numpy as np
from scipy import signal
from scipy.io import wavfile

# ---------------------------------------------------------------- parameters
SEED = 20261123
SR = 44100
PEAK_DBFS = -1.0
FADE_IN_MS = 3.0
FADE_OUT_MS = 5.0
PREVIEW_GAP_S = 0.4

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Fighters", "Rax")
PREVIEW_DIR = os.path.join(ROOT, "Tools", "out", "audio")

# Footsteps: rubber-sole "thump" + a little scuff noise.
# Thump kept above ~100 Hz so it is still audible on phone speakers.
STEP = {
    "variants": [  # (duration s, thump start Hz, thump end Hz, scuff level, scuff centre Hz)
        (0.055, 330.0, 180.0, 0.35, 2600.0),
        (0.060, 310.0, 170.0, 0.30, 2900.0),
        (0.058, 350.0, 190.0, 0.38, 2400.0),
        (0.065, 300.0, 160.0, 0.33, 2700.0),
    ],
    "thump_decay": 0.016,
    "scuff_decay": 0.018,
    "scuff_delay": 0.004,
    "lowpass": 3000.0,
}

# Blaster shot: low thump + soft harmonic glide (up, then down) + small hiss.
SHOT = {
    "variants": [  # (duration s, glide base Hz)
        (0.170, 900.0),
        (0.180, 820.0),
        (0.160, 980.0),
    ],
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
    "thud_hz": (420.0, 260.0),
    "thud_decay": 0.030,
    "slap_band": (250.0, 1400.0),
    "slap_level": 0.55,
    "slap_decay": 0.018,
    "sparkle_hz": [1150.0, 1730.0, 2600.0],
    "sparkle_delays": [0.010, 0.022, 0.034],
    "sparkle_level": 0.45,
    "sparkle_decay": 0.030,
}

# Ultimate: 0.3 s charge + big discharge.
ULT = {
    "duration": 1.20,
    "charge_time": 0.55,
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
        (0.160, 260.0, 1500.0, 2100.0),
        (0.180, 230.0, 1800.0, 1300.0),
    ],
    "thud_end_ratio": 0.5,
    "thud_decay": 0.040,
    "noise_lowpass": 700.0,
    "noise_level": 0.35,
    "noise_decay": 0.030,
    "squeak_delay": 0.025,
    "squeak_len": 0.070,
    "squeak_level": 0.30,
    "squeak_vibrato_hz": 45.0,
    "squeak_vibrato_depth": 0.03,
}

# Small cartoon creature (raccoon) formants (Hz): F1, F2, F3, F4 - short tract, bright.
VOWELS = {
    "a": (1000.0, 1750.0, 3600.0, 4600.0),
    "e": (650.0, 2450.0, 3700.0, 4700.0),
    "i": (420.0, 3000.0, 3900.0, 4800.0),
    "o": (620.0, 1100.0, 3400.0, 4500.0),
    "u": (460.0, 1000.0, 3300.0, 4400.0),
}
FORMANT_BW = (100.0, 140.0, 230.0, 300.0)

# Wordless voice: f0 contour points (t s, Hz), vowel path (t s, vowel),
# amplitude points (t s, gain) - many points = chitter, breath onset ("h") length, breath level.
VOICE = {
    "rax_vo_effort": {  # sneaky "hup!"
        "duration": 0.24,
        "f0": [(0.0, 400.0), (0.06, 470.0), (0.24, 380.0)],
        "vowels": [(0.0, "u"), (0.24, "a")],
        "amp": [(0.0, 0.0), (0.04, 0.0), (0.06, 1.0), (0.15, 0.8), (0.24, 0.0)],
        "breath_onset": 0.05,
        "breath_level": 0.10,
    },
    "rax_vo_hurt": {  # yelp "yip"
        "duration": 0.26,
        "f0": [(0.0, 520.0), (0.05, 540.0), (0.26, 380.0)],
        "vowels": [(0.0, "i"), (0.26, "e")],
        "amp": [(0.0, 0.0), (0.015, 1.0), (0.12, 0.7), (0.26, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.05,
    },
    "rax_vo_win": {  # cheeky chitter-giggle "hehehe!"
        "duration": 0.80,
        "f0": [(0.0, 380.0), (0.30, 440.0), (0.60, 500.0), (0.80, 470.0)],
        "vowels": [(0.0, "e"), (0.80, "e")],
        "amp": [(0.0, 0.0), (0.02, 1.0), (0.08, 0.15), (0.12, 1.0), (0.18, 0.15), (0.22, 1.0), (0.28, 0.15),
                (0.32, 1.0), (0.38, 0.15), (0.42, 1.0), (0.50, 0.2), (0.54, 1.0), (0.80, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.08,
    },
    "rax_vo_lose": {  # whiny "aw-w..." falling ("Wasn't me...")
        "duration": 0.85,
        "f0": [(0.0, 470.0), (0.30, 420.0), (0.38, 450.0), (0.85, 320.0)],
        "vowels": [(0.0, "a"), (0.40, "o"), (0.85, "u")],
        "amp": [(0.0, 0.0), (0.04, 0.9), (0.28, 0.75), (0.34, 0.45), (0.42, 0.85), (0.85, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.05,
    },
}
VOICE_COMMON = {
    "vibrato_hz": 7.0,
    "vibrato_cents": 18.0,
    "jitter_cents": 6.0,
    "tilt": 1.0,              # harmonic amplitude ~ 1/k^tilt
    "max_harm_hz": 6000.0,
    "frame_s": 0.002,
}

CHECKS = {
    "shot_centroid_max_hz": 3000.0,
    "voice_f0_range": (300.0, 560.0),
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


def make_shot(v):
    """Acorn flick: finger-snap click (short bright noise) + quick whistle glide (spinning acorn) + tiny low thock."""
    p = SHOT
    dur, base = v
    t = t_axis(dur)
    click = highpass(noise(len(t)), 1500.0)
    click *= 0.9 / (np.std(click) + 1e-9) * 0.3 * attack_decay(t, 0.0008, 0.008)
    ph, _ = sweep_phase(260.0, 180.0, t, curve=0.4)
    thock = 0.35 * np.sin(ph) * attack_decay(t, 0.001, 0.02)
    f = base * (1.0 + 0.6 * np.clip(t / 0.03, 0, 1)) * (0.8 ** np.clip((t - 0.03) / (dur - 0.03), 0, 1))
    f = f * (1 + 0.05 * np.sin(2 * np.pi * 34.0 * t))  # wobble of the spinning acorn
    tone = harmonic_tone(f, [1.0, 0.2]) * 0.4 * attack_decay(t, 0.008, 0.05, 0.01)
    return click + thock + lowpass(tone, 2600.0)


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
    """Swap: warbling up-sweep swoosh, a twinkling shimmer, a 'blip' at the swap, then a soft pop + falling glide."""
    p = ULT
    t = t_axis(p["duration"])
    ct = p["charge_time"]
    x = np.clip(t / ct, 0, 1)
    f = 260.0 * (4.0 ** (x ** 1.3)) * (1 + 0.08 * np.sin(2 * np.pi * (8 + 20 * x) * t))
    warble = harmonic_tone(f, [1.0, 0.35, 0.12]) * (0.1 + 0.9 * x) * (t < ct)
    warble = lowpass(warble, 3000.0) * 0.5
    sw = bandpass(noise(len(t)), 700.0, 3200.0)
    sw *= 0.35 / (np.std(sw) + 1e-9) * 0.3 * np.sin(np.pi * x) ** 2 * (t < ct)
    shim = np.zeros_like(t)
    for k in range(10):
        d = rng.uniform(0.05, p["duration"] - 0.2)
        shim += np.sin(2 * np.pi * rng.uniform(1600, 3200) * t) * attack_decay(t, 0.002, 0.05, d)
    shim *= 0.08
    ph, _ = sweep_phase(520.0, 140.0, np.maximum(t - ct, 0) + 1e-6, curve=0.6)
    pop = np.sin(ph) * attack_decay(t, 0.002, 0.12, ct) * 0.6
    pz = bandpass(noise(len(t)), 300.0, 2000.0)
    pz *= 0.5 / (np.std(pz) + 1e-9) * 0.3 * attack_decay(t, 0.001, 0.03, ct)
    return warble + sw + shim + pop + pz


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


def f0_track(x, fmin=200.0, fmax=800.0):
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
    if name.startswith("rax_attack") and centroid(x) > CHECKS["shot_centroid_max_hz"]:
        ok = False
    if name.startswith("rax_vo"):
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
        sounds.append((f"rax_step_{i:02d}", make_step(v)))
    for i, v in enumerate(SHOT["variants"], 1):
        sounds.append((f"rax_attack_{i:02d}", make_shot(v)))
    sounds.append(("rax_hit", make_hit()))
    sounds.append(("rax_ult", make_ult()))
    for i, v in enumerate(HURT["variants"], 1):
        sounds.append((f"rax_hurt_{i:02d}", make_hurt(v)))
    for name in ("rax_vo_effort", "rax_vo_hurt", "rax_vo_win", "rax_vo_lose"):
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
    fig.suptitle("Rax SFX - spectrograms (0-8 kHz)", fontsize=11)
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
    write_wav(os.path.join(PREVIEW_DIR, "rax_preview.wav"), np.concatenate(preview))
    make_sheet(sounds, os.path.join(PREVIEW_DIR, "rax_sheet.png"))
    print(f"SFX: {len(sounds)} files -> {OUT_DIR}; checks {'OK' if all_ok else 'FAILED'}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
