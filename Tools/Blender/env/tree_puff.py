# Procedural "puffy" organic frame models (A.4a fallback for image-to-3D, Docs/Briefs/A-EnvTarget.md §3.1). Headless:
#   Tools/blender.ps1 -Script Tools/Blender/env/tree_puff.py -ScriptArgs @('tree_tall','tree_wide','rocks_moss')   (no args = all)
# Model = union of primitives from puff.json (canopy lobes on an ellipsoid shell, tapered trunk, root blobs, boulders)
# -> voxel remesh (one closed smooth surface, creases between lobes stay) -> decimate to budget -> colour per primitive
# label (canopy gradient light top / dark bottom, lobe highlight, trunk, moss caps by normal) -> AO bake -> FBX + GLB.
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import envcommon as ec

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
cfg_all = json.load(open(os.path.join(here, "puff.json"), encoding="utf-8"))
out_dir = os.path.join(repo, cfg_all["exportDir"])
names = argv or list(cfg_all["models"].keys())

def add_ellipsoid(c, r, segs=28, rings=14):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=1.0, location=c)
    o = bpy.context.active_object
    o.scale = r
    return o

def add_cone(z0, z1, r0, r1, segs=20):
    bpy.ops.mesh.primitive_cone_add(vertices=segs, radius1=r0, radius2=r1, depth=z1 - z0, location=(0, 0, (z0 + z1) / 2))
    return bpy.context.active_object

def fib_points(n, seed):
    rng = np.random.default_rng(seed)
    i = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * i / n)
    th = math.pi * (1 + 5 ** 0.5) * i + rng.uniform(0, 6.28)
    p = np.stack([np.cos(th) * np.sin(phi), np.sin(th) * np.sin(phi), np.cos(phi)], 1)
    return p + rng.normal(scale=0.08, size=p.shape)

def build(name, m):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rng = np.random.default_rng(m.get("seed", 1))
    parts, prims = [], []  # prims: (label, kind, params) for colour labels
    for cl in m.get("canopy", []):
        c, r = np.array(cl["center"], float), np.array(cl["radii"], float)
        parts.append(add_ellipsoid(c.tolist(), (r * cl.get("core", 0.82)).tolist()))
        prims.append(("canopy", "ell", (c, r * cl.get("core", 0.82))))
        for p in fib_points(cl["lobes"], m.get("seed", 1) + len(prims)):
            if p[2] < cl.get("minZ", -0.55):
                continue
            lr = rng.uniform(*cl["lobeRadius"])
            pc = c + p / np.linalg.norm(p) * r * cl.get("shell", 0.78)
            parts.append(add_ellipsoid(pc.tolist(), (lr, lr, lr * cl.get("lobeSquash", 0.92)), 20, 10))
            prims.append(("canopy", "ell", (pc, np.array([lr, lr, lr * cl.get("lobeSquash", 0.92)]))))
    if "trunk" in m:
        t = m["trunk"]
        parts.append(add_cone(-0.05, t["height"], t["r0"], t["r1"]))
        prims.append(("trunk", "cyl", (t["r0"], t["r1"], t["height"])))
        for k in range(t.get("roots", 0)):
            a = 2 * math.pi * k / t["roots"] + rng.uniform(-0.3, 0.3)
            rr = t["rootReach"] * rng.uniform(0.85, 1.1)
            pc = np.array([math.cos(a) * rr, math.sin(a) * rr, 0.02])
            s = t["rootSize"]
            o = add_ellipsoid(pc.tolist(), (s[0], s[1], s[2]), 16, 8)
            o.rotation_euler = (0, 0, a)
            parts.append(o)
            prims.append(("trunk", "ell", (pc, np.array([s[0], s[0], s[2]]))))
    for b in m.get("boulders", []):
        c, r = np.array(b["center"], float), np.array(b["radii"], float)
        o = add_ellipsoid(c.tolist(), r.tolist(), 32, 16)
        o.rotation_euler = (0, 0, math.radians(b.get("yaw", 0)))
        parts.append(o)
        prims.append(("rock", "ell", (c, r)))
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.join()
    obj = bpy.context.active_object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ec.apply_modifier(obj, "REMESH", mode="VOXEL", voxel_size=m["voxel"])
    if m.get("noise", 0) > 0:  # gentle low-frequency wobble so boulders are not perfect ellipsoids
        tex = bpy.data.textures.new("n", "CLOUDS")
        tex.noise_scale = m.get("noiseScale", 0.35)
        ec.apply_modifier(obj, "DISPLACE", texture=tex, strength=m["noise"], mid_level=0.5)
    ec.apply_modifier(obj, "SMOOTH", factor=0.5, iterations=m.get("smooth", 2))
    # cut below ground, flatten base
    co = ec.verts_np(obj)
    co[:, 2] = np.maximum(co[:, 2], 0.0)
    obj.data.vertices.foreach_set("co", co.ravel())
    dense, tris = ec.decimate_to(obj, m["tris"])
    obj.data.update()

    # ---- colours ----
    co, nrm = ec.verts_np(obj), ec.normals_np(obj)
    pal = {k: ec.hex_rgb(v) for k, v in m["colours"].items()}
    def sdf(kind, prm):
        if kind == "ell":
            c, r = prm
            return (np.linalg.norm((co - c) / r, axis=1) - 1) * r.min()
        r0, r1, h = prm
        rad = r0 + (r1 - r0) * np.clip(co[:, 2] / h, 0, 1)
        d_side = np.sqrt(co[:, 0] ** 2 + co[:, 1] ** 2) - rad
        return np.maximum(d_side, co[:, 2] - h)
    d = np.stack([sdf(k, p) for _, k, p in prims], 1)
    owner = d.argmin(1)
    label = np.array([prims[i][0] for i in owner])
    col = np.zeros((len(co), 3))
    # canopy: vertical gradient + up-facing light + lobe-top highlight
    cz = co[label == "canopy", 2] if (label == "canopy").any() else np.array([0.0])
    z0, z1 = cz.min(), cz.max()
    h = np.clip((co[:, 2] - z0) / max(z1 - z0, 1e-6), 0, 1)
    up = np.clip(nrm[:, 2], -1, 1)
    t = np.clip(0.55 * h + 0.45 * (up * 0.5 + 0.5), 0, 1)
    for k in ("canopyDark", "canopyMid", "canopyLight"):
        pal.setdefault(k, np.zeros(3))
    can = np.where(t[:, None] < 0.5, pal["canopyDark"] + (pal["canopyMid"] - pal["canopyDark"]) * (t[:, None] / 0.5),
                   pal["canopyMid"] + (pal["canopyLight"] - pal["canopyMid"]) * ((t[:, None] - 0.5) / 0.5))
    # each lobe a touch different in tone (vinyl-toy variety)
    jitter = np.random.default_rng(3).uniform(-0.035, 0.035, size=(len(prims), 1))
    can = can * (1 + jitter[owner])
    m_can = label == "canopy"
    col[m_can] = can[m_can]
    if "trunkLight" in pal:
        tz = np.clip(co[:, 2] / max(m.get("trunk", {}).get("height", 1), 1e-6), 0, 1)[:, None]
        tr = pal["trunkDark"] + (pal["trunkLight"] - pal["trunkDark"]) * tz
        col[label == "trunk"] = tr[label == "trunk"]
    if "rock" in pal:
        rk = pal["rockDark"] + (pal["rock"] - pal["rockDark"]) * (up[:, None] * 0.5 + 0.5)
        m_r = label == "rock"
        col[m_r] = rk[m_r]
        corner = None
        if "moss" in pal:  # moss caps on faces (sharp edge via face-corner colours), cap raised slightly
            cz_own = np.array([prims[i][2][0][2] for i in owner])
            fc, fn = ec.face_centres(obj)
            fv = [list(p.vertices) for p in obj.data.polygons]
            first = np.array([f[0] for f in fv])
            wav = 0.12 * np.sin(fc[:, 0] * 17 + fc[:, 1] * 11) + 0.08 * np.sin(fc[:, 1] * 23 - fc[:, 0] * 7)
            fmoss = (label[first] == "rock") & (fn[:, 2] > m.get("mossUp", 0.45) + wav) & (fc[:, 2] > cz_own[first])
            mz = np.clip((up - 0.3) * 1.6, 0, 1)[:, None]
            moss_col = pal["mossDark"] + (pal["moss"] - pal["mossDark"]) * mz
            corner = ec.corner_colours(obj, [col, moss_col], fmoss.astype(int))
            inside = np.ones(len(co), bool)
            touched = np.zeros(len(co), bool)
            for f, mo in zip(fv, fmoss):
                if mo:
                    touched[f] = True
                else:
                    inside[f] = False
            lift = inside & touched
            co2 = co.copy()
            co2[lift] += nrm[lift] * m.get("mossThickness", 0.012)
            obj.data.vertices.foreach_set("co", co2.ravel())
            obj.data.update()
    else:
        corner = None
    gloss = np.where(label == "canopy", m.get("glossCanopy", 0.15), 0.08)
    ao_mean, ao_min = ec.finish_colours(obj, col if corner is None else corner, gloss, ao_strength=m.get("aoStrength", 0.5), ao_distance=m.get("aoDistance", 0.3))
    print(f"[puff] {name} dense {dense} ao mean {ao_mean:.2f} min {ao_min:.2f}", flush=True)
    if "trunk" not in m:  # no trunk: pivot = centre of the footprint
        co = ec.verts_np(obj)
        lo, hi = co.min(0), co.max(0)
        co[:, :2] -= (lo[:2] + hi[:2]) / 2
        obj.data.vertices.foreach_set("co", co.ravel())
        obj.data.update()
    return ec.export(obj, out_dir, name, m["tris"], "[puff]")

for n in names:
    build(n, cfg_all["models"][n])
