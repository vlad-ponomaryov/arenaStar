# Elixa v2 (id "elixa"), Docs/Briefs/A-Roster3D.md wave 3: the base model of build_elixa.py brought to the Theo v3 level.
# Headless (Blender 5.2, numpy only):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_elixa_v2.py -- [out_dir] [--h 0.0025]
# Output (default Art/Source/3D/elixa/, same file names as v1): elixa_game.fbx/.glb (<= 4000 tris, mesh "Elixa", armature
# "Rig" with the FighterBone bones + Extra = the cat, <= 4 smooth weights per vertex, one material "FighterVC" -> elixa_tex.png),
# elixa_tex.png (1024 RGBA: RGB = albedo with baked AO / light gradients / decals, A = gloss), elixa_weapon.fbx/.glb (mesh
# "ElixaFlask" <= 1200 tris, origin = the right-hand grip, forward -Y, empty "Muzzle" at the flask front, material
# "FighterVC" -> elixa_weapon_tex.png 512).
# Method = build_theo_v3.py (its SDF nodes, surface nets meshing, per-shell quadric decimation, smart UV, procedural paint
# baked on the exact surface, smooth weights): copied below, then Elixa's own model and paint. Units are final: no height
# normalisation (with the hat the figure is ~1.2 tall, the body ~0.94 like Theo); only the feet are moved to z = 0.
# Format: Blender Z up, faces -Y, FBX axis_forward=-Z, axis_up=Y, FBX_SCALE_ALL, no leaf bones, vertex colour "Col".
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HRES = float(ARGV[ARGV.index("--h") + 1]) if "--h" in ARGV else 0.0025
POS = [a for i, a in enumerate(ARGV) if not a.startswith("--") and (i == 0 or ARGV[i - 1] != "--h")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "elixa")
os.makedirs(OUT_DIR, exist_ok=True)
T0 = time.time()


def log(*a):
    print(f"[elixa2 {time.time() - T0:6.1f}s]", *a, flush=True)


F32 = np.float32
BIG = F32(10.0)
TEX = 1024
MESH_NAME = "Elixa"
BOUNDS = (np.array([-0.36, -0.30, -0.012]), np.array([0.36, 0.30, 1.25]))

# =====================================================================================================================
# small math
# =====================================================================================================================
def nrm(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def frame(z_dir, y_hint=(0, 0, 1)):
    """3x3, columns = local x, y, z in world: z along z_dir, y as close as possible to y_hint."""
    z = nrm(z_dir)
    y = np.asarray(y_hint, float) - z * np.dot(y_hint, z)
    if np.linalg.norm(y) < 1e-6:
        y = np.array([1.0, 0, 0]) - z * z[0]
    y = nrm(y)
    return np.stack([np.cross(y, z), y, z], 1)


def rot(axis, deg):
    a = math.radians(deg)
    x, y, z = nrm(axis)
    c, s, C = math.cos(a), math.sin(a), 1 - math.cos(a)
    return np.array([[c + x * x * C, x * y * C - z * s, x * z * C + y * s],
                     [y * x * C + z * s, c + y * y * C, y * z * C - x * s],
                     [z * x * C - y * s, z * y * C + x * s, c + z * z * C]])


def mirror(p, s):
    """Point given for the character's right side (-X) mirrored to side s (-1 right, +1 left)."""
    p = np.array(p, float)
    p[0] = abs(p[0]) * s
    return p


def bez(P0, P1, P2, P3, n):
    t = np.linspace(0, 1, n)[:, None]
    P0, P1, P2, P3 = (np.asarray(p, float) for p in (P0, P1, P2, P3))
    return (1 - t) ** 3 * P0 + 3 * (1 - t) ** 2 * t * P1 + 3 * (1 - t) * t * t * P2 + t ** 3 * P3


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


# =====================================================================================================================
# SDF nodes: d(P) -> float32 distances (negative inside). lo/hi = bounding box of the solid (d > 0 outside it).
# =====================================================================================================================
class Node:
    lo = np.full(3, -1e9)
    hi = np.full(3, 1e9)
    bone = None

    def leaves(self):
        return []

    def kids(self):
        return []


def _inbb(P, lo, hi, m):
    return np.all((P >= lo - m) & (P <= hi + m), axis=1)


class Leaf(Node):
    def leaves(self):
        return [self] if self.bone else []


class Ell(Leaf):
    def __init__(self, c, r, R=None, bone=None):
        self.c, self.r = np.asarray(c, F32), np.asarray(r, F32)
        self.R = None if R is None else np.asarray(R, F32)
        Rf = np.eye(3) if R is None else np.asarray(R, float)
        e = np.sqrt(((Rf * np.asarray(r, float)[None, :]) ** 2).sum(1))
        self.lo, self.hi, self.bone = np.asarray(c) - e, np.asarray(c) + e, bone

    def d(self, P):
        Q = P - self.c
        if self.R is not None:
            Q = Q @ self.R
        k0 = np.sqrt(((Q / self.r) ** 2).sum(1))
        k1 = np.sqrt(((Q / (self.r * self.r)) ** 2).sum(1))
        return (k0 * (k0 - 1) / np.maximum(k1, 1e-9)).astype(F32)


class RBox(Leaf):
    def __init__(self, c, half, rad=0.0, R=None, bone=None):
        self.c, self.b, self.rad = np.asarray(c, F32), np.asarray(half, F32) - F32(rad), F32(rad)
        self.half = np.asarray(half, float)
        self.R = None if R is None else np.asarray(R, F32)
        Rf = np.eye(3) if R is None else np.asarray(R, float)
        e = np.abs(Rf) @ self.half
        self.lo, self.hi, self.bone = np.asarray(c) - e, np.asarray(c) + e, bone

    def d(self, P):
        Q = P - self.c
        if self.R is not None:
            Q = Q @ self.R
        q = np.abs(Q) - self.b
        return (np.sqrt((np.maximum(q, 0) ** 2).sum(1)) + np.minimum(q.max(1), 0) - self.rad).astype(F32)


class RCone(Leaf):
    """Exact round cone between points a, b with radii ra, rb (Quilez)."""

    def __init__(self, a, b, ra, rb, bone=None):
        self.a, self.b = np.asarray(a, F32), np.asarray(b, F32)
        self.ra, self.rb = F32(ra), F32(rb)
        m = max(ra, rb)
        self.lo, self.hi, self.bone = np.minimum(a, b) - m, np.maximum(a, b) + m, bone

    def d(self, P):
        ba = self.b - self.a
        l2 = F32(ba @ ba)
        rr = self.ra - self.rb
        a2 = l2 - rr * rr
        il2 = F32(1.0) / l2
        pa = P - self.a
        y = pa @ ba
        z = y - l2
        x2v = pa * l2 - y[:, None] * ba
        x2 = (x2v * x2v).sum(1)
        y2 = y * y * l2
        z2 = z * z * l2
        k = np.sign(rr) * rr * rr * x2
        d3 = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - self.ra
        d1 = np.sqrt(x2 + z2) * il2 - self.rb
        d2 = np.sqrt(x2 + y2) * il2 - self.ra
        return np.where(np.sign(z) * a2 * z2 > k, d1, np.where(np.sign(y) * a2 * y2 < k, d2, d3)).astype(F32)


class RCyl(Leaf):
    """Rounded cylinder: centre c, axis, radius r (taper: +t at +h end), half height h, edge radius rad, xy scale."""

    def __init__(self, c, axis, r, h, rad=0.0, taper=0.0, sxy=(1.0, 1.0), up=(0, 0, 1), bone=None):
        up = up if abs(np.dot(nrm(axis), up)) < 0.95 else (1, 0, 0)
        self.R = frame(axis, up).astype(F32)
        self.c, self.r, self.h, self.rad, self.t = np.asarray(c, F32), F32(r), F32(h), F32(rad), F32(taper)
        self.s = np.asarray([sxy[0], sxy[1]], F32)
        e = np.abs(self.R) @ np.array([(r + abs(taper)) * sxy[0], (r + abs(taper)) * sxy[1], h])
        self.lo, self.hi, self.bone = np.asarray(c) - e, np.asarray(c) + e, bone

    def d(self, P):
        Q = (P - self.c) @ self.R
        rr = self.r + self.t * np.clip(Q[:, 2] / self.h, -1, 1)
        rho = np.sqrt((Q[:, 0] / self.s[0]) ** 2 + (Q[:, 1] / self.s[1]) ** 2) * self.s.min()
        dx = rho - (rr - self.rad)
        dz = np.abs(Q[:, 2]) - (self.h - self.rad)
        return (np.minimum(np.maximum(dx, dz), 0) + np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dz, 0) ** 2)
                - self.rad).astype(F32)


class Torus(Leaf):
    def __init__(self, c, axis, R_, r, sxy=(1.0, 1.0), bone=None):
        self.R = frame(axis, (0, 0, 1) if abs(nrm(axis)[2]) < 0.95 else (1, 0, 0)).astype(F32)
        self.c, self.R_, self.r = np.asarray(c, F32), F32(R_), F32(r)
        self.s = np.asarray(sxy, F32)
        e = np.abs(self.R) @ np.array([(R_ + r) * sxy[0], (R_ + r) * sxy[1], r])
        self.lo, self.hi, self.bone = np.asarray(c) - e, np.asarray(c) + e, bone

    def d(self, P):
        Q = (P - self.c) @ self.R
        qx = np.sqrt((Q[:, 0] / self.s[0]) ** 2 + (Q[:, 1] / self.s[1]) ** 2) - self.R_
        return (np.sqrt(qx * qx + Q[:, 2] ** 2) - self.r).astype(F32)


class Loft(Leaf):
    """Vertical loft: at height z the section is the superellipse |u/a|^n + |v/b|^n = 1 centred at (cx, cy), with
    a = half width (X), b = (yb - yf) / 2, cy = (yf + yb) / 2. Tables over increasing z; ends close smoothly because the
    first / last rows are small. Distance = 2-D first-order estimate (r - 1) / |grad r| (sign exact)."""

    def __init__(self, rows, bone=None):
        R = np.asarray(rows, float)  # z, a, cx, yf, yb, n
        self.z, self.a, self.cx, self.yf, self.yb, self.n = (R[:, i].astype(F32) for i in range(6))
        self.lo = np.array([(R[:, 2] - R[:, 1]).min(), R[:, 3].min(), R[:, 0].min()])
        self.hi = np.array([(R[:, 2] + R[:, 1]).max(), R[:, 4].max(), R[:, 0].max()])
        self.bone = bone

    def d(self, P):
        z = np.clip(P[:, 2], self.z[0], self.z[-1])
        a = np.interp(z, self.z, self.a)
        cx = np.interp(z, self.z, self.cx)
        yf = np.interp(z, self.z, self.yf)
        yb = np.interp(z, self.z, self.yb)
        n = np.interp(z, self.z, self.n)
        b = (yb - yf) * 0.5
        u = np.abs(P[:, 0] - cx) / a
        v = np.abs(P[:, 1] - (yf + yb) * 0.5) / b
        un, vn = np.power(u + 1e-9, n), np.power(v + 1e-9, n)
        r = np.power(un + vn, 1.0 / n)
        # grad r = r^(1-n) * (u^(n-1)/a, v^(n-1)/b)
        rp = np.power(r + 1e-9, 1 - n)
        gu, gv = rp * un / (u + 1e-9) / a, rp * vn / (v + 1e-9) / b
        d2 = (r - 1) / np.maximum(np.sqrt(gu * gu + gv * gv), 1e-6)
        dz = np.maximum(self.z[0] - P[:, 2], P[:, 2] - self.z[-1])
        return np.where(dz > 0, np.sqrt(np.maximum(d2, 0) ** 2 + dz * dz) + np.minimum(d2, 0), d2).astype(F32)


class Ribbon(Leaf):
    """Swept ellipse along a polyline: per point centre C, width axis W, thickness axis T, half width w, half thickness t.
    Each segment is an elliptic capsule (ellipsoid-style distance in its local frame, rounded ends)."""

    def __init__(self, C, W, T, w, t, bone=None):
        self.C = np.asarray(C, F32)
        self.W, self.T = np.asarray(W, F32), np.asarray(T, F32)
        self.w, self.t = np.maximum(np.asarray(w, F32), 4e-4), np.maximum(np.asarray(t, F32), 4e-4)
        m = float(max(self.w.max(), self.t.max()))
        self.lo, self.hi, self.bone = self.C.min(0) - m, self.C.max(0) + m, bone
        segs = []
        for i in range(len(C) - 1):
            mm = float(max(self.w[i], self.w[i + 1], self.t[i], self.t[i + 1]))
            segs.append((np.minimum(self.C[i], self.C[i + 1]) - mm, np.maximum(self.C[i], self.C[i + 1]) + mm))
        self.sbb = segs
        self.k = F32(1.00 * float(np.median(self.t)))

    def _seg(self, i, Q):
        A, B = self.C[i], self.C[i + 1]
        ab = B - A
        L2 = F32(ab @ ab)
        tt = np.clip(((Q - A) @ ab) / L2, 0, 1)
        Cq = A + tt[:, None] * ab
        Wq = self.W[i] + (self.W[i + 1] - self.W[i]) * tt[:, None]
        Tq = self.T[i] + (self.T[i + 1] - self.T[i]) * tt[:, None]
        wq = self.w[i] + (self.w[i + 1] - self.w[i]) * tt
        tq = self.t[i] + (self.t[i + 1] - self.t[i]) * tt
        D = Q - Cq
        x = (D * Wq).sum(1) / np.sqrt((Wq * Wq).sum(1))
        y = (D * Tq).sum(1) / np.sqrt((Tq * Tq).sum(1))
        zl = np.sqrt(np.maximum((D * D).sum(1) - x * x - y * y, 0))
        e = np.minimum(wq, tq)
        k0 = np.sqrt((x / wq) ** 2 + (y / tq) ** 2 + (zl / e) ** 2)
        k1 = np.sqrt((x / wq ** 2) ** 2 + (y / tq ** 2) ** 2 + (zl / e ** 2) ** 2)
        return (k0 * (k0 - 1) / np.maximum(k1, 1e-9)).astype(F32), tt, x / wq, y / tq

    def d(self, P):
        out = np.full(len(P), BIG, F32)
        for i, (lo, hi) in enumerate(self.sbb):
            m = _inbb(P, lo, hi, 0.004)
            if not m.any():
                continue
            idx = np.nonzero(m)[0]
            out[idx] = smin(out[idx], self._seg(i, P[idx])[0], self.k)
        return out

    def local(self, P):
        """Nearest segment per point: distance, s along the ribbon (0 root .. 1 tip), x / half width, y / half thickness."""
        n = len(P)
        best = np.full(n, BIG, F32)
        sp, xw, yt = np.zeros(n, F32), np.zeros(n, F32), np.zeros(n, F32)
        ns = len(self.C) - 1
        for i, (lo, hi) in enumerate(self.sbb):
            m = _inbb(P, lo, hi, 0.006)
            if not m.any():
                continue
            idx = np.nonzero(m)[0]
            dd, tt, xx, yy = self._seg(i, P[idx])
            b = dd < best[idx]
            j = idx[b]
            best[j], sp[j], xw[j], yt[j] = dd[b], (i + tt[b]) / ns, xx[b], yy[b]
        return best, sp, xw, yt


class Poly2(Node):
    """Prism: closed polygon V in a plane ('xz' front, 'yz' side, 'xy' top) extruded along the third axis (optionally
    limited to [e0, e1] on that axis)."""
    AX = {"xz": (0, 2, 1), "yz": (1, 2, 0), "xy": (0, 1, 2)}

    def __init__(self, V, plane="xz", e=None):
        self.V = np.asarray(V, F32)
        self.ia, self.ib, self.ie = self.AX[plane]
        self.e = e
        lo, hi = np.full(3, -1e9), np.full(3, 1e9)
        lo[self.ia], lo[self.ib] = self.V[:, 0].min(), self.V[:, 1].min()
        hi[self.ia], hi[self.ib] = self.V[:, 0].max(), self.V[:, 1].max()
        if e is not None:
            lo[self.ie], hi[self.ie] = e
        self.lo, self.hi = lo, hi

    def d(self, P):
        px, pz = P[:, self.ia], P[:, self.ib]
        V = self.V
        dmin = (px - V[0, 0]) ** 2 + (pz - V[0, 1]) ** 2
        s = np.ones_like(px)
        n = len(V)
        for i in range(n):
            j = i - 1
            ex, ez = V[j, 0] - V[i, 0], V[j, 1] - V[i, 1]
            wx, wz = px - V[i, 0], pz - V[i, 1]
            t = np.clip((wx * ex + wz * ez) / (ex * ex + ez * ez), 0, 1)
            bx, bz = wx - ex * t, wz - ez * t
            dmin = np.minimum(dmin, bx * bx + bz * bz)
            c1, c2, c3 = pz >= V[i, 1], pz < V[j, 1], ex * wz > ez * wx
            s = np.where((c1 & c2 & c3) | (~c1 & ~c2 & ~c3), -s, s)
        d = s * np.sqrt(dmin)
        if self.e is not None:
            d = np.maximum(d, np.maximum(self.e[0] - P[:, self.ie], P[:, self.ie] - self.e[1]))
        return d.astype(F32)


class Band2(Node):
    """|distance to polyline V| - w in a plane, extruded (open or closed polyline)."""

    def __init__(self, V, w, plane="xz", closed=False):
        self.V, self.w, self.closed = np.asarray(V, F32), F32(w), closed
        self.ia, self.ib, self.ie = Poly2.AX[plane]
        lo, hi = np.full(3, -1e9), np.full(3, 1e9)
        lo[self.ia], lo[self.ib] = self.V[:, 0].min() - w, self.V[:, 1].min() - w
        hi[self.ia], hi[self.ib] = self.V[:, 0].max() + w, self.V[:, 1].max() + w
        self.lo, self.hi = lo, hi

    def d(self, P):
        return (seg_dist2(P[:, self.ia], P[:, self.ib], self.V, self.closed) - self.w).astype(F32)


def seg_dist2(px, pz, V, closed=False):
    d = np.full(px.shape, 1e9, F32)
    n = len(V)
    for i in range(n if closed else n - 1):
        j = (i + 1) % n
        ex, ez = V[j, 0] - V[i, 0], V[j, 1] - V[i, 1]
        wx, wz = px - V[i, 0], pz - V[i, 1]
        t = np.clip((wx * ex + wz * ez) / (ex * ex + ez * ez + 1e-12), 0, 1)
        d = np.minimum(d, (wx - ex * t) ** 2 + (wz - ez * t) ** 2)
    return np.sqrt(d)


class Half(Node):
    """(P - p0) . n < 0 is inside."""

    def __init__(self, n, p0):
        self.n, self.p0 = nrm(n).astype(F32), np.asarray(p0, F32)

    def d(self, P):
        return ((P - self.p0) @ self.n).astype(F32)


class Slab(Node):
    def __init__(self, n, c, half):
        self.n, self.c, self.h = nrm(n).astype(F32), np.asarray(c, F32), F32(half)

    def d(self, P):
        return (np.abs((P - self.c) @ self.n) - self.h).astype(F32)


def _bbu(ch):
    return np.min([c.lo for c in ch], 0), np.max([c.hi for c in ch], 0)


class U(Node):
    """Union; k > 0 = smooth (polynomial smin)."""

    def __init__(self, *ch, k=0.0):
        self.ch, self.k = [c for c in ch if c is not None], k
        self.lo, self.hi = _bbu(self.ch)

    def leaves(self):
        return [l for c in self.ch for l in c.leaves()]

    def kids(self):
        return self.ch

    def d(self, P):
        out = np.full(len(P), BIG, F32)
        for c in self.ch:
            m = _inbb(P, c.lo, c.hi, self.k + 0.006)
            if not m.any():
                continue
            idx = np.nonzero(m)[0] if not m.all() else None
            if idx is None:
                out = smin(out, c.d(P), self.k).astype(F32)
            else:
                out[idx] = smin(out[idx], c.d(P[idx]), self.k)
        return out


class I(Node):
    """a intersected with every b (smooth k); evaluated only near / inside a."""

    def __init__(self, a, *bs, k=0.0):
        self.a, self.bs, self.k = a, bs, k
        lo, hi = a.lo.copy(), a.hi.copy()
        for b in bs:
            lo, hi = np.maximum(lo, b.lo), np.minimum(hi, b.hi)
        self.lo, self.hi = lo, hi

    def leaves(self):
        return self.a.leaves()

    def kids(self):
        return [self.a]

    def d(self, P):
        d = self.a.d(P)
        for b in self.bs:
            sel = np.nonzero(d < 0.05)[0]
            if len(sel):
                d[sel] = smax(d[sel], b.d(P[sel]), self.k)
        return d


class S(Node):
    """a minus every b (smooth k)."""

    def __init__(self, a, *bs, k=0.0):
        self.a, self.bs, self.k = a, bs, k
        self.lo, self.hi = a.lo, a.hi

    def leaves(self):
        return self.a.leaves()

    def kids(self):
        return [self.a]

    def d(self, P):
        d = self.a.d(P)
        for b in self.bs:
            sel = np.nonzero((d < 0.05) & _inbb(P, b.lo, b.hi, self.k + 0.006))[0]
            if len(sel):
                d[sel] = smax(d[sel], -b.d(P[sel]), self.k)
        return d


class Off(Node):
    def __init__(self, ch, r):
        self.ch, self.r = ch, F32(r)
        self.lo, self.hi = ch.lo - max(r, 0), ch.hi + max(r, 0)

    def leaves(self):
        return self.ch.leaves()

    def kids(self):
        return [self.ch]

    def d(self, P):
        return self.ch.d(P) - self.r


class OffVar(Node):
    """Offset by base + fn(P) (a smooth bulge, small next to the base): d = child - base - fn(P)."""

    def __init__(self, ch, base, fn, extra=0.02):
        self.ch, self.base, self.fn = ch, F32(base), fn
        self.lo, self.hi = ch.lo - (base + extra), ch.hi + (base + extra)

    def leaves(self):
        return self.ch.leaves()

    def kids(self):
        return [self.ch]

    def d(self, P):
        return (self.ch.d(P) - self.base - np.asarray(self.fn(P), F32)).astype(F32)


def chamfer_box(c, R, half, rad, cham, k=0.002):
    """Rounded box in frame R (columns = local x, y, z) with chamfered edges; cham = [((i, si), (j, sj), w)]: the edge
    between face (axis i, sign si) and face (axis j, sign sj) is cut by a 45-degree plane leaving a bevel of leg w."""
    c, R = np.asarray(c, float), np.asarray(R, float)
    cuts = []
    for (i, si), (j, sj), w in cham:
        n = nrm(R[:, i] * si + R[:, j] * sj)
        cuts.append(Half(n, c + n * (half[i] + half[j] - w) / math.sqrt(2)))
    return I(RBox(c, half, rad, R), *cuts, k=k)


class Shell(Node):
    def __init__(self, ch, t):
        self.ch, self.t = ch, F32(t)
        self.lo, self.hi = ch.lo - t, ch.hi + t

    def leaves(self):
        return self.ch.leaves()

    def kids(self):
        return [self.ch]

    def d(self, P):
        return np.abs(self.ch.d(P)) - self.t


class ExZ(Node):
    """The child's horizontal section at z0 extruded over [z0e, z1e]."""

    def __init__(self, ch, z0, z0e, z1e):
        self.ch, self.z0, self.e = ch, F32(z0), (F32(z0e), F32(z1e))
        self.lo = np.array([ch.lo[0], ch.lo[1], z0e])
        self.hi = np.array([ch.hi[0], ch.hi[1], z1e])

    def leaves(self):
        return self.ch.leaves()

    def d(self, P):
        Q = P.copy()
        Q[:, 2] = self.z0
        d2 = self.ch.d(Q)
        dz = np.maximum(self.e[0] - P[:, 2], P[:, 2] - self.e[1])
        return (np.minimum(np.maximum(d2, dz), 0) + np.sqrt(np.maximum(d2, 0) ** 2 + np.maximum(dz, 0) ** 2)).astype(F32)


def grad(fn, P, e=3e-4):
    P = np.asarray(P, F32)
    g = np.empty_like(P)
    for i in range(3):
        dv = np.zeros(3, F32)
        dv[i] = e
        g[:, i] = (fn(P + dv) - fn(P - dv)) / (2 * e)
    return g


def project(fn, P, it=6, off=0.0, clip=0.01):
    """Newton projection onto the iso-surface fn = off (steps along the unit gradient, each at most clip)."""
    P = np.asarray(P, F32).reshape(-1, 3).copy()
    for _ in range(it):
        d = fn(P) - off
        g = grad(fn, P)
        gn = np.sqrt((g * g).sum(1))
        stp = np.clip(d / np.maximum(gn, 0.5), -clip, clip)
        P -= (g / np.maximum(gn, 1e-6)[:, None] * stp[:, None]).astype(F32)
    return P


def trace(fn, o, dv, tmax=0.8, step=0.6):
    """First hit of a ray from o along dv."""
    o, dv = np.asarray(o, float), nrm(dv)
    t = 0.0
    for _ in range(2000):
        p = o + dv * t
        d = float(fn(p[None].astype(F32))[0])
        if d < 1e-4:
            return project(fn, p[None])[0].astype(float)
        t += min(max(d * step, 2e-4), 0.01)
        if t > tmax:
            break
    raise RuntimeError(f"trace missed from {o} along {dv}")


# =====================================================================================================================
# palette (sRGB hex), gloss = vertex / texture alpha
# =====================================================================================================================
def hexc(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])

def hermite(x, y, xx):
    x, y = np.asarray(x, float), np.asarray(y, float)
    m = np.zeros_like(y)
    m[1:-1] = (y[2:] - y[:-2]) / (x[2:] - x[:-2])
    m[0], m[-1] = (y[1] - y[0]) / (x[1] - x[0]), (y[-1] - y[-2]) / (x[-1] - x[-2])
    i = np.clip(np.searchsorted(x, xx) - 1, 0, len(x) - 2)
    h = x[i + 1] - x[i]
    t = (xx - x[i]) / h
    return ((2 * t ** 3 - 3 * t ** 2 + 1) * y[i] + (t ** 3 - 2 * t ** 2 + t) * h * m[i] + (-2 * t ** 3 + 3 * t ** 2) * y[i + 1]
            + (t ** 3 - t ** 2) * h * m[i + 1])


def smooth_rows(rows, n=72):
    R = np.asarray(rows, float)
    zz = np.linspace(R[0, 0], R[-1, 0], n)
    return np.stack([zz] + [hermite(R[:, 0], R[:, c], zz) for c in range(1, R.shape[1])], 1)


def front_pt(node, x, z, side=-1, y0=0.5):
    """Surface point seen from the front (side -1, camera on -Y) or the back (+1)."""
    return trace(node.d, (x, side * y0, z), (0, -side, 0))


def radial_pt(node, z, az, c=(0, 0, 0), r0=0.4):
    """Surface point on the horizontal ray from far out toward the vertical axis through c (az from the front to +X)."""
    a = math.radians(az)
    dv = np.array([math.sin(a), -math.cos(a), 0.0])
    o = np.array([c[0], c[1], z]) + dv * r0
    return trace(node.d, o, -dv)


def normal_at(node, p):
    return nrm(grad(node.d, np.asarray(p, F32)[None])[0])

def fig_d(P):
    P = np.asarray(P, F32)
    out = np.full(len(P), BIG, F32)
    for p in PARTS:
        m = _inbb(P, p.lo, p.hi, 0.01)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        out[idx] = np.minimum(out[idx], p.node.d(P[idx]))
    return out


def fig_owner(P):
    P = np.asarray(P, F32)
    best = np.full(len(P), BIG, F32)
    own = np.zeros(len(P), np.int32)
    for i, p in enumerate(PARTS):
        m = _inbb(P, p.lo, p.hi, 0.01)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        d = p.node.d(P[idx])
        better = d < best[idx] - 1e-6 * p.prio
        best[idx[better]] = d[better]
        own[idx[better]] = i
    return own, best


# =====================================================================================================================
# meshing: narrow-band sampling + surface nets + Newton projection
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
    return Fv, n, int(act.sum() * B ** 3)


def surface_nets(Fv, lo, h):
    n = np.array(Fv.shape)
    Sg = Fv < 0
    nx, ny, nz = n
    cnt = np.zeros((nx - 1, ny - 1, nz - 1), np.uint8)
    for c in CORN:
        cnt += Sg[c[0]:nx - 1 + c[0], c[1]:ny - 1 + c[1], c[2]:nz - 1 + c[2]]
    act = (cnt > 0) & (cnt < 8)
    del cnt
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


def mesh_field(fn, h, lo=(-0.34, -0.17, -0.012), hi=(0.34, 0.17, 1.03)):
    lo, hi = np.asarray(lo, float), np.asarray(hi, float)
    Fv, n, ne = sample_grid(fn, lo, hi, h)
    log(f"grid {tuple(n)} h {h * 1000:.1f} mm: {ne / 1e6:.2f} M samples")
    V, Q = surface_nets(Fv, lo, h)
    del Fv
    V = V.astype(F32)
    for _ in range(3):  # Newton steps along the unit gradient, at most 0.4 cell each (no jumps where the gradient vanishes)
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


# =====================================================================================================================
# shape stage: silhouettes + quick previews
def sstep(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mixc(a, b, t):
    t = np.asarray(t, float)
    return np.asarray(a, float) + (np.asarray(b, float) - np.asarray(a, float)) * (t[..., None] if t.ndim else t)


H = hexc
KEY_L = nrm((-0.30, -0.62, 0.73))       # baked key light: front, above, from the viewer's left
VIEW = np.array([0.0, -1.0, 0.0])
HALF_V = nrm(KEY_L + VIEW)


def ramp(key, v):
    pos, cols = RAMP[key]
    v = np.clip(v, 0, 1)
    return np.stack([np.interp(v, pos, cols[:, i]) for i in range(3)], 1)


def shade(key, N, occ, spec=0.0):
    kind, aos = MAT[key][1], MAT[key][2]
    lam = np.clip(N @ KEY_L, -1, 1)
    if kind == "metal":
        nz, ny = N[:, 2], N[:, 1]
        top = sstep(0.05, 0.85, nz)
        hor = np.exp(-((nz - 0.0) / 0.22) ** 2)
        flo = sstep(-0.2, -0.9, nz)
        fr = sstep(0.15, 0.95, -ny)
        v = 0.30 + 0.50 * top - 0.34 * hor * (1 - 0.5 * fr) + 0.28 * flo + 0.24 * fr * (1 - top) + 0.10 * lam
        c = ramp(key, v)
        sp = np.clip(N @ HALF_V, 0, 1) ** 40 * 0.70
    else:
        t = sstep(-0.85, 0.90, 0.6 * lam + 0.4 * N[:, 2])
        c = ramp(key, t)
        sp = np.clip(N @ HALF_V, 0, 1) ** 26 * spec
    c = c + (1 - c) * sp[:, None]
    return np.clip(c * (1 - aos * occ)[:, None], 0, 1)


def gloss_of(key):
    return MAT[key][3] if key in MAT else PAL[key][1]


def seg2(px, pz, V):
    """Distance to an open 2-D polyline V (n, 2) and the fraction along it (0..1)."""
    V = np.asarray(V, float)
    best = np.full(px.shape, 1e9)
    frac = np.zeros(px.shape)
    L = np.r_[0, np.cumsum(np.linalg.norm(np.diff(V, axis=0), axis=1))]
    for i in range(len(V) - 1):
        ex, ez = V[i + 1, 0] - V[i, 0], V[i + 1, 1] - V[i, 1]
        wx, wz = px - V[i, 0], pz - V[i, 1]
        t = np.clip((wx * ex + wz * ez) / (ex * ex + ez * ez + 1e-12), 0, 1)
        d = np.sqrt((wx - ex * t) ** 2 + (wz - ez * t) ** 2)
        b = d < best
        best = np.where(b, d, best)
        frac = np.where(b, (L[i] + t * (L[i + 1] - L[i])) / L[-1], frac)
    return best, frac


def seg3(P, C):
    """Distance from points to a 3-D polyline C."""
    C = np.asarray(C, float)
    best = np.full(len(P), 1e9)
    for i in range(len(C) - 1):
        ab = C[i + 1] - C[i]
        t = np.clip(((P - C[i]) @ ab) / (ab @ ab), 0, 1)
        best = np.minimum(best, np.linalg.norm(P - (C[i] + t[:, None] * ab), axis=1))
    return best


def stroke(c, mask_w, col):
    """Blend colour col into c with weight mask_w (0..1)."""
    return mixc(c, col, np.clip(mask_w, 0, 1))


def line_w(d, half, soft=0.0007):
    return 1 - sstep(half - soft, half + soft, d)


GLOW, GLOW_HOT, GLOW_EDGE = H("#FFB347"), H("#FFF0B8"), H("#F07A1C")


def eye_paint(c, g, u, v, s, tint=None):
    """Painted cartoon eye in face-plane coordinates (u to +X, v up) around its centre; s = side (outer corner)."""
    au, top, bot = 0.0238, 0.0172, 0.0140
    q = np.clip(1 - (u / au) ** 2, 0, 1)
    vt = top * np.sqrt(q) * (1 + 0.10 * s * u / au)
    vb = -bot * np.sqrt(q)
    inside = (np.abs(u) < au) & (v < vt) & (v > vb)
    soft = np.clip(np.minimum(vt - v, v - vb) / 0.0009, 0, 1) * inside
    white = H("#FFFFFF") if tint is None else tint["white"]
    w_sh = mixc(white, white * 0.86, sstep(-0.004, 0.012, v) * 0.6)
    c = stroke(c, soft, w_sh)
    iu, iv = s * -0.0012, -0.0010
    r = np.sqrt((u - iu) ** 2 + (v - iv) ** 2)
    ir = 0.0140
    iris_out = H("#5A3216") if tint is None else tint["iris_out"]
    iris_in = H("#C47B36") if tint is None else tint["iris_in"]
    icol = mixc(iris_in, iris_out, sstep(0.004, ir, r))
    icol = mixc(icol, icol * 1.25, sstep(0.0, -0.010, v - iv) * 0.5)
    c = stroke(c, soft * line_w(r, ir, 0.0005), np.clip(icol, 0, 1))
    c = stroke(c, soft * line_w(np.abs(r - ir + 0.0006), 0.0008, 0.0004) * 0.8, iris_out * 0.6)
    pupil = H("#1E140C") if tint is None else tint["pupil"]
    c = stroke(c, soft * line_w(r, 0.0064, 0.0004), pupil)
    hl = H("#FFFFFF") if tint is None else tint["spark"]
    r1 = np.sqrt((u - iu - s * -0.0045) ** 2 + (v - iv - 0.0052) ** 2)
    r2 = np.sqrt((u - iu - s * 0.0042) ** 2 + (v - iv + 0.0046) ** 2)
    c = stroke(c, soft * line_w(r1, 0.0034, 0.0004), hl)
    c = stroke(c, soft * line_w(r2, 0.0016, 0.0003) * 0.9, hl)
    g = np.where(inside, 1.0, g)
    # upper lash line (thicker toward the outer corner, small flick) and a faint lower lid line
    lash = H("#2A1A12") if tint is None else tint["lash"]
    outer = sstep(-0.2, 1.0, s * u / au)
    thick = 0.0020 + 0.0016 * outer
    dl = np.abs(v - vt - 0.0004)
    wl = line_w(dl, thick, 0.0004) * (np.abs(u) < au * 1.02)
    fl_u, fl_v = s * (au + 0.0045), top * 0.30 + 0.0030
    dflick = seg2(u, v, [(s * au * 0.85, top * 0.45), (fl_u, fl_v)])[0]
    wl = np.maximum(wl, line_w(dflick, 0.0016, 0.0004))
    c = stroke(c, wl, lash)
    wlo = line_w(np.abs(v - vb + 0.0003), 0.0006, 0.0003) * (np.abs(u) < au * 0.85) * 0.45
    c = stroke(c, wlo, lash * 1.6)
    return c, g

def field_ao(P, N):
    occ = np.zeros(len(P), F32)
    for h, w in ((0.0035, 0.28), (0.008, 0.26), (0.015, 0.20), (0.025, 0.15), (0.040, 0.11)):
        d = fig_d((P + N * h).astype(F32))
        occ += F32(w) * np.clip((h - d) / h, 0, 1)
    return np.clip(occ, 0, 1)


def surface_normals(P, fn=None):
    g = grad(fn or fig_d, P, e=2.5e-4)
    return (g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)).astype(F32)


def paint_points(P, parts=None):
    """P on the exact surface -> sRGB colour, gloss, owner index. parts: restrict normals and owners to these parts (one
    shell of the game mesh); the AO always sees the whole figure."""
    P = np.asarray(P, F32)
    idx = list(range(len(PARTS))) if parts is None else [PARTS.index(p) for p in parts]
    fn = None if parts is None else shell_fn(parts)
    N = surface_normals(P, fn)
    occ = field_ao(P, N)
    best = np.full(len(P), BIG, F32)
    own = np.full(len(P), idx[0], np.int32)
    for i in idx:
        p = PARTS[i]
        m = _inbb(P, p.lo, p.hi, 0.01)
        if not m.any():
            continue
        j = np.nonzero(m)[0]
        d = p.node.d(P[j])
        b = d < best[j]
        best[j[b]] = d[b]
        own[j[b]] = i
    col = np.zeros((len(P), 3))
    gl = np.zeros(len(P))
    for i in idx:
        m = own == i
        if m.any():
            c, g = PARTS[i].paint(P[m].astype(np.float64), N[m].astype(np.float64), occ[m].astype(np.float64))
            col[m], gl[m] = c, g
    return np.clip(col, 0, 1), np.clip(gl, 0, 1), own

def decimate_to(ob, target):
    for attempt in range(5):
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        if tris <= target:
            break
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        m = ob.modifiers.new("Dec", "DECIMATE")
        m.ratio = target / tris * (0.995 if attempt == 0 else 0.985)
        m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=m.name)
        log(f"  decimate pass {attempt}: {tris} -> {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
    m = ob.modifiers.new("Tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=m.name)
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# The game mesh is three overlapping closed shells (head + hair, two hands, body), meshed and decimated on their own so
# that each gets an exact budget: the hair gets most of it. The joins are hidden: the neck goes up into the head, the
def shell_parts(k):
    pre = SHELLS[k][1]
    if pre:
        return [p for p in PARTS if any(p.name.startswith(x) for x in pre)]
    taken = {p.name for kk in range(len(SHELLS)) if SHELLS[kk][1] for p in shell_parts(kk)}
    return [p for p in PARTS if p.name not in taken]


def shell_fn(parts):
    def fn(P):
        P = np.asarray(P, F32)
        out = np.full(len(P), BIG, F32)
        for p in parts:
            m = _inbb(P, p.lo, p.hi, 0.01)
            if m.any():
                idx = np.nonzero(m)[0]
                out[idx] = np.minimum(out[idx], p.node.d(P[idx]))
        return out
    return fn


def build_low(Vh, Th, target, fn, name="Theo"):
    ob = to_mesh(name, Vh, Th)
    # surface nets pinch thin features into non-manifold edges; quadric collapse stalls on them -> split them open
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
    V = project(fn, V, it=3, clip=0.0015).astype(np.float64)   # small Newton correction onto the shell's surface
    log(f"  shell {name}: {len(V)} verts {len(T)} tris (target {target}), {len(bad)} non-manifold edges split")
    return V, T


def build_shells(h):
    Vs, Ts, sid, base = [], [], [], 0
    for k, (name, _, budget) in enumerate(SHELLS):
        parts = shell_parts(k)
        fn = shell_fn(parts)
        lo = np.min([p.lo for p in parts], 0) - 0.02
        hi = np.max([p.hi for p in parts], 0) + 0.02
        lo, hi = np.maximum(lo, BOUNDS[0]), np.minimum(hi, BOUNDS[1])
        Vh, Th = mesh_field(fn, h, lo, hi)
        V, T = build_low(Vh, Th, budget, fn, name)
        Vs.append(V)
        Ts.append(T + base)
        sid.append(np.full(len(V), k))
        base += len(V)
    V, T, sid = np.concatenate(Vs), np.concatenate(Ts), np.concatenate(sid)
    ob = to_mesh(MESH_NAME, V, T)
    for p in ob.data.polygons:
        p.use_smooth = True
    own, _ = fig_owner(V[T].mean(1).astype(F32))
    hair = sum(1 for i in own if PARTS[i].name == "hair")
    log(f"low poly: {len(V)} verts {len(T)} tris in {len(SHELLS)} shells, hair {hair}")
    return ob, sid


def project_shells(V, sid, it=5, clip=0.002):
    V = np.asarray(V, float).copy()
    for k in range(len(SHELLS)):
        m = sid == k
        if m.any():
            V[m] = project(shell_fn(shell_parts(k)), V[m], it=it, clip=clip)
    return V

def unwrap(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(89), island_margin=0.002, area_weight=1.0, scale_to_bounds=False)
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")
    me = ob.data
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    # islands: faces joined across edges whose UVs agree
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
    own, _ = fig_owner(cen.astype(F32))
    names = np.array([PARTS[i].name for i in own])
    ff = face_factor(cen, names)
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
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    log(f"UV: {len(np.unique(isl))} islands")
    return uv.reshape(-1, 2)


def raster(UVc, res):
    """UVc (nt, 3, 2) -> per texel: triangle index (-1 empty) and barycentrics; conservative by 0.7 px."""
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
        L = np.stack([l0[ok][better], l1[ok][better], l2[ok][better]], 1)
        L = np.clip(L, -0.2, 1.2)
        bary[idx] = L / L.sum(1, keepdims=True)
    return tri, bary


def dilate(img, filled, iters=24):
    H_, W_ = filled.shape
    img = img.copy()
    filled = filled.copy()
    for _ in range(iters):
        acc = np.zeros_like(img)
        cnt = np.zeros((H_, W_), np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                sf = np.roll(np.roll(filled, dy, 0), dx, 1)
                si = np.roll(np.roll(img, dy, 0), dx, 1)
                acc += si * sf[..., None]
                cnt += sf
        new = (~filled) & (cnt > 0)
        if not new.any():
            break
        img[new] = acc[new] / cnt[new][:, None]
        filled |= new
    img[~filled] = img[filled].mean(0)
    return img


def bake_texture(ob, uv, sid):
    """Per texel: the point of the game mesh -> the exact surface of ITS shell (sid per vertex), paint there."""
    me = ob.data
    V, T = mesh_np(ob)
    me.calc_loop_triangles()
    lt_loops = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt_loops)
    lt_loops = lt_loops.reshape(-1, 3)
    vn = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", vn)
    vn = vn.reshape(-1, 3)
    UVc = uv[lt_loops]
    tri, bary = raster(UVc, TEX)
    has = tri >= 0
    ti = tri[has]
    b = bary[has].astype(np.float64)
    P0 = (V[T[ti, 0]] * b[:, :1] + V[T[ti, 1]] * b[:, 1:2] + V[T[ti, 2]] * b[:, 2:3])
    N0 = (vn[T[ti, 0]] * b[:, :1] + vn[T[ti, 1]] * b[:, 1:2] + vn[T[ti, 2]] * b[:, 2:3])
    N0 /= np.maximum(np.linalg.norm(N0, axis=1, keepdims=True), 1e-9)
    tsid = sid[T[ti, 0]]
    log(f"bake: {has.sum()} texels ({has.mean() * 100:.0f}% of {TEX}^2)")
    cols, gls = np.zeros((len(P0), 3)), np.zeros(len(P0))
    n_r = 0
    for k in range(len(SHELLS)):
        sel = np.nonzero(tsid == k)[0]
        if not len(sel):
            continue
        parts = shell_parts(k)
        fn = shell_fn(parts)
        # (a) along the interpolated normal (cage-like), (b) closest point; (a) only where it converged close by
        P = P0[sel].astype(F32)
        n = N0[sel].astype(F32)
        t = np.zeros(len(P), F32)
        for _ in range(6):
            Q = P + n * t[:, None]
            d = fn(Q)
            e = F32(3e-4)
            dd = (fn(Q + n * e) - fn(Q - n * e)) / (2 * e)
            stp = np.where(np.abs(dd) > 0.25, d / np.where(np.abs(dd) > 0.25, dd, 1), d)
            t = np.clip(t - np.clip(stp, -0.003, 0.003), -0.008, 0.008).astype(F32)
        Qr = P + n * t[:, None]
        ok = np.abs(fn(Qr)) < 3e-4
        Qc = project(fn, P, it=6, clip=0.003)
        dr, dc = np.linalg.norm(Qr - P, axis=1), np.linalg.norm(Qc - P, axis=1)
        use_r = ok & (dr < 1.5 * dc + 0.0008)
        n_r += int(use_r.sum())
        Q = project(fn, np.where(use_r[:, None], Qr, Qc), it=2, clip=0.002)
        CH = 120000
        for c0 in range(0, len(Q), CH):
            cc, gg, _ = paint_points(Q[c0:c0 + CH], parts)
            cols[sel[c0:c0 + CH]], gls[sel[c0:c0 + CH]] = cc, gg
        log(f"  painted shell {SHELLS[k][0]}: {len(sel)} texels")
    log(f"bake projection: along normal {n_r / len(P0) * 100:.0f}%, closest point {100 - n_r / len(P0) * 100:.0f}%")
    img = np.zeros((TEX * TEX, 4), np.float32)
    img[has, :3] = cols
    img[has, 3] = gls
    img = img.reshape(TEX, TEX, 4)
    img = dilate(img, has.reshape(TEX, TEX))
    return img  # rows = v (bottom-up, Blender image order)


def save_texture(img, path):
    im = bpy.data.images.new(os.path.basename(path)[:-4], TEX, TEX, alpha=True)
    im.alpha_mode = "CHANNEL_PACKED"
    im.pixels.foreach_set(np.ascontiguousarray(img, np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im


def sample_tex(img, uv, blur=2):
    """Bilinear sample of the texture (rows bottom-up) at UVs, box-blurred by +-blur texels (vertex colour fallback)."""
    res = img.shape[0]
    acc = np.zeros((len(uv), 4))
    n = 0
    for dy in range(-blur, blur + 1, max(blur, 1)):
        for dx in range(-blur, blur + 1, max(blur, 1)):
            x = np.clip(uv[:, 0] * res - 0.5 + dx, 0, res - 1)
            y = np.clip(uv[:, 1] * res - 0.5 + dy, 0, res - 1)
            x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
            x1, y1 = np.minimum(x0 + 1, res - 1), np.minimum(y0 + 1, res - 1)
            fx, fy = (x - x0)[:, None], (y - y0)[:, None]
            acc += (img[y0, x0] * (1 - fx) * (1 - fy) + img[y0, x1] * fx * (1 - fy) + img[y1, x0] * (1 - fx) * fy
                    + img[y1, x1] * fx * fy)
            n += 1
    return acc / n


def srgb_to_lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def shell_owner(V, sid):
    """Owning part per vertex, among the parts of the vertex's own shell."""
    own = np.zeros(len(V), np.int32)
    for k in range(len(SHELLS)):
        sel = np.nonzero(sid == k)[0]
        if not len(sel):
            continue
        idx = [PARTS.index(p) for p in shell_parts(k)]
        P = V[sel].astype(F32)
        best = np.full(len(P), BIG, F32)
        o = np.full(len(P), idx[0], np.int32)
        for i in idx:
            pr = PARTS[i]
            m = _inbb(P, pr.lo, pr.hi, 0.01)
            if not m.any():
                continue
            j = np.nonzero(m)[0]
            d = pr.node.d(P[j])
            b = d < best[j]
            best[j[b]] = d[b]
            o[j[b]] = i
        own[sel] = o
    return own


def vertex_weights(V, sid):
    own = shell_owner(V, sid)
    W = [None] * len(V)
    for i, p in enumerate(PARTS):
        idx = np.nonzero(own == i)[0]
        if not len(idx):
            continue
        spec = p.bone
        if spec[0] == "suit":
            ws = suit_weights(V[idx])
            for k, j in enumerate(idx):
                W[j] = ws[k]
        elif spec[0] == "fixed":
            for j in idx:
                W[j] = dict(spec[1])
        else:
            for j in idx:
                W[j] = {spec[0]: 1.0}
    out = []
    for w in W:
        top = sorted(w.items(), key=lambda kv: -kv[1])[:4]
        top = [(k, v) for k, v in top if v > 0.02] or top[:1]
        t = sum(v for _, v in top)
        out.append({k: v / t for k, v in top})
    return out


def make_object(name, V, T, uvc, W, img, tex_path, shift, scale):
    """Mesh + armature in the shared format. uvc: per triangle-corner UVs (nt*3, 2)."""
    Vn = (V + shift) * scale
    ob = to_mesh(name, Vn, T)
    me = ob.data
    if len(me.polygons) != len(T):
        raise RuntimeError(f"{name}: validate changed faces {len(me.polygons)} vs {len(T)}")
    ul = me.uv_layers.new(name="UVMap")
    # loops are in polygon order = triangle order with the corners as given
    ul.data.foreach_set("uv", np.asarray(uvc, np.float64).ravel())
    smp = sample_tex(img, uvc, blur=3)
    rgb = srgb_to_lin(smp[:, :3])
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([rgb, smp[:, 3:4]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    mat = bpy.data.materials.get("FighterVC") or bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    texn = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    texn.image = bpy.data.images.load(tex_path, check_existing=True)
    texn.image.alpha_mode = "CHANNEL_PACKED"
    nt.links.new(texn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
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
    by = {}
    for i, w in enumerate(W):
        for bn, val in w.items():
            by.setdefault((bn, round(float(val), 3)), []).append(i)
    for (bn, val), idx in by.items():
        groups[bn].add(idx, val, "REPLACE")
    ob.parent = arm
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return ob, arm


def export(ob, arm, base):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)



# =====================================================================================================================
# parts
# =====================================================================================================================
class Part:
    def __init__(self, name, node, key, bone, paint=None, prio=0):
        self.name, self.node, self.key, self.bone, self.paint, self.prio = name, node, key, bone, paint, prio
        self.lo, self.hi = node.lo, node.hi


PARTS = []


def part(name, node, key, bone, paint=None, prio=0):
    p = Part(name, node, key, bone, paint, prio)
    PARTS.append(p)
    return p


def sstep_np(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}

# =====================================================================================================================
# MODEL (Blender: Z up, faces -Y, +X = the character's left). Final units; proportions as Theo v3 (head, shoulders, hands).
# =====================================================================================================================
# ---------------- bodice + arms (one smooth "suit": plum dress with long bell sleeves) ----------------
TORSO = Loft(smooth_rows([  # z, half width, cx, y front, y back, exponent
    (0.430, 0.040, 0, -0.035, 0.040, 2.2),
    (0.445, 0.078, 0, -0.062, 0.068, 2.4),
    (0.480, 0.076, 0, -0.060, 0.066, 2.4),
    (0.520, 0.078, 0, -0.062, 0.068, 2.5),
    (0.560, 0.084, 0, -0.069, 0.074, 2.5),
    (0.600, 0.088, 0, -0.068, 0.078, 2.6),
    (0.640, 0.086, 0, -0.062, 0.076, 2.6),
    (0.670, 0.077, 0, -0.054, 0.068, 2.4),
    (0.695, 0.056, 0, -0.042, 0.052, 2.2),
    (0.712, 0.034, 0, -0.030, 0.036, 2.0)]))
SH = {s: mirror((-0.112, 0.004, 0.632), s) for s in SIDES}
EL = {s: mirror((-0.168, 0.000, 0.532), s) for s in SIDES}
WR = {s: mirror((-0.214, -0.008, 0.442), s) for s in SIDES}
HIP = {s: mirror((-0.050, 0.005, 0.400), s) for s in SIDES}
KN = {s: mirror((-0.060, 0.002, 0.250), s) for s in SIDES}
AN = {s: mirror((-0.066, 0.010, 0.098), s) for s in SIDES}
ARM = {}
ARM_DN = {}
for s in SIDES:
    a = "Arm" + SN[s]
    dn = nrm(WR[s] - EL[s])
    ARM_DN[s] = dn
    puff = Ell(SH[s] + mirror((-0.006, 0, 0.004), s), (0.050, 0.048, 0.046), bone=a)
    upper = RCone(SH[s], EL[s], 0.031, 0.029, bone=a)
    bell = S(RCone(EL[s], WR[s] + dn * 0.006, 0.030, 0.047, bone=a),
             Ell(WR[s] + dn * 0.016, (0.037, 0.037, 0.016), frame(dn)), k=0.004)  # hollow opening of the bell sleeve
    ARM[s] = U(puff, upper, bell, k=0.012)
SUIT = U(TORSO, *[ARM[s] for s in SIDES], k=0.016)
part("suit", SUIT, "plum", ("suit",))

# ---------------- skirt: a bell with soft pleats, rounded hem ----------------
SKIRT_L = Loft(smooth_rows([
    (0.262, 0.140, 0, -0.126, 0.136, 2.2),
    (0.278, 0.163, 0, -0.148, 0.158, 2.3),
    (0.300, 0.164, 0, -0.149, 0.159, 2.3),
    (0.340, 0.148, 0, -0.134, 0.144, 2.3),
    (0.390, 0.124, 0, -0.110, 0.120, 2.3),
    (0.440, 0.100, 0, -0.085, 0.094, 2.4),
    (0.480, 0.084, 0, -0.068, 0.076, 2.5),
    (0.500, 0.070, 0, -0.056, 0.064, 2.4)]))


def pleats(P):
    az = np.arctan2(P[:, 0], -P[:, 1])
    return 0.0055 * np.cos(az * 11.0) * sstep_np(0.47, 0.32, P[:, 2])


SKIRT = OffVar(SKIRT_L, 0.0, pleats, extra=0.008)
part("skirt", SKIRT, "plum", ("fixed", {"Root": 1.0}))

# ---------------- legs (striped stockings), Mary-Jane shoes ----------------
for s in SIDES:
    lg = "Leg" + SN[s]
    pts = [HIP[s], mirror((-0.056, 0.004, 0.330), s), KN[s], mirror((-0.064, 0.006, 0.175), s), AN[s]]
    rad = [0.044, 0.042, 0.036, 0.033, 0.028]
    part("leg_" + SN[s], U(*[RCone(pts[i], pts[i + 1], rad[i], rad[i + 1], bone=lg) for i in range(len(pts) - 1)]),
         "sock", (lg,))
    shoe = I(U(Ell(mirror((-0.068, -0.024, 0.040), s), (0.040, 0.064, 0.040)),
               RCone(AN[s], mirror((-0.068, 0.006, 0.050), s), 0.031, 0.036), k=0.02),
             Half((0, 0, -1), (0, 0, 0.0)), k=0.004)
    part("shoe_" + SN[s], shoe, "shoe", (lg,))

# ---------------- hands (skin: palm, three half-bent fingers, thumb) - Theo v3 hands, 0.86 scale ----------------
HS = 0.86
HANDF, FINGER_INFO = {}, {}
for s in SIDES:
    a = "Arm" + SN[s]
    dn = nrm(mirror((-0.36, 0.0, -0.93), s))
    f0 = np.array([-s * 0.55, -0.83, 0.0])
    fw = nrm(f0 - dn * np.dot(f0, dn))
    out = np.cross(fw, dn)
    out = nrm(out if out[0] * s > 0 else -out)
    W0 = WR[s]
    pc = W0 + dn * 0.040 * HS + out * 0.004 * HS
    HANDF[s] = (pc, dn, out, fw)
    Rh = np.stack([out, fw, dn], 1)
    palm = RBox(pc + out * 0.002 * HS, (0.022 * HS, 0.039 * HS, 0.035 * HS), 0.017 * HS, Rh, bone=a)
    wrist = RCone(W0 - dn * 0.016, pc - dn * 0.012 * HS, 0.025, 0.026 * HS, bone=a)
    fingers = []
    for k, fo in enumerate((-0.0215, 0.0, 0.0215)):
        fo *= HS
        ln = (0.92, 1.0, 0.94)[k] * HS
        b0 = pc + dn * 0.024 * HS + fw * fo + out * 0.003 * HS
        b1 = b0 + dn * 0.030 * ln + out * 0.001 + fw * fo * 0.05
        b2 = b1 + dn * 0.021 * ln - out * 0.011 * HS
        b3 = b2 + dn * 0.007 * ln - out * 0.012 * HS
        r0 = 0.0112 * HS
        fingers += [RCone(b0, b1, r0, r0 * 0.95, bone=a), RCone(b1, b2, r0 * 0.95, r0 * 0.87, bone=a),
                    RCone(b2, b3, r0 * 0.87, r0 * 0.76, bone=a)]
    FINGER_INFO[s] = (pc + dn * 0.024 * HS + out * 0.003 * HS, dn, out, fw)
    tb = pc + (fw * 0.031 - out * 0.010 - dn * 0.014) * HS
    tm = tb + (fw * 0.012 + dn * 0.028 - out * 0.004) * HS
    tt = tm + (fw * 0.004 + dn * 0.022 - out * 0.006) * HS
    thumb = U(RCone(tb, tm, 0.0150 * HS, 0.0140 * HS, bone=a), RCone(tm, tt, 0.0140 * HS, 0.0115 * HS, bone=a), k=0.004)
    HAND = U(U(wrist, palm, thumb, k=0.010), U(*fingers, k=0.0), k=0.007)
    part("hand_" + SN[s], HAND, "skin", (a,))

# ---------------- collar (Peter Pan collar with honey trim), sash with a bow, potion vials ----------------
COLLAR = U(I(Off(TORSO, 0.010), Slab((0, 0, 1), (0, 0, 0.684), 0.018), k=0.006),
           *[Ell(mirror((-0.036, -0.050, 0.672), s), (0.044, 0.026, 0.016), rot((0, 1, 0), s * 14) @ rot((1, 0, 0), -24))
             for s in SIDES], k=0.010)
part("collar", COLLAR, "plum_l", ("fixed", {"Body": 1.0}))
SASH_Z = 0.478
part("sash", I(Off(TORSO, 0.009), Slab((0, 0, 1), (0, 0, SASH_Z), 0.016), k=0.004), "sash", ("fixed", {"Root": 0.5, "Body": 0.5}))
BOW_C = np.array([0.040, -0.074, SASH_Z])
BOW = U(Ell(BOW_C, (0.012, 0.010, 0.012)),
        Ell(BOW_C + (-0.020, 0.002, 0.006), (0.020, 0.008, 0.012), rot((0, 1, 0), 20)),
        Ell(BOW_C + (0.020, 0.004, 0.006), (0.020, 0.008, 0.012), rot((0, 1, 0), -20)),
        RCone(BOW_C + (-0.004, 0.0, -0.008), BOW_C + (-0.014, -0.004, -0.050), 0.0075, 0.0055),
        RCone(BOW_C + (0.004, 0.0, -0.008), BOW_C + (0.016, -0.002, -0.046), 0.0075, 0.0055), k=0.006)
part("bow", BOW, "sash", ("fixed", {"Root": 0.5, "Body": 0.5}))
VIALS = []
for x in (-0.062, -0.030):
    c = np.array([x, -0.082 + abs(x) * 0.25, SASH_Z - 0.030])
    VIALS.append(U(Ell(c, (0.0125, 0.0115, 0.0150)), RCone(c + (0, 0, 0.010), c + (0, 0, 0.026), 0.006, 0.0055),
                   RCyl(c + (0, 0, 0.029), (0, 0, 1), 0.0070, 0.0045, 0.002), k=0.004))
part("vials", U(*VIALS), "glass", ("fixed", {"Root": 1.0}))

# ---------------- head (skin): Theo v3 head ----------------
CRAN = Ell((0, 0.002, 0.832), (0.094, 0.098, 0.112), bone="Head")
FACE = Ell((0, -0.022, 0.778), (0.089, 0.081, 0.070), bone="Head")
CHEEKS = [Ell((s * 0.050, -0.060, 0.760), (0.046, 0.040, 0.037), bone="Head") for s in SIDES]
CHIN = Ell((0, -0.068, 0.728), (0.032, 0.028, 0.021), bone="Head")
NOSE = U(Ell((0, -0.099, 0.774), (0.014, 0.014, 0.012), bone="Head"),
         Ell((0, -0.094, 0.788), (0.008, 0.010, 0.012), bone="Head"), k=0.010)
EARS = []
for s in SIDES:
    Re = rot((0, 0, 1), s * 20) @ rot((0, 1, 0), s * -6)
    ear = Ell((s * 0.106, 0.014, 0.782), (0.016, 0.024, 0.032), Re, bone="Head")
    concha = Ell((s * 0.120, 0.008, 0.780), (0.008, 0.014, 0.020), Re)
    EARS.append(S(ear, concha, k=0.006))
HEAD_SKULL = U(U(CRAN, FACE, CHIN, k=0.04), *CHEEKS, k=0.04)
HEAD = U(HEAD_SKULL, NOSE, *EARS, k=0.012)
part("head", HEAD, "skin", ("Head",))
part("neck", RCone((0, 0.006, 0.655), (0, 0.004, 0.745), 0.035, 0.034), "skin", ("fixed", {"Head": 0.6, "Body": 0.4}))
EYE = {}
for s in SIDES:
    p = front_pt(HEAD_SKULL, s * 0.0465, 0.8020)
    EYE[s] = (p, nrm(normal_at(HEAD_SKULL, p) + np.array([0, -1.5, 0])))
# brows: bold, a little lowered at the inner ends (the grumpy look), thin raised ribbons
BROWS = {-1: [(-0.078, 0.829), (-0.064, 0.839), (-0.046, 0.840), (-0.030, 0.834), (-0.017, 0.826)],
         1: [(0.017, 0.826), (0.030, 0.834), (0.046, 0.840), (0.064, 0.839), (0.078, 0.829)]}
for s in SIDES:
    C = np.array([front_pt(HEAD_SKULL, x, z) for x, z in BROWS[s]])
    Nn = np.array([normal_at(HEAD_SKULL, c) for c in C])
    C = C - Nn * 0.0002
    tan = np.gradient(C, axis=0)
    Wv = np.array([nrm(t) for t in tan])
    part("brow_" + SN[s], Ribbon(C, np.cross(Nn, Wv) * -1, Nn, np.array([0.0045, 0.0060, 0.0062, 0.0058, 0.0046]),
                                 np.full(5, 0.0012), bone="Head"), "hair", ("Head",))

# ---------------- hair: thick black waves - scalp + wavy ribbon locks (fringe, face framing, long back curtain) ----------
HAIR_YZ = np.array([(-0.20, 0.880), (-0.11, 0.878), (-0.07, 0.870), (-0.040, 0.852), (-0.034, 0.800), (-0.010, 0.760),
                    (0.010, 0.735), (0.040, 0.700), (0.20, 0.700), (0.20, 1.10), (-0.20, 1.10)])
CAP = S(I(U(Off(CRAN, 0.011), Ell((0.0, 0.034, 0.800), (0.096, 0.084, 0.090)), k=0.03), Poly2(HAIR_YZ, "yz"), k=0.008),
        Ell((0, -0.098, 0.775), (0.084, 0.060, 0.093)), k=0.012)
LOCKS = []


def catmull(P, n):
    P = np.asarray(P, float)
    P = np.vstack([2 * P[0] - P[1], P, 2 * P[-1] - P[-2]])
    out = []
    seg = len(P) - 3
    for i in range(seg):
        p0, p1, p2, p3 = P[i], P[i + 1], P[i + 2], P[i + 3]
        for t in np.linspace(0, 1, max(n // seg, 3), endpoint=(i == seg - 1)):
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    return np.array(out)


def lock(ctrl, W, th=0.48, n=30, tone=0.0, root=0.55, tipw=0.10):
    """Wavy lock through control points: elliptic ribbon, width axis around the head, thickness outward."""
    C = catmull(ctrl, n)
    s = np.linspace(0, 1, len(C))
    prof = (root + (1 - root) * sstep_np(0.0, 0.25, s)) * (1 - (1 - tipw) * sstep_np(0.55, 1.0, s) ** 1.2)
    w = np.maximum(W * prof, 0.0022)
    t = np.maximum(w * th, 0.0018)
    tan = np.gradient(C, axis=0)
    tan /= np.linalg.norm(tan, axis=1, keepdims=True)
    Tv, Wv = [], []
    for c, tg in zip(C, tan):
        o = c - np.array([0.0, 0.020, np.clip(c[2], 0.70, 0.86)])
        up = o - tg * np.dot(o, tg)
        if np.linalg.norm(up) < 1e-5:
            up = np.array([0, 1.0, 0])
        up = nrm(up)
        Tv.append(up)
        Wv.append(np.cross(tg, up))
    rb = Ribbon(C, Wv, Tv, w, t, bone="Head")
    rb.tone = tone
    LOCKS.append(rb)
    return rb


def ring(az, r, z, yc=0.020):
    a = math.radians(az)
    return (r * math.sin(a), yc - r * math.cos(a), z)


LOCK_ITEMS = []
# fringe: swept from a parting on the character's left toward the right, ends curling over the brows' outer ends
for x0, x1, xt, zt, w in ((0.030, -0.010, -0.040, 0.858, 0.024), (0.030, -0.045, -0.072, 0.846, 0.022),
                          (0.034, 0.030, 0.012, 0.862, 0.021), (0.040, 0.070, 0.082, 0.846, 0.020),
                          (0.030, -0.075, -0.098, 0.826, 0.020)):
    LOCK_ITEMS.append(([(x0, -0.040, 0.952), (x0 * 0.5 + x1 * 0.5, -0.090, 0.910), (x1, -0.110, 0.878), (xt, -0.104, zt)], w, 0.0))
# face framing: from the temples down past the cheeks, waving out, tips on the collar (left one shorter: the cat)
for s in SIDES:
    zt = 0.700 if s < 0 else 0.742
    LOCK_ITEMS.append(([mirror((-0.080, -0.060, 0.925), s), mirror((-0.112, -0.062, 0.850), s),
                        mirror((-0.110, -0.054, 0.790), s), mirror((-0.126, -0.046, 0.745), s), mirror((-0.118, -0.050, zt), s)],
                       0.026, 0.06))
    LOCK_ITEMS.append(([mirror((-0.090, -0.020, 0.935), s), mirror((-0.124, -0.024, 0.850), s),
                        mirror((-0.122, -0.010, 0.770), s), mirror((-0.136, 0.000, 0.715), s), mirror((-0.122, 0.006, 0.665), s)],
                       0.030, -0.04))
# back curtain: long locks from the crown down to the shoulder blades, alternating waves
BACK_AZ = (-62, -40, -20, 0, 20, 40, 62)
for i, az in enumerate(BACK_AZ):
    k = abs(az) / 62.0
    wv = 0.010 if i % 2 == 0 else -0.010
    zt = 0.555 + 0.050 * k + (0.012 if i % 2 else 0.0)
    c = [ring(180 + az, 0.090, 0.950), ring(180 + az, 0.122, 0.870), ring(180 + az, 0.124 + wv, 0.790),
         ring(180 + az * 0.92, 0.112 - wv * 0.6, 0.715)]
    lo = []
    for z, rr in ((0.655, 0.110 + wv), (0.600, 0.104 - wv)):
        x, y, _ = ring(180 + az * 0.85, rr, z)
        lo.append((x, max(y, 0.096 + 0.012 * (1 - k)), z))
    x, y, _ = ring(180 + az * 0.8, 0.100, zt)
    lo.append((x, max(y, 0.090), zt))
    LOCK_ITEMS.append((c + lo, 0.036 if i % 2 == 0 else 0.032, (0.07, -0.05, 0.03, 0.08, -0.03, 0.05, -0.06)[i]))
# back-sides (behind the ears, down to the shoulders)
for s in SIDES:
    for az, zt, w in ((100, 0.680, 0.030), (125, 0.640, 0.032)):
        a = s * az
        LOCK_ITEMS.append(([ring(a * 0.9, 0.095, 0.940), ring(a, 0.124, 0.860), ring(a, 0.130, 0.780), ring(a * 0.97, 0.122, 0.720),
                            ring(a * 0.94, 0.120, zt)], w, 0.0))
# filler under the curtain (no gaps between the locks)
HFILL = U(Ell((0, 0.058, 0.760), (0.100, 0.060, 0.120)), Ell((0, 0.088, 0.650), (0.080, 0.030, 0.075)), k=0.03)
HAIR = U(CAP, HFILL, *[lock(c, w * 1.05, tone=t) for c, w, t in LOCK_ITEMS], k=0.004)
part("hair", HAIR, "hair", ("Head",))

# ---------------- hat: wide brim (front lifted), crown bending back and to the left, honey band, star buckle -------------
HAT_C = np.array([0.0, 0.012, 0.926])
HAT_AX = nrm((0.0, 0.12, 1.0))
BRIM = RCyl(HAT_C, HAT_AX, 0.232, 0.0075, 0.0070)
CROWN_P = [HAT_C + (0, 0.000, -0.010), HAT_C + (0, 0.010, 0.070), (0.010, 0.050, 1.070), (0.036, 0.090, 1.128),
           (0.070, 0.128, 1.150), (0.100, 0.150, 1.142)]
CROWN_R = [0.116, 0.096, 0.066, 0.040, 0.020, 0.008]
CROWN = U(*[RCone(CROWN_P[i], CROWN_P[i + 1], CROWN_R[i], CROWN_R[i + 1]) for i in range(len(CROWN_P) - 1)], k=0.006)
HAT_BODY = U(BRIM, CROWN, k=0.020)
part("hat", HAT_BODY, "plum", ("Head",))
BAND_Z = 0.956
part("hat_band", I(Off(CROWN, 0.0055), Slab(HAT_AX, HAT_C + HAT_AX * (BAND_Z - HAT_C[2]), 0.0145), k=0.003), "honey", ("Head",))
STAR = []
for i in range(10):
    a = math.pi / 2 + i * math.pi / 5
    r = 0.020 if i % 2 == 0 else 0.0085
    STAR.append((-0.046 + r * math.cos(a), BAND_Z + 0.002 + r * math.sin(a)))
part("hat_star", I(Off(CROWN, 0.0095), Poly2(STAR, "xz"), Half((0, 1, 0), (0, -0.04, 0)), k=0.0025), "honey", ("Head",))

# ---------------- the cat Ugolyok on the left shoulder (Extra) ----------------
CAT = np.array([0.158, 0.012, 0.708])
CAT_H = CAT + (0.006, -0.094, 0.036)
cat_body = Ell(CAT, (0.050, 0.074, 0.040), rot((0, 0, 1), 6))
cat_head = U(Ell(CAT_H, (0.044, 0.038, 0.037)), *[Ell(CAT_H + (s * 0.020, -0.018, -0.012), (0.022, 0.020, 0.018)) for s in SIDES],
             Ell(CAT_H + (0, -0.034, -0.008), (0.012, 0.010, 0.009)), k=0.012)
cat_ears = [RCone(CAT_H + (s * 0.024, 0.002, 0.026), CAT_H + (s * 0.032, 0.004, 0.060), 0.016, 0.003) for s in SIDES]
TAIL = [CAT + (0.004, 0.064, -0.004), CAT + (0.022, 0.092, -0.050), CAT + (0.030, 0.096, -0.105), CAT + (0.022, 0.082, -0.150),
        CAT + (0.002, 0.070, -0.165)]
cat_tail = U(*[RCone(TAIL[i], TAIL[i + 1], (0.013, 0.012, 0.011, 0.010)[i], (0.012, 0.011, 0.010, 0.009)[i]) for i in range(4)], k=0.004)
cat_paws = [Ell(CAT + (s * 0.024, -0.058, -0.030), (0.013, 0.016, 0.018)) for s in SIDES]
CAT_N = U(U(cat_body, cat_head, k=0.022), *cat_ears, cat_tail, *cat_paws, k=0.010)
part("cat", CAT_N, "cat", ("fixed", {"Extra": 1.0}))
CAT_EYE = {}
for s in SIDES:
    p = trace(cat_head.d, CAT_H + (s * 0.017, -0.12, 0.006), (0, 1, 0))
    CAT_EYE[s] = p


# =====================================================================================================================
# paint (colour sRGB 0..1 + gloss) on the exact surface
# =====================================================================================================================
MAT = {  # colour ramp (position, sRGB hex) over the baked light value, kind, ao strength, gloss
    "plum": (((0.0, "#1E0C22"), (0.40, "#411D47"), (0.72, "#5B2A5E"), (1.0, "#8C5290")), "plastic", 0.58, 0.30),
    "plum_l": (((0.0, "#2E1434"), (0.45, "#5E2E64"), (0.75, "#7A3F7E"), (1.0, "#A671AA")), "plastic", 0.55, 0.35),
    "plum_d": (((0.0, "#12071A"), (0.5, "#2C1432"), (1.0, "#4E2A55")), "plastic", 0.50, 0.25),
    "honey": (((0.0, "#4A2E0A"), (0.35, "#8A5A18"), (0.62, "#C8902E"), (0.85, "#E8A93A"), (1.0, "#FFD98A")), "metal", 0.50, 0.85),
    "sash": (((0.0, "#6A3A0E"), (0.45, "#C2822A"), (0.78, "#E8A93A"), (1.0, "#FFD27A")), "plastic", 0.55, 0.45),
    "skin": (((0.0, "#4E2918"), (0.45, "#87533A"), (0.75, "#A66A49"), (1.0, "#CF9472")), "plastic", 0.45, 0.18),
    "hair": (((0.0, "#08050C"), (0.45, "#181120"), (0.75, "#2C2140"), (1.0, "#5A4678")), "plastic", 0.70, 0.40),
    "sock": (((0.0, "#100814"), (0.5, "#2A1830"), (1.0, "#4A3052")), "plastic", 0.55, 0.15),
    "shoe": (((0.0, "#1C0E07"), (0.5, "#4A2A1C"), (1.0, "#80503A")), "plastic", 0.50, 0.60),
    "cat": (((0.0, "#050507"), (0.5, "#15151D"), (0.8, "#272736"), (1.0, "#4A4A66")), "plastic", 0.55, 0.45),
    "glass": (((0.0, "#9A7A48"), (0.5, "#EAD8B0"), (1.0, "#FFF8E8")), "plastic", 0.30, 1.0),
    "cork": (((0.0, "#4A2E16"), (0.5, "#8A5A32"), (1.0, "#BE9060")), "plastic", 0.45, 0.20),
}
RAMP = {k: (np.array([p for p, _ in v[0]]), np.array([H(c) for _, c in v[0]])) for k, v in MAT.items()}


def paint_flat(key, spec=0.0):
    def f(P, N, occ):
        return shade(key, N, occ, spec), np.full(len(P), gloss_of(key))
    return f


def honey_trim(c, g, w, N, occ):
    c = stroke(c, w, shade("honey", N, occ))
    return c, np.where(w > 0.5, gloss_of("honey"), g)


def paint_suit(P, N, occ):
    c = shade("plum", N, occ, 0.12)
    g = np.full(len(P), gloss_of("plum"))
    front = sstep(0.2, 0.6, -N[:, 1])
    # sleeve cuffs: a honey band at the bell opening, a thin one at the puff
    for s in SIDES:
        dn = ARM_DN[s]
        t = (P - WR[s]) @ dn
        rad = np.linalg.norm((P - WR[s]) - t[:, None] * dn, axis=1)
        w = line_w(np.abs(t + 0.004), 0.0055) * (rad > 0.030)
        c, g = honey_trim(c, g, w, N, occ)
        sl = (P - SH[s]) @ nrm(EL[s] - SH[s])
        c = stroke(c, line_w(np.abs(sl - 0.036), 0.0022) * 0.9, H("#7A3F7E"))
    # bodice: honey buttons down the front, darker side panels
    x, z = P[:, 0], P[:, 2]
    for zb in (0.530, 0.570, 0.610):
        r = np.sqrt(x ** 2 + (z - zb) ** 2)
        c, g = honey_trim(c, g, line_w(r, 0.0062) * front * (z < 0.65), N, occ)
        c = stroke(c, line_w(np.abs(r - 0.0068), 0.0010) * front * 0.5, H("#2A1030"))
    side = sstep(0.040, 0.060, np.abs(x)) * (z < 0.64) * (z > 0.49) * (np.abs(x) < 0.095)
    c = stroke(c, side * 0.35, H("#2E1434"))
    return c, g


STARS = []
RNG = np.random.default_rng(7)
for i in range(14):
    az = (i / 14) * 2 * math.pi + RNG.uniform(-0.15, 0.15)
    STARS.append((az, RNG.uniform(0.315, 0.400), RNG.uniform(0.0055, 0.0080)))


def star_mask(u, v, r):
    a = np.arctan2(v, u)
    rr = np.sqrt(u * u + v * v)
    lim = r * (0.42 + 0.58 * np.abs(np.cos(a * 2.5)) ** 3)
    return 1 - sstep(lim - 0.0006, lim + 0.0006, rr)


def paint_skirt(P, N, occ):
    c = shade("plum", N, occ, 0.10)
    g = np.full(len(P), gloss_of("plum"))
    z = P[:, 2]
    hem = sstep(0.304, 0.298, z)
    c = stroke(c, hem, shade("plum_d", N, occ))
    c, g = honey_trim(c, g, line_w(np.abs(z - 0.301), 0.0032), N, occ)
    az = np.arctan2(P[:, 0], -P[:, 1])
    rr = np.sqrt(P[:, 0] ** 2 + (P[:, 1] - 0.005) ** 2)
    for a0, z0, r in STARS:
        da = (az - a0 + math.pi) % (2 * math.pi) - math.pi
        m = star_mask(da * rr, z - z0, r)
        c = stroke(c, m * 0.95, H("#E8B04A"))
    return c, g


def paint_leg(P, N, occ):
    c = shade("sock", N, occ, 0.08)
    stripe = sstep(0.35, 0.65, np.abs(np.sin(P[:, 2] * math.pi / 0.032)))
    c = stroke(c, stripe * (P[:, 2] < 0.29)[:, None][:, 0] * 0.9, shade("plum_l", N, occ))
    return c, np.full(len(P), 0.15)


def paint_shoe(s):
    def f(P, N, occ):
        c = shade("shoe", N, occ, 0.45)
        g = np.full(len(P), gloss_of("shoe"))
        sole = sstep(0.012, 0.007, P[:, 2])
        c = stroke(c, sole, H("#1A0E08"))
        # strap over the instep with a honey button on the outside
        ys = P[:, 1] + 0.012
        c = stroke(c, line_w(np.abs(ys), 0.0065) * (P[:, 2] > 0.040), shade("plum_d", N, occ))
        b = np.sqrt((P[:, 0] - s * 0.104) ** 2 + (P[:, 1] + 0.012) ** 2 + (P[:, 2] - 0.058) ** 2)
        c, g = honey_trim(c, g, line_w(b, 0.0060), N, occ)
        return c, g
    return f


def paint_collar(P, N, occ):
    c = shade("plum_l", N, occ, 0.12)
    g = np.full(len(P), gloss_of("plum_l"))
    edge = sstep(0.6705, 0.6675, P[:, 2]) * sstep(-0.2, 0.3, -N[:, 2] + 0.5)
    c, g = honey_trim(c, g, edge, N, occ)
    return c, g


def fig_part_d(name, P):
    return PARTS_BY[name].node.d(np.asarray(P, F32))


def paint_sash(P, N, occ):
    c = shade("sash", N, occ, 0.30)
    g = np.full(len(P), gloss_of("sash"))
    rib = line_w(np.abs(P[:, 2] - SASH_Z - 0.0115), 0.0012) + line_w(np.abs(P[:, 2] - SASH_Z + 0.0115), 0.0012)
    c = stroke(c, np.clip(rib, 0, 1) * 0.5, H("#8A5414"))
    return c, g


def paint_glow(P, N, occ):
    lam = np.clip(N @ KEY_L, 0, 1)
    v = sstep(-0.3, 0.9, N[:, 2]) * 0.4 + lam * 0.4
    c = mixc(H("#E08A1E"), H("#FFE08A"), v)
    sp = np.clip(N @ HALF_V, 0, 1) ** 30
    c = c + (1 - c) * sp[:, None]
    return np.clip(c, 0, 1), np.full(len(P), 1.0)


def paint_vials(P, N, occ):
    c, g = paint_glow(P, N, occ)
    top = P[:, 2] > SASH_Z - 0.030 + 0.010
    corkm = P[:, 2] > SASH_Z - 0.030 + 0.024
    c = np.where(top[:, None], shade("glass", N, occ, 0.6), c)
    c = np.where(corkm[:, None], shade("cork", N, occ), c)
    return c, np.where(corkm, 0.2, g)


EYE_C = {s: np.array([s * 0.0465, 0.8020]) for s in SIDES}
ELIXA_EYE = dict(white=H("#FFF8F0"), iris_out=H("#7A4410"), iris_in=H("#E0A23A"), pupil=H("#1A1008"), spark=H("#FFFFFF"),
                 lash=H("#120A10"))


def paint_head(P, N, occ):
    c = shade("skin", N, occ, 0.10)
    g = np.full(len(P), gloss_of("skin"))
    x, z = P[:, 0], P[:, 2]
    front = sstep(0.1, 0.5, -N[:, 1])
    for s in SIDES:
        b = np.exp(-(((x - s * 0.058) / 0.020) ** 2 + ((z - 0.766) / 0.013) ** 2)) * front
        c = stroke(c, b * 0.40, H("#C0604E"))
    nose = np.exp(-((x / 0.012) ** 2 + ((z - 0.772) / 0.009) ** 2)) * front
    c = stroke(c, nose * 0.25, H("#B86A50"))
    # mouth: a small knowing smirk, the character's left corner (+X) up, a tiny dimple there
    u = x
    vm = 0.7410 - 0.0020 * (1 - (u / 0.024) ** 2) + 0.0040 * sstep(0.0, 0.026, u) ** 2
    half = 0.00150 * np.clip(1 - (np.abs(u - 0.002) / 0.028) ** 4, 0, 1)
    wm = line_w(np.abs(z - vm), half, 0.0005) * (u > -0.026) * (u < 0.029) * front
    c = stroke(c, wm, H("#4A1C18"))
    lip = np.exp(-(((u - 0.002) / 0.016) ** 2 + ((z - 0.7345) / 0.0038) ** 2)) * front
    c = stroke(c, lip * 0.30, H("#9A4A44"))
    dim = np.sqrt((u - 0.031) ** 2 + (z - 0.7445) ** 2)
    c = stroke(c, line_w(dim, 0.0012) * front * 0.5, H("#5E2E22"))
    for s_ in SIDES:
        c = brow_stroke(c, x, z, s_, front)
    for s in SIDES:
        c, g = eye_paint(c, g, x - EYE_C[s][0], z - EYE_C[s][1], s, ELIXA_EYE)
        # half-lowered upper lid (grumpy-stylish): skin over the top of the eye
        uu, vv = x - EYE_C[s][0], z - EYE_C[s][1]
        lid = (np.abs(uu) < 0.024) & (vv > 0.0105 - 0.0030 * (uu * s / 0.024)) & (vv < 0.0200)
        c = np.where(lid[:, None], stroke(c, 1.0, shade("skin", N, occ * 1.3, 0.1)), c)
        ll = line_w(np.abs(vv - (0.0105 - 0.0030 * (uu * s / 0.024))), 0.0019 + 0.0010 * sstep(-0.2, 1.0, s * uu / 0.024)) \
            * (np.abs(uu) < 0.0255)
        c = stroke(c, ll, ELIXA_EYE["lash"])
    return c, g


def brow_stroke(c, x, z, s, front):
    V = np.array(BROWS[s], float)
    d, f = seg2(x, z, V)
    inner = f if s > 0 else 1 - f
    half = 0.0060 * (1 - 0.35 * (1 - inner) ** 1.5) + 0.0006
    w = line_w(d, half, 0.0006) * front
    return stroke(c, w, mixc(H("#0E0812"), H("#2A1E36"), sstep(half, 0.0, d) * 0.6))


def paint_brow(P, N, occ):
    c = shade("skin", N, occ, 0.10)
    for s_ in SIDES:
        c = brow_stroke(c, P[:, 0], P[:, 2], s_, np.ones(len(P)))
    return c, np.full(len(P), 0.15)


def paint_hair(P, N, occ):
    n = len(P)
    best = np.full(n, BIG, F32)
    sp, xw, tone = np.zeros(n, F32), np.zeros(n, F32), np.zeros(n, F32)
    for L in LOCKS:
        m = _inbb(P, L.lo, L.hi, 0.006)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        d, s_, x_, _ = L.local(P[idx].astype(F32))
        b = d < best[idx]
        j = idx[b]
        best[j], sp[j], xw[j], tone[j] = d[b], s_[b], x_[b], L.tone
    on = sstep(0.0070, 0.0015, best)
    lam = np.clip(N @ KEY_L, -1, 1)
    light = sstep(-0.85, 0.90, 0.6 * lam + 0.4 * N[:, 2])
    ridge = 1 - np.clip(np.abs(xw), 0, 1) ** 2
    edge = sstep(0.70, 1.0, np.abs(xw))
    v = 0.40 + 0.30 * light + 0.10 * ridge - 0.10 * edge + tone
    v = v * on + (0.36 + 0.24 * light) * (1 - on)
    c = ramp("hair", v)
    # glossy violet sheen band along each lock where it faces the light (black hair reads as hair, not as a hole)
    sheen = np.clip(N @ HALF_V, 0, 1) ** 10 * ridge * on
    c = c + (H("#8C74B8") - c) * (sheen * 0.55)[:, None]
    c = c * (1 - 0.35 * np.minimum(occ, 0.55))[:, None]
    return np.clip(c, 0, 1), np.full(n, 0.40)


def paint_hat(P, N, occ):
    c = shade("plum", N, occ, 0.10)
    g = np.full(len(P), gloss_of("plum"))
    rel = P - HAT_C
    h = rel @ HAT_AX
    rr = np.linalg.norm(rel - h[:, None] * HAT_AX, axis=1)
    under = sstep(0.0, -0.6, N @ HAT_AX) * (rr > 0.10)
    c = stroke(c, under * 0.7, shade("plum_d", N, occ))
    c, g = honey_trim(c, g, line_w(np.abs(rr - 0.214), 0.0032) * sstep(0.2, 0.6, N @ HAT_AX), N, occ)
    # crown: lighter toward the tip, two patch stitches
    c = stroke(c, sstep(1.02, 1.15, P[:, 2])[:, None][:, 0] * 0.35, H("#8C5290"))
    pz = P[:, 2]
    patch = (np.abs(P[:, 0] - 0.032) < 0.022) & (np.abs(pz - 1.045) < 0.016) & (P[:, 1] < 0.05)
    c = np.where(patch[:, None], stroke(c, 0.6, H("#7A3F7E")), c)
    for k in range(3):
        st = line_w(np.abs(P[:, 0] - 0.016 - k * 0.016), 0.0012) * (np.abs(pz - 1.045) < 0.020) * (P[:, 1] < 0.05)
        c = stroke(c, st * 0.8, H("#E8A93A"))
    return c, g


def paint_cat(P, N, occ):
    c = shade("cat", N, occ, 0.30)
    g = np.full(len(P), gloss_of("cat"))
    sheen = np.clip(N @ HALF_V, 0, 1) ** 12 * 0.35
    c = c + (H("#6A6A9A") - c) * sheen[:, None]
    for s in SIDES:
        e = CAT_EYE[s]
        d = P - e
        u, v = d[:, 0], d[:, 2]
        front = sstep(0.2, 0.6, -N[:, 1]) * (np.abs(d[:, 1]) < 0.012)
        r = np.sqrt((u / 0.0105) ** 2 + (v / 0.0090) ** 2)
        eye = (1 - sstep(0.92, 1.05, r)) * front
        col = mixc(H("#FFE08A"), H("#E8A030"), sstep(0.0, 1.0, r))
        c = stroke(c, eye, col)
        slit = (1 - sstep(0.0018, 0.0026, np.abs(u - s * 0.0005))) * (r < 0.85)
        c = stroke(c, slit * front, H("#0A0806"))
        hl = np.sqrt((u + s * 0.003) ** 2 + (v - 0.003) ** 2)
        c = stroke(c, line_w(hl, 0.0018) * front, H("#FFFFFF"))
        g = np.where(eye > 0.5, 1.0, g)
    nose_c = CAT_H + np.array([0.0, -0.043, -0.004])
    nd = np.linalg.norm(P - nose_c, axis=1)
    c = stroke(c, line_w(nd, 0.0050), H("#C07A8A"))
    for s in SIDES:  # inner ears
        ea, eb = CAT_H + (s * 0.024, 0.002, 0.026), CAT_H + (s * 0.032, 0.004, 0.060)
        t = np.clip(((P - ea) @ (eb - ea)) / ((eb - ea) @ (eb - ea)), 0, 1)
        dd = np.linalg.norm(P - (ea + t[:, None] * (eb - ea)), axis=1)
        inner = (N[:, 1] < -0.3) & (t > 0.15) & (t < 0.8)
        c = stroke(c, inner * 0.6, H("#5A3A4A"))
    return c, g


PARTS_BY = {p.name: p for p in PARTS}


def assign_paints():
    for p in PARTS:
        n = p.name
        if n == "suit":
            f = paint_suit
        elif n == "skirt":
            f = paint_skirt
        elif n.startswith("leg_"):
            f = paint_leg
        elif n.startswith("shoe_"):
            f = paint_shoe(-1 if n.endswith("R") else 1)
        elif n.startswith("hand_") or n == "neck":
            f = paint_flat("skin", 0.08)
        elif n == "collar":
            f = paint_collar
        elif n in ("sash", "bow"):
            f = paint_sash
        elif n == "vials":
            f = paint_vials
        elif n == "head":
            f = paint_head
        elif n.startswith("brow"):
            f = paint_brow
        elif n == "hair":
            f = paint_hair
        elif n == "hat":
            f = paint_hat
        elif n in ("hat_band", "hat_star"):
            f = paint_flat("honey")
        elif n == "cat":
            f = paint_cat
        else:
            f = paint_flat(p.key if p.key in MAT else "plum")
        p.paint = f


assign_paints()


# =====================================================================================================================
# game mesh: shells, UV scale factors, bones, weights
# =====================================================================================================================
SHELLS = [  # name, part-name prefixes, game triangles
    ("head", ("head", "neck", "brow", "hair"), 1430),
    ("hat", ("hat",), 440),
    ("hand_R", ("hand_R",), 160),
    ("hand_L", ("hand_L",), 160),
    ("cat", ("cat",), 400),
    ("body", (), 1390),
]


def face_factor(C, own_names):
    f = np.ones(len(C))
    z, y, x = C[:, 2], C[:, 1], C[:, 0]
    head = np.char.startswith(own_names, "head")
    f[head] = 1.35
    face = head & (y < -0.04) & (z < 0.87) & (np.abs(x) < 0.105)
    f[face] = 2.4
    for pre, val in (("brow", 2.0), ("hand_", 1.1), ("cat", 1.25), ("leg_", 0.8), ("shoe_", 0.8),
                     ("vials", 0.8), ("hair", 0.9), ("hat", 0.55), ("skirt", 0.85)):
        f[np.char.startswith(own_names, pre)] = val
    f[np.char.startswith(own_names, "hat_star")] = 1.4
    catface = np.char.startswith(own_names, "cat") & (y < CAT_H[1] - 0.01)
    f[catface] = 1.9
    return f


PROP_HEAD = HANDF[-1][0]
BONE_DEF = [  # name, parent, head, tail (final coordinates before the z shift)
    ("Root", None, (0, 0, 0), (0, 0, 0.42)),
    ("Body", "Root", (0, 0, 0.42), (0, 0, 0.675)),
    ("Head", "Body", (0, 0, 0.675), (0, 0, 0.98)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", PROP_HEAD, PROP_HEAD + np.array([0, -0.10, 0])),
    ("EyeR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("EyeL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("LidR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("LidL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("Extra", "Body", CAT, CAT_H),
]
BONES = [b[0] for b in BONE_DEF]


def suit_weights(P):
    groups = [("torso", TORSO)]
    for s in SIDES:
        puff, up, bell = ARM[s].ch
        groups += [("Arm" + SN[s], puff), ("Arm" + SN[s], up), ("Arm" + SN[s], bell)]
    D = np.stack([np.maximum(g.d(P.astype(F32)), 0) for _, g in groups], 0)
    Wt = np.exp(-(D - D.min(0)) / 0.010)
    # the shoulder blend: the torso near the shoulder joint keeps some arm weight, the puff keeps some body weight
    zb = sstep(0.45, 0.52, P[:, 2])
    names = [n for n, _ in groups]
    out = []
    for j in range(len(P)):
        w = {}
        for i, nm in enumerate(names):
            v = Wt[i, j]
            if v < 0.02:
                continue
            if nm == "torso":
                w["Root"] = w.get("Root", 0) + v * (1 - zb[j])
                w["Body"] = w.get("Body", 0) + v * zb[j]
            else:
                w[nm] = w.get(nm, 0) + v
        out.append(w)
    return out


# =====================================================================================================================
# weapon: honey potion flask (its own SDF figure, shell and 512 texture); origin = the right-hand grip, forward = -Y
# =====================================================================================================================
FL_C = np.array([0.0, -0.062, 0.030])
FL_R = (0.050, 0.050, 0.053)
W_PARTS = []


def build_weapon_parts():
    global PARTS
    keep = PARTS
    PARTS = []
    bulb = Ell(FL_C, FL_R)
    neck = RCone(FL_C + (0, 0.040, -0.002), (0, 0.004, 0.026), 0.020, 0.0155)
    lip = Torus((0, 0.002, 0.026), (0, 1, 0), 0.0165, 0.0042)
    part("flask_glass", U(U(bulb, neck, k=0.014), lip, k=0.003), "glass", ("Prop",))
    part("flask_cork", RCyl((0, 0.016, 0.0255), (0, 1, 0), 0.0150, 0.0125, 0.0045, taper=0.002), "cork", ("Prop",))
    part("flask_strap", U(Torus(FL_C, (1, 0, 0), 0.0505, 0.0050), Torus(FL_C + (0, 0.036, 0.0), (0, 1, 0), 0.0345, 0.0045), k=0.004),
         "honey", ("Prop",))
    star = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5
        r = 0.024 if i % 2 == 0 else 0.010
        star.append((r * math.cos(a), FL_C[2] + r * math.sin(a)))
    part("flask_star", I(Off(bulb, 0.0045), Poly2(star, "xz"), Half((0, 1, 0), (0, -0.085, 0)), k=0.002), "honey", ("Prop",))
    out = PARTS
    PARTS = keep
    return out


def paint_flask_glass(P, N, occ):
    rel = P - FL_C
    level = 0.012 + 0.004 * np.sin(rel[:, 0] * 60)
    liquid = sstep(level + 0.002, level - 0.002, rel[:, 2]) * (rel[:, 1] < 0.034)
    lam = np.clip(N @ KEY_L, 0, 1)
    rim = sstep(0.55, 0.1, np.abs(N @ np.array([0, -1.0, 0])))
    liq = mixc(H("#FFD86A"), H("#E8901E"), sstep(-0.05, 0.03, rel[:, 2]))
    liq = mixc(liq, H("#C25E10"), rim * 0.6)
    glass = mixc(H("#F4E6C6"), H("#FFFFFF"), lam * 0.5)
    glass = mixc(glass, H("#C9A878"), rim * 0.5)
    c = mixc(glass, liq, liquid)
    c = stroke(c, line_w(np.abs(rel[:, 2] - level), 0.0015) * (rel[:, 1] < 0.03), H("#FFF0B0"))
    # bubbles and a sparkle highlight
    for bx, bz, br in ((-0.018, -0.010, 0.0040), (0.012, -0.022, 0.0030), (0.022, 0.000, 0.0025), (-0.006, -0.030, 0.0022)):
        d = np.sqrt((rel[:, 0] - bx) ** 2 + (rel[:, 2] - bz) ** 2)
        c = stroke(c, line_w(np.abs(d - br), 0.0009) * (N[:, 1] < -0.3) * 0.9, H("#FFF6D0"))
    sp = np.clip(N @ HALF_V, 0, 1) ** 40
    c = c + (1 - c) * sp[:, None]
    return np.clip(c, 0, 1), np.full(len(P), 1.0)


def assign_weapon_paints(parts):
    for p in parts:
        p.paint = {"flask_glass": paint_flask_glass, "flask_cork": paint_flat("cork")}.get(p.name, paint_flat("honey"))


# =====================================================================================================================
# main
# =====================================================================================================================
def save_tex(img, path):
    return save_texture(img, path)


def make_weapon(parts, h):
    global PARTS, SHELLS, TEX, MESH_NAME, BOUNDS
    saved = (PARTS, SHELLS, TEX, MESH_NAME, BOUNDS)
    PARTS, SHELLS, TEX, MESH_NAME = parts, [("flask", (), 1180)], 512, "ElixaFlask"
    BOUNDS = (np.array([-0.08, -0.14, -0.05]), np.array([0.08, 0.06, 0.10]))
    try:
        low, sid = build_shells(h)
        uv = unwrap(low)
        img = bake_texture(low, uv, sid)
        tex_path = os.path.join(OUT_DIR, "elixa_weapon_tex.png")
        save_texture(img, tex_path)
        V, T = mesh_np(low)
        me = low.data
        me.calc_loop_triangles()
        lt = np.empty(len(me.loop_triangles) * 3, np.int64)
        me.loop_triangles.foreach_get("loops", lt)
        uvc = uv[lt]
        bpy.data.objects.remove(low, do_unlink=True)
        ob = to_mesh("ElixaFlask", V, T)
        ob.data.uv_layers.new(name="UVMap").data.foreach_set("uv", np.asarray(uvc, np.float64).ravel())
        smp = sample_tex(img, uvc, blur=2)
        attr = ob.data.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
        attr.data.foreach_set("color", np.hstack([srgb_to_lin(smp[:, :3]), smp[:, 3:4]]).astype(np.float32).ravel())
        mat = bpy.data.materials.new("FighterVC")
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = bpy.data.images.load(tex_path, check_existing=True)
        tn.image.alpha_mode = "CHANNEL_PACKED"
        nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
        ob.data.materials.append(mat)
        for p in ob.data.polygons:
            p.use_smooth = True
        mz = bpy.data.objects.new("Muzzle", None)
        mz.empty_display_size = 0.02
        mz.location = (0.0, FL_C[1] - FL_R[1] - 0.006, FL_C[2])
        bpy.context.scene.collection.objects.link(mz)
        mz.parent = ob
        base = os.path.join(OUT_DIR, "elixa_weapon")
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        mz.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH", "EMPTY"}, add_leaf_bones=False,
                                 apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                                 mesh_smooth_type="FACE", bake_anim=False, path_mode="STRIP")
        bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
        log(f"RESULT weapon tris {len(T)}/1200 verts {len(V)}, muzzle {tuple(np.round(mz.location, 3))}")
        return len(T)
    finally:
        PARTS, SHELLS, TEX, MESH_NAME, BOUNDS = saved


def full_stage():
    t_start = time.time()
    low, sid = build_shells(HRES)
    V, T = mesh_np(low)
    shift = np.array([0.0, 0.0, -V[:, 2].min()])
    log("shift", np.round(shift, 4))
    uv_loops = unwrap(low)
    img = bake_texture(low, uv_loops, sid)
    tex_path = os.path.join(OUT_DIR, "elixa_tex.png")
    save_texture(img, tex_path)
    me = low.data
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    uvc = uv_loops[lt]
    W = vertex_weights(V, sid)
    bpy.data.objects.remove(low, do_unlink=True)
    ob, arm = make_object("Elixa", V, T, uvc, W, img, tex_path, shift, 1.0)
    export(ob, arm, os.path.join(OUT_DIR, "elixa_game"))
    infl = max(len(w) for w in W)
    smooth = sum(1 for w in W if len(w) > 1)
    log(f"RESULT game tris {len(T)}/4000 verts {len(V)} bones {len(BONES)} max infl {infl}, multi-weight verts {smooth}")
    log("prop head", np.round(PROP_HEAD + shift, 3), "eyes", np.round(EYE[-1][0] + shift, 3), np.round(EYE[1][0] + shift, 3),
        "height", round(float(V[:, 2].max() + shift[2]), 3))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    wp = build_weapon_parts()
    assign_weapon_paints(wp)
    make_weapon(wp, 0.0012)
    log(f"full stage {time.time() - t_start:.0f}s")


bpy.ops.wm.read_factory_settings(use_empty=True)
full_stage()
log("done")
