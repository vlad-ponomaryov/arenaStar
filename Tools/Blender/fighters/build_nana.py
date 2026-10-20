# Nana Chill (Grandma Chill, aura healer, ~70), base 3D model (Docs/Briefs/A-Roster3D.md, wave 2). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_nana.py -- [out_dir]
# Same method as build_lark.py: analytic primitives -> voxel remesh shell (high) -> colour per face from the primitive list
# (layered priority, label-only decals: glasses, calm half-closed eyes, blush, mouth, buttons) -> quadric decimate -> smart UV
# -> Cycles bake high->low (RGB = albedo, A = gloss) into nana_tex.png 1024 -> rig with the 12 FighterBone bones, rigid
# weights -> FBX/GLB. Weapon = thermos of warm tea: nana_weapon.fbx/.glb, vertex colour "Col" (alpha = gloss), origin = grip
# under the right hand (hang on Prop without offset), thermos along -Y, empty "Muzzle" at the spout (steam / tea splash).
# Silhouette from above: rolled yoga mat across the back + grey bun with two crossed knitting needles.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

ID = "nana"
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", ID)
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]


PAL = {  # sRGB hex, gloss
    "skin": hexc("#EBC39C", 0.15), "blush": hexc("#E79A8A", 0.15), "hair": hexc("#DADBE2", 0.45),
    "hair_d": hexc("#B9BAC6", 0.35), "brow": hexc("#C6C7D0", 0.2),
    "cardi": hexc("#7FCBB4", 0.25), "cardi_d": hexc("#5FAE97", 0.2), "shirt": hexc("#FFF3DC", 0.2),
    "button": hexc("#F2B33D", 0.7), "legs": hexc("#5B4A7A", 0.15), "shoe": hexc("#F7F5F0", 0.5),
    "sole": hexc("#5FAE97", 0.3), "mat": hexc("#F2896B", 0.35), "mat_d": hexc("#D9694C", 0.3),
    "mat_end": hexc("#F9B9A2", 0.3), "strap": hexc("#6B5390", 0.3), "needle": hexc("#E8B84A", 0.8),
    "pearl": hexc("#F2896B", 0.8), "rim": hexc("#8E3B5A", 0.7), "white": hexc("#F7F7F5", 0.6),
    "iris": hexc("#4A3524", 1.0), "pupil": hexc("#1A1A1A", 1.0), "mouth": hexc("#B4574A", 0.3),
    "thermos": hexc("#3FA7C9", 0.8), "thermos_d": hexc("#2C7E9C", 0.7), "cap": hexc("#F7F5F0", 0.7),
    "steel": hexc("#C9CED6", 0.9), "grip": hexc("#5B4A7A", 0.3), "glow": hexc("#FFE3A6", 1.0),
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

# ---------------- body (Blender: Z up, faces -Y, character's right = -X); a bit shorter than Theo, bun on top ----------------
SH = {-1: (-0.15, 0.0, 0.55), 1: (0.15, 0.0, 0.55)}
EL = {-1: (-0.20, -0.04, 0.43), 1: (0.20, -0.02, 0.43)}
HA = {-1: (-0.12, -0.14, 0.40), 1: (0.15, -0.10, 0.36)}
HIP = {-1: (-0.065, 0.0, 0.32), 1: (0.065, 0.0, 0.32)}
AN = {-1: (-0.075, 0.0, 0.085), 1: (0.075, 0.0, 0.085)}
EYE = {s: (0.062 * s, -0.142, 0.725) for s in (-1, 1)}
HEAD_C = (0, 0, 0.74)
BODY = [
    E("skin", "Head", 1, HEAD_C, (0.165, 0.152, 0.158)),
    E("skin", "Head", 1, (0, -0.02, 0.675), (0.125, 0.12, 0.08)),  # soft jaw / cheeks
    E("hair", "Head", 2, (0, 0.045, 0.785), (0.172, 0.152, 0.15)),  # hair pulled back
    E("hair", "Head", 2, (0, 0.07, 0.70), (0.15, 0.10, 0.08)),  # back of the hair
    E("hair_d", "Head", 3, (0, 0.04, 0.892), (0.06, 0.06, 0.02)),  # bun base ring
    E("hair", "Head", 4, (0, 0.04, 0.935), (0.085, 0.082, 0.065)),  # the bun
    C("needle", "Head", 5, (-0.15, -0.01, 0.955), (0.12, 0.09, 0.915), 0.0085),  # knitting needles through the bun
    C("needle", "Head", 5, (0.14, -0.02, 0.96), (-0.11, 0.10, 0.92), 0.0085),
    E("pearl", "Head", 6, (-0.15, -0.01, 0.955), (0.02, 0.02, 0.02)),
    E("pearl", "Head", 6, (0.14, -0.02, 0.96), (0.02, 0.02, 0.02)),
    E("skin", "Head", 2, (-0.165, 0.0, 0.73), (0.022, 0.03, 0.036)),  # ears
    E("skin", "Head", 2, (0.165, 0.0, 0.73), (0.022, 0.03, 0.036)),
    E("skin", "Head", 3, (0, -0.16, 0.705), (0.02, 0.016, 0.018)),  # nose
    C("skin", "Head", 1, (0, 0, 0.55), (0, 0, 0.63), 0.045),  # neck
    # long mint cardigan over a cream shirt
    E("cardi", "Body", 1, (0, 0, 0.47), (0.152, 0.12, 0.145)),
    E("cardi", "Root", 1, (0, 0.0, 0.355), (0.158, 0.128, 0.075)),  # flared hem over the hips
    E("cardi_d", "Root", 2, (0, 0.0, 0.30), (0.16, 0.13, 0.022)),  # hem band
    E("cardi_d", "Body", 3, (0, 0.012, 0.585), (0.11, 0.095, 0.035)),  # collar
    P("c", "shirt", "Body", 4, False, a=(0, -0.13, 0.33), b=(0, -0.11, 0.59), ra=0.032, rb=0.04),  # open front
    E("blush", "Head", 6, (-0.095, -0.12, 0.69), (0.03, 0.04, 0.018), geo=False),
    E("blush", "Head", 6, (0.095, -0.12, 0.69), (0.03, 0.04, 0.018), geo=False),
    E("mouth", "Head", 7, (0.0, -0.152, 0.655), (0.028, 0.03, 0.007), geo=False),  # calm smile
    E("legs", "Root", 0, (0, 0, 0.30), (0.11, 0.09, 0.05)),
]
for bz in (0.36, 0.43, 0.50):  # gold buttons along the cardigan edge
    BODY.append(E("button", "Body" if bz > 0.40 else "Root", 6, (-0.047, -0.125, bz), (0.012, 0.03, 0.012), geo=False))
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    eb = "Eye" + ("R" if s < 0 else "L")
    BODY += [E("rim", "Head", 6, (ex, ey, ez), (0.05, 0.04, 0.047), geo=False),  # round glasses (painted rim)
             E("white", "Head", 7, (ex, ey, ez), (0.036, 0.04, 0.034), geo=False),
             E("iris", eb, 8, (ex, ey, ez - 0.006), (0.022, 0.045, 0.023), geo=False),
             E("pupil", eb, 9, (ex, ey, ez - 0.006), (0.011, 0.05, 0.012), geo=False),
             E("skin", "Head", 10, (ex, ey, ez + 0.03), (0.04, 0.05, 0.03), geo=False),  # calm half-closed lid
             E("brow", "Head", 8, (ex + 0.004 * s, ey + 0.004, ez + 0.068), (0.034, 0.03, 0.009), geo=False)]
    side = "R" if s < 0 else "L"
    BODY += [C("cardi", "Arm" + side, 2, SH[s], EL[s], 0.052, 0.046),
             C("cardi", "Arm" + side, 2, EL[s], HA[s], 0.046, 0.042),
             E("cardi", "Arm" + side, 2, SH[s], (0.062, 0.062, 0.058)),
             E("cardi_d", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.75 + np.array(EL[s]) * 0.25), (0.047, 0.047, 0.047)),
             E("skin", "Arm" + side, 4, HA[s], (0.036, 0.04, 0.036)),
             C("legs", "Leg" + side, 1, HIP[s], AN[s], 0.054, 0.044),
             E("shoe", "Leg" + side, 3, (AN[s][0], -0.025, 0.052), (0.056, 0.088, 0.05)),
             E("sole", "Leg" + side, 4, (AN[s][0], -0.025, 0.004), (0.06, 0.092, 0.014))]
# rolled yoga mat across the back (strap over the left shoulder)
MAT_A, MAT_B = (-0.20, 0.155, 0.36), (0.20, 0.155, 0.66)
BODY += [C("mat", "Body", 2, MAT_A, MAT_B, 0.058),
         E("mat_end", "Body", 4, MAT_A, (0.05, 0.06, 0.06), geo=False),
         E("mat_end", "Body", 4, MAT_B, (0.05, 0.06, 0.06), geo=False)]
for t in (0.25, 0.75):
    q = np.array(MAT_A) * (1 - t) + np.array(MAT_B) * t
    BODY.append(E("strap", "Body", 3, tuple(q), (0.022, 0.075, 0.075), geo=False))
BODY += [P("c", "strap", "Body", 5, False, a=(0.12, -0.11, 0.58), b=(-0.13, -0.10, 0.36), ra=0.012, rb=0.012)]

# ---------------- weapon: thermos of warm tea (local: origin = grip, forward -Y, up +Z) ----------------
WEAPON = [
    C("grip", "Prop", 2, (0, 0.0, -0.035), (0, 0.0, 0.03), 0.018),  # handle in the fist
    C("thermos", "Prop", 1, (0, 0.04, 0.06), (0, -0.11, 0.06), 0.042),  # flask
    E("thermos_d", "Prop", 3, (0, -0.04, 0.06), (0.046, 0.012, 0.046)),  # bands
    E("thermos_d", "Prop", 3, (0, 0.03, 0.06), (0.046, 0.012, 0.046)),
    E("steel", "Prop", 2, (0, -0.125, 0.06), (0.037, 0.016, 0.037)),  # neck
    C("cap", "Prop", 3, (0, -0.135, 0.06), (0, -0.17, 0.06), 0.04, 0.036),  # cup-cap
    E("glow", "Prop", 4, (0, -0.177, 0.06), (0.026, 0.008, 0.026)),  # warm tea surface
    C("grip", "Prop", 2, (0, 0.0, 0.04), (0, 0.0, 0.03), 0.015),
]
MUZZLE = (0, -0.19, 0.06)
BONE_EXTRA = []
ROOT_BELOW = 0.40  # Body-labelled verts below this go to Root (hem)


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
    m.use_collapse_triangulate = True
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


NAME = ID.capitalize()
# ================================ body ================================
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
high = build_shell(BODY, NAME + "High", 0.0045)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = NAME
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print(ID.upper(), "body tris", TRIS)
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
timg = bpy.data.images.new(ID + "_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, ID + "_tex.png")
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
NECK = SH[1][2] + 0.09
BONE_DEF = [
    ("Root", None, (0, 0, 0), (0, 0, HIP[1][2] + 0.06)),
    ("Body", "Root", (0, 0, HIP[1][2] + 0.06), (0, 0, NECK)),
    ("Head", "Body", (0, 0, NECK), (0, 0, NECK + 0.36)),
    ("ArmR", "Body", SH[-1], HA[-1]),
    ("ArmL", "Body", SH[1], HA[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", HA[-1], tuple(np.array(HA[-1]) + (0, -0.10, 0))),
    ("EyeR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("EyeL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("LidR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("LidL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
] + BONE_EXTRA
arm_data = bpy.data.armatures.new(NAME + "Rig")
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
vlab = label([p for p in BODY], V)
groups = {b[0]: low.vertex_groups.new(name=b[0]) for b in BONE_DEF}
by = {}
for i, li in enumerate(vlab):
    bn = BODY[li].bone
    if bn.startswith("Eye"):  # painted eye decals ride the head; eye bones are pivots for the animator
        bn = "Head"
    if bn == "Body" and V[i, 2] < ROOT_BELOW:
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


export([arm, low], os.path.join(OUT, ID + "_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, NAME + "Weapon", 0.003)
face_colors(gun, WEAPON)
gtris = decimate(gun, 1180)
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
export([gun, muzzle], os.path.join(OUT, ID + "_weapon"), {"MESH", "EMPTY"})
print(ID.upper(), "done: body", TRIS, "weapon", gtris, "out", OUT)
