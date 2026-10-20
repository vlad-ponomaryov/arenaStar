# WIP (2026-10-20): only header + model parts written; paint, rig, weapon and main NOT done - not runnable yet.
# Finish by assembling as build_mossy.py: helpers from build_wren.py 28-136, 247-435, 550-712, 758-811 + paint/rig/weapon/main.
# Bree (id bree, Docs/Roster-v3.md 3.2, Roster-v4 7 #13): 12-year-old roller freestyler with bubbles - round lilac
# helmet with stickers, long dark braid, white tee + open lilac vest, quad skates with lemon wheels, heart elbow pads,
# a huge bubble hoop on her back (wider than the shoulders), a bubble-mix bottle on the belt, a bubble wand in hand.
# Base model for Docs/Briefs/A-Roster3D.md (wave 2), compact Theo v3 pipeline (as build_wren.py / build_mossy.py).
#   blender -b --factory-startup --python Tools/Blender/fighters/build_bree.py -- [out_dir]
# Output (default Art/Source/3D/bree/): bree_game.fbx/.glb (<= 4000 tris, mesh "Bree", material "FighterVC" with the
# texture, armature "Rig": 12 FighterBone bones + Tail = braid), bree_tex.png (1024 RGBA: RGB albedo + AO, A = gloss),
# bree_weapon.fbx/.glb (bubble wand, origin = right hand, forward -Y, empty "Muzzle" at the ring centre).
# Format as Theo: Z up, faces -Y, wheels z = 0, height HEIGHT; FBX axis_forward=-Z, axis_up=Y.
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
POS = [a for a in ARGV if not a.startswith("--")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "bree")
os.makedirs(OUT_DIR, exist_ok=True)
T0 = time.time()
F32 = np.float32
BIG = F32(10.0)
HEIGHT = 1.0           # Theo = 1.0 (both 12); skates included
TEX = 1024
BUDGET_HEAD, BUDGET_BODY, BUDGET_WEAPON = 1700, 2250, 1150
NAME = "bree"


def torus_n(c, n, R, r):
    """Torus centred at c whose ring plane is perpendicular to unit vector n."""
    c, n = np.asarray(c, F32), np.asarray(nrm(n), F32)

    def f(P):
        Q = P - c
        h = Q @ n
        q = np.sqrt(np.maximum((Q * Q).sum(1) - h * h, 0)) - R
        return (np.sqrt(q * q + h * h) - r).astype(F32)
    return f


# =====================================================================================================================
# palette (sRGB) and gloss. Roster-v3 3.2: lilac #B98CFF (helmet, vest, skates), lemon #FFE14D (wheels, hair tie,
# stripes), white #FFFFFF (tee, socks), bubble shimmer #E6F7FF (energy only). Own: warm brown skin, dark hair,
# darker lilac shorts.
# =====================================================================================================================
PAL = {
    "skin": ("#A8704A", 0.20), "hair": ("#2A1C17", 0.40), "shirt": ("#FFFFFF", 0.12), "vest": ("#B98CFF", 0.30),
    "shorts": ("#6A55A8", 0.15), "sock": ("#FFFFFF", 0.12), "skate": ("#B98CFF", 0.80), "wheel": ("#FFE14D", 0.70),
    "plate": ("#8C8C9A", 0.75), "helmet": ("#B98CFF", 0.80), "strap": ("#6A55A8", 0.30), "lemon": ("#FFE14D", 0.55),
    "pad": ("#B98CFF", 0.60), "hoop": ("#B98CFF", 0.75), "bottle": ("#E6F7FF", 0.90),
}
PALW = {"grip": ("#FFE14D", 0.45), "shaft": ("#B98CFF", 0.70), "ring": ("#B98CFF", 0.80), "film": ("#E6F7FF", 0.95)}

# =====================================================================================================================
# MODEL (model units; normalised to HEIGHT at export). Character's right = -X, front = -Y.
# =====================================================================================================================
SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}
HEAD_C = np.array([0, 0.0, 0.600])
HEAD_R = np.array([0.150, 0.138, 0.140])
SH = {s: np.array([0.088 * s, 0.004, 0.455]) for s in SIDES}
EL = {-1: np.array([-0.125, -0.050, 0.390]), 1: np.array([0.118, 0.008, 0.375])}
WR = {-1: np.array([-0.088, -0.140, 0.385]), 1: np.array([0.130, -0.006, 0.300])}
HAND = {-1: np.array([-0.078, -0.166, 0.388]), 1: np.array([0.134, -0.012, 0.272])}
HIP = {s: np.array([0.046 * s, 0.004, 0.300]) for s in SIDES}
AN = {s: np.array([0.054 * s, 0.000, 0.105]) for s in SIDES}
EYE = {s: np.array([0.056 * s, -0.122, 0.590]) for s in SIDES}   # centre of the painted eye (on the face surface)
BRAID = [np.array([0, 0.140, 0.585]), np.array([0, 0.180, 0.520]), np.array([0, 0.205, 0.455]),
         np.array([0, 0.222, 0.395]), np.array([0, 0.232, 0.340])]
HOOP_C, HOOP_N, HOOP_R = np.array([0, 0.150, 0.505]), np.array([0, math.cos(math.radians(35)), -math.sin(math.radians(35))]), 0.195


class Part:
    def __init__(self, name, f, key, bone, k=0.0, shell="body"):
        self.name, self.f, self.key, self.bone, self.k, self.shell = name, f, key, bone, k, shell


PARTS = []


def part(name, f, key, bone, k=0.0, shell="body"):
    PARTS.append(Part(name, f, key, bone, k, shell))


HZ = HEAD_C[2]
# head: face, hair cap, side-swept fringe, braid (Tail), round lilac helmet with chin strap
part("head", ell(HEAD_C, HEAD_R), "skin", "Head", shell="head")
part("cheeks", ell(HEAD_C + (0, -0.03, -0.045), (0.128, 0.105, 0.090)), "skin", "Head", k=0.03, shell="head")
_cap = ell(HEAD_C + (0, 0.012, 0.004), (0.157, 0.150, 0.150))
part("hair", lambda P: smax(_cap(P), (-0.035 - P[:, 1]).astype(F32), 0.02), "hair", "Head", k=0.01, shell="head")
part("fringe", ell(HEAD_C + (0.03, -0.112, 0.062), (0.105, 0.040, 0.032), rot((0, 1, 0), -10)), "hair", "Head", k=0.012,
     shell="head")
for i in range(len(BRAID) - 1):
    a, b = BRAID[i], BRAID[i + 1]
    part(f"braid_{i}", ell((a + b) / 2, (0.034 - 0.004 * i, 0.030 - 0.003 * i, 0.044)), "hair", "Tail", k=0.012, shell="head")
part("braid_tie", ell(BRAID[-1] + (0, 0.002, -0.004), (0.020, 0.020, 0.014)), "lemon", "Tail", shell="head")
part("braid_tuft", cone(BRAID[-1] + (0, 0.002, -0.012), BRAID[-1] + (0, 0.012, -0.060), 0.020, 0.008), "hair", "Tail",
     k=0.01, shell="head")
_hel = ell(HEAD_C + (0, 0.006, 0.022), (0.164, 0.158, 0.150))
_hel_in = ell(HEAD_C + (0, 0.006, 0.022), (0.148, 0.142, 0.134))
part("helmet", lambda P: smax(smax(_hel(P), -_hel_in(P), 0.004),
                              (HZ + 0.065 - P[:, 2] - 0.06 * np.maximum(-P[:, 1], 0)).astype(F32), 0.012),
     "helmet", "Head", shell="head")
for s in SIDES:
    part(f"strap_{SN[s]}", cone((0.135 * s, -0.020, HZ + 0.040), (0.085 * s, -0.075, HZ - 0.110), 0.0065, 0.0065), "strap",
         "Head", shell="head")
# body
part("neck", cone((0, 0.004, 0.455), (0, 0.004, 0.52), 0.032, 0.030), "skin", "Body", k=0.01)
part("torso", ell((0, 0.004, 0.395), (0.080, 0.064, 0.098)), "shirt", "Body", k=0.01)
_vest = ell((0, 0.006, 0.400), (0.088, 0.072, 0.095))
_vcut = box((0, -0.10, 0.40), (0.030, 0.10, 0.13), 0.01)
part("vest", lambda P: smax(smax(_vest(P), -_vcut(P), 0.006), (P[:, 2] - 0.475).astype(F32), 0.01), "vest", "Body", k=0.004)
part("belt", cyl((0, 0.004, 0.318), (0, 0, 1), 0.076, 0.010, 0.006), "lemon", "Root", k=0.006)
part("pelvis", ell((0, 0.004, 0.300), (0.072, 0.058, 0.050)), "shorts", "Root", k=0.02)
part("bottle", cyl((0.088, 0.030, 0.288), (0, 0, 1), 0.020, 0.032, 0.008), "bottle", "Root", k=0.0)
part("bottle_cap", cyl((0.088, 0.030, 0.326), (0, 0, 1), 0.012, 0.008, 0.004), "lemon", "Root", k=0.0)
# bubble hoop on the back (tilted 35 deg so it reads as a ring from the top camera) + holder
part("hoop", torus_n(HOOP_C, HOOP_N, HOOP_R, 0.0135), "hoop", "Body", k=0.0)
part("hoop_holder", cone((0, 0.050, 0.395), HOOP_C - (0, 0.110, 0.157) + (0, 0.004, 0.0), 0.016, 0.013), "lemon", "Body",
     k=0.01)
for s in SIDES:
    part(f"sleeve_{SN[s]}", cone(SH[s], SH[s] + (EL[s] - SH[s]) * 0.45, 0.036, 0.031), "shirt", ("Arm", s), k=0.015)
    part(f"upper_{SN[s]}", cone(SH[s], EL[s], 0.026, 0.023), "skin", ("Arm", s), k=0.008)
    part(f"pad_{SN[s]}", ell(EL[s], (0.030, 0.030, 0.030)), "pad", ("Arm", s), k=0.006)
    part(f"fore_{SN[s]}", cone(EL[s], WR[s], 0.023, 0.020), "skin", ("Arm", s), k=0.008)
    part(f"hand_{SN[s]}", ell(HAND[s], (0.029, 0.031, 0.028)), "skin", ("Arm", s), k=0.012)
    part(f"shorts_{SN[s]}", cone(HIP[s], HIP[s] + (0.004 * s, -0.004, -0.075), 0.042, 0.038), "shorts", ("Leg", s), k=0.015)
    part(f"leg_{SN[s]}", cone(HIP[s], AN[s] + (0, 0, 0.04), 0.034, 0.027), "skin", ("Leg", s), k=0.012)
    part(f"sock_{SN[s]}", cyl(AN[s] + (0, 0.000, 0.135), (0, 0, 1), 0.031, 0.016, 0.006), "sock", ("Leg", s), k=0.006)
    part(f"skate_{SN[s]}", cone(AN[s] + (0, 0, 0.125), AN[s] + (0, -0.004, 0.065), 0.037, 0.040), "skate", ("Leg", s), k=0.012)
    part(f"skate_toe_{SN[s]}", ell(AN[s] + (0, -0.030, 0.050), (0.040, 0.062, 0.032)), "skate", ("Leg", s), k=0.02)
    part(f"skate_rim_{SN[s]}", cyl(AN[s] + (0, 0, 0.125), (0, 0, 1), 0.039, 0.008, 0.005), "lemon", ("Leg", s), k=0.004)
    part(f"plate_{SN[s]}", box(AN[s] + (0, -0.022, 0.036), (0.022, 0.062, 0.006), 0.004), "plate", ("Leg", s), k=0.0)
    part(f"stop_{SN[s]}", ell(AN[s] + (0, -0.092, 0.030), (0.016, 0.014, 0.016)), "lemon", ("Leg", s), k=0.004)
    for wy in (-0.060, 0.016):
        for wx in (-1, 1):
            part(f"wheel_{SN[s]}{wy}{wx}", cyl(AN[s] + (0.024 * wx, wy, 0.022), (1, 0, 0), 0.022, 0.0095, 0.005), "wheel",
                 ("Leg", s), k=0.0)


def eval_parts(P, parts):
    P = np.asarray(P, F32)
    out = np.full(len(P), BIG, F32)
    for p in parts:
        out = smin(out, p.f(P), p.k) if p.k > 0 else np.minimum(out, p.f(P))
    return np.maximum(out, (-0.0 - P[:, 2]).astype(F32))


def fig_d(P):
    return eval_parts(P, PARTS)


def owner(P, parts=None):
    parts = parts or PARTS
    D = np.stack([p.f(np.asarray(P, F32)) for p in parts], 0)
    return np.argmin(D, 0)


def uv_boost(cen):
    ff = np.ones(len(cen))
    ff[cen[:, 2] > HZ - 0.12] = 1.3
    face = (cen[:, 2] > HZ - 0.12) & (cen[:, 2] < HZ + 0.055) & (cen[:, 1] < -0.06) & (np.abs(cen[:, 0]) < 0.11)
    ff[face] = 2.4
    return ff

