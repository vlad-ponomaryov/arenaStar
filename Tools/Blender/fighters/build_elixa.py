# Elixa (id "elixa") base model, Docs/Briefs/A-Roster3D.md. Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_elixa.py -- [out_dir]
# Simple "toy vinyl" figure from smooth primitives (no SDF bake yet): one mesh, palette texture 1024 (RGB = colour,
# A = gloss), vertex colour "Col" (same), FighterBone rig + Extra (cat on the left shoulder). Weapon = honey potion flask,
# separate mesh with origin at the right-hand grip, "forward" = -Y, empty "Muzzle" at the flask front.
# Format as Theo v3: Blender Z up, facing -Y, FBX axis_forward=-Z, axis_up=Y, FBX_SCALE_ALL, no leaf bones.
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "elixa")
ID = "elixa"
TEX = 1024
CELL = 64  # palette cell size in the texture

# palette: name -> (sRGB hex, gloss); plum / honey (Roster-v2 §4, Roster-v4 §7)
PAL = {
    "skin": ("#9A6243", 0.18), "skin_d": ("#7E4C33", 0.18), "blush": ("#B8705A", 0.18),
    "hair": ("#1E1726", 0.35), "hair_hi": ("#3A2C48", 0.40),
    "plum": ("#5B2A5E", 0.30), "plum_d": ("#3A1B40", 0.25), "plum_l": ("#7A3F7E", 0.35),
    "honey": ("#E8A93A", 0.85), "honey_l": ("#FFD27A", 1.0), "honey_glow": ("#FFC94A", 1.0),
    "cork": ("#8A5A32", 0.20), "glass": ("#F6E7C8", 1.0),
    "white": ("#F7F2EC", 1.0), "iris": ("#C9862A", 1.0), "pupil": ("#1A1210", 1.0),
    "cat": ("#1A1A22", 0.45), "cat_in": ("#5A3A4A", 0.3), "shoe": ("#4A2A1C", 0.6), "sock": ("#2A1830", 0.15),
    "star": ("#FFE08A", 1.0),
}
KEYS = list(PAL)


def hexc(h):
    h = h.lstrip("#")
    return [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]


def lin(c):
    c = np.asarray(c)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def cell_uv(key):
    i = KEYS.index(key)
    cx, cy = i % (TEX // CELL), i // (TEX // CELL)
    return ((cx + 0.5) * CELL / TEX, (cy + 0.5) * CELL / TEX)


bpy.ops.wm.read_factory_settings(use_empty=True)
PARTS = []  # (object, key, bone)


def finish(ob, key, bone, smooth=True):
    me = ob.data
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    u = cell_uv(key)
    uvl = me.uv_layers[0].data
    for l in uvl:
        l.uv = u
    c = hexc(PAL[key][0])
    col = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for d in col.data:
        d.color_srgb = (c[0], c[1], c[2], PAL[key][1])
    vg = ob.vertex_groups.new(name=bone)
    vg.add(list(range(len(me.vertices))), 1.0, "REPLACE")
    PARTS.append((ob, key, bone))
    return ob


def apply(ob):
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ob.select_set(False)


def ell(c, r, key, bone, seg=14, rings=8, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=1, location=c, rotation=rot)
    ob = bpy.context.object
    ob.scale = r
    apply(ob)
    return finish(ob, key, bone)


def tube(a, b, r1, r2, key, bone, n=10, cap=True):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=n, radius1=r1, radius2=r2, depth=d.length, location=(a + b) / 2,
                                    end_fill_type="NGON" if cap else "NOTHING")
    ob = bpy.context.object
    ob.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    apply(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode="OBJECT")
    return finish(ob, key, bone)


def torus(c, R, r, key, bone, maj=20, mn=5, rot=(0, 0, 0), sz=1.0):
    bpy.ops.mesh.primitive_torus_add(major_segments=maj, minor_segments=mn, major_radius=R, minor_radius=r,
                                     location=c, rotation=rot)
    ob = bpy.context.object
    ob.scale = (1, 1, sz)
    apply(ob)
    return finish(ob, key, bone)


SIDES = (-1, 1)  # -1 = character's right (-X), +1 = left
# ---------------------------------------------------------------------------------------------------- legs, shoes
for s in SIDES:
    L = "LegR" if s < 0 else "LegL"
    tube((s * 0.055, 0, 0.36), (s * 0.066, 0.004, 0.09), 0.036, 0.028, "sock", L, n=10)
    ell((s * 0.066, -0.018, 0.045), (0.042, 0.068, 0.040), "shoe", L, seg=12, rings=7)
    torus((s * 0.066, 0.004, 0.09), 0.031, 0.009, "honey", L, maj=12, mn=4)          # honey ankle cuff
# ---------------------------------------------------------------------------------------------------- dress
tube((0, 0.004, 0.29), (0, 0.002, 0.50), 0.165, 0.092, "plum", "Root", n=18)          # bell skirt
torus((0, 0.004, 0.292), 0.163, 0.014, "plum_d", "Root", maj=24, mn=5)                 # hem roll
ell((0, 0.002, 0.575), (0.098, 0.072, 0.115), "plum_d", "Body", seg=16, rings=9)       # bodice
torus((0, 0.002, 0.497), 0.093, 0.013, "honey", "Body", maj=20, mn=5)                  # honey sash
ell((0.0, -0.088, 0.497), (0.022, 0.012, 0.018), "honey_l", "Body", seg=8, rings=5)    # sash knot
# small cape collar
tube((0, 0.004, 0.62), (0, 0.004, 0.69), 0.125, 0.06, "plum_l", "Body", n=16)
# potion vials on the sash (honey)
for x in (-0.07, 0.07):
    ell((x, -0.062, 0.475), (0.016, 0.014, 0.022), "honey_glow", "Body", seg=8, rings=5)
# ---------------------------------------------------------------------------------------------------- arms
SH = {s: (s * 0.112, 0.0, 0.632) for s in SIDES}
WR = {s: (s * 0.215, -0.012, 0.420) for s in SIDES}
for s in SIDES:
    A = "ArmR" if s < 0 else "ArmL"
    ell(SH[s], (0.042, 0.042, 0.042), "plum", A, seg=10, rings=6)
    tube(SH[s], WR[s], 0.034, 0.042, "plum", A, n=10)                                  # bell sleeve
    w = Vector(WR[s])
    ell(tuple(w + Vector((s * 0.008, -0.004, -0.026))), (0.026, 0.024, 0.030), "skin", A, seg=10, rings=6)  # hand
# ---------------------------------------------------------------------------------------------------- head
tube((0, 0.004, 0.66), (0, 0.004, 0.71), 0.030, 0.030, "skin_d", "Head", n=8, cap=False)
HC = (0, -0.004, 0.795)
ell(HC, (0.118, 0.110, 0.122), "skin", "Head", seg=20, rings=12)
for s in SIDES:
    ell((s * 0.062, -0.088, 0.758), (0.022, 0.010, 0.014), "blush", "Head", seg=8, rings=5)
    ell((s * 0.112, 0.004, 0.790), (0.014, 0.016, 0.022), "skin", "Head", seg=8, rings=5)  # ears
EYE = {s: (s * 0.044, -0.098, 0.795) for s in SIDES}
for s in SIDES:
    e = EYE[s]
    ell(e, (0.026, 0.012, 0.030), "white", "Head", seg=12, rings=7)
    ell((e[0] + s * 0.002, e[1] - 0.008, e[2] - 0.002), (0.017, 0.008, 0.020), "iris", "Head", seg=10, rings=6)
    ell((e[0] + s * 0.002, e[1] - 0.013, e[2] - 0.002), (0.009, 0.005, 0.011), "pupil", "Head", seg=8, rings=5)
    ell((e[0] - s * 0.006, e[1] - 0.016, e[2] + 0.009), (0.004, 0.003, 0.004), "white", "Head", seg=6, rings=4)
    tube((e[0] - s * 0.018, e[1] + 0.004, e[2] + 0.040), (e[0] + s * 0.020, e[1] + 0.002, e[2] + 0.046),
         0.005, 0.003, "hair", "Head", n=6)  # brow
ell((0, -0.110, 0.770), (0.010, 0.008, 0.008), "skin_d", "Head", seg=8, rings=5)          # nose
tube((-0.016, -0.101, 0.738), (0.016, -0.101, 0.738), 0.004, 0.004, "skin_d", "Head", n=6)  # smirk
# ---------------------------------------------------------------------------------------------------- hair: thick black waves
ell((0, 0.020, 0.815), (0.130, 0.118, 0.120), "hair", "Head", seg=18, rings=10)            # cap
ell((0, -0.060, 0.858), (0.112, 0.060, 0.040), "hair", "Head", seg=14, rings=7)            # fringe
for s in SIDES:
    ell((s * 0.098, -0.048, 0.835), (0.040, 0.042, 0.050), "hair", "Head", seg=10, rings=6)  # side sweep
    for i, (x, y, z, r) in enumerate(((0.110, 0.020, 0.74, 0.050), (0.118, 0.050, 0.66, 0.046),
                                      (0.105, 0.070, 0.585, 0.042), (0.085, 0.080, 0.52, 0.034))):
        ell((s * x, y, z), (r * 0.85, r, r * 1.05), "hair" if i % 2 == 0 else "hair_hi", "Head", seg=10, rings=6)
for i, (y, z, r) in enumerate(((0.085, 0.73, 0.080), (0.090, 0.64, 0.072), (0.085, 0.56, 0.058))):
    ell((0, y, z), (r * 1.25, r * 0.6, r), "hair_hi" if i == 1 else "hair", "Head", seg=12, rings=7)
# ---------------------------------------------------------------------------------------------------- hat (signature from above)
HAT_Z = 0.890
tube((0, 0.012, HAT_Z - 0.008), (0, 0.012, HAT_Z + 0.008), 0.228, 0.220, "plum", "Head", n=28)   # wide brim
torus((0, 0.012, HAT_Z), 0.224, 0.010, "plum_d", "Head", maj=28, mn=4)                          # brim edge
tube((0, 0.012, HAT_Z), (0.0, 0.035, 1.010), 0.128, 0.085, "plum_l", "Head", n=18)              # crown lower
tube((0.0, 0.035, 1.010), (0.030, 0.075, 1.110), 0.085, 0.040, "plum_l", "Head", n=14)          # crown mid, bending back
tube((0.030, 0.075, 1.110), (0.085, 0.125, 1.150), 0.040, 0.006, "plum_l", "Head", n=10)        # floppy tip
torus((0, 0.014, HAT_Z + 0.028), 0.124, 0.016, "honey", "Head", maj=20, mn=5, sz=1.2)           # honey band
ell((-0.070, -0.100, HAT_Z + 0.030), (0.020, 0.010, 0.020), "star", "Head", seg=8, rings=5)     # buckle-star
# ---------------------------------------------------------------------------------------------------- cat "Ugolyok" on the left shoulder
CAT = Vector((0.150, -0.010, 0.705))
ell(tuple(CAT), (0.048, 0.072, 0.042), "cat", "Extra", seg=12, rings=7)                     # body lying along the shoulder
ell(tuple(CAT + Vector((0.016, -0.095, 0.040))), (0.046, 0.040, 0.040), "cat", "Extra", seg=12, rings=8)  # head
for s in SIDES:
    hp = CAT + Vector((0.016 + s * 0.026, -0.095, 0.072))
    tube(tuple(hp), tuple(hp + Vector((s * 0.008, 0.002, 0.030))), 0.015, 0.002, "cat", "Extra", n=6)   # ear
    ell(tuple(CAT + Vector((0.016 + s * 0.017, -0.131, 0.046))), (0.010, 0.005, 0.011), "honey_glow", "Extra", seg=8, rings=5)
    ell(tuple(CAT + Vector((0.016 + s * 0.017, -0.135, 0.046))), (0.003, 0.003, 0.008), "pupil", "Extra", seg=6, rings=4)
ell(tuple(CAT + Vector((0.016, -0.133, 0.030))), (0.007, 0.004, 0.005), "cat_in", "Extra", seg=6, rings=4)
pts = [CAT + Vector((0.0, 0.060, -0.005)), CAT + Vector((0.030, 0.090, -0.060)), CAT + Vector((0.040, 0.085, -0.120)),
       CAT + Vector((0.030, 0.060, -0.150))]
for a, b in zip(pts, pts[1:]):
    tube(tuple(a), tuple(b), 0.012, 0.010, "cat", "Extra", n=8)
for p in pts[1:]:
    ell(tuple(p), (0.012, 0.012, 0.012), "cat", "Extra", seg=8, rings=5)
for s in (-1, 1):  # paws hanging over the shoulder front
    ell(tuple(CAT + Vector((s * 0.022, -0.050, -0.030))), (0.012, 0.014, 0.016), "cat", "Extra", seg=8, rings=5)

# ---------------------------------------------------------------------------------------------------- join body
body = [p[0] for p in PARTS]
bpy.ops.object.select_all(action="DESELECT")
for o in body:
    o.select_set(True)
bpy.context.view_layer.objects.active = body[0]
bpy.ops.object.join()
ob = bpy.context.object
ob.name = ob.data.name = "Elixa"
PARTS.clear()
# budget: quadric decimation of the joined body (parts stay separate shells, palette UV per shell is kept)
_n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
if _n > 3950:
    dm = ob.modifiers.new("Dec", "DECIMATE")
    dm.ratio = 3950 / _n
    dm.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier="Dec")

# ---------------------------------------------------------------------------------------------------- weapon: potion flask
GRIP = Vector((-0.223, -0.022, 0.392))  # centre of the right hand (Prop head)
# modelled around origin: neck/cork held in the fist, round honey body forward (-Y) and a bit up
ell((0, -0.060, 0.030), (0.052, 0.052, 0.054), "honey_glow", "Prop", seg=16, rings=10)            # liquid bulb
ell((0, -0.072, 0.050), (0.018, 0.012, 0.016), "honey_l", "Prop", seg=8, rings=5)                 # highlight
torus((0, -0.060, 0.030), 0.050, 0.006, "honey", "Prop", maj=16, mn=4, rot=(0, math.radians(90), 0))  # gold strap
tube((0, -0.012, 0.022), (0, 0.010, 0.018), 0.020, 0.018, "glass", "Prop", n=10)                  # neck (in fist)
tube((0, 0.010, 0.018), (0, 0.034, 0.016), 0.022, 0.020, "cork", "Prop", n=10)                    # cork
ell((0, -0.112, 0.030), (0.016, 0.006, 0.016), "star", "Prop", seg=8, rings=5)                    # star label front
for a in range(4):  # little star on the strap top
    t = a * math.pi / 2
    tube((0, -0.060, 0.086), (0.018 * math.cos(t), -0.060 + 0.018 * math.sin(t), 0.088), 0.006, 0.001, "star", "Prop", n=4)
wobs = [p[0] for p in PARTS]
bpy.ops.object.select_all(action="DESELECT")
for o in wobs:
    o.select_set(True)
bpy.context.view_layer.objects.active = wobs[0]
bpy.ops.object.join()
weapon = bpy.context.object
weapon.name = weapon.data.name = "ElixaFlask"
weapon.vertex_groups.clear()
muzzle = bpy.data.objects.new("Muzzle", None)
muzzle.empty_display_size = 0.02
muzzle.location = (0, -0.118, 0.030)
bpy.context.scene.collection.objects.link(muzzle)
muzzle.parent = weapon

# ---------------------------------------------------------------------------------------------------- normalise height (body without hat = 1.0 like Theo? keep hat extra)
V = np.array([v.co[:] for v in ob.data.vertices])
zmin = V[:, 2].min()
shift = Vector((-(V[:, 0].min() + V[:, 0].max()) / 2 * 0, 0, -zmin))
ob.data.transform(Matrix.Translation(shift))
for o in (weapon,):
    pass

# ---------------------------------------------------------------------------------------------------- texture + material
img_px = np.zeros((TEX, TEX, 4), dtype=np.float32)
img_px[..., :3] = 0.5
img_px[..., 3] = 0.2
for k, (h, g) in PAL.items():
    i = KEYS.index(k)
    cx, cy = i % (TEX // CELL), i // (TEX // CELL)
    img_px[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL, :3] = hexc(h)
    img_px[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL, 3] = g
os.makedirs(OUT, exist_ok=True)
tex_path = os.path.join(OUT, f"{ID}_tex.png")
img = bpy.data.images.new(f"{ID}_tex", TEX, TEX, alpha=True)
img.pixels.foreach_set(img_px.ravel())  # bpy rows bottom-up = v up: cell_uv matches
img.filepath_raw = tex_path
img.file_format = "PNG"
img.alpha_mode = "CHANNEL_PACKED"
img.save()


def material(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    return m


mat = material("FighterVC")
for o in (ob, weapon):
    o.data.materials.clear()
    o.data.materials.append(mat)

# ---------------------------------------------------------------------------------------------------- rig
z = -shift.z
BONES = [  # name, parent, head, tail
    ("Root", None, (0, 0, 0), (0, 0, 0.36)),
    ("Body", "Root", (0, 0, 0.36), (0, 0, 0.665)),
    ("Head", "Body", (0, 0, 0.665), (0, 0, 0.92)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", (-0.055, 0, 0.36), (-0.066, 0.004, 0.09)),
    ("LegL", "Root", (0.055, 0, 0.36), (0.066, 0.004, 0.09)),
    ("Prop", "Body", tuple(GRIP), tuple(GRIP + Vector((0, -0.08, 0)))),
    ("EyeR", "Head", EYE[-1], (EYE[-1][0], EYE[-1][1] - 0.05, EYE[-1][2])),
    ("EyeL", "Head", EYE[1], (EYE[1][0], EYE[1][1] - 0.05, EYE[1][2])),
    ("LidR", "Head", EYE[-1], (EYE[-1][0], EYE[-1][1] - 0.04, EYE[-1][2])),
    ("LidL", "Head", EYE[1], (EYE[1][0], EYE[1][1] - 0.04, EYE[1][2])),
    ("Extra", "Body", tuple(CAT), tuple(CAT + Vector((0, -0.07, 0.04)))),
]
ad = bpy.data.armatures.new("Rig")
arm = bpy.data.objects.new("Rig", ad)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for n, par, h, t in BONES:
    eb = ad.edit_bones.new(n)
    eb.head = Vector(h) + shift
    eb.tail = Vector(t) + shift
    if par:
        eb.parent = ad.edit_bones[par]
        eb.use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")
for n, *_ in BONES:
    if n not in ob.vertex_groups:
        ob.vertex_groups.new(name=n)
ob.parent = arm
mod = ob.modifiers.new("Armature", "ARMATURE")
mod.object = arm
# weapon at the grip for the .blend/preview only: exported with origin at the grip
weapon.location = (0, 0, 0)

ntri = sum(len(p.vertices) - 2 for p in ob.data.polygons)
wtri = sum(len(p.vertices) - 2 for p in weapon.data.polygons)
H = max(v.co.z for v in ob.data.vertices)
print(f"ELIXA body tris {ntri} verts {len(ob.data.vertices)} height {H:.3f}; weapon tris {wtri}")


# ---------------------------------------------------------------------------------------------------- export
def triangulate(o):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode="OBJECT")


triangulate(ob)
triangulate(weapon)


def export(objs, base):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, axis_forward="-Z", axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL", add_leaf_bones=False, mesh_smooth_type="FACE",
                             colors_type="SRGB", object_types={"ARMATURE", "MESH", "EMPTY"}, path_mode="STRIP")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)


export([arm, ob], os.path.join(OUT, f"{ID}_game"))
export([weapon, muzzle], os.path.join(OUT, f"{ID}_weapon"))
print("ELIXA done", OUT)
