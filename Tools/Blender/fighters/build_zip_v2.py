# Zip v2 (id zip), the parkour assassin with a grappling hook (Docs/Briefs/A-Roster3D.md "Wave 3", Docs/Roster-v2.md §4 #6):
# brought up to the Theo v3 level. Headless (Blender 5.2, numpy only):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_zip_v2.py -- [out_dir]
# Output (default Art/Source/3D/zip/, same names as v1): zip_game.fbx/.glb (<= 4000 tris, mesh "Zip", material "FighterVC"
# with zip_tex.png, armature "Rig" = the 12 FighterBone bones + "Tail" (scarf), UV "UVMap", corner colour "Col" = blurred
# texture, alpha = gloss), zip_weapon.fbx/.glb (grapple launcher <= 1200 tris, UV + zip_weapon_tex.png + "Col", origin =
# grip centre in the right fist, barrel along -Y, empty "Muzzle" at the hook tip), zip_tex.png / zip_weapon_tex.png
# (1024 / 512 RGBA: RGB = albedo with baked light ramp + AO, A = gloss).
# What changed against v1 (build_zip.py, left untouched): one smooth SDF suit (hoodie + joggers + arms + legs blended, no
# creases at the hips / shoulders), gloves with palm + 3 fingers + thumb (right fist wrapped round the launcher grip), eyes
# as geometry (eyeballs in shallow sockets, own shells, skinned to EyeR/EyeL), raised brows, carved smirk, curved hair
# tufts, Theo v3 pipeline (per-shell decimation, smart UV with a larger face, texel-exact bake with tonal ramps),
# smooth joint weights (shoulder, elbow, hip, waist, neck), the scarf tail on its own bone "Tail" (Body -> Tail gradient),
# textured weapon.
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
    print(f"[zip2 {time.time() - T0:6.1f}s]", *a, flush=True)


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
# parts, palette ramps
# =====================================================================================================================
def hexc(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])


H = hexc
SIDES = (-1, 1)
SN = {-1: "R", 1: "L"}


def sstep(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mixc(a, b, t):
    t = np.asarray(t, float)
    return np.asarray(a, float) + (np.asarray(b, float) - np.asarray(a, float)) * (t[..., None] if t.ndim else t)


def stroke(c, w, col):
    return mixc(c, col, np.clip(w, 0, 1))


def line_w(d, half, soft=0.0007):
    return 1 - sstep(half - soft, half + soft, d)


KEY_L = nrm((-0.30, -0.62, 0.73))   # baked key light: front, above, from the viewer's left (as Theo v3)
HALF_V = nrm(KEY_L + np.array([0.0, -1.0, 0.0]))
MAT = {  # ramp (pos, sRGB) over the baked light value, kind, AO strength, gloss
    "skin": (((0.0, "#7E4A2A"), (0.5, "#C68F60"), (1.0, "#EEC59C")), "plastic", 0.48, 0.18),
    "hair": (((0.0, "#100B08"), (0.45, "#2E231D"), (0.8, "#4C3A2F"), (1.0, "#6E5747")), "plastic", 0.70, 0.32),
    "hood": (((0.0, "#111015"), (0.5, "#2A2830"), (1.0, "#4C4954")), "plastic", 0.60, 0.14),
    "crimson": (((0.0, "#43051A"), (0.4, "#8C0E2C"), (0.7, "#C8173F"), (1.0, "#F04C6A")), "plastic", 0.60, 0.55),
    "pants": (((0.0, "#1A1B20"), (0.5, "#3C3E47"), (1.0, "#646772")), "plastic", 0.60, 0.10),
    "shoe": (((0.0, "#86827C"), (0.5, "#D9D4CB"), (1.0, "#FFFDF8")), "plastic", 0.50, 0.55),
    "glove": (((0.0, "#1C1C22"), (0.5, "#45454E"), (1.0, "#70707C")), "plastic", 0.55, 0.35),
    "rope": (((0.0, "#7E540A"), (0.45, "#D29C1C"), (0.75, "#F2C431"), (1.0, "#FFE88E")), "plastic", 0.60, 0.25),
    "strap": (((0.0, "#35240F"), (0.5, "#7A5A34"), (1.0, "#AC895C")), "plastic", 0.55, 0.20),
    "metal": (((0.0, "#4A4E55"), (0.5, "#A6ACB4"), (1.0, "#F6F8FB")), "metal", 0.40, 0.85),
    "dark": (((0.0, "#111115"), (0.5, "#2E2E35"), (1.0, "#575762")), "plastic", 0.55, 0.30),
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
        hor = np.exp(-(nz / 0.22) ** 2)
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
    return MAT[key][3]


class Part:
    def __init__(self, name, node, key, bone, paint=None, prio=0):
        self.name, self.node, self.key, self.bone, self.prio = name, node, key, bone, prio
        self.paint = paint or paint_flat(key)
        self.lo, self.hi = node.lo, node.hi


PARTS = []


def paint_flat(key, spec=0.08):
    def f(P, N, occ):
        return shade(key, N, occ, spec), np.full(len(P), gloss_of(key))
    return f


def part(name, node, key, bone, paint=None, prio=0, lst=None):
    p = Part(name, node, key, bone, paint, prio)
    (PARTS if lst is None else lst).append(p)
    return node


def unit_n(node, p):
    g = grad(node.d, np.asarray(p, F32)[None])[0].astype(float)
    return g / np.linalg.norm(g)


def front_pt(node, x, z):
    return trace(node.d, (x, -0.3, z), (0, 1, 0))


def catmull(P, n):
    """Smooth path through control points P (Catmull-Rom), n samples."""
    P = np.asarray(P, float)
    Q = np.vstack([P[0] * 2 - P[1], P, P[-1] * 2 - P[-2]])
    out = []
    for t in np.linspace(0, len(P) - 1, n):
        i = min(int(t), len(P) - 2)
        u = t - i
        p0, p1, p2, p3 = Q[i], Q[i + 1], Q[i + 2], Q[i + 3]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3))
    return np.array(out)


# =====================================================================================================================
# MODEL (Blender Z up, faces -Y, +X = the character's left). Height ~1.0. Same proportions / joints as v1.
# =====================================================================================================================
SH = {s: mirror((-0.128, 0.006, 0.622), s) for s in SIDES}
EL = {-1: np.array([-0.170, -0.030, 0.520]), 1: np.array([0.180, -0.005, 0.522])}
WR = {-1: np.array([-0.158, -0.135, 0.478]), 1: np.array([0.192, -0.050, 0.440])}
HIP = {s: mirror((-0.056, 0.005, 0.425), s) for s in SIDES}
KN = {s: mirror((-0.066, -0.010, 0.255), s) for s in SIDES}
AN = {s: mirror((-0.070, 0.006, 0.095), s) for s in SIDES}

# ---------------- suit: hoodie torso + joggers + arms + legs, one smooth body ----------------
CHEST = Ell((0, 0.004, 0.585), (0.122, 0.082, 0.095))
BELLY = Ell((0, 0.0, 0.49), (0.108, 0.074, 0.085))
TORSO = U(CHEST, BELLY, k=0.05)
PELVIS = Ell((0, 0.004, 0.428), (0.100, 0.070, 0.060))
SHB, UPA, FOA, THI, SHN = {}, {}, {}, {}, {}
for s in SIDES:
    SHB[s] = Ell(SH[s] + mirror((0.006, 0, 0.006), s), (0.052, 0.050, 0.050))
    UPA[s] = RCone(SH[s], EL[s], 0.046, 0.040)
    d = nrm(WR[s] - EL[s])
    FOA[s] = RCone(EL[s], WR[s] - d * 0.004, 0.040, 0.035)
    THI[s] = U(RCone(HIP[s], mirror((-0.062, 0.000, 0.340), s), 0.058, 0.056),
               RCone(mirror((-0.062, 0.000, 0.340), s), KN[s], 0.056, 0.046), k=0.01)
    SHN[s] = RCone(KN[s], AN[s] + (0, 0, 0.02), 0.046, 0.039)
UPPER = U(TORSO, *[U(SHB[s], UPA[s], FOA[s], k=0.012) for s in SIDES], k=0.024)
LOWER = U(PELVIS, *[U(THI[s], SHN[s], k=0.012) for s in SIDES], k=0.02)
SUIT = U(UPPER, LOWER, k=0.03)
WEIGHT_GROUPS = [("torso", TORSO), ("Root", PELVIS)]
for s in SIDES:
    WEIGHT_GROUPS += [("Body", SHB[s]), ("Arm" + SN[s], UPA[s]), ("Arm" + SN[s], FOA[s]), ("Leg" + SN[s], THI[s]),
                      ("Leg" + SN[s], SHN[s])]
PANTS_GROUP = U(PELVIS, *THI.values(), *SHN.values())
TOP_GROUP = U(TORSO, *SHB.values(), *UPA.values(), *FOA.values())


def arm_stripe(P, s):
    """Distance (radians) of P from the outer line of the arm s (for the sleeve stripe)."""
    best = np.full(len(P), 9.0)
    for a, b in ((SH[s], EL[s]), (EL[s], WR[s])):
        d = nrm(b - a)
        o = np.array([s, 0, 0.0]) - d * d[0] * s
        o = nrm(o)
        f = np.cross(d, o)
        t = np.clip((P - a) @ d / np.linalg.norm(b - a), -0.2, 1.05)
        r = P - (a + np.outer(t, b - a))
        ang = np.abs(np.arctan2(r @ f, r @ o))
        best = np.minimum(best, ang)
    return best


def leg_stripe(P, s):
    a, b = HIP[s], AN[s]
    d = nrm(b - a)
    o = nrm(np.array([s, 0, 0.0]) - d * d[0] * s)
    f = np.cross(d, o)
    t = (P - a) @ d
    r = P - (a + np.outer(t, d))
    return np.abs(np.arctan2(r @ f, r @ o))


def paint_suit(P, N, occ):
    dp = PANTS_GROUP.d(P.astype(F32))
    dt = TOP_GROUP.d(P.astype(F32))
    pants = sstep(-0.004, 0.004, dt - dp)   # 1 where the joggers are closer
    c = mixc(shade("hood", N, occ, 0.05), shade("pants", N, occ, 0.03), pants)
    g = 0.14 * (1 - pants) + 0.10 * pants
    cr = shade("crimson", N, occ, 0.3)
    for s in SIDES:
        side = (P[:, 0] * s > 0.02)
        a = arm_stripe(P, s)
        w = line_w(a, 0.16, 0.03) * (1 - pants) * side
        c, g = stroke(c, w, cr), np.where(w > 0.5, 0.5, g)
        lg = leg_stripe(P, s)
        w = line_w(lg, 0.13, 0.03) * pants * side * (P[:, 2] > 0.13)
        c, g = stroke(c, w, cr), np.where(w > 0.5, 0.5, g)
    # zipper: dark tape with light teeth down the hoodie front
    zf = (np.abs(P[:, 0]) < 0.006) & (P[:, 1] < -0.03) & (P[:, 2] > 0.445) & (P[:, 2] < 0.66)
    teeth = 0.5 + 0.5 * np.sin(P[:, 2] * 2400)
    zc = mixc(H("#141318"), H("#B9BEC6"), teeth * line_w(np.abs(P[:, 0]), 0.0022, 0.0006))
    c = stroke(c, zf * line_w(np.abs(P[:, 0]), 0.0042, 0.0008), zc)
    # soft fabric folds: elbow / knee creases, waist pull
    for s in SIDES:
        e = np.exp(-np.sum(((P - EL[s]) / 0.03) ** 2, 1)) * (0.5 + 0.5 * np.sin(P[:, 2] * 260))
        c = c * (1 - 0.10 * e)[:, None]
        k = np.exp(-np.sum(((P - (KN[s] + (0, 0.03, 0))) / 0.035) ** 2, 1)) * (0.5 + 0.5 * np.sin(P[:, 2] * 220))
        c = c * (1 - 0.12 * k)[:, None]
    return np.clip(c, 0, 1), g


part("suit", SUIT, "hood", ("suit",), paint_suit)
TORSO_OFF = Off(TORSO, 0.0)
part("hem", I(Off(SUIT, 0.006), Slab((0, 0, 1), (0, 0, 0.444), 0.016), k=0.004), "crimson", ("waist",))
part("pocket", I(Off(TORSO, 0.006), Poly2([(-0.062, 0.462), (0.062, 0.462), (0.046, 0.525), (-0.046, 0.525)], "xz"),
                  Half((0, 1, 0), (0, -0.02, 0)), k=0.004), "hood", ("waist",), paint_flat("hood", 0.04))
part("hoodfold", U(Ell((0, 0.060, 0.655), (0.098, 0.052, 0.045)), Ell((0, 0.085, 0.625), (0.07, 0.035, 0.05)), k=0.03),
     "hood", "Body")
part("hoodlin", I(Ell((0, 0.058, 0.672), (0.085, 0.045, 0.034)), Half((0, 0, -1), (0, 0, 0.675))), "crimson", "Body")
# arms: crimson cuffs; legs: crimson ankle cuffs + sneakers
for s in SIDES:
    d = nrm(WR[s] - EL[s])
    part("cuff" + SN[s], RCyl(WR[s] - d * 0.010, d, 0.041, 0.013, rad=0.007), "crimson", "Arm" + SN[s])
    part("ankle" + SN[s], RCyl(AN[s] + (0, 0, 0.03), (0, 0, 1), 0.042, 0.016, rad=0.008), "crimson", "Leg" + SN[s])
SHOE = {}
for s in SIDES:
    shoe = I(U(Ell(mirror((-0.071, -0.024, 0.048), s), (0.050, 0.090, 0.050)),
               Ell(mirror((-0.070, 0.012, 0.075), s), (0.044, 0.050, 0.042)),
               Ell(mirror((-0.070, -0.040, 0.068), s), (0.040, 0.050, 0.030)), k=0.02), Half((0, 0, -1), (0, 0, 0.0)), k=0.004)
    SHOE[s] = shoe

    def paint_shoe(P, N, occ, s=s):
        c = shade("shoe", N, occ, 0.25)
        g = np.full(len(P), gloss_of("shoe"))
        x = P[:, 0] - s * 0.071
        # laces on the instep: short dark bars across the top
        top = (N[:, 2] > 0.35) & (P[:, 1] < 0.01) & (P[:, 1] > -0.075) & (np.abs(x) < 0.017)
        bars = line_w(np.abs(((P[:, 1] + 0.075) % 0.016) - 0.008), 0.0022, 0.0006)
        c = stroke(c, top * bars, H("#2E2C33"))
        # crimson heel tab and side flash (plain arc, no logo)
        heel = np.exp(-(((P[:, 1] - 0.058) / 0.012) ** 2 + ((P[:, 2] - 0.085) / 0.018) ** 2))
        c = stroke(c, sstep(0.4, 0.6, heel), shade("crimson", N, occ, 0.3))
        side = (x * s > 0.025)
        arc = np.abs(P[:, 2] - (0.040 + 0.25 * (P[:, 1] + 0.02) ** 2 * 10))
        c = stroke(c, side * line_w(arc, 0.004, 0.001) * (P[:, 1] > -0.07) * (P[:, 1] < 0.04), shade("crimson", N, occ, 0.3))
        return c, g
    part("shoe" + SN[s], shoe, "shoe", "Leg" + SN[s], paint_shoe)
    part("sole" + SN[s], I(Off(shoe, 0.004), Slab((0, 0, 1), (0, 0, 0.008), 0.010), k=0.002), "crimson", "Leg" + SN[s])

# ---------------- scarf: wrap round the neck + long tail flowing back (bone Tail) ----------------
SC_CTRL = np.array([(0.030, 0.064, 0.674), (0.056, 0.110, 0.642), (0.070, 0.165, 0.604), (0.068, 0.225, 0.568),
                    (0.052, 0.285, 0.543), (0.064, 0.345, 0.522), (0.092, 0.402, 0.502)])
SC = catmull(SC_CTRL, 22)
nsc = len(SC)
tw = np.linspace(0, 3.0, nsc)
SW = np.stack([np.ones(nsc), np.sin(tw) * 0.25, np.full(nsc, 0.05)], 1)
ST = np.tile(np.array([[0.0, -0.1, 1.0]]), (nsc, 1))
SCARF_T = Ribbon(SC, SW, ST, np.linspace(0.034, 0.047, nsc), np.linspace(0.0095, 0.0080, nsc))
SCARF_W = Torus((0, 0.006, 0.684), (0, 0.15, 1), 0.058, 0.025, sxy=(1.0, 0.9))


def paint_scarf(P, N, occ):
    c = shade("crimson", N, occ, 0.35)
    _, sp, xw, _ = SCARF_T.local(P.astype(F32))
    dw = SCARF_W.d(P.astype(F32))
    on_tail = (SCARF_T.d(P.astype(F32)) < dw)
    # knit ribs across the wrap, two darker bands near the tip, slightly darker edges of the tail
    rib = 0.5 + 0.5 * np.sin(np.arctan2(P[:, 0], P[:, 1] - 0.006) * 34)
    c = c * (1 - 0.07 * rib * (~on_tail))[:, None]
    band = (line_w(np.abs(sp - 0.80), 0.022, 0.006) + line_w(np.abs(sp - 0.88), 0.014, 0.005)) * on_tail
    c = stroke(c, np.clip(band, 0, 1), shade("hood", N, occ, 0.1))
    edge = sstep(0.75, 1.0, np.abs(xw)) * on_tail
    c = c * (1 - 0.18 * edge)[:, None]
    # fringe stripes at the very tip
    fr = on_tail & (sp > 0.965)
    c = c * (1 - 0.25 * fr * (0.5 + 0.5 * np.sin(xw * 18)))[:, None]
    return np.clip(c, 0, 1), np.full(len(P), gloss_of("crimson"))


part("scarf", U(SCARF_W, SCARF_T, k=0.012), "crimson", ("scarf",), paint_scarf)

# ---------------- rope coil on the back + chest strap ----------------
COIL_C = np.array([0.0, 0.092, 0.545])


def paint_coil(P, N, occ):
    c = shade("rope", N, occ, 0.15)
    ang = np.arctan2(P[:, 2] - COIL_C[2], P[:, 0] - COIL_C[0])
    st = 0.5 + 0.5 * np.sin(ang * 40 + P[:, 1] * 300)
    c = c * (0.80 + 0.20 * st)[:, None]
    return c, np.full(len(P), gloss_of("rope"))


part("coil", U(Torus(COIL_C, (0, 1, 0.15), 0.058, 0.019), Torus(COIL_C + (0.006, 0.012, -0.004), (0.1, 1, 0.15), 0.052, 0.017),
               Torus(COIL_C + (-0.004, 0.024, 0.003), (-0.1, 1, 0.1), 0.046, 0.015), k=0.004), "rope", "Body", paint_coil)


def paint_strap(P, N, occ):
    c = shade("strap", N, occ, 0.1)
    n = nrm((0.75, 0, 1))
    off = (P - np.array([0, 0, 0.54])) @ n
    c = stroke(c, line_w(np.abs(np.abs(off) - 0.010), 0.0008, 0.0004) * 0.6, H("#D8B888"))   # stitching
    bk = np.exp(-(((P[:, 0] + 0.045) / 0.012) ** 2 + ((P[:, 2] - 0.574) / 0.012) ** 2)) * (P[:, 1] < 0)
    c = stroke(c, sstep(0.45, 0.55, bk), shade("metal", N, occ))
    return c, np.where(bk > 0.5, 0.85, gloss_of("strap"))


part("strap", I(Shell(Off(TORSO, 0.0065), 0.0042), Slab(nrm((0.75, 0, 1)), (0, 0, 0.54), 0.0145), k=0.002), "strap",
     "Body", paint_strap)

# ---------------- hands: gloves with palm, 3 fingers, thumb ----------------
# right: a fist round the launcher grip; the grip centre is the Prop bone (weapon origin), grip axis as in the weapon
GRIP_A = nrm((0, -0.25, 1))
HAND_INFO = {}
s = -1
dR = nrm(WR[s] - EL[s])
o_ = nrm(np.array([-1.0, 0, 0]) - GRIP_A * GRIP_A[0] * -1)
f_ = nrm(np.array([0, -1.0, 0]) - GRIP_A * np.dot(GRIP_A, (0, -1, 0)))
f_ = nrm(f_ - o_ * np.dot(f_, o_))
PC_R = WR[s] + dR * 0.032 + np.array([0, 0, -0.004])
PROP = PC_R - o_ * 0.030 + f_ * 0.004          # grip centre: 3 cm inward of the palm
Rh = np.stack([o_, f_, GRIP_A], 1)


def ring(h, ang, r):
    a = math.radians(ang)
    return PROP + GRIP_A * h + r * (math.cos(a) * o_ + math.sin(a) * f_)


fingers = []
for k, h in enumerate((0.0205, 0.0, -0.0205)):
    tipa = (186, 182, 172)[k]
    pts = [ring(h, 36, 0.036), ring(h, 88, 0.0315), ring(h, 140, 0.0305), ring(h, tipa, 0.030)]
    rads = [0.0118, 0.0112, 0.0104, 0.0094]
    fingers += [RCone(pts[i], pts[i + 1], rads[i], rads[i + 1]) for i in range(3)]
thumb_pts = [PC_R + f_ * -0.014 + GRIP_A * 0.018 - o_ * 0.004, ring(0.029, -95, 0.032), ring(0.033, -160, 0.0295)]
thumb = U(RCone(thumb_pts[0], thumb_pts[1], 0.0150, 0.0132), RCone(thumb_pts[1], thumb_pts[2], 0.0132, 0.0112), k=0.004)
palm = RBox(PC_R, (0.0135, 0.027, 0.031), 0.0125, Rh)
wrist = RCone(WR[s] - dR * 0.014, PC_R - f_ * 0.012, 0.031, 0.030)
HANDN = {s: U(U(wrist, palm, thumb, k=0.010), U(*fingers, k=0.0), k=0.007)}
HAND_INFO[s] = (PC_R, o_, f_, GRIP_A)
PLATE = {s: I(Off(HANDN[s], 0.0042), RBox(PC_R + o_ * 0.012 + f_ * 0.006, (0.012, 0.022, 0.026), 0.009, Rh), k=0.003)}
# left: relaxed, fingers half bent toward the palm (Theo v3 hand)
s = 1
dn = nrm(nrm(WR[s] - EL[s]) + np.array([0.18, 0, 0]))
f0 = np.array([-s * 0.55, -0.83, 0.0])
fw = nrm(f0 - dn * np.dot(f0, dn))
out = np.cross(fw, dn)
out = nrm(out if out[0] * s > 0 else -out)
W0 = WR[s]
pc = W0 + dn * 0.044 + out * 0.004
RhL = np.stack([out, fw, dn], 1)
palmL = RBox(pc + out * 0.002, (0.025, 0.043, 0.038), 0.018, RhL)
wristL = RCone(W0 - dn * 0.014, pc - dn * 0.012, 0.032, 0.032)
fl = []
for k, fo in enumerate((-0.0235, 0.0, 0.0235)):
    ln = (0.92, 1.0, 0.94)[k]
    b0 = pc + dn * 0.025 + fw * fo + out * 0.003
    b1 = b0 + dn * 0.031 * ln + out * 0.001 + fw * fo * 0.05
    b2 = b1 + dn * 0.022 * ln - out * 0.011
    b3 = b2 + dn * 0.007 * ln - out * 0.012
    fl += [RCone(b0, b1, 0.0127, 0.0121), RCone(b1, b2, 0.0121, 0.0110), RCone(b2, b3, 0.0110, 0.0098)]
tb = pc + fw * 0.032 - out * 0.010 - dn * 0.014
tm = tb + fw * 0.012 + dn * 0.029 - out * 0.004
tt = tm + fw * 0.004 + dn * 0.023 - out * 0.006
thumbL = U(RCone(tb, tm, 0.0176, 0.0164), RCone(tm, tt, 0.0164, 0.0137), k=0.004)
HANDN[s] = U(U(wristL, palmL, thumbL, k=0.010), U(*fl, k=0.0), k=0.007)
HAND_INFO[s] = (pc, out, fw, dn)
PLATE[s] = I(Off(HANDN[s], 0.0042), RBox(pc + out * 0.024 - dn * 0.003 + fw * 0.004, (0.012, 0.032, 0.025), 0.009, RhL), k=0.003)


def paint_glove(s):
    pc_, out_, _, _ = HAND_INFO[s]

    def f(P, N, occ):
        c = shade("glove", N, occ, 0.15)
        # palm grip pad: slightly darker on the inner side
        inner = sstep(0.0, -0.02, (P - pc_) @ out_)
        c = c * (1 - 0.15 * inner)[:, None]
        return c, np.full(len(P), gloss_of("glove"))
    return f


def paint_plate(P, N, occ):
    c = shade("crimson", N, occ, 0.4)
    return c, np.full(len(P), 0.6)


for s in SIDES:
    part("hand" + SN[s], HANDN[s], "glove", "Arm" + SN[s], paint_glove(s))
    part("plate" + SN[s], PLATE[s], "crimson", "Arm" + SN[s], paint_plate)

# ---------------- head ----------------
CRAN = Ell((0, 0.004, 0.838), (0.104, 0.104, 0.112))
FACE = Ell((0, -0.020, 0.782), (0.094, 0.086, 0.075))
CHIN = Ell((0, -0.066, 0.735), (0.038, 0.030, 0.022))
CHEEKS = [Ell(mirror((-0.053, -0.058, 0.764), s), (0.046, 0.040, 0.036)) for s in SIDES]
SKULL = U(CRAN, FACE, CHIN, *CHEEKS, k=0.04)
NOSE = U(Ell((0, -0.103, 0.778), (0.015, 0.016, 0.014)), Ell((0, -0.096, 0.792), (0.008, 0.011, 0.013)), k=0.01)
EARS = []
for s in SIDES:
    Re = rot((0, 0, 1), s * 18) @ rot((0, 1, 0), s * -6)
    ear = Ell(mirror((-0.100, 0.012, 0.795), s), (0.017, 0.024, 0.030), Re)
    concha = Ell(mirror((-0.114, 0.006, 0.793), s), (0.008, 0.014, 0.019), Re)
    EARS.append(S(ear, concha, k=0.005))
HEAD0 = U(SKULL, NOSE, *EARS, k=0.012)
# eyes: eyeballs (ellipsoids) in shallow sockets, gaze straight ahead and a touch outward
EYE = {}
for s in SIDES:
    p = front_pt(SKULL, s * 0.045, 0.801)
    g = nrm(unit_n(SKULL, p) + np.array([0, -1.6, 0]))
    up = np.array([0, 0, 1.0])
    w = nrm(up - g * np.dot(up, g))
    u = np.cross(g, w)
    R = np.stack([u, g, w], 1)
    rad = np.array([0.0215, 0.0150, 0.0255])
    c = p - g * (rad[1] - 0.0060)
    EYE[s] = (c, g, R, rad, Ell(c, rad, R))
# smirk: a shallow carved groove, the character's left corner (+X) lifted
MOUTH_XZ = [(-0.024, 0.7445), (-0.012, 0.7418), (0.000, 0.7412), (0.012, 0.7428), (0.022, 0.7462), (0.030, 0.7515)]
MC = np.array([front_pt(HEAD0, x, z) for x, z in MOUTH_XZ])
MN = np.array([unit_n(HEAD0, c) for c in MC])
MW = np.gradient(MC, axis=0)
MOUTH = Ribbon(MC + MN * 0.0004, MW, MN, np.full(len(MC), 0.0010), np.linspace(0.0016, 0.0020, len(MC)))
HEAD = S(HEAD0, MOUTH, *[Off(EYE[s][4], 0.0012) for s in SIDES], k=0.0025)
NECK = RCone((0, 0.004, 0.63), (0, 0.004, 0.73), 0.039, 0.037)
LASH = H("#1C120D")


def paint_head(P, N, occ):
    c = shade("skin", N, occ, 0.10)
    g = np.full(len(P), gloss_of("skin"))
    x, z = P[:, 0], P[:, 2]
    front = sstep(0.1, 0.5, -N[:, 1])
    for s in SIDES:
        b = np.exp(-(((x - s * 0.062) / 0.020) ** 2 + ((z - 0.764) / 0.012) ** 2)) * front
        c = stroke(c, b * 0.35, H("#D9806A"))
        e = np.exp(-(((x - s * 0.110) / 0.012) ** 2 + ((z - 0.794) / 0.022) ** 2)) * sstep(0.2, 0.7, s * N[:, 0])
        c = stroke(c, e * 0.30, H("#C77458"))
        # lash line: a dark rim over the upper half of each socket, thicker at the outer corner, small flick
        ec, gz, R, rad, ell = EYE[s]
        de = ell.d(P.astype(F32))
        Q = (P - ec) @ R
        vu = Q[:, 2] / rad[2]
        uu = Q[:, 0] / rad[0]
        outer = sstep(-0.3, 1.0, s * uu)
        top = sstep(-0.35, 0.05, vu)
        lw = line_w(de, 0.0030 + 0.0016 * outer, 0.0006) * top * front
        c = stroke(c, lw, LASH)
        fl_d = seg3(P, [ec + R @ np.array([s * rad[0] * 0.85, 0, rad[2] * 0.35]), ec + R @ np.array([s * (rad[0] + 0.006), -0.004, rad[2] * 0.62])])
        c = stroke(c, line_w(fl_d, 0.0017, 0.0005) * front, LASH)
        low = line_w(np.abs(de - 0.0012), 0.0007, 0.0004) * sstep(0.1, -0.4, vu) * (np.abs(uu) < 0.8) * front
        c = stroke(c, low * 0.35, H("#6E3E28"))
    nose = np.exp(-((x / 0.012) ** 2 + ((z - 0.774) / 0.010) ** 2)) * front
    c = stroke(c, nose * 0.18, H("#D98A6E"))
    # mouth (the carved groove) dark, a small lip tint under it
    dm = MOUTH.d(P.astype(F32))
    c = stroke(c, line_w(dm, 0.0016, 0.0007) * front, H("#5A2A20"))
    lip = np.exp(-(((x - 0.004) / 0.016) ** 2 + ((z - 0.7365) / 0.0035) ** 2)) * front
    c = stroke(c, lip * 0.22, H("#B86A58"))
    # faint stubble-free chin highlight; tiny scar-plaster? no: keep the face clean
    return np.clip(c, 0, 1), g


def paint_eye(s):
    ec, gz, R, rad, _ = EYE[s]

    def f(P, N, occ):
        Q = (P - ec) @ R
        u, v = Q[:, 0], Q[:, 2]
        white = mixc(H("#FFFFFF"), H("#D6D2D4"), sstep(-0.004, 0.016, v) * 0.7)   # the lid shades the top
        c = white * (1 - 0.35 * occ)[:, None]
        iu, iv = s * -0.0010, -0.0008
        r = np.sqrt((u - iu) ** 2 + (v - iv) ** 2)
        ir = 0.0128
        icol = mixc(H("#B07A3E"), H("#3E2410"), sstep(0.004, ir, r))
        icol = mixc(icol, icol * 1.3, sstep(0.0, -0.010, v - iv) * 0.5)
        c = stroke(c, line_w(r, ir, 0.0005), np.clip(icol, 0, 1))
        c = stroke(c, line_w(np.abs(r - ir + 0.0006), 0.0008, 0.0004) * 0.8, H("#24140A"))
        c = stroke(c, line_w(r, 0.0060, 0.0004), H("#120A06"))
        r1 = np.sqrt((u - iu - s * -0.0046) ** 2 + (v - iv - 0.0054) ** 2)
        r2 = np.sqrt((u - iu - s * 0.0044) ** 2 + (v - iv + 0.0048) ** 2)
        c = stroke(c, line_w(r1, 0.0034, 0.0004), H("#FFFFFF"))
        c = stroke(c, line_w(r2, 0.0016, 0.0003) * 0.9, H("#FFFFFF"))
        return np.clip(c, 0, 1), np.full(len(P), 1.0)
    return f


# brows: raised ribbons, cocky slant (the character's right brow arched higher)
BROWS = {-1: [(-0.077, 0.839), (-0.062, 0.851), (-0.045, 0.855), (-0.029, 0.851), (-0.018, 0.844)],
         1: [(0.017, 0.840), (0.030, 0.845), (0.046, 0.847), (0.062, 0.843), (0.076, 0.835)]}
BROWN = {}
for s in SIDES:
    C = np.array([front_pt(HEAD0, x, z) for x, z in BROWS[s]])
    Nn = np.array([unit_n(HEAD0, c) for c in C])
    C = catmull(C, 9)
    Nn = catmull(Nn, 9)
    Nn /= np.linalg.norm(Nn, axis=1, keepdims=True)
    Wv = np.gradient(C, axis=0)
    inner = np.linspace(0, 1, len(C)) if s > 0 else np.linspace(1, 0, len(C))   # 0 at the nose end
    wv = 0.0058 * (1 - 0.40 * (1 - inner) ** 1.5) + 0.0010
    BROWN[s] = Ribbon(C + Nn * 0.0012, Wv, Nn, wv * (1 - 0.6 * np.abs(np.linspace(-1, 1, len(C))) ** 6), np.full(len(C), 0.0024))


def paint_brow(P, N, occ):
    c = shade("hair", N, occ * 0.4, 0.05)
    return c, np.full(len(P), 0.2)


# hair: cap (front edge higher than the back) + curved tousled tufts
CAP = I(Off(CRAN, 0.014), Half(nrm((0, -0.45, -1)), (0, 0, 0.815)), k=0.01)
TUFTS = []
rng = np.random.default_rng(6)
HC = np.array([0.0, 0.012, 0.850])
TUFT_DIRS = [(-0.15, -0.75, 0.62), (0.25, -0.70, 0.62), (0.55, -0.45, 0.6), (-0.55, -0.40, 0.65), (0.0, -0.2, 1.0),
             (0.45, 0.1, 0.9), (-0.45, 0.15, 0.88), (0.1, 0.5, 0.85), (-0.25, 0.65, 0.7), (0.35, 0.65, 0.65),
             (0.75, 0.15, 0.55), (-0.75, 0.1, 0.55), (0.0, 0.85, 0.45), (0.05, -0.55, 0.85), (-0.30, -0.25, 0.92),
             (0.30, -0.20, 0.92)]
TUFT_TONE = []
for i, dv in enumerate(TUFT_DIRS):
    dv = nrm(dv)
    a = HC + dv * 0.082
    b = HC + dv * (0.150 + 0.012 * rng.standard_normal()) + np.array([0.012 * rng.standard_normal(), 0.02, 0.0])
    if dv[1] < -0.5:  # fringe: tips fall forward-down over the forehead
        b = HC + dv * 0.14 + np.array([0.02 * np.sign(dv[0] + 0.01), 0.0, -0.035])
    side = nrm(np.cross(dv, (0, 0, 1)) + 1e-6)
    m = (a + b) / 2 + dv * 0.012 + side * 0.010 * (1 if i % 2 else -1)   # each tuft bends a little sideways
    pts = catmull([a, m, b], 5)
    rr = np.linspace(0.040, 0.009, 5)
    TUFTS.append(U(*[RCone(pts[j], pts[j + 1], rr[j], rr[j + 1]) for j in range(4)], k=0.004))
    TUFT_TONE.append((a, b, rng.uniform(-0.08, 0.10)))
HAIR = U(CAP, *TUFTS, k=0.018)


def paint_hair(P, N, occ):
    c = shade("hair", N, occ * 0.8, 0.30)
    tip = np.clip((np.linalg.norm(P - HC, axis=1) - 0.10) / 0.06, 0, 1)
    c = c * (0.85 + 0.40 * tip)[:, None]
    for a, b, tone in TUFT_TONE:   # per-tuft tone + a soft sheen along the tuft
        d = seg3(P, [a, b])
        c = c * (1 + tone * line_w(d, 0.03, 0.01))[:, None]
    strand = 0.5 + 0.5 * np.sin((P[:, 0] * 0.8 + P[:, 1] * 0.6) * 520 + P[:, 2] * 140)
    c = c * (0.93 + 0.07 * strand)[:, None]
    return np.clip(c, 0, 1), np.full(len(P), gloss_of("hair"))


def paint_neck(P, N, occ):
    c = shade("skin", N, occ, 0.05)
    return c * 0.92, np.full(len(P), 0.18)


def seg3(P, C):
    C = np.asarray(C, float)
    best = np.full(len(P), 1e9)
    for i in range(len(C) - 1):
        ab = C[i + 1] - C[i]
        t = np.clip(((P - C[i]) @ ab) / (ab @ ab), 0, 1)
        best = np.minimum(best, np.linalg.norm(P - (C[i] + t[:, None] * ab), axis=1))
    return best


part("head", HEAD, "skin", "Head", paint_head)
part("neck", NECK, "skin", ("neck",), paint_neck)
for s in SIDES:
    part("brow" + SN[s], BROWN[s], "hair", "Head", paint_brow, prio=1)
part("hair", HAIR, "hair", "Head", paint_hair)
for s in SIDES:
    part("eye" + SN[s], EYE[s][4], "skin", "Eye" + SN[s], paint_eye(s))
log("model defined:", len(PARTS), "parts")


def fig_d(P):
    P = np.asarray(P, F32)
    out = np.full(len(P), BIG, F32)
    for p in PARTS:
        m = _inbb(P, p.lo, p.hi, 0.01)
        if m.any():
            idx = np.nonzero(m)[0]
            out[idx] = np.minimum(out[idx], p.node.d(P[idx]))
    return out


# =====================================================================================================================
# weapon "Hookshot": toy-like grapple launcher (crimson body, yellow rope reel, metal claw hook). Origin = grip centre.
# =====================================================================================================================
WPARTS = []
GY = 0.05  # barrel axis height above the grip centre


def paint_grip(P, N, occ):
    c = shade("dark", N, occ, 0.2)
    rib = line_w(np.abs(((P @ GRIP_A) % 0.008) - 0.004), 0.0012, 0.0005) * (P[:, 1] < 0.0)
    return c * (1 - 0.3 * rib)[:, None], np.full(len(P), 0.3)


def paint_wbody(P, N, occ):
    c = shade("crimson", N, occ, 0.6)
    g = np.full(len(P), 0.6)
    # panel lines around the barrel + a dark side vent strip with slots
    for y0 in (-0.004, -0.080):
        c = stroke(c, line_w(np.abs(P[:, 1] - y0), 0.0010, 0.0004) * 0.8, H("#4A0716"))
    vent = (np.abs(P[:, 2] - GY) < 0.010) & (np.abs(P[:, 0]) > 0.026) & (P[:, 1] < -0.020) & (P[:, 1] > -0.070)
    slot = line_w(np.abs(((P[:, 1] + 0.07) % 0.010) - 0.005), 0.0018, 0.0005)
    c = stroke(c, vent * (0.6 + 0.4 * slot), H("#1B1B20"))
    # yellow stripe on top
    topm = line_w(np.abs(P[:, 0]), 0.0035, 0.0006) * (N[:, 2] > 0.6) * (P[:, 1] < -0.01)
    c = stroke(c, topm, shade("rope", N, occ, 0.2))
    return np.clip(c, 0, 1), g


def paint_reel(P, N, occ):
    c = shade("rope", N, occ, 0.15)
    rc = np.array([0, 0.0, GY + 0.030])
    ang = np.arctan2(P[:, 2] - rc[2], P[:, 1] - rc[1])
    st = 0.5 + 0.5 * np.sin(P[:, 0] * 900 + ang * 2)
    return c * (0.82 + 0.18 * st)[:, None], np.full(len(P), 0.25)


def paint_rope(P, N, occ):
    c = shade("rope", N, occ, 0.15)
    st = 0.5 + 0.5 * np.sin(P[:, 1] * 900 + np.arctan2(P[:, 2] - GY, P[:, 0]) * 2)
    return c * (0.82 + 0.18 * st)[:, None], np.full(len(P), 0.25)


part("grip", RCyl((0, 0.008, 0.0), GRIP_A, 0.019, 0.042, rad=0.008), "dark", None, paint_grip, lst=WPARTS)
part("guard", Torus((0, -0.020, 0.010), (1, 0, 0), 0.026, 0.006), "dark", None, lst=WPARTS)
part("body", U(RCyl((0, -0.035, GY), (0, -1, 0), 0.036, 0.075, rad=0.014, taper=-0.006),
               RBox((0, 0.035, GY), (0.028, 0.02, 0.028), 0.01), k=0.01), "crimson", None, paint_wbody, lst=WPARTS)
part("sight", RBox((0, -0.020, GY + 0.040), (0.006, 0.020, 0.007), 0.004), "metal", None, lst=WPARTS)
part("reel", RCyl((0, 0.000, GY + 0.030), (1, 0, 0), 0.030, 0.034, rad=0.006), "rope", None, paint_reel, lst=WPARTS)
part("reelcap", RCyl((0, 0.000, GY + 0.030), (1, 0, 0), 0.016, 0.040, rad=0.004), "metal", None, lst=WPARTS)
part("collar", RCyl((0, -0.115, GY), (0, -1, 0), 0.031, 0.010, rad=0.004), "metal", None, lst=WPARTS)
part("rope", RCone((0, -0.125, GY), (0, -0.150, GY), 0.008, 0.008), "rope", None, paint_rope, lst=WPARTS)
HOOK_TIP = np.array([0.0, -0.205, GY])
hk = [RCone((0, -0.145, GY), HOOK_TIP, 0.010, 0.008)]
for a in (90, 210, 330):
    r = np.array([math.cos(math.radians(a)), 0, math.sin(math.radians(a))])
    p0 = HOOK_TIP + (0, 0.005, 0)
    p1 = HOOK_TIP + r * 0.040 + (0, 0.012, 0)
    p2 = HOOK_TIP + r * 0.040 + (0, 0.040, 0)
    hk += [RCone(p0, p1, 0.007, 0.006), RCone(p1, p2, 0.006, 0.003)]
part("hook", U(*hk, k=0.006), "metal", None, lst=WPARTS)
MUZZLE = HOOK_TIP + (0, -0.01, 0)


def weap_d(P):
    P = np.asarray(P, F32)
    out = np.full(len(P), BIG, F32)
    for p in WPARTS:
        out = np.minimum(out, p.node.d(P))
    return out


# =====================================================================================================================
# pipeline (Theo v3): shells -> decimation -> smart UV (bigger face) -> texel bake on the exact surface -> rig -> export
# =====================================================================================================================
TEX = 1024
BODY_SHELLS = [  # name, part names, triangles
    ("head", ("head", "neck", "browR", "browL", "hair"), 1520),
    ("eyeR", ("eyeR",), 76),
    ("eyeL", ("eyeL",), 76),
    ("handR", ("handR", "plateR"), 230),
    ("handL", ("handL", "plateL"), 210),
    ("body", None, 1830),
]
WEAP_SHELLS = [("weapon", None, 1180)]


def shell_parts(shells, parts, k):
    names = shells[k][1]
    if names:
        return [p for p in parts if p.name in names]
    taken = {n for sh in shells if sh[1] for n in sh[1]}
    return [p for p in parts if p.name not in taken]


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
    m = ob.modifiers.new("Tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=m.name)


def build_low(Vh, Th, target, fn, name):
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
    log(f"  shell {name}: {len(V)} verts {len(T)} tris (target {target})")
    return V, T


def build_shells(shells, parts, h, blo, bhi, name):
    Vs, Ts, sid, base = [], [], [], 0
    for k, (sn, _, budget) in enumerate(shells):
        ps = shell_parts(shells, parts, k)
        fn = shell_fn(ps)
        lo = np.maximum(np.min([p.lo for p in ps], 0) - 0.02, blo)
        hi = np.minimum(np.max([p.hi for p in ps], 0) + 0.02, bhi)
        hh = h * (0.5 if sn.startswith("eye") else 1.0)
        Vh, Th = mesh_field(fn, hh, lo, hi)
        V, T = build_low(Vh, Th, budget, fn, sn)
        Vs.append(V)
        Ts.append(T + base)
        sid.append(np.full(len(V), k))
        base += len(V)
    V, T, sid = np.concatenate(Vs), np.concatenate(Ts), np.concatenate(sid)
    ob = to_mesh(name, V, T)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob, sid


def owner(P, parts):
    P = np.asarray(P, F32)
    best = np.full(len(P), BIG, F32)
    own = np.zeros(len(P), np.int32)
    for i, p in enumerate(parts):
        m = _inbb(P, p.lo, p.hi, 0.01)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        d = p.node.d(P[idx]) - F32(1e-5 * p.prio)
        b = d < best[idx]
        best[idx[b]] = d[b]
        own[idx[b]] = i
    return own


def unwrap(ob, parts, factor_fn):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(80), island_margin=0.002, area_weight=1.0, scale_to_bounds=False)
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
    own = owner(cen, parts)
    names = np.array([parts[i].name for i in own])
    ff = factor_fn(cen, names)
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


def body_factor(C, names):
    f = np.ones(len(C))
    z, y, x = C[:, 2], C[:, 1], C[:, 0]
    head = z > 0.70
    f[head] = 1.3
    face = head & (y < -0.04) & (z < 0.87) & (np.abs(x) < 0.10)
    f[face] = 2.4
    for pre, val in (("eye", 2.8), ("brow", 2.0), ("hand", 1.2), ("plate", 1.2), ("shoe", 0.8), ("sole", 0.6)):
        f[np.char.startswith(names, pre)] = val
    return f


def weap_factor(C, names):
    return np.ones(len(C))


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


def dilate(img, filled, iters=24):
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


def field_ao(fn, P, N):
    occ = np.zeros(len(P), F32)
    for h, w in ((0.0035, 0.28), (0.008, 0.26), (0.015, 0.20), (0.025, 0.15), (0.040, 0.11)):
        d = fn((P + N * h).astype(F32))
        occ += F32(w) * np.clip((h - d) / h, 0, 1)
    return np.clip(occ, 0, 1)


def paint_points(P, parts, fn, ao_fn):
    P = np.asarray(P, F32)
    g = grad(fn, P, e=2.5e-4)
    N = (g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)).astype(F32)
    occ = field_ao(ao_fn, P, N)
    own = owner(P, parts)
    col, gl = np.zeros((len(P), 3)), np.zeros(len(P))
    for i, p in enumerate(parts):
        m = own == i
        if m.any():
            c, gg = p.paint(P[m].astype(np.float64), N[m].astype(np.float64), occ[m].astype(np.float64))
            col[m], gl[m] = c, gg
    return np.clip(col, 0, 1), np.clip(gl, 0, 1)


def bake(ob, uv, sid, shells, parts, ao_fn, res):
    me = ob.data
    V, T = mesh_np(ob)
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    lt = lt.reshape(-1, 3)
    vn = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", vn)
    vn = vn.reshape(-1, 3)
    tri, bary = raster(uv[lt], res)
    has = tri >= 0
    ti, b = tri[has], bary[has].astype(np.float64)
    P0 = V[T[ti, 0]] * b[:, :1] + V[T[ti, 1]] * b[:, 1:2] + V[T[ti, 2]] * b[:, 2:3]
    N0 = vn[T[ti, 0]] * b[:, :1] + vn[T[ti, 1]] * b[:, 1:2] + vn[T[ti, 2]] * b[:, 2:3]
    N0 /= np.maximum(np.linalg.norm(N0, axis=1, keepdims=True), 1e-9)
    tsid = sid[T[ti, 0]]
    log(f"bake: {has.sum()} texels ({has.mean() * 100:.0f}% of {res}^2)")
    cols, gls = np.zeros((len(P0), 3)), np.zeros(len(P0))
    for k in range(len(shells)):
        sel = np.nonzero(tsid == k)[0]
        if not len(sel):
            continue
        ps = shell_parts(shells, parts, k)
        fn = shell_fn(ps)
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
        Q = project(fn, np.where(use_r[:, None], Qr, Qc), it=2, clip=0.002)
        CH = 100000
        for c0 in range(0, len(Q), CH):
            cc, gg = paint_points(Q[c0:c0 + CH], ps, fn, ao_fn)
            cols[sel[c0:c0 + CH]], gls[sel[c0:c0 + CH]] = cc, gg
        log(f"  painted shell {shells[k][0]}: {len(sel)} texels")
    img = np.zeros((res * res, 4), np.float32)
    img[has, :3] = cols
    img[has, 3] = gls
    return dilate(img.reshape(res, res, 4), has.reshape(res, res))


def save_texture(img, path, name):
    res = img.shape[0]
    im = bpy.data.images.new(name, res, res, alpha=True)
    im.alpha_mode = "CHANNEL_PACKED"
    im.pixels.foreach_set(np.ascontiguousarray(img, np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()


def sample_tex(img, uv, blur=2):
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


# ---- bones and weights ----
BONE_DEF = [  # name, parent, head, tail (model coordinates before normalisation)
    ("Root", None, (0, 0, 0), (0, 0, 0.425)),
    ("Body", "Root", (0, 0, 0.425), (0, 0, 0.68)),
    ("Head", "Body", (0, 0, 0.68), (0, 0, 1.0)),   # tail replaced by the top of the head below
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", PROP, PROP + np.array([0, -0.10, 0])),
    ("EyeR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("EyeL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("LidR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("LidL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("Tail", "Body", SC[1], SC[-1]),
]
BONES = [b[0] for b in BONE_DEF]
WAIST = (0.432, 0.492)   # Root -> Body blend over the waist (z)


def suit_weights(P):
    P32 = P.astype(F32)
    D = np.stack([np.maximum(g.d(P32), 0) for _, g in WEIGHT_GROUPS], 0)
    Wt = np.exp(-(D - D.min(0)) / 0.011)
    zb = sstep(WAIST[0], WAIST[1], P[:, 2])
    names = [n for n, _ in WEIGHT_GROUPS]
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


def vertex_weights(V, sid):
    W = [None] * len(V)
    for k in range(len(BODY_SHELLS)):
        sel = np.nonzero(sid == k)[0]
        if not len(sel):
            continue
        ps = shell_parts(BODY_SHELLS, PARTS, k)
        own = owner(V[sel], ps)
        for i, p in enumerate(ps):
            idx = sel[own == i]
            if not len(idx):
                continue
            spec, P = p.bone, V[idx]
            if spec == ("suit",):
                for j, w in zip(idx, suit_weights(P)):
                    W[j] = w
            elif spec == ("waist",):
                zb = sstep(WAIST[0], WAIST[1], P[:, 2])
                for j, b in zip(idx, zb):
                    W[j] = {"Root": 1 - b, "Body": b}
            elif spec == ("neck",):
                hb = sstep(0.655, 0.705, P[:, 2])
                for j, b in zip(idx, hb):
                    W[j] = {"Body": 1 - b, "Head": b}
            elif spec == ("scarf",):
                _, sp, _, _ = SCARF_T.local(P.astype(F32))
                dt, dw = SCARF_T.d(P.astype(F32)), SCARF_W.d(P.astype(F32))
                tb = sstep(0.04, 0.45, sp) * (dt < dw + 0.004)
                for j, b in zip(idx, tb):
                    W[j] = {"Body": 1 - b, "Tail": b}
            else:
                for j in idx:
                    W[j] = {spec: 1.0}
    out = []
    for w in W:
        top = sorted(w.items(), key=lambda kv: -kv[1])[:3]
        top = [(k, v) for k, v in top if v > 0.02] or top[:1]
        t = sum(v for _, v in top)
        out.append({k: v / t for k, v in top})
    return out


def make_material(img_path):
    mat = bpy.data.materials.new("FighterVC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    texn = nt.nodes.new("ShaderNodeTexImage")
    texn.image = bpy.data.images.load(img_path, check_existing=True)
    texn.image.alpha_mode = "CHANNEL_PACKED"
    nt.links.new(texn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
    return mat


def mesh_with_uv(name, Vn, T, uvc, img, tex_path):
    ob = to_mesh(name, Vn, T)
    me = ob.data
    if len(me.polygons) != len(T):
        raise RuntimeError(f"{name}: validate changed faces {len(me.polygons)} vs {len(T)}")
    ul = me.uv_layers.new(name="UVMap")
    ul.data.foreach_set("uv", np.asarray(uvc, np.float64).ravel())
    smp = sample_tex(img, uvc, blur=3)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(smp[:, :3]), smp[:, 3:4]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    me.materials.append(make_material(tex_path))
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


def loop_uvs(ob, uv):
    me = ob.data
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    return uv[lt]


def build_body():
    BLO, BHI = np.array([-0.32, -0.27, -0.012]), np.array([0.32, 0.47, 1.06])
    Vh, Th = mesh_field(fig_d, 0.004, lo=BLO, hi=BHI)
    lo, hi = Vh.min(0), Vh.max(0)
    del Vh, Th
    shift = np.array([0.0, 0.0, -lo[2]])
    scale = 1.0 / (hi[2] - lo[2])
    top = hi[2]
    log("bounds", np.round(lo, 3), np.round(hi, 3), "scale", round(scale, 4))
    low, sid = build_shells(BODY_SHELLS, PARTS, 0.0025, BLO, BHI, "Zip")
    uv = unwrap(low, PARTS, body_factor)
    img = bake(low, uv, sid, BODY_SHELLS, PARTS, fig_d, TEX)
    tex_path = os.path.join(OUT_DIR, "zip_tex.png")
    save_texture(img, tex_path, "zip_tex")
    V, T = mesh_np(low)
    uvc = loop_uvs(low, uv)
    W = vertex_weights(V, sid)
    bpy.data.objects.remove(low, do_unlink=True)
    ob = mesh_with_uv("Zip", (V + shift) * scale, T, uvc, img, tex_path)
    arm_data = bpy.data.armatures.new("ZipRig")
    arm = bpy.data.objects.new("Rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, parent, hd, tl in BONE_DEF:
        if bn == "Head":
            tl = (0, 0, top)
        eb = arm_data.edit_bones.new(bn)
        eb.head = Vector((np.asarray(hd, float) + shift) * scale)
        eb.tail = Vector((np.asarray(tl, float) + shift) * scale)
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
    export([ob, arm], os.path.join(OUT_DIR, "zip_game"), {"ARMATURE", "MESH"})
    mixed = sum(1 for w in W if len(w) > 1)
    used = sorted({b for w in W for b in w})
    log("RESULT body tris", len(T), "verts", len(V), "max infl", max(len(w) for w in W), "mixed", mixed, "bones used", used)
    log("Prop head", np.round((PROP + shift) * scale, 3), "eyes", np.round((EYE[-1][0] + shift) * scale, 3),
        "tail", np.round((SC[1] + shift) * scale, 3), np.round((SC[-1] + shift) * scale, 3))
    return shift, scale, len(T)


def build_weapon(scale):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
    lo, hi = np.array([-0.08, -0.26, -0.07]), np.array([0.08, 0.08, 0.12])
    low, sid = build_shells(WEAP_SHELLS, WPARTS, 0.0011, lo, hi, "ZipHookshot")
    uv = unwrap(low, WPARTS, weap_factor)
    img = bake(low, uv, sid, WEAP_SHELLS, WPARTS, weap_d, 512)
    tex_path = os.path.join(OUT_DIR, "zip_weapon_tex.png")
    save_texture(img, tex_path, "zip_weapon_tex")
    V, T = mesh_np(low)
    uvc = loop_uvs(low, uv)
    bpy.data.objects.remove(low, do_unlink=True)
    ob = mesh_with_uv("ZipHookshot", V * scale, T, uvc, img, tex_path)
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
