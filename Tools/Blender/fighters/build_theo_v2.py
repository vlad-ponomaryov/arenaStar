# Theo v2, the starter shooter (Docs/Briefs/A-Theo3D.md step 2b): modelled from scratch, smooth "glossy toy figure" look.
# Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_theo_v2.py -- [out_dir] [--fast]
#   Tools/blender.ps1 -Script Tools/Blender/fighters/build_theo_v2.py
# Output (default Art/Source/3D/theo/): theo_v2_game.fbx/.glb (<= 4000 tris) and theo_v2_show.fbx/.glb (<= 20000 tris), both
# with the same armature (12 FighterBone bones), one mesh, one material. The blaster is v1's theo_blaster.fbx (not rebuilt).
# Format = build_theo.py / prepare_fighter.py: Blender Z up, faces -Y, height 1.0, feet on z = 0, centred on X/Y; FBX
# axis_forward=-Z, axis_up=Y, colors_type=SRGB; colour only in the "Col" attribute (corner, linear in Blender), alpha = gloss.
#
# How: every part is a signed distance field (smooth unions of ellipsoids / round cones / rounded boxes, armour = offset
# shells of the body cut by outlines traced from the front reference 001.png), meshed by numpy surface nets with Newton
# projection onto the exact surface (high-poly, smooth), faces buried inside other parts are removed, then each part is
# decimated (quadric collapse) to its share of the show / game budget. Colour per face from the nearest primitive's palette
# key, soft gradients (hair roots/tips, cheek blush) per vertex, ray-cast AO. Weights: rigid per part or a soft-min over the
# body primitives' bones (smooth shoulders, hips, waist), at most 2 influences.
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector, Matrix

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
sys.path.insert(0, os.path.join(here, "..", "env"))
from envcommon import ambient_occlusion, srgb_to_lin, hex_rgb  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FAST = "--fast" in argv
pos_args = [a for a in argv if not a.startswith("--")]
out_dir = os.path.abspath(pos_args[0]) if pos_args else os.path.join(repo, "Art", "Source", "3D", "theo")
os.makedirs(out_dir, exist_ok=True)
T0 = time.time()
log = lambda *a: print(f"[theo2 {time.time() - T0:6.1f}s]", *a, flush=True)

BUDGET = {"game": 4000, "show": 20000}
HSCALE = 1.6 if FAST else 1.0

# ---------- palette: sRGB hex, gloss (vertex alpha), AO weight ----------
PAL = {
    "red":         ("#B3141C", 0.75, 1.0),
    "red_dark":    ("#8A0E15", 0.60, 1.0),
    "gold":        ("#E0A93A", 0.88, 1.0),
    "gold_dark":   ("#B98228", 0.75, 1.0),
    "graphite":    ("#3D3836", 0.10, 0.6),
    "graphite_d":  ("#2E2A29", 0.15, 0.7),
    "belt":        ("#4A403B", 0.25, 1.0),
    "metal":       ("#A29C98", 0.80, 1.0),
    "headset":     ("#4C4846", 0.50, 1.0),
    "skin":        ("#F6C7A6", 0.18, 0.8),
    "mouth":       ("#9A4A3C", 0.25, 0.4),
    "hair":        ("#E3B04E", 0.35, 1.0),
    "brow":        ("#B07A36", 0.15, 0.5),
    "lash":        ("#3A2416", 0.30, 0.2),
    "eye_white":   ("#FFFFFF", 1.00, 0.3),
    "iris":        ("#8A5428", 1.00, 0.2),
    "iris_rim":    ("#4A2A14", 1.00, 0.2),
    "pupil":       ("#22160E", 1.00, 0.2),
    "spark":       ("#FFFFFF", 1.00, 0.0),
    "visor":       ("#FF9A2E", 1.00, 0.15),
    "visor_rim":   ("#E0741A", 0.85, 0.5),
    "visor_iris":  ("#D8661C", 1.00, 0.1),
    "visor_pupil": ("#8A3A12", 1.00, 0.1),
    "visor_spark": ("#FFF0D8", 1.00, 0.0),
    "visor_white": ("#FFC584", 1.00, 0.1),
    "glow":        ("#FFB040", 1.00, 0.15),
    "glow_hot":    ("#FFD47A", 1.00, 0.1),
    "cyan":        ("#46D3D6", 1.00, 0.2),
}
KEYS = list(PAL)
KEY = {k: i for i, k in enumerate(KEYS)}
SRGB = np.array([hex_rgb(PAL[k][0]) for k in KEYS])
GLOSS = np.array([PAL[k][1] for k in KEYS])
AOW = np.array([PAL[k][2] for k in KEYS])

F32 = np.float32
BIG = F32(1e3)
MARG = 0.03   # bbox margin of every primitive
THR = 0.06    # intersections / subtractions evaluate their cutter only where the base is closer than this


def nrm(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def axes(z_dir, y_hint=(0, 0, 1)):
    """3x3, columns = local x, y, z in world: z along z_dir, y as close as possible to y_hint."""
    z = nrm(z_dir)
    y = np.asarray(y_hint, float)
    y = y - z * y.dot(z)
    if np.linalg.norm(y) < 1e-6:
        y = np.array([1.0, 0, 0]) - z * z[0]
    y = nrm(y)
    return np.stack([np.cross(y, z), y, z], 1)


def rotz(deg):
    a = math.radians(deg)
    return np.array([[math.cos(a), -math.sin(a), 0], [math.sin(a), math.cos(a), 0], [0, 0, 1]])


def roty(deg):
    a = math.radians(deg)
    return np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])


def rotx(deg):
    a = math.radians(deg)
    return np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])


def PX(x, y):
    """Reference 001.png pixel (1024 px, figure 930 px tall, centre x 512, soles at y 975) -> model (X, Z)."""
    return ((x - 512) / 930.0, (975 - y) / 930.0)


# =====================================================================================================================
# SDF nodes. ev(P) -> (distance float32, palette key int16); negative inside. bb = world AABB (lo, hi).
# =====================================================================================================================
INF3 = (np.full(3, -np.inf), np.full(3, np.inf))


def inbb(P, bb, m):
    lo, hi = bb
    return np.all((P >= lo - m) & (P <= hi + m), axis=1)


class Node:
    bb = INF3
    bone = None

    def leaves(self):
        return []

    def d(self, P):
        return self.ev(np.asarray(P, F32))[0]


class Prim(Node):
    """Leaf: local = ((P - o) @ R) / s, f(local) * min(s)."""

    def __init__(self, key, bone, o=(0, 0, 0), R=None, s=None, ext=None):
        self.key = KEY[key] if key else -1
        self.bone = bone
        self.o = np.asarray(o, F32)
        self.R = None if R is None else np.asarray(R, F32)
        self.s = None if s is None else np.asarray(s, F32)
        if ext is not None:
            e = np.asarray(ext, float) * (1 if s is None else np.asarray(s, float))
            c = np.array([[sx, sy, sz] for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]) * e
            W = c @ (np.eye(3) if R is None else np.asarray(R, float)).T + np.asarray(o, float)
            self.bb = (W.min(0), W.max(0))

    def ev(self, P):
        Q = P - self.o
        if self.R is not None:
            Q = Q @ self.R
        if self.s is not None:
            Q = Q / self.s
        d = self.f(Q)
        if self.s is not None:
            d = d * self.s.min()
        return d.astype(F32), np.full(len(P), self.key, np.int16)

    def leaves(self):
        return [self] if self.bone else []


class Ell(Prim):
    def __init__(self, key, bone, c, r, R=None):
        super().__init__(key, bone, c, R, None, r)
        self.r = np.asarray(r, F32)

    def f(self, Q):
        k0 = np.linalg.norm(Q / self.r, axis=1)
        k1 = np.linalg.norm(Q / (self.r * self.r), axis=1)
        return k0 * (k0 - 1) / np.maximum(k1, 1e-6)


class Box(Prim):
    def __init__(self, key, bone, c, half, rad=0.0, R=None):
        super().__init__(key, bone, c, R, None, half)
        self.half = np.asarray(half, float)
        self.b = np.asarray(half, F32) - rad
        self.rad = rad

    def f(self, Q):
        q = np.abs(Q) - self.b
        return np.linalg.norm(np.maximum(q, 0), axis=1) + np.minimum(q.max(1), 0) - self.rad


class Cyl(Prim):
    """Rounded cylinder along local z (axis), radius r (+taper at +h, -taper at -h), half height h, edge radius rad."""

    def __init__(self, key, bone, c, axis, r, h, rad=0.0, taper=0.0, s=None, up=(0, 0, 1)):
        R = axes(axis, up if abs(np.dot(nrm(axis), up)) < 0.95 else (1, 0, 0))
        e = r + abs(taper)
        super().__init__(key, bone, c, R, None if s is None else (s[0], s[1], 1.0), (e, e, h))
        self.r, self.h, self.rad, self.t = r, h, rad, taper

    def f(self, Q):
        rr = self.r + self.t * np.clip(Q[:, 2] / self.h, -1, 1)
        dx = np.sqrt(Q[:, 0] ** 2 + Q[:, 1] ** 2) - (rr - self.rad)
        dz = np.abs(Q[:, 2]) - (self.h - self.rad)
        return np.minimum(np.maximum(dx, dz), 0) + np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dz, 0) ** 2) - self.rad


class Torus(Prim):
    def __init__(self, key, bone, c, axis, R_, r, s=None, up=(0, 0, 1)):
        R = axes(axis, up if abs(np.dot(nrm(axis), up)) < 0.95 else (1, 0, 0))
        super().__init__(key, bone, c, R, None if s is None else (s[0], s[1], 1.0), (R_ + r, R_ + r, r))
        self.R_, self.r = R_, r

    def f(self, Q):
        qx = np.sqrt(Q[:, 0] ** 2 + Q[:, 1] ** 2) - self.R_
        return np.sqrt(qx * qx + Q[:, 2] ** 2) - self.r


class Cone(Prim):
    """Round cone between world points a, b (radii ra, rb); optional flattening along unit n by factor f (< 1 = flat)."""

    def __init__(self, key, bone, a, b, ra, rb, n=None, f=1.0):
        super().__init__(key, bone)
        self.a, self.b = np.asarray(a, F32), np.asarray(b, F32)
        self.ra, self.rb = F32(ra), F32(rb)
        self.n = None if n is None else nrm(n).astype(F32)
        self.fl = F32(f)
        m = max(ra, rb)
        self.bb = (np.minimum(self.a, self.b) - m, np.maximum(self.a, self.b) + m)

    def ev(self, P):
        if self.n is not None and self.fl != 1:
            m = (self.a + self.b) * 0.5
            t = (P - m) @ self.n
            P = P + (t * (1.0 / self.fl - 1.0))[:, None] * self.n
        ba = self.b - self.a
        l2 = F32(ba @ ba)
        rr = self.ra - self.rb
        a2 = l2 - rr * rr
        il2 = 1.0 / l2
        pa = P - self.a
        y = pa @ ba
        z = y - l2
        x2v = pa * l2 - y[:, None] * ba
        x2 = np.einsum("ij,ij->i", x2v, x2v)
        y2 = y * y * l2
        z2 = z * z * l2
        k = np.sign(rr) * rr * rr * x2
        d3 = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - self.ra
        d1 = np.sqrt(x2 + z2) * il2 - self.rb
        d2 = np.sqrt(x2 + y2) * il2 - self.ra
        d = np.where(np.sign(z) * a2 * z2 > k, d1, np.where(np.sign(y) * a2 * y2 < k, d2, d3))
        if self.n is not None and self.fl != 1:
            d = d * self.fl
        return d.astype(F32), np.full(len(P), self.key, np.int16)


def chain(key, bone, pts, radii, n=None, f=1.0, k=0.0):
    """Polyline of round cones (union)."""
    segs = []
    for i in range(len(pts) - 1):
        nn = n[i] if (n is not None and np.ndim(n) == 2) else n
        segs.append(Cone(key, bone, pts[i], pts[i + 1], radii[i], radii[i + 1], nn, f))
    return U(*segs, k=k)


def poly2(px, pz, V):
    """Signed distance to a closed polygon V (n, 2) in the XZ plane (negative inside)."""
    d = (px - V[0, 0]) ** 2 + (pz - V[0, 1]) ** 2
    s = np.ones_like(px)
    n = len(V)
    for i in range(n):
        j = i - 1
        ex, ez = V[j, 0] - V[i, 0], V[j, 1] - V[i, 1]
        wx, wz = px - V[i, 0], pz - V[i, 1]
        t = np.clip((wx * ex + wz * ez) / (ex * ex + ez * ez), 0, 1)
        bx, bz = wx - ex * t, wz - ez * t
        d = np.minimum(d, bx * bx + bz * bz)
        c1 = pz >= V[i, 1]
        c2 = pz < V[j, 1]
        c3 = ex * wz > ez * wx
        flip = (c1 & c2 & c3) | (~c1 & ~c2 & ~c3)
        s = np.where(flip, -s, s)
    return s * np.sqrt(d)


def line2(px, pz, V, closed):
    d = np.full(px.shape, np.inf, F32)
    n = len(V)
    for i in range(n if closed else n - 1):
        j = (i + 1) % n
        ex, ez = V[j, 0] - V[i, 0], V[j, 1] - V[i, 1]
        wx, wz = px - V[i, 0], pz - V[i, 1]
        t = np.clip((wx * ex + wz * ez) / (ex * ex + ez * ez), 0, 1)
        d = np.minimum(d, (wx - ex * t) ** 2 + (wz - ez * t) ** 2)
    return np.sqrt(d)


class PolyXZ(Node):
    """Prism: polygon (model X, Z) extruded along Y."""

    def __init__(self, V):
        self.V = np.asarray(V, F32)
        lo, hi = self.V.min(0), self.V.max(0)
        self.bb = (np.array([lo[0], -np.inf, lo[1]]), np.array([hi[0], np.inf, hi[1]]))

    def ev(self, P):
        return poly2(P[:, 0], P[:, 2], self.V).astype(F32), np.full(len(P), -1, np.int16)


class BandXZ(Node):
    """|distance to polyline| - w in the XZ plane, extruded along Y."""

    def __init__(self, V, w, closed=True):
        self.V, self.w, self.closed = np.asarray(V, F32), w, closed
        lo, hi = self.V.min(0) - w, self.V.max(0) + w
        self.bb = (np.array([lo[0], -np.inf, lo[1]]), np.array([hi[0], np.inf, hi[1]]))

    def ev(self, P):
        return (line2(P[:, 0], P[:, 2], self.V, self.closed) - self.w).astype(F32), np.full(len(P), -1, np.int16)


class RRectXZ(Node):
    def __init__(self, c, half, rad, ang=0.0):
        self.c, self.h, self.rad, self.a = np.asarray(c, F32), np.asarray(half, F32), rad, math.radians(ang)
        e = float(max(half)) + 0.01
        self.bb = (np.array([c[0] - e, -np.inf, c[1] - e]), np.array([c[0] + e, np.inf, c[1] + e]))

    def ev(self, P):
        x, z = P[:, 0] - self.c[0], P[:, 2] - self.c[1]
        ca, sa = math.cos(self.a), math.sin(self.a)
        x, z = x * ca + z * sa, -x * sa + z * ca
        qx, qz = np.abs(x) - (self.h[0] - self.rad), np.abs(z) - (self.h[1] - self.rad)
        d = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qz, 0) ** 2) + np.minimum(np.maximum(qx, qz), 0) - self.rad
        return d.astype(F32), np.full(len(P), -1, np.int16)


class Half(Node):
    """Half space (P - p0) . n < 0 is inside."""

    def __init__(self, n, p0):
        self.n, self.p0 = nrm(n).astype(F32), np.asarray(p0, F32)

    def ev(self, P):
        return ((P - self.p0) @ self.n).astype(F32), np.full(len(P), -1, np.int16)


class Slab(Node):
    def __init__(self, n, c, half):
        self.n, self.c, self.h = nrm(n).astype(F32), np.asarray(c, F32), half

    def ev(self, P):
        return (np.abs((P - self.c) @ self.n) - self.h).astype(F32), np.full(len(P), -1, np.int16)


def _bb_union(bbs):
    return (np.min([b[0] for b in bbs], 0), np.max([b[1] for b in bbs], 0))


class U(Node):
    def __init__(self, *ch, k=0.0):
        self.ch, self.k = [c for c in ch if c is not None], k
        self.bb = _bb_union([c.bb for c in self.ch])

    def leaves(self):
        return [l for c in self.ch for l in c.leaves()]

    def ev(self, P):
        n = len(P)
        d = np.full(n, BIG, F32)
        key = np.full(n, -1, np.int16)
        for c in self.ch:
            m = inbb(P, c.bb, self.k + MARG)
            if not m.any():
                continue
            idx = np.nonzero(m)[0]
            dc, kc = c.ev(P[idx])
            a = d[idx]
            if self.k > 0:
                hh = np.maximum(self.k - np.abs(a - dc), 0) / self.k
                nd = np.minimum(a, dc) - hh * hh * self.k * 0.25
            else:
                nd = np.minimum(a, dc)
            key[idx] = np.where(dc < a, kc, key[idx])
            d[idx] = nd
        return d, key


class I(Node):
    """a intersected with every b (smooth k)."""

    def __init__(self, a, *bs, k=0.0):
        self.a, self.bs, self.k = a, bs, k
        lo, hi = a.bb
        for b in bs:
            lo, hi = np.maximum(lo, b.bb[0]), np.minimum(hi, b.bb[1])
        self.bb = (lo, hi)

    def leaves(self):
        return self.a.leaves()

    def ev(self, P):
        d, key = self.a.ev(P)
        for b in self.bs:
            inb = inbb(P, b.bb, MARG)
            sel = np.nonzero(inb & (d < THR))[0]
            if len(sel):
                db = b.ev(P[sel])[0]
                a = d[sel]
                if self.k > 0:
                    hh = np.maximum(self.k - np.abs(a - db), 0) / self.k
                    d[sel] = np.maximum(a, db) + hh * hh * self.k * 0.25
                else:
                    d[sel] = np.maximum(a, db)
            d[~inb] = BIG
        return d, key


class Sub(Node):
    def __init__(self, a, *bs, k=0.0):
        self.a, self.bs, self.k = a, bs, k
        self.bb = a.bb

    def leaves(self):
        return self.a.leaves()

    def ev(self, P):
        d, key = self.a.ev(P)
        for b in self.bs:
            sel = np.nonzero(inbb(P, b.bb, MARG + self.k) & (d < THR))[0]
            if len(sel):
                db = -b.ev(P[sel])[0]
                a = d[sel]
                if self.k > 0:
                    hh = np.maximum(self.k - np.abs(a - db), 0) / self.k
                    d[sel] = np.maximum(a, db) + hh * hh * self.k * 0.25
                else:
                    d[sel] = np.maximum(a, db)
        return d, key


class Off(Node):
    def __init__(self, ch, r):
        self.ch, self.r = ch, r
        self.bb = (ch.bb[0] - max(r, 0), ch.bb[1] + max(r, 0))

    def leaves(self):
        return self.ch.leaves()

    def ev(self, P):
        d, k = self.ch.ev(P)
        return d - F32(self.r), k


class Shell(Node):
    def __init__(self, ch, t):
        self.ch, self.t = ch, t
        self.bb = (ch.bb[0] - t, ch.bb[1] + t)

    def leaves(self):
        return self.ch.leaves()

    def ev(self, P):
        d, k = self.ch.ev(P)
        return np.abs(d) - F32(self.t), k


class Paint(Node):
    def __init__(self, ch, region, key):
        self.ch, self.rg, self.key = ch, region, KEY[key]
        self.bb = ch.bb

    def leaves(self):
        return self.ch.leaves()

    def ev(self, P):
        d, k = self.ch.ev(P)
        sel = np.nonzero(d < 0.01)[0]
        if len(sel):
            k[sel] = np.where(self.rg.ev(P[sel])[0] < 0, self.key, k[sel])
        return d, k


class Col(Node):
    """Same shape, one palette key."""

    def __init__(self, ch, key):
        self.ch, self.key = ch, KEY[key]
        self.bb = ch.bb

    def leaves(self):
        return self.ch.leaves()

    def ev(self, P):
        d, k = self.ch.ev(P)
        return d, np.full(len(P), self.key, np.int16)


class ExZ(Node):
    """Child's cross-section at z0 extruded over z in [zc - hz, zc + hz]."""

    def __init__(self, ch, z0, zc, hz):
        self.ch, self.z0, self.zc, self.hz = ch, z0, zc, hz
        self.bb = (np.array([ch.bb[0][0], ch.bb[0][1], zc - hz]), np.array([ch.bb[1][0], ch.bb[1][1], zc + hz]))

    def leaves(self):
        return self.ch.leaves()

    def ev(self, P):
        Q = P.copy()
        Q[:, 2] = self.z0
        d2, k = self.ch.ev(Q)
        dz = np.abs(P[:, 2] - self.zc) - self.hz
        d = np.minimum(np.maximum(d2, dz), 0) + np.sqrt(np.maximum(d2, 0) ** 2 + np.maximum(dz, 0) ** 2)
        return d.astype(F32), k


# ---------- surface helpers ----------
def grad(node, P, e=4e-4):
    P = np.asarray(P, F32)
    g = np.zeros_like(P)
    for i in range(3):
        dv = np.zeros(3, F32)
        dv[i] = e
        g[:, i] = (node.d(P + dv) - node.d(P - dv)) / (2 * e)
    return g


def stick(node, pts, off=0.0, it=6):
    """Newton-project points onto the iso-surface node = off."""
    P = np.asarray(pts, F32).reshape(-1, 3).copy()
    for _ in range(it):
        d = node.d(P) - off
        g = grad(node, P)
        P -= (g * (np.clip(d, -0.02, 0.02) / np.maximum((g * g).sum(1), 1e-6))[:, None]).astype(F32)
    return P


def trace(node, origin, direction, tmax=0.6):
    """First hit of a ray (sphere tracing with a safety factor; the fields are approximate)."""
    o, dv = np.asarray(origin, float), nrm(direction)
    t = 0.0
    for _ in range(600):
        p = o + dv * t
        d = float(node.d(p[None])[0])
        if d < 2e-5:
            return stick(node, p[None])[0].astype(float)
        t += min(max(d * 0.5, 1e-4), 0.01)
        if t > tmax:
            break
    raise RuntimeError(f"trace missed from {origin} along {direction}")


def normal(node, p):
    return nrm(grad(node, np.asarray(p, F32)[None])[0])


def front(node, x, z, side=-1):
    """Surface point seen from the front (side -1, from -Y) or the back (+1)."""
    return trace(node, (x, side * 0.5, z), (0, -side, 0))


def radial(node, z, az, c=(0, 0, 0)):
    """Surface point on the horizontal ray from far out toward the vertical axis through c; az from the front toward +X."""
    a = math.radians(az)
    dv = np.array([math.sin(a), -math.cos(a), 0.0])
    o = np.array([c[0], c[1], z]) + dv * 0.5
    return trace(node, o, -dv)


# =====================================================================================================================
# MODEL (Blender coords: Z up, faces -Y, +X = the character's left = the viewer's right in 001)
# =====================================================================================================================
SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}
GROUPS = []   # dict(name, node, h, w, bone)
MESHP = []    # fn(lod) -> list of (V, F(tris), fkey, weights)


def grp(name, node, h, w, bone, cull=True, boxes=False):
    """cull: this part hides faces of other parts buried inside it (off for thin surface details);
    boxes: the part is only rounded boxes -> the game LOD uses plain 12-triangle boxes instead of a decimated surface."""
    GROUPS.append(dict(name=name, node=node, h=h * HSCALE, w=w, bone=bone, cull=cull, boxes=boxes))


# ---------------- head ----------------
CRAN = Ell("skin", "Head", (0, 0.008, 0.834), (0.097, 0.110, 0.108))
JAW = Ell("skin", "Head", (0, -0.014, 0.772), (0.089, 0.090, 0.082))
CHIN = Ell("skin", "Head", (0, -0.068, 0.712), (0.034, 0.026, 0.022))
CHEEKS = [Ell("skin", "Head", (s * 0.050, -0.052, 0.748), (0.042, 0.038, 0.036)) for s in SIDES]
NOSE = U(Ell("skin", "Head", (0, -0.099, 0.770), (0.017, 0.016, 0.014)),
         Ell("skin", "Head", (0, -0.092, 0.786), (0.009, 0.012, 0.014)), k=0.01)
HEAD_LENS = U(CRAN, JAW, k=0.05)
EARS = []
for s in SIDES:
    Re = rotz(s * 18) @ roty(s * -8)
    ear = Ell("skin", "Head", (s * 0.110, 0.016, 0.778), (0.016, 0.024, 0.031), Re)
    concha = Ell("skin", None, (s * 0.124, 0.010, 0.776), (0.008, 0.014, 0.020), Re)
    EARS.append(Sub(ear, concha, k=0.006))
NECK = Cone("skin", "Head", (0, 0.012, 0.655), (0, 0.006, 0.735), 0.039, 0.040)
HEAD_BASE = U(U(U(CRAN, JAW, CHIN, k=0.05), *CHEEKS, k=0.045), NOSE, *EARS, NECK, k=0.02)
# eyes: rim centre on the face, gaze slightly outward
EYE = {}
for s in SIDES:
    p = front(HEAD_BASE, s * 0.044, 0.802)
    n = normal(HEAD_BASE, p)
    d = nrm(n + np.array([0, -1.8, 0]))
    EYE[s] = (p, d)
HEAD = HEAD_BASE
grp("head", HEAD, 0.0024, 13, "Head")

# eyes (mesh parts): dome with pupil / iris / rim / white bands + a spark
EYE_RX, EYE_RZ, EYE_H, EYE_SINK = 0.0255, 0.0285, 0.0105, 0.0035
EYE_U = [0.0, 0.13, 0.25, 0.36, 0.50, 0.56, 0.72, 0.88, 1.0]
EYE_COL = ["pupil", "pupil", "iris", "iris", "iris_rim", "eye_white", "eye_white", "eye_white"]


def lathe_arrays(prof, segs, M, keys, close_end=True):
    """prof [(rx, ry, z)]: rings around local z; first point may be the apex (r = 0). Returns V, tris, per-tri key."""
    V, Fc, K = [], [], []
    rings = []
    for (rx, ry, z) in prof:
        if rx < 1e-9:
            rings.append([len(V)])
            V.append((0, 0, z))
        else:
            r = []
            for j in range(segs):
                a = 2 * math.pi * j / segs
                r.append(len(V))
                V.append((rx * math.cos(a), ry * math.sin(a), z))
            rings.append(r)
    for b in range(len(rings) - 1):
        A, B = rings[b], rings[b + 1]
        for j in range(segs):
            q = (j + 1) % segs
            if len(A) == 1:
                Fc.append((A[0], B[j], B[q]))
                K.append(keys[b])
            else:
                Fc += [(A[j], B[j], B[q]), (A[j], B[q], A[q])]
                K += [keys[b], keys[b]]
    V = np.asarray(V, float) @ M[:3, :3].T + M[:3, 3]
    return V, np.asarray(Fc, np.int64), K


def frame4(o, z_dir, y_hint=(0, 0, 1)):
    M = np.eye(4)
    M[:3, :3] = axes(z_dir, y_hint)
    M[:3, 3] = o
    return M


def eye_parts(lod):
    segs = 16 if lod == "show" else 9
    us = EYE_U if lod == "show" else [0.0, 0.25, 0.50, 0.56, 0.80, 1.0]
    cols = EYE_COL if lod == "show" else ["pupil", "iris", "iris_rim", "eye_white", "eye_white"]
    out = []
    for s in SIDES:
        p, d = EYE[s]
        M = frame4(p - d * EYE_SINK, d)
        prof = [(u * EYE_RX, u * EYE_RZ, EYE_H * (1 - u * u)) for u in us]
        V, Fc, K = lathe_arrays(prof, segs, M, cols)
        out.append((V, Fc, K, [{"Eye" + SN[s]: 1.0}] * len(V)))
        # spark: small dome on the iris, up and to the outer side
        u, ang = 0.33, math.radians(55 if s < 0 else 125)
        lp = np.array([u * EYE_RX * math.cos(ang), u * EYE_RZ * math.sin(ang), EYE_H * (1 - u * u) + 0.0006])
        sp = M[:3, :3] @ lp + M[:3, 3]
        r = 0.0048
        V2, F2, K2 = lathe_arrays([(0, 0, 0.0016), (r * 0.6, r * 0.6, 0.0010), (r, r, 0.0)], 6 if lod == "show" else 5,
                                  frame4(sp, d), ["spark", "spark"])
        out.append((V2, F2, K2, [{"Eye" + SN[s]: 1.0}] * len(V2)))
    return out


MESHP.append(eye_parts)

# brows, lash line, mouth (stuck on the face)
for s in SIDES:
    lift = 0.006 if s > 0 else 0.0
    pts2 = [(s * 0.077, 0.833 + lift * 1.2), (s * 0.050, 0.847 + lift), (s * 0.017, 0.839 + lift * 0.4)]
    pts = np.array([front(HEAD_BASE, x, z) for x, z in pts2])
    pts = stick(HEAD_BASE, pts, 0.002)
    ns = [normal(HEAD_BASE, (pts[i] + pts[i + 1]) / 2) for i in range(2)]
    grp("brow_" + SN[s], chain("brow", "Head", pts, [0.0068, 0.0092, 0.0078], ns, 0.55, 0.004), 0.0011, 0.9, "Head", cull=False)
p, d = EYE[-1]
M = frame4(p - d * EYE_SINK, d)
lash = []
for a in np.linspace(math.radians(8), math.radians(172), 7):
    lash.append(M[:3, :3] @ np.array([math.cos(a) * EYE_RX * 0.92, math.sin(a) * EYE_RZ * 0.90, 0.0]) + M[:3, 3])
lash = stick(HEAD_BASE, np.array(lash), 0.0012)
grp("lash_R", chain("lash", "Head", lash, [0.0042, 0.0038, 0.0032, 0.0030, 0.0028, 0.0026, 0.0022], None, 1.0, 0.002),
    0.0009, 0.8, "Head", cull=False)
mouth2 = [(-0.030, 0.741), (-0.012, 0.735), (0.008, 0.735), (0.026, 0.740), (0.036, 0.747)]
mpts = stick(HEAD_BASE, np.array([front(HEAD_BASE, x, z) for x, z in mouth2]), -0.0004)
mn = [normal(HEAD_BASE, (mpts[i] + mpts[i + 1]) / 2) for i in range(len(mpts) - 1)]
grp("mouth", chain("mouth", "Head", mpts, [0.0024, 0.0034, 0.0036, 0.0032, 0.0022], mn, 0.6, 0.002), 0.0009, 0.6, "Head", cull=False)

# visor monocle on the character's left eye (+X), hinge in front of the left ear
LENS_RECT = RRectXZ((0.057, 0.801), (0.042, 0.028), 0.013, ang=-3)
lens_surf = Off(HEAD_LENS, 0.021)
LENS = Col(I(Shell(lens_surf, 0.0021), LENS_RECT, Half((0, 1, 0), (0, 0, 0))), "visor")
grp("visor_lens", LENS, 0.0011, 3.6, "Head")
rim2 = []
for i in range(28):  # rounded-rectangle outline of the lens
    a = 2 * math.pi * i / 28
    cx, cz = math.cos(a), math.sin(a)
    q = np.sign([cx, cz]) * np.abs([cx, cz]) ** 0.35
    ca, sa = math.cos(math.radians(-3)), math.sin(math.radians(-3))
    x, z = 0.0405 * q[0], 0.0265 * q[1]
    rim2.append((0.057 + x * ca - z * sa, 0.801 + x * sa + z * ca))
rim = np.array([front(lens_surf, x, z) for x, z in rim2])
rim = np.vstack([rim, rim[:1]])
grp("visor_rim", chain("visor_rim", "Head", rim, [0.0030] * len(rim), k=0.0015), 0.0010, 1.2, "Head")
lens_end = front(lens_surf, 0.097, 0.806)
HINGE = np.array([0.104, -0.022, 0.806])
grp("visor_arm", U(Cone("headset", "Head", lens_end, HINGE, 0.0036, 0.0036),
                   Cyl("gold", "Head", HINGE + [0.002, 0, 0], (1, 0, 0), 0.0105, 0.0042, 0.002)), 0.0011, 0.9, "Head")


def visor_eye(lod):
    """The eye seen through the lens, painted on its outer surface: tinted white, iris, pupil, spark."""
    p, d = EYE[1]
    q = trace(LENS, p + d * 0.06, -d)
    segs = 14 if lod == "show" else 8
    out = []
    Ax = axes(d)
    for c, prof, cols, lift in (
            (q, [(0, 0, 0), (0.0075, 0.0083, 0), (0.0160, 0.0176, 0), (0.0235, 0.0262, 0)],
             ["visor_pupil", "visor_iris", "visor_white"], 0.0007 if lod == "show" else 0.0013),
            (q + Ax[:, 0] * (-0.0058) + Ax[:, 1] * 0.0062, [(0, 0, 0), (0.0036, 0.0036, 0)], ["visor_spark"], 0.0011 if lod == "show" else 0.0017)):
        V, Fc, K = lathe_arrays(prof, segs if len(prof) > 2 else 6, frame4(c, d), cols)
        V = stick(LENS, V, lift).astype(float)
        out.append((V, Fc, K, [{"Head": 1.0}] * len(V)))
    return out


MESHP.append(visor_eye)

# headset on the right ear (-X): ear cup with a gold ring, boom along the cheek, mic capsule
cup_c = np.array([-0.112, -0.004, 0.772])
cup_ax = nrm((-1, -0.25, 0))
boom2 = [(-0.110, -0.030, 0.762), (-0.098, -0.058, 0.738), (-0.080, -0.082, 0.722), (-0.060, -0.094, 0.717)]
boom = stick(HEAD_BASE, np.array(boom2), 0.0075)
grp("headset", U(Cyl("headset", "Head", cup_c, cup_ax, 0.0205, 0.0065, 0.004),
                 Torus("gold", "Head", cup_c + cup_ax * 0.0062, cup_ax, 0.0150, 0.0028),
                 chain("headset", "Head", boom, [0.0034] * 4),
                 Ell("graphite_d", "Head", boom[-1] + np.array([0.004, -0.002, 0.0]), (0.0095, 0.0075, 0.0075), rotz(-25))),
    0.0011, 1.6, "Head")

# ---------------- hair: cap + feather locks (Bezier, flattened against the head, tapered to a point) ----------------
HAIR_C, HAIR_R = np.array([0.0, 0.008, 0.836]), np.array([0.109, 0.122, 0.120])
cap = I(Off(CRAN, 0.009), Half((0, -0.90, -1.0), (0, 0, 0.82)), k=0.008)
cap = Col(cap, "hair")


def hp(az, el, rr):
    a, e = math.radians(az), math.radians(el)
    return HAIR_C + rr * HAIR_R * np.array([math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)])


LOCKS = [  # (az, el, rr) root, control, tip; half-width; az 0 = front, +90 = +X (viewer's right)
    ((-48, 80, 0.90), (-52, 50, 1.08), (-68, 16, 1.02), 0.034),   # fringe: over the cap front, down onto the forehead
    ((-22, 82, 0.90), (-20, 50, 1.08), (-8, 13, 0.98), 0.037),
    ((4, 82, 0.90), (10, 50, 1.08), (26, 14, 0.98), 0.037),
    ((28, 80, 0.90), (40, 50, 1.08), (56, 18, 1.02), 0.034),
    ((55, 75, 0.90), (68, 48, 1.08), (84, 26, 1.10), 0.032),
    ((-20, 62, 0.90), (-10, 84, 1.20), (20, 86, 1.50), 0.032),    # crown, up
    ((35, 55, 0.90), (50, 76, 1.18), (78, 76, 1.46), 0.030),
    ((-60, 55, 0.90), (-66, 74, 1.18), (-84, 72, 1.44), 0.030),
    ((160, 62, 0.90), (150, 82, 1.16), (115, 84, 1.42), 0.032),
    ((100, 66, 0.90), (105, 80, 1.16), (130, 80, 1.38), 0.028),
    ((-85, 52, 0.90), (-98, 38, 1.16), (-104, 22, 1.44), 0.029),  # sides, tips out
    ((-100, 28, 0.90), (-104, 14, 1.10), (-108, 4, 1.34), 0.026),
    ((80, 52, 0.90), (92, 38, 1.15), (98, 24, 1.42), 0.029),
    ((98, 28, 0.90), (102, 14, 1.08), (104, 2, 1.28), 0.025),
    ((140, 42, 0.90), (128, 62, 1.15), (112, 72, 1.40), 0.032),   # back, swept up
    ((-140, 42, 0.90), (-128, 62, 1.15), (-112, 72, 1.40), 0.032),
    ((170, 18, 0.90), (165, 40, 1.12), (150, 56, 1.34), 0.032),
    ((-165, 18, 0.90), (-160, 40, 1.12), (-145, 56, 1.34), 0.031),
    ((120, 10, 0.92), (112, 28, 1.10), (104, 44, 1.32), 0.027),
    ((-120, 10, 0.92), (-112, 28, 1.10), (-104, 44, 1.32), 0.027),
    ((-150, 62, 0.90), (-140, 80, 1.15), (-120, 84, 1.38), 0.029),
]
LOCK_FLAT = 0.50


def lock(r, c, t, w, nseg=9):
    P0, P1, P2 = hp(*r), hp(*c), hp(*t)
    ts = np.linspace(0, 1, nseg + 1)
    pts = np.array([(1 - u) ** 2 * P0 + 2 * (1 - u) * u * P1 + u * u * P2 for u in ts])
    prof = (0.50 + 1.3 * ts) * (1 - ts) ** 1.35 / 0.57
    radii = np.maximum(w * prof, 0.0015)
    n = nrm(P1 - HAIR_C)  # one flattening axis per lock (per-segment axes crease the lock)
    return chain("hair", "Head", pts, radii, n, LOCK_FLAT, k=0.004)


HAIR = U(cap, *[lock(*L) for L in LOCKS], k=0.0)
grp("hair", HAIR, 0.0024, 17, "Head")

# ---------------- body suit (graphite): torso, arms, hands, legs ----------------
CHEST = Ell("graphite", "Body", (0, 0.004, 0.585), (0.096, 0.078, 0.100))
BELLY = Ell("graphite", "Body", (0, 0.000, 0.490), (0.094, 0.075, 0.075))
PELVIS = Ell("graphite", "Root", (0, 0.006, 0.405), (0.104, 0.078, 0.072))
NECK_S = Cone("graphite", "Body", (0, 0.006, 0.64), (0, 0.006, 0.705), 0.046, 0.043)
TORSO_P = U(CHEST, BELLY, k=0.04)
TORSO_B = U(BELLY, PELVIS, k=0.04)
SH = {s: np.array([s * 0.124, 0.004, 0.632]) for s in SIDES}
EL = {s: np.array([s * 0.188, 0.008, 0.522]) for s in SIDES}
WR = {s: np.array([s * 0.247, 0.000, 0.418]) for s in SIDES}
HIP = {s: np.array([s * 0.058, 0.004, 0.400]) for s in SIDES}
KN = {s: np.array([s * 0.097, -0.004, 0.250]) for s in SIDES}
AN = {s: np.array([s * 0.106, 0.004, 0.100]) for s in SIDES}
ARMS, LEGS, THIGH = [], [], {}
for s in SIDES:
    a = "Arm" + SN[s]
    ARMS += [Ell("graphite", "Body", SH[s] + [-s * 0.010, 0, 0], (0.046, 0.046, 0.046)),
             Cone("graphite", a, SH[s], EL[s], 0.036, 0.031), Cone("graphite", a, EL[s], WR[s], 0.031, 0.027)]
    THIGH[s] = Cone("graphite", "Leg" + SN[s], HIP[s], KN[s], 0.064, 0.048)
    LEGS += [THIGH[s], Cone("graphite", "Leg" + SN[s], KN[s], AN[s], 0.048, 0.042)]
SUIT = U(U(TORSO_P, PELVIS, NECK_S, k=0.04), *ARMS, *LEGS, k=0.022)
grp("suit", SUIT, 0.0038, 15, "leaves")

# hands: palm + 3 fingers + thumb, palm toward the thigh; gold plate on the back
HANDC = {}
for s in SIDES:
    a = "Arm" + SN[s]
    dn = nrm(nrm(WR[s] - EL[s]) + np.array([0, 0, -0.35]))
    inw = np.array([-s, 0.0, 0.0])
    inw = nrm(inw - dn * inw.dot(dn))
    fw = np.cross(dn, inw) * (-s)
    fw = nrm(fw if fw[1] < 0 else -fw)
    Rh = np.stack([inw, fw, dn], 1)  # local x = inward, y = forward, z = down
    pc = WR[s] + dn * 0.034
    HANDC[s] = pc
    parts = [Cone("graphite", a, WR[s] - dn * 0.006, pc, 0.026, 0.023),
             Box("graphite", a, pc, (0.0135, 0.0255, 0.026), 0.011, Rh)]
    for fo, ln in ((-0.0165, 0.92), (0.0, 1.0), (0.0165, 0.90)):
        b = pc + dn * 0.020 + fw * fo + inw * 0.001
        m = b + dn * 0.021 * ln + inw * 0.006 + fw * fo * 0.25
        t = m + dn * 0.016 * ln + inw * 0.012 + fw * fo * 0.15
        parts.append(chain("graphite", a, [b, m, t], [0.0094, 0.0090, 0.0082], k=0.004))
    tb = pc + fw * 0.019 + inw * 0.007 - dn * 0.006
    tm = tb + fw * 0.013 + dn * 0.016 + inw * 0.006
    tt = tm + fw * 0.003 + dn * 0.015 + inw * 0.010
    parts.append(chain("graphite", a, [tb, tm, tt], [0.0098, 0.0090, 0.0078], k=0.004))
    grp("hand_" + SN[s], U(*parts, k=0.006), 0.0018, 4.2, a)
    grp("glove_plate_" + SN[s], Box("gold", a, pc - inw * 0.0170 - dn * 0.003, (0.0045, 0.022, 0.020), 0.0042, Rh), 0.0014, 0.8, a, boxes=True)

# ---------------- chest plate (red) with gold trim, abdomen tongue, collar, emblem, back piece ----------------
CHEST_V = np.array([PX(*q) for q in [(437, 338), (470, 350), (512, 368), (554, 350), (587, 338), (625, 342), (628, 420),
                                       (618, 470), (604, 505), (580, 497), (555, 485), (530, 473), (512, 470), (494, 473),
                                       (469, 485), (444, 497), (420, 505), (406, 470), (396, 420), (399, 342)]])
TONGUE_V = np.array([PX(*q) for q in [(468, 478), (556, 478), (556, 552), (468, 552)]])
BACK_V = np.array([(-0.135, 0.705), (0.135, 0.705), (0.135, 0.508), (0.07, 0.515), (0.0, 0.522), (-0.07, 0.515), (-0.135, 0.508)])
FRONT_H, BACK_H = Half((0, 1, 0), (0, 0, 0)), Half((0, -1, 0), (0, 0, 0))
plate_region = U(I(PolyXZ(CHEST_V), FRONT_H), I(PolyXZ(BACK_V), BACK_H))
PLATE = Col(I(Off(TORSO_P, 0.015), plate_region, k=0.004), "red")
grp("plate", PLATE, 0.0028, 7, "Body")
grp("tongue", Col(I(Off(TORSO_P, 0.011), PolyXZ(TONGUE_V), FRONT_H, k=0.003), "red"), 0.0024, 1.4, "Body")
trim_band = U(I(BandXZ(CHEST_V, 0.0062), Half((0, 1, 0), (0, 0.004, 0))), I(BandXZ(BACK_V, 0.0062), Half((0, -1, 0), (0, -0.004, 0))))
grp("trim", Col(I(Off(TORSO_P, 0.0185), trim_band, k=0.0025), "gold"), 0.0019, 7, "Body")
COLLAR_C = np.array([0.0, 0.006, 0.684])
collar = Sub(Cyl("red", "Body", COLLAR_C, (0, 0, 1), 0.074, 0.024, 0.010, s=(1, 0.86)),
             Cyl("red", None, COLLAR_C, (0, 0, 1), 0.053, 0.05, 0.0, s=(1, 0.86)), k=0.004)
grp("collar", collar, 0.0022, 2.6, "Body")
grp("collar_rim", Torus("gold", "Body", COLLAR_C + [0, 0, 0.0225], (0, 0, 1), 0.0635, 0.0072, s=(1, 0.86)), 0.0015, 1.4, "Body")
# emblem: gold ball, gold comet tail up to +X, orange orbit ring
ec = front(PLATE, 0.003, 0.602)
en = normal(PLATE, ec)
eR = axes(en, (0, 0, 1))
ex, ey = eR[:, 0], eR[:, 1]
ball = ec + en * 0.007
ring_ax = nrm(ey * 1.0 + en * 0.95 - ex * 0.40)
tail = stick(PLATE, np.array([ball + ex * 0.012 + ey * 0.009, ball + ex * 0.030 + ey * 0.021, ball + ex * 0.050 + ey * 0.030]), 0.002)
grp("emblem", U(Ell("gold", "Body", ball, (0.0135, 0.0135, 0.0135)),
                chain("gold", "Body", [ball] + list(tail), [0.0105, 0.0095, 0.0065, 0.0015], en, 0.45),
                Torus("glow", "Body", ball, ring_ax, 0.0290, 0.0036)), 0.0011, 2.2, "Body")
bc_ = trace(PLATE, (0, 0.5, 0.615), (0, -1, 0))
grp("back_piece", U(Box("gold", "Body", bc_ + [0, 0.004, 0], (0.032, 0.012, 0.052), 0.008),
                    Box("red", "Body", bc_ + [0, 0.010, 0.002], (0.024, 0.010, 0.042), 0.007),
                    Box("gold", "Body", bc_ + [0, 0.012, -0.048], (0.012, 0.008, 0.006), 0.003)), 0.0016, 1.6, "Body", boxes=True)

# ---------------- shoulder pads (gold, two plates + rivets) ----------------
RIVETS = []


def rivet_parts(lod):
    segs = 10 if lod == "show" else 6
    out = []
    for p, n, w in RIVETS:
        r = 0.0070
        prof = [(0, 0, 0.0045), (r * 0.55, r * 0.55, 0.0036), (r * 0.9, r * 0.9, 0.0018), (r, r, -0.0015)]
        V, Fc, K = lathe_arrays(prof if lod == "show" else [prof[0], prof[2], prof[3]], segs, frame4(p, n), ["gold"] * 3)
        out.append((V, Fc, K, [dict(w)] * len(V)))
    return out


MESHP.append(rivet_parts)
for s in SIDES:
    c1 = np.array([s * 0.130, 0.004, 0.627])
    n1 = nrm((s * 0.45, 0, 0.9))
    c2 = np.array([s * 0.150, 0.004, 0.596])
    n2 = nrm((s * 1.0, 0, 0.22))
    b1 = Box("gold", "Body", c1, (0.070, 0.083, 0.062), 0.040, roty(s * 22))
    b2 = Box("gold", "Body", c2, (0.062, 0.076, 0.052), 0.034, roty(s * 48))
    pad1 = I(Shell(b1, 0.0048), Half(-n1, c1 - n1 * 0.004), k=0.004)
    pad2 = I(Shell(b2, 0.0048), Half(-n2, c2 + n2 * 0.020), Half((0, 0, 1), (0, 0, 0.630)), k=0.004)
    riv = []
    for node, c, dv in ((pad1, c1, (s * 0.40, -0.62, 0.66)), (pad2, c2, (s * 0.85, -0.45, -0.15))):
        p = trace(node, c + nrm(dv) * 0.3, -np.asarray(dv))
        RIVETS.append((p, normal(node, p), {"Body": 0.6, "Arm" + SN[s]: 0.4}))
    grp("pad_" + SN[s], U(pad1, pad2), 0.0022, 4.2, {"Body": 0.6, "Arm" + SN[s]: 0.4})

# ---------------- bracers (red, gold rims, cyan light) ----------------
for s in SIDES:
    a = "Arm" + SN[s]
    dv = nrm(WR[s] - EL[s])
    L = np.linalg.norm(WR[s] - EL[s])
    c = EL[s] + dv * L * 0.56
    h = L * 0.34
    br = Cyl("red", a, c, dv, 0.0395, h, 0.006, taper=0.0035)
    grp("bracer_" + SN[s], br, 0.0022, 3, a)
    grp("bracer_rims_" + SN[s], U(Cyl("gold", a, c - dv * h, dv, 0.0425, 0.0062, 0.003),
                                  Cyl("gold", a, c + dv * (h - 0.004), dv, 0.0465, 0.0085, 0.0035)), 0.0015, 2.2, a)
    out = np.array([s, 0, 0.0])
    out = nrm(out - dv * out.dot(dv))
    out = nrm(out + np.array([0, -0.7, 0]))
    gp = trace(br, c + dv * h * 0.45 + out * 0.2, -out)
    Rg = axes(out, dv)
    grp("gem_" + SN[s], U(Box("gold", a, gp + out * 0.0005, (0.0110, 0.0150, 0.0035), 0.0025, Rg),
                          Box("cyan", a, gp + out * 0.0030, (0.0075, 0.0110, 0.0030), 0.0022, Rg)), 0.0010, 0.9, a, boxes=True)

# ---------------- belt (graphite-brown band, boxes with orange lights, red buckle), pouches, strap ----------------
BELT_Z = 0.441
belt = Col(I(Off(TORSO_B, 0.014), Slab((0, 0, 1), (0, 0, BELT_Z), 0.026), k=0.005), "belt")
grp("belt", belt, 0.0024, 2.4, "Root")
boxes, lights = [], []
for az in (-52, -28, 28, 52, 150, -150, 180):
    p = radial(belt, BELT_Z, az)
    n = normal(belt, p)
    Rb = axes(n, (0, 0, 1))
    if az == 180:
        boxes.append(Box("graphite_d", "Root", p + n * 0.004, (0.022, 0.026, 0.008), 0.004, Rb))
        continue
    boxes.append(Box("graphite_d", "Root", p + n * 0.003, (0.0135, 0.0265, 0.0085), 0.0035, Rb))
    lights.append(Box("glow", "Root", p + n * 0.0105, (0.0060, 0.0135, 0.0030), 0.0022, Rb))
pb = radial(belt, BELT_Z, 0)
nb = normal(belt, pb)
Rb = axes(nb, (0, 0, 1))
boxes.append(Box("graphite_d", "Root", pb + nb * 0.003, (0.025, 0.020, 0.0075), 0.004, Rb))
grp("belt_boxes", U(*boxes), 0.0017, 3.0, "Root", boxes=True)
grp("belt_lights", U(*lights), 0.0011, 1.6, "Root", boxes=True)
grp("buckle", Box("red", "Root", pb + nb * 0.0095, (0.0175, 0.0125, 0.0040), 0.0028, Rb), 0.0011, 0.7, "Root", boxes=True)
for s in SIDES:
    p = radial(THIGH[s], 0.362, s * 90, c=(s * 0.07, 0, 0))
    n = normal(THIGH[s], p)
    Rp = axes(n, (0, 0, 1))
    grp("pouch_" + SN[s], U(Box("graphite_d", "Leg" + SN[s], p + n * 0.012, (0.028, 0.040, 0.014), 0.008, Rp),
                            Box("graphite_d", "Leg" + SN[s], p + n * 0.024 + Rp[:, 1] * 0.026, (0.029, 0.010, 0.004), 0.003, Rp)),
        0.0020, 1.6, "Leg" + SN[s], boxes=True)
grp("strap", Col(I(Off(THIGH[-1], 0.0045), Slab((-0.35, 0, 1), (-0.09, 0, 0.318), 0.0075), k=0.002), "graphite_d"), 0.0018, 0.8, "LegR")

# ---------------- glowing seams (orange piping) on the suit ----------------
def seam(name, pts, r=0.0040, w=1.0):
    P = stick(SUIT, np.asarray(pts, float), 0.0015)
    grp(name, chain("glow", "leaves", P, [r] * len(P), k=0.002), 0.0013, w, ("ref", SUIT), cull=False)


for s in SIDES:
    seam("seam_tongue_" + SN[s], [front(SUIT, s * 0.0505, z) for z in (0.535, 0.505, 0.466)], w=0.6)
    seam("seam_leg_" + SN[s], [front(SUIT, s * x, z) for x, z in ((0.052, 0.414), (0.056, 0.375), (0.068, 0.330),
                                                                   (0.084, 0.296), (0.096, 0.278))], w=1.0)
    d_ = nrm(EL[s] - SH[s])
    out = np.array([s, 0, 0.0])
    out = nrm(out - d_ * out.dot(d_))
    pts = [SH[s] + (EL[s] - SH[s]) * t + out * 0.05 for t in (0.45, 0.75, 1.0)]
    d2 = nrm(WR[s] - EL[s])
    out2 = nrm(out - d2 * out.dot(d2))
    pts += [EL[s] + (WR[s] - EL[s]) * 0.18 + out2 * 0.05]
    seam("seam_arm_" + SN[s], pts, w=0.9)

# ---------------- knee pads (gold, octagonal domes) ----------------
for s in SIDES:
    kc = KN[s] + np.array([s * 0.002, -0.024, -0.002])
    octo = I(Box(None, None, kc, (0.036, 0.3, 0.044)), Box(None, None, kc, (0.044, 0.3, 0.044), 0, roty(45)))
    grp("knee_" + SN[s], I(Ell("gold", "Leg" + SN[s], kc, (0.050, 0.038, 0.060)), octo, k=0.004), 0.0022, 2.0, "Leg" + SN[s])

# ---------------- boots (red), gold cuff with a front notch, instep strap, gold sole, glowing underside, ankle discs ----
for s in SIDES:
    lg = "Leg" + SN[s]
    x = s * 0.110
    shaft = Cyl("red", lg, (x, 0.004, 0.138), (0, 0, 1), 0.060, 0.068, 0.018, taper=0.004, s=(1, 1.08))
    toe = Ell("red", lg, (x, -0.050, 0.056), (0.061, 0.086, 0.053))
    heel = Ell("red", lg, (x, 0.036, 0.060), (0.058, 0.055, 0.058))
    foot = U(toe, heel, k=0.03)
    core = U(shaft, foot, k=0.035)
    boot = I(core, Half((0, 0, -1), (0, 0, 0.034)))
    grp("boot_" + SN[s], boot, 0.0026, 5, lg)
    cuff_core = Cyl("gold", lg, (x, 0.004, 0.138), (0, 0, 1), 0.060, 0.068, 0.0, taper=0.004, s=(1, 1.08))
    cuff = I(Off(cuff_core, 0.0075), Slab((0, 0, 1), (0, 0, 0.198), 0.0225), k=0.004)
    cuff = Sub(cuff, Box(None, None, (x, -0.075, 0.226), (0.016, 0.03, 0.016), 0.002, roty(45)), k=0.004)
    grp("cuff_" + SN[s], cuff, 0.0019, 2.0, lg)
    grp("instep_" + SN[s], Col(I(Off(foot, 0.0045), Slab((0, 0.55, 1), (x, -0.062, 0.088), 0.0075), Half((0, 1, 0), (0, -0.03, 0)),
                                 k=0.002), "gold"), 0.0016, 1.0, lg)
    sole2d = ExZ(foot, 0.056, 0.029, 0.015)
    grp("sole_" + SN[s], Col(I(Off(sole2d, 0.0065), Half((0, 0, 1), (0, 0, 0.044)), k=0.005), "gold"), 0.0022, 2.0, lg)
    grp("sole_glow_" + SN[s], Col(ExZ(Off(foot, -0.002), 0.056, 0.0085, 0.0085), "glow"), 0.0020, 0.8, lg)
    discs, hot = [], []
    for side in (-1, 1):
        p = trace(core, (x + side * 0.3, 0.004, 0.098), (-side, 0, 0))
        n = normal(core, p)
        discs.append(Cyl("gold", lg, p + n * 0.0015, n, 0.0195, 0.0050, 0.0028))
        hot.append(Cyl("glow_hot", lg, p + n * 0.0045, n, 0.0098, 0.0045, 0.0022))
    grp("discs_" + SN[s], U(*discs), 0.0012, 1.2, lg)
    grp("disc_glow_" + SN[s], U(*hot), 0.0010, 0.6, lg)

BONE_DEF = [  # name, parent, head, tail (before normalisation)
    ("Root", None, (0, 0, 0), (0, 0, 0.40)),
    ("Body", "Root", (0, 0, 0.40), (0, 0, 0.68)),
    ("Head", "Body", (0, 0, 0.68), (0, 0, 1.0)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", HANDC[-1], HANDC[-1] + np.array([0, -0.10, 0])),
    ("EyeR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("EyeL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("LidR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("LidL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
]
BONES = [b[0] for b in BONE_DEF]
log("model defined:", len(GROUPS), "sdf parts")


# =====================================================================================================================
# meshing: surface nets + Newton projection
# =====================================================================================================================
CORN = np.array([(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 1)])
EDG = [(a, b) for a in range(8) for b in range(a + 1, 8) if np.abs(CORN[a] - CORN[b]).sum() == 1]


def surface_nets(node, h):
    lo, hi = node.bb
    lo, hi = np.asarray(lo, float) - 3 * h, np.asarray(hi, float) + 3 * h
    lo, hi = np.maximum(lo, [-0.4, -0.4, -0.05]), np.minimum(hi, [0.4, 0.4, 1.1])
    n = np.ceil((hi - lo) / h).astype(int) + 1
    while np.prod(n) > 9e6:
        h *= 1.1
        n = np.ceil((hi - lo) / h).astype(int) + 1
    axs = [lo[i] + np.arange(n[i]) * h for i in range(3)]
    G = np.stack(np.meshgrid(*axs, indexing="ij"), -1).reshape(-1, 3).astype(F32)
    Fv = node.d(G).reshape(n)
    del G
    S = Fv < 0
    nx, ny, nz = n
    cnt = np.zeros((nx - 1, ny - 1, nz - 1), np.uint8)
    for c in CORN:
        cnt += S[c[0]:nx - 1 + c[0], c[1]:ny - 1 + c[1], c[2]:nz - 1 + c[2]]
    act = (cnt > 0) & (cnt < 8)
    ijk = np.argwhere(act)
    if len(ijk) == 0:
        return np.zeros((0, 3)), np.zeros((0, 4), np.int64)
    vid = np.full(act.shape, -1, np.int64)
    vid[act] = np.arange(len(ijk))
    acc = np.zeros((len(ijk), 3))
    num = np.zeros(len(ijk))
    for a, b in EDG:
        ia, ib = ijk + CORN[a], ijk + CORN[b]
        fa, fb = Fv[ia[:, 0], ia[:, 1], ia[:, 2]], Fv[ib[:, 0], ib[:, 1], ib[:, 2]]
        m = (fa < 0) != (fb < 0)
        t = fa[m] / (fa[m] - fb[m])
        acc[m] += CORN[a] + t[:, None] * (CORN[b] - CORN[a])
        num[m] += 1
    V = lo + (ijk + acc / num[:, None]) * h
    quads = []
    for ax in range(3):
        u, v = (ax + 1) % 3, (ax + 2) % 3
        sl0 = [slice(None)] * 3
        sl1 = [slice(None)] * 3
        sl0[ax] = slice(0, n[ax] - 1)
        sl1[ax] = slice(1, n[ax])
        e = S[tuple(sl0)] != S[tuple(sl1)]
        p = np.argwhere(e)
        p = p[(p[:, u] >= 1) & (p[:, u] <= n[u] - 2) & (p[:, v] >= 1) & (p[:, v] <= n[v] - 2)]
        if len(p) == 0:
            continue
        inside = S[p[:, 0], p[:, 1], p[:, 2]]
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
    # Newton projection onto the exact surface (step clipped to a cell)
    V = V.astype(F32)
    for _ in range(3):
        d = node.d(V)
        g = grad(node, V, e=h * 0.3)
        step = np.clip(d, -0.35 * h, 0.35 * h) / np.maximum((g * g).sum(1), 1e-4)
        V = V - (g * step[:, None]).astype(F32)
    return V.astype(float), Q


def to_mesh(name, V, Fc):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in V], [], [tuple(f) for f in Fc])
    me.validate(clean_customdata=False)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def mesh_arrays(ob):
    me = ob.data
    V = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", V)
    T = []
    for p in me.polygons:
        vs = list(p.vertices)
        for i in range(1, len(vs) - 1):
            T.append((vs[0], vs[i], vs[i + 1]))
    return V.reshape(-1, 3), np.asarray(T, np.int64).reshape(-1, 3)


def decimate(V, Q, target):
    ob = to_mesh("tmp", V, Q)
    # surface nets pinch thin features into non-manifold edges; quadric collapse stalls on them -> split them open
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bad = [e for e in bm.edges if not e.is_manifold and not e.is_boundary]
    if bad:
        bmesh.ops.split_edges(bm, edges=bad)
        bm.to_mesh(ob.data)
    bm.free()
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    for _ in range(4):  # collapse can stop short of the ratio on creased / open meshes: repeat
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        if tris <= target * 1.02:
            break
        m = ob.modifiers.new("Dec", "DECIMATE")
        m.ratio = target / tris
        m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=m.name)
    m = ob.modifiers.new("Tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=m.name)
    Vd, Td = mesh_arrays(ob)
    me = ob.data
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.meshes.remove(me)
    return Vd, Td


def compact(V, Fc):
    used = np.unique(Fc)
    remap = np.full(len(V), -1, np.int64)
    remap[used] = np.arange(len(used))
    return V[used], remap[Fc]


bpy.ops.wm.read_factory_settings(use_empty=True)
HI = []
for g in GROUPS:
    V, Q = surface_nets(g["node"], g["h"])
    g["V"], g["Q"] = V, Q
    g["mbb"] = (V.min(0), V.max(0)) if len(V) else (np.zeros(3), np.zeros(3))
log(f"surface nets: {sum(len(g['Q']) for g in GROUPS) * 2} tris in {len(GROUPS)} parts")

# remove faces buried inside other parts (their union surface stays closed)
for g in GROUPS:
    V, Q = g["V"], g["Q"]
    if len(Q) == 0:
        continue
    lo, hi = g["mbb"]
    buried = np.zeros(len(V), bool)
    for o in GROUPS:
        if o is g or not o["cull"]:
            continue
        olo, ohi = o["mbb"]
        if np.any(olo > hi + 0.002) or np.any(ohi < lo - 0.002):
            continue
        m = inbb(V, (olo, ohi), 0.002)
        idx = np.nonzero(m & ~buried)[0]
        if len(idx):
            buried[idx] |= o["node"].d(V[idx]) < -0.0008
    keep = ~buried[Q].all(1)
    g["V"], g["Q"] = compact(V, Q[keep])
    g["culled"] = int((~keep).sum())
log(f"after culling: {sum(len(g['Q']) for g in GROUPS) * 2} tris; most culled: "
    + ", ".join(f"{g['name']} {g['culled'] * 2}" for g in sorted(GROUPS, key=lambda g: -g['culled'])[:5]))

def components(nv, Q):
    """Connected component label per vertex (union-find)."""
    par = np.arange(nv)

    def find(x):
        r = x
        while par[r] != r:
            r = par[r]
        while par[x] != r:
            par[x], x = r, par[x]
        return r
    for i, j in np.concatenate([Q[:, [0, 1]], Q[:, [1, 2]], Q[:, [2, 3]] if Q.shape[1] > 3 else Q[:, [0, 2]]]):
        ri, rj = find(i), find(j)
        if ri != rj:
            par[ri] = rj
    return np.array([find(i) for i in range(nv)])


# drop loose islands (culling slivers, crumbs at thin tips): they cannot be decimated and waste the game budget
for g in GROUPS:
    if len(g["Q"]) == 0:
        continue
    lab = components(len(g["V"]), g["Q"])
    fl = lab[g["Q"][:, 0]]
    ids, cnt = np.unique(fl, return_counts=True)
    small = set(ids[cnt < max(40, 0.01 * cnt.max())].tolist())
    keep = np.array([l not in small for l in fl])
    if (~keep).any():
        g["V"], g["Q"] = compact(g["V"], g["Q"][keep])
    g["islands"] = (len(ids), len(small))
log("islands (total, dropped): " + ", ".join(f"{g['name']} {g['islands']}" for g in GROUPS if g.get("islands", (1, 0))[0] > 3))

# normalisation (height 1, feet on 0, centred on X/Y) from the high-poly union
allV = np.concatenate([g["V"] for g in GROUPS])
LO, HI_ = allV.min(0), allV.max(0)
SHIFT = np.array([-(LO[0] + HI_[0]) / 2, -(LO[1] + HI_[1]) / 2, -LO[2]])
SCALE = 1.0 / (HI_[2] - LO[2])
norm = lambda P: (np.asarray(P, float) + SHIFT) * SCALE
log("bounds", np.round(LO, 3), np.round(HI_, 3), "scale", round(SCALE, 4))


# =====================================================================================================================
# weights, colours, assembly per LOD
# =====================================================================================================================
def leaf_weights(node, V, sigma=0.010):
    leaves = node.leaves()
    names = sorted(set(l.bone for l in leaves))
    D = np.full((len(names), len(V)), 1e3)
    for l in leaves:
        i = names.index(l.bone)
        D[i] = np.minimum(D[i], l.d(V))
    D = np.maximum(D, 0)
    W = np.exp(-(D - D.min(0)) / sigma)
    out = []
    for j in range(len(V)):
        top = np.argsort(-W[:, j])[:2]
        w = {names[i]: W[i, j] for i in top if W[i, j] > 0.03}
        t = sum(w.values())
        out.append({k: v / t for k, v in w.items()})
    return out


def weights_for(g, V):
    b = g["bone"]
    if isinstance(b, str) and b == "leaves":
        return leaf_weights(g["node"], V)
    if isinstance(b, tuple) and b[0] == "ref":
        return leaf_weights(b[1], V)
    if isinstance(b, dict):
        return [dict(b)] * len(V)
    return [{b: 1.0}] * len(V)


def hair_tint(P, base):
    r = np.linalg.norm((P - HAIR_C) / HAIR_R, axis=1)
    t = np.clip((r - 0.95) / 0.40, 0, 1)
    dark, light = hex_rgb("#C08736"), hex_rgb("#F4D27A")
    lo = base + (dark - base) * (0.55 * (1 - np.clip(t * 2.5, 0, 1)))[:, None]
    return lo + (light - lo) * np.clip((t - 0.55) / 0.45, 0, 1)[:, None] * 0.85


def skin_tint(P, base):
    w = np.zeros(len(P))
    for s in SIDES:
        c = np.array([s * 0.058, -0.088, 0.752])
        w = np.maximum(w, np.exp(-((P - c) ** 2).sum(1) / (2 * 0.017 ** 2)))
    blush = hex_rgb("#F2A48C")
    return base + (blush - base) * (0.55 * w)[:, None]


TINT = {KEY["hair"]: hair_tint, KEY["skin"]: skin_tint}


CUBE_V = np.array([[x, y, z] for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)], float)
CUBE_F = np.array([(0, 1, 3), (0, 3, 2), (4, 6, 7), (4, 7, 5), (0, 4, 5), (0, 5, 1), (2, 3, 7), (2, 7, 6), (0, 2, 6), (0, 6, 4),
                   (1, 5, 7), (1, 7, 3)])


def box_parts(g):
    """Plain boxes for every Box leaf of the part (game LOD)."""
    out = []
    stack = [g["node"]]
    while stack:
        n = stack.pop()
        if isinstance(n, Box):
            R_ = np.eye(3) if n.R is None else n.R.astype(float)
            V = (CUBE_V * n.half) @ R_.T + n.o
            out.append((V, CUBE_F.copy(), [KEYS[n.key]] * 12))
        for attr in ("ch", "bs"):
            v = getattr(n, attr, None)
            if isinstance(v, (list, tuple)):
                stack += list(v)
            elif isinstance(v, Node):
                stack.append(v)
        if isinstance(getattr(n, "a", None), Node):
            stack.append(n.a)
    return out


def assemble(lod):
    budget = BUDGET[lod]
    parts = []
    for fn in MESHP:
        parts += fn(lod)
    sdf = GROUPS
    if lod == "game":
        sdf = [g for g in GROUPS if not g["boxes"]]
        for g in GROUPS:
            if g["boxes"]:
                for V, Fc, K in box_parts(g):
                    parts.append((V, Fc, K, weights_for(g, V)))
                g.setdefault("tris", {})["game"] = 12 * len(box_parts(g))
    fixed = sum(len(p[1]) for p in parts)
    wsum = sum(g["w"] for g in sdf)
    scale = 1.0
    for attempt in range(4):
        avail = (budget - fixed - 12) * scale
        res = []
        for g in sdf:
            tgt = max(10, int(avail * g["w"] / wsum))
            Vd, Td = decimate(g["V"], g["Q"], tgt)
            res.append((g, Vd, Td))
        total = fixed + sum(len(r[2]) for r in res)
        if total <= budget:
            break
        scale *= (budget - fixed - 12) / (total - fixed) * 0.995
        worst = sorted(res, key=lambda r: -(len(r[2]) - max(10, int(avail * r[0]["w"] / wsum))))[:4]
        log(f"{lod}: {total} tris over budget, rescale {scale:.3f}; over target: "
            + ", ".join(f"{r[0]['name']} {len(r[2])}/{max(10, int(avail * r[0]['w'] / wsum))}" for r in worst))
    Vs, Fs, Ks, Ws = [], [], [], []
    base = 0
    for g, Vd, Td in res:
        cen = Vd[Td].mean(1)
        k = g["node"].ev(cen.astype(F32))[1]
        fallback = next((l.key for l in g["node"].leaves() if l.key >= 0), KEY["graphite"])
        k = np.where(k < 0, fallback, k)
        Vs.append(Vd)
        Fs.append(Td + base)
        Ks.append(k)
        Ws += weights_for(g, Vd)
        base += len(Vd)
        g.setdefault("tris", {})[lod] = len(Td)
    for V, Fc, K, W in parts:
        Vs.append(V)
        Fs.append(np.asarray(Fc) + base)
        Ks.append(np.array([KEY[k] for k in K]))
        Ws += W
        base += len(V)
    V, Fc, K = np.concatenate(Vs), np.concatenate(Fs), np.concatenate(Ks)
    # drop degenerate / duplicate triangles (Blender's validate would remove them and break the face -> key mapping)
    area = np.linalg.norm(np.cross(V[Fc[:, 1]] - V[Fc[:, 0]], V[Fc[:, 2]] - V[Fc[:, 0]]), axis=1)
    ok = (Fc[:, 0] != Fc[:, 1]) & (Fc[:, 1] != Fc[:, 2]) & (Fc[:, 0] != Fc[:, 2]) & (area > 1e-11)
    _, first = np.unique(np.sort(Fc, 1), axis=0, return_index=True)
    uniq = np.zeros(len(Fc), bool)
    uniq[first] = True
    ok &= uniq
    if (~ok).any():
        log(f"{lod}: dropped {int((~ok).sum())} degenerate triangles")
    return V, Fc[ok], K[ok], Ws


def build_object(name, V, Fc, K, W, ao_rays=24, ao_dist=0.07, ao_strength=0.40):
    V = norm(V)
    ob = to_mesh(name, V, Fc)
    me = ob.data
    if len(me.polygons) != len(Fc):
        raise RuntimeError(f"{name}: validate removed faces ({len(me.polygons)} of {len(Fc)})")
    occl = ambient_occlusion(ob, rays=ao_rays, distance=ao_dist)
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    lf = np.repeat(np.arange(len(me.polygons)), [len(p.loop_indices) for p in me.polygons])
    kl = K[lf]
    srgb = SRGB[kl].copy()
    Pw = V[lv] / SCALE - SHIFT  # model space for the tints
    for key, fn in TINT.items():
        m = kl == key
        if m.any():
            srgb[m] = fn(Pw[m], SRGB[key])
    shade = 1.0 - ao_strength * AOW[kl] * occl[lv]
    col = np.clip(srgb * shade[:, None], 0, 1)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(col), GLOSS[kl][:, None]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    mat = bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.5
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    if hasattr(me, "set_sharp_from_angle"):
        me.set_sharp_from_angle(angle=math.radians(60))
    # armature
    arm_data = bpy.data.armatures.new(name + "Rig")
    arm = bpy.data.objects.new("Rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, parent, hd, tl in BONE_DEF:
        eb = arm_data.edit_bones.new(bn)
        eb.head, eb.tail = Vector(norm(hd)), Vector(norm(tl))
        if parent:
            eb.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    groups = {bn: ob.vertex_groups.new(name=bn) for bn in BONES}
    infl = np.zeros(len(W), int)
    by = {}
    for i, w in enumerate(W):
        for bn, val in w.items():
            by.setdefault((bn, round(val, 3)), []).append(i)
        infl[i] = len(w)
    for (bn, val), idx in by.items():
        groups[bn].add(idx, val, "REPLACE")
    ob.parent = arm
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return ob, arm, float(occl.mean()), int(infl.max()), int((infl > 1).sum())


def export(ob, arm, base):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)


results = {}
for lod in ("show", "game"):
    V, Fc, K, W = assemble(lod)
    ob, arm, ao, mi, blend = build_object("Theo", V, Fc, K, W, ao_rays=24 if lod == "game" else 16)
    base = os.path.join(out_dir, f"theo_v2_{lod}")
    export(ob, arm, base)
    tris = len(ob.data.polygons)
    results[lod] = tris
    log(f"RESULT {lod} tris {tris}/{BUDGET[lod]} {'OK' if tris <= BUDGET[lod] else 'OVER'} verts {len(ob.data.vertices)} "
        f"bones {len(BONES)} max infl {mi} blended verts {blend} ao mean occl {ao:.2f} -> {base}.fbx")
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.objects.remove(arm, do_unlink=True)
log("per part game/show tris: " + ", ".join(f"{g['name']} {g['tris'].get('game')}/{g['tris'].get('show')}" for g in GROUPS))
log("prop head (Prop bone) ", np.round(norm(HANDC[-1]), 3), "eyes", np.round(norm(EYE[-1][0]), 3), np.round(norm(EYE[1][0]), 3))

# ---------- round-trip check of the game FBX (what Unity reads) ----------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(out_dir, "theo_v2_game.fbx"), colors_type="SRGB")
m = next(o for o in bpy.context.scene.objects if o.type == "MESH")
a = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
dg = bpy.context.evaluated_depsgraph_get()
ev = m.evaluated_get(dg)
pts = np.array([tuple(ev.matrix_world @ vv.co) for vv in ev.to_mesh().vertices])
ca = m.data.color_attributes[0]
log("FBX check: bones", sorted(b.name for b in a.data.bones), "mesh height", round(float(pts[:, 2].max() - pts[:, 2].min()), 4),
    "x", np.round([pts[:, 0].min(), pts[:, 0].max()], 3), "colour attr", ca.name, ca.domain, ca.data_type,
    "materials", len(m.data.materials), "tris", len(m.data.polygons))
