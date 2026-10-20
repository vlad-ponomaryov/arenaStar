# Lark v2, the scout with the drone Pix (Docs/Briefs/A-Roster3D.md "Wave 3"): one smooth "vinyl toy" figure at the level of
# Theo v3 (build_theo_v3.py, whose SDF / meshing / bake / rig machinery is copied here verbatim). Headless, Blender 5.2:
#   blender -b --factory-startup --python Tools/Blender/fighters/build_lark_v2.py -- [out_dir] [--stage shape|full]
#   --stage full (default): models, textures, rigs and exports the body and the weapon;
#   --stage shape: only the exact-surface mesh coloured by part -> Tools/out/art/roster3d/lark_v2_quick.png.
# Output (default Art/Source/3D/lark/, same names as v1): lark_game.fbx/.glb (<= 4000 tris: mesh "Lark", material
# "FighterVC" -> lark_tex.png, armature "Rig" with the 12 FighterBone bones + Extra (the drone), <= 4 smooth weights per
# vertex), lark_tex.png (1024 RGBA: RGB = albedo with baked light gradients / AO / decals, A = gloss),
# lark_weapon.fbx/.glb (<= 1200 tris: mesh "LarkWeapon", material "FighterVC_W" -> lark_weapon_tex.png 512, empty "Muzzle"
# at the suction-cup tip; origin = the grip under the right hand: hang on Prop without offset, barrel along -Y, up +Z).
# Both meshes also carry the corner colour "Col" (the texture blurred, alpha = gloss) as the vertex-colour fallback.
# Format as v1 / Theo: Blender Z up, faces -Y, height 1.0 (feet z = 0), FBX axis_forward=-Z, axis_up=Y, colors_type=SRGB.
#
# Method (as Theo v3): one signed distance field of smooth unions (jacket loft + puffy sleeves + pants + boots, a head of
# cranium / face / cheeks / chin, eyes as glossy dome geometry on the EyeR / EyeL bones, brow ribbons, a carved smile, a
# copper bob envelope with ribbon bangs, goggles with a strap pushed up on the hair, a lumpy fur collar, gloves with three
# curled fingers + thumb wrapped around the grip / the tube, the drone with rotor guards) -> narrow-band surface nets per
# shell -> quadric decimation to exact shell budgets -> smart UV (face / eyes enlarged) -> procedural paint evaluated on
# the exact surface per texel (ramped light, AO from the field, freckles, blush, painted irises, lash lines, rib knit,
# zipper, pockets, badge, hair strands, lens streaks) -> rig with distance-based smooth weights at the shoulders / hips.
import bpy, bmesh, sys, os, math, time
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = ARGV[ARGV.index("--stage") + 1] if "--stage" in ARGV else "full"
POS = [a for i, a in enumerate(ARGV) if not a.startswith("--") and (i == 0 or ARGV[i - 1] != "--stage")]
OUT_DIR = os.path.abspath(POS[0]) if POS else os.path.join(REPO, "Art", "Source", "3D", "lark")
PREV_DIR = os.path.join(REPO, "Tools", "out", "art", "roster3d")
os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(PREV_DIR, exist_ok=True)
T0 = time.time()
GRID_LO, GRID_HI = np.array([-0.46, -0.30, -0.02]), np.array([0.46, 0.30, 1.10])
NAME = ["Lark"]


class TS:  # the two image helpers of theo_silhouette.py
    @staticmethod
    def load_rgba(path):
        img = bpy.data.images.load(path, check_existing=False)
        W, H_ = img.size
        px = np.empty(W * H_ * 4, np.float32)
        img.pixels.foreach_get(px)
        bpy.data.images.remove(img)
        return px.reshape(H_, W, 4)[::-1].copy()

    @staticmethod
    def save_rgb(arr, path):
        H_, W = arr.shape[:2]
        if arr.shape[2] == 3:
            arr = np.concatenate([arr, np.ones((H_, W, 1), arr.dtype)], 2)
        img = bpy.data.images.new("out", W, H_, alpha=True)
        img.pixels.foreach_set(np.ascontiguousarray(arr[::-1], np.float32).ravel())
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        bpy.data.images.remove(img)


def log(*a):
    print(f"[lark2 {time.time() - T0:6.1f}s]", *a, flush=True)


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
# palette (sRGB hex), gloss
# =====================================================================================================================
def hexc(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])

PAL = {
    "skin": ("#F4CDB0", 0.18), "hair": ("#B85A2A", 0.35), "brow": ("#7A3418", 0.15), "jacket": ("#D6A12B", 0.30),
    "jacket_d": ("#B5841C", 0.25), "fur": ("#EFE1C2", 0.08), "pants": ("#4B5245", 0.10), "boot": ("#6A4428", 0.30),
    "sole": ("#2C2622", 0.20), "glove": ("#4A3326", 0.25), "strap": ("#5A3A22", 0.25), "lens": ("#5FC6CF", 1.0),
    "brass": ("#C9A04A", 0.85), "eye": ("#FFFFFF", 1.0), "drone": ("#E9ECEF", 0.70), "drone_d": ("#4A4F57", 0.55),
    "glow": ("#48D6E0", 1.0), "badge": ("#3C8FC9", 0.50), "white": ("#F2F2EE", 0.60), "teal": ("#3FAAB6", 0.80),
    "orange": ("#F07A2A", 0.70), "coral": ("#FF6B4A", 0.60),
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
# MODEL (Blender: Z up, faces -Y, +X = the character's left). Pre-normalisation units (height ~1.0).
# =====================================================================================================================
# ---------------- skeleton points ----------------
SH = {s: mirror((-0.140, 0.0, 0.600), s) for s in SIDES}
EL = {-1: np.array([-0.205, 0.040, 0.495]), 1: np.array([0.075, -0.155, 0.470])}
WR = {-1: np.array([-0.165, -0.060, 0.430]), 1: np.array([-0.068, -0.215, 0.428])}
HA = np.array([-0.120, -0.100, 0.420])            # grip of the launcher = weapon origin = Prop bone head
TUBE_Z = HA[2] + 0.055                             # tube axis height (along Y at x = HA.x)
HIP = {s: mirror((-0.062, 0.0, 0.355), s) for s in SIDES}
KN = {s: mirror((-0.070, -0.008, 0.225), s) for s in SIDES}
AN = {s: mirror((-0.078, 0.004, 0.118), s) for s in SIDES}
DRONE = np.array([-0.250, 0.070, 0.960])

# ---------------- jacket (torso loft + puffy sleeves), pants, boots ----------------
JACKET_T = Loft(smooth_rows([  # z, half width, cx, y front, y back, exponent
    (0.392, 0.060, 0, -0.060, 0.060, 2.0),
    (0.402, 0.128, 0, -0.104, 0.100, 2.4),
    (0.430, 0.146, 0, -0.115, 0.110, 2.6),
    (0.470, 0.151, 0, -0.119, 0.112, 2.6),
    (0.520, 0.153, 0, -0.121, 0.113, 2.6),
    (0.560, 0.150, 0, -0.118, 0.110, 2.6),
    (0.600, 0.140, 0, -0.106, 0.102, 2.5),
    (0.630, 0.116, 0, -0.086, 0.086, 2.3),
    (0.650, 0.076, 0, -0.060, 0.060, 2.1),
    (0.662, 0.034, 0, -0.030, 0.030, 2.0)], 60))
SHOULDER, UPPER, FORE = {}, {}, {}
for s in SIDES:
    SHOULDER[s] = Ell(SH[s] + mirror((0.006, 0.0, 0.004), s), (0.060, 0.058, 0.056))
    UPPER[s] = RCone(SH[s], EL[s], 0.053, 0.048)
    FORE[s] = RCone(EL[s], WR[s], 0.048, 0.041)
JACKET = U(U(JACKET_T, *[U(SHOULDER[s], UPPER[s], k=0.02) for s in SIDES], k=0.03), *FORE.values(), k=0.006)
part("jacket", JACKET, "jacket", ("body",))
HEM = I(Off(JACKET_T, 0.006), Slab((0, 0, 1), (0, 0, 0.418), 0.020), k=0.004)
part("hem", HEM, "jacket_d", ("body",))
CUFF = {}
for s in SIDES:
    dv = nrm(WR[s] - EL[s])
    CUFF[s] = I(Off(FORE[s], 0.005), Slab(dv, WR[s] - dv * 0.016, 0.014), k=0.003)
    part("cuff_" + SN[s], CUFF[s], "jacket_d", ("Arm" + SN[s],))
# badge patch (left chest, +X) and the zipper ridge
BADGE_P = front_pt(JACKET_T, 0.078, 0.565)
BADGE_N = normal_at(JACKET_T, BADGE_P)
part("badge", I(Off(JACKET_T, 0.004), RCyl(BADGE_P, BADGE_N, 0.029, 0.03, 0.0), k=0.003), "badge", ("Body",))
# fur collar: a lumpy torus around the neck base, front lower
COL_C = np.array([0.0, 0.010, 0.636])
COL_AX = nrm((0, -0.22, 1))


def fur_bulge(P):
    a = np.arctan2(P[:, 1] - COL_C[1], P[:, 0] - COL_C[0])
    zz = (P - COL_C) @ COL_AX
    return 0.0048 * (0.5 + 0.5 * np.sin(13 * a + 2.0 * np.sin(5 * a))) + 0.0025 * np.sin(40 * zz)


COLLAR = OffVar(Torus(COL_C, COL_AX, 0.086, 0.034, sxy=(1.14, 1.0)), 0.0, fur_bulge, extra=0.01)
part("collar", COLLAR, "fur", ("Body",))

HIPS = Loft(smooth_rows([
    (0.300, 0.030, 0, -0.030, 0.030, 2.0),
    (0.312, 0.092, 0, -0.072, 0.072, 2.2),
    (0.340, 0.118, 0, -0.086, 0.088, 2.4),
    (0.380, 0.124, 0, -0.090, 0.092, 2.5),
    (0.420, 0.118, 0, -0.088, 0.088, 2.5),
    (0.436, 0.070, 0, -0.055, 0.055, 2.2)], 40))
LEGS = {}
for s in SIDES:
    pts = [HIP[s], KN[s], AN[s] + np.array([0, 0, 0.02])]
    LEGS[s] = U(RCone(pts[0], pts[1], 0.060, 0.050), RCone(pts[1], pts[2], 0.050, 0.046), k=0.01)
PANTS = U(HIPS, *LEGS.values(), k=0.02)
part("pants", PANTS, "pants", ("body",))
BOOT, SOLE_Z = {}, 0.024
for s in SIDES:
    ax_ = AN[s]
    shaft = RCone((ax_[0], 0.006, 0.070), (ax_[0], 0.004, 0.158), 0.052, 0.050)
    foot = Ell((ax_[0] * 1.04, -0.032, 0.052), (0.058, 0.094, 0.052))
    toe = Ell((ax_[0] * 1.06, -0.085, 0.045), (0.050, 0.045, 0.042))
    BOOT[s] = U(shaft, foot, toe, k=0.03)
    part("boot_" + SN[s], BOOT[s], "boot", ("Leg" + SN[s],))
    part("sole_" + SN[s], I(Off(BOOT[s], 0.0055), Half((0, 0, 1), (0, 0, SOLE_Z)), k=0.003), "sole", ("Leg" + SN[s],))
    part("bootroll_" + SN[s], Torus((ax_[0], 0.004, 0.160), (0, 0, 1), 0.050, 0.011), "boot", ("Leg" + SN[s],))
    part("strap_" + SN[s], I(Off(BOOT[s], 0.0045), Slab(nrm((0, 0.45, 1)), (ax_[0], -0.03, 0.098), 0.0085), k=0.002),
         "strap", ("Leg" + SN[s],))
    bp = front_pt(BOOT[s], ax_[0] + s * 0.0, 0.088)
    part("buckle_" + SN[s], RBox(bp + np.array([0, -0.004, 0.0]), (0.012, 0.006, 0.010), 0.004), "brass", ("Leg" + SN[s],))

# ---------------- hands: gloves, three curled fingers + thumb ----------------
FINGER_R = 0.0115


def chain(pts, r0, r1, k=0.004):
    pts = [np.asarray(p, float) for p in pts]
    n = len(pts) - 1
    rr = np.linspace(r0, r1, n + 1)
    return U(*[RCone(pts[i], pts[i + 1], rr[i], rr[i + 1]) for i in range(n)], k=k)


HAND_INFO = {}
# right fist around the vertical grip at HA (angles in the XY plane, 180 = -X, 270 = -Y)
G = HA
fing = []
for k_, (z, sc) in enumerate(((0.420, 1.0), (0.399, 0.97), (0.378, 0.90))):
    pts = []
    for j, a in enumerate((198, 236, 274, 312, 346)):
        r = (0.038, 0.032, 0.031, 0.031, 0.030)[j]
        ar = math.radians(a)
        pts.append((G[0] + r * math.cos(ar), G[1] + r * math.sin(ar), z - 0.002 * j))
    fing.append(chain(pts, FINGER_R * sc, FINGER_R * sc * 0.86))
a0 = math.radians(176)
rad_ = np.array([math.cos(a0), math.sin(a0), 0.0])
tan_ = np.array([-math.sin(a0), math.cos(a0), 0.0])
PC_R = G + rad_ * 0.043 + np.array([0, 0, -0.022])
palm = RBox(PC_R, (0.015, 0.027, 0.034), 0.013, np.stack([rad_, tan_, (0, 0, 1)], 1))
wrist = RCone(WR[-1], PC_R + np.array([0, 0.004, 0.006]), 0.029, 0.027)
thumb = chain([(-0.152, -0.117, 0.433), (-0.133, -0.131, 0.445), (-0.110, -0.128, 0.444)], 0.0140, 0.0112)
HAND_R = U(U(wrist, palm, thumb, k=0.012), U(*fing, k=0.0), k=0.008)
part("hand_R", HAND_R, "glove", ("ArmR",))
HAND_INFO[-1] = (PC_R, rad_)
# left hand under the tube (circle in the XZ plane around the tube axis, 270 = below, 180 = -X)
C_L = np.array([HA[0], -0.240, TUBE_Z])
fing = []
for k_, (y, sc) in enumerate(((-0.262, 1.0), (-0.240, 0.97), (-0.218, 0.90))):
    pts = []
    for j, a in enumerate((292, 254, 216, 182, 160)):
        r = (0.048, 0.044, 0.043, 0.043, 0.042)[j]
        ar = math.radians(a)
        pts.append((C_L[0] + r * math.cos(ar), y, C_L[2] + r * math.sin(ar)))
    fing.append(chain(pts, FINGER_R * sc, FINGER_R * sc * 0.86))
a0 = math.radians(302)
rad_ = np.array([math.cos(a0), 0.0, math.sin(a0)])
tanl = np.array([-math.sin(a0), 0.0, math.cos(a0)])
PC_L = C_L + rad_ * 0.050
palm = RBox(PC_L, (0.014, 0.031, 0.030), 0.013, np.stack([rad_, (0, 1, 0), tanl], 1))
wrist = RCone(WR[1], PC_L + rad_ * 0.004, 0.029, 0.027)
tp = []
for j, a in enumerate((322, 348, 14)):
    ar = math.radians(a)
    r = (0.050, 0.046, 0.044)[j]
    tp.append((C_L[0] + r * math.cos(ar), -0.252 - 0.004 * j, C_L[2] + r * math.sin(ar)))
thumb = chain(tp, 0.0140, 0.0112)
HAND_L = U(U(wrist, palm, thumb, k=0.012), U(*fing, k=0.0), k=0.008)
part("hand_L", HAND_L, "glove", ("ArmL",))
HAND_INFO[1] = (PC_L, rad_)

# ---------------- head ----------------
CRAN = Ell((0, 0.012, 0.805), (0.150, 0.146, 0.150))
FACE = Ell((0, -0.025, 0.752), (0.135, 0.122, 0.108))
CHEEKS = [Ell((s * 0.072, -0.078, 0.728), (0.066, 0.060, 0.052)) for s in SIDES]
CHIN = Ell((0, -0.098, 0.682), (0.052, 0.042, 0.032))
HEAD_SKULL = U(U(CRAN, FACE, CHIN, k=0.05), *CHEEKS, k=0.05)
NOSE = U(Ell((0, -0.150, 0.736), (0.020, 0.017, 0.016)), Ell((0, -0.143, 0.752), (0.010, 0.012, 0.014)), k=0.01)
HEAD0 = U(HEAD_SKULL, NOSE, k=0.012)
SMILE = []
for i in range(9):
    x = -0.034 + 0.068 * i / 8
    SMILE.append((x, 0.6995 + 0.0105 * (x / 0.034) ** 2 + 0.0012 * (x / 0.034)))
SMILE_C = np.array([front_pt(HEAD0, x, z) for x, z in SMILE])
SN_ = np.array([normal_at(HEAD0, c) for c in SMILE_C])
tanm = np.array([nrm(t) for t in np.gradient(SMILE_C, axis=0)])
grove_w = 0.0026 * np.clip(1 - (np.linspace(-1, 1, 9)) ** 4, 0.35, 1)
GROOVE = Ribbon(SMILE_C + SN_ * 0.0012, np.cross(SN_, tanm), SN_, grove_w, np.full(9, 0.0026))
HEAD = S(HEAD0, GROOVE, k=0.0025)
part("head", HEAD, "skin", ("Head",))
part("neck", RCone((0, 0.008, 0.590), (0, 0.010, 0.700), 0.047, 0.044), "skin", ("neck",))
EYE = {}
for s in SIDES:
    p = front_pt(HEAD_SKULL, s * 0.060, 0.772)
    n = normal_at(HEAD_SKULL, p)
    gaze = nrm(n + np.array([0, -1.6, 0]))
    up = nrm(np.array([0, 0, 1.0]) - gaze * gaze[2])
    tx = np.cross(up, gaze) * -1 if s < 0 else np.cross(gaze, up)
    tx = nrm(tx if tx[0] * s > 0 or True else -tx)
    Rm = np.stack([nrm(np.cross(gaze, up)) * -1, gaze, up], 1)  # columns: x (to -X... side), normal, up
    Rm[:, 0] = nrm(np.cross(up, gaze))
    c = p - gaze * 0.0055
    EYE[s] = (c, gaze, Rm)
    part("eye_" + SN[s], Ell(c, (0.0300, 0.0130, 0.0370), Rm), "eye", ("Eye" + SN[s],))
BROWS = {s: [(s * x, z) for x, z in ((0.026, 0.8255), (0.044, 0.8335), (0.064, 0.8355), (0.084, 0.8305), (0.098, 0.8215))]
         for s in SIDES}
BROW_N = {}
for s in SIDES:
    C_ = np.array([front_pt(HEAD_SKULL, x, z) for x, z in BROWS[s]])
    Nn = np.array([normal_at(HEAD_SKULL, c) for c in C_])
    tan = np.array([nrm(t) for t in np.gradient(C_, axis=0)])
    wv = np.array([0.0052, 0.0068, 0.0068, 0.0060, 0.0042])
    part("brow_" + SN[s], Ribbon(C_ + Nn * 0.0004, np.cross(Nn, tan), Nn, wv, np.full(5, 0.0022)), "brow", ("Head",))

# ---------------- hair: copper bob envelope + ribbon bangs ----------------
HAIR_V = U(Off(CRAN, 0.016), *[Ell((s * 0.120, 0.008, 0.748), (0.072, 0.128, 0.118)) for s in SIDES],
           Ell((0, 0.072, 0.742), (0.142, 0.098, 0.105)), k=0.04)
HAIR_YZ = np.array([(-0.30, 0.872), (-0.118, 0.866), (-0.094, 0.818), (-0.084, 0.740), (-0.070, 0.690), (-0.040, 0.674),
                    (0.30, 0.674), (0.30, 1.20), (-0.30, 1.20)])
HAIR = S(I(HAIR_V, Poly2(HAIR_YZ, "yz"), k=0.012), Ell((0, -0.105, 0.735), (0.112, 0.075, 0.105)), k=0.012)
part("hair", HAIR, "hair", ("Head",))
BANGS = []
BANG_DEF = [(-0.098, 0.857, -0.010), (-0.052, 0.846, -0.006), (-0.006, 0.852, 0.004), (0.042, 0.843, 0.010),
            (0.088, 0.855, 0.014)]
OFFC = Off(CRAN, 0.020)
for i, (x, ztip, sw) in enumerate(BANG_DEF):
    NB = 12
    zz = np.linspace(0.950, ztip, NB)
    xs = x * np.linspace(0.45, 1.0, NB) + sw * np.linspace(0, 1, NB) ** 2
    C_ = np.array([front_pt(OFFC, xx, z_) for xx, z_ in zip(xs, zz)])
    Nn = np.array([normal_at(OFFC, c) for c in C_])
    C_ = C_ + Nn * (0.003 + 0.006 * np.linspace(0, 1, NB) ** 3)[:, None]
    tan = np.array([nrm(t) for t in np.gradient(C_, axis=0)])
    w = 0.034 * (1 - 0.55 * np.linspace(0, 1, NB) ** 2)
    t_ = 0.011 * (1 - 0.45 * np.linspace(0, 1, NB))
    rb = Ribbon(C_, np.cross(Nn, tan), Nn, w, t_)
    rb.tone = (0.06, -0.04, 0.08, -0.02, 0.05)[i]
    BANGS.append(rb)
part("bangs", U(*BANGS, k=0.004), "hair", ("Head",))
# goggles pushed up on the hair: strap band + two brass rims with domed teal lenses
HAIR_ALL = U(HAIR, *BANGS)
STRAP_N = nrm((0, 0.40, 1))
part("goggle_strap", I(Off(HAIR_ALL, 0.0065), Slab(STRAP_N, (0, -0.15, 0.922), 0.0115), k=0.003), "strap", ("Head",))
GOG = {}
for s in SIDES:
    p = trace(HAIR_ALL.d, (s * 0.060, -0.40, 0.928), (0, 1, 0))
    n = nrm(normal_at(HAIR_ALL, p) * 0.6 + np.array([0, -0.9, 0.10]))
    c = p + n * 0.010
    GOG[s] = (c, n)
    part("goggle_rim_" + SN[s], RCyl(c, n, 0.046, 0.013, 0.006), "brass", ("Head",))
    Rl = frame(n)
    part("goggle_lens_" + SN[s], Ell(c + n * 0.006, (0.036, 0.036, 0.0125), Rl), "lens", ("Head",))
part("goggle_bridge", RCone(GOG[-1][0] + np.array([0.040, 0, 0.004]), GOG[1][0] + np.array([-0.040, 0, 0.004]), 0.008, 0.008),
     "brass", ("Head",))

# ---------------- drone Pix (Extra) ----------------
dx_, dy_, dz_ = DRONE
part("drone_body", U(Ell(DRONE, (0.050, 0.050, 0.026)), Ell(DRONE + np.array([0, 0, -0.012]), (0.040, 0.040, 0.020)), k=0.01),
     "drone", ("Extra",))
part("drone_belly", I(Off(Ell(DRONE, (0.050, 0.050, 0.026)), 0.003), Half((0, 0, 1), DRONE + np.array([0, 0, -0.006])), k=0.003),
     "drone_d", ("Extra",))
part("drone_eye", Ell(DRONE + np.array([0, -0.046, -0.002]), (0.017, 0.009, 0.012)), "glow", ("Extra",))
arms_, guards, hubs = [], [], []
for ax, ay in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
    tip = DRONE + np.array([0.078 * ax, 0.078 * ay, 0.010])
    arms_.append(RCone(DRONE + np.array([0, 0, 0.002]), tip, 0.012, 0.009))
    guards.append(Torus(tip, (0, 0, 1), 0.037, 0.0068))
    hubs.append(RCyl(tip + np.array([0, 0, 0.006]), (0, 0, 1), 0.009, 0.007, 0.003))
    hubs.append(RBox(tip + np.array([0, 0, 0.011]), (0.031, 0.006, 0.0018), 0.0016, rot((0, 0, 1), 30 * ax * ay)))
part("drone_arms", U(*arms_, *hubs, k=0.004), "drone_d", ("Extra",))
part("drone_guards", U(*guards), "drone", ("Extra",))
part("drone_light", Ell(DRONE + np.array([0, 0, 0.024]), (0.016, 0.016, 0.008)), "glow", ("Extra",))

BODY_PARTS = list(PARTS)
log("body model defined:", len(PARTS), "parts")

# ---------------- weapon: pneumatic harpoon-dart launcher (local: origin = grip, barrel -Y, up +Z) ----------------
PARTS = []
WZ = 0.055
part("w_grip", RCone((0, 0.010, -0.058), (0, 0.0, 0.034), 0.0175, 0.0195), "strap", ("Prop",))
part("w_guard", Torus((0, -0.026, 0.016), (1, 0, 0), 0.017, 0.0042, sxy=(1.0, 1.25)), "drone_d", ("Prop",))
part("w_trigger", RCone((0, -0.020, 0.030), (0, -0.026, 0.008), 0.0045, 0.0040), "orange", ("Prop",))
part("w_stock", U(RBox((0, 0.075, WZ - 0.006), (0.022, 0.050, 0.030), 0.018), RCone((0, 0.030, WZ), (0, 0.120, WZ - 0.020), 0.030, 0.026),
                  k=0.012), "jacket", ("Prop",))
part("w_tube", RCyl((0, -0.095, WZ), (0, 1, 0), 0.032, 0.180, 0.008, taper=0.002), "white", ("Prop",))
for i, y in enumerate((0.040, -0.040, -0.170)):
    part(f"w_ring{i}", Torus((0, y, WZ), (0, 1, 0), 0.033, 0.0058), "teal", ("Prop",))
part("w_muzzle", S(RCyl((0, -0.290, WZ), (0, 1, 0), 0.041, 0.020, 0.008, taper=-0.004), RCyl((0, -0.306, WZ), (0, 1, 0), 0.017, 0.012)),
     "orange", ("Prop",))
part("w_dart", RCone((0, -0.280, WZ), (0, -0.398, WZ), 0.0105, 0.0095), "orange", ("Prop",))
part("w_fins", U(*[RBox((0, -0.318, WZ), (0.0016, 0.012, 0.016), 0.0012, rot((0, 1, 0), a)) for a in (0, 90)]), "teal", ("Prop",))
part("w_cup", S(Ell((0, -0.406, WZ), (0.030, 0.015, 0.030)), Ell((0, -0.420, WZ), (0.022, 0.010, 0.022)), k=0.003), "coral", ("Prop",))
part("w_scope", U(RCyl((0, -0.095, WZ + 0.056), (0, 1, 0), 0.017, 0.052, 0.006),
                  RBox((0, -0.095, WZ + 0.036), (0.008, 0.024, 0.010), 0.004), k=0.004), "drone_d", ("Prop",))
part("w_lens", Ell((0, -0.148, WZ + 0.056), (0.0125, 0.0045, 0.0125)), "glow", ("Prop",))
part("w_tank", RCyl((0, -0.010, WZ - 0.040), (0, 1, 0), 0.016, 0.036, 0.008), "brass", ("Prop",))
MUZZLE = np.array([0.0, -0.4215, WZ])
WEAPON_PARTS = list(PARTS)
PARTS = BODY_PARTS
log("weapon model defined:", len(WEAPON_PARTS), "parts")


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
    cam.data.ortho_scale = 1.30
    sc.camera = cam
    panels = []
    for name, yaw, pitch in views:
        yr, pr = math.radians(yaw), math.radians(pitch)
        t = Vector((0, 0, 0.5))
        dv = Vector((math.sin(yr) * math.cos(pr), -math.cos(yr) * math.cos(pr), math.sin(pr)))
        cam.location = t + dv * 4
        cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        fp = os.path.join(PREV_DIR, f"_q_lark_{name}.png")
        sc.render.filepath = fp
        bpy.ops.render.render(write_still=True)
        panels.append(TS.load_rgba(fp)[..., :3])
        os.remove(fp)
    TS.save_rgb(np.concatenate(panels, 1), path)
    log("preview", path)

CLOSEUPS = [
    ("face", (0.0, -0.10, 0.790), 0, 4, 0.36),
    ("hand", (-0.120, -0.150, 0.430), -25, 15, 0.30),
    ("drone", (-0.250, 0.070, 0.960), 20, 35, 0.24),
    ("34", (0.0, 0.0, 0.500), 35, 10, 1.10),
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
        fp = os.path.join(PREV_DIR, f"_c_lark_{name}.png")
        sc.render.filepath = fp
        bpy.ops.render.render(write_still=True)
        panels.append(TS.load_rgba(fp)[..., :3])
        os.remove(fp)
    for o in hid:
        o.hide_render = False
    TS.save_rgb(np.concatenate(panels, 1), path)
    log("closeups", path)


def shape_stage():
    V, T = mesh_field(fig_d, 0.004, GRID_LO, GRID_HI)
    ob = to_mesh("LarkHi", V, T)
    col = key_colours(V.astype(F32))
    attr = ob.data.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.hstack([col ** 2.2, np.ones((len(V), 1))]).astype(np.float32).ravel())
    for p in ob.data.polygons:
        p.use_smooth = True
    lo, hi = V.min(0), V.max(0)
    log("bounds", np.round(lo, 3), np.round(hi, 3), "height", round(hi[2] - lo[2], 4))
    quick_preview(ob, os.path.join(PREV_DIR, "lark_v2_quick.png"))
    closeups([ob], os.path.join(PREV_DIR, "lark_v2_closeups.png"))


# =====================================================================================================================
# paint
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
    "skin": (((0.0, "#C47A62"), (0.5, "#F2C2A2"), (1.0, "#FFE4D0")), "plastic", 0.45, 0.18),
    "hair": (((0.0, "#4E1C0A"), (0.40, "#8E3E1A"), (0.72, "#B85A2A"), (1.0, "#E89058")), "plastic", 0.70, 0.35),
    "brow": (((0.0, "#4A1A0A"), (0.5, "#7A3418"), (1.0, "#9A4E28")), "plastic", 0.30, 0.15),
    "jacket": (((0.0, "#6A440A"), (0.40, "#A87818"), (0.72, "#D6A12B"), (1.0, "#F4CC66")), "plastic", 0.60, 0.30),
    "jacket_d": (((0.0, "#523606"), (0.5, "#956A14"), (1.0, "#C4962E")), "plastic", 0.55, 0.25),
    "fur": (((0.0, "#9E8C6E"), (0.5, "#E0CFAC"), (1.0, "#FFF7E6")), "plastic", 0.60, 0.08),
    "pants": (((0.0, "#1E221C"), (0.5, "#4B5245"), (1.0, "#7C8472")), "plastic", 0.60, 0.10),
    "boot": (((0.0, "#2A190E"), (0.5, "#6A4428"), (1.0, "#9E6E48")), "plastic", 0.55, 0.30),
    "sole": (((0.0, "#121010"), (0.5, "#2C2622"), (1.0, "#4C443E")), "plastic", 0.45, 0.20),
    "glove": (((0.0, "#1C130D"), (0.5, "#4A3326"), (1.0, "#80604A")), "plastic", 0.55, 0.25),
    "strap": (((0.0, "#22150B"), (0.5, "#5A3A22"), (1.0, "#8A6442")), "plastic", 0.50, 0.25),
    "brass": (((0.0, "#4A3410"), (0.35, "#8A6A2A"), (0.62, "#C9A04A"), (0.85, "#E8C878"), (1.0, "#FFF2BE")), "metal", 0.45, 0.85),
    "drone": (((0.0, "#868C94"), (0.5, "#DADFE4"), (1.0, "#FFFFFF")), "plastic", 0.45, 0.70),
    "drone_d": (((0.0, "#191C21"), (0.5, "#454A52"), (1.0, "#767C86")), "plastic", 0.50, 0.55),
    "white": (((0.0, "#929496"), (0.5, "#E4E4E0"), (1.0, "#FFFFFF")), "plastic", 0.50, 0.60),
    "teal": (((0.0, "#1A6670"), (0.5, "#3FAAB6"), (1.0, "#90E8F0")), "plastic", 0.45, 0.80),
    "orange": (((0.0, "#862E06"), (0.5, "#E06A22"), (1.0, "#FFB274")), "plastic", 0.50, 0.70),
    "coral": (((0.0, "#962616"), (0.5, "#F2603E"), (1.0, "#FFAC92")), "plastic", 0.50, 0.60),
    "badge": (((0.0, "#18466E"), (0.5, "#3C8FC9"), (1.0, "#8ECAF2")), "plastic", 0.50, 0.50),
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



def paint_flat(key, spec=0.0):
    def f(P, N, occ):
        return shade(key, N, occ, spec), np.full(len(P), gloss_of(key))
    return f


def rib(c, P, axis_u, freq, amt):
    """Rib-knit stripes: modulate brightness by a sine of the coordinate u."""
    m = 0.5 + 0.5 * np.sin(axis_u * freq)
    return np.clip(c * (1 - amt + amt * 2 * m)[:, None] ** 0.5, 0, 1)


# ---- jacket: zipper, pockets, seams, sleeve pocket ----
ZIP_X = 0.0


def paint_jacket(P, N, occ):
    c = shade("jacket", N, occ, 0.10)
    g = np.full(len(P), gloss_of("jacket"))
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    front = (y < -0.02) & (z < 0.645)
    # zipper: dark tape + brass teeth, centre front
    dz = np.abs(x - ZIP_X)
    tape = line_w(dz, 0.0055, 0.0008) * front * (z > 0.405)
    c = stroke(c, tape, H("#6E4C10"))
    teeth = line_w(dz, 0.0026, 0.0006) * front * (0.5 + 0.5 * np.sign(np.sin(z * 900))) * (z > 0.405)
    c = stroke(c, teeth, H("#E2C070"))
    g = np.where(teeth > 0.5, 0.85, g)
    puller = np.exp(-((x / 0.004) ** 2 + ((z - 0.600) / 0.009) ** 2)) * front
    c = stroke(c, sstep(0.3, 0.6, puller), H("#C9A04A"))
    # welt pockets: slanted slits with a lighter lip
    for s in SIDES:
        d, f = seg2(x, z, [(s * 0.060, 0.505), (s * 0.104, 0.468)])
        m = (y < -0.05)
        c = stroke(c, line_w(d, 0.0028, 0.0008) * m, H("#5C3C08"))
        c = stroke(c, line_w(np.abs(d - 0.0052), 0.0010, 0.0006) * m * 0.6, H("#F0C860"))
        # shoulder / raglan seam (stitched line from the collar to the armpit)
        d2, _ = seg2(x * s, z, [(0.070, 0.640), (0.100, 0.600), (0.118, 0.560), (0.128, 0.520)])
        m2 = np.abs(y) < 0.13
        c = stroke(c, line_w(d2, 0.0016, 0.0006) * m2 * 0.7, H("#8A6214"))
        stitch = line_w(np.abs(d2 - 0.0042), 0.0007, 0.0004) * (np.sin((z + y) * 700) > 0) * m2
        c = stroke(c, stitch * 0.7, H("#F6D88A"))
    # sleeve pocket (left upper arm, outer side) with a zip line
    q = P - (SH[1] + (EL[1] - SH[1]) * 0.35)
    au = nrm(EL[1] - SH[1])
    vv = q @ au
    side = sstep(0.2, 0.6, N[:, 0])
    rect = (np.abs(vv) < 0.030) & (np.abs(z - (SH[1][2] - 0.04)) < 0.10)
    box = sstep(0.0, 0.003, 0.030 - np.abs(vv)) * side * rect
    edge = line_w(np.abs(np.abs(vv) - 0.030), 0.0012, 0.0005) * side * rect
    c = stroke(c, box * 0.25, H("#B07E1C"))
    c = stroke(c, edge * 0.8, H("#8A6214"))
    c = stroke(c, line_w(np.abs(vv - 0.018), 0.0012, 0.0005) * box, H("#5C3C08"))
    # back: a yoke seam
    back = y > 0.04
    c = stroke(c, line_w(np.abs(z - 0.585 + 0.25 * (x / 0.14) ** 2 * 0.1), 0.0016, 0.0006) * back * (np.abs(x) < 0.12) * 0.6,
               H("#8A6214"))
    return c, g


def paint_rib(P, N, occ):
    c = shade("jacket_d", N, occ, 0.05)
    a = np.arctan2(P[:, 0], -P[:, 1])
    c = rib(c, P, a, 70, 0.10)
    return c, np.full(len(P), gloss_of("jacket_d"))


def paint_cuff(s):
    dv = nrm(WR[s] - EL[s])
    e1 = nrm(np.cross(dv, (0, 0, 1)))
    e2 = np.cross(dv, e1)

    def f(P, N, occ):
        c = shade("jacket_d", N, occ, 0.05)
        q = P - WR[s]
        a = np.arctan2(q @ e2, q @ e1)
        c = rib(c, P, a, 18, 0.12)
        return c, np.full(len(P), gloss_of("jacket_d"))
    return f


def paint_fur(P, N, occ):
    c = shade("fur", N, occ, 0.0)
    a = np.arctan2(P[:, 1] - COL_C[1], P[:, 0] - COL_C[0])
    zz = (P - COL_C) @ COL_AX
    tuft = 0.5 + 0.5 * np.sin(13 * a + 2.0 * np.sin(5 * a)) * np.sin(40 * zz + 3 * a)
    c = np.clip(c * (0.90 + 0.14 * tuft)[:, None], 0, 1)
    return c, np.full(len(P), 0.08)


def paint_badge(P, N, occ):
    c = shade("badge", N, occ, 0.3)
    e1 = nrm(np.cross(BADGE_N, (0, 0, 1)))
    e2 = np.cross(e1, BADGE_N) * -1
    q = P - BADGE_P
    u, v = q @ e1, q @ e2
    if np.mean(v) < -1:  # (keep numpy happy)
        pass
    r = np.sqrt(u * u + v * v)
    c = stroke(c, line_w(np.abs(r - 0.024), 0.0020, 0.0006), H("#F2F2EE"))
    # a stylised wing: three stacked feathers (original emblem)
    for i, (du, dv_, L) in enumerate(((0.0, 0.006, 0.018), (0.002, -0.001, 0.014), (0.004, -0.008, 0.010))):
        d, _ = seg2(u, v, [(-0.012 + du, dv_), (-0.012 + du + L, dv_ + 0.004)])
        c = stroke(c, line_w(d, 0.0030 - 0.0004 * i, 0.0006), H("#FFD45A"))
    return c, np.full(len(P), 0.5)


def paint_pants(P, N, occ):
    c = shade("pants", N, occ, 0.05)
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    for s in SIDES:  # outer side seam + a knee patch line
        d = np.abs(np.arctan2(y, (x - s * 0.06) * s))
        m = (z < 0.34) & (np.abs(x) > 0.03)
        c = stroke(c, line_w(d, 0.05, 0.02) * m * 0.35, H("#363C31"))
        kn = np.exp(-(((x - KN[s][0]) / 0.03) ** 2 + ((z - KN[s][2]) / 0.025) ** 2)) * (y < -0.02)
        c = stroke(c, line_w(np.abs(kn - 0.5), 0.05, 0.03) * 0.6, H("#2C3128"))
    return c, np.full(len(P), gloss_of("pants"))


def paint_boot(P, N, occ):
    c = shade("boot", N, occ, 0.25)
    z = P[:, 2]
    # welt stitching above the sole
    st = line_w(np.abs(z - (SOLE_Z + 0.010)), 0.0010, 0.0005) * (np.sin((P[:, 0] + P[:, 1]) * 600) > 0)
    c = stroke(c, st * 0.7, H("#E8C890"))
    # toe cap line
    toe = line_w(np.abs(np.sqrt(((P[:, 0] - np.sign(P[:, 0]) * 0.082) / 0.05) ** 2 + ((P[:, 1] + 0.085) / 0.045) ** 2) - 1.0),
                 0.035, 0.015) * (P[:, 1] < -0.06) * (z > SOLE_Z)
    c = stroke(c, toe * 0.35, H("#4A2E1A"))
    return c, np.full(len(P), gloss_of("boot"))


def paint_sole(P, N, occ):
    c = shade("sole", N, occ, 0.0)
    c = stroke(c, line_w(np.abs(P[:, 2] - 0.010), 0.0012, 0.0005) * 0.5, H("#5A524A"))
    return c, np.full(len(P), 0.2)


def paint_glove(s):
    pc, rad_ = HAND_INFO[s]

    def f(P, N, occ):
        c = shade("glove", N, occ, 0.25)
        # lighter knuckle sheen on the back of the hand + a cuff band at the wrist
        bk = sstep(0.2, 0.8, N @ rad_)
        c = stroke(c, bk * 0.25, H("#7A5844"))
        dw = np.linalg.norm(P - WR[s], axis=1)
        c = stroke(c, line_w(np.abs(dw - 0.020), 0.0030, 0.001) * 0.6, H("#2A1C14"))
        return c, np.full(len(P), 0.30)
    return f


# ---- head: eyes, lashes, brows, blush, freckles, mouth ----
RNG = np.random.default_rng(5)
FRECKLES = []
for s in SIDES:
    for _ in range(11):
        FRECKLES.append((s * RNG.uniform(0.032, 0.098), RNG.uniform(0.712, 0.748), RNG.uniform(0.0016, 0.0024)))
for _ in range(5):
    FRECKLES.append((RNG.uniform(-0.020, 0.020), RNG.uniform(0.736, 0.752), RNG.uniform(0.0013, 0.0018)))
FRECKLES = np.array(FRECKLES)
EYE_A = (0.0300, 0.0370)


def eye_uv(P, s):
    c, gaze, Rm = EYE[s]
    q = P - c
    return q @ Rm[:, 0] * s, q @ Rm[:, 2]   # u toward the outer corner, v up


def paint_eye(s):
    def f(P, N, occ):
        u, v = eye_uv(P, s)
        au, av = EYE_A
        sh = sstep(-0.004, 0.030, v)
        c = mixc(H("#FFFFFF"), H("#D8DCE6"), sh * 0.7)
        iu, iv = -0.0020, -0.0030
        r = np.sqrt(((u - iu) / 1.0) ** 2 + ((v - iv) / 1.12) ** 2)
        ir = 0.0205
        icol = mixc(H("#7CC48A"), H("#2C6040"), sstep(0.004, ir, r))
        icol = mixc(icol, H("#1F4A30"), sstep(0.0, 0.016, v - iv) * 0.45)
        c = stroke(c, line_w(r, ir, 0.0006), icol)
        c = stroke(c, line_w(np.abs(r - ir + 0.0008), 0.0010, 0.0004) * 0.8, H("#173A26"))
        c = stroke(c, line_w(r, 0.0098, 0.0005), H("#141414"))
        r1 = np.sqrt((u - iu - 0.0068) ** 2 + (v - iv - 0.0078) ** 2)
        r2 = np.sqrt((u - iu + 0.0062) ** 2 + (v - iv + 0.0070) ** 2)
        c = stroke(c, line_w(r1, 0.0050, 0.0005), H("#FFFFFF"))
        c = stroke(c, line_w(r2, 0.0024, 0.0004) * 0.9, H("#FFFFFF"))
        # upper lid shadow + lash line on the dome's top edge
        q = (u / au) ** 2 + (v / av) ** 2
        top = (v > 0.004) & (q > 0.62)
        c = stroke(c, sstep(0.62, 0.80, q) * top, H("#2A1610"))
        c = np.clip(c * (1 - 0.25 * occ)[:, None], 0, 1)
        return c, np.full(len(P), 1.0)
    return f


def brow_stroke(c, x, z, s, front):
    V = np.array(BROWS[s], float)
    d, f = seg2(x, z, V)
    inner = 1 - f if s > 0 else f
    inner = f if s > 0 else 1 - f
    half = 0.0066 * (1 - 0.40 * (1 - inner) ** 1.5) + 0.0006
    w = line_w(d, half, 0.0006) * front
    col = mixc(H("#5A220C"), H("#8E4220"), sstep(half, 0.0, d) * 0.7)
    return stroke(c, w, col)


def paint_head(P, N, occ):
    c = shade("skin", N, occ, 0.10)
    g = np.full(len(P), gloss_of("skin"))
    x, z = P[:, 0], P[:, 2]
    front = sstep(0.1, 0.5, -N[:, 1])
    for s in SIDES:
        b = np.exp(-(((x - s * 0.078) / 0.026) ** 2 + ((z - 0.722) / 0.017) ** 2)) * front
        c = stroke(c, b * 0.50, H("#F49A8A"))
    nose = np.exp(-((x / 0.015) ** 2 + ((z - 0.738) / 0.011) ** 2)) * front
    c = stroke(c, nose * 0.25, H("#F2A08C"))
    for fx, fz, fr in FRECKLES:
        r = np.sqrt((x - fx) ** 2 + (z - fz) ** 2)
        c = stroke(c, line_w(r, fr, 0.0005) * front * 0.60, H("#C07A52"))
    # mouth: the carved smile line darkened, a little lower lip tint and corner dimples
    d, f = seg2(x, z, np.array(SMILE))
    wm = line_w(d, 0.0022 * np.clip(1 - (2 * f - 1) ** 4, 0.4, 1), 0.0006) * front
    c = stroke(c, wm, H("#8A3A30"))
    lip = np.exp(-((x / 0.017) ** 2 + ((z - 0.6905) / 0.0045) ** 2)) * front
    c = stroke(c, lip * 0.30, H("#E48A80"))
    for s_ in SIDES:
        c = brow_stroke(c, x, z, s_, front)
        # lash line around the top of the eye dome on the skin (thicker toward the outer corner, small flick)
        u, v = eye_uv(P, s_)
        au, av = EYE_A
        q = np.sqrt((u / (au + 0.0018)) ** 2 + (v / (av + 0.0018)) ** 2)
        outer = sstep(-0.3, 1.0, u / au)
        lash = line_w(np.abs(q - 1.0), 0.07 + 0.06 * outer, 0.02) * (v > -0.004) * front
        fl = seg2(u, v, [(au * 0.80, av * 0.50), (au + 0.0075, av * 0.62 + 0.004)])[0]
        lash = np.maximum(lash, line_w(fl, 0.0019, 0.0005) * front)
        c = stroke(c, lash, H("#2A1610"))
        low = line_w(np.abs(q - 1.0), 0.04, 0.02) * (v < -0.010) * (np.abs(u) < au * 0.8) * front * 0.35
        c = stroke(c, low, H("#B0705A"))
    return c, g


def paint_brow(P, N, occ):
    c = shade("brow", N, occ, 0.1)
    return c, np.full(len(P), 0.15)


def paint_hair(P, N, occ):
    n = len(P)
    lam = np.clip(N @ KEY_L, -1, 1)
    light = sstep(-0.85, 0.90, 0.6 * lam + 0.4 * N[:, 2])
    q = P - np.array([0.0, 0.03, 0.975])
    az = np.arctan2(q[:, 0], -q[:, 1])
    el = np.arctan2(-q[:, 2], np.sqrt(q[:, 0] ** 2 + q[:, 1] ** 2))
    strand = 0.5 + 0.5 * np.sin(az * 26 + 1.5 * np.sin(az * 7) + el * 2.0)
    # bangs: per-lock ridge and tone
    best = np.full(n, BIG, F32)
    xw, tone = np.zeros(n, F32), np.zeros(n, F32)
    for L in BANGS:
        m = _inbb(P, L.lo, L.hi, 0.006)
        if not m.any():
            continue
        idx = np.nonzero(m)[0]
        d, s_, x_, _ = L.local(P[idx].astype(F32))
        b = d < best[idx]
        j = idx[b]
        best[j], xw[j], tone[j] = d[b], x_[b], L.tone
    on = sstep(0.006, 0.0015, best)
    ridge = 1 - np.clip(np.abs(xw), 0, 1) ** 2
    v = 0.40 + 0.34 * light + 0.07 * (strand - 0.5) * (1 - on) + on * (0.07 * ridge + tone - 0.04 * sstep(0.7, 1.0, np.abs(xw)))
    # sheen band (a soft ring of light across the crown)
    ring = np.exp(-((P[:, 2] - 0.905) / 0.016) ** 2) * sstep(-0.2, 0.6, -N[:, 1] * 0.3 + N[:, 2] * 0.7 + 0.2)
    v = v + 0.10 * ring * (0.6 + 0.4 * strand)
    c = ramp("hair", v)
    sp_ = np.clip(N @ HALF_V, 0, 1) ** 18 * 0.10
    c = c + (1 - c) * sp_[:, None]
    c = c * (1 - 0.45 * np.minimum(occ, 0.6))[:, None]
    return np.clip(c, 0, 1), np.full(n, 0.35)


def paint_lens(c_n):
    c0, n0 = c_n
    e1 = nrm(np.cross(n0, (0, 0, 1)))
    e2 = np.cross(e1, n0) * -1

    def f(P, N, occ):
        q = P - c0
        u, v = q @ e1, q @ e2
        base = mixc(H("#1E6F7C"), H("#7FE0E8"), sstep(-0.03, 0.03, v))
        r = np.sqrt(u * u + v * v)
        base = mixc(base, H("#12454E"), sstep(0.022, 0.036, r) * 0.6)
        streak = line_w(np.abs((u + 0.010) * 0.7 + (v - 0.010) * 0.7), 0.0042, 0.0012) * (r < 0.032)
        base = stroke(base, streak * 0.85, H("#F2FFFF"))
        dot = line_w(np.sqrt((u - 0.013) ** 2 + (v + 0.012) ** 2), 0.0035, 0.0008)
        base = stroke(base, dot * 0.8, H("#F2FFFF"))
        return np.clip(base, 0, 1), np.full(len(P), 1.0)
    return f


def paint_glow(P, N, occ):
    c = mixc(H("#3CC8D6"), H("#D8FCFF"), sstep(0.2, 0.95, -N[:, 1] * 0.5 + N[:, 2] * 0.5 + 0.3))
    return c, np.full(len(P), 1.0)


def paint_drone(P, N, occ):
    c = shade("drone", N, occ, 0.4)
    q = P - DRONE
    # panel line around the shell + a small orange stripe
    c = stroke(c, line_w(np.abs(q[:, 2] - 0.006), 0.0010, 0.0005) * 0.6, H("#7A808A"))
    st = line_w(np.abs(q[:, 0] - 0.0), 0.004, 0.0008) * (q[:, 2] > 0.012) * (np.abs(q[:, 1]) < 0.04)
    c = stroke(c, st, H("#F07A2A"))
    return c, np.full(len(P), 0.7)


def paint_tube(P, N, occ):
    c = shade("white", N, occ, 0.35)
    y = P[:, 1]
    # small decal: two orange chevrons + a panel line
    for y0 in (-0.105, -0.122):
        d = np.abs(np.abs(P[:, 0]) * 0.8 + (y - y0)) * (P[:, 2] > WZ + 0.005)
        c = stroke(c, line_w(d, 0.0025, 0.0008) * (np.abs(P[:, 0]) < 0.024) * (np.abs(N[:, 0]) > 0.25), H("#F07A2A"))
    c = stroke(c, line_w(np.abs(y + 0.235), 0.0010, 0.0005) * 0.7, H("#9A9C9E"))
    return c, np.full(len(P), 0.6)


def paint_grip(P, N, occ):
    c = shade("strap", N, occ, 0.1)
    c = rib(c, P, P[:, 2], 260, 0.15)
    return c, np.full(len(P), 0.25)


def assign_paints(parts):
    for p in parts:
        n = p.name
        if n == "jacket":
            f = paint_jacket
        elif n == "hem":
            f = paint_rib
        elif n.startswith("cuff_"):
            f = paint_cuff(-1 if n.endswith("R") else 1)
        elif n == "collar":
            f = paint_fur
        elif n == "badge":
            f = paint_badge
        elif n == "pants":
            f = paint_pants
        elif n.startswith("boot_") or n.startswith("bootroll"):
            f = paint_boot
        elif n.startswith("sole_"):
            f = paint_sole
        elif n.startswith("hand_"):
            f = paint_glove(-1 if n.endswith("R") else 1)
        elif n in ("head", "neck"):
            f = paint_head
        elif n.startswith("eye_"):
            f = paint_eye(-1 if n.endswith("R") else 1)
        elif n.startswith("brow"):
            f = paint_brow
        elif n in ("hair", "bangs"):
            f = paint_hair
        elif n.startswith("goggle_lens"):
            f = paint_lens(GOG[-1 if n.endswith("R") else 1])
        elif p.key == "glow":
            f = paint_glow
        elif n in ("drone_body", "drone_guards"):
            f = paint_drone
        elif n == "w_tube":
            f = paint_tube
        elif n == "w_grip":
            f = paint_grip
        else:
            f = paint_flat(p.key, 0.3)
        p.paint = f


assign_paints(BODY_PARTS)
assign_paints(WEAPON_PARTS)


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
# low poly, UVs, bake, rig, export
# =====================================================================================================================
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
BODY_SHELLS = [  # name, part-name prefixes, game triangles
    ("head", ("head", "neck", "brow", "hair", "bangs", "goggle"), 1480),
    ("eyes", ("eye_",), 150),
    ("hand_R", ("hand_R",), 200),
    ("hand_L", ("hand_L",), 200),
    ("drone", ("drone",), 240),
    ("body", (), 1720),
]
WEAPON_SHELLS = [("weapon", (), 1185)]
SHELLS = BODY_SHELLS


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
        lo, hi = np.maximum(lo, GRID_LO), np.minimum(hi, GRID_HI)
        Vh, Th = mesh_field(fn, h, lo, hi)
        V, T = build_low(Vh, Th, budget, fn, name)
        Vs.append(V)
        Ts.append(T + base)
        sid.append(np.full(len(V), k))
        base += len(V)
    V, T, sid = np.concatenate(Vs), np.concatenate(Ts), np.concatenate(sid)
    ob = to_mesh(NAME[0], V, T)
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
    if NAME[0] != "Lark":
        f[np.char.startswith(own_names, "w_lens") | np.char.startswith(own_names, "w_ring")] = 1.4
        f[np.char.startswith(own_names, "w_tube")] = 1.2
        return f
    head = z > 0.64
    f[head] = 1.3
    face = head & (y < -0.06) & (z < 0.86) & (np.abs(x) < 0.13)
    f[face] = 2.3
    for pre, val in (("eye_", 3.0), ("brow", 2.0), ("goggle_lens", 1.6), ("badge", 2.0), ("hand_", 1.1), ("drone", 1.1),
                     ("sole", 0.6), ("pants", 0.85)):
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
    im = bpy.data.images.new(os.path.basename(path), TEX, TEX, alpha=True)
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



BONE_DEF = [  # name, parent, head, tail (pre-normalisation)
    ("Root", None, (0, 0, 0), (0, 0, 0.40)),
    ("Body", "Root", (0, 0, 0.40), (0, 0, 0.64)),
    ("Head", "Body", (0, 0, 0.64), (0, 0, 1.0)),
    ("ArmR", "Body", SH[-1], WR[-1]),
    ("ArmL", "Body", SH[1], WR[1]),
    ("LegR", "Root", HIP[-1], AN[-1]),
    ("LegL", "Root", HIP[1], AN[1]),
    ("Prop", "Body", HA, HA + np.array([0, -0.10, 0])),
    ("EyeR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("EyeL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("LidR", "Head", EYE[-1][0], EYE[-1][0] + EYE[-1][1] * 0.03),
    ("LidL", "Head", EYE[1][0], EYE[1][0] + EYE[1][1] * 0.03),
    ("Extra", "Body", DRONE, DRONE + np.array([0, 0, 0.06])),
]
BONES = [b[0] for b in BONE_DEF]


def body_weights(P):
    """Smooth weights for the jacket / pants / rib parts: exp falloff over the distance to the sub-primitives."""
    P = np.asarray(P, F32)
    groups = [("torso", JACKET_T), ("hips", HIPS)]
    for s in SIDES:
        groups += [("Body", SHOULDER[s]), ("Arm" + SN[s], UPPER[s]), ("Arm" + SN[s], FORE[s]), ("Leg" + SN[s], LEGS[s])]
    D = np.stack([np.maximum(g.d(P), 0) for _, g in groups], 0)
    Wt = np.exp(-(D - D.min(0)) / 0.012)
    zb = sstep(0.43, 0.50, P[:, 2])
    out = []
    names = [n for n, _ in groups]
    for j in range(len(P)):
        w = {}
        for i, nm in enumerate(names):
            v = float(Wt[i, j])
            if v < 0.02:
                continue
            if nm == "torso":
                w["Root"] = w.get("Root", 0) + v * (1 - zb[j])
                w["Body"] = w.get("Body", 0) + v * zb[j]
            elif nm == "hips":
                w["Root"] = w.get("Root", 0) + v
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
        spec = p.bone[0]
        if spec == "body":
            ws = body_weights(V[idx])
            for k, j in enumerate(idx):
                W[j] = ws[k]
        elif spec == "neck":
            t = sstep(0.62, 0.68, V[idx, 2])
            for k, j in enumerate(idx):
                W[j] = {"Body": 1 - t[k], "Head": t[k]}
        elif spec.startswith("Arm"):  # gloves / cuffs: ride the arm, the cuff blends a bit with the forearm sleeve
            for j in idx:
                W[j] = {spec: 1.0}
        else:
            for j in idx:
                W[j] = {spec: 1.0}
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



def weapon_object(V, T, uvc, img, tex_path, scale):
    ob = to_mesh("LarkWeapon", V * scale, T)
    me = ob.data
    ul = me.uv_layers.new(name="UVMap")
    ul.data.foreach_set("uv", np.asarray(uvc, np.float64).ravel())
    smp = sample_tex(img, uvc, blur=2)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(smp[:, :3]), smp[:, 3:4]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    mat = bpy.data.materials.new("FighterVC_W")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    texn = nt.nodes.new("ShaderNodeTexImage")
    texn.image = bpy.data.images.load(tex_path, check_existing=True)
    texn.image.alpha_mode = "CHANNEL_PACKED"
    nt.links.new(texn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    mz = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(mz)
    mz.location = Vector(MUZZLE * scale)
    mz.parent = ob
    return ob, mz


def tri_uvs(ob, uv_loops):
    me = ob.data
    me.calc_loop_triangles()
    lt = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    return uv_loops[lt]


def full_stage():
    global PARTS, SHELLS, TEX
    t_start = time.time()
    # ---- body ----
    NAME[0] = "Lark"
    low, sid = build_shells(0.0028)
    V, T = mesh_np(low)
    zmin, zmax = V[:, 2].min(), V[:, 2].max()
    shift = np.array([0.0, 0.0, -zmin])
    scale = 1.0 / (zmax - zmin)
    log("normalisation shift", np.round(shift, 4), "scale", round(scale, 4))
    uv_loops = unwrap(low)
    img = bake_texture(low, uv_loops, sid)
    tex_path = os.path.join(OUT_DIR, "lark_tex.png")
    save_texture(img, tex_path)
    log("texture", tex_path)
    uvc = tri_uvs(low, uv_loops)
    W = vertex_weights(V, sid)
    bpy.data.objects.remove(low, do_unlink=True)
    ob, arm = make_object("Lark", V, T, uvc, W, img, tex_path, shift, scale)
    export(ob, arm, os.path.join(OUT_DIR, "lark_game"))
    body_tris, infl = len(T), max(len(w) for w in W)
    prop = (HA + shift) * scale
    # ---- weapon ----
    PARTS, SHELLS, TEX = WEAPON_PARTS, WEAPON_SHELLS, 512
    NAME[0] = "LarkWeapon"
    wlo, wsid = build_shells(0.0020)
    WV, WT = mesh_np(wlo)
    wuv = unwrap(wlo)
    wimg = bake_texture(wlo, wuv, wsid)
    wtex = os.path.join(OUT_DIR, "lark_weapon_tex.png")
    save_texture(wimg, wtex)
    wuvc = tri_uvs(wlo, wuv)
    bpy.data.objects.remove(wlo, do_unlink=True)
    gun, mz = weapon_object(WV, WT, wuvc, wimg, wtex, scale)
    bpy.ops.object.select_all(action="DESELECT")
    gun.select_set(True)
    mz.select_set(True)
    bpy.context.view_layer.objects.active = gun
    base = os.path.join(OUT_DIR, "lark_weapon")
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH", "EMPTY"}, add_leaf_bones=False,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                             mesh_smooth_type="FACE", bake_anim=False, path_mode="RELATIVE")
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
    log(f"RESULT body tris {body_tris}/4000 {'OK' if body_tris <= 4000 else 'OVER'} max infl {infl} bones {len(BONES)}; "
        f"weapon tris {len(WT)}/1200 {'OK' if len(WT) <= 1200 else 'OVER'}")
    log("prop head", np.round(prop, 3), "muzzle (weapon local)", np.round(MUZZLE * scale, 3), "eyes",
        np.round((EYE[-1][0] + shift) * scale, 3), np.round((EYE[1][0] + shift) * scale, 3))
    log(f"full stage {time.time() - t_start:.0f}s")


if STAGE == "shape":
    bpy.ops.wm.read_factory_settings(use_empty=True)
    shape_stage()
    log("done")
elif STAGE == "full":
    bpy.ops.wm.read_factory_settings(use_empty=True)
    full_stage()
    log("done")
