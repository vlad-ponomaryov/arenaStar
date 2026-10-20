# Tess, the garage inventor in a home-made exosuit (Docs/Roster-v4.md 4.4, Docs/Briefs/A-Roster3D.md). Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_tess.py -- [out_dir]
# Output (default Art/Source/3D/tess/): tess_game.fbx/.glb (<= 4000 tris; mesh "Tess", armature "Rig", 12 FighterBone bones,
# material "FighterVC" -> tess_tex.png), tess_weapon.fbx/.glb (<= 1200 tris; right forearm spring blaster, origin = right hand
# grip = Prop bone head, barrel along -Y, empty "Muzzle" at the barrel tip), tess_tex.png (1024 RGBA: RGB = albedo with AO
# and soft key light, A = gloss).
# Format as Theo v3: Blender Z up, faces -Y, height 1.0, feet on z = 0; FBX axis_forward=-Z, axis_up=Y, colors_type=SRGB;
# UV "UVMap"; corner colour "Col" (linear, alpha = gloss).
# Method (simple base): smooth primitives (UV spheres / ellipsoids, cylinders, cones, tori, bevelled boxes) per part, each
# part owns one palette key and one bone (rigid weights, shoulder / hip seams blended 50/50 near the joint); per-vertex AO by
# BVH ray casts; smart-UV + Cycles emission bake of the vertex colours into the texture.
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "tess")
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene


def hexc(h):
    h = h.lstrip("#")
    return [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]


def lin(c):
    c = np.asarray(c)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


# palette key -> (sRGB hex, gloss)
PAL = {
    "lime": ("#B6E02E", 0.6), "lime_d": ("#86A81E", 0.35), "suit": ("#7E9A2A", 0.15), "ply": ("#C9A56B", 0.3),
    "ply_d": ("#A9844C", 0.25), "brass": ("#B8862F", 0.85), "tin": ("#A7ADB2", 0.8), "steam": ("#F4FFE0", 1.0),
    "skin": ("#8A5A3C", 0.18), "lip": ("#6E3E2C", 0.2), "hair": ("#2A1A12", 0.3), "eye_w": ("#FAF6EE", 0.9),
    "iris": ("#4A2A16", 1.0), "pupil": ("#120A06", 1.0), "spark": ("#FFFFFF", 1.0), "rubber": ("#2C2A2A", 0.2),
    "sole": ("#EDEAE0", 0.3), "glove": ("#6B4A2E", 0.2), "tape": ("#3C4A9A", 0.4), "oil": ("#3A2A22", 0.5),
    "lens": ("#CFE8A0", 1.0), "strap": ("#4A3A2C", 0.2),
}

PARTS = []  # (object, key, bone)


def _finish(bm, name, key, bone, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), smooth=True, coll=PARTS):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    SC.collection.objects.link(ob)
    M = Matrix.Translation(loc) @ Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4() @ \
        Matrix.Diagonal((*scale, 1))
    me.transform(M)
    for p in me.polygons:
        p.use_smooth = smooth
    coll.append((ob, key, bone))
    return ob


def sph(name, key, bone, loc, r, rot=(0, 0, 0), seg=14, ring=9, coll=PARTS):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=ring, radius=1.0)
    rr = r if isinstance(r, (tuple, list)) else (r, r, r)
    return _finish(bm, name, key, bone, loc, rot, rr, coll=coll)


def cyl(name, key, bone, a, b, r1, r2=None, seg=12, cap=True, coll=PARTS, smooth=True):
    """Cylinder / cone from point a to point b."""
    a, b = Vector(a), Vector(b)
    r2 = r1 if r2 is None else r2
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, segments=seg, radius1=r1, radius2=r2, depth=(b - a).length)
    me_rot = (b - a).normalized().to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ me_rot, verts=bm.verts)
    return _finish(bm, name, key, bone, smooth=smooth, coll=coll)


def torus(name, key, bone, loc, R, r, rot=(0, 0, 0), seg=14, mseg=6, scale=(1, 1, 1), coll=PARTS):
    bm = bmesh.new()
    verts = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        row = []
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            row.append(bm.verts.new(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b))))
        verts.append(row)
    for i in range(seg):
        for j in range(mseg):
            bm.faces.new((verts[i][j], verts[(i + 1) % seg][j], verts[(i + 1) % seg][(j + 1) % mseg], verts[i][(j + 1) % mseg]))
    return _finish(bm, name, key, bone, loc, rot, scale, coll=coll)


def box(name, key, bone, loc, size, rot=(0, 0, 0), bevel=0.3, coll=PARTS):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES", profile=0.5)
    return _finish(bm, name, key, bone, loc, rot, size, coll=coll)


S = (-1, 1)          # -1 = character's right (-X), +1 = left (+X)
SN = {-1: "R", 1: "L"}

# ---------------- body proportions (height 1.0 including the curl puff) ----------------
HIP_Z, NECK_Z = 0.36, 0.60
SH = {s: (s * 0.125, 0.0, 0.575) for s in S}
EL = {s: (s * 0.165, -0.02, 0.475) for s in S}
WR = {s: (s * 0.185, -0.075, 0.395) for s in S}
HP = {s: (s * 0.058, 0.0, 0.36) for s in S}
KN = {s: (s * 0.068, -0.01, 0.215) for s in S}
AN = {s: (s * 0.072, 0.005, 0.085) for s in S}

# torso: jumpsuit (moss-lime cloth), pelvis
sph("torso", "suit", "Body", (0, 0.005, 0.505), (0.112, 0.085, 0.115), seg=16, ring=10)
sph("pelvis", "suit", "Root", (0, 0.005, 0.385), (0.098, 0.078, 0.065), seg=16, ring=8)
cyl("neck", "skin", "Body", (0, 0.0, 0.58), (0, 0.0, 0.66), 0.036, seg=10)
# pockets on the hips
for s in S:
    box("pocket" + SN[s], "suit", "Root", (s * 0.085, -0.045, 0.375), (0.026, 0.016, 0.026), rot=(0, 0, s * 35), bevel=0.4)
# chest plate: plywood shell + tin trim, brass rivets, a blue tape strip and a sticker (lime star)
sph("chest", "ply", "Body", (0, -0.052, 0.52), (0.105, 0.05, 0.085), seg=16, ring=8)
box("tape", "tape", "Body", (0.04, -0.098, 0.548), (0.042, 0.006, 0.009), rot=(0, 25, 0), bevel=0.4)
sph("sticker", "lime", "Body", (-0.05, -0.098, 0.49), (0.018, 0.006, 0.018), seg=8, ring=5)
for x, z in ((-0.072, 0.565), (0.072, 0.565), (-0.075, 0.47), (0.075, 0.47)):
    sph("rivet", "brass", "Body", (x, -0.088 + abs(x) * 0.25, z), 0.0085, seg=8, ring=5)
# belt (rubber) with brass buckle
torus("belt", "rubber", "Root", (0, 0.005, 0.415), 0.097, 0.012, scale=(1.08, 0.86, 1), seg=16)
box("buckle", "brass", "Root", (0, -0.088, 0.415), (0.022, 0.008, 0.016), bevel=0.35)

# backpack: plywood box with the wind-up key (big brass bow above the back) and three brass gear marks
box("pack", "ply_d", "Body", (0, 0.095, 0.51), (0.08, 0.035, 0.085), bevel=0.35)
cyl("keyshaft", "brass", "Body", (0, 0.12, 0.56), (0, 0.20, 0.60), 0.016, seg=10)
torus("keybowL", "brass", "Body", (0.085, 0.215, 0.62), 0.055, 0.017, rot=(90, 0, 0), seg=14, mseg=6, scale=(1.25, 0.8, 1))
torus("keybowR", "brass", "Body", (-0.085, 0.215, 0.62), 0.055, 0.017, rot=(90, 0, 0), seg=14, mseg=6, scale=(1.25, 0.8, 1))
sph("keyhub", "brass", "Body", (0, 0.212, 0.615), (0.032, 0.022, 0.032), seg=10, ring=6)
for i, x in enumerate((-0.045, 0.0, 0.045)):
    cyl("gear%d" % i, "brass", "Body", (x, 0.128, 0.455), (x, 0.138, 0.455), 0.017, seg=10)
    cyl("gearc%d" % i, "steam", "Body", (x, 0.136, 0.455), (x, 0.141, 0.455), 0.008, seg=8)

# arms: shoulder pots (tin half spheres, brass rivet), lime sleeves, elbow springs (brass coils), gloves
for s in S:
    b = "Arm" + SN[s]
    sph("pot" + SN[s], "tin", b, (s * 0.135, 0.0, 0.585), (0.058, 0.058, 0.045), seg=14, ring=8)
    torus("potrim" + SN[s], "tin", b, (s * 0.135, 0.0, 0.565), 0.052, 0.008, seg=14, mseg=5)
    sph("potrivet" + SN[s], "brass", b, (s * 0.188, 0.0, 0.592), 0.009, seg=8, ring=5)
    cyl("upper" + SN[s], "suit", b, SH[s], EL[s], 0.032, 0.029, seg=10)
    for k in range(3):
        t = 0.3 + 0.2 * k
        c = Vector(EL[s]).lerp(Vector(WR[s]), t * 0.25)
        torus("spring%s%d" % (SN[s], k), "brass", b, (c.x, c.y, c.z + 0.012 - 0.012 * k), 0.033, 0.007,
              rot=(-20, s * -10, 0), seg=12, mseg=5)
    cyl("fore" + SN[s], "suit", b, EL[s], WR[s], 0.029, 0.027, seg=10)
    sph("glove" + SN[s], "glove", b, (WR[s][0] + s * 0.004, WR[s][1] - 0.02, WR[s][2] - 0.025), (0.032, 0.036, 0.034),
        seg=12, ring=8)
    cyl("cuff" + SN[s], "glove", b, Vector(WR[s]) + Vector((0, 0.02, 0.02)), Vector(WR[s]), 0.034, 0.034, seg=10)
# left forearm blaster (the right one is the weapon FBX on Prop): same shape as build_weapon(), mirrored, part of the body
LEFT_BLASTER = []

# legs: lime trousers, bike-tyre knee pads, high lime sneakers with white soles and tin steam nozzles, steam puffs
for s in S:
    b = "Leg" + SN[s]
    cyl("thigh" + SN[s], "suit", b, HP[s], KN[s], 0.044, 0.038, seg=12)
    cyl("shin" + SN[s], "suit", b, KN[s], AN[s], 0.036, 0.034, seg=12)
    torus("tyre" + SN[s], "rubber", b, (KN[s][0], KN[s][1] - 0.03, KN[s][2]), 0.028, 0.012, rot=(90, 0, 0), seg=14, mseg=6)
    sph("hub" + SN[s], "tin", b, (KN[s][0], KN[s][1] - 0.036, KN[s][2]), (0.016, 0.008, 0.016), seg=8, ring=5)
    cyl("bootshaft" + SN[s], "lime", b, (AN[s][0], 0.005, 0.045), (AN[s][0], 0.005, 0.14), 0.045, 0.041, seg=14)
    sph("boottoe" + SN[s], "lime", b, (AN[s][0], -0.04, 0.035), (0.046, 0.062, 0.036), seg=14, ring=8)
    box("sole" + SN[s], "sole", b, (AN[s][0], -0.022, 0.012), (0.049, 0.078, 0.012), bevel=0.5)
    sph("lace" + SN[s], "sole", b, (AN[s][0], -0.052, 0.07), (0.022, 0.02, 0.016), seg=8, ring=5)
    cyl("nozzle" + SN[s], "tin", b, (AN[s][0], 0.05, 0.07), (AN[s][0], 0.085, 0.05), 0.018, 0.026, seg=10, cap=True)
    torus("nozring" + SN[s], "brass", b, (AN[s][0], 0.06, 0.064), 0.021, 0.005, rot=(60, 0, 0), seg=10, mseg=4)
    # steam puffs trailing behind the heels (energy colour), readable from above
    for k, (dy, dz, r) in enumerate(((0.11, 0.05, 0.038), (0.15, 0.04, 0.032), (0.185, 0.058, 0.024))):
        sph("puff%s%d" % (SN[s], k), "steam", b, (AN[s][0] + s * 0.006 * k, dy, dz), r, seg=10, ring=6)

# head: skin, curls, pineapple puff, welder goggles on the forehead, face
HC = Vector((0, -0.005, 0.765))
sph("head", "skin", "Head", HC, (0.112, 0.104, 0.112), seg=20, ring=14)
sph("jaw", "skin", "Head", (0, -0.035, 0.72), (0.083, 0.07, 0.06), seg=16, ring=8)
for s in S:
    sph("ear" + SN[s], "skin", "Head", (s * 0.11, 0.005, 0.755), (0.018, 0.012, 0.025), seg=8, ring=6)
sph("nose", "skin", "Head", (0, -0.112, 0.752), (0.014, 0.011, 0.011), seg=8, ring=6)
sph("mouth", "lip", "Head", (0.006, -0.1, 0.716), (0.022, 0.006, 0.008), seg=10, ring=5)
sph("tooth", "eye_w", "Head", (0.002, -0.103, 0.718), (0.007, 0.004, 0.004), seg=6, ring=4)
sph("oil", "oil", "Head", (-0.06, -0.093, 0.732), (0.017, 0.005, 0.01), rot=(0, 0, 40), seg=8, ring=5)
EYE = {}
for s in S:
    c = Vector((s * 0.043, -0.09, 0.768))
    sph("eye" + SN[s], "eye_w", "Eye" + SN[s], c, (0.024, 0.013, 0.029), rot=(0, 0, -s * 18), seg=12, ring=8)
    sph("iris" + SN[s], "iris", "Eye" + SN[s], c + Vector((-s * 0.002, -0.011, -0.002)), (0.016, 0.006, 0.02), rot=(0, 0, -s * 18),
        seg=10, ring=6)
    sph("pupil" + SN[s], "pupil", "Eye" + SN[s], c + Vector((-s * 0.002, -0.015, -0.002)), (0.008, 0.004, 0.011),
        rot=(0, 0, -s * 18), seg=8, ring=5)
    sph("spark" + SN[s], "spark", "Eye" + SN[s], c + Vector((s * 0.005, -0.018, 0.008)), 0.004, seg=6, ring=4)
    EYE[s] = (c, Vector((s * 0.15, -1, 0)).normalized())
    box("brow" + SN[s], "hair", "Head", (s * 0.045, -0.096, 0.812), (0.019, 0.005, 0.004), rot=(0, s * -12, s * 12), bevel=0.45)
# hair: cap of curls (back/sides), curls around the hairline, pineapple puff on top tied with a lime band
sph("haircap", "hair", "Head", (0, 0.012, 0.795), (0.118, 0.112, 0.1), seg=18, ring=10)
rng = np.random.default_rng(7)
for k in range(10):
    a = math.radians(-120 + k * 26.6)
    x, y = 0.112 * math.sin(a), 0.105 * -math.cos(a) * -1
    if y < -0.06:
        continue
    sph("curl%d" % k, "hair", "Head", (x, y + 0.01, 0.745 + 0.012 * math.sin(k * 1.7)), 0.033, seg=10, ring=6)
cyl("tie", "lime", "Head", (0, 0.01, 0.875), (0, 0.01, 0.9), 0.045, 0.042, seg=12)
PUFF = [(0, 0.01, 0.94, 0.06), (0.04, 0.0, 0.925, 0.042), (-0.04, 0.02, 0.928, 0.042), (0.0, -0.035, 0.925, 0.04),
        (0.0, 0.05, 0.93, 0.042), (0.025, 0.025, 0.97, 0.035), (-0.025, -0.015, 0.968, 0.034)]
for k, (x, y, z, r) in enumerate(PUFF):
    sph("puffhair%d" % k, "hair", "Head", (x, y, z), r, seg=10, ring=7)
# welder goggles pushed up on the forehead: strap + two brass cups with pale lime lenses
torus("gogstrap", "strap", "Head", (0, -0.003, 0.83), 0.115, 0.009, rot=(-14, 0, 0), seg=18, mseg=4, scale=(1, 0.97, 1))
for s in S:
    a = Vector((s * 0.04, -0.098, 0.842))
    bb = a + Vector((0, -0.024, 0.012))
    cyl("gogcup" + SN[s], "brass", "Head", a, bb, 0.03, 0.028, seg=12)
    cyl("goglens" + SN[s], "lens", "Head", bb, bb + Vector((0, -0.004, 0.002)), 0.022, seg=12)
box("gogbridge", "brass", "Head", (0, -0.112, 0.848), (0.012, 0.006, 0.006), bevel=0.4)


# bigger toy head: scale the head parts by 1.12 about the neck
HEAD_K = 1.12
for ob, key, bone in PARTS:
    if bone in ("Head", "EyeL", "EyeR"):
        ob.data.transform(Matrix.Translation((0, 0, 0.66)) @ Matrix.Scale(HEAD_K, 4) @ Matrix.Translation((0, 0, -0.66)))
for s_ in S:
    c, d = EYE[s_]
    EYE[s_] = (Vector((0, 0, 0.66)) + (c - Vector((0, 0, 0.66))) * HEAD_K, d)


# ---------------- weapon: spring pea blaster (toy water pistol parts) ----------------
def build_blaster(coll, side=-1, origin=Vector((0, 0, 0)), bone="Prop"):
    """Origin = grip centre (under the hand), barrel along -Y."""
    o = origin
    m = lambda x, y, z: (o.x + side * -x, o.y + y, o.z + z)  # side -1: as authored
    cyl("bl_tube", "lime", bone, m(0, 0.07, 0.03), m(0, -0.21, 0.03), 0.028, 0.025, seg=14, coll=coll)
    cyl("bl_tail", "lime_d", bone, m(0, 0.075, 0.03), m(0, 0.10, 0.03), 0.03, 0.022, seg=14, coll=coll)
    cyl("bl_tank", "tin", bone, m(0, 0.045, 0.072), m(0, -0.06, 0.072), 0.022, 0.022, seg=12, coll=coll)
    sph("bl_tankcapF", "tin", bone, m(0, -0.06, 0.072), 0.022, seg=12, ring=6, coll=coll)
    sph("bl_tankcapB", "tin", bone, m(0, 0.045, 0.072), 0.022, seg=12, ring=6, coll=coll)
    cyl("bl_cap", "brass", bone, m(0, -0.005, 0.09), m(0, -0.005, 0.104), 0.01, seg=8, coll=coll)
    for k in range(4):
        torus("bl_coil%d" % k, "brass", bone, m(0, -0.12 - k * 0.02, 0.03), 0.03, 0.0055, rot=(90, 0, 0), seg=12, mseg=4,
              coll=coll)
    cyl("bl_muzzle", "brass", bone, m(0, -0.21, 0.03), m(0, -0.235, 0.03), 0.026, 0.032, seg=14, coll=coll)
    sph("bl_pea", "steam", bone, m(0, -0.237, 0.03), 0.021, seg=10, ring=6, coll=coll)
    box("bl_strap", "strap", bone, m(0, 0.035, 0.004), (0.036, 0.012, 0.03), bevel=0.3, coll=coll)
    box("bl_grip", "ply", bone, m(0, -0.012, -0.012), (0.012, 0.016, 0.03), rot=(15, 0, 0), bevel=0.4, coll=coll)
    sph("bl_dial", "tin", bone, m(-0.03, 0.02, 0.04), (0.006, 0.014, 0.014), seg=8, ring=5, coll=coll)


PROP_HEAD = Vector((WR[-1][0] + -1 * 0.004, WR[-1][1] - 0.02, WR[-1][2] - 0.025))
LEFT_HAND = Vector((WR[1][0] + 0.004, WR[1][1] - 0.02, WR[1][2] - 0.025))
build_blaster(PARTS, side=1, origin=LEFT_HAND, bone="ArmL")
WPARTS = []
build_blaster(WPARTS, side=-1, origin=Vector((0, 0, 0)), bone="Prop")


# ---------------- join, colour, AO, bake ----------------
def ptris(ob):
    return sum(len(f.vertices) - 2 for f in ob.data.polygons)


def fit_budget(parts, target, small=60):
    """Collapse-decimate the bigger parts so the whole set fits the triangle budget."""
    for _ in range(4):
        cnt = [ptris(o) for o, _, _ in parts]
        tot = sum(cnt)
        if tot <= target:
            return
        sm = sum(c for c in cnt if c <= small)
        ratio = max(0.15, (target - sm) / max(1, tot - sm)) * 0.97
        for (o, _, _), c in zip(parts, cnt):
            if c > small:
                m = o.modifiers.new("dec", "DECIMATE")
                m.ratio = ratio
                m.use_collapse_triangulate = True
                bpy.context.view_layer.objects.active = o
                with bpy.context.temp_override(object=o, active_object=o):
                    bpy.ops.object.modifier_apply(modifier="dec")
        small = small * 0.7


def join(parts, name, target):
    fit_budget(parts, target)
    keys = list(PAL)
    for ob, key, bone in parts:
        me = ob.data
        a = me.attributes.new("pkey", "INT", "POINT")
        a.data.foreach_set("value", [keys.index(key)] * len(me.vertices))
        vg = ob.vertex_groups.new(name=bone)
        vg.add(list(range(len(me.vertices))), 1.0, "REPLACE")
    bpy.ops.object.select_all(action="DESELECT")
    for ob, _, _ in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    ob = parts[0][0]
    ob.name = ob.data.name = name
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    return ob


def colourise(ob, light=True):
    me = ob.data
    keys = list(PAL)
    nv = len(me.vertices)
    pk = np.zeros(nv, np.int32)
    me.attributes["pkey"].data.foreach_get("value", pk)
    co = np.zeros(nv * 3, np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    me.update()
    nr = np.zeros(nv * 3, np.float32)
    me.vertices.foreach_get("normal", nr)
    nr = nr.reshape(-1, 3)
    bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
    rng = np.random.default_rng(3)
    dirs = rng.normal(size=(20, 3))
    dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
    ao = np.ones(nv)
    for i in range(nv):
        n = Vector(nr[i])
        p = Vector(co[i]) + n * 0.002
        hit = 0
        cnt = 0
        for d in dirs:
            dv = Vector(d)
            if dv.dot(n) < 0:
                dv = -dv
            cnt += 1
            if bvh.ray_cast(p, dv, 0.08)[0] is not None:
                hit += 1
        ao[i] = 1 - 0.6 * hit / cnt
    key_l = np.array([-0.3, -0.55, 0.78])
    key_l /= np.linalg.norm(key_l)
    base = np.array([lin(hexc(PAL[k][0])) for k in keys])
    gl = np.array([PAL[k][1] for k in keys])
    emiss = np.array([k in ("steam", "spark") for k in keys])
    lam = np.clip(nr @ key_l, -1, 1)
    shade = 0.82 + 0.22 * lam if light else np.ones(nv)
    shade = np.where(emiss[pk], 1.0, shade * ao)
    col = base[pk] * shade[:, None]
    col = np.clip(col, 0, 1)
    loops_v = np.zeros(len(me.loops), np.int32)
    me.loops.foreach_get("vertex_index", loops_v)
    rgba = np.hstack([col[loops_v], gl[pk][loops_v][:, None]]).astype(np.float32)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", rgba.ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = me.color_attributes.find("Col")
    me.attributes.remove(me.attributes["pkey"])
    return attr


def unwrap(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    me = ob.data
    if "UVMap" not in me.uv_layers:
        me.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004, area_weight=0.0)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake(objs, res, path):
    """Bake corner colours (RGB) and alpha (gloss) of every object into one RGBA image (objects' UVs must not overlap)."""
    SC.render.engine = "CYCLES"
    SC.cycles.samples = 1
    SC.cycles.device = "CPU"
    out = {}
    for chan in ("rgb", "a"):
        img = bpy.data.images.new("bake_" + chan, res, res, alpha=False, float_buffer=True)
        for ob in objs:
            mat = bpy.data.materials.new("BK")
            mat.use_nodes = True
            nt = mat.node_tree
            for n in list(nt.nodes):
                nt.nodes.remove(n)
            outn = nt.nodes.new("ShaderNodeOutputMaterial")
            em = nt.nodes.new("ShaderNodeEmission")
            at = nt.nodes.new("ShaderNodeAttribute")
            at.attribute_name = "Col"
            nt.links.new(at.outputs["Color" if chan == "rgb" else "Alpha"], em.inputs["Color"])
            nt.links.new(em.outputs[0], outn.inputs[0])
            tn = nt.nodes.new("ShaderNodeTexImage")
            tn.image = img
            nt.nodes.active = tn
            ob.data.materials.clear()
            ob.data.materials.append(mat)
        bpy.ops.object.select_all(action="DESELECT")
        for ob in objs:
            ob.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.bake(type="EMIT", margin=6, use_clear=True)
        px = np.empty(res * res * 4, np.float32)
        img.pixels.foreach_get(px)
        out[chan] = px.reshape(res, res, 4)
    rgb = out["rgb"][..., :3]
    srgb = np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * np.power(np.clip(rgb, 0, None), 1 / 2.4) - 0.055)
    a = out["a"][..., 0:1]
    final = np.concatenate([np.clip(srgb, 0, 1), np.clip(a, 0, 1)], axis=2).astype(np.float32)
    im = bpy.data.images.new("tess_tex", res, res, alpha=True)
    im.pixels.foreach_set(final.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return path


def final_material(ob, tex_path):
    mat = bpy.data.materials.get("FighterVC") or bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    texn = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    texn.image = bpy.data.images.load(tex_path, check_existing=True)
    texn.image.alpha_mode = "CHANNEL_PACKED"
    nt.links.new(texn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
    ob.data.materials.clear()
    ob.data.materials.append(mat)


def tris(ob):
    return len(ob.data.polygons)


body = join(PARTS, "Tess", 3950)
weapon = join(WPARTS, "TessWeapon", 1180)

# normalise: body height 1.0 (feet on z = 0, centred on X/Y); the weapon gets the same scale
V = np.array([v.co[:] for v in body.data.vertices])
zmin, zmax = V[:, 2].min(), V[:, 2].max()
cx, cy = (V[:, 0].min() + V[:, 0].max()) / 2, 0.0
SCALE = 1.0 / (zmax - zmin)
SHIFT = Vector((-cx, -cy, -zmin))
body.data.transform(Matrix.Scale(SCALE, 4) @ Matrix.Translation(SHIFT))
weapon.data.transform(Matrix.Scale(SCALE, 4))


def P(p):
    return (Vector(p) + SHIFT) * SCALE


print("TRIS body", tris(body), "weapon", tris(weapon))
assert tris(body) <= 4000 and tris(weapon) <= 1200, "over budget"

colourise(body)
colourise(weapon)
unwrap(body)
unwrap(weapon)
# body UVs -> the left 7/8 of the atlas... simpler: body owns the bottom 3/4, weapon the top 1/4 strip
for ob, (u0, v0, su, sv) in ((body, (0, 0, 1.0, 0.75)), (weapon, (0.0, 0.76, 0.24, 0.24))):
    uv = ob.data.uv_layers["UVMap"]
    a = np.zeros(len(uv.data) * 2, np.float32)
    uv.data.foreach_get("uv", a)
    a = a.reshape(-1, 2)
    a[:, 0] = u0 + a[:, 0] * su
    a[:, 1] = v0 + a[:, 1] * sv
    uv.data.foreach_set("uv", a.ravel())
TEX = os.path.join(OUT, "tess_tex.png")
bake([body, weapon], 1024, TEX)
final_material(body, TEX)
final_material(weapon, TEX)

# ---------------- armature ----------------
BONE_DEF = [
    ("Root", None, (0, 0, 0), (0, 0, HIP_Z)),
    ("Body", "Root", (0, 0, HIP_Z), (0, 0, NECK_Z + 0.04)),
    ("Head", "Body", (0, 0, NECK_Z + 0.04), (0, 0, 1.0 / SCALE + zmin)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HP[-1], AN[-1]),
    ("LegL", "Root", HP[1], AN[1]),
    ("Prop", "Body", PROP_HEAD, PROP_HEAD + Vector((0, -0.10, 0))),
    ("EyeR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("EyeL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("LidR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("LidL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
]
ad = bpy.data.armatures.new("TessRig")
arm = bpy.data.objects.new("Rig", ad)
SC.collection.objects.link(arm)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for bn, par, hd, tl in BONE_DEF:
    eb = ad.edit_bones.new(bn)
    eb.head, eb.tail = P(hd), P(tl)
    if par:
        eb.parent = ad.edit_bones[par]
bpy.ops.object.mode_set(mode="OBJECT")
# blend the joints: sleeve verts near the shoulder 50/50 Arm/Body, thigh verts near the hip 50/50 Leg/Root
for s in S:
    for gname, other, centre, rad in (("Arm" + SN[s], "Body", P(SH[s]), 0.035), ("Leg" + SN[s], "Root", P(HP[s]), 0.04)):
        g = body.vertex_groups[gname]
        go = body.vertex_groups.get(other) or body.vertex_groups.new(name=other)
        for v in body.data.vertices:
            if any(e.group == g.index for e in v.groups) and (v.co - centre).length < rad:
                g.add([v.index], 0.5, "REPLACE")
                go.add([v.index], 0.5, "REPLACE")
for bn, *_ in BONE_DEF:
    if bn not in body.vertex_groups:
        body.vertex_groups.new(name=bn)
body.parent = arm
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm
# weapon: no bones; muzzle empty at the barrel tip
for g in list(weapon.vertex_groups):
    weapon.vertex_groups.remove(g)
muz = bpy.data.objects.new("Muzzle", None)
SC.collection.objects.link(muz)
muz.location = Vector((0, -0.245, 0.03)) * SCALE
muz.parent = weapon


def export(objs, base, types):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types=types, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)


export([arm, body], os.path.join(OUT, "tess_game"), {"ARMATURE", "MESH"})
export([weapon, muz], os.path.join(OUT, "tess_weapon"), {"MESH", "EMPTY"})
pb = arm.data.bones["Prop"].head_local
print("TESS: body tris", tris(body), "verts", len(body.data.vertices), "| weapon tris", tris(weapon),
      "| Prop", tuple(round(c, 3) for c in pb), "| scale", round(SCALE, 4))
