# Brumm (Docs/Roster-v4.md 6.4, Docs/Briefs/A-Roster3D.md): a big good-natured 16-year-old in round barrel power armour
# made from a water tank, porthole helmet, a pot with the cactus "Spike" on his left shoulder, chunky cone gun in both hands.
# Same method and format as Theo v3 (build_theo_v3.py: SDF library, surface nets, decimation, smart UV, per-texel bake,
# rig, export are copied from it; the model, paints and the weapon are Brumm's own). Headless, numpy only:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_brumm.py -- [out_dir]
# Output (default Art/Source/3D/brumm/): brumm_game.fbx/.glb (<= 4000 tris), brumm_weapon.fbx/.glb (<= 1200 tris, origin =
# grip on the Prop bone, barrel along -Y, empty "Muzzle" at the barrel end), brumm_tex.png (1024 RGBA: RGB albedo, A gloss).
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
POS = [a for a in ARGV if not a.startswith("--")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "brumm")
os.makedirs(OUT_DIR, exist_ok=True)
T0 = time.time()


def log(*a):
    print(f"[brumm {time.time() - T0:6.1f}s]", *a, flush=True)


F32 = np.float32
BIG = F32(10.0)


# ---- library copied from build_theo_v3.py ----
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


PAL = {
    "red": ("#B3141C", 0.75), "red_dark": ("#7E0B12", 0.60), "red_light": ("#D8262C", 0.75),
    "gold": ("#E0A93A", 0.88), "gold_dark": ("#9C6A1F", 0.70), "gold_light": ("#FFE08C", 0.95),
    "graphite": ("#3D3836", 0.12), "graphite_d": ("#2B2725", 0.16), "graphite_l": ("#58514D", 0.14),
    "belt": ("#4A403B", 0.25), "metal": ("#A8A29E", 0.80), "headset": ("#55504D", 0.45),
    "skin": ("#F6C7A6", 0.18), "hair": ("#E3B04E", 0.35), "visor": ("#FF9A2E", 1.0),
    "glow": ("#FFB040", 1.0), "glow_hot": ("#FFE08A", 1.0), "cyan": ("#46D3D6", 1.0),
}


class Part:
    def __init__(self, name, node, key, bone, paint=None, prio=0):
        self.name, self.node, self.key, self.bone, self.paint, self.prio = name, node, key, bone, paint, prio
        self.lo, self.hi = node.lo, node.hi


PARTS = []


def part(name, node, key, bone, paint=None, prio=0):
    p = Part(name, node, key, bone, paint, prio)
    PARTS.append(p)
    return p


SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}

# =====================================================================================================================
# figure evaluation
# =====================================================================================================================
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
# paint: colour (sRGB 0..1) and gloss on the exact surface, per part (decals, trims, seams, eyes, AO, metal banding)
# =====================================================================================================================
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
MAT = {  # colour ramp (position, sRGB hex) over the baked light value, kind, ao strength, gloss
    "red": (((0.0, "#38030A"), (0.40, "#780A12"), (0.70, "#A7131B"), (1.0, "#D83A32")), "plastic", 0.62, 0.75),
    "gold": (((0.0, "#4A2E0A"), (0.35, "#8A5A18"), (0.62, "#C88E2E"), (0.85, "#E8B850"), (1.0, "#FFE08E")), "metal", 0.50, 0.88),
    "graphite": (((0.0, "#28231F"), (0.5, "#4E4642"), (1.0, "#7A706A")), "plastic", 0.62, 0.14),
    "graphite_d": (((0.0, "#1E1A18"), (0.5, "#37312E"), (1.0, "#5A514C")), "plastic", 0.55, 0.20),
    "belt": (((0.0, "#28221F"), (0.5, "#4A403B"), (1.0, "#6C605A")), "plastic", 0.55, 0.25),
    "headset": (((0.0, "#2A2725"), (0.5, "#534E4B"), (1.0, "#857E79")), "plastic", 0.45, 0.45),
    "skin": (((0.0, "#CF7A5C"), (0.5, "#F5B790"), (1.0, "#FFDCC2")), "plastic", 0.48, 0.18),
    "hair": (((0.0, "#8A5216"), (0.45, "#CF913A"), (0.75, "#E9B45A"), (1.0, "#F8D58A")), "plastic", 0.85, 0.35),
    "brow": (((0.0, "#7E5420"), (0.5, "#B0823A"), (1.0, "#CFA25A")), "plastic", 0.30, 0.15),
    "metal": (((0.0, "#5E5853"), (0.5, "#ACA59F"), (1.0, "#F2EEEA")), "metal", 0.40, 0.80),
}
RAMP = {k: (np.array([p for p, _ in v[0]]), np.array([H(c) for _, c in v[0]])) for k, v in MAT.items()}


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


def glow_line(c, g, d, half):
    w = line_w(d, half, 0.0006)
    core = 1 - sstep(0, half * 0.9, d)
    col = mixc(GLOW_EDGE, mixc(GLOW, GLOW_HOT, core * 0.6), sstep(0, half, half - d) * 0.8 + 0.2)
    return stroke(c, w, col), np.maximum(g, w * 1.0)
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


# =====================================================================================================================
# low poly: decimation with protected head / hands / hair, UVs, texture bake, rig, export
# =====================================================================================================================
BUDGET = {"game": 4000, "show": 20000}
TEX = 1024


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
# wrists into the bracers. (The union of the shells is the same surface as the whole figure.)
SHELLS = [  # name, part-name prefixes, game triangles
    ("head", ("head", "brow", "hair", "visor", "headset"), 1850),
    ("hand_R", ("hand_R", "glove_plate_R", "thumb_plate_R"), 190),
    ("hand_L", ("hand_L", "glove_plate_L", "thumb_plate_L"), 190),
    ("body", (), 1730),
]


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
        for _ in range(10):  # Brumm: wider normal search (big flat barrel triangles), see full_stage note
            Q = P + n * t[:, None]
            d = fn(Q)
            e = F32(3e-4)
            dd = (fn(Q + n * e) - fn(Q - n * e)) / (2 * e)
            stp = np.where(np.abs(dd) > 0.25, d / np.where(np.abs(dd) > 0.25, dd, 1), d)
            t = np.clip(t - np.clip(stp, -0.004, 0.004), -0.025, 0.025).astype(F32)
        Qr = P + n * t[:, None]
        ok = np.abs(fn(Qr)) < 3e-4
        Qc = project(fn, P, it=6, clip=0.003)
        dr, dc = np.linalg.norm(Qr - P, axis=1), np.linalg.norm(Qc - P, axis=1)
        use_r = ok & (dr < 3.0 * dc + 0.004)
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
    im = bpy.data.images.new("brumm_tex", TEX, TEX, alpha=True)
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
# BRUMM MODEL (Blender: Z up, faces -Y, +X = the character's left). Model units, normalised to height 1.0 on export.
# =====================================================================================================================
MAT.update({
    "sand": (((0.0, "#6E5A3A"), (0.40, "#B9A57A"), (0.72, "#E3D3A8"), (1.0, "#FFF7E2")), "plastic", 0.55, 0.62),
    "sand_d": (((0.0, "#5A4830"), (0.45, "#A08C62"), (0.8, "#CDBB8E"), (1.0, "#EADBB4")), "plastic", 0.55, 0.50),
    "teal": (((0.0, "#0C4639"), (0.45, "#2A9580"), (0.78, "#3FB59B"), (1.0, "#A8F2DF")), "plastic", 0.50, 0.90),
    "cactus": (((0.0, "#0F4A3C"), (0.45, "#2B9A80"), (0.8, "#3FB59B"), (1.0, "#8FE6CF")), "plastic", 0.60, 0.30),
    "choc": (((0.0, "#1E130B"), (0.45, "#46301F"), (0.8, "#5A3E2B"), (1.0, "#8C6D55")), "plastic", 0.55, 0.30),
    "clay": (((0.0, "#3A2315"), (0.45, "#74492C"), (0.8, "#94603C"), (1.0, "#C08E68")), "plastic", 0.55, 0.20),
})
RAMP = {k: (np.array([p for p, _ in v[0]]), np.array([H(c) for _, c in v[0]])) for k, v in MAT.items()}
SAND_HI, CHOC, LIGHT, TEAL = H("#FFF7E2"), H("#5A3E2B"), H("#FFF4D6"), H("#3FB59B")

# joints (right side -X; mirrored)
SH = {s: mirror((-0.232, 0.0, 0.565), s) for s in SIDES}
EL = {s: mirror((-0.262, -0.075, 0.455), s) for s in SIDES}
WR = {s: mirror((-0.150, -0.200, 0.410), s) for s in SIDES}
HANDC = {s: mirror((-0.118, -0.226, 0.405), s) for s in SIDES}
HIP = {s: mirror((-0.100, 0.0, 0.245), s) for s in SIDES}
AN = {s: mirror((-0.102, -0.005, 0.065), s) for s in SIDES}

BARREL_C = np.array([0.0, 0.01, 0.435])
BARREL = RCyl(BARREL_C, (0, 0, 1), 0.235, 0.205, 0.085, sxy=(1, 0.88))
part("barrel", BARREL, "sand", ("Body",))
for i, z in enumerate((0.318, 0.560)):
    part(f"band{i}", RCyl((0, 0.01, z), (0, 0, 1), 0.243 if i == 0 else 0.241, 0.018, 0.010, sxy=(1, 0.88)), "choc", ("Body",))
part("gauge", RCyl((0, -0.196, 0.44), (0, -1, 0), 0.062, 0.016, 0.008), "teal", ("Body",))
part("collar", RCyl((0, 0.01, 0.648), (0, 0, 1), 0.118, 0.032, 0.02), "choc", ("Body",))
part("tank_cap", RCyl((0, 0.13, 0.52), (0, 1, 0), 0.05, 0.05, 0.015), "sand_d", ("Body",))   # back filler cap
for s in SIDES:
    n = SN[s]
    part(f"pauldron_{n}", Ell(mirror((-0.218, 0.0, 0.585), s), (0.082, 0.082, 0.072)), "sand", ("Body",))
    part(f"arm_{n}", U(RCone(SH[s], EL[s], 0.058, 0.054), RCone(EL[s], WR[s], 0.054, 0.048), k=0.02), "sand_d", ("Arm" + n,))
    part(f"glove_{n}", U(Ell(HANDC[s], (0.050, 0.046, 0.048)), RCyl(WR[s] * 0.6 + HANDC[s] * 0.4, nrm(HANDC[s] - EL[s]), 0.05, 0.02, 0.01), k=0.015),
         "choc", ("Arm" + n,))
    part(f"leg_{n}", RCyl(mirror((-0.100, 0.0, 0.17), s), (0, 0, 1), 0.072, 0.09, 0.03), "sand_d", ("Leg" + n,))
    part(f"knee_{n}", Torus(mirror((-0.100, 0.0, 0.205), s), (0, 0, 1), 0.07, 0.012), "choc", ("Leg" + n,))
    part(f"boot_{n}", U(Ell(mirror((-0.104, -0.035, 0.052), s), (0.080, 0.108, 0.056)),
                        RCyl(mirror((-0.104, -0.02, 0.016), s), (0, 0, 1), 0.08, 0.016, 0.008, sxy=(1, 1.3)), k=0.02),
         "choc", ("Leg" + n,))

HEAD_C = np.array([0.0, 0.015, 0.800])
HELMET = S(Ell(HEAD_C, (0.152, 0.148, 0.150)), Ell((0, -0.150, 0.790), (0.100, 0.075, 0.098)), k=0.012)
part("helmet", HELMET, "sand", ("Head",))
FACE_C = np.array([0.0, -0.010, 0.792])
part("face", Ell(FACE_C, (0.112, 0.112, 0.108)), "skin", ("Head",))
RIM_C = np.array([0.0, -0.108, 0.792])
part("rim", Torus(RIM_C, (0, -1, 0), 0.100, 0.020, sxy=(1.02, 1.0)), "teal", ("Head",), prio=2)
part("helm_top", RCyl((0, 0.02, 0.945), (0, 0, 1), 0.035, 0.012, 0.006), "choc", ("Head",))

POT_C = np.array([0.218, 0.025, 0.690])
part("pot", RCyl(POT_C, (0, 0, 1), 0.050, 0.040, 0.010, taper=0.008), "clay", ("Body",))
part("pot_rim", Torus(POT_C + (0, 0, 0.038), (0, 0, 1), 0.057, 0.010), "clay", ("Body",))
part("cactus", U(Ell(POT_C + (0, 0, 0.088), (0.036, 0.036, 0.066)),
                 Ell(POT_C + (0.046, 0, 0.098), (0.018, 0.018, 0.028)), Ell(POT_C + (0.030, 0, 0.082), (0.022, 0.016, 0.012)),
                 k=0.012), "cactus", ("Body",))
part("flower", Ell(POT_C + (-0.004, -0.012, 0.155), (0.018, 0.018, 0.012)), "light", ("Body",), prio=1)

# eyes on the face (centre on the surface, gaze -Y)
EYE = {}
for s in SIDES:
    e = trace(lambda P: Ell(FACE_C, (0.112, 0.112, 0.108)).d(P), (s * 0.040, -0.3, 0.805), (0, 1, 0))
    EYE[s] = (e, np.array([0.0, -1.0, 0.0]))


def paint_flat2(key, spec=0.15):
    def f(P, N, occ):
        return shade(key, N, occ, spec), np.full(len(P), gloss_of(key))
    return f


def paint_barrel(P, N, occ):
    c = shade("sand", N, occ, 0.35)
    q = P - BARREL_C
    ang = np.degrees(np.arctan2(q[:, 0], -q[:, 1] / 0.88))
    # vertical welding seams every 60 deg (not across the gauge), horizontal rivet rows next to the bands
    da = np.abs(((ang + 30) % 60) - 30)
    seam = (1 - sstep(1.0, 1.6, da)) * (np.abs(ang) > 20) * (P[:, 2] > 0.33) * (P[:, 2] < 0.545)
    c = stroke(c, seam * 0.75, H("#7A6646"))
    for z in (0.345, 0.533):
        ra = np.abs(((ang + 7.5) % 15) - 7.5) * 0.0036
        dd = np.sqrt(ra ** 2 + (P[:, 2] - z) ** 2)
        riv = 1 - sstep(0.006, 0.0085, dd)
        hi = 1 - sstep(0.0, 0.006, np.sqrt((ra + 0.002) ** 2 + (P[:, 2] - z - 0.002) ** 2))
        c = stroke(c, riv, mixc(H("#B49C6C"), SAND_HI, hi))
    # top lid ring and a small teal "level" stripe on the back
    c = stroke(c, (1 - sstep(0.004, 0.006, np.abs(np.sqrt(q[:, 0] ** 2 + (q[:, 1] / 0.88) ** 2) - 0.15))) * (N[:, 2] > 0.6), H("#9C8760"))
    return c, np.full(len(P), 0.62)


def paint_band(P, N, occ):
    c = shade("choc", N, occ, 0.4)
    q = P - BARREL_C
    ang = np.degrees(np.arctan2(q[:, 0], -q[:, 1] / 0.88))
    ra = np.abs(((ang + 10) % 20) - 10) * 0.0038
    riv = (1 - sstep(0.008, 0.011, ra)) * (np.abs(N[:, 2]) < 0.6)
    c = stroke(c, riv, H("#C9B489"))
    return c, np.where(riv > 0.5, 0.8, 0.35)


def paint_gauge(P, N, occ):
    c = shade("teal", N, occ, 0.9)
    r = np.sqrt(P[:, 0] ** 2 + (P[:, 2] - 0.44) ** 2)
    c = stroke(c, 1 - sstep(0.044, 0.047, r) * (1 - sstep(0.050, 0.053, r)) - (r < 0.044), H("#1C5E50"))
    c = stroke(c, sstep(0.048, 0.052, r), H("#5A3E2B"))
    # needle and a highlight
    nd = seg2(P[:, 0], P[:, 2], np.array([(0, 0.44), (0.026, 0.462)]))[0]
    c = stroke(c, (1 - sstep(0.003, 0.0045, nd)) * (r < 0.044), H("#FFF4D6"))
    hl = 1 - sstep(0.006, 0.010, np.sqrt((P[:, 0] + 0.022) ** 2 + (P[:, 2] - 0.462) ** 2))
    return stroke(c, hl, H("#E8FFF8")), np.full(len(P), 1.0)


def paint_face(P, N, occ):
    c = shade("skin", N, occ, 0.1)
    for s in SIDES:
        e = EYE[s][0]
        u, v = (P[:, 0] - e[0]) / 0.020, (P[:, 2] - e[2]) / 0.026
        r = np.sqrt(u * u + v * v)
        front = P[:, 1] < -0.06
        c = stroke(c, (1 - sstep(0.92, 1.05, r)) * front, H("#2B1A10"))
        iris = 1 - sstep(0.70, 0.80, np.sqrt(u * u + (v + 0.05) ** 2))
        c = stroke(c, iris * front * 0.55, H("#6A4224"))
        sp = 1 - sstep(0.22, 0.32, np.sqrt((u + 0.3) ** 2 + (v - 0.38) ** 2))
        sp2 = 1 - sstep(0.10, 0.16, np.sqrt((u - 0.28) ** 2 + (v + 0.36) ** 2))
        c = stroke(c, (sp + sp2) * front, H("#FFFFFF"))
        # brows: soft brown arcs, kind (outer ends lower)
        bd = seg2(P[:, 0], P[:, 2], np.array([(e[0] - s * 0.020, e[2] + 0.034), (e[0], e[2] + 0.040), (e[0] + s * 0.022, e[2] + 0.033)]))[0]
        c = stroke(c, (1 - sstep(0.0035, 0.005, bd)) * front, H("#6B4A2C"))
        # cheeks
        ch = 1 - sstep(0.0, 0.022, np.sqrt((P[:, 0] - s * 0.062) ** 2 + (P[:, 2] - 0.770) ** 2))
        c = stroke(c, ch * 0.35 * front, H("#F08C7A"))
    # smile
    xs = np.linspace(-0.030, 0.030, 9)
    sm = np.stack([xs, 0.758 + 220 * xs ** 2 * 0.06 + 0.0], 1)
    sd = seg2(P[:, 0], P[:, 2], sm)[0]
    c = stroke(c, (1 - sstep(0.0028, 0.0042, sd)) * (P[:, 1] < -0.06), H("#7A3A28"))
    return c, np.full(len(P), 0.18)


def paint_helmet(P, N, occ):
    c = shade("sand", N, occ, 0.35)
    q = P - HEAD_C
    # chocolate seam ring around the helmet (side to side over the top) and rivets around the porthole
    c = stroke(c, 1 - sstep(0.0035, 0.0055, np.abs(q[:, 0] - 0.0) * (q[:, 1] > 0.02) + (q[:, 1] <= 0.02) * 1), H("#7A6646"))
    a = np.arctan2(P[:, 2] - RIM_C[2], P[:, 0])
    r = np.sqrt(P[:, 0] ** 2 + (P[:, 2] - RIM_C[2]) ** 2)
    ra = np.abs(((np.degrees(a) + 22.5) % 45) - 22.5) * 0.0024
    riv = (1 - sstep(0.006, 0.009, np.sqrt(ra ** 2 + (r - 0.142) ** 2))) * (P[:, 1] < -0.04)
    c = stroke(c, riv, mixc(H("#B49C6C"), SAND_HI, 0.4))
    return c, np.full(len(P), 0.62)


def paint_rim(P, N, occ):
    c = shade("teal", N, occ, 0.9)
    return c, np.full(len(P), 0.95)


def paint_cactus(P, N, occ):
    c = shade("cactus", N, occ, 0.15)
    q = P - (POT_C + (0, 0, 0.088))
    a = np.degrees(np.arctan2(q[:, 1], q[:, 0]))
    rib = np.abs(((a + 22.5) % 45) - 22.5)
    c = stroke(c, (1 - sstep(2.0, 5.0, rib)) * 0.35, H("#1E6A58"))
    # tiny soft spine dots (light, rounded, kid-safe)
    dz = np.abs(((q[:, 2] + 0.01) % 0.025) - 0.0125)
    dots = (1 - sstep(1.5, 3.0, rib)) * (1 - sstep(0.002, 0.0035, dz))
    c = stroke(c, dots, H("#FFF4D6"))
    return c, np.full(len(P), 0.3)


def paint_light(P, N, occ):
    c = mixc(H("#FFF4D6"), H("#FFE3A0"), sstep(0.0, 0.5, -N[:, 2]))
    return c, np.full(len(P), 1.0)


def paint_pot(P, N, occ):
    c = shade("clay", N, occ, 0.1)
    d = np.abs(P[:, 2] - (POT_C[2] - 0.004))
    c = stroke(c, (1 - sstep(0.004, 0.006, d)) * (N[:, 2] < 0.5), TEAL)  # teal strap holding the pot
    return c, np.where(d < 0.005, 0.5, 0.2)


PAINT_BY = {"barrel": paint_barrel, "gauge": paint_gauge, "face": paint_face, "helmet": paint_helmet, "rim": paint_rim,
            "cactus": paint_cactus, "flower": paint_light, "pot": paint_pot}
for p in PARTS:
    if p.name in PAINT_BY:
        p.paint = PAINT_BY[p.name]
    elif p.name.startswith("band"):
        p.paint = paint_band
    elif p.key == "light":
        p.paint = paint_light
    else:
        p.paint = paint_flat2(p.key, 0.3 if p.key in ("sand", "sand_d", "choc") else 0.1)
PARTS_BY = {p.name: p for p in PARTS}

SHELLS = [  # name, part-name prefixes, game triangles
    ("head", ("helmet", "face", "rim", "helm_top"), 1250),
    ("pot", ("pot", "cactus", "flower"), 430),
    ("arms", ("arm_", "glove_"), 560),
    ("body", (), 1740),
]
BOUNDS = (np.array([-0.40, -0.34, -0.03]), np.array([0.40, 0.30, 1.02]))


def face_factor(C, own_names):
    f = np.ones(len(C))
    f[C[:, 2] > 0.65] = 1.3
    f[np.char.startswith(own_names, "face")] = 2.6
    f[np.char.startswith(own_names, "gauge")] = 1.8
    f[np.char.startswith(own_names, "cactus")] = 1.6
    return f


def build_shells(h):
    Vs, Ts, sid, base = [], [], [], 0
    for k, (name, _, budget) in enumerate(SHELLS):
        parts = shell_parts(k)
        fn = shell_fn(parts)
        lo = np.maximum(np.min([p.lo for p in parts], 0) - 0.02, BOUNDS[0])
        hi = np.minimum(np.max([p.hi for p in parts], 0) + 0.02, BOUNDS[1])
        Vh, Th = mesh_field(fn, h, lo, hi)
        V, T = build_low(Vh, Th, budget, fn, name)
        Vs.append(V)
        Ts.append(T + base)
        sid.append(np.full(len(V), k))
        base += len(V)
    V, T, sid = np.concatenate(Vs), np.concatenate(Ts), np.concatenate(sid)
    ob = to_mesh("Brumm", V, T)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob, sid


PROP_HEAD = HANDC[-1]
BONE_DEF = [
    ("Root", None, (0, 0, 0), (0, 0, 0.245)),
    ("Body", "Root", (0, 0, 0.245), (0, 0, 0.655)),
    ("Head", "Body", (0, 0, 0.655), (0, 0, 0.96)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", PROP_HEAD, PROP_HEAD + np.array([0, -0.10, 0])),
    ("EyeR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("EyeL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("LidR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("LidL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
]
BONES = [b[0] for b in BONE_DEF]


# =====================================================================================================================
# weapon: "Cone Gun" - a chunky toy multi-barrel of pipes and a valve wheel (vertex colours, alpha = gloss)
# =====================================================================================================================
def build_weapon(scale):
    """Local model units: origin = right-hand grip (Prop), barrel along -Y, +X towards the left hand."""
    bpy.ops.object.select_all(action="DESELECT")
    objs = []
    cx = abs(HANDC[-1][0]) + HANDC[1][0]   # housing centred between the hands
    cx *= 0.5

    def add(kind, loc, rot=(0, 0, 0), col="#E3D3A8", gloss=0.6, **kw):
        if kind == "cyl":
            bpy.ops.mesh.primitive_cylinder_add(location=loc, rotation=rot, **kw)
        elif kind == "torus":
            bpy.ops.mesh.primitive_torus_add(location=loc, rotation=rot, **kw)
        elif kind == "sphere":
            bpy.ops.mesh.primitive_uv_sphere_add(location=loc, rotation=rot, **kw)
        elif kind == "cone":
            bpy.ops.mesh.primitive_cone_add(location=loc, rotation=rot, **kw)
        o = bpy.context.active_object
        me = o.data
        lin = srgb_to_lin(H(col))
        a = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
        a.data.foreach_set("color", np.tile(np.r_[lin, gloss], len(me.loops)).astype(np.float32))
        objs.append(o)
        return o
    RY = (math.pi / 2, 0, 0)
    gx = cx + HANDC[-1][0] * 0 + 0.118   # = x offset of the housing centre from the right hand
    gx = cx   # right hand at local x 0, left hand at local x = 2 * cx
    # rear block (body of the gun) with a rounded back
    add("cyl", (gx, -0.03, 0.03), RY, "#CDBB8E", 0.55, vertices=16, radius=0.078, depth=0.16)
    add("sphere", (gx, 0.05, 0.03), (0, 0, 0), "#B9A57A", 0.55, segments=12, ring_count=5, radius=0.076)
    # chocolate bands
    for y in (-0.10, 0.03):
        add("cyl", (gx, y, 0.03), RY, "#5A3E2B", 0.35, vertices=16, radius=0.084, depth=0.022)
    # round shroud and four barrels
    add("cyl", (gx, -0.20, 0.03), RY, "#5A3E2B", 0.4, vertices=16, radius=0.046, depth=0.18)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        add("cyl", (gx + 0.034 * math.cos(a), -0.25, 0.03 + 0.034 * math.sin(a)), RY, "#3A2A1E", 0.5, vertices=8, radius=0.016, depth=0.20)
    add("torus", (gx, -0.33, 0.03), RY, "#E3D3A8", 0.65, major_segments=12, minor_segments=5, major_radius=0.058, minor_radius=0.016)
    add("torus", (gx, -0.235, 0.03), RY, "#E3D3A8", 0.65, major_segments=12, minor_segments=4, major_radius=0.056, minor_radius=0.012)
    # valve wheel on top + its stem, hopper of soft pine cones (teal box with cones peeking)
    add("cyl", (gx, -0.06, 0.12), (0, 0, 0), "#5A3E2B", 0.4, vertices=8, radius=0.008, depth=0.04)
    add("torus", (gx, -0.06, 0.14), (0, 0, 0), "#3FB59B", 0.9, major_segments=12, minor_segments=4, major_radius=0.034, minor_radius=0.008)
    for k in range(3):
        a = math.radians(60 * k)
        add("cyl", (gx, -0.06, 0.14), (math.pi / 2, 0, a), "#3FB59B", 0.9, vertices=6, radius=0.004, depth=0.066)
    add("cyl", (gx + 0.085, 0.0, 0.07), (0, 0, 0), "#3FB59B", 0.8, vertices=12, radius=0.036, depth=0.06)
    for k in range(3):
        add("cone", (gx + 0.085 + 0.016 * math.cos(k * 2.1), 0.016 * math.sin(k * 2.1), 0.115), (0, 0, 0), "#8A5A30", 0.3,
            vertices=8, radius1=0.016, radius2=0.004, depth=0.032)
    # steam pipe on the side
    add("cyl", (gx - 0.085, -0.06, 0.04), RY, "#E3D3A8", 0.6, vertices=8, radius=0.012, depth=0.16)
    # grips under the hands
    add("cyl", (0.0, 0.0, -0.005), (0.25, 0, 0), "#5A3E2B", 0.3, vertices=10, radius=0.022, depth=0.09)
    add("cyl", (2 * cx, -0.0, -0.005), (0.25, 0, 0), "#5A3E2B", 0.3, vertices=10, radius=0.022, depth=0.09)
    # muzzle cap inside the shroud (warm light only when firing -> dark here)
    add("cyl", (gx, -0.345, 0.03), RY, "#2A1C12", 0.3, vertices=12, radius=0.044, depth=0.006)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    w = bpy.context.active_object
    w.name = w.data.name = "BrummWeapon"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for v in w.data.vertices:
        v.co = v.co * scale
    m = w.modifiers.new("Tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=m.name)
    for p in w.data.polygons:
        p.use_smooth = True
    mat = bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    bs = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    ca = mat.node_tree.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    mat.node_tree.links.new(ca.outputs["Color"], bs.inputs["Base Color"])
    bs.inputs["Roughness"].default_value = 0.45
    w.data.color_attributes.active_color = w.data.color_attributes["Col"]
    w.data.materials.clear()
    w.data.materials.append(mat)
    mz = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(mz)
    mz.location = Vector((gx * scale, -0.35 * scale, 0.03 * scale))
    mz.parent = w
    tris = len(w.data.polygons)
    return w, mz, tris


def full_stage():
    Vh, Th = mesh_field(fig_d, 0.004, *BOUNDS)
    lo, hi = Vh.min(0), Vh.max(0)
    del Vh, Th
    shift = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
    scale = 1.0 / (hi[2] - lo[2])
    log("normalisation shift", np.round(shift, 4), "scale", round(scale, 4), "width", round((hi[0] - lo[0]) * scale, 3))
    low, sid = build_shells(0.003)
    uv_loops = unwrap(low)
    img = bake_texture(low, uv_loops, sid)
    tex_path = os.path.join(OUT_DIR, "brumm_tex.png")
    save_texture(img, tex_path)
    V, T = mesh_np(low)
    me = low.data
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    uvc = uv_loops[lt]
    W = vertex_weights(V, sid)
    bpy.data.objects.remove(low, do_unlink=True)
    ob, arm = make_object("Brumm", V, T, uvc, W, img, tex_path, shift, scale)
    export(ob, arm, os.path.join(OUT_DIR, "brumm_game"))
    log(f"RESULT body tris {len(T)}/4000 verts {len(V)} bones {len(BONES)} max infl {max(len(w) for w in W)}")
    log("prop head", np.round((PROP_HEAD + shift) * scale, 3), "eyes", np.round((EYE[-1][0] + shift) * scale, 3))
    bpy.ops.object.select_all(action="DESELECT")
    w, mz, wt = build_weapon(scale)
    bpy.ops.object.select_all(action="DESELECT")
    w.select_set(True)
    mz.select_set(True)
    bpy.context.view_layer.objects.active = w
    base = os.path.join(OUT_DIR, "brumm_weapon")
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH", "EMPTY"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True, export_vertex_color="ACTIVE")
    log(f"RESULT weapon tris {wt}/1200, muzzle {tuple(round(c, 3) for c in mz.location)}")


bpy.ops.wm.read_factory_settings(use_empty=True)
full_stage()
log("done")
