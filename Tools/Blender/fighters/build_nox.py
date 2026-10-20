# Nox (13, ninja in wool socks / stealth scout), base 3D model (Docs/Briefs/A-Roster3D.md, Docs/Roster-v4.md 6.3). Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_nox.py -- [out_dir]
# Pipeline = build_lark.py (analytic primitives -> voxel remesh -> per-face colour labels -> decimate -> smart UV -> Cycles
# bake high->low, RGB = albedo, A = gloss -> nox_tex.png 1024 -> 12 FighterBone bones + Tail (scarf), rigid weights).
# Look: night-plum hoodie with the hood up, long mint knitted scarf trailing behind as a tail (shows direction), striped
# wool socks over sneakers. Weapon: a fan of three soft felt stars in the right hand (nox_weapon.fbx/.glb, origin = grip,
# throw direction -Y, empty "Muzzle" in front of the middle star). No Naruto/Ninjago cues (no orange, forehead plate,
# whisker marks, spiky blond hair, face mask, colour-element suits).
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "nox")
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]




PAL = {  # sRGB hex, gloss
    "skin": hexc("#EBC3A2", 0.15), "hair": hexc("#2E2433", 0.35), "hood": hexc("#4B3A6B", 0.2),
    "hood_d": hexc("#3B2D56", 0.18), "hood_in": hexc("#2F2445", 0.1), "scarf": hexc("#9EE6C8", 0.12),
    "scarf_d": hexc("#6B5A8E", 0.12), "pants": hexc("#3A3550", 0.1), "sock": hexc("#9EE6C8", 0.1),
    "sock_d": hexc("#4B3A6B", 0.1), "shoe": hexc("#2F2840", 0.4), "sole": hexc("#F2F0EA", 0.35),
    "string": hexc("#9EE6C8", 0.3), "white": hexc("#F7F7F5", 0.6), "iris": hexc("#5A4632", 1.0),
    "pupil": hexc("#1A1A1A", 1.0), "mouth": hexc("#8E4A44", 0.3), "brow": hexc("#2E2433", 0.3),
    "felt": hexc("#9EE6C8", 0.08), "felt2": hexc("#C9B6EE", 0.08), "felt3": hexc("#FFE08A", 0.08),
    "stitch": hexc("#FFFFFF", 0.1), "cheek": hexc("#F0A79A", 0.1),
}
VOXEL = 0.0045
WAIST_Z, NECK_Z, HEAD_TOP, BODY_ROOT_Z = 0.38, 0.61, 0.97, 0.43

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

# ---------------- body (Blender: Z up, faces -Y, character's right = -X) ----------------
SH = {-1: (-0.14, 0.0, 0.575), 1: (0.14, 0.0, 0.575)}
EL = {-1: (-0.18, -0.05, 0.46), 1: (0.19, 0.0, 0.45)}
HA = {-1: (-0.12, -0.17, 0.47), 1: (0.17, -0.06, 0.36)}  # right: star fan raised forward; left: down, slightly forward
HIP = {-1: (-0.06, 0.0, 0.34), 1: (0.06, 0.0, 0.34)}
AN = {-1: (-0.075, 0.0, 0.085), 1: (0.075, 0.0, 0.085)}
EYE = {s: (0.058 * s, -0.142, 0.745) for s in (-1, 1)}
HEAD = (0, 0, 0.76)
BODY = [
    E("skin", "Head", 2, HEAD, (0.15, 0.142, 0.148)),
    E("skin", "Head", 2, (0, -0.02, 0.69), (0.115, 0.11, 0.07)),
    # hood: shell over the head, open in front (face has higher priority), soft point at the back-top
    E("hood", "Head", 1, (0, 0.025, 0.775), (0.178, 0.17, 0.182)),
    E("hood", "Head", 1, (0, 0.11, 0.86), (0.07, 0.07, 0.07)),
    E("hood_d", "Head", 3, (0, -0.12, 0.77), (0.15, 0.04, 0.16), geo=False),  # hood rim
    E("skin", "Head", 4, (0, -0.075, 0.735), (0.122, 0.09, 0.118)),  # face in the opening
    E("hair", "Head", 5, (0, -0.125, 0.838), (0.11, 0.05, 0.035)),  # dark fringe under the hood
    E("hair", "Head", 5, (-0.05, -0.138, 0.825), (0.045, 0.03, 0.035)),
    E("skin", "Head", 5, (0, -0.16, 0.718), (0.015, 0.016, 0.018)),  # nose
    E("white", "Head", 6, EYE[-1], (0.036, 0.02, 0.034)),  # half-closed, serious eyes
    E("white", "Head", 6, EYE[1], (0.036, 0.02, 0.034)),
    E("brow", "Head", 7, (-0.058, -0.15, 0.785), (0.034, 0.03, 0.007), geo=False),
    E("brow", "Head", 7, (0.058, -0.15, 0.785), (0.034, 0.03, 0.007), geo=False),
    E("mouth", "Head", 7, (0, -0.152, 0.668), (0.018, 0.03, 0.006), geo=False),  # small straight mouth
    E("cheek", "Head", 7, (-0.085, -0.13, 0.69), (0.022, 0.03, 0.014), geo=False),
    E("cheek", "Head", 7, (0.085, -0.13, 0.69), (0.022, 0.03, 0.014), geo=False),
    C("skin", "Head", 1, (0, 0, 0.56), (0, 0, 0.64), 0.04),  # neck
    # hoodie
    E("hood", "Body", 1, (0, 0, 0.49), (0.155, 0.12, 0.13)),
    E("hood_d", "Body", 3, (0, -0.105, 0.43), (0.09, 0.04, 0.045)),  # kangaroo pocket
    E("hood_d", "Root", 2, (0, 0, 0.385), (0.152, 0.117, 0.03)),  # hem band
    E("pants", "Root", 1, (0, 0, 0.345), (0.125, 0.095, 0.065)),
    C("string", "Body", 4, (-0.03, -0.13, 0.55), (-0.035, -0.135, 0.50), 0.006),  # drawstrings
    C("string", "Body", 4, (0.03, -0.13, 0.55), (0.035, -0.135, 0.50), 0.006),
]
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    eb = "Eye" + ("R" if s < 0 else "L")
    BODY += [E("iris", eb, 8, (ex, ey - 0.02, ez - 0.006), (0.024, 0.012, 0.026), geo=False),
             E("pupil", eb, 9, (ex, ey - 0.024, ez - 0.006), (0.012, 0.01, 0.014), geo=False),
             E("hood_in", eb, 9, (ex, ey - 0.02, ez + 0.022), (0.04, 0.03, 0.014), geo=False),  # heavy upper lid line
             E("white", eb, 10, (ex + 0.012 * s, ey - 0.026, ez + 0.004), (0.006, 0.01, 0.006), geo=False)]
    side = "R" if s < 0 else "L"
    BODY += [C("hood", "Arm" + side, 2, SH[s], EL[s], 0.048, 0.042),
             C("hood", "Arm" + side, 2, EL[s], HA[s], 0.042, 0.038),
             E("hood", "Arm" + side, 2, SH[s], (0.058, 0.058, 0.055)),
             E("hood_d", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.8 + np.array(EL[s]) * 0.2), (0.04, 0.04, 0.04)),
             E("skin", "Arm" + side, 4, HA[s], (0.034, 0.038, 0.034)),
             C("pants", "Leg" + side, 1, HIP[s], AN[s], 0.052, 0.046),
             C("sock", "Leg" + side, 2, (AN[s][0], 0.0, 0.07), (AN[s][0], 0.0, 0.2), 0.05, 0.05),  # wool sock cuffs
             E("sock", "Leg" + side, 3, (AN[s][0], 0.0, 0.205), (0.056, 0.056, 0.022)),  # rolled top
             E("shoe", "Leg" + side, 4, (AN[s][0], -0.03, 0.045), (0.054, 0.09, 0.04)),
             E("sole", "Leg" + side, 5, (AN[s][0], -0.03, 0.006), (0.058, 0.094, 0.014))]
    for z in (0.10, 0.14, 0.18):  # sock stripes (decal rings)
        BODY.append(E("sock_d", "Leg" + side, 4, (AN[s][0], 0.0, z), (0.07, 0.07, 0.011), geo=False))
# long knitted scarf: thick wrap around the neck + a long tail trailing behind (Tail bone), stripes, fringe
BODY += [E("scarf", "Body", 5, (0, 0.0, 0.605), (0.115, 0.105, 0.05)),
         E("scarf", "Body", 5, (0.02, -0.01, 0.57), (0.1, 0.095, 0.035))]
SCARF = [(0.03, 0.09, 0.61), (0.05, 0.19, 0.60), (0.065, 0.29, 0.575), (0.075, 0.39, 0.54), (0.08, 0.48, 0.505),
         (0.083, 0.56, 0.475)]
for i in range(len(SCARF) - 1):
    key = "scarf" if i % 2 == 0 else "scarf_d"
    BODY.append(C(key, "Tail", 5, SCARF[i], SCARF[i + 1], 0.04, 0.038))
for fx in (-0.025, 0.0, 0.025):  # fringe
    a = np.array(SCARF[-1])
    BODY.append(C("scarf", "Tail", 6, tuple(a), tuple(a + (fx, 0.055, -0.02)), 0.009))
EXTRA_BONES = [("Tail", "Body", SCARF[0], SCARF[-1])]

# ---------------- weapon: fan of three felt stars (local: origin = grip, throw -Y, up +Z) ----------------
WEAPON = [C("pants", "Prop", 1, (0, 0.0, -0.02), (0, 0.0, 0.03), 0.014)]  # tiny dark ring the stars are held by
STARS = (((-0.05, -0.02, 0.05), "felt2", 0.25), ((0.0, -0.035, 0.075), "felt", 0.0), ((0.05, -0.02, 0.05), "felt3", -0.25))
for (cx, cy, cz), key, tilt in STARS:
    c = np.array((cx, cy, cz))
    WEAPON.append(E(key, "Prop", 2, tuple(c), (0.022, 0.014, 0.022)))
    WEAPON.append(E("stitch", "Prop", 4, tuple(c + (0, -0.012, 0)), (0.006, 0.006, 0.006), geo=False))
    for k in range(5):  # 5 puffy arms in the X-Z plane
        a = tilt + 2 * math.pi * k / 5
        tip = c + 0.058 * np.array((math.sin(a), 0.0, math.cos(a)))
        WEAPON.append(C(key, "Prop", 2, tuple(c), tuple(tip), 0.016, 0.006))
        WEAPON.append(E("stitch", "Prop", 3, tuple(c * 0.5 + tip * 0.5 + (0, -0.014, 0)), (0.004, 0.005, 0.004), geo=False))
MUZZLE = (0, -0.06, 0.075)
WS = 1.3  # star fan scale (reads from the top-down camera)
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
high = build_shell(BODY, "NoxHigh", VOXEL)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Nox"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("NOX body tris", TRIS)
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
timg = bpy.data.images.new("nox_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "nox_tex.png")
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
arm_data = bpy.data.armatures.new("NoxRig")
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


export([arm, low], os.path.join(OUT, "nox_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "NoxWeapon", 0.004)
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
export([gun, muzzle], os.path.join(OUT, "nox_weapon"), {"MESH", "EMPTY"})
print("NOX done: body", TRIS, "weapon", gtris, "out", OUT)
