# Procedural 3D model of Theo, the starter shooter (Docs/Briefs/A-Theo3D.md step 2), until the TRELLIS.2 body replaces it.
# Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_theo.py -- [out_dir]
#   Tools/blender.ps1 -Script Tools/Blender/fighters/build_theo.py
# Output (default Art/Source/3D/theo/): theo_game.fbx/.glb (body, armature with FighterBone names, weights) and
# theo_blaster.fbx/.glb (mesh only, origin = right-hand grip). Format = Tools/Blender/prepare_fighter.py:
#   Blender Z up, character faces -Y, height 1.0, feet on z = 0, centred on XZ; FBX axis_forward=-Z, axis_up=Y (Unity: faces +Z,
#   right hand on +X); one mesh, one material; colour only in the "Col" attribute (FLOAT_COLOR, linear in Blender, the FBX
#   writes sRGB - colors_type="SRGB", Unity linearises), alpha = gloss (0 matte cloth/skin .. 1 glass/glow).
# Parts are low-poly primitives (UV spheres, elliptic lathes, bevelled boxes, prisms, hair blades) coloured flat per face
# from the palette + ray-cast AO. Weights: rigid per part, smooth blends at shoulders, hips and waist.
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
sys.path.insert(0, os.path.join(here, "..", "env"))
from envcommon import ambient_occlusion, srgb_to_lin, hex_rgb  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = os.path.abspath(argv[0]) if argv else os.path.join(repo, "Art", "Source", "3D", "theo")
os.makedirs(out_dir, exist_ok=True)
log = lambda *a: print("[theo]", *a, flush=True)

BODY_BUDGET, BLASTER_BUDGET = 4000, 1200

# ---------- palette: sRGB hex, gloss (vertex alpha), AO weight ----------
PAL = {
    "red":        ("#B3141C", 0.70, 1.0),
    "red_dark":   ("#8A0E15", 0.55, 1.0),
    "gold":       ("#E0A93A", 0.85, 1.0),
    "gold_dark":  ("#B98228", 0.70, 1.0),
    "graphite":   ("#3D3836", 0.10, 1.0),
    "graphite_d": ("#2E2A29", 0.10, 1.0),
    "belt":       ("#4A403B", 0.20, 1.0),
    "grip":       ("#6B6460", 0.30, 1.0),
    "metal":      ("#A29C98", 0.80, 1.0),
    "headset":    ("#4C4846", 0.50, 1.0),
    "skin":       ("#F6C7A6", 0.15, 0.8),
    "skin_shade": ("#E8A88A", 0.10, 0.8),
    "mouth":      ("#8E3B30", 0.20, 0.5),
    "hair":       ("#E3B04E", 0.30, 1.0),
    "hair_dark":  ("#C38A36", 0.25, 1.0),
    "hair_light": ("#F2CD72", 0.35, 1.0),
    "brow":       ("#A87232", 0.10, 0.6),
    "eye_white":  ("#FFFFFF", 1.00, 0.3),
    "iris":       ("#7A4A24", 1.00, 0.3),
    "pupil":      ("#2B1D12", 1.00, 0.3),
    "visor":      ("#FF9A2E", 1.00, 0.2),
    "visor_rim":  ("#E0741A", 0.80, 0.6),
    "glow":       ("#FFB040", 1.00, 0.2),
    "glow_hot":   ("#FFD47A", 1.00, 0.1),
    "cyan":       ("#46D3D6", 1.00, 0.3),
}


# ---------- primitives (local space) ----------
def uvsphere(u, v):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    return bm


def box(size, bevel=0.0, segs=1, axes="xyz"):
    """Centred box; bevel only edges parallel to the listed axes."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for vv in bm.verts:
        vv.co = Vector((vv.co.x * size[0], vv.co.y * size[1], vv.co.z * size[2]))
    if bevel > 0:
        idx = {"x": 0, "y": 1, "z": 2}
        edges = [e for e in bm.edges if any(abs((e.verts[1].co - e.verts[0].co).normalized()[idx[a]]) > 0.9 for a in axes)]
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type="OFFSET", segments=segs, profile=0.5, affect="EDGES",
                        clamp_overlap=True)
    return bm


def lathe(prof, segs, closed=False, phase=0.0):
    """prof: [(r, z)] or [(rx, ry, z)] around local Z. Faces carry the band index in the int layer 'ring'
    (band k = between profile points k and k+1; caps get -1 at the start, len(prof) - 1 at the end)."""
    bm = bmesh.new()
    ring_l = bm.faces.layers.int.new("ring")
    rings = []
    for p in prof:
        rx, ry, z = (p[0], p[0], p[1]) if len(p) == 2 else p
        if rx < 1e-7:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((rx * math.cos(phase + 2 * math.pi * j / segs), ry * math.sin(phase + 2 * math.pi * j / segs), z))
                          for j in range(segs)])
    pairs = list(zip(rings, rings[1:]))
    if closed:
        pairs.append((rings[-1], rings[0]))
    for k, (A, B) in enumerate(pairs):
        for j in range(segs):
            q = (j + 1) % segs
            if len(A) == 1 and len(B) > 1:
                f = bm.faces.new((A[0], B[q], B[j]))
            elif len(B) == 1 and len(A) > 1:
                f = bm.faces.new((A[j], A[q], B[0]))
            elif len(A) > 1:
                f = bm.faces.new((A[j], A[q], B[q], B[j]))
            else:
                continue
            f[ring_l] = k
    if not closed:
        if len(rings[0]) > 1:
            bm.faces.new(list(reversed(rings[0])))[ring_l] = -1
        if len(rings[-1]) > 1:
            bm.faces.new(rings[-1])[ring_l] = len(prof) - 1
    return bm


def prism(outline, width, bevel=0.0):
    """Outline [(y, z)] in the side view, extruded along X (centred)."""
    bm = bmesh.new()
    a = [bm.verts.new((-width / 2, y, z)) for y, z in outline]
    b = [bm.verts.new((width / 2, y, z)) for y, z in outline]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, offset_type="OFFSET", segments=1, profile=0.5, affect="EDGES",
                        clamp_overlap=True)
    return bm


def blade(length, r0, flat, bend, segs=4):
    """Hair feather along local Z: wide on X, thin on Y, tip curls along +Y by `bend`."""
    prof = [(r0, r0 * flat, 0.0), (r0 * 0.8, r0 * flat * 0.8, length * 0.35), (r0 * 0.45, r0 * flat * 0.5, length * 0.72), (0, 0, length)]
    bm = lathe(prof, segs, phase=math.pi / segs)
    for vv in bm.verts:
        t = vv.co.z / length
        vv.co.y += bend * t * t
    return bm


# ---------- transforms ----------
def xf(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    R = Matrix.Identity(4)
    for axis, a in zip("XYZ", rot):
        if a:
            R = Matrix.Rotation(math.radians(a), 4, axis) @ R
    return Matrix.Translation(loc) @ R @ Matrix.Diagonal((*scale, 1.0))


def frame(origin, z_dir, y_hint=(0, 0, 1)):
    """Matrix with local Z along z_dir, local Y as close as possible to y_hint."""
    z = Vector(z_dir).normalized()
    y = Vector(y_hint)
    y = (y - z * y.dot(z))
    if y.length < 1e-6:
        y = Vector((1, 0, 0)) - z * z.x
    y.normalize()
    x = y.cross(z)
    M = Matrix((x, y, z)).transposed().to_4x4()
    M.translation = Vector(origin)
    return M


def smooth(e0, e1, t):
    t = min(max((t - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


# ---------- model accumulator ----------
class Model:
    def __init__(self):
        self.co, self.faces, self.fkey, self.w = [], [], [], []

    def add(self, bm, color, M=None, bone="Body"):
        """color: palette key or fn(info) -> key with info = dict(lc, wc, n, ring); bone: name, {name: w} or fn(world co) -> {name: w}."""
        M = M or Matrix.Identity(4)
        bm.verts.index_update()
        bm.normal_update()
        ring_l = bm.faces.layers.int.get("ring")
        base = len(self.co)
        for vv in bm.verts:
            self.co.append(M @ vv.co)
        for f in bm.faces:
            self.faces.append([base + vv.index for vv in f.verts])
            if callable(color):
                lc = f.calc_center_median()
                info = {"lc": lc, "wc": M @ lc, "n": f.normal.copy(), "ring": f[ring_l] if ring_l is not None else 0}
                self.fkey.append(color(info))
            else:
                self.fkey.append(color)
        for vv in bm.verts:
            if callable(bone):
                self.w.append(bone(M @ vv.co))
            elif isinstance(bone, dict):
                self.w.append(dict(bone))
            else:
                self.w.append({bone: 1.0})
        bm.free()

    def tris(self):
        return sum(len(f) - 2 for f in self.faces)

    def build(self, name, ao_rays=24, ao_dist=0.07, ao_strength=0.38):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(c) for c in self.co], [], self.faces)
        me.update()
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        # triangulate here so the face -> colour key list stays aligned (ngons split into several triangles)
        keys = list(self.fkey)
        layer = me.attributes.new("fk", "INT", "FACE")
        layer.data.foreach_set("value", np.arange(len(keys), dtype=np.int32))
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        m = obj.modifiers.new("Tri", "TRIANGULATE")
        m.quad_method, m.ngon_method = "BEAUTY", "BEAUTY"
        bpy.ops.object.modifier_apply(modifier=m.name)
        fk = np.empty(len(me.polygons), dtype=np.int32)
        me.attributes["fk"].data.foreach_get("value", fk)
        me.attributes.remove(me.attributes["fk"])
        occl = ambient_occlusion(obj, rays=ao_rays, distance=ao_dist) if ao_rays else np.zeros(len(me.vertices))
        lv = np.empty(len(me.loops), dtype=np.int64)
        me.loops.foreach_get("vertex_index", lv)
        lf = np.repeat(np.arange(len(me.polygons)), [len(p.loop_indices) for p in me.polygons])
        srgb = np.array([hex_rgb(PAL[keys[i]][0]) for i in fk])
        gloss = np.array([PAL[keys[i]][1] for i in fk])
        aow = np.array([PAL[keys[i]][2] for i in fk])
        shade = 1.0 - ao_strength * aow[lf] * occl[lv]
        col = np.clip(srgb[lf] * shade[:, None], 0, 1)
        attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
        attr.data.foreach_set("color", np.hstack([srgb_to_lin(col), gloss[lf][:, None]]).astype(np.float32).ravel())
        me.color_attributes.active_color = attr
        me.color_attributes.render_color_index = 0
        mat = bpy.data.materials.new("FighterVC")
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Col"
        nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.6
        me.materials.append(mat)
        for p in me.polygons:
            p.use_smooth = True
        if hasattr(me, "set_sharp_from_angle"):
            me.set_sharp_from_angle(angle=math.radians(55))
        self.used_keys = sorted(set(keys[i] for i in fk))
        self.ao_mean = float(1.0 - ao_strength * occl.mean())
        return obj, fk


# =====================================================================================================================
# BODY (height 1.0; reference Art/Source/Flow/2026-10-19_theo-3dref/001.png, 925 px tall, centre x = 512)
# =====================================================================================================================
B = Model()
HEAD_C, HEAD_R = Vector((0, 0, 0.79)), Vector((0.125, 0.115, 0.125))
CAP_C, CAP_R = Vector((0, 0.03, 0.835)), Vector((0.138, 0.130, 0.118))
SHOULDER = lambda s: Vector((s * 0.135, 0.0, 0.62))
WRIST = lambda s: Vector((s * 0.262, -0.005, 0.395))
HAND = lambda s: Vector((s * 0.276, -0.012, 0.345))
HIP = lambda s: Vector((s * 0.060, 0.0, 0.375))
ANKLE = lambda s: Vector((s * 0.082, 0.0, 0.10))
SIDE = {-1: "R", 1: "L"}  # character right = -X (faces -Y)


def head_surface(x, z, inset=0.0):
    """Point and normal on the head ellipsoid front (y < 0) at (x, z)."""
    u, w = x / HEAD_R.x, (z - HEAD_C.z) / HEAD_R.z
    y = -HEAD_R.y * math.sqrt(max(1 - u * u - w * w, 0.02))
    p = Vector((x, y, z))
    n = Vector((x / HEAD_R.x ** 2, y / HEAD_R.y ** 2, (z - HEAD_C.z) / HEAD_R.z ** 2)).normalized()
    return p - n * inset, n


# --- head: ellipsoid with a narrower jaw ---
bm = uvsphere(12, 9)
for vv in bm.verts:
    k = max(0.0, -vv.co.z)
    vv.co.x *= 1 - 0.22 * k * k
    vv.co.y *= 1 - 0.08 * k * k
    if vv.co.z < 0 and vv.co.y < 0:
        vv.co.y -= 0.06 * k * (-vv.co.y)  # chin slightly forward
B.add(bm, "skin", xf(HEAD_C, scale=HEAD_R), "Head")
for s in (-1, 1):  # ears
    B.add(uvsphere(6, 5), "skin", xf((s * 0.121, 0.008, 0.775), (0, 0, s * -10), (0.02, 0.03, 0.038)), "Head")
B.add(uvsphere(6, 4), "skin_shade", xf((0, -0.113, 0.764), scale=(0.017, 0.013, 0.013)), "Head")  # nose
p, n = head_surface(0.006, 0.727, 0.004)
B.add(uvsphere(8, 4), "mouth", xf(p, (0, 4, 0), (0.030, 0.007, 0.006)), "Head")  # small smile
for s in (-1, 1):  # brows (the left one, above the visor, raised)
    p, n = head_surface(s * 0.052, 0.853 + (0.006 if s > 0 else 0), 0.0)
    B.add(box((0.048, 0.012, 0.010), 0.003, 1, "x"), "brow", frame(p, n, (0, 0, 1)) @ xf(rot=(0, 0, s * 7)), "Head")

# --- eyes: lathe disc along the gaze (white, brown iris, pupil); on EyeL / EyeR ---
EYE = {}
for s in (-1, 1):
    p, n = head_surface(s * 0.053, 0.802, 0.006)
    d = (n + Vector((0, -1.6, 0))).normalized()
    EYE[s] = (p, d)
    zt = 0.012
    prof = [(0, 0, zt), (0.0085, 0.0095, zt - 0.0006), (0.016, 0.018, zt - 0.002), (0.026, 0.029, zt - 0.0055), (0.029, 0.032, zt - 0.010),
            (0.027, 0.030, 0.0)]
    col = lambda i: "pupil" if i["ring"] == 0 else ("iris" if i["ring"] == 1 else "eye_white")
    B.add(lathe(prof, 8), col, frame(p, d, (0, 0, 1)), "Eye" + SIDE[s])

# --- visor monocle over the left eye (+X, viewer's right in 001), strap to the ear piece ---
pv, dv = EYE[1]
Mv = frame(pv + dv * 0.02 + Vector((0.004, 0, 0.002)), dv, (0, 0, 1))
lens = lambda i: "visor" if abs(i["n"].z) > 0.7 else "visor_rim"
B.add(box((0.090, 0.060, 0.012), 0.014, 2, "z"), lens, Mv, "Head")
B.add(box((0.006, 0.010, 0.042), 0.0, 1), "visor_rim", frame(Vector((0.112, -0.062, 0.806)), Vector((0.45, 1, 0)), (0, 0, 1)), "Head")
for s, key in ((-1, "headset"), (1, "headset")):  # ear pieces (right one carries the mic boom)
    B.add(lathe([(0, 0.0), (0.022, 0.0), (0.024, 0.006), (0.024, 0.012), (0.016, 0.018), (0, 0.018)], 8),
          lambda i: "gold" if i["ring"] in (1, 2) else "headset", xf((s * 0.128, 0.0, 0.783), (0, s * 90, 0)), "Head")
a, b = Vector((-0.140, -0.012, 0.772)), Vector((-0.078, -0.098, 0.722))
B.add(lathe([(0.0042, 0.0), (0.0042, (b - a).length)], 4), "headset", frame(a, b - a), "Head")  # mic boom
B.add(uvsphere(6, 4), "graphite_d", xf(b, scale=(0.010, 0.010, 0.010)), "Head")

# --- hair: cap + feathers sweeping up and to the left, back ones down ---
def hair_key(i):
    z = i["wc"].z
    return "hair_light" if z > 0.955 else ("hair_dark" if z < 0.80 else "hair")
bm = uvsphere(12, 7)
B.add(bm, hair_key, xf(CAP_C, (-14, 0, 0), CAP_R), "Head")
TUFTS = [  # azimuth from front toward +X, elevation, direction (world), length, base radius, curl (+ away from the head, - hugs it)
    (-30, 28, (0.62, -0.55, 0.50), 0.090, 0.050, 0.012),   # fringe sweeps up and to the left (+X)
    (0, 30, (0.75, -0.48, 0.42), 0.098, 0.054, 0.014),
    (28, 24, (0.88, -0.30, 0.32), 0.085, 0.048, 0.010),
    (55, 14, (0.85, -0.10, -0.15), 0.068, 0.042, 0.000),
    (-60, 14, (-0.45, -0.30, -0.70), 0.064, 0.040, -0.004),  # right temple lock
    (-10, 62, (0.30, 0.55, 0.45), 0.088, 0.056, -0.012),     # crown, lying back
    (45, 55, (0.70, 0.45, 0.30), 0.082, 0.052, -0.012),
    (-60, 50, (-0.70, 0.50, 0.30), 0.082, 0.052, -0.012),
    (100, 35, (0.65, 0.60, -0.15), 0.075, 0.048, -0.010),
    (-100, 35, (-0.65, 0.60, -0.15), 0.075, 0.048, -0.010),
    (180, 55, (0.05, 0.85, 0.10), 0.085, 0.056, -0.012),
    (140, 30, (0.40, 0.70, -0.45), 0.078, 0.050, -0.010),
    (-140, 30, (-0.40, 0.70, -0.45), 0.078, 0.050, -0.010),
    (180, 8, (0.00, 0.30, -0.95), 0.075, 0.050, -0.006),     # back, falling down
    (125, 4, (0.45, 0.20, -0.87), 0.064, 0.044, -0.004),
    (-125, 4, (-0.45, 0.20, -0.87), 0.064, 0.044, -0.004),
]
for az, el, dvec, L, r0, curl in TUFTS:
    a_, e_ = math.radians(az), math.radians(el)
    u = Vector((math.sin(a_) * math.cos(e_), -math.cos(a_) * math.cos(e_), math.sin(e_)))
    rx = Matrix.Rotation(math.radians(-14), 3, "X")  # same tilt as the cap
    base = CAP_C + rx @ (Vector((u.x * CAP_R.x, u.y * CAP_R.y, u.z * CAP_R.z)) * 0.93)
    nrm = (rx @ Vector((u.x / CAP_R.x, u.y / CAP_R.y, u.z / CAP_R.z))).normalized()
    B.add(blade(L, r0, 0.30, curl), hair_key, frame(base, Vector(dvec), nrm), "Head")

# --- torso (Body; waist blends into Root) ---
waist = lambda c: {"Body": smooth(0.40, 0.475, c.z), "Root": 1 - smooth(0.40, 0.475, c.z)}
B.add(lathe([(0.098, 0.072, 0.40), (0.106, 0.075, 0.50), (0.110, 0.076, 0.58), (0.100, 0.070, 0.65), (0.060, 0.052, 0.69)], 12),
      "graphite", None, waist)
chest = [(0.100, 0.080, 0.470), (0.112, 0.086, 0.488), (0.118, 0.089, 0.53), (0.121, 0.090, 0.59), (0.116, 0.086, 0.64),
         (0.098, 0.076, 0.668), (0.074, 0.062, 0.684), (0.062, 0.054, 0.688)]
chest_col = lambda i: "gold" if i["ring"] in (0, 5, 6) else ("red_dark" if i["ring"] == -1 else "red")
B.add(lathe(chest, 12), chest_col, xf((0, -0.006, 0)), "Body")
B.add(lathe([(0.062, 0.054, 0.655), (0.066, 0.058, 0.700), (0.062, 0.054, 0.714), (0.050, 0.044, 0.716)], 12),
      lambda i: "gold" if i["ring"] >= 1 else "graphite_d", None, "Body")  # collar
# emblem: orange orbit ring around a gold comet head + gold tail
em = frame(Vector((0.0, -0.093, 0.598)), Vector((0, -1, 0.12)), (0, 0, 1))
B.add(lathe([(0, 0, 0.007), (0.010, 0.010, 0.0065), (0.013, 0.013, 0.006), (0.024, 0.017, 0.005), (0.027, 0.019, 0.0)], 10),
      lambda i: "gold" if i["ring"] <= 0 else ("red_dark" if i["ring"] == 1 else "glow"), em, "Body")
B.add(box((0.030, 0.006, 0.007), 0.002, 1, "x"), "gold", em @ xf((0.017, 0.012, 0.006), (0, 0, 30)), "Body")
# shoulder pads: gold dome + lower plate, half on the arm
for s in (-1, 1):
    pad = {"Body": 0.6, "Arm" + SIDE[s]: 0.4}
    B.add(uvsphere(8, 5), "gold", xf((s * 0.148, 0.0, 0.648), (0, s * 28, 0), (0.068, 0.074, 0.046)), pad)
    B.add(uvsphere(8, 4), "gold_dark", xf((s * 0.172, 0.0, 0.602), (0, s * 40, 0), (0.056, 0.068, 0.030)), pad)

# --- pelvis, belt with glowing lights, thigh pouches (Root) ---
B.add(lathe([(0.075, 0.060, 0.300), (0.097, 0.072, 0.335), (0.104, 0.075, 0.385), (0.102, 0.074, 0.430)], 12), "graphite", None, waist)
B.add(lathe([(0.106, 0.078, 0.418), (0.111, 0.082, 0.425), (0.111, 0.082, 0.466), (0.106, 0.078, 0.473)], 14), "belt", None, waist)
B.add(box((0.042, 0.010, 0.036), 0.004, 1, "y"), "gold", xf((0, -0.084, 0.444)), "Root")
B.add(box((0.026, 0.006, 0.020)), "red", xf((0, -0.090, 0.444)), "Root")
for x in (-0.078, -0.045, 0.045, 0.078):
    y = -0.082 * math.sqrt(max(1 - (x / 0.111) ** 2, 0.0)) - 0.002
    B.add(box((0.018, 0.010, 0.026)), "glow", xf((x, y, 0.444), (0, 0, math.degrees(math.atan2(x, 0.18)))), "Root")
for x in (-0.05, 0.05):
    B.add(box((0.020, 0.010, 0.024)), "glow", xf((x, 0.074, 0.444)), "Root")
for s in (-1, 1):
    B.add(box((0.026, 0.050, 0.062), 0.006, 1, "x"), "graphite_d", xf((s * 0.112, -0.012, 0.352), (0, s * -8, 0)), "Leg" + SIDE[s])

# --- arms: graphite sleeve, red/gold bracer, gold cuff, graphite glove (smooth blend at the shoulder) ---
for s in (-1, 1):
    arm = "Arm" + SIDE[s]
    S, W, H = SHOULDER(s), WRIST(s), HAND(s)
    ax = (W - S)
    L = ax.length
    Ma = frame(S, ax, (0, -1, 0))
    def arm_w(c, S=S, d=ax.normalized(), arm=arm):
        t = (c - S).dot(d)
        k = smooth(-0.005, 0.045, t)
        return {arm: k, "Body": 1 - k}
    B.add(lathe([(0.032, 0.0), (0.037, 0.02), (0.033, 0.5 * L), (0.029, L)], 8), "graphite", Ma, arm_w)
    bz0, bz1 = 0.50 * L, 0.90 * L
    br = [(0.040, bz0), (0.045, bz0 + 0.016), (0.042, bz0 + 0.020), (0.042, bz1 - 0.020), (0.046, bz1 - 0.016), (0.040, bz1 + 0.004)]
    B.add(lathe(br, 8, phase=math.pi / 8), lambda i: "red" if i["ring"] == 2 else "gold", Ma, arm)
    gp = Ma @ Vector((0, 0, (bz0 + bz1) / 2)) + Vector((s, 0, 0)) * 0.043 + Vector((0, -0.006, 0))
    B.add(box((0.008, 0.020, 0.026)), "cyan", xf(gp, (0, s * -25, 0)), arm)
    B.add(lathe([(0.034, L - 0.004), (0.040, L + 0.004), (0.039, L + 0.022), (0.030, L + 0.026)], 8), "gold", Ma, arm)  # glove cuff
    tilt = math.degrees(math.atan2(-ax.x, -ax.z))
    B.add(uvsphere(8, 6), "graphite", xf(H, (0, tilt, 0), (0.028, 0.040, 0.052)), arm)  # mitten hand, palm inward
    B.add(uvsphere(6, 4), "graphite", xf(H + Vector((-s * 0.008, -0.030, 0.012)), (0, tilt, 0), (0.012, 0.012, 0.024)), arm)  # thumb
    B.add(box((0.012, 0.040, 0.030), 0.004, 1, "x"), "gold", xf(H + Vector((s * 0.022, -0.002, 0.018)), (0, tilt, 0)), arm)  # back plate

# --- legs: graphite with orange seams, gold knee pads, red boots with gold cuff, sole and glowing ankle discs ---
for s in (-1, 1):
    leg = "Leg" + SIDE[s]
    Hh, A = HIP(s), ANKLE(s)
    ax = A - Hh
    L = ax.length
    Ml = frame(Hh, ax, (0, -1, 0))
    def leg_w(c, Hh=Hh, d=ax.normalized(), leg=leg):
        k = smooth(-0.01, 0.05, (c - Hh).dot(d))
        return {leg: k, "Root": 1 - k}
    B.add(lathe([(0.050, 0.0), (0.053, 0.03), (0.048, 0.45 * L), (0.042, 0.75 * L), (0.040, L)], 8), "graphite", Ml, leg_w)
    for ang in (-0.55 * s, -0.05 * s):  # front-outer seams on the thigh
        dir_ = Vector((math.sin(ang) * -1, -math.cos(ang), 0))
        p0 = Hh + Vector((0, 0, -0.025)) + dir_ * 0.051
        p1 = Hh + ax * 0.46 + dir_ * 0.047
        B.add(box((0.006, 0.006, (p1 - p0).length)), "glow", frame((p0 + p1) / 2, p1 - p0, dir_), leg)
    kp = Hh + ax * 0.55 + Vector((s * 0.002, -0.036, 0))
    B.add(uvsphere(7, 5), "gold", xf(kp, (8, 0, 0), (0.047, 0.030, 0.052)), leg)
    bc = Vector((s * 0.086, 0.004, 0))
    boot = [(0.054, 0.060, 0.020), (0.056, 0.062, 0.150), (0.061, 0.067, 0.168), (0.063, 0.069, 0.204), (0.050, 0.056, 0.210)]
    B.add(lathe(boot, 10), lambda i: "gold" if i["ring"] >= 2 else "red", xf(bc), leg)
    B.add(uvsphere(8, 5), "red", xf(bc + Vector((0, -0.040, 0.050)), (0, 0, 0), (0.061, 0.092, 0.046)), leg)  # toe cap
    B.add(box((0.122, 0.200, 0.022), 0.008, 1, "z"), lambda i: "glow" if i["lc"].z < 0 and abs(i["n"].z) < 0.5 else "gold", xf(bc + Vector((0, -0.032, 0.011))), leg)  # sole, glowing lower lip
    disc = [(0, 0.010), (0.013, 0.010), (0.022, 0.006), (0.022, 0.0)]
    B.add(lathe(disc, 8), lambda i: "glow_hot" if i["ring"] == 0 else "gold", xf(bc + Vector((s * 0.058, 0.0, 0.088)), (0, s * 90, 0)), leg)

body_tris = B.tris()
log("body parts tris (before triangulate)", body_tris)

# =====================================================================================================================
# BLASTER (reference 003.png side view; origin = right-hand grip; barrel along -Y; length 0.60 of the body height)
# =====================================================================================================================
G = Model()
SC = 0.60 / 1210.0  # metres per reference pixel (stock butt x = 85 .. muzzle x = 1295)
GX, GY = 460, 545   # grip centre in the reference
P = lambda px, py: (-(px - GX) * SC, (GY - py) * SC)
AXZ = (GY - 345) * SC  # barrel axis height
along = lambda px0, z=AXZ, x=0.0: Matrix.Translation((x, -(px0 - GX) * SC, z)) @ Matrix.Rotation(math.radians(90), 4, "X")

G.add(prism([P(*q) for q in [(135, 272), (432, 262), (440, 447), (300, 442), (238, 524), (140, 552)]], 0.060, 0.006), "red")  # stock
G.add(box((0.068, 56 * SC, 290 * SC), 0.010, 1, "x"), "gold", xf((0, *P(114, 411))))  # butt plate
y, z = P(320, 296)
G.add(box((0.064, 236 * SC, 64 * SC), 0.006, 1, "y"), "gold", xf((0, y, z)))  # top rail
y, z = P(308, 367)
G.add(box((0.064, 200 * SC, 40 * SC)), "glow", xf((0, y, z)))  # stock glow strip
G.add(prism([P(*q) for q in [(412, 300), (472, 232), (845, 226), (856, 470), (846, 562), (652, 562), (606, 452), (430, 449)]], 0.074, 0.008),
      "red")  # receiver
cell = [(0, 0, 0.0), (0.054, 0.034, 0.0), (0.054, 0.034, 0.016), (0.049, 0.029, 0.020), (0.049, 0.029, 0.104), (0.054, 0.034, 0.108),
        (0.054, 0.034, 0.124), (0, 0, 0.124)]
G.add(lathe(cell, 8), lambda i: "glow" if i["ring"] == 3 else "gold", along(522, (GY - 343) * SC))  # energy cell
y, z = P(722, 512)
G.add(box((0.080, 150 * SC, 122 * SC)), "gold", xf((0, y, z)))  # lower cell frame
G.add(box((0.086, 112 * SC, 88 * SC), 0.008, 1, "x"), "glow", xf((0, y, z)))  # lower cell glass
G.add(prism([P(*q) for q in [(445, 448), (568, 448), (512, 628), (482, 650), (404, 632)]], 0.044, 0.008), "grip")  # grip
G.add(prism([P(*q) for q in [(398, 620), (502, 626), (496, 668), (400, 662)]], 0.050), "gold")  # grip cap
G.add(prism([P(*q) for q in [(548, 452), (590, 452), (566, 506)]], 0.012), "grip")  # trigger
# scope
sc = [(0, 0.0), (0.020, 0.0), (0.020, 0.030), (0.015, 0.032), (0.015, 0.075), (0.022, 0.090), (0.030, 0.140), (0.033, 0.142),
      (0.033, 0.160), (0, 0.160)]
G.add(lathe(sc, 8), lambda i: {0: "gold", 1: "gold", 2: "metal", 3: "metal", 4: "red", 5: "red", 6: "gold", 7: "gold", 8: "glow_hot"}.get(i["ring"], "gold"),
      along(540, (GY - 158) * SC))
y, z = P(676, 206)
G.add(box((0.030, 92 * SC, 72 * SC), 0.004, 1, "y"), "metal", xf((0, y, z)))  # scope mount
G.add(box((0.034, 24 * SC, 24 * SC)), "glow", xf((0, y, (GY - 160) * SC)))  # mount light
# barrel: gold collar, metal core, glowing coils, vent block with fins, red bell muzzle with gold rim and glowing bore
G.add(lathe([(0.050, 0.0), (0.057, 0.006), (0.057, 0.024), (0.050, 0.028)], 10), "gold", along(842))
G.add(lathe([(0.036, 0.0), (0.036, 0.130)], 8), "graphite", along(870))
for k in range(4):
    G.add(lathe([(0.034, 0.0), (0.058, 0.007), (0.034, 0.014)], 8, closed=True), "glow", along(902 + k * 48))
y, z = P(978, 478)
G.add(box((0.060, 236 * SC, 70 * SC), 0.006, 1, "y"), "gold", xf((0, y, z)))  # vent frame
for k in range(5):
    G.add(box((0.072, 0.007, 56 * SC)), "metal", xf((0, P(892 + k * 40, 0)[0], z)))
bell = [(0.040, 0.0), (0.050, 0.012), (0.060, 0.045), (0.062, 0.072), (0.066, 0.074), (0.066, 0.098), (0.050, 0.098), (0.046, 0.090), (0, 0.090)]
G.add(lathe(bell, 10), lambda i: "red" if i["ring"] in (-1, 0, 1, 2) else ("gold" if i["ring"] in (3, 4, 5) else "glow_hot"), along(1100))
y, z = P(1210, 212)
G.add(box((0.022, 60 * SC, 40 * SC), 0.005, 1, "x"), "gold", xf((0, y, z)))  # muzzle fin
log("blaster parts tris (before triangulate)", G.tris())


# =====================================================================================================================
# build, rig, export
# =====================================================================================================================
bpy.ops.wm.read_factory_settings(use_empty=True)
body, _ = B.build("Theo")
co = np.array([tuple(c) for c in B.co])
lo, hi = co.min(0), co.max(0)
log("body bounds", np.round(lo, 3), np.round(hi, 3))
# normalise exactly: height 1, feet on 0, centred on X/Y (as prepare_fighter.py)
sh = Vector((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]))
sc_ = 1.0 / (hi[2] - lo[2])
body.data.transform(Matrix.Diagonal((sc_, sc_, sc_, 1)) @ Matrix.Translation(sh))
norm = lambda v: (Vector(v) + sh) * sc_

BONES = [  # name, parent, head, tail
    ("Root", None, (0, 0, 0), (0, 0, 0.375)),
    ("Body", "Root", (0, 0, 0.375), (0, 0, 0.665)),
    ("Head", "Body", (0, 0, 0.665), (0, 0, 1.0)),
    ("ArmR", "Body", SHOULDER(-1), WRIST(-1)),
    ("ArmL", "Body", SHOULDER(1), WRIST(1)),
    ("LegR", "Root", HIP(-1), ANKLE(-1)),
    ("LegL", "Root", HIP(1), ANKLE(1)),
    ("Prop", "Body", HAND(-1), HAND(-1) + Vector((0, -0.10, 0))),
    ("EyeR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("EyeL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("LidR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("LidL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
]
arm_data = bpy.data.armatures.new("Rig")
arm = bpy.data.objects.new("Rig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for name, parent, h, t in BONES:
    eb = arm_data.edit_bones.new(name)
    eb.head, eb.tail = norm(h), norm(t)
    if parent:
        eb.parent = arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode="OBJECT")
# weights (per original vertex; triangulation keeps vertex order)
groups = {name: body.vertex_groups.new(name=name) for name, *_ in BONES}
infl = []
for i, w in enumerate(B.w):
    tot = sum(w.values())
    used = 0
    for name, val in w.items():
        if val / tot > 1e-4:
            groups[name].add([i], val / tot, "REPLACE")
            used += 1
    infl.append(used)
body.parent = arm
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm
tri_body = len(body.data.polygons)
weighted = {name: sum(1 for w in B.w if w.get(name, 0) > 1e-4) for name, *_ in BONES}
log("weights per bone", weighted, "max influences", max(infl), "blended verts", sum(1 for x in infl if x > 1))

base = os.path.join(out_dir, "theo_game")
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                         mesh_smooth_type="FACE", bake_anim=False)
bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
bd = body.data
log(f"RESULT body tris {tri_body}/{BODY_BUDGET} {'OK' if tri_body <= BODY_BUDGET else 'OVER'} verts {len(bd.vertices)} "
    f"bones {len(BONES)} colours {len(B.used_keys)} ao mean {B.ao_mean:.2f} prop head {tuple(round(c, 3) for c in norm(HAND(-1)))} "
    f"scale {sc_:.4f} -> {base}.fbx")

# blaster: same scale as the body so it matches the hand
blaster, _ = G.build("TheoBlaster", ao_dist=0.05)
blaster.data.transform(Matrix.Diagonal((sc_, sc_, sc_, 1)))
gco = np.array([tuple(c) for c in G.co]) * sc_
glo, ghi = gco.min(0), gco.max(0)
bpy.ops.object.select_all(action="DESELECT")
blaster.select_set(True)
bpy.context.view_layer.objects.active = blaster
gbase = os.path.join(out_dir, "theo_blaster")
bpy.ops.export_scene.fbx(filepath=gbase + ".fbx", use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", colors_type="SRGB", mesh_smooth_type="FACE", bake_anim=False)
bpy.ops.export_scene.gltf(filepath=gbase + ".glb", export_format="GLB", use_selection=True)
tri_g = len(blaster.data.polygons)
log(f"RESULT blaster tris {tri_g}/{BLASTER_BUDGET} {'OK' if tri_g <= BLASTER_BUDGET else 'OVER'} verts {len(blaster.data.vertices)} "
    f"size x {ghi[0]-glo[0]:.3f} y(len) {ghi[1]-glo[1]:.3f} z {ghi[2]-glo[2]:.3f} (y {glo[1]:.3f}..{ghi[1]:.3f}, z {glo[2]:.3f}..{ghi[2]:.3f}) "
    f"-> {gbase}.fbx")

# ---------- round-trip check of the FBX (what Unity will read) ----------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=base + ".fbx", colors_type="SRGB")
m = next(o for o in bpy.context.scene.objects if o.type == "MESH")
a = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
dg = bpy.context.evaluated_depsgraph_get()
ev = m.evaluated_get(dg)
pts = np.array([tuple(ev.matrix_world @ vv.co) for vv in ev.to_mesh().vertices])
ca = m.data.color_attributes[0]
cv = np.empty(len(ca.data) * 4, dtype=np.float32)
ca.data.foreach_get("color", cv)
cv = cv.reshape(-1, 4)
log("FBX check: bones", sorted(b.name for b in a.data.bones), "mesh height", round(float(pts[:, 2].max() - pts[:, 2].min()), 4),
    "x", np.round([pts[:, 0].min(), pts[:, 0].max()], 3), "colour attr", ca.name, ca.domain, ca.data_type,
    "alpha values", sorted(set(np.round(cv[:, 3], 2).tolist())), "materials", len(m.data.materials))
