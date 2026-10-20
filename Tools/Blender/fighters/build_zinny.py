# Zinny (id zinny, Docs/Roster-v4.md 4.3): 11-year-old girl gardener, two high "palm" pigtails with sunflower ties,
# butterfly goggles, mint overalls, polka-dot gloves, yellow rubber boots, watering-can backpack; weapon = toy hedge
# trimmer "Buzzy" with a chain of rubber daisies. Base model for Docs/Briefs/A-Roster3D.md (wave 2), pipeline as
# build_aiko.py / build_wren.py. Headless (Blender 5.2, numpy only):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_zinny.py -- [out_dir]
# Output (default Art/Source/3D/zinny/): zinny_game.fbx/.glb (<= 4000 tris, mesh "Zinny", material "FighterVC" with the
# texture, armature "Rig" with the 12 FighterBone bones), zinny_tex.png (1024 RGBA: RGB albedo + baked AO, A = gloss),
# zinny_weapon.fbx/.glb (<= 1200 tris, mesh "ZinnyWeapon", vertex colour "Col", origin = rear grip under the right hand,
# bar along -Y = forward, flat side up). Format as Theo: Z up, faces -Y, feet z = 0; FBX axis_forward=-Z, axis_up=Y.
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector

ID, NAME = "zinny", "Zinny"
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
POS = [a for a in ARGV if not a.startswith("--")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", ID)
os.makedirs(OUT_DIR, exist_ok=True)
T0 = time.time()
F32 = np.float32
BIG = F32(10.0)
HEIGHT = 0.99          # incl. the pigtails; body ~0.92 (11 years, Theo = 1.0 at 12)
TEX = 1024
BUDGET_HEAD, BUDGET_BODY, BUDGET_WEAPON = 1850, 2100, 1180
FACE_Z0, FACE_Z1 = 0.45, 0.62   # face band (model units) gets more texture area

LO_ALL, HI_ALL = (-0.30, -0.32, -0.005), (0.30, 0.26, 0.86)

def log(*a):
    print(f"[{ID} {time.time() - T0:6.1f}s]", *a, flush=True)


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
# palette (sRGB) and gloss. Roster-v4: mint-green #7FD6A0 (overalls, trimmer body), sunflower #FFCF3A (daisy centres, hair
# ties), white #FFFFFF (daisies, polka dots), glow #FFF6A8 (energy only, not used here). Own: skin, hair, shirt, boots.
# =====================================================================================================================
PAL = {
    "skin": ("#B97E58", 0.2), "hair": ("#3A2418", 0.4), "shirt": ("#FFF4DC", 0.12), "overall": ("#7FD6A0", 0.15),
    "glove": ("#FFCF3A", 0.3), "boot": ("#FFCF3A", 0.85), "sole": ("#C9962A", 0.4), "tie": ("#FFCF3A", 0.4),
    "goggle": ("#FFFFFF", 0.6), "lens": ("#9FE3D8", 0.95), "can": ("#5FC3AE", 0.75), "rose": ("#FFCF3A", 0.8),
}
PALW = {"body": ("#7FD6A0", 0.75), "bar": ("#DDF5E6", 0.6), "handle": ("#FFCF3A", 0.7), "daisy": ("#FFFFFF", 0.35),
        "grill": ("#4FA77A", 0.4)}

# =====================================================================================================================
# MODEL (model units; normalised to HEIGHT at export). Character's right = -X.
# =====================================================================================================================
SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}
HEAD_C = np.array([0, 0.0, 0.560])
HEAD_R = np.array([0.146, 0.134, 0.136])
SH = {s: np.array([0.080 * s, 0.004, 0.418]) for s in SIDES}
HAND = {-1: np.array([-0.030, -0.140, 0.292])}
HAND[1] = HAND[-1] + np.array([0.0, -0.100, 0.064])
WR = {-1: HAND[-1] + np.array([-0.020, 0.026, 0.004]), 1: HAND[1] + np.array([0.026, 0.020, -0.004])}
EL = {-1: np.array([-0.112, -0.050, 0.330]), 1: np.array([0.110, -0.110, 0.360])}
HIP = {s: np.array([0.044 * s, 0.004, 0.262]) for s in SIDES}
AN = {s: np.array([0.050 * s, 0.000, 0.075]) for s in SIDES}
EYE = {s: np.array([0.055 * s, -0.121, 0.548]) for s in SIDES}
TIE = {s: np.array([0.112 * s, 0.010, 0.690]) for s in SIDES}
CAN_C = np.array([0, 0.118, 0.355])


class Part:
    def __init__(self, name, f, key, bone, k=0.0, shell="body"):
        self.name, self.f, self.key, self.bone, self.k, self.shell = name, f, key, bone, k, shell


PARTS = []


def part(name, f, key, bone, k=0.0, shell="body"):
    PARTS.append(Part(name, f, key, bone, k, shell))


def torus(c, axis, R, r):
    c, ax = np.asarray(c, F32), np.asarray(nrm(axis), F32)

    def f(P):
        Q = P - c
        y = Q @ ax
        x = np.sqrt(np.maximum((Q * Q).sum(1) - y * y, 0))
        return (np.sqrt((x - R) ** 2 + y * y) - r).astype(F32)
    return f


# head: face, hair cap + side-swept fringe, two high palm pigtails with sunflower ties, butterfly goggles on the forehead
part("head", ell(HEAD_C, HEAD_R), "skin", "Head", shell="head")
part("cheeks", ell(HEAD_C + (0, -0.03, -0.045), (0.124, 0.102, 0.088)), "skin", "Head", k=0.03, shell="head")
_cap = ell(HEAD_C + (0, 0.012, 0.006), (0.155, 0.150, 0.150))
part("hair", lambda P: smax(_cap(P), (-0.040 - P[:, 1] - 0.25 * np.maximum(HEAD_C[2] + 0.02 - P[:, 2], 0)).astype(F32),
                            0.02), "hair", "Head", k=0.01, shell="head")
part("fringe", ell(HEAD_C + (0.030, -0.108, 0.078), (0.100, 0.044, 0.034), rot((0, 1, 0), -12)), "hair", "Head", k=0.012,
     shell="head")
for s in SIDES:
    t = TIE[s]
    part(f"tie_{SN[s]}", torus(t, (0.45 * s, 0, 1), 0.024, 0.011), "tie", "Head", shell="head")
    part(f"tail_root_{SN[s]}", cone(t - np.array([0.04 * s, 0, 0.03]), t, 0.026, 0.020), "hair", "Head", k=0.015,
         shell="head")
    for j, (out_deg, fb) in enumerate(((10, 0.0), (45, -0.5), (45, 0.6), (80, 0.1))):
        a = math.radians(out_deg)
        d = nrm((math.sin(a) * s, fb * 0.5 * math.sin(a), math.cos(a)))
        c0 = t + d * 0.062
        R = np.column_stack([nrm(np.cross(d, (0, 1, 0)) if abs(d[1]) < 0.9 else (1, 0, 0)), nrm(np.cross(d, np.cross(d, (0, 1, 0)))), d])
        part(f"palm_{SN[s]}{j}", ell(c0, (0.026, 0.020, 0.062), R), "hair", "Head", k=0.012, shell="head")
for s in SIDES:
    gc = HEAD_C + np.array([0.046 * s, -0.122, 0.090])
    part(f"goggle_{SN[s]}", ell(gc, (0.044, 0.020, 0.032), rot((0, 1, 0), -22 * s) @ rot((1, 0, 0), -20)), "goggle", "Head",
         k=0.004, shell="head")
    part(f"lens_{SN[s]}", ell(gc + (0.002 * s, -0.010, 0.002), (0.034, 0.014, 0.024), rot((0, 1, 0), -22 * s) @ rot((1, 0, 0), -20)),
         "lens", "Head", shell="head")
# body
part("neck", cone((0, 0.004, 0.41), (0, 0.004, 0.48), 0.031, 0.029), "skin", "Body", k=0.01)
part("torso", ell((0, 0.004, 0.352), (0.080, 0.064, 0.090)), "shirt", "Body", k=0.01)
part("bib", box((0, -0.050, 0.345), (0.052, 0.022, 0.058), 0.018), "overall", "Body", k=0.012)
part("pelvis", ell((0, 0.004, 0.262), (0.076, 0.062, 0.058)), "overall", "Root", k=0.02)
part("pocket", box((0, -0.062, 0.360), (0.030, 0.010, 0.020), 0.008), "overall", "Body", k=0.006)
# watering-can backpack
part("can", cyl(CAN_C, (0, 0, 1), 0.058, 0.062, 0.018), "can", "Body", k=0.01)
part("can_handle", lambda P: smax(torus(CAN_C + (0, 0, 0.062), (0, 1, 0), 0.036, 0.008)(P),
                                  (CAN_C[2] + 0.064 - P[:, 2]).astype(F32), 0.003), "can", "Body", k=0.004)
part("spout", cone(CAN_C + (0.040, 0.010, 0.010), CAN_C + (0.120, 0.020, 0.115), 0.016, 0.010), "can", "Body", k=0.01)
part("rose", cyl(CAN_C + (0.128, 0.021, 0.126), (0.6, 0.1, 0.8), 0.022, 0.008, 0.005), "rose", "Body", k=0.004)
for s in SIDES:
    part(f"sleeve_{SN[s]}", cone(SH[s], SH[s] + (EL[s] - SH[s]) * 0.4, 0.035, 0.031), "shirt", ("Arm", s), k=0.015)
    part(f"upper_{SN[s]}", cone(SH[s], EL[s], 0.026, 0.023), "skin", ("Arm", s), k=0.008)
    part(f"fore_{SN[s]}", cone(EL[s], WR[s], 0.023, 0.021), "skin", ("Arm", s), k=0.008)
    part(f"cuff_{SN[s]}", cone(WR[s] + (EL[s] - WR[s]) * 0.25, WR[s], 0.027, 0.026), "glove", ("Arm", s), k=0.006)
    part(f"hand_{SN[s]}", ell(HAND[s], (0.031, 0.032, 0.030)), "glove", ("Arm", s), k=0.012)
    part(f"leg_{SN[s]}", cone(HIP[s], AN[s] + (0, 0, 0.10), 0.040, 0.033), "overall", ("Leg", s), k=0.015)
    part(f"boot_{SN[s]}", cone(AN[s] + (0, 0, 0.105), AN[s] + (0, -0.004, 0.030), 0.040, 0.042), "boot", ("Leg", s), k=0.012)
    part(f"boot_toe_{SN[s]}", ell(AN[s] + (0, -0.032, -0.038), (0.043, 0.062, 0.038)), "boot", ("Leg", s), k=0.02)
    part(f"sole_{SN[s]}", lambda P, s=s: smax(ell(AN[s] + (0, -0.028, -0.030), (0.047, 0.069, 0.042))(P),
                                                (P[:, 2] - 0.013).astype(F32), 0.004), "sole", ("Leg", s), k=0.004)


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
    for s in SIDES:   # blush
        b = ell2(x, z, 0.080 * s, 0.500, 0.028, 0.016)
        c = mixc(c, hexc("#E07E62"), 0.35 * sstep(1.0, 0.3, b) * front)
    for s in SIDES:   # big lively eyes
        ex, ez = EYE[s][0], EYE[s][2]
        e = ell2(x, z, ex, ez, 0.026, 0.032)
        m = sstep(1.04, 0.96, e) * front
        c = mixc(c, hexc("#1E1410"), m)
        ir = ell2(x, z, ex, ez - 0.004, 0.019, 0.024)
        c = mixc(c, hexc("#6A3E1E"), sstep(1.0, 0.85, ir) * m)
        pu = ell2(x, z, ex, ez - 0.004, 0.010, 0.012)
        c = mixc(c, hexc("#140C08"), sstep(1.0, 0.8, pu) * m)
        h1 = ell2(x, z, ex - 0.008 * s - 0.004, ez + 0.011, 0.0075, 0.0085)
        h2 = ell2(x, z, ex + 0.008, ez - 0.012, 0.0035, 0.0035)
        c = mixc(c, np.ones(3), np.maximum(sstep(1.0, 0.7, h1), sstep(1.0, 0.6, h2)) * m)
        lash = (np.abs(ell2(x, z, ex, ez, 0.030, 0.036) - 1) < 0.09) & (z > ez + 0.01) & ((x - ex) * s > 0.0)
        c = mixc(c, hexc("#1E1410"), lash * front)
        g = np.where(m > 0.5, 1.0, g)
        bz = ez + 0.050 + 0.006 * (1 - ((x - ex) / 0.020) ** 2)    # lively arched brows
        br = (np.abs(z - bz) < 0.0045) & (np.abs(x - ex) < 0.019)
        c = mixc(c, hexc("#2A1810"), br * front)
    # big open grin with a row of top teeth
    top = 0.470
    lower = top - 0.028 * (1 - (x / 0.030) ** 2)
    mouth = (z < top) & (z > lower) & (np.abs(x) < 0.030)
    c = mixc(c, hexc("#6E2622"), mouth * front)
    teeth = mouth & (z > top - 0.008)
    c = mixc(c, hexc("#FFFBF2"), teeth * front)
    tongue = mouth & (z < top - 0.019) & (np.abs(x) < 0.013)
    c = mixc(c, hexc("#E57A73"), tongue * front)
    return c, g


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
    # hair: lighter streaks on the palm pigtails, goggle strap round the head
    hm = keys == "hair"
    palm = hm & np.char.startswith(names, "palm")
    c[palm & (np.sin((x + z) * 140) > 0.55)] = hexc("#5C3A24")
    strap = hm & (np.abs(z - (HEAD_C[2] + 0.095 - 0.10 * np.maximum(y, 0))) < 0.009) & ~np.char.startswith(names, "palm")
    c[strap] = hexc("#FFFFFF")
    # sunflower ties: brown seed centre ring on the inner side
    tm = keys == "tie"
    c[tm & (z > np.where(x > 0, TIE[1][2], TIE[-1][2]) + 0.008)] = hexc("#8A5A22")
    # overalls: shoulder straps over the shirt, yellow buttons, white stitches, pocket with a tiny daisy
    sm = keys == "shirt"
    for s in SIDES:
        st = sm & (np.abs(x - 0.040 * s) < 0.014) & (z > 0.38) & (names == "torso")
        c[st] = hexc(PAL["overall"][0])
        bt = (np.isin(names, ["bib"])) & (np.sqrt((x - 0.036 * s) ** 2 + (z - 0.395) ** 2) < 0.009)
        c[bt] = hexc("#FFCF3A")
        g[bt] = 0.8
    om = keys == "overall"
    stitch = om & np.isin(names, ["bib", "pocket"]) & (np.abs(np.abs(x) - 0.040) < 0.002) & (np.sin(z * 500) > 0)
    c[stitch] = hexc("#E9FFF1")
    pk = (names == "pocket")
    dd = np.sqrt(x ** 2 + (z - 0.360) ** 2)
    c[pk & (dd < 0.011)] = hexc("#FFFFFF")
    c[pk & (dd < 0.0045)] = hexc("#FFCF3A")
    # rolled cuffs of the overalls above the boots
    c[om & (z < 0.200) & (z > 0.180)] = hexc("#A6E6BE")
    # gloves: white polka dots
    gm = keys == "glove"
    hsh = np.sin(np.floor(x * 70) * 12.9898 + np.floor(y * 70) * 78.233 + np.floor(z * 70) * 37.719) * 43758.5453
    fx, fy, fz = (x * 70) % 1 - 0.5, (y * 70) % 1 - 0.5, (z * 70) % 1 - 0.5
    dot = gm & (fx ** 2 + fy ** 2 + fz ** 2 < 0.10) & ((hsh - np.floor(hsh)) > 0.2)
    c[dot] = hexc("#FFFFFF")
    # watering can: white daisy decal on the back, darker rim
    cm = keys == "can"
    dc = np.sqrt(x ** 2 + (z - CAN_C[2]) ** 2)
    ang = np.arctan2(z - CAN_C[2], x)
    petals = cm & (y > CAN_C[1] + 0.03) & (dc < 0.030 * (0.55 + 0.45 * np.abs(np.cos(3 * ang))))
    c[petals] = hexc("#FFFFFF")
    c[cm & (y > CAN_C[1] + 0.03) & (dc < 0.010)] = hexc("#FFCF3A")
    c[cm & (np.abs(z - (CAN_C[2] + 0.055)) < 0.006) & np.isin(names, ["can"])] = hexc("#3F9C88")
    c[(keys == "rose") & (np.sin(x * 900) * np.sin(z * 900) > 0.6)] = hexc("#C9962A")
    # boots: lighter rim
    bm_ = keys == "boot"
    c[bm_ & (z > 0.165)] = hexc("#FFE07A")
    fm = np.isin(names, ["head", "cheeks"])
    if fm.any():
        cc, gg = paint_face(c[fm], g[fm], P[fm].astype(float), N[fm])
        c[fm], g[fm] = cc, gg
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
    ff[cen[:, 2] > FACE_Z0] = 1.3
    face = (cen[:, 2] > FACE_Z0) & (cen[:, 2] < FACE_Z1) & (cen[:, 1] < -0.06) & (np.abs(cen[:, 0]) < 0.11)
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
    ("Root", None, (0, 0, 0), (0, 0, 0.262)),
    ("Body", "Root", (0, 0, 0.262), (0, 0, 0.440)),
    ("Head", "Body", (0, 0, 0.440), (0, 0, 0.72)),
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
                t = sstep(0.235, 0.195, float(v[2]))
                w = {"Leg" + SN[s]: 0.5 + 0.5 * t, "Root": 0.5 - 0.5 * t}
        elif bone == "Robe":     # skirt of the yukata: follows the legs a little near the hem
            s = 1 if v[0] > 0 else -1
            t = 0.55 * sstep(0.22, 0.10, float(v[2])) * sstep(0.0, 0.035, abs(float(v[0])))
            w = {"Leg" + SN[s]: t, "Root": 1 - t}
        elif bone == "Body":
            t = sstep(0.28, 0.32, float(v[2]))
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
# weapon: toy hedge trimmer "Buzzy". Local frame: origin = rear grip under the right hand, bar along -Y (forward), flat
# side up (+Z); the top loop handle (left hand) is at (0, -0.10, 0.064). Chain = rubber daisies round the bar edge.
# =====================================================================================================================
BAR_Y0, BAR_Y1, BAR_W, BAR_T = -0.150, -0.640, 0.050, 0.012
DAISIES = [(sx * 0.056, yy) for sx in (-1, 1) for yy in np.arange(-0.200, BAR_Y1 + 0.001, -0.054)]
DAISIES += [(BAR_W * 1.12 * math.cos(a), BAR_Y1 - BAR_W * 1.12 * math.sin(a)) for a in np.radians((45, 90, 135))]
WPARTS = []


def wpart(name, f, key, k=0.0):
    WPARTS.append(Part(name, f, key, "Prop", k))


wpart("grip", cone((0, 0.045, 0), (0, -0.035, 0), 0.0175, 0.019), "handle", k=0.004)
wpart("grip_end", ell((0, 0.050, 0), (0.022, 0.014, 0.022)), "handle", k=0.004)
wpart("motor", box((0, -0.100, 0.0), (0.044, 0.064, 0.038), 0.030), "body", k=0.012)
wpart("loop", lambda P: smax(torus((0, -0.100, 0.0), (1, 0, 0), 0.058, 0.011)(P), (0.012 - P[:, 2]).astype(F32), 0.004),
      "handle", k=0.006)
wpart("collar", cyl((0, -0.165, 0), (0, 1, 0), 0.040, 0.012, 0.008), "grill", k=0.006)
wpart("bar", lambda P: smin(box((0, (BAR_Y0 + BAR_Y1) / 2, 0), (BAR_W, (BAR_Y0 - BAR_Y1) / 2, BAR_T), 0.010)(P),
                            cyl((0, BAR_Y1, 0), (0, 0, 1), BAR_W, BAR_T, 0.010)(P), 0.01), "bar", k=0.006)
for i, (dx, dy) in enumerate(DAISIES):
    wpart(f"daisy_{i}", ell((dx, dy, 0), (0.022, 0.022, 0.010)), "daisy", k=0.0)
W_PITCH, W_ROLL = 6.0, 0.0
W_R = rot((1, 0, 0), W_PITCH)
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
    x, y, z = P[:, 0].astype(float), P[:, 1].astype(float), P[:, 2].astype(float)
    dm = keys == "daisy"
    D = np.array(DAISIES)
    d2 = (x[:, None] - D[None, :, 0]) ** 2 + (y[:, None] - D[None, :, 1]) ** 2
    near = np.argmin(d2, 1)
    r = np.sqrt(d2[np.arange(len(x)), near])
    ang = np.arctan2(y - D[near, 1], x - D[near, 0])
    c[dm & (r < 0.0085)] = hexc("#FFCF3A")
    c[dm & (r > 0.0085) & (np.cos(8 * ang) < -0.55)] = hexc("#E8EEE8")     # petal gaps
    mm = keys == "body"
    c[mm & (np.abs(x) > 0.030) & (np.sin(y * 260) > 0.3) & (np.abs(z) < 0.02)] = hexc(PALW["grill"][0])   # vents
    c[mm & (np.sqrt(x ** 2 + (y + 0.10) ** 2) < 0.012) & (z > 0.03)] = hexc("#FFCF3A")                   # power button
    bm_ = keys == "bar"
    c[bm_ & (np.abs(x) < 0.006) & (y < -0.20) & (y > BAR_Y1 + 0.02)] = hexc("#B9E6C9")                   # centre groove
    c[(keys == "handle") & (np.sin(y * 200) > 0.4) & (y > -0.04)] = hexc("#F0B520")
    return c, g

def build_weapon(scale):
    lo, hi = np.array([-0.10, BAR_Y1 - 0.09, -0.06]), np.array([0.10, 0.08, 0.10])
    V, T = build_low(eval_w, 0.0030, lo, hi, BUDGET_WEAPON, NAME + "Weapon")
    c, g = weapon_paint(V.astype(F32))
    Vw = (V @ W_R.T) * scale
    ob = to_mesh(NAME + "Weapon", Vw, T)
    me = ob.data
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    lin = np.hstack([srgb_to_lin(c), g[:, None]]).astype(np.float32)
    me.calc_loop_triangles()
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
    tip = (np.array([0, BAR_Y1 - BAR_W, 0]) @ W_R.T) * scale
    log(f"weapon: {len(T)} tris, length {(0.06 - BAR_Y1 + BAR_W) * scale:.3f}, tip {np.round(tip, 3)}")
    return ob, len(T), tip



# =====================================================================================================================
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    shells = []
    for nm in ("head", "body"):
        parts = [p for p in PARTS if p.shell == nm]
        shells.append((nm, parts, (lambda pp: (lambda P: eval_parts(P, pp)))(parts)))
    lo_all, hi_all = LO_ALL, HI_ALL
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
    low = to_mesh(NAME, V, T)
    uv = unwrap(low, None)
    img = bake_texture(low, uv, sid, shells)
    tex_path = os.path.join(OUT_DIR, ID + "_tex.png")
    im = bpy.data.images.new(ID + "_tex", TEX, TEX, alpha=True)
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
    ob, arm = make_object(NAME, V, T, uvc, W, img, tex_path, shift, scale)
    export([ob, arm], arm, os.path.join(OUT_DIR, ID + "_game"))
    log(f"RESULT body tris {len(T)}/4000 verts {len(V)} max infl {max(len(w) for w in W)}")
    log("bones (export units):", {bn: tuple(np.round((np.asarray(hd) + shift) * scale, 3)) for bn, _, hd, _ in BONE_DEF})
    bpy.ops.wm.read_factory_settings(use_empty=True)
    wob, wtris, tip = build_weapon(scale)
    bpy.ops.object.select_all(action="DESELECT")
    export([wob], wob, os.path.join(OUT_DIR, ID + "_weapon"))
    log(f"RESULT weapon tris {wtris}/1200")
    log("done")


main()
