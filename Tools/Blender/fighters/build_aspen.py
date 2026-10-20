# Aspen (legend-hunter archer, 14, with the stoat Snezhok), base 3D model (Docs/Briefs/A-Roster3D.md, wave 2). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_aspen.py -- [out_dir]
# Method = build_lark.py: analytic primitives -> voxel remesh (one smooth vinyl shell) -> colour per face by primitive list
# (label-only decals) -> decimate to budget -> smart UV -> Cycles bake high->low (RGB albedo, A gloss) -> aspen_tex.png 1024 ->
# rig with the 12 FighterBone bones + Extra (stoat in the quiver hood), rigid weights -> FBX/GLB.
# Weapon (bow with a nocked suction-cup arrow): aspen_weapon.fbx/.glb, vertex colour "Col" (alpha = gloss), origin = bow grip
# in the right hand (hang on Prop without offset), arrow along -Y, bow limbs along X, empty "Muzzle" at the suction cup.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "aspen")
os.makedirs(OUT, exist_ok=True)


def hexc(h, g):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [g]


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

PAL = {  # sRGB hex, gloss
    "skin": hexc("#EFC9A6", 0.15), "hair": hexc("#1F1C24", 0.45), "hair_h": hexc("#3A3544", 0.5),
    "jacket": hexc("#D9772E", 0.3), "jacket_d": hexc("#A9541F", 0.25), "shirt": hexc("#F3E6CC", 0.1),
    "pants": hexc("#56653F", 0.1), "boot": hexc("#6B4226", 0.3), "sole": hexc("#2C2622", 0.2),
    "glove": hexc("#5A3A24", 0.2), "strap": hexc("#4A2E1C", 0.25), "leather": hexc("#8A5530", 0.3),
    "cam": hexc("#2E3238", 0.6), "lens": hexc("#7FC4EC", 1.0), "chrome": hexc("#C9CED6", 0.8),
    "white": hexc("#F7F7F5", 0.6), "iris": hexc("#4A2C1A", 1.0), "pupil": hexc("#141414", 1.0),
    "mouth": hexc("#A9504A", 0.3), "blush": hexc("#F2A58E", 0.1),
    "stoat": hexc("#F8F6F0", 0.3), "stoat_tip": hexc("#26222A", 0.3), "glow": hexc("#8FE8FF", 1.0),
    "nose_s": hexc("#E79AA0", 0.4), "fur": hexc("#EFE1C2", 0.1),
    "wood": hexc("#B9824A", 0.55), "wood_d": hexc("#8C5A2E", 0.5), "teal": hexc("#2FA39A", 0.6),
    "string": hexc("#F2EEDF", 0.3), "shaft": hexc("#F2E2B6", 0.4), "cup": hexc("#3EC1B0", 0.7),
}

# ---------------- body (Blender: Z up, faces -Y, character's right = -X) ----------------
SH = {-1: (-0.15, 0.0, 0.60), 1: (0.15, 0.0, 0.60)}
EL = {-1: (-0.17, -0.10, 0.52), 1: (0.11, -0.04, 0.50)}
HA = {-1: (-0.10, -0.22, 0.50), 1: (-0.06, -0.12, 0.51)}  # right hand = bow grip, left hand draws the string
HIP = {-1: (-0.065, 0.0, 0.36), 1: (0.065, 0.0, 0.36)}
AN = {-1: (-0.08, 0.0, 0.09), 1: (0.08, 0.0, 0.09)}
EYE = {s: (0.062 * s, -0.143, 0.772) for s in (-1, 1)}
STOAT = (0.10, 0.17, 0.78)  # head of the stoat peeking out of the quiver hood over the left shoulder
EXTRA = STOAT
BODY = [
    E("skin", "Head", 1, (0, 0, 0.79), (0.165, 0.152, 0.16)),
    E("skin", "Head", 1, (0, -0.02, 0.72), (0.12, 0.12, 0.08)),  # jaw / cheeks
    E("hair", "Head", 2, (0, 0.035, 0.835), (0.18, 0.162, 0.165)),
    E("hair", "Head", 2, (-0.115, 0.015, 0.745), (0.075, 0.135, 0.125)),  # straight bob sides to the jaw
    E("hair", "Head", 2, (0.115, 0.015, 0.745), (0.075, 0.135, 0.125)),
    E("hair", "Head", 2, (0, 0.07, 0.73), (0.16, 0.11, 0.10)),  # bob back
    E("hair", "Head", 3, (0, -0.095, 0.895), (0.16, 0.07, 0.065)),  # blunt bangs
    E("hair_h", "Head", 4, (-0.06, -0.06, 0.975), (0.07, 0.05, 0.02), geo=False),  # shine streak
    E("skin", "Head", 3, (0, -0.158, 0.752), (0.016, 0.014, 0.018)),  # nose
    C("skin", "Head", 1, (0, 0, 0.60), (0, 0, 0.68), 0.045),  # neck
    E("jacket", "Body", 1, (0, 0, 0.52), (0.152, 0.118, 0.152)),
    E("shirt", "Body", 3, (0, -0.06, 0.625), (0.06, 0.07, 0.035)),  # collar opening
    E("fur", "Body", 3, (0, 0.06, 0.645), (0.13, 0.08, 0.045)),  # fold of the hood at the neck
    E("jacket_d", "Root", 2, (0, 0, 0.405), (0.155, 0.12, 0.045)),  # hem band
    E("pants", "Root", 1, (0, 0, 0.37), (0.12, 0.09, 0.07)),
    E("leather", "Root", 3, (0, 0, 0.44), (0.153, 0.119, 0.014)),  # belt
    E("leather", "Root", 4, (0.12, -0.07, 0.42), (0.035, 0.03, 0.04)),  # belt pouch
    # camera on a strap
    E("cam", "Body", 5, (0, -0.135, 0.545), (0.055, 0.03, 0.035)),
    E("chrome", "Body", 6, (0, -0.16, 0.54), (0.026, 0.02, 0.026)),
    E("lens", "Body", 7, (0, -0.178, 0.54), (0.018, 0.008, 0.018)),
    E("chrome", "Body", 6, (0.035, -0.13, 0.583), (0.01, 0.01, 0.008)),
    P("c", "strap", "Body", 4, False, a=(-0.045, -0.12, 0.57), b=(-0.09, -0.07, 0.64), ra=0.008, rb=0.008),
    P("c", "strap", "Body", 4, False, a=(0.045, -0.12, 0.57), b=(0.09, -0.07, 0.64), ra=0.008, rb=0.008),
    # pockets (decals)
    E("jacket_d", "Body", 4, (-0.085, -0.11, 0.47), (0.035, 0.03, 0.03), geo=False),
    E("jacket_d", "Body", 4, (0.085, -0.11, 0.47), (0.035, 0.03, 0.03), geo=False),
    # quiver across the back: leather tube from right hip to left shoulder, furry hood at the top
    C("leather", "Body", 3, (-0.08, 0.13, 0.43), (0.08, 0.15, 0.70), 0.05, 0.055),
    P("c", "strap", "Body", 4, False, a=(0.14, -0.09, 0.60), b=(-0.12, -0.10, 0.44), ra=0.01, rb=0.01),
    E("fur", "Body", 4, (0.09, 0.155, 0.72), (0.075, 0.07, 0.055)),  # hood
    C("shaft", "Body", 3, (0.03, 0.17, 0.70), (0.0, 0.20, 0.80), 0.008),  # spare arrows
    E("cup", "Body", 4, (0.0, 0.20, 0.805), (0.022, 0.022, 0.012)),
    C("shaft", "Body", 3, (0.05, 0.19, 0.70), (0.035, 0.24, 0.79), 0.008),
    E("cup", "Body", 4, (0.035, 0.24, 0.795), (0.022, 0.022, 0.012)),
    # stoat Snezhok
    E("stoat", "Extra", 5, STOAT, (0.045, 0.04, 0.04)),
    E("stoat", "Extra", 5, (STOAT[0], STOAT[1] - 0.035, STOAT[2] - 0.01), (0.025, 0.03, 0.022)),  # snout
    E("nose_s", "Extra", 6, (STOAT[0], STOAT[1] - 0.064, STOAT[2] - 0.008), (0.008, 0.006, 0.006)),
    E("stoat", "Extra", 5, (STOAT[0] - 0.03, STOAT[1] + 0.005, STOAT[2] + 0.035), (0.014, 0.01, 0.016)),  # ears
    E("stoat", "Extra", 5, (STOAT[0] + 0.03, STOAT[1] + 0.005, STOAT[2] + 0.035), (0.014, 0.01, 0.016)),
    E("glow", "Extra", 7, (STOAT[0] - 0.018, STOAT[1] - 0.036, STOAT[2] + 0.012), (0.008, 0.006, 0.008), geo=False),
    E("glow", "Extra", 7, (STOAT[0] + 0.018, STOAT[1] - 0.036, STOAT[2] + 0.012), (0.008, 0.006, 0.008), geo=False),
    C("stoat", "Extra", 4, (-0.10, 0.17, 0.44), (-0.14, 0.20, 0.38), 0.022, 0.018),  # tail out of the quiver bottom
    E("stoat_tip", "Extra", 5, (-0.145, 0.205, 0.37), (0.022, 0.022, 0.026)),
    # face
    E("white", "Head", 6, EYE[-1], (0.043, 0.02, 0.04)),
    E("white", "Head", 6, EYE[1], (0.043, 0.02, 0.04)),
    E("mouth", "Head", 7, (0.0, -0.152, 0.703), (0.028, 0.03, 0.008), geo=False),
]
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    eb = "Eye" + ("R" if s < 0 else "L")
    BODY += [E("iris", eb, 8, (ex, ey - 0.02, ez - 0.004), (0.028, 0.012, 0.03), geo=False),
             E("pupil", eb, 9, (ex, ey - 0.024, ez - 0.004), (0.014, 0.01, 0.016), geo=False),
             E("white", eb, 10, (ex + 0.012 * s, ey - 0.026, ez + 0.013), (0.007, 0.01, 0.008), geo=False),
             E("hair", "Head", 9, (ex, ey - 0.02, ez + 0.033), (0.04, 0.02, 0.0035), geo=False),  # upper lash line
             E("blush", "Head", 8, (0.095 * s, -0.125, 0.725), (0.026, 0.03, 0.014), geo=False)]
    side = "R" if s < 0 else "L"
    BODY += [C("jacket", "Arm" + side, 2, SH[s], EL[s], 0.05, 0.043),
             C("jacket", "Arm" + side, 2, EL[s], HA[s], 0.043, 0.036),
             E("jacket", "Arm" + side, 2, SH[s], (0.06, 0.06, 0.055)),
             E("jacket_d", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.8 + np.array(EL[s]) * 0.2), (0.04, 0.04, 0.04)),
             E("glove", "Arm" + side, 4, HA[s], (0.036, 0.04, 0.035)),
             C("pants", "Leg" + side, 1, HIP[s], AN[s], 0.055, 0.045),
             C("boot", "Leg" + side, 2, (AN[s][0], 0.0, 0.07), (AN[s][0], 0.0, 0.18), 0.05, 0.052),  # tall boots
             E("leather", "Leg" + side, 3, (AN[s][0], 0.0, 0.19), (0.056, 0.056, 0.016)),  # boot cuff
             E("boot", "Leg" + side, 3, (AN[s][0], -0.025, 0.055), (0.056, 0.088, 0.052)),
             E("sole", "Leg" + side, 4, (AN[s][0], -0.025, 0.004), (0.06, 0.092, 0.014))]

# ---------------- weapon: bow with suction-cup arrow (local: origin = grip, arrow along -Y, bow limbs along X) ----------------
WEAPON = [C("teal", "Prop", 4, (-0.025, 0, 0), (0.025, 0, 0), 0.02)]  # grip wrap
TIPS = []
for s in (-1, 1):
    pts = []
    for i in range(7):
        x = s * 0.035 * i
        y = 0.085 * (abs(x) / 0.21) ** 2 - 0.012 * (i == 6)
        pts.append((x, y, 0.0))
    for i in range(6):
        WEAPON.append(C("wood" if i % 2 == 0 else "wood_d", "Prop", 2, pts[i], pts[i + 1], 0.018 - 0.0015 * i, 0.018 - 0.0015 * (i + 1)))
    tip = (s * 0.225, 0.07, 0.0)
    WEAPON.append(C("wood_d", "Prop", 2, pts[6], tip, 0.009, 0.008))  # recurve tip
    WEAPON.append(E("teal", "Prop", 3, tip, (0.013, 0.013, 0.013)))
    TIPS.append(tip)
NOCK = (0.0, 0.12, 0.0)
for t in TIPS:
    WEAPON.append(C("string", "Prop", 3, t, NOCK, 0.0055))
WEAPON += [
    C("shaft", "Prop", 3, NOCK, (0, -0.20, 0), 0.0075),
    C("cup", "Prop", 4, (0.0, 0.10, 0.0), (0.0, 0.13, 0.0), 0.012),  # nock fletch
    E("cup", "Prop", 5, (0, -0.205, 0), (0.03, 0.012, 0.03)),  # suction cup
]
MUZZLE = (0, -0.22, 0.0)


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
high = build_shell(BODY, "AspenHigh", 0.0045)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Aspen"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("ASPEN body tris", TRIS)
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
timg = bpy.data.images.new("aspen_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "aspen_tex.png")
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
    ("Extra", "Body", EXTRA, tuple(np.array(EXTRA) + (0, 0, 0.06))),
]
arm_data = bpy.data.armatures.new("AspenRig")
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


export([arm, low], os.path.join(OUT, "aspen_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "AspenWeapon", 0.003)
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
export([gun, muzzle], os.path.join(OUT, "aspen_weapon"), {"MESH", "EMPTY"})
print("ASPEN done: body", TRIS, "weapon", gtris, "out", OUT)
