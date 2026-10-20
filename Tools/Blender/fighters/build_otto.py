# Otto IX (10, boy-king "crowned on a Tuesday"; team booster), base 3D model (Docs/Briefs/A-Roster3D.md, wave 2;
# look: Docs/Roster-v3.md §5). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_otto.py -- [out_dir]
# Method = build_lark.py: analytic primitives -> voxel remesh -> per-face colour from the primitive list (label-only decals:
# ermine spots, jewels, eyes) -> decimate to budget -> smart UV -> Cycles bake high->low (RGB = albedo, A = gloss) into
# otto_tex.png 1024 -> 12 FighterBone bones + Tail (the long robe train), rigid weights -> FBX/GLB.
# Weapon (sceptre-rattle): otto_weapon.fbx/.glb, vertex colour "Col" (alpha = gloss), origin = grip under the right hand
# (hang on Prop without offset), held upright leaning forward (-Y), empty "Muzzle" above the rattle (wax-seal throws).
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "otto")
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]




PAL = {  # sRGB hex, gloss
    "skin": hexc("#F4CDB0", 0.15), "blush": hexc("#EBA08C", 0.15), "hair": hexc("#7A4A2A", 0.35),
    "gold": hexc("#E3B53A", 0.9), "gold_d": hexc("#B8862A", 0.8), "gem_v": hexc("#7B3FB0", 1.0),
    "gem_t": hexc("#2FB8A8", 1.0), "velvet": hexc("#6A2C8E", 0.45), "velvet_d": hexc("#4E1F6B", 0.4),
    "ermine": hexc("#F7F4EC", 0.1), "spot": hexc("#26222A", 0.2), "tunic": hexc("#F3EBD8", 0.25),
    "stock": hexc("#F8F6F0", 0.2), "shoe": hexc("#4E1F6B", 0.6), "white": hexc("#F7F7F5", 0.6),
    "iris": hexc("#5A7A3A", 1.0), "pupil": hexc("#1A1A1A", 1.0), "mouth": hexc("#A0453A", 0.3),
}


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
SH = {-1: (-0.145, 0.0, 0.59), 1: (0.145, 0.0, 0.59)}
EL = {-1: (-0.20, 0.0, 0.46), 1: (0.21, -0.06, 0.55)}
HA = {-1: (-0.13, -0.10, 0.42), 1: (0.20, -0.14, 0.66)}  # left hand raised: "by royal decree!"
HIP = {-1: (-0.06, 0.0, 0.35), 1: (0.06, 0.0, 0.35)}
AN = {-1: (-0.075, 0.0, 0.09), 1: (0.075, 0.0, 0.09)}
EYE = {s: (0.062 * s, -0.142, 0.775) for s in (-1, 1)}
TILT = 0.12  # oversized crown slipped to one side: z += TILT * x


def cz(x, z):
    return z + TILT * x


BODY = [
    E("skin", "Head", 1, (0, 0, 0.79), (0.165, 0.152, 0.16)),
    E("skin", "Head", 1, (0, -0.02, 0.72), (0.12, 0.12, 0.08)),
    E("hair", "Head", 2, (0, 0.035, 0.82), (0.175, 0.16, 0.15)),  # soft curls
    E("hair", "Head", 3, (0, -0.12, 0.865), (0.13, 0.05, 0.04)),  # fringe
    E("skin", "Head", 2, (-0.168, 0.0, 0.775), (0.024, 0.032, 0.036)),
    E("skin", "Head", 2, (0.168, 0.0, 0.775), (0.024, 0.032, 0.036)),
    E("skin", "Head", 3, (0, -0.158, 0.752), (0.018, 0.016, 0.018)),
    C("skin", "Head", 1, (0, 0, 0.59), (0, 0, 0.68), 0.045),
    # crown (too big, slips down to the right brow)
    E("velvet", "Head", 3, (0.02, 0.0, cz(0.02, 0.955)), (0.16, 0.15, 0.075)),
    E("gold", "Head", 5, (0.02, 0.0, cz(0.02, 0.925)), (0.19, 0.18, 0.045)),
    E("ermine", "Head", 6, (0.02, 0.0, cz(0.02, 0.888)), (0.197, 0.187, 0.02)),
    E("gold", "Head", 6, (0.02, 0.0, cz(0.02, 1.02)), (0.03, 0.03, 0.03)),  # top orb
    E("mouth", "Head", 7, (0.0, -0.152, 0.703), (0.03, 0.03, 0.01), geo=False),
    E("white", "Head", 6, EYE[-1], (0.04, 0.02, 0.048)),
    E("white", "Head", 6, EYE[1], (0.04, 0.02, 0.048)),
    # tunic + ermine shoulder cape + velvet robe
    E("tunic", "Body", 1, (0, 0, 0.51), (0.14, 0.11, 0.15)),
    E("ermine", "Body", 4, (0, 0.01, 0.625), (0.19, 0.15, 0.06)),  # ermine capelet
    E("velvet", "Body", 2, (0, 0.08, 0.36), (0.19, 0.095, 0.31)),  # robe back, one smooth fall to the floor
    E("velvet_d", "Root", 1, (0, 0.0, 0.34), (0.12, 0.09, 0.07)),  # breeches
    E("gold", "Root", 4, (0, -0.0, 0.40), (0.142, 0.108, 0.018)),  # gold sash belt
    # train: three times longer than him in spirit, ~1.15 in practice (longest top-view trail of the roster)
    C("velvet", "Tail", 2, (0, 0.16, 0.10), (0, 0.42, 0.04), 0.09, 0.05),
] + [E("velvet", "Tail", 2, (0, y, 0.035 - 0.012 * (y - 0.3)), (0.2 + 0.09 * (y - 0.3), 0.16, 0.035 - 0.01 * (y - 0.3)))
     for y in np.linspace(0.32, 1.02, 9)] + [  # smooth widening wedge
    E("ermine", "Tail", 3, (0, 1.12, 0.028), (0.24, 0.11, 0.03)),  # ermine hem
]
for k in range(10):  # crown points with gold balls
    a = 2 * np.pi * (k + 0.5) / 10
    x, y = 0.02 + 0.175 * np.sin(a), -0.165 * np.cos(a)
    tx, ty = 0.02 + 0.19 * np.sin(a), -0.18 * np.cos(a)
    BODY += [C("gold", "Head", 5, (x, y, cz(x, 0.95)), (tx, ty, cz(tx, 1.03)), 0.026, 0.012),
             E("gold", "Head", 6, (tx, ty, cz(tx, 1.035)), (0.017, 0.017, 0.017))]
for k, key in enumerate(("gem_t", "gem_v", "gem_t", "gem_v", "gem_t")):  # jewels on the band (labels)
    a = (k - 2) * 0.45
    x, y = 0.02 + 0.19 * np.sin(a), -0.18 * np.cos(a)
    BODY.append(E(key, "Head", 7, (x, y, cz(x, 0.93)), (0.022, 0.03, 0.022), geo=False))
rs = np.random.default_rng(9)
for _ in range(40):  # ermine spots (labels) on the capelet: sides and back only (nothing under the face)
    th = rs.uniform(-0.15 * np.pi, 1.15 * np.pi)
    BODY.append(E("spot", "Body", 7, (0.17 * np.cos(th), 0.01 + 0.135 * np.sin(th), 0.64 + rs.uniform(-0.02, 0.02)),
                  (0.008, 0.008, 0.016), geo=False))
for _ in range(24):
    BODY.append(E("spot", "Tail", 7, (rs.uniform(-0.2, 0.2), rs.uniform(1.06, 1.2), 0.05), (0.01, 0.014, 0.04), geo=False))
for k in range(12):
    a = 2 * np.pi * k / 12
    x = 0.02 + 0.197 * np.sin(a)
    BODY.append(E("spot", "Head", 7, (x, -0.187 * np.cos(a), cz(x, 0.888)), (0.009, 0.009, 0.014), geo=False))
for z in (0.46, 0.51, 0.56):  # gold buttons on the tunic
    BODY.append(E("gold", "Body", 5, (0, -0.108, z), (0.016, 0.012, 0.016)))
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    eb = "Eye" + ("R" if s < 0 else "L")
    BODY += [E("iris", eb, 8, (ex, ey - 0.02, ez - 0.004), (0.027, 0.012, 0.033), geo=False),
             E("pupil", eb, 9, (ex, ey - 0.024, ez - 0.004), (0.013, 0.01, 0.017), geo=False),
             E("white", eb, 10, (ex + 0.012 * s, ey - 0.026, ez + 0.016), (0.007, 0.01, 0.008), geo=False),
             E("blush", "Head", 5, (0.095 * s, -0.12, 0.725), (0.03, 0.04, 0.02), geo=False)]
    side = "R" if s < 0 else "L"
    BODY += [C("velvet", "Arm" + side, 2, SH[s], EL[s], 0.05, 0.046),
             C("velvet", "Arm" + side, 2, EL[s], HA[s], 0.046, 0.04),
             E("velvet", "Arm" + side, 2, SH[s], (0.06, 0.06, 0.056)),
             E("ermine", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.75 + np.array(EL[s]) * 0.25), (0.052, 0.052, 0.045)),
             E("skin", "Arm" + side, 4, HA[s], (0.036, 0.04, 0.036)),
             C("stock", "Leg" + side, 1, (HIP[s][0], 0.0, 0.30), AN[s], 0.045, 0.04),
             E("shoe", "Leg" + side, 3, (AN[s][0], -0.03, 0.05), (0.055, 0.085, 0.048)),
             E("gold", "Leg" + side, 4, (AN[s][0], -0.1, 0.07), (0.03, 0.015, 0.02))]  # buckles

# ---------------- weapon: sceptre-rattle (local: origin = grip, held upright, leaning forward -Y) ----------------
TOP = (0, -0.11, 0.30)
WEAPON = [
    C("gold", "Prop", 1, (0, 0.03, -0.09), TOP, 0.016),  # handle
    E("gold_d", "Prop", 2, (0, 0.03, -0.095), (0.026, 0.026, 0.026)),  # pommel
    E("gold_d", "Prop", 2, (0, -0.06, 0.16), (0.03, 0.03, 0.018)),  # collar
    E("velvet", "Prop", 2, TOP, (0.068, 0.068, 0.068)),  # rattle ball
    E("gold", "Prop", 3, TOP, (0.072, 0.072, 0.014)),  # gold ring around the ball
    E("gold", "Prop", 3, (0, -0.125, 0.385), (0.024, 0.024, 0.024)),  # finial
]
for k in range(8):  # white dots on the rattle ball (labels)
    a = 2 * np.pi * k / 8
    for zz in (0.035, -0.035):
        WEAPON.append(E("ermine", "Prop", 4, (0.06 * np.cos(a), -0.11 + 0.06 * np.sin(a), 0.30 + zz),
                        (0.015, 0.015, 0.015), geo=False))
MUZZLE = (0, -0.125, 0.40)

BONES = [
    ("Root", None, (0, 0, 0), (0, 0, 0.40)),
    ("Body", "Root", (0, 0, 0.40), (0, 0, 0.63)),
    ("Head", "Body", (0, 0, 0.63), (0, 0, 1.0)),
    ("ArmR", "Body", SH[-1], HA[-1]),
    ("ArmL", "Body", SH[1], HA[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", HA[-1], tuple(np.array(HA[-1]) + (0, -0.10, 0))),
    ("EyeR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("EyeL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("LidR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("LidL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("Tail", "Root", (0, 0.14, 0.10), (0, 1.12, 0.03)),
]


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
high = build_shell(BODY, "OttoHigh", 0.0045)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Otto"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("OTTO body tris", TRIS)
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
timg = bpy.data.images.new("otto_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "otto_tex.png")
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
BONE_DEF = BONES
arm_data = bpy.data.armatures.new("OttoRig")
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
    if bn == "Body" and V[i, 2] < 0.45:
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


export([arm, low], os.path.join(OUT, "otto_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "OttoWeapon", 0.004)
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
export([gun, muzzle], os.path.join(OUT, "otto_weapon"), {"MESH", "EMPTY"})
print("OTTO done: body", TRIS, "weapon", gtris, "out", OUT)
