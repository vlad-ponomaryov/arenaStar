# Tess v2, the garage inventor in a home-made exosuit (Docs/Roster-v4.md 4.4, Docs/Briefs/A-Roster3D.md wave 3): one smooth
# glossy "vinyl toy" figure built the Theo v3 way (build_theo_v3.py: the SDF / meshing / paint / bake machinery is copied
# here, theo files untouched). Headless (Blender 5.2, numpy only):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_tess_v2.py -- [out_dir] [--stage shape|full]
#   --stage full (default): models, textures, rigs and exports; --stage shape: a quick vertex-colour preview only
#   (Tools/out/art/roster3d/_tess_v2_quick.png).
# Output (default Art/Source/3D/tess/, same names as v1): tess_game.fbx/.glb (<= 4000 tris; mesh "Tess", armature "Rig" with
# the 12 FighterBone bones, smooth weights at the joints, <= 4 per vertex, material "FighterVC" -> tess_tex.png),
# tess_weapon.fbx/.glb (<= 1200 tris; the right spring pea blaster, origin = right hand grip = Prop bone head, barrel along
# -Y, empty "Muzzle" at the barrel tip, same material / texture), tess_tex.png (1024 RGBA shared by body and weapon:
# RGB = albedo with baked AO / light gradients / metal banding / decals, A = gloss).
# Format as Theo v3: Blender Z up, faces -Y, height 1.0, feet on z = 0, centred on X/Y (without the weapon); FBX
# axis_forward=-Z, axis_up=Y, colors_type=SRGB; UV "UVMap"; corner colour "Col" (linear) = the texture blurred, A = gloss.
# Method: the figure is one signed distance field (numpy): jumpsuit body of a lofted superellipse torso + round-cone limbs
# (smooth unions), plates / pots / pads as offset shells of the body, head of smooth-unioned ellipsoids with modelled eye
# bulges (EyeL/R), brow ribbons and a carved smile, curly hair = cap + smooth-unioned curl bumps + "pineapple" puff. The field
# is meshed (surface nets + Newton projection), decimated per shell to the budget, smart-UV unwrapped (face enlarged) and
# textured by a procedural paint function evaluated on the exact surface for every texel (eyes, freckles, plywood grain,
# tape, sticker, gear teeth, tyre tread, laces, AO from the field, metal banding).
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = ARGV[ARGV.index("--stage") + 1] if "--stage" in ARGV else "full"
POS = [a for i, a in enumerate(ARGV) if not a.startswith("--") and (i == 0 or ARGV[i - 1] != "--stage")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "tess")
PREV_DIR = os.path.join(REPO, "Tools", "out", "art", "roster3d")
os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(PREV_DIR, exist_ok=True)
T0 = time.time()
BB_LO, BB_HI = (-0.36, -0.40, -0.012), (0.36, 0.30, 1.06)


def log(*a):
    print(f"[tess2 {time.time() - T0:6.1f}s]", *a, flush=True)


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

# =====================================================================================================================
# palette: colour ramps (position, sRGB hex) over the baked light value, kind, AO strength, gloss (Docs/Roster-v4.md 4.4)
# =====================================================================================================================
def hexc(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])


MAT = {
    "suit": (((0.0, "#2E3A0C"), (0.45, "#5E7420"), (0.72, "#7E9A2A"), (1.0, "#A9C44C")), "plastic", 0.60, 0.15),
    "suit_d": (((0.0, "#26300A"), (0.5, "#4E6218"), (1.0, "#7E9A2A")), "plastic", 0.55, 0.15),
    "lime": (((0.0, "#4A640A"), (0.40, "#86A81E"), (0.70, "#B6E02E"), (1.0, "#E6F884")), "plastic", 0.55, 0.60),
    "ply": (((0.0, "#5E4420"), (0.45, "#A9844C"), (0.75, "#C9A56B"), (1.0, "#EED7A6")), "plastic", 0.55, 0.30),
    "ply_d": (((0.0, "#4A3418"), (0.5, "#8C6A3A"), (0.78, "#A9844C"), (1.0, "#CDAE78")), "plastic", 0.55, 0.25),
    "brass": (((0.0, "#3A2608"), (0.35, "#7A5418"), (0.62, "#B8862F"), (0.85, "#DDB050"), (1.0, "#FFE7A2")), "metal", 0.50, 0.85),
    "tin": (((0.0, "#3E4246"), (0.40, "#767C82"), (0.70, "#A7ADB2"), (1.0, "#F0F3F6")), "metal", 0.45, 0.80),
    "skin": (((0.0, "#3E2214"), (0.50, "#7E5036"), (0.78, "#9A6644"), (1.0, "#C08C64")), "plastic", 0.48, 0.18),
    "hair": (((0.0, "#0C0604"), (0.5, "#241610"), (0.8, "#3E2A1C"), (1.0, "#5E4430")), "plastic", 0.60, 0.30),
    "rubber": (((0.0, "#121010"), (0.5, "#2C2A2A"), (1.0, "#585454")), "plastic", 0.50, 0.20),
    "sole": (((0.0, "#8A867C"), (0.5, "#D8D4C8"), (1.0, "#F8F6F0")), "plastic", 0.45, 0.30),
    "glove": (((0.0, "#2A1A0C"), (0.5, "#6B4A2E"), (1.0, "#A07650")), "plastic", 0.50, 0.22),
    "strap": (((0.0, "#1E160E"), (0.5, "#4A3A2C"), (1.0, "#76624E")), "plastic", 0.45, 0.20),
    "steam": (((0.0, "#C4DCA0"), (0.5, "#EAF8CC"), (1.0, "#FFFFF4")), "plastic", 0.15, 1.00),
    "lens": (((0.0, "#4C6A26"), (0.5, "#A8CC70"), (1.0, "#E8F8C8")), "plastic", 0.30, 1.00),
}
PAL = {k: ("#%02X%02X%02X" % tuple(int(round(c * 255)) for c in hexc(v[0][-2][1])), v[3]) for k, v in MAT.items()}


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
# MODEL (Blender: Z up, faces -Y, +X = the character's left = the viewer's right in 001). Numbers are model units at the
# final scale (height 1.0) read from 001 (front, 936 px / unit) and 002 (profile / back, 684 px / unit).
# =====================================================================================================================
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



# =====================================================================================================================
# MODEL (Blender: Z up, faces -Y, +X = the character's left). Units ~ final (height ~1.0 incl. the curl puff).
# =====================================================================================================================
# ---------------- jumpsuit body: torso loft, neck, arms, legs (moss-lime cloth) ----------------
TORSO = Loft(smooth_rows([  # z, half width, cx, y front, y back, exponent
    (0.352, 0.030, 0, -0.030, 0.036, 2.0),
    (0.366, 0.066, 0, -0.050, 0.056, 2.2),
    (0.388, 0.086, 0, -0.060, 0.066, 2.4),
    (0.425, 0.090, 0, -0.064, 0.068, 2.4),
    (0.465, 0.085, 0, -0.063, 0.064, 2.4),
    (0.505, 0.087, 0, -0.066, 0.066, 2.5),
    (0.545, 0.091, 0, -0.068, 0.070, 2.5),
    (0.585, 0.091, 0, -0.064, 0.072, 2.5),
    (0.615, 0.084, 0, -0.056, 0.066, 2.4),
    (0.640, 0.066, 0, -0.046, 0.052, 2.2),
    (0.656, 0.040, 0, -0.032, 0.036, 2.0)]))
NECK = RCone((0, 0.004, 0.62), (0, 0.000, 0.715), 0.036, 0.034, bone="Body")
SH = {s: mirror((-0.110, 0.004, 0.590), s) for s in SIDES}
EL = {s: mirror((-0.160, -0.022, 0.498), s) for s in SIDES}
WR = {s: mirror((-0.186, -0.070, 0.420), s) for s in SIDES}
HIP = {s: mirror((-0.052, 0.004, 0.385), s) for s in SIDES}
KN = {s: mirror((-0.068, -0.004, 0.236), s) for s in SIDES}
AN = {s: mirror((-0.074, 0.006, 0.110), s) for s in SIDES}
ARM, LEG = {}, {}
for s in SIDES:
    a = "Arm" + SN[s]
    ARM[s] = U(Ell(SH[s] + mirror((0.008, 0, 0.004), s), (0.042, 0.042, 0.042), bone="Body"),
               RCone(SH[s], EL[s], 0.035, 0.031, bone=a), RCone(EL[s], WR[s], 0.030, 0.027, bone=a), k=0.01)
    lg = "Leg" + SN[s]
    pts = [HIP[s], mirror((-0.062, 0.003, 0.330), s), KN[s], AN[s]]
    rad = [0.048, 0.047, 0.040, 0.035]
    LEG[s] = U(*[RCone(pts[i], pts[i + 1], rad[i], rad[i + 1], bone=lg) for i in range(len(pts) - 1)], k=0.0)
TORSO_NECK = U(TORSO, NECK, k=0.02)
SUIT = U(*[U(TORSO_NECK, ARM[s], LEG[s], k=0.020) for s in SIDES])
part("suit", SUIT, "suit", ("suit",))

# ---------------- gloves: palm, three fingers curled round the grip, thumb on top (leather) ----------------
HANDF = {}
for s in SIDES:
    a = "Arm" + SN[s]
    dn = nrm(mirror((-0.25, -0.30, -0.92), s))                 # hand axis: down, a little forward and outward
    f0 = np.array([-s * 0.40, -0.90, 0.0])
    fw = nrm(f0 - dn * np.dot(f0, dn))
    out = np.cross(fw, dn)
    out = nrm(out if out[0] * s > 0 else -out)
    W0 = WR[s]
    pc = W0 + dn * 0.036 + out * 0.003
    HANDF[s] = (pc, dn, out, fw)
    Rh = np.stack([out, fw, dn], 1)
    palm = RBox(pc + out * 0.002, (0.022, 0.034, 0.030), 0.016, Rh, bone=a)
    wrist = RCone(W0 - dn * 0.010, pc - dn * 0.010, 0.029, 0.029, bone=a)
    fingers = []
    for k, fo in enumerate((-0.019, 0.0, 0.019)):
        b0 = pc + dn * 0.020 + fw * fo + out * 0.003
        b1 = b0 + dn * 0.020 - out * 0.004
        b2 = b1 + dn * 0.006 - out * 0.016
        b3 = b2 - dn * 0.006 - out * 0.008
        fingers += [RCone(b0, b1, 0.0108, 0.0102, bone=a), RCone(b1, b2, 0.0102, 0.0094, bone=a),
                    RCone(b2, b3, 0.0094, 0.0084, bone=a)]
    tb = pc + fw * 0.027 - out * 0.010 - dn * 0.010
    tm = tb + fw * 0.010 + dn * 0.020 - out * 0.008
    tt = tm + fw * 0.002 + dn * 0.014 - out * 0.010
    thumb = U(RCone(tb, tm, 0.0145, 0.0135, bone=a), RCone(tm, tt, 0.0135, 0.0115, bone=a), k=0.004)
    cuff = RCyl(W0 - dn * 0.004, dn, 0.033, 0.011, 0.006, bone=a)
    HAND = U(U(wrist, palm, thumb, k=0.010), U(*fingers, k=0.0), k=0.006)
    part("hand_" + SN[s], U(HAND, cuff, k=0.004), "glove", (a,))

# ---------------- head (skin) with modelled eyes, brows and a carved smile ----------------
CRAN = Ell((0, 0.004, 0.818), (0.094, 0.096, 0.104), bone="Head")
FACE = Ell((0, -0.020, 0.772), (0.088, 0.080, 0.068), bone="Head")
CHEEKS = [Ell((s * 0.050, -0.056, 0.756), (0.045, 0.040, 0.036), bone="Head") for s in SIDES]
CHIN = Ell((0, -0.066, 0.724), (0.034, 0.028, 0.021), bone="Head")
NOSE = Ell((0, -0.098, 0.768), (0.015, 0.014, 0.012), bone="Head")
EARS = []
for s in SIDES:
    Re = rot((0, 0, 1), s * 20) @ rot((0, 1, 0), s * -6)
    ear = Ell((s * 0.104, 0.012, 0.778), (0.016, 0.024, 0.031), Re, bone="Head")
    concha = Ell((s * 0.118, 0.006, 0.776), (0.007, 0.014, 0.019), Re)
    EARS.append(S(ear, concha, k=0.006))
HEAD_SKULL = U(U(CRAN, FACE, CHIN, k=0.04), *CHEEKS, k=0.04)
EYE, EYE_C = {}, {}
for s in SIDES:
    p = front_pt(HEAD_SKULL, s * 0.046, 0.792)
    EYE[s] = (p, nrm(normal_at(HEAD_SKULL, p) + np.array([0, -1.5, 0])))
    EYE_C[s] = np.array([s * 0.046, 0.792])
# smile: a shallow groove along the mouth curve (painted dark on top, a tooth gap in the middle)
MOUTH = [(-0.030, 0.737), (-0.016, 0.7315), (0.0, 0.7300), (0.016, 0.7315), (0.031, 0.7385)]
MC = np.array([front_pt(HEAD_SKULL, x, z) for x, z in MOUTH])
MN = np.array([normal_at(HEAD_SKULL, c) for c in MC])
MW = np.array([nrm(t) for t in np.gradient(MC, axis=0)])
SMILE = Ribbon(MC + MN * 0.0004, np.cross(MN, MW) * -1, MN, np.array([0.0016, 0.0024, 0.0026, 0.0024, 0.0016]),
               np.full(5, 0.0022))
HEAD = S(U(HEAD_SKULL, NOSE, *EARS, k=0.012), SMILE, k=0.002)
part("head", HEAD, "skin", ("Head",))
# eye bulges: shallow lenses on the face (the painted eye sits on them), one per Eye bone
for s in SIDES:
    p, gz = EYE[s]
    n = normal_at(HEAD_SKULL, p)
    Rz = frame(n, (0, 0, 1))
    part("eye_" + SN[s], I(Ell(p - n * 0.006, (0.031, 0.034, 0.012), Rz, bone="Eye" + SN[s]), Off(HEAD_SKULL, 0.0045), k=0.0015),
         "skin", ("Eye" + SN[s],), prio=2)
BROWS = {-1: [(-0.076, 0.826), (-0.062, 0.834), (-0.045, 0.836), (-0.029, 0.832), (-0.020, 0.826)],
         1: [(0.020, 0.826), (0.029, 0.832), (0.045, 0.836), (0.062, 0.834), (0.076, 0.826)]}
for s in SIDES:
    C = np.array([front_pt(HEAD_SKULL, x, z) for x, z in BROWS[s]])
    Nn = np.array([normal_at(HEAD_SKULL, c) for c in C])
    C = C - Nn * 0.0002
    Wv = np.array([nrm(t) for t in np.gradient(C, axis=0)])
    part("brow_" + SN[s], Ribbon(C, np.cross(Nn, Wv) * -1, Nn, np.array([0.0050, 0.0064, 0.0066, 0.0060, 0.0046]),
                                 np.full(5, 0.0018), bone="Head"), "hair", ("Head",))

# ---------------- hair: a cap of curls (bumps smooth-unioned on the scalp shell) + the "pineapple" puff ----------------
HAIR_YZ = np.array([(-0.25, 0.880), (-0.10, 0.880), (-0.080, 0.866), (-0.050, 0.845), (-0.035, 0.790), (0.000, 0.770),
                    (0.040, 0.770), (0.070, 0.745), (0.090, 0.720), (0.25, 0.720), (0.25, 1.10), (-0.25, 1.10)])
CAPB = I(Off(CRAN, 0.011), Poly2(HAIR_YZ, "yz"), k=0.010)
RNG_H = np.random.default_rng(5)
CURLS = []
for el in (8, 24, 40, 56):
    nseg = int(16 * math.cos(math.radians(el))) + 4
    for i in range(nseg):
        az = 360.0 * (i + 0.5 * (el // 16 % 2)) / nseg + RNG_H.uniform(-6, 6)
        a, e = math.radians(az), math.radians(el)
        d = np.array([math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e) * 0.98, math.sin(e)])
        o = np.array([0, 0.004, 0.818])
        try:
            p = trace(CAPB.d, o + d * 0.3, -d, tmax=0.4)
        except RuntimeError:
            continue
        if p[1] < -0.035 and p[2] < 0.875:   # keep the forehead / face clear
            continue
        if p[2] < 0.735:
            continue
        CURLS.append((p - d * 0.004, RNG_H.uniform(0.016, 0.021)))
PUFF_C = np.array([0.0, 0.012, 0.952])
PUFF = [(PUFF_C, 0.052)]
for k in range(9):
    a = math.radians(k * 40 + 10)
    e = math.radians(18 if k % 2 else -12)
    d = np.array([math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)])
    PUFF.append((PUFF_C + d * 0.046, 0.026))
PUFF.append((PUFF_C + np.array([0, 0, 0.046]), 0.030))
CURL_NODES = [Ell(c, (r, r, r)) for c, r in CURLS] + [Ell(c, (r, r, r)) for c, r in PUFF]
HAIR = U(CAPB, RCone((0, 0.010, 0.880), PUFF_C, 0.040, 0.044), *CURL_NODES, k=0.010)
part("hair", HAIR, "hair", ("Head",))
TIE = Torus((0, 0.010, 0.900), (0, 0, 1), 0.043, 0.0085, bone="Head")
part("hairtie", TIE, "lime", ("Head",))

# ---------------- welder goggles pushed up on the forehead: strap + two brass cups with lenses ----------------
GOG_Z = 0.868
STRAP = I(Shell(Off(CRAN, 0.0135), 0.0030), Slab(nrm((0, -0.22, 1)), (0, 0, GOG_Z), 0.0075), k=0.002)
part("gogstrap", STRAP, "strap", ("Head",))
GOG = {}
for s in SIDES:
    p = front_pt(Off(CRAN, 0.012), s * 0.038, GOG_Z + 0.004)
    n = nrm(normal_at(CRAN, p) + np.array([0, 0, 0.45]))
    cup = RCyl(p + n * 0.010, n, 0.027, 0.012, 0.005, taper=-0.002, bone="Head")
    lens = RCyl(p + n * 0.021, n, 0.0205, 0.003, 0.002, bone="Head")
    GOG[s] = (p + n * 0.024, n)
    part("gogcup_" + SN[s], cup, "brass", ("Head",))
    part("goglens_" + SN[s], lens, "lens", ("Head",), prio=1)
part("gogbridge", RCone(GOG[-1][0] - GOG[-1][1] * 0.012, GOG[1][0] - GOG[1][1] * 0.012, 0.005, 0.005, bone="Head"),
     "brass", ("Head",))

# ---------------- chest plate (plywood with tin trim), belt, pockets ----------------
PLATE_F = np.array([(-0.074, 0.620), (0.074, 0.620), (0.086, 0.560), (0.080, 0.505), (0.040, 0.478), (-0.040, 0.478),
                    (-0.080, 0.505), (-0.086, 0.560)])
PLATE = I(Off(TORSO, 0.012), Poly2(PLATE_F, "xz"), Half((0, 1, 0), (0, -0.010, 0)), k=0.006)
part("chest", PLATE, "ply", ("Body",))
BELT_Z = 0.432
part("belt", I(Off(TORSO, 0.010), Slab((0, 0, 1), (0, 0, BELT_Z), 0.0135), k=0.004), "rubber", ("Root",))
pf = front_pt(TORSO, 0.0, BELT_Z)
part("buckle", RBox(pf + np.array([0, -0.010, 0]), (0.020, 0.006, 0.016), 0.004), "brass", ("Root",))
for s in SIDES:
    pp = radial_pt(TORSO, 0.395, s * 62)
    nn = normal_at(TORSO, pp)
    part("pocket_" + SN[s], RBox(pp + nn * 0.004, (0.024, 0.024, 0.026), 0.008, frame(nn, (0, 0, 1))), "suit_d", ("Root",))

# ---------------- backpack (plywood box) with the wind-up key (horizontal brass butterfly) and three gears ----------------
PACK_C = np.array([0.0, 0.092, 0.530])
part("pack", RBox(PACK_C, (0.076, 0.034, 0.080), 0.016), "ply_d", ("Body",))
KEY_Z = 0.585
KEY = U(RCyl((0, 0.150, KEY_Z), (0, 1, 0), 0.013, 0.030, 0.004),
        RCyl((0, 0.183, KEY_Z), (0, 0, 1), 0.022, 0.011, 0.005),
        *[S(RCyl((s * 0.052, 0.200, KEY_Z), (0, 0, 1), 0.048, 0.010, 0.006, sxy=(1.0, 0.72)),
            RCyl((s * 0.056, 0.201, KEY_Z), (0, 0, 1), 0.022, 0.03, 0.0, sxy=(1.0, 0.65)), k=0.004) for s in SIDES], k=0.006)
part("key", KEY, "brass", ("Body",))
GEARS = [(-0.044, 0.468, 0.020), (0.0, 0.452, 0.016), (0.042, 0.470, 0.020)]
for i, (gx, gz, gr) in enumerate(GEARS):
    part("gear%d" % i, RCyl((gx, PACK_C[1] + 0.036, gz + 0.04), (0, 1, 0), gr, 0.004, 0.0015), "brass", ("Body",))

# ---------------- shoulder pots (tin, with a handle stub and a rivet), elbow springs ----------------
for s in SIDES:
    a = "Arm" + SN[s]
    c = SH[s] + mirror((-0.016, 0.0, 0.018), s)
    dome = I(Ell(c, (0.056, 0.054, 0.046)), Half((0, 0, -1), c - np.array([0, 0, 0.010])), k=0.004)
    rim = Torus(c - np.array([0, 0, 0.008]), (0, 0, 1), 0.052, 0.0065)
    handle = RBox(c + mirror((-0.060, 0.0, -0.002), s), (0.016, 0.009, 0.0045), 0.004)
    knob = Ell(c + np.array([0, 0, 0.046]), (0.010, 0.010, 0.007))
    part("pot_" + SN[s], U(dome, rim, handle, knob, k=0.003), "tin", ("fixed", {a: 0.7, "Body": 0.3}))
    d = nrm(WR[s] - EL[s])
    for k in range(3):
        cc = EL[s] + d * (0.010 + 0.016 * k)
        part("spring%d_%s" % (k, SN[s]), Torus(cc, d, 0.031, 0.0058), "brass", (a,))

# ---------------- legs: bike-tyre knee pads, high lime sneakers, tin steam nozzles, steam puffs ----------------
for s in SIDES:
    lg = "Leg" + SN[s]
    kc = KN[s] + np.array([0, -0.036, 0.004])
    part("tyre_" + SN[s], Torus(kc, (0, -1, 0.15), 0.025, 0.012), "rubber", (lg,))
    part("hub_" + SN[s], Ell(kc + np.array([0, -0.004, 0]), (0.015, 0.008, 0.015)), "tin", (lg,), prio=1)
    ax, ay = AN[s][0], AN[s][1]
    shaft = RCone((ax, ay, 0.050), (ax, ay, 0.150), 0.046, 0.040)
    toe = Ell((ax, ay - 0.040, 0.040), (0.046, 0.064, 0.036))
    heel = Ell((ax, ay + 0.022, 0.042), (0.044, 0.036, 0.036))
    part("boot_" + SN[s], U(shaft, toe, heel, k=0.018), "lime", (lg,))
    part("sole_" + SN[s], RBox((ax, ay - 0.016, 0.013), (0.049, 0.078, 0.013), 0.010), "sole", (lg,))
    part("nozzle_" + SN[s], U(RCone((ax, ay + 0.050, 0.072), (ax, ay + 0.086, 0.054), 0.017, 0.024),
                             Torus((ax, ay + 0.062, 0.066), (0, 1, -0.5), 0.019, 0.0045), k=0.002), "tin", (lg,))
    puffs = [Ell((ax + s * 0.004 * k, ay + dy, dz), (r, r, r * 0.9)) for k, (dy, dz, r) in
             enumerate(((0.112, 0.050, 0.030), (0.146, 0.044, 0.026), (0.176, 0.058, 0.020)))]
    part("puff_" + SN[s], U(*puffs, k=0.012), "steam", (lg,))


# ---------------- spring pea blaster (both hands; the right one is the weapon FBX on Prop) ----------------
BL_G = {}


def blaster(G, s, bone, tag):
    """G = grip centre (in the fist); barrel along -Y, above the hand. s = side (only the dial mirrors)."""
    G = np.asarray(G, float)
    BL_G[tag] = G
    zt = 0.034
    tube = U(RCyl(G + (0, -0.060, zt), (0, 1, 0), 0.030, 0.130, 0.010),
             RCone(G + (0, 0.068, zt), G + (0, 0.094, zt), 0.031, 0.022), k=0.006)
    tank = RCone(G + (0, 0.040, zt + 0.042), G + (0, -0.062, zt + 0.042), 0.023, 0.023)
    neck = RCyl(G + (0, -0.010, zt + 0.024), (0, 0, 1), 0.011, 0.012, 0.003)
    cap = RCyl(G + (0, -0.010, zt + 0.068), (0, 0, 1), 0.010, 0.006, 0.002)
    grip = RBox(G + (0, 0.004, -0.004), (0.013, 0.016, 0.030), 0.008, rot((1, 0, 0), -15))
    dial = RCyl(G + (s * 0.030, 0.024, zt + 0.004), (1, 0, 0), 0.012, 0.004, 0.002)
    coils = U(*[Torus(G + (0, -0.124 - k * 0.020, zt), (0, 1, 0), 0.031, 0.0060) for k in range(4)], k=0.0)
    muz = RCone(G + (0, -0.192, zt), G + (0, -0.222, zt), 0.027, 0.035)
    muz_in = RCone(G + (0, -0.205, zt), G + (0, -0.240, zt), 0.019, 0.026)
    pea = Ell(G + (0, -0.214, zt), (0.021, 0.021, 0.021))
    parts = [("tube", U(tube, neck, k=0.006), "lime"), ("tank", tank, "tin"), ("cap", cap, "brass"), ("grip", grip, "ply"),
             ("dial", dial, "tin"), ("coils", coils, "brass"), ("muzzle", S(muz, muz_in, k=0.002), "brass"),
             ("pea", pea, "steam")]
    for n_, nd, key in parts:
        part("bl%s_%s" % (tag, n_), nd, key, (bone,),
             prio=1 if n_ in ("pea", "cap") else 0)
    return G + np.array([0, -0.236, zt])


PROP_HEAD = HANDF[-1][0] + HANDF[-1][1] * 0.012
MUZZLE_W = blaster(PROP_HEAD, -1, "Prop", "W")
blaster(HANDF[1][0] + HANDF[1][1] * 0.012, 1, "ArmL", "L")
log("model defined:", len(PARTS), "parts,", len(CURLS), "curls")



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


def mesh_field(fn, h, lo=BB_LO, hi=BB_HI):
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

def key_colours(P):
    own, _ = fig_owner(P)
    keys = [PARTS[i].key for i in range(len(PARTS))]
    cols = np.array([hexc(PAL[k][0]) for k in keys])
    return cols[own]

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



# ---- Tess paints ----
RNG = np.random.default_rng(11)
FRECKLES = []
for s in SIDES:
    for _ in range(16):
        FRECKLES.append((s * RNG.uniform(0.020, 0.074), RNG.uniform(0.752, 0.780), RNG.uniform(0.0012, 0.0019)))
for _ in range(7):
    FRECKLES.append((RNG.uniform(-0.016, 0.016), RNG.uniform(0.764, 0.786), RNG.uniform(0.0010, 0.0015)))
FRECKLES = np.array(FRECKLES)
EYE_TINT = dict(white=H("#FFFBF2"), iris_out=H("#3A200E"), iris_in=H("#9A6430"), pupil=H("#120A06"), spark=H("#FFFFFF"),
                lash=H("#140C08"))


def paint_face(P, N, occ, c, g):
    x, z = P[:, 0], P[:, 2]
    front = sstep(0.1, 0.5, -N[:, 1])
    for s in SIDES:
        b = np.exp(-(((x - s * 0.058) / 0.020) ** 2 + ((z - 0.756) / 0.013) ** 2)) * front
        c = stroke(c, b * 0.30, H("#B0604A"))
    for fx, fz, fr in FRECKLES:
        r = np.sqrt((x - fx) ** 2 + (z - fz) ** 2)
        c = stroke(c, line_w(r, fr, 0.0005) * front * 0.75, H("#4A2A18"))
    # oil smudge on the right cheek (two short smears)
    for (x0, z0, x1, z1, w) in ((-0.072, 0.744, -0.050, 0.752, 0.0042), (-0.068, 0.736, -0.054, 0.741, 0.0030)):
        d, f = seg2(x, z, [(x0, z0), (x1, z1)])
        m = line_w(d, w * (1 - 0.5 * f), 0.0012) * front * 0.75
        c = stroke(c, m, H("#2A221E"))
        g = np.maximum(g, m * 0.6)
    # mouth: dark smile line, wider in the middle, one tooth with a gap
    u = x
    vm = 0.7302 + 0.0060 * (u / 0.031) ** 2 + 0.0016 * sstep(0.0, 0.031, u)
    half = 0.0021 * np.clip(1 - (np.abs(u) / 0.033) ** 4, 0, 1)
    wm = line_w(np.abs(z - vm), half, 0.0005) * (np.abs(u) < 0.034) * front
    c = stroke(c, wm, H("#3E1A12"))
    tooth = (np.abs(u - 0.0045) < 0.0038) & (z > vm - 0.0002) & (z < vm + 0.0019) & (front > 0.3)
    c = np.where(tooth[:, None], H("#F6F0E4"), c)
    lip = np.exp(-((u / 0.016) ** 2 + ((z - 0.7245) / 0.0035) ** 2)) * front
    c = stroke(c, lip * 0.22, H("#A0604C"))
    for s in SIDES:
        c = brow_stroke(c, x, z, s, front)
        c, g = eye_paint(c, g, x - EYE_C[s][0], z - EYE_C[s][1], s, EYE_TINT)
    return c, g


def paint_head(P, N, occ):
    c = shade("skin", N, occ, 0.10)
    g = np.full(len(P), gloss_of("skin"))
    x, z = P[:, 0], P[:, 2]
    for s in SIDES:
        e = np.exp(-(((x - s * 0.110) / 0.012) ** 2 + ((z - 0.777) / 0.020) ** 2)) * sstep(0.2, 0.7, s * N[:, 0])
        c = stroke(c, e * 0.30, H("#5A3020"))
    return paint_face(P, N, occ, c, g)


def brow_stroke(c, x, z, s, front):
    V = np.array(BROWS[s], float)
    d, f = seg2(x, z, V)
    inner = f if s > 0 else 1 - f
    half = 0.0062 * (1 - 0.35 * inner ** 1.5) + 0.0006
    w = line_w(d, half, 0.0006) * front
    return stroke(c, w, mixc(H("#140C08"), H("#3A261A"), sstep(half, 0.0, d) * 0.7))


def paint_brow(P, N, occ):
    x, z = P[:, 0], P[:, 2]
    c = shade("skin", N, occ, 0.10)
    for s_ in SIDES:
        c = brow_stroke(c, x, z, s_, np.ones(len(P)))
    return c, np.full(len(P), 0.2)


def paint_eye(P, N, occ):
    c = shade("skin", N, occ, 0.15)
    g = np.full(len(P), gloss_of("skin"))
    return paint_face(P, N, occ, c, g)


CURL_C = np.array([c for c, _ in CURLS] + [c for c, _ in PUFF])
CURL_R = np.array([r for _, r in CURLS] + [r for _, r in PUFF])


def paint_hair(P, N, occ):
    n = len(P)
    lam = np.clip(N @ KEY_L, -1, 1)
    light = sstep(-0.85, 0.90, 0.6 * lam + 0.4 * N[:, 2])
    # nearest curl: a lighter spiral ring per curl (twisted coil look), dark in the creases between curls
    D = np.full(n, 9.0)
    ang = np.zeros(n)
    rr = np.ones(n)
    for c0, r0 in zip(CURL_C, CURL_R):
        v = P - c0
        d = np.sqrt((v * v).sum(1)) / r0
        b = d < D
        D = np.where(b, d, D)
        ang = np.where(b, np.arctan2(v[:, 2], v[:, 0] + v[:, 1] * 0.7), ang)
        rr = np.where(b, r0, rr)
    on = sstep(1.35, 0.95, D)
    spiral = 0.5 + 0.5 * np.sin(ang * 2 + D * 9.0)
    v = 0.36 + 0.34 * light + on * (0.12 * spiral - 0.04) - 0.10 * (1 - on)
    c = ramp("hair", v)
    sp = np.clip(N @ HALF_V, 0, 1) ** 16 * 0.16 * on
    c = c + (1 - c) * sp[:, None]
    c = c * (1 - 0.40 * np.minimum(occ, 0.6))[:, None]
    return np.clip(c, 0, 1), np.full(n, 0.30)


def paint_metal(key, rivets=()):
    def f(P, N, occ):
        c = shade(key, N, occ)
        for rc, rr in rivets:
            r = np.linalg.norm(P - np.asarray(rc), axis=1)
            c = stroke(c, line_w(r, rr, 0.0006), shade("brass", N, occ * 0.5) * 1.1)
        return np.clip(c, 0, 1), np.full(len(P), gloss_of(key))
    return f


def paint_flat(key, spec=0.0):
    def f(P, N, occ):
        return shade(key, N, occ, spec), np.full(len(P), gloss_of(key))
    return f


def grain(P, base, N, occ, key, scale=1.0):
    """Plywood: wavy grain bands across X, a few knots."""
    x, z = P[:, 0], P[:, 2]
    w = np.sin((z * 210 + np.sin(x * 60) * 1.6 + np.sin(x * 23 + 1.3) * 0.9) * scale)
    c = mixc(base, base * 0.82, sstep(0.55, 1.0, w) * 0.8)
    return c


STAR_C = np.array([0.044, 0.585])


def star_mask(u, v, R=0.016, r=0.0068):
    a = np.arctan2(v, u) - math.pi / 2
    k = (a % (2 * math.pi / 5)) / (2 * math.pi / 5)
    lim = r + (R - r) * np.abs(1 - 2 * k) ** 1.6
    return 1 - sstep(lim - 0.0006, lim + 0.0006, np.sqrt(u * u + v * v))


def paint_chest(P, N, occ):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    c = shade("ply", N, occ, 0.15)
    c = grain(P, c, N, occ, "ply")
    g = np.full(len(P), gloss_of("ply"))
    # dark rim (edge of the plate) and a tin trim along the top edge
    de = seg_dist2(x.astype(F32), z.astype(F32), PLATE_F.astype(F32), True)
    c = stroke(c, line_w(de, 0.0030, 0.0010) * 0.55, H("#5A3E1C"))
    top = line_w(np.abs(z - 0.611), 0.0045) * (np.abs(x) < 0.074)
    c = stroke(c, top, shade("tin", N, occ))
    g = np.where(top > 0.5, 0.8, g)
    # rivets
    for rx, rz in ((-0.064, 0.596), (0.064, 0.596), (-0.070, 0.520), (0.070, 0.520), (0.0, 0.492)):
        r = np.sqrt((x - rx) ** 2 + (z - rz) ** 2)
        c = stroke(c, line_w(r, 0.0062), H("#5A3E1C"))
        c = stroke(c, line_w(r, 0.0050), mixc(H("#8A6020"), H("#FFE7A2"), sstep(-0.004, 0.004, (z - rz) - (x - rx) * 0.6)))
        g = np.where(r < 0.005, 0.85, g)
    # blue electrical tape strip across the left-hand corner (with a torn end)
    a = math.radians(-28)
    tu, tv = (x + 0.045) * math.cos(a) + (z - 0.548) * math.sin(a), -(x + 0.045) * math.sin(a) + (z - 0.548) * math.cos(a)
    tape = (np.abs(tu) < 0.034 + 0.002 * np.sin(tv * 1400)) & (np.abs(tv) < 0.0075)
    tc = mixc(H("#2A3478"), H("#5A68C0"), sstep(-0.3, 0.9, N @ KEY_L))
    c = np.where(tape[:, None], tc, c)
    g = np.where(tape, 0.45, g)
    # lime star sticker with a white border
    u, v = x - STAR_C[0], z - STAR_C[1]
    c = stroke(c, star_mask(u, v, 0.0195, 0.0090), H("#FFFFFF"))
    c = stroke(c, star_mask(u, v), mixc(H("#86A81E"), H("#E2F57A"), sstep(-0.01, 0.01, v)))
    return np.clip(c, 0, 1), g


def paint_pack(P, N, occ):
    c = shade("ply_d", N, occ, 0.10)
    c = grain(P, c, N, occ, "ply_d", 0.8)
    # dark edge strips on the box corners (darker where the normal turns)
    turn = 1 - np.max(np.abs(N), axis=1)
    c = stroke(c, sstep(0.10, 0.35, turn) * 0.35, H("#3A2A14"))
    return np.clip(c, 0, 1), np.full(len(P), gloss_of("ply_d"))


def paint_gear(gx, gz, gr):
    def f(P, N, occ):
        x, z = P[:, 0] - gx, P[:, 2] - (gz + 0.04)
        r = np.sqrt(x * x + z * z)
        a = np.arctan2(z, x)
        teeth = 0.5 + 0.5 * np.sign(np.sin(a * 8))
        c = shade("brass", N, occ)
        cut = (r > gr * 0.80) & (teeth < 0.5)
        c = np.where(cut[:, None], shade("ply_d", N, occ) * 0.7, c)
        c = stroke(c, line_w(np.abs(r - gr * 0.45), 0.0016), H("#5A3A10"))
        c = stroke(c, line_w(r, gr * 0.22), shade("steam", N, occ * 0))
        return np.clip(c, 0, 1), np.where(cut, 0.25, 0.85)
    return f


def paint_suit(P, N, occ):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    c = shade("suit", N, occ, 0.05)
    # stitched seams: dashed light lines along the outer sides of arms and legs, a zip line down the front of the belly
    dash = 0.5 + 0.5 * np.sign(np.sin(z * 900))
    for C in SEAMS:
        d = seg3(P, C)
        c = stroke(c, line_w(d, 0.0013, 0.0004) * dash * 0.8, H("#C9DC7A"))
        c = stroke(c, line_w(np.abs(d - 0.0026), 0.0007, 0.0004) * 0.5, H("#2E3A0C"))
    zip_ = line_w(np.abs(x), 0.0018) * (y < 0) * (z > 0.44) * (z < 0.48)
    c = stroke(c, zip_, shade("tin", N, occ))
    # lime patch on the left thigh, stitched
    pu, pv = x - 0.068, z - 0.315
    patch = (np.abs(pu) < 0.020) & (np.abs(pv) < 0.017) & (y < 0.0)
    c = np.where(patch[:, None], shade("lime", N, occ), c)
    edge = line_w(np.abs(np.maximum(np.abs(pu) - 0.020, np.abs(pv) - 0.017) + 0.003), 0.0008) * (y < 0)
    c = stroke(c, edge * 0.8 * (0.5 + 0.5 * np.sign(np.sin((pu + pv) * 1500))), H("#4A640A"))
    return np.clip(c, 0, 1), np.where(patch, 0.5, gloss_of("suit"))


def _side_seam(pts):
    return project(SUIT.d, np.array(pts), it=8)


SEAMS = []
for s in SIDES:
    d = nrm(EL[s] - SH[s])
    o = nrm(mirror((-abs(d[2]), 0.0, abs(d[0])), s))
    SEAMS.append(_side_seam([SH[s] + (EL[s] - SH[s]) * t + o * 0.05 for t in np.linspace(0.3, 1.0, 6)]))
    d2 = nrm(WR[s] - EL[s])
    SEAMS.append(_side_seam([EL[s] + (WR[s] - EL[s]) * t + o * 0.05 for t in np.linspace(0.25, 0.85, 5)]))
    SEAMS.append(np.array([radial_pt(SUIT, z, s * 90, c=(HIP[s][0], 0, 0), r0=0.3) for z in np.linspace(0.18, 0.37, 8)]))


def paint_glove(P, N, occ):
    c = shade("glove", N, occ, 0.10)
    return c, np.full(len(P), gloss_of("glove"))


def paint_belt(P, N, occ):
    c = shade("rubber", N, occ, 0.10)
    c = stroke(c, line_w(np.abs(P[:, 2] - BELT_Z), 0.0012) * 0.5, H("#585454"))
    return c, np.full(len(P), 0.25)


def paint_tyre(s):
    kc = KN[s] + np.array([0, -0.036, 0.004])

    def f(P, N, occ):
        v = P - kc
        a = np.arctan2(v[:, 2], v[:, 0])
        c = shade("rubber", N, occ, 0.15)
        tread = sstep(0.2, 0.9, np.abs(np.sin(a * 9))) * sstep(0.022, 0.034, np.sqrt(v[:, 0] ** 2 + v[:, 2] ** 2))
        c = stroke(c, tread * 0.55, H("#0A0808"))
        return c, np.full(len(P), 0.2)
    return f


def paint_boot(s):
    ax, ay = AN[s][0], AN[s][1]

    def f(P, N, occ):
        x, y, z = P[:, 0], P[:, 1], P[:, 2]
        c = shade("lime", N, occ, 0.4)
        g = np.full(len(P), gloss_of("lime"))
        # white laces on the instep (3 crosses) and a white toe cap stripe
        u = x - ax
        on_top = (y < ay - 0.010) & (N[:, 2] > 0.2)
        for k in range(3):
            yy = ay - 0.024 - k * 0.012
            d1 = np.abs((u) - (y - yy) * 0.9)
            d2 = np.abs((u) + (y - yy) * 0.9)
            box = (np.abs(u) < 0.018) & (np.abs(y - yy) < 0.008)
            m = (line_w(d1, 0.0022) + line_w(d2, 0.0022)) * box * on_top
            c = stroke(c, np.clip(m, 0, 1), H("#F6F4EE"))
        # dark stripe round the top of the shaft
        c = stroke(c, line_w(np.abs(z - 0.142), 0.004) * (N[:, 2] < 0.7), H("#4A640A"))
        return np.clip(c, 0, 1), g
    return f


def paint_sole(P, N, occ):
    c = shade("sole", N, occ, 0.2)
    c = stroke(c, line_w(np.abs(P[:, 2] - 0.013), 0.0012) * 0.6, H("#8A867C"))
    return c, np.full(len(P), 0.3)


def paint_steam(P, N, occ):
    c = shade("steam", N, occ * 0.3, 0.6)
    return c, np.ones(len(P))


def paint_lens(P, N, occ):
    c = shade("lens", N, occ * 0.5, 0.9)
    # reflection streak
    q = (P[:, 0] * 0.7 + P[:, 2] * 0.7)
    st = line_w(np.abs(((q * 180) % 1.0) - 0.5), 0.10, 0.05) * sstep(0.3, 0.8, -N[:, 1])
    c = stroke(c, st * 0.45, H("#FFFFFF"))
    return c, np.ones(len(P))


def paint_blaster(name, G):
    def f(P, N, occ):
        if name == "tube":
            c = shade("lime", N, occ, 0.5)
            # dark bands round the barrel + a lighter stripe along the top
            for yb in (0.040, -0.095):
                c = stroke(c, line_w(np.abs(P[:, 1] - G[1] - yb), 0.0045), shade("lime", N, occ) * 0.55)
            c = stroke(c, line_w(np.abs(P[:, 0] - G[0]), 0.004) * sstep(0.5, 0.9, N[:, 2]) * 0.5, H("#F0FFA0"))
            return np.clip(c, 0, 1), np.full(len(P), 0.6)
        if name == "pea":
            return shade("steam", N, occ * 0.2, 0.8), np.ones(len(P))
        if name in ("tank", "dial"):
            return shade("tin", N, occ), np.full(len(P), 0.8)
        if name == "grip":
            c = shade("ply", N, occ, 0.1)
            return grain(P, c, N, occ, "ply", 1.6), np.full(len(P), 0.3)
        return shade("brass", N, occ), np.full(len(P), 0.85)
    return f


def assign_paints():
    for p in PARTS:
        n = p.name
        if n == "suit":
            f = paint_suit
        elif n.startswith("hand_"):
            f = paint_glove
        elif n == "head":
            f = paint_head
        elif n.startswith("eye_"):
            f = paint_eye
        elif n.startswith("brow"):
            f = paint_brow
        elif n == "hair":
            f = paint_hair
        elif n == "chest":
            f = paint_chest
        elif n == "pack":
            f = paint_pack
        elif n.startswith("gear"):
            f = paint_gear(*GEARS[int(n[4:])])
        elif n == "belt":
            f = paint_belt
        elif n.startswith("tyre_"):
            f = paint_tyre(-1 if n.endswith("R") else 1)
        elif n.startswith("boot_"):
            f = paint_boot(-1 if n.endswith("R") else 1)
        elif n.startswith("sole_"):
            f = paint_sole
        elif n.startswith("puff_"):
            f = paint_steam
        elif n.startswith("goglens"):
            f = paint_lens
        elif n.startswith("bl"):
            f = paint_blaster(n.split("_", 1)[1], BL_G[n[2]])
        elif p.key in ("brass", "tin"):
            f = paint_metal(p.key)
        else:
            f = paint_flat(p.key, 0.2)
        p.paint = f


assign_paints()


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



# Shells meshed and decimated on their own (exact budget each). "weapon" = the right blaster -> tess_weapon.fbx; the rest
# is the body (<= 4000 together). The left blaster is part of the body mesh (on ArmL).
SHELLS = [  # name, part-name prefixes, game triangles
    ("head", ("head", "eye_", "brow", "hair", "gog"), 1480),
    ("hand_R", ("hand_R",), 170),
    ("hand_L", ("hand_L",), 170),
    ("blaster_L", ("blL_",), 440),
    ("weapon", ("blW_",), 1170),
    ("body", (), 1720),
]
WEAPON_SHELL = 4


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


def build_low(Vh, Th, target, fn, name="Tess"):
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
        lo, hi = np.maximum(lo, BB_LO), np.minimum(hi, BB_HI)
        Vh, Th = mesh_field(fn, h, lo, hi)
        V, T = build_low(Vh, Th, budget, fn, name)
        Vs.append(V)
        Ts.append(T + base)
        sid.append(np.full(len(V), k))
        base += len(V)
    V, T, sid = np.concatenate(Vs), np.concatenate(Ts), np.concatenate(sid)
    ob = to_mesh("Tess", V, T)
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



def face_factor(C, own_names):
    f = np.ones(len(C))
    z, y, x = C[:, 2], C[:, 1], C[:, 0]
    head = z > 0.69
    f[head] = 1.25
    face = head & (y < -0.035) & (z < 0.87) & (np.abs(x) < 0.10)
    f[face] = 2.4
    for pre, val in (("eye_", 2.6), ("brow", 2.2), ("hand_", 1.1), ("chest", 1.6), ("blW_", 1.25), ("blL_", 1.1),
                     ("puff_", 0.6), ("sole_", 0.6), ("pack", 0.8)):
        f[np.char.startswith(own_names, pre)] = val
    return f


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
    im = bpy.data.images.new("tess_tex", TEX, TEX, alpha=True)
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




# ---- bones and weights ----
HIP_Z, NECK_Z = 0.385, 0.665
BONE_DEF = [  # name, parent, head, tail (model coordinates before normalisation)
    ("Root", None, (0, 0, 0), (0, 0, HIP_Z)),
    ("Body", "Root", (0, 0, HIP_Z), (0, 0, NECK_Z)),
    ("Head", "Body", (0, 0, NECK_Z), (0, 0, 1.02)),
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


def suit_weights(P):
    """Smooth weights over the jumpsuit: soft-min of the distances to the torso / shoulder ball / upper arm / forearm /
    leg pieces (10 mm falloff), the torso split Root -> Body over the waist."""
    groups = [("torso", TORSO_NECK)]
    for s in SIDES:
        sh, up, lo = ARM[s].ch
        groups += [("Body", sh), ("Arm" + SN[s], up), ("Arm" + SN[s], lo), ("Leg" + SN[s], LEG[s])]
    D = np.stack([np.maximum(g.d(P.astype(F32)), 0) for _, g in groups], 0)
    Wt = np.exp(-(D - D.min(0)) / 0.010)
    out = []
    zb = sstep(0.42, 0.48, P[:, 2])
    names = [n for n, _ in groups]
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


SUB_EDGES = {}



def make_weapon(V, T, uvc, img, tex_path, scale):
    """Weapon mesh: origin = the Prop bone head (grip), same scale as the body, empty "Muzzle" at the barrel tip."""
    Vn = (V - PROP_HEAD) * scale
    ob = to_mesh("TessWeapon", Vn, T)
    me = ob.data
    ul = me.uv_layers.new(name="UVMap")
    ul.data.foreach_set("uv", np.asarray(uvc, np.float64).ravel())
    smp = sample_tex(img, uvc, blur=3)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(smp[:, :3]), smp[:, 3:4]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    me.materials.append(bpy.data.materials["FighterVC"])
    for p in me.polygons:
        p.use_smooth = True
    muz = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(muz)
    muz.location = Vector((MUZZLE_W - PROP_HEAD) * scale)
    muz.parent = ob
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    muz.select_set(True)
    bpy.context.view_layer.objects.active = ob
    base = os.path.join(OUT_DIR, "tess_weapon")
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH", "EMPTY"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
    return ob, tuple(np.round(np.asarray(muz.location), 3))


def subset(V, T, uvc, mask_v):
    keep_t = mask_v[T].all(1)
    used = np.unique(T[keep_t])
    remap = np.full(len(V), -1)
    remap[used] = np.arange(len(used))
    uv3 = np.asarray(uvc).reshape(-1, 3, 2)[keep_t].reshape(-1, 2)
    return V[used], remap[T[keep_t]], uv3, used


def full_stage():
    t_start = time.time()
    body_parts = [p for p in PARTS if not p.name.startswith("blW_")]
    Vh, Th = mesh_field(shell_fn(body_parts), 0.004)
    lo, hi = Vh.min(0), Vh.max(0)
    del Vh, Th
    shift = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
    scale = 1.0 / (hi[2] - lo[2])
    log("normalisation shift", np.round(shift, 4), "scale", round(scale, 4))
    low, sid = build_shells(0.0025)
    uv_loops = unwrap(low)
    img = bake_texture(low, uv_loops, sid)
    tex_path = os.path.join(OUT_DIR, "tess_tex.png")
    save_texture(img, tex_path)
    log("texture", tex_path)
    V, T = mesh_np(low)
    me = low.data
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    uvc = uv_loops[lt]
    bpy.data.objects.remove(low, do_unlink=True)
    wmask = sid == WEAPON_SHELL
    Vb, Tb, Ub, ub = subset(V, T, uvc, ~wmask)
    Vw, Tw, Uw, _ = subset(V, T, uvc, wmask)
    W = vertex_weights(Vb, sid[ub])
    ob, arm = make_object("Tess", Vb, Tb, Ub, W, img, tex_path, shift, scale)
    export(ob, arm, os.path.join(OUT_DIR, "tess_game"))
    wob, muz = make_weapon(Vw, Tw, Uw, img, tex_path, scale)
    infl = max(len(w) for w in W)
    log(f"RESULT body tris {len(Tb)}/4000 verts {len(Vb)} max infl {infl} | weapon tris {len(Tw)}/1200 muzzle {muz}")
    log("prop head (Prop bone)", np.round((PROP_HEAD + shift) * scale, 3), "eyes",
        np.round((EYE[-1][0] + shift) * scale, 3), np.round((EYE[1][0] + shift) * scale, 3))
    assert len(Tb) <= 4000 and len(Tw) <= 1200, "over budget"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT_DIR, "tess_game.fbx"), colors_type="SRGB")
    m = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    a = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    pts = np.array([tuple(m.matrix_world @ v.co) for v in m.data.vertices])
    log("FBX check: bones", sorted(b.name for b in a.data.bones), "height", round(float(pts[:, 2].max() - pts[:, 2].min()), 4),
        "tris", sum(len(p.vertices) - 2 for p in m.data.polygons))
    log(f"full stage {time.time() - t_start:.0f}s")


def shape_stage():
    V, T = mesh_field(fig_d, 0.004, BB_LO, BB_HI)
    ob = to_mesh("TessQuick", V, T)
    cols = key_colours(V[T].mean(1).astype(F32))
    attr = ob.data.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([np.repeat(srgb_to_lin(cols), 3, 0), np.ones((len(T) * 3, 1))]).astype(np.float32).ravel())
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "VERTEX"
    sc.display.shading.show_cavity = True
    sc.world = bpy.data.worlds.new("W")
    sc.render.resolution_x = sc.render.resolution_y = 520
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.15
    sc.camera = cam
    panels = []
    for name, yaw, pitch in (("front", 0, 4), ("34", 40, 10), ("side", -90, 4), ("back", 180, 4), ("top", 0, 89.5), ("game", 0, 55)):
        yr, pr = math.radians(yaw), math.radians(pitch)
        t = Vector((0, 0, 0.5))
        dv = Vector((math.sin(yr) * math.cos(pr), -math.cos(yr) * math.cos(pr), math.sin(pr)))
        cam.location = t + dv * 4
        cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        fp = os.path.join(PREV_DIR, f"_tmp_tess_v2q_{name}.png")
        sc.render.filepath = fp
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(fp)
        px = np.empty(520 * 520 * 4, np.float32)
        im.pixels.foreach_get(px)
        panels.append(px.reshape(520, 520, 4))
        os.remove(fp)
    sheet = np.concatenate(panels, 1)
    out = bpy.data.images.new("q", sheet.shape[1], sheet.shape[0])
    out.pixels.foreach_set(np.ascontiguousarray(sheet).ravel())
    out.filepath_raw = os.path.join(PREV_DIR, "_tess_v2_quick.png")
    out.file_format = "PNG"
    out.save()
    log("quick preview", out.filepath_raw)


bpy.ops.wm.read_factory_settings(use_empty=True)
if STAGE == "shape":
    shape_stage()
else:
    full_stage()
log("done")

