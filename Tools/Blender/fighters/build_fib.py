# Fib (14, sticker-trickster / illusionist), base 3D model (Docs/Briefs/A-Roster3D.md, Docs/Roster-v4.md 6.1). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_fib.py -- [out_dir]
# Pipeline = build_lark.py (analytic primitives -> voxel remesh -> per-face colour labels -> decimate -> smart UV -> Cycles
# bake high->low, RGB = albedo, A = gloss -> fib_tex.png 1024 -> 12 FighterBone bones, rigid weights -> FBX/GLB).
# Look: long thin boy in a huge bell-shaped plum coat with ~30 bulging mustard pockets, ginger curls, freckles, cap worn
# backwards. Weapon: toy pocket flashlight (shadow-bird attack): fib_weapon.fbx/.glb, origin = grip under the right hand,
# beam along -Y, empty "Muzzle" at the lens. No Loki cues (no green/gold, horns, cape, slick black hair, daggers).
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "fib")
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]




PAL = {  # sRGB hex, gloss
    "skin": hexc("#F6D3B6", 0.15), "hair": hexc("#C9602A", 0.35), "hair_d": hexc("#9C4519", 0.3),
    "coat": hexc("#6A3F5C", 0.3), "coat_d": hexc("#53304A", 0.25), "pocket": hexc("#F2D68A", 0.25),
    "pocket_d": hexc("#D9B864", 0.2), "shirt": hexc("#FFF4DA", 0.12), "pants": hexc("#3E3A44", 0.1),
    "shoe": hexc("#F2D68A", 0.5), "sole": hexc("#4A2E40", 0.3), "cap": hexc("#F2D68A", 0.45),
    "cap_d": hexc("#6A3F5C", 0.45), "white": hexc("#F7F7F5", 0.6), "iris": hexc("#6B8A3A", 1.0),
    "pupil": hexc("#1A1A1A", 1.0), "mouth": hexc("#9E4236", 0.3), "freckle": hexc("#CF8A5E", 0.15),
    "torch": hexc("#F2D68A", 0.7), "torch_d": hexc("#53304A", 0.6), "glow": hexc("#FFF2C2", 1.0),
    "candy": hexc("#FF8FB0", 0.8), "sticker": hexc("#8FD6F0", 0.6), "sticker2": hexc("#B4E07A", 0.6),
    "button": hexc("#D9B864", 0.8),
}
VOXEL = 0.0045
WAIST_Z, NECK_Z, HEAD_TOP, BODY_ROOT_Z = 0.40, 0.63, 1.0, 0.45

class P:
    """Primitive: kind 'e' (ellipsoid c, r) or 'c' (capsule a, b, ra, rb). geo=False -> label only (decal)."""

    def __init__(self, kind, key, bone, prio, geo=True, **k):
        self.kind, self.key, self.bone, self.prio, self.geo, self.k = kind, key, bone, prio, geo, k

    def d(self, X):
        if self.kind == "e":
            c, r = np.array(self.k["c"]), np.array(self.k["r"])
            q = np.linalg.norm((X - c) / r, axis=1)
            return (q - 1) * r.min()
        a, b = np.array(self.k["a"]), np.array(self.k["b"])
        ba = b - a
        t = np.clip(((X - a) @ ba) / (ba @ ba), 0, 1)
        r = self.k["ra"] + (self.k["rb"] - self.k["ra"]) * t
        return np.linalg.norm(X - (a + t[:, None] * ba), axis=1) - r

    def make(self):
        if self.kind == "e":
            bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=1, location=self.k["c"])
            o = bpy.context.object
            o.scale = self.k["r"]
            return [o]
        a, b = Vector(self.k["a"]), Vector(self.k["b"])
        ra, rb = self.k["ra"], self.k["rb"]
        out = []
        for c, r in ((a, ra), (b, rb)):
            bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=r, location=c)
            out.append(bpy.context.object)
        bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=ra, radius2=rb, depth=(b - a).length, location=(a + b) / 2)
        o = bpy.context.object
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = (b - a).to_track_quat("Z", "Y")
        out.append(o)
        return out


E = lambda key, bone, prio, c, r, geo=True: P("e", key, bone, prio, geo, c=c, r=r)
C = lambda key, bone, prio, a, b, ra, rb=None, geo=True: P("c", key, bone, prio, geo, a=a, b=b, ra=ra, rb=ra if rb is None else rb)
import random

# ---------------- body (Blender: Z up, faces -Y, character's right = -X) ----------------
SH = {-1: (-0.125, 0.0, 0.60), 1: (0.125, 0.0, 0.60)}
EL = {-1: (-0.165, -0.04, 0.48), 1: (0.15, -0.10, 0.52)}
HA = {-1: (-0.11, -0.16, 0.47), 1: (0.04, -0.26, 0.56)}  # right: flashlight; left: shadow puppet in the beam
HIP = {-1: (-0.055, 0.0, 0.36), 1: (0.055, 0.0, 0.36)}
AN = {-1: (-0.07, 0.0, 0.085), 1: (0.07, 0.0, 0.085)}
EYE = {s: (0.058 * s, -0.135, 0.79) for s in (-1, 1)}
HEAD = (0, 0, 0.80)
BODY = [
    E("skin", "Head", 1, HEAD, (0.152, 0.142, 0.15)),
    E("skin", "Head", 1, (0, -0.025, 0.72), (0.11, 0.105, 0.075)),  # narrow jaw (long sly face)
    C("skin", "Head", 1, (0, 0, 0.60), (0, 0, 0.69), 0.04),  # neck
    # cap worn backwards: dome + brim pointing back + top button
    E("cap", "Head", 3, (0, 0.012, 0.865), (0.16, 0.152, 0.105)),
    E("cap_d", "Head", 4, (0, 0.012, 0.825), (0.162, 0.154, 0.02), geo=False),  # plum band
    E("cap_d", "Head", 4, (0, 0.20, 0.835), (0.11, 0.10, 0.016)),  # brim (back)
    E("cap_d", "Head", 5, (0, 0.012, 0.968), (0.022, 0.022, 0.014)),
    E("hair", "Head", 2, (0, 0.03, 0.80), (0.158, 0.15, 0.13)),  # hair mass under the cap
    E("skin", "Head", 3, (0, -0.06, 0.78), (0.135, 0.11, 0.13)),  # face over the hair
    E("skin", "Head", 2, (-0.153, 0.0, 0.79), (0.02, 0.03, 0.035)),  # ears
    E("skin", "Head", 2, (0.153, 0.0, 0.79), (0.02, 0.03, 0.035)),
    E("skin", "Head", 3, (0, -0.152, 0.765), (0.016, 0.018, 0.02)),  # nose
    E("white", "Head", 6, EYE[-1], (0.038, 0.02, 0.044)),
    E("white", "Head", 6, EYE[1], (0.038, 0.02, 0.044)),
    E("hair_d", "Head", 7, (-0.06, -0.14, 0.845), (0.034, 0.03, 0.008), geo=False),  # brows (one raised: sly)
    E("hair_d", "Head", 7, (0.06, -0.14, 0.855), (0.034, 0.03, 0.008), geo=False),
    E("mouth", "Head", 7, (0.025, -0.145, 0.712), (0.034, 0.03, 0.008), geo=False),  # lopsided grin
    E("mouth", "Head", 7, (0.055, -0.14, 0.718), (0.012, 0.03, 0.01), geo=False),
    # coat: shoulders -> big bell, mustard hem band, upturned collar, open V with a cream shirt
    C("coat", "Body", 1, (0, 0, 0.60), (0, 0, 0.45), 0.11, 0.135),
    E("pocket", "Root", 2, (0, 0, 0.228), (0.212, 0.202, 0.024)),  # hem band
    E("coat_d", "Body", 3, (0, 0.02, 0.635), (0.12, 0.105, 0.05)),  # collar
    E("shirt", "Body", 3, (0, -0.11, 0.57), (0.04, 0.03, 0.05), geo=False),  # V opening
    E("coat_d", "Root", 2, (0, -0.215, 0.30), (0.008, 0.03, 0.13), geo=False),  # front seam
    C("pants", "Root", 1, (0, 0, 0.36), (0, 0, 0.30), 0.09),
]
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    eb = "Eye" + ("R" if s < 0 else "L")
    BODY += [E("iris", eb, 8, (ex, ey - 0.02, ez - 0.004), (0.025, 0.012, 0.031), geo=False),
             E("pupil", eb, 9, (ex + 0.006, ey - 0.024, ez - 0.004), (0.012, 0.01, 0.016), geo=False),  # side glance
             E("white", eb, 10, (ex + 0.014 * s, ey - 0.026, ez + 0.015), (0.007, 0.01, 0.008), geo=False)]
    for fx, fz in ((0.055, 0.735), (0.08, 0.745), (0.07, 0.722), (0.095, 0.728), (0.045, 0.75)):
        BODY.append(E("freckle", "Head", 8, (fx * s, -0.13, fz), (0.007, 0.03, 0.007), geo=False))
    # curls sticking out under the cap (front fringe + sides + back)
    BODY += [E("hair", "Head", 4, (0.07 * s, -0.115, 0.875), (0.045, 0.035, 0.03)),
             E("hair", "Head", 4, (0.14 * s, -0.05, 0.835), (0.035, 0.045, 0.04)),
             E("hair", "Head", 4, (0.13 * s, 0.07, 0.80), (0.04, 0.05, 0.045)),
             E("hair", "Head", 4, (0.05 * s, 0.14, 0.78), (0.05, 0.04, 0.045))]
    side = "R" if s < 0 else "L"
    BODY += [C("coat", "Arm" + side, 2, SH[s], EL[s], 0.048, 0.044),
             C("coat", "Arm" + side, 2, EL[s], HA[s], 0.044, 0.05),  # sleeve widens to the cuff
             E("coat", "Arm" + side, 2, SH[s], (0.058, 0.058, 0.055)),
             E("pocket", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.82 + np.array(EL[s]) * 0.18), (0.05, 0.05, 0.05)),
             E("skin", "Arm" + side, 4, HA[s], (0.034, 0.038, 0.034)),
             C("pants", "Leg" + side, 1, HIP[s], AN[s], 0.045, 0.038),
             E("shoe", "Leg" + side, 3, (AN[s][0], -0.03, 0.05), (0.05, 0.085, 0.045)),
             E("sole", "Leg" + side, 4, (AN[s][0], -0.03, 0.006), (0.054, 0.09, 0.014))]
BODY.append(C("skin", "ArmL", 5, HA[1], (0.02, -0.30, 0.60), 0.012))  # "bird beak" fingers of the shadow puppet
# ~30 bulging pockets around the coat (mustard patches with a darker flap), some with stuff peeking out
rnd = random.Random(14)


BELL = ((0.45, 0.137, 0.07), (0.41, 0.148, 0.07), (0.37, 0.16, 0.07), (0.33, 0.172, 0.07), (0.29, 0.184, 0.06), (0.26, 0.195, 0.045), (0.24, 0.2, 0.03))
for z, r, h in BELL:  # bell skirt: stacked flat ellipsoids (flat hem, legs visible below)
    BODY.append(E("coat", "Root", 1, (0, 0, z), (r, r * 0.95, h)))


def coat_r(z):
    return float(np.interp(z, [0.24, 0.27, 0.32, 0.38, 0.44, 0.60], [0.2, 0.19, 0.175, 0.158, 0.14, 0.11]))


for z, n in ((0.28, 7), (0.36, 6), (0.46, 4)):
    off = rnd.uniform(0, 1)
    for k in range(n):
        a = 2 * math.pi * (k + off) / n
        r = coat_r(z) + 0.01
        x, y = r * math.sin(a), -r * math.cos(a)
        bone = "Root" if z < BODY_ROOT_Z else "Body"
        sz = rnd.uniform(0.026, 0.032)
        BODY += [E("pocket", bone, 3, (x, y, z), (sz, sz, sz * 0.95)),
                 E("pocket_d", bone, 4, (x * 1.04, y * 1.04, z + sz * 0.75), (sz * 1.1, sz * 1.1, sz * 0.3), geo=False)]
        if rnd.random() < 0.35:
            key = rnd.choice(("candy", "sticker", "sticker2"))
            BODY.append(E(key, bone, 5, (x * 1.02, y * 1.02, z + sz * 0.95), (0.016, 0.016, 0.02)))
for z in (0.40, 0.47, 0.54):  # coat buttons down the front
    BODY.append(E("button", "Body" if z > BODY_ROOT_Z else "Root", 6, (0.0, -coat_r(z) - 0.004, z), (0.012, 0.008, 0.012)))
EXTRA_BONES = []

# ---------------- weapon: toy pocket flashlight (local: origin = grip, beam -Y, up +Z) ----------------
WEAPON = [
    C("torch", "Prop", 1, (0, 0.06, 0.0), (0, -0.08, 0.0), 0.024),  # body
    E("torch_d", "Prop", 2, (0, 0.065, 0.0), (0.026, 0.012, 0.026)),  # tail cap
    E("torch_d", "Prop", 2, (0, -0.01, 0.0), (0.027, 0.008, 0.027)),  # grip rings
    E("torch_d", "Prop", 2, (0, 0.025, 0.0), (0.027, 0.008, 0.027)),
    C("torch_d", "Prop", 3, (0, -0.03, 0.022), (0, -0.05, 0.022), 0.008),  # switch
    C("torch", "Prop", 2, (0, -0.08, 0.0), (0, -0.135, 0.0), 0.026, 0.045),  # flared head
    E("torch_d", "Prop", 3, (0, -0.137, 0.0), (0.05, 0.012, 0.05)),  # bezel
    E("glow", "Prop", 4, (0, -0.142, 0.0), (0.04, 0.008, 0.04)),  # lens
    E("candy", "Prop", 5, (0.024, 0.01, 0.0), (0.006, 0.02, 0.012), geo=False),  # star sticker on the side
]
MUZZLE = (0, -0.155, 0.0)
WS = 1.4  # flashlight scale (reads from the top-down camera)
for p in WEAPON:
    for key in ("c", "r", "a", "b"):
        if key in p.k:
            p.k[key] = tuple(np.array(p.k[key]) * WS)
    for key in ("ra", "rb"):
        if key in p.k:
            p.k[key] *= WS
MUZZLE = tuple(np.array(MUZZLE) * WS)

def label(parts, X):
    """Highest-priority part containing the point (d < eps), else nearest."""
    D = np.stack([p.d(X) for p in parts], 1)
    pr = np.array([p.prio for p in parts], float)
    inside = D < 0.0025
    score = np.where(inside, pr[None, :] * 10 - D, -1e9)
    best = score.argmax(1)
    none = ~inside.any(1)
    best[none] = D[none].argmin(1)
    return best


def build_shell(parts, name, voxel):
    objs = []
    for p in parts:
        if p.geo:
            objs += p.make()
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.object
    ob.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    m = ob.modifiers.new("R", "REMESH")
    m.mode, m.voxel_size, m.use_smooth_shade = "VOXEL", voxel, True
    bpy.ops.object.modifier_apply(modifier="R")
    return ob


def face_colors(ob, parts):
    me = ob.data
    n = len(me.polygons)
    cen = np.empty(n * 3)
    me.polygons.foreach_get("center", cen)
    lab = label(parts, cen.reshape(-1, 3))
    cols = np.array([PAL[parts[i].key] for i in lab], np.float32)
    lt = np.empty(n, np.int64)
    me.polygons.foreach_get("loop_total", lt)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.repeat(cols, lt, axis=0).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = me.color_attributes.find("Col")
    return lab


def decimate(ob, tris):
    bpy.context.view_layer.objects.active = ob
    m = ob.modifiers.new("D", "DECIMATE")
    m.ratio = 1.0
    m.use_collapse_triangulate = True
    # triangles after remesh = 2 * quads
    cur = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    m.ratio = min(1.0, tris / cur * 0.995)
    bpy.ops.object.modifier_apply(modifier="D")
    m = ob.modifiers.new("T", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier="T")
    return len(ob.data.polygons)


def emit_mat(name, use_alpha):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for nd in list(nt.nodes):
        nt.nodes.remove(nd)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    nt.links.new(ca.outputs["Alpha" if use_alpha else "Color"], em.inputs["Color"])
    nt.links.new(em.outputs[0], out.inputs[0])
    return mat


# ================================ body ================================
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
high = build_shell(BODY, "FibHigh", VOXEL)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Fib"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("FIB body tris", TRIS)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
bpy.ops.object.mode_set(mode="OBJECT")
low.data.uv_layers[0].name = "UVMap"

# bake high -> low: RGB then gloss
scene.render.engine = "CYCLES"
scene.cycles.samples = 1
scene.cycles.device = "CPU"
RES = 1024
imgs = {}
for tag, use_a in (("rgb", False), ("a", True)):
    high.data.materials.clear()
    high.data.materials.append(emit_mat("E" + tag, use_a))
    img = bpy.data.images.new("bake_" + tag, RES, RES, float_buffer=True)
    low.data.materials.clear()
    lm = bpy.data.materials.new("L" + tag)
    lm.use_nodes = True
    tn = lm.node_tree.nodes.new("ShaderNodeTexImage")
    tn.image = img
    lm.node_tree.nodes.active = tn
    low.data.materials.append(lm)
    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.bake(type="EMIT", use_selected_to_active=True, cage_extrusion=0.012, max_ray_distance=0.03, margin=8)
    px = np.empty(RES * RES * 4, np.float32)
    img.pixels.foreach_get(px)
    imgs[tag] = px.reshape(RES, RES, 4)
rgb = imgs["rgb"][..., :3]
rgb = np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * np.power(np.clip(rgb, 0, None), 1 / 2.4) - 0.055)
tex = np.dstack([np.clip(rgb, 0, 1), np.clip(imgs["a"][..., 0], 0, 1)])
timg = bpy.data.images.new("fib_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "fib_tex.png")
timg.filepath_raw = tex_path
timg.file_format = "PNG"
timg.save()
bpy.data.objects.remove(high, do_unlink=True)

# low: vertex colour fallback (flat per face, alpha = gloss), material FighterVC with the texture
lab = face_colors(low, BODY)
me = low.data
mat = bpy.data.materials.new("FighterVC")
mat.use_nodes = True
nt = mat.node_tree
bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
tn = nt.nodes.new("ShaderNodeTexImage")
tn.image = bpy.data.images.load(tex_path, check_existing=True)
tn.image.alpha_mode = "CHANNEL_PACKED"
nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.45
me.materials.clear()
me.materials.append(mat)
for p in me.polygons:
    p.use_smooth = True

# rig
BONE_DEF = [
    ("Root", None, (0, 0, 0), (0, 0, WAIST_Z)),
    ("Body", "Root", (0, 0, WAIST_Z), (0, 0, NECK_Z)),
    ("Head", "Body", (0, 0, NECK_Z), (0, 0, HEAD_TOP)),
    ("ArmR", "Body", SH[-1], HA[-1]),
    ("ArmL", "Body", SH[1], HA[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", HA[-1], tuple(np.array(HA[-1]) + (0, -0.10, 0))),
    ("EyeR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("EyeL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("LidR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("LidL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
] + EXTRA_BONES
arm_data = bpy.data.armatures.new("FibRig")
arm = bpy.data.objects.new("Rig", arm_data)
scene.collection.objects.link(arm)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for bn, parent, hd, tl in BONE_DEF:
    eb = arm_data.edit_bones.new(bn)
    eb.head, eb.tail = Vector(hd), Vector(tl)
    if parent:
        eb.parent = arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode="OBJECT")
V = np.empty(len(me.vertices) * 3)
me.vertices.foreach_get("co", V)
V = V.reshape(-1, 3)
vlab = label(BODY, V)
groups = {b[0]: low.vertex_groups.new(name=b[0]) for b in BONE_DEF}
by = {}
for i, li in enumerate(vlab):
    bn = BODY[li].bone
    if bn.startswith("Eye"):  # painted eye decals ride the head; eye bones are pivots for the animator
        bn = "Head"
    if bn == "Body" and V[i, 2] < BODY_ROOT_Z:
        bn = "Root"
    by.setdefault(bn, []).append(i)
for bn, idx in by.items():
    groups[bn].add(idx, 1.0, "REPLACE")
low.parent = arm
mod = low.modifiers.new("Armature", "ARMATURE")
mod.object = arm


def export(objs, base, types):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types=types, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)


export([arm, low], os.path.join(OUT, "fib_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "FibWeapon", 0.004)
face_colors(gun, WEAPON)
gtris = decimate(gun, 1180)
face_colors(gun, WEAPON) if "Col" not in gun.data.color_attributes else None
gm = bpy.data.materials.get("FighterVC")
gun.data.materials.clear()
gvc = bpy.data.materials.new("FighterVC_W")
gvc.use_nodes = True
vcn = gvc.node_tree.nodes.new("ShaderNodeVertexColor")
vcn.layer_name = "Col"
gb = next(n for n in gvc.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
gvc.node_tree.links.new(vcn.outputs["Color"], gb.inputs["Base Color"])
gun.data.materials.append(gvc)
for p in gun.data.polygons:
    p.use_smooth = True
muzzle = bpy.data.objects.new("Muzzle", None)
scene.collection.objects.link(muzzle)
muzzle.location = MUZZLE
muzzle.parent = gun
export([gun, muzzle], os.path.join(OUT, "fib_weapon"), {"MESH", "EMPTY"})
print("FIB done: body", TRIS, "weapon", gtris, "out", OUT)
