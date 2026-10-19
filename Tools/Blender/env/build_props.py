# Procedural "vinyl toy" frame props and mode pickups (A.4a, Docs/Briefs/A-EnvTarget.md §3.1 path B). Runs headless:
#   Tools/blender.ps1 -Script Tools/Blender/env/build_props.py [-ScriptArgs @('Tools/Blender/env/props.json','--only','lantern,acorn')]
#   blender -b --factory-startup --python Tools/Blender/env/build_props.py -- [props.json] [--only a,b]
# Every size, colour and segment count lives in props.json. Per prop: parts built with bmesh (lathe / box with bevel / prism /
# blades) -> vertex colours (sRGB hex, alpha = gloss) -> AO by BVH ray casts with an analytic ground plane + ground-contact
# darkening -> triangulate -> weighted normals -> FBX (Unity) + GLB into <exportDir>/<name>.*. Log: tris, bounds, colours, budget.
import bpy, bmesh, sys, os, json, math, random
from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
cfg_path = next((a for a in argv if a.endswith(".json")), os.path.join(here, "props.json"))
cfg_path = cfg_path if os.path.isabs(cfg_path) else os.path.join(repo, cfg_path)
only = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
cfg = json.load(open(cfg_path, encoding="utf-8"))
out_dir = os.path.join(repo, cfg["exportDir"])
os.makedirs(out_dir, exist_ok=True)
log = lambda *a: print("[props]", *a, flush=True)


# ---------- colour ----------
def hex_rgb(h):
    h = h.lstrip("#")
    return Vector([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def ramp(stops, t):
    t = min(max(t, 0.0), 1.0) * (len(stops) - 1)
    i = min(int(t), len(stops) - 2)
    return stops[i].lerp(stops[i + 1], t - i)


def to_linear(c):
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


def paint(spec, pts, axis_origin):
    """spec: {hex} | {grad:[bottom..top]} along part z | {radial:[inner,outer]} by distance from part axis; + gloss, ao (AO weight)."""
    if "hex" in spec:
        cols = [hex_rgb(spec["hex"])] * len(pts)
    elif "grad" in spec:
        stops = [hex_rgb(h) for h in spec["grad"]]
        z0, z1 = min(p.z for p in pts), max(p.z for p in pts)
        cols = [ramp(stops, (p.z - z0) / max(z1 - z0, 1e-6)) for p in pts]
    else:
        stops = [hex_rgb(h) for h in spec["radial"]]
        d = [(p - axis_origin).xy.length for p in pts]
        dmax = max(max(d), 1e-6)
        cols = [ramp(stops, x / dmax) for x in d]
    return [Vector((c.x, c.y, c.z, spec.get("gloss", 0.1))) for c in cols]


# ---------- geometry primitives (local: bottom at z = 0, centred on the Z axis) ----------
def bm_lathe(profile, segs, cap_bottom=True, cap_top=True, phase=0.0):
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r < 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(phase + 2 * math.pi * j / segs), r * math.sin(phase + 2 * math.pi * j / segs), z))
                          for j in range(segs)])
    for A, B in zip(rings, rings[1:]):
        for j in range(segs):
            k = (j + 1) % segs
            if len(A) == 1 and len(B) > 1:
                bm.faces.new((A[0], B[k], B[j]))
            elif len(B) == 1 and len(A) > 1:
                bm.faces.new((A[j], A[k], B[0]))
            elif len(A) > 1:
                bm.faces.new((A[j], A[k], B[k], B[j]))
    if cap_bottom and len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if cap_top and len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    return bm


def bm_box(size, bevel=0.0, segs=1, axes="xyz", open_bottom=False, open_top=False):
    """Box with its bottom at z = 0; bevel only edges parallel to the listed axes."""
    sx, sy, sz = size
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz + sz / 2))
    if bevel > 0:
        idx = {"x": 0, "y": 1, "z": 2}
        edges = [e for e in bm.edges if any(abs((e.verts[1].co - e.verts[0].co).normalized()[idx[a]]) > 0.9 for a in axes)]
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type="OFFSET", segments=segs, profile=0.5, affect="EDGES", clamp_overlap=True)
    drop = [f for f in bm.faces if (open_bottom and all(v.co.z < 1e-5 for v in f.verts)) or (open_top and all(v.co.z > sz - 1e-5 for v in f.verts))]
    if drop:
        bmesh.ops.delete(bm, geom=drop, context="FACES_ONLY")
    return bm


def bm_prism(outline, depth, open_bottom=True):
    """Outline (x, z) counter-clockwise seen from -Y, extruded along Y (centred)."""
    bm = bmesh.new()
    f = [bm.verts.new((x, -depth / 2, z)) for x, z in outline]
    b = [bm.verts.new((x, depth / 2, z)) for x, z in outline]
    bm.faces.new(f)
    bm.faces.new(list(reversed(b)))
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        if open_bottom and outline[i][1] < 1e-6 and outline[j][1] < 1e-6:
            continue
        bm.faces.new((f[i], f[j], b[j], b[i]))
    return bm


def bm_rock(size, segs, rings, amp, scale, seed, flat=0.06, squash=0.15):
    prof = [(math.sin(math.pi * i / rings) * 0.5, 0.5 - 0.5 * math.cos(math.pi * i / rings)) for i in range(rings + 1)]
    bm = bm_lathe(prof, segs, phase=seed)
    off = Vector((seed * 7.31, seed * 3.17, seed * 1.93))
    sx, sy, sz = size
    uz = bm.verts.layers.float.new("uz")  # undeformed height 0..1 (moss edge follows the lathe rings)
    for v in bm.verts:
        v[uz] = v.co.z
        p = v.co.copy()
        d = Vector((p.x, p.y, p.z - 0.5))
        n = d.normalized() if d.length > 1e-6 else Vector((0, 0, 1))
        p = p + n * amp / max(sx, 1e-6) * noise.noise(p * scale + off)
        z = p.z * sz
        top = 1.0 - squash * max(0.0, (p.z - 0.6) / 0.4) ** 2  # flatter, broader crown (pebble, not egg)
        v.co = Vector((p.x * sx * (1.0 + 0.12 * (1 - abs(p.z - 0.45) * 2)), p.y * sy * (1.0 + 0.12 * (1 - abs(p.z - 0.45) * 2)), z * top))
    zmin = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z = max(v.co.z - zmin - flat * sz, 0.0)
    bm.normal_update()
    return bm


def xf(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    R = Matrix.Identity(4)
    for axis, a in zip("XYZ", rot):
        if a:
            R = Matrix.Rotation(math.radians(a), 4, axis) @ R
    return Matrix.Translation(loc) @ R @ Matrix.Diagonal((*scale, 1.0))


def align_z(normal):
    return Vector((0, 0, 1)).rotation_difference(normal).to_matrix().to_4x4()


class Prop:
    def __init__(self):
        self.co, self.faces, self.col, self.ao = [], [], [], []

    def add(self, bm, spec, mat=None, recalc=True, ao=1.0):
        mat = mat or Matrix.Identity(4)
        if recalc:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.verts.index_update()
        base = len(self.co)
        pts = [mat @ v.co for v in bm.verts]
        self.co += pts
        self.col += paint(spec, pts, mat @ Vector((0, 0, 0)))
        self.ao += [ao * spec.get("ao", 1.0)] * len(pts)
        self.faces += [tuple(base + v.index for v in f.verts) for f in bm.faces]
        bm.free()


# ---------- prop builders ----------
def build_fence_picket(p, P):
    L, n, w, d, h, drop = p["length"], p["pickets"], p["picketWidth"], p["picketDepth"], p["picketHeight"], p["tipDrop"]
    outline = [(-w / 2, 0), (w / 2, 0), (w / 2, h - drop), (w * 0.3, h - drop * 0.25), (0, h), (-w * 0.3, h - drop * 0.25), (-w / 2, h - drop)]
    for i in range(n):
        P.add(bm_prism(outline, d), p["picket"], xf(((i + 0.5) * L / n - L / 2, 0, 0)))
    for z in p["railZ"]:
        P.add(bm_box((L, p["railDepth"], p["railHeight"])), p["rail"], xf((0, p["railY"], z)))


def build_fence_rail(p, P):
    L, n, r, h, rt = p["length"], p["posts"], p["postRadius"], p["postHeight"], p["postTopRound"]
    for i in range(n):
        prof = [(r, 0), (r, h - rt), (r * 0.62, h - rt * 0.15), (0, h)]
        P.add(bm_lathe(prof, p["postSegments"], cap_bottom=False, phase=math.pi / p["postSegments"]), p["post"],
              xf(((i + 0.5) * L / n - L / 2, 0, 0)))
    for z in p["railZ"]:
        bm = bm_box((L, p["railWidth"], p["railHeight"]), p["railBevel"], p["railBevelSegments"], axes="x")
        P.add(bm, p["rail"], xf((0, 0, z - p["railHeight"] / 2)))


def build_lantern(p, P):
    b, po, cap, arm, lp = p["base"], p["post"], p["cap"], p["arm"], p["lamp"]
    P.add(bm_box(b["size"], b["bevel"], b["segments"], axes="z", open_bottom=True), b["color"])
    z = b["size"][2] - 0.01
    P.add(bm_box(po["size"], po["bevel"], 1, axes="z", open_bottom=True), po["color"], xf((0, 0, z)))
    z_top = z + po["size"][2]
    cr, ch = cap["radius"], cap["height"]
    P.add(bm_lathe([(cr, 0), (cr * 1.05, ch * 0.35), (cr * 0.55, ch * 0.85), (0, ch)], cap["segments"], phase=math.pi / cap["segments"]),
          cap["color"], xf((0, 0, z_top - 0.005)))
    a = arm["size"]
    z_arm = z_top - a * 1.6
    P.add(bm_box((arm["length"], a, a)), arm["color"], xf((arm["length"] / 2, 0, z_arm)))
    # hanging lamp under the arm end: roof (4-sided pyramid), corner bars, glass, bottom plate
    x = lp["x"]
    rw, rlip, rh = lp["roof"]
    gw, gh = lp["glass"]
    pw, ph = lp["plate"]
    z_roof = z_arm - rh * 0.55
    P.add(bm_lathe([(rw * 0.72, 0), (rw * 0.72, rlip), (0, rh)], 4, phase=math.pi / 4), lp["frameColor"], xf((x, 0, z_roof)))
    z_glass = z_roof - gh + 0.005
    P.add(bm_box((gw, gw, gh), open_bottom=True, open_top=True), lp["glassColor"], xf((x, 0, z_glass)), ao=0.0)
    bar, o = lp["bar"], gw / 2
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.add(bm_box((bar, bar, gh + 0.01), open_bottom=True, open_top=True), lp["frameColor"], xf((x + sx * o, sy * o, z_glass - 0.005)))
    P.add(bm_box((pw, pw, ph)), lp["frameColor"], xf((x, 0, z_glass - ph + 0.002)))


def build_flower(p, P):
    H, N, M = p["height"], p["petals"], p["petalSamples"]
    ri, ro, cup, th = p["rInner"], p["rOuter"], p["cup"], p["thickness"]
    head = xf((0, 0, H), rot=(p["tilt"], 0, 0))  # tilt towards -Y (front / game camera)
    # rosette: closed disc, top fan + bottom fan over a lobed rim
    bm = bmesh.new()
    rim = []
    for k in range(N * M):
        u = p["petalU"][k % M] if "petalU" in p else (k % M) / M
        ang = 2 * math.pi * k / (N * M) + math.pi / 2
        r = ri + (ro - ri) * math.sin(math.pi * u) ** p.get("petalRound", 0.5)
        rim.append(bm.verts.new((r * math.cos(ang), r * math.sin(ang), cup * (r / ro) ** 2)))
    top = bm.verts.new((0, 0, th * 0.5))
    bot = bm.verts.new((0, 0, -th))
    for k in range(len(rim)):
        j = (k + 1) % len(rim)
        bm.faces.new((top, rim[k], rim[j]))
        bm.faces.new((bot, rim[j], rim[k]))
    P.add(bm, p["petal"], head)
    cr, chh = p["centerRadius"], p["centerHeight"]
    P.add(bm_lathe([(cr, 0), (cr * 0.75, chh * 0.8), (0, chh)], p["centerSegments"], cap_bottom=False), p["center"], head @ xf((0, 0, th * 0.2)))
    sr, ss = p["stemRadius"], p.get("stemSegments", 4)
    P.add(bm_lathe([(sr * 1.2, 0), (sr, H)], ss, cap_bottom=False, cap_top=False, phase=math.pi / ss), p["stem"])
    # leaves: two thin folded diamonds (duplicated surface with a little thickness, winding set by hand)
    ll, lw = p["leafLength"], p["leafWidth"]
    for ang in p["leafAngles"]:
        bm = bmesh.new()
        pts_top = [(0, 0, 0.004), (ll * 0.45, lw / 2, 0.02), (ll, 0, 0.004), (ll * 0.45, -lw / 2, 0.02)]
        pts_bot = [(x, y, z - 0.012) for x, y, z in pts_top]
        t = [bm.verts.new(c) for c in pts_top]
        u = [bm.verts.new(c) for c in pts_bot]
        bm.faces.new((t[0], t[3], t[2])); bm.faces.new((t[0], t[2], t[1]))
        bm.faces.new((u[0], u[2], u[3])); bm.faces.new((u[0], u[1], u[2]))
        P.add(bm, p["leaf"], xf((0, 0, p["leafZ"]), rot=(0, -p["leafLift"], ang)), recalc=False)


def build_mushroom(p, P):
    R, CH, sr, SH, segs = p["capRadius"], p["capHeight"], p["stemRadius"], p["stemHeight"], p["segments"]
    P.add(bm_lathe([(sr * 1.15, 0), (sr * 0.95, SH * 0.6), (sr * 0.85, SH + 0.01)], p["stemSegments"], cap_bottom=False, cap_top=False), p["stem"])
    zr = SH  # rim height
    prof = [(sr * 0.9, zr - 0.005), (R * 0.97, zr - 0.01), (R, zr + CH * 0.18), (R * 0.75, zr + CH * 0.72), (0, zr + CH)]
    P.add(bm_lathe(prof, segs), p["cap"])
    # spots: little domes on the cap surface; (angle deg, t along profile rim->top, radius)
    outer = prof[2:]
    for ang, t, rs in p["spots"]:
        k = min(int(t * (len(outer) - 1)), len(outer) - 2)
        f = t * (len(outer) - 1) - k
        (r0, z0), (r1, z1) = outer[k], outer[k + 1]
        r, z = r0 + (r1 - r0) * f, z0 + (z1 - z0) * f
        a = math.radians(ang)
        radial = Vector((math.cos(a), math.sin(a), 0))
        tangent = radial * (r1 - r0) + Vector((0, 0, z1 - z0))
        nrm = tangent.cross(Vector((-math.sin(a), math.cos(a), 0))).normalized()
        if nrm.z < 0:
            nrm = -nrm
        pos = radial * r + Vector((0, 0, z)) - nrm * 0.004
        P.add(bm_lathe([(rs, 0), (0, p["spotHeight"])], p["spotSegments"], cap_bottom=False),
              p["spot"], Matrix.Translation(pos) @ align_z(nrm))


def build_tuft(p, P):
    rng = random.Random(p["seed"])
    n, w = p["blades"], p["width"]
    for i in range(n):
        a = 2 * math.pi * (i + rng.uniform(-0.25, 0.25)) / n
        rad = p["spread"] * rng.uniform(0.3, 1.0) * (0.3 if i == 0 else 1.0)
        c = Vector((math.cos(a) * rad, math.sin(a) * rad, 0))
        h = rng.uniform(*p["height"])
        lean = Vector((math.cos(a), math.sin(a), 0)) * p["lean"] * (rad / p["spread"] + 0.3)
        bm = bmesh.new()
        rot = rng.uniform(0, 2 * math.pi)
        base = [bm.verts.new(c + Vector((math.cos(rot + k * 2.094) * w / 2, math.sin(rot + k * 2.094) * w * 0.25, 0))) for k in range(3)]
        tip = bm.verts.new(c + lean + Vector((0, 0, h)))
        for k in range(3):
            bm.faces.new((base[k], base[(k + 1) % 3], tip))
        P.add(bm, p["blade"])
    # shared colour range: every blade uses the full gradient over its own height (P.add paints per part)


def build_rock(p, P):
    ox, oy = p.get("offset", [0, 0])
    bm = bm_rock(p["size"], p["segments"], p["rings"], p["noise"], p["noiseScale"], p["seed"], p["flatBottom"], p["squashTop"])
    moss = p.get("moss")
    if moss:
        off = Vector((p["seed"] * 1.7, 4.2, 0.3))
        uz = bm.verts.layers.float["uz"]
        # moss cap: faces whose ring height (undeformed) is above the threshold, wobbled per segment with noise
        sel = {f for f in bm.faces if min(v[uz] for v in f.verts) + moss["noise"] * noise.noise(Vector((*f.calc_center_median().xy, 0)) * 3.0 + off)
               > moss["threshold"]}
        mb = bmesh.new()
        vmap = {}
        for f in sel:
            for v in f.verts:
                if v not in vmap:
                    border = any(g not in sel for g in v.link_faces)
                    vmap[v] = mb.verts.new(v.co + v.normal * (0.002 if border else moss["thickness"]))
            mb.faces.new([vmap[v] for v in f.verts])
        P.add(bm, p["rock"], xf((ox, oy, 0)))
        P.add(mb, moss["color"], xf((ox, oy, 0)), recalc=False)
    else:
        P.add(bm, p["rock"], xf((ox, oy, 0)))
    pb = p.get("pebble")
    if pb:
        P.add(bm_rock(pb["size"], pb["segments"], pb["rings"], pb["noise"], 3.0, pb["seed"]), p["rock"], xf((pb["pos"][0], pb["pos"][1], 0)))


def build_acorn(p, P):
    r, bh, chh, segs = p["radius"], p["bodyHeight"], p["capHeight"], p["segments"]
    body = [(0, 0), (r * 0.3, bh * 0.05), (r * 0.72, bh * 0.2), (r * 0.97, bh * 0.45), (r, bh * 0.7), (r * 0.9, bh * 0.92)]
    P.add(bm_lathe(body, segs), p["body"])
    z0 = bh * 0.8
    fl = p.get("capFlare", 1.1)
    cap = [(r * 1.04, z0), (r * fl, z0 + chh * 0.25), (r * 1.02, z0 + chh * 0.65), (r * 0.62, z0 + chh * 0.95), (0, z0 + chh)]
    P.add(bm_lathe(cap, segs), p["cap"])
    sr, sh = p["stemRadius"], p["stemHeight"]
    P.add(bm_lathe([(sr, 0), (sr * 0.8, sh), (0, sh + 0.008)], 6, cap_bottom=False), p["stem"], xf((0, 0, z0 + chh - 0.01), rot=(0, 18, 0)))


BUILDERS = {"fence_picket": build_fence_picket, "fence_rail": build_fence_rail, "lantern": build_lantern, "flower": build_flower,
            "mushroom": build_mushroom, "tuft": build_tuft, "rock": build_rock, "acorn": build_acorn}


# ---------- AO, mesh, export ----------
def fib_hemisphere(n):
    """Cosine-weighted directions around +Z (deterministic)."""
    dirs = []
    for i in range(n):
        u = (i + 0.5) / n
        r, phi = math.sqrt(u), i * math.pi * (3 - math.sqrt(5))
        dirs.append(Vector((r * math.cos(phi), r * math.sin(phi), math.sqrt(max(0.0, 1 - u)))))
    return dirs


def bake_ao(P, me, aocfg):
    bvh = BVHTree.FromPolygons(P.co, P.faces)
    dirs = fib_hemisphere(aocfg["rays"])
    dist, strength = aocfg["distance"], aocfg["strength"]
    ch, cm = aocfg["contactHeight"], aocfg["contactMul"]
    for i, v in enumerate(me.vertices):
        if P.ao[i] <= 0:
            continue
        n = v.normal.copy()
        rot = Vector((0, 0, 1)).rotation_difference(n)
        o = v.co + n * 0.003
        hit = 0
        for d0 in dirs:
            d = rot @ d0
            if aocfg["groundPlane"] and d.z < -1e-4 and (o.z / -d.z) < dist:
                hit += 1
            elif bvh.ray_cast(o, d, dist)[0] is not None:
                hit += 1
        ao = 1.0 - strength * hit / len(dirs) * P.ao[i]
        contact = cm + (1 - cm) * min(max(v.co.z / ch, 0.0), 1.0)
        c = P.col[i]
        P.col[i] = Vector((c.x * ao * contact, c.y * ao * contact, c.z * ao * contact, c.w))


def vc_material():
    mat = bpy.data.materials.get("EnvVC") or bpy.data.materials.new("EnvVC")
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    if not any(n.type == "VERTEX_COLOR" for n in nt.nodes):
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Col"
        nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    return mat


def build(name, p):
    P = Prop()
    BUILDERS[p["type"]](p, P)
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(c) for c in P.co], [], P.faces)
    me.update()
    bake_ao(P, me, cfg["ao"])
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    flat = []
    for c in P.col:
        flat += to_linear((min(c.x, 1.0), min(c.y, 1.0), min(c.z, 1.0))) + [c.w]
    attr.data.foreach_set("color", flat)
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    # triangulate, smooth + weighted normals
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(me)
    bm.free()
    me.materials.append(vc_material())
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for o in bpy.context.scene.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    wn = obj.modifiers.new("WN", "WEIGHTED_NORMAL")
    wn.weight = cfg.get("weightedNormalWeight", 50)
    wn.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=wn.name)
    return obj


def export(obj, name):
    for o in bpy.context.scene.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    base = os.path.join(out_dir, name)
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", colors_type="SRGB", mesh_smooth_type="FACE", use_mesh_modifiers=True,
                             bake_anim=False, add_leaf_bones=False)
    try:
        bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True, export_vertex_color="ACTIVE")
    except TypeError:
        bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
rows, all_ok = [], True
for name, p in cfg["props"].items():
    if only and name not in only:
        continue
    obj = build(name, p)
    me = obj.data
    tris = len(me.polygons)
    xs, ys, zs = ([v.co[k] for v in me.vertices] for k in range(3))
    fx, fy = p["footprint"]
    ground = [v.co for v in me.vertices if v.co.z < 0.05]
    # base footprint: ground-contact vertices inside the cell(s); single-cell round props also inside radius 0.47
    in_cell = all(abs(c.x) <= fx + 1e-4 and abs(c.y) <= fy + 1e-4 for c in ground) and \
        (fx >= 0.5 or all(c.xy.length <= 0.47 + 1e-4 for c in ground))
    has_col = len(me.color_attributes) == 1 and me.color_attributes[0].data_type == "FLOAT_COLOR"
    ok = tris <= p["budget"] and has_col and in_cell and abs(min(zs)) < 1e-4
    all_ok &= ok
    export(obj, name)
    log(f"{name}: tris {tris}/{p['budget']} bounds x[{min(xs):.3f},{max(xs):.3f}] y[{min(ys):.3f},{max(ys):.3f}] z[{min(zs):.3f},{max(zs):.3f}]"
        f" colours {has_col} footprint {in_cell} {'OK' if ok else 'FAIL'}")
    rows.append((name, tris, ok))
    bpy.data.objects.remove(obj, do_unlink=True)
log("RESULT", "OK" if all_ok else "FAIL", f"{len(rows)} props,", ", ".join(f"{n} {t}" for n, t, _ in rows), "->", out_dir)
