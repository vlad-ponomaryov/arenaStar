# Dozer (tank, sleepy pillow-fight champion, 13) base model, Docs/Briefs/A-Roster3D.md. Headless (Blender 5.2, numpy):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_dozer.py -- [out_dir]
# SDF library copied from build_kovach.py / build_theo_v3.py; one shell, baked 1024 texture, 13 bones (+Extra = cap tail).
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector
T0 = time.time()
def log(*a):
    print(f"[dozer {time.time() - T0:6.1f}s]", *a, flush=True)
F32 = np.float32
BIG = F32(10.0)


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





# =====================================================================================================================
# Dozer: palette, parts, figure
# =====================================================================================================================
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "dozer")
os.makedirs(OUT_DIR, exist_ok=True)
TEX = 1024
BODY_BUDGET, WEAPON_BUDGET = 3950, 1180

PAL = {  # sRGB, gloss (alpha). Roster-v3 2.1: lime / chocolate / cream / warm gold (energy only)
    "lime": ("#B6F03A", 0.35), "lime_d": ("#86B82A", 0.30), "choc": ("#5A3A2A", 0.12), "choc_d": ("#3E271C", 0.15),
    "cream": ("#FFF1DC", 0.25), "gold": ("#FFD45A", 1.0), "skin": ("#F4C6A6", 0.22), "blush": ("#F09A8C", 0.25),
    "hair": ("#E9C25E", 0.30), "mask": ("#3B3446", 0.35), "white": ("#FBF7F0", 1.0), "pupil": ("#2A1D16", 1.0),
    "lip": ("#B65A52", 0.40), "sole": ("#EADCC4", 0.20), "plaid": ("#B6F03A", 0.20), "case": ("#FFF1DC", 0.25),
}


def plaid_col(P, base):
    """Lime pyjama check: darker lime bands both ways + thin cream lines."""
    u, v = P[:, 0] / 0.07, P[:, 2] / 0.07
    fu, fv = u - np.floor(u), v - np.floor(v)
    band = ((fu < 0.32).astype(float) + (fv < 0.32).astype(float))
    col = base * (1.0 - 0.17 * band)[:, None]
    line = (np.abs(fu - 0.66) < 0.05) | (np.abs(fv - 0.66) < 0.05)
    col[line] = col[line] * 0.4 + hexc("#FFF1DC") * 0.6
    return col


def case_col(P, base):
    """Pillowcase: cream with lime stripes across the width (world X)."""
    u = P[:, 0] / 0.11 + 0.5
    f = u - np.floor(u)
    col = base.copy()
    st = (f > 0.38) & (f < 0.62)
    col[st] = hexc("#B6F03A")
    return col


def knit_col(P, base):
    """Subtle vertical knit ribs on the sweater."""
    u = P[:, 0] / 0.022
    f = u - np.floor(u)
    return base * (1.0 - 0.07 * (np.abs(f - 0.5) < 0.18))[:, None]


PATTERN = {"plaid": plaid_col, "case": case_col, "choc": knit_col}


class Part:
    def __init__(self, name, node, key, bone, prio=0):
        self.name, self.node, self.key, self.bone, self.prio = name, node, key, bone, prio
        self.lo, self.hi = node.lo, node.hi


def mk(parts, name, node, key, bone, prio=0):
    parts.append(Part(name, node, key, bone, prio))


SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}
BODY, WEAPON = [], []

# --- torso: big soft kid in a baggy knitted sweater (no muscles), short legs in plaid pyjama trousers -----------------
TORSO = U(Ell((0, 0.0, 0.40), (0.19, 0.15, 0.17)), Ell((0, 0.0, 0.50), (0.215, 0.14, 0.09)),
          Ell((0, 0.01, 0.28), (0.18, 0.14, 0.07)), k=0.07)
SH = {s: mirror((-0.215, 0.0, 0.515), s) for s in SIDES}
EL = {s: mirror((-0.275, -0.10, 0.415), s) for s in SIDES}
WR = {s: mirror((-0.295, -0.215, 0.405), s) for s in SIDES}
HD = {s: mirror((-0.300, -0.255, 0.405), s) for s in SIDES}
HIP = {s: mirror((-0.090, 0.0, 0.25), s) for s in SIDES}
AN = {s: mirror((-0.105, 0.0, 0.065), s) for s in SIDES}
SHOULDER = {s: Ell(SH[s], (0.095, 0.095, 0.085)) for s in SIDES}
UPPER = {s: RCone(SH[s], EL[s], 0.072, 0.064) for s in SIDES}
SWEATER = U(TORSO, *SHOULDER.values(), *UPPER.values(), k=0.045)
mk(BODY, "sweater", SWEATER, "choc", "torso")
mk(BODY, "hem", I(Off(TORSO, 0.012), Slab((0, 0, 1), (0, 0, 0.245), 0.028), k=0.004), "choc_d", "torso", prio=2)
mk(BODY, "collar", Torus((0, 0.0, 0.575), (0, 0, 1), 0.075, 0.03, sxy=(1.15, 1.0)), "choc_d", "Body", prio=2)
for s in SIDES:
    axis = np.array(WR[s]) - EL[s]
    mk(BODY, "fore_" + SN[s], RCone(EL[s], WR[s], 0.064, 0.060), "choc", "Arm" + SN[s])
    mk(BODY, "cuff_" + SN[s], RCyl(np.array(EL[s]) + axis * 0.85, axis, 0.066, 0.022, 0.01), "choc_d", "Arm" + SN[s],
       prio=2)
    mk(BODY, "hand_" + SN[s], U(Ell(HD[s], (0.055, 0.058, 0.060)),
                                Ell(np.array(HD[s]) + mirror((0.035, -0.02, 0.025), s), (0.022, 0.024, 0.026)), k=0.02),
       "skin", "Arm" + SN[s])
    mk(BODY, "leg_" + SN[s], RCone(HIP[s], AN[s] + np.array([0, 0, 0.03]), 0.092, 0.080), "plaid", "Leg" + SN[s])
    # big fluffy slippers on a rubber sole
    sc = AN[s] + np.array([0, -0.04, -0.025])
    mk(BODY, "slipper_" + SN[s], U(Ell(sc + np.array([0, 0, 0.012]), (0.078, 0.112, 0.052)),
                                   Ell(AN[s] + np.array([0, 0.0, 0.02]), (0.07, 0.07, 0.05)), k=0.03),
       "choc", "Leg" + SN[s], prio=1)
    mk(BODY, "sole_" + SN[s], I(Ell(sc, (0.082, 0.116, 0.05)), Half((0, 0, 1), (0, 0, sc[2] - 0.022)), k=0.004),
       "sole", "Leg" + SN[s], prio=2)
mk(BODY, "waist", I(Off(TORSO, 0.004), Slab((0, 0, 1), (0, 0, 0.215), 0.03), k=0.004), "plaid", "Root", prio=1)

# --- head: big round head (~0.33 of the height), round cheeks, sleepy half-closed eyes, straw tufts --------------------
NECK = RCone((0, 0.0, 0.54), (0, 0.0, 0.62), 0.07, 0.07)
mk(BODY, "neck", NECK, "skin", "Body")
HC = np.array([0, 0.0, 0.745])
SKULL = U(Ell(HC, (0.165, 0.158, 0.16)), Ell(HC + np.array([0, -0.03, -0.055]), (0.15, 0.125, 0.105)), k=0.05)
CHEEKS = [Ell(mirror((-0.085, -0.115, 0.700), s), (0.055, 0.045, 0.045)) for s in SIDES]
NOSE = Ell((0, -0.163, 0.725), (0.024, 0.02, 0.022))
EARS = [Ell(mirror((-0.163, 0.01, 0.735), s), (0.02, 0.03, 0.036)) for s in SIDES]
HEAD = U(SKULL, *CHEEKS, NOSE, *EARS, k=0.025)
mk(BODY, "head", HEAD, "skin", "Head")
for i, s in enumerate(SIDES):
    mk(BODY, "blush_" + SN[s], I(Off(CHEEKS[i], 0.004), Half((0, 1, 0), (0, -0.13, 0)), k=0.004), "blush", "Head", prio=3)
# gap-tooth smile: dark mouth with two front teeth and a gap
mk(BODY, "mouth", Ell((0, -0.150, 0.665), (0.040, 0.016, 0.014)), "lip", "Head", prio=3)
for s in SIDES:
    mk(BODY, "tooth_" + SN[s], RBox(mirror((-0.012, -0.160, 0.672), s), (0.008, 0.006, 0.007), 0.002), "white", "Head",
       prio=4)
EYE = {}
for s in SIDES:
    c = mirror((-0.058, -0.143, 0.758), s)
    g = nrm((s * 0.25, -1, 0.05))
    EYE[s] = (c, g)
    mk(BODY, "eye_" + SN[s], Ell(c, (0.030, 0.013, 0.030)), "white", "Head", prio=3)
    mk(BODY, "pupil_" + SN[s], Ell(c + g * 0.010 + np.array([0, 0, -0.006]), (0.015, 0.006, 0.016)), "pupil", "Head",
       prio=4)
    # heavy sleepy upper lid: covers the top half of the eye
    mk(BODY, "lid_" + SN[s], I(Ell(c + np.array([0, 0.002, 0.0]), (0.034, 0.017, 0.034)),
                               Half((0, 0, -1), c + np.array([0, 0, 0.002])), k=0.003), "skin", "Head", prio=5)
    mk(BODY, "brow_" + SN[s], RCone(c + mirror((-0.022, -0.006, 0.036), s), c + mirror((0.018, -0.008, 0.040), s),
                                     0.007, 0.006), "hair", "Head", prio=3)
# sleep mask pushed up on the forehead
MASK = I(Off(SKULL, 0.007), Slab((0, 0, 1), (0, 0, 0.828), 0.024), Half((0, 1, 0), (0, -0.03, 0)), k=0.004)
mk(BODY, "mask", MASK, "mask", "Head", prio=3)
mk(BODY, "mask_band", I(Off(SKULL, 0.005), Slab((0, 0, 1), (0, 0, 0.835), 0.010), k=0.003), "mask", "Head", prio=2)
# straw tufts sticking out under the cap (sides and back)
for s in SIDES:
    for a, z, l in ((-0.6, 0.86, 0.07), (-0.2, 0.85, 0.08), (0.3, 0.84, 0.075), (0.75, 0.835, 0.07)):
        base = HC + np.array([s * 0.15 * math.cos(a), 0.15 * math.sin(a) + 0.02, z - HC[2]])
        tip = base + np.array([s * l * 0.85 * math.cos(a), l * 0.6 * math.sin(a) + 0.01, -0.035])
        mk(BODY, f"tuft_{SN[s]}_{a}", RCone(base, tip, 0.024, 0.006), "hair", "Head", prio=1)
mk(BODY, "fringe", RCone((0.04, -0.13, 0.86), (0.075, -0.16, 0.815), 0.022, 0.006), "hair", "Head", prio=1)

# --- long nightcap with a pompon trailing backwards (main top-view sign), crescent night-light on its front -----------
CAP_IN = I(Off(SKULL, 0.014), Half((0, 0, -1), (0, 0, 0.845)), k=0.01)
CAP_PTS = bez((0, 0.0, 0.87), (0, 0.02, 1.02), (0, 0.20, 1.04), (0, 0.33, 0.93), 9)
CAP_R = np.linspace(0.15, 0.03, 9)
CAP_SEG = [RCone(CAP_PTS[i], CAP_PTS[i + 1], CAP_R[i], CAP_R[i + 1]) for i in range(5)]
TAIL_SEG = [RCone(CAP_PTS[i], CAP_PTS[i + 1], CAP_R[i], CAP_R[i + 1]) for i in range(5, 8)]
mk(BODY, "cap", U(CAP_IN, *CAP_SEG, k=0.03), "lime", "Head")
mk(BODY, "cap_tail", U(*TAIL_SEG, k=0.02), "lime", "Extra")
mk(BODY, "cap_brim", I(Off(SKULL, 0.024), Slab((0, 0, 1), (0, 0, 0.862), 0.018), k=0.006), "lime_d", "Head", prio=2)
POM = CAP_PTS[-1] + np.array([0, 0.02, -0.03])
mk(BODY, "pompon", Ell(POM, (0.052, 0.052, 0.052)), "cream", "Extra", prio=1)
MOON_C = np.array([0.0, -0.140, 0.935])
MOON = S(RCyl(MOON_C, (0, -1, 0.35), 0.042, 0.012, 0.006),
         RCyl(MOON_C + np.array([0.022, -0.004, 0.012]), (0, -1, 0.35), 0.036, 0.03, 0.0), k=0.003)
mk(BODY, "moon", MOON, "gold", "Head", prio=4)
PROP_HEAD = np.array(HD[-1], float)


# --- pillow "Cloud" (separate mesh; origin = the right-hand grip, forward = -Y, up = +Z) -----------------------------
def W(p):
    return np.asarray(p, float) - PROP_HEAD


PIL_C = np.array([0.0, -0.31, 0.43])
PR = rot((1, 0, 0), 28)  # top edge tilted forward
PILLOW = U(RBox(W(PIL_C), (0.30, 0.04, 0.215), 0.035, R=PR), Ell(W(PIL_C), (0.27, 0.088, 0.19), R=PR), k=0.05)
CORNERS = []
for sx in (-1, 1):
    for sz in (-1, 1):
        cc = PIL_C + PR @ np.array([sx * 0.29, 0, sz * 0.205])
        CORNERS.append(Ell(W(cc), (0.035, 0.03, 0.035), R=PR))
mk(WEAPON, "pillow", U(PILLOW, *CORNERS, k=0.02), "case", "Prop")
# grandma's star patch on the front face (hero-LOD detail, reads as a small gold-ish spot)
STAR_C = PIL_C + PR @ np.array([0.13, -0.085, 0.09])
star = []
for i in range(10):
    a = math.pi / 2 + i * math.pi / 5
    r = 0.045 if i % 2 == 0 else 0.019
    star.append((r * math.cos(a), r * math.sin(a)))


class Moved(Node):
    """Child evaluated in a frame: P -> (P - c) @ R, with xy polygon star laid onto the pillow face."""

    def __init__(self, ch, c, R, lo, hi):
        self.ch, self.c, self.R = ch, np.asarray(c, F32), np.asarray(R, F32)
        self.lo, self.hi = lo, hi

    def d(self, P):
        return self.ch.d(((P - self.c) @ self.R).astype(F32))


STAR_LOCAL = Poly2(star, "xz", e=(-0.2, 0.2))
STAR_N = Moved(STAR_LOCAL, W(STAR_C), PR, W(STAR_C) - 0.06, W(STAR_C) + 0.06)
mk(WEAPON, "star", I(Off(PILLOW, 0.005), STAR_N, k=0.002), "gold", "Prop", prio=3)


def fig_fn(parts):
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


def owner(parts, P):
    P = np.asarray(P, F32)
    best = np.full(len(P), BIG, F32)
    own = np.zeros(len(P), np.int32)
    for i, p in enumerate(parts):
        m = _inbb(P, p.lo, p.hi, 0.01)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        d = p.node.d(P[idx]) - F32(4e-4) * p.prio
        b = d < best[idx]
        best[idx[b]] = d[b]
        own[idx[b]] = i
    return own


KEY_L = nrm((-0.35, -0.6, 0.72))
HALF_V = nrm(KEY_L + np.array([0, -1.0, 0]))


def paint(parts, fn, P):
    N = grad(fn, P, e=2.5e-4)
    N = N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
    occ = np.zeros(len(P), F32)
    for h, w in ((0.004, 0.3), (0.01, 0.28), (0.02, 0.22), (0.035, 0.15)):
        d = fn((P + N * h).astype(F32))
        occ += F32(w) * np.clip((h - d) / h, 0, 1)
    own = owner(parts, P)
    base = np.array([hexc(PAL[p.key][0]) for p in parts])[own]
    keys = np.array([p.key for p in parts])[own]
    for k, f in PATTERN.items():
        m = keys == k
        if m.any():
            base[m] = f(np.asarray(P)[m], base[m])
    gl = np.array([PAL[p.key][1] for p in parts])[own]
    flat = np.array([p.key in ("gold", "white", "pupil") for p in parts])[own]
    lam = np.clip(N @ KEY_L, -1, 1)
    light = 0.82 + 0.26 * lam - 0.32 * np.clip(occ, 0, 1)
    spec = np.clip(N @ HALF_V, 0, 1) ** 24 * gl * 0.45
    col = base * light[:, None] + spec[:, None]
    col = np.where(flat[:, None], base * (0.95 + 0.1 * lam[:, None]), col)
    return np.clip(col, 0, 1), gl, own
def build(parts, budget, name, h, lo, hi):
    fn = fig_fn(parts)
    Vh, Th = mesh_field(fn, h, lo, hi)
    ob = to_mesh(name, Vh, Th)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bad = [e for e in bm.edges if not e.is_manifold and not e.is_boundary]
    if bad:
        bmesh.ops.split_edges(bm, edges=bad)
    bm.to_mesh(ob.data)
    bm.free()
    decimate_to(ob, budget)
    V, T = mesh_np(ob)
    V = project(fn, V, it=3, clip=0.0015).astype(np.float64)
    ob.data.vertices.foreach_set("co", V.ravel())
    ob.data.update()
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=1.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    me = ob.data
    V, T = mesh_np(ob)
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    lt = lt.reshape(-1, 3)
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uvc = uv.reshape(-1, 2)[lt]
    tri, bary = raster(uvc, TEX)
    has = tri >= 0
    ti, b = tri[has], bary[has].astype(np.float64)
    P0 = V[T[ti, 0]] * b[:, :1] + V[T[ti, 1]] * b[:, 1:2] + V[T[ti, 2]] * b[:, 2:3]
    Q = project(fn, P0, it=6, clip=0.003)
    cols, gls = np.zeros((len(Q), 3)), np.zeros(len(Q))
    CH = 150000
    for c0 in range(0, len(Q), CH):
        c, g, _ = paint(parts, fn, Q[c0:c0 + CH])
        cols[c0:c0 + CH], gls[c0:c0 + CH] = c, g
    img = np.zeros((TEX * TEX, 4), np.float32)
    img[has, :3], img[has, 3] = cols, gls
    img = dilate(img.reshape(TEX, TEX, 4), has.reshape(TEX, TEX))
    own = owner(parts, V)
    bpy.data.objects.remove(ob, do_unlink=True)
    log(f"{name}: {len(T)} tris {len(V)} verts, texels {has.mean() * 100:.0f}%")
    return V, T, uvc.reshape(-1, 2), img, own


def save_png(img, path, name):
    im = bpy.data.images.new(name, TEX, TEX, alpha=True)
    im.alpha_mode = "CHANNEL_PACKED"
    im.pixels.foreach_set(np.ascontiguousarray(img, np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()


def make_mesh(name, V, T, uvc, img, tex_path):
    ob = to_mesh(name, V, T)
    me = ob.data
    me.uv_layers.new(name="UVMap").data.foreach_set("uv", np.asarray(uvc, np.float64).ravel())
    smp = sample_tex(img, uvc, blur=3)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(smp[:, :3]), smp[:, 3:4]]).astype(np.float32).ravel())
    mat = bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = bpy.data.images.load(tex_path, check_existing=True)
    tn.image.alpha_mode = "CHANNEL_PACKED"
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def export(objs, base, types):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[-1]
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types=types, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)




def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    Vb, Tb, Ub, Ib, own = build(BODY, BODY_BUDGET, "Dozer", 0.003, (-0.40, -0.32, -0.02), (0.40, 0.44, 1.22))
    zmin, zmax = Vb[:, 2].min(), Vb[:, 2].max()
    shift = np.array([-(Vb[:, 0].min() + Vb[:, 0].max()) / 2, 0.0, -zmin])  # depth: keep the torso centre at y = 0
    scale = 1.0 / (zmax - zmin)
    tex = os.path.join(OUT_DIR, "dozer_tex.png")
    save_png(Ib, tex, "dozer_tex")
    ob = make_mesh("Dozer", (Vb + shift) * scale, Tb, Ub, Ib, tex)
    BONE_DEF = [("Root", None, (0, 0, zmin), (0, 0, 0.30)), ("Body", "Root", (0, 0, 0.30), (0, 0, 0.58)),
                ("Head", "Body", (0, 0, 0.58), (0, 0, 0.92)),
                ("ArmR", "Body", SH[-1], WR[-1]), ("ArmL", "Body", SH[1], WR[1]),
                ("LegR", "Root", HIP[-1], AN[-1]), ("LegL", "Root", HIP[1], AN[1]),
                ("Prop", "Body", PROP_HEAD, PROP_HEAD + np.array([0, -0.10, 0])),
                ("Extra", "Head", CAP_PTS[5], POM)]
    for pre in ("Eye", "Lid"):
        for s in (-1, 1):
            c, g = EYE[s]
            BONE_DEF.append((pre + SN[s], "Head", c, c + g * 0.03))
    ad = bpy.data.armatures.new("DozerRig")
    arm = bpy.data.objects.new("Rig", ad)
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    heads = {}
    for bn, par, hd, tl in BONE_DEF:
        eb = ad.edit_bones.new(bn)
        eb.head = Vector((np.asarray(hd, float) + shift) * scale)
        eb.tail = Vector((np.asarray(tl, float) + shift) * scale)
        heads[bn] = tuple(round(c, 3) for c in eb.head)
        if par:
            eb.parent = ad.edit_bones[par]
    bpy.ops.object.mode_set(mode="OBJECT")
    groups = {b[0]: ob.vertex_groups.new(name=b[0]) for b in BONE_DEF}
    z = Vb[:, 2]
    for i in range(len(Vb)):
        bn = BODY[own[i]].bone
        if bn == "torso":  # waist blend Root <-> Body
            t = float(np.clip((z[i] - 0.27) / 0.08, 0, 1))
            if t < 1:
                groups["Root"].add([i], 1 - t, "REPLACE")
            if t > 0:
                groups["Body"].add([i], t, "REPLACE")
        else:
            groups[bn].add([i], 1.0, "REPLACE")
    ob.parent = arm
    ob.modifiers.new("Armature", "ARMATURE").object = arm
    export([ob, arm], os.path.join(OUT_DIR, "dozer_game"), {"ARMATURE", "MESH"})
    # pillow, same scale as the body so it fits the hands
    Vw, Tw, Uw, Iw, _ = build(WEAPON, WEAPON_BUDGET, "DozerPillow", 0.003, (-0.08, -0.29, -0.32), (0.68, 0.19, 0.32))
    wtex = os.path.join(OUT_DIR, "dozer_weapon_tex.png")
    save_png(Iw, wtex, "dozer_weapon_tex")
    wob = make_mesh("DozerPillow", Vw * scale, Tw, Uw, Iw, wtex)
    export([wob], os.path.join(OUT_DIR, "dozer_weapon"), {"MESH"})
    log(f"RESULT body tris {len(Tb)}/4000 verts {len(Vb)}; weapon tris {len(Tw)}/1200; scale {scale:.4f}")
    log("BONES", heads)
    log("weapon extent", np.round(Vw.min(0) * scale, 3), np.round(Vw.max(0) * scale, 3))


main()
log("done")
