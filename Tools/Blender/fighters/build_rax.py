# Rax (raccoon trickster, steals ult charge), base 3D model (Docs/Briefs/A-Roster3D.md, wave 2). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_rax.py -- [out_dir]
# Same method as build_lark.py / build_nana.py: analytic primitives -> voxel remesh shell -> colour per face from the
# primitive list (decals: mask, white brows / muzzle stripe, eyes, belly, ring stripes on the tail, polka dots) -> decimate ->
# smart UV -> Cycles bake (RGB = albedo, A = gloss) into rax_tex.png 1024 -> rig with the 12 FighterBone bones + Tail + Extra.
# Weapon = a "flick" acorn held in the right paw (rax_weapon.fbx/.glb, origin = grip, tip along -Y, empty "Muzzle" at the tip).
# Silhouette from above: big striped tail trailing back + red polka-dot bundle (Extra, glows on the "Swap" ult) on the back.
# Reference: Art/Source/Flow/2026-10-19_vex-raccoon (grey raccoon, dark mask, white brows, ringed tail) - shapes only.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

ID = "rax"
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
    "fur": hexc("#8E909C", 0.2), "fur_d": hexc("#5E606B", 0.2), "light": hexc("#E6E3EA", 0.2),
    "belly": hexc("#C9C5D4", 0.2), "mask": hexc("#2E2F37", 0.3), "paw": hexc("#3A3A43", 0.3),
    "ear_in": hexc("#C98FA0", 0.2), "nose": hexc("#1E1E24", 0.9), "white": hexc("#F7F7F5", 0.6),
    "iris": hexc("#9A6A2C", 1.0), "pupil": hexc("#1A1A1A", 1.0), "mouth": hexc("#7A3B3B", 0.3),
    "tooth": hexc("#F7F5F0", 0.5), "bundle": hexc("#D9503F", 0.35), "dot": hexc("#F6E7C8", 0.35),
    "knot": hexc("#B83C2E", 0.35), "strap": hexc("#7A4A2A", 0.3), "acorn": hexc("#C27D3E", 0.6),
    "cap": hexc("#6E4826", 0.3), "stem": hexc("#4E331B", 0.3), "shine": hexc("#F2C48A", 0.8),
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

# ---------------- body (Blender: Z up, faces -Y, character's right = -X); chubby, short legs, big head ----------------
SH = {-1: (-0.14, 0.0, 0.54), 1: (0.14, 0.0, 0.54)}
EL = {-1: (-0.19, -0.04, 0.44), 1: (0.19, -0.03, 0.44)}
HA = {-1: (-0.13, -0.15, 0.42), 1: (0.17, -0.09, 0.38)}
HIP = {-1: (-0.07, 0.0, 0.29), 1: (0.07, 0.0, 0.29)}
AN = {-1: (-0.085, 0.0, 0.075), 1: (0.085, 0.0, 0.075)}
EYE = {s: (0.066 * s, -0.146, 0.755) for s in (-1, 1)}
HEAD_C = (0, 0, 0.74)
BODY = [
    E("fur", "Head", 1, HEAD_C, (0.18, 0.158, 0.155)),
    E("light", "Head", 2, (-0.115, -0.04, 0.685), (0.075, 0.075, 0.052)),  # fluffy cheeks
    E("light", "Head", 2, (0.115, -0.04, 0.685), (0.075, 0.075, 0.052)),
    C("light", "Head", 3, (0, -0.10, 0.705), (0, -0.215, 0.69), 0.062, 0.03),  # snout
    E("nose", "Head", 5, (0, -0.238, 0.698), (0.026, 0.02, 0.018)),
    E("fur_d", "Head", 2, (0, 0.06, 0.80), (0.1, 0.09, 0.07), geo=False),  # darker crown
    E("mask", "Head", 5, (0, -0.12, 0.752), (0.175, 0.1, 0.056), geo=False),  # bandit mask
    P("c", "light", "Head", 6, False, a=(0, -0.20, 0.735), b=(0, -0.15, 0.80), ra=0.018, rb=0.024),  # stripe between the eyes
    E("mouth", "Head", 6, (0.0, -0.195, 0.662), (0.04, 0.05, 0.009), geo=False),  # sly grin
    E("tooth", "Head", 7, (0.012, -0.2, 0.666), (0.012, 0.05, 0.006), geo=False),
    C("fur", "Head", 1, (0, 0, 0.54), (0, 0, 0.62), 0.06),  # neck
    E("fur", "Body", 1, (0, 0, 0.44), (0.15, 0.13, 0.15)),  # round body
    E("fur", "Root", 1, (0, 0, 0.33), (0.14, 0.12, 0.08)),
    E("belly", "Body", 3, (0, -0.075, 0.43), (0.105, 0.07, 0.13)),
    P("c", "strap", "Body", 5, False, a=(0.13, -0.12, 0.57), b=(-0.12, -0.145, 0.36), ra=0.03, rb=0.03),  # bundle strap
]
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    eb = "Eye" + ("R" if s < 0 else "L")
    BODY += [E("white", "Head", 7, (ex, ey, ez), (0.035, 0.045, 0.038), geo=False),
             E("iris", eb, 8, (ex - 0.006 * s, ey, ez - 0.004), (0.026, 0.05, 0.028), geo=False),
             E("pupil", eb, 9, (ex - 0.006 * s, ey, ez - 0.004), (0.014, 0.055, 0.016), geo=False),
             E("white", eb, 10, (ex + 0.006 * s, ey, ez + 0.012), (0.007, 0.06, 0.008), geo=False),
             E("fur", "Head", 10, (ex + 0.01 * s, ey, ez + 0.037), (0.05, 0.05, 0.016), geo=False),  # sly lid line
             E("light", "Head", 8, (ex + 0.02 * s, ey + 0.005, ez + 0.075), (0.048, 0.05, 0.014), geo=False),  # white brows
             C("fur", "Head", 2, (0.11 * s, 0.015, 0.84), (0.145 * s, 0.025, 0.95), 0.05, 0.016),  # pointed ears
             E("ear_in", "Head", 3, (0.125 * s, -0.025, 0.885), (0.028, 0.03, 0.045), geo=False),
             E("mask", "Head", 4, (0.145 * s, 0.025, 0.95), (0.04, 0.05, 0.03), geo=False)]  # dark ear tips
    side = "R" if s < 0 else "L"
    BODY += [C("fur", "Arm" + side, 2, SH[s], EL[s], 0.048, 0.042),
             C("fur", "Arm" + side, 2, EL[s], HA[s], 0.042, 0.036),
             E("fur", "Arm" + side, 2, SH[s], (0.058, 0.058, 0.055)),
             E("paw", "Arm" + side, 4, HA[s], (0.042, 0.045, 0.038)),
             C("fur", "Leg" + side, 1, HIP[s], AN[s], 0.06, 0.048),
             E("paw", "Leg" + side, 3, (AN[s][0], -0.03, 0.045), (0.06, 0.085, 0.045))]
# big ringed tail trailing back and up
T0, T1, T2 = (0, 0.10, 0.27), (0, 0.29, 0.33), (0, 0.44, 0.47)
BODY += [C("fur", "Tail", 1, T0, T1, 0.055, 0.085), C("fur", "Tail", 1, T1, T2, 0.085, 0.07),
         E("mask", "Tail", 3, (0, 0.47, 0.50), (0.075, 0.06, 0.06))]  # dark tip
for y in (0.19, 0.27, 0.355, 0.415):
    z = np.interp(y, (T0[1], T1[1], T2[1]), (T0[2], T1[2], T2[2]))
    BODY.append(E("mask", "Tail", 3, (0, y, z), (0.12, 0.022, 0.12), geo=False))
# red polka-dot bundle on the back (Extra: glows / wobbles on the ult)
BUN = (0.0, 0.17, 0.56)
BODY += [E("bundle", "Extra", 2, BUN, (0.125, 0.085, 0.105)),
         E("knot", "Extra", 3, (0, 0.13, 0.665), (0.04, 0.032, 0.034)),
         E("knot", "Extra", 3, (-0.045, 0.13, 0.70), (0.032, 0.016, 0.03)),  # knot ears
         E("knot", "Extra", 3, (0.045, 0.13, 0.70), (0.032, 0.016, 0.03))]
for dx, dy, dz in ((-0.07, 0.17, 0.60), (0.06, 0.20, 0.61), (0.0, 0.24, 0.55), (-0.08, 0.20, 0.51), (0.08, 0.16, 0.50),
                   (0.0, 0.17, 0.645), (-0.03, 0.24, 0.62), (0.04, 0.245, 0.50)):
    BODY.append(E("dot", "Extra", 4, (dx, dy, dz), (0.018, 0.018, 0.018), geo=False))

# ---------------- weapon: a big "flick" acorn in the right paw (local: origin = grip, tip -Y, up +Z) ----------------
WEAPON = [
    E("acorn", "Prop", 1, (0, -0.045, 0.035), (0.034, 0.05, 0.034)),
    E("cap", "Prop", 2, (0, -0.002, 0.035), (0.04, 0.026, 0.04)),
    C("stem", "Prop", 3, (0, 0.015, 0.035), (0, 0.035, 0.045), 0.007),
    E("shine", "Prop", 3, (0.012, -0.06, 0.06), (0.01, 0.018, 0.008), geo=False),
]
MUZZLE = (0, -0.1, 0.035)
BONE_EXTRA = [("Tail", "Root", T0, T2), ("Extra", "Body", BUN, (BUN[0], BUN[1], BUN[2] + 0.12))]
ROOT_BELOW = 0.36  # Body-labelled verts below this go to Root


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
