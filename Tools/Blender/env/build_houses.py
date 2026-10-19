# Procedural frame models for the arena border (A.4a, Docs/Briefs/A-EnvTarget.md §3.1): mushroom houses and the
# hollow stump den. Headless:
#   Tools/blender.ps1 -Script Tools/Blender/env/build_houses.py -ScriptArgs @('house_red','stump_hollow')   (no args = all)
# Parameters: Tools/Blender/env/houses.json. Output: Art/Source/3D/env/export/<name>.fbx + .glb.
# Shapes are lathes (surfaces of revolution), sweeps and patches projected onto a host surface (spots, door, window,
# hollow). Every part keeps its own vertices, so colour regions stay sharp; colour is a face-corner FLOAT_COLOR "Col"
# (linear, from sRGB hex) with alpha = gloss, darkened by baked ambient occlusion (BVH rays, ground plane occludes)
# and a darker base. Pivot bottom centre, Z up, 1 unit = 1 cell, front = -Y (Unity +Z after the FBX axis swap).
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
cfg_all = json.load(open(os.path.join(here, "houses.json"), encoding="utf-8"))
names = [a for a in argv if not a.startswith("--")] or list(cfg_all["models"])
out_dir = os.path.join(repo, cfg_all["exportDir"])
os.makedirs(out_dir, exist_ok=True)
log = lambda *a: print("[houses]", *a, flush=True)
TAU = 2 * math.pi


def srgb_to_lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])


def unit(v):
    v = np.asarray(v, dtype=float)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


# ---------------------------------------------------------------- mesh container
class M:
    def __init__(self):
        self.V, self.T, self.C, self.O = [], [], [], []  # O: triangle occludes in the AO bake (big forms only)

    def v(self, p):
        self.V.append([float(x) for x in p])
        return len(self.V) - 1

    def tri(self, a, b, c, k):
        self.T.append((a, b, c))
        self.C.append(k)
        self.O.append(True)

    def add(self, o, occluder=True):
        off = len(self.V)
        self.V += o.V
        self.T += [(a + off, b + off, c + off) for a, b, c in o.T]
        self.C += o.C
        self.O += [x and occluder for x in o.O]

    def flip(self):
        self.T = [(a, c, b) for a, b, c in self.T]

    def face_normals(self):
        V, T = np.array(self.V), np.array(self.T)
        return np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])

    def orient_solid(self):
        """Flip if the (roughly closed) part has negative signed volume around its centroid."""
        V, T = np.array(self.V), np.array(self.T)
        V = V - V.mean(0)
        vol = np.einsum("ij,ij->i", V[T[:, 0]], np.cross(V[T[:, 1]], V[T[:, 2]])).sum()
        if vol < 0:
            self.flip()


def grid(m, rows, closed, colfn):
    """rows: lists of vertex indices; a one-element row is a pole. Winding: rows top->down, columns CCW = outward."""
    for i in range(len(rows) - 1):
        A, B = rows[i], rows[i + 1]
        n = max(len(A), len(B))
        rng = range(n if closed else n - 1)
        for j in rng:
            k = colfn(i, j)
            if len(A) == 1:
                m.tri(A[0], B[j], B[(j + 1) % n], k)
            elif len(B) == 1:
                m.tri(A[j], B[0], A[(j + 1) % n], k)
            else:
                a, b, c, d = A[j], B[j], B[(j + 1) % n], A[(j + 1) % n]
                m.tri(a, b, c, k)
                m.tri(a, c, d, k)


def lathe(profile, segs, colfn, rmod=None, phase=0.0):
    m = M()
    rows = []
    for r, z in profile:
        if r < 1e-6:
            rows.append([m.v((0, 0, z))])
            continue
        row = []
        for j in range(segs):
            th = phase + TAU * j / segs
            rr = rmod(th, z, r) if rmod else r
            row.append(m.v((rr * math.cos(th), rr * math.sin(th), z)))
        rows.append(row)
    grid(m, rows, True, colfn)
    return m


def sphere_rows(m, centre, radius, axis, elevations, segs, squash=1.0):
    axis = unit(axis)
    t = unit(np.cross([0, 0, 1], axis)) if abs(axis[2]) < 0.95 else np.array([1.0, 0, 0])
    b = np.cross(axis, t)
    rows = []
    for e in elevations:
        er = math.radians(e)
        if e >= 89.9:
            rows.append([m.v(centre + axis * radius * squash)])
            continue
        row = []
        for j in range(segs):
            th = TAU * j / segs
            p = centre + radius * math.cos(er) * (t * math.cos(th) + b * math.sin(th)) + axis * radius * math.sin(er) * squash
            row.append(m.v(p))
        rows.append(row)
    return rows


# ---------------------------------------------------------------- host surfaces and projection
class Surface:
    def __init__(self, m):
        self.V, self.T = np.array(m.V), np.array(m.T)
        fn = m.face_normals()
        vn = np.zeros_like(self.V)
        for k in range(3):
            np.add.at(vn, self.T[:, k], fn)
        self.vn = vn / np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)
        self.bvh = BVHTree.FromPolygons([tuple(p) for p in self.V], [tuple(int(i) for i in t) for t in self.T])

    def hit(self, origin, direction):
        loc, nrm, idx, _ = self.bvh.ray_cast(Vector(tuple(origin)), Vector(tuple(direction)))
        if loc is None:
            loc, nrm, idx, _ = self.bvh.find_nearest(Vector(tuple(origin)))
        q = np.array(loc)
        a, b, c = self.V[self.T[idx]]
        v0, v1, v2 = b - a, c - a, q - a
        d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        w1 = (d11 * d20 - d01 * d21) / den if abs(den) > 1e-18 else 0
        w2 = (d00 * d21 - d01 * d20) / den if abs(den) > 1e-18 else 0
        w = np.clip([1 - w1 - w2, w1, w2], 0, 1)
        n = unit((self.vn[self.T[idx]] * w[:, None]).sum(0))
        return q, n


def body_mapper(surf, theta_deg, zc, rref):
    """(u, v) = arc length / height on a lathe-like body around the Z axis, centred at angle theta, height zc."""
    th0 = math.radians(theta_deg)

    def f(u, v):
        th = th0 + u / rref
        d = np.array([math.cos(th), math.sin(th), 0.0])
        return surf.hit(d * 3 + np.array([0, 0, zc + v]), -d)
    return f


def tangent_mapper(surf, c, n):
    t = unit(np.cross([0, 0, 1], n)) if abs(n[2]) < 0.95 else np.array([1.0, 0, 0])
    b = np.cross(n, t)

    def f(u, v):
        return surf.hit(c + t * u + b * v + n * 0.4, -n)
    return f


def patch(mapper, rows_uvh, closed, colfn):
    """rows of (u, v, h) local points -> projected onto the host and lifted by h along its normal."""
    m = M()
    rows, nrm = [], []
    for row in rows_uvh:
        ids = []
        for u, v, h in row:
            p, n = mapper(u, v)
            ids.append(m.v(p + n * h))
            nrm.append(n)
        rows.append(ids)
    grid(m, rows, closed, colfn)
    fn = m.face_normals()
    N = np.array(nrm)
    T = np.array(m.T)
    ref = N[T[:, 0]] + N[T[:, 1]] + N[T[:, 2]]
    if np.einsum("ij,ij->i", fn, ref).sum() < 0:
        m.flip()
    return m


def ellipse_ring(a, b, offs, segs, phase=0.0):
    """Cross-section rows around an ellipse: offs = [(outward offset, h)] -> rows of (u, v, h)."""
    rows = []
    for o, h in offs:
        row = []
        for j in range(segs):
            t = phase + TAU * j / segs
            row.append(((a + o) * math.cos(t), (b + o) * math.sin(t), h))
        rows.append(row)
    return rows


def ring_frame(mapper, a, b, width, height, segs, key, phase=0.0):
    # inner lip -> rounded top -> outer edge sunk into the host: a soft vinyl rim
    offs = [(0.0, 0.004), (width * 0.18, height * 0.92), (width * 0.62, height), (width * 1.0, -0.006)]
    rows = ellipse_ring(a, b, offs, segs, phase)
    return patch(mapper, rows, True, lambda i, j: key)


def disc(mapper, a, b, h, segs, key):
    rows = [[(0.0, 0.0, h)]] + ellipse_ring(a, b, [(0.0, h)], segs)
    return patch(mapper, rows, True, lambda i, j: key)


def ridge(mapper, p0, p1, width, height, samples, key):
    """Straight soft bar from p0 to p1 in (u, v): 4-point cross-section."""
    p0, p1 = np.array(p0, float), np.array(p1, float)
    d = unit(p1 - p0)
    side = np.array([-d[1], d[0]])
    cs = [(-0.5, -0.004), (-0.28, height), (0.28, height), (0.5, -0.004)]
    rows = []
    for s, h in cs:
        row = []
        for k in range(samples):
            p = p0 + (p1 - p0) * k / (samples - 1) + side * s * width
            row.append((p[0], p[1], h))
        rows.append(row)
    return patch(mapper, rows, False, lambda i, j: key)


# ---------------------------------------------------------------- models
def build_house(cfg):
    out = M()
    bc, cc = cfg["body"], cfg["cap"]
    body = lathe(bc["profile"], bc["segments"], lambda i, j: "body", phase=math.pi / bc["segments"])
    out.add(body)
    body_s = Surface(body)

    wave_n, wave_a = cc["waveCount"], cc["waveAmp"]
    rmax = max(r for r, z in cc["profile"])
    rmod = (lambda th, z, r: r * (1 + wave_a * (r / rmax) ** 4 * math.cos(wave_n * th))) if wave_n else None
    cap = lathe(cc["profile"], cc["segments"], lambda i, j: "cap", rmod=rmod)
    out.add(cap)
    cap_s = Surface(cap)

    sc = cfg["spots"]
    cz = np.array([0, 0, cc["centerZ"]])
    for th, el, rad in sc["list"]:
        d = np.array([math.cos(math.radians(el)) * math.cos(math.radians(th)),
                      math.cos(math.radians(el)) * math.sin(math.radians(th)), math.sin(math.radians(el))])
        c, n = cap_s.hit(cz + d * 4, -d)
        mp = tangent_mapper(cap_s, c, n)
        H = sc["height"]
        s = sc["segments"]
        rows = [[(0.0, 0.0, H)]] + ellipse_ring(rad, rad, [(-rad * 0.3, H * 0.82), (0.0, -0.012)], s)
        out.add(patch(mp, rows, True, lambda i, j: "spot"), occluder=False)

    dc = cfg["door"]
    rref = np.interp(dc["z"], [z for r, z in reversed(bc["profile"])], [r for r, z in reversed(bc["profile"])])
    mp = body_mapper(body_s, dc["thetaDeg"], dc["z"], rref)
    a, b = dc["halfWidth"], dc["halfHeight"]
    out.add(ring_frame(mp, a, b, dc["frameWidth"], dc["frameHeight"], dc["segments"], "frame"), occluder=False)
    # plank panel: softly domed disc + dark groove strips between the planks
    ae, be = a + 0.014, b + 0.014
    h0, he = dc["plankHeight"], dc["plankHeight"] * 0.45
    ph = lambda u, v: he + (h0 - he) * max(0.0, 1 - (u / ae) ** 2 - (v / be) ** 2)
    ds = dc["segments"]
    rows = [[(0.0, 0.0, h0)]]
    for f in (0.55, 1.0):
        ring = []
        for j in range(ds):
            u, v = ae * f * math.cos(TAU * j / ds), be * f * math.sin(TAU * j / ds)
            ring.append((u, v, ph(u, v)))
        rows.append(ring)
    out.add(patch(mp, rows, True, lambda i, j: "plank"), occluder=False)
    g = dc["grooveWidth"]
    for k in range(1, dc["planks"]):
        x = -ae + 2 * ae * k / dc["planks"]
        vm = be * math.sqrt(max(0.0, 1 - (x / ae) ** 2)) * 0.97
        srows = [[(x + sx, vm * (1 - t), ph(x + sx, vm * (1 - t)) + 0.004) for t in (0.0, 1.0, 2.0)] for sx in (-g / 2, g / 2)]
        out.add(patch(mp, srows, False, lambda i, j: "groove"), occluder=False)
    # knob
    p, n = mp(a * 0.62, -0.01)
    km = M()
    kr = dc["knobRadius"]
    grid(km, sphere_rows(km, p + n * (ph(a * 0.62, 0) + kr * 0.3), kr, n, [90, 30, -40], 6), True, lambda i, j: "knob")
    km.orient_solid()
    out.add(km, occluder=False)

    for wc in cfg["windows"]:
        rref = np.interp(wc["z"], [z for r, z in reversed(bc["profile"])], [r for r, z in reversed(bc["profile"])])
        mp = body_mapper(body_s, wc["thetaDeg"], wc["z"], rref)
        R = wc["radius"]
        out.add(disc(mp, R + 0.01, R + 0.01, 0.003, wc["segments"], "glass"), occluder=False)
        out.add(ring_frame(mp, R, R, wc["frameWidth"], wc["frameHeight"], wc["segments"], "frame", phase=math.pi / wc["segments"]), occluder=False)
        bw = wc["barWidth"]
        out.add(ridge(mp, (-R - 0.01, 0), (R + 0.01, 0), bw, wc["frameHeight"] * 0.75, 2, "frame"), occluder=False)
        out.add(ridge(mp, (0, -R - 0.01), (0, R + 0.01), bw, wc["frameHeight"] * 0.75, 2, "frame"), occluder=False)
    return out


def build_stump(cfg):
    out = M()
    bc = cfg["body"]
    step = TAU / bc["segments"]
    # grooves snap to vertex columns: one pushed-in column makes a soft V crease under smooth shading
    grooves = [(round(math.radians(t) / step) * step, d, math.radians(w)) for t, d, w in bc["grooves"]]
    wood_r = bc["woodRadius"]
    hc = cfg["hollow"]
    h_th, h_z = math.radians(hc["thetaDeg"]), hc["z"]
    h_a, h_b = hc["halfWidth"] + hc["lipWidth"], hc["halfHeight"] + hc["lipWidth"]

    def rmod(th, z, r):
        if r <= wood_r + 1e-6:
            return r
        k = 0.0
        for t0, d, w in grooves:
            da = (th - t0 + math.pi) % TAU - math.pi
            k += d * math.exp(-0.5 * (da / w) ** 2)
        dh = (th - h_th + math.pi) % TAU - math.pi
        q = (dh * r / h_a) ** 2 + ((z - h_z) / h_b) ** 2  # keep the bark smooth under the hollow
        k *= min(1.0, max(0.0, q - 0.7) / 0.6)
        return r * (1 - k)

    body = lathe(bc["profile"], bc["segments"], lambda i, j: "wood" if i == 0 else "bark", rmod=rmod)
    top_z = bc["profile"][0][1]

    rc = cfg["roots"]
    roots = M()
    for ang in rc["angles"]:
        a = math.radians(ang)
        d = np.array([math.cos(a), math.sin(a), 0.0])
        side = np.array([-d[1], d[0], 0.0])
        m = M()
        rows = []
        n_r = len(rc["radii"])
        for k, rad in enumerate(rc["radii"]):
            t = k / n_r
            dist = rc["startR"] + (rc["tipR"] - rc["startR"]) * t ** rc["spread"]
            zc = rc["tipZ"] + (rc["startZ"] - rc["tipZ"]) * (1 - t) ** 1.7
            c = d * dist + np.array([0, 0, zc])
            row = []
            for j in range(rc["segments"]):
                th = TAU * j / rc["segments"]
                p = c + side * math.cos(th) * rad * 1.15 + np.array([0, 0, 1.0]) * math.sin(th) * rad * 0.8
                p[2] = max(p[2], 0.0)
                row.append(m.v(p))
            rows.append(row)
        # rounded tip: one smaller ring just before the end, then the pole on the ground line
        last = rc["radii"][-1]
        c = d * (rc["tipR"] - last * 0.45) + np.array([0, 0, rc["tipZ"] + last * 0.25])
        row = []
        for j in range(rc["segments"]):
            th = TAU * j / rc["segments"]
            p = c + side * math.cos(th) * last * 0.8 + np.array([0, 0, 1.0]) * math.sin(th) * last * 0.55
            p[2] = max(p[2], 0.0)
            row.append(m.v(p))
        rows.append(row)
        rows.append([m.v(d * rc["tipR"] + np.array([0, 0, rc["tipZ"] + last * 0.2]))])
        grid(m, rows, True, lambda i, j: "bark")
        m.orient_solid()
        roots.add(m)

    out.add(body)
    out.add(roots)
    body_s = Surface(body)
    union = M()
    union.add(body)
    union.add(roots)
    union_s = Surface(union)

    rref = float(np.interp(hc["z"], [z for r, z in reversed(bc["profile"])], [r for r, z in reversed(bc["profile"])]))
    mp = body_mapper(body_s, hc["thetaDeg"], hc["z"], rref)
    a, b = hc["halfWidth"], hc["halfHeight"]
    hh = 0.018
    rows = [[(0.0, 0.0, hh)]] + ellipse_ring(a * 0.35, b * 0.35, [(0.0, hh)], hc["segments"]) \
        + ellipse_ring(a * 0.7, b * 0.7, [(0.0, hh)], hc["segments"]) \
        + ellipse_ring(a + 0.014, b + 0.014, [(0.0, hh)], hc["segments"])
    out.add(patch(mp, rows, True, lambda i, j: "hollow"), occluder=False)
    out.add(ring_frame(mp, a, b, hc["lipWidth"], hc["lipHeight"], hc["segments"], "bark"))

    # spiral growth ring on the flat top (raised soft ridge)
    sp = cfg["spiral"]
    top = lambda u, v: (np.array([u, v, top_z]), np.array([0, 0, 1.0]))
    pts = []
    for k in range(sp["samples"]):
        t = k / (sp["samples"] - 1)
        r = sp["r0"] + (sp["r1"] - sp["r0"]) * t
        ph = TAU * sp["turns"] * t
        pts.append(np.array([r * math.cos(ph), r * math.sin(ph)]))
    cs = [(-0.5, 0.0005), (0.0, sp["height"]), (0.5, 0.0005)]
    srows = []
    for s, h in cs:
        row = []
        for k, p in enumerate(pts):
            tg = unit(pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)])
            nn = np.array([-tg[1], tg[0]])
            q = p + nn * s * sp["width"]
            row.append((q[0], q[1], h))
        srows.append(row)
    out.add(patch(top, srows, False, lambda i, j: "ring"), occluder=False)

    mc = cfg["moss"]
    for th, z, rad, cnt in mc["list"]:
        thr = math.radians(th)
        d = np.array([math.cos(thr), math.sin(thr), 0.0])
        if z >= top_z - 0.08:  # on the top rim: drop from above near the edge
            c, n = union_s.hit(d * 0.43 + np.array([0, 0, 2.0]), np.array([0, 0, -1.0]))
        else:
            c, n = union_s.hit(d * 3 + np.array([0, 0, z]), -d)
        t = unit(np.cross([0, 0, 1], n)) if abs(n[2]) < 0.95 else np.array([1.0, 0, 0])
        bb = np.cross(n, t)
        offs = [(0, 0, 1.0), (1.25, 0.35, 0.78), (-1.1, 0.5, 0.7), (0.2, -1.2, 0.72)][:cnt]
        for ou, ov, sc in offs:
            p0 = c + (t * ou + bb * ov) * rad
            p, nn = union_s.hit(p0 + n * 0.3, -n)
            r = rad * sc
            m = M()
            grid(m, sphere_rows(m, p - nn * r * 0.3, r, nn, [90, 40, -20], mc["segments"]), True, lambda i, j: "moss")
            m.orient_solid()
            out.add(m, occluder=False)
    return out


# ---------------------------------------------------------------- colour, AO, export
def finish(name, cfg, part):
    d = dict(cfg_all["defaults"])
    V = np.array(part.V)
    T = np.array(part.T)
    keys = list(part.C)
    occ = np.array(part.O)
    fn = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
    area = np.linalg.norm(fn, axis=1) * 0.5
    keep = area > 1e-8
    T, fn = T[keep], fn[keep]
    keys = [k for k, kp in zip(keys, keep) if kp]
    occ = occ[keep]
    used = np.unique(T)
    remap = -np.ones(len(V), dtype=np.int64)
    remap[used] = np.arange(len(used))
    V, T = V[used], remap[T]

    vn = np.zeros_like(V)
    for k in range(3):
        np.add.at(vn, T[:, k], fn)
    vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)

    bvh = BVHTree.FromPolygons([tuple(p) for p in V], [tuple(int(i) for i in t) for t in T[occ]])
    rng = np.random.default_rng(7)
    dirs = rng.normal(size=(d["aoRays"], 3))
    dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
    dist = d["aoDistance"]
    occl = np.zeros(len(V))
    for i in range(len(V)):
        n = vn[i]
        p0 = V[i] + n * 1e-3
        hit = tot = 0.0
        for dd in dirs:
            c = dd @ n
            if c <= 0:
                dd, c = -dd, -c
            tot += c
            if p0[2] + dd[2] * dist < 0 or bvh.ray_cast(Vector(tuple(p0)), Vector(tuple(dd)), dist)[0] is not None:
                hit += c
        occl[i] = hit / max(tot, 1e-6)
    e = np.vstack([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]])
    acc, cnt = occl.copy(), np.ones(len(V))
    for x, y in ((0, 1), (1, 0)):
        np.add.at(acc, e[:, x], occl[e[:, y]])
        np.add.at(cnt, e[:, x], 1)
    occl = 0.5 * occl + 0.5 * acc / cnt  # one soft blur pass hides per-vertex ray noise
    ao = 1.0 - d["aoStrength"] * occl
    ground = d["groundDark"] + (1 - d["groundDark"]) * np.clip(V[:, 2] / d["groundDarkHeight"], 0, 1)
    shade = ao * ground

    pal = {k: (hex_rgb(v[0]), float(v[1])) for k, v in cfg["colours"].items()}
    corner = np.zeros((len(T) * 3, 4), dtype=np.float32)
    for f, (tri, k) in enumerate(zip(T, keys)):
        base, gloss = pal[k]
        for c in range(3):
            corner[f * 3 + c, :3] = srgb_to_lin(base * shade[tri[c]])
            corner[f * 3 + c, 3] = gloss

    me = bpy.data.meshes.new(name)
    me.from_pydata(V.tolist(), [], T.tolist())
    me.update()
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", corner.ravel())
    me.color_attributes.active_color = attr
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    mat = bpy.data.materials.new("EnvVC")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    vc = mat.node_tree.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    mat.node_tree.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    me.materials.append(mat)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)

    lo, hi = V.min(0), V.max(0)
    foot = V[V[:, 2] < 0.05]
    foot_r = float(np.sqrt((foot[:, 0] ** 2 + foot[:, 1] ** 2)).max()) if len(foot) else 0.0
    max_r = float(np.sqrt((V[:, 0] ** 2 + V[:, 1] ** 2)).max())
    tris = len(T)
    ok = tris <= cfg["tris"]
    log(name, "tris", tris, "/", cfg["tris"], "verts", len(V), "ao mean", round(float(ao.mean()), 3), "min", round(float(ao.min()), 3))
    log(name, "bounds", np.round(lo, 3).tolist(), np.round(hi, 3).tolist(), "footprint r", round(foot_r, 3), "max r", round(max_r, 3))
    log(name, "colours", ", ".join(f"{k} {pal[k][0] if False else cfg['colours'][k][0]} x{keys.count(k)}" for k in pal if k in keys))

    base = os.path.join(out_dir, name)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", colors_type="SRGB", mesh_smooth_type="FACE", bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
    bpy.data.objects.remove(obj, do_unlink=True)
    log(f"RESULT {name} tris {tris}/{cfg['tris']} {'OK' if ok else 'OVER'} height {hi[2]:.2f} width {hi[0]-lo[0]:.2f}x{hi[1]-lo[1]:.2f} -> {base}.fbx")
    return ok


bpy.ops.wm.read_factory_settings(use_empty=True)
results = []
for nm in names:
    c = cfg_all["models"][nm]
    part = build_house(c) if c["kind"] == "house" else build_stump(c)
    results.append(finish(nm, c, part))
log("RESULT all", "OK" if all(results) else "OVER", len(results), "models")
