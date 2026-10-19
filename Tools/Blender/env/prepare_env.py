# Image-to-3D environment prop preparation (A.4a, Docs/Briefs/A-EnvTarget.md §3.1 path (И)). Headless:
#   Tools/blender.ps1 -Script Tools/Blender/env/prepare_env.py -ScriptArgs @('<trellis.glb>','<name>')
#   (reads Tools/Blender/env/env_models.json -> entry <name>; writes Art/Source/3D/env/export/<name>.fbx + .glb)
# Steps: import GLB -> normalise (height 1, base on ground, centred on the trunk/base) -> voxel remesh (one closed
# surface) -> drop small islands -> decimate to the triangle budget -> vertex colours sampled from the TRELLIS.2
# base-colour texture (nearest surface point on the original mesh, UV, bilinear) -> optional palette pull + saturation
# -> ambient occlusion by BVH ray casts (ground plane occludes) -> darker base -> scale to target height (cells) ->
# export FBX (Unity, Y up) + GLB. Colour is sRGB in the file (FBX colors_type SRGB); alpha = gloss.
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
src, name = os.path.abspath(argv[0]), argv[1]
here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
cfg_all = json.load(open(os.path.join(here, "env_models.json"), encoding="utf-8"))
cfg = dict(cfg_all["defaults"])
cfg.update(cfg_all["models"][name])
out_dir = os.path.join(repo, cfg_all["exportDir"])
os.makedirs(out_dir, exist_ok=True)
log = lambda *a: print("[prepare]", *a, flush=True)
rng = np.random.default_rng(7)

def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((np.clip(c, 0, 1) + 0.055) / 1.055) ** 2.4)

def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])

def verts_np(o):
    v = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get("co", v)
    return v.reshape(-1, 3)

def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)

# ---------- import ----------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
if len(meshes) > 1:
    bpy.ops.object.join()
src_obj = bpy.context.view_layer.objects.active
for o in list(bpy.context.scene.objects):
    if o is not src_obj:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
src_obj.rotation_euler = (0, 0, math.radians(cfg.get("yawDeg", 0.0)))
bpy.ops.object.transform_apply(rotation=True)
log("imported tris", tri_count(src_obj))

# base-colour texture of the generated asset
tex = None
for mat in src_obj.data.materials:
    if mat and mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == "BSDF_PRINCIPLED":
                for l in n.inputs["Base Color"].links:
                    node = l.from_node
                    while node.type != "TEX_IMAGE" and node.inputs and any(i.links for i in node.inputs):
                        node = next(i.links[0].from_node for i in node.inputs if i.links)
                    if node.type == "TEX_IMAGE":
                        tex = node.image
if tex is None:
    raise RuntimeError("no base colour texture in " + src)
tw, th = tex.size
tpx = np.empty(tw * th * 4, dtype=np.float32)
tex.pixels.foreach_get(tpx)
tpx = tpx.reshape(th, tw, 4)  # row 0 = bottom (v = 0)
tex_is_srgb = tex.colorspace_settings.name.lower().startswith("srgb")
log("texture", tex.name, tw, "x", th, "colorspace", tex.colorspace_settings.name)

# normalise: height 1, base centred (centre of the lowest slice), on the ground
v = verts_np(src_obj)
lo, hi = v.min(0), v.max(0)
s = 1.0 / (hi[2] - lo[2])
base = v[v[:, 2] < lo[2] + cfg["baseSlice"] * (hi[2] - lo[2])]
cx, cy = (base[:, 0].min() + base[:, 0].max()) / 2, (base[:, 1].min() + base[:, 1].max()) / 2
src_obj.scale = (s, s, s)
src_obj.location = (-cx * s, -cy * s, -lo[2] * s)
bpy.ops.object.transform_apply(location=True, scale=True)

# UV + triangle lookup on the original mesh (for colour transfer)
sm = src_obj.data
sm.calc_loop_triangles()
uv_layer = sm.uv_layers.active.data
n_lt = len(sm.loop_triangles)
lt_loops = np.empty(n_lt * 3, dtype=np.int64)
sm.loop_triangles.foreach_get("loops", lt_loops)
lt_verts = np.empty(n_lt * 3, dtype=np.int64)
sm.loop_triangles.foreach_get("vertices", lt_verts)
lt_poly = np.empty(n_lt, dtype=np.int64)
sm.loop_triangles.foreach_get("polygon_index", lt_poly)
uv_all = np.empty(len(uv_layer) * 2)
uv_layer.foreach_get("uv", uv_all)
uv_all = uv_all.reshape(-1, 2)
src_co = verts_np(src_obj)
poly_to_lts = {}
for i, p in enumerate(lt_poly):
    poly_to_lts.setdefault(int(p), []).append(i)
src_bvh = BVHTree.FromObject(src_obj, bpy.context.evaluated_depsgraph_get())

def sample_tex(uv):
    x = np.clip(uv[:, 0] % 1.0 * tw - 0.5, 0, tw - 1.001)
    y = np.clip(uv[:, 1] % 1.0 * th - 0.5, 0, th - 1.001)
    x0, y0 = x.astype(int), y.astype(int)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    c = (tpx[y0, x0, :3] * (1 - fx) * (1 - fy) + tpx[y0, x0 + 1, :3] * fx * (1 - fy)
         + tpx[y0 + 1, x0, :3] * (1 - fx) * fy + tpx[y0 + 1, x0 + 1, :3] * fx * fy)
    if not tex_is_srgb:  # pixels are linear only for non-colour data; Blender stores sRGB images as sRGB bytes -> floats
        pass
    return c

def surface_colour(points):
    uvs = np.zeros((len(points), 2))
    for k, p in enumerate(points):
        loc, nrm, fidx, dist = src_bvh.find_nearest(Vector(p.tolist()))
        best = None
        for t in poly_to_lts.get(fidx, []):
            a, b, c = src_co[lt_verts[t * 3:t * 3 + 3]]
            q = np.array(loc)
            v0, v1, v2 = b - a, c - a, q - a
            d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
            d20, d21 = v2 @ v0, v2 @ v1
            den = d00 * d11 - d01 * d01
            if abs(den) < 1e-18:
                continue
            w1 = (d11 * d20 - d01 * d21) / den
            w2 = (d00 * d21 - d01 * d20) / den
            w0 = 1 - w1 - w2
            err = -min(w0, w1, w2)
            if best is None or err < best[0]:
                best = (err, t, np.clip([w0, w1, w2], 0, 1))
        if best is None:
            continue
        _, t, w = best
        w = w / w.sum()
        uvs[k] = (uv_all[lt_loops[t * 3:t * 3 + 3]] * w[:, None]).sum(0)
    return sample_tex(uvs)

# ---------- remesh, islands, decimate ----------
work = src_obj.copy()
work.data = src_obj.data.copy()
bpy.context.scene.collection.objects.link(work)
work.data.materials.clear()
bpy.ops.object.select_all(action="DESELECT")
work.select_set(True)
bpy.context.view_layer.objects.active = work
mod = work.modifiers.new("Remesh", "REMESH")
mod.mode = "VOXEL"
mod.voxel_size = cfg["voxelSize"]
bpy.ops.object.modifier_apply(modifier=mod.name)
bm = bmesh.new()
bm.from_mesh(work.data)
bm.verts.ensure_lookup_table()
seen, islands = set(), []
for start in bm.verts:
    if start.index in seen:
        continue
    stack, isl = [start], []
    seen.add(start.index)
    while stack:
        cur = stack.pop()
        isl.append(cur)
        for e in cur.link_edges:
            w = e.other_vert(cur)
            if w.index not in seen:
                seen.add(w.index)
                stack.append(w)
    islands.append(isl)
limit = cfg["minIslandFraction"] * len(bm.verts)
drop = [vv for isl in islands if len(isl) < limit for vv in isl]
bmesh.ops.delete(bm, geom=drop, context="VERTS")
bm.to_mesh(work.data)
bm.free()
log("remesh islands", len(islands), "dropped verts", len(drop), "tris", tri_count(work))
if cfg.get("smoothIterations", 0):
    sm_mod = work.modifiers.new("Smooth", "CORRECTIVE_SMOOTH")
    sm_mod.iterations = cfg["smoothIterations"]
    sm_mod.smooth_type = "SIMPLE"
    sm_mod.use_only_smooth = False
    bpy.ops.object.modifier_apply(modifier=sm_mod.name)

# colour the dense remesh first (better averaging), decimation then interpolates the attribute
co = verts_np(work)
col = surface_colour(co)
attr = work.data.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
attr.data.foreach_set("color", np.hstack([col, np.ones((len(col), 1))]).astype(np.float32).ravel())
dense = tri_count(work)
dec = work.modifiers.new("Decimate", "DECIMATE")
dec.ratio = min(1.0, cfg["tris"] / dense * 0.985)
bpy.ops.object.modifier_apply(modifier=dec.name)
tri = work.modifiers.new("Tri", "TRIANGULATE")
bpy.ops.object.modifier_apply(modifier=tri.name)
log("decimated", dense, "->", tri_count(work))
me = work.data

# ---------- colour clean-up ----------
co = verts_np(work)
cbuf = np.empty(len(me.vertices) * 4, dtype=np.float32)
me.color_attributes["Col"].data.foreach_get("color", cbuf)
col = cbuf.reshape(-1, 4)[:, :3].astype(np.float64)  # values as sampled from the image (sRGB-encoded floats)
edges = np.empty(len(me.edges) * 2, dtype=np.int64)
me.edges.foreach_get("vertices", edges)
edges = edges.reshape(-1, 2)
for _ in range(cfg.get("colourBlur", 0)):  # light neighbour average removes texture speckle
    acc, cnt = col.copy(), np.ones(len(col))
    for a, b in ((0, 1), (1, 0)):
        np.add.at(acc, edges[:, b], col[edges[:, a]])
        np.add.at(cnt, edges[:, b], 1)
    col = acc / cnt[:, None]
if cfg.get("palette"):
    pal = np.array([hex_rgb(h) for h in cfg["palette"]])
    # match by hue-ish distance on normalised colour, keep some of the original lightness variation
    idx = ((col[:, None, :] - pal[None]) ** 2).sum(-1).argmin(1)
    target = pal[idx]
    k = cfg.get("paletteStrength", 0.6)
    lum = lambda c: c @ np.array([0.2126, 0.7152, 0.0722])
    shade = np.clip(lum(col) / np.maximum(lum(target), 1e-3), 0.75, 1.2)[:, None]
    col = col * (1 - k) + np.clip(target * shade, 0, 1) * k
sat = cfg.get("saturation", 1.0)
if sat != 1.0:
    g = col.mean(1, keepdims=True)
    col = np.clip(g + (col - g) * sat, 0, 1)

# ---------- ambient occlusion (ray casts; ground plane z = 0 occludes) ----------
target_h = cfg["height"]
work_bvh = BVHTree.FromObject(work, bpy.context.evaluated_depsgraph_get())
nrm = np.empty(len(me.vertices) * 3)
me.vertices.foreach_get("normal", nrm)
nrm = nrm.reshape(-1, 3)
dist = cfg["aoDistance"] / target_h  # AO distance is given in cells; the model is still height 1 here
dirs = rng.normal(size=(cfg["aoRays"], 3))
dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
occl = np.zeros(len(co))
for i in range(len(co)):
    n = nrm[i]
    hit = 0.0
    tot = 0.0
    p0 = co[i] + n * 1e-3
    for d in dirs:
        c = d @ n
        if c <= 0:
            d = -d
            c = -c
        tot += c
        if p0[2] + d[2] * dist < 0:  # ground
            hit += c
            continue
        if work_bvh.ray_cast(Vector(p0.tolist()), Vector(d.tolist()), dist)[0] is not None:
            hit += c
    occl[i] = hit / max(tot, 1e-6)
ao = 1.0 - cfg["aoStrength"] * occl
z = co[:, 2] * target_h  # cells
ground = cfg["groundDark"] + (1 - cfg["groundDark"]) * np.clip(z / cfg["groundDarkHeight"], 0, 1)
col = col * (ao * ground)[:, None]
log("ao mean", round(float(ao.mean()), 3), "min", round(float(ao.min()), 3))

gloss = np.full(len(col), cfg.get("gloss", 0.15))
lin = srgb_to_lin(np.clip(col, 0, 1))  # FLOAT_COLOR stores linear; FBX export writes sRGB bytes
me.color_attributes["Col"].data.foreach_set("color", np.hstack([lin, gloss[:, None]]).astype(np.float32).ravel())
me.color_attributes.active_color = me.color_attributes["Col"]
mat = bpy.data.materials.new("EnvVC")
mat.use_nodes = True
bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
vc = mat.node_tree.nodes.new("ShaderNodeVertexColor")
vc.layer_name = "Col"
mat.node_tree.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.8
me.materials.clear()
me.materials.append(mat)
for p in me.polygons:
    p.use_smooth = True

# ---------- scale to cells, report, export ----------
work.scale = (target_h,) * 3
bpy.ops.object.transform_apply(scale=True)
bpy.data.objects.remove(src_obj, do_unlink=True)
work.name = name
work.data.name = name
co = verts_np(work)
lo, hi = co.min(0), co.max(0)
foot = co[co[:, 2] < 0.05]
foot_r = float(np.sqrt((foot[:, 0] ** 2 + foot[:, 1] ** 2)).max()) if len(foot) else 0.0
tris = tri_count(work)
ok = tris <= cfg["tris"]
log("bounds", np.round(lo, 3).tolist(), np.round(hi, 3).tolist(), "footprint r", round(foot_r, 3), "colours yes")
base = os.path.join(out_dir, name)
bpy.ops.object.select_all(action="DESELECT")
work.select_set(True)
bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", colors_type="SRGB", mesh_smooth_type="FACE", bake_anim=False)
bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
log(f"RESULT {name} tris {tris}/{cfg['tris']} {'OK' if ok else 'OVER'} height {hi[2]:.2f} width {hi[0]-lo[0]:.2f}x{hi[1]-lo[1]:.2f} -> {base}.fbx")
