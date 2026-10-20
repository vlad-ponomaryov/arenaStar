# Mossy (id mossy, Docs/Roster-v4.md 5): a big, slow, kind forest spirit - a walking cone-shaped haystack of moss and
# willow fronds down to the ground (no legs visible), two firefly lights under the fringe, a twig nest with a sleeping
# robin on top. Base model for Docs/Briefs/A-Roster3D.md (wave 2), compact Theo v3 pipeline (as build_wren.py).
#   blender -b --factory-startup --python Tools/Blender/fighters/build_mossy.py -- [out_dir]
# Output (default Art/Source/3D/mossy/): mossy_game.fbx/.glb (<= 4000 tris, mesh "Mossy", material "FighterVC" with the
# texture, armature "Rig": 12 FighterBone bones + Extra = nest and bird), mossy_tex.png (1024 RGBA: RGB albedo + AO,
# A = gloss), mossy_weapon.fbx/.glb (dandelion fluff puff on a stem, origin = right hand, forward -Y, empty "Muzzle"
# at the puff centre). Format as Theo: Z up, faces -Y, base z = 0, height HEIGHT; FBX axis_forward=-Z, axis_up=Y.
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
POS = [a for a in ARGV if not a.startswith("--")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "mossy")
os.makedirs(OUT_DIR, exist_ok=True)
T0 = time.time()
F32 = np.float32
BIG = F32(10.0)
HEIGHT = 1.18          # Theo = 1.0; Mossy is a big tank-sized spirit (HP 6000)
TEX = 1024
BUDGET_BODY, BUDGET_WEAPON = 3960, 1150
NAME = "mossy"

def log(*a):
    print(f"[{NAME} {time.time() - T0:6.1f}s]", *a, flush=True)


def nrm(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def rot(axis, deg):
    a = math.radians(deg)
    x, y, z = nrm(axis)
    c, s, C = math.cos(a), math.sin(a), 1 - math.cos(a)
    return np.array([[c + x * x * C, x * y * C - z * s, x * z * C + y * s],
                     [y * x * C + z * s, c + y * y * C, y * z * C - x * s],
                     [z * x * C - y * s, z * y * C + x * s, c + z * z * C]])


def smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.maximum(k - np.abs(a - b), 0) / k
    return np.minimum(a, b) - h * h * k * 0.25


def smax(a, b, k):
    if k <= 0:
        return np.maximum(a, b)
    h = np.maximum(k - np.abs(a - b), 0) / k
    return np.maximum(a, b) + h * h * k * 0.25


# ---- SDF primitives: f(P) -> distances (float32, negative inside) ----
def ell(c, r, R=None):
    c, r = np.asarray(c, F32), np.asarray(r, F32)
    Rm = None if R is None else np.asarray(R, F32)

    def f(P):
        Q = P - c
        if Rm is not None:
            Q = Q @ Rm
        k0 = np.sqrt(((Q / r) ** 2).sum(1))
        k1 = np.sqrt(((Q / (r * r)) ** 2).sum(1))
        return (k0 * (k0 - 1) / np.maximum(k1, 1e-9)).astype(F32)
    return f


def cone(a, b, ra, rb):
    a, b = np.asarray(a, F32), np.asarray(b, F32)
    ba = b - a
    L2 = float((ba * ba).sum())

    def f(P):
        t = np.clip(((P - a) @ ba) / L2, 0, 1)
        q = P - a - t[:, None] * ba
        return (np.sqrt((q * q).sum(1)) - (ra + (rb - ra) * t)).astype(F32)
    return f


def box(c, half, rad, R=None):
    c, half = np.asarray(c, F32), np.asarray(half, F32)
    Rm = None if R is None else np.asarray(R, F32)

    def f(P):
        Q = P - c
        if Rm is not None:
            Q = Q @ Rm
        q = np.abs(Q) - (half - rad)
        return (np.sqrt((np.maximum(q, 0) ** 2).sum(1)) + np.minimum(q.max(1), 0) - rad).astype(F32)
    return f


def cyl(c, axis, r, h, rad=0.002):
    """Rounded cylinder centred at c, axis unit vector, radius r, half-length h."""
    c, ax = np.asarray(c, F32), np.asarray(nrm(axis), F32)

    def f(P):
        Q = P - c
        y = Q @ ax
        x = np.sqrt(np.maximum((Q * Q).sum(1) - y * y, 0))
        dx, dy = x - (r - rad), np.abs(y) - (h - rad)
        return (np.minimum(np.maximum(dx, dy), 0) + np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dy, 0) ** 2) - rad).astype(F32)
    return f


def grad(fn, P, e=3e-4):
    P = np.asarray(P, F32)
    g = np.empty_like(P)
    for i in range(3):
        dv = np.zeros(3, F32)
        dv[i] = e
        g[:, i] = (fn(P + dv) - fn(P - dv)) / (2 * e)
    return g


def project(fn, P, it=6, clip=0.01):
    P = np.asarray(P, F32).reshape(-1, 3).copy()
    for _ in range(it):
        d = fn(P)
        g = grad(fn, P)
        gn = np.sqrt((g * g).sum(1))
        stp = np.clip(d / np.maximum(gn, 0.5), -clip, clip)
        P -= (g / np.maximum(gn, 1e-6)[:, None] * stp[:, None]).astype(F32)
    return P


def hexc(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])



def torus(c, R, r, sq=1.0):
    """Horizontal torus centred at c, ring radius R, tube radius r; z squashed by sq."""
    c = np.asarray(c, F32)

    def f(P):
        Q = P - c
        q = np.sqrt(Q[:, 0] ** 2 + Q[:, 1] ** 2) - R
        return (np.sqrt(q * q + (Q[:, 2] / sq) ** 2) - r).astype(F32)
    return f


# =====================================================================================================================
# palette (sRGB) and gloss. Roster-v4 5: moss #6FA35A, honey amber #E0A84A (fur tips), bark #6B4A32 (nest),
# firefly #F4FF9A (energy only). Own: dark hollow under the fringe, robin colours, dandelion white.
# =====================================================================================================================
PAL = {
    "fur": ("#6FA35A", 0.30), "fringe": ("#6FA35A", 0.30), "arm": ("#6FA35A", 0.30), "hollow": ("#22301E", 0.10),
    "nest": ("#6B4A32", 0.20), "twig": ("#7E5A3C", 0.20), "bird": ("#8A6A4E", 0.45), "breast": ("#E8743B", 0.45),
    "beak": ("#E0A84A", 0.6),
}
PALW = {"fluff": ("#FFFDF2", 0.15), "core": ("#CDBB86", 0.25), "stem": ("#7FAF5A", 0.35)}

# =====================================================================================================================
# MODEL (model units; normalised to HEIGHT at export). Character's right = -X, front = -Y.
# =====================================================================================================================
SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}
SH = {s: np.array([0.150 * s, -0.010, 0.470]) for s in SIDES}
EL = {-1: np.array([-0.220, -0.090, 0.380]), 1: np.array([0.225, -0.040, 0.370])}
WR = {-1: np.array([-0.190, -0.170, 0.320]), 1: np.array([0.220, -0.085, 0.285])}
HAND = {-1: np.array([-0.175, -0.195, 0.310]), 1: np.array([0.212, -0.100, 0.265])}
HIP = {s: np.array([0.100 * s, 0.000, 0.200]) for s in SIDES}
AN = {s: np.array([0.130 * s, -0.010, 0.020]) for s in SIDES}
EYE = {s: np.array([0.046 * s, -0.190, 0.478]) for s in SIDES}   # firefly lights in the hollow under the fringe
NEST_C = np.array([0.0, 0.015, 0.795])


class Part:
    def __init__(self, name, f, key, bone, k=0.0, shell="body"):
        self.name, self.f, self.key, self.bone, self.k, self.shell = name, f, key, bone, k, shell


PARTS = []


def part(name, f, key, bone, k=0.0, shell="body"):
    PARTS.append(Part(name, f, key, bone, k, shell))


_cone = cone((0, 0.0, 0.07), (0, 0.0, 0.66), 0.315, 0.115)
_dome = ell((0, 0.0, 0.655), (0.165, 0.160, 0.140))
_hollow = ell((0, -0.205, 0.478), (0.112, 0.070, 0.058))


def _fronds(P, amp, n=22, base_z=0.85):
    ang = np.arctan2(P[:, 1], P[:, 0])
    z = P[:, 2]
    wav = (np.sin(n * ang + 2.2 * np.sin(z * 9.0) + 1.7 * np.sin(5 * ang)) * (0.65 + 0.35 * np.sin(7 * ang + 2.0))
           + 0.4 * np.sin(1.6 * n * ang - 3.1 * z + 1.3) + 0.25 * np.sin(3 * ang + 6 * z))   # irregular, not cactus ribs
    return (amp * wav * np.clip((base_z - z) / base_z, 0.15, 1.0)).astype(F32)


def _haystack(P):
    d = smin(_cone(P), _dome(P), 0.10) + _fronds(P, 0.011)
    ang = np.arctan2(P[:, 1], P[:, 0])
    hem = (0.018 + 0.026 * (0.5 + 0.5 * np.sin(22 * ang + 0.7 + 1.7 * np.sin(5 * ang)))).astype(F32)   # scalloped frond tips, base floats
    d = smax(d, (hem - P[:, 2]) * F32(0.7), 0.012)
    return smax(d, -_hollow(P), 0.020)


part("haystack", _haystack, "fur", "Body")
_fr = ell((0, -0.150, 0.552), (0.150, 0.080, 0.052))


def _fringe(P):
    d = _fr(P) + _fronds(P, 0.008, n=30, base_z=0.9)
    lip = (0.512 + 0.012 * np.sin(np.arctan2(P[:, 1] + 0.05, P[:, 0]) * 26) - P[:, 2]).astype(F32)
    return smax(d, lip, 0.008)


part("fringe", _fringe, "fringe", "Head", k=0.02)
part("hollow", lambda P: smax(_hollow(P) + F32(0.012), -_haystack(P) - F32(0.03), 0.01), "hollow", "Head")
for s in SIDES:
    part(f"arm_{SN[s]}", lambda P, s=s: cone(SH[s], EL[s], 0.068, 0.058)(P) + _fronds(P, 0.006, n=26), "arm", ("Arm", s),
         k=0.05)
    part(f"fore_{SN[s]}", lambda P, s=s: cone(EL[s], WR[s], 0.058, 0.048)(P) + _fronds(P, 0.005, n=26), "arm", ("Arm", s),
         k=0.03)
    part(f"hand_{SN[s]}", ell(HAND[s], (0.050, 0.048, 0.044)), "arm", ("Arm", s), k=0.025)
# nest + sleeping robin on the top (bone Extra)
part("nest", lambda P: smax(torus(NEST_C, 0.078, 0.034, 0.75)(P), (NEST_C[2] - 0.035 - P[:, 2]).astype(F32), 0.01),
     "nest", "Extra", k=0.03)
part("nest_bottom", ell(NEST_C + (0, 0, -0.012), (0.085, 0.085, 0.030)), "nest", "Extra", k=0.02)
for i, a in enumerate((20, 85, 160, 215, 290, 340)):
    r = math.radians(a)
    d0 = np.array([math.cos(r), math.sin(r), 0.0])
    p0 = NEST_C + d0 * 0.085 + (0, 0, 0.005 + 0.008 * (i % 2))
    part(f"twig_{i}", cone(p0, p0 + d0 * 0.045 + (0, 0, 0.022 - 0.012 * (i % 3)), 0.0075, 0.0045), "twig", "Extra")
BIRD_C = NEST_C + (0, 0.010, 0.040)
part("bird_body", ell(BIRD_C, (0.044, 0.056, 0.038)), "bird", "Extra", k=0.0)
part("bird_breast", ell(BIRD_C + (0, -0.030, -0.002), (0.034, 0.026, 0.030)), "breast", "Extra", k=0.012)
part("bird_head", ell(BIRD_C + (0, -0.044, 0.030), (0.029, 0.028, 0.027)), "bird", "Extra", k=0.012)
part("bird_beak", cone(BIRD_C + (0, -0.068, 0.028), BIRD_C + (0, -0.086, 0.024), 0.008, 0.002), "beak", "Extra")
part("bird_tail", box(BIRD_C + (0, 0.062, 0.018), (0.016, 0.026, 0.006), 0.005, rot((1, 0, 0), -25)), "bird", "Extra",
     k=0.008)


def eval_parts(P, parts):
    P = np.asarray(P, F32)
    out = np.full(len(P), BIG, F32)
    for p in parts:
        out = smin(out, p.f(P), p.k) if p.k > 0 else np.minimum(out, p.f(P))
    return np.maximum(out, (-0.0 - P[:, 2]).astype(F32))


def fig_d(P):
    return eval_parts(P, PARTS)


def owner(P, parts=None):
    parts = parts or PARTS
    D = np.stack([p.f(np.asarray(P, F32)) for p in parts], 0)
    return np.argmin(D, 0)


def uv_boost(cen):
    ff = np.ones(len(cen))
    face = (cen[:, 2] > 0.40) & (cen[:, 2] < 0.58) & (cen[:, 1] < -0.10) & (np.abs(cen[:, 0]) < 0.14)
    ff[face] = 2.4
    ff[cen[:, 2] > 0.74] = 1.6
    return ff

# =====================================================================================================================
# meshing (as build_theo_v3.py)
# =====================================================================================================================
CORN = np.array([(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 1)])
EDG = [(a, b) for a in range(8) for b in range(a + 1, 8) if np.abs(CORN[a] - CORN[b]).sum() == 1]


def sample_grid(fn, lo, hi, h, B=8):
    n = (np.ceil((hi - lo) / h).astype(int) + 1)
    nb = np.ceil(n / B).astype(int)
    bi = np.stack(np.meshgrid(*[np.arange(k) for k in nb], indexing="ij"), -1).reshape(-1, 3)
    cen = lo + (bi * B + (B - 1) / 2.0) * h
    dc = fn(cen.astype(F32))
    rad = B * h * math.sqrt(3) / 2 * 1.8 + 2 * h
    act = np.abs(dc) < rad
    Fb = np.where(dc < 0, -BIG, BIG).astype(F32).reshape(nb)
    Fv = np.repeat(np.repeat(np.repeat(Fb, B, 0), B, 1), B, 2)[:n[0], :n[1], :n[2]].copy()
    ab = bi[act]
    off = np.stack(np.meshgrid(*[np.arange(B)] * 3, indexing="ij"), -1).reshape(-1, 3)
    for c0 in range(0, len(ab), 2000):
        I_ = (ab[c0:c0 + 2000, None, :] * B + off[None]).reshape(-1, 3)
        I_ = I_[(I_ < n).all(1)]
        P = (lo + I_ * h).astype(F32)
        Fv[I_[:, 0], I_[:, 1], I_[:, 2]] = fn(P)
    return Fv


def surface_nets(Fv, lo, h):
    n = np.array(Fv.shape)
    Sg = Fv < 0
    nx, ny, nz = n
    cnt = np.zeros((nx - 1, ny - 1, nz - 1), np.uint8)
    for c in CORN:
        cnt += Sg[c[0]:nx - 1 + c[0], c[1]:ny - 1 + c[1], c[2]:nz - 1 + c[2]]
    act = (cnt > 0) & (cnt < 8)
    ijk = np.argwhere(act)
    vid = np.full(act.shape, -1, np.int32)
    vid[act] = np.arange(len(ijk), dtype=np.int32)
    acc = np.zeros((len(ijk), 3), np.float32)
    num = np.zeros(len(ijk), np.float32)
    for a, b in EDG:
        ia, ib = ijk + CORN[a], ijk + CORN[b]
        fa, fb = Fv[ia[:, 0], ia[:, 1], ia[:, 2]], Fv[ib[:, 0], ib[:, 1], ib[:, 2]]
        m = (fa < 0) != (fb < 0)
        t = fa[m] / (fa[m] - fb[m])
        acc[m] += CORN[a] + t[:, None] * (CORN[b] - CORN[a])
        num[m] += 1
    V = lo + (ijk + acc / np.maximum(num, 1)[:, None]) * h
    quads = []
    for ax in range(3):
        u, v = (ax + 1) % 3, (ax + 2) % 3
        sl0, sl1 = [slice(None)] * 3, [slice(None)] * 3
        sl0[ax], sl1[ax] = slice(0, n[ax] - 1), slice(1, n[ax])
        e = Sg[tuple(sl0)] != Sg[tuple(sl1)]
        p = np.argwhere(e)
        p = p[(p[:, u] >= 1) & (p[:, u] <= n[u] - 2) & (p[:, v] >= 1) & (p[:, v] <= n[v] - 2)]
        if len(p) == 0:
            continue
        inside = Sg[p[:, 0], p[:, 1], p[:, 2]]
        cells = []
        for du, dv in ((-1, -1), (0, -1), (0, 0), (-1, 0)):
            c = p.copy()
            c[:, u] += du
            c[:, v] += dv
            cells.append(vid[c[:, 0], c[:, 1], c[:, 2]])
        q = np.stack(cells, 1)
        q[~inside] = q[~inside][:, ::-1]
        quads.append(q)
    Q = np.concatenate(quads)
    Q = Q[(Q >= 0).all(1)]
    return V.astype(np.float64), Q


def mesh_field(fn, h, lo, hi):
    lo, hi = np.asarray(lo, float), np.asarray(hi, float)
    Fv = sample_grid(fn, lo, hi, h)
    V, Q = surface_nets(Fv, lo, h)
    V = V.astype(F32)
    for _ in range(3):
        d = fn(V)
        g = grad(fn, V, e=h * 0.25)
        gn = np.sqrt((g * g).sum(1))
        stp = np.clip(d / np.maximum(gn, 0.5), -0.4 * h, 0.4 * h)
        V -= (g / np.maximum(gn, 1e-6)[:, None] * stp[:, None]).astype(F32)
    tris = np.concatenate([Q[:, [0, 1, 2]], Q[:, [0, 2, 3]]])
    log(f"surface nets: {len(V)} verts {len(tris)} tris")
    return V.astype(np.float64), tris


def to_mesh(name, V, T):
    me = bpy.data.meshes.new(name)
    V, T = np.asarray(V, np.float64), np.asarray(T, np.int64)
    me.vertices.add(len(V))
    me.vertices.foreach_set("co", V.ravel())
    me.loops.add(len(T) * 3)
    me.loops.foreach_set("vertex_index", T.ravel().astype(np.int32))
    me.polygons.add(len(T))
    me.polygons.foreach_set("loop_start", np.arange(0, len(T) * 3, 3, dtype=np.int32))
    me.update(calc_edges=True)
    me.validate(clean_customdata=False)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def mesh_np(ob):
    me = ob.data
    V = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", V)
    me.calc_loop_triangles()
    T = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("vertices", T)
    return V.reshape(-1, 3), T.reshape(-1, 3)


def decimate_to(ob, target):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    for attempt in range(5):
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        if tris <= target:
            break
        m = ob.modifiers.new("Dec", "DECIMATE")
        m.ratio = target / tris * (0.995 if attempt == 0 else 0.985)
        m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=m.name)
    m = ob.modifiers.new("Tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=m.name)


def build_low(fn, h, lo, hi, target, name):
    Vh, Th = mesh_field(fn, h, lo, hi)
    ob = to_mesh(name, Vh, Th)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bad = [e for e in bm.edges if not e.is_manifold and not e.is_boundary]
    if bad:
        bmesh.ops.split_edges(bm, edges=bad)
    bm.to_mesh(ob.data)
    bm.free()
    decimate_to(ob, target)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    V, T = mesh_np(ob)
    bpy.data.objects.remove(ob, do_unlink=True)
    area = np.linalg.norm(np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]]), axis=1)
    _, first = np.unique(np.sort(T, 1), axis=0, return_index=True)
    keep = np.zeros(len(T), bool)
    keep[first] = True
    keep &= (area > 1e-10) & (T[:, 0] != T[:, 1]) & (T[:, 1] != T[:, 2]) & (T[:, 0] != T[:, 2])
    used = np.unique(T[keep])
    remap = np.full(len(V), -1)
    remap[used] = np.arange(len(used))
    V, T = V[used], remap[T[keep]]
    V = project(fn, V, it=3, clip=0.0015).astype(np.float64)
    log(f"  {name}: {len(V)} verts {len(T)} tris (target {target})")
    return V, T


# =====================================================================================================================
# paint
# =====================================================================================================================
def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mixc(a, b, t):
    t = np.asarray(t, float)
    return a * (1 - t[..., None]) + b * t[..., None]


def field_ao(fn, P, N):
    occ = np.zeros(len(P), F32)
    for h, w in ((0.004, 0.28), (0.009, 0.26), (0.016, 0.2), (0.026, 0.15), (0.040, 0.11)):
        d = fn((P + N * h).astype(F32))
        occ += F32(w) * np.clip((h - d) / h, 0, 1)
    return np.clip(occ, 0, 1)


def ell2(x, z, cx, cz, rx, rz):
    return np.sqrt(((x - cx) / rx) ** 2 + ((z - cz) / rz) ** 2)


# =====================================================================================================================
# paint
# =====================================================================================================================
def paint_points(P, parts, fn):
    P = np.asarray(P, F32)
    N = grad(fn, P, e=2.5e-4)
    N = (N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)).astype(F32)
    own = owner(P, parts)
    keys = np.array([parts[i].key for i in own])
    c = np.zeros((len(P), 3))
    g = np.zeros(len(P))
    for k, (hx, gl) in PAL.items():
        m = keys == k
        c[m] = hexc(hx)
        g[m] = gl
    x, y, z = P[:, 0].astype(float), P[:, 1].astype(float), P[:, 2].astype(float)
    ang = np.arctan2(y, x)
    fur = np.isin(keys, ["fur", "fringe", "arm"])
    # willow fronds: darker grooves / lighter ridges along the strands, amber (autumn grass) at the tips
    wav = (np.sin(22 * ang + 2.2 * np.sin(z * 9.0) + 1.7 * np.sin(5 * ang)) * (0.65 + 0.35 * np.sin(7 * ang + 2.0))
           + 0.4 * np.sin(35.2 * ang - 3.1 * z + 1.3))
    fine = np.sin(70 * ang + 5 * np.sin(z * 23))
    c[fur] = mixc(c[fur], hexc("#4E7E40"), 0.55 * sstep(0.2, -0.9, wav[fur]))
    c[fur] = mixc(c[fur], hexc("#8DBE6A"), 0.45 * sstep(0.5, 1.2, wav[fur]) * (fine[fur] > -0.2))
    tip = sstep(0.16, 0.03, z)                                   # hem tips
    c[fur] = mixc(c[fur], hexc("#E0A84A"), 0.85 * tip[fur])
    fr = keys == "fringe"
    ftip = sstep(0.545, 0.515, z)                                # fringe tips over the hollow
    c[fr] = mixc(c[fr], hexc("#E0A84A"), 0.75 * ftip[fr])
    am = keys == "arm"
    hand = np.zeros(len(P), bool)
    for s in SIDES:
        hand |= np.linalg.norm(P - HAND[s], axis=1) < 0.06
    c[am & hand] = mixc(c[am & hand], hexc("#E0A84A"), np.full((am & hand).sum(), 0.55))
    # a few amber leaves / grass blades scattered on the coat
    hsh = np.sin(x * 812.1 + y * 371.7 + z * 523.3) * 43758.5453
    spk = (keys == "fur") & ((hsh - np.floor(hsh)) > 0.985)
    c[spk] = hexc("#E9BC5E")
    # hollow: dark, with two firefly lights (glow halo)
    ho = keys == "hollow"
    for s in SIDES:
        d = np.sqrt((x - EYE[s][0]) ** 2 + ((z - EYE[s][2]) * 0.9) ** 2)
        halo = sstep(0.040, 0.010, d) * ho
        c = mixc(c, hexc("#7C8A3E"), 0.6 * halo)
        core = sstep(0.0165, 0.0115, d) * ho
        c = mixc(c, hexc("#F4FF9A"), core)
        g = np.where(core > 0.5, 0.9, g)
    # nest: woven twig stripes
    ne = np.isin(keys, ["nest", "twig"])
    wv = np.sin(ang * 30 + (z - NEST_C[2]) * 160) * np.sin(ang * 13 - (z - NEST_C[2]) * 120)
    c[ne] = mixc(c[ne], hexc("#4A3222"), 0.6 * (wv[ne] > 0.25))
    c[ne] = mixc(c[ne], hexc("#A57B52"), 0.5 * (wv[ne] < -0.55))
    # bird: closed sleepy eyes (two dark arcs) + light wing edge
    bi = keys == "bird"
    hc = BIRD_C + (0, -0.044, 0.030)
    for s in SIDES:
        ex, ez = hc[0] + 0.019 * s, hc[2] + 0.004
        arc = (np.abs(np.sqrt((x - ex) ** 2 + (z - ez - 0.006) ** 2) - 0.008) < 0.0022) & (z < ez + 0.006) & (y < hc[1])
        c[bi & arc] = hexc("#2A1D15")
    wing = bi & (np.abs(np.abs(x) - 0.040) < 0.006) & (z < BIRD_C[2] + 0.01)
    c[wing] = hexc("#B39272")
    # light: AO + soft top light
    occ = field_ao(fn, P, N)
    lit = 0.80 + 0.20 * np.clip(N[:, 2] * 0.5 + 0.5, 0, 1)
    c = c * ((1 - 0.55 * occ) * lit)[:, None]
    dmin = np.minimum(*[np.sqrt((x - EYE[s][0]) ** 2 + (z - EYE[s][2]) ** 2) for s in SIDES])
    c[ho & (dmin < 0.0125)] = hexc("#F4FF9A")                    # fireflies stay bright (no AO)
    return np.clip(c, 0, 1), g


# =====================================================================================================================
# UV + texture bake (as build_theo_v3.py)
# =====================================================================================================================
def unwrap(ob, own_fn):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(70), island_margin=0.003, area_weight=1.0, scale_to_bounds=False)
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")
    me = ob.data
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    uvl = bm.loops.layers.uv.active
    par = list(range(len(bm.faces)))

    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]
            a = par[a]
        return a
    for e in bm.edges:
        lf = e.link_loops
        if len(lf) != 2:
            continue
        l1, l2 = lf
        a1, b1 = l1[uvl].uv, l1.link_loop_next[uvl].uv
        a2, b2 = l2[uvl].uv, l2.link_loop_next[uvl].uv
        if (a1 - b2).length < 1e-6 and (b1 - a2).length < 1e-6:
            ra, rb = find(l1.face.index), find(l2.face.index)
            if ra != rb:
                par[ra] = rb
    isl = np.array([find(i) for i in range(len(bm.faces))])
    cen = np.array([f.calc_center_median()[:] for f in bm.faces])
    area = np.array([f.calc_area() for f in bm.faces])
    bm.free()
    ff = uv_boost(cen)
    loop_face = np.repeat(np.arange(len(me.polygons)), [len(p.loop_indices) for p in me.polygons])
    for k in np.unique(isl):
        fm = isl == k
        fac = float((ff[fm] * area[fm]).sum() / max(area[fm].sum(), 1e-12))
        lm = fm[loop_face]
        c0 = uv[lm].mean(0)
        uv[lm] = c0 + (uv[lm] - c0) * fac
    me.uv_layers.active.data.foreach_set("uv", uv.ravel())
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    log(f"UV: {len(np.unique(isl))} islands")
    return uv.reshape(-1, 2)


def raster(UVc, res):
    tri = np.full(res * res, -1, np.int32)
    score = np.full(res * res, -1e9, np.float32)
    bary = np.zeros((res * res, 3), np.float32)
    P = UVc * res - 0.5
    for i in range(len(P)):
        a, b, c = P[i]
        x0, x1 = int(max(math.floor(min(a[0], b[0], c[0]) - 1), 0)), int(min(math.ceil(max(a[0], b[0], c[0]) + 1), res - 1))
        y0, y1 = int(max(math.floor(min(a[1], b[1], c[1]) - 1), 0)), int(min(math.ceil(max(a[1], b[1], c[1]) + 1), res - 1))
        if x1 < x0 or y1 < y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        X, Y = X.ravel().astype(np.float64), Y.ravel().astype(np.float64)
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        l0 = ((b[1] - c[1]) * (X - c[0]) + (c[0] - b[0]) * (Y - c[1])) / den
        l1 = ((c[1] - a[1]) * (X - c[0]) + (a[0] - c[0]) * (Y - c[1])) / den
        l2 = 1 - l0 - l1
        e0, e1, e2 = np.linalg.norm(b - c), np.linalg.norm(c - a), np.linalg.norm(a - b)
        dist = np.minimum(np.minimum(l0 * abs(den) / max(e0, 1e-9), l1 * abs(den) / max(e1, 1e-9)), l2 * abs(den) / max(e2, 1e-9))
        ok = dist > -0.7
        if not ok.any():
            continue
        idx = (Y[ok].astype(int) * res + X[ok].astype(int))
        better = dist[ok] > score[idx]
        idx = idx[better]
        tri[idx] = i
        score[idx] = dist[ok][better]
        L = np.clip(np.stack([l0[ok][better], l1[ok][better], l2[ok][better]], 1), -0.2, 1.2)
        bary[idx] = L / L.sum(1, keepdims=True)
    return tri, bary


def dilate(img, filled, iters=16):
    img, filled = img.copy(), filled.copy()
    for _ in range(iters):
        acc = np.zeros_like(img)
        cnt = np.zeros(filled.shape, np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                sf = np.roll(np.roll(filled, dy, 0), dx, 1)
                acc += np.roll(np.roll(img, dy, 0), dx, 1) * sf[..., None]
                cnt += sf
        new = (~filled) & (cnt > 0)
        if not new.any():
            break
        img[new] = acc[new] / cnt[new][:, None]
        filled |= new
    img[~filled] = img[filled].mean(0)
    return img


def bake_texture(ob, uv, sid, shells):
    me = ob.data
    V, T = mesh_np(ob)
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    lt = lt.reshape(-1, 3)
    tri, bary = raster(uv[lt], TEX)
    has = tri >= 0
    ti = tri[has]
    b = bary[has].astype(np.float64)
    P0 = V[T[ti, 0]] * b[:, :1] + V[T[ti, 1]] * b[:, 1:2] + V[T[ti, 2]] * b[:, 2:3]
    tsid = sid[T[ti, 0]]
    cols, gls = np.zeros((len(P0), 3)), np.zeros(len(P0))
    for k, (nm, parts, fn) in enumerate(shells):
        sel = np.nonzero(tsid == k)[0]
        Q = project(fn, P0[sel], it=6, clip=0.004)
        for c0 in range(0, len(Q), 150000):
            cc, gg = paint_points(Q[c0:c0 + 150000], parts, fn)
            cols[sel[c0:c0 + 150000]], gls[sel[c0:c0 + 150000]] = cc, gg
        log(f"  painted {nm}: {len(sel)} texels")
    img = np.zeros((TEX * TEX, 4), np.float32)
    img[has, :3] = cols
    img[has, 3] = gls
    return dilate(img.reshape(TEX, TEX, 4), has.reshape(TEX, TEX))


def sample_tex(img, uv, blur=2):
    res = img.shape[0]
    acc = np.zeros((len(uv), 4))
    n = 0
    for dy in (-blur, 0, blur):
        for dx in (-blur, 0, blur):
            x = np.clip(uv[:, 0] * res - 0.5 + dx, 0, res - 1).astype(int)
            y = np.clip(uv[:, 1] * res - 0.5 + dy, 0, res - 1).astype(int)
            acc += img[y, x]
            n += 1
    return acc / n


def srgb_to_lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)



# =====================================================================================================================
# rig (Extra = nest + robin, child of Head). Legs are hidden under the coat: LegL/LegR move the hem halves.
# =====================================================================================================================
PROP_HEAD = HAND[-1]
BONE_DEF = [
    ("Root", None, (0, 0, 0.018), (0, 0, 0.20)),
    ("Body", "Root", (0, 0, 0.20), (0, 0, 0.50)),
    ("Head", "Body", (0, 0, 0.50), (0, 0, 0.76)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", PROP_HEAD, PROP_HEAD + np.array([0, -0.08, 0])),
    ("EyeR", "Head", EYE[-1] + (0, 0.012, 0), EYE[-1] + (0, -0.018, 0)),
    ("EyeL", "Head", EYE[1] + (0, 0.012, 0), EYE[1] + (0, -0.018, 0)),
    ("LidR", "Head", EYE[-1] + (0, 0.012, 0), EYE[-1] + (0, -0.018, 0)),
    ("LidL", "Head", EYE[1] + (0, 0.012, 0), EYE[1] + (0, -0.018, 0)),
    ("Extra", "Head", NEST_C - (0, 0, 0.03), NEST_C + (0, 0, 0.10)),
]
BONES = [b[0] for b in BONE_DEF]


def vertex_weights(V, parts_of_vertex):
    W = []
    for p, v in zip(parts_of_vertex, V):
        bone = p.bone
        z = float(v[2])
        if isinstance(bone, tuple):
            _, s = bone
            t = sstep(0.0, 0.06, float(np.dot(v - SH[s], nrm(EL[s] - SH[s]))))
            w = {"Arm" + SN[s]: 0.5 + 0.5 * t, "Body": 0.5 - 0.5 * t}
        elif bone == "Body":          # the haystack: Root (hem) -> Body -> Head (top), hem halves on the hidden legs
            if z >= 0.40:
                t = sstep(0.48, 0.60, z)
                w = {"Head": t, "Body": 1 - t}
            elif z >= 0.20:
                t = sstep(0.30, 0.20, z)
                w = {"Root": t, "Body": 1 - t}
            else:
                s = 1 if v[0] > 0 else -1
                t = 0.6 * sstep(0.20, 0.04, z) * sstep(0.03, 0.14, abs(float(v[0])))
                w = {"Leg" + SN[s]: t, "Root": 1 - t}
        else:
            w = {bone: 1.0}
        w = {k: x for k, x in w.items() if x > 0.02}
        tot = sum(w.values())
        W.append({k: x / tot for k, x in w.items()})
    return W


def make_object(name, V, T, uvc, W, img, tex_path, shift, scale):
    Vn = (V + shift) * scale
    ob = to_mesh(name, Vn, T)
    me = ob.data
    ul = me.uv_layers.new(name="UVMap")
    ul.data.foreach_set("uv", np.asarray(uvc, np.float64).ravel())
    smp = sample_tex(img, uvc, blur=3)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(smp[:, :3]), smp[:, 3:4]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    mat = bpy.data.materials.get("FighterVC") or bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    texn = nt.nodes.new("ShaderNodeTexImage")
    texn.image = bpy.data.images.load(tex_path, check_existing=True)
    texn.image.alpha_mode = "CHANNEL_PACKED"
    nt.links.new(texn.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    arm_data = bpy.data.armatures.new(name + "Rig")
    arm = bpy.data.objects.new("Rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, parent, hd, tl in BONE_DEF:
        eb = arm_data.edit_bones.new(bn)
        eb.head, eb.tail = Vector((np.asarray(hd) + shift) * scale), Vector((np.asarray(tl) + shift) * scale)
        if parent:
            eb.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    groups = {bn: ob.vertex_groups.new(name=bn) for bn in BONES}
    for i, w in enumerate(W):
        for bn, val in w.items():
            groups[bn].add([i], float(val), "REPLACE")
    ob.parent = arm
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return ob, arm


def export(objs, active, base):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active
    types = {"ARMATURE", "MESH", "EMPTY"}
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types=types, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)



# =====================================================================================================================
# "weapon": a dandelion fluff puff on a short stem (Fluff Puff attack). Local frame: origin = right hand centre,
# forward -Y, up +Z. Empty "Muzzle" at the puff centre (lob spawn point).
# =====================================================================================================================
PUFF_C = np.array([0.0, -0.085, 0.085])
PUFF_R = 0.072
WPARTS = []


def wpart(name, f, key, k=0.0):
    WPARTS.append(Part(name, f, key, "Prop", k))


def _puff(P):
    Q = P - PUFF_C.astype(F32)
    r = np.sqrt((Q * Q).sum(1))
    n = Q / np.maximum(r, 1e-6)[:, None]
    bump = np.abs(np.sin(15 * n[:, 0] + 0.3) * np.sin(15 * n[:, 1] + 1.1) * np.sin(15 * n[:, 2] + 2.0)) ** 0.5
    return (r - PUFF_R - 0.007 * bump).astype(F32)


wpart("puff", _puff, "fluff")
wpart("stem", cone((0, 0.025, -0.015), PUFF_C + (0, 0.03, -0.04), 0.0085, 0.0065), "stem", k=0.004)
wpart("stem_top", cone(PUFF_C + (0, 0.03, -0.04), PUFF_C, 0.0065, 0.012), "core", k=0.004)


def eval_w(P):
    P = np.asarray(P, F32)
    out = np.full(len(P), BIG, F32)
    for p in WPARTS:
        out = smin(out, p.f(P), p.k) if p.k > 0 else np.minimum(out, p.f(P))
    return out


def weapon_paint(P):
    own = owner(P, WPARTS)
    keys = np.array([WPARTS[i].key for i in own])
    c = np.zeros((len(P), 3))
    g = np.zeros(len(P))
    for k, (hx, gl) in PALW.items():
        m = keys == k
        c[m] = hexc(hx)
        g[m] = gl
    pf = keys == "fluff"
    Q = P - PUFF_C
    r = np.linalg.norm(Q, axis=1)
    c[pf] = mixc(c[pf], hexc("#E6E0CC"), sstep(PUFF_R + 0.006, PUFF_R, r[pf]))   # creamy grooves between tufts
    return c, g


def build_weapon(scale):
    lo, hi = np.array([-0.10, -0.19, -0.04]), np.array([0.10, 0.06, 0.18])
    V, T = build_low(eval_w, 0.0028, lo, hi, BUDGET_WEAPON, "MossyWeapon")
    c, g = weapon_paint(V)
    ob = to_mesh("MossyWeapon", V * scale, T)
    me = ob.data
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    lin = np.hstack([srgb_to_lin(c), g[:, None]]).astype(np.float32)
    lv = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", lv)
    attr.data.foreach_set("color", lin[lv].ravel())
    me.color_attributes.active_color = attr
    mat = bpy.data.materials.get("FighterVC") or bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    vcn = nt.nodes.new("ShaderNodeVertexColor")
    vcn.layer_name = "Col"
    nt.links.new(vcn.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    mz = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(mz)
    mz.location = Vector(PUFF_C * scale)
    mz.parent = ob
    log(f"weapon: {len(T)} tris, muzzle {np.round(PUFF_C * scale, 3)}")
    return ob, mz, len(T)


# =====================================================================================================================
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = PARTS
    shells = [("body", parts, fig_d)]
    lo_all, hi_all = (-0.34, -0.34, -0.005), (0.34, 0.34, 0.92)
    Vh, Th = mesh_field(fig_d, 0.004, lo_all, hi_all)
    lo, hi = Vh.min(0), Vh.max(0)
    shift = np.array([0.0, 0.0, -lo[2]])
    scale = HEIGHT / (hi[2] - lo[2])
    log("bbox", np.round(lo, 3), np.round(hi, 3), "scale", round(scale, 4))
    V, T = build_low(fig_d, 0.0026, lo_all, hi_all, BUDGET_BODY, "body")
    sid = np.zeros(len(V), int)
    low = to_mesh("Mossy", V, T)
    uv = unwrap(low, None)
    img = bake_texture(low, uv, sid, shells)
    tex_path = os.path.join(OUT_DIR, NAME + "_tex.png")
    im = bpy.data.images.new(NAME + "_tex", TEX, TEX, alpha=True)
    im.alpha_mode = "CHANNEL_PACKED"
    im.pixels.foreach_set(np.ascontiguousarray(img, np.float32).ravel())
    im.filepath_raw = tex_path
    im.file_format = "PNG"
    im.save()
    V, T = mesh_np(low)
    me = low.data
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    uvc = uv[lt]
    bpy.data.objects.remove(low, do_unlink=True)
    o = owner(V.astype(F32), parts)
    W = vertex_weights(V, [parts[i] for i in o])
    ob, arm = make_object("Mossy", V, T, uvc, W, img, tex_path, shift, scale)
    export([ob, arm], arm, os.path.join(OUT_DIR, NAME + "_game"))
    log(f"RESULT body tris {len(T)}/4000 verts {len(V)} max infl {max(len(w) for w in W)}")
    log("bones (export units):", {bn: tuple(np.round((np.asarray(hd) + shift) * scale, 3)) for bn, _, hd, _ in BONE_DEF})
    bpy.ops.wm.read_factory_settings(use_empty=True)
    wob, mz, wtris = build_weapon(scale)
    export([wob, mz], wob, os.path.join(OUT_DIR, NAME + "_weapon"))
    log(f"RESULT weapon tris {wtris}/1200")
    log("done")


main()
