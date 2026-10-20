"""Nox sound set: original synthesis (numpy + scipy), no samples.

Writes 16-bit mono WAV files to Assets/_Project/Audio/Fighters/Nox/, a preview
(all sounds in a row) and a spectrogram sheet to Tools/out/audio/, and prints
objective checks (duration, peak, edge clicks, spectral centroid, voice f0).

Deterministic: fixed seed, all tunable parameters in dictionaries below.
Usage:  python Tools/Audio/synth_nox.py
Brief:  Docs/Briefs/A-Roster3D.md (sounds), Docs/Roster-v4.md 6.3. Generated from synth_nox.py and retuned for Nox:
        muffled wool-sock steps, fan of three soft felt-star whooshes, flour-bomb "poof" + tiny "chih!" ult,
        quiet breathy 13-year-old boy voice (no words).
"""
import os
import sys

import numpy as np
from scipy import signal
from scipy.io import wavfile

# ---------------------------------------------------------------- parameters
SEED = 20261119
SR = 44100
PEAK_DBFS = -1.0
FADE_IN_MS = 3.0
FADE_OUT_MS = 5.0
PREVIEW_GAP_S = 0.4

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Fighters", "Nox")
PREVIEW_DIR = os.path.join(ROOT, "Tools", "out", "audio")

# Footsteps: rubber-sole "thump" + a little scuff noise.
# Thump kept above ~100 Hz so it is still audible on phone speakers.
# Wool socks over sneakers: very soft, muffled thump + woolly brush.
STEP = {
    "variants": [  # (duration s, thump start Hz, thump end Hz, scuff level, scuff centre Hz)
        (0.080, 210.0, 115.0, 0.30, 1100.0),
        (0.090, 195.0, 108.0, 0.26, 1300.0),
        (0.085, 225.0, 120.0, 0.32, 1000.0),
        (0.095, 185.0, 104.0, 0.28, 1200.0),
    ],
    "thump_decay": 0.020,
    "scuff_decay": 0.026,
    "scuff_delay": 0.006,
    "lowpass": 1500.0,
}

# Blaster shot: low thump + soft harmonic glide (up, then down) + small hiss.
# Felt-star fan: three quick soft whooshes ("ffp-ffp-ffp"), each a rising band-noise swish with a felt flutter.
SHOT = {
    "variants": [  # (duration s, swish centre Hz)
        (0.240, 1100.0),
        (0.230, 1000.0),
        (0.250, 1200.0),
    ],
    "count": 3,
    "spacing": 0.045,
    "swish_attack": 0.012,
    "swish_decay": 0.045,
    "swish_rise": 1.6,
    "swish_q": 0.55,
    "level": 0.9,
    "flutter_hz": 55.0,
    "flutter_depth": 0.35,
    "puff_lowpass": 500.0,
    "puff_level": 0.25,
}

# Projectile hit: dense soft "slap" + a short sparkle.
HIT = {
    "duration": 0.150,
    "thud_hz": (320.0, 140.0),
    "thud_decay": 0.030,
    "slap_band": (250.0, 1400.0),
    "slap_level": 0.55,
    "slap_decay": 0.018,
    "sparkle_hz": [2350.0, 3100.0, 3900.0],
    "sparkle_delays": [0.010, 0.022, 0.034],
    "sparkle_level": 0.10,
    "sparkle_decay": 0.030,
}

# Ultimate: 0.3 s charge + big discharge.
# Ultimate "Flour Bomb": paper-bag pop, big soft lowpassed "poof" cloud, then a tiny muffled "chih!" (sneeze-like noise).
ULT = {
    "duration": 1.30,
    "pop_hz": (260.0, 90.0),
    "pop_decay": 0.040,
    "pop_level": 0.55,
    "poof_lowpass": (2400.0, 500.0),
    "poof_attack": 0.015,
    "poof_decay": 0.40,
    "poof_level": 0.9,
    "dust_band": (2500.0, 6000.0),
    "dust_level": 0.10,
    "dust_decay": 0.35,
    "chih_time": 0.75,
    "chih_band": (2200.0, 5200.0),
    "chih_attack": 0.02,
    "chih_decay": 0.06,
    "chih_level": 0.30,
}

# Hurt: dull thud + short armour squeak (no voice).
HURT = {
    "variants": [  # (duration s, thud start Hz, squeak start Hz, squeak end Hz)
        (0.160, 130.0, 1150.0, 1550.0),
        (0.180, 115.0, 1350.0, 1050.0),
    ],
    "thud_end_ratio": 0.5,
    "thud_decay": 0.040,
    "noise_lowpass": 700.0,
    "noise_level": 0.35,
    "noise_decay": 0.030,
    "squeak_delay": 0.025,
    "squeak_len": 0.070,
    "squeak_level": 0.16,
    "squeak_vibrato_hz": 45.0,
    "squeak_vibrato_depth": 0.03,
}

# Teen-girl vowel formants (slightly lower than Theo's child set) (Hz): F1, F2, F3, F4.
# Young-boy vowel formants (Hz): F1, F2, F3, F4.
VOWELS = {
    "a": (850.0, 1450.0, 3100.0, 4100.0),
    "e": (570.0, 2150.0, 3200.0, 4200.0),
    "i": (360.0, 2700.0, 3450.0, 4300.0),
    "o": (540.0, 950.0, 2950.0, 4000.0),
    "u": (400.0, 900.0, 2850.0, 3900.0),
}
FORMANT_BW = (90.0, 130.0, 220.0, 300.0)

# Wordless voice: f0 contour points (t s, Hz), vowel path (t s, vowel),
# amplitude points (t s, gain), breath onset ("h") length, breath level.
# Nox talks in a whisper: more breath, softer contours.
VOICE = {
    "nox_vo_effort": {  # short determined "hh-ha"
        "duration": 0.30,
        "f0": [(0.0, 250.0), (0.08, 265.0), (0.30, 235.0)],
        "vowels": [(0.0, "a"), (0.30, "a")],
        "amp": [(0.0, 0.0), (0.07, 0.0), (0.10, 0.9), (0.18, 0.7), (0.30, 0.0)],
        "breath_onset": 0.09,
        "breath_level": 0.30,
    },
    "nox_vo_hurt": {  # held-in "mf!"
        "duration": 0.28,
        "f0": [(0.0, 275.0), (0.08, 280.0), (0.28, 230.0)],
        "vowels": [(0.0, "u"), (0.28, "o")],
        "amp": [(0.0, 0.0), (0.02, 1.0), (0.16, 0.7), (0.28, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.18,
    },
    "nox_vo_win": {  # quiet pleased "mm-hm!"
        "duration": 0.70,
        "f0": [(0.0, 240.0), (0.20, 255.0), (0.30, 230.0), (0.45, 270.0), (0.70, 285.0)],
        "vowels": [(0.0, "u"), (0.30, "u"), (0.40, "a"), (0.70, "a")],
        "amp": [(0.0, 0.0), (0.03, 0.8), (0.22, 0.7), (0.30, 0.2), (0.36, 0.9), (0.58, 0.8), (0.70, 0.0)],
        "breath_onset": 0.0,
        "breath_level": 0.15,
    },
    "nox_vo_lose": {  # soft sigh "ha-a..."
        "duration": 0.85,
        "f0": [(0.0, 260.0), (0.30, 245.0), (0.85, 205.0)],
        "vowels": [(0.0, "a"), (0.50, "o"), (0.85, "u")],
        "amp": [(0.0, 0.0), (0.06, 0.8), (0.40, 0.65), (0.85, 0.0)],
        "breath_onset": 0.05,
        "breath_level": 0.25,
    },
}
VOICE_COMMON = {
    "vibrato_hz": 5.5,
    "vibrato_cents": 18.0,
    "jitter_cents": 6.0,
    "tilt": 1.0,              # harmonic amplitude ~ 1/k^tilt
    "max_harm_hz": 6000.0,
    "frame_s": 0.002,
}

CHECKS = {
    "shot_centroid_max_hz": 2500.0,
    "voice_f0_range": (200.0, 290.0),
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
    """Dart launcher: air puff (band noise) + rubbery rising "thwip" + small low pop."""
    p = SHOT
    dur, base = v
    t = t_axis(dur)
    ph, _ = sweep_phase(*p["thump_hz"], t, curve=0.35)
    thump = p["thump_level"] * np.sin(ph) * attack_decay(t, 0.002, p["thump_decay"])
    up_t = p["glide_up_time"]
    f = np.where(t < up_t, base * (p["glide_up"] ** (t / up_t)),
                 base * p["glide_up"] * ((p["glide_down"] / p["glide_up"]) ** ((t - up_t) / (dur - up_t))))
    tone = harmonic_tone(f, p["harmonics"])
    tone = lowpass(tone, p["tone_lowpass"], order=4)
    tone *= p["tone_level"] * attack_decay(t, p["tone_attack"], p["tone_decay"], 0.012)
    puff = bandpass(noise(len(t)), *p["hiss_band"])
    puff *= p["hiss_level"] / (np.std(puff) + 1e-9) * 0.3 * attack_decay(t, 0.003, p["hiss_decay"])
    return thump + tone + puff


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
    p = ULT
    t = t_axis(p["duration"])
    ct = p["charge_time"]
    # charge: rising harmonic tone with accelerating tremolo + rising noise
    x = np.clip(t / ct, 0, 1)
    f = p["charge_hz"][0] * (p["charge_hz"][1] / p["charge_hz"][0]) ** (x ** 1.4)
    trem_rate = p["charge_tremolo_hz"][0] + (p["charge_tremolo_hz"][1] - p["charge_tremolo_hz"][0]) * x
    trem = 0.75 + 0.25 * np.sin(2 * np.pi * np.cumsum(trem_rate) / SR)
    charge_env = (0.12 + 0.88 * x ** 1.6) * (t < ct + 0.01)
    charge_env = charge_env * np.clip((ct + 0.01 - t) / 0.01, 0, 1)
    charge = p["charge_level"] * harmonic_tone(f, p["charge_harmonics"]) * trem * charge_env
    cn = bandpass(noise(len(t)), *p["charge_noise_band"])
    charge += p["charge_noise_level"] * cn / (np.std(cn) + 1e-9) * 0.3 * charge_env
    # discharge
    tb = np.maximum(t - ct, 0.0)
    after = (t >= ct).astype(float)
    fb = p["boom_hz"][0] * (p["boom_hz"][1] / p["boom_hz"][0]) ** np.clip(tb / 0.5, 0, 1) ** 0.5
    boom = np.sin(2 * np.pi * np.cumsum(fb * after) / SR) * attack_decay(t, 0.003, p["boom_decay"], ct)
    fbl = p["blast_hz"][0] * (p["blast_hz"][1] / p["blast_hz"][0]) ** np.clip(tb / 0.6, 0, 1)
    blast = harmonic_tone(fbl * after + 1e-3, p["blast_harmonics"])
    blast += 0.5 * harmonic_tone((fbl * 1.01) * after + 1e-3, p["blast_harmonics"])
    blast = lowpass(blast, p["blast_lowpass"], order=4) * attack_decay(t, 0.008, p["blast_decay"], ct)
    rumble = lowpass(noise(len(t)), p["rumble_lowpass"], order=4)
    rumble *= p["rumble_level"] / (np.std(rumble) + 1e-9) * 0.3 * attack_decay(t, 0.004, p["rumble_decay"], ct)
    return charge + p["boom_level"] * boom + p["blast_level"] * blast + rumble


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


def f0_track(x, fmin=150.0, fmax=700.0):
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
    if name.startswith("nox_attack") and centroid(x) > CHECKS["shot_centroid_max_hz"]:
        ok = False
    if name.startswith("nox_vo"):
        tr = f0_track(x)
        lo, hi = CHECKS["voice_f0_range"]
        tol = CHECKS["voice_f0_tolerance"]
        info += f"  f0 med {np.median(tr):5.0f} min {tr.min():5.0f} max {tr.max():5.0f} (n={len(tr)})"
        if tr.min() < lo - tol or tr.max() > hi + tol:
            ok = False
    print(("OK   " if ok else "FAIL ") + info)
    return ok



def make_shot(v):
    """Three staggered soft swishes (band noise with a rising centre) + a faint low puff of air."""
    p = SHOT
    dur, fc = v
    t = t_axis(dur)
    out = np.zeros_like(t)
    for i in range(p["count"]):
        d = i * p["spacing"]
        lo_n = bandpass(noise(len(t)), fc * (1 - p["swish_q"]), fc * (1 + p["swish_q"]))
        hi_n = bandpass(noise(len(t)), fc * p["swish_rise"] * (1 - p["swish_q"]), min(fc * p["swish_rise"] * (1 + p["swish_q"]), 9000.0))
        x = np.clip((t - d) / (p["swish_attack"] + p["swish_decay"]), 0, 1)
        sw = lo_n * (1 - x) + hi_n * x
        sw /= np.std(sw) + 1e-9
        fl = 1.0 - p["flutter_depth"] * 0.5 * (1 + np.sin(2 * np.pi * p["flutter_hz"] * t))
        out += (1.0 - 0.15 * i) * 0.3 * sw * fl * attack_decay(t, p["swish_attack"], p["swish_decay"], d)
    puff = lowpass(noise(len(t)), p["puff_lowpass"], order=4)
    puff *= p["puff_level"] / (np.std(puff) + 1e-9) * 0.3 * attack_decay(t, 0.004, 0.03)
    return p["level"] * out + puff


def make_ult():
    """Flour bomb: pop + big soft poof whose lowpass closes over time + fine dust hiss + tiny "chih!"."""
    p = ULT
    t = t_axis(p["duration"])
    ph, _ = sweep_phase(*p["pop_hz"], t, curve=0.4)
    pop = p["pop_level"] * np.sin(ph) * attack_decay(t, 0.002, p["pop_decay"])
    n = noise(len(t))
    seg = int(0.05 * SR)
    poof = np.zeros_like(t)
    fcs = np.geomspace(p["poof_lowpass"][0], p["poof_lowpass"][1], (len(t) + seg - 1) // seg)
    full = [lowpass(n, fc, order=2) for fc in fcs]
    for k, fc in enumerate(fcs):  # piecewise lowpass, crossfaded by a raised-cosine window per segment
        w = np.zeros_like(t)
        c = k * seg
        idx = np.arange(max(0, c - seg), min(len(t), c + seg))
        w[idx] = 0.5 * (1 + np.cos(np.pi * (idx - c) / seg))
        poof += full[k] * w
    poof *= p["poof_level"] / (np.std(poof[: int(0.3 * SR)]) + 1e-9) * 0.3 * attack_decay(t, p["poof_attack"], p["poof_decay"], 0.01)
    dust = bandpass(noise(len(t)), *p["dust_band"])
    dust *= p["dust_level"] / (np.std(dust) + 1e-9) * 0.3 * attack_decay(t, 0.03, p["dust_decay"], 0.02)
    chih = bandpass(noise(len(t)), *p["chih_band"])
    chih *= p["chih_level"] / (np.std(chih) + 1e-9) * 0.3 * attack_decay(t, p["chih_attack"], p["chih_decay"], p["chih_time"])
    return pop + poof + dust + chih


# ---------------------------------------------------------------- main
def build_all():
    sounds = []
    for i, v in enumerate(STEP["variants"], 1):
        sounds.append((f"nox_step_{i:02d}", make_step(v)))
    for i, v in enumerate(SHOT["variants"], 1):
        sounds.append((f"nox_attack_{i:02d}", make_shot(v)))
    sounds.append(("nox_hit", make_hit()))
    sounds.append(("nox_ult", make_ult()))
    for i, v in enumerate(HURT["variants"], 1):
        sounds.append((f"nox_hurt_{i:02d}", make_hurt(v)))
    for name in ("nox_vo_effort", "nox_vo_hurt", "nox_vo_win", "nox_vo_lose"):
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
    fig.suptitle("Nox SFX - spectrograms (0-8 kHz)", fontsize=11)
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
    write_wav(os.path.join(PREVIEW_DIR, "nox_preview.wav"), np.concatenate(preview))
    make_sheet(sounds, os.path.join(PREVIEW_DIR, "nox_sheet.png"))
    print(f"SFX: {len(sounds)} files -> {OUT_DIR}; checks {'OK' if all_ok else 'FAILED'}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
