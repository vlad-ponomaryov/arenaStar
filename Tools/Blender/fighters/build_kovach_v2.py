# Kovach v2 (tank, gentle giant blacksmith), wave 3 of Docs/Briefs/A-Roster3D.md: brought up to Theo v3. Headless (Blender 5.2):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_kovach_v2.py -- [out_dir]
# Same SDF library as build_kovach.py (copied from build_theo_v3.py). v2: one smooth flesh body (smooth union of shirt, arms,
# hands, legs, neck, head), hands with fingers (3 + thumb), the right fist grips the hammer resting on the shoulder, a
# modelled smile, tonal ramp bake with detail (hammered iron, wood grain, leather wrap), smooth joint weights, Muzzle empty.
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector
T0 = time.time()
def log(*a):
    print(f"[kovach {time.time() - T0:6.1f}s]", *a, flush=True)
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
# Kovach v2: palette, parts, figure
# =====================================================================================================================
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(ARGV[0]) if ARGV else os.path.join(REPO, "Art", "Source", "3D", "kovach")
os.makedirs(OUT_DIR, exist_ok=True)
TEX = 1024
BODY_BUDGET, WEAPON_BUDGET = 3950, 1180
H = hexc

PAL = {  # flat keys: sRGB, gloss (alpha)
    "glow": ("#FFB347", 1.0), "white": ("#F7F3EC", 1.0), "pupil": ("#24160E", 1.0), "lip": ("#8E4A3C", 0.40),
    "mouth": ("#3A1410", 0.50),
}
MAT = {  # tonal ramp (position, sRGB) over the baked light value, kind, AO strength, gloss
    "iron": (((0.0, "#25272C"), (0.40, "#4A4E55"), (0.62, "#5B5F66"), (0.85, "#8C9199"), (1.0, "#C8CCD2")), "metal", 0.45, 0.80),
    "iron_d": (((0.0, "#18191C"), (0.45, "#33363C"), (0.70, "#3E4148"), (1.0, "#6E727A")), "metal", 0.45, 0.65),
    "ochre": (((0.0, "#5E3310"), (0.40, "#9C5A1E"), (0.70, "#C77A2E"), (1.0, "#E8A95E")), "plastic", 0.55, 0.30),
    "ochre_d": (((0.0, "#3E220B"), (0.50, "#7A4719"), (0.75, "#8E5420"), (1.0, "#B9793A")), "plastic", 0.50, 0.25),
    "linen": (((0.0, "#9C8C70"), (0.45, "#D6C8AA"), (0.75, "#EFE3C8"), (1.0, "#FFF9EA")), "plastic", 0.50, 0.12),
    "skin": (((0.0, "#3A2013"), (0.40, "#643A22"), (0.68, "#7A4A2E"), (0.88, "#9C6644"), (1.0, "#B88260")), "plastic", 0.50, 0.22),
    "beard": (((0.0, "#0A0908"), (0.55, "#1E1A18"), (1.0, "#3E3632")), "plastic", 0.40, 0.30),
    "trousers": (((0.0, "#211812"), (0.50, "#4A3A2E"), (1.0, "#6E5A49")), "plastic", 0.55, 0.12),
    "wood": (((0.0, "#43270F"), (0.50, "#7A4E2A"), (0.75, "#8A5A32"), (1.0, "#BC8A58")), "plastic", 0.50, 0.30),
}
RAMP = {k: (np.array([p for p, _ in v[0]]), np.array([H(c) for _, c in v[0]])) for k, v in MAT.items()}


class Part:
    def __init__(self, name, node, key, bone, prio=0, soft=False):
        self.name, self.node, self.key, self.bone, self.prio, self.soft = name, node, key, bone, prio, soft
        self.lo, self.hi = node.lo, node.hi


def mk(parts, name, node, key, bone, prio=0, soft=False):
    parts.append(Part(name, node, key, bone, prio, soft))


SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}
BODY, WEAPON = [], []

# ---------------- pose of the right arm: fist in front of the shoulder, hammer handle up and back over the shoulder ----
GRIP = np.array([-0.300, -0.170, 0.600])                    # fist centre on the handle axis (= Prop bone head)
HAX = nrm((0.0, 0.170, 0.130))                              # handle axis, grip -> hammer head (up and back)
FAX = nrm(np.cross(np.array([1.0, 0, 0]), HAX))            # forearm direction (perpendicular to the handle), up-forward
if FAX[2] < 0:
    FAX = -FAX
QOUT = np.array([-1.0, 0, 0])                               # outward (palm side of the right fist)

# torso: barrel chest, broad shoulders (toy proportions, no muscles)
TORSO = U(Ell((0, 0.005, 0.50), (0.175, 0.125, 0.19)), Ell((0, 0.0, 0.60), (0.215, 0.135, 0.11)),
          Ell((0, 0.01, 0.38), (0.15, 0.11, 0.09)), k=0.06)
SH = {s: mirror((-0.235, 0.0, 0.625), s) for s in SIDES}
WR = {1: mirror((-0.295, -0.075, 0.355), 1), -1: GRIP - FAX * 0.068 + QOUT * 0.022}
EL = {1: mirror((-0.285, -0.025, 0.475), 1), -1: WR[-1] - FAX * 0.165 + np.array([0.004, 0.02, 0.0])}
HIP = {s: mirror((-0.085, 0.0, 0.36), s) for s in SIDES}
AN = {s: mirror((-0.10, 0.0, 0.085), s) for s in SIDES}
SHOULDER = {s: Ell(SH[s], (0.105, 0.105, 0.095)) for s in SIDES}
UPPER = {s: U(RCone(SH[s], EL[s], 0.075, 0.066), Ell(EL[s], (0.066, 0.066, 0.066)), k=0.01) for s in SIDES}
SHIRT = U(TORSO, *SHOULDER.values(), *UPPER.values(), k=0.045)
mk(BODY, "shirt", SHIRT, "linen", "soft", soft=True)
FORE, HAND = {}, {}
for s in SIDES:
    axis = np.array(WR[s]) - EL[s]
    FORE[s] = RCone(EL[s], WR[s], 0.062, 0.052)
    # rolled sleeve cuff at the elbow, bare forearm, iron bracer
    mk(BODY, "cuff_" + SN[s], RCyl(np.array(EL[s]) + (np.array(SH[s]) - EL[s]) * 0.12, np.array(SH[s]) - EL[s],
                                   0.078, 0.02, 0.009), "linen", "Arm" + SN[s], prio=1)
    mk(BODY, "fore_" + SN[s], FORE[s], "skin", "soft", soft=True)
    mk(BODY, "bracer_" + SN[s], RCyl(np.array(EL[s]) + axis * 0.60, axis, 0.068, 0.045, 0.012, taper=-0.004), "iron",
       "Arm" + SN[s], prio=2)

# ---------------- left hand: relaxed, palm + three half-bent fingers + thumb (Theo's glove scaled up 1.45) ----------
HS = 1.45
s = 1
dn = nrm(np.array(WR[s]) - EL[s] + np.array([0.02, 0, 0]))
f0 = np.array([-s * 0.55, -0.83, 0.0])
fw = nrm(f0 - dn * np.dot(f0, dn))
out = np.cross(fw, dn)
out = nrm(out if out[0] * s > 0 else -out)
pc = np.array(WR[s]) + dn * 0.042 * HS + out * 0.004
Rh = np.stack([out, fw, dn], 1)
palm = RBox(pc + out * 0.003, (0.024 * HS, 0.041 * HS, 0.037 * HS), 0.018 * HS, Rh)
wrist = RCone(np.array(WR[s]) - dn * 0.015, pc - dn * 0.015, 0.045, 0.045)
fingers = []
for k, fo in enumerate((-0.0225, 0.0, 0.0225)):
    ln = (0.92, 1.0, 0.94)[k] * HS
    b0 = pc + dn * 0.024 * HS + fw * fo * HS + out * 0.004
    b1 = b0 + dn * 0.030 * ln + out * 0.001 + fw * fo * 0.05
    b2 = b1 + dn * 0.021 * ln - out * 0.016
    b3 = b2 + dn * 0.007 * ln - out * 0.017
    r0 = 0.0122 * HS
    fingers += [RCone(b0, b1, r0, r0 * 0.95), RCone(b1, b2, r0 * 0.95, r0 * 0.87), RCone(b2, b3, r0 * 0.87, r0 * 0.77)]
tb = pc + fw * 0.031 * HS - out * 0.014 - dn * 0.014 * HS
tm = tb + fw * 0.012 * HS + dn * 0.028 * HS - out * 0.006
tt = tm + fw * 0.004 * HS + dn * 0.022 * HS - out * 0.008
thumb = U(RCone(tb, tm, 0.0170 * HS, 0.0158 * HS), RCone(tm, tt, 0.0158 * HS, 0.0132 * HS), k=0.006)
HAND[1] = U(U(wrist, palm, thumb, k=0.014), U(*fingers, k=0.0), k=0.009)

# ---------------- right fist around the handle: palm outside, three fingers wrapping the front, thumb over the back ----
RFR = 0.0175                                                  # finger radius
RARC = 0.020 + RFR + 0.001                                    # finger arc radius around the handle axis


def arc_pts(h, a0, a1, n=6, r=RARC):
    t = np.radians(np.linspace(a0, a1, n))
    return [GRIP + HAX * h + r * (math.cos(a) * QOUT + math.sin(a) * FAX) for a in t]


rf = []
for k, h in enumerate((-0.034, 0.0, 0.034)):                  # pinky (toward the pommel) .. index (toward the head)
    pts = arc_pts(h, 35, 240 - 10 * abs(k - 1), 6, RARC * (0.97 if k != 1 else 1.0))
    rr = np.linspace(RFR, RFR * 0.82, len(pts))
    rf += [RCone(pts[i], pts[i + 1], rr[i], rr[i + 1]) for i in range(len(pts) - 1)]
RPALM = Ell(GRIP + QOUT * 0.040 - FAX * 0.004, (0.030, 0.050, 0.058), np.stack([QOUT, FAX, HAX], 1))
RKNUCK = Ell(GRIP + QOUT * 0.028 + FAX * 0.024, (0.024, 0.024, 0.056), np.stack([QOUT, FAX, HAX], 1))
RWRIST = RCone(WR[-1] - FAX * 0.01, GRIP + QOUT * 0.035 - FAX * 0.02, 0.046, 0.042)
tp = arc_pts(0.050, -20, -150, 5, RARC * 0.98)
RTHUMB = U(RCone(GRIP + QOUT * 0.045 + HAX * 0.030 - FAX * 0.02, tp[0], 0.020, 0.019),
           *[RCone(tp[i], tp[i + 1], 0.019 - 0.0012 * i, 0.019 - 0.0012 * (i + 1)) for i in range(len(tp) - 1)], k=0.006)
HAND[-1] = U(U(RWRIST, RPALM, RKNUCK, RTHUMB, k=0.012), U(*rf, k=0.0), k=0.008)
for s in SIDES:
    mk(BODY, "hand_" + SN[s], HAND[s], "skin", "soft", soft=True)

LEG = {}
for s in SIDES:
    LEG[s] = RCone(HIP[s], AN[s] + np.array([0, 0, 0.03]), 0.085, 0.068)
    mk(BODY, "leg_" + SN[s], LEG[s], "trousers", "soft", soft=True)
    BOOT = U(RCyl(AN[s] + np.array([0, 0, 0.02]), (0, 0, 1), 0.074, 0.05, 0.02),
             Ell(AN[s] + np.array([0, -0.045, -0.025]), (0.075, 0.105, 0.055)), k=0.03)
    mk(BODY, "boot_" + SN[s], BOOT, "iron_d", "Leg" + SN[s], prio=1)
    mk(BODY, "sole_" + SN[s], I(Off(BOOT, 0.004), Slab((0, 0, 1), (0, 0, -0.012), 0.016), k=0.003), "ochre_d",
       "Leg" + SN[s], prio=2)
# leather apron: a trapezoid shell over the front of the torso + a flap over the thighs; pocket, belt, neck straps
APRON_V = [(-0.11, 0.66), (0.11, 0.66), (0.19, 0.20), (-0.19, 0.20)]
APRON_REG = Poly2(APRON_V, "xz")
APRON = U(I(Off(TORSO, 0.014), APRON_REG, Half((0, 1, 0), (0, -0.03, 0)), k=0.006),
          I(RBox((0, -0.118, 0.27), (0.19, 0.016, 0.10), 0.012, R=rot((1, 0, 0), -8)), APRON_REG, k=0.004), k=0.02)
mk(BODY, "apron", APRON, "ochre", "torso", prio=2)
POCKET_C = np.array([0, -0.150, 0.43])
mk(BODY, "pocket", RBox(POCKET_C, (0.07, 0.012, 0.045), 0.01, R=rot((1, 0, 0), -6)), "ochre_d", "torso", prio=3)
BELT = I(Off(TORSO, 0.022), Slab((0, 0, 1), (0, 0, 0.36), 0.02), k=0.004)
mk(BODY, "belt", BELT, "ochre_d", "torso", prio=3)
mk(BODY, "buckle", RBox((0, -0.150, 0.36), (0.034, 0.010, 0.026), 0.008), "iron", "torso", prio=4)
for s in SIDES:
    mk(BODY, "strap_" + SN[s], I(Off(TORSO, 0.02), Band2([(s * 0.10, 0.655), (s * 0.13, 0.75)], 0.018, "xz"),
                                 k=0.004), "ochre_d", "torso", prio=3)

# ---------------- head: shaved, short black beard with a wide smile, bushy brows, amber goggles on the forehead -------
NECK = RCone((0, 0.0, 0.64), (0, -0.005, 0.73), 0.075, 0.07)
mk(BODY, "neck", NECK, "skin", "soft", soft=True)
SKULL = U(Ell((0, 0.0, 0.825), (0.118, 0.120, 0.130)), Ell((0, -0.03, 0.775), (0.110, 0.095, 0.085)), k=0.05)
CHEEKS = [Ell(mirror((-0.056, -0.088, 0.786), s), (0.040, 0.030, 0.026)) for s in SIDES]   # raised by the smile
NOSE = U(Ell((0, -0.122, 0.800), (0.026, 0.022, 0.026)), Ell((0, -0.112, 0.818), (0.014, 0.016, 0.018)), k=0.012)
EARS = [Ell(mirror((-0.118, 0.005, 0.805), s), (0.018, 0.026, 0.032)) for s in SIDES]
FACE = U(SKULL, *CHEEKS, k=0.03)
HEADN = U(FACE, NOSE, *EARS, k=0.012)
mk(BODY, "head", HEADN, "skin", "soft", soft=True)
# smile: crescent (flat top edge, deep curved bottom, corners up) cut through the beard; dark mouth + teeth band inside
MW = 0.046
tt_ = np.linspace(-1, 1, 15)
SMILE_TOP = [(MW * t, 0.7595 + 0.0045 * t * t) for t in tt_]
SMILE_BOT = [(MW * t, 0.7425 + 0.0215 * t * t) for t in tt_[::-1][1:-1]]
SMILE = Poly2(SMILE_TOP + SMILE_BOT, "xz")
BEARD = I(Off(SKULL, 0.016), Half((0, 0, 1), (0, 0, 0.778)), Half((0, 1, 0), (0, 0.035, 0)), k=0.015)
MOUSTACHE = I(Off(FACE, 0.012), Band2([(-0.052, 0.762), (-0.026, 0.772), (0, 0.774), (0.026, 0.772), (0.052, 0.762)],
                                      0.008, "xz"), Half((0, 1, 0), (0, -0.07, 0)), k=0.004)
mk(BODY, "beard", S(U(BEARD, MOUSTACHE, k=0.006), Off(SMILE, 0.0035), k=0.003), "beard", "Head", prio=2)
mk(BODY, "lips", I(Off(FACE, 0.010), Off(SMILE, 0.0035), Half((0, 1, 0), (0, -0.07, 0)), k=0.002), "lip", "Head", prio=3)
mk(BODY, "mouth", I(Off(FACE, 0.006), SMILE, Half((0, 1, 0), (0, -0.07, 0)), k=0.002), "mouth", "Head", prio=4)
EYE = {}
for s in SIDES:
    c = mirror((-0.046, -0.104, 0.818), s)
    g = nrm((s * 0.25, -1, 0.05))
    EYE[s] = (c, g)
    mk(BODY, "eye_" + SN[s], Ell(c, (0.025, 0.013, 0.026)), "white", "Head", prio=3)
    mk(BODY, "pupil_" + SN[s], Ell(c + g * 0.0105, (0.0115, 0.005, 0.0135)), "pupil", "Head", prio=4)
    bp = [c + mirror(p, s) for p in ((0.026, -0.006, 0.026), (0.008, -0.012, 0.036), (-0.012, -0.010, 0.039),
                                     (-0.032, -0.002, 0.033))]
    mk(BODY, "brow_" + SN[s], U(RCone(bp[0], bp[1], 0.0085, 0.0105), RCone(bp[1], bp[2], 0.0105, 0.0095),
                                RCone(bp[2], bp[3], 0.0095, 0.0060), k=0.004), "beard", "Head", prio=3)
    lc = mirror((-0.046, -0.098, 0.897), s)
    gax = nrm((s * 0.15, -1, 0.55))
    mk(BODY, "goggle_" + SN[s], RCyl(lc, gax, 0.036, 0.016, 0.006), "iron_d", "Head", prio=3)
    mk(BODY, "lens_" + SN[s], RCyl(lc + gax * 0.012, gax, 0.027, 0.008, 0.004), "glow", "Head", prio=4)
mk(BODY, "goggle_strap", I(Off(SKULL, 0.008), Slab(nrm((0, 0.35, 1)), (0, 0, 0.89), 0.014), k=0.003), "iron_d", "Head",
   prio=2)
mk(BODY, "goggle_bridge", RCone((-0.016, -0.118, 0.888), (0.016, -0.118, 0.888), 0.007, 0.007), "iron_d", "Head", prio=3)
PROP_HEAD = GRIP.copy()

# ---------------- hammer (separate mesh, modelled in body space resting on the right shoulder; origin = GRIP) --------
LAX = nrm(np.cross(HAX, np.array([1.0, 0, 0])))             # long axis of the head (forward-up end = striking face)
if LAX[2] < 0:
    LAX = -LAX
XAX = np.cross(HAX, LAX)
RHM = np.stack([XAX, HAX, LAX], 1)
HC = GRIP + HAX * 0.40                                        # hammer head centre
MUZZLE = HC + LAX * 0.158                                     # centre of the forward-up striking face
mk(WEAPON, "handle", RCyl(GRIP + HAX * 0.13, HAX, 0.020, 0.25, 0.008), "wood", "Prop")
mk(WEAPON, "grip", RCyl(GRIP - HAX * 0.02, HAX, 0.0235, 0.075, 0.008), "ochre_d", "Prop", prio=1)
mk(WEAPON, "pommel", Ell(GRIP - HAX * 0.125, (0.03, 0.03, 0.022), np.stack([XAX, LAX, HAX], 1)), "iron", "Prop", prio=1)
mk(WEAPON, "collar", RCyl(GRIP + HAX * 0.305, HAX, 0.027, 0.014, 0.006), "iron_d", "Prop", prio=1)
HHEAD = chamfer_box(HC, RHM, np.array([0.085, 0.075, 0.135]), 0.012,
                    [((0, si), (2, sk), 0.025) for si in (-1, 1) for sk in (-1, 1)] +
                    [((1, si), (2, sk), 0.025) for si in (-1, 1) for sk in (-1, 1)])
mk(WEAPON, "hhead", HHEAD, "iron", "Prop")
for sk in (-1, 1):
    mk(WEAPON, f"face_{sk}", RCyl(HC + LAX * sk * 0.14, LAX, 0.07, 0.012, 0.006), "iron_d", "Prop", prio=1)
mk(WEAPON, "glowline", RBox(HC, (0.087, 0.077, 0.012), 0.004, R=RHM), "glow", "Prop", prio=2)
for sk in (-1, 1):
    for sx in (-1, 1):
        mk(WEAPON, f"rivet_{sk}{sx}", Ell(HC + LAX * sk * 0.085 + XAX * sx * 0.087, (0.009, 0.009, 0.009)), "iron_d",
           "Prop", prio=3)


# =====================================================================================================================
# figure evaluation: the flesh (shirt, forearms, hands, legs, neck, head) is one smooth union; clothes / props on top
# =====================================================================================================================
SOFT_K = 0.028
FLESH = U(U(SHIRT, NECK, LEG[-1], LEG[1], FORE[-1], FORE[1], k=SOFT_K), HEADN, k=0.03)
FLESH = U(FLESH, HAND[-1], HAND[1], k=0.012)


def fig_fn(parts):
    hard = [p for p in parts if not p.soft]
    has_soft = any(p.soft for p in parts)

    def fn(P):
        P = np.asarray(P, F32)
        out = FLESH.d(P) if has_soft else np.full(len(P), BIG, F32)
        for p in hard:
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
        m = _inbb(P, p.lo, p.hi, 0.03)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        d = p.node.d(P[idx]) - F32(4e-4) * p.prio
        b = d < best[idx]
        best[idx[b]] = d[b]
        own[idx[b]] = i
    return own


# =====================================================================================================================
# paint: tonal ramps over the baked light + per-part detail (hammered iron, wood grain, leather wrap, stitches, face)
# =====================================================================================================================
def sstep(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mixc(a, b, t):
    t = np.asarray(t, float)
    return np.asarray(a, float) + (np.asarray(b, float) - np.asarray(a, float)) * (t[..., None] if t.ndim else t)


def line_w(d, half, soft=0.0007):
    return np.clip((half - d) / soft + 0.5, 0, 1)


def stroke(c, w, col):
    return c + (np.asarray(col, float) - c) * np.asarray(w, float)[:, None]


NDIR = [nrm(v) for v in ((0.62, 0.31, 0.72), (-0.45, 0.83, 0.33), (0.21, -0.54, 0.81), (0.91, -0.12, -0.39))]


def noise(P, f, seed=0):
    """Cheap smooth pseudo-noise in about [-1, 1] (products of sines along fixed directions)."""
    a, b, c, d = (NDIR[(i + seed) % 4] for i in range(4))
    return (np.sin(P @ a * f + 1.3 * seed) * np.sin(P @ b * f * 1.37 + 0.7) * 1.4
            + 0.5 * np.sin(P @ c * f * 2.11 + 2.1 * seed) * np.sin(P @ d * f * 1.73))


KEY_L = nrm((-0.30, -0.62, 0.73))
HALF_V = nrm(KEY_L + np.array([0, -1.0, 0]))


def ramp(key, v):
    pos, cols = RAMP[key]
    v = np.clip(v, 0, 1)
    return np.stack([np.interp(v, pos, cols[:, i]) for i in range(3)], 1)


def shade(key, N, occ, dv=0.0, spec=0.0):
    kind, aos = MAT[key][1], MAT[key][2]
    lam = np.clip(N @ KEY_L, -1, 1)
    if kind == "metal":
        nz, ny = N[:, 2], N[:, 1]
        top = sstep(0.05, 0.85, nz)
        hor = np.exp(-((nz - 0.0) / 0.22) ** 2)
        flo = sstep(-0.2, -0.9, nz)
        fr = sstep(0.15, 0.95, -ny)
        v = 0.30 + 0.50 * top - 0.30 * hor * (1 - 0.5 * fr) + 0.24 * flo + 0.24 * fr * (1 - top) + 0.10 * lam + dv
        c = ramp(key, v)
        sp = np.clip(N @ HALF_V, 0, 1) ** 40 * 0.55
    else:
        t = sstep(-0.85, 0.90, 0.6 * lam + 0.4 * N[:, 2]) + dv
        c = ramp(key, t)
        sp = np.clip(N @ HALF_V, 0, 1) ** 26 * spec
    c = c + (1 - c) * sp[:, None]
    return np.clip(c * (1 - aos * occ)[:, None], 0, 1)


def gloss_of(key):
    return MAT[key][3] if key in MAT else PAL[key][1]


def handle_local(P):
    D = P - GRIP
    along = D @ HAX
    ang = np.arctan2(D @ LAX, D @ XAX)
    return along, ang


CROW = {s: [[(s * 0.073, 0.819), (s * 0.090, 0.828)], [(s * 0.074, 0.815), (s * 0.093, 0.815)],
            [(s * 0.073, 0.811), (s * 0.089, 0.802)]] for s in SIDES}


def detail(p, P, N, occ):
    """Colour of the points P owned by part p."""
    key, nm = p.key, p.name
    x, z = P[:, 0], P[:, 2]
    lam = np.clip(N @ KEY_L, -1, 1)
    if key in PAL:  # flat colours
        c = np.tile(H(PAL[key][0]), (len(P), 1)) * (0.92 + 0.12 * lam[:, None]) * (1 - 0.35 * occ[:, None])
        if nm.startswith("lens"):
            s = -1 if nm.endswith("R") else 1
            lc = mirror((-0.046, -0.098, 0.897), s) + nrm((s * 0.15, -1, 0.55)) * 0.012
            r = np.linalg.norm(P - lc, axis=1) / 0.03
            c = mixc(H("#FFF0B8"), H("#F08A1E"), sstep(0.1, 1.0, r))
            c = stroke(c, line_w(np.linalg.norm(P - lc - mirror((-0.010, 0, 0.012), s), axis=1), 0.005, 0.002), H("#FFFFFF"))
        elif nm == "glowline":
            r = np.abs((P - HC) @ LAX) / 0.012
            c = mixc(H("#FFF2C2"), H("#E8741A"), sstep(0.0, 1.0, r))
        elif nm.startswith("eye"):
            s = -1 if nm.endswith("R") else 1
            ce, g = EYE[s]
            dp = P - ce
            r = np.linalg.norm(dp - np.outer(dp @ g, g), axis=1)
            front = sstep(0.0, 0.006, dp @ g)
            c = mixc(H("#F7F3EC"), H("#D8CFC4"), sstep(-0.005, -0.025, z - ce[2]) * 0.0 + sstep(0.006, 0.020, z - ce[2]) * 0.4)
            icol = mixc(H("#B0702E"), H("#5A3014"), sstep(0.004, 0.0165, r))
            c = stroke(c, line_w(r, 0.0165, 0.0012) * front, icol)
        elif nm.startswith("pupil"):
            s = -1 if nm.endswith("R") else 1
            ce, g = EYE[s]
            hp = ce + g * 0.016 + np.array([-0.005, 0, 0.0055])
            c = stroke(c, line_w(np.linalg.norm(P - hp, axis=1), 0.0038, 0.0012), H("#FFFFFF"))
            hp2 = ce + g * 0.016 + np.array([0.004, 0, -0.005])
            c = stroke(c, line_w(np.linalg.norm(P - hp2, axis=1), 0.0018, 0.0008), H("#FFFFFF"))
        elif nm == "mouth":
            top = 0.7595 + 0.0045 * (x / MW) ** 2
            c = stroke(c, line_w(top - z, 0.0058, 0.0010), H("#F2ECE0"))          # upper teeth
            c = stroke(c, np.exp(-((x / 0.020) ** 2 + ((z - 0.748) / 0.005) ** 2)) * 0.8, H("#9A3C34"))  # tongue
        return np.clip(c, 0, 1)
    dv = 0.0
    if key in ("iron", "iron_d"):
        dv = 0.06 * noise(P, 160, 1) + 0.03 * noise(P, 420, 2)                # hammered, forged surface
        if nm.startswith("face_"):
            dv = dv + 0.14                                                      # polished striking faces
    elif key == "wood":
        along, ang = handle_local(P)
        g = np.sin(ang * 5 + along * 22 + 1.8 * np.sin(along * 55 + ang * 2))
        dv = 0.10 * g - 0.10 * sstep(0.85, 1.0, np.abs(np.sin(ang * 9 + along * 7)))
    elif key == "beard":
        dv = 0.10 * np.sin(x * 900 + z * 260 + 3 * noise(P, 90)) + 0.05 * noise(P, 300)
    elif key == "linen":
        dv = 0.025 * np.sin(x * 1400) * np.sin(z * 1400) + 0.05 * noise(P, 60, 3)
    elif key == "trousers":
        dv = 0.04 * noise(P, 120, 2)
    elif key == "ochre":
        dv = 0.05 * noise(P, 70, 1) - 0.20 * sstep(0.45, 0.85, noise(P, 26, 3))  # leather + scorch marks
    c = shade(key, N, occ, dv, spec=0.10 if key in ("skin", "ochre", "ochre_d", "wood") else 0.03)
    if nm == "grip":  # leather wrap: a helix with dark gaps
        along, ang = handle_local(P)
        fr = np.mod(ang / (2 * np.pi) + along / 0.024, 1.0)
        c = stroke(c, line_w(np.abs(fr - 0.5), 0.07, 0.03), c * 0.45)
    if nm == "apron":  # light stitches near the border on the front
        d = seg_dist2(x.astype(F32), z.astype(F32), np.asarray(APRON_V, F32), True)
        dash = (np.sin((x + z * 0.6) * 520) > 0.1) & (N[:, 1] < -0.3)
        c = stroke(c, line_w(np.abs(d - 0.011), 0.0016, 0.0008) * dash, H("#EAC88E"))
    if nm == "pocket":
        Q = (P - POCKET_C) @ rot((1, 0, 0), -6)
        d = np.minimum(0.07 - np.abs(Q[:, 0]), 0.045 - np.abs(Q[:, 2]))
        dash = np.sin((Q[:, 0] + Q[:, 2]) * 600) > 0.1
        c = stroke(c, line_w(np.abs(d - 0.008), 0.0014, 0.0007) * dash * (N[:, 1] < -0.3), H("#E0B878"))
    if nm == "head":
        front = sstep(0.1, 0.5, -N[:, 1])
        for s in SIDES:
            b = np.exp(-(((x - s * 0.060) / 0.024) ** 2 + ((z - 0.790) / 0.014) ** 2)) * front
            c = stroke(c, b * 0.40, H("#9A4A38"))                               # warm cheeks
            for seg in CROW[s]:                                                  # smile wrinkles at the eye corners
                d = seg2_dist(x, z, seg)
                c = stroke(c, line_w(d, 0.0011, 0.0007) * front * 0.65, c * 0.62)
        nose = np.exp(-(((x + 0.006) / 0.010) ** 2 + ((z - 0.810) / 0.008) ** 2)) * front
        c = stroke(c, nose * 0.35, H("#B88262"))
        top = sstep(0.55, 0.95, N[:, 2])                                         # shaved head sheen
        c = stroke(c, top * 0.18 * np.clip(N @ HALF_V, 0, 1) ** 8, H("#C8946E"))
    if nm.startswith("hand_") or nm.startswith("fore_"):
        c = stroke(c, sstep(0.25, 0.6, occ) * 0.35, H("#4E2A18"))               # deeper creases between fingers
    return np.clip(c, 0, 1)


def seg2_dist(px, pz, V):
    V = np.asarray(V, float)
    d = np.full(px.shape, 1e9)
    for i in range(len(V) - 1):
        ex, ez = V[i + 1, 0] - V[i, 0], V[i + 1, 1] - V[i, 1]
        wx, wz = px - V[i, 0], pz - V[i, 1]
        t = np.clip((wx * ex + wz * ez) / (ex * ex + ez * ez), 0, 1)
        d = np.minimum(d, (wx - ex * t) ** 2 + (wz - ez * t) ** 2)
    return np.sqrt(d)


def paint(parts, fn, P):
    N = grad(fn, P, e=2.5e-4)
    N = N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
    occ = np.zeros(len(P), F32)
    for h, w in ((0.004, 0.3), (0.01, 0.28), (0.02, 0.22), (0.035, 0.15)):
        d = fn((P + N * h).astype(F32))
        occ += F32(w) * np.clip((h - d) / h, 0, 1)
    occ = np.clip(occ, 0, 1).astype(np.float64)
    own = owner(parts, P)
    col = np.zeros((len(P), 3))
    gl = np.zeros(len(P))
    for i in np.unique(own):
        sel = np.nonzero(own == i)[0]
        p = parts[i]
        col[sel] = detail(p, P[sel].astype(np.float64), N[sel].astype(np.float64), occ[sel])
        gl[sel] = gloss_of(p.key)
    return col, gl, own


# =====================================================================================================================
# build (mesh -> decimate -> UV -> bake), weights, export
# =====================================================================================================================
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
    ob.data.validate(clean_customdata=False)
    ob.data.update()
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
    smp = sample_tex(img, uvc, blur=2)
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
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types=types, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)


# ---- smooth joint weights: flesh vertices blend between the limb / torso / head volumes they sit between ----
WGROUPS = [("torso", U(TORSO, SHOULDER[-1], SHOULDER[1], k=0.045)), ("ArmR", U(UPPER[-1], FORE[-1], HAND[-1])),
           ("ArmL", U(UPPER[1], FORE[1], HAND[1])), ("LegR", LEG[-1]), ("LegL", LEG[1]), ("Head", FACE)]
W_FALL = 0.014


def torso_w(z):
    t = float(sstep(0.38, 0.46, z))
    return {"Root": 1 - t, "Body": t}


def vertex_weights(V, own):
    P = V.astype(F32)
    D = np.stack([np.maximum(g.d(P), 0) for _, g in WGROUPS], 0)
    Wt = np.exp(-(D - D.min(0)) / W_FALL)
    out = []
    for j in range(len(V)):
        p = BODY[own[j]]
        if p.soft:
            w = {}
            for i, (nm, _) in enumerate(WGROUPS):
                v = float(Wt[i, j])
                if v < 0.02:
                    continue
                if nm == "torso":
                    for k2, v2 in torso_w(V[j, 2]).items():
                        w[k2] = w.get(k2, 0) + v * v2
                else:
                    w[nm] = w.get(nm, 0) + v
        elif p.bone == "torso":
            w = torso_w(V[j, 2])
        else:
            w = {p.bone: 1.0}
        top = sorted(w.items(), key=lambda kv: -kv[1])[:4]
        top = [(k2, v2) for k2, v2 in top if v2 > 0.02] or top[:1]
        t = sum(v2 for _, v2 in top)
        out.append({k2: v2 / t for k2, v2 in top})
    return out


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    Vb, Tb, Ub, Ib, own = build(BODY, BODY_BUDGET, "Kovach", 0.003, (-0.44, -0.27, -0.03), (0.42, 0.22, 1.0))
    zmin, zmax = Vb[:, 2].min(), Vb[:, 2].max()
    shift = np.array([-(Vb[:, 0].min() + Vb[:, 0].max()) / 2, 0.0, -zmin])
    log("x extent", Vb[:, 0].min(), Vb[:, 0].max(), "shift", shift)
    shift[0] = 0.0  # keep the symmetric body centred (the raised right fist would pull the centre off the midline)
    scale = 1.0 / (zmax - zmin)
    tex = os.path.join(OUT_DIR, "kovach_tex.png")
    save_png(Ib, tex, "kovach_tex")
    ob = make_mesh("Kovach", (Vb + shift) * scale, Tb, Ub, Ib, tex)
    BONE_DEF = [("Root", None, (0, 0, zmin), (0, 0, 0.40)), ("Body", "Root", (0, 0, 0.40), (0, 0, 0.70)),
                ("Head", "Body", (0, 0, 0.70), (0, 0, zmax)),
                ("ArmR", "Body", SH[-1], WR[-1]), ("ArmL", "Body", SH[1], WR[1]),
                ("LegR", "Root", HIP[-1], AN[-1]), ("LegL", "Root", HIP[1], AN[1]),
                ("Prop", "Body", PROP_HEAD, PROP_HEAD + HAX * 0.10)]
    for pre in ("Eye", "Lid"):
        for s in (-1, 1):
            c, g = EYE[s]
            BONE_DEF.append((pre + SN[s], "Head", c, c + g * 0.03))
    ad = bpy.data.armatures.new("KovachRig")
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
    W = vertex_weights(Vb, own)
    nmulti = 0
    for i, w in enumerate(W):
        nmulti += len(w) > 1
        for bn, v in w.items():
            groups[bn].add([i], v, "REPLACE")
    log(f"weights: {nmulti}/{len(W)} vertices blend 2+ bones, max {max(len(w) for w in W)} influences")
    ob.parent = arm
    ob.modifiers.new("Armature", "ARMATURE").object = arm
    export([ob, arm], os.path.join(OUT_DIR, "kovach_game"), {"ARMATURE", "MESH"})
    # hammer: modelled in body space on the shoulder; exported with its origin at the grip (= Prop head)
    lo = np.min([p.lo for p in WEAPON], 0) - 0.02
    hi = np.max([p.hi for p in WEAPON], 0) + 0.02
    Vw, Tw, Uw, Iw, _ = build(WEAPON, WEAPON_BUDGET, "KovachHammer", 0.0025, lo, hi)
    wtex = os.path.join(OUT_DIR, "kovach_weapon_tex.png")
    save_png(Iw, wtex, "kovach_weapon_tex")
    wob = make_mesh("KovachHammer", (Vw - PROP_HEAD) * scale, Tw, Uw, Iw, wtex)
    muz = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(muz)
    muz.location = Vector((MUZZLE - PROP_HEAD) * scale)
    muz.parent = wob
    export([wob, muz], os.path.join(OUT_DIR, "kovach_weapon"), {"MESH", "EMPTY"})
    log(f"RESULT body tris {len(Tb)}/4000 verts {len(Vb)}; weapon tris {len(Tw)}/1200; scale {scale:.4f}")
    log("BONES", heads)
    log("Muzzle", np.round((MUZZLE - PROP_HEAD) * scale, 3))
    log("weapon extent", np.round((Vw.min(0) - PROP_HEAD) * scale, 3), np.round((Vw.max(0) - PROP_HEAD) * scale, 3))


main()
log("done")
