# Bront (15, loud teen storm-caller with the pocket thundercloud Rumbly), base 3D model (Docs/Briefs/A-Roster3D.md, wave 2).
# Headless, Blender 5.2:  blender -b --factory-startup --python Tools/Blender/fighters/build_bront.py -- [out_dir]
# Same method as build_bront.py: analytic primitives -> voxel remesh -> per-face colour from the primitive list (label-only
# decals: eyes, brows, grin, hair sparks, pocket) -> decimate -> smart UV -> Cycles bake high->low (RGB = albedo, A = gloss)
# into bront_tex.png 1024 -> rig with the 12 FighterBone bones + Extra (cloud Rumbly), rigid weights -> FBX/GLB.
# Whole figure scaled by K = 1.05. Top view: ball of hair + cloud beside it + thin line of the hammer.
# Weapon (copper lightning-rod hammer, held like a staff, never thrown): bront_weapon.fbx/.glb, vertex colour "Col"
# (alpha = gloss), origin = grip under the right hand (hang on Prop without offset), head along -Y, empty "Muzzle" at the
# needle tip (lightning source).
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "bront")
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]


PAL = {  # sRGB hex, gloss
    "skin": hexc("#6B4430", 0.2), "skin_d": hexc("#57361F", 0.2), "hair": hexc("#1E1A1C", 0.3),
    "spark": hexc("#F2FF5A", 1.0), "hood": hexc("#7C8494", 0.25), "hood_d": hexc("#636A79", 0.2),
    "pants": hexc("#3B4050", 0.15), "shoe": hexc("#EDEEF0", 0.4), "sole": hexc("#5A606C", 0.3),
    "copper": hexc("#C8763A", 0.85), "copper_d": hexc("#8E4E22", 0.6), "wrap": hexc("#4A3A30", 0.25),
    "white": hexc("#F7F7F5", 0.6), "iris": hexc("#5A3A22", 1.0), "pupil": hexc("#141414", 1.0),
    "mouth": hexc("#5A2420", 0.3), "teeth": hexc("#F7F7F5", 0.5), "brow": hexc("#1E1A1C", 0.2),
    "cloud": hexc("#A3AAB8", 0.35), "cloud_d": hexc("#7C8494", 0.3), "c_eye": hexc("#2A2E36", 0.9),
}
K = 1.05  # global scale (15-year-old, a bit taller than Theo)
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
SH = {-1: (-0.16, 0.0, 0.60), 1: (0.16, 0.0, 0.60)}
EL = {-1: (-0.21, 0.02, 0.47), 1: (0.10, -0.14, 0.50)}
HA = {-1: (-0.12, -0.10, 0.42), 1: (-0.10, -0.28, 0.43)}
HIP = {-1: (-0.065, 0.0, 0.36), 1: (0.065, 0.0, 0.36)}
AN = {-1: (-0.08, 0.0, 0.09), 1: (0.08, 0.0, 0.09)}
EYE = {s: (0.06 * s, -0.138, 0.765) for s in (-1, 1)}
CLOUD = (0.24, 0.10, 1.20)  # Rumbly, the pocket thundercloud, over his left shoulder
BODY = [
    E("skin", "Head", 1, (0, 0, 0.78), (0.158, 0.148, 0.158)),
    E("skin", "Head", 1, (0, -0.02, 0.71), (0.118, 0.115, 0.08)),  # jaw
    E("hair", "Head", 2, (0, 0.03, 0.91), (0.215, 0.20, 0.17)),  # big electrified curls: a ball
    E("hair", "Head", 2, (0, 0.06, 0.80), (0.16, 0.13, 0.12)),  # back of the ball
    E("skin", "Head", 2, (-0.16, 0.0, 0.76), (0.022, 0.03, 0.035)),  # ears
    E("skin", "Head", 2, (0.16, 0.0, 0.76), (0.022, 0.03, 0.035)),
    E("skin_d", "Head", 3, (0, -0.155, 0.74), (0.02, 0.016, 0.018)),  # nose
    C("skin", "Head", 1, (0, 0, 0.60), (0, 0, 0.68), 0.045),  # neck
    E("hood", "Body", 1, (0, 0, 0.52), (0.155, 0.12, 0.15)),
    E("hood_d", "Body", 3, (0, 0.07, 0.635), (0.12, 0.075, 0.055)),  # hood bunched behind the neck
    E("hood_d", "Root", 2, (0, 0, 0.405), (0.152, 0.117, 0.03)),  # hem band
    E("pants", "Root", 1, (0, 0, 0.37), (0.125, 0.095, 0.07)),
    E("hood_d", "Body", 5, (0, -0.12, 0.47), (0.09, 0.03, 0.045), geo=False),  # kangaroo pocket
    E("white", "Head", 6, EYE[-1], (0.038, 0.02, 0.044)),
    E("white", "Head", 6, EYE[1], (0.038, 0.02, 0.044)),
    E("mouth", "Head", 7, (0.012, -0.145, 0.695), (0.042, 0.03, 0.014), geo=False),  # wide grin
    E("teeth", "Head", 8, (0.012, -0.15, 0.701), (0.032, 0.03, 0.006), geo=False),
]
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    side = "R" if s < 0 else "L"
    BODY += [E("iris", "Eye" + side, 8, (ex, ey - 0.02, ez - 0.004), (0.024, 0.012, 0.03), geo=False),
             E("pupil", "Eye" + side, 9, (ex, ey - 0.024, ez - 0.004), (0.012, 0.01, 0.015), geo=False),
             E("white", "Eye" + side, 10, (ex + 0.01 * s, ey - 0.026, ez + 0.014), (0.007, 0.01, 0.008), geo=False),
             E("brow", "Head", 8, (ex + 0.005 * s, ey - 0.01, ez + 0.058), (0.035, 0.03, 0.009), geo=False)]
    BODY += [C("copper", "Body", 4, (0.035 * s, -0.12, 0.615), (0.04 * s, -0.128, 0.53), 0.007),  # drawstrings
             E("copper", "Body", 5, (0.04 * s, -0.128, 0.525), (0.012, 0.012, 0.016))]
    BODY += [C("hood", "Arm" + side, 2, SH[s], EL[s], 0.055, 0.047),
             C("hood", "Arm" + side, 2, EL[s], HA[s], 0.047, 0.04),
             E("hood", "Arm" + side, 2, SH[s], (0.065, 0.065, 0.06)),
             E("hood_d", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.75 + np.array(EL[s]) * 0.25), (0.044, 0.044, 0.044)),
             E("skin", "Arm" + side, 4, HA[s], (0.04, 0.044, 0.038)),
             C("pants", "Leg" + side, 1, HIP[s], AN[s], 0.058, 0.048),
             E("shoe", "Leg" + side, 3, (AN[s][0], -0.025, 0.058), (0.06, 0.095, 0.055)),
             E("sole", "Leg" + side, 4, (AN[s][0], -0.025, 0.006), (0.064, 0.1, 0.016)),
             E("copper", "Leg" + side, 5, (AN[s][0] + 0.06 * s, -0.03, 0.06), (0.012, 0.05, 0.012), geo=False)]
# lemon sparks crackling in the hair (label only)
for sx, sy, sz in ((-0.14, -0.08, 0.98), (0.12, -0.10, 1.0), (0.02, 0.0, 1.075), (-0.19, 0.06, 0.92), (0.07, 0.14, 1.03)):
    # tiny zigzag crackles: two short slanted strokes each
    BODY += [P("c", "spark", "Head", 6, False, a=(sx - 0.018, sy, sz + 0.012), b=(sx + 0.004, sy, sz), ra=0.007, rb=0.007),
             P("c", "spark", "Head", 6, False, a=(sx + 0.004, sy, sz), b=(sx - 0.01, sy, sz - 0.02), ra=0.007, rb=0.007)]
# Rumbly: a cluster of puffs on Extra, darker belly, cute eyes, a little lemon zigzag bolt hanging under it
cx, cy, cz = CLOUD
for dx, dz, r in ((0, 0.02, 0.09), (-0.09, -0.01, 0.07), (0.09, -0.01, 0.07), (-0.03, 0.09, 0.07), (0.05, 0.07, 0.065),
                  (0.0, -0.03, 0.075)):
    BODY.append(E("cloud", "Extra", 2, (cx + dx, cy, cz + dz), (r, r * 0.9, r)))
BODY += [E("cloud_d", "Extra", 4, (cx, cy, cz - 0.075), (0.2, 0.12, 0.03), geo=False),
         E("c_eye", "Extra", 6, (cx - 0.035, cy - 0.078, cz + 0.02), (0.012, 0.03, 0.017), geo=False),
         E("c_eye", "Extra", 6, (cx + 0.035, cy - 0.078, cz + 0.02), (0.012, 0.03, 0.017), geo=False)]
bolt = [(cx, cy, cz - 0.08), (cx + 0.03, cy, cz - 0.13), (cx - 0.015, cy, cz - 0.15), (cx + 0.015, cy, cz - 0.21)]
for a, b in zip(bolt, bolt[1:]):
    BODY.append(C("spark", "Extra", 3, a, b, 0.012))

# ---- weapon: copper lightning-rod hammer (local: origin = grip, held like a staff, head forward -Y, up +Z) ----
WEAPON = [
    C("copper_d", "Prop", 1, (0, 0.16, 0.0), (0, -0.36, 0.0), 0.021),  # long handle
    C("wrap", "Prop", 2, (0, 0.06, 0.0), (0, -0.05, 0.0), 0.025),  # grip wrap (right hand)
    C("wrap", "Prop", 2, (0, -0.13, 0.0), (0, -0.23, 0.0), 0.025),  # grip wrap (left hand)
    E("copper", "Prop", 2, (0, 0.17, 0.0), (0.03, 0.03, 0.03)),  # pommel
    E("copper", "Prop", 2, (0, -0.37, 0.0), (0.038, 0.03, 0.038)),  # collar
    E("copper", "Prop", 1, (0, -0.45, 0.0), (0.075, 0.075, 0.075)),  # round ball head
    E("copper_d", "Prop", 3, (0, -0.45, 0.0), (0.078, 0.014, 0.078)),  # ring around the ball
    E("spark", "Prop", 4, (0, -0.45, 0.0), (0.079, 0.006, 0.079), geo=False),  # glowing seam
    C("copper", "Prop", 2, (0, -0.52, 0.0), (0, -0.64, 0.0), 0.012, 0.005),  # lightning-rod needle
    E("spark", "Prop", 4, (0, -0.645, 0.0), (0.014, 0.014, 0.014)),  # charged tip
]
MUZZLE = (0, -0.66, 0.0)


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
CLOUD = tuple(np.array(CLOUD) * K)


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
high = build_shell(BODY, "BrontHigh", 0.0045)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Bront"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("BRONT body tris", TRIS)
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
timg = bpy.data.images.new("bront_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "bront_tex.png")
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
    ("Extra", "Body", CLOUD, tuple(np.array(CLOUD) + (0, 0, 0.08))),
]
arm_data = bpy.data.armatures.new("BrontRig")
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


export([arm, low], os.path.join(OUT, "bront_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "BrontWeapon", 0.004)
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
export([gun, muzzle], os.path.join(OUT, "bront_weapon"), {"MESH", "EMPTY"})
print("BRONT done: body", TRIS, "weapon", gtris, "out", OUT)
