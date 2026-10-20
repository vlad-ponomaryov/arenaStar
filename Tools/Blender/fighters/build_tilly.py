# Tilly (9, the youngest: balloon-animal summoner), base 3D model (Docs/Briefs/A-Roster3D.md, wave 2). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_tilly.py -- [out_dir]
# Same method as build_tilly.py: analytic primitives -> voxel remesh -> per-face colour from the primitive list (label-only
# decals: eyes, freckles, strawberry seeds, overall straps) -> decimate -> smart UV -> Cycles bake high->low (RGB = albedo,
# A = gloss) into tilly_tex.png 1024 -> rig with the 12 FighterBone bones + Extra (balloon bunch), rigid weights -> FBX/GLB.
# Whole figure scaled by K = 0.9 (she is smaller than Theo); the balloon bunch rises above the head (top view: cluster of
# pastel circles). Weapon (balloon pump): tilly_weapon.fbx/.glb, vertex colour "Col" (alpha = gloss), origin = grip under the
# right hand (hang on Prop without offset), nozzle along -Y, empty "Muzzle" at the little balloon on the nozzle.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "tilly")
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]


PAL = {  # sRGB hex, gloss
    "skin": hexc("#F6D3BC", 0.15), "blush": hexc("#F3A6A0", 0.15), "hair": hexc("#E0A84A", 0.35),
    "hair_d": hexc("#C38A32", 0.3), "tie": hexc("#F58BB0", 0.6), "shirt": hexc("#FFF4E2", 0.15),
    "leaf": hexc("#6CC25E", 0.35), "over": hexc("#F2667A", 0.35), "over_d": hexc("#D9506A", 0.3),
    "seed": hexc("#FFE27A", 0.5), "button": hexc("#FFE27A", 0.8), "shoe": hexc("#9FE3CF", 0.45),
    "sole": hexc("#F7F7F5", 0.3), "white": hexc("#F7F7F5", 0.6), "iris": hexc("#6B8F3E", 1.0),
    "pupil": hexc("#1A1A1A", 1.0), "mouth": hexc("#B5524A", 0.3), "freckle": hexc("#D99A72", 0.15),
    "string": hexc("#F7F2EA", 0.2), "pack": hexc("#9FE3CF", 0.4),
    "b_lilac": hexc("#C9B3F2", 0.95), "b_mint": hexc("#A8E6CF", 0.95), "b_peach": hexc("#FFC8A2", 0.95),
    "b_lemon": hexc("#FFF1A0", 0.95), "b_pink": hexc("#FFB3D1", 0.95), "b_lime": hexc("#D4F0A0", 0.95),
    "pump": hexc("#C9B3F2", 0.7), "pump_d": hexc("#A58ADB", 0.6),
}
K = 0.9  # global scale (9-year-old, a bit smaller than Theo)
ROOT_Z = 0.45 * K

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

# ==== PARTS (Blender: Z up, faces -Y, character right = -X); written at height 1.0, scaled by K below ====
SH = {-1: (-0.145, 0.0, 0.60), 1: (0.145, 0.0, 0.60)}
EL = {-1: (-0.19, 0.02, 0.47), 1: (0.13, -0.10, 0.50)}
HA = {-1: (-0.12, -0.10, 0.43), 1: (-0.04, -0.20, 0.46)}
HIP = {-1: (-0.06, 0.0, 0.36), 1: (0.06, 0.0, 0.36)}
AN = {-1: (-0.07, 0.0, 0.09), 1: (0.07, 0.0, 0.09)}
EYE = {s: (0.066 * s, -0.142, 0.77) for s in (-1, 1)}
KNOT = (0.0, 0.13, 0.66)  # balloon strings tie to the little backpack
BALL = [  # (centre, radius, colour) - the bunch above the head, slightly back
    ((0.02, 0.10, 1.32), 0.105, "b_pink"), ((0.17, 0.12, 1.26), 0.095, "b_mint"),
    ((-0.13, 0.13, 1.25), 0.095, "b_lemon"), ((0.09, 0.22, 1.40), 0.09, "b_lilac"),
    ((-0.07, 0.24, 1.38), 0.09, "b_peach"), ((0.06, -0.03, 1.21), 0.085, "b_lime"),
]
BODY = [
    E("skin", "Head", 1, (0, 0, 0.79), (0.17, 0.156, 0.163)),
    E("skin", "Head", 1, (0, -0.02, 0.72), (0.125, 0.12, 0.08)),  # cheeks
    E("hair", "Head", 2, (0, 0.035, 0.83), (0.18, 0.162, 0.165)),
    E("hair", "Head", 2, (0, 0.06, 0.75), (0.15, 0.11, 0.10)),  # back
    E("hair", "Head", 3, (-0.05, -0.095, 0.905), (0.11, 0.07, 0.055)),  # side-swept bangs
    E("hair", "Head", 3, (0.07, -0.09, 0.915), (0.09, 0.07, 0.05)),
    E("skin", "Head", 2, (-0.168, 0.0, 0.77), (0.022, 0.03, 0.035)),  # ears
    E("skin", "Head", 2, (0.168, 0.0, 0.77), (0.022, 0.03, 0.035)),
    E("skin", "Head", 3, (0, -0.16, 0.75), (0.016, 0.014, 0.016)),  # button nose
    C("skin", "Head", 1, (0, 0, 0.60), (0, 0, 0.68), 0.042),  # neck
    E("shirt", "Body", 1, (0, 0, 0.53), (0.135, 0.105, 0.13)),
    E("leaf", "Body", 3, (0, 0.012, 0.628), (0.115, 0.098, 0.035)),  # strawberry-leaf collar
    E("over", "Root", 1, (0, 0, 0.40), (0.14, 0.11, 0.09)),  # overall bottom (round "strawberry" belly)
    E("over", "Body", 2, (0, -0.055, 0.52), (0.105, 0.07, 0.085)),  # bib
    E("pack", "Body", 2, (0, 0.10, 0.53), (0.09, 0.05, 0.08)),  # little backpack (balloon knot)
    E("button", "Body", 4, (-0.06, -0.12, 0.585), (0.018, 0.014, 0.018)),
    E("button", "Body", 4, (0.06, -0.12, 0.585), (0.018, 0.014, 0.018)),
    E("blush", "Head", 6, (-0.1, -0.13, 0.72), (0.03, 0.03, 0.018), geo=False),
    E("blush", "Head", 6, (0.1, -0.13, 0.72), (0.03, 0.03, 0.018), geo=False),
    E("white", "Head", 6, EYE[-1], (0.044, 0.02, 0.054)),
    E("white", "Head", 6, EYE[1], (0.044, 0.02, 0.054)),
    E("mouth", "Head", 7, (0.0, -0.152, 0.703), (0.026, 0.03, 0.01), geo=False),
    E("over_d", "Root", 3, (0, 0, 0.40), (0.142, 0.112, 0.012), geo=False),  # waist seam
]
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    side = "R" if s < 0 else "L"
    BODY += [E("iris", "Eye" + side, 8, (ex, ey - 0.02, ez - 0.004), (0.03, 0.012, 0.037), geo=False),
             E("pupil", "Eye" + side, 9, (ex, ey - 0.024, ez - 0.004), (0.015, 0.01, 0.02), geo=False),
             E("white", "Eye" + side, 10, (ex + 0.012 * s, ey - 0.026, ez + 0.017), (0.008, 0.01, 0.009), geo=False)]
    for fx, fz in ((0.055, 0.728), (0.08, 0.738), (0.07, 0.714), (0.095, 0.722)):
        BODY.append(E("freckle", "Head", 8, (fx * s, -0.14, fz), (0.0075, 0.03, 0.0075), geo=False))
    # pigtail bunches high on the sides + hair ties
    BODY += [E("hair", "Head", 2, (0.205 * s, 0.03, 0.86), (0.065, 0.06, 0.085)),
             E("hair_d", "Head", 2, (0.24 * s, 0.04, 0.80), (0.05, 0.05, 0.07)),
             E("tie", "Head", 4, (0.172 * s, 0.025, 0.885), (0.03, 0.035, 0.035))]
    # overall straps (label only)
    BODY += [P("c", "over", "Body", 5, False, a=(0.06 * s, -0.11, 0.59), b=(0.085 * s, 0.0, 0.66), ra=0.02, rb=0.02),
             P("c", "over", "Body", 5, False, a=(0.085 * s, 0.0, 0.66), b=(0.06 * s, 0.10, 0.55), ra=0.02, rb=0.02)]
    BODY += [C("shirt", "Arm" + side, 2, SH[s], EL[s], 0.048, 0.04),
             C("skin", "Arm" + side, 2, EL[s], HA[s], 0.034, 0.03),
             E("shirt", "Arm" + side, 2, SH[s], (0.058, 0.058, 0.055)),
             E("skin", "Arm" + side, 4, HA[s], (0.036, 0.04, 0.035)),
             C("over", "Leg" + side, 1, HIP[s], AN[s], 0.055, 0.046),
             E("shirt", "Leg" + side, 3, (AN[s][0], 0.0, 0.125), (0.054, 0.054, 0.02)),  # rolled cuff
             E("shoe", "Leg" + side, 3, (AN[s][0], -0.03, 0.055), (0.056, 0.09, 0.05)),
             E("sole", "Leg" + side, 4, (AN[s][0], -0.03, 0.006), (0.06, 0.094, 0.014))]
for sx, sz in ((-0.06, 0.37), (0.0, 0.34), (0.07, 0.38), (-0.03, 0.43), (0.05, 0.45), (-0.09, 0.42),
               (0.04, 0.52), (-0.04, 0.55), (0.0, 0.49)):
    BODY.append(E("seed", "Body" if sz > 0.46 else "Root", 6, (sx, -0.13, sz), (0.01, 0.06, 0.014), geo=False))
# balloon bunch on Extra: strings from the backpack knot to each balloon, a knot nub under each balloon
for c, r, col in BALL:
    bot = (c[0], c[1], c[2] - r * 1.05)
    BODY += [E(col, "Extra", 3, c, (r, r, r * 1.12)),
             E(col, "Extra", 3, bot, (0.016, 0.016, 0.018)),
             C("string", "Extra", 2, KNOT, bot, 0.0065)]
    BODY.append(E("white", "Extra", 5, (c[0] - r * 0.35, c[1] - r * 0.55, c[2] + r * 0.45), (0.022, 0.03, 0.03), geo=False))
BODY.append(E("tie", "Extra", 3, KNOT, (0.022, 0.022, 0.022)))

# ---- weapon: balloon pump (local: origin = grip, nozzle -Y, up +Z) ----
WEAPON = [
    C("pump_d", "Prop", 2, (0, 0.0, -0.05), (0, 0.0, 0.03), 0.02),  # grip
    C("pump", "Prop", 1, (0, 0.07, 0.055), (0, -0.17, 0.055), 0.04),  # barrel
    E("seed", "Prop", 3, (0, -0.04, 0.055), (0.043, 0.012, 0.043)),  # lemon rings
    E("b_mint", "Prop", 3, (0, -0.12, 0.055), (0.043, 0.012, 0.043)),
    C("pump_d", "Prop", 2, (0, 0.07, 0.055), (0, 0.15, 0.055), 0.012),  # plunger rod
    C("b_mint", "Prop", 3, (-0.06, 0.16, 0.055), (0.06, 0.16, 0.055), 0.018),  # T handle
    C("b_peach", "Prop", 2, (0, -0.17, 0.055), (0, -0.23, 0.055), 0.026, 0.011),  # nozzle
    E("b_pink", "Prop", 3, (0, -0.30, 0.06), (0.05, 0.065, 0.05)),  # little balloon on the nozzle
    E("white", "Prop", 5, (-0.02, -0.32, 0.085), (0.012, 0.015, 0.012), geo=False),
]
MUZZLE = (0, -0.37, 0.06)


def _scale(parts, k):
    for p in parts:
        for key in ("c", "r", "a", "b"):
            if key in p.k:
                p.k[key] = tuple(np.array(p.k[key]) * k)
        for key in ("ra", "rb"):
            if key in p.k:
                p.k[key] *= k


_scale(BODY, K)
_scale(WEAPON, K)
MUZZLE = tuple(np.array(MUZZLE) * K)
for _d in (SH, EL, HA, HIP, AN, EYE):
    for _s in list(_d):
        _d[_s] = tuple(np.array(_d[_s]) * K)
KNOT = tuple(np.array(KNOT) * K)


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
high = build_shell(BODY, "TillyHigh", 0.0045)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Tilly"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("TILLY body tris", TRIS)
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
timg = bpy.data.images.new("tilly_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "tilly_tex.png")
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
# rig (coordinates already scaled by K)
BONE_DEF = [
    ("Root", None, (0, 0, 0), (0, 0, 0.40 * K)),
    ("Body", "Root", (0, 0, 0.40 * K), (0, 0, 0.64 * K)),
    ("Head", "Body", (0, 0, 0.64 * K), (0, 0, 1.0 * K)),
    ("ArmR", "Body", SH[-1], HA[-1]),
    ("ArmL", "Body", SH[1], HA[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", HA[-1], tuple(np.array(HA[-1]) + (0, -0.10, 0))),
    ("EyeR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("EyeL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("LidR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("LidL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("Extra", "Body", KNOT, tuple(np.array(KNOT) + (0, 0, 0.5 * K))),
]
arm_data = bpy.data.armatures.new("TillyRig")
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
    if bn == "Body" and V[i, 2] < ROOT_Z:
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


export([arm, low], os.path.join(OUT, "tilly_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "TillyWeapon", 0.004)
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
export([gun, muzzle], os.path.join(OUT, "tilly_weapon"), {"MESH", "EMPTY"})
print("TILLY done: body", TRIS, "weapon", gtris, "out", OUT)
