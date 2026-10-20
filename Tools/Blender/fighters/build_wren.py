# Wren (id wren, Docs/Roster-v4.md 4.1): 8-year-old girl with a huge sword made from an old door. Base model for
# Docs/Briefs/A-Roster3D.md, after the Theo v3 pipeline (build_theo_v3.py) but compact. Headless (Blender 5.2, numpy only):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_wren.py -- [out_dir]
# Output (default Art/Source/3D/wren/): wren_game.fbx/.glb (<= 4000 tris, mesh "Wren", material "FighterVC" with the
# texture, armature "Rig" with the 12 FighterBone bones), wren_tex.png (1024 RGBA: RGB albedo with baked AO, A = gloss),
# wren_weapon.fbx/.glb (<= 1200 tris, mesh "WrenWeapon", vertex colour "Col" only, origin = grip under the right hand,
# blade along -Y = forward, top +Z). Format as Theo: Z up, faces -Y, feet z = 0, height HEIGHT; FBX axis_forward=-Z, axis_up=Y.
# Method: one signed distance field of primitives (smooth unions) -> surface nets + Newton -> two shells (head, body)
# quadric-decimated to their budgets -> smart UV -> per-texel paint on the exact surface (face, freckles, star, glitter).
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
POS = [a for a in ARGV if not a.startswith("--")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "wren")
os.makedirs(OUT_DIR, exist_ok=True)
T0 = time.time()
F32 = np.float32
BIG = F32(10.0)
HEIGHT = 0.78          # Theo = 1.0; Wren is 8 (Theo 12)
TEX = 1024
BUDGET_HEAD, BUDGET_BODY, BUDGET_WEAPON = 1950, 2000, 1180


def log(*a):
    print(f"[wren {time.time() - T0:6.1f}s]", *a, flush=True)


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


# =====================================================================================================================
# palette (sRGB) and gloss. Roster-v4: peach #FFB48A (tutu, ribbons), light wood #D9B48A (sword), ivory #FFF6E6 (T-shirt),
# brass #E8C15A (door knob). Own additions: skin, ginger hair, mint helmet + boots (sky/red are team colours).
# =====================================================================================================================
PAL = {
    "skin": ("#FCD6BC", 0.18), "hair": ("#C2602E", 0.35), "shirt": ("#FFF6E6", 0.12), "tutu": ("#FFB48A", 0.18),
    "ribbon": ("#FF9E70", 0.35), "legs": ("#7C5A62", 0.12), "boot": ("#5FC3AE", 0.85), "helmet": ("#7FD8C4", 0.75),
    "sole": ("#3F8F80", 0.4), "strap": ("#4E6E68", 0.3),
}
PALW = {"wood": ("#D9B48A", 0.25), "wood_dark": ("#B48A5E", 0.2), "brass": ("#E8C15A", 0.9), "grip": ("#8A6040", 0.2),
        "tape": ("#FFB48A", 0.2), "slot": ("#3A2A20", 0.1), "hinge": ("#8C8C94", 0.8)}

# =====================================================================================================================
# MODEL (model units; normalised to HEIGHT at export). Character's right = -X.
# =====================================================================================================================
SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}
HEAD_C = np.array([0, 0.0, 0.535])
HEAD_R = np.array([0.150, 0.138, 0.140])
SH = {s: np.array([0.083 * s, 0.004, 0.393]) for s in SIDES}
EL = {-1: np.array([-0.112, -0.060, 0.330]), 1: np.array([0.100, -0.060, 0.330])}
WR = {-1: np.array([-0.050, -0.150, 0.300]), 1: np.array([0.002, -0.098, 0.305])}
HAND = {-1: np.array([-0.036, -0.170, 0.300]), 1: np.array([-0.018, -0.116, 0.308])}
HIP = {s: np.array([0.045 * s, 0.004, 0.245]) for s in SIDES}
AN = {s: np.array([0.052 * s, 0.000, 0.075]) for s in SIDES}
EYE = {s: np.array([0.056 * s, -0.122, 0.525]) for s in SIDES}   # centre of the painted eye (on the face surface)


class Part:
    def __init__(self, name, f, key, bone, k=0.0, shell="body"):
        self.name, self.f, self.key, self.bone, self.k, self.shell = name, f, key, bone, k, shell


PARTS = []


def part(name, f, key, bone, k=0.0, shell="body"):
    PARTS.append(Part(name, f, key, bone, k, shell))


# head: face, hair cap (cut away in front of the face), fringe, buns + bows, bike helmet
part("head", ell(HEAD_C, HEAD_R), "skin", "Head", shell="head")
part("cheeks", ell(HEAD_C + (0, -0.03, -0.045), (0.128, 0.105, 0.090)), "skin", "Head", k=0.03, shell="head")
_cap = ell(HEAD_C + (0, 0.012, 0.004), (0.157, 0.150, 0.150))
part("hair", lambda P: smax(_cap(P), (-0.035 - P[:, 1]).astype(F32), 0.02), "hair", "Head", k=0.01, shell="head")
part("fringe", ell(HEAD_C + (0, -0.112, 0.060), (0.112, 0.040, 0.034)), "hair", "Head", k=0.012, shell="head")
for s in SIDES:
    part(f"bun_{SN[s]}", ell((0.128 * s, 0.035, 0.635), (0.062, 0.060, 0.058)), "hair", "Head", k=0.02, shell="head")
    bc = np.array([0.150 * s, 0.000, 0.580])
    part(f"bow_knot_{SN[s]}", ell(bc, (0.016, 0.016, 0.016)), "ribbon", "Head", shell="head")
    for dy in (-1, 1):
        part(f"bow_wing_{SN[s]}{dy}", ell(bc + (0.004 * s, 0.030 * dy, 0.006), (0.013, 0.030, 0.022), rot((1, 0, 0), 25 * dy)),
             "ribbon", "Head", k=0.006, shell="head")
    part(f"bow_tail_{SN[s]}", cone(bc, bc + (0.022 * s, 0.018, -0.070), 0.011, 0.007), "ribbon", "Head", k=0.006, shell="head")
_hel = ell(HEAD_C + (0, 0.008, 0.020), (0.166, 0.160, 0.150))
_hel_in = ell(HEAD_C + (0, 0.008, 0.020), (0.150, 0.144, 0.134))
part("helmet", lambda P: smax(smax(_hel(P), -_hel_in(P), 0.004), (0.598 - P[:, 2] - 0.05 * np.maximum(-P[:, 1], 0)).astype(F32),
                              0.012), "helmet", "Head", shell="head")
part("helmet_ridge", box(HEAD_C + (0, 0.0, 0.168), (0.016, 0.135, 0.012), 0.008), "helmet", "Head", k=0.015, shell="head")
for s in SIDES:
    part(f"strap_{SN[s]}", cone((0.135 * s, -0.035, 0.570), (0.090 * s, -0.080, 0.430), 0.0065, 0.0065), "strap", "Head",
         shell="head")
# body
part("neck", cone((0, 0.004, 0.40), (0, 0.004, 0.46), 0.032, 0.030), "skin", "Body", k=0.01)
part("torso", ell((0, 0.004, 0.335), (0.080, 0.064, 0.095)), "shirt", "Body", k=0.01)
part("pelvis", ell((0, 0.004, 0.252), (0.070, 0.056, 0.048)), "legs", "Root", k=0.02)


def _tutu(P):
    c = np.array([0, 0.004, 0.252], F32)
    Q = P - c
    ang = np.arctan2(Q[:, 1], Q[:, 0])
    rr = np.sqrt(Q[:, 0] ** 2 + Q[:, 1] ** 2)
    wav = (0.007 * np.sin(14 * ang) * np.clip(rr / 0.13, 0, 1)).astype(F32)
    return smin(ell(c, (0.142, 0.135, 0.040))(P) + wav, ell(c + (0, 0, 0.022), (0.120, 0.112, 0.032))(P), 0.02)


part("tutu", _tutu, "tutu", "Root", k=0.0)
for s in SIDES:
    part(f"sleeve_{SN[s]}", cone(SH[s], SH[s] + (EL[s] - SH[s]) * 0.45, 0.036, 0.031), "shirt", ("Arm", s), k=0.015)
    part(f"upper_{SN[s]}", cone(SH[s], EL[s], 0.027, 0.024), "skin", ("Arm", s), k=0.008)
    part(f"fore_{SN[s]}", cone(EL[s], WR[s], 0.024, 0.021), "skin", ("Arm", s), k=0.008)
    part(f"hand_{SN[s]}", ell(HAND[s], (0.030, 0.032, 0.029)), "skin", ("Arm", s), k=0.012)
    part(f"leg_{SN[s]}", cone(HIP[s], AN[s] + (0, 0, 0.05), 0.036, 0.029), "legs", ("Leg", s), k=0.015)
    part(f"boot_{SN[s]}", cone(AN[s] + (0, 0, 0.085), AN[s] + (0, -0.006, 0.030), 0.036, 0.040), "boot", ("Leg", s), k=0.012)
    part(f"boot_toe_{SN[s]}", ell(AN[s] + (0, -0.030, -0.040), (0.040, 0.060, 0.036)), "boot", ("Leg", s), k=0.02)
    part(f"boot_rim_{SN[s]}", cyl(AN[s] + (0, 0, 0.085), (0, 0, 1), 0.040, 0.010, 0.006), "boot", ("Leg", s), k=0.004)
    part(f"sole_{SN[s]}", lambda P, s=s: smax(ell(AN[s] + (0, -0.026, -0.030), (0.044, 0.066, 0.040))(P),
                                                (P[:, 2] - 0.012).astype(F32), 0.004), "sole", ("Leg", s), k=0.004)


def eval_parts(P, parts):
    P = np.asarray(P, F32)
    out = np.full(len(P), BIG, F32)
    for p in parts:
        out = smin(out, p.f(P), p.k) if p.k > 0 else np.minimum(out, p.f(P))
    return np.maximum(out, (-0.0 - P[:, 2]).astype(F32))   # flat under the soles (z >= 0)


def fig_d(P):
    return eval_parts(P, PARTS)


def owner(P, parts=None):
    parts = parts or PARTS
    D = np.stack([p.f(np.asarray(P, F32)) for p in parts], 0)
    return np.argmin(D, 0)


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


def paint_face(c, g, P, N):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    front = sstep(-0.04, -0.09, y)
    # blush
    for s in SIDES:
        b = ell2(x, z, 0.082 * s, 0.478, 0.030, 0.018)
        c = mixc(c, hexc("#FFA58F"), 0.45 * sstep(1.0, 0.3, b) * front)
    # freckles (fixed dots across the nose and cheeks)
    for fx, fz in ((-0.030, 0.492), (-0.044, 0.500), (-0.058, 0.488), (-0.072, 0.498), (-0.038, 0.482), (-0.064, 0.476)):
        for s in SIDES:
            dd = np.sqrt((x - fx * -s) ** 2 + (z - fz) ** 2)
            c = mixc(c, hexc("#C98A5E"), 0.75 * sstep(0.0042, 0.0028, dd) * front)
    # eyes: big dark ovals with brown iris and two highlights; lash flick on the outer corner
    for s in SIDES:
        ex, ez = EYE[s][0], EYE[s][2]
        e = ell2(x, z, ex, ez, 0.026, 0.033)
        m = sstep(1.04, 0.96, e) * front
        c = mixc(c, hexc("#2A1B14"), m)
        ir = ell2(x, z, ex, ez - 0.004, 0.019, 0.025)
        c = mixc(c, hexc("#7A4A26"), sstep(1.0, 0.85, ir) * m)
        pu = ell2(x, z, ex, ez - 0.004, 0.010, 0.013)
        c = mixc(c, hexc("#1A100B"), sstep(1.0, 0.8, pu) * m)
        h1 = ell2(x, z, ex - 0.008 * s - 0.004, ez + 0.012, 0.0075, 0.0085)
        h2 = ell2(x, z, ex + 0.008, ez - 0.013, 0.0035, 0.0035)
        hl = np.maximum(sstep(1.0, 0.7, h1), sstep(1.0, 0.6, h2)) * m
        c = mixc(c, np.ones(3), hl)
        lash = np.abs(ell2(x, z, ex, ez, 0.030, 0.037) - 1) < 0.08
        lash &= (z > ez + 0.01) & ((x - ex) * s > 0.0)
        c = mixc(c, hexc("#2A1B14"), lash * front)
        g = np.where(m > 0.5, 1.0, g)
        # brows
        bz = ez + 0.050 + 0.004 * (1 - ((x - ex) / 0.022) ** 2)
        br = (np.abs(z - bz) < 0.0045) & (np.abs(x - ex) < 0.020)
        c = mixc(c, hexc("#9A4A22"), br * front)
    # smile with a gap-tooth: open dark mouth, upper teeth white with a gap in the middle
    mz = 0.441 + 0.10 * x ** 2 * 18
    mouth = (z < 0.452) & (z > 0.425 + 0.0) & (np.abs(x) < 0.026)
    lower = 0.452 - 0.026 * (1 - (x / 0.026) ** 2)
    mouth &= z > lower
    c = mixc(c, hexc("#7A2E2A"), mouth * front)
    teeth = mouth & (z > 0.444) & (np.abs(x) > 0.0035) & (np.abs(x) < 0.018)
    c = mixc(c, hexc("#FFFBF2"), teeth * front)
    tongue = mouth & (z < 0.434) & (np.abs(x) < 0.012)
    c = mixc(c, hexc("#E57A73"), tongue * front)
    _ = mz
    return c, g


def star_mask(u, v, cu, cv, r):
    a = np.arctan2(v - cv, u - cu)
    rr = np.sqrt((u - cu) ** 2 + (v - cv) ** 2)
    k = np.cos(5 * (a - math.pi / 2)) * 0.5 + 0.5
    return rr < r * (0.45 + 0.55 * k ** 3)


def paint_points(P, parts, fn):
    P = np.asarray(P, F32)
    N = grad(fn, P, e=2.5e-4)
    N = (N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)).astype(F32)
    own = owner(P, parts)
    keys = np.array([parts[i].key for i in own])
    names = np.array([parts[i].name for i in own])
    c = np.zeros((len(P), 3))
    g = np.zeros(len(P))
    for k, (hx, gl) in PAL.items():
        m = keys == k
        c[m] = hexc(hx)
        g[m] = gl
    x, y, z = P[:, 0].astype(float), P[:, 1].astype(float), P[:, 2].astype(float)
    # hair: darker at the roots / under the helmet, light streaks on the buns
    hm = keys == "hair"
    c[hm] = mixc(c[hm], hexc("#E08446"), 0.35 * sstep(0.60, 0.68, z[hm]) * (np.sin(60 * np.arctan2(y[hm], x[hm] - np.sign(x[hm]) * 0.128)) > 0.4))
    # helmet: darker vent stripes along the top + brass star sticker on the right side
    he = keys == "helmet"
    vent = he & (np.abs(np.abs(x) - 0.055) < 0.012) & (z > 0.64) & (np.abs(y) < 0.11)
    c[vent] = hexc("#3E9C89")
    st = he & star_mask(y, z, 0.00, 0.62, 0.042) & (x < -0.10)
    c[st] = hexc("#E8C15A")
    g[st] = 0.9
    st2 = he & star_mask(y, z, 0.02, 0.63, 0.030) & (x > 0.12)
    c[st2] = hexc("#FFB48A")
    # boots: glitter specks
    bm_ = keys == "boot"
    hsh = np.sin(x * 9123.1 + y * 3711.7 + z * 5233.3) * 43758.5453
    spk = bm_ & ((hsh - np.floor(hsh)) > 0.93)
    c[spk] = np.array([1.0, 0.97, 0.85])
    # shirt: peach collar band + small brass door-knob badge on the chest
    sm = keys == "shirt"
    collar = sm & (z > 0.405)
    c[collar] = hexc("#FFB48A")
    badge = sm & (np.sqrt((x - 0.035) ** 2 + (z - 0.355) ** 2) < 0.014) & (y < 0)
    c[badge] = hexc("#E8C15A")
    g[badge] = 0.85
    # tutu: tulle layers (lighter rim bands)
    tm = keys == "tutu"
    rr = np.sqrt(x ** 2 + y ** 2)
    c[tm] = mixc(c[tm], hexc("#FFD2B6"), 0.5 * sstep(0.09, 0.14, rr[tm]) * (np.sin(rr[tm] * 260) > 0))
    # leggings: soft stripes
    lg = (keys == "legs") & (z < 0.22)
    c[lg] = mixc(c[lg], hexc("#94707A"), (np.sin(z[lg] * 180) > 0.3) * 1.0)
    # face
    fm = np.isin(names, ["head", "cheeks"])
    if fm.any():
        cc, gg = paint_face(c[fm], g[fm], P[fm].astype(float), N[fm])
        c[fm], g[fm] = cc, gg
    # light: AO + soft top light
    occ = field_ao(fn, P, N)
    lit = 0.80 + 0.20 * np.clip(N[:, 2] * 0.5 + 0.5, 0, 1)
    c = c * ((1 - 0.55 * occ) * lit)[:, None]
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
    ff = np.ones(len(cen))
    ff[cen[:, 2] > 0.42] = 1.3
    face = (cen[:, 2] > 0.42) & (cen[:, 2] < 0.59) & (cen[:, 1] < -0.06) & (np.abs(cen[:, 0]) < 0.11)
    ff[face] = 2.4
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
# rig
# =====================================================================================================================
PROP_HEAD = HAND[-1]
BONE_DEF = [
    ("Root", None, (0, 0, 0), (0, 0, 0.245)),
    ("Body", "Root", (0, 0, 0.245), (0, 0, 0.415)),
    ("Head", "Body", (0, 0, 0.415), (0, 0, 0.70)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", PROP_HEAD, PROP_HEAD + np.array([0, -0.08, 0])),
    ("EyeR", "Head", EYE[-1] + (0, 0.012, 0), EYE[-1] + (0, -0.018, 0)),
    ("EyeL", "Head", EYE[1] + (0, 0.012, 0), EYE[1] + (0, -0.018, 0)),
    ("LidR", "Head", EYE[-1] + (0, 0.012, 0), EYE[-1] + (0, -0.018, 0)),
    ("LidL", "Head", EYE[1] + (0, 0.012, 0), EYE[1] + (0, -0.018, 0)),
]
BONES = [b[0] for b in BONE_DEF]


def vertex_weights(V, parts_of_vertex):
    W = []
    for p, v in zip(parts_of_vertex, V):
        bone = p.bone
        if isinstance(bone, tuple):
            kind, s = bone
            if kind == "Arm":
                t = sstep(0.0, 0.045, float(np.dot(v - SH[s], nrm(EL[s] - SH[s]))))
                w = {"Arm" + SN[s]: 0.5 + 0.5 * t, "Body": 0.5 - 0.5 * t}
            else:
                t = sstep(0.215, 0.18, float(v[2]))
                w = {"Leg" + SN[s]: 0.5 + 0.5 * t, "Root": 0.5 - 0.5 * t}
        elif bone == "Body":
            t = sstep(0.27, 0.31, float(v[2]))
            w = {"Body": t, "Root": 1 - t} if p.name == "torso" else {"Body": 1.0}
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
# weapon: "Creak", the door sword. Local frame: origin = grip centre under the right hand, blade along -Y, flat face up (+Z)
# =====================================================================================================================
W_LEN, W_WID, W_TH = 0.86, 0.250, 0.034
G0 = -0.060                       # guard (door knob rose) y
B0, B1 = G0 - 0.012, G0 - 0.012 - W_LEN
WPARTS = []


def wpart(name, f, key, k=0.0):
    WPARTS.append(Part(name, f, key, "Prop", k))


_slab = box((0, (B0 + B1) / 2, 0), (W_WID / 2, W_LEN / 2, W_TH / 2), 0.008)
_pan1 = box((0, B0 - 0.25, W_TH / 2 + 0.004), (0.080, 0.15, 0.010), 0.006)
_pan2 = box((0, B1 + 0.20, W_TH / 2 + 0.004), (0.080, 0.13, 0.010), 0.006)
_pan1b = box((0, B0 - 0.25, -W_TH / 2 - 0.004), (0.080, 0.15, 0.010), 0.006)
_pan2b = box((0, B1 + 0.20, -W_TH / 2 - 0.004), (0.080, 0.13, 0.010), 0.006)


def _door(P):
    d = _slab(P)
    for pn in (_pan1, _pan2, _pan1b, _pan2b):
        d = smax(d, -pn(P), 0.004)
    return d


wpart("door", _door, "wood")
SLOT_Y = B0 - 0.47
wpart("slot_frame", box((0, SLOT_Y, W_TH / 2), (0.070, 0.022, 0.006), 0.003), "brass")
for hy in (B0 - 0.12, B1 + 0.12):
    wpart(f"hinge_{hy:.2f}", box((W_WID / 2 + 0.002, hy, 0), (0.008, 0.035, W_TH / 2 + 0.004), 0.003), "hinge")
wpart("grip", cone((0, 0.080, 0), (0, G0, 0), 0.0175, 0.019), "grip")
wpart("pommel", ell((0, 0.092, 0), (0.024, 0.022, 0.024)), "brass", k=0.006)
wpart("rose", cyl((0, G0, 0), (0, 1, 0), 0.050, 0.008, 0.004), "brass")
wpart("knob_neck", cone((0.040, G0 - 0.004, 0.0), (0.072, G0 - 0.004, 0.022), 0.011, 0.010), "brass", k=0.006)
wpart("knob", ell((0.082, G0 - 0.004, 0.030), (0.030, 0.026, 0.030)), "brass", k=0.008)
W_PITCH, W_ROLL = 11.0, 12.0     # tip down toward the ground, flat face rolled toward the camera
W_R = rot((1, 0, 0), W_PITCH) @ rot((0, 1, 0), W_ROLL)


def weapon_d_local(P):
    return eval_w(P)


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
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    door = keys == "wood"
    # recessed panels darker, panel bevel, edge band
    inpan = door & (np.abs(x) < 0.083) & ((np.abs(y - (B0 - 0.25)) < 0.153) | (np.abs(y - (B1 + 0.20)) < 0.133)) & (np.abs(z) < W_TH / 2 + 0.001)
    c[inpan] = hexc("#C9A276")
    # wood grain stripes along the blade
    gr = door & (np.sin(x * 220 + np.sin(y * 18) * 1.5) > 0.75)
    c[gr] = c[gr] * 0.93
    # letter slot: dark opening inside the brass frame
    slot = (keys == "brass") & (np.abs(y - SLOT_Y) < 0.008) & (np.abs(x) < 0.055) & (z > W_TH / 2)
    c[slot] = hexc(PALW["slot"][0])
    g[slot] = 0.1
    # grip: peach tape wraps
    tape = (keys == "grip") & (np.sin(y * 160) > 0.2)
    c[tape] = hexc(PALW["tape"][0])
    return c, g


def build_weapon(scale):
    lo, hi = np.array([-0.15, B1 - 0.03, -0.05]), np.array([0.15, 0.13, 0.08])
    V, T = build_low(eval_w, 0.0035, lo, hi, BUDGET_WEAPON, "WrenWeapon")
    c, g = weapon_paint(V.astype(F32))
    Vw = (V @ W_R.T) * scale
    ob = to_mesh("WrenWeapon", Vw, T)
    me = ob.data
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    lin = np.hstack([srgb_to_lin(c), g[:, None]]).astype(np.float32)
    me.calc_loop_triangles()
    lv = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", lv)
    attr.data.foreach_set("color", lin[lv].ravel())
    me.color_attributes.active_color = attr
    mat = bpy.data.materials.get("FighterVC") or bpy.data.materials.new("FighterVC")
    mat.use_nodes = True   # vertex colour -> base colour, so the glTF exporter writes COLOR_0
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    vcn = nt.nodes.new("ShaderNodeVertexColor")
    vcn.layer_name = "Col"
    nt.links.new(vcn.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    tip = (np.array([0, B1, 0]) @ W_R.T) * scale
    log(f"weapon: {len(T)} tris, length {(0.10 - B1) * scale:.3f}, tip {np.round(tip, 3)}")
    return ob, len(T), tip


# =====================================================================================================================
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    shells = []
    for nm in ("head", "body"):
        parts = [p for p in PARTS if p.shell == nm]
        shells.append((nm, parts, (lambda pp: (lambda P: eval_parts(P, pp)))(parts)))
    lo_all, hi_all = (-0.26, -0.24, -0.005), (0.26, 0.20, 0.76)
    Vh, Th = mesh_field(fig_d, 0.004, lo_all, hi_all)
    lo, hi = Vh.min(0), Vh.max(0)
    shift = np.array([-(lo[0] + hi[0]) / 2 * 0, -(lo[1] + hi[1]) / 2 * 0, -lo[2]])
    scale = HEIGHT / (hi[2] - lo[2])
    log("bbox", np.round(lo, 3), np.round(hi, 3), "scale", round(scale, 4))
    Vs, Ts, sid, base = [], [], [], 0
    for k, (nm, parts, fn) in enumerate(shells):
        V, T = build_low(fn, 0.0024, lo_all, hi_all, BUDGET_HEAD if nm == "head" else BUDGET_BODY, nm)
        Vs.append(V)
        Ts.append(T + base)
        sid.append(np.full(len(V), k))
        base += len(V)
    V, T, sid = np.concatenate(Vs), np.concatenate(Ts), np.concatenate(sid)
    low = to_mesh("Wren", V, T)
    uv = unwrap(low, None)
    img = bake_texture(low, uv, sid, shells)
    tex_path = os.path.join(OUT_DIR, "wren_tex.png")
    im = bpy.data.images.new("wren_tex", TEX, TEX, alpha=True)
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
    # owner part per vertex within its shell -> weights
    pv = [None] * len(V)
    for k, (nm, parts, fn) in enumerate(shells):
        sel = np.nonzero(sid == k)[0]
        o = owner(V[sel].astype(F32), parts)
        for j, oi in zip(sel, o):
            pv[j] = parts[oi]
    W = vertex_weights(V, pv)
    ob, arm = make_object("Wren", V, T, uvc, W, img, tex_path, shift, scale)
    export([ob, arm], arm, os.path.join(OUT_DIR, "wren_game"))
    log(f"RESULT body tris {len(T)}/4000 verts {len(V)} max infl {max(len(w) for w in W)}")
    log("bones (export units):", {bn: tuple(np.round((np.asarray(hd) + shift) * scale, 3)) for bn, _, hd, _ in BONE_DEF})
    bpy.ops.wm.read_factory_settings(use_empty=True)
    wob, wtris, tip = build_weapon(scale)
    bpy.ops.object.select_all(action="DESELECT")
    export([wob], wob, os.path.join(OUT_DIR, "wren_weapon"))
    log(f"RESULT weapon tris {wtris}/1200")
    log("done")


main()
