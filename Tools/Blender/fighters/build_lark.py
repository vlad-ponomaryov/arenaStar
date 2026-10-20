# Lark (scout-sniper with the drone Pix), base 3D model (Docs/Briefs/A-Roster3D.md). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_lark.py -- [out_dir]
# Method (simplified vs build_theo_v3): analytic primitives (ellipsoids / capsules) -> Blender meshes -> voxel remesh into one
# smooth "vinyl" shell (high) -> colour per face from the primitive list (layered priority, label-only decals: iris, mouth,
# freckles, zipper, badge) -> quadric decimate to the game budget -> smart UV -> Cycles bake high->low (RGB = albedo,
# A = gloss) into lark_tex.png 1024 -> rig with the 12 FighterBone bones + Extra (drone), rigid weights -> FBX/GLB.
# Weapon (harpoon dart launcher): lark_weapon.fbx/.glb, vertex colour "Col" (alpha = gloss), origin = grip under the right
# hand (hang on Prop without offset), barrel along -Y, empty "Muzzle" at the dart tip.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "lark")
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]


PAL = {  # sRGB hex, gloss
    "skin": hexc("#F4CDB0", 0.15), "hair": hexc("#B85A2A", 0.35), "hair_d": hexc("#8E4320", 0.3),
    "jacket": hexc("#D6A12B", 0.3), "jacket_d": hexc("#B5841C", 0.25), "fur": hexc("#EFE1C2", 0.1),
    "pants": hexc("#4B5245", 0.1), "boot": hexc("#6A4428", 0.3), "sole": hexc("#2C2622", 0.2),
    "glove": hexc("#4A3326", 0.2), "strap": hexc("#5A3A22", 0.25), "lens": hexc("#5FC6CF", 1.0),
    "brass": hexc("#C9A04A", 0.8), "white": hexc("#F7F7F5", 0.6), "iris": hexc("#3E7A4E", 1.0),
    "pupil": hexc("#1A1A1A", 1.0), "mouth": hexc("#A0453A", 0.3), "freckle": hexc("#C98A62", 0.15),
    "drone": hexc("#E9ECEF", 0.7), "drone_d": hexc("#4A4F57", 0.6), "rotor": hexc("#2B2F35", 0.5),
    "glow": hexc("#48D6E0", 1.0), "badge": hexc("#3C8FC9", 0.5), "zip": hexc("#8A6A20", 0.6),
    "coral": hexc("#FF6B4A", 0.6), "orange": hexc("#F07A2A", 0.7),
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
SH = {-1: (-0.15, 0.0, 0.60), 1: (0.15, 0.0, 0.60)}
EL = {-1: (-0.20, 0.02, 0.47), 1: (0.10, -0.15, 0.50)}
HA = {-1: (-0.12, -0.10, 0.42), 1: (-0.09, -0.27, 0.44)}
HIP = {-1: (-0.06, 0.0, 0.36), 1: (0.06, 0.0, 0.36)}
AN = {-1: (-0.075, 0.0, 0.09), 1: (0.075, 0.0, 0.09)}
EYE = {s: (0.062 * s, -0.142, 0.775) for s in (-1, 1)}
DRONE = (-0.25, 0.07, 0.96)
BODY = [
    E("skin", "Head", 1, (0, 0, 0.79), (0.165, 0.152, 0.16)),
    E("skin", "Head", 1, (0, -0.02, 0.72), (0.12, 0.12, 0.08)),  # jaw / cheeks
    E("hair", "Head", 2, (0, 0.035, 0.83), (0.178, 0.16, 0.165)),
    E("hair", "Head", 2, (-0.11, 0.01, 0.75), (0.08, 0.13, 0.11)),  # bob sides to the jaw
    E("hair", "Head", 2, (0.11, 0.01, 0.75), (0.08, 0.13, 0.11)),
    E("hair", "Head", 2, (0, 0.06, 0.73), (0.15, 0.11, 0.09)),  # bob back
    E("hair", "Head", 3, (0, -0.09, 0.905), (0.155, 0.075, 0.06)),  # bangs
    E("strap", "Head", 4, (0, 0.03, 0.93), (0.181, 0.164, 0.016)),  # goggle strap
    E("brass", "Head", 5, (-0.058, -0.135, 0.945), (0.05, 0.035, 0.044)),  # goggle rims
    E("brass", "Head", 5, (0.058, -0.135, 0.945), (0.05, 0.035, 0.044)),
    E("lens", "Head", 6, (-0.058, -0.155, 0.945), (0.038, 0.022, 0.033)),
    E("lens", "Head", 6, (0.058, -0.155, 0.945), (0.038, 0.022, 0.033)),
    E("skin", "Head", 2, (-0.168, 0.0, 0.78), (0.022, 0.03, 0.035)),  # ears (mostly under the bob)
    E("skin", "Head", 2, (0.168, 0.0, 0.78), (0.022, 0.03, 0.035)),
    E("skin", "Head", 3, (0, -0.158, 0.755), (0.018, 0.016, 0.02)),  # nose
    C("skin", "Head", 1, (0, 0, 0.60), (0, 0, 0.68), 0.045),  # neck
    E("jacket", "Body", 1, (0, 0, 0.52), (0.15, 0.115, 0.15)),
    E("fur", "Body", 3, (0, 0.012, 0.635), (0.125, 0.105, 0.04)),  # bomber collar
    E("jacket_d", "Root", 2, (0, 0, 0.415), (0.148, 0.112, 0.035)),  # hem band
    E("pants", "Root", 1, (0, 0, 0.37), (0.12, 0.09, 0.07)),
    E("badge", "Body", 6, (0.075, -0.115, 0.56), (0.03, 0.02, 0.03), geo=False),
    P("c", "zip", "Body", 6, False, a=(0, -0.12, 0.43), b=(0, -0.12, 0.63), ra=0.006, rb=0.006),
    E("white", "Head", 6, EYE[-1], (0.04, 0.02, 0.048)),
    E("white", "Head", 6, EYE[1], (0.04, 0.02, 0.048)),
    E("mouth", "Head", 7, (0.01, -0.152, 0.703), (0.03, 0.03, 0.009), geo=False),
]
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    BODY += [E("iris", "Eye" + ("R" if s < 0 else "L"), 8, (ex, ey - 0.02, ez - 0.004), (0.027, 0.012, 0.033), geo=False),
             E("pupil", "Eye" + ("R" if s < 0 else "L"), 9, (ex, ey - 0.024, ez - 0.004), (0.013, 0.01, 0.017), geo=False),
             E("white", "Eye" + ("R" if s < 0 else "L"), 10, (ex + 0.012 * s, ey - 0.026, ez + 0.016), (0.007, 0.01, 0.008), geo=False)]
    for fx, fz in ((0.06, 0.725), (0.085, 0.735), (0.075, 0.712), (0.1, 0.718)):
        BODY.append(E("freckle", "Head", 8, (fx * s, -0.135, fz), (0.0075, 0.03, 0.0075), geo=False))
    side = "R" if s < 0 else "L"
    BODY += [C("jacket", "Arm" + side, 2, SH[s], EL[s], 0.05, 0.043),
             C("jacket", "Arm" + side, 2, EL[s], HA[s], 0.043, 0.036),
             E("jacket", "Arm" + side, 2, SH[s], (0.06, 0.06, 0.055)),
             E("jacket_d", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.8 + np.array(EL[s]) * 0.2), (0.04, 0.04, 0.04)),
             E("glove", "Arm" + side, 4, HA[s], (0.038, 0.042, 0.036)),
             C("pants", "Leg" + side, 1, HIP[s], AN[s], 0.055, 0.045),
             E("boot", "Leg" + side, 3, (AN[s][0], -0.025, 0.055), (0.056, 0.088, 0.052)),
             E("sole", "Leg" + side, 4, (AN[s][0], -0.025, 0.004), (0.06, 0.092, 0.014))]
# drone "Pix": an X of arms with 4 rotors, hovering over the right shoulder
dx, dy, dz = DRONE
BODY += [E("drone", "Extra", 2, DRONE, (0.05, 0.05, 0.028)),
         E("glow", "Extra", 4, (dx, dy - 0.045, dz), (0.016, 0.012, 0.012)),
         E("glow", "Extra", 4, (dx, dy, dz + 0.024), (0.018, 0.018, 0.008), geo=False)]
for ax, ay in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
    tip = (dx + 0.075 * ax, dy + 0.075 * ay, dz + 0.008)
    BODY += [C("drone_d", "Extra", 3, DRONE, tip, 0.012),
             E("rotor", "Extra", 3, tip, (0.04, 0.04, 0.006)),
             E("glow", "Extra", 5, tip, (0.01, 0.01, 0.012))]

# ---------------- weapon: harpoon dart launcher (local: origin = grip, barrel -Y, up +Z) ----------------
WEAPON = [
    C("strap", "Prop", 2, (0, 0.005, -0.055), (0, 0.0, 0.025), 0.019),  # grip
    C("white", "Prop", 1, (0, 0.10, 0.055), (0, -0.30, 0.055), 0.034, 0.03),  # long tube
    E("jacket", "Prop", 2, (0, 0.14, 0.035), (0.03, 0.07, 0.05)),  # stock
    E("lens", "Prop", 3, (0, -0.02, 0.055), (0.037, 0.012, 0.037)),  # teal rings
    E("lens", "Prop", 3, (0, -0.16, 0.055), (0.034, 0.012, 0.034)),
    E("orange", "Prop", 3, (0, -0.30, 0.055), (0.045, 0.022, 0.045)),  # muzzle ring
    C("orange", "Prop", 2, (0, -0.30, 0.055), (0, -0.40, 0.055), 0.011),  # dart
    E("coral", "Prop", 4, (0, -0.405, 0.055), (0.03, 0.01, 0.03)),  # suction cup
    C("drone_d", "Prop", 3, (0, -0.04, 0.105), (0, -0.15, 0.105), 0.018),  # scope
    E("glow", "Prop", 4, (0, -0.155, 0.105), (0.014, 0.008, 0.014)),
    C("drone_d", "Prop", 3, (0, -0.06, 0.07), (0, -0.06, 0.09), 0.01),
]
MUZZLE = (0, -0.42, 0.055)


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
high = build_shell(BODY, "LarkHigh", 0.0045)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Lark"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("LARK body tris", TRIS)
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
timg = bpy.data.images.new("lark_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "lark_tex.png")
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
    ("Root", None, (0, 0, 0), (0, 0, 0.40)),
    ("Body", "Root", (0, 0, 0.40), (0, 0, 0.64)),
    ("Head", "Body", (0, 0, 0.64), (0, 0, 1.0)),
    ("ArmR", "Body", SH[-1], HA[-1]),
    ("ArmL", "Body", SH[1], HA[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", HA[-1], tuple(np.array(HA[-1]) + (0, -0.10, 0))),
    ("EyeR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("EyeL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("LidR", "Head", EYE[-1], tuple(np.array(EYE[-1]) + (0, -0.03, 0))),
    ("LidL", "Head", EYE[1], tuple(np.array(EYE[1]) + (0, -0.03, 0))),
    ("Extra", "Body", DRONE, tuple(np.array(DRONE) + (0, 0, 0.06))),
]
arm_data = bpy.data.armatures.new("LarkRig")
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


export([arm, low], os.path.join(OUT, "lark_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "LarkWeapon", 0.004)
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
export([gun, muzzle], os.path.join(OUT, "lark_weapon"), {"MESH", "EMPTY"})
print("LARK done: body", TRIS, "weapon", gtris, "out", OUT)
