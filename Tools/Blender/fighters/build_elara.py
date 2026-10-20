# Elara (stargazer's apprentice, 12, mage-artillery), base 3D model (Docs/Briefs/A-Roster3D.md, wave 2). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_elara.py -- [out_dir]
# Method = build_lark.py: analytic primitives -> voxel remesh (one smooth vinyl shell) -> colour per face by primitive list
# (label-only decals: star map, eyes, blush) -> decimate to budget -> smart UV -> Cycles bake high->low (RGB albedo, A gloss)
# -> elara_tex.png 1024 -> rig with the 12 FighterBone bones (no Extra), rigid weights -> FBX/GLB.
# Weapon (telescope-staff with a star): elara_weapon.fbx/.glb, vertex colour "Col" (alpha = gloss), origin = grip in the right
# hand (hang on Prop without offset), staff up +Z, telescope leaning forward (-Y), empty "Muzzle" at the star on top.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "elara")
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
    "skin": hexc("#6B4230", 0.2), "hair": hexc("#2B1B16", 0.3), "hair_h": hexc("#4A3128", 0.35),
    "pearl": hexc("#F4EFE6", 0.7), "pearl_d": hexc("#E2D8CA", 0.6), "peach": hexc("#F7B08F", 0.35),
    "peach_d": hexc("#E8906C", 0.3), "gold": hexc("#E8B84A", 0.85), "star": hexc("#FFE07A", 1.0),
    "line": hexc("#D9B866", 0.6), "sole": hexc("#B98A36", 0.5),
    "white": hexc("#F7F7F5", 0.6), "iris": hexc("#6A3E1E", 1.0), "pupil": hexc("#141010", 1.0),
    "mouth": hexc("#8A3A36", 0.3), "blush": hexc("#B8604E", 0.1), "lens": hexc("#BFE6FF", 1.0),
    "brass": hexc("#C99A44", 0.8),
}

# ---------------- body (Blender: Z up, faces -Y, character's right = -X); 12 y.o. -> top of curls ~1.0 ----------------
SH = {-1: (-0.14, 0.0, 0.585), 1: (0.14, 0.0, 0.585)}
EL = {-1: (-0.20, -0.03, 0.50), 1: (0.20, 0.0, 0.49)}
HA = {-1: (-0.17, -0.11, 0.44), 1: (0.19, -0.06, 0.42)}  # right hand holds the telescope-staff
HIP = {-1: (-0.055, 0.0, 0.34), 1: (0.055, 0.0, 0.34)}
AN = {-1: (-0.07, 0.0, 0.085), 1: (0.07, 0.0, 0.085)}
EYE = {s: (0.063 * s, -0.142, 0.762) for s in (-1, 1)}
EXTRA = (0.0, 0.03, 0.93)  # unused (no extra geometry); kept out of the rig
BODY = [
    E("skin", "Head", 1, (0, 0, 0.775), (0.16, 0.148, 0.155)),
    E("skin", "Head", 1, (0, -0.02, 0.71), (0.118, 0.118, 0.078)),  # jaw / cheeks
    E("skin", "Head", 3, (0, -0.155, 0.742), (0.019, 0.015, 0.016)),  # nose
    E("skin", "Head", 2, (-0.158, 0.0, 0.765), (0.022, 0.03, 0.035)),  # ears
    E("skin", "Head", 2, (0.158, 0.0, 0.765), (0.022, 0.03, 0.035)),
    C("skin", "Head", 1, (0, 0, 0.58), (0, 0, 0.67), 0.043),  # neck
    # cloud of curls
    E("hair", "Head", 2, (0, 0.04, 0.85), (0.19, 0.18, 0.15)),
    E("hair_h", "Head", 4, (-0.07, -0.02, 0.985), (0.07, 0.06, 0.02), geo=False),  # shine
]
for i in range(10):  # ring of puffs around the head
    a = math.radians(-150 + i * 33.3)
    x, y = 0.155 * math.sin(a), 0.045 + 0.14 * math.cos(a)
    BODY.append(E("hair", "Head", 2, (x, y, 0.86 + 0.03 * math.cos(a * 2)), (0.078, 0.078, 0.078)))
for x, y, z, r in ((-0.09, 0.0, 0.97, 0.08), (0.09, 0.0, 0.97, 0.08), (0, 0.09, 0.96, 0.085), (0, -0.06, 0.955, 0.075),
                   (-0.13, 0.10, 0.76, 0.075), (0.13, 0.10, 0.76, 0.075), (0, 0.15, 0.76, 0.08)):
    BODY.append(E("hair", "Head", 2, (x, y, z), (r, r, r)))
for x in (-0.09, -0.03, 0.03, 0.09):  # curls along the hairline
    BODY.append(E("hair", "Head", 3, (x, -0.12, 0.895 - 0.02 * abs(x) / 0.09), (0.04, 0.035, 0.035)))
for s in (-1, 1):  # crescent hair clips (3 beads along an arc)
    for k, ang in enumerate((-50, 0, 50)):
        a = math.radians(ang)
        cx, cz = 0.165 * s + 0.028 * math.cos(a) * s, 0.905 + 0.028 * math.sin(a)
        BODY.append(E("gold", "Head", 5, (cx, -0.085, cz), (0.016 - 0.004 * (k != 1), 0.016, 0.016 - 0.004 * (k != 1))))
BODY += [
    # round cape-collar with the star map (top view: a big pearl disc)
    E("pearl", "Body", 3, (0, 0.015, 0.58), (0.275, 0.245, 0.07)),
    E("peach", "Body", 4, (0, 0.015, 0.552), (0.278, 0.248, 0.03), geo=False),
    E("gold", "Body", 5, (0, -0.11, 0.645), (0.028, 0.02, 0.026)),  # clasp
    E("star", "Body", 6, (0, -0.132, 0.645), (0.014, 0.01, 0.014)),
    # dress
    C("peach", "Body", 1, (0, 0, 0.53), (0, 0, 0.40), 0.11, 0.155),
    E("peach", "Root", 1, (0, 0, 0.37), (0.165, 0.145, 0.06)),  # flared skirt hem
    E("gold", "Root", 4, (0, 0, 0.325), (0.17, 0.15, 0.016), geo=False),  # hem band
    E("peach_d", "Root", 3, (0, 0, 0.455), (0.14, 0.115, 0.013), geo=False),  # waist sash
    # face
    E("white", "Head", 6, EYE[-1], (0.045, 0.02, 0.05)),
    E("white", "Head", 6, EYE[1], (0.045, 0.02, 0.05)),
    E("mouth", "Head", 7, (0.0, -0.15, 0.692), (0.026, 0.03, 0.01), geo=False),
]
# star map on the collar: gold dots + thin constellation lines
STARS = [(-0.2, -0.06), (-0.13, -0.155), (-0.05, -0.2), (0.08, -0.18), (0.18, -0.1), (0.225, 0.035),
         (0.15, 0.15), (-0.02, 0.2), (-0.17, 0.13), (-0.235, 0.02)]
for x, y in STARS:
    BODY.append(E("star", "Body", 7, (x, y + 0.015, 0.62), (0.014, 0.014, 0.06), geo=False))
for (x0, y0), (x1, y1) in zip(STARS[:5], STARS[1:6]):
    BODY.append(P("c", "line", "Body", 6, False, a=(x0, y0 + 0.015, 0.63), b=(x1, y1 + 0.015, 0.63), ra=0.004, rb=0.004))
for (x0, y0), (x1, y1) in zip(STARS[6:9], STARS[7:10]):
    BODY.append(P("c", "line", "Body", 6, False, a=(x0, y0 + 0.015, 0.63), b=(x1, y1 + 0.015, 0.63), ra=0.004, rb=0.004))
for s in (-1, 1):
    ex, ey, ez = EYE[s]
    eb = "Eye" + ("R" if s < 0 else "L")
    BODY += [E("iris", eb, 8, (ex, ey - 0.02, ez - 0.004), (0.03, 0.012, 0.034), geo=False),
             E("pupil", eb, 9, (ex, ey - 0.024, ez - 0.004), (0.015, 0.01, 0.018), geo=False),
             E("white", eb, 10, (ex + 0.012 * s, ey - 0.026, ez + 0.015), (0.008, 0.01, 0.009), geo=False),
             E("star", eb, 10, (ex - 0.01 * s, ey - 0.026, ez - 0.018), (0.005, 0.01, 0.005), geo=False),
             E("hair", "Head", 9, (ex, ey - 0.02, ez + 0.042), (0.042, 0.02, 0.0035), geo=False),  # lash line
             E("blush", "Head", 8, (0.098 * s, -0.122, 0.712), (0.026, 0.03, 0.014), geo=False)]
    side = "R" if s < 0 else "L"
    BODY += [C("pearl", "Arm" + side, 2, SH[s], EL[s], 0.045, 0.04),
             C("pearl", "Arm" + side, 2, EL[s], HA[s], 0.04, 0.046),  # bell sleeve
             E("gold", "Arm" + side, 3, tuple(np.array(HA[s]) * 0.75 + np.array(EL[s]) * 0.25), (0.046, 0.046, 0.014), geo=False),
             E("skin", "Arm" + side, 4, HA[s], (0.034, 0.038, 0.034)),
             C("skin", "Leg" + side, 1, HIP[s], AN[s], 0.045, 0.038),
             E("pearl", "Leg" + side, 3, (AN[s][0], -0.022, 0.05), (0.05, 0.08, 0.048)),
             E("gold", "Leg" + side, 4, (AN[s][0], -0.08, 0.06), (0.018, 0.014, 0.018)),  # star buckle
             E("sole", "Leg" + side, 4, (AN[s][0], -0.022, 0.004), (0.054, 0.084, 0.013))]

# ---------------- weapon: telescope-staff (local: origin = grip, staff up +Z, telescope leans forward -Y) ----------------
TOP = (0, -0.11, 0.50)
WEAPON = [
    C("pearl", "Prop", 1, (0, 0.0, -0.40), (0, 0.0, 0.12), 0.017),  # staff rod
    E("gold", "Prop", 3, (0, 0, -0.40), (0.024, 0.024, 0.02)),  # foot cap
    E("peach", "Prop", 3, (0, 0, -0.03), (0.024, 0.024, 0.05)),  # grip wrap
    E("gold", "Prop", 4, (0, 0, 0.03), (0.026, 0.026, 0.01)),
    E("gold", "Prop", 4, (0, 0, -0.09), (0.026, 0.026, 0.01)),
    C("brass", "Prop", 2, (0, 0.0, 0.10), (0, -0.05, 0.28), 0.026, 0.03),  # telescope sections
    C("pearl", "Prop", 2, (0, -0.05, 0.28), (0, -0.09, 0.42), 0.032, 0.036),
    E("gold", "Prop", 3, (0, -0.05, 0.28), (0.036, 0.036, 0.016)),
    C("brass", "Prop", 2, (0, -0.09, 0.42), TOP, 0.04, 0.044),
    E("peach", "Prop", 3, (0, -0.09, 0.42), (0.042, 0.042, 0.014)),
    E("lens", "Prop", 5, (0, -0.113, 0.51), (0.034, 0.034, 0.01)),
]
SC = (0, -0.16, 0.60)  # five-point star on top of the telescope (in the XZ plane)
WEAPON.append(E("star", "Prop", 6, SC, (0.026, 0.018, 0.026)))
for k in range(5):
    a = math.radians(90 + k * 72)
    WEAPON.append(C("star", "Prop", 5, SC, (SC[0] + 0.06 * math.cos(a), SC[1], SC[2] + 0.06 * math.sin(a)), 0.017, 0.005))
WEAPON.append(C("gold", "Prop", 4, TOP, (0, -0.14, 0.56), 0.008))  # star mount
MUZZLE = SC


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
high = build_shell(BODY, "ElaraHigh", 0.0045)
face_colors(high, BODY)
bpy.ops.object.select_all(action="DESELECT")
high.select_set(True)
bpy.context.view_layer.objects.active = high
bpy.ops.object.duplicate()
low = bpy.context.object
low.name = "Elara"
low.data.color_attributes.remove(low.data.color_attributes["Col"])
TRIS = decimate(low, 3950)
print("ELARA body tris", TRIS)
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
timg = bpy.data.images.new("elara_tex", RES, RES, alpha=True)
timg.pixels.foreach_set(tex.astype(np.float32).ravel())
tex_path = os.path.join(OUT, "elara_tex.png")
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
]
arm_data = bpy.data.armatures.new("ElaraRig")
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


export([arm, low], os.path.join(OUT, "elara_game"), {"ARMATURE", "MESH"})

# ================================ weapon ================================
gun = build_shell(WEAPON, "ElaraWeapon", 0.0035)
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
export([gun, muzzle], os.path.join(OUT, "elara_weapon"), {"MESH", "EMPTY"})
print("ELARA done: body", TRIS, "weapon", gtris, "out", OUT)
