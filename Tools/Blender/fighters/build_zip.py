# Zip (id zip), the parkour assassin with a grappling hook (Docs/Briefs/A-Roster3D.md, Docs/Roster-v2.md §4 #6): a simple
# glossy "vinyl toy" base model in the Theo v3 format. Headless (Blender 5.2, numpy only):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_zip.py -- [out_dir]
# Output (default Art/Source/3D/zip/): zip_game.fbx/.glb (<= 4000 tris, mesh "Zip", material "FighterVC" with zip_tex.png,
# armature "Rig" with the 12 FighterBone bones, UV "UVMap", corner colour "Col" = texture, alpha = gloss),
# zip_weapon.fbx/.glb (grapple launcher <= 1200 tris, vertex colours, origin = grip under the right hand, barrel along -Y,
# empty "Muzzle" at the hook), zip_tex.png (1024 RGBA: RGB = albedo with baked AO, A = gloss).
# Format as Theo v3: Blender Z up, faces -Y, height 1.0, feet on z = 0; FBX axis_forward=-Z, axis_up=Y, colors_type=SRGB.
# Method (simplified Theo v3): SDF primitives (classes copied from build_theo_v3.py) -> surface nets -> Newton projection ->
# decimate -> smart UV -> every texel rasterised back to 3D, projected on the exact field and painted procedurally.
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
POS = [a for a in ARGV if not a.startswith("--")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "zip")
os.makedirs(OUT_DIR, exist_ok=True)
T0 = time.time()


def log(*a):
    print(f"[zip {time.time() - T0:6.1f}s]", *a, flush=True)




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
# palette (sRGB) and gloss
# =====================================================================================================================
def hexc(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])


H = hexc
PAL = {  # key: (sRGB, gloss)
    "skin": (H("#C68F60"), 0.18), "hair": (H("#2E231D"), 0.32), "hood": (H("#26242B"), 0.14),
    "crimson": (H("#C8173F"), 0.55), "pants": (H("#3C3E47"), 0.10), "shoe": (H("#F0ECE4"), 0.55),
    "sole": (H("#C8173F"), 0.5), "glove": (H("#45454E"), 0.35), "rope": (H("#F2C431"), 0.25),
    "strap": (H("#7A5A34"), 0.2), "metal": (H("#C9CED6"), 0.85), "dark": (H("#2A2A30"), 0.3),
    "eye_w": (H("#FFFFFF"), 1.0), "iris": (H("#5A3A1E"), 1.0), "pupil": (H("#1B120C"), 1.0), "brow": (H("#2A1E17"), 0.2),
    "mouth": (H("#7A3B2C"), 0.3), "blush": (H("#D9876A"), 0.18),
}
PARTS = []  # (name, node, key, bone)
SIDES = (-1, 1)


def part(name, node, key, bone):
    PARTS.append((name, node, key, bone))
    return node


def mir(p, s):
    p = np.array(p, float)
    p[0] = abs(p[0]) * s
    return p


# =====================================================================================================================
# MODEL (Blender Z up, faces -Y, +X = the character's left). Height ~1.0.
# =====================================================================================================================
# body (hoodie): chest + belly; hips (joggers)
TORSO = U(Ell((0, 0.004, 0.585), (0.122, 0.082, 0.095)), Ell((0, 0.0, 0.49), (0.108, 0.074, 0.085)), k=0.05)
part("torso", TORSO, "hood", "Body")
part("pelvis", Ell((0, 0.004, 0.425), (0.100, 0.068, 0.055)), "pants", "Root")
part("hem", I(Off(TORSO, 0.006), Slab((0, 0, 1), (0, 0, 0.432), 0.016), k=0.004), "crimson", "Body")
part("pocket", I(Off(TORSO, 0.005), Poly2([(-0.06, 0.455), (0.06, 0.455), (0.045, 0.52), (-0.045, 0.52)], "xz"),
                  Half((0, 1, 0), (0, -0.02, 0)), k=0.004), "hood", "Body")
part("hoodfold", U(Ell((0, 0.060, 0.655), (0.098, 0.052, 0.045)), Ell((0, 0.085, 0.625), (0.07, 0.035, 0.05)), k=0.03),
     "hood", "Body")
part("hoodlin", I(Ell((0, 0.058, 0.672), (0.085, 0.045, 0.034)), Half((0, 0, -1), (0, 0, 0.675))), "crimson", "Body")
part("neck", RCone((0, 0.004, 0.64), (0, 0.004, 0.72), 0.038, 0.036), "skin", "Head")
# scarf: wrap round the neck + long tail flowing back (reads from the top-down camera)
part("scarfwrap", Torus((0, 0.006, 0.682), (0, 0.15, 1), 0.058, 0.024, sxy=(1.0, 0.9)), "crimson", "Body")
SC = np.array([(0.035, 0.062, 0.672), (0.058, 0.108, 0.640), (0.072, 0.165, 0.600), (0.070, 0.225, 0.565),
               (0.050, 0.285, 0.540), (0.062, 0.345, 0.520), (0.090, 0.400, 0.500)])
SW = np.tile(np.array([[1.0, 0.0, 0.05]]), (len(SC), 1))
SW[:, 1] += np.sin(np.linspace(0, 3.0, len(SC))) * 0.25
ST = np.tile(np.array([[0.0, -0.1, 1.0]]), (len(SC), 1))
part("scarftail", Ribbon(SC, SW, ST, np.linspace(0.036, 0.048, len(SC)), np.full(len(SC), 0.009)), "crimson", "Body")
# rope coil on the back + strap across the chest
COIL_C = np.array([0.0, 0.092, 0.545])
part("coil", U(Torus(COIL_C, (0, 1, 0.15), 0.058, 0.019), Torus(COIL_C + (0.006, 0.012, -0.004), (0.1, 1, 0.15), 0.052, 0.017),
               Torus(COIL_C + (-0.004, 0.024, 0.003), (-0.1, 1, 0.1), 0.046, 0.015), k=0.004), "rope", "Body")
part("strap", I(Shell(Off(TORSO, 0.006), 0.004), Slab(nrm((0.75, 0, 1)), (0, 0, 0.54), 0.014), k=0.002), "strap", "Body")

# arms: right (-X) holds the launcher forward, left relaxed a little forward
SH = {s: mir((-0.128, 0.006, 0.622), s) for s in SIDES}
EL = {-1: np.array([-0.170, -0.030, 0.520]), 1: np.array([0.180, -0.005, 0.522])}
WR = {-1: np.array([-0.158, -0.135, 0.478]), 1: np.array([0.192, -0.050, 0.440])}
HAND = {}
for s in SIDES:
    sn = "R" if s < 0 else "L"
    part("upper" + sn, U(RCone(SH[s], EL[s], 0.047, 0.040), Ell(SH[s] + (0, 0, 0.005), (0.052, 0.05, 0.05)), k=0.02),
         "hood", "Arm" + sn)
    part("fore" + sn, RCone(EL[s], WR[s] - nrm(WR[s] - EL[s]) * 0.012, 0.040, 0.036), "hood", "Arm" + sn)
    d = nrm(WR[s] - EL[s])
    part("cuff" + sn, RCyl(WR[s] - d * 0.012, d, 0.040, 0.013, rad=0.008), "crimson", "Arm" + sn)
    hc = WR[s] + d * 0.035
    HAND[s] = hc
    fist = Ell(hc + d * 0.02 + (0, 0, -0.004), (0.030, 0.030, 0.030))
    part("glove" + sn, U(Ell(hc, (0.034, 0.036, 0.038)), fist, k=0.02), "glove", "Arm" + sn)
    part("knuck" + sn, I(Off(fist, 0.005), Half((0, 0, -1), hc + (0, 0, 0.006))), "crimson", "Arm" + sn)

# legs (joggers) + sneakers
HIP = {s: mir((-0.056, 0.005, 0.425), s) for s in SIDES}
KN = {s: mir((-0.066, -0.010, 0.255), s) for s in SIDES}
AN = {s: mir((-0.070, 0.006, 0.095), s) for s in SIDES}
for s in SIDES:
    sn = "R" if s < 0 else "L"
    part("thigh" + sn, RCone(HIP[s], KN[s], 0.058, 0.046), "pants", "Leg" + sn)
    part("shin" + sn, RCone(KN[s], AN[s] + (0, 0, 0.02), 0.046, 0.040), "pants", "Leg" + sn)
    part("ankle" + sn, RCyl(AN[s] + (0, 0, 0.03), (0, 0, 1), 0.041, 0.016, rad=0.008), "crimson", "Leg" + sn)
    shoe = I(U(Ell(mir((-0.071, -0.022, 0.048), s), (0.050, 0.088, 0.050)),
               Ell(mir((-0.070, 0.012, 0.075), s), (0.044, 0.050, 0.040)), k=0.02), Half((0, 0, -1), (0, 0, 0.0)), k=0.004)
    part("shoe" + sn, shoe, "shoe", "Leg" + sn)
    part("sole" + sn, I(Off(shoe, 0.004), Slab((0, 0, 1), (0, 0, 0.008), 0.010), k=0.002), "sole", "Leg" + sn)

# head
CRAN = Ell((0, 0.004, 0.838), (0.104, 0.104, 0.112))
FACE = Ell((0, -0.020, 0.782), (0.094, 0.086, 0.075))
CHIN = Ell((0, -0.066, 0.735), (0.038, 0.030, 0.022))
HEAD = U(CRAN, FACE, CHIN, *[Ell(mir((-0.053, -0.058, 0.764), s), (0.046, 0.040, 0.036)) for s in SIDES], k=0.04)
NOSE = Ell((0, -0.103, 0.778), (0.015, 0.016, 0.014))
EARS = [Ell(mir((-0.100, 0.012, 0.795), s), (0.016, 0.022, 0.028)) for s in SIDES]
part("head", U(HEAD, NOSE, *EARS, k=0.012), "skin", "Head")
# hair: cap (front edge higher than the back) + tousled tufts
CAP = I(Off(CRAN, 0.014), Half(nrm((0, -0.45, -1)), (0, 0, 0.815)), k=0.01)
TUFTS = []
rng = np.random.default_rng(6)
HC = np.array([0.0, 0.012, 0.850])
TUFT_DIRS = [(-0.15, -0.75, 0.62), (0.25, -0.70, 0.62), (0.55, -0.45, 0.6), (-0.55, -0.40, 0.65), (0.0, -0.2, 1.0),
             (0.45, 0.1, 0.9), (-0.45, 0.15, 0.88), (0.1, 0.5, 0.85), (-0.25, 0.65, 0.7), (0.35, 0.65, 0.65),
             (0.75, 0.15, 0.55), (-0.75, 0.1, 0.55), (0.0, 0.85, 0.45), (0.05, -0.55, 0.85)]
for dv in TUFT_DIRS:
    dv = nrm(dv)
    a = HC + dv * 0.085
    b = HC + dv * (0.150 + 0.012 * rng.standard_normal()) + np.array([0.012 * rng.standard_normal(), 0.02, 0.0])
    if dv[1] < -0.5:  # fringe: tips fall forward-down over the forehead
        b = HC + dv * 0.14 + np.array([0.02 * np.sign(dv[0] + 0.01), 0.0, -0.035])
    TUFTS.append(RCone(a, b, 0.040, 0.010))
part("hair", U(CAP, *TUFTS, k=0.018), "hair", "Head")

EYE_C = {s: np.array([s * 0.044, 0.800]) for s in SIDES}  # (x, z) on the face


def fig_d(P):
    out = np.full(len(P), BIG, F32)
    for _, nd, _, _ in PARTS:
        m = _inbb(P, nd.lo, nd.hi, 0.02)
        if m.any():
            idx = np.nonzero(m)[0]
            out[idx] = smin(out[idx], nd.d(P[idx]), 0.004)
    return out


def part_dists(P, parts):
    D = np.full((len(parts), len(P)), BIG, F32)
    for i, pt in enumerate(parts):
        nd = pt[1]
        m = _inbb(P, nd.lo, nd.hi, 0.02)
        if m.any():
            idx = np.nonzero(m)[0]
            D[i, idx] = nd.d(P[idx])
    return D


# =====================================================================================================================
# weapon: "Hookshot" grapple launcher (toy-like: crimson body, yellow rope reel, metal claw hook). Origin = grip.
# =====================================================================================================================
WPARTS = []
GY = 0.05  # barrel axis height above the grip centre
WPARTS.append(("grip", RCyl((0, 0.008, 0.0), (0, -0.25, 1), 0.019, 0.042, rad=0.008), "dark", None))
WPARTS.append(("guard", Torus((0, -0.020, 0.010), (1, 0, 0), 0.026, 0.006), "dark", None))
WPARTS.append(("body", U(RCyl((0, -0.035, GY), (0, -1, 0), 0.036, 0.075, rad=0.014, taper=-0.006),
                         RBox((0, 0.035, GY), (0.028, 0.02, 0.028), 0.01), k=0.01), "crimson", None))
WPARTS.append(("reel", RCyl((0, 0.000, GY + 0.030), (1, 0, 0), 0.030, 0.034, rad=0.006), "rope", None))
WPARTS.append(("reelcap", RCyl((0, 0.000, GY + 0.030), (1, 0, 0), 0.016, 0.040, rad=0.004), "metal", None))
WPARTS.append(("collar", RCyl((0, -0.115, GY), (0, -1, 0), 0.031, 0.010, rad=0.004), "metal", None))
WPARTS.append(("rope", RCone((0, -0.125, GY), (0, -0.150, GY), 0.008, 0.008), "rope", None))
HOOK_TIP = np.array([0.0, -0.205, GY])
hk = [RCone((0, -0.145, GY), HOOK_TIP, 0.010, 0.008)]
for a in (90, 210, 330):
    r = np.array([math.cos(math.radians(a)), 0, math.sin(math.radians(a))])
    p0 = HOOK_TIP + (0, 0.005, 0)
    p1 = HOOK_TIP + r * 0.040 + (0, 0.012, 0)
    p2 = HOOK_TIP + r * 0.040 + (0, 0.040, 0)
    hk += [RCone(p0, p1, 0.007, 0.006), RCone(p1, p2, 0.006, 0.003)]
WPARTS.append(("hook", U(*hk, k=0.006), "metal", None))
MUZZLE = HOOK_TIP + (0, -0.01, 0)


def weap_d(P):
    out = np.full(len(P), BIG, F32)
    for _, nd, _, _ in WPARTS:
        out = np.minimum(out, nd.d(P))
    return out


# =====================================================================================================================
# paint
# =====================================================================================================================
TEX = 1024
BUDGET_BODY, BUDGET_WEAP = 3950, 1150


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def field_ao(fn, P, N):
    occ = np.zeros(len(P), F32)
    for h, w in ((0.004, 0.28), (0.009, 0.26), (0.016, 0.2), (0.026, 0.15), (0.04, 0.11)):
        d = fn((P + N * h).astype(F32))
        occ += F32(w) * np.clip((h - d) / h, 0, 1)
    return np.clip(occ, 0, 1)


def unit_grad(fn, P):
    g = grad(fn, P, e=5e-4)
    return g / np.maximum(np.sqrt((g * g).sum(1)), 1e-6)[:, None]


def mix(c, t, col):
    return c * (1 - t[:, None]) + np.asarray(col)[None] * t[:, None]


def paint(P, parts, fn):
    """RGB (sRGB 0..1) + gloss for surface points P."""
    D = part_dists(P, parts)
    own = D.argmin(0)
    keys = [p[2] for p in parts]
    names = [p[0] for p in parts]
    col = np.array([PAL[keys[o]][0] for o in own])
    gl = np.array([PAL[keys[o]][1] for o in own], float)
    N = unit_grad(fn, P)
    if "head" in names:
        hd = own == names.index("head")
        x, z, y = P[:, 0], P[:, 2], P[:, 1]
        front = hd & (N[:, 1] < -0.25)
        for s in SIDES:
            ex, ez = EYE_C[s]
            u, v = (x - ex) / 0.024, (z - ez) / 0.031
            r = np.sqrt(u * u + v * v)
            m = front & (r < 1.0)
            col[m] = PAL["eye_w"][0]
            ir = np.sqrt(((x - ex + s * 0.004) / 0.016) ** 2 + ((z - ez + 0.002) / 0.020) ** 2)
            col[m & (ir < 1)] = PAL["iris"][0]
            col[m & (ir < 0.5)] = PAL["pupil"][0]
            sp = np.sqrt(((x - ex + s * 0.004 - 0.007) / 0.006) ** 2 + ((z - ez - 0.008) / 0.006) ** 2)
            col[m & (sp < 1)] = (1, 1, 1)
            gl[m] = 1.0
            ring = front & (r >= 1.0) & (r < 1.15) & (v > -0.2)  # lash line on top
            col[ring] = PAL["brow"][0]
            # brow: cocky slant (outer end higher on the right side of the face)
            t = np.clip((x * s - 0.018) / 0.052, 0, 1)
            bz = 0.848 + 0.010 * t * (1.4 if s < 0 else 0.6) - 0.012 * (t - 0.5) ** 2 * 4 * 0.3
            bm = front & (np.abs(z - bz) < 0.0065 * (1.1 - 0.5 * t)) & (x * s > 0.016) & (x * s < 0.072)
            col[bm] = PAL["brow"][0]
            gl[bm] = 0.2
            # blush
            bl = front & (np.sqrt(((x - s * 0.060) / 0.022) ** 2 + ((z - 0.765) / 0.012) ** 2) < 1)
            col[bl] = col[bl] * 0.75 + PAL["blush"][0] * 0.25
        # smirk: curve rising to the character's left (+X)
        mz = 0.742 + 0.010 * sstep(-0.005, 0.03, x) + 0.002 * (x / 0.03) ** 2
        mm = front & (np.abs(z - mz) < 0.0032) & (x > -0.022) & (x < 0.030)
        col[mm] = PAL["mouth"][0]
    if "hair" in names:  # strand shading: darker near the cap, lighter at the tips
        hr = own == names.index("hair")
        tip = np.clip((np.linalg.norm(P - HC, axis=1) - 0.10) / 0.06, 0, 1)
        col[hr] = col[hr] * (0.85 + 0.45 * tip[hr, None])
    if "coil" in names:  # rope twist stripes
        rp = own == names.index("coil")
        ang = np.arctan2(P[:, 2] - COIL_C[2], P[:, 0] - COIL_C[0])
        st = 0.5 + 0.5 * np.sin(ang * 40 + P[:, 1] * 300)
        col[rp] = col[rp] * (0.82 + 0.18 * st[rp, None])
    if "torso" in names:  # crimson side stripes on the hoodie sleeves / zip line
        for nm in ("upperR", "upperL", "foreR", "foreL"):
            ar = own == names.index(nm)
            s = -1 if nm.endswith("R") else 1
            mm = ar & (np.abs(P[:, 1] - 0.0) < 0.010) & (P[:, 0] * s > 0)
            col[mm] = PAL["crimson"][0]
            gl[mm] = PAL["crimson"][1]
        tz = own == names.index("torso")
        zl = tz & (np.abs(P[:, 0]) < 0.004) & (P[:, 1] < 0) & (P[:, 2] > 0.52)
        col[zl] = PAL["crimson"][0]
    occ = field_ao(fn, P, N)
    light = 0.80 + 0.22 * np.clip(N[:, 2] * 0.6 + (-N[:, 1]) * 0.3 + 0.2, 0, 1)
    col = col * (light * (1 - 0.55 * occ))[:, None]
    return np.clip(col, 0, 1), gl, own


# =====================================================================================================================
# mesh helpers
# =====================================================================================================================
def decimate(ob, target):
    for _ in range(3):
        nt = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        if nt <= target:
            break
        mod = ob.modifiers.new("Dec", "DECIMATE")
        mod.ratio = target / nt * (0.99 if _ == 0 else 0.97)
        mod.use_collapse_triangulate = True
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        ob.modifiers.remove(mod)
        old = ob.data
        ob.data = me
        bpy.data.meshes.remove(old)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()


def smart_uv(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    me = ob.data
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    return uv.reshape(-1, 2)


def raster(V, T, UVc, res):
    """Texel centres covered by each triangle -> (texel ix, iy, triangle, barycentrics)."""
    out_i, out_t, out_b = [], [], []
    for t in range(len(T)):
        uv = UVc[t * 3:t * 3 + 3] * res - 0.5
        x0, y0 = np.floor(uv.min(0)).astype(int)
        x1, y1 = np.ceil(uv.max(0)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, res - 1), min(y1, res - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        gx, gy = gx.ravel(), gy.ravel()
        a, b, c = uv
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        l0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / den
        l1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / den
        l2 = 1 - l0 - l1
        e = -0.6 / max(np.abs(uv.max(0) - uv.min(0)).max(), 1)
        m = (l0 >= e) & (l1 >= e) & (l2 >= e)
        if not m.any():
            continue
        out_i.append(np.stack([gx[m], gy[m]], 1))
        out_t.append(np.full(m.sum(), t))
        out_b.append(np.clip(np.stack([l0[m], l1[m], l2[m]], 1), 0, 1))
    I_ = np.concatenate(out_i)
    Tt = np.concatenate(out_t)
    B = np.concatenate(out_b)
    B /= B.sum(1, keepdims=True)
    # keep one sample per texel (last wins)
    key = I_[:, 1] * res + I_[:, 0]
    _, first = np.unique(key[::-1], return_index=True)
    sel = len(key) - 1 - first
    return I_[sel], Tt[sel], B[sel]


def dilate(img, filled, iters=12):
    for _ in range(iters):
        acc = np.zeros_like(img)
        cnt = np.zeros(filled.shape, float)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            f = np.roll(np.roll(filled, dy, 0), dx, 1)
            acc += np.roll(np.roll(img, dy, 0), dx, 1) * f[..., None]
            cnt += f
        new = (~filled) & (cnt > 0)
        img[new] = acc[new] / cnt[new, None]
        filled = filled | new
    return img


def srgb_to_lin(c):
    c = np.asarray(c, float)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


BONES = ["Root", "Body", "Head", "ArmR", "ArmL", "LegR", "LegL", "Prop", "EyeR", "EyeL", "LidR", "LidL"]


def vertex_weights(V):
    bones = sorted(set(p[3] for p in PARTS))
    D = part_dists(V.astype(F32), PARTS)
    bd = np.stack([D[[i for i, p in enumerate(PARTS) if p[3] == b]].min(0) for b in bones])
    W = []
    order = np.argsort(bd, 0)
    for i in range(len(V)):
        a, b = order[0, i], order[1, i]
        da, db = bd[a, i], bd[b, i]
        wb = 0.5 * math.exp(-max(db - da, 0) / 0.008)
        if wb < 0.06:
            W.append({bones[a]: 1.0})
        else:
            W.append({bones[a]: 1 - wb, bones[b]: wb})
    return W


def make_material(img_path=None):
    mat = bpy.data.materials.get("FighterVC") or bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    if img_path:
        texn = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
        texn.image = bpy.data.images.load(img_path, check_existing=True)
        texn.image.alpha_mode = "CHANNEL_PACKED"
        nt.links.new(texn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
    return mat


def set_corner_colors(me, rgb_srgb, gloss):
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(rgb_srgb), gloss[:, None]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0


# =====================================================================================================================
# build
# =====================================================================================================================
def build_body():
    Vh, Th = mesh_field(fig_d, 0.0032, lo=(-0.30, -0.27, -0.012), hi=(0.30, 0.47, 1.06))
    lo, hi = Vh.min(0), Vh.max(0)
    shift = np.array([0.0, 0.0, -lo[2]])
    scale = 1.0 / (hi[2] - lo[2])
    log("bounds", np.round(lo, 3), np.round(hi, 3), "scale", round(scale, 4))
    ob = to_mesh("Zip", Vh, Th)
    decimate(ob, BUDGET_BODY)
    V, T = mesh_np(ob)
    V = project(fig_d, V, it=4, clip=0.003).astype(np.float64)  # back onto the exact surface
    ob.data.vertices.foreach_set("co", V.ravel())
    ob.data.update()
    log(f"game mesh {len(V)} verts {len(T)} tris")
    uvl = smart_uv(ob)
    me = ob.data
    # polygons are triangles; loops in polygon order
    T = np.array([list(p.vertices) for p in me.polygons])
    lst = np.array([p.loop_start for p in me.polygons])
    UVc = uvl[(lst[:, None] + np.arange(3)[None]).ravel()]
    Ix, Tt, B = raster(V, T, UVc, TEX)
    P = (V[T[Tt, 0]] * B[:, :1] + V[T[Tt, 1]] * B[:, 1:2] + V[T[Tt, 2]] * B[:, 2:3]).astype(F32)
    P = project(fig_d, P, it=3, clip=0.004)
    log(f"texels {len(P)} ({len(P) / TEX ** 2 * 100:.0f}%)")
    rgb, gl, _ = paint(P, PARTS, fig_d)
    img = np.zeros((TEX, TEX, 4))
    filled = np.zeros((TEX, TEX), bool)
    img[Ix[:, 1], Ix[:, 0], :3] = rgb
    img[Ix[:, 1], Ix[:, 0], 3] = gl
    filled[Ix[:, 1], Ix[:, 0]] = True
    img = dilate(img, filled)
    tex_path = os.path.join(OUT_DIR, "zip_tex.png")
    im = bpy.data.images.new("zip_tex", TEX, TEX, alpha=True)
    im.pixels.foreach_set(img.astype(np.float32).ravel())
    im.filepath_raw = tex_path
    im.file_format = "PNG"
    im.save()
    # per-corner colour = texture at the corner UV
    cx = np.clip((UVc[:, 0] * TEX).astype(int), 0, TEX - 1)
    cy = np.clip((UVc[:, 1] * TEX).astype(int), 0, TEX - 1)
    smp = img[cy, cx]
    # rebuild normalised object
    W = vertex_weights(V)
    Vn = (V + shift) * scale
    me.vertices.foreach_set("co", Vn.ravel())
    me.update()
    me.uv_layers.active.name = "UVMap"
    set_corner_colors(me, smp[:, :3], smp[:, 3])
    me.materials.append(make_material(tex_path))
    for p in me.polygons:
        p.use_smooth = True
    # armature
    eyes = {}
    for s in SIDES:
        ex, ez = EYE_C[s]
        p = trace(fig_d, (ex, -0.3, ez), (0, 1, 0))
        eyes[s] = p + np.array([0, 0.012, 0])
    prop = HAND[-1]
    bone_def = [
        ("Root", None, (0, 0, 0), (0, 0, 0.425)), ("Body", "Root", (0, 0, 0.425), (0, 0, 0.68)),
        ("Head", "Body", (0, 0, 0.68), (0, 0, 1.0 / scale - shift[2])),
        ("ArmR", "Body", SH[-1], WR[-1]), ("ArmL", "Body", SH[1], WR[1]),
        ("LegR", "Root", HIP[-1], AN[-1]), ("LegL", "Root", HIP[1], AN[1]),
        ("Prop", "Body", prop, prop + np.array([0, -0.10, 0])),
        ("EyeR", "Head", eyes[-1], eyes[-1] + (0, -0.03, 0)), ("EyeL", "Head", eyes[1], eyes[1] + (0, -0.03, 0)),
        ("LidR", "Head", eyes[-1], eyes[-1] + (0, -0.03, 0)), ("LidL", "Head", eyes[1], eyes[1] + (0, -0.03, 0)),
    ]
    arm_data = bpy.data.armatures.new("ZipRig")
    arm = bpy.data.objects.new("Rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, parent, hd, tl in bone_def:
        eb = arm_data.edit_bones.new(bn)
        eb.head = Vector((np.asarray(hd, float) + shift) * scale)
        eb.tail = Vector((np.asarray(tl, float) + shift) * scale)
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
    export([ob, arm], os.path.join(OUT_DIR, "zip_game"), {"ARMATURE", "MESH"})
    log("RESULT body tris", len(T), "verts", len(V), "max infl", max(len(w) for w in W))
    log("Prop head", np.round((prop + shift) * scale, 3), "eyes", np.round((eyes[-1] + shift) * scale, 3))
    return shift, scale, len(T)


def export(objs, base, types):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[-1]
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types=types, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    try:
        bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True, export_vertex_color="ACTIVE")
    except TypeError:
        bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)


def build_weapon(scale):
    for o in list(bpy.data.objects):  # body is exported: drop it and its textured material (the weapon gets a plain one)
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
    Vh, Th = mesh_field(weap_d, 0.0012, lo=(-0.08, -0.26, -0.07), hi=(0.08, 0.08, 0.12))
    ob = to_mesh("ZipHookshot", Vh, Th)
    decimate(ob, BUDGET_WEAP)
    V, T = mesh_np(ob)
    V = project(weap_d, V, it=3, clip=0.002).astype(np.float64)
    bpy.data.objects.remove(ob, do_unlink=True)
    ob = to_mesh("ZipHookshot", V * scale, T)
    me = ob.data
    V, T = mesh_np(ob)
    V = V / scale
    C = V[T].mean(1).astype(F32)
    C = project(weap_d, C, it=3, clip=0.003)
    rgb, gl, _ = paint(C, WPARTS, weap_d)
    set_corner_colors(me, np.repeat(rgb, 3, 0), np.repeat(gl, 3, 0))
    me.materials.append(make_material(None))
    mz = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(mz)
    mz.location = Vector(MUZZLE * scale)
    mz.parent = ob
    export([mz, ob], os.path.join(OUT_DIR, "zip_weapon"), {"MESH", "EMPTY"})
    log("RESULT weapon tris", len(T), "muzzle", np.round(MUZZLE * scale, 3))
    return len(T)


bpy.ops.wm.read_factory_settings(use_empty=True)
shift, scale, nb = build_body()
nw = build_weapon(scale)
log(f"done body {nb} tris, weapon {nw} tris")
