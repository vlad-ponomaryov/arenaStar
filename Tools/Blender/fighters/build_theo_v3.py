# Theo v3, the starter shooter (Docs/Briefs/A-Theo3D.md step 2c): one smooth glossy "vinyl toy" figure modelled to the
# silhouettes of the Flow references 001.png (front) and 002.png (profile / back). Headless (Blender 5.2, numpy only):
#   blender -b --factory-startup --python Tools/Blender/fighters/build_theo_v3.py -- [out_dir] [--stage shape|full]
#   --stage full (default, ~5 min): models, textures, rigs and exports everything below and checks the silhouettes;
#   --stage shape (~35 s): only the exact-surface mesh -> IoU, v3_overlay_dev.png, v3_compare_dev.png, v3_quick.png.
# Output (default Art/Source/3D/theo/): theo_v3_game.fbx/.glb (<= 4000 tris), theo_v3_show.fbx/.glb (<= 20000 tris = the game
# mesh split 1:4 and projected onto the exact surface: same UVs, same texture), theo_v3_tex.png (1024 RGBA: RGB = albedo
# with baked AO / light gradients / metallic banding / decals, A = gloss). Both meshes: one mesh "Theo", one material
# "FighterVC" (image texture node -> the PNG), armature "Rig" with the 12 FighterBone bones, <= 4 weights per vertex.
# Format as v1/v2 (build_theo.py, prepare_fighter.py): Blender Z up, faces -Y, height 1.0, feet on z = 0, centred on X/Y;
# FBX axis_forward=-Z, axis_up=Y, colors_type=SRGB; UV map "UVMap"; vertex colour "Col" (corner, linear in Blender) = the
# texture blurred per corner (fallback for the current vertex-colour shader), alpha = gloss.
# Checks (Tools/out/art/theo3d/): v3_overlay.png (game mesh silhouette over 001 / 002 profile / 002 back, IoU and contour
# distances in the log, theo_silhouette.py), v3_compare.png (reference | unlit textured game mesh, same scale).
#
# Method: the whole figure is one signed distance field (numpy): a body shell of lofted superellipse sections and round
# cones fitted to the reference silhouettes (smooth unions), armour plates as offset shells of the body cut by outlines
# traced from 001 / 002 (rounded edges), a hair envelope lofted from the silhouette notches with chunky ribbon locks lying on
# it (great-circle paths, tips lifting to the silhouette spikes), face parts. The field is meshed (narrow-band surface nets
# + Newton projection, 2.5 mm), quadric-decimated to the game budget, smart-UV unwrapped (face / chest islands enlarged)
# and textured by evaluating a procedural paint function on the exact surface for every texel (eyes, brows, mouth,
# freckles, emblem, trims, panel lines, seams, lights, AO from the field, red gradient, metallic gold banding).
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)
import theo_silhouette as TS  # noqa: E402

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = ARGV[ARGV.index("--stage") + 1] if "--stage" in ARGV else "full"
POS = [a for i, a in enumerate(ARGV) if not a.startswith("--") and (i == 0 or ARGV[i - 1] != "--stage")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "theo")
PREV_DIR = os.path.join(REPO, "Tools", "out", "art", "theo3d")
os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(PREV_DIR, exist_ok=True)
T0 = time.time()


def log(*a):
    print(f"[theo3 {time.time() - T0:6.1f}s]", *a, flush=True)


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


# ---------------- suit: torso loft, neck, arms, legs (graphite) ----------------
TORSO = Loft(smooth_rows([  # z, half width, cx, y front, y back, exponent
    (0.352, 0.026, 0, -0.030, 0.040, 2.0),
    (0.366, 0.060, 0, -0.050, 0.062, 2.2),
    (0.385, 0.088, 0, -0.062, 0.074, 2.4),
    (0.410, 0.095, 0, -0.068, 0.079, 2.5),
    (0.440, 0.097, 0, -0.074, 0.080, 2.5),
    (0.470, 0.097, 0, -0.077, 0.077, 2.5),
    (0.500, 0.092, 0, -0.077, 0.075, 2.5),
    (0.530, 0.092, 0, -0.078, 0.080, 2.5),
    (0.560, 0.093, 0, -0.080, 0.088, 2.6),
    (0.600, 0.092, 0, -0.076, 0.094, 2.6),
    (0.640, 0.089, 0, -0.068, 0.091, 2.6),
    (0.670, 0.081, 0, -0.058, 0.077, 2.4),
    (0.695, 0.062, 0, -0.047, 0.058, 2.2),
    (0.712, 0.040, 0, -0.035, 0.042, 2.0)]))
NECK = RCone((0, 0.004, 0.66), (0, 0.002, 0.745), 0.043, 0.040, bone="Body")
SH = {s: mirror((-0.118, 0.006, 0.628), s) for s in SIDES}   # shoulder joint (under the pad)
EL = {s: mirror((-0.180, 0.002, 0.526), s) for s in SIDES}   # elbow (top rim of the bracer)
WR = {s: mirror((-0.232, -0.004, 0.432), s) for s in SIDES}  # wrist (bottom rim of the bracer)
HIP = {s: mirror((-0.054, 0.005, 0.400), s) for s in SIDES}
KN = {s: mirror((-0.089, 0.004, 0.255), s) for s in SIDES}
SHIN = {s: mirror((-0.100, 0.008, 0.205), s) for s in SIDES}
AN = {s: mirror((-0.116, 0.014, 0.115), s) for s in SIDES}
ARM, LEG = {}, {}
for s in SIDES:
    a = "Arm" + SN[s]
    ARM[s] = U(Ell(SH[s] + mirror((0.010, 0, 0.004), s), (0.046, 0.046, 0.046), bone="Body"),
               RCone(SH[s], EL[s], 0.042, 0.037, bone=a), RCone(EL[s], WR[s], 0.035, 0.031, bone=a), k=0.01)
    lg = "Leg" + SN[s]
    pts = [HIP[s], mirror((-0.071, 0.003, 0.340), s), mirror((-0.078, 0.002, 0.300), s), KN[s], SHIN[s], AN[s]]
    rad = [0.050, 0.060, 0.053, 0.048, 0.044, 0.040]
    LEG[s] = U(*[RCone(pts[i], pts[i + 1], rad[i], rad[i + 1], bone=lg) for i in range(len(pts) - 1)], k=0.0)
TORSO_NECK = U(TORSO, NECK, k=0.02)
SUIT = U(*[U(TORSO_NECK, ARM[s], LEG[s], k=0.022) for s in SIDES])  # legs blend with the hips, not with each other
part("suit", SUIT, "graphite", ("suit",))

# ---------------- hands (glove: palm, three half-bent fingers curling toward the palm, thumb in front) ----------------
HANDF, FINGER_INFO = {}, {}
for s in SIDES:
    a = "Arm" + SN[s]
    dn = nrm(mirror((-0.36, 0.0, -0.93), s))                  # hand axis, ~21 deg from vertical, outward
    f0 = np.array([-s * 0.55, -0.83, 0.0])                    # finger spread / thumb side: forward and inward
    fw = nrm(f0 - dn * np.dot(f0, dn))
    out = np.cross(fw, dn)
    out = nrm(out if out[0] * s > 0 else -out)                 # back of the hand (outward)
    W0 = WR[s]
    pc = W0 + dn * 0.042 + out * 0.004
    HANDF[s] = (pc, dn, out, fw)
    Rh = np.stack([out, fw, dn], 1)  # local x = outward (back of the hand), y = forward, z = down
    palm = RBox(pc + out * 0.002, (0.024, 0.041, 0.037), 0.018, Rh, bone=a)
    wrist = RCone(W0 - dn * 0.012, pc - dn * 0.012, 0.031, 0.031, bone=a)
    fingers = []
    for k, fo in enumerate((-0.0225, 0.0, 0.0225)):
        ln = (0.92, 1.0, 0.94)[k]
        b0 = pc + dn * 0.024 + fw * fo + out * 0.003            # knuckle
        b1 = b0 + dn * 0.030 * ln + out * 0.001 + fw * fo * 0.05  # first joint (straight down)
        b2 = b1 + dn * 0.021 * ln - out * 0.011                   # half bent toward the palm
        b3 = b2 + dn * 0.007 * ln - out * 0.012                   # tip
        fingers += [RCone(b0, b1, 0.0122, 0.0116, bone=a), RCone(b1, b2, 0.0116, 0.0106, bone=a),
                    RCone(b2, b3, 0.0106, 0.0094, bone=a)]
    FINGER_INFO[s] = (pc + dn * 0.024 + out * 0.003, dn, out, fw)
    tb = pc + fw * 0.031 - out * 0.010 - dn * 0.014
    tm = tb + fw * 0.012 + dn * 0.028 - out * 0.004
    tt = tm + fw * 0.004 + dn * 0.022 - out * 0.006
    thumb = U(RCone(tb, tm, 0.0170, 0.0158, bone=a), RCone(tm, tt, 0.0158, 0.0132, bone=a), k=0.004)
    HAND = U(U(wrist, palm, thumb, k=0.010), U(*fingers, k=0.0), k=0.007)
    part("hand_" + SN[s], HAND, "graphite", (a,))
    # gold plates: back of the hand (over the palm) and on the thumb
    bp = pc + out * 0.024 - dn * 0.003 + fw * 0.004
    part("glove_plate_" + SN[s], I(Off(HAND, 0.0045), RBox(bp, (0.012, 0.031, 0.024), 0.009, Rh), k=0.003), "gold", (a,))
    part("thumb_plate_" + SN[s], I(Off(HAND, 0.0040), RBox(tb + (tm - tb) * 0.45 + fw * 0.010, (0.016, 0.013, 0.018), 0.008,
                                                              np.stack([out, fw, nrm(tm - tb)], 1)), k=0.003), "gold", (a,))

# ---------------- head (skin) ----------------
CRAN = Ell((0, 0.002, 0.832), (0.094, 0.098, 0.112), bone="Head")
FACE = Ell((0, -0.022, 0.778), (0.091, 0.083, 0.072), bone="Head")
CHEEKS = [Ell((s * 0.051, -0.060, 0.760), (0.047, 0.041, 0.038), bone="Head") for s in SIDES]
CHIN = Ell((0, -0.070, 0.727), (0.036, 0.030, 0.022), bone="Head")
NOSE = U(Ell((0, -0.101, 0.775), (0.017, 0.017, 0.014), bone="Head"),
         Ell((0, -0.095, 0.790), (0.009, 0.012, 0.014), bone="Head"), k=0.012)
EARS = []
for s in SIDES:
    Re = rot((0, 0, 1), s * 20) @ rot((0, 1, 0), s * -6)
    ear = Ell((s * 0.108, 0.014, 0.782), (0.017, 0.026, 0.034), Re, bone="Head")
    concha = Ell((s * 0.122, 0.008, 0.780), (0.008, 0.015, 0.021), Re)
    EARS.append(S(ear, concha, k=0.006))
HEAD_SKULL = U(U(CRAN, FACE, CHIN, k=0.04), *CHEEKS, k=0.04)
HEAD = U(HEAD_SKULL, NOSE, *EARS, k=0.012)
part("head", HEAD, "skin", ("Head",))
EYE = {}  # eye centre on the face and gaze direction (for bones and paint)
for s in SIDES:
    p = front_pt(HEAD_SKULL, s * 0.047, 0.803)
    EYE[s] = (p, nrm(normal_at(HEAD_SKULL, p) + np.array([0, -1.5, 0])))
# brows: thin raised ribbons (their shape reaches the texture through the paint owner + AO)
BROWS = {-1: [(-0.080, 0.826), (-0.064, 0.836), (-0.045, 0.838), (-0.028, 0.834), (-0.019, 0.829)],
         1: [(0.017, 0.831), (0.030, 0.840), (0.048, 0.845), (0.064, 0.842), (0.077, 0.836)]}
for s in SIDES:
    C = np.array([front_pt(HEAD_SKULL, x, z) for x, z in BROWS[s]])
    Nn = np.array([normal_at(HEAD_SKULL, c) for c in C])
    C = C - Nn * 0.0002
    tan = np.gradient(C, axis=0)
    Wv = np.array([nrm(t) for t in tan])
    wv = np.array([0.0055, 0.0068, 0.0068, 0.0064, 0.0050]) * 1.0
    part("brow_" + SN[s], Ribbon(C, np.cross(Nn, Wv) * -1, Nn, wv, np.full(5, 0.0011), bone="Head"), "hair", ("Head",))

# ---------------- hair: a hair volume + chunky ribbon locks lying on it (only the tips lift off) ----------------
HV_C = np.array([0.0, 0.010, 0.860])
HVOL = Loft(smooth_rows([  # outer hair envelope from the notches between the lock tips (001 front, 002 profile)
    (0.724, 0.058, 0, 0.000, 0.062, 2.2),
    (0.745, 0.080, 0, -0.010, 0.084, 2.2),
    (0.770, 0.095, 0, -0.030, 0.097, 2.3),
    (0.800, 0.104, 0, -0.060, 0.110, 2.4),
    (0.830, 0.111, 0, -0.090, 0.118, 2.5),
    (0.860, 0.118, 0, -0.108, 0.122, 2.6),
    (0.890, 0.120, 0, -0.112, 0.122, 2.7),
    (0.920, 0.112, 0, -0.108, 0.115, 2.8),
    (0.945, 0.098, 0, -0.098, 0.100, 2.8),
    (0.962, 0.080, 0, -0.080, 0.078, 2.6),
    (0.974, 0.045, 0, -0.050, 0.045, 2.2),
    (0.979, 0.010, 0, -0.012, 0.012, 2.0)], 60))
HAIR_YZ = np.array([(-0.20, 0.880), (-0.11, 0.876), (-0.07, 0.866), (-0.040, 0.848), (-0.030, 0.800), (-0.004, 0.800),
                    (0.004, 0.838), (0.040, 0.842), (0.060, 0.800), (0.078, 0.745), (0.060, 0.722), (0.20, 0.722),
                    (0.20, 1.10), (-0.20, 1.10)])
CAP = S(I(U(Off(CRAN, 0.010), Ell((0.0, 0.030, 0.800), (0.090, 0.078, 0.074)), k=0.03), Poly2(HAIR_YZ, "yz"), k=0.008),
        Ell((0, -0.098, 0.775), (0.084, 0.060, 0.093)), k=0.012)  # scalp under the locks; HVOL = the outer envelope
LOCKS = []


def hdir(az, el):
    a, e = math.radians(az), math.radians(el)
    return np.array([math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)])


def on_vol(d):
    """Surface point of the hair volume along direction d from its centre, and the outward normal there."""
    p = trace(HVOL.d, HV_C + d * 0.30, -d)
    return p, normal_at(HVOL, p)


def slerp(d0, d1, t):
    d0, d1 = nrm(d0), nrm(d1)
    om = math.acos(float(np.clip(np.dot(d0, d1), -1, 1)))
    if om < 1e-4:
        return d0
    return (math.sin((1 - t) * om) * d0 + math.sin(t * om) * d1) / math.sin(om)


def lock(start, tip, W, th=0.46, n=32, rise=0.62):
    """Lock from start (az, el) on the volume to the tip point along a great circle: the body lies on the envelope (top
    flush with it), the last part lifts off so that the tip lands on the target (silhouette spikes)."""
    tip = np.asarray(tip, float)
    d0, d1 = hdir(*start), nrm(tip - HV_C)
    s = np.linspace(0, 1, n)
    pts, nors = [], []
    for k in range(n):
        p, nn = on_vol(nrm(slerp(d0, d1, s[k])))
        pts.append(p)
        nors.append(nn)
    pts, nors = np.array(pts), np.array(nors)
    prof = (0.15 + 0.85 * sstep_np(0.0, 0.30, s)) * np.cos(np.clip(s, 0, 1) * math.pi / 2) ** 0.65
    w = np.maximum(W * prof / prof.max(), 0.0024)
    t = np.maximum(w * th, 0.0013)
    end_out = float(np.dot(tip - pts[-1], nors[-1]))
    lift = sstep_np(rise, 1.0, s) ** 1.5
    sink = 0.80 + 1.2 * (1 - sstep_np(0.0, 0.25, s))
    C = pts - nors * (t * sink)[:, None] + nors * (lift * max(end_out, 0.0))[:, None]
    C[-1] = tip
    tan = np.gradient(C, axis=0)
    tan /= np.linalg.norm(tan, axis=1, keepdims=True)
    Tv, Wv = [], []
    for c, tg, nn in zip(C, tan, nors):
        up = nn - tg * np.dot(nn, tg)
        if np.linalg.norm(up) < 1e-4:
            up = nrm(c - HV_C) - tg * np.dot(nrm(c - HV_C), tg)
        up = nrm(up)
        Tv.append(up)
        Wv.append(np.cross(tg, up))
    rb = Ribbon(C, Wv, Tv, w, t, bone="Head")
    LOCKS.append(rb)
    return rb


def sstep_np(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


LOCK_DEF = [  # start (az, el) on the envelope -> tip point (silhouette targets from 001 / 002), half width
    # front quiff: locks rise from the front hairline up and to the character's left (viewer's right); the forehead stays
    # bare in the middle, two locks curl down at its sides
    ((-24, 20), (0.058, -0.030, 0.988), 0.031),
    ((2, 16), (0.007, -0.040, 1.000), 0.031),
    ((26, 20), (0.105, -0.040, 0.955), 0.031),
    ((-46, 22), (-0.055, -0.080, 0.993), 0.031),
    ((-20, 40), (-0.028, -0.118, 0.948), 0.031),
    ((-62, 30), (-0.110, -0.050, 0.970), 0.028),
    ((-55, 62), (0.064, -0.100, 0.862), 0.034),
    ((-40, 52), (-0.074, -0.104, 0.860), 0.026),
    ((40, 25), (0.138, -0.010, 0.905), 0.027),
    # character's left side (+X, viewer's right)
    ((-20, 75), (0.112, 0.012, 0.950), 0.029),
    ((20, 65), (0.140, 0.028, 0.898), 0.029),
    ((45, 55), (0.130, 0.000, 0.862), 0.025),
    ((60, 30), (0.105, -0.034, 0.818), 0.019),
    # character's right side (-X, viewer's left)
    ((10, 78), (-0.112, 0.004, 0.962), 0.029),
    ((-20, 65), (-0.140, -0.020, 0.935), 0.029),
    ((-40, 55), (-0.143, 0.000, 0.875), 0.027),
    ((-55, 35), (-0.114, -0.030, 0.844), 0.020),
    # back, upper row swept back over the crown
    ((-25, 44), (-0.030, 0.100, 0.955), 0.031),
    ((5, 42), (0.010, 0.137, 0.915), 0.033),
    ((35, 44), (0.070, 0.110, 0.940), 0.031),
    ((-70, 48), (-0.110, 0.080, 0.930), 0.029),
    ((70, 48), (0.110, 0.080, 0.925), 0.029),
    # back, middle row pointing down-back
    ((-170, 60), (-0.060, 0.127, 0.855), 0.031),
    ((170, 60), (0.055, 0.127, 0.850), 0.031),
    ((-140, 55), (-0.112, 0.080, 0.850), 0.027),
    ((140, 55), (0.113, 0.080, 0.845), 0.027),
    ((180, 50), (0.000, 0.130, 0.820), 0.030),
    # nape
    ((-165, 15), (-0.030, 0.098, 0.752), 0.029),
    ((165, 15), (0.032, 0.098, 0.750), 0.029),
    ((-150, 20), (-0.076, 0.078, 0.775), 0.025),
    ((150, 20), (0.077, 0.078, 0.772), 0.025),
]
HAIR = U(CAP, *[lock(st, tp, w * 1.16) for st, tp, w in LOCK_DEF], k=0.003)
part("hair", HAIR, "hair", ("Head",))

# ---------------- visor (monocle on the character's left eye, +X) and headset (right ear, -X) ----------------
VIS_V = []
for i in range(32):
    a = 2 * math.pi * i / 32
    cx, cz = math.cos(a), math.sin(a)
    q = np.sign([cx, cz]) * np.abs([cx, cz]) ** 0.30
    VIS_V.append((0.057 + 0.039 * q[0], 0.804 + 0.0285 * q[1]))
VISOR = I(Off(HEAD_SKULL, 0.014), Poly2(VIS_V, "xz"), Half((0, 1, 0), (0, -0.035, 0)), k=0.0065)
part("visor", VISOR, "visor", ("Head",))
part("visor_hinge", U(RBox((0.098, -0.038, 0.806), (0.008, 0.013, 0.021), 0.005),
                      RCone((0.099, -0.030, 0.812), (0.104, 0.002, 0.816), 0.0045, 0.0045), k=0.004), "headset", ("Head",))
BOOM = [(-0.101, -0.020, 0.776), (-0.098, -0.048, 0.752), (-0.084, -0.077, 0.736), (-0.064, -0.094, 0.731)]
part("headset", U(RCone((-0.102, -0.012, 0.783), (-0.102, -0.012, 0.832), 0.0095, 0.0085),
                  *[RCone(BOOM[i], BOOM[i + 1], 0.0045, 0.0045) for i in range(3)],
                  RCone((-0.066, -0.094, 0.731), (-0.046, -0.101, 0.731), 0.0068, 0.0068), k=0.004), "headset", ("Head",))

# ---------------- collar, vest (chest + back plates with the abdomen tongue), spine panel ----------------
COLLAR_C = np.array([0.0, 0.002, 0.6865])   # the ring sits on the chest plate: its front flush with the neckline trim
COLLAR = S(RCyl(COLLAR_C, (0, 0, 1), 0.079, 0.0205, 0.009, sxy=(1, 0.98)),
           RCyl(COLLAR_C + [0, 0, 0.01], (0, 0, 1), 0.054, 0.04, 0.0, sxy=(1, 0.98)), k=0.005)
part("collar", COLLAR, "red", ("Body",))
VEST_F = np.array([(-0.135, 0.705), (-0.086, 0.700), (-0.066, 0.679), (-0.052, 0.663), (0.052, 0.663), (0.066, 0.679),
                   (0.086, 0.700), (0.135, 0.705), (0.135, 0.62), (0.113, 0.56), (0.106, 0.537), (0.080, 0.546),
                   (0.050, 0.553), (0.047, 0.545), (0.046, 0.474), (-0.046, 0.474), (-0.047, 0.545), (-0.050, 0.553),
                   (-0.080, 0.546), (-0.106, 0.537), (-0.113, 0.56), (-0.135, 0.62)])
VEST_B = np.array([(-0.135, 0.705), (0.135, 0.705), (0.135, 0.52), (0.090, 0.505), (0.050, 0.518), (0.0, 0.528),
                   (-0.050, 0.518), (-0.090, 0.505), (-0.135, 0.52)])
VEST_REG = U(I(Poly2(VEST_F, "xz"), Half((0, 1, 0), (0, 0.012, 0))), I(Poly2(VEST_B, "xz"), Half((0, -1, 0), (0, 0.012, 0))))
def vest_bulge(P):
    """Convex chest plate (pectoral dome, slight abdomen and back domes) on top of the plate thickness."""
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    fr = np.clip((0.0 - y) / 0.04, 0, 1)
    bk = np.clip((y - 0.0) / 0.04, 0, 1)
    chest = 0.0135 * np.exp(-((x / 0.078) ** 2 + ((z - 0.592) / 0.045) ** 2)) * fr
    tongue = 0.0035 * np.exp(-((x / 0.040) ** 2 + ((z - 0.515) / 0.035) ** 2)) * fr
    back = 0.0040 * np.exp(-((x / 0.075) ** 2 + ((z - 0.600) / 0.050) ** 2)) * bk
    return chest + tongue + back


VEST_T = 0.013
VEST = I(OffVar(TORSO, VEST_T, vest_bulge), VEST_REG, k=0.006)
part("vest", VEST, "red", ("Body",))
ARCH = [(-0.113, 0.566), (-0.107, 0.5415), (-0.080, 0.5505), (-0.050, 0.5575), (0.0, 0.5600), (0.050, 0.5575), (0.080, 0.5505),
        (0.107, 0.5415), (0.113, 0.566)]
YOKE = [(-0.086, 0.697), (-0.066, 0.676), (-0.052, 0.660), (0.052, 0.660), (0.066, 0.676), (0.086, 0.697)]
BACK_TRIM = [(-0.136, 0.525), (-0.090, 0.5095), (-0.050, 0.5225), (0.0, 0.5325), (0.050, 0.5225), (0.090, 0.5095),
             (0.136, 0.525)]
FRONT_H, BACK_H = Half((0, 1, 0), (0, 0.012, 0)), Half((0, -1, 0), (0, 0.012, 0))
TRIM_SURF = OffVar(TORSO, VEST_T + 0.0035, vest_bulge)
part("vest_trim", U(I(TRIM_SURF, U(Band2(ARCH, 0.0046), Band2(YOKE, 0.0046)), Half((0, 1, 0), (0, -0.045, 0)), k=0.0025),
                    I(TRIM_SURF, Band2(BACK_TRIM, 0.0046), Half((0, -1, 0), (0, 0.045, 0)), k=0.0025)), "gold", ("Body",))
SPINE = I(OffVar(TORSO, VEST_T + 0.008, vest_bulge), Poly2([(-0.036, 0.666), (0.036, 0.666), (0.027, 0.553), (-0.027, 0.553)], "xz"),
          Half((0, -1, 0), (0, 0.03, 0)), k=0.004)
part("spine", SPINE, "red", ("Body",))

# ---------------- shoulder pads: three layered faceted plates (box shells with big chamfers = "hip roof" facets), rivets --
PAD_INFO, RIVETS = {}, {}
for s in SIDES:
    w = {"Body": 0.6, "Arm" + SN[s]: 0.4}

    def frm(tilt):
        t = math.radians(tilt)
        ax = mirror((-math.cos(t), 0, -math.sin(t)), s)   # outward (down the slope)
        az = mirror((-math.sin(t), 0, math.cos(t)), s)    # face normal of the top facet (up, tilted outward)
        return np.stack([ax, np.array([0.0, 1.0, 0.0]), az], 1)

    def roof(hx, hy, hz, c_top, c_fb, c_out, c_vert):
        # chamfers: top-outer, top-front/back, outer-front/back (vertical), top-inner
        return [((2, 1), (0, 1), c_out), ((2, 1), (1, -1), c_fb), ((2, 1), (1, 1), c_fb), ((0, 1), (1, -1), c_vert),
                ((0, 1), (1, 1), c_vert), ((2, 1), (0, -1), c_top)]
    layers = []
    for (cx, cy, cz), tilt, half, cham, t_, zcut in (
            ((-0.142, 0.016, 0.657), 15, (0.049, 0.082, 0.046), roof(0.049, 0.082, 0.046, 0.012, 0.030, 0.028, 0.024), 0.011,
             -0.022),
            ((-0.168, 0.016, 0.613), 45, (0.024, 0.076, 0.028), roof(0.024, 0.076, 0.028, 0.006, 0.022, 0.016, 0.018), 0.010,
             -0.010),
            ((-0.173, 0.016, 0.588), 62, (0.020, 0.070, 0.022), roof(0.020, 0.070, 0.022, 0.005, 0.019, 0.013, 0.016), 0.009,
             -0.008)):
        R_ = frm(tilt)
        c_ = mirror((cx, cy, cz), s)
        h_ = np.array(half)
        solid = chamfer_box(c_, R_, h_, 0.003, cham, k=0.003)
        # solid plate above its cut plane (a hollow shell would leave dark voids over the shoulder for the bake)
        shell = I(solid, Half(-R_[:, 2], c_ + R_[:, 2] * zcut), k=0.002)
        layers.append((c_, R_, h_, solid, shell, zcut))
    pad_geo = U(*[L[4] for L in layers])
    RIVETS[s] = []
    for x, z, side in ((0.128, 0.662, -1), (0.163, 0.625, -1), (0.118, 0.660, 1), (0.160, 0.624, 1)):
        try:
            p = trace(pad_geo.d, (s * x, side * 0.4, z), (0, -side, 0))
        except RuntimeError:
            continue
        nn = normal_at(pad_geo, p)
        RIVETS[s].append((p, nn))
    riv = [Ell(p + nn * 0.0006, (0.0060, 0.0060, 0.0034), frame(nn)) for p, nn in RIVETS[s]]
    PAD_INFO[s] = [(c_, R_, h_, zc) for c_, R_, h_, _, _, zc in layers]
    part("pad_" + SN[s], U(pad_geo, *riv, k=0.0015), "gold", ("fixed", w))

# ---------------- bracers (red band, gold rims, stepped plates, cyan light) ----------------
GEM_INFO = {}
for s in SIDES:
    a = "Arm" + SN[s]
    dv = nrm(WR[s] - EL[s])
    part("bracer_" + SN[s], RCyl(EL[s] + dv * 0.058, dv, 0.047, 0.040, 0.006, taper=-0.002), "red", (a,))
    part("bracer_rims_" + SN[s], U(RCyl(EL[s] + dv * 0.013, dv, 0.0530, 0.0095, 0.0045),
                                   RCyl(EL[s] + dv * 0.100, dv, 0.0505, 0.0085, 0.0040)), "gold", (a,))
    o = mirror((-1, 0, 0), s)
    o = nrm(o - dv * np.dot(o, dv))
    fwd = nrm(np.cross(dv, o) * (-1 if s < 0 else 1))
    if fwd[1] > 0:
        fwd = -fwd
    Rb = np.stack([o, fwd, dv], 1)
    steps = U(RBox(EL[s] + dv * 0.032 + o * 0.047, (0.006, 0.026, 0.0075), 0.003, Rb),
              RBox(EL[s] + dv * 0.049 + o * 0.046, (0.006, 0.024, 0.0065), 0.003, Rb))
    part("bracer_steps_" + SN[s], steps, "gold", (a,))
    gp = EL[s] + dv * 0.078 + nrm(o * 0.8 + fwd * 0.6) * 0.046
    Rg = np.stack([nrm(o * 0.8 + fwd * 0.6), nrm(np.cross(dv, nrm(o * 0.8 + fwd * 0.6))), dv], 1)
    GEM_INFO[s] = (gp, Rg, np.array([0.007, 0.011, 0.014]))
    part("bracer_gem_" + SN[s], RBox(gp, (0.007, 0.011, 0.014), 0.004, Rg), "graphite_d", (a,))

# ---------------- belt (band, boxes with lights, buckle), pouches, strap ----------------
BELT_Z = 0.4615
BELT = I(Off(TORSO, 0.013), Slab((0, 0, 1), (0, 0, BELT_Z), 0.0155), k=0.004)
part("belt", BELT, "belt", ("Root",))
BOXES, BOX_INFO = [], []
for az, hw, light in ((-38, 0.0165, True), (38, 0.0165, True), (-64, 0.011, True), (64, 0.011, True), (-128, 0.016, True),
                      (128, 0.016, True), (-152, 0.013, True), (152, 0.013, True)):
    p = radial_pt(BELT, BELT_Z, az)
    nn = normal_at(BELT, p)
    nn = nrm(nn - np.array([0, 0, nn[2]]))
    Rb = frame(nn, (0, 0, 1))
    BOXES.append(RBox(p + nn * 0.004, (hw, 0.0255, 0.010), 0.004, Rb))
    BOX_INFO.append((p + nn * 0.004, Rb, np.array([hw, 0.0255, 0.010]), light))
pb = radial_pt(BELT, BELT_Z, 180)
nb = normal_at(BELT, pb)
BOXES.append(RBox(pb + nb * 0.006, (0.026, 0.021, 0.011), 0.006, frame(nb, (0, 0, 1))))
BOX_INFO.append((pb + nb * 0.006, frame(nb, (0, 0, 1)), np.array([0.026, 0.021, 0.011]), False))
part("belt_boxes", U(*BOXES), "graphite_d", ("Root",))
pf = radial_pt(BELT, BELT_Z, 0)
nf = normal_at(BELT, pf)
BUCKLE = (pf + nf * 0.004, frame(nf, (0, 0, 1)), np.array([0.026, 0.0155, 0.007]))
part("buckle", RBox(BUCKLE[0], BUCKLE[2], 0.0045, BUCKLE[1]), "graphite_d", ("Root",))
for s in SIDES:
    lg = "Leg" + SN[s]
    p = radial_pt(LEG[s], 0.390, s * 92, c=(s * 0.07, 0, 0))
    nn = normal_at(LEG[s], p)
    Rp = frame(nn, (0, 0, 1))
    part("pouch_" + SN[s], U(RBox(p + nn * 0.010, (0.032, 0.036, 0.011), 0.007, Rp),
                             RBox(p + nn * 0.020 + Rp[:, 1] * 0.028, (0.033, 0.009, 0.004), 0.003, Rp)), "graphite_d", (lg,))
part("strap", I(Off(LEG[-1], 0.0045), Slab(nrm((-0.20, 0, 1)), (-0.085, 0, 0.343), 0.0068), k=0.002), "graphite_d", ("LegR",))

# ---------------- knee pads (gold, octagonal domes) ----------------
for s in SIDES:
    cx, cz = s * 0.100, 0.252
    octo = [(cx - 0.018, cz + 0.049), (cx + 0.018, cz + 0.049), (cx + 0.037, cz + 0.023), (cx + 0.037, cz - 0.023),
            (cx + 0.018, cz - 0.049), (cx - 0.018, cz - 0.049), (cx - 0.037, cz - 0.023), (cx - 0.037, cz + 0.023)]
    part("knee_" + SN[s], I(Ell((cx, -0.030, cz), (0.047, 0.037, 0.060)), Poly2(octo, "xz"), Half((0, 1, 0), (0, -0.015, 0)),
                            k=0.004), "gold", ("Leg" + SN[s],))

# ---------------- boots: shaft, toe cap, heel, gold cuff / instep strap / sole, glowing underside, ankle discs -------
BOOT, DISC_INFO, SHAFT = {}, {}, {}
for s in SIDES:
    lg = "Leg" + SN[s]
    rows = [  # z, a, cx, yf, yb, n  (right boot; mirrored by cx)
        (0.075, 0.066, -0.120, -0.060, 0.088, 2.8),
        (0.100, 0.068, -0.118, -0.052, 0.088, 2.8),
        (0.120, 0.066, -0.116, -0.046, 0.086, 2.8),
        (0.140, 0.064, -0.114, -0.043, 0.083, 2.8),
        (0.160, 0.062, -0.110, -0.044, 0.080, 2.8),
        (0.180, 0.061, -0.107, -0.046, 0.078, 2.8),
        (0.205, 0.055, -0.101, -0.046, 0.075, 2.7),
        (0.226, 0.050, -0.099, -0.043, 0.071, 2.5),
    ]
    rows = [(z, a, s * abs(cx), yf, yb, n) for (z, a, cx, yf, yb, n) in rows]
    shaft = Loft(smooth_rows(rows, 40), bone=lg)
    SHAFT[s] = shaft
    toe = Ell(mirror((-0.137, -0.062, 0.058), s), (0.072, 0.070, 0.047), bone=lg)
    heel = Ell(mirror((-0.129, 0.044, 0.062), s), (0.062, 0.048, 0.052), bone=lg)
    foot = U(toe, heel, k=0.03)
    BOOT[s] = I(U(shaft, foot, k=0.03), Half((0, 0, -1), (0, 0, 0.030)))
    part("boot_" + SN[s], BOOT[s], "red", (lg,))
    cxs = s * 0.104
    cuff_reg = Poly2([(cxs - 0.09, 0.176), (cxs + 0.09, 0.176), (cxs + 0.09, 0.214), (cxs + 0.034, 0.217),
                      (cxs + 0.022, 0.227), (cxs - 0.022, 0.227), (cxs - 0.034, 0.217), (cxs - 0.09, 0.214)], "xz")
    part("cuff_" + SN[s], I(Off(shaft, 0.0065), cuff_reg, k=0.004), "gold", (lg,))
    part("instep_" + SN[s], I(Off(U(shaft, foot, k=0.03), 0.0045), Slab(nrm((0, 0.62, 1)), mirror((-0.13, -0.058, 0.080), s), 0.0085),
                              Half((0, 1, 0), (0, -0.018, 0)), k=0.003), "gold", (lg,))
    sole2 = ExZ(foot, 0.045, 0.011, 0.034)
    part("sole_" + SN[s], I(Off(sole2, 0.0055), Half((0, 0, 1), (0, 0, 0.034)), k=0.004), "gold", (lg,))
    part("sole_glow_" + SN[s], ExZ(Off(foot, -0.004), 0.045, 0.0, 0.0125), "glow", (lg,))
    discs = []
    DISC_INFO[s] = []
    for side in (-1, 1):
        p = trace(shaft.d, (cxs + side * 0.3, 0.026, 0.108), (-side, 0, 0))
        nn = normal_at(shaft, p)
        discs.append(RCyl(p + nn * 0.002, nn, 0.0245, 0.0062, 0.0030))
        DISC_INFO[s].append((p + nn * 0.002, nn))
    part("discs_" + SN[s], U(*discs), "gold", (lg,))

log("model defined:", len(PARTS), "parts")


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
# shape stage: silhouettes + quick previews
# =====================================================================================================================
def key_colours(P):
    own, _ = fig_owner(P)
    keys = [PARTS[i].key for i in range(len(PARTS))]
    cols = np.array([hexc(PAL[k][0]) for k in keys])
    return cols[own]


def quick_preview(ob, path, views=(("front", 0, 4), ("34", 40, 10), ("side", 90, 4), ("back", 180, 4), ("game", 0, 55))):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "VERTEX"
    sc.display.shading.show_cavity = True
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.render.resolution_x = sc.render.resolution_y = 640
    cam = bpy.data.objects.get("PrevCam") or bpy.data.objects.new("PrevCam", bpy.data.cameras.new("PrevCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.12
    sc.camera = cam
    panels = []
    for name, yaw, pitch in views:
        yr, pr = math.radians(yaw), math.radians(pitch)
        t = Vector((0, 0, 0.5))
        dv = Vector((math.sin(yr) * math.cos(pr), -math.cos(yr) * math.cos(pr), math.sin(pr)))
        cam.location = t + dv * 4
        cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        fp = os.path.join(PREV_DIR, f"_q_{name}.png")
        sc.render.filepath = fp
        bpy.ops.render.render(write_still=True)
        panels.append(TS.load_rgba(fp)[..., :3])
        os.remove(fp)
    TS.save_rgb(np.concatenate(panels, 1), path)
    log("preview", path)


CLOSEUPS = [  # name, target, yaw, pitch, ortho scale (camera from yaw: 0 = front, 90 = character's left side)
    ("pad", (-0.150, 0.0, 0.640), -35, 15, 0.20),
    ("hand", (-0.258, -0.010, 0.372), -10, 5, 0.17),
    ("crown", (0.0, 0.010, 0.920), 0, 80, 0.30),
    ("chest", (0.0, -0.070, 0.600), 50, 0, 0.25),
]


def closeups(objs, path, color_type="VERTEX", engine="WORKBENCH", size=480):
    """Close-up panels (pad, hand, crown, chest) of the given objects -> one PNG row."""
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = color_type
    sc.display.shading.show_cavity = False
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.render.resolution_x = sc.render.resolution_y = size
    cam = bpy.data.objects.get("CloseCam") or bpy.data.objects.new("CloseCam", bpy.data.cameras.new("CloseCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    sc.camera = cam
    keep = set(objs)
    hid = [o for o in sc.objects if o.type == "MESH" and o not in keep and not o.hide_render]
    for o in hid:
        o.hide_render = True
    panels = []
    for name, tgt, yaw, pitch, sca in CLOSEUPS:
        yr, pr = math.radians(yaw), math.radians(pitch)
        t = Vector(tgt)
        dv = Vector((math.sin(yr) * math.cos(pr), -math.cos(yr) * math.cos(pr), math.sin(pr)))
        cam.location = t + dv * 3
        cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = sca
        fp = os.path.join(PREV_DIR, f"_c_{name}.png")
        sc.render.filepath = fp
        bpy.ops.render.render(write_still=True)
        panels.append(TS.load_rgba(fp)[..., :3])
        os.remove(fp)
    for o in hid:
        o.hide_render = False
    TS.save_rgb(np.concatenate(panels, 1), path)
    log("closeups", path)


def shape_stage():
    V, T = mesh_field(fig_d, 0.003)
    ob = to_mesh("TheoHi", V, T)
    col = key_colours(V.astype(F32))
    attr = ob.data.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.hstack([col ** 2.2, np.ones((len(V), 1))]).astype(np.float32).ravel())
    for p in ob.data.polygons:
        p.use_smooth = True
    lo, hi = V.min(0), V.max(0)
    log("bounds", np.round(lo, 3), np.round(hi, 3), "height", round(hi[2] - lo[2], 4))
    views = TS.ref_views()
    masks = TS.render_masks(views, [ob])
    TS.report(views, masks, os.path.join(PREV_DIR, "v3_overlay_dev.png"), "v3 shape")
    TS.compare_sheet(views, TS.render_shaded(views, [ob]), os.path.join(PREV_DIR, "v3_compare_dev.png"))
    quick_preview(ob, os.path.join(PREV_DIR, "v3_quick.png"))
    closeups([ob], os.path.join(PREV_DIR, "v3_closeups_dev.png"))


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


# ---- suit seams (orange piping), traced on the suit surface ----
def _arm_seam(s):
    d = nrm(EL[s] - SH[s])
    o = nrm(mirror((-abs(d[2]), 0.0, abs(d[0])), s))
    pts = [SH[s] + (EL[s] - SH[s]) * t + o * 0.06 + np.array([0, -0.012, 0]) for t in np.linspace(0.40, 1.0, 7)]
    return project(SUIT.d, np.array(pts), it=8)


SEAMS = []
for s in SIDES:
    SEAMS.append(project(SUIT.d, np.array([front_pt(SUIT, s * x, z) for x, z in (
        (0.0505, 0.553), (0.0505, 0.474), (0.051, 0.440), (0.053, 0.410), (0.057, 0.380), (0.062, 0.350), (0.068, 0.325),
        (0.074, 0.305), (0.080, 0.293))]), it=4))
    SEAMS.append(_arm_seam(s))
    SEAMS.append(np.array([radial_pt(SUIT, z, s * 72) for z in (0.540, 0.520, 0.500, 0.478)]))


def paint_suit(P, N, occ):
    c = shade("graphite", N, occ, 0.06)
    g = np.full(len(P), gloss_of("graphite"))
    for C in SEAMS:
        c, g = glow_line(c, g, seg3(P, C), 0.0030)
    # knee / ankle rings of the suit (slightly darker bands under the pads)
    return c, g


def paint_hand(s):
    kn, dn, out, fw = FINGER_INFO[s]

    def f(P, N, occ):
        c = shade("graphite", N, occ, 0.10)
        ld = (P - kn) @ dn
        c = stroke(c, line_w(np.abs(ld - 0.0005), 0.0012) * 0.35 * sstep(0.0, 0.5, (P - kn) @ out / 0.012), H("#1A1715"))
        return c, np.full(len(P), 0.22)
    return f


def paint_gold(P, N, occ):
    return shade("gold", N, occ), np.full(len(P), gloss_of("gold"))


def paint_red(P, N, occ):
    return shade("red", N, occ, 0.60), np.full(len(P), gloss_of("red"))


def paint_flat(key, spec=0.0):
    def f(P, N, occ):
        return shade(key, N, occ, spec), np.full(len(P), gloss_of(key))
    return f


# ---- head: eyes, mouth, blush, freckles ----
EYE_C = {s: np.array([s * 0.0475, 0.8035]) for s in SIDES}
RNG = np.random.default_rng(11)
FRECKLES = []
for s in SIDES:
    for _ in range(15):
        FRECKLES.append((s * RNG.uniform(0.024, 0.074), RNG.uniform(0.762, 0.790), RNG.uniform(0.0010, 0.0016)))
for _ in range(6):
    FRECKLES.append((RNG.uniform(-0.016, 0.016), RNG.uniform(0.782, 0.796), RNG.uniform(0.0009, 0.0013)))
FRECKLES = np.array(FRECKLES)


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


def paint_head(P, N, occ):
    c = shade("skin", N, occ, 0.10)
    g = np.full(len(P), gloss_of("skin"))
    x, z = P[:, 0], P[:, 2]
    front = sstep(0.1, 0.5, -N[:, 1])
    # blush, ears
    for s in SIDES:
        b = np.exp(-(((x - s * 0.060) / 0.020) ** 2 + ((z - 0.765) / 0.014) ** 2)) * front
        c = stroke(c, b * 0.45, H("#F2998A"))
        e = np.exp(-(((x - s * 0.114) / 0.012) ** 2 + ((z - 0.781) / 0.022) ** 2)) * sstep(0.2, 0.7, s * N[:, 0])
        c = stroke(c, e * 0.35, H("#E99582"))
    nose = np.exp(-((x / 0.013) ** 2 + ((z - 0.772) / 0.010) ** 2)) * front
    c = stroke(c, nose * 0.22, H("#F2A08C"))
    # freckles
    for fx, fz, fr in FRECKLES:
        r = np.sqrt((x - fx) ** 2 + (z - fz) ** 2)
        c = stroke(c, line_w(r, fr, 0.0005) * front * 0.55, H("#C47E5E"))
    # mouth: soft smile, the character's left corner (+X) lifted
    u = x
    vm = 0.7405 - 0.0032 * (1 - (u / 0.031) ** 2) + 0.0034 * sstep(0.0, 0.034, u) ** 2
    dm = np.abs(z - vm)
    half = 0.00175 * np.clip(1 - (np.abs(u + 0.002) / 0.035) ** 4, 0, 1)
    wm = line_w(dm, half, 0.0005) * (np.abs(u) < 0.036) * front
    c = stroke(c, wm, H("#9A4A3C"))
    lip = np.exp(-((u / 0.018) ** 2 + ((z - 0.733) / 0.004) ** 2)) * front
    c = stroke(c, lip * 0.18, H("#E7958A"))
    # brows (painted strokes; the thin brow ribbons only add relief)
    for s_ in SIDES:
        c = brow_stroke(c, x, z, s_, front)
    # eyes (the left eye +X sits under the visor; it is painted on the visor too)
    s = -1
    c, g = eye_paint(c, g, x - EYE_C[s][0], z - EYE_C[s][1], s)
    s = 1
    c, g = eye_paint(c, g, x - EYE_C[s][0], z - EYE_C[s][1], s)
    return c, g


def brow_stroke(c, x, z, s, front):
    V = np.array(BROWS[s], float)
    d, f = seg2(x, z, V)
    inner = f if s > 0 else 1 - f            # 0 at the inner end (near the nose)
    half = 0.0068 * (1 - 0.45 * inner ** 1.5) + 0.0006
    w = line_w(d, half, 0.0006) * front
    col = mixc(H("#8C5E22"), H("#C99848"), sstep(half, 0.0, d) * 0.8)
    return stroke(c, w, col)


def paint_brow(P, N, occ):
    x, z = P[:, 0], P[:, 2]
    c = shade("skin", N, occ, 0.10)
    for s_ in SIDES:
        c = brow_stroke(c, x, z, s_, np.ones(len(P)))
    return c, np.full(len(P), 0.15)


VIS_TINT = dict(white=H("#FFC983"), iris_out=H("#B04A12"), iris_in=H("#E8781E"), pupil=H("#7A2C0C"), spark=H("#FFF4DE"),
                lash=H("#8E3A10"))


def paint_visor(P, N, occ):
    x, z = P[:, 0], P[:, 2]
    base = mixc(H("#F57A16"), H("#FFB04A"), sstep(0.776, 0.834, z))
    lam = np.clip(N @ KEY_L, 0, 1)
    c = base * (0.86 + 0.18 * lam)[:, None]
    g = np.full(len(P), 1.0)
    c, g = eye_paint(c, g, x - EYE_C[1][0], z - EYE_C[1][1], 1, VIS_TINT)
    dr = seg_dist2(x.astype(F32), z.astype(F32), np.asarray(VIS_V, F32), True)
    c = stroke(c, line_w(dr, 0.0036, 0.0008), H("#FFD68E"))
    c = stroke(c, line_w(np.abs(dr - 0.0050), 0.0007, 0.0004) * 0.6, H("#C8561A"))
    streak = line_w(np.abs((x - 0.040) * 0.8 - (z - 0.825) * 0.6), 0.0035, 0.0015) * (z > 0.80)
    c = stroke(c, streak * 0.45, H("#FFF3D6"))
    return np.clip(c, 0, 1), g


def paint_headset(P, N, occ):
    c = shade("headset", N, occ, 0.3)
    return c, np.full(len(P), gloss_of("headset"))


def paint_hinge(P, N, occ):
    c = shade("headset", N, occ, 0.3)
    r = np.sqrt((P[:, 1] + 0.038) ** 2 + (P[:, 2] - 0.807) ** 2)
    m = line_w(r, 0.0062, 0.0006) * sstep(0.3, 0.7, N[:, 0])
    c = stroke(c, m, shade("gold", N, occ))
    return c, np.where(m > 0.5, 0.88, gloss_of("headset"))


def paint_hair(P, N, occ):
    n = len(P)
    best = np.full(n, BIG, F32)
    sp, xw = np.zeros(n, F32), np.zeros(n, F32)
    for L in LOCKS:
        m = _inbb(P, L.lo, L.hi, 0.006)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        d, s_, x_, _ = L.local(P[idx].astype(F32))
        b = d < best[idx]
        j = idx[b]
        best[j], sp[j], xw[j] = d[b], s_[b], x_[b]
    on = sstep(0.0065, 0.0015, best)
    lam = np.clip(N @ KEY_L, -1, 1)
    light = sstep(-0.85, 0.90, 0.6 * lam + 0.4 * N[:, 2])
    ridge = 1 - np.clip(np.abs(xw), 0, 1) ** 2
    edge = sstep(0.62, 1.0, np.abs(xw))  # lock edges read as creases where the locks overlap
    v = (0.10 + 0.50 * light + 0.16 * sstep(0.2, 1.0, sp) + 0.14 * ridge - 0.22 * edge
         - 0.07 * line_w(np.abs(np.abs(xw) - 0.45), 0.06, 0.04))
    v = v * on + (0.18 + 0.50 * light) * (1 - on)   # scalp between the locks: close to the lock colour (no dark patches)
    c = ramp("hair", v)
    sp_ = np.clip(N @ HALF_V, 0, 1) ** 18 * 0.20 * (1 - edge)
    c = c + (1 - c) * sp_[:, None]
    c = c * (1 - 0.60 * np.minimum(occ, 0.60))[:, None]
    return np.clip(c, 0, 1), np.full(n, 0.35)


# ---- vest: chest plate, abdomen tongue, back plate ----
PANEL_F = [[(s * 0.094, 0.652), (s * 0.097, 0.606), (s * 0.089, 0.584), (s * 0.071, 0.569), (s * 0.052, 0.564)] for s in SIDES]
PANEL_B = [[(s * 0.030, 0.560), (s * 0.068, 0.574), (s * 0.090, 0.602)] for s in SIDES] + \
          [[(s * 0.040, 0.652), (s * 0.066, 0.660), (s * 0.095, 0.662)] for s in SIDES]
VENTS = []
for s in SIDES:
    for (cx, cz) in ((0.073, 0.582), (0.080, 0.571)):
        a = math.radians(-35 * s)
        VENTS.append([(s * cx - 0.0065 * math.cos(a), cz - 0.0065 * math.sin(a)), (s * cx + 0.0065 * math.cos(a), cz + 0.0065 * math.sin(a))])
    for (cx, cz) in ((0.058, 0.6835), (0.068, 0.6775)):
        a = math.radians(45 * s)
        VENTS.append([(s * cx - 0.005 * math.cos(a), cz - 0.005 * math.sin(a)), (s * cx + 0.005 * math.cos(a), cz + 0.005 * math.sin(a))])
RIVETS_F = [(s * 0.068, 0.651) for s in SIDES]
EMB = np.array([0.0, 0.615])


def emblem(c, g, x, z, front):
    u, v = x - EMB[0], z - EMB[1]
    r = np.sqrt(u * u + v * v)
    gl, gd, gm = H("#FFE59A"), H("#A86E1C"), H("#E9B44A")
    # thin circle
    c = stroke(c, line_w(np.abs(r - 0.0215), 0.0017, 0.0005) * front, gm)
    # orbit ellipse (rotated 14 deg); back half hidden behind the ball
    a = math.radians(14)
    eu, ev = u * math.cos(a) + v * math.sin(a), -u * math.sin(a) + v * math.cos(a)
    er = np.sqrt((eu / 0.047) ** 2 + (ev / 0.0115) ** 2)
    don = np.abs(er - 1) * 0.0115
    orbit = line_w(don, 0.0027, 0.0006) * front
    behind = (ev > 0) & (r < 0.0128)
    ocol = mixc(H("#F07A1C"), H("#FFC067"), sstep(-0.01, 0.012, ev))
    # comet tail strokes (tapered) toward the upper right
    for (x0, z0, x1, z1, w0) in ((0.006, 0.622, 0.034, 0.644, 0.0046), (0.010, 0.617, 0.041, 0.635, 0.0040),
                                 (0.003, 0.626, 0.024, 0.649, 0.0034)):
        d, f = seg2(u, v, [(x0, z0 - EMB[1]), (x1, z1 - EMB[1])])
        w = w0 * (1 - f) + 0.0006
        c = stroke(c, line_w(d, w, 0.0005) * front, mixc(gm, gl, f))
    c = stroke(c, orbit * behind, ocol)
    # ball with its own shading
    ball = line_w(r, 0.0118, 0.0005) * front
    sh = sstep(-0.012, 0.012, -u * 0.6 + v * 0.8)
    c = stroke(c, ball, mixc(gd, gl, sh))
    c = stroke(c, line_w(np.sqrt((u + 0.004) ** 2 + (v - 0.0045) ** 2), 0.0028, 0.0010) * front, H("#FFF8E0"))
    c = stroke(c, orbit * ~behind, ocol)
    g = np.maximum(g, np.maximum(ball, orbit) * 0.9)
    return c, g


def paint_vest(P, N, occ):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    c = shade("red", N, occ, 0.60)
    g = np.full(len(P), gloss_of("red"))
    front = y < 0.012
    fm = front.astype(float)
    gold = shade("gold", N, occ)
    d_arch = seg2(x, z, ARCH)[0]
    w = line_w(d_arch, 0.0046) * fm
    d_yoke = seg2(x, z, YOKE)[0]
    w = np.maximum(w, line_w(d_yoke, 0.0045) * fm)
    d_back = seg2(x, z, BACK_TRIM)[0]
    w = np.maximum(w, line_w(d_back, 0.0046) * (1 - fm))
    pl = np.zeros(len(P))
    for V in PANEL_F:
        pl = np.maximum(pl, line_w(seg2(x, z, V)[0], 0.0010) * fm)
    for V in PANEL_B:
        pl = np.maximum(pl, line_w(seg2(x, z, V)[0], 0.0010) * (1 - fm))
    for V in VENTS:
        pl = np.maximum(pl, line_w(seg2(x, z, V)[0], 0.0021) * fm)
    c = stroke(c, pl * 0.85, H("#4A060B"))
    # tongue notch (graphite recess at its top)
    notch = (np.abs(x) < 0.033 - (0.552 - z) * 0.35) & (z > 0.536) & (z < 0.551) & front
    c = np.where(notch[:, None], shade("graphite_d", N, occ), c)
    for rx, rz in RIVETS_F:
        r = np.sqrt((x - rx) ** 2 + (z - rz) ** 2)
        c = stroke(c, line_w(r, 0.0068) * fm, H("#4A060B"))
        mcol = mixc(H("#8E8882"), H("#F4F0EC"), sstep(-0.004, 0.004, (z - rz) - (x - rx) * 0.5))
        c = stroke(c, line_w(r, 0.0054) * fm, mcol)
    c = stroke(c, w, gold)
    g = np.where(w > 0.5, gloss_of("gold"), g)
    c, g = emblem(c, g, x, z, fm)
    return np.clip(c, 0, 1), g


def paint_collar(P, N, occ):
    c = shade("red", N, occ, 0.45)
    w = sstep(0.6900, 0.6920, P[:, 2])
    c = stroke(c, w, shade("gold", N, occ))
    c = stroke(c, line_w(np.abs(P[:, 2] - 0.6900), 0.0009) * 0.7, H("#4A060B"))
    return c, np.where(w > 0.5, 0.88, 0.75)


def paint_spine(P, N, occ):
    x, z = P[:, 0], P[:, 2]
    c = shade("red", N, occ, 0.45)
    gold = shade("gold", N, occ)
    cap = sstep(0.631, 0.633, z)
    slot = (np.abs(x) < 0.019) & (z > 0.553) & (z < 0.574)
    inner = (np.abs(x) < 0.0125) & (z > 0.5575) & (z < 0.569)
    c = stroke(c, cap, gold)
    c = np.where(slot[:, None], gold, c)
    c = np.where(inner[:, None], shade("graphite_d", N, occ), c)
    return c, np.where((cap > 0.5) | slot, 0.88, 0.75)


def box_local(P, info):
    c0, R, h = info
    return (P - c0) @ np.asarray(R, float), h


def paint_boxes(P, N, occ):
    c = shade("graphite_d", N, occ, 0.15)
    g = np.full(len(P), 0.22)
    best = np.full(len(P), 1e9)
    Lb = np.zeros((len(P), 3))
    which = np.full(len(P), -1)
    for i, (c0, R, h, light) in enumerate(BOX_INFO):
        L = (P - c0) @ np.asarray(R, float)
        dd = np.max(np.abs(L) / h, axis=1)
        b = dd < best
        best, Lb, which = np.where(b, dd, best), np.where(b[:, None], L, Lb), np.where(b, i, which)
    for i, (c0, R, h, light) in enumerate(BOX_INFO):
        m = which == i
        if not m.any():
            continue
        L = Lb[m]
        face = sstep(h[2] - 0.004, h[2] - 0.001, L[:, 2])
        if light:
            win = line_w(np.maximum(np.abs(L[:, 0]) - (h[0] - 0.0075), np.abs(L[:, 1]) - 0.0150) * 1.0, 0.0, 0.0006) * face
            frame_ = line_w(np.maximum(np.abs(L[:, 0]) - (h[0] - 0.0055), np.abs(L[:, 1]) - 0.0170), 0.0, 0.0005) * face
            core = np.exp(-((L[:, 0] / (h[0] * 0.5)) ** 2 + (L[:, 1] / 0.010) ** 2))
            cc = c[m]
            cc = stroke(cc, frame_, H("#1A1614"))
            cc = stroke(cc, win, mixc(GLOW_EDGE, mixc(GLOW, GLOW_HOT, core * 0.8), 0.6 + 0.4 * core))
            c[m] = cc
            g[m] = np.maximum(g[m], win)
        else:
            flap = line_w(np.abs(L[:, 1] - 0.006), 0.0009) * face
            snap = line_w(np.sqrt(L[:, 0] ** 2 + (L[:, 1] - 0.0115) ** 2), 0.0035) * face
            cc = stroke(c[m], flap * 0.8, H("#1A1614"))
            cc = stroke(cc, snap, H("#E0A93A"))
            c[m] = cc
    return np.clip(c, 0, 1), g


def paint_buckle(P, N, occ):
    c = shade("graphite_d", N, occ, 0.2)
    L, h = box_local(P, BUCKLE)
    face = sstep(h[2] - 0.0035, h[2] - 0.001, L[:, 2])
    red = line_w(np.maximum(np.abs(L[:, 0]) - 0.0135, np.abs(L[:, 1]) - 0.0075), 0.0, 0.0006) * face
    c = stroke(c, red, shade("red", N, occ, 0.5))
    return c, np.where(red > 0.5, 0.75, 0.22)


def paint_pouch(P, N, occ):
    c = shade("graphite_d", N, occ, 0.15)
    return c, np.full(len(P), 0.2)


def paint_belt(P, N, occ):
    c = shade("belt", N, occ, 0.1)
    z = P[:, 2]
    for zz in (BELT_Z - 0.0115, BELT_Z + 0.0115):
        c = stroke(c, line_w(np.abs(z - zz), 0.0007) * 0.5, H("#2A2421"))
    return c, np.full(len(P), 0.25)


def paint_pad(s):
    layers = PAD_INFO[s]
    rivs = RIVETS[s]

    def f(P, N, occ):
        c = shade("gold", N, occ)
        g = np.full(len(P), 0.88)
        c_, R_, h_, zc = layers[0]   # top plate: inset groove 10 mm inside its outline (front / back / outer facets)
        L = (P - c_) @ np.asarray(R_, float)
        q = np.maximum(np.abs(L[:, 0]) / (h_[0] - 0.010), np.abs(L[:, 1]) / (h_[1] - 0.010))
        on_top = (np.max(np.abs(L) / (h_ + 0.004), axis=1) < 1.0) & (L[:, 2] > zc + 0.012)
        grv = line_w(np.abs(q - 1.0) * min(h_[0], h_[1]), 0.0010) * on_top
        c = stroke(c, grv * 0.80, H("#5E3C0E"))
        c = stroke(c, line_w(np.abs(q - 1.0) * min(h_[0], h_[1]) - 0.0022, 0.0008) * on_top * 0.6, H("#FFE7A0"))
        for (c_, R_, h_, zc) in layers[1:]:  # orange piping around the two lower lames, near their open lower edge
            L = (P - c_) @ np.asarray(R_, float)
            inside = np.max(np.abs(L) / (h_ + 0.004), axis=1) < 1.0
            c, g = glow_line(c, g, np.where(inside, np.abs(L[:, 2] - (zc + 0.0075)), 1.0), 0.0015)
        for p, nn in rivs:
            r = np.linalg.norm(P - p, axis=1)
            c = stroke(c, line_w(np.abs(r - 0.0068), 0.0010), H("#5E3C0E"))
            c = stroke(c, line_w(r, 0.0058), mixc(H("#B07A22"), H("#FFF0B8"), sstep(-0.004, 0.004, P[:, 2] - p[2])))
        return np.clip(c, 0, 1), g
    return f


def paint_gem(s):
    info = GEM_INFO[s]

    def f(P, N, occ):
        c = shade("graphite_d", N, occ, 0.2)
        L, h = box_local(P, info)
        face = sstep(h[0] - 0.003, h[0] - 0.001, L[:, 0])
        win = line_w(np.maximum(np.abs(L[:, 1]) - 0.0075, np.abs(L[:, 2]) - 0.0105), 0.0, 0.0005) * face
        core = np.exp(-((L[:, 1] / 0.006) ** 2 + (L[:, 2] / 0.008) ** 2))
        c = stroke(c, win, mixc(H("#2FA7B0"), H("#C8FFFF"), core * 0.7))
        return c, np.maximum(win, 0.2)
    return f


def paint_knee(s):
    cx, cz = s * 0.100, 0.252
    octo = np.array([(cx - 0.018, cz + 0.049), (cx + 0.018, cz + 0.049), (cx + 0.037, cz + 0.023), (cx + 0.037, cz - 0.023),
                     (cx + 0.018, cz - 0.049), (cx - 0.018, cz - 0.049), (cx - 0.037, cz - 0.023), (cx - 0.037, cz + 0.023)], F32)

    def f(P, N, occ):
        c = shade("gold", N, occ)
        d = seg_dist2(P[:, 0].astype(F32), P[:, 2].astype(F32), octo, True)
        c = stroke(c, line_w(np.abs(d - 0.0075), 0.0010) * 0.8, H("#7A4E14"))
        return c, np.full(len(P), 0.88)
    return f


def paint_cuff(P, N, occ):
    c = shade("gold", N, occ)
    c = stroke(c, line_w(np.abs(P[:, 2] - 0.1985), 0.0008) * 0.6, H("#7A4E14"))
    return c, np.full(len(P), 0.88)


def paint_sole(P, N, occ):
    c = shade("gold", N, occ)
    c = stroke(c, line_w(np.abs(P[:, 2] - 0.0215), 0.0008) * 0.6, H("#6E4612"))
    return c, np.full(len(P), 0.88)


def paint_glow(P, N, occ):
    c = mixc(GLOW, GLOW_HOT, 0.45 + 0.4 * sstep(0.0, 0.012, P[:, 2]))
    return c, np.ones(len(P))


def paint_discs(s):
    info = DISC_INFO[s]

    def f(P, N, occ):
        c = shade("gold", N, occ)
        g = np.full(len(P), 0.88)
        best = np.full(len(P), 1e9)
        for p0, n0 in info:
            D = P - p0
            rr = np.linalg.norm(D - np.outer(D @ n0, n0), axis=1)
            best = np.minimum(best, np.where(np.abs(D @ n0) < 0.02, rr, 1e9))
        core = 1 - sstep(0.0, 0.0125, best)
        hot = line_w(best, 0.0125, 0.0006)
        c = stroke(c, line_w(np.abs(best - 0.0133), 0.0010) * 0.8, H("#6E4612"))
        c = stroke(c, hot, mixc(GLOW, GLOW_HOT, core))
        return c, np.maximum(g, hot)
    return f


def paint_boot(P, N, occ):
    return shade("red", N, occ, 0.60), np.full(len(P), 0.75)


PARTS_BY = {p.name: p for p in PARTS}


def assign_paints():
    for p in PARTS:
        n = p.name
        f = None
        if n == "suit":
            f = paint_suit
        elif n.startswith("hand_"):
            f = paint_hand(-1 if n.endswith("R") else 1)
        elif n == "head":
            f = paint_head
        elif n.startswith("brow"):
            f = paint_brow
        elif n == "hair":
            f = paint_hair
        elif n == "visor":
            f = paint_visor
        elif n == "visor_hinge":
            f = paint_hinge
        elif n == "headset":
            f = paint_headset
        elif n == "collar":
            f = paint_collar
        elif n == "vest":
            f = paint_vest
        elif n == "spine":
            f = paint_spine
        elif n.startswith("pad_"):
            f = paint_pad(-1 if n.endswith("R") else 1)
        elif n.startswith("bracer_gem_"):
            f = paint_gem(-1 if n.endswith("R") else 1)
        elif n.startswith("bracer_") and not n.startswith("bracer_rims") and not n.startswith("bracer_steps"):
            f = paint_red
        elif n == "belt":
            f = paint_belt
        elif n == "belt_boxes":
            f = paint_boxes
        elif n == "buckle":
            f = paint_buckle
        elif n.startswith("pouch") or n == "strap":
            f = paint_pouch
        elif n.startswith("knee_"):
            f = paint_knee(-1 if n.endswith("R") else 1)
        elif n.startswith("cuff_"):
            f = paint_cuff
        elif n.startswith("sole_glow"):
            f = paint_glow
        elif n.startswith("sole_"):
            f = paint_sole
        elif n.startswith("discs_"):
            f = paint_discs(-1 if n.endswith("R") else 1)
        elif n.startswith("boot_"):
            f = paint_boot
        elif p.key == "gold":
            f = paint_gold
        else:
            f = paint_flat(p.key if p.key in MAT else "graphite")
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


def build_shells(h):
    Vs, Ts, sid, base = [], [], [], 0
    for k, (name, _, budget) in enumerate(SHELLS):
        parts = shell_parts(k)
        fn = shell_fn(parts)
        lo = np.min([p.lo for p in parts], 0) - 0.02
        hi = np.max([p.hi for p in parts], 0) + 0.02
        lo, hi = np.maximum(lo, (-0.34, -0.17, -0.012)), np.minimum(hi, (0.34, 0.17, 1.03))
        Vh, Th = mesh_field(fn, h, lo, hi)
        V, T = build_low(Vh, Th, budget, fn, name)
        Vs.append(V)
        Ts.append(T + base)
        sid.append(np.full(len(V), k))
        base += len(V)
    V, T, sid = np.concatenate(Vs), np.concatenate(Ts), np.concatenate(sid)
    ob = to_mesh("Theo", V, T)
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
    head = z > 0.70
    f[head] = 1.35
    face = head & (y < -0.04) & (z < 0.87) & (np.abs(x) < 0.105)
    f[face] = 2.3
    for pre, val in (("visor", 2.0), ("brow", 2.0), ("headset", 1.4), ("hand_", 1.15), ("glove", 1.15), ("thumb", 1.15),
                     ("sole_glow", 0.45)):
        f[np.char.startswith(own_names, pre)] = val
    chest = np.char.startswith(own_names, "vest") & (y < 0) & (z > 0.585) & (z < 0.66) & (np.abs(x) < 0.06)
    f[chest] = 1.8
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
    im = bpy.data.images.new("theo_v3_tex", TEX, TEX, alpha=True)
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
PROP_HEAD = HANDF[-1][0]
BONE_DEF = [  # name, parent, head, tail (model coordinates before normalisation)
    ("Root", None, (0, 0, 0), (0, 0, 0.40)),
    ("Body", "Root", (0, 0, 0.40), (0, 0, 0.68)),
    ("Head", "Body", (0, 0, 0.68), (0, 0, 1.0)),
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
    groups = [("torso", TORSO_NECK)]
    for s in SIDES:
        sh, up, lo = ARM[s].ch
        groups += [("Body", sh), ("Arm" + SN[s], up), ("Arm" + SN[s], lo), ("Leg" + SN[s], LEG[s])]
    D = np.stack([np.maximum(g.d(P.astype(F32)), 0) for _, g in groups], 0)
    Wt = np.exp(-(D - D.min(0)) / 0.010)
    out = []
    zb = sstep(0.44, 0.50, P[:, 2])
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


def subdivide(V, T, uvc, W):
    """1:4 split; midpoints shared per edge; per-corner UVs interpolated; weights averaged."""
    key = SUB_EDGES
    key.clear()
    newV = [v for v in V]
    newW = [dict(w) for w in W]

    def mid(a, b):
        k = (min(a, b), max(a, b))
        if k not in key:
            key[k] = len(newV)
            newV.append((V[a] + V[b]) * 0.5)
            w = {}
            for d in (W[a], W[b]):
                for bn, v in d.items():
                    w[bn] = w.get(bn, 0) + v * 0.5
            newW.append(w)
        return key[k]
    T2, U2 = [], []
    uvc = np.asarray(uvc).reshape(-1, 3, 2)
    for t, (ua, ub, uc) in zip(T, uvc):
        a, b, c = (int(i) for i in t)
        ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
        uab, ubc, uca = (ua + ub) / 2, (ub + uc) / 2, (uc + ua) / 2
        T2 += [(a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca)]
        U2 += [(ua, uab, uca), (uab, ub, ubc), (uca, ubc, uc), (uab, ubc, uca)]
    out = []
    for w in newW:
        top = sorted(w.items(), key=lambda kv: -kv[1])[:4]
        t = sum(v for _, v in top)
        out.append({k: v / t for k, v in top})
    return np.array(newV), np.array(T2), np.array(U2).reshape(-1, 2), out


def full_stage():
    t_start = time.time()
    Vh, Th = mesh_field(fig_d, 0.003)   # the exact union, for the normalisation only
    lo, hi = Vh.min(0), Vh.max(0)
    del Vh, Th
    shift = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
    scale = 1.0 / (hi[2] - lo[2])
    log("normalisation shift", np.round(shift, 4), "scale", round(scale, 4))
    low, sid = build_shells(0.0025)
    uv_loops = unwrap(low)
    img = bake_texture(low, uv_loops, sid)
    tex_path = os.path.join(OUT_DIR, "theo_v3_tex.png")
    save_texture(img, tex_path)
    log("texture", tex_path)
    V, T = mesh_np(low)
    me = low.data
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    uvc = uv_loops[lt]
    W = vertex_weights(V, sid)
    bpy.data.objects.remove(low, do_unlink=True)
    res = {}
    ob, arm = make_object("Theo", V, T, uvc, W, img, tex_path, shift, scale)
    export(ob, arm, os.path.join(OUT_DIR, "theo_v3_game"))
    infl = max(len(w) for w in W)
    res["game"] = (len(T), len(V), infl)
    views = TS.ref_views()
    masks = TS.render_masks(views, [ob])
    sil = TS.report(views, masks, os.path.join(PREV_DIR, "v3_overlay.png"), "v3 game")
    TS.compare_sheet(views, TS.render_shaded(views, [ob], color_type="TEXTURE", light="FLAT"),
                     os.path.join(PREV_DIR, "v3_compare.png"))
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.objects.remove(arm, do_unlink=True)
    # show: the game mesh split 1:4, every vertex projected onto the exact surface (same UVs, same texture)
    V2, T2, U2, W2 = subdivide(V, T, uvc, W)
    sid2 = np.concatenate([sid, np.zeros(len(V2) - len(V), int)])
    for (a_, b_), j in SUB_EDGES.items():  # midpoints belong to the shell of their edge
        sid2[j] = sid[a_]
    V2 = project_shells(V2, sid2).astype(np.float64)
    ob, arm = make_object("Theo", V2, T2, U2, W2, img, tex_path, shift, scale)
    export(ob, arm, os.path.join(OUT_DIR, "theo_v3_show"))
    res["show"] = (len(T2), len(V2), max(len(w) for w in W2))
    masks2 = TS.render_masks(views, [ob])
    sil2 = TS.report(views, masks2, None, "v3 show")
    for lod, (t, v, mi) in res.items():
        log(f"RESULT {lod} tris {t}/{BUDGET[lod]} {'OK' if t <= BUDGET[lod] else 'OVER'} verts {v} bones {len(BONES)} max infl {mi}")
    log("prop head (Prop bone)", np.round((PROP_HEAD + shift) * scale, 3), "eyes",
        np.round((EYE[-1][0] + shift) * scale, 3), np.round((EYE[1][0] + shift) * scale, 3))
    # round trip of the game FBX (what Unity reads)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT_DIR, "theo_v3_game.fbx"), colors_type="SRGB")
    m = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    a = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    pts = np.array([tuple(m.matrix_world @ v.co) for v in m.data.vertices])
    infl = max(len([g for g in v.groups if g.weight > 0]) for v in m.data.vertices)
    log("FBX check: bones", sorted(b.name for b in a.data.bones), "height", round(float(pts[:, 2].max() - pts[:, 2].min()), 4),
        "x", np.round([pts[:, 0].min(), pts[:, 0].max()], 3), "uv layers", len(m.data.uv_layers), "colour attrs",
        [(c.name, c.domain, c.data_type) for c in m.data.color_attributes], "materials", len(m.data.materials),
        "tris", sum(len(p.vertices) - 2 for p in m.data.polygons), "max infl", infl)
    log(f"full stage {time.time() - t_start:.0f}s")


if STAGE == "shape":
    bpy.ops.wm.read_factory_settings(use_empty=True)
    shape_stage()
    log("done")
elif STAGE == "full":
    bpy.ops.wm.read_factory_settings(use_empty=True)
    full_stage()
    log("done")
